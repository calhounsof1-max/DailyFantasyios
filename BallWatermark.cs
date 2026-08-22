namespace DailyFantasyMAUI;

public class BallWatermark : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        float cx = dirtyRect.Width  / 2f;
        float cy = dirtyRect.Height / 2f;
        float r  = Math.Min(dirtyRect.Width, dirtyRect.Height) / 2f * 0.88f;

        // Gold ball (radial gradient: bright gold center → dark gold edge)
        var ballPaint = new RadialGradientPaint
        {
            StartColor = Color.FromArgb("#FFE95A"),
            EndColor   = Color.FromArgb("#7A5000"),
            Center     = new Point(0.38, 0.30),
            Radius     = 0.65,
        };
        var ballRect = new RectF(cx - r, cy - r, r * 2, r * 2);
        canvas.SetFillPaint(ballPaint, ballRect);
        canvas.FillCircle(cx, cy, r);

        // Shine highlight (white ellipse, upper-left)
        canvas.FillColor = Colors.White.WithAlpha(0.35f);
        canvas.FillEllipse(cx - r * 0.42f, cy - r * 0.52f, r * 0.60f, r * 0.38f);

        // "7"
        canvas.FontColor  = Colors.White;
        canvas.FontSize   = r * 1.15f;
        canvas.Font       = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.DrawString("7", cx, cy - r * 0.08f, HorizontalAlignment.Center);

        // "LOTTERY"
        canvas.FontSize = r * 0.27f;
        canvas.DrawString("LOTTERY", cx, cy + r * 0.64f, HorizontalAlignment.Center);

        // Corner stars
        DrawStar(canvas, cx - r * 0.78f, cy - r * 0.78f, r * 0.14f);
        DrawStar(canvas, cx + r * 0.78f, cy - r * 0.78f, r * 0.11f);
        DrawStar(canvas, cx + r * 0.78f, cy + r * 0.72f, r * 0.10f);
        DrawStar(canvas, cx - r * 0.78f, cy + r * 0.72f, r * 0.11f);
    }

    static void DrawStar(ICanvas canvas, float cx, float cy, float size)
    {
        canvas.FillColor = Color.FromArgb("#FFD700");
        int points = 5;
        var path = new PathF();
        for (int i = 0; i < points * 2; i++)
        {
            double angle = Math.PI / points * i - Math.PI / 2;
            float rad    = i % 2 == 0 ? size : size * 0.45f;
            float x      = cx + (float)(Math.Cos(angle) * rad);
            float y      = cy + (float)(Math.Sin(angle) * rad);
            if (i == 0) path.MoveTo(x, y);
            else        path.LineTo(x, y);
        }
        path.Close();
        canvas.FillPath(path);
    }
}
