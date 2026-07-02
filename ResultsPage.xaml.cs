namespace DailyFantasyMAUI;

public partial class ResultsPage : ContentPage
{
    private DateResultData? _lastData;
    private DateTime? _lastRunDate;
    internal static bool SkipNextRefresh;
    internal static bool NeedsRefresh;
    private Grid? _highlightedRow;
    // key = (Game, SetNumber, RowNumber) → Preferences key for "collected" state
    private readonly Dictionary<(string Game, int Set, int Row), string> _collKeyMap = new();
    private readonly Dictionary<string, bool> _sectionCollapsed = new();

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
        AppShell.WinnerPageInstance.ClearHighlight();
        AppShell.SuperLottoPageInstance.ClearHighlight();
        AppShell.PowerballPageInstance.ClearHighlight();
        AppShell.MegaMillionsPageInstance.ClearHighlight();
        AppShell.Daily3PageInstance.ClearHighlight();
        AppShell.Daily4PageInstance.ClearHighlight();
        AppShell.DailyDerbyPageInstance.ClearHighlight();
        if (_highlightedRow != null)
        {
            _highlightedRow.BackgroundColor = Colors.White;
            _highlightedRow = null;
        }
        if (SkipNextRefresh) { SkipNextRefresh = false; return; }
        if (_lastData != null && _lastRunDate == DateTime.Today && !NeedsRefresh) return;
        NeedsRefresh = false;
        resultDatePicker.Date = DateTime.Today;
        _ = RunCheck(DateTime.Today);
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
        _lastData = null;
        _lastRunDate = null;
        ResultsPageCls.ClearCache();   // always fetch fresh draw data
        SetBusy(true, $"Checking {date:ddd, MMM d, yyyy}...");
        resultsContainer.Children.Clear();

        var data = await ResultsPageCls.ProcessDateAsync(date);
        _lastData = data;
        _lastRunDate = date;

