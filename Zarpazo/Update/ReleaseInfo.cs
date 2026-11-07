namespace Zarpazo.Update;

/// <summary>A published version of Zarpazo, as far as the updater needs to know it.</summary>
/// <param name="PageUrl">The release page, for reading what changed or downloading by hand.</param>
/// <param name="SetupUrl">The installer, or null if the release has none that can be trusted.</param>
/// <param name="SetupSha256">The installer's hash in lower-case hex, as GitHub reports it; null if it didn't.</param>
internal sealed record ReleaseInfo(Version Version, string Tag, string PageUrl, string Notes, string? SetupUrl, long SetupSize, string? SetupSha256)
{
    /// <summary>The installer can be downloaded and checked, so the update can be applied from inside the app.</summary>
    public bool CanInstallInPlace => SetupUrl != null && SetupSha256 != null;
}
