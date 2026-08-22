using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class Daily3Page : ContentPage
{
    const int Rows = 10;
    const int Cols = 3;

    readonly Label[] _wLabels;  // midday
    readonly Label[] _eLabels;  // evening

    readonly Entry[,] _entries = new Entry[Rows, Cols];
    readonly Label[]  _results = new Label[Rows];
    readonly Button[] _betTypeBtns = new Button[Rows];
    readonly Button[] _drawFilterBtns = new Button[Rows];
    readonly CheckBox[] _permChks = new CheckBox[Rows];
    readonly Label[]    _permLabels = new Label[Rows];
    readonly BoxView[]  _dataDots = new BoxView[Rows]; // shows when a row already has saved numbers
    DateTime?[] _playStart   = new DateTime?[Rows];
    DateTime?[] _playEnd     = new DateTime?[Rows];
    string[]    _logSnapshot = new string[Rows];
    string[]    _drawStart   = new string[Rows];
    string[]    _drawEnd     = new string[Rows];
    Grid?       _advOverlay;
    DatePicker? _advStartPicker;
    DatePicker? _advEndPicker;
    Label?      _advWarnLabel;
    Entry?      _advDrawStartEntry;
    Entry?      _advDrawEndEntry;
    int         _advRow = -1;

    string[] _betTypes = Enumerable.Repeat("S", Rows).ToArray();
    string[] _drawFilters = Enumerable.Repeat("B", Rows).ToArray(); // B=Both M=Midday E=Evening
    static readonly string[] BetCycle = ["S", "B", "S&B"];
    static readonly string[] DrawCycle = ["B", "M", "E"];
    int  _activeSlot = -1;
    bool _suppressPickerEvent = false;
    int  _pickerSuppressEpoch = 0;
    bool _suppressExcl = false;
    bool _loading = false;
    bool _retreating = false;
    CancellationTokenSource? _autoSaveCts;
    readonly Dictionary<int, (string entries, string betTypes, string drawFilters)> _slotCache = new();
    View? _highlightedView;

    List<(string DateLabel, int[] Midday, int MiddayDrawNum, int[] Evening, int EveningDrawNum)> _draws = new();
    bool _drawsLoaded = false;

    int[]? _winMidday;
    int[]? _winEvening;
    bool _isPanning = false;
    bool _voiceOn = false;
    bool _voiceSettingText = false;
    bool _overrideMode = false;
    bool _suppressAdvApply = false;  // blocks ApplyAdvanceToRowIfActive during slot loads
    bool _advDatesLoaded   = false;  // prevents SaveAdvanceDates from overwriting before LoadAdvanceDates runs
    int  _voiceRow = 0, _voiceCol = 0;
    Entry? _voiceTarget = null;
    Color _voiceTargetOldColor = Colors.White;

    // "mm" = came via carousel from MegaMillions; "main" = navigated directly
    internal static string ComingFrom { get; set; } = "mm";

    public Daily3Page()
    {
        InitializeComponent();
        _wLabels = new[] { lblW1, lblW2, lblW3 };
        _eLabels = new[] { lblE1, lblE2, lblE3 };
        BuildRows();
        BuildSlotPicker();
        BuildAdvancePlayOverlay();
        _suppressPickerEvent = true;
        allBetPicker.Items.Add("Straight");
        allBetPicker.Items.Add("Box");
        allBetPicker.Items.Add("Str+Box");
        allBetPicker.SelectedIndex = Preferences.Get("d3_allBetIdx", 0);
        _suppressPickerEvent = false;
    }

    double _panLeft, _panRight;

    private async void OnPagePan(object? sender, PanUpdatedEventArgs e)
    {
        if (_isPanning) return;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panLeft = _panRight = 0;
                break;
            case GestureStatus.Running:
                if (e.TotalX < _panLeft)  _panLeft  = e.TotalX;
                if (e.TotalX > _panRight) _panRight = e.TotalX;
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_panLeft < -40) // left → go to D4
                {
                    _isPanning = true;
                    Daily4Page.ComingFrom = "d3";
                    AppShell.Daily4PageInstance.PrePosition(true);
                    await Shell.Current.GoToAsync(nameof(Daily4Page), false);
                    _isPanning = false;
                }
                else if (_panRight > 40) // right → go back
                {
                    _isPanning = true;
                    await GoBack();
                    _isPanning = false;
                }
                _panLeft = _panRight = 0;
                break;
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = GoBack();
        return true;
    }

    private async Task GoBack()
    {
        if (ComingFrom == "mm")
            AppShell.MegaMillionsPageInstance.PrePosition(false);
        else
        {
            double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
            Shell.Current.CurrentPage.TranslationX = -w;
        }
        await Shell.Current.GoToAsync("..", false);
    }

    private async void BtnGameMenu_Clicked(object sender, EventArgs e)
    {
        string? choice = await DisplayActionSheet("Go to Game", "Cancel", null,
            "Fantasy 5", "Super Lotto", "Powerball", "Mega Millions", "Daily 3", "Daily 4", "Daily Derby", "Hot Spot", "Notifications", "Summary of Winnings", "Check Wins for Draw#");
        if (choice == null || choice == "Cancel") return;
        if (choice == "Notifications") { await Shell.Current.GoToAsync(nameof(NotificationsPage), false); return; }
        if (choice == "Summary of Winnings") { await Shell.Current.GoToAsync(nameof(SummaryPage), false); return; }
        if (choice == "Check Wins for Draw#") { DrawSearchPage.PresetGame = "Daily 3"; await Shell.Current.GoToAsync(nameof(DrawSearchPage), false); return; }
        MainPage.Instance?.ShowNavOverlay($"Loading {choice}...");
        await Shell.Current.Navigation.PopToRootAsync(false);
        await Task.Delay(100);
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

    private async void BtnGoHome_Clicked(object sender, EventArgs e) =>
        await Shell.Current.Navigation.PopToRootAsync(false);

    private async void BtnNumIntel_Clicked(object sender, EventArgs e)
    {
        string? choice = await DisplayActionSheet("Number Intelligence", "Cancel", null,
            "Back-Test Numbers", "Hot & Cold Numbers", "Positional Frequency",
            "Sum Range", "Balance Check", "Pairs & Triplets",
            "Number Gap Tracker", "Ticket Scorer", "Wheeling System", "Rundown Calculator");
        if (choice == null || choice == "Cancel") return;
        if (choice == "Back-Test Numbers")    { BackTestPage.PresetGame      = "Daily 3"; await Shell.Current.GoToAsync(nameof(BackTestPage),      false); return; }
        if (choice == "Hot & Cold Numbers")   { HotColdPage.PresetGame       = "Daily 3"; await Shell.Current.GoToAsync(nameof(HotColdPage),       false); return; }
        if (choice == "Positional Frequency") { PositionalFreqPage.PresetGame = "Daily 3"; await Shell.Current.GoToAsync(nameof(PositionalFreqPage), false); return; }
        if (choice == "Sum Range")            { SumRangePage.PresetGame      = "Daily 3"; await Shell.Current.GoToAsync(nameof(SumRangePage),      false); return; }
        if (choice == "Balance Check")        { BalanceCheckPage.PresetGame  = "Daily 3"; await Shell.Current.GoToAsync(nameof(BalanceCheckPage),  false); return; }
        if (choice == "Pairs & Triplets")     { PairsTripletsPage.PresetGame = "Daily 3"; await Shell.Current.GoToAsync(nameof(PairsTripletsPage), false); return; }
        if (choice == "Number Gap Tracker")   { GapTrackerPage.PresetGame    = "Daily 3"; await Shell.Current.GoToAsync(nameof(GapTrackerPage),    false); return; }
        if (choice == "Ticket Scorer")        { TicketScorerPage.PresetGame  = "Daily 3"; await Shell.Current.GoToAsync(nameof(TicketScorerPage),  false); return; }
        if (choice == "Wheeling System")      { WheelingPage.PresetGame      = "Daily 3"; await Shell.Current.GoToAsync(nameof(WheelingPage),      false); return; }
        if (choice == "Rundown Calculator")   { RundownPage.PresetGame       = "Daily 3"; await Shell.Current.GoToAsync(nameof(RundownPage),       false); return; }
    }

    private async void BtnGoD4_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        Daily4Page.ComingFrom = "d3";
        AppShell.Daily4PageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(Daily4Page), false);
    }

    internal void PrePosition(bool fromRight)
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        TranslationX = fromRight ? w : -w;
        spinner.IsVisible = true;
        spinner.IsRunning = true;
        loadingOverlay.IsVisible = true;
    }

    protected override void OnAppearing()
    {
        this.TranslateTo(0, 0, 220, Easing.CubicOut);
        base.OnAppearing();
        UpdateTicketCount();
        _ = LoadAllDraws();
        _ = DrawNumberService.RefreshDaily3IfNeededAsync();
        Dispatcher.Dispatch(() =>
        {
            int pendingRow = -1;
            if (PendingHighlight.HasPending && PendingHighlight.Game == "D3")
            {
                _activeSlot = PendingHighlight.Slot;
                pendingRow  = PendingHighlight.Row;
                PendingHighlight.Clear();
                Preferences.Set("d3_active_slot", _activeSlot);
                FillFromSlot(_activeSlot);
            }
            else
            {
                _activeSlot = Preferences.Get("d3_active_slot", -1);
                if (_activeSlot < 0)
                {
                    var current = Preferences.Get("d3_entries", "");
                    if (!string.IsNullOrEmpty(current))
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            if (SlotHasData(i) && Preferences.Get(SetKey(i), "") == current)
                            {
                                _activeSlot = i;
                                break;
                            }
                        }
                    }
                    if (_activeSlot < 0) _activeSlot = 0;
                }
                if (SlotHasData(_activeSlot))
                    FillFromSlot(_activeSlot);
                else
                {
                    LoadEntries();
                    LoadAdvanceDates(_activeSlot);
                    RefreshAdvAllPanel();
                }
            }
            UpdateSlotPicker();
            if (pendingRow >= 0)
                _ = HighlightRow(pendingRow);
            TakeLogSnapshot();
        });
    }

    void TakeLogSnapshot()
    {
        for (int r = 0; r < Rows; r++)
        {
            var nums = new List<string>();
            bool full = true;
            for (int c = 0; c < Cols; c++)
            {
                string v = _entries[r, c].Text ?? "";
                if (string.IsNullOrWhiteSpace(v)) { full = false; break; }
                nums.Add(v);
            }
            _logSnapshot[r] = full ? string.Join(" ", nums) : "";
        }
    }

    async Task LogCurrentTicketsAsync()
    {
        var rows = new List<(int, int, string, string, string, string)>();
        for (int s = 0; s < 10; s++)
        {
            string set   = Preferences.Get($"d3_set_{s}", "");
            string adv   = Preferences.Get($"d3_adv_{s}", "");
            string dfRaw = Preferences.Get($"d3_drawfilters_{s}", "");
            if (string.IsNullOrEmpty(set)) continue;
            var vals     = set.Split('|');
            var advParts = string.IsNullOrEmpty(adv) ? new string[Rows] : adv.Split('|');
            var dfParts  = string.IsNullOrEmpty(dfRaw) ? Array.Empty<string>() : dfRaw.Split('|');
            if (advParts.Length < Rows) Array.Resize(ref advParts, Rows);
            for (int r = 0; r < Rows; r++)
            {
                var nums = new List<string>(); bool full = true;
                for (int c = 0; c < Cols; c++)
                {
                    string v = r * Cols + c < vals.Length ? vals[r * Cols + c] : "";
                    if (string.IsNullOrWhiteSpace(v)) { full = false; break; }
                    nums.Add(v);
                }
                if (!full) continue;
                string numbersStr = string.Join(" ", nums);
                string df = r < dfParts.Length && !string.IsNullOrEmpty(dfParts[r]) ? dfParts[r] : "B";
                string pf = "", pt = "";
                string drawStartStr = "", drawEndStr = "";
                if (!string.IsNullOrEmpty(advParts[r]))
                {
                    var pair = advParts[r].Split('~');
                    if (pair.Length >= 2)
                    {
                        if (DateTime.TryParseExact(pair[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var fd)) pf = fd.ToString("M/d");
                        string eff = string.IsNullOrEmpty(pair[1]) ? pair[0] : pair[1];
                        if (DateTime.TryParseExact(eff, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var td)) pt = td.ToString("M/d");
                    }
                    if (pair.Length >= 3) drawStartStr = pair[2] ?? "";
                    if (pair.Length >= 4) drawEndStr   = pair[3] ?? "";
                }
                // For non-active slots, determine validity using advance dates or draw numbers.
                // Regular tickets with no advance info were purchased on a different date — skip.
                bool hasAdvDate = !string.IsNullOrEmpty(pf) || !string.IsNullOrEmpty(pt);
                bool hasDrawNum = !string.IsNullOrEmpty(drawStartStr) || !string.IsNullOrEmpty(drawEndStr);
                if (s != _activeSlot && !hasAdvDate)
                {
                    if (!hasDrawNum) continue; // regular ticket from a different day — skip
                    // Draw-number advance ticket: skip if its draw range has already passed
                    int nextDraw = DrawNumberService.GetNextDraw("Daily 3");
                    if (nextDraw > 0)
                    {
                        string endStr = !string.IsNullOrEmpty(drawEndStr) ? drawEndStr : drawStartStr;
                        if (int.TryParse(endStr, out int drawEnd) && drawEnd < nextDraw) continue;
                    }
                }
                if (df == "B") { rows.Add((s, r, numbersStr, "M", pf, pt)); rows.Add((s, r, numbersStr, "E", pf, pt)); }
                else rows.Add((s, r, numbersStr, df, pf, pt));
            }
        }
        // Only clear today's entries for the active slot — other slots' past entries stay intact.
        await Services.TicketLogService.ClearTodayGameSlotAsync("D3", _activeSlot >= 0 ? _activeSlot : 0);
        await Services.TicketLogService.LogRowsAsync("D3", rows);
    }

    // A row with 1-2 of 3 digits filled was never a completed ticket. Left in place, it
    // silently persists in Preferences and can get miscounted by any code path that only
    // checks a row's first column instead of the whole row. Scrub on exit only (never during
    // normal typing/autosave) so an in-progress row is never wiped out from under the user.
    private void ScrubIncompleteRows()
    {
        for (int r = 0; r < Rows; r++)
        {
            bool anyFilled = false, allFilled = true;
            for (int c = 0; c < Cols; c++)
            {
                bool has = !string.IsNullOrWhiteSpace(_entries[r, c].Text);
                if (has) anyFilled = true; else allFilled = false;
            }
            if (anyFilled && !allFilled)
                ClearRow(r);
        }
    }

    protected override void OnDisappearing()
    {
        ScrubIncompleteRows();
        SaveAdvanceDates(_activeSlot);
        SaveEntries();
        if (_activeSlot >= 0)
        {
            SaveSet(_activeSlot);
            Preferences.Set("d3_active_slot", _activeSlot);
        }
        Services.TicketLogService.PendingWriteTask = LogCurrentTicketsAsync();
        _ = Services.TicketLogService.ForceSyncTodayAsync();
        base.OnDisappearing();
        if (_voiceOn) StopVoice();
        if (_highlightedView != null) { _highlightedView.BackgroundColor = Colors.White; _highlightedView = null; }
    }

    private void TriggerAutoSaveIndicator()
    {
        _autoSaveCts?.Cancel();
        _autoSaveCts = new CancellationTokenSource();
        var token = _autoSaveCts.Token;
        Task.Delay(600, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_activeSlot >= 0) SaveSet(_activeSlot);
                lblStatus.Text = "Auto-saved \u2713";
                var cts2 = new CancellationTokenSource();
                Task.Delay(1500, cts2.Token).ContinueWith(_ =>
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (lblStatus.Text == "Auto-saved \u2713")
                            lblStatus.Text = "Ready";
                    }));
            });
        });
    }

    private void SaveEntries()
    {
        var vals = new string[Rows * Cols];
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                vals[r * Cols + c] = _entries[r, c].Text ?? "";
        Preferences.Set("d3_entries", string.Join("|", vals));
        Preferences.Set("d3_bettypes", string.Join("|", _betTypes));
        Preferences.Set("d3_drawfilters", string.Join("|", _drawFilters));
        Preferences.Set("d3_from", fromEntry.Text ?? "");
    }

    private void LoadEntries()
    {
        _loading = true;
        ClearAllEntries();
        var saved = Preferences.Get("d3_entries", "");
        if (!string.IsNullOrEmpty(saved))
        {
            var vals = saved.Split('|');
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    int idx = r * Cols + c;
                    if (idx < vals.Length)
                        _entries[r, c].Text = vals[idx];
                }
        }
        LoadBetTypes("d3_bettypes");
        LoadDrawFilters(Preferences.Get("d3_drawfilters", ""));
        fromEntry.Text = Preferences.Get("d3_from", "");
        _loading = false;
        if (int.TryParse(fromEntry.Text, out int n))
            HighlightRows(n);
    }

    private void LoadBetTypes(string key) =>
        LoadBetTypes_FromString(Preferences.Get(key, ""));

    private void LoadDrawFilters(string value)
    {
        var parts = value.Split('|');
        for (int r = 0; r < Rows; r++)
        {
            var v = r < parts.Length ? parts[r] : "B";
            _drawFilters[r] = DrawCycle.Contains(v) ? v : "B";
            UpdateDrawFilterBtn(r);
        }
    }

    // Rows with no stored bet type (blank/new rows, cleared rows, brand-new sets) should
    // default to whatever "All" is currently set to, not a hardcoded Straight — otherwise the
    // All dropdown shows one thing while rows silently use another. Every place in this file
    // that resets a row's bet type to a bare default must go through this, not "S" directly.
    private string DefaultBetType()
    {
        int allIdx = Preferences.Get("d3_allBetIdx", 0);
        return BetCycle[Math.Clamp(allIdx, 0, BetCycle.Length - 1)];
    }

    private void LoadBetTypes_FromString(string value)
    {
        string defaultBt = DefaultBetType();
        var parts = value.Split('|');
        for (int r = 0; r < Rows; r++)
        {
            var val = r < parts.Length && !string.IsNullOrEmpty(parts[r]) ? parts[r] : defaultBt;
            if (val == "S+B") val = "S&B";
            _betTypes[r] = BetCycle.Contains(val) ? val : defaultBt;
            UpdateBetBtn(r);
        }
    }

    // ── Saved Number slots ───────────────────────────────────────────────────

    private string SetKey(int slot) => $"d3_set_{slot}";

    // Small dot next to the row# so a row that already holds saved numbers is visible
    // *before* you start typing into it — never clears/touches the data itself.
    private void UpdateRowDataDots()
    {
        if (_entries == null || _dataDots == null) return;
        for (int r = 0; r < Rows; r++)
        {
            if (_dataDots[r] == null) continue;
            bool hasData = !string.IsNullOrWhiteSpace(_entries[r, 0]?.Text) ||
                           !string.IsNullOrWhiteSpace(_entries[r, 1]?.Text) ||
                           !string.IsNullOrWhiteSpace(_entries[r, 2]?.Text);
            _dataDots[r].IsVisible = hasData;
        }
    }

    private void UpdateTicketCount()
    {
        int total = 0;
        for (int s = 0; s < 10; s++)
        {
            if (s == _activeSlot)
            {
                // count live from in-memory entries for the active slot — a row only
                // counts once ALL 3 digits are filled, not just the first.
                if (_entries != null)
                    for (int r = 0; r < Rows; r++)
                    {
                        bool full = true;
                        for (int c = 0; c < Cols; c++)
                            if (string.IsNullOrWhiteSpace(_entries[r, c]?.Text)) { full = false; break; }
                        if (full) total++;
                    }
            }
            else
            {
                var vals = Preferences.Get($"d3_set_{s}", "").Split('|');
                for (int r = 0; r < 10; r++)
                {
                    int idx = r * 3;
                    bool full = idx + 3 <= vals.Length;
                    for (int c = 0; full && c < 3; c++)
                        if (string.IsNullOrWhiteSpace(vals[idx + c])) full = false;
                    if (full) total++;
                }
            }
        }
        UpdateRowDataDots();
        lblTicketCount.Text = total > 0 ? $"🎟 {total}" : "";
    }
    private string BetKey(int slot) => $"d3_btypes_{slot}";
    private string DfKey(int slot)  => $"d3_drawfilters_{slot}";

    private string GetCurrentEntryString()
    {
        var vals = new string[Rows * Cols];
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                vals[r * Cols + c] = _entries[r, c].Text ?? "";
        return string.Join("|", vals);
    }

    private void ClearAllEntries()
    {
        string defaultBt = DefaultBetType();
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
                _entries[r, c].Text = "";
            _betTypes[r] = defaultBt;
            UpdateBetBtn(r);
            _drawFilters[r] = "B";
            UpdateDrawFilterBtn(r);
        }
        foreach (var lbl in _results) lbl.Text = "";
        Array.Clear(_playStart, 0, Rows);
        Array.Clear(_playEnd,   0, Rows);
        Array.Clear(_drawStart, 0, Rows);
        Array.Clear(_drawEnd,   0, Rows);
        _advDatesLoaded = true; // user-triggered clear — allow subsequent saves
        UpdateAllResultBackgrounds();
        if (int.TryParse(fromEntry.Text, out int n)) HighlightRows(n);
    }

    private void ClearRow(int r)
    {
        for (int c = 0; c < Cols; c++)
            _entries[r, c].Text = "";
        _results[r].Text = "";
        _playStart[r] = null;
        _playEnd[r]   = null;
        _drawStart[r] = "";
        _drawEnd[r]   = "";
        UpdateResultBackground(r);
        _betTypes[r] = DefaultBetType();
        UpdateBetBtn(r);
        if (_permChks[r] != null) _permChks[r].IsChecked = false;
        _permLabels[r].IsVisible = false;
        SaveEntries();
    }

    private void SaveSet(int slot, bool refreshPicker = true)
    {
        string data = GetCurrentEntryString();
        bool isEmpty = data.Replace("|", "").Trim().Length == 0;
        if (isEmpty)
        {
            Preferences.Remove(SetKey(slot));
            Preferences.Remove(BetKey(slot));
            Preferences.Remove(DfKey(slot));
        }
        else
        {
            Preferences.Set(SetKey(slot), data);
            Preferences.Set(BetKey(slot), string.Join("|", _betTypes));
            Preferences.Set(DfKey(slot), string.Join("|", _drawFilters));
        }
        ResultsPage.NeedsRefresh = true;
        // SlotPicker_Changed calls this mid-flight (refreshPicker:false) then refreshes the
        // picker itself right after — calling UpdateSlotPicker() here too would reassign the
        // native Picker's SelectedIndex/Unfocus from inside its own change callback, which
        // wedges the UI thread against Android's input dispatcher (ANR: "waited for FocusEvent").
        if (refreshPicker) UpdateSlotPicker();
    }

    private void FillFromSlot(int slot)
    {
        var saved = Preferences.Get(SetKey(slot), "");
        if (string.IsNullOrEmpty(saved)) return;
        _suppressAdvApply = true;
        try
        {
            _loading = true;
            var vals = saved.Split('|');
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    int idx = r * Cols + c;
                    _entries[r, c].Text = idx < vals.Length ? vals[idx] : "";
                }
            LoadBetTypes(BetKey(slot));
            LoadDrawFilters(Preferences.Get(DfKey(slot), ""));
            _loading = false;
            LoadAdvanceDates(slot);
            RefreshAdvAllPanel();
            CheckAll();
            UpdateAllResultBackgrounds();
        }
        finally { _suppressAdvApply = false; }
    }

    private bool SlotHasData(int slot) =>
        !string.IsNullOrEmpty(Preferences.Get(SetKey(slot), ""));

    private string ExclKey(int slot) => $"excl_set_d3_{slot}";

    private string SlotLabel(int slot)
    {
        bool excl = Preferences.Get(ExclKey(slot), false);
        string mark = SlotHasData(slot) ? "  ✓" : "";
        return excl ? $"Set {slot + 1}{mark} [X]" : $"Set {slot + 1}{mark}";
    }

    private void UpdateExclCheckbox()
    {
        _suppressExcl = true;
        chkExcl.IsChecked = _activeSlot >= 0 && Preferences.Get(ExclKey(_activeSlot), false);
        _suppressExcl = false;
    }

    private void ChkExcl_CheckedChanged(object sender, CheckedChangedEventArgs e)
    {
        if (_suppressExcl || _activeSlot < 0) return;
        Preferences.Set(ExclKey(_activeSlot), e.Value);
        UpdateSlotPicker();
    }

    private bool _advAllDateMode = false;

    private async void BtnAdvAllCombinedSet_Clicked(object sender, EventArgs e)
    {
        bool hasDate = advAllFromPicker.Date.HasValue;
        DateTime from = advAllFromPicker.Date ?? DateTime.Today;
        DateTime toRaw = advAllToPicker.Date ?? DateTime.Today;
        var to = toRaw >= from ? toRaw : from;
        string ds = (entAdvAllStart.Text ?? "").Trim();
        string de = (entAdvAllEnd.Text ?? "").Trim();
        bool hasDraw = !string.IsNullOrEmpty(ds) && int.TryParse(ds, out _);
        if (!hasDate && !hasDraw) return;

        if (hasDraw)
        {
            string? warn = SpendingTracker.CheckDrawRangeWarning("D3", from, to, ds, de, "B",
                currentDrawNumber: DrawNumberService.GetNextDraw("Daily 3"));
            if (warn != null)
            {
                bool proceed = await DisplayAlert("Check Draw # Range", warn, "Save Anyway", "Fix It");
                if (!proceed)
                {
                    int current = DrawNumberService.GetNextDraw("Daily 3");
                    if (current <= 0) current = await DrawNumberService.EnsureNextDrawAsync("Daily 3");
                    if (current > 0)
                    {
                        entAdvAllStart.Text = current.ToString();
                        entAdvAllEnd.Text   = current.ToString();
                    }
                    return;
                }
            }
        }

        var sb = new System.Text.StringBuilder();
        if (hasDraw) sb.Append($"Draw #{ds}" + (string.IsNullOrEmpty(de) || de == ds ? "" : $"–{de}"));
        if (hasDate && hasDraw) sb.Append("  ");
        if (hasDate) sb.Append($"{from:M/d/yy}" + (to.Date == from.Date ? "" : $"–{to:M/d/yy}"));

        int applyCount = Rows;
        if (Preferences.Get("set_row_picker", true))
        {
            var options = Enumerable.Range(1, Rows).Select(i => $"{i} row{(i == 1 ? "" : "s")}").Append("Clear All Advances").ToArray();
            string? choice = await DisplayActionSheet($"Apply {sb} to:", "Cancel", null, options);
            if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;
            if (choice == "Clear All Advances")
            {
                for (int r = 0; r < Rows; r++)
                { _playStart[r] = null; _playEnd[r] = null; _drawStart[r] = ""; _drawEnd[r] = ""; }
                if (_activeSlot >= 0) SaveAdvanceDates(_activeSlot);
                UpdateAllResultBackgrounds();
                for (int r = 0; r < Rows; r++)
                    for (int c = 0; c < Cols; c++)
                        if (string.IsNullOrWhiteSpace(_entries[r, c].Text))
                            _entries[r, c].BackgroundColor = Color.FromArgb("#F5F5F5");
                return;
            }
            applyCount = int.Parse(choice.Split(' ')[0]);
        }

        for (int r = 0; r < Rows; r++)
        {
            _playStart[r] = null; _playEnd[r] = null;
            _drawStart[r] = ""; _drawEnd[r] = "";
        }
        for (int r = 0; r < applyCount && r < Rows; r++)
        {
            if (hasDate) { _playStart[r] = from; _playEnd[r] = to; }
            if (hasDraw) { _drawStart[r] = ds; _drawEnd[r] = string.IsNullOrEmpty(de) ? ds : de; }
        }
        if (_activeSlot >= 0) SaveAdvanceDates(_activeSlot);
        UpdateAllResultBackgrounds();
        // Tint empty rows that have advance dates
        for (int r = 0; r < Rows; r++)
        {
            bool hasAdv = _playStart[r].HasValue || _playEnd[r].HasValue || !string.IsNullOrEmpty(_drawStart[r]);
            var bg = hasAdv ? Color.FromArgb("#E3F2FD") : Color.FromArgb("#F5F5F5");
            for (int c = 0; c < Cols; c++)
                if (string.IsNullOrWhiteSpace(_entries[r, c].Text))
                    _entries[r, c].BackgroundColor = bg;
        }
    }

    private static bool TryParseAdvDate(string? text, out DateTime result)
    {
        if (DateTime.TryParseExact(text ?? "", new[] { "M/d/yy", "M/d/yyyy", "MM/dd/yy", "MM/dd/yyyy", "M/d/yy", "M-d-yy", "M-d-yyyy" },
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out result)) return true;
        if (DateTime.TryParse(text ?? "", out result)) return true;
        result = DateTime.Today;
        return false;
    }

    private void ApplyAdvanceToRowIfActive(int row)
    {
        if (_suppressAdvApply) return;
        bool alreadySet = _playStart[row].HasValue || _playEnd[row].HasValue || !string.IsNullOrEmpty(_drawStart[row]);
        if (alreadySet && !_overrideMode) return;
        bool applied = false;
        if (advAllFromPicker.Date.HasValue)
        {
            DateTime from = advAllFromPicker.Date ?? DateTime.Today;
            DateTime to = advAllToPicker.Date ?? DateTime.Today;
            if (to < from) to = from;
            _playStart[row] = from; _playEnd[row] = to;
            applied = true;
        }
        string ds = (entAdvAllStart.Text ?? "").Trim();
        string de = (entAdvAllEnd.Text ?? "").Trim();
        if (!string.IsNullOrEmpty(ds) && int.TryParse(ds, out _))
        {
            _drawStart[row] = ds;
            _drawEnd[row] = string.IsNullOrEmpty(de) ? ds : de;
            applied = true;
        }
        if (applied)
        {
            if (_activeSlot >= 0) SaveAdvanceDates(_activeSlot);
            UpdateResultBackground(row);
        }
    }

    private void BuildSlotPicker()
    {
        for (int i = 0; i < 10; i++)
            slotPicker.Items.Add(SlotLabel(i));
    }

    private void UpdateSlotPicker()
    {
        _suppressPickerEvent = true;
        for (int i = 0; i < 10; i++)
            slotPicker.Items[i] = SlotLabel(i);
        slotPicker.SelectedIndex = _activeSlot;
        slotPicker.Unfocus();
        UpdateExclCheckbox();
        UpdateTicketCount();
        // Android can deliver Focused asynchronously, after this method returns (e.g. the
        // native view re-gaining focus when the page re-attaches after a Shell navigation) —
        // that's what pops the native Set-picker dialog on top of the highlighted row. Resetting
        // the flag immediately here was too early to catch that late Focused; the handler resets
        // it instead. This fallback clears it if Focused never fires, so a later real picker tap
        // isn't silently ignored. The epoch guards against a fallback from an older call firing
        // after a newer UpdateSlotPicker() call already started a fresh suppression window.
        int epoch = ++_pickerSuppressEpoch;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () =>
        {
            if (_pickerSuppressEpoch == epoch) _suppressPickerEvent = false;
        });
    }

    private void SlotPicker_Focused(object sender, FocusEventArgs e)
    {
        if (_suppressPickerEvent)
            slotPicker.Unfocus();
        _suppressPickerEvent = false;
        _pickerSuppressEpoch++;
    }

    private void SlotPicker_Changed(object sender, EventArgs e)
    {
        if (_suppressPickerEvent) return;
        int slot = slotPicker.SelectedIndex;
        if (slot < 0) return;
        // Turn off OVR when switching sets so it doesn't affect the new set
        _overrideMode = false;
        btnOverride.BackgroundColor = Color.FromArgb("#546E7A");
        advAllPanel.BackgroundColor = Colors.Transparent;
        // Cache current slot before switching. _slotCache is in-memory only (fast re-switch
        // within this page visit) — it must NOT be the only copy, or the outgoing slot's
        // tickets are lost for good if the app process dies before this page is exited
        // normally while that slot is active again. SaveSet() persists them to disk now too.
        if (_activeSlot >= 0)
        {
            _slotCache[_activeSlot] = (GetCurrentEntryString(), string.Join("|", _betTypes), string.Join("|", _drawFilters));
            SaveSet(_activeSlot, refreshPicker: false);
        }
        SaveAdvanceDates(_activeSlot);
        _activeSlot = slot;
        Preferences.Set("d3_active_slot", slot);
        ClearAllEntries();
        _suppressAdvApply = true;
        try
        {
            if (_slotCache.TryGetValue(slot, out var cached))
            {
                _loading = true;
                var vals = cached.entries.Split('|');
                for (int r = 0; r < Rows; r++)
                    for (int c = 0; c < Cols; c++)
                    {
                        int idx = r * Cols + c;
                        _entries[r, c].Text = idx < vals.Length ? vals[idx] : "";
                    }
                LoadBetTypes_FromString(cached.betTypes);
                LoadDrawFilters(cached.drawFilters);
                _loading = false;
                CheckAll();
                LoadAdvanceDates(slot);
                RefreshAdvAllPanel();
                UpdateAllResultBackgrounds();
            }
            else if (SlotHasData(slot))
                FillFromSlot(slot);
        }
        finally { _suppressAdvApply = false; }
        UpdateSlotPicker();
    }

    // ── Highlight a row (called after navigating from ResultsPage) ───────────

    internal void ClearHighlight()
    {
        if (_highlightedView == null) return;
        _highlightedView.BackgroundColor = Colors.White;
        _highlightedView = null;
    }

    private async Task HighlightRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= rowsContainer.Children.Count) return;
        if (rowsContainer.Children[rowIndex] is not View rowView) return;
        // rowsContainer holds Border cards whose Content is a wrapper stack (row Grid +
        // permsLabel) — the row Grid has its own opaque white background, so the highlight
        // must land on that Grid directly, not the wrapper, or the white paints over it.
        View target = rowView;
        if (rowView is Border b && b.Content is Layout wrapper && wrapper.Children.Count > 0
            && wrapper.Children[0] is View innerRow)
            target = innerRow;
        else if (rowView is Border b2 && b2.Content is View inner)
            target = inner;
        _highlightedView = target;
        target.BackgroundColor = Color.FromArgb("#FFF176");
        if (rowsContainer.Parent is ScrollView sv)
            await sv.ScrollToAsync(rowView, ScrollToPosition.MakeVisible, true);
        await Task.Delay(2000);
        target.BackgroundColor = Colors.White;
        _highlightedView = null;
    }

    // ── Highlight rows ───────────────────────────────────────────────────────

    private void HighlightRows(int count)
    {
        for (int r = 0; r < Rows; r++)
        {
            var color = r < count ? Color.FromArgb("#FFF176") : Color.FromArgb("#F5F5F5");
            for (int c = 0; c < Cols; c++)
                _entries[r, c].BackgroundColor = color;
        }
    }

    private void FromEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(fromEntry.Text, out int n)) HighlightRows(n);
        else HighlightRows(0);
    }

    // ── Build 10 input rows (3 boxes each) ───────────────────────────────────

    private void BuildRows()
    {
        for (int r = 0; r < Rows; r++)
        {
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),       // 0: row#
                    new ColumnDefinition(GridLength.Star),       // 1: digit1
                    new ColumnDefinition(GridLength.Star),       // 2: digit2
                    new ColumnDefinition(GridLength.Star),       // 3: digit3
                    new ColumnDefinition(GridLength.Auto),       // 4: perm checkbox
                    new ColumnDefinition(new GridLength(40)),    // 5: bet type
                    new ColumnDefinition(new GridLength(32)),    // 6: draw filter
                    new ColumnDefinition(GridLength.Auto),       // 7: result
                },
                ColumnSpacing = 6,
                BackgroundColor = Colors.White,
                Margin = new Thickness(0),
                Padding = new Thickness(10, 8),
            };

            int rowIdx = r;
            var rowNum = new Label
            {
                Text = $"{r + 1,2}.",
                FontSize = 11,
                TextColor = Color.FromArgb("#1565C0"),
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 18,
            };
            rowNum.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () =>
                {
                    bool ok = await DisplayAlert("Clear Row", $"Clear row {rowIdx + 1}?", "Ok", "Cancel");
                    if (ok) ClearRow(rowIdx);
                })
            });
            Grid.SetColumn(rowNum, 0);
            row.Children.Add(rowNum);

            // Small dot: this row already has saved numbers in it — shown before
            // you type, never auto-clears anything. Tap the row# to clear on purpose.
            var dataDot = new BoxView
            {
                WidthRequest  = 7,
                HeightRequest = 7,
                CornerRadius  = 3.5,
                Color = Color.FromArgb("#F59E0B"),
                IsVisible = false,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions   = LayoutOptions.Start,
                Margin = new Thickness(0, 1, 1, 0),
                InputTransparent = true,
            };
            Grid.SetColumn(dataDot, 0);
            row.Children.Add(dataDot);
            _dataDots[r] = dataDot;

            for (int c = 0; c < Cols; c++)
            {
                var entry = new Entry
                {
                    Keyboard = Keyboard.Numeric,
                    FontSize = 22,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.Black,
                    BackgroundColor = Color.FromArgb("#F5F5F5"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    HeightRequest = 50,
                    MaxLength = 1,
                };
                entry.HandlerChanged += ForceBlackText;

                int row_ = r, col_ = c;
                entry.Focused += (_, _) => { if (!_loading && !_retreating) Dispatcher.Dispatch(() => _entries[row_, col_].Text = ""); _retreating = false; };
                EntryHelper.AttachBackspace(entry, () => RetreatFocus(row_, col_));
                entry.TextChanged += (_, _) =>
                {
                    if (_loading) return;
                    if (!_voiceSettingText && _entries[row_, col_].Text?.Length == 1)
                        AdvanceFocus(row_, col_);
                    SaveEntries();
                    TriggerAutoSaveIndicator();
                    CheckAll();
                    UpdatePermsIfChecked(row_);
                    UpdateTicketCount();
                };

                _entries[r, c] = entry;
                Grid.SetColumn(entry, c + 1);
                row.Children.Add(entry);
            }

            int chkRow = r;
            var permChk = new CheckBox
            {
                Color = Color.FromArgb("#1565C0"),
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(2, 0),
            };
            permChk.CheckedChanged += (_, _) => UpdatePermsIfChecked(chkRow);
            _permChks[r] = permChk;
            Grid.SetColumn(permChk, 4);
            row.Children.Add(permChk);

            var betBtn = new Button
            {
                Text = "Str",
                FontSize = 9,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#1565C0"),
                WidthRequest = 42,
                HeightRequest = 30,
                CornerRadius = 6,
                Padding = new Thickness(0),
                VerticalOptions = LayoutOptions.Center,
            };
            int rowIdx2 = r;
            betBtn.Clicked += (_, _) => CycleBetType(rowIdx2);
            _betTypeBtns[r] = betBtn;
            Grid.SetColumn(betBtn, 5);
            row.Children.Add(betBtn);

            var drawBtn = new Button
            {
                Text = "Both",
                FontSize = 8,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#37474F"),
                WidthRequest = 40,
                HeightRequest = 30,
                CornerRadius = 6,
                Padding = new Thickness(0),
                Margin = new Thickness(4, 0, 0, 0),
                VerticalOptions = LayoutOptions.Center,
            };
            int rowIdx3 = r;
            drawBtn.Clicked += (_, _) => CycleDrawFilter(rowIdx3);
            _drawFilterBtns[r] = drawBtn;
            Grid.SetColumn(drawBtn, 6);
            row.Children.Add(drawBtn);

            var result = new Label
            {
                Text = "+",
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#4B6A8A"),
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap,
            };
            _results[r] = result;
            var resultBorder = new Border
            {
                Content = result,
                Stroke = new SolidColorBrush(Color.FromArgb("#2D4A6A")),
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(4, 2),
                WidthRequest = 52,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
            };
            Grid.SetColumn(resultBorder, 7);
            row.Children.Add(resultBorder);

            int ri = r;
            resultBorder.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => ShowAdvancePlayOverlay(ri))
            });

            var permsLabel = new Label
            {
                IsVisible = false,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1565C0"),
                BackgroundColor = Color.FromArgb("#E3F2FD"),
                Padding = new Thickness(12, 4),
                LineBreakMode = LineBreakMode.WordWrap,
            };
            _permLabels[r] = permsLabel;

            var wrapper = new VerticalStackLayout { Spacing = 0 };
            wrapper.Children.Add(row);
            wrapper.Children.Add(permsLabel);
            var card = new Border
            {
                Content = wrapper,
                BackgroundColor = Colors.White,
                Stroke = new SolidColorBrush(Color.FromArgb("#E5E7EB")),
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(12) },
                Padding = new Thickness(0),
                Margin = new Thickness(0, 4),
            };
            rowsContainer.Children.Add(card);
        }
    }

    private void UpdatePermsIfChecked(int r)
    {
        if (_permChks[r] == null || !_permChks[r].IsChecked)
        {
            _permLabels[r].IsVisible = false;
            return;
        }
        var parts = new string[Cols];
        for (int c = 0; c < Cols; c++) parts[c] = _entries[r, c].Text ?? "";
        if (parts.Any(p => string.IsNullOrEmpty(p)))
        {
            _permLabels[r].IsVisible = false;
            return;
        }
        var perms = GetUniquePerms(parts, "");
        _permLabels[r].Text = string.Join("\n", perms);
        _permLabels[r].IsVisible = true;
    }

    static List<string> GetUniquePerms(string[] parts, string sep)
    {
        var set = new HashSet<string>();
        var arr = (string[])parts.Clone();
        PermuteD3(arr, 0, set, sep);
        return set.ToList();
    }

    static void PermuteD3(string[] arr, int start, HashSet<string> results, string sep)
    {
        if (start == arr.Length - 1) { results.Add(string.Join(sep, arr)); return; }
        for (int i = start; i < arr.Length; i++)
        {
            (arr[start], arr[i]) = (arr[i], arr[start]);
            PermuteD3(arr, start + 1, results, sep);
            (arr[start], arr[i]) = (arr[i], arr[start]);
        }
    }

    private void CycleDrawFilter(int row)
    {
        int idx = Array.IndexOf(DrawCycle, _drawFilters[row]);
        _drawFilters[row] = DrawCycle[(idx + 1) % DrawCycle.Length];
        UpdateDrawFilterBtn(row);
        CheckAll();
    }

    private void UpdateDrawFilterBtn(int row)
    {
        var btn = _drawFilterBtns[row];
        btn.Text = _drawFilters[row] switch { "M" => "Mid", "E" => "Eve", _ => "Both" };
        btn.BackgroundColor = _drawFilters[row] switch
        {
            "M" => Color.FromArgb("#1565C0"),
            "E" => Color.FromArgb("#0D47A1"),
            _   => Color.FromArgb("#37474F"),
        };
    }

    private void CycleBetType(int row)
    {
        int idx = Array.IndexOf(BetCycle, _betTypes[row]);
        _betTypes[row] = BetCycle[(idx + 1) % BetCycle.Length];
        UpdateBetBtn(row);
        SaveEntries();
        CheckAll();
    }

    private void UpdateBetBtn(int row)
    {
        var btn = _betTypeBtns[row];
        btn.Text = _betTypes[row] switch { "B" => "Box", "S&B" => "S+B", _ => "Str" };
        btn.BackgroundColor = _betTypes[row] switch
        {
            "B"   => Color.FromArgb("#00695C"),
            "S&B" => Color.FromArgb("#6A1B9A"),
            _     => Color.FromArgb("#1565C0"),
        };
    }

    private void AllBetPicker_Changed(object sender, EventArgs e)
    {
        if (_suppressPickerEvent) return;
        int idx = allBetPicker.SelectedIndex;
        if (idx < 0) return;
        Preferences.Set("d3_allBetIdx", idx);
        string bt = BetCycle[idx];
        for (int r = 0; r < Rows; r++)
        {
            _betTypes[r] = bt;
            UpdateBetBtn(r);
        }
        if (_activeSlot >= 0)
        {
            SaveSet(_activeSlot);
            _slotCache[_activeSlot] = (GetCurrentEntryString(), string.Join("|", _betTypes), string.Join("|", _drawFilters));
        }
        SaveEntries();
        CheckAll();
    }

    private void AdvanceFocus(int row, int col)
    {
        int nextCol = col + 1;
        int nextRow = row;
        if (nextCol >= Cols) { nextCol = 0; nextRow = row + 1; }
        if (nextRow < Rows)
            Dispatcher.Dispatch(() => _entries[nextRow, nextCol].Focus());
    }

    private void RetreatFocus(int row, int col)
    {
        _retreating = true;
        if (col > 0) EntryHelper.SelectAll(_entries[row, col - 1]);
        else if (row > 0) EntryHelper.SelectAll(_entries[row - 1, Cols - 1]);
    }

    private void ForceBlackText(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is Entry entry &&
            entry.Handler?.PlatformView is Android.Widget.EditText et)
        {
            et.SetTextColor(Android.Graphics.Color.Black);
            et.SetSelectAllOnFocus(true);
        }
