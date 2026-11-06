using System.Windows;
using System.Windows.Media;

namespace Zarpazo.Editor;

internal sealed class ArrowAnnotation(Color color, double thickness, Point start)
    : LineAnnotation(color, thickness, start)
{
    private const double HeadAngleDegrees = 25;

    public override void Draw(DrawingContext context)
    {
        base.Draw(context);

        Vector shaft = Start - End;
        if (shaft.Length == 0)
        {
            return;
        }

        shaft.Normalize();
        shaft *= Math.Max(12, Stroke.Thickness * 4);
        foreach (double angle in new[] { HeadAngleDegrees, -HeadAngleDegrees })
        {
            Matrix rotation = Matrix.Identity;
            rotation.Rotate(angle);
            context.DrawLine(Stroke, End, End + shaft * rotation);
        }
    }
}
