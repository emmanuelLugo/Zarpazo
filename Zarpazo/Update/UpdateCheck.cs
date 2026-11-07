namespace Zarpazo.Update;

/// <summary>The outcome of asking GitHub for the latest version: a newer release, nothing newer, or a failure.</summary>
internal sealed record UpdateCheck(ReleaseInfo? Newer, string? Error)
{
    public static UpdateCheck UpToDate { get; } = new(null, null);

    public static UpdateCheck Found(ReleaseInfo release) => new(release, null);

    public static UpdateCheck Failed(string message) => new(null, message);

    public bool IsFailure => Error != null;
}
