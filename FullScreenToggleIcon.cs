namespace DailyFantasyMAUI;

// Hand-drawn "expand"/"collapse" arrow-cluster icon for HotSpotPage's Full Screen toggle —
// user supplied a reference image (four bold chevron arrows pointing outward for "expand",
// pointing inward for "collapse") and asked for that exact look, which no built-in emoji/glyph
// matches closely enough. Same GraphicsView+IDrawable pattern BallWatermark.cs already uses.
public class FullScreenToggleIcon : IDrawable
{
    // false = expand (⛶, entering Full Screen) — arrows point away from center.
    // true  = collapse (▭, back to Regular Mode) — arrows point toward center.
    public bool Collapse { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float cx = dirtyRect.Width  / 2f;
        float cy = dirtyRect.Height / 2f;
        float r  = Math.Min(dirtyRect.Width, dirtyRect.Height) / 2f;

        float offset      = r * 0.50f;  // how far each chevron's anchor sits from center
        float armLen      = r * 0.62f;  // length of each chevron's two strokes
        float strokeWidth = Math.Max(2f, r * 0.30f);

        canvas.StrokeColor    = Colors.White;
        canvas.StrokeSize     = strokeWidth;
        canvas.StrokeLineCap  = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        // NE, SE, SW, NW (Y grows downward, so -45° is up-right).
        double[] dirDeg = { -45, 45, 135, -135 };
        foreach (double dirAngleDeg in dirDeg)
        {
            double dirRad = dirAngleDeg * Math.PI / 180.0;
            float ax = cx + (float)(Math.Cos(dirRad) * offset);
            float ay = cy + (float)(Math.Sin(dirRad) * offset);
            double tipAngle = Collapse ? dirRad + Math.PI : dirRad;
            DrawChevron(canvas, ax, ay, tipAngle, armLen);
        }
    }

    static void DrawChevron(ICanvas canvas, float ax, float ay, double tipAngleRad, float len)
    {
        float tipX = ax + (float)(Math.Cos(tipAngleRad) * len * 0.35f);
        float tipY = ay + (float)(Math.Sin(tipAngleRad) * len * 0.35f);
        double backAngle = tipAngleRad + Math.PI;
        double spread = 30 * Math.PI / 180.0;
        float a1X = tipX + (float)(Math.Cos(backAngle - spread) * len);
        float a1Y = tipY + (float)(Math.Sin(backAngle - spread) * len);
        float a2X = tipX + (float)(Math.Cos(backAngle + spread) * len);
        float a2Y = tipY + (float)(Math.Sin(backAngle + spread) * len);
        canvas.DrawLine(a1X, a1Y, tipX, tipY);
        canvas.DrawLine(tipX, tipY, a2X, a2Y);
    }
}