        SetBusy(false, "");
        BuildResultsUI(data);
    }

    // ── Build results UI ─────────────────────────────────────────────────────

    private void BuildResultsUI(DateResultData data)
    {
        resultsContainer.Children.Clear();
        totalWinBanner.IsVisible = false;
        _collKeyMap.Clear();

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
        if (ResultsPageCls.HasSets("f5"))
        {
            string f5DrawLabel = data.F5DrawNumber > 0 ? $"Draw #{data.F5DrawNumber}  " : "";
            string f5Win = data.F5Numbers.Length > 0
                ? f5DrawLabel + "Winning: " + string.Join("  ", data.F5Numbers.Select(n => n.ToString("D2")))
                : "No draw found for this date";
            var f5Winners = data.Winners.Where(w => w.Game == "F5").ToList();
            BuildSection("FANTASY 5", "#FF8F00", f5Win, f5Winners, "F5",
                f5Winners.Sum(w => ParsePrize(w.Prize).amount), data.F5DrawDate);
        }

        // ── SL section ───────────────────────────────────────────────────────
        if (ResultsPageCls.HasSets("sl"))
        {
            string slDrawLabel = data.SLDrawNumber > 0 ? $"Draw #{data.SLDrawNumber}  " : "";
            string slWin = data.SLMain.Length > 0
                ? slDrawLabel + "Winning: " + string.Join("  ", data.SLMain.Select(n => n.ToString("D2")))
                  + "   Mega: " + data.SLMega.ToString("D2")
                : "No draw found for this date";
            var slWinners = data.Winners.Where(w => w.Game == "SL").ToList();
            BuildSection("SUPER LOTTO PLUS", "#7B1FA2", slWin, slWinners, "SL",
                slWinners.Sum(w => ParsePrize(w.Prize).amount), data.SLDrawDate);
        }

        // ── PB section ───────────────────────────────────────────────────────
        if (ResultsPageCls.HasSets("pb"))
        {
            string pbDrawLabel = data.PBDrawNumber > 0 ? $"Draw #{data.PBDrawNumber}  " : "";
            string pbWin = data.PBMain.Length > 0
                ? pbDrawLabel + "Winning: " + string.Join("  ", data.PBMain.Select(n => n.ToString("D2")))
                  + "   PB: " + data.PBBall.ToString("D2")
                : "No draw found for this date";
            var pbWinners = data.Winners.Where(w => w.Game == "PB").ToList();
            BuildSection("POWERBALL", "#C62828", pbWin, pbWinners, "PB",
                pbWinners.Sum(w => ParsePrize(w.Prize).amount), data.PBDrawDate);
        }

        // ── MM section ───────────────────────────────────────────────────────
        if (ResultsPageCls.HasSets("mm"))
        {
            string mmDrawLabel = data.MMDrawNumber > 0 ? $"Draw #{data.MMDrawNumber}  " : "";
            string mmWin = data.MMMain.Length > 0
                ? mmDrawLabel + "Winning: " + string.Join("  ", data.MMMain.Select(n => n.ToString("D2")))
                  + "   MB: " + data.MMBall.ToString("D2")
                : "No draw found for this date";
            var mmWinners = data.Winners.Where(w => w.Game == "MM").ToList();
            BuildSection("MEGA MILLIONS", "#F57F17", mmWin, mmWinners, "MM",
                mmWinners.Sum(w => ParsePrize(w.Prize).amount), data.MMDrawDate);
        }

        // ── D3 section ───────────────────────────────────────────────────────
        if (ResultsPageCls.HasSets("d3"))
        {
            string d3Win;
            if (data.D3Midday != null || data.D3Evening != null)
            {
                string midLabel = data.D3MiddayDrawNum  > 0 ? $"#{data.D3MiddayDrawNum} " : "";
                string eveLabel = data.D3EveningDrawNum > 0 ? $"#{data.D3EveningDrawNum} " : "";
                string mid = data.D3Midday  != null ? midLabel + string.Join("-", data.D3Midday)  : "?";
                string eve;
                if (data.D3Evening != null)
                {
                    eve = eveLabel + string.Join("-", data.D3Evening);
                    // If evening is from a different date (yesterday's fallback), note it
                    if (!string.IsNullOrEmpty(data.D3EveningDateLabel) &&
                        !string.IsNullOrEmpty(data.D3MiddayDateLabel) &&
                        data.D3EveningDateLabel != data.D3MiddayDateLabel)
                        eve += " (prev)";
                }
                else
                {
                    eve = "pending";
                }
                d3Win = $"Midday: {mid}      Evening: {eve}";
            }
            else
            {
                d3Win = "No draw found for this date";
            }
            var d3Winners = data.Winners.Where(w => w.Game == "D3").ToList();
            BuildSection("DAILY 3", "#1565C0", d3Win, d3Winners, "D3",
                d3Winners.Sum(w => ParsePrize(w.Prize).amount), data.D3MiddayDateLabel);
        }

        // ── D4 section ───────────────────────────────────────────────────────
        if (ResultsPageCls.HasSets("d4"))
        {
            string d4DrawLabel = data.D4DrawNumber > 0 ? $"Draw #{data.D4DrawNumber}  " : "";
            string d4Win = data.D4Numbers != null
                ? d4DrawLabel + "Draw: " + string.Join("-", data.D4Numbers)
                : "No draw found for this date";
            var d4Winners = data.Winners.Where(w => w.Game == "D4").ToList();
            BuildSection("DAILY 4", "#00695C", d4Win, d4Winners, "D4",
                d4Winners.Sum(w => ParsePrize(w.Prize).amount), data.D4DrawDate);
        }

        // ── DD section ───────────────────────────────────────────────────────
        if (ResultsPageCls.HasDDSets())
        {
            string ddWin;
            string ddDrawLabel = data.DDDrawNumber > 0 ? $"Draw #{data.DDDrawNumber}  " : "";
            if (data.DDHorses != null && data.DDHorses.Length == 3)
            {
                ddWin = ddDrawLabel + $"1st:{data.DDHorses[0]}  2nd:{data.DDHorses[1]}  3rd:{data.DDHorses[2]}";
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
            var ddWinners = data.Winners.Where(w => w.Game == "DD").ToList();
            BuildSection("DAILY DERBY", "#5D4037", ddWin, ddWinners, "DD",
                ddWinners.Sum(w => ParsePrize(w.Prize).amount), data.DDDrawDate);
        }

        // ── Summary ──────────────────────────────────────────────────────────
        bool anySets = ResultsPageCls.HasSets("f5") || ResultsPageCls.HasSets("sl") ||
                       ResultsPageCls.HasSets("pb") || ResultsPageCls.HasSets("mm") ||
                       ResultsPageCls.HasSets("d3") || ResultsPageCls.HasSets("d4") ||
                       ResultsPageCls.HasDDSets();

        if (!anySets)
        {
            lblBottom.Text = "No sets saved — go add your numbers first";
            resultsContainer.Children.Add(new Label
            {
                Text = "No sets saved. Add your numbers in the game pages first.",
                TextColor = Color.FromArgb("#888"),
                FontSize = 14,
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 28)
            });
            return;
        }

        UpdateSummaryLabel();
        RefreshTotalWinnings();

        if (!data.Winners.Any(w => !w.IsActiveNoWin))
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

    // gameKey = "F5","SL","PB","MM","D3","D4","DD" — matches WinnerEntry.Game
    private void BuildSection(string title, string colorHex, string winNumbers,
        List<WinnerEntry> winners, string gameKey, decimal gameTotal = 0, string drawDateStr = "")
    {
        bool sectionIsPast = DateTime.TryParse(drawDateStr, out var drawDateParsed) && drawDateParsed.Date < DateTime.Today;

        // Separate past/current draw results from "still active, no win" rows
        var actualWinners = winners.Where(w => !w.IsActiveNoWin).ToList();
        var activeNoWin   = winners.Where(w =>  w.IsActiveNoWin).ToList();
        // Only entries with an actual prize count as "wins"
        int winCount = actualWinners.Count(w => !string.IsNullOrEmpty(w.Prize));

        // Auto-add actual wins (with prize) to Summary of Winnings
        foreach (var aw in actualWinners.Where(w => !string.IsNullOrEmpty(w.Prize)))
        {
            string awDateStr = !string.IsNullOrEmpty(aw.DrawDate) ? aw.DrawDate : drawDateStr;
            if (!DateTime.TryParse(awDateStr, out var awDate) || awDate == default) continue;
            string sk = $"auto_{aw.Game}_{aw.SetNumber}_{aw.RowNumber}_{awDate:yyyyMMdd}";
            var (awAmt, _, _, awFree) = ParsePrize(aw.Prize);
            _ = SummaryPage.AddWinAsync(new WinningRecord
            {
                Game         = aw.Game,
                Date         = awDate.ToString("yyyy-MM-dd"),
                Numbers      = aw.Numbers,
                Amount       = awAmt,
                IsFreeTicket = awFree,
                Note         = aw.MatchLabel,
                SourceKey    = sk,
            });
        }

        var accent = Color.FromArgb(colorHex);

        // ── Section header bar ──────────────────────────────────────────────
        bool isCollapsed = _sectionCollapsed.TryGetValue(gameKey, out bool cv) && cv;

        var headerGrid = new Grid
        {
            BackgroundColor = accent,
            Padding = new Thickness(12, 7),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),   // chevron
                new ColumnDefinition(GridLength.Star),   // title
                new ColumnDefinition(GridLength.Auto),   // game total
                new ColumnDefinition(GridLength.Auto),   // win count
            }
        };

        var chevron = new Label
        {
            Text = isCollapsed ? "▶" : "▼",
            FontSize = 13,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(chevron, 0);
        headerGrid.Children.Add(chevron);

        string headerDateStr = "";
        if (!string.IsNullOrEmpty(drawDateStr) &&
            DateTime.TryParse(drawDateStr, out var headerDate) && headerDate != default)
            headerDateStr = "  " + headerDate.ToString("M/d/yy");

        var titleLbl = new Label
        {
            Text = title + headerDateStr,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(titleLbl, 1);
        headerGrid.Children.Add(titleLbl);

        if (gameTotal > 0)
        {
            var totalLbl = new Label
            {
                Text = $"${gameTotal:N0}",
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#FFFF66"),
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(totalLbl, 2);
            headerGrid.Children.Add(totalLbl);
        }

        var countLbl = new Label
        {
            Text = winCount > 0
                ? $"{winCount} WIN{(winCount == 1 ? "" : "S")}"
                : "No wins",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = winCount > 0 ? Color.FromArgb("#A5D6A7") : Color.FromArgb("#BBBBBB"),
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(countLbl, 3);
        headerGrid.Children.Add(countLbl);

        resultsContainer.Children.Add(headerGrid);

        // ── Section body (winning numbers + rows) ──────────────────────────
        var sectionBody = new VerticalStackLayout { IsVisible = !isCollapsed };

        var winLbl = new Label
        {
            Text = winNumbers,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#222"),
            BackgroundColor = Color.FromArgb("#EEF2FF"),
            Padding = new Thickness(12, 5)
        };
        var firstActualWin = actualWinners.FirstOrDefault(w => !string.IsNullOrEmpty(w.Prize));
        if (firstActualWin != null)
        {
            var winTap = new TapGestureRecognizer();
            winTap.Tapped += (_, _) => _ = OnWinnerRowTappedAsync(firstActualWin);
            winLbl.GestureRecognizers.Add(winTap);
        }
        sectionBody.Children.Add(winLbl);

        if (actualWinners.Count == 0 && activeNoWin.Count == 0)
        {
            sectionBody.Children.Add(new Label
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
            // ── Actual winning rows ──────────────────────────────────────────
            foreach (var w in actualWinners)
            {
                // Determine isPast per-row using the entry's own DrawDate
                string rowDateStr = !string.IsNullOrEmpty(w.DrawDate) ? w.DrawDate : drawDateStr;
                bool isRowPast = DateTime.TryParse(rowDateStr, out var rowDate) && rowDate.Date < DateTime.Today;
                string rowDateFormatted = isRowPast ? rowDate.ToString("M/d/yy") : "";

                var row = isRowPast
                    ? new Grid
                    {
                        BackgroundColor = Colors.White,
                        Padding = new Thickness(6, 4),
                        Margin = new Thickness(0, 1),
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(new GridLength(44)),   // S# R#
                            new ColumnDefinition(GridLength.Star),      // numbers
                            new ColumnDefinition(new GridLength(40)),   // match
                            new ColumnDefinition(new GridLength(60)),   // prize
                            new ColumnDefinition(new GridLength(28)),   // checkbox
                            new ColumnDefinition(new GridLength(62)),   // date
                        }
                    }
                    : new Grid
                    {
                        BackgroundColor = Colors.White,
                        Padding = new Thickness(10, 5),
                        Margin = new Thickness(0, 1),
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(new GridLength(55)),   // S# R#
                            new ColumnDefinition(GridLength.Star),      // numbers
                            new ColumnDefinition(GridLength.Auto),      // match
                            new ColumnDefinition(new GridLength(86)),   // prize
                        }
                    };

                row.Children.Add(MakeLabel($"S{w.SetNumber} R{w.RowNumber}",
                    12, "#555", 0, FontAttributes.Bold));

                var numsLbl = MakeLabel(w.Numbers, isRowPast ? 11 : 12, isRowPast ? "#999" : "#111", 1, FontAttributes.Bold);
                numsLbl.LineBreakMode = LineBreakMode.NoWrap;
                if (isRowPast) numsLbl.TextDecorations = TextDecorations.Strikethrough;
                row.Children.Add(numsLbl);

                var matchLbl = MakeLabel(w.MatchLabel, 12, isRowPast ? "#AAA" : "#E65100", 2,
                    FontAttributes.Bold, TextAlignment.Center);
                matchLbl.LineBreakMode = LineBreakMode.NoWrap;
                row.Children.Add(matchLbl);

                string prizeText = !string.IsNullOrEmpty(w.Prize) ? w.Prize : (isRowPast ? "No match" : "");
                bool isJackpot = w.Prize.Contains("JACKPOT");
                string prizeColor = isRowPast ? "#999" : (isJackpot ? "#B71C1C" : "#1B5E20");
                var prizeLbl = MakeLabel(prizeText, isJackpot ? 12 : 13, prizeColor, 3,
                    FontAttributes.Bold, TextAlignment.End);
                if (isRowPast) prizeLbl.TextDecorations = TextDecorations.Strikethrough;
                row.Children.Add(prizeLbl);

                if (isRowPast && !string.IsNullOrEmpty(rowDateFormatted) && !string.IsNullOrEmpty(w.Prize))
                {
                    string collKey = $"coll_{w.Game}_{w.SetNumber}_{w.RowNumber}_{rowDateStr}";
                    _collKeyMap[(w.Game, w.SetNumber, w.RowNumber)] = collKey;

                    var cb = new CheckBox
                    {
                        IsChecked = Preferences.Get(collKey, false),
                        Color = Color.FromArgb("#4CAF50"),
                        VerticalOptions = LayoutOptions.Center,
                        HorizontalOptions = LayoutOptions.Center,
                        WidthRequest = 24,
                        HeightRequest = 24,
                    };
                    Grid.SetColumn(cb, 4);
                    cb.CheckedChanged += (_, e) =>
                    {
                        Preferences.Set(collKey, e.Value);
                        RefreshTotalWinnings();
                    };
                    row.Children.Add(cb);

                    var dateLbl = MakeLabel(rowDateFormatted, 11, "#888", 5,
                        FontAttributes.None, TextAlignment.End);
                    dateLbl.Margin = new Thickness(2, 0, 4, 0);
                    dateLbl.LineBreakMode = LineBreakMode.NoWrap;
                    row.Children.Add(dateLbl);
                }

                // Tap to navigate to that set with the row highlighted
                var tap = new TapGestureRecognizer();
                var capturedW = w;
                var capturedRow = row;
                tap.Tapped += (_, _) => _ = OnWinnerRowTappedAsync(capturedW, capturedRow);
                row.GestureRecognizers.Add(tap);

                sectionBody.Children.Add(row);
            }

            // ── Active advance tickets with no win today ──────────────────────
            if (activeNoWin.Count > 0)
            {
                foreach (var w in activeNoWin)
                {
                    var row = new Grid
                    {
                        BackgroundColor = Color.FromArgb("#E3F2FD"),
                        Padding = new Thickness(10, 4),
                        Margin = new Thickness(0, 1),
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(new GridLength(55)),   // S# R#
                            new ColumnDefinition(GridLength.Star),      // numbers
                            new ColumnDefinition(GridLength.Auto),      // match
                            new ColumnDefinition(new GridLength(86)),   // status
                        }
                    };
                    row.Children.Add(MakeLabel($"S{w.SetNumber} R{w.RowNumber}", 12, "#555", 0, FontAttributes.Bold));
                    row.Children.Add(MakeLabel(w.Numbers, 12, "#888", 1));
                    var activeMatchLbl = MakeLabel(w.MatchLabel, 11, "#999", 2, FontAttributes.None, TextAlignment.Center);
                    activeMatchLbl.LineBreakMode = LineBreakMode.NoWrap;
                    row.Children.Add(activeMatchLbl);
                    string playToLabel = !string.IsNullOrEmpty(w.PlayToDate) ? w.PlayToDate : "In play";
                    row.Children.Add(MakeLabel(playToLabel, 12, "#1565C0", 3, FontAttributes.Bold, TextAlignment.End));
                    sectionBody.Children.Add(row);
                }
            }
        }

        resultsContainer.Children.Add(sectionBody);

        // Tap header to collapse/expand
        var headerTap = new TapGestureRecognizer();
        headerTap.Tapped += (_, _) =>
        {
            sectionBody.IsVisible = !sectionBody.IsVisible;
            chevron.Text = sectionBody.IsVisible ? "▼" : "▶";
            _sectionCollapsed[gameKey] = !sectionBody.IsVisible;
        };
        headerGrid.GestureRecognizers.Add(headerTap);

        // Spacer between sections
        resultsContainer.Children.Add(new BoxView
        {
            HeightRequest = 8,
            BackgroundColor = Color.FromArgb("#E0E5EA")
        });
    }

    // ── Winner row tap → navigate to game page at that slot/row ─────────────

    private async Task OnWinnerRowTappedAsync(WinnerEntry w, Grid? tappedRow = null)
    {
        if (tappedRow != null)
        {
            tappedRow.BackgroundColor = Color.FromArgb("#FFF9C4");
            _highlightedRow = tappedRow;
        }
        PendingHighlight.Game = w.Game;
        PendingHighlight.Slot = w.SetNumber - 1;
        PendingHighlight.Row  = w.RowNumber - 1;
        SkipNextRefresh = true;

        switch (w.Game)
        {
            case "F5":
                WinnerPage.ComingFrom = "results";
                AppShell.WinnerPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(WinnerPage), false);
                break;
            case "SL":
                SuperLottoPage.ComingFrom = "results";
                AppShell.SuperLottoPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(SuperLottoPage), false);
                break;
            case "PB":
                PowerballPage.ComingFrom = "results";
                AppShell.PowerballPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(PowerballPage), false);
                break;
            case "MM":
                MegaMillionsPage.ComingFrom = "results";
                AppShell.MegaMillionsPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(MegaMillionsPage), false);
                break;
            case "D3":
                Daily3Page.ComingFrom = "results";
                AppShell.Daily3PageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(Daily3Page), false);
                break;
            case "D4":
                Daily4Page.ComingFrom = "results";
                AppShell.Daily4PageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(Daily4Page), false);
                break;
            case "DD":
                DailyDerbyPage.ComingFrom = "results";
                AppShell.DailyDerbyPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(DailyDerbyPage), false);
                break;
        }
    }

    private void RefreshTotalWinnings()
    {
        if (_lastData == null) return;
        var active = _lastData.Winners
            .Where(w => !w.IsActiveNoWin)
            .Where(w => !_collKeyMap.TryGetValue((w.Game, w.SetNumber, w.RowNumber), out var key)
                        || Preferences.Get(key, false))
            .ToList();
        UpdateTotalWinnings(active);
    }

    private void UpdateTotalWinnings(List<WinnerEntry> winners)
    {
        if (winners.Count == 0)
        {
            totalWinBanner.IsVisible = false;
            return;
        }

        decimal total = 0;
        bool hasEstimate = false;
        bool hasJackpot  = false;
        int freeTickets  = 0;

        foreach (var w in winners)
        {
            var (amt, est, jackpot, free) = ParsePrize(w.Prize);
            total += amt;
            if (est)     hasEstimate = true;
            if (jackpot) hasJackpot  = true;
            if (free)    freeTickets++;
        }

        string totalStr = total >= 1_000_000
            ? $"${total / 1_000_000:F2}M"
            : $"${total:N0}";
        if (hasEstimate) totalStr = "~" + totalStr;
        if (hasJackpot)  totalStr += " + JACKPOT";

        lblTotalWin.Text = $"TOTAL WINNINGS: {totalStr}";

        int cashPrizes = winners.Count - freeTickets;
        string countStr = $"{winners.Count} prize{(winners.Count == 1 ? "" : "s")}";
        if (freeTickets > 0)
            countStr += $"  ({freeTickets} free ticket{(freeTickets == 1 ? "" : "s")})";
        lblWinCount.Text = countStr;

        totalWinBanner.IsVisible = true;

        if (total >= 100)
            PlayBellSound();
    }

    private static void PlayBellSound()
    {
#if ANDROID
        try
        {
            var uri = Android.Media.RingtoneManager.GetDefaultUri(Android.Media.RingtoneType.Notification);
            var ringtone = Android.Media.RingtoneManager.GetRingtone(
                Android.App.Application.Context, uri);
            ringtone?.Play();
        }
        catch { }
#endif
    }

    internal static (decimal amount, bool isEstimate, bool isJackpot, bool isFreeTicket) ParsePrize(string prize)
    {
        if (string.IsNullOrWhiteSpace(prize))
            return (0, false, false, false);

        if (prize.Contains("JACKPOT", StringComparison.OrdinalIgnoreCase))
            return (0, false, true, false);

        if (prize.Contains("Free Ticket", StringComparison.OrdinalIgnoreCase))
            return (0, false, false, true);

        bool isEstimate = prize.Contains('~') || prize.Contains("est.", StringComparison.OrdinalIgnoreCase);

        // Handle combined prizes like "$428 + $68"
        decimal total = 0;
        foreach (var part in prize.Split('+'))
        {
            var clean = part.Trim()
                            .TrimStart('~')
                            .Replace("$", "")
                            .Replace(",", "")
                            .Replace("est.", "", StringComparison.OrdinalIgnoreCase)
                            .Trim();

            if (clean.EndsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                if (decimal.TryParse(clean[..^1], out var m))
                    total += m * 1_000_000;
            }
            else if (decimal.TryParse(clean, out var v))
            {
                total += v;
            }
        }

        return (total, isEstimate, false, false);
    }

    private void UpdateSummaryLabel()
    {
        if (_lastData == null) return;
        int count = _lastData.Winners.Count(w => !w.IsActiveNoWin);
        lblBottom.Text = count > 0
            ? $"{count} winner{(count == 1 ? "" : "s")} found for {_lastData.DateLabel}"
            : $"No winners found for {_lastData.DateLabel}";
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

    private async void BtnGameMenu_Clicked(object sender, EventArgs e)
    {
        string? choice = await DisplayActionSheet("Go to Game", "Cancel", null,
            "Fantasy 5", "Super Lotto", "Powerball", "Mega Millions", "Daily 3", "Daily 4", "Daily Derby", "Notifications", "Summary of Winnings", "Check Wins for Draw#");
        if (choice == null || choice == "Cancel") return;
        if (choice == "Summary of Winnings") { await Shell.Current.GoToAsync(nameof(SummaryPage), false); return; }
        if (choice == "Check Wins for Draw#") { DrawSearchPage.PresetGame = "Fantasy 5"; await Shell.Current.GoToAsync(nameof(DrawSearchPage), false); return; }
        await GoHomeAsync();
        switch (choice)
        {
            case "Fantasy 5":    WinnerPage.ComingFrom    = "main"; await Shell.Current.GoToAsync(nameof(WinnerPage),      false); break;
            case "Super Lotto":  SuperLottoPage.ComingFrom = "main"; await Shell.Current.GoToAsync(nameof(SuperLottoPage),  false); break;
            case "Powerball":    PowerballPage.ComingFrom  = "main"; await Shell.Current.GoToAsync(nameof(PowerballPage),   false); break;
            case "Mega Millions":MegaMillionsPage.ComingFrom="main"; await Shell.Current.GoToAsync(nameof(MegaMillionsPage),false); break;
            case "Daily 3":      Daily3Page.ComingFrom     = "main"; await Shell.Current.GoToAsync(nameof(Daily3Page),      false); break;
            case "Daily 4":      Daily4Page.ComingFrom     = "main"; await Shell.Current.GoToAsync(nameof(Daily4Page),      false); break;
            case "Daily Derby":  DailyDerbyPage.ComingFrom = "main"; await Shell.Current.GoToAsync(nameof(DailyDerbyPage),  false); break;
        }
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
