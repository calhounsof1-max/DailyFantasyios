namespace DailyFantasyMAUI;

/// <summary>
/// Classic Summary renderer — draws winning numbers as plain colored text.
/// </summary>
public static class SummaryRendererClassic
{
    public static View MakeNumbersView(string numbers, Color accent, string gameKey)
    {
        if (string.IsNullOrWhiteSpace(numbers))
            return new Label { Text = "—", FontSize = 10, TextColor = Color.FromArgb("#AAA"),
                               VerticalOptions = LayoutOptions.Center };

        var fs = new FormattedString();
        foreach (var part in numbers.Split(new char[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out _))
            {
                fs.Spans.Add(new Span
                {
                    Text = part + " ", FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = accent
                });
                continue;
            }
            fs.Spans.Add(new Span
            {
                Text = part + " ", FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = accent
            });
        }

        return new Label
        {
            FormattedText   = fs,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode   = LineBreakMode.NoWrap,
        };
    }
}
