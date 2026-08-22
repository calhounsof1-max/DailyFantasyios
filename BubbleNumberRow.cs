using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

/// <summary>
/// Renders a space-separated (or compact) number string either as plain monospace text
/// or as lottery-ball ellipse bubbles — toggled via ShowBubbles.
/// When HasMegaBall=true the last bubble is rendered in MegaBallColor (gold star).
/// </summary>
public class BubbleNumberRow : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(BubbleNumberRow), "",
            propertyChanged: (b, o, n) => ((BubbleNumberRow)b).Rebuild());

    public static readonly BindableProperty ShowBubblesProperty =
        BindableProperty.Create(nameof(ShowBubbles), typeof(bool), typeof(BubbleNumberRow), false,
            propertyChanged: (b, o, n) => ((BubbleNumberRow)b).Rebuild());

    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(BubbleNumberRow),
            Color.FromArgb("#3B82F6"),
            propertyChanged: (b, o, n) => ((BubbleNumberRow)b).Rebuild());

    public static readonly BindableProperty PlainTextColorProperty =
        BindableProperty.Create(nameof(PlainTextColor), typeof(Color), typeof(BubbleNumberRow),
            Color.FromArgb("#C8DFF2"),
            propertyChanged: (b, o, n) => ((BubbleNumberRow)b).Rebuild());

    public static readonly BindableProperty HasMegaBallProperty =
        BindableProperty.Create(nameof(HasMegaBall), typeof(bool), typeof(BubbleNumberRow), false,
            propertyChanged: (b, o, n) => ((BubbleNumberRow)b).Rebuild());

    public string Text           { get => (string)GetValue(TextProperty);          set => SetValue(TextProperty, value); }
    public bool   ShowBubbles    { get => (bool)GetValue(ShowBubblesProperty);     set => SetValue(ShowBubblesProperty, value); }
    public Color  AccentColor    { get => (Color)GetValue(AccentColorProperty);    set => SetValue(AccentColorProperty, value); }
    public Color  PlainTextColor { get => (Color)GetValue(PlainTextColorProperty); set => SetValue(PlainTextColorProperty, value); }
    public bool   HasMegaBall    { get => (bool)GetValue(HasMegaBallProperty);     set => SetValue(HasMegaBallProperty, value); }

    // Red for mega ball bubble
    static readonly Color MegaBallColor = Color.FromArgb("#E53935");

    void Rebuild()
    {
        if (!ShowBubbles)
        {
            Content = new Label
            {
                Text                    = Text,
                FontFamily              = "Monospace",
                FontSize                = 16,
                FontAttributes          = FontAttributes.Bold,
                TextColor               = PlainTextColor,
                BackgroundColor         = Colors.Transparent,
                Padding                 = new Thickness(12, 10),
                VerticalOptions         = LayoutOptions.Center,
            };
            return;
        }

        // Split numbers — handles "01 12 23 39" (space-sep) and "489" (D3 compact, no spaces)
        var parts = (Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1 && parts[0].Length > 1 && parts[0].All(char.IsDigit))
            parts = parts[0].Select(c => c.ToString()).ToArray();

        double size     = parts.Length > 5 ? 26 : 30;
        double fontSize = size * 0.52;

        var layout = new FlexLayout
        {
            Direction      = Microsoft.Maui.Layouts.FlexDirection.Row,
            Wrap           = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            AlignItems     = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            Margin         = new Thickness(8, 6),
        };

        int lastIdx = parts.Length - 1;
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out int n)) continue;
            bool isMega = HasMegaBall && (i == lastIdx);
            Color bubbleColor = isMega ? MegaBallColor : AccentColor;
            layout.Children.Add(new Border
            {
                WidthRequest    = size,
                HeightRequest   = size,
                StrokeThickness = 0,
                StrokeShape     = new Ellipse(),
                BackgroundColor = bubbleColor,
                Margin          = new Thickness(2),
                Content         = new Label
                {
                    Text                    = n.ToString(),
                    FontSize                = fontSize,
                    FontAttributes          = FontAttributes.Bold,
                    TextColor               = isMega ? Colors.White : Colors.Black,
                    HorizontalOptions       = LayoutOptions.Center,
                    VerticalOptions         = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment   = TextAlignment.Center,
                }
            });
        }

        Content = layout;
    }
}
