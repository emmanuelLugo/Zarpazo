using System.IO;
using Zarpazo.Settings;

namespace Zarpazo.Update;

/// <summary>
/// What the rest of the app talks to: when to look for a new version, which one is waiting, whether it is already downloaded,
/// and how to install it. Used from the UI thread.
/// </summary>
internal sealed class UpdateService
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);

    private readonly SettingsStore _settings;
    private readonly UpdateChecker _checker;
    private readonly UpdateDownloader _downloader;
    private readonly Func<string, string?> _launchInstaller;
    private readonly Func<bool> _isInstalledCopy;
    private Task<UpdateCheck>? _running;
    private Task _staging = Task.CompletedTask;

    /// <param name="launchInstaller">Runs the downloaded installer and closes the app; returns null, or why it couldn't.</param>
    /// <param name="isInstalledCopy">Whether this running copy is the one the installer put on the PC; only that one updates itself.</param>
    public UpdateService(SettingsStore settings, UpdateChecker checker, UpdateDownloader downloader, Func<string, string?> launchInstaller,
        Func<bool>? isInstalledCopy = null)
    {
        _settings = settings;
        _checker = checker;
        _downloader = downloader;
        _launchInstaller = launchInstaller;
        _isInstalledCopy = isInstalledCopy ?? InstalledCopy.IsThisCopyInstalled;
    }

    /// <summary>The newer version found by the last check, or null.</summary>
    public ReleaseInfo? Available { get; private set; }

    /// <summary>The version in <see cref="Available"/> once it is downloaded and checked, ready to install with one click; else null.</summary>
    public ReleaseInfo? Ready { get; private set; }

    /// <summary>True while the waiting update is being downloaded in the background.</summary>
    public bool IsDownloading { get; private set; }

    /// <summary>Raised when <see cref="Available"/>, <see cref="Ready"/> or <see cref="IsDownloading"/> changes.</summary>
    public event Action? Changed;

    /// <summary>How much of the background download is done, from 0 to 1.</summary>
    public event Action<double>? DownloadProgress;

    /// <summary>Completes when the background download that a check started is over (done or not).</summary>
    public Task Staged => _staging;

    /// <summary>True when the update can be downloaded, checked and installed from inside the app.</summary>
    public bool CanInstallInPlace => Available?.CanInstallInPlace == true && _isInstalledCopy();

    /// <summary>A quiet check is due when they're turned on and a day has passed since the last good one.</summary>
    public static bool IsDue(bool enabled, DateTime lastCheckUtc, DateTime nowUtc) =>
        enabled && (lastCheckUtc > nowUtc || nowUtc - lastCheckUtc >= CheckEvery); // a date in the future (clock changed) counts as due

    public bool IsDue() => IsDue(_settings.Current.CheckForUpdates, _settings.Current.LastUpdateCheck, DateTime.UtcNow);

    /// <summary>
    /// Picks up an update downloaded in an earlier run, without going to the network: if its file is still there and still
    /// matches what was published, it is ready again. Anything else about it is forgotten.
    /// </summary>
    public async Task RestorePendingAsync(Version current, CancellationToken cancel = default)
    {
        AppSettings settings = _settings.Current;
        if (settings.PendingUpdateTag.Length == 0)
        {
            return;
        }

        ReleaseInfo? release = ReleaseFeed.ForTag(settings.PendingUpdateTag, settings.PendingUpdateSize, settings.PendingUpdateSha256);
        if (release != null && release.Version > current && _isInstalledCopy() && await _downloader.TryReuseAsync(release, cancel) != null)
        {
            Available = Ready = release;
            Changed?.Invoke();
            return;
        }

        ForgetPending(deleteFile: true); // installed already, replaced, damaged, or deleted
    }

    /// <summary>Asks GitHub. A failed check changes nothing, so a quiet one is tried again later.</summary>
    public async Task<UpdateCheck> CheckAsync(CancellationToken cancel = default)
    {
        if (_running != null)
        {
            return await _running; // a look is already under way: share its answer
        }

        _running = RunCheckAsync(cancel);
        try
        {
            return await _running;
        }
        finally
        {
            _running = null; // cleared here, not inside the task: one that finishes at once would be stored after it cleared itself
        }
    }

    private async Task<UpdateCheck> RunCheckAsync(CancellationToken cancel)
    {
        UpdateCheck result = await _checker.CheckAsync(cancel);
        if (!result.IsFailure)
        {
            _settings.Current.LastUpdateCheck = DateTime.UtcNow;
            TrySave();

            if (Ready != null && Ready.Tag != result.Newer?.Tag)
            {
                Ready = null; // a newer one came out, or this one was installed by hand
                ForgetPending(deleteFile: true);
            }

            Available = result.Newer;
            Changed?.Invoke();
            if (Available != null && Ready == null)
            {
                _staging = StageAsync(CancellationToken.None);
            }
        }
        return result;
    }

    // Downloads the waiting update on its own, if the person allows it and this copy can install it. Never throws and never
    // bothers anyone: a failed download is simply tried again with the next check.
    private async Task StageAsync(CancellationToken cancel)
    {
        if (Available is not { CanInstallInPlace: true } release || IsDownloading || Ready?.Tag == release.Tag
            || !_settings.Current.DownloadUpdatesInBackground || !_isInstalledCopy())
        {
            return;
        }

        IsDownloading = true;
        Changed?.Invoke();
        try
        {
            string? path = await _downloader.TryReuseAsync(release, cancel)
                ?? await _downloader.DownloadAsync(release, new Progress<double>(done => DownloadProgress?.Invoke(done)), cancel);
            if (Available?.Tag == release.Tag && path != null)
            {
                Ready = release;
                RememberPending(release);
            }
        }
        catch (Exception ex) when (ex is UpdateException or OperationCanceledException or IOException or UnauthorizedAccessException)
        {
            // nothing to do: the next check tries again
        }
        finally
        {
            IsDownloading = false;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Checks the waiting update (downloading it first if it isn't yet) and hands it to the installer, which closes the app.
    /// </summary>
    /// <exception cref="UpdateException">There is nothing to install, or a step failed.</exception>
    public async Task InstallAsync(IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        ReleaseInfo release = Available ?? throw new UpdateException("No hay ninguna actualización pendiente.");
        await _staging; // a background download under way is finished first, they would both write the same file

        // Checked again even when it was downloaded earlier: the file may have sat in the folder for days.
        string setup = await _downloader.TryReuseAsync(release, cancel) ?? await _downloader.DownloadAsync(release, progress, cancel);
        ForgetPending(deleteFile: false); // from here the installer decides; a failed run must not be retried at every start
        if (_launchInstaller(setup) is { } problem)
        {
            RememberPending(release); // it didn't start: the file is still good
            throw new UpdateException(problem);
        }
    }

    private void RememberPending(ReleaseInfo release)
    {
        _settings.Current.PendingUpdateTag = release.Tag;
        _settings.Current.PendingUpdateSha256 = release.SetupSha256 ?? "";
        _settings.Current.PendingUpdateSize = release.SetupSize;
        TrySave();
    }

    private void ForgetPending(bool deleteFile)
    {
        if (deleteFile)
        {
            _downloader.Clear();
        }

        AppSettings settings = _settings.Current;
        if (settings.PendingUpdateTag.Length > 0)
        {
            settings.PendingUpdateTag = settings.PendingUpdateSha256 = "";
            settings.PendingUpdateSize = 0;
            TrySave();
        }
    }

    private void TrySave()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // what is remembered only saves a download or spaces out the next check; losing it costs one more of them
        }
    }
}
