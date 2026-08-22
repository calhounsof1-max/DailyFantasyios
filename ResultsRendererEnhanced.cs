namespace DailyFantasyMAUI;

/// <summary>
/// Enhanced Results renderer — draws winning numbers and player ticket numbers
/// as colored circular bubble balls.
/// </summary>
public static class ResultsRendererEnhanced
{
    // ── Winning numbers box ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the winning-numbers view for the section header box (balls style).
    /// For D3 pass both winningNums (Midday) and winningNums2 (Evening).
    /// For D4/DD with no raw int[] pass null — falls back to the plain winNumbers string.
    /// </summary>
    public static View MakeWinningNumbersView(
        int[]? winningNums, int[]? winningNums2, string winNumbers,
        Color accent, int drawNumber, int drawNumber2, string winNums2Suffix)
    {
        if (winningNums2 != null)
        {
            // D3: two labeled rows — Midday (blue) + Evening (red)
            var stack = new VerticalStackLayout { Spacing = 4 };

            if (winningNums != null && winningNums.Length > 0)
            {
                string mdLabel = drawNumber > 0 ? $"Midday  #{drawNumber}" : "Midday";
                stack.Children.Add(new Label
                {
                    Text = mdLabel, FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#333")
                });
                stack.Children.Add(MakeWinningBalls(winningNums, Color.FromArgb("#42A5F5")));
            }
            else
            {
                stack.Children.Add(new Label
                {
                    Text = "Midday: pending", FontSize = 11,
                    TextColor = Color.FromArgb("#888")
                });
            }

            if (winningNums2.Length > 0)
            {
                string evLabel = drawNumber2 > 0 ? $"Evening  #{drawNumber2}" : "Evening";
                if (!string.IsNullOrEmpty(winNums2Suffix)) evLabel += $"  {winNums2Suffix}";
                stack.Children.Add(new Label
                {
                    Text = evLabel, FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#333")
                });
                stack.Children.Add(MakeWinningBalls(winningNums2, Color.FromArgb("#EF5350")));
            }
            else
            {
                stack.Children.Add(new Label
                {
                    Text = "Evening: pending", FontSize = 11,
                    TextColor = Color.FromArgb("#888")
                });
            }

            return stack;
        }
        else if (winningNums != null && winningNums.Length > 0)
        {
            var stack = new VerticalStackLayout { Spacing = 2 };
            stack.Children.Add(new Label
            {
                Text = "Winning Numbers:",
                FontSize = 11, FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#333")
            });
            stack.Children.Add(MakeWinningBalls(winningNums, accent));
            return stack;
        }
        else
        {
            // D4 / DD / no draw — plain text fallback
            return new Label
            {
                Text = winNumbers, FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#222")
            };
        }
    }

    // ── Player ticket numbers ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the numbers view for a ticket row (colored circle balls).
    /// isPast = grey balls; matched numbers in winSet = orange balls.
    /// </summary>
    public static View MakePlayerNumbersView(string numbers, Color accent, bool isPast,
        HashSet<int>? winSet, string gameKey, bool bonusMatch = false)
        => MakePlayerBalls(numbers, accent, isPast, winSet, gameKey, bonusMatch);

    // ── Private drawing helpers ───────────────────────────────────────────────

    static View MakeWinningBalls(int[] nums, Color accent)
    {
        var layout = new FlexLayout
        {
            Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
            Wrap      = Microsoft.Maui.Layouts.FlexWrap.Wrap
        };
        foreach (var n in nums)
        {
            layout.Children.Add(new Border
            {
                WidthRequest = 30, HeightRequest = 30,
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
                BackgroundColor = accent,
                Margin = new Thickness(2, 2),
                Content = new Label
                {
                    Text = n.ToString(),
                    FontSize = 11, FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions   = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment   = TextAlignment.Center,
                }
            });
        }
        return layout;
    }

    static View MakePlayerBalls(string numbers, Color accent, bool isPast,
        HashSet<int>? winSet, string gameKey, bool bonusMatch = false)
    {
        double size     = 22;
        double fontSize = size * 0.5;

        var layout = new HorizontalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
        };

        foreach (var part in numbers.Split(new char[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("|"))
            {
                int colonIdx = part.IndexOf(':');
                if (colonIdx >= 0 && int.TryParse(part[(colonIdx + 1)..], out int bonus))
                {
                    layout.Children.Add(new Label
                    {
                        Text = "·", FontSize = 10,
                        TextColor = Color.FromArgb("#999"),
                        VerticalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0)
                    });
                    Color bonusColor = bonusMatch ? Color.FromArgb("#2E7D32")
                                      : isPast    ? Color.FromArgb("#BBBBBB")
                                                  : Color.FromArgb("#F57F17");
                    layout.Children.Add(new Border
                    {
                        WidthRequest = size, HeightRequest = size,
                        StrokeThickness = 0,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
                        BackgroundColor = bonusColor,
                        Margin = new Thickness(0),
                        Content = new Label
                        {
                            Text = bonus.ToString(),
                            FontSize = fontSize, FontAttributes = FontAttributes.Bold,
                            TextColor = Colors.White,
                            HorizontalOptions = LayoutOptions.Center,
                            VerticalOptions   = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.Center,
                            VerticalTextAlignment   = TextAlignment.Center,
                        }
                    });
                }
                continue;
            }

            if (!int.TryParse(part, out int n)) continue;
            bool isMatch  = winSet?.Contains(n) ?? false;
            Color ballColor = isPast    ? Color.FromArgb("#BBBBBB")
                            : isMatch   ? Color.FromArgb("#E65100")
                                        : accent;
            layout.Children.Add(new Border
            {
                WidthRequest = size, HeightRequest = size,
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
                BackgroundColor = ballColor,
                Margin = new Thickness(0),
                Content = new Label
                {
                    Text = n.ToString(),
                    FontSize = fontSize, FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions   = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment   = TextAlignment.Center,
                }
            });
        }
        return layout;
    }
}
