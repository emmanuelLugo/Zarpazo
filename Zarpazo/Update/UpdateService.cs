using System.IO;
using Zarpazo.Settings;

namespace Zarpazo.Update;

/// <summary>
/// What the rest of the app talks to: when to look for a new version, which one is waiting, and how to install it.
/// Used from the UI thread.
/// </summary>
internal sealed class UpdateService
{
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(24);

    private readonly SettingsStore _settings;
    private readonly UpdateChecker _checker;
    private readonly UpdateDownloader _downloader;
    private readonly Func<string, string?> _launchInstaller;
    private Task<UpdateCheck>? _running;

    /// <param name="launchInstaller">Runs the downloaded installer and closes the app; returns null, or why it couldn't.</param>
    public UpdateService(SettingsStore settings, UpdateChecker checker, UpdateDownloader downloader, Func<string, string?> launchInstaller)
    {
        _settings = settings;
        _checker = checker;
        _downloader = downloader;
        _launchInstaller = launchInstaller;
    }

    /// <summary>The newer version found by the last check, or null.</summary>
    public ReleaseInfo? Available { get; private set; }

    /// <summary>Raised when <see cref="Available"/> changes.</summary>
    public event Action? Changed;

    /// <summary>True when the update can be downloaded, checked and installed from inside the app.</summary>
    public bool CanInstallInPlace => Available?.CanInstallInPlace == true && InstalledCopy.IsThisCopyInstalled();

    /// <summary>A quiet check is due when they're turned on and a day has passed since the last good one.</summary>
    public static bool IsDue(bool enabled, DateTime lastCheckUtc, DateTime nowUtc) =>
        enabled && (lastCheckUtc > nowUtc || nowUtc - lastCheckUtc >= CheckEvery); // a date in the future (clock changed) counts as due

    public bool IsDue() => IsDue(_settings.Current.CheckForUpdates, _settings.Current.LastUpdateCheck, DateTime.UtcNow);

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
            try
            {
                _settings.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the date only spaces out the next check; losing it just means one more
            }

            Available = result.Newer;
            Changed?.Invoke();
        }
        return result;
    }

    /// <summary>Downloads the waiting update, checks it and hands it to the installer, which closes the app.</summary>
    /// <exception cref="UpdateException">There is nothing to install, or a step failed.</exception>
    public async Task InstallAsync(IProgress<double>? progress = null, CancellationToken cancel = default)
    {
        ReleaseInfo release = Available ?? throw new UpdateException("No hay ninguna actualización pendiente.");
        string setup = await _downloader.DownloadAsync(release, progress, cancel);
        if (_launchInstaller(setup) is { } problem)
        {
            throw new UpdateException(problem);
        }
    }
}
