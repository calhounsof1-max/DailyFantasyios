using System.Globalization;
using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

public partial class AdvanceGamesPage : ContentPage
{
    record GameDef(string Prefix, string Name, string AccentHex, int Cols, int SpecialCol, string SpecialLabel);

    static readonly GameDef[] Games =
    [
        new("f5", "Fantasy 5",     "#FF8F00", 5, -1, ""),
        new("sl", "Super Lotto",   "#7B1FA2", 6,  5, "Mega"),
        new("pb", "Powerball",     "#C62828", 6,  5, "PB"),
        new("mm", "Mega Millions", "#F57F17", 6,  5, "MB"),
        new("d3", "Daily 3",       "#1565C0", 3, -1, ""),
        new("d4", "Daily 4",       "#00695C", 4, -1, ""),
        new("dd", "Daily Derby",   "#5D4037", 4, -1, ""),
    ];

    public AdvanceGamesPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        BuildCards();
    }

    void BuildCards()
    {
        _container.Children.Clear();
        var now   = DateTime.Now;
        var today = now.Date;
        bool anyData = false;

        foreach (var game in Games)
        {
            var entries = new List<(int Slot, int Row, DateTime? Start, DateTime? End, string[] Numbers, string DrawFilter)>();

            for (int slot = 0; slot < 10; slot++)
            {
                string advRaw = Preferences.Get($"{game.Prefix}_adv_{slot}", "");
                if (string.IsNullOrEmpty(advRaw)) continue;

                string setRaw = Preferences.Get($"{game.Prefix}_set_{slot}", "");
                string[] setVals = string.IsNullOrEmpty(setRaw)
                    ? new string[game.Cols * 10]
                    : setRaw.Split('|');
                if (setVals.Length < game.Cols * 10)
                    Array.Resize(ref setVals, game.Cols * 10);

                // For Daily 3: read per-row draw filters to determine 1pm vs 8pm cutoff
                string[] d3DfParts = Array.Empty<string>();
                if (game.Prefix == "d3")
                {
                    string dfRaw = Preferences.Get($"d3_drawfilters_{slot}", "");
                    if (!string.IsNullOrEmpty(dfRaw))
                        d3DfParts = dfRaw.Split('|');
                }

                string[] advRows = advRaw.Split('|');
                for (int r = 0; r < 10 && r < advRows.Length; r++)
                {
                    string[] pair = advRows[r].Split('~');
                    if (pair.Length < 2) continue;

                    DateTime? start = null, end = null;
                    if (DateTime.TryParseExact(pair[0], "yyyyMMdd", null,
                            DateTimeStyles.None, out var sd)) start = sd;
                    if (DateTime.TryParseExact(pair[1], "yyyyMMdd", null,
                            DateTimeStyles.None, out var ed)) end = ed;

                    if (start == null && end == null) continue;

                    // Read the numbers for this row
                    var nums = new string[game.Cols];
                    for (int c = 0; c < game.Cols; c++)
                    {
                        int idx = r * game.Cols + c;
                        nums[c] = idx < setVals.Length ? (setVals[idx] ?? "").Trim() : "";
                    }

                    string drawFilter = game.Prefix == "d3" && r < d3DfParts.Length
                        ? (d3DfParts[r] ?? "B") : "B";

                    entries.Add((slot, r, start, end, nums, drawFilter));
                }
            }

            if (entries.Count == 0) continue;
            anyData = true;
            _container.Children.Add(BuildGameCard(game, entries, today, now));
        }

        if (!anyData)
        {
            _container.Children.Add(new Label
            {
                Text = "No advance play dates found.\n\nSet advance dates on each game page by tapping a result row.",
                FontSize = 14,
                TextColor = Color.FromArgb("#6B7280"),
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 40, 0, 0),
            });
        }
    }

    Frame BuildGameCard(GameDef game,
        List<(int Slot, int Row, DateTime? Start, DateTime? End, string[] Numbers, string DrawFilter)> entries,
        DateTime today, DateTime now)
    {
        TimeSpan CutoffFor(string df) =>
            game.Prefix == "d3" && df == "M" ? TimeSpan.FromHours(13) : TimeSpan.FromHours(20);
        bool IsActive(DateTime? refDate, string df) =>
            refDate.HasValue && (refDate.Value.Date > today ||
                (refDate.Value.Date == today && now.TimeOfDay < CutoffFor(df)));

        var accentColor = Color.FromArgb(game.AccentHex);
        int active  = entries.Count(e => IsActive(e.End ?? e.Start, e.DrawFilter));
        int expired = entries.Count - active;

        var stack    = new VerticalStackLayout { Spacing = 0 };
        var bodyViews = new List<View>();
        bool isExpanded = true;

        var chevronLabel = new Label
        {
            Text = "▼",
            FontSize = 13,
            TextColor = Color.FromArgb("#FFFFFFCC"),
            VerticalOptions = LayoutOptions.Center,
        };

        // ── Game header ────────────────────────────────────────────────────
        var header = new Grid
        {
            BackgroundColor = accentColor,
            Padding = new Thickness(14, 10),
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        header.Add(chevronLabel, 0, 0);
        header.Add(new Label
        {
            Text = game.Name,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(6, 0, 0, 0),
        }, 1, 0);

        string summary = active > 0 && expired > 0 ? $"{active} active · {expired} expired"
                       : active  > 0 ? $"{active} active"
                       : $"{expired} expired";
        header.Add(new Label
        {
            Text = summary,
            FontSize = 11,
            TextColor = Color.FromArgb("#FFFFFFCC"),
            VerticalOptions = LayoutOptions.Center,
        }, 2, 0);

        header.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                isExpanded = !isExpanded;
                chevronLabel.Text = isExpanded ? "▼" : "▶";
                foreach (var v in bodyViews)
                    v.IsVisible = isExpanded;
            })
        });

        stack.Children.Add(header);

        // ── Entry rows ─────────────────────────────────────────────────────
        bool first = true;
        foreach (var (slot, row, start, end, nums, drawFilter) in entries
            .OrderBy(e => e.Slot).ThenBy(e => e.Row))
        {
            if (!first)
            {
                var divider = new BoxView
                {
                    HeightRequest = 1,
                    BackgroundColor = Color.FromArgb("#E5E7EB"),
                    Margin = new Thickness(0, 0),
                };
                stack.Children.Add(divider);
                bodyViews.Add(divider);
            }
            first = false;

            var refDate   = end ?? start;
            bool isActive = IsActive(refDate, drawFilter);
            int daysLeft  = refDate.HasValue ? (int)(refDate.Value.Date - today).TotalDays : int.MinValue;

            string fromText = start.HasValue ? start.Value.ToString("M/d/yy") : "—";
            string toText   = end.HasValue   ? end.Value.ToString("M/d/yy")   : "—";

            string badgeText;
            string badgeHex;
            if (!refDate.HasValue)  { badgeText = "?";                  badgeHex = "#6B7280"; }
            else if (!isActive)     { badgeText = "Expired";            badgeHex = "#DC2626"; }
            else if (daysLeft == 0) { badgeText = "TODAY";              badgeHex = "#DC2626"; }
            else if (daysLeft <= 3) { badgeText = $"{daysLeft}d left";  badgeHex = "#F59E0B"; }
            else                    { badgeText = $"{daysLeft}d left";  badgeHex = "#059669"; }

            var rowContainer = new VerticalStackLayout
            {
                Spacing = 0,
                Padding = new Thickness(14, 8),
                BackgroundColor = isActive ? Colors.White : Color.FromArgb("#FFF5F5"),
            };

            // Line 1: Set · Row label + badge on right
            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            topRow.Add(new Label
            {
                Text = $"Set {slot + 1}  ·  Row {row + 1}",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = isActive ? Color.FromArgb(game.AccentHex) : Color.FromArgb("#9CA3AF"),
                VerticalOptions = LayoutOptions.Center,
            }, 0, 0);

            topRow.Add(new Border
            {
                BackgroundColor = Color.FromArgb(badgeHex),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6) },
                Padding = new Thickness(8, 3),
                VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = badgeText,
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                },
            }, 1, 0);

            rowContainer.Children.Add(topRow);

            // Line 2: numbers as chips
            bool hasNumbers = nums.Any(n => !string.IsNullOrWhiteSpace(n));
            if (hasNumbers)
            {
                var numsRow = new FlexLayout
                {
                    Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
                    Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
                    Margin = new Thickness(0, 5, 0, 3),
                };

                int mainCols = game.SpecialCol >= 0 ? game.SpecialCol : game.Cols;
                for (int c = 0; c < game.Cols; c++)
                {
                    string val = c < nums.Length ? nums[c] : "";
                    if (string.IsNullOrWhiteSpace(val)) continue;

                    bool isSpecial = game.SpecialCol >= 0 && c == game.SpecialCol;
                    string chipBg  = isSpecial ? game.AccentHex : "#E8EDF2";
                    string chipFg  = isSpecial ? "#FFFFFF" : "#1E2733";

                    // For special ball, prefix the label
                    string display = isSpecial && !string.IsNullOrEmpty(game.SpecialLabel)
                        ? $"{game.SpecialLabel}:{val}"
                        : val;

                    numsRow.Children.Add(new Border
                    {
                        BackgroundColor = Color.FromArgb(chipBg),
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(5) },
                        Padding = new Thickness(9, 4),
                        Margin = new Thickness(0, 0, 4, 4),
                        Content = new Label
                        {
                            Text = display,
                            FontSize = 13,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb(chipFg),
                        },
                    });
                }
                rowContainer.Children.Add(numsRow);
            }

            // Line 3: From / To dates
            var datesRow = new HorizontalStackLayout { Spacing = 12 };
            datesRow.Children.Add(new Label
            {
                Text = $"From: {fromText}",
                FontSize = 11,
                TextColor = isActive ? Color.FromArgb("#6B7280") : Color.FromArgb("#9CA3AF"),
            });
            datesRow.Children.Add(new Label
            {
                Text = "→",
                FontSize = 11,
                TextColor = Color.FromArgb("#9CA3AF"),
            });
            datesRow.Children.Add(new Label
            {
                Text = $"To: {toText}",
                FontSize = 11,
                FontAttributes = isActive ? FontAttributes.Bold : FontAttributes.None,
                TextColor = isActive ? Color.FromArgb("#374151") : Color.FromArgb("#9CA3AF"),
            });
            rowContainer.Children.Add(datesRow);

            // Tap row to navigate to that game/set/row
            int capturedSlot = slot;
            int capturedRow  = row;
            rowContainer.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () =>
                    await NavigateToGame(game, capturedSlot, capturedRow))
            });

            stack.Children.Add(rowContainer);
            bodyViews.Add(rowContainer);
        }

        return new Frame
        {
            BackgroundColor = Colors.White,
            CornerRadius = 12,
            BorderColor = Color.FromArgb("#E5E7EB"),
            Padding = 0,
            HasShadow = false,
            IsClippedToBounds = true,
            Content = stack,
        };
    }

    // ── Navigate to game page ──────────────────────────────────────────────────

    async Task NavigateToGame(GameDef game, int slot, int row)
    {
        PendingHighlight.Game = game.Prefix.ToUpper();
        PendingHighlight.Slot = slot;
        PendingHighlight.Row  = row;

        switch (game.Prefix)
        {
            case "f5":
                WinnerPage.ComingFrom = "advgames";
                AppShell.WinnerPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(WinnerPage), false);
                break;
            case "sl":
                SuperLottoPage.ComingFrom = "advgames";
                AppShell.SuperLottoPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(SuperLottoPage), false);
                break;
            case "pb":
                PowerballPage.ComingFrom = "advgames";
                AppShell.PowerballPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(PowerballPage), false);
                break;
            case "mm":
                MegaMillionsPage.ComingFrom = "advgames";
                AppShell.MegaMillionsPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(MegaMillionsPage), false);
                break;
            case "d3":
                Daily3Page.ComingFrom = "advgames";
                AppShell.Daily3PageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(Daily3Page), false);
                break;
            case "d4":
                Daily4Page.ComingFrom = "advgames";
                AppShell.Daily4PageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(Daily4Page), false);
                break;
            case "dd":
                DailyDerbyPage.ComingFrom = "advgames";
                AppShell.DailyDerbyPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(DailyDerbyPage), false);
                break;
        }
    }

    // ── Navigation ─────────────────────────────────────────────────────────────

    async void BtnBack_Clicked(object sender, EventArgs e)
    {
        Shell.Current.CurrentPage.TranslationX = -DeviceDisplay.MainDisplayInfo.Width
                                                 / DeviceDisplay.MainDisplayInfo.Density;
        await Shell.Current.GoToAsync("..", false);
    }

    protected override bool OnBackButtonPressed()
    {
        _ = BtnBack_ClickedAsync();
        return true;
    }

    async Task BtnBack_ClickedAsync()
    {
        Shell.Current.CurrentPage.TranslationX = -DeviceDisplay.MainDisplayInfo.Width
                                                 / DeviceDisplay.MainDisplayInfo.Density;
        await Shell.Current.GoToAsync("..", false);
    }
}
