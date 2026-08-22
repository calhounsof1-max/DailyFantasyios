namespace DailyFantasyMAUI;

/// <summary>
/// Classic Results renderer — draws winning numbers and player ticket numbers
/// as plain formatted text (no bubble balls). This is the default view.
/// </summary>
public static class ResultsRendererClassic
{
    // ── Winning numbers box ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the winning-numbers view for the section header box (plain text style).
    /// </summary>
    public static View MakeWinningNumbersView(
        int[]? winningNums, int[]? winningNums2, string winNumbers,
        Color accent, int drawNumber, int drawNumber2, string winNums2Suffix)
    {
        if (winningNums2 != null)
        {
            // D3: Midday + Evening on separate lines, plain text
            var stack = new VerticalStackLayout { Spacing = 3 };

            string mdLabel = drawNumber > 0 ? $"Midday #{drawNumber}" : "Midday";
            string midText = winningNums != null && winningNums.Length > 0
                ? $"{mdLabel}:  {string.Join("  ", winningNums)}"
                : "Midday: pending";
            stack.Children.Add(new Label
            {
                Text = midText, FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1565C0"),
                LineBreakMode = LineBreakMode.NoWrap,
            });

            string evLabel = drawNumber2 > 0 ? $"Evening #{drawNumber2}" : "Evening";
            if (!string.IsNullOrEmpty(winNums2Suffix)) evLabel += $" {winNums2Suffix}";
            string eveText = winningNums2.Length > 0
                ? $"{evLabel}:  {string.Join("  ", winningNums2)}"
                : "Evening: pending";
            stack.Children.Add(new Label
            {
                Text = eveText, FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#C62828"),
                LineBreakMode = LineBreakMode.NoWrap,
            });

            return stack;
        }
        else
        {
            // All other games — the winNumbers string already has draw# + numbers
            return new Label
            {
                Text = winNumbers, FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#222"),
                LineBreakMode = LineBreakMode.NoWrap,
            };
        }
    }

    // ── Player ticket numbers ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the numbers view for a ticket row (plain FormattedText style).
    /// Matched numbers are shown in orange bold; past rows are grey.
    /// </summary>
    public static View MakePlayerNumbersView(string numbers, Color accent, bool isPast,
        HashSet<int>? winSet, string gameKey, bool bonusMatch = false)
    {
        var fs = new FormattedString();
        string? bonusNumberText = null;

        foreach (var part in numbers.Split(new char[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("|"))
            {
                // Bonus ball: |PB:12, |M:5, |MB:23 — rendered as its own ball below, not inline text
                int colonIdx = part.IndexOf(':');
                bonusNumberText = colonIdx >= 0 ? part[(colonIdx + 1)..] : part[1..];
                continue;
            }

            if (!int.TryParse(part, out int n)) continue;
            bool isMatch = !isPast && (winSet?.Contains(n) ?? false);

            fs.Spans.Add(new Span
            {
                Text = n.ToString() + " ",
                FontSize = 11,
                FontAttributes = isMatch ? FontAttributes.Bold : FontAttributes.None,
                TextColor = isPast    ? Color.FromArgb("#999999")
                          : isMatch   ? Color.FromArgb("#E65100")
                                      : Color.FromArgb("#333333")
            });
        }

        var numsLabel = new Label
        {
            FormattedText   = fs,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode   = LineBreakMode.NoWrap,
        };

        if (bonusNumberText == null)
            return numsLabel;

        // Mega/Power/Bonus ball: an actual circle, green when it matched the draw's bonus number
        var row = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        row.Children.Add(numsLabel);
        row.Children.Add(new Label
        {
            Text = "·", FontSize = 11,
            TextColor = Color.FromArgb("#999999"),
            VerticalOptions = LayoutOptions.Center,
        });

        Color bonusBallColor = bonusMatch ? Color.FromArgb("#2E7D32")
                              : isPast    ? Color.FromArgb("#BBBBBB")
                                          : Color.FromArgb("#F57F17");
        row.Children.Add(new Border
        {
            WidthRequest    = 20,
            HeightRequest   = 20,
            StrokeThickness = 0,
            StrokeShape     = new Microsoft.Maui.Controls.Shapes.Ellipse(),
            BackgroundColor = bonusBallColor,
            Content = new Label
            {
                Text                    = bonusNumberText,
                FontSize                = 10,
                FontAttributes          = FontAttributes.Bold,
                TextColor               = Colors.White,
                HorizontalOptions       = LayoutOptions.Center,
                VerticalOptions         = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment   = TextAlignment.Center,
            }
        });
        return row;
    }
}
