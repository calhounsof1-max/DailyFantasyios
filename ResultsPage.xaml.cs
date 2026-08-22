using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class ResultsPage : ContentPage
{
    private DateResultData? _lastData;
    private DateTime? _lastRunDate;
    private DateTime _lastFetchTime = DateTime.MinValue;
    private CancellationTokenSource? _autoRefreshCts;
    private const int AutoRefreshIntervalSeconds = 60;
    internal static bool SkipNextRefresh;
    internal static bool NeedsRefresh;
    private Grid? _highlightedRow;
    // key = the specific WinnerEntry row instance → Preferences key for "collected" state.
    // Keyed by instance (not Game/Set/Row) because one ticket can have multiple win rows —
    // one per draw date it won on — and each needs its own independent collected state.
    private readonly Dictionary<WinnerEntry, string> _collKeyMap = new();
    private readonly Dictionary<string, bool> _sectionCollapsed = new();
    private readonly Dictionary<string, Label> _gameTotalLabels = new();

    public ResultsPage()
    {
        InitializeComponent();
    }

    internal void PrePosition(bool fromRight)
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        TranslationX = fromRight ? w : -w;
    }

    private void UpdateTicketCount()
    {
        int CountRows(string prefix, int cols)
        {
            int CountFromData(string data) {
                var vals = data.Split('|');
                return Enumerable.Range(0, 10).Count(r =>
                    Enumerable.Range(0, cols).Any(c => {
                        int idx = r * cols + c;
                        return idx < vals.Length && !string.IsNullOrWhiteSpace(vals[idx]);
                    }));
            }
            int total = Enumerable.Range(0, 10)
                .Where(s => !string.IsNullOrEmpty(Preferences.Get($"{prefix}_set_{s}", "")))
                .Sum(s => CountFromData(Preferences.Get($"{prefix}_set_{s}", "")));
            if (total == 0) {
                var live = Preferences.Get($"{prefix}_entries", "");
                if (!string.IsNullOrEmpty(live)) total = CountFromData(live);
            }
            return total;
        }

        int f5 = CountRows("f5", 5), sl = CountRows("sl", 6), pb = CountRows("pb", 6);
        int mm = CountRows("mm", 6), d3 = CountRows("d3", 3), d4 = CountRows("d4", 4), dd = CountRows("dd", 4);
        int total = f5 + sl + pb + mm + d3 + d4 + dd;

        ticketBadgeRow.Children.Clear();

        if (total == 0)
        {
            ticketBadgeRow.Children.Add(new Label
            {
                Text = "No tickets saved — add numbers in each game page",
                FontSize = 11, TextColor = Color.FromArgb("#888"),
                VerticalOptions = LayoutOptions.Center
            });
            return;
        }

        var pills = new (string Label, int Count, string Color)[]
        {
            ("F5", f5, "#FF8F00"),
            ("SL", sl, "#7B1FA2"),
            ("PB", pb, "#C62828"),
            ("MM", mm, "#F57F17"),
            ("D3", d3, "#1565C0"),
            ("D4", d4, "#00695C"),
            ("DD", dd, "#5D4037"),
        };

        foreach (var (label, count, hex) in pills)
        {
            if (count == 0) continue;
            var pill = new Border
            {
                BackgroundColor = Color.FromArgb(hex),
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 11 },
                Padding = new Thickness(9, 3),
                Margin = new Thickness(0, 0, 4, 2),
                Content = new Label
                {
                    Text = $"{label} ×{count}",
                    FontSize = 11, FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White
                }
            };
            ticketBadgeRow.Children.Add(pill);
        }

        ticketBadgeRow.Children.Add(new Label
        {
            Text = $"= {total} Total",
            FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#333"),
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(2, 0, 0, 0)
        });
    }

    protected override void OnAppearing()
    {
        this.TranslateTo(0, 0, 220, Easing.CubicOut);
        base.OnAppearing();
        UpdateTicketCount();
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
        if (SkipNextRefresh) { SkipNextRefresh = false; SetBusy(false, ""); StartAutoRefresh(); return; }

        bool wasNeedsRefresh = NeedsRefresh;

        // Already have fresh data for today — nothing to do (auto-refresh timer handles updates)
        if (_lastData != null && _lastRunDate == DateTime.Today && !wasNeedsRefresh)
        {
            SetBusy(false, "");
            StartAutoRefresh();
            return;
        }

        NeedsRefresh = false;
        // AutoClearExpiredOldSets() removed — was silently deleting non-active slots without confirmation.
        // Expired play cleanup is handled by CheckAutoPurgeOnStartupAsync (startup dialog with "No, Keep All").
        resultDatePicker.Date = DateTime.Today;

        if (_lastData != null && !wasNeedsRefresh)
        {
            // Have stale/yesterday data — show it instantly, refresh quietly in background
            SetBusy(false, "");
            BuildResultsUI(_lastData);
            _ = Task.Run(() => BackgroundRefreshAsync(DateTime.Today));
            StartAutoRefresh();
        }
        else
        {
            // No UI data yet, OR tickets changed (wasNeedsRefresh) — do a fresh load.
            // Do NOT clear the draw cache — reuse whatever LoadAllDrawsAsync already fetched (fast).
            // This re-reads all ticket prefs so newly entered tickets show up immediately.
            if (btnCheckTickets != null) btnCheckTickets.IsEnabled = false;
            if (btnRefresh != null) btnRefresh.IsEnabled = false;
            _ = Task.Delay(80).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(async () =>
            {
                SetBusy(true, $"Checking {DateTime.Today:ddd, MMM d, yyyy}...");
                var data = await ResultsPageCls.ProcessDateAsync(DateTime.Today);
                _lastData = data;
                _lastRunDate = DateTime.Today;
                _lastFetchTime = DateTime.Now;
                SetBusy(false, "");
                BuildResultsUI(data);
                if (btnCheckTickets != null) btnCheckTickets.IsEnabled = true;
                if (btnRefresh != null) btnRefresh.IsEnabled = true;
                StartAutoRefresh();
            }));
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _autoRefreshCts?.Cancel();
        _autoRefreshCts = null;
        lblCountdown.Text = "";
    }

    private void StartAutoRefresh()
    {
        _autoRefreshCts?.Cancel();
        _autoRefreshCts = new CancellationTokenSource();
        var token = _autoRefreshCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    for (int secs = AutoRefreshIntervalSeconds; secs > 0; secs--)
                    {
                        if (token.IsCancellationRequested) return;
                        int display = secs;
                        await MainThread.InvokeOnMainThreadAsync(() =>
                            lblCountdown.Text = $"Next refresh in {display}s");
                        await Task.Delay(1000, token);
                    }
                    if (token.IsCancellationRequested) break;

                    await MainThread.InvokeOnMainThreadAsync(() => lblCountdown.Text = "Refreshing...");

                    var selectedDate = await MainThread.InvokeOnMainThreadAsync(
                        () => resultDatePicker?.Date ?? DateTime.Today);
                    if (selectedDate.Date == DateTime.Today)
                        await BackgroundRefreshAsync(DateTime.Today);
                }
            }
            catch (TaskCanceledException) { }
        }, token);
    }

    // ── Silent background refresh ────────────────────────────────────────────

    private async Task BackgroundRefreshAsync(DateTime date)
    {
        try
        {
            ResultsPageCls.ClearCache();
            var data = await ResultsPageCls.ProcessDateAsync(date);

            // Only rebuild the UI if the winning numbers actually changed
            // (draws are posted once per day — most background refreshes return identical data)
            bool changed = _lastData == null
                || !data.F5Numbers.SequenceEqual(_lastData.F5Numbers)
                || !data.SLMain.SequenceEqual(_lastData.SLMain)
                || !data.PBMain.SequenceEqual(_lastData.PBMain)
                || !data.MMMain.SequenceEqual(_lastData.MMMain)
                || data.D3MiddayDrawNum  != _lastData.D3MiddayDrawNum
                || data.D3EveningDrawNum != _lastData.D3EveningDrawNum
                || data.D4DrawNumber     != _lastData.D4DrawNumber
                || data.DDDrawNumber     != _lastData.DDDrawNumber;

            _lastData    = data;
            _lastRunDate = date;
            _lastFetchTime = DateTime.Now;

            if (changed)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    SetBusy(false, "");
                    BuildResultsUI(data);
                });
            }
            else
            {
                await MainThread.InvokeOnMainThreadAsync(() => SetBusy(false, ""));
            }
        }
        catch
        {
            await MainThread.InvokeOnMainThreadAsync(() => SetBusy(false, ""));
        }
    }

    // ── Date picker — auto-run check when date changes ───────────────────────

    private void DatePicker_DateSelected(object sender, DateChangedEventArgs e)
    {
        _ = RunCheck(e.NewDate ?? DateTime.Today);
    }

    // ── CHECK TICKETS button ─────────────────────────────────────────────────

    private async void BtnCheckTickets_Clicked(object sender, EventArgs e)
    {
        if (btnCheckTickets != null) btnCheckTickets.IsEnabled = false;
        if (btnRefresh != null) btnRefresh.IsEnabled = false;
        SetBusy(true, "Checking tickets...");
        try { await RunCheck(resultDatePicker?.Date ?? DateTime.Today); }
        finally
        {
            if (btnCheckTickets != null) btnCheckTickets.IsEnabled = true;
            if (btnRefresh != null) btnRefresh.IsEnabled = true;
        }
    }

    /// <summary>Clears the in-memory cache so the next visit re-reads current prefs.</summary>
    internal void InvalidateCache()
    {
        _lastData    = null;
        _lastRunDate = null;
        NeedsRefresh = true;
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
        _lastFetchTime = DateTime.Now;

        SetBusy(false, "");
        BuildResultsUI(data);
    }

    // ── Build results UI ─────────────────────────────────────────────────────

    private void BuildResultsUI(DateResultData data)
    {
        resultsContainer.Children.Clear();
        totalWinBanner.IsVisible = false;
        _collKeyMap.Clear();
        _gameTotalLabels.Clear();

        // Read the current enhance-mode preference (false = classic plain text by default)
        bool enhanced = EnhanceModeService.IsEnhanced(EnhanceModeService.ResultsPageKey);

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
        if (!ResultsPageCls.IsGameExcluded("f5"))
        {
            string f5DrawLabel = data.F5DrawNumber > 0 ? $"Draw #{data.F5DrawNumber}  " : "";
            string f5Win = data.F5Numbers.Length > 0
                ? f5DrawLabel + "Winning: " + string.Join("  ", data.F5Numbers.Select(n => n.ToString("D2")))
                : "No draw found for this date";
            var f5Winners = data.Winners.Where(w => w.Game == "F5").ToList();
            BuildSection("FANTASY 5", "#FF8F00", f5Win, f5Winners, "F5",
                CheckedGameTotal(f5Winners), data.F5DrawDate, data.F5DrawNumber, 0,
                data.F5Numbers.Length > 0 ? data.F5Numbers : null,
                enhanced: enhanced);
        }

        // ── SL section ───────────────────────────────────────────────────────
        if (!ResultsPageCls.IsGameExcluded("sl"))
        {
            string slDrawLabel = data.SLDrawNumber > 0 ? $"Draw #{data.SLDrawNumber}  " : "";
            string slWin = data.SLMain.Length > 0
                ? slDrawLabel + "Winning: " + string.Join("  ", data.SLMain.Select(n => n.ToString("D2")))
                  + "   Mega: " + data.SLMega.ToString("D2")
                : "No draw found for this date";
            var slWinners = data.Winners.Where(w => w.Game == "SL").ToList();
            BuildSection("SUPER LOTTO PLUS", "#7B1FA2", slWin, slWinners, "SL",
                CheckedGameTotal(slWinners), data.SLDrawDate, data.SLDrawNumber, 0,
                data.SLMain.Length > 0 ? [..data.SLMain, data.SLMega] : null,
                enhanced: enhanced);
        }

        // ── PB section ───────────────────────────────────────────────────────
        if (!ResultsPageCls.IsGameExcluded("pb"))
        {
            string pbDrawLabel = data.PBDrawNumber > 0 ? $"Draw #{data.PBDrawNumber}  " : "";
            string pbWin = data.PBMain.Length > 0
                ? pbDrawLabel + "Winning: " + string.Join("  ", data.PBMain.Select(n => n.ToString("D2")))
                  + "   PB: " + data.PBBall.ToString("D2")
                : "No draw found for this date";
            var pbWinners = data.Winners.Where(w => w.Game == "PB").ToList();
            BuildSection("POWERBALL", "#C62828", pbWin, pbWinners, "PB",
                CheckedGameTotal(pbWinners), data.PBDrawDate, data.PBDrawNumber, 0,
                data.PBMain.Length > 0 ? [..data.PBMain, data.PBBall] : null,
                enhanced: enhanced);
        }

        // ── MM section ───────────────────────────────────────────────────────
        if (!ResultsPageCls.IsGameExcluded("mm"))
        {
            string mmDrawLabel = data.MMDrawNumber > 0 ? $"Draw #{data.MMDrawNumber}  " : "";
            string mmWin = data.MMMain.Length > 0
                ? mmDrawLabel + "Winning: " + string.Join("  ", data.MMMain.Select(n => n.ToString("D2")))
                  + "   MB: " + data.MMBall.ToString("D2")
                : "No draw found for this date";
            var mmWinners = data.Winners.Where(w => w.Game == "MM").ToList();
            BuildSection("MEGA MILLIONS", "#F57F17", mmWin, mmWinners, "MM",
                CheckedGameTotal(mmWinners), data.MMDrawDate, data.MMDrawNumber, 0,
                data.MMMain.Length > 0 ? [..data.MMMain, data.MMBall] : null,
                enhanced: enhanced);
        }

        // ── D3 section ───────────────────────────────────────────────────────
        if (!ResultsPageCls.IsGameExcluded("d3"))
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
            bool d3EveFromPrev = !string.IsNullOrEmpty(data.D3EveningDateLabel) &&
                                 !string.IsNullOrEmpty(data.D3MiddayDateLabel) &&
                                 data.D3EveningDateLabel != data.D3MiddayDateLabel;
            BuildSection("DAILY 3", "#1565C0", d3Win, d3Winners, "D3",
                CheckedGameTotal(d3Winners), data.D3MiddayDateLabel,
                data.D3MiddayDrawNum, data.D3EveningDrawNum,
                winningNums:  data.D3Midday,
                winningNums2: data.D3Evening ?? Array.Empty<int>(),
                winNums2Suffix: d3EveFromPrev ? "(prev)" : "",
                enhanced: enhanced);
        }

        // ── D4 section ───────────────────────────────────────────────────────
        if (!ResultsPageCls.IsGameExcluded("d4"))
        {
            string d4DrawLabel = data.D4DrawNumber > 0 ? $"Draw #{data.D4DrawNumber}  " : "";
            string d4Win = data.D4Numbers != null
                ? d4DrawLabel + "Draw: " + string.Join("-", data.D4Numbers)
                : "No draw found for this date";
            var d4Winners = data.Winners.Where(w => w.Game == "D4").ToList();
            BuildSection("DAILY 4", "#00695C", d4Win, d4Winners, "D4",
                CheckedGameTotal(d4Winners), data.D4DrawDate, data.D4DrawNumber,
                enhanced: enhanced);
        }

        // ── DD section ───────────────────────────────────────────────────────
        if (!ResultsPageCls.IsGameExcluded("dd"))
        {
            string ddWin;
            int[]? ddWinNums = null;
            string ddDrawLabel = data.DDDrawNumber > 0 ? $"Draw #{data.DDDrawNumber}  " : "";
            if (data.DDHorses != null && data.DDHorses.Length == 3)
            {
                ddWinNums = data.DDHorses;
                string raceInfo = "";
                if (!string.IsNullOrEmpty(data.DDRaceTime))
                {
                    string norm = new string(data.DDRaceTime.Where(char.IsDigit).ToArray());
                    string last3 = norm.Length >= 3 ? norm[^3..] : norm;
                    raceInfo = $"  ⏱{data.DDRaceTime}  [{last3}]";
                }
                // "Winning:" marker lets BuildSection extract the draw info label from the rest
                ddWin = ddDrawLabel + $"1st:{data.DDHorses[0]}  2nd:{data.DDHorses[1]}  3rd:{data.DDHorses[2]}{raceInfo}  Winning: {data.DDHorses[0]}  {data.DDHorses[1]}  {data.DDHorses[2]}";
            }
            else
            {
                ddWin = "No draw found for this date";
            }
            var ddWinners = data.Winners.Where(w => w.Game == "DD").ToList();
            BuildSection("DAILY DERBY", "#5D4037", ddWin, ddWinners, "DD",
                CheckedGameTotal(ddWinners), data.DDDrawDate, data.DDDrawNumber,
                winningNums: ddWinNums, enhanced: enhanced);
        }

        // ── Summary ──────────────────────────────────────────────────────────
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
        List<WinnerEntry> winners, string gameKey, decimal gameTotal = 0, string drawDateStr = "",
        int drawNumber = 0, int drawNumber2 = 0, int[]? winningNums = null,
        int[]? winningNums2 = null, string winNums2Suffix = "", bool enhanced = false)
    {
        bool sectionIsPast = DateTime.TryParse(drawDateStr, out var drawDateParsed) && drawDateParsed.Date < DateTime.Today;

        // Separate past/current draw results from "still active, no win" rows
        var actualWinners = winners.Where(w => !w.IsActiveNoWin).ToList();
        var activeNoWin   = winners.Where(w =>  w.IsActiveNoWin).ToList();

        // After midnight: hide expired entries from the Results page display (display-only, no data deleted).
        // D3 actual wins: route through IsRowStillVisible so the same 6am cutoff used by TOTAL
        // WINNINGS applies here too (a win's own DrawDate is meaningful — it's the day it won).
        // D3 activeNoWin: DrawDate on these rows is NOT the ticket's date — it's stamped with
        // result.D3MiddayDateLabel (the currently-loaded draw's date, which lags at yesterday
        // until today's draws post), so running it through the date-aware check misread every
        // still-active ticket as "a stale win from yesterday" and hid them all (found 2026-08-02
        // right after install). Keep the original pure draw-number comparison for these instead.
        if (gameKey == "D3")
        {
            actualWinners = actualWinners.Where(IsRowStillVisible).ToList();
            int minD3Draw = (drawNumber > 0 && drawNumber2 > 0)
                ? Math.Min(drawNumber, drawNumber2)
                : Math.Max(drawNumber, drawNumber2);
            if (minD3Draw > 0)
                activeNoWin = activeNoWin.Where(w => w.DrawEnd <= 0 || w.DrawEnd >= minD3Draw).ToList();
        }
        else
        {
            // Non-D3: hide rows whose date has passed, win or not — D3 is the only exception
            // (uses draw# instead, handled above). "Passed" means different things depending
            // on the row: a regular single-day ticket goes by its own DrawDate, but an advance
            // (multi-draw) ticket that won on an early draw is still CURRENT until its play
            // range's own end date — the win's DrawDate can be "past" while the ticket itself
            // isn't (e.g. a 7/25-9/16 SL ticket that won $1 on its first draw is still active).
            actualWinners = actualWinners.Where(IsRowStillVisible).ToList();
            // activeNoWin: only filter ADVANCE tickets (have PlayFromDate or DrawEnd set).
            // Regular tickets (PlayFromDate="" and DrawEnd=0) always keep — their PlayToDate
            // is stamped at processing time and may be stale from yesterday's cache.
            activeNoWin = activeNoWin.Where(w => {
                bool hasAdvanceDates = !string.IsNullOrEmpty(w.PlayFromDate) || w.DrawEnd > 0;
                if (!hasAdvanceDates) return true;
                if (string.IsNullOrEmpty(w.PlayToDate)) return true;
                if (DateTime.TryParseExact(w.PlayToDate, "MM/dd", null,
                    System.Globalization.DateTimeStyles.None, out var endDate))
                {
                    endDate = new DateTime(DateTime.Today.Year, endDate.Month, endDate.Day);
                    return endDate.Date >= DateTime.Today;
                }
                return true;
            }).ToList();
        }
        // Recompute the header total from the post-filter list so it never shows a $ amount
        // for a row that was just removed from view (was the "$1 — No wins" bug, 2026-07-28).
        gameTotal = CheckedGameTotal(actualWinners);

        // Only entries with an actual prize count as "wins"
        int winCount = actualWinners.Count(w => !string.IsNullOrEmpty(w.Prize));

        // Auto-add actual wins (with a resolved amount) to Summary of Winnings —
        // skip "n/a" so a pending win isn't recorded as a $0 win before the real
        // payout posts on calottery.com.
        foreach (var aw in actualWinners.Where(w => !string.IsNullOrEmpty(w.Prize) && !w.Prize.Equals("n/a", StringComparison.OrdinalIgnoreCase)))
        {
            string awDateStr = !string.IsNullOrEmpty(aw.DrawDate) ? aw.DrawDate : drawDateStr;
            if (!DateTime.TryParse(awDateStr, out var awDate) || awDate == default) continue;
            string sk = $"auto_{aw.Game}_{awDate:yyyyMMdd}_{aw.Numbers.Replace(" ", "")}";
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
            headerDateStr = "  " + headerDate.ToString("MM/dd/yy");

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

        var gameTotalLbl = new Label
        {
            Text = gameTotal > 0 ? $"${gameTotal:N0}" : "",
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#FFFF66"),
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 10, 0),
            IsVisible = gameTotal > 0,
        };
        Grid.SetColumn(gameTotalLbl, 2);
        headerGrid.Children.Add(gameTotalLbl);
        _gameTotalLabels[gameKey] = gameTotalLbl;

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

        // Active (no-win) rows: if ticket has a draw# range, show the End draw# so user knows when it expires.
        // Winning rows: show the draw# that actually produced the win, not the range's end.
        // Otherwise fall back to the section's current draw number.
        int GetRowDrawNum(WinnerEntry entry, bool preferDrawEnd = true)
        {
            if (preferDrawEnd && entry.DrawEnd > 0) return entry.DrawEnd;
            if (!preferDrawEnd && entry.MatchDrawNum > 0) return entry.MatchDrawNum;
            // Regular ticket (no advance draw# set): show the upcoming draw (next after last fetched)
            if (string.IsNullOrEmpty(entry.DrawDate))
                return entry.Game == "D3" ? Math.Max(drawNumber, drawNumber2) + 1 : drawNumber + 1;
            return entry.Game == "D3" ? Math.Max(drawNumber, drawNumber2) : drawNumber;
        }

        // Winning numbers header — balls if we have raw numbers, plain text fallback
        var winHeaderBox = new VerticalStackLayout
        {
            BackgroundColor = enhanced ? Colors.White : Color.FromArgb("#EEF2FF"),
            Padding = new Thickness(12, enhanced ? 10 : 6),
            Spacing = 4
        };

        // In enhanced mode — show a small game logo above the numbers
        if (enhanced)
        {
            string logoSrc = gameKey switch
            {
                "F5" => "logo_fantasy5.png",
                "SL" => "logo_superlotto.png",
                "PB" => "logo_powerball.png",
                "MM" => "logo_megamillions.png",
                "D3" => "logo_daily3.png",
                "D4" => "logo_daily4.png",
                "DD" => "logo_dailyderby.png",
                _    => ""
            };
            if (!string.IsNullOrEmpty(logoSrc))
                winHeaderBox.Children.Add(new Image
                {
                    Source            = logoSrc,
                    HeightRequest     = 100,
                    HorizontalOptions = LayoutOptions.Center,
                    Aspect            = Aspect.AspectFit,
                    Margin            = new Thickness(0, 4, 0, 8)
                });
        }

        // Draw # / label line
        var firstActualWin = actualWinners.FirstOrDefault(w => !string.IsNullOrEmpty(w.Prize));
        if (!string.IsNullOrEmpty(winNumbers))
        {
            // Extract just the draw label part (before "Winning:")
            int winIdx = winNumbers.IndexOf("Winning:", StringComparison.OrdinalIgnoreCase);
            string drawInfoText = winIdx > 0 ? winNumbers[..winIdx].Trim() : "";
            if (!string.IsNullOrEmpty(drawInfoText))
            {
                winHeaderBox.Children.Add(new Label
                {
                    Text = drawInfoText,
                    FontSize = 11,
                    TextColor = Color.FromArgb("#555"),
                });
            }
        }

        // Winning numbers display — delegate to the active renderer
        winHeaderBox.Children.Add(enhanced
            ? ResultsRendererEnhanced.MakeWinningNumbersView(winningNums, winningNums2, winNumbers, accent, drawNumber, drawNumber2, winNums2Suffix)
            : ResultsRendererClassic.MakeWinningNumbersView(winningNums, winningNums2, winNumbers, accent, drawNumber, drawNumber2, winNums2Suffix));

        if (firstActualWin != null)
        {
            var winTap = new TapGestureRecognizer();
            winTap.Tapped += (_, _) => _ = OnWinnerRowTappedAsync(firstActualWin);
            winHeaderBox.GestureRecognizers.Add(winTap);
        }
        sectionBody.Children.Add(winHeaderBox);

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
                // Determine isPast per-row using the entry's own DrawDate only (no section date fallback for regular tickets)
                string rowDateStr = w.DrawDate ?? "";
                DateTime rowDate = default;
                bool isRowPast = !string.IsNullOrEmpty(rowDateStr) && DateTime.TryParse(rowDateStr, out rowDate) && rowDate.Date < DateTime.Today;
                string rowDateFormatted = isRowPast ? rowDate.ToString("MM/dd") : "";

                var row = new Grid
                {
                    BackgroundColor = Colors.White,
                    Padding = new Thickness(10, 7),
                    Margin = new Thickness(0),
                    ColumnSpacing = 4,
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Auto),      // S# R#
                        new ColumnDefinition(GridLength.Star),      // numbers — always gets whatever room is left
                        new ColumnDefinition(GridLength.Auto),      // match
                        new ColumnDefinition(GridLength.Auto),      // draw#
                        new ColumnDefinition(GridLength.Auto),      // prize (tappable — doubles as collected toggle)
                        new ColumnDefinition(GridLength.Auto),      // date
                    }
                };

                var setLbl = MakeLabel($"Set{w.SetNumber}·R{w.RowNumber}", 9, "#555", 0, FontAttributes.Bold);
                setLbl.LineBreakMode = LineBreakMode.NoWrap;
                if (isRowPast) setLbl.TextDecorations = TextDecorations.Strikethrough;
                row.Children.Add(setLbl);

                // Numbers — delegate to active renderer, then append bet type for D3/D4
                {
                    var winSet = winningNums != null ? new HashSet<int>(winningNums) : null;
                    bool bonusMatch = w.MatchLabel.Contains('+');
                    var numsView = enhanced
                        ? ResultsRendererEnhanced.MakePlayerNumbersView(w.Numbers, accent, isRowPast, isRowPast ? null : winSet, gameKey, bonusMatch)
                        : ResultsRendererClassic.MakePlayerNumbersView(w.Numbers, accent, isRowPast, isRowPast ? null : winSet, gameKey, bonusMatch);
                    if (!string.IsNullOrEmpty(w.BetType) && (w.Game == "D3" || w.Game == "D4"))
                    {
                        var numsRow = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
                        numsRow.Children.Add(numsView);
                        numsRow.Children.Add(new Label
                        {
                            Text = w.BetType, FontSize = 9, FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#1565C0"),
                            VerticalOptions = LayoutOptions.Center,
                            LineBreakMode = LineBreakMode.NoWrap
                        });
                        if (w.Game == "D3" && !string.IsNullOrEmpty(w.DrawFilter))
                        {
                            numsRow.Children.Add(new Label
                            {
                                Text = D3DrawFilterLabel(w.DrawFilter), FontSize = 9, FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromArgb("#6A1B9A"),
                                VerticalOptions = LayoutOptions.Center,
                                LineBreakMode = LineBreakMode.NoWrap
                            });
                        }
                        Grid.SetColumn(numsRow, 1);
                        row.Children.Add(numsRow);
                    }
                    else
                    {
                        Grid.SetColumn(numsView, 1);
                        row.Children.Add(numsView);
                    }
                }

                row.Children.Add(MakeMatchBadge(w.MatchLabel, w.Prize, isRowPast, 2));

                int rowDrawNum = GetRowDrawNum(w, preferDrawEnd: false);
                string drawNumText = rowDrawNum > 0 ? $"#{rowDrawNum}" : "";
                var drawNumLbl = MakeLabel(drawNumText, 10, "#777", 3,
                    FontAttributes.None, TextAlignment.Center);
                drawNumLbl.Margin = new Thickness(6, 0, 0, 0);
                drawNumLbl.LineBreakMode = LineBreakMode.NoWrap;
                row.Children.Add(drawNumLbl);

                bool isJackpot    = w.Prize.Contains("JACKPOT");
                bool isFreeTicket = w.Prize.Contains("Free Ticket", StringComparison.OrdinalIgnoreCase);
                string prizeText  = isFreeTicket ? "Free" : (!string.IsNullOrEmpty(w.Prize) ? w.Prize : (isRowPast ? "—" : ""));

                // "n/a" = confirmed win, real payout not posted by calottery.com yet —
                // no collected-toggle until there's an actual amount to collect against.
                bool hasResolvedPrize = !string.IsNullOrEmpty(w.Prize) && !w.Prize.Equals("n/a", StringComparison.OrdinalIgnoreCase);
                // Multi-draw ticket still has draws remaining beyond this win — shown as a second line below the row.
                int winDrawNum = w.Game == "D3" ? Math.Max(drawNumber, drawNumber2) : drawNumber;
                bool stillActiveMultiDraw = w.DrawEnd > 0 && w.DrawEnd > winDrawNum && !string.IsNullOrEmpty(w.PlayToDate);
                // Show the full advance range whenever it's known, not just while draws remain —
                // otherwise a completed advance ticket's win row only shows one date (the day it
                // won), hiding which range it was actually part of.
                bool hasPlayRange = !string.IsNullOrEmpty(w.PlayFromDate) && !string.IsNullOrEmpty(w.PlayToDate);

                if (hasResolvedPrize)
                {
                    string displayDate = isRowPast ? rowDateFormatted : DateTime.Today.ToString("MM/dd");

                    Border prizePill;
                    Label prizeTxt;

                    if (isRowPast)
                    {
                        // A win from an earlier draw within this ticket's still-active window.
                        // Never green — shown as a checked/strikethrough item with its own
                        // date + amount, independently toggleable per draw date.
                        string effectiveDateStr = !string.IsNullOrEmpty(rowDateStr) ? rowDateStr : DateTime.Today.ToString("yyyy-MM-dd");
                        string collKey = $"coll_{w.Game}_{w.SetNumber}_{w.RowNumber}_{effectiveDateStr}";
                        _collKeyMap[w] = collKey;

                        bool cbState = Preferences.Get(collKey, true);
                        string PillText(bool @checked) => (@checked ? "✓ " : "") + $"{rowDateFormatted}  {prizeText}";

                        prizeTxt = new Label
                        {
                            Text = PillText(cbState),
                            FontSize = 8,
                            FontAttributes = FontAttributes.Bold,
                            TextDecorations = cbState ? TextDecorations.Strikethrough : TextDecorations.None,
                            TextColor = cbState ? Color.FromArgb("#78909C") : (isJackpot ? Color.FromArgb("#B71C1C") : Color.FromArgb("#1B5E20")),
                            HorizontalOptions = LayoutOptions.Center,
                            VerticalOptions = LayoutOptions.Center,
                            LineBreakMode = LineBreakMode.NoWrap,
                        };
                        prizePill = new Border
                        {
                            StrokeThickness = 1.5,
                            Stroke = new SolidColorBrush(Color.FromArgb("#B0BEC5")),
                            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                            BackgroundColor = cbState ? Color.FromArgb("#ECEFF1") : Colors.Transparent,
                            Padding = new Thickness(4, 1),
                            HorizontalOptions = LayoutOptions.End,
                            VerticalOptions = LayoutOptions.Center,
                            Content = prizeTxt,
                        };
                        prizePill.GestureRecognizers.Add(new TapGestureRecognizer
                        {
                            Command = new Command(() =>
                            {
                                cbState = !cbState;
                                Preferences.Set(collKey, cbState);
                                prizeTxt.Text = PillText(cbState);
                                prizeTxt.TextDecorations = cbState ? TextDecorations.Strikethrough : TextDecorations.None;
                                prizeTxt.TextColor = cbState ? Color.FromArgb("#78909C") : (isJackpot ? Color.FromArgb("#B71C1C") : Color.FromArgb("#1B5E20"));
                                prizePill.BackgroundColor = cbState ? Color.FromArgb("#ECEFF1") : Colors.Transparent;
                                RefreshTotalWinnings();
                                RefreshGameHeader(gameKey);
                            })
                        });
                    }
                    else
                    {
                        // Today's win — the "current" result. Always green, always counted;
                        // no collected-toggle (that's only meaningful for past wins).
                        prizeTxt = new Label
                        {
                            Text = prizeText,
                            FontSize = isJackpot ? 11 : 12,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Colors.White,
                            HorizontalOptions = LayoutOptions.Center,
                            VerticalOptions = LayoutOptions.Center,
                            LineBreakMode = LineBreakMode.NoWrap,
                        };
                        prizePill = new Border
                        {
                            StrokeThickness = 1.5,
                            Stroke = new SolidColorBrush(Color.FromArgb("#4CAF50")),
                            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
                            BackgroundColor = Color.FromArgb("#4CAF50"),
                            Padding = new Thickness(6, 2),
                            HorizontalOptions = LayoutOptions.End,
                            VerticalOptions = LayoutOptions.Center,
                            Content = prizeTxt,
                        };
                    }
                    Grid.SetColumn(prizePill, 4);
                    row.Children.Add(prizePill);

                    string dateText = hasPlayRange ? $"{w.PlayFromDate} - {w.PlayToDate}" : displayDate;
                    var dateLbl = MakeLabel(dateText, 9,
                        stillActiveMultiDraw ? "#1565C0" : "#888", 5,
                        stillActiveMultiDraw ? FontAttributes.Bold : FontAttributes.None, TextAlignment.End);
                    dateLbl.Margin = new Thickness(2, 0, 4, 0);
                    dateLbl.LineBreakMode = LineBreakMode.NoWrap;
                    row.Children.Add(dateLbl);
                }
                else
                {
                    var prizeLbl = MakeLabel(prizeText, isJackpot ? 11 : 12,
                        isRowPast ? "#999" : (isJackpot ? "#B71C1C" : "#1B5E20"), 4,
                        FontAttributes.Bold, TextAlignment.End);
                    if (isRowPast) prizeLbl.TextDecorations = TextDecorations.Strikethrough;
                    row.Children.Add(prizeLbl);
                }

                // Past win (any date before today) — strike a line across the set#/numbers/
                // match/draw# columns so it visually reads as a completed past result. Stops
                // before the prize pill column (4), which already carries its own strikethrough text.
                if (isRowPast)
                {
                    var winStrikeLine = new BoxView
                    {
                        Color = Color.FromArgb("#78909C"),
                        HeightRequest = 2,
                        VerticalOptions = LayoutOptions.Center,
                        HorizontalOptions = LayoutOptions.Fill,
                        InputTransparent = true,
                    };
                    Grid.SetColumn(winStrikeLine, 0);
                    Grid.SetColumnSpan(winStrikeLine, 4);
                    row.Children.Add(winStrikeLine);
                }

                // Tap to navigate to that set with the row highlighted
                var tap = new TapGestureRecognizer();
                var capturedW = w;
                var capturedRow = row;
                tap.Tapped += (_, _) => _ = OnWinnerRowTappedAsync(capturedW, capturedRow);
                row.GestureRecognizers.Add(tap);

                var winCard = new Border
                {
                    Content = row,
                    Stroke = new SolidColorBrush(Color.FromArgb("#E5E7EB")),
                    StrokeThickness = 1,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) },
                    Padding = new Thickness(0),
                    Margin = new Thickness(0, 3),
                };
                sectionBody.Children.Add(winCard);
            }

            // ── Active advance tickets with no win today ──────────────────────
            if (activeNoWin.Count > 0)
            {
                foreach (var w in activeNoWin)
                {
                    // D3 midday-only or evening-only ticket, and its own draw has already posted
                    // a result — there's no other draw to wait for on this ticket, so it's done.
                    bool d3Done = w.Game == "D3" && _lastData != null && w.DrawFilter switch
                    {
                        "M" => D3TimingRules.IsMiddayDone(_lastData.D3MiddayDrawNum, w.DrawEnd),
                        "E" => D3TimingRules.IsEveningDone(_lastData.D3EveningDrawNum, w.DrawEnd),
                        _   => false,
                    };

                    var row = new Grid
                    {
                        BackgroundColor = d3Done ? Color.FromArgb("#ECEFF1") : Color.FromArgb("#E3F2FD"),
                        Padding = new Thickness(10, 7),
                        Margin = new Thickness(0),
                        ColumnSpacing = 4,
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(GridLength.Auto),      // S# R#
                            new ColumnDefinition(GridLength.Star),      // numbers — always gets whatever room is left
                            new ColumnDefinition(GridLength.Auto),      // match
                            new ColumnDefinition(GridLength.Auto),      // draw#
                            new ColumnDefinition(GridLength.Auto),      // status
                        }
                    };
                    var activeSetLbl = MakeLabel($"Set{w.SetNumber}·R{w.RowNumber}", 9, "#555", 0, FontAttributes.Bold);
                    activeSetLbl.LineBreakMode = LineBreakMode.NoWrap;
                    row.Children.Add(activeSetLbl);
                    {
                        var numsView = enhanced
                            ? ResultsRendererEnhanced.MakePlayerNumbersView(w.Numbers, accent, false, null, w.Game)
                            : ResultsRendererClassic.MakePlayerNumbersView(w.Numbers, accent, false, null, w.Game);
                        if (!string.IsNullOrEmpty(w.BetType) && (w.Game == "D3" || w.Game == "D4"))
                        {
                            var numsRow = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
                            numsRow.Children.Add(numsView);
                            numsRow.Children.Add(new Label
                            {
                                Text = w.BetType, FontSize = 9, FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromArgb("#1565C0"),
                                VerticalOptions = LayoutOptions.Center,
                                LineBreakMode = LineBreakMode.NoWrap
                            });
                            if (w.Game == "D3" && !string.IsNullOrEmpty(w.DrawFilter))
                            {
                                numsRow.Children.Add(new Label
                                {
                                    Text = D3DrawFilterLabel(w.DrawFilter), FontSize = 9, FontAttributes = FontAttributes.Bold,
                                    TextColor = Color.FromArgb("#6A1B9A"),
                                    VerticalOptions = LayoutOptions.Center,
                                    LineBreakMode = LineBreakMode.NoWrap
                                });
                            }
                            Grid.SetColumn(numsRow, 1);
                            row.Children.Add(numsRow);
                        }
                        else
                        {
                            Grid.SetColumn(numsView, 1);
                            row.Children.Add(numsView);
                        }
                    }
                    row.Children.Add(MakeMatchBadge(w.MatchLabel, "", false, 2));
                    int activeDrawNum = GetRowDrawNum(w);
                    string activeDrawText = activeDrawNum > 0 ? $"#{activeDrawNum}" : "";
                    var activeDrawLbl = MakeLabel(activeDrawText, 10, "#777", 3, FontAttributes.None, TextAlignment.Center);
                    activeDrawLbl.Margin = new Thickness(6, 0, 0, 0);
                    activeDrawLbl.LineBreakMode = LineBreakMode.NoWrap;
                    row.Children.Add(activeDrawLbl);
                    string playRangeLabel = !string.IsNullOrEmpty(w.PlayFromDate) && !string.IsNullOrEmpty(w.PlayToDate)
                        ? $"{w.PlayFromDate} - {w.PlayToDate}"
                        : !string.IsNullOrEmpty(w.PlayToDate) ? w.PlayToDate : "—";
                    var playRangeLbl = MakeLabel(playRangeLabel, 9, "#1565C0", 4, FontAttributes.Bold, TextAlignment.End);
                    playRangeLbl.LineBreakMode = LineBreakMode.NoWrap;
                    row.Children.Add(playRangeLbl);
                    if (d3Done)
                    {
                        var strikeLine = new BoxView
                        {
                            Color = Color.FromArgb("#78909C"),
                            HeightRequest = 2,
                            VerticalOptions = LayoutOptions.Center,
                            HorizontalOptions = LayoutOptions.Fill,
                            InputTransparent = true,
                        };
                        Grid.SetColumn(strikeLine, 0);
                        Grid.SetColumnSpan(strikeLine, row.ColumnDefinitions.Count);
                        row.Children.Add(strikeLine);
                    }
                    // Tap to navigate to that set with the row highlighted
                    var advTap = new TapGestureRecognizer();
                    var capturedAdvW = w;
                    var capturedAdvRow = row;
                    advTap.Tapped += (_, _) => _ = OnWinnerRowTappedAsync(capturedAdvW, capturedAdvRow);
                    row.GestureRecognizers.Add(advTap);
                    var advCard = new Border
                    {
                        Content = row,
                        Stroke = new SolidColorBrush(Color.FromArgb(d3Done ? "#CFD8DC" : "#90CAF9")),
                        StrokeThickness = 1,
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) },
                        Padding = new Thickness(0),
                        Margin = new Thickness(0, 3),
                    };
                    sectionBody.Children.Add(advCard);
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
            .Where(IsRowStillVisible)
            .Where(w => !_collKeyMap.TryGetValue(w, out var key)
                        || Preferences.Get(key, true))
            .ToList();
        UpdateTotalWinnings(active);
    }

    // Single source of truth for "is this row still current," shared by BuildSection's
    // actualWinners filter and the TOTAL WINNINGS banner so they can't disagree again.
    // - D3: draw# completion (it draws twice a day, so calendar-date alone is unreliable).
    // - Advance ticket (PlayFromDate set or DrawEnd>0): current until its PlayToDate passes —
    //   an early win inside a still-running range must NOT be treated as expired.
    // - Regular single-day ticket: current until its own DrawDate passes.
    private bool IsRowStillVisible(WinnerEntry w)
    {
        if (_lastData == null) return true;
        return _lastData.IsWinnerCurrent(w);
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

    private void RefreshGameHeader(string gameKey)
    {
        if (_lastData == null || !_gameTotalLabels.TryGetValue(gameKey, out var lbl)) return;
        // Must apply the same "is this still current" filter RefreshTotalWinnings/BuildSection
        // use — this header bar was reading _lastData.Winners directly with no date/draw
        // currency check at all, so a stale win from a prior day kept showing here (with its
        // own dollar total) even after the TOTAL WINNINGS banner had correctly stopped
        // counting it. A 4th place needing the same rule, found 2026-08-02.
        var winners = _lastData.Winners.Where(w => w.Game == gameKey && !w.IsActiveNoWin && IsRowStillVisible(w)).ToList();
        decimal total = CheckedGameTotal(winners);
        lbl.Text = total > 0 ? $"${total:N0}" : "";
        lbl.IsVisible = total > 0;
    }

    // Only sum prizes whose collection checkbox is checked (past rows); non-past rows always counted
    private decimal CheckedGameTotal(List<WinnerEntry> winners)
    {
        decimal total = 0;
        foreach (var w in winners)
        {
            if (string.IsNullOrEmpty(w.Prize)) continue;
            var (amt, _, _, _) = ParsePrize(w.Prize);
            if (amt == 0) continue;
            string rowDateStr = w.DrawDate ?? "";
            if (!string.IsNullOrEmpty(rowDateStr) &&
                DateTime.TryParse(rowDateStr, out var rowDate) &&
                rowDate.Date < DateTime.Today)
            {
                string collKey = $"coll_{w.Game}_{w.SetNumber}_{w.RowNumber}_{rowDateStr}";
                if (!Preferences.Get(collKey, true)) continue;
            }
            total += amt;
        }
        return total;
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

    // Parse player number string into (displayText, isMatch) pairs for highlighting
    static List<(string Text, bool IsMatch)> ParsePlayerNums(string nums, HashSet<int>? winSet)
    {
        var result = new List<(string, bool)>();
        foreach (var part in nums.Split(' '))
        {
            if (string.IsNullOrEmpty(part)) continue;
            if (part.StartsWith("|"))
            {
                result.Add((part, false));
                continue;
            }
            if (int.TryParse(part, out int n))
                result.Add((part, winSet?.Contains(n) ?? false));
            else
                result.Add((part, false));
        }
        return result;
    }

    // Build a colored badge Border for match count.
    // Bonus-only matches ("+M"/"+MB"/"+PB", 0 main numbers) skip the badge entirely —
    // that info is now conveyed by turning the bonus ball itself green (see MakePlayerNumbersView),
    // and a "0/5" badge next to a win just reads as a loss.
    // Converts the stored per-row D3 draw filter ("B"/"M"/"E") to the display tag the user reads on-screen.
    static string D3DrawFilterLabel(string df) => df switch { "M" => "M", "E" => "E", _ => "M+E" };

    static View MakeMatchBadge(string matchLabel, string prize, bool isRowPast, int col)
    {
        bool hasBonusSuffix = matchLabel.Contains('+');
        int count;
        string displayLabel;
        if (hasBonusSuffix)
        {
            var lm = System.Text.RegularExpressions.Regex.Match(matchLabel, @"^(\d+)");
            count = lm.Success ? int.Parse(lm.Groups[1].Value) : 0;
            if (count == 0)
            {
                var empty = new BoxView { WidthRequest = 0, HeightRequest = 0, Color = Colors.Transparent };
                Grid.SetColumn(empty, col);
                return empty;
            }
            displayLabel = $"{count}/5";
        }
        else
        {
            var m = System.Text.RegularExpressions.Regex.Match(matchLabel, @"(\d+)/\d+");
            count = m.Success ? int.Parse(m.Groups[1].Value) : 0;
            displayLabel = matchLabel;
        }

        string bgColor;
        if (isRowPast)
            bgColor = "#BDBDBD";
        else if (!string.IsNullOrEmpty(prize))
            bgColor = "#FFB300";  // gold = won something
        else
            bgColor = count >= 3 ? "#43A047" : count >= 1 ? "#FF8F00" : "#9E9E9E";

        var badge = new Border
        {
            BackgroundColor = Color.FromArgb(bgColor),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(0, 2),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = displayLabel,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
            }
        };
        Grid.SetColumn(badge, col);
        return badge;
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
            "Fantasy 5", "Super Lotto", "Powerball", "Mega Millions", "Daily 3", "Daily 4", "Daily Derby", "Hot Spot", "Notifications", "Summary of Winnings", "Check Wins for Draw#");
        if (choice == null || choice == "Cancel") return;
        if (choice == "Summary of Winnings") { await Shell.Current.GoToAsync(nameof(SummaryPage), false); return; }
        if (choice == "Check Wins for Draw#") { DrawSearchPage.PresetGame = "Fantasy 5"; await Shell.Current.GoToAsync(nameof(DrawSearchPage), false); return; }
        await GoHomeAsync();
        await Task.Yield();
        switch (choice)
        {
            case "Fantasy 5":    WinnerPage.ComingFrom    = "main"; AppShell.WinnerPageInstance.PrePosition(true);     await Shell.Current.GoToAsync(nameof(WinnerPage),      false); break;
            case "Super Lotto":  SuperLottoPage.ComingFrom = "main"; AppShell.SuperLottoPageInstance.PrePosition(true); await Shell.Current.GoToAsync(nameof(SuperLottoPage),  false); break;
            case "Powerball":    PowerballPage.ComingFrom  = "main"; AppShell.PowerballPageInstance.PrePosition(true);  await Shell.Current.GoToAsync(nameof(PowerballPage),   false); break;
            case "Mega Millions":MegaMillionsPage.ComingFrom="main"; AppShell.MegaMillionsPageInstance.PrePosition(true);await Shell.Current.GoToAsync(nameof(MegaMillionsPage),false); break;
            case "Daily 3":      Daily3Page.ComingFrom     = "main"; AppShell.Daily3PageInstance.PrePosition(true);     await Shell.Current.GoToAsync(nameof(Daily3Page),      false); break;
            case "Daily 4":      Daily4Page.ComingFrom     = "main"; AppShell.Daily4PageInstance.PrePosition(true);     await Shell.Current.GoToAsync(nameof(Daily4Page),      false); break;
            case "Daily Derby":  DailyDerbyPage.ComingFrom = "main"; AppShell.DailyDerbyPageInstance.PrePosition(true); await Shell.Current.GoToAsync(nameof(DailyDerbyPage),  false); break;
            case "Hot Spot":      await Shell.Current.GoToAsync(nameof(HotSpotPage), false); break;
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = GoHomeAsync();
        return true;
    }

    // Silently remove non-active slots whose advance dates have all expired.
    static void AutoClearExpiredOldSets()
    {
        foreach (var prefix in new[] { "f5", "sl", "pb", "mm", "d3", "d4", "dd" })
        {
            int activeSlot = Preferences.Get($"{prefix}_active_slot", -1);
            for (int s = 0; s < 10; s++)
            {
                if (s == activeSlot) continue;
                string setData = Preferences.Get($"{prefix}_set_{s}", "");
                if (string.IsNullOrEmpty(setData)) continue;
                string adv = Preferences.Get($"{prefix}_adv_{s}", "");
                // Non-active slot with no advance dates at all — leave alone (may be user's saved set)
                if (string.IsNullOrEmpty(adv)) continue;
                // Only clear if ALL advance dates in this slot are expired
                bool allExpired = adv.Split('|').All(row =>
                {
                    if (string.IsNullOrEmpty(row) || row == "~~~" || row == "~") return true;
                    var p = row.Split('~');
                    if (p.Length >= 2 && !string.IsNullOrEmpty(p[1]) &&
                        DateTime.TryParseExact(p[1], "yyyyMMdd", null,
                            System.Globalization.DateTimeStyles.None, out var end))
                        return end.Date < DateTime.Today;
                    if (p.Length >= 1 && !string.IsNullOrEmpty(p[0]) &&
                        DateTime.TryParseExact(p[0], "yyyyMMdd", null,
                            System.Globalization.DateTimeStyles.None, out var start))
                        return start.Date < DateTime.Today;
                    return true;
                });
                if (!allExpired) continue;
                Preferences.Remove($"{prefix}_set_{s}");
                Preferences.Remove($"{prefix}_adv_{s}");
                if (prefix == "d3") Preferences.Remove($"d3_drawfilters_{s}");
            }
        }
    }

    private async Task ClearOldSetsAsync()
    {
        bool confirm = await DisplayAlert("Clear Old Sets",
            "This will permanently remove all saved sets (S2, S3, ...) that are not your current active set, for every game. Your active set numbers are kept.\n\nContinue?",
            "Clear", "Cancel");
        if (!confirm) return;

        var games = new[] { "f5", "sl", "pb", "mm", "d3", "d4", "dd" };
        int cleared = 0;
        foreach (var prefix in games)
        {
            int activeSlot = Preferences.Get($"{prefix}_active_slot", -1);
            for (int s = 0; s < 10; s++)
            {
                if (s == activeSlot) continue;
                string setKey = $"{prefix}_set_{s}";
                string advKey = $"{prefix}_adv_{s}";
                if (!string.IsNullOrEmpty(Preferences.Get(setKey, "")) ||
                    !string.IsNullOrEmpty(Preferences.Get(advKey, "")))
                {
                    Preferences.Remove(setKey);
                    Preferences.Remove(advKey);
                    if (prefix == "d3") Preferences.Remove($"d3_drawfilters_{s}");
                    cleared++;
                }
            }
            // Also clear expired advance dates on the active slot
            if (activeSlot >= 0)
            {
                string advActive = Preferences.Get($"{prefix}_adv_{activeSlot}", "");
                if (!string.IsNullOrEmpty(advActive))
                {
                    var rows = advActive.Split('|');
                    bool allExpired = rows.All(row =>
                    {
                        if (string.IsNullOrEmpty(row) || row == "~~~" || row == "~") return true;
                        var parts = row.Split('~');
                        if (parts.Length < 2) return true;
                        return DateTime.TryParseExact(parts[1], "yyyyMMdd", null,
                            System.Globalization.DateTimeStyles.None, out var end)
                            && end.Date < DateTime.Today;
                    });
                    if (allExpired)
                        Preferences.Remove($"{prefix}_adv_{activeSlot}");
                }
            }
        }

        ResultsPageCls.ClearCache();
        UpdateTicketCount();
        await RunCheck(resultDatePicker?.Date ?? DateTime.Today);
        await DisplayAlert("Done", $"Cleared {cleared} old set(s). Results refreshed.", "OK");
    }

    private async Task GoHomeAsync()
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        Shell.Current.CurrentPage.TranslationX = -w;
        await Shell.Current.GoToAsync("..", false);
    }

    private async void BtnRefresh_Clicked(object sender, EventArgs e)
    {
        if (btnCheckTickets != null) btnCheckTickets.IsEnabled = false;
        if (btnRefresh != null) btnRefresh.IsEnabled = false;
        SetBusy(true, "Refreshing...");
        try
        {
            ResultsPageCls.ClearCache();
            await RunCheck(resultDatePicker?.Date ?? DateTime.Today);
        }
        finally
        {
            if (btnCheckTickets != null) btnCheckTickets.IsEnabled = true;
            if (btnRefresh != null) btnRefresh.IsEnabled = true;
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void SetBusy(bool busy, string message)
    {
        spinner.IsVisible = busy;
        spinner.IsRunning = busy;
        lblStatus.Text = message;
        if (!busy && _lastFetchTime > DateTime.MinValue)
            lblLastUpdated.Text = $"Updated {_lastFetchTime:h:mm:ss tt}";
    }
}
