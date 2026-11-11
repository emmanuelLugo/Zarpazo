using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Zarpazo.Settings;
using Zarpazo.Update;

namespace Zarpazo.Tests;

public sealed class UpdateTests : IDisposable
{
    private const string Setup = "https://github.com/emmanuelLugo/Zarpazo/releases/download/v0.3.0/Zarpazo-Setup-0.3.0.exe";
    private static readonly byte[] SetupBytes = Encoding.ASCII.GetBytes("pretend this is an installer");
    private static readonly string SetupHash = Convert.ToHexString(SHA256.HashData(SetupBytes)).ToLowerInvariant();

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "Zarpazo.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // The shape of GitHub's answer, trimmed to the fields the reader looks at.
    private static string Json(string tag = "v0.3.0", bool draft = false, bool prerelease = false, string? setupUrl = Setup,
        string? digest = null, long size = -1, string assetName = "Zarpazo-Setup-0.3.0.exe") => $$"""
        {
          "tag_name": "{{tag}}", "draft": {{draft.ToString().ToLowerInvariant()}}, "prerelease": {{prerelease.ToString().ToLowerInvariant()}},
          "html_url": "https://github.com/emmanuelLugo/Zarpazo/releases/tag/{{tag}}",
          "body": "### Añadido\n- Algo nuevo",
          "assets": [
            { "name": "Zarpazo.exe", "size": 1, "digest": "sha256:{{new string('a', 64)}}", "browser_download_url": "https://github.com/emmanuelLugo/Zarpazo/releases/download/{{tag}}/Zarpazo.exe" },
            { "name": "{{assetName}}", "size": {{(size < 0 ? SetupBytes.Length : size)}}, "digest": "{{digest ?? "sha256:" + SetupHash}}", "browser_download_url": "{{setupUrl}}" }
          ]
        }
        """;

    private static ReleaseInfo Release(string? url = Setup, string? sha = null, long size = -1) =>
        new(new Version(0, 3, 0), "v0.3.0", "https://github.com/emmanuelLugo/Zarpazo/releases/tag/v0.3.0", "", url,
            size < 0 ? SetupBytes.Length : size, sha ?? SetupHash);

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Ok(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };

    // ---- versions ----

    [Theory]
    [InlineData("0.2.1", 0, 2, 1)]
    [InlineData("v0.3.0", 0, 3, 0)]
    [InlineData("V1.0.4", 1, 0, 4)]
    [InlineData("0.2.1+e29d744", 0, 2, 1)]
    [InlineData("0.4.0-beta.1", 0, 4, 0)]
    public void Version_texts_are_read_whatever_their_decoration(string text, int major, int minor, int build)
    {
        Assert.True(AppVersion.TryParse(text, out Version version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("7")]
    [InlineData(null)]
    public void Text_that_is_not_a_version_is_refused(string? text)
    {
        Assert.False(AppVersion.TryParse(text, out _));
    }

    [Fact]
    public void This_build_knows_its_own_version()
    {
        Assert.True(AppVersion.Current > new Version(0, 0));
    }

    // ---- reading a release ----

    [Fact]
    public void A_release_gives_its_version_notes_and_the_verified_installer()
    {
        ReleaseInfo? release = ReleaseFeed.Parse(Json());

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 3, 0), release.Version);
        Assert.Equal("v0.3.0", release.Tag);
        Assert.Contains("Algo nuevo", release.Notes);
        Assert.Equal(Setup, release.SetupUrl);
        Assert.Equal(SetupBytes.Length, release.SetupSize);
        Assert.Equal(SetupHash, release.SetupSha256);
        Assert.True(release.CanInstallInPlace);
    }

    [Fact]
    public void Drafts_and_pre_releases_are_not_offered()
    {
        Assert.Null(ReleaseFeed.Parse(Json(draft: true)));
        Assert.Null(ReleaseFeed.Parse(Json(prerelease: true)));
    }

    [Fact]
    public void A_tag_that_is_not_a_version_is_not_offered()
    {
        Assert.Null(ReleaseFeed.Parse(Json(tag: "nightly")));
    }

