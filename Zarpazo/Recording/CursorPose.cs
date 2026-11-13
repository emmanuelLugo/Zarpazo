namespace Zarpazo.Recording;

/// <summary>Where the drawn pointer is at a moment and how it stands.</summary>
/// <param name="X">The tip, in shares of the recorded picture (0 to 1).</param>
/// <param name="Angle">How far it leans, in degrees; positive turns it clockwise around the tip.</param>
/// <param name="Scale">1 at rest; a little less for a moment after a click.</param>
internal readonly record struct CursorPose(double X, double Y, double Angle, double Scale);
