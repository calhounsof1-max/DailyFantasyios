namespace DailyFantasyMAUI;

public partial class ResultsPage : ContentPage
{
    public ResultsPage()
    {
        InitializeComponent();
    }

    internal void PrePosition(bool fromRight)
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        TranslationX = fromRight ? w : -w;
    }

    protected override void OnAppearing()
    {
        this.TranslateTo(0, 0, 220, Easing.CubicOut);
        base.OnAppearing();
        resultDatePicker.Date = DateTime.Today;
        _ = RunCheck(resultDatePicker?.Date ?? DateTime.Today);
    }

    // ── Date picker — auto-run check when date changes ───────────────────────

    private void DatePicker_DateSelected(object sender, DateChangedEventArgs e)
    {
        _ = RunCheck(e.NewDate ?? DateTime.Today);
    }

    // ── CHECK TICKETS button ─────────────────────────────────────────────────

    private async void BtnCheckTickets_Clicked(object sender, EventArgs e)
    {
        await RunCheck(resultDatePicker?.Date ?? DateTime.Today);
    }

    private async Task RunCheck(DateTime date)
    {
        SetBusy(true, $"Checking {date:ddd, MMM d, yyyy}...");
        resultsContainer.Children.Clear();

        var data = await ResultsPageCls.ProcessDateAsync(date);

        SetBusy(false, "");
        BuildResultsUI(data);
    }

    // ── Build results UI ─────────────────────────────────────────────────────

    private void BuildResultsUI(DateResultData data)
    {
        resultsContainer.Children.Clear();

        if (!string.IsNullOrEmpty(data.Error))
        {
            lblBottom.Text = data.Error;
            resultsContainer.Children.Add(new Label
            {
                Text = data.Error,
                TextColor = Color.FromArgb("#888"),
                FontSize = 14,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 28)
            });
            return;
        }

        // ── F5 section ───────────────────────────────────────────────────────
        string f5Win = data.F5Numbers.Length > 0
            ? "Winning: " + string.Join("  ", data.F5Numbers.Select(n => n.ToString("D2")))
            : "No draw found for this date";

        BuildSection("FANTASY 5", "#FF8F00", f5Win,
            data.Winners.Where(w => w.Game == "F5").ToList());

        // ── SL section ───────────────────────────────────────────────────────
        string slWin = data.SLMain.Length > 0
            ? "Winning: " + string.Join("  ", data.SLMain.Select(n => n.ToString("D2")))
              + "   Mega: " + data.SLMega.ToString("D2")
            : "No draw found for this date";

        BuildSection("SUPER LOTTO PLUS", "#7B1FA2", slWin,
            data.Winners.Where(w => w.Game == "SL").ToList());

        // ── PB section ───────────────────────────────────────────────────────
        string pbWin = data.PBMain.Length > 0
            ? "Winning: " + string.Join("  ", data.PBMain.Select(n => n.ToString("D2")))
              + "   PB: " + data.PBBall.ToString("D2")
            : "No draw found for this date";

        BuildSection("POWERBALL", "#C62828", pbWin,
            data.Winners.Where(w => w.Game == "PB").ToList());

        // ── D3 section ───────────────────────────────────────────────────────
        string d3Win;
        if (data.D3Midday != null || data.D3Evening != null)
        {
            string mid = data.D3Midday  != null ? string.Join("-", data.D3Midday)  : "?";
            string eve = data.D3Evening != null ? string.Join("-", data.D3Evening) : "-";
            d3Win = $"Midday: {mid}      Evening: {eve}";
        }
        else
        {
            d3Win = "No draw found for this date";
        }

        BuildSection("DAILY 3", "#1565C0", d3Win,
            data.Winners.Where(w => w.Game == "D3").ToList());

        // ── D4 section ───────────────────────────────────────────────────────
        string d4Win = data.D4Numbers != null
            ? "Draw: " + string.Join("-", data.D4Numbers)
            : "No draw found for this date";

        BuildSection("DAILY 4", "#00695C", d4Win,
            data.Winners.Where(w => w.Game == "D4").ToList());

        // ── DD section ───────────────────────────────────────────────────────
        string ddWin;
        if (data.DDHorses != null && data.DDHorses.Length == 3)
        {
            ddWin = $"1st:{data.DDHorses[0]}  2nd:{data.DDHorses[1]}  3rd:{data.DDHorses[2]}";
            if (!string.IsNullOrEmpty(data.DDRaceTime))
            {
                string norm = new string(data.DDRaceTime.Where(char.IsDigit).ToArray());
                string last3 = norm.Length >= 3 ? norm[^3..] : norm;
                ddWin += $"   ⏱{data.DDRaceTime}  [{last3}]";
            }
        }
        else
        {
            ddWin = "No draw found for this date";
        }

        BuildSection("DAILY DERBY", "#5D4037", ddWin,
            data.Winners.Where(w => w.Game == "DD").ToList());

        // ── Summary ──────────────────────────────────────────────────────────
        int total = data.Winners.Count;
        string summary = total > 0
            ? $"{total} winner{(total == 1 ? "" : "s")} found for {data.DateLabel}"
            : $"No winners found for {data.DateLabel}";
        lblBottom.Text = summary;

        if (total == 0)
        {
            resultsContainer.Children.Add(new Label
            {
                Text = "No winning tickets found for this date.",
                TextColor = Color.FromArgb("#888"),
                FontSize = 14,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 28)
            });
        }

        // Footnote if any jackpot result couldn't be confirmed from API
        bool hasJackpotPending = data.Winners.Any(w => w.Prize.Contains("JACKPOT*"));
        if (hasJackpotPending)
        {
            resultsContainer.Children.Add(new Label
            {
                Text = "* Jackpot amount not yet in API — tap Status bar to copy log, then REFRESH to retry. Verify exact amount at calottery.com",
                TextColor = Color.FromArgb("#C62828"),
                FontSize = 11,
                Padding = new Thickness(10, 8),
                BackgroundColor = Color.FromArgb("#FFF3E0"),
                LineBreakMode = LineBreakMode.WordWrap
            });
        }
    }

    private void BuildSection(string title, string colorHex, string winNumbers, List<WinnerEntry> winners)
    {
        var accent = Color.FromArgb(colorHex);

        // Section header bar
        var headerGrid = new Grid
        {
            BackgroundColor = accent,
            Padding = new Thickness(12, 7),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        var titleLbl = new Label
        {
            Text = title,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(titleLbl, 0);
        headerGrid.Children.Add(titleLbl);

        var countLbl = new Label
        {
            Text = winners.Count > 0
                ? $"{winners.Count} WIN{(winners.Count == 1 ? "" : "S")}"
                : "No wins",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = winners.Count > 0
                ? Color.FromArgb("#FFFF66")
                : Color.FromArgb("#BBBBBB"),
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(countLbl, 1);
        headerGrid.Children.Add(countLbl);

        resultsContainer.Children.Add(headerGrid);

        // Winning numbers sub-row
        resultsContainer.Children.Add(new Label
        {
            Text = winNumbers,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#222"),
            BackgroundColor = Color.FromArgb("#EEF2FF"),
            Padding = new Thickness(12, 5)
        });

        if (winners.Count == 0)
        {
            resultsContainer.Children.Add(new Label
            {
                Text = "  No matching sets",
                FontSize = 12,
                TextColor = Color.FromArgb("#999"),
                BackgroundColor = Colors.White,
                Padding = new Thickness(12, 7)
            });
        }
        else
        {
            foreach (var w in winners)
            {
                var row = new Grid
                {
                    BackgroundColor = Colors.White,
                    Padding = new Thickness(10, 5),
                    Margin = new Thickness(0, 1),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(new GridLength(55)),   // S# R#
                        new ColumnDefinition(GridLength.Star),      // numbers
                        new ColumnDefinition(new GridLength(52)),   // match
                        new ColumnDefinition(new GridLength(86)),   // prize
                    }
                };

                row.Children.Add(MakeLabel($"S{w.SetNumber} R{w.RowNumber}",
                    12, "#555", 0, FontAttributes.Bold));

                var numsLbl = MakeLabel(w.Numbers, 12, "#111", 1, FontAttributes.Bold);
                numsLbl.LineBreakMode = LineBreakMode.NoWrap;
                row.Children.Add(numsLbl);

                row.Children.Add(MakeLabel(w.MatchLabel, 12, "#E65100", 2,
                    FontAttributes.Bold, TextAlignment.Center));

                bool isJackpot = w.Prize.Contains("JACKPOT");
                var prizeLbl = MakeLabel(w.Prize, isJackpot ? 12 : 13,
                    isJackpot ? "#B71C1C" : "#1B5E20", 3,
                    FontAttributes.Bold, TextAlignment.End);
                row.Children.Add(prizeLbl);

                resultsContainer.Children.Add(row);
            }
        }

        // Spacer between sections
        resultsContainer.Children.Add(new BoxView
        {
            HeightRequest = 8,
            BackgroundColor = Color.FromArgb("#E0E5EA")
        });
    }

    static Label MakeLabel(string text, double fontSize, string colorHex, int col,
        FontAttributes attrs = FontAttributes.None,
        TextAlignment hAlign = TextAlignment.Start)
    {
        var lbl = new Label
        {
            Text = text,
            FontSize = fontSize,
            FontAttributes = attrs,
            TextColor = Color.FromArgb(colorHex),
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = hAlign
        };
        Grid.SetColumn(lbl, col);
        return lbl;
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    // Tap bottom bar to copy log (helps debug prize data issues)
    private async void BottomBar_Tapped(object sender, TappedEventArgs e)
    {
        string log = await Services.Logger.ReadLogAsync();
        await Clipboard.Default.SetTextAsync(log);
        var orig = lblBottom.Text;
        lblBottom.Text = "Log copied — paste into notes to inspect prize JSON";
        await Task.Delay(2000);
        lblBottom.Text = orig;
    }

    private async void BtnBack_Clicked(object sender, EventArgs e)
    {
        await GoHomeAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        _ = GoHomeAsync();
        return true;
    }

    private async Task GoHomeAsync()
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        Shell.Current.CurrentPage.TranslationX = -w;
        await Shell.Current.GoToAsync("..", false);
    }

    private async void BtnRefresh_Clicked(object sender, EventArgs e)
    {
        ResultsPageCls.ClearCache();
        await RunCheck(resultDatePicker?.Date ?? DateTime.Today);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void SetBusy(bool busy, string message)
    {
        spinner.IsVisible = busy;
        spinner.IsRunning = busy;
        lblStatus.Text = message;
    }
}
