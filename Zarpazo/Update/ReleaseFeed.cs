using System.Globalization;
using System.Text.Json;

namespace Zarpazo.Update;

/// <summary>Where releases come from, and a reader for what GitHub's "latest release" call returns.</summary>
internal static class ReleaseFeed
{
    public const string LatestUrl = "https://api.github.com/repos/emmanuelLugo/Zarpazo/releases/latest";

    /// <summary>The installer is only ever downloaded from here: a link anywhere else in the answer is ignored.</summary>
    public const string DownloadPrefix = "https://github.com/emmanuelLugo/Zarpazo/releases/download/";

    public const string ReleasesPage = "https://github.com/emmanuelLugo/Zarpazo/releases/latest";

    private const string PagePrefix = "https://github.com/emmanuelLugo/Zarpazo/";
    private const string DigestPrefix = "sha256:";

    /// <summary>The release, or null when it is a draft or pre-release or its tag is not a version.</summary>
    /// <exception cref="JsonException">The text is not JSON.</exception>
    public static ReleaseInfo? Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Flag(root, "draft") || Flag(root, "prerelease"))
        {
            return null;
        }

        string? tag = Text(root, "tag_name");
        if (!AppVersion.TryParse(tag, out Version version))
        {
            return null;
        }

        string page = Text(root, "html_url") ?? "";
        if (!page.StartsWith(PagePrefix, StringComparison.Ordinal))
        {
            page = ReleasesPage;
        }

        // The versioned name never changes meaning, unlike Zarpazo-Setup.exe, which always points at the newest.
        string setupName = $"Zarpazo-Setup-{tag!.Trim().TrimStart('v', 'V')}.exe";
        string? setupUrl = null;
        long setupSize = 0;
        string? sha256 = null;
        if (root.TryGetProperty("assets", out JsonElement assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string? url = Text(asset, "browser_download_url");
                if (!string.Equals(Text(asset, "name"), setupName, StringComparison.OrdinalIgnoreCase)
                    || url == null || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                setupUrl = url;
                setupSize = asset.TryGetProperty("size", out JsonElement size) && size.TryGetInt64(out long bytes) ? bytes : 0;
                sha256 = Hash(Text(asset, "digest"));
                break;
            }
        }

        return new ReleaseInfo(version, tag.Trim(), page, Text(root, "body") ?? "", setupUrl, setupSize, sha256);
    }

    // "sha256:" followed by 64 hex digits; anything else means the hash can't be relied on.
    private static string? Hash(string? digest)
    {
        if (digest == null || !digest.StartsWith(DigestPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string hex = digest[DigestPrefix.Length..].ToLower(CultureInfo.InvariantCulture);
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex : null;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}
