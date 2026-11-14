namespace Zarpazo.Recording;

/// <summary>How big the saved video is next to the recording (see <see cref="VideoExportOptions"/>).</summary>
internal enum ExportResolution
{
    /// <summary>The size of the recording, or of its crop.</summary>
    Original,

    /// <summary>90 % of each side.</summary>
    High,

    /// <summary>75 % of each side.</summary>
    Medium,

    /// <summary>Half of each side: a small file to send.</summary>
    Low,
}