    [Fact]
    public void An_installer_link_pointing_anywhere_else_is_ignored()
    {
        ReleaseInfo? release = ReleaseFeed.Parse(Json(setupUrl: "https://evil.example/Zarpazo-Setup-0.3.0.exe"));

        Assert.NotNull(release);
        Assert.Null(release.SetupUrl);
        Assert.False(release.CanInstallInPlace);
    }

    [Theory]
    [InlineData("md5:abc")]
    [InlineData("sha256:1234")]
    [InlineData("sha256:zzzz1234zzzz1234zzzz1234zzzz1234zzzz1234zzzz1234zzzz1234zzzz1234")]
    public void An_installer_without_a_usable_hash_cannot_be_installed_in_place(string digest)
    {
        ReleaseInfo? release = ReleaseFeed.Parse(Json(digest: digest));

        Assert.NotNull(release);
        Assert.False(release.CanInstallInPlace);
    }

    [Fact]
    public void Only_the_installer_of_that_very_version_counts()
    {
        ReleaseInfo? release = ReleaseFeed.Parse(Json(assetName: "Zarpazo-Setup-0.1.0.exe"));

        Assert.NotNull(release);
        Assert.Null(release.SetupUrl);
    }

    // ---- looking for updates ----

    [Fact]
    public async Task A_newer_release_is_reported_and_the_request_is_polite()
    {
        var handler = new FakeHandler(_ => Ok(Json()));
        var checker = new UpdateChecker(new HttpClient(handler), new Version(0, 2, 1));

        UpdateCheck result = await checker.CheckAsync();

        Assert.Equal(new Version(0, 3, 0), result.Newer?.Version);
        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal(ReleaseFeed.LatestUrl, request.RequestUri!.ToString());
        Assert.Contains("Zarpazo-Updater/0.2.1", request.Headers.UserAgent.ToString());
        Assert.Null(request.Headers.Authorization);
    }

