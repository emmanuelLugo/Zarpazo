using System.Windows.Media.Imaging;

namespace Zarpazo.Editor;

/// <summary>The frozen screenshot plus how many of its pixels fit in one layout unit of the overlay.</summary>
internal sealed record ScreenSource(BitmapSource Image, double ScaleX, double ScaleY);