#endif
    }

    // ── Load draws ───────────────────────────────────────────────────────────

    private async Task LoadAllDraws()
    {
        if (_drawsLoaded)
        {
            bool hasTodayDraw = _draws.Any(d =>
                DateTime.TryParse(d.DateLabel, out var dt) && dt.Date == DateTime.Today);
            if (hasTodayDraw)
            {
                await Task.Delay(400);
                spinner.IsVisible = false;
                spinner.IsRunning = false;
                loadingOverlay.IsVisible = false;
                return;
            }
            _drawsLoaded = false;
            _draws.Clear();
        }

        spinner.IsVisible = true;
        spinner.IsRunning = true;
        loadingOverlay.IsVisible = true;
        lblDrawDate.Text = "Fetching Daily 3 draws...";

        var raw = await GetDataEntry.LoadD3CsvDraws();

        spinner.IsVisible = false;
        spinner.IsRunning = false;
        loadingOverlay.IsVisible = false;

        if (raw.Count == 0)
        {
            string errMsg = string.IsNullOrEmpty(GetDataEntry.LastError)
                ? "Daily3: Could not load draw history"
                : $"Daily3: {GetDataEntry.LastError}";
            lblDrawDate.Text = errMsg;
            lblStatus.Text = errMsg;
            return;
        }

        // Deduplicate by DrawNumber — prevents duplicate CSV rows (from old buggy append) from
        // creating phantom Evening entries when a date only has one real draw
        var deduped = raw
            .Where(d => d.DrawNumber > 0)
            .GroupBy(d => d.DrawNumber)
            .Select(g => g.First())
            .ToList();

        // Group by date: each date may have Midday and/or Evening
        var grouped = deduped
            .Select(d => (Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                          d.DrawDate, d.DrawNumber, d.Numbers, d.DrawTime))
            .Where(d => d.Date != DateTime.MinValue)
            .GroupBy(d => d.Date.Date)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var midday  = g.FirstOrDefault(x => x.DrawTime?.ToLower().Contains("midday") == true
                                                  || x.DrawTime?.ToLower().Contains("mid") == true);
                var evening = g.FirstOrDefault(x => x.DrawTime?.ToLower().Contains("evening") == true
                                                  || x.DrawTime?.ToLower().Contains("eve") == true);

                if (midday.Numbers == null && evening.Numbers == null)
                {
                    // No DrawTime info — use DrawNumber order: lower = midday, higher = evening
                    var ordered = g.OrderBy(x => x.DrawNumber).ToList();
                    midday  = ordered.Count >= 1 ? ordered[0] : default;
                    evening = ordered.Count >= 2 ? ordered[1] : default;
                }
                else if (midday.Numbers == null && evening.Numbers != null)
                {
                    var other = g.FirstOrDefault(x => x.DrawTime != evening.DrawTime);
                    if (other.Numbers != null) midday = other;
                }
                var dateLabel = g.Key.ToString("ddd MMM d, yyyy");
                return (DateLabel: dateLabel,
                        Midday:         midday.Numbers  ?? Array.Empty<int>(),
                        MiddayDrawNum:  midday.Numbers  != null ? midday.DrawNumber  : 0,
                        Evening:        evening.Numbers ?? Array.Empty<int>(),
                        EveningDrawNum: evening.Numbers != null ? evening.DrawNumber : 0);
            })
            .ToList();

        _draws = grouped;
        _drawsLoaded = true;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var dates = _draws
                .Select(d => DateTime.TryParse(d.DateLabel, out var dt) ? dt.Date : DateTime.MinValue)
                .Where(d => d != DateTime.MinValue)
                .ToList();
            if (dates.Count == 0) return;
            drawDatePicker.MinimumDate = dates.Last();
            drawDatePicker.MaximumDate = DateTime.Today;
            var targetDate = DateTime.Today;
            if (drawDatePicker.Date == targetDate)
                ShowDrawForDate(targetDate);
            else
                drawDatePicker.Date = targetDate;
        });
    }

    private void ShowDrawForDate(DateTime date)
    {
        var match = _draws.FirstOrDefault(d =>
            DateTime.TryParse(d.DateLabel, out var dt) && dt.Date <= date.Date);

        if (match.DateLabel == null) return;

        _winMidday  = match.Midday.Length  > 0 ? match.Midday  : null;
        _winEvening = match.Evening.Length > 0 ? match.Evening : null;

        lblDrawDate.Text = match.DateLabel;

        lblMiddayDrawNum.Text      = match.MiddayDrawNum  > 0 ? $"Draw #{match.MiddayDrawNum}"  : "";
        lblMiddayDrawNum.IsVisible = match.MiddayDrawNum  > 0;

        // If no evening draw for this date yet, fall back to the most recent available
        if (_winEvening == null)
        {
            var eveEntry = _draws.FirstOrDefault(d =>
                d.Evening.Length > 0 &&
                DateTime.TryParse(d.DateLabel, out var edt) && edt.Date <= date.Date);
            if (eveEntry.DateLabel != null)
            {
                _winEvening = eveEntry.Evening;
                string eveNumStr = eveEntry.EveningDrawNum > 0 ? $"Draw #{eveEntry.EveningDrawNum}" : "";
                // Show date if it's from a different day than selected
                bool sameDay = DateTime.TryParse(eveEntry.DateLabel, out var eveDate) && eveDate.Date == date.Date;
                lblEveningDrawNum.Text = sameDay ? eveNumStr
                    : (eveNumStr.Length > 0 ? $"{eveNumStr}  ({eveEntry.DateLabel})" : eveEntry.DateLabel);
                lblEveningDrawNum.IsVisible = true;
            }
            else
            {
                lblEveningDrawNum.Text = "";
                lblEveningDrawNum.IsVisible = false;
            }
        }
        else
        {
            lblEveningDrawNum.Text      = match.EveningDrawNum > 0 ? $"Draw #{match.EveningDrawNum}" : "";
            lblEveningDrawNum.IsVisible = match.EveningDrawNum > 0;
        }

        for (int i = 0; i < 3; i++)
        {
            _wLabels[i].Text = _winMidday  != null ? _winMidday[i].ToString()  : "?";
            _eLabels[i].Text = _winEvening != null ? _winEvening[i].ToString() : "-";
        }

        CheckAll();
    }

    private void DrawDatePicker_DateSelected(object sender, DateChangedEventArgs e) =>
        ShowDrawForDate(e.NewDate ?? DateTime.Today);

    // ── Check ────────────────────────────────────────────────────────────────

    private void CheckAll()
    {
        for (int r = 0; r < Rows; r++)
        {
            var userNums = new int[3];
            bool allFilled = true;
            for (int c = 0; c < Cols; c++)
            {
                if (!int.TryParse(_entries[r, c].Text, out userNums[c]))
                    allFilled = false;
            }

            if (!allFilled)
            {
                for (int c = 0; c < Cols; c++)
                    _entries[r, c].BackgroundColor = Color.FromArgb("#F5F5F5");
                _results[r].Text = "";
                continue;
            }

            string bt = _betTypes[r];
            string df = _drawFilters[r]; // B, M, or E
            string? winMid = null, winEve = null;
            if (df != "E") winMid = CheckWin(userNums, _winMidday, bt);
            if (df != "M") winEve = CheckWin(userNums, _winEvening, bt);

            string? win;
            string? winLabel;
            if (winMid != null && winEve != null)
            {
                win = winMid;
                winLabel = $"M+E\n{winMid}";
            }
            else if (winMid != null)
            {
                win = winMid;
                winLabel = df == "B" ? $"Mid:\n{winMid}" : winMid;
            }
            else if (winEve != null)
            {
                win = winEve;
                winLabel = df == "B" ? $"Eve:\n{winEve}" : winEve;
            }
            else
            {
                win = null;
                winLabel = null;
            }

            bool isStrWin = win == "Straight" || win == "S&B!";
            var bg = win == null  ? Color.FromArgb("#FFCDD2")
                   : isStrWin     ? Color.FromArgb("#F9A825")
                                  : Color.FromArgb("#FFF9C4");
            for (int c = 0; c < Cols; c++)
                _entries[r, c].BackgroundColor = bg;

            if (win == null)
            {
                _results[r].Text = "✗";
                _results[r].TextColor = Color.FromArgb("#C62828");
            }
            else
            {
                _results[r].Text = winLabel!;
                _results[r].TextColor = isStrWin
                    ? Color.FromArgb("#1B5E20")
                    : Color.FromArgb("#E65100");
            }

        }
        UpdateAllResultBackgrounds();
    }


    static string? CheckWin(int[] user, int[]? win, string bt)
    {
        if (win == null || win.Length < 3) return null;
        bool isStr = user.SequenceEqual(win);
        bool isBox = !isStr && user.OrderBy(x => x).SequenceEqual(win.OrderBy(x => x));

        return bt switch
        {
            "S"   => isStr ? "Straight" : null,
            "B"   => (isStr || isBox) ? "Box" : null,
            "S&B" => isStr ? "S&B!" : isBox ? "Box" : null,
            _     => null,
        };
    }

    internal void ClearForArchive()
    {
        _slotCache.Clear();
        _activeSlot = -1;
        _loading = true;
        ClearAllEntries();
        _loading = false;
        UpdateSlotPicker();
    }

    internal void InvalidateAfterPurge()
    {
        _slotCache.Clear();
        _advDatesLoaded = false;
    }

    private void BtnCheck_Clicked(object sender, EventArgs e) => CheckAll();

    private void BtnClearAll_Clicked(object sender, EventArgs e)
    {
        ClearAllEntries();
        SaveEntries();
    }

    private async void BtnRefresh_Clicked(object sender, EventArgs e)
    {
        _drawsLoaded = false;
        _draws.Clear();
        lblStatus.Text = "Fetching latest draws...";
        await GetDataEntry.UpdateD3CsvAsync();  // pull new draws from API into CSV first
        await LoadAllDraws();
    }

    private async void StatusBar_Tapped(object sender, TappedEventArgs e)
    {
        string log = await Logger.ReadLogAsync();
        await Clipboard.Default.SetTextAsync(log);
        var orig = lblStatus.Text;
        lblStatus.Text = "Log copied to clipboard";
        await Task.Delay(1500);
        lblStatus.Text = orig;
    }

    private void BtnOverride_Clicked(object sender, EventArgs e)
    {
        _overrideMode = !_overrideMode;
        btnOverride.BackgroundColor = _overrideMode
            ? Color.FromArgb("#E65100")
            : Color.FromArgb("#546E7A");
        advAllPanel.BackgroundColor = _overrideMode
            ? Color.FromArgb("#FFF3E0")
            : Colors.Transparent;
    }

    private async void BtnQuickPick_Clicked(object sender, EventArgs e)
    {
        string? choice = await DisplayActionSheet("Quick Pick — How many empty rows?", "Cancel", null,
            "1", "2", "3", "5", "10", "All");
        if (choice == null || choice == "Cancel") return;
        int max = choice == "All" ? Rows : int.TryParse(choice, out int n) ? n : 1;

        var rng = Random.Shared;
        int filled = 0;
        for (int r = 0; r < Rows && filled < max; r++)
        {
            bool empty = true;
            for (int c = 0; c < Cols; c++)
                if (!string.IsNullOrEmpty(_entries[r, c].Text)) { empty = false; break; }
            if (!empty) continue;

            for (int c = 0; c < Cols; c++)
                _entries[r, c].Text = rng.Next(0, 10).ToString();
            filled++;
        }

        if (filled == 0)
            lblStatus.Text = "No empty rows to fill";
        else
        {
            CheckAll();
            SaveEntries();
            lblStatus.Text = $"Quick Pick: filled {filled} row{(filled == 1 ? "" : "s")}";
        }
    }

    private async void BtnClearSets_Clicked(object sender, EventArgs e)
    {
        string setLabel = _activeSlot >= 0 ? $"Set {_activeSlot + 1}" : "Current Set";
        string? choice = await DisplayActionSheet("Clear Sets", "Cancel", null,
            $"Clear {setLabel} only",
            "Clear ALL 10 sets");
        if (choice == null || choice == "Cancel") return;

        int skipped = 0;
        if (choice.StartsWith("Clear ALL"))
        {
            bool confirm = await DisplayAlert("Clear All Sets", "Remove all 10 saved sets?", "Yes", "Cancel");
            if (!confirm) return;
            bool activePartial = false;
            for (int i = 0; i < 10; i++)
            {
                if (SlotHasFutureAdvDate(i))
                {
                    skipped++;
                    if (i == _activeSlot) activePartial = true;
                    else PartialClearSlot(i);
                }
                else
                {
                    Preferences.Remove(SetKey(i));
                    Preferences.Remove(BetKey(i));
                    Preferences.Remove(AdvDatesKey(i));
                }
            }
            _slotCache.Clear();
            if (activePartial)
            {
                var now   = DateTime.Now;
                var today = now.Date;
                for (int r = 0; r < Rows; r++)
                {
                    var refDate = _playEnd[r] ?? _playStart[r];
                    if (!refDate.HasValue || refDate.Value.Date < today || (refDate.Value.Date == today && now.TimeOfDay >= (_drawFilters[r] == "M" ? TimeSpan.FromHours(13) : TimeSpan.FromHours(20)))) ClearRow(r);
                }
                if (_activeSlot >= 0) { SaveSet(_activeSlot); SaveAdvanceDates(_activeSlot); }
            }
            else if (_activeSlot < 0 || !SlotHasFutureAdvDate(_activeSlot))
                ClearAllEntries();
        }
        else
        {
            if (_activeSlot < 0) return;
            Preferences.Remove(SetKey(_activeSlot));
            Preferences.Remove(BetKey(_activeSlot));
            ClearAllEntries();
        }

        UpdateSlotPicker();
        if (sender is Button btn)
        {
            var orig = btn.Text; var origColor = btn.BackgroundColor;
            btn.Text = skipped > 0 ? $"Cleared ({skipped} kept)" : "Cleared"; btn.BackgroundColor = Color.FromArgb("#1B5E20");
            await Task.Delay(1200);
            btn.Text = orig; btn.BackgroundColor = origColor;
        }
    }

    private void BtnVoice_Clicked(object sender, EventArgs e)
    {
        if (!Services.VoiceNumberService.IsAvailable) { lblStatus.Text = "Speech recognition not available"; return; }
        if (_voiceOn) StopVoice(); else StartVoice();
    }

    void StartVoice()
    {
        _voiceRow = 0; _voiceCol = 0;
        VoiceSkipFilled();
        if (_voiceRow >= Rows) { lblStatus.Text = "No empty cells"; return; }
        _voiceOn = true;
        btnVoice.BackgroundColor = Colors.Red;
        SetVoiceTarget();
        Services.VoiceNumberService.StatusUpdate += OnVoiceStatus;
        Services.VoiceNumberService.StartContinuous(OnVoiceNumbers);
    }

    void StopVoice()
    {
        _voiceOn = false;
        ClearVoiceTarget();
        Services.VoiceNumberService.StatusUpdate -= OnVoiceStatus;
        Services.VoiceNumberService.Stop();
        btnVoice.BackgroundColor = Color.FromArgb("#0277BD");
        lblStatus.Text = "Mic off";
    }

    void SetVoiceTarget()
    {
        if (_voiceTarget != null) _voiceTarget.BackgroundColor = _voiceTargetOldColor;
        if (_voiceRow < Rows)
        {
            _voiceTarget = _entries[_voiceRow, _voiceCol];
            _voiceTargetOldColor = _voiceTarget.BackgroundColor;
            _voiceTarget.BackgroundColor = Color.FromArgb("#A5D6A7");
        }
    }

    void ClearVoiceTarget()
    {
        if (_voiceTarget != null) _voiceTarget.BackgroundColor = _voiceTargetOldColor;
        _voiceTarget = null;
    }

    void OnVoiceStatus(string msg) => MainThread.BeginInvokeOnMainThread(() => lblStatus.Text = msg);

    void OnVoiceNumbers(List<int> nums)
    {
        if (!_voiceOn) return;
        foreach (int n in nums)
        {
            if (_voiceRow >= Rows) { StopVoice(); return; }
            if (n >= 0 && n <= 9)
            {
                _voiceSettingText = true;
                _entries[_voiceRow, _voiceCol].Text = n.ToString();
                _voiceSettingText = false;
                _voiceCol++;
                if (_voiceCol >= Cols) { _voiceCol = 0; _voiceRow++; }
                VoiceSkipFilled();
            }
        }
        CheckAll(); SaveEntries();
        SetVoiceTarget(); // after CheckAll so green highlight isn't wiped
        if (_voiceOn && _voiceRow < Rows)
            lblStatus.Text = $"🔴 Listening | row {_voiceRow + 1} col {_voiceCol + 1}";
    }

    void VoiceSkipFilled()
    {
        while (_voiceRow < Rows && !string.IsNullOrEmpty(_entries[_voiceRow, _voiceCol].Text))
        {
            _voiceCol++;
            if (_voiceCol >= Cols) { _voiceCol = 0; _voiceRow++; }
        }
    }

    private async void BtnSave_Clicked(object sender, EventArgs e)
    {
        string? choice = await DisplayActionSheet("Save", "Cancel", null, "Save to Slot", "Save to MyFavorite");
        if (choice == null || choice == "Cancel") return;
        if (choice == "Save to MyFavorite")
        {
            SaveEntries();
            await MyFavoritePage.SaveCurrentToMyFavoriteAsync(
                "Daily 3", "d3_set_", _activeSlot < 0 ? 0 : _activeSlot,
                GetCurrentEntryString(), string.Join("|", _betTypes));
            return;
        }
        // Cache current slot then flush all cached slots
        if (_activeSlot >= 0)
            _slotCache[_activeSlot] = (GetCurrentEntryString(), string.Join("|", _betTypes), string.Join("|", _drawFilters));
        SaveEntries();
        foreach (var (slot, (entries, betTypes, drawFilters)) in _slotCache)
        {
            bool isEmpty = entries.Replace("|", "").Trim().Length == 0;
            if (isEmpty)
            {
                Preferences.Remove(SetKey(slot));
                Preferences.Remove(BetKey(slot));
                Preferences.Remove(DfKey(slot));
            }
            else
            {
                Preferences.Set(SetKey(slot), entries);
                Preferences.Set(BetKey(slot), betTypes);
                Preferences.Set(DfKey(slot), drawFilters);
            }
        }
        UpdateSlotPicker();
        UpdateTicketCount();
        int savedCount = _slotCache.Count(kv => kv.Value.entries.Replace("|", "").Trim().Length > 0);
        if (sender is Button btn)
        {
            var orig = btn.Text; var origColor = btn.BackgroundColor;
            btn.Text = savedCount > 1 ? $"ALL {savedCount} ✓" : _activeSlot >= 0 ? $"SET {_activeSlot + 1} ✓" : "SAVED";
            btn.BackgroundColor = Color.FromArgb("#1B5E20");
            await Task.Delay(1200);
            btn.Text = orig; btn.BackgroundColor = origColor;
        }
    }

    // ── Advance Play Dates ────────────────────────────────────────────────────

    private void BuildAdvancePlayOverlay()
    {
        _advStartPicker = new DatePicker
        {
            Format = "MMM d, yyyy",
            FontSize = 14,
            Date = DateTime.Today,
            MinimumDate = new DateTime(2020, 1, 1),
            MaximumDate = new DateTime(2035, 12, 31),
            TextColor = Colors.White,
        };
        _advEndPicker = new DatePicker
        {
            Format = "MMM d, yyyy",
            FontSize = 14,
            Date = DateTime.Today,
            MinimumDate = new DateTime(2020, 1, 1),
            MaximumDate = new DateTime(2035, 12, 31),
            TextColor = Colors.White,
        };
        _advStartPicker.DateSelected += (_, e) =>
        {
            if (_advEndPicker!.Date < e.NewDate)
                _advEndPicker.Date = e.NewDate;
        };
        _advDrawStartEntry = new Entry
        {
            Placeholder = "Start #",
            Keyboard = Keyboard.Numeric,
            PlaceholderColor = Color.FromArgb("#6B7280"),
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#2D3748"),
            FontSize = 14,
        };
        _advDrawEndEntry = new Entry
        {
            Placeholder = "End # (optional)",
            Keyboard = Keyboard.Numeric,
            PlaceholderColor = Color.FromArgb("#6B7280"),
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#2D3748"),
            FontSize = 14,
        };
        var drawGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            ColumnSpacing = 6,
        };
        drawGrid.Add(_advDrawStartEntry, 0, 0);
        drawGrid.Add(new Label { Text = "—", TextColor = Color.FromArgb("#8B9DC3"), VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center, FontSize = 16 }, 1, 0);
        drawGrid.Add(_advDrawEndEntry, 2, 0);

        _advWarnLabel = new Label
        {
            FontSize = 11,
            TextColor = Color.FromArgb("#F59E0B"),
            IsVisible = false,
        };

        void UpdateAdvWarning()
        {
            string df = _advRow >= 0 && _advRow < _drawFilters.Length ? _drawFilters[_advRow] : "B";
            string w = SpendingTracker.CheckDrawRangeWarning(
                "D3", _advStartPicker!.Date ?? DateTime.Today, _advEndPicker!.Date ?? DateTime.Today,
                _advDrawStartEntry!.Text ?? "", _advDrawEndEntry!.Text ?? "", df,
                currentDrawNumber: DrawNumberService.GetNextDraw("Daily 3")) ?? "";
            _advWarnLabel!.Text = w;
            _advWarnLabel.IsVisible = !string.IsNullOrEmpty(w);
        }
        _advDrawStartEntry.TextChanged += (_, _) => UpdateAdvWarning();
        _advDrawEndEntry.TextChanged   += (_, _) => UpdateAdvWarning();
        _advStartPicker.DateSelected   += (_, _) => UpdateAdvWarning();
        _advEndPicker.DateSelected     += (_, _) => UpdateAdvWarning();

        var btnClear  = new Button { Text = "Clear",  BackgroundColor = Color.FromArgb("#4B5563"), TextColor = Colors.White, CornerRadius = 10, HeightRequest = 42, FontSize = 13 };
        var btnCancel = new Button { Text = "Cancel", BackgroundColor = Color.FromArgb("#1E293B"), TextColor = Colors.White, CornerRadius = 10, HeightRequest = 42, FontSize = 13 };
        var btnOk     = new Button { Text = "OK",     BackgroundColor = Color.FromArgb("#2563EB"), TextColor = Colors.White, CornerRadius = 10, HeightRequest = 42, FontSize = 13, FontAttributes = FontAttributes.Bold };

        btnClear.Clicked += (_, _) =>
        {
            if (_advRow < 0) return;
            _playStart[_advRow] = null;
            _playEnd[_advRow]   = null;
            _drawStart[_advRow] = "";
            _drawEnd[_advRow]   = "";
            UpdateResultBackground(_advRow);
            SaveAdvanceDates(_activeSlot);
            _advOverlay!.IsVisible = false;
        };
        btnCancel.Clicked += (_, _) => _advOverlay!.IsVisible = false;
        btnOk.Clicked += async (_, _) =>
        {
            if (_advRow < 0) return;
            string ds = (_advDrawStartEntry!.Text ?? "").Trim();
            string de = (_advDrawEndEntry!.Text ?? "").Trim();
            string df = _advRow < _drawFilters.Length ? _drawFilters[_advRow] : "B";
            string? warn = SpendingTracker.CheckDrawRangeWarning("D3", _advStartPicker!.Date ?? DateTime.Today, _advEndPicker!.Date ?? DateTime.Today, ds, de, df,
                currentDrawNumber: DrawNumberService.GetNextDraw("Daily 3"));
            if (warn != null)
            {
                bool proceed = await DisplayAlert("Check Draw # Range", warn, "Save Anyway", "Fix It");
                if (!proceed)
                {
                    int current = DrawNumberService.GetNextDraw("Daily 3");
                    if (current <= 0) current = await DrawNumberService.EnsureNextDrawAsync("Daily 3");
                    if (current > 0)
                    {
                        ds = current.ToString();
                        de = current.ToString();
                        _advDrawStartEntry.Text = ds;
                        _advDrawEndEntry.Text   = de;
                    }
                    UpdateAdvWarning();
                    return;
                }
            }
            _playStart[_advRow] = _advStartPicker!.Date;
            _playEnd[_advRow]   = _advEndPicker!.Date;
            _drawStart[_advRow] = ds;
            _drawEnd[_advRow]   = de;
            UpdateResultBackground(_advRow);
            SaveAdvanceDates(_activeSlot);
            ResultsPageCls.ClearCache(); ResultsPage.NeedsRefresh = true;
            _advOverlay!.IsVisible = false;
        };

        var btnRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8,
        };
        Grid.SetColumn(btnClear,  0); btnRow.Children.Add(btnClear);
        Grid.SetColumn(btnCancel, 1); btnRow.Children.Add(btnCancel);
        Grid.SetColumn(btnOk,     2); btnRow.Children.Add(btnOk);

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(16) },
            Padding = new Thickness(20, 18),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 310,
            MaximumHeightRequest = 500,
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Spacing = 12,
                    Children =
                    {
                        new Label { Text = "Advance Play Dates", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center },
                        new Label { Text = "Play From", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                        _advStartPicker,
                        new Label { Text = "Play To", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                        _advEndPicker,
                        new Label { Text = "Draw # (optional)", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                        drawGrid,
                        _advWarnLabel,
                        btnRow,
                    }
                }
            }
        };

        _advOverlay = new Grid { BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false };
        _advOverlay.Children.Add(card);

        var rootGrid = (Grid)Content;
        Grid.SetRow(_advOverlay, 0);
        Grid.SetRowSpan(_advOverlay, 99);
        rootGrid.Children.Add(_advOverlay);
    }



    private async void ShowAdvancePlayOverlay(int row)
    {
        _advRow = row;
        _advStartPicker!.Date = _playStart[row] ?? DateTime.Today;
        var defEnd = _playEnd[row];
        if (!defEnd.HasValue)
            defEnd = (_playStart[row] ?? DateTime.Today);
        _advEndPicker!.Date = defEnd.Value;
        _advDrawStartEntry!.Text = _drawStart[row] ?? "";
        _advDrawEndEntry!.Text   = _drawEnd[row] ?? "";
        _advOverlay!.IsVisible = true;
        if (string.IsNullOrEmpty(_advDrawStartEntry.Text) && string.IsNullOrEmpty(_advDrawEndEntry.Text))
        {
            int current = DrawNumberService.GetNextDraw("Daily 3");
            if (current <= 0) current = await DrawNumberService.EnsureNextDrawAsync("Daily 3");
            if (current > 0 && _advRow == row
                && string.IsNullOrEmpty(_advDrawStartEntry.Text) && string.IsNullOrEmpty(_advDrawEndEntry.Text))
            {
                _advDrawStartEntry.Text = current.ToString();
                _advDrawEndEntry.Text   = current.ToString();
            }
        }
    }

    private void UpdateResultBackground(int r)
    {
        bool hasAdv = _playStart[r].HasValue || _playEnd[r].HasValue
                   || !string.IsNullOrEmpty(_drawStart[r]);
        bool hasNums = false;
        for (int c = 0; c < Cols; c++)
            if (!string.IsNullOrEmpty(_entries[r, c].Text)) { hasNums = true; break; }
        _results[r].BackgroundColor = (hasAdv && hasNums) ? Color.FromArgb("#1A3A8A") : Colors.Transparent;
        bool showingResult = !string.IsNullOrEmpty(_results[r].Text) && _results[r].Text != "+";
        if (!showingResult)
        {
            _results[r].Text = "+";
            _results[r].TextColor = (hasAdv && hasNums) ? Colors.White : Color.FromArgb("#4B6A8A");
        }
    }

    private void UpdateAllResultBackgrounds()
    {
        for (int r = 0; r < Rows; r++) UpdateResultBackground(r);
    }

    private string AdvDatesKey(int slot) => $"d3_adv_{slot}";
    private void PartialClearSlot(int slot)
    {
        string raw = Preferences.Get(SetKey(slot), "");
        if (string.IsNullOrEmpty(raw)) return;
        var vals = raw.Split('|');
        string advRaw = Preferences.Get(AdvDatesKey(slot), "");
        var advParts = string.IsNullOrEmpty(advRaw) ? new string[Rows] : advRaw.Split('|');
        if (advParts.Length < Rows) Array.Resize(ref advParts, Rows);
        var now   = DateTime.Now;
        var today = now.Date;
        string dfRaw = Preferences.Get(DfKey(slot), "");
        var dfParts = string.IsNullOrEmpty(dfRaw) ? new string[Rows] : dfRaw.Split('|');
        for (int r = 0; r < Rows; r++)
        {
            bool keep = false;
            if (r < advParts.Length && advParts[r] != null)
            {
                var pair = advParts[r].Split('~');
                if (pair.Length >= 2)
                {
                    DateTime? end = null, start = null;
                    if (DateTime.TryParseExact(pair[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var ed)) end = ed;
                    if (DateTime.TryParseExact(pair[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var sd)) start = sd;
                    var refDate = end ?? start;
                    string df = r < dfParts.Length ? (dfParts[r] ?? "B") : "B";
                    TimeSpan cutoff = df == "M" ? TimeSpan.FromHours(13) : TimeSpan.FromHours(20);
                    keep = refDate.HasValue && (refDate.Value.Date > today || (refDate.Value.Date == today && now.TimeOfDay < cutoff));
                }
            }
            if (!keep)
            {
                for (int c = 0; c < Cols; c++)
                    if (r * Cols + c < vals.Length) vals[r * Cols + c] = "";
                if (r < advParts.Length) advParts[r] = "~";
            }
        }
        string newData = string.Join("|", vals);
        if (newData.Replace("|", "").Trim().Length == 0)
        { Preferences.Remove(SetKey(slot)); Preferences.Remove(AdvDatesKey(slot)); }
        else
        { Preferences.Set(SetKey(slot), newData); Preferences.Set(AdvDatesKey(slot), string.Join("|", advParts)); }
    }
    private bool SlotHasFutureAdvDate(int slot)
    {
        var now   = DateTime.Now;
        var today = now.Date;
        if (slot == _activeSlot)
        {
            for (int r = 0; r < Rows; r++)
            {
                var refDate = _playEnd[r] ?? _playStart[r];
                TimeSpan cutoff = _drawFilters[r] == "M" ? TimeSpan.FromHours(13) : TimeSpan.FromHours(20);
                if (refDate.HasValue && (refDate.Value.Date > today || (refDate.Value.Date == today && now.TimeOfDay < cutoff))) return true;
            }
            return false;
        }
        if (string.IsNullOrEmpty(Preferences.Get(SetKey(slot), ""))) return false;
        string raw = Preferences.Get(AdvDatesKey(slot), "");
        if (string.IsNullOrEmpty(raw)) return false;
        string dfRaw2 = Preferences.Get(DfKey(slot), "");
        var dfParts2 = string.IsNullOrEmpty(dfRaw2) ? new string[0] : dfRaw2.Split('|');
        int idx2 = 0;
        foreach (var part in raw.Split('|'))
        {
            var pair = part.Split('~');
            if (pair.Length < 2) { idx2++; continue; }
            DateTime? end = null, start = null;
            if (DateTime.TryParseExact(pair[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var ed)) end = ed;
            if (DateTime.TryParseExact(pair[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var sd)) start = sd;
            var refDate = end ?? start;
            string df2 = idx2 < dfParts2.Length ? (dfParts2[idx2] ?? "B") : "B";
            TimeSpan cutoff2 = df2 == "M" ? TimeSpan.FromHours(13) : TimeSpan.FromHours(20);
            if (refDate.HasValue && (refDate.Value.Date > today || (refDate.Value.Date == today && now.TimeOfDay < cutoff2))) return true;
            idx2++;
        }
        return false;
    }

    private void SaveAdvanceDates(int slot)
    {
        if (slot < 0) return;
        if (!_advDatesLoaded) return; // never write before we've loaded — would blank out stored dates
        var parts = new string[Rows];
        for (int r = 0; r < Rows; r++)
        {
            string s = _playStart[r].HasValue ? _playStart[r]!.Value.ToString("yyyyMMdd") : "";
            string e = _playEnd[r].HasValue   ? _playEnd[r]!.Value.ToString("yyyyMMdd")   : "";
            parts[r] = $"{s}~{e}~{_drawStart[r] ?? ""}~{_drawEnd[r] ?? ""}";
        }
        Preferences.Set(AdvDatesKey(slot), string.Join("|", parts));
    }

    public void FlushAdvanceDates()
    {
        if (_activeSlot >= 0) SaveAdvanceDates(_activeSlot);
    }

    private async void RefreshAdvAllPanel()
    {
        advAllFromPicker.Date = DateTime.Today;
        advAllToPicker.Date   = DateTime.Today;
        int next = await DrawNumberService.EnsureNextDrawAsync("Daily 3");
        if (next > 0) { entAdvAllStart.Text = next.ToString(); entAdvAllEnd.Text = next.ToString(); }
    }

    private void LoadAdvanceDates(int slot)
    {
        _advDatesLoaded = false;
        Array.Clear(_playStart, 0, Rows);
        Array.Clear(_playEnd,   0, Rows);
        Array.Clear(_drawStart, 0, Rows);
        Array.Clear(_drawEnd,   0, Rows);
        if (slot >= 0)
        {
            string raw = Preferences.Get(AdvDatesKey(slot), "");
            if (!string.IsNullOrEmpty(raw))
            {
                var parts = raw.Split('|');
                for (int r = 0; r < Rows && r < parts.Length; r++)
                {
                    var pair = parts[r].Split('~');
                    if (pair.Length >= 2)
                    {
                        if (DateTime.TryParseExact(pair[0], "yyyyMMdd", null,
                            System.Globalization.DateTimeStyles.None, out var sd))
                            _playStart[r] = sd;
                        if (DateTime.TryParseExact(pair[1], "yyyyMMdd", null,
                            System.Globalization.DateTimeStyles.None, out var ed))
                            _playEnd[r] = ed;
                    }
                    _drawStart[r] = pair.Length > 2 ? pair[2] : "";
                    _drawEnd[r]   = pair.Length > 3 ? pair[3] : "";
                }
            }
        }
        _advDatesLoaded = true; // safe to save from this point forward
    }

}
