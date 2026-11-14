namespace Zarpazo.Recording;

/// <summary>How much data each picture of the saved video gets: more is sharper and heavier (see <see cref="VideoExportOptions"/>).</summary>
internal enum ExportQuality
{
    /// <summary>A small file; text is a little soft.</summary>
    Light,

    /// <summary>What Zarpazo always saved.</summary>
    Balanced,

    /// <summary>A heavy file with sharper text.</summary>
    High,
}
