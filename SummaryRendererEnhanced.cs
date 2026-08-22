namespace DailyFantasyMAUI;

/// <summary>
/// Enhanced Summary renderer — draws winning numbers as colored circular bubble balls.
/// Draws the whole row of balls into a single GraphicsView instead of one Border+Ellipse
/// per ball: a shaped Border needs its own Skia-drawn native view, and at 5-6 balls per
/// winning record across dozens of records that was adding several seconds to BuildUI().
/// </summary>
public static class SummaryRendererEnhanced
{
    public static View MakeNumbersView(string numbers, Color accent, string gameKey)
    {
        if (string.IsNullOrWhiteSpace(numbers))
            return new Label { Text = "—", FontSize = 10, TextColor = Color.FromArgb("#AAA"),
                               VerticalOptions = LayoutOptions.Center };

        double size = (gameKey == "D3" || gameKey == "D4") ? 26 : 22;

        var nums = numbers
            .Split(new char[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => int.TryParse(p, out _))
            .Select(int.Parse)
            .ToList();

        if (nums.Count == 0)
            return new Label { Text = "—", FontSize = 10, TextColor = Color.FromArgb("#AAA"),
                               VerticalOptions = LayoutOptions.Center };

        double spacing = 2;
        double width   = nums.Count * (size + spacing) + spacing;

        return new GraphicsView
        {
            WidthRequest    = width,
            HeightRequest   = size + spacing * 2,
            VerticalOptions = LayoutOptions.Center,
            Drawable        = new BallRowDrawable(nums, accent, size, spacing),
        };
    }
}

class BallRowDrawable : IDrawable
{
    readonly List<int> _nums;
    readonly Color      _accent;
    readonly float      _size;
    readonly float      _spacing;

    public BallRowDrawable(List<int> nums, Color accent, double size, double spacing)
    {
        _nums = nums; _accent = accent; _size = (float)size; _spacing = (float)spacing;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FontColor = Colors.White;
        canvas.FontSize  = _size * 0.56f;
        float x = _spacing;
        foreach (var n in _nums)
        {
            var rect = new RectF(x, _spacing, _size, _size);
            canvas.FillColor = _accent;
            canvas.FillEllipse(rect);
            canvas.DrawString(n.ToString(), rect, HorizontalAlignment.Center, VerticalAlignment.Center);
            x += _size + _spacing;
        }
    }
}