    [Fact]
    public async Task The_same_or_an_older_release_is_not_an_update()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(_ => Ok(Json()))), new Version(0, 3, 0));
        Assert.Null((await checker.CheckAsync()).Newer);

        var newer = new UpdateChecker(new HttpClient(new FakeHandler(_ => Ok(Json()))), new Version(0, 4, 0));
        UpdateCheck result = await newer.CheckAsync();
        Assert.Null(result.Newer);
        Assert.False(result.IsFailure);
    }

    [Fact]
    public async Task No_release_published_yet_is_not_an_error()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))), new Version(0, 2, 1));

        UpdateCheck result = await checker.CheckAsync();

        Assert.False(result.IsFailure);
        Assert.Null(result.Newer);
    }

    [Fact]
    public async Task A_server_error_is_reported_as_a_failure()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))), new Version(0, 2, 1));

        UpdateCheck result = await checker.CheckAsync();

        Assert.True(result.IsFailure);
        Assert.Contains("403", result.Error);
    }

    [Fact]
    public async Task Being_offline_is_reported_as_a_failure_and_never_thrown()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(_ => throw new HttpRequestException("no network"))), new Version(0, 2, 1));

        UpdateCheck result = await checker.CheckAsync();

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task An_answer_that_is_not_json_is_reported_as_a_failure()
    {
        var checker = new UpdateChecker(new HttpClient(new FakeHandler(_ => Ok("<html>captive portal</html>"))), new Version(0, 2, 1));

        Assert.True((await checker.CheckAsync()).IsFailure);
    }

    // ---- when to look ----

    [Fact]
    public void A_quiet_look_is_due_a_day_after_the_last_one_and_only_when_turned_on()
    {
        DateTime now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(UpdateService.IsDue(true, DateTime.MinValue, now)); // never looked
        Assert.True(UpdateService.IsDue(true, now.AddHours(-25), now));
        Assert.False(UpdateService.IsDue(true, now.AddHours(-23), now));
        Assert.False(UpdateService.IsDue(false, DateTime.MinValue, now));
        Assert.True(UpdateService.IsDue(true, now.AddDays(3), now)); // the clock went back: don't wait for the future
    }

    [Fact]
    public async Task A_good_look_is_remembered_and_a_failed_one_is_not()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var fail = true;
        var handler = new FakeHandler(_ => fail ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Ok(Json()));
        var http = new HttpClient(handler);
        var service = new UpdateService(store, new UpdateChecker(http, new Version(0, 2, 1)), new UpdateDownloader(http, _folder), (_, _) => null);
        int changes = 0;
        service.Changed += () => changes++;

        await service.CheckAsync();
        Assert.Equal(DateTime.MinValue, store.Current.LastUpdateCheck); // so it is tried again soon
        Assert.Null(service.Available);
        Assert.Equal(0, changes);

        fail = false;
        await service.CheckAsync();
        Assert.True(store.Current.LastUpdateCheck > DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(new Version(0, 3, 0), service.Available?.Version);
        Assert.Equal(1, changes);
        Assert.Equal(store.Current.LastUpdateCheck, new SettingsStore(store.FilePath).Current.LastUpdateCheck);
    }

    [Fact]
    public void Updates_are_on_by_default_and_the_choice_is_kept()
    {
        string path = Path.Combine(_folder, "settings.json");
        var store = new SettingsStore(path);
        Assert.True(store.Current.CheckForUpdates);

        store.Current.CheckForUpdates = false;
        store.Save();

        Assert.False(new SettingsStore(path).Current.CheckForUpdates);
    }

    // ---- downloading ----

    [Fact]
    public async Task A_download_that_matches_the_published_hash_is_kept()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SetupBytes) });
        var downloader = new UpdateDownloader(new HttpClient(handler), _folder);
        var progress = new List<double>();

        string path = await downloader.DownloadAsync(Release(), new Progress<double>(progress.Add));

        Assert.Equal(SetupBytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(Path.Combine(_folder, "Zarpazo-Setup-0.3.0.exe"), path);
        Assert.Equal(Setup, handler.Requests[0].RequestUri!.ToString());
        Assert.Single(Directory.GetFiles(_folder)); // no .part left behind
    }

    [Fact]
    public async Task A_download_with_another_hash_is_thrown_away()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes("pretend this is an installer!")) });
        var downloader = new UpdateDownloader(new HttpClient(handler), _folder);

        // same size claimed as the real one is not needed: the size and the hash are both checked
        var ex = await Assert.ThrowsAsync<UpdateException>(() => downloader.DownloadAsync(Release(size: 29)));

        Assert.Contains("no coincide", ex.Message);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task A_download_of_the_wrong_size_is_thrown_away()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SetupBytes[..10]) });
        var downloader = new UpdateDownloader(new HttpClient(handler), _folder);

        var ex = await Assert.ThrowsAsync<UpdateException>(() => downloader.DownloadAsync(Release()));

        Assert.Contains("incompleta", ex.Message);
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public async Task Nothing_is_downloaded_from_outside_the_projects_releases()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SetupBytes) });
        var downloader = new UpdateDownloader(new HttpClient(handler), _folder);

        await Assert.ThrowsAsync<UpdateException>(() => downloader.DownloadAsync(Release(url: "https://evil.example/setup.exe")));
        await Assert.ThrowsAsync<UpdateException>(() => downloader.DownloadAsync(Release(url: null)));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_failed_download_says_so()
    {
        var downloader = new UpdateDownloader(new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))), _folder);

        var ex = await Assert.ThrowsAsync<UpdateException>(() => downloader.DownloadAsync(Release()));

        Assert.Contains("404", ex.Message);
    }

    [Fact]
    public async Task Installing_downloads_checks_and_then_hands_the_file_to_the_installer()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var http = new HttpClient(new FakeHandler(request => request.RequestUri!.AbsoluteUri == ReleaseFeed.LatestUrl
            ? Ok(Json())
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SetupBytes) }));
        string? launched = null;
        var service = new UpdateService(store, new UpdateChecker(http, new Version(0, 2, 1)), new UpdateDownloader(http, Path.Combine(_folder, "dl")),
            (path, _) => { launched = path; return null; });
        await service.CheckAsync();

        await service.InstallAsync();

        Assert.Equal(Path.Combine(_folder, "dl", "Zarpazo-Setup-0.3.0.exe"), launched);
    }

    [Fact]
    public async Task If_the_installer_cannot_start_the_reason_reaches_the_person()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var http = new HttpClient(new FakeHandler(request => request.RequestUri!.AbsoluteUri == ReleaseFeed.LatestUrl
            ? Ok(Json())
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SetupBytes) }));
        var service = new UpdateService(store, new UpdateChecker(http, new Version(0, 2, 1)), new UpdateDownloader(http, Path.Combine(_folder, "dl")),
            (_, _) => "Hay una grabación en curso.");
        await service.CheckAsync();

        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.InstallAsync());

        Assert.Equal("Hay una grabación en curso.", ex.Message);
    }

    [Fact]
    public async Task Installing_with_nothing_waiting_is_refused()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var http = new HttpClient(new FakeHandler(_ => Ok(Json())));
        var service = new UpdateService(store, new UpdateChecker(http, new Version(0, 9, 0)), new UpdateDownloader(http, _folder), (_, _) => null);

        await Assert.ThrowsAsync<UpdateException>(() => service.InstallAsync());
    }

    // ---- downloading in the background ----

    private HttpResponseMessage Github(HttpRequestMessage request) => request.RequestUri!.AbsoluteUri == ReleaseFeed.LatestUrl
        ? Ok(Json())
        : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SetupBytes) };

    private UpdateService Service(SettingsStore store, FakeHandler handler, bool installed = true, Func<string, bool, string?>? launch = null, string? downloads = null,
        Func<bool>? busy = null)
    {
        var http = new HttpClient(handler);
        return new UpdateService(store, new UpdateChecker(http, new Version(0, 2, 1)),
            new UpdateDownloader(http, downloads ?? Path.Combine(_folder, "dl")), launch ?? ((_, _) => null), () => installed, busy);
    }

    private static bool IsSetupRequest(HttpRequestMessage request) => request.RequestUri!.AbsoluteUri != ReleaseFeed.LatestUrl;

    [Fact]
    public async Task A_new_version_is_downloaded_in_the_background_and_becomes_ready()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var handler = new FakeHandler(Github);
        UpdateService service = Service(store, handler);
        int changes = 0;
        service.Changed += () => changes++;

        await service.CheckAsync();
        await service.Staged;

        Assert.Equal(new Version(0, 3, 0), service.Ready?.Version);
        Assert.False(service.IsDownloading);
        Assert.True(File.Exists(Path.Combine(_folder, "dl", "Zarpazo-Setup-0.3.0.exe")));
        Assert.Equal("v0.3.0", store.Current.PendingUpdateTag); // remembered for the next start
        Assert.True(changes >= 3); // found, downloading, ready
    }

    [Fact]
    public async Task Nothing_is_downloaded_when_the_person_turned_it_off()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        store.Current.DownloadUpdatesInBackground = false;
        var handler = new FakeHandler(Github);
        UpdateService service = Service(store, handler);

        await service.CheckAsync();
        await service.Staged;

        Assert.NotNull(service.Available);
        Assert.Null(service.Ready);
        Assert.DoesNotContain(handler.Requests, IsSetupRequest);
    }

    [Fact]
    public async Task A_copy_the_installer_did_not_put_there_does_not_download_by_itself()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var handler = new FakeHandler(Github);
        UpdateService service = Service(store, handler, installed: false);

        await service.CheckAsync();
        await service.Staged;

        Assert.NotNull(service.Available);
        Assert.False(service.CanInstallInPlace);
        Assert.Null(service.Ready);
        Assert.DoesNotContain(handler.Requests, IsSetupRequest);
    }

    [Fact]
    public async Task A_failed_background_download_leaves_the_update_available_for_the_next_look()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var handler = new FakeHandler(request => IsSetupRequest(request) ? new HttpResponseMessage(HttpStatusCode.BadGateway) : Ok(Json()));
        UpdateService service = Service(store, handler);

        await service.CheckAsync();
        await service.Staged;

        Assert.NotNull(service.Available);
        Assert.Null(service.Ready);
        Assert.False(service.IsDownloading);
        Assert.Equal("", store.Current.PendingUpdateTag);
    }

    [Fact]
    public async Task A_ready_update_is_remembered_across_restarts_without_the_network()
    {
        string path = Path.Combine(_folder, "settings.json");
        var first = new SettingsStore(path);
        UpdateService before = Service(first, new FakeHandler(Github));
        await before.CheckAsync();
        await before.Staged;

        var handler = new FakeHandler(_ => throw new InvalidOperationException("no request expected"));
        UpdateService after = Service(new SettingsStore(path), handler);
        await after.RestorePendingAsync(new Version(0, 2, 1));

        Assert.Equal(new Version(0, 3, 0), after.Ready?.Version);
        Assert.Equal(after.Ready, after.Available);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_remembered_update_that_was_installed_or_damaged_is_forgotten()
    {
        string path = Path.Combine(_folder, "settings.json");
        UpdateService before = Service(new SettingsStore(path), new FakeHandler(Github));
        await before.CheckAsync();
        await before.Staged;

        // already running that version (or a newer one)
        var installed = new SettingsStore(path);
        UpdateService same = Service(installed, new FakeHandler(Github));
        await same.RestorePendingAsync(new Version(0, 3, 0));
        Assert.Null(same.Ready);
        Assert.Equal("", installed.Current.PendingUpdateTag);
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "dl")));

        // the file was tampered with while it waited
        UpdateService again = Service(new SettingsStore(path), new FakeHandler(Github));
        await again.CheckAsync();
        await again.Staged;
        string setup = Path.Combine(_folder, "dl", "Zarpazo-Setup-0.3.0.exe");
        await File.WriteAllTextAsync(setup, "something else");
        var damaged = new SettingsStore(path);
        UpdateService restored = Service(damaged, new FakeHandler(Github));
        await restored.RestorePendingAsync(new Version(0, 2, 1));
        Assert.Null(restored.Ready);
        Assert.Equal("", damaged.Current.PendingUpdateTag);
        Assert.False(File.Exists(setup));
    }

    [Fact]
    public async Task Installing_a_ready_update_uses_the_file_already_on_disk()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var handler = new FakeHandler(Github);
        string? launched = null;
        UpdateService service = Service(store, handler, launch: (path, _) => { launched = path; return null; });
        await service.CheckAsync();
        await service.Staged;
        int requests = handler.Requests.Count;

        await service.InstallAsync();

        Assert.Equal(Path.Combine(_folder, "dl", "Zarpazo-Setup-0.3.0.exe"), launched);
        Assert.Equal(requests, handler.Requests.Count); // nothing downloaded twice
        Assert.Equal("", store.Current.PendingUpdateTag); // a failed run must not be retried at every start
    }

    [Fact]
    public async Task A_file_changed_after_it_was_checked_is_downloaded_again_before_installing()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var handler = new FakeHandler(Github);
        string? launched = null;
        UpdateService service = Service(store, handler, launch: (path, _) => { launched = path; return null; });
        await service.CheckAsync();
        await service.Staged;
        string setup = Path.Combine(_folder, "dl", "Zarpazo-Setup-0.3.0.exe");
        await File.WriteAllTextAsync(setup, "swapped by something else");

        await service.InstallAsync();

        Assert.Equal(setup, launched);
        Assert.Equal(SetupBytes, await File.ReadAllBytesAsync(setup)); // what the installer was given is the published one
    }

    [Fact]
    public async Task If_the_installer_does_not_start_the_ready_update_is_kept()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        UpdateService service = Service(store, new FakeHandler(Github), launch: (_, _) => "No se pudo.");
        await service.CheckAsync();
        await service.Staged;

        await Assert.ThrowsAsync<UpdateException>(() => service.InstallAsync());

        Assert.Equal("v0.3.0", store.Current.PendingUpdateTag);
        Assert.NotNull(service.Ready);
    }

    [Fact]
    public async Task Restarting_now_asks_the_installer_to_open_the_app_again_and_closing_does_not()
    {
        var calls = new List<bool>();
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        UpdateService service = Service(store, new FakeHandler(Github), launch: (_, relaunch) => { calls.Add(relaunch); return "stay"; });
        await service.CheckAsync();
        await service.Staged;

        await Assert.ThrowsAsync<UpdateException>(() => service.InstallAsync());
        await Assert.ThrowsAsync<UpdateException>(() => service.InstallAsync(relaunch: false));

        Assert.Equal([true, false], calls);
    }

    [Fact]
    public async Task Installing_on_close_is_only_possible_for_a_ready_update_and_is_remembered()
    {
        string path = Path.Combine(_folder, "settings.json");
        var store = new SettingsStore(path);
        UpdateService service = Service(store, new FakeHandler(Github));

        service.InstallOnClose = true; // nothing ready yet
        Assert.False(service.InstallOnClose);

        await service.CheckAsync();
        await service.Staged;
        service.InstallOnClose = true;

        Assert.True(service.InstallOnClose);
        UpdateService next = Service(new SettingsStore(path), new FakeHandler(Github)); // after a restart
        await next.RestorePendingAsync(new Version(0, 2, 1));
        Assert.True(next.InstallOnClose);
    }

    [Fact]
    public async Task Choosing_close_is_forgotten_once_the_update_is_installed_or_replaced()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        bool newer = false;
        var handler = new FakeHandler(request => IsSetupRequest(request) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SetupBytes) }
            : Ok(newer ? Json(tag: "v0.4.0", setupUrl: Setup.Replace("0.3.0", "0.4.0"), assetName: "Zarpazo-Setup-0.4.0.exe") : Json()));
        UpdateService service = Service(store, handler);
        await service.CheckAsync();
        await service.Staged;
        service.InstallOnClose = true;

        newer = true; // another version comes out before the person closes the app
        await service.CheckAsync();
        await service.Staged;

        Assert.Equal(new Version(0, 4, 0), service.Ready?.Version);
        Assert.False(service.InstallOnClose); // the new one has to be chosen again
        Assert.False(store.Current.ApplyUpdateOnClose);
    }

    [Fact]
    public async Task Installing_resets_the_choice_so_a_failed_install_is_not_retried_at_every_start()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        UpdateService service = Service(store, new FakeHandler(Github), launch: (_, _) => null);
        await service.CheckAsync();
        await service.Staged;
        service.InstallOnClose = true;

        await service.InstallAsync(relaunch: false);

        Assert.False(store.Current.ApplyUpdateOnClose);
        Assert.Equal("", store.Current.PendingUpdateTag);
    }

    // ---- not interrupting ----

    [Fact]
    public async Task Restarting_while_the_person_is_busy_is_put_off_and_nothing_is_lost()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var launches = new List<bool>();
        bool busy = true;
        UpdateService service = Service(store, new FakeHandler(Github), launch: (_, relaunch) => { launches.Add(relaunch); return null; }, busy: () => busy);
        await service.CheckAsync();
        await service.Staged;
        int changes = 0;
        service.Changed += () => changes++;

        bool started = await service.RestartAsync();

        Assert.False(started);
        Assert.True(service.RestartQueued);
        Assert.Empty(launches);
        Assert.NotNull(service.Ready); // still downloaded and remembered
        Assert.Equal("v0.3.0", store.Current.PendingUpdateTag);
        Assert.Equal(1, changes); // so the tray and settings can say it is waiting

        await service.RestartAsync(); // asked again while waiting: still one queued restart
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task A_put_off_restart_happens_as_soon_as_nothing_is_in_use()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var launches = new List<bool>();
        bool busy = true;
        UpdateService service = Service(store, new FakeHandler(Github), launch: (_, relaunch) => { launches.Add(relaunch); return null; }, busy: () => busy);
        await service.CheckAsync();
        await service.Staged;
        await service.RestartAsync();

        Assert.False(await service.ResumeIfIdleAsync()); // still doing something
        Assert.Empty(launches);

        busy = false;
        Assert.True(await service.ResumeIfIdleAsync());

        Assert.Equal([true], launches);
        Assert.False(service.RestartQueued);
    }

    [Fact]
    public async Task Nothing_restarts_by_itself_when_the_person_never_asked()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var launches = new List<bool>();
        UpdateService service = Service(store, new FakeHandler(Github), launch: (_, relaunch) => { launches.Add(relaunch); return null; }, busy: () => false);
        await service.CheckAsync();
        await service.Staged;

        Assert.False(await service.ResumeIfIdleAsync());

        Assert.Empty(launches);
    }

    [Fact]
    public async Task Something_started_during_the_last_check_stops_the_restart()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var launches = new List<bool>();
        int asked = 0;
        // free when the restart is asked for, busy by the time the installer would be launched
        UpdateService service = Service(store, new FakeHandler(Github), launch: (_, relaunch) => { launches.Add(relaunch); return null; }, busy: () => ++asked > 1);
        await service.CheckAsync();
        await service.Staged;

        bool started = await service.RestartAsync();

        Assert.False(started);
        Assert.True(service.RestartQueued);
        Assert.Empty(launches);
        Assert.Equal("v0.3.0", store.Current.PendingUpdateTag); // not forgotten: the file is still good
    }

    [Fact]
    public async Task Installing_directly_while_busy_is_refused_but_closing_the_app_is_not_held_up()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        var launches = new List<bool>();
        UpdateService service = Service(store, new FakeHandler(Github), launch: (_, relaunch) => { launches.Add(relaunch); return null; }, busy: () => true);
        await service.CheckAsync();
        await service.Staged;

        await Assert.ThrowsAsync<UpdateBusyException>(() => service.InstallAsync());
        Assert.Empty(launches);

        await service.InstallAsync(relaunch: false); // the person is leaving: nothing to protect
        Assert.Equal([false], launches);
    }

    [Fact]
    public async Task A_put_off_restart_is_dropped_when_a_newer_version_replaces_the_ready_one()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        bool newer = false;
        var handler = new FakeHandler(request => IsSetupRequest(request) ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SetupBytes) }
            : Ok(newer ? Json(tag: "v0.4.0", setupUrl: Setup.Replace("0.3.0", "0.4.0"), assetName: "Zarpazo-Setup-0.4.0.exe") : Json()));
        UpdateService service = Service(store, handler, busy: () => true);
        await service.CheckAsync();
        await service.Staged;
        await service.RestartAsync();
        Assert.True(service.RestartQueued);

        newer = true;
        await service.CheckAsync();
        await service.Staged;

        Assert.False(service.RestartQueued); // it was about the old version; the new one is announced again
        Assert.Equal(new Version(0, 4, 0), service.Ready?.Version);
    }

    [Fact]
    public async Task If_a_put_off_restart_fails_later_it_is_reported_once_and_not_retried()
    {
        var store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        bool busy = true;
        UpdateService service = Service(store, new FakeHandler(Github), launch: (_, _) => "No se pudo iniciar el instalador.", busy: () => busy);
        await service.CheckAsync();
        await service.Staged;
        await service.RestartAsync();
        busy = false;

        var ex = await Assert.ThrowsAsync<UpdateException>(() => service.ResumeIfIdleAsync());

        Assert.Equal("No se pudo iniciar el instalador.", ex.Message);
        Assert.False(service.RestartQueued);
        Assert.False(await service.ResumeIfIdleAsync());
    }

    [Fact]
    public async Task A_file_already_on_disk_is_reused_only_when_size_and_hash_match()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(SetupBytes) });
        var downloader = new UpdateDownloader(new HttpClient(handler), _folder);
        ReleaseInfo release = Release();

        Assert.Null(await downloader.TryReuseAsync(release)); // nothing there yet
        string path = await downloader.DownloadAsync(release);
        Assert.Equal(path, await downloader.TryReuseAsync(release));
        Assert.Null(await downloader.TryReuseAsync(Release(sha: new string('0', 64)))); // other hash
        Assert.Null(await downloader.TryReuseAsync(Release(size: 5))); // other size
    }

    [Fact]
    public void A_remembered_tag_gives_back_the_release_and_its_installer_link()
    {
        ReleaseInfo? release = ReleaseFeed.ForTag("v0.3.0", SetupBytes.Length, SetupHash.ToUpperInvariant());

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 3, 0), release.Version);
        Assert.Equal(Setup, release.SetupUrl);
        Assert.Equal(SetupHash, release.SetupSha256);
        Assert.True(release.CanInstallInPlace);
        Assert.Null(ReleaseFeed.ForTag("", 1, SetupHash));
        Assert.Null(ReleaseFeed.ForTag("v0.3.0", 1, "not-a-hash"));
        Assert.Null(ReleaseFeed.ForTag("v0.3.0", 1, null));
    }

    // ---- saying what changed after an update ----

    [Theory]
    [InlineData("0.4.0", 0, 5, 1, true)]   // updated since the last run
    [InlineData("v0.5.0", 0, 5, 1, true)]
    [InlineData("0.5.1", 0, 5, 1, false)]  // same version
    [InlineData("0.6.0", 0, 5, 1, false)]  // a downgrade says nothing
    [InlineData("", 0, 5, 1, false)]       // first run
    [InlineData(null, 0, 5, 1, false)]
    [InlineData("garbage", 0, 5, 1, false)]
    public void An_update_is_noticed_only_when_the_last_run_was_an_older_version(string? lastRun, int major, int minor, int build, bool expected)
    {
        Assert.Equal(expected, AppVersion.IsUpdateFrom(lastRun, new Version(major, minor, build)));
    }

    [Fact]
    public void The_notes_of_a_version_are_at_its_release_page()
    {
        Assert.Equal("https://github.com/emmanuelLugo/Zarpazo/releases/tag/v0.5.1", ReleaseFeed.PageFor(new Version(0, 5, 1)));
    }

    [Fact]
    public void The_version_that_ran_last_is_remembered_and_empty_by_default()
    {
        string path = Path.Combine(_folder, "settings.json");
        var store = new SettingsStore(path);
        Assert.Equal("", store.Current.LastRunVersion);

        store.Current.LastRunVersion = "0.5.1";
        store.Save();

        Assert.Equal("0.5.1", new SettingsStore(path).Current.LastRunVersion);
    }

    // ---- which copy updates itself ----

    [Theory]
    [InlineData(@"C:\Users\Ana\AppData\Local\Programs\Zarpazo\Zarpazo.exe", @"C:\Users\Ana\AppData\Local\Programs\Zarpazo\", true)]
    [InlineData(@"C:\Users\Ana\AppData\Local\Programs\Zarpazo\Zarpazo.exe", @"c:\users\ana\appdata\local\programs\zarpazo", true)]
    [InlineData(@"D:\FlutterProyectos\Zarpazo\publish\Zarpazo.exe", @"C:\Users\Ana\AppData\Local\Programs\Zarpazo\", false)]
    [InlineData(@"C:\Users\Ana\Downloads\Zarpazo.exe", @"C:\Users\Ana\AppData\Local\Programs\Zarpazo\", false)]
    [InlineData(@"C:\Users\Ana\Downloads\Zarpazo.exe", null, false)]
    [InlineData(null, @"C:\Zarpazo\", false)]
    public void Only_the_copy_the_installer_put_on_the_pc_updates_itself(string? process, string? installLocation, bool expected)
    {
        Assert.Equal(expected, InstalledCopy.Matches(process, installLocation));
    }
}
