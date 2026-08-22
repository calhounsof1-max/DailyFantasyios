using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class WinnerPage : ContentPage
{
    const int Rows = 10;
    const int Cols = 5;

    readonly Border[] _wBorders;
    readonly Label[]  _wLabels;

    // [row, col] entries and per-row result label
    readonly Entry[,]  _entries     = new Entry[Rows, Cols];
    readonly Label[]   _results     = new Label[Rows];
    readonly Button[]  _fpBtns      = new Button[Rows];   // unused placeholders
    readonly Border[]  _fpBorders   = new Border[Rows];  // Free Play toggle borders
    readonly bool[]    _freePlay    = new bool[Rows];    // per-row free play flag
    int _activeSlot = -1;
    bool _suppressPickerEvent = false;
    int  _pickerSuppressEpoch = 0;
    bool _suppressExcl = false;
    readonly Dictionary<int, string> _slotCache = new();
    View? _highlightedView;
    CancellationTokenSource? _autoSaveCts;

    DateTime?[] _playStart = new DateTime?[Rows];
    DateTime?[] _playEnd   = new DateTime?[Rows];
    string[]    _logSnapshot = new string[Rows]; // numbers per row at OnAppearing, for change detection
    string[]    _drawStart = new string[Rows];
    string[]    _drawEnd   = new string[Rows];
    Grid?       _advOverlay;
    DatePicker? _advStartPicker;
    DatePicker? _advEndPicker;
    Label?      _advWarnLabel;
    Entry?      _advDrawStartEntry;
    Entry?      _advDrawEndEntry;
    int         _advRow = -1;

    int[] _winningNumbers = Array.Empty<int>();
    List<(DateTime Date, string Label, int DrawNumber, int[] Numbers)> _allDraws = new();
    bool _drawsLoaded = false;
    bool _isPanning = false;
    bool _voiceOn = false;
    bool _voiceSettingText = false;
    bool _overrideMode = false;
    bool _suppressAdvApply = false;  // blocks ApplyAdvanceToRowIfActive during slot loads
    bool _advDatesLoaded   = false;  // prevents SaveAdvanceDates from overwriting before LoadAdvanceDates runs
    int  _voiceRow = 0, _voiceCol = 0;
    Entry? _voiceTarget = null;
    Color _voiceTargetOldColor = Colors.White;

    internal static string ComingFrom { get; set; } = "main";

    public WinnerPage()
    {
        InitializeComponent();

        _wBorders = new[] { W1, W2, W3, W4, W5 };
        _wLabels  = new[] { lblW1, lblW2, lblW3, lblW4, lblW5 };

        BuildRows();
        BuildSlotPicker();
        BuildAdvancePlayOverlay();
    }

    double _panLeft;  // most-negative TotalX this gesture
    double _panRight; // most-positive TotalX this gesture

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
                if (_panLeft < -40) // left → go to SL
                {
                    _isPanning = true;
                    SuperLottoPage.ComingFrom = "f5";
                    AppShell.SuperLottoPageInstance.PrePosition(true);
                    await Shell.Current.GoToAsync(nameof(SuperLottoPage), false);
                    _isPanning = false;
                }
                else if (_panRight > 40) // right → go back to MainPage
                {
                    _isPanning = true;
                    double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
                    Shell.Current.CurrentPage.TranslationX = -w; // pre-position MainPage
                    await Shell.Current.GoToAsync("..", false);
                    _isPanning = false;
                }
                _panLeft = _panRight = 0;
                break;
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = GoBackWithSlide();
        return true; // prevent default Shell back (no animation)
    }

    private async Task GoBackWithSlide()
    {
        if (_isPanning) return;
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        Shell.Current.CurrentPage.TranslationX = -w; // pre-position MainPage from left
        await Shell.Current.GoToAsync("..", false);
    }

    private async void BtnGoHome_Clicked(object sender, EventArgs e) => await GoBackWithSlide();

    private async void BtnGameMenu_Clicked(object sender, EventArgs e)
    {
        string? choice = await DisplayActionSheet("Go to Game", "Cancel", null,
            "Fantasy 5", "Super Lotto", "Powerball", "Mega Millions", "Daily 3", "Daily 4", "Daily Derby", "Hot Spot", "Notifications", "Summary of Winnings", "Check Wins for Draw#");
        if (choice == null || choice == "Cancel") return;
        if (choice == "Notifications") { await Shell.Current.GoToAsync(nameof(NotificationsPage), false); return; }
        if (choice == "Summary of Winnings") { await Shell.Current.GoToAsync(nameof(SummaryPage), false); return; }
        if (choice == "Check Wins for Draw#") { DrawSearchPage.PresetGame = "Fantasy 5"; await Shell.Current.GoToAsync(nameof(DrawSearchPage), false); return; }
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

    private async void BtnGoSL_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        SuperLottoPage.ComingFrom = "f5";
        AppShell.SuperLottoPageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(SuperLottoPage), false);
    }

    private async void BtnNumIntel_Clicked(object sender, EventArgs e)
    {
        string? choice = await DisplayActionSheet("Number Intelligence", "Cancel", null,
            "Back-Test Numbers", "Hot & Cold Numbers", "Positional Frequency",
            "Sum Range", "Balance Check", "Pairs & Triplets",
            "Number Gap Tracker", "Ticket Scorer", "Wheeling System", "Combo Filter");
        if (choice == null || choice == "Cancel") return;
        if (choice == "Back-Test Numbers")    { BackTestPage.PresetGame      = "Fantasy 5"; await Shell.Current.GoToAsync(nameof(BackTestPage),      false); return; }
        if (choice == "Hot & Cold Numbers")   { HotColdPage.PresetGame       = "Fantasy 5"; await Shell.Current.GoToAsync(nameof(HotColdPage),       false); return; }
        if (choice == "Positional Frequency") { PositionalFreqPage.PresetGame = "Fantasy 5"; await Shell.Current.GoToAsync(nameof(PositionalFreqPage), false); return; }
        if (choice == "Sum Range")            { SumRangePage.PresetGame      = "Fantasy 5"; await Shell.Current.GoToAsync(nameof(SumRangePage),      false); return; }
        if (choice == "Balance Check")        { BalanceCheckPage.PresetGame  = "Fantasy 5"; await Shell.Current.GoToAsync(nameof(BalanceCheckPage),  false); return; }
        if (choice == "Pairs & Triplets")     { PairsTripletsPage.PresetGame = "Fantasy 5"; await Shell.Current.GoToAsync(nameof(PairsTripletsPage), false); return; }
        if (choice == "Number Gap Tracker")   { GapTrackerPage.PresetGame    = "Fantasy 5"; await Shell.Current.GoToAsync(nameof(GapTrackerPage),    false); return; }
        if (choice == "Ticket Scorer")        { TicketScorerPage.PresetGame  = "Fantasy 5"; await Shell.Current.GoToAsync(nameof(TicketScorerPage),  false); return; }
        if (choice == "Wheeling System")      { WheelingPage.PresetGame      = "Fantasy 5"; await Shell.Current.GoToAsync(nameof(WheelingPage),      false); return; }
        if (choice == "Combo Filter")         { ComboFilterPage.PresetGame   = "Fantasy 5"; await Shell.Current.GoToAsync(nameof(ComboFilterPage),   false); return; }
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
        // TranslationX was pre-set by PrePosition before navigation — just animate in
        this.TranslateTo(0, 0, 220, Easing.CubicOut);
        base.OnAppearing();
        UpdateTicketCount();
        _ = LoadAllDraws();
        Dispatcher.Dispatch(() =>
        {
            int pendingRow = -1;
            if (PendingHighlight.HasPending && PendingHighlight.Game == "F5")
            {
                _activeSlot = PendingHighlight.Slot;
                pendingRow  = PendingHighlight.Row;
                PendingHighlight.Clear();
                Preferences.Set("f5_active_slot", _activeSlot);
                FillFromSlot(_activeSlot);
            }
            else
            {
                _activeSlot = Preferences.Get("f5_active_slot", -1);
                if (_activeSlot < 0)
                {
                    var current = Preferences.Get("f5_entries", "");
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
                nums.Add(v.PadLeft(2, '0'));
            }
            _logSnapshot[r] = full ? string.Join(" ", nums) : "";
        }
    }

    async Task LogCurrentTicketsAsync()
    {
        // Log ALL slots from Preferences (already flushed in OnDisappearing before this call).
        // Active slot UI entries are already saved to Preferences by SaveEntries()/SaveSet() above.
        var allRows = new List<(int, int, string, string, string, string)>();
        var fpDict  = new Dictionary<(int, int), bool>();

        for (int s = 0; s < 10; s++)
        {
            string set   = Preferences.Get($"f5_set_{s}", "");
            string adv   = Preferences.Get($"f5_adv_{s}", "");
            string fpRaw = Preferences.Get($"f5_freeplay_{s}", "");
            var fpParts  = string.IsNullOrEmpty(fpRaw) ? Array.Empty<string>() : fpRaw.Split('|');
            if (string.IsNullOrEmpty(set)) continue;

            var vals     = set.Split('|');
            var advParts = string.IsNullOrEmpty(adv) ? new string[Rows] : adv.Split('|');
            if (advParts.Length < Rows) Array.Resize(ref advParts, Rows);

            for (int r = 0; r < Rows; r++)
            {
                var nums = new List<string>(); bool full = true;
                for (int c = 0; c < Cols; c++)
                {
                    string v = r * Cols + c < vals.Length ? vals[r * Cols + c] : "";
                    if (string.IsNullOrWhiteSpace(v)) { full = false; break; }
                    nums.Add(v.PadLeft(2, '0'));
                }
                if (!full) continue;

                string pf = "", pt = "";
                if (!string.IsNullOrEmpty(advParts[r]))
                {
                    var pair = advParts[r].Split('~');
                    if (pair.Length >= 2)
                    {
                        if (DateTime.TryParseExact(pair[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var fd))
                            pf = fd.ToString("M/d");
                        string eff = string.IsNullOrEmpty(pair[1]) ? pair[0] : pair[1];
                        if (DateTime.TryParseExact(eff, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var td))
                            pt = td.ToString("M/d");
                    }
                }
                bool isFp = r < fpParts.Length && fpParts[r] == "1";
                allRows.Add((s, r, string.Join(" ", nums), "", pf, pt));
                fpDict[(s, r)] = isFp;
            }
        }

        // Clear ALL today's F5 log entries, then re-log every slot.
        for (int s = 0; s < 10; s++)
            await Services.TicketLogService.ClearTodayGameSlotAsync("F5", s);
        if (allRows.Count > 0)
            await Services.TicketLogService.LogRowsAsync("F5", allRows, fpDict);
    }

    protected override void OnDisappearing()
    {
        // Save all data BEFORE base.OnDisappearing() — on Android, Entry controls can be
        // detached after base call and return empty text, which would erase saved data.
        SaveAdvanceDates(_activeSlot);
        SaveEntries();
        if (_activeSlot >= 0)
        {
            _slotCache[_activeSlot] = GetCurrentEntryString();
            Preferences.Set("f5_active_slot", _activeSlot);
            SaveFreePlay(_activeSlot);
        }
        // Flush all cached slot changes to Preferences
        foreach (var (slot, entries) in _slotCache)
        {
            bool isEmpty = entries.Replace("|", "").Trim().Length == 0;
            if (isEmpty)
                Preferences.Remove(SetKey(slot));
            else
                Preferences.Set(SetKey(slot), entries);
        }
        Services.TicketLogService.PendingWriteTask = LogCurrentTicketsAsync();
        base.OnDisappearing();
        if (_voiceOn) StopVoice();
        if (_highlightedView != null) { _highlightedView.BackgroundColor = Colors.White; _highlightedView = null; }
    }

    private void SaveEntries()
    {
        var vals = new string[Rows * Cols];
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                vals[r * Cols + c] = _entries[r, c].Text ?? "";
        Preferences.Set("f5_entries", string.Join("|", vals));
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

    private void LoadEntries()
    {
        var saved = Preferences.Get("f5_entries", "");
        if (string.IsNullOrEmpty(saved)) { ClearAllEntries(); return; }
        var vals = saved.Split('|');
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                int idx = r * Cols + c;
                if (idx < vals.Length)
                    _entries[r, c].Text = vals[idx];
            }
        LoadFreePlay(_activeSlot);
    }

    // ── Set slots (save/load 10 named sets) ─────────────────────────────────

    private string SetKey(int slot) => $"f5_set_{slot}";
    private void UpdateTicketCount()
    {
        int total = 0;
        for (int s = 0; s < 10; s++)
        {
            if (s == _activeSlot && _entries != null)
            {
                for (int r = 0; r < Rows; r++)
                    if (!string.IsNullOrWhiteSpace(_entries[r, 0]?.Text)) total++;
            }
            else
            {
                var vals = Preferences.Get($"f5_set_{s}", "").Split('|');
                for (int r = 0; r < 10; r++)
                    if (r * 5 < vals.Length && !string.IsNullOrWhiteSpace(vals[r * 5])) total++;
            }
        }
        lblTicketCount.Text = total > 0 ? $"🎟 {total}" : "";
    }

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
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                _entries[r, c].Text = "";
                _entries[r, c].BackgroundColor = Color.FromArgb("#F5F5F5");
            }
        foreach (var lbl in _results) lbl.Text = "";
        Array.Clear(_playStart, 0, Rows);
        Array.Clear(_playEnd,   0, Rows);
        Array.Clear(_drawStart, 0, Rows);
        Array.Clear(_drawEnd,   0, Rows);
        _advDatesLoaded = true; // user-triggered clear — allow subsequent saves
        ClearFreePlay();
        UpdateAllResultBackgrounds();
    }

    private void ClearRow(int r)
    {
        for (int c = 0; c < Cols; c++)
        {
            _entries[r, c].Text = "";
            _entries[r, c].BackgroundColor = Color.FromArgb("#F5F5F5");
        }
        _results[r].Text = "";
        _playStart[r] = null;
        _playEnd[r]   = null;
        _drawStart[r] = "";
        _drawEnd[r]   = "";
        _freePlay[r]  = false;
        RefreshFpBtn(r);
        UpdateResultBackground(r);
        SaveEntries();
        if (_activeSlot >= 0) SaveSet(_activeSlot);
    }

    // ── Free Play helpers ─────────────────────────────────────────────────────

    private string FpKey(int slot) => slot >= 0 ? $"f5_freeplay_{slot}" : "f5_freeplay_live";

    private void SaveFreePlay(int slot)
    {
        var vals = _freePlay.Select(f => f ? "1" : "0");
        Preferences.Set(FpKey(slot), string.Join("|", vals));
    }

    private void LoadFreePlay(int slot)
    {
        var raw = Preferences.Get(FpKey(slot), "");
        var parts = string.IsNullOrEmpty(raw) ? Array.Empty<string>() : raw.Split('|');
        for (int r = 0; r < Rows; r++)
        {
            _freePlay[r] = r < parts.Length && parts[r] == "1";
            RefreshFpBtn(r);
        }
    }

    private void ClearFreePlay()
    {
        for (int r = 0; r < Rows; r++)
        {
            _freePlay[r] = false;
            RefreshFpBtn(r);
        }
    }

    private void RefreshFpBtn(int r)
    {
        if (_fpBorders[r] == null) return;
        var lbl = _fpBorders[r].Content as Label;
        if (_freePlay[r])
        {
            _fpBorders[r].BackgroundColor = Color.FromArgb("#E65100");
            if (lbl != null) lbl.TextColor = Colors.White;
        }
        else
        {
            _fpBorders[r].BackgroundColor = Color.FromArgb("#37474F");
            if (lbl != null) lbl.TextColor = Color.FromArgb("#78909C");
        }
    }

    // Count FP-flagged rows that have data in the given entries string
    internal static int CountFreePlayRows(string entries, string fpRaw)
    {
        if (string.IsNullOrEmpty(entries) || string.IsNullOrEmpty(fpRaw)) return 0;
        var vals  = entries.Split('|');
        var flags = fpRaw.Split('|');
        int count = 0;
        for (int r = 0; r < 10; r++)
        {
            int idx = r * 5;
            if (idx < vals.Length && !string.IsNullOrWhiteSpace(vals[idx])
                && r < flags.Length && flags[r] == "1")
                count++;
        }
        return count;
    }

    // Count FP-flagged rows active for TODAY only (advance-play rows only count if start date = today)
    internal static int CountFreePlayRowsToday(string entries, string fpRaw, string adv)
    {
        if (string.IsNullOrEmpty(entries) || string.IsNullOrEmpty(fpRaw)) return 0;
        var vals    = entries.Split('|');
        var flags   = fpRaw.Split('|');
        var advRows = string.IsNullOrEmpty(adv) ? Array.Empty<string>() : adv.Split('|');
        int count   = 0;
        for (int r = 0; r < 10; r++)
        {
            int idx = r * 5;
            if (idx >= vals.Length || string.IsNullOrWhiteSpace(vals[idx])) continue;
            if (r >= flags.Length || flags[r] != "1") continue;
            // If row has advance dates, only count when start date is today
            // and the end date has not already passed
            if (r < advRows.Length && !string.IsNullOrEmpty(advRows[r])
                && advRows[r] != "~~~" && advRows[r] != "~")
            {
                var pair = advRows[r].Split('~');

                // If end date is set and already past, ticket is expired — skip
                if (pair.Length >= 2 && !string.IsNullOrEmpty(pair[1])
                    && DateTime.TryParseExact(pair[1], "yyyyMMdd", null,
                        System.Globalization.DateTimeStyles.None, out var ed)
                    && ed.Date < DateTime.Today)
                    continue;

                bool startIsToday = pair.Length >= 1
                    && !string.IsNullOrEmpty(pair[0])
                    && DateTime.TryParseExact(pair[0], "yyyyMMdd", null,
                        System.Globalization.DateTimeStyles.None, out var sd)
                    && sd.Date == DateTime.Today;
                if (!startIsToday) continue;
            }
            count++;
        }
        return count;
    }

    private void SaveSet(int slot, bool refreshPicker = true)
    {
        string data = GetCurrentEntryString();
        bool isEmpty = data.Replace("|", "").Trim().Length == 0;
        if (isEmpty)
        {
            Preferences.Remove(SetKey(slot));   // saving empty clears the slot
            Preferences.Remove(FpKey(slot));
        }
        else
        {
            Preferences.Set(SetKey(slot), data);
            SaveFreePlay(slot);
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
        var vals = saved.Split('|');
        _suppressAdvApply = true;
        try
        {
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                {
                    int idx = r * Cols + c;
                    _entries[r, c].Text = idx < vals.Length ? vals[idx] : "";
                }
            LoadAdvanceDates(slot);
            RefreshAdvAllPanel();
            LoadFreePlay(slot);
            CheckAll();
            UpdateAllResultBackgrounds();
        }
        finally { _suppressAdvApply = false; }
    }

    private bool SlotHasData(int slot) =>
        !string.IsNullOrEmpty(Preferences.Get(SetKey(slot), ""));

    private bool IsCurrentSaved()
    {
        string current = GetCurrentEntryString();
        if (current.Replace("|", "").Trim().Length == 0) return true;
        for (int i = 0; i < 10; i++)
            if (Preferences.Get(SetKey(i), "") == current) return true;
        return false;
    }


    private bool _advAllDateMode = false;

    private void BtnClearAdvSlot_Clicked(object sender, EventArgs e)
    {
        if (_activeSlot < 0) return;
        for (int r = 0; r < Rows; r++)
        {
            _playStart[r] = null;
            _playEnd[r]   = null;
            _drawStart[r] = "";
            _drawEnd[r]   = "";
        }
        SaveAdvanceDates(_activeSlot);
        UpdateAllResultBackgrounds();
    }

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
            string? warn = SpendingTracker.CheckDrawRangeWarning("F5", from, to, ds, de,
                currentDrawNumber: DrawNumberService.GetNextDraw("Fantasy 5"));
            if (warn != null)
            {
                bool proceed = await DisplayAlert("Check Draw # Range", warn, "Save Anyway", "Fix It");
                if (!proceed)
                {
                    int current = DrawNumberService.GetNextDraw("Fantasy 5");
                    if (current <= 0) current = await DrawNumberService.EnsureNextDrawAsync("Fantasy 5");
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

    private string ExclKey(int slot) => $"excl_set_f5_{slot}";

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

    private void UpdateSlotPicker()
    {
        _suppressPickerEvent = true;
        for (int i = 0; i < 10; i++)
            slotPicker.Items[i] = SlotLabel(i);
        slotPicker.SelectedIndex = _activeSlot;
        slotPicker.Unfocus();
        UpdateExclCheckbox();
        UpdateTicketCount();
        // See Daily3Page.SlotPicker_Focused for why the reset happens there, not here.
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
        // Cache current slot before switching. _slotCache is in-memory only — must not be the
        // only copy, or the outgoing slot's tickets are lost for good if the app process dies
        // before this page is exited normally while that slot is active again. SaveSet persists
        // them to disk now too (see Daily3Page's same fix, 2026-08-03).
        if (_activeSlot >= 0)
        {
            _slotCache[_activeSlot] = GetCurrentEntryString();
            SaveSet(_activeSlot, refreshPicker: false);
        }
        SaveAdvanceDates(_activeSlot);
        SaveFreePlay(_activeSlot);
        _activeSlot = slot;
        Preferences.Set("f5_active_slot", slot);
        ClearAllEntries();
        _suppressAdvApply = true;
        try
        {
            if (_slotCache.TryGetValue(slot, out var cached))
            {
                var vals = cached.Split('|');
                for (int r = 0; r < Rows; r++)
                    for (int c = 0; c < Cols; c++)
                    {
                        int idx = r * Cols + c;
                        _entries[r, c].Text = idx < vals.Length ? vals[idx] : "";
                    }
                CheckAll();
                LoadAdvanceDates(slot);
                RefreshAdvAllPanel();
                LoadFreePlay(slot);
                UpdateAllResultBackgrounds();
            }
            else if (SlotHasData(slot))
                FillFromSlot(slot);
        }
        finally { _suppressAdvApply = false; }
        UpdateSlotPicker();
    }

    // ── Build 10 input rows ──────────────────────────────────────────────────

    private void BuildRows()
    {
        for (int r = 0; r < Rows; r++)
        {
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),   // row # label
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),   // result label
                    new ColumnDefinition(GridLength.Auto),   // FP button
                },
                ColumnSpacing = 6,
                BackgroundColor = Colors.White,
                Margin = new Thickness(0),
                Padding = new Thickness(10, 8),
            };

            // Row number — tap to clear this row
            int rowIdx = r;
            var rowNum = new Label
            {
                Text = $"{r + 1,2}.",
                FontSize = 13,
                TextColor = Color.FromArgb("#FF7043"),
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 24,
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

            // 5 Entry boxes
            for (int c = 0; c < Cols; c++)
            {
                var entry = new Entry
                {
                    Keyboard = Keyboard.Numeric,
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.Black,
                    BackgroundColor = Color.FromArgb("#F5F5F5"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    HeightRequest = 44,
                    MaxLength = 2,
                };
                entry.HandlerChanged += ForceBlackText;
                AttachMaxClamp(entry, 39);

                // Capture row/col for the lambda
                int row_ = r, col_ = c;
                EntryHelper.AttachBackspace(entry, () => RetreatFocus(row_, col_));
                entry.TextChanged += (s, e) =>
                {
                    // Duplicate prevention
                    string nv = e.NewTextValue ?? "";
                    if (nv.Length == ((Entry)s!).MaxLength && int.TryParse(nv, out int entered) && entered > 0)
                    {
                        for (int ci = 0; ci < Cols; ci++)
                        {
                            if (ci == col_) continue;
                            if (int.TryParse(_entries[row_, ci].Text, out int ex) && ex == entered)
                            {
                                ((Entry)s!).Text = e.OldTextValue ?? "";
                                lblStatus.Text = $"Row {row_ + 1}: {entered} already used — no duplicates";
                                return;
                            }
                        }
                    }
                    if (!_voiceSettingText && nv.Length == 2)
                    {
                        if (int.TryParse(nv, out int cv) && cv >= 1)
                            AdvanceFocus(row_, col_);
                        else
                            ((Entry)s!).Text = ""; // reject "00", stay on this box
                    }
                    SaveEntries();
                    TriggerAutoSaveIndicator();
                    bool rowFull = true;
                    for (int ci = 0; ci < Cols; ci++)
                        if (string.IsNullOrEmpty(_entries[row_, ci].Text)) { rowFull = false; break; }
                    if (rowFull) CheckAll();
                    UpdateTicketCount();
                };

                _entries[r, c] = entry;
                Grid.SetColumn(entry, c + 1);
                row.Children.Add(entry);
            }

            // Result label (tappable — opens advance play date picker)
            var result = new Label
            {
                Text = "+",
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#4B6A8A"),
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
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
                WidthRequest = 36,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
            };
            Grid.SetColumn(resultBorder, 6);
            row.Children.Add(resultBorder);

            int ri = r;
            resultBorder.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => ShowAdvancePlayOverlay(ri))
            });

            // FP (Free Play) toggle — use Border+Label to avoid Android minimum button size
            var fpLabel = new Label
            {
                Text = "FP",
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#78909C"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };
            var fpBorder = new Border
            {
                Content = fpLabel,
                BackgroundColor = Color.FromArgb("#37474F"),
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 3 },
                Padding = new Thickness(5, 3),
                WidthRequest = 32,
                HeightRequest = 28,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
            };
            // Store border as the "button" for color updates
            _fpBtns[r] = new Button { IsVisible = false }; // placeholder — not used for display
            int fpRow = r;
            fpBorder.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    _freePlay[fpRow] = !_freePlay[fpRow];
                    RefreshFpBtn(fpRow);
                    SaveFreePlay(_activeSlot);
                })
            });
            // Store border reference for color refresh
            _fpBorders[r] = fpBorder;
            Grid.SetColumn(fpBorder, 7);
            row.Children.Add(fpBorder);

            var card = new Border
            {
                Content = row,
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

    private void AdvanceFocus(int row, int col)
    {
        int nextCol = col + 1;
        int nextRow = row;
        if (nextCol >= Cols) { nextCol = 0; nextRow = row + 1; }
        if (nextRow < Rows)
            _entries[nextRow, nextCol].Focus();
    }

    private void RetreatFocus(int row, int col)
    {
        if (col > 0) { EntryHelper.SelectAll(_entries[row, col - 1]); return; }
        if (row > 0) EntryHelper.SelectAll(_entries[row - 1, Cols - 1]);
    }

    static void AttachMaxClamp(Entry entry, int max)
    {
        entry.TextChanged += (s, e) =>
        {
            var ent = (Entry)s!;
            string nv = e.NewTextValue ?? "";
            if (int.TryParse(nv, out int v) &&
                (v > max || (nv.Length == ent.MaxLength && v < 1)))
                ent.Text = e.OldTextValue ?? "";
        };
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
        // rowsContainer holds Border cards — highlight the inner Grid so it's visible
        var target = (rowView is Border b && b.Content is View inner) ? inner : rowView;
        _highlightedView = target;
        target.BackgroundColor = Color.FromArgb("#FFF176");
        if (rowsContainer.Parent is ScrollView sv)
            await sv.ScrollToAsync(rowView, ScrollToPosition.MakeVisible, true);
        await Task.Delay(2000);
        target.BackgroundColor = Colors.White;
        _highlightedView = null;
    }

    // ── WebView-based fetch (bypasses server bot detection) ──────────────────

    private Task<string?> FetchViaWebView(string url)
    {
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<WebNavigatedEventArgs>? handler = null;
        handler = async (s, e) =>
        {
            fetchView.Navigated -= handler;
            if (e.Result == WebNavigationResult.Success)
            {
                try
                {
                    var raw = await fetchView.EvaluateJavaScriptAsync(
                        "(function(){var p=document.querySelector('pre');return p?p.textContent:(document.body.innerText||document.body.textContent||'');})()");
                    if (raw != null && raw.Length > 1 && raw[0] == '"' && raw[^1] == '"')
                        raw = System.Text.RegularExpressions.Regex.Unescape(raw[1..^1]);
                    tcs.TrySetResult(raw);
                }
                catch { tcs.TrySetResult(null); }
            }
            else tcs.TrySetResult(null);
        };
        fetchView.Navigated += handler;
        MainThread.BeginInvokeOnMainThread(() =>
            fetchView.Source = new UrlWebViewSource { Url = url });
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(45));
    }

    // ── Load all past draws and populate Picker ──────────────────────────────

    private async Task LoadAllDraws()
    {
        // Re-fetch if today's draw hasn't been loaded yet (e.g. cached before 6:30pm draw)
        if (_drawsLoaded)
        {
            bool hasTodayDraw = _allDraws.Any(d => d.Date.Date == DateTime.Today);
            if (hasTodayDraw)
            {
                await Task.Delay(400);
                spinner.IsVisible = false;
                spinner.IsRunning = false;
                loadingOverlay.IsVisible = false;
                return;
            }
            _drawsLoaded = false;
            _allDraws.Clear();
        }

        spinner.IsVisible = true;
        spinner.IsRunning = true;
        loadingOverlay.IsVisible = true;
        lblDrawDate.Text = "Fetching draws from calottery.com...";

        var raw = await GetDataEntry.GetPastDraws(30);

        spinner.IsVisible = false;
        spinner.IsRunning = false;
        loadingOverlay.IsVisible = false;

        if (raw.Count == 0)
        {
            string errMsg = string.IsNullOrEmpty(GetDataEntry.LastError)
                ? "Fantasy5: Could not fetch — check internet connection"
                : $"Fantasy5: {GetDataEntry.LastError}";
            lblDrawDate.Text = errMsg;
            lblStatus.Text = errMsg;
            _ = Logger.LogAsync(errMsg);
            return;
        }

        _allDraws = raw
            .Select(d => (
                Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                Label: d.DrawDate,
                DrawNumber: d.DrawNumber,
                Numbers: d.Numbers))
            .Where(d => d.Date != DateTime.MinValue)
            .ToList();

        _drawsLoaded = true;

        // Use today's draw only if the API actually returned it
        bool todayAvailable = _allDraws.Any(d => d.Date.Date == DateTime.Today);
        var defaultDraw = todayAvailable
            ? _allDraws.First()
            : _allDraws.FirstOrDefault(d => d.Date.Date < DateTime.Today);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            drawDatePicker.MinimumDate = _allDraws.Last().Date.Date;
            drawDatePicker.MaximumDate = DateTime.Today;
            var targetDate = DateTime.Today;
            if (drawDatePicker.Date == targetDate)
                ShowDrawForDate(targetDate); // DateSelected won't fire if date unchanged
            else
                drawDatePicker.Date = targetDate;
        });
    }

    private void ShowDrawForDate(DateTime date)
    {
        // Fall back to yesterday only if today's draw isn't in the API data yet
        bool todayAvailable = _allDraws.Any(d => d.Date.Date == DateTime.Today);
        if (date.Date == DateTime.Today && !todayAvailable)
            date = date.AddDays(-1);

        // Find the closest completed draw on or before the selected date
        var match = _allDraws.FirstOrDefault(d => d.Date.Date <= date.Date);
        if (match.Numbers == null) return;

        _winningNumbers  = match.Numbers;
        lblDrawDate.Text = match.DrawNumber > 0 ? $"{match.Label}  Draw #{match.DrawNumber}" : match.Label;
        for (int i = 0; i < _wLabels.Length; i++)
            _wLabels[i].Text = match.Numbers[i].ToString();
        CheckAll();
    }

    private void DrawDatePicker_DateSelected(object sender, DateChangedEventArgs e) =>
        ShowDrawForDate(e.NewDate ?? DateTime.Today);

    // ── Check all rows ───────────────────────────────────────────────────────

    private void CheckAll()
    {
        if (_winningNumbers.Length == 0) return;
        var winSet = new HashSet<int>(_winningNumbers);

        for (int r = 0; r < Rows; r++)
        {
            int matchCount = 0;
            for (int c = 0; c < Cols; c++)
            {
                if (int.TryParse(_entries[r, c].Text, out int n) && winSet.Contains(n))
                {
                    _entries[r, c].BackgroundColor = Color.FromArgb("#F9A825"); // gold = match
                    matchCount++;
                }
                else
                {
                    bool hasValue = !string.IsNullOrWhiteSpace(_entries[r, c].Text);
                    _entries[r, c].BackgroundColor = hasValue
                        ? Color.FromArgb("#FFCDD2")   // red-tint = no match
                        : Color.FromArgb("#F5F5F5");   // grey = empty
                }
            }

            _results[r].Text = matchCount > 0 ? $"{matchCount}/5" : "";
            _results[r].TextColor = matchCount >= 3
                ? Color.FromArgb("#2E7D32")
                : Color.FromArgb("#C62828");

        }
        UpdateAllResultBackgrounds();
    }


    internal void ClearForArchive()
    {
        _slotCache.Clear();
        _activeSlot = -1;
        ClearAllEntries();
        UpdateSlotPicker();
    }

    /// <summary>Called after a purge to drop the slot number cache so OnDisappearing can't write stale numbers back to prefs.</summary>
    internal void InvalidateAfterPurge()
    {
        _slotCache.Clear();
        _advDatesLoaded = false; // prevent SaveAdvanceDates from writing stale null values before next LoadAdvanceDates
    }

    // ── Button handlers ──────────────────────────────────────────────────────

    private void BtnCheck_Clicked(object sender, EventArgs e) => CheckAll();

    private void BtnClearAll_Clicked(object sender, EventArgs e)
    {
        ClearAllEntries();
        SaveEntries();
        if (_activeSlot >= 0) SaveSet(_activeSlot);
    }

    private async void BtnRefresh_Clicked(object sender, EventArgs e)
    {
        _drawsLoaded = false;
        _allDraws.Clear();
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

            var nums = Enumerable.Range(1, 39).OrderBy(_ => rng.Next()).Take(Cols).OrderBy(n => n).ToList();
            for (int c = 0; c < Cols; c++)
                _entries[r, c].Text = nums[c].ToString();
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
        bool confirm = await DisplayAlert("Clear All Sets", "Remove all 10 saved sets?", "Yes", "Cancel");
        if (!confirm) return;

        int skipped = 0;
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
                if (!refDate.HasValue || refDate.Value.Date < today || (refDate.Value.Date == today && now.TimeOfDay >= TimeSpan.FromHours(20))) ClearRow(r);
            }
            if (_activeSlot >= 0) { SaveSet(_activeSlot); SaveAdvanceDates(_activeSlot); }
        }
        else if (_activeSlot < 0 || !SlotHasFutureAdvDate(_activeSlot))
            ClearAllEntries();
        UpdateSlotPicker();

        if (sender is Button btn)
        {
            var orig = btn.Text;
            var origColor = btn.BackgroundColor;
            btn.Text = skipped > 0 ? $"Cleared ({skipped} kept)" : "Cleared";
            btn.BackgroundColor = Color.FromArgb("#1B5E20");
            await Task.Delay(1200);
            btn.Text = orig;
            btn.BackgroundColor = origColor;
        }
    }

    private void BtnVoice_Clicked(object sender, EventArgs e)
    {
        if (!Services.VoiceNumberService.IsAvailable) { lblStatus.Text = "Speech recognition not available"; return; }
        if (_voiceOn) StopVoice(); else StartVoice();
    }

    void StartVoice()
    {
        // Find first empty cell
        _voiceRow = 0; _voiceCol = 0;
        VoiceSkipFilled(Cols);
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
            if (n >= 1 && n <= 39)
            {
                _voiceSettingText = true;
                _entries[_voiceRow, _voiceCol].Text = n.ToString();
                _voiceSettingText = false;
                _voiceCol++;
                if (_voiceCol >= Cols) { _voiceCol = 0; _voiceRow++; }
                VoiceSkipFilled(Cols);
            }
        }
        CheckAll(); SaveEntries();
        SetVoiceTarget(); // after CheckAll so green highlight isn't wiped
        if (_voiceOn && _voiceRow < Rows)
            lblStatus.Text = $"🔴 Listening | row {_voiceRow + 1} col {_voiceCol + 1}";
    }

    void VoiceSkipFilled(int totalCols)
    {
        while (_voiceRow < Rows && !string.IsNullOrEmpty(_entries[_voiceRow, _voiceCol].Text))
        {
            _voiceCol++;
            if (_voiceCol >= totalCols) { _voiceCol = 0; _voiceRow++; }
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
                "Fantasy 5", "f5_set_", _activeSlot < 0 ? 0 : _activeSlot, GetCurrentEntryString());
            return;
        }
        // Cache current slot then flush all cached slots
        if (_activeSlot >= 0)
            _slotCache[_activeSlot] = GetCurrentEntryString();
        SaveEntries();
        foreach (var (slot, entries) in _slotCache)
        {
            bool isEmpty = entries.Replace("|", "").Trim().Length == 0;
            if (isEmpty)
                Preferences.Remove(SetKey(slot));
            else
                Preferences.Set(SetKey(slot), entries);
        }
        UpdateSlotPicker();
        int savedCount = _slotCache.Count(kv => kv.Value.Replace("|", "").Trim().Length > 0);
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
            string w = SpendingTracker.CheckDrawRangeWarning(
                "F5", _advStartPicker!.Date ?? DateTime.Today, _advEndPicker!.Date ?? DateTime.Today,
                _advDrawStartEntry!.Text ?? "", _advDrawEndEntry!.Text ?? "",
                currentDrawNumber: DrawNumberService.GetNextDraw("Fantasy 5")) ?? "";
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
            string? warn = SpendingTracker.CheckDrawRangeWarning("F5", _advStartPicker!.Date ?? DateTime.Today, _advEndPicker!.Date ?? DateTime.Today, ds, de,
                currentDrawNumber: DrawNumberService.GetNextDraw("Fantasy 5"));
            if (warn != null)
            {
                bool proceed = await DisplayAlert("Check Draw # Range", warn, "Save Anyway", "Fix It");
                if (!proceed)
                {
                    int current = DrawNumberService.GetNextDraw("Fantasy 5");
                    if (current <= 0) current = await DrawNumberService.EnsureNextDrawAsync("Fantasy 5");
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
            _playEnd[_advRow]   = _advEndPicker!.Date >= _advStartPicker!.Date ? _advEndPicker!.Date : _advStartPicker!.Date;
            _drawStart[_advRow] = ds;
            _drawEnd[_advRow]   = de;
            UpdateResultBackground(_advRow);
            SaveAdvanceDates(_activeSlot);
            _ = TicketLogService.RecordAdvanceEnteredAsync("F5", _activeSlot, _advRow, _playStart[_advRow]!.Value, _playEnd[_advRow]!.Value);
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
        if (!defEnd.HasValue || defEnd.Value.Date < (_playStart[row]?.Date ?? DateTime.Today))
            defEnd = (_playStart[row] ?? DateTime.Today);
        _advEndPicker!.Date = defEnd.Value;
        _advDrawStartEntry!.Text = _drawStart[row] ?? "";
        _advDrawEndEntry!.Text   = _drawEnd[row] ?? "";
        _advOverlay!.IsVisible = true;
        if (string.IsNullOrEmpty(_advDrawStartEntry.Text) && string.IsNullOrEmpty(_advDrawEndEntry.Text))
        {
            int current = DrawNumberService.GetNextDraw("Fantasy 5");
            if (current <= 0) current = await DrawNumberService.EnsureNextDrawAsync("Fantasy 5");
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

    private string AdvDatesKey(int slot) => $"f5_adv_{slot}";
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
                    keep = refDate.HasValue && (refDate.Value.Date > today || (refDate.Value.Date == today && now.TimeOfDay < TimeSpan.FromHours(20)));
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
                if (refDate.HasValue && (refDate.Value.Date > today || (refDate.Value.Date == today && now.TimeOfDay < TimeSpan.FromHours(20)))) return true;
            }
            return false;
        }
        // Only protect slots that have actual saved numbers (guards against stale adv date prefs)
        if (string.IsNullOrEmpty(Preferences.Get(SetKey(slot), ""))) return false;
        string raw = Preferences.Get(AdvDatesKey(slot), "");
        if (string.IsNullOrEmpty(raw)) return false;
        foreach (var part in raw.Split('|'))
        {
            var pair = part.Split('~');
            if (pair.Length < 2) continue;
            DateTime? end = null, start = null;
            if (DateTime.TryParseExact(pair[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var ed)) end = ed;
            if (DateTime.TryParseExact(pair[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var sd)) start = sd;
            var refDate = end ?? start;
            if (refDate.HasValue && (refDate.Value.Date > today || (refDate.Value.Date == today && now.TimeOfDay < TimeSpan.FromHours(20)))) return true;
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
        ResultsPage.NeedsRefresh = true;
    }

    public void FlushAdvanceDates()
    {
        if (_activeSlot >= 0) SaveAdvanceDates(_activeSlot);
    }

    private async void RefreshAdvAllPanel()
    {
        advAllFromPicker.Date = DateTime.Today;
        advAllToPicker.Date   = DateTime.Today;
        int next = await DrawNumberService.EnsureNextDrawAsync("Fantasy 5");
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
