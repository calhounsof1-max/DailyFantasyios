using DailyFantasyMAUI.Services;
using DailyFantasyMAUI.ViewModel;

namespace DailyFantasyMAUI;

public partial class MainPage : ContentPage
{
    readonly MainViewModel vm = new();
    int _mode = 0; // 0=F5, 1=SL, 2=D3
    bool _isRestoring = false;
    bool _initialized = false;
    bool _isPanning = false;
    Entry[] _boxes = null!;

    public MainPage()
    {
        InitializeComponent();
        BindingContext = vm;
        cmbRecurrence.SelectedIndex = 0;

        foreach (var entry in new[] { Box1, Box2, Box3, Box4, Box5, Box6, Box7,
                                      Box8, Box9, Box10, Box11, Box12,
                                      MaxNum, HowMany })
            entry.HandlerChanged += ForceBlackText;
    }

    double _panPeak; // most-negative TotalX seen during this gesture

    private async void OnPagePan(object? sender, PanUpdatedEventArgs e)
    {
        if (_isPanning) return;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panPeak = 0;
                break;
            case GestureStatus.Running:
                if (e.TotalX < _panPeak) _panPeak = e.TotalX; // track leftward peak
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_panPeak < -40) // left swipe — no drag tracking, just navigate
                {
                    _isPanning = true;
                    NavDir.FromRight = true;
                    SavePreferences();
                    AppShell.WinnerPageInstance.PrePosition(true);
                    await Shell.Current.GoToAsync(nameof(WinnerPage), false);
                    _isPanning = false;
                }
                _panPeak = 0;
                break;
        }
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

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_initialized)
        {
            // TranslationX was pre-set by the caller before navigating back — just animate in
            await this.TranslateTo(0, 0, 220, Easing.CubicOut);
            return;
        }
        _initialized = true;

        _boxes = new[] { Box1, Box2, Box3, Box4, Box5, Box6, Box7, Box8, Box9, Box10, Box11, Box12 };
        for (int i = 0; i < _boxes.Length; i++)
        {
            int idx = i;
            _boxes[i].TextChanged += (_, _) =>
            {
                SavePreferences();
                if (_boxes[idx].Text?.Length == 2 && idx + 1 < _boxes.Length)
                    _boxes[idx + 1].Focus();
            };
        }
        MaxNum.TextChanged += (_, _) => SavePreferences();
        HowMany.TextChanged += (_, _) => SavePreferences();
        RestorePreferences();
        await vm.LoadDataAsync();
        await vm.LoadPicksAsync(_mode);
        var comboMeta = await vm.LoadSavedCombosAsync();
        if (comboMeta is { } meta)
        {
            _isRestoring = true;
            try
            {
                for (int i = 0; i < meta.Boxes.Length && i < _boxes.Length; i++)
                    _boxes[i].Text = meta.Boxes[i];
                MaxNum.Text = meta.MaxNum;
                HowMany.Text = meta.HowMany;
            }
            finally { _isRestoring = false; }
        }
        UpdateCombosLabel();
        if (int.TryParse(HowMany.Text, out int from)) HighlightBoxes(from);
        btnInsertToWinner.Text = _mode == 0 ? "Insert Combos → F5 Winner"
                                : _mode == 1 ? "Insert Combos → SL Winner"
                                :              "Insert Combos → Daily 3";
        UpdateModeButtons();
        vm.ActiveTab = 0;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SavePreferences();
    }

    private void SavePreferences()
    {
        if (_isRestoring || _boxes == null) return;
        for (int i = 0; i < _boxes.Length; i++)
            Preferences.Set($"box{i + 1}", _boxes[i].Text ?? "");
        Preferences.Set("maxnum", MaxNum.Text ?? "");
        Preferences.Set("howmany", HowMany.Text ?? "");
        Preferences.Set("gameMode", _mode);
    }

    private void RestorePreferences()
    {
        if (_boxes == null) return;
        _isRestoring = true;
        try
        {
            _mode = Preferences.Get("gameMode", 0);

            for (int i = 0; i < _boxes.Length; i++)
            {
                var saved = Preferences.Get($"box{i + 1}", "");
                if (!string.IsNullOrEmpty(saved)) _boxes[i].Text = saved;
            }
            var maxnum = Preferences.Get("maxnum", "");
            var howmany = Preferences.Get("howmany", "");
            if (!string.IsNullOrEmpty(maxnum)) MaxNum.Text = maxnum;
            if (!string.IsNullOrEmpty(howmany)) HowMany.Text = howmany;
        }
        finally
        {
            _isRestoring = false;
        }
    }

    // ── Mode toggle (F5 / SL / D3) ────────────────────────────────────────────

    private void UpdateModeButtons()
    {
        btnModeF5.BackgroundColor = _mode == 0 ? Color.FromArgb("#FF8F00") : Color.FromArgb("#3D3D5C");
        btnModeF5.TextColor       = _mode == 0 ? Colors.White : Color.FromArgb("#8B9DC3");
        btnModeSL.BackgroundColor = _mode == 1 ? Color.FromArgb("#7B1FA2") : Color.FromArgb("#3D3D5C");
        btnModeSL.TextColor       = _mode == 1 ? Colors.White : Color.FromArgb("#8B9DC3");
        btnModeD3.BackgroundColor = _mode == 2 ? Color.FromArgb("#1565C0") : Color.FromArgb("#3D3D5C");
        btnModeD3.TextColor       = _mode == 2 ? Colors.White : Color.FromArgb("#8B9DC3");
    }

    private async Task SwitchMode(int mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        MaxNum.Text  = mode == 2 ? "3" : mode == 1 ? "6" : "5";
        HowMany.Text = mode == 0 ? "39" : mode == 1 ? "47" : "9";
        Preferences.Set("gameMode", _mode);
        UpdateModeButtons();
        btnInsertToWinner.Text = mode == 0 ? "Insert Combos → F5 Winner"
                                : mode == 1 ? "Insert Combos → SL Winner"
                                :              "Insert Combos → Daily 3";
        await vm.LoadPicksAsync(_mode);
        UpdateCombosLabel();
    }

    private async void BtnModeF5_Clicked(object sender, EventArgs e) => await SwitchMode(0);
    private async void BtnModeSL_Clicked(object sender, EventArgs e) => await SwitchMode(1);
    private async void BtnModeD3_Clicked(object sender, EventArgs e) => await SwitchMode(2);

    // ── History selection → fill boxes ───────────────────────────────────────

    private void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection?.Count == 0) return;
        var selected = e.CurrentSelection[0]?.ToString();
        if (selected == null) return;

        string[] parts;
        bool isFiveNum = _mode != 2;
        if (isFiveNum)
            parts = selected.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        else
            parts = selected.ToCharArray().Select(c => c.ToString()).ToArray();

        if (!_showingDraws)
        {
            int count = _mode == 1 ? 6 : isFiveNum ? 5 : 3;
            for (int i = 0; i < count && i < _boxes.Length && i < parts.Length; i++)
                _boxes[i].Text = parts[i];
        }

        int idx = vm.Picks.IndexOf(selected);
        if (idx >= 0)
            vm.StatusMessage = $"Item {idx + 1} of {vm.Picks.Count}  —  {selected.Trim()}";

        if (!_showingDraws)
        {
            string joinSep = isFiveNum ? " " : "";
            ShowReorderOverlay(parts, idx, vm.Picks, joinSep, lstHistory, isFiveNum);
        }
    }

    private void Recurrence_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection?.Count == 0) return;
        var selected = e.CurrentSelection[0];
        if (selected == null) return;
        int idx = vm.RecurrenceResults.IndexOf((DailyFantasyMAUI.Model.ModelDaily)selected);
        if (idx >= 0)
            vm.StatusMessage = $"Item {idx + 1} of {vm.RecurrenceResults.Count}";
    }

    private void Combos_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection?.Count == 0) return;
        var selected = e.CurrentSelection[0]?.ToString();
        if (selected == null) return;
        int idx = vm.Combinations.IndexOf(selected);
        if (idx >= 0)
            vm.StatusMessage = $"Item {idx + 1} of {vm.Combinations.Count}  —  {selected}";
        ShowReorderOverlay(
            selected.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            idx, vm.Combinations, " ", lstCombos, _mode != 2);
    }

    // ── Reorder overlay ───────────────────────────────────────────────────────

    string[] _reorderParts = [];
    int _reorderSelectedIdx = -1;
    int _reorderListIdx = -1;
    string _reorderJoinSep = " ";
    System.Collections.ObjectModel.ObservableCollection<string>? _reorderCollection;
    CollectionView? _reorderSourceList;
    Button[] _reorderBtns = [];

    private void ShowReorderOverlay(
        string[] parts, int listIdx,
        System.Collections.ObjectModel.ObservableCollection<string> collection,
        string joinSep, CollectionView sourceList, bool padToTwo = false)
    {
        // Pad to 2 digits for Fantasy 5 (e.g. "1" → "01", "12" stays "12")
        _reorderParts = new string[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (padToTwo && int.TryParse(parts[i], out int v))
                _reorderParts[i] = v.ToString("D2");
            else
                _reorderParts[i] = parts[i];
        }

        _reorderListIdx = listIdx;
        _reorderCollection = collection;
        _reorderJoinSep = joinSep;
        _reorderSourceList = sourceList;
        _reorderSelectedIdx = -1;

        // Fit buttons within ~270px (leaves buffer inside 280px inner frame)
        int n = _reorderParts.Length;
        const int spacing = 4;
        int btnSize = Math.Min(64, (270 - (n - 1) * spacing) / n);
        int fontSize = btnSize >= 58 ? 20 : btnSize >= 46 ? 17 : 14;
        reorderNumbers.Spacing = spacing;

        reorderNumbers.Children.Clear();
        _reorderBtns = new Button[n];
        for (int i = 0; i < n; i++)
        {
            int idx = i;
            var btn = new Button
            {
                Text = _reorderParts[i],
                FontSize = fontSize,
                FontAttributes = FontAttributes.Bold,
                BackgroundColor = Color.FromArgb("#3F51B5"),
                TextColor = Colors.White,
                WidthRequest = btnSize,
                HeightRequest = btnSize,
                Padding = new Thickness(2, 0),
                CornerRadius = 8
            };
            btn.Clicked += (_, _) => ReorderNumber_Clicked(idx);
            _reorderBtns[i] = btn;
            reorderNumbers.Children.Add(btn);
        }
        reorderOverlay.IsVisible = true;
    }

    private void ReorderNumber_Clicked(int idx)
    {
        if (_reorderSelectedIdx == -1)
        {
            _reorderSelectedIdx = idx;
            _reorderBtns[idx].BackgroundColor = Color.FromArgb("#F9A825");
        }
        else if (_reorderSelectedIdx == idx)
        {
            _reorderBtns[idx].BackgroundColor = Color.FromArgb("#3F51B5");
            _reorderSelectedIdx = -1;
        }
        else
        {
            (_reorderParts[_reorderSelectedIdx], _reorderParts[idx]) =
                (_reorderParts[idx], _reorderParts[_reorderSelectedIdx]);
            _reorderBtns[_reorderSelectedIdx].Text = _reorderParts[_reorderSelectedIdx];
            _reorderBtns[idx].Text = _reorderParts[idx];
            _reorderBtns[_reorderSelectedIdx].BackgroundColor = Color.FromArgb("#3F51B5");
            _reorderSelectedIdx = -1;
        }
    }

    private void ReorderCancel_Clicked(object sender, EventArgs e)
    {
        reorderOverlay.IsVisible = false;
        _reorderSourceList?.ClearValue(CollectionView.SelectedItemProperty);
    }

    private void ReorderApply_Clicked(object sender, EventArgs e)
    {
        if (_reorderCollection != null && _reorderListIdx >= 0 && _reorderListIdx < _reorderCollection.Count)
        {
            string newValue = string.Join(_reorderJoinSep, _reorderParts);
            // RemoveAt+Insert forces CollectionView to refresh (index-setter doesn't always trigger UI update)
            _reorderCollection.RemoveAt(_reorderListIdx);
            _reorderCollection.Insert(_reorderListIdx, newValue);
            vm.StatusMessage = $"Reordered: {newValue}";
        }
        reorderOverlay.IsVisible = false;
        _reorderSourceList?.ClearValue(CollectionView.SelectedItemProperty);
    }

    // ── MaxNum / HowMany ──────────────────────────────────────────────────────

    private void MaxNum_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCombosLabel();
    }

    private void HowMany_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCombosLabel();
        if (int.TryParse(HowMany.Text, out int n))
            HighlightBoxes(n);
        else
            HighlightBoxes(0);
    }

    private void UpdateCombosLabel()
    {
        if (!int.TryParse(MaxNum.Text, out int maxNum) || !int.TryParse(HowMany.Text, out int howMany)) return;
        if (maxNum <= 0 || howMany <= 0 || maxNum > howMany) return;
        vm.UpdateCombinations(maxNum, howMany);
    }

    private void HighlightBoxes(int count)
    {
        if (_boxes == null) return;
        for (int i = 0; i < _boxes.Length; i++)
            _boxes[i].BackgroundColor = (i < count) ? Color.FromArgb("#FFF176") : Colors.White;
    }

    // ── Clear ─────────────────────────────────────────────────────────────────

    private void BtnClear_Clicked(object sender, EventArgs e)
    {
        if (_boxes == null) return;
        foreach (var box in _boxes) box.Text = "";
        HighlightBoxes(0);
    }

    // ── Shift buttons ─────────────────────────────────────────────────────────

    private void BtnShiftLeft_Clicked(object sender, EventArgs e)
    {
        if (_boxes == null) return;
        string first = _boxes[0].Text;
        for (int i = 0; i < _boxes.Length - 1; i++)
            _boxes[i].Text = _boxes[i + 1].Text;
        _boxes[_boxes.Length - 1].Text = first;
    }

    private void BtnShiftRight_Clicked(object sender, EventArgs e)
    {
        if (_boxes == null) return;
        string last = _boxes[_boxes.Length - 1].Text;
        for (int i = _boxes.Length - 1; i > 0; i--)
            _boxes[i].Text = _boxes[i - 1].Text;
        _boxes[0].Text = last;
    }

    // ── Process (Combinations) ────────────────────────────────────────────────

    private async void BtnProcess_Clicked(object sender, EventArgs e)
    {
        // Permutations mode: checkbox checked
        // Pool = all filled boxes; Pick = PICK (MaxNum) digits per combo
        if (chkBoxPerms.IsChecked)
        {
            if (!int.TryParse(MaxNum.Text, out int pickCount) || pickCount <= 0)
            {
                vm.StatusMessage = "Set PICK to the number of digits per combination";
                return;
            }
            if (!int.TryParse(HowMany.Text, out int fromCount) || fromCount <= 0)
            {
                vm.StatusMessage = "Set FROM to how many numbers are in your pool";
                return;
            }
            fromCount = Math.Min(fromCount, _boxes.Length);
            if (fromCount < pickCount)
            {
                vm.StatusMessage = $"FROM must be at least {pickCount} (the PICK value)";
                return;
            }
            var pool = new string[fromCount];
            for (int i = 0; i < fromCount; i++)
            {
                string v = _boxes[i].Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(v))
                {
                    vm.StatusMessage = $"Fill all {fromCount} boxes (box {i + 1} is empty)";
                    return;
                }
                pool[i] = v;
            }
            string sep = _mode == 2 ? "" : " ";
            var perms = GetOrderedPerms(pool, pickCount, sep);
            vm.Combinations = new System.Collections.ObjectModel.ObservableCollection<string>(perms);
            MainViewModel.SharedCombos = perms;
            vm.NumberInList = perms.Count;
            vm.StatusMessage = $"All {pickCount}-digit combos from {pool.Length} numbers — {perms.Count} total";
            vm.ActiveTab = 2;
            return;
        }

        if (!int.TryParse(MaxNum.Text, out int maxNum) || !int.TryParse(HowMany.Text, out int howMany)) return;

        int[] mNum = new int[100];
        for (int i = 0; i < _boxes.Length; i++)
        {
            if (int.TryParse(_boxes[i].Text, out int v))
                mNum[i + 1] = v;
        }
        // positions beyond the 12 boxes default to their sequential value
        for (int i = _boxes.Length + 1; i < 100; i++)
            mNum[i] = i;

        vm.IsLoading = true;
        long total = (long)Math.Round(vm.PossibleCombinations);
        vm.StatusMessage = $"Adding 0 of {total:N0}...";

        var progress = new Progress<int>(count =>
            vm.StatusMessage = $"Adding {count:N0} of {total:N0}...");

        try
        {
            var results = await Task.Run(() => vm.ComputeCombinationsAsync(mNum, maxNum, howMany, count => ((IProgress<int>)progress).Report(count)));
            vm.Combinations = new System.Collections.ObjectModel.ObservableCollection<string>(results);
            MainViewModel.SharedCombos = results;
            vm.NumberInList = results.Count;
            vm.StatusMessage = $"Complete — {results.Count:N0} of {total:N0} added";
            vm.ActiveTab = 2;
        }
        catch (Exception ex)
        {
            vm.StatusMessage = "Error: " + ex.Message;
            await DisplayAlert("Combos Error", ex.GetType().Name + ": " + ex.Message, "OK");
        }
        finally
        {
            vm.IsLoading = false;
        }
    }

    // ── Recurrence ────────────────────────────────────────────────────────────

    private void Recurrence_Changed(object sender, EventArgs e) { }

    private void BtnRecurrence_Clicked(object sender, EventArgs e)
    {
        string numbers = $"{Box1.Text} {Box2.Text} {Box3.Text} {Box4.Text} {Box5.Text}";
        string matchCount = cmbRecurrence.SelectedIndex >= 0
            ? cmbRecurrence.Items[cmbRecurrence.SelectedIndex]
            : "2";

        vm.SearchRecurrence(numbers, matchCount);
        vm.StatusMessage = $"Complete — {vm.NumberInList} matches found";
        vm.ActiveTab = 1;
    }

    // ── Draws toggle (reuses History listbox) ────────────────────────────────

    bool _showingDraws = false;

    private async void BtnDraws_Clicked(object sender, EventArgs e)
    {
        if (!_showingDraws)
        {
            if (vm.DrawsHistory.Count == 0)
                await vm.LoadDrawsAsync();
            vm.Picks = vm.DrawsHistory;
            vm.StatusMessage = $"Past draws — {vm.DrawsHistory.Count} records";
            _showingDraws = true;
        }
        else
        {
            await vm.LoadPicksAsync(_mode);
            vm.StatusMessage = $"My picks — {vm.Picks.Count} entries";
            _showingDraws = false;
        }
        vm.ActiveTab = 0;
    }

    // ── Winner page ───────────────────────────────────────────────────────────

    private async void BtnWinner_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        SavePreferences();
        AppShell.WinnerPageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(WinnerPage), false);
    }

    private async void BtnScan_Clicked(object sender, EventArgs e)
    {
        string result = await DisplayActionSheet("What are you scanning?", "Cancel", null,
            "Ticket (printed machine receipt)", "Slip (bubble form you filled in)");
        if (result?.Contains("Ticket") == true)
            await Shell.Current.GoToAsync(nameof(ScanTicketPage), false);
        else if (result?.Contains("Slip") == true)
            await Shell.Current.GoToAsync(nameof(ScanSlipPage), false);
    }

    private async void BtnSuperLotto_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        SavePreferences();
        SuperLottoPage.ComingFrom = "main";
        AppShell.SuperLottoPageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(SuperLottoPage), false);
    }

    private async void BtnDaily3_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        SavePreferences();
        Daily3Page.ComingFrom = "main";
        AppShell.Daily3PageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(Daily3Page), false);
    }

    private async void BtnPowerball_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        SavePreferences();
        AppShell.PowerballPageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(PowerballPage), false);
    }

    private async void BtnNavDropdown_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        string result = await DisplayActionSheet(null, "Cancel", null,
            "Fantasy 5", "Super Lotto", "Daily 3", "Daily 4", "Powerball", "Daily Derby");
        if (result == null || result == "Cancel") return;
        SavePreferences();
        switch (result)
        {
            case "Fantasy 5":
                AppShell.WinnerPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(WinnerPage), false);
                break;
            case "Super Lotto":
                SuperLottoPage.ComingFrom = "main";
                AppShell.SuperLottoPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(SuperLottoPage), false);
                break;
            case "Daily 3":
                Daily3Page.ComingFrom = "main";
                AppShell.Daily3PageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(Daily3Page), false);
                break;
            case "Daily 4":
                Daily4Page.ComingFrom = "main";
                AppShell.Daily4PageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(Daily4Page), false);
                break;
            case "Powerball":
                PowerballPage.ComingFrom = "main";
                AppShell.PowerballPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(PowerballPage), false);
                break;
            case "Daily Derby":
                DailyDerbyPage.ComingFrom = "main";
                AppShell.DailyDerbyPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(DailyDerbyPage), false);
                break;
        }
    }

    private async void BtnResults_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        SavePreferences();
        AppShell.ResultsPageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(ResultsPage), false);
    }

    private async void BtnViewSets_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        AppShell.ViewSetsPageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(ViewSetsPage), false);
    }

    // ── Tab switching ─────────────────────────────────────────────────────────

    private void TabHistory_Clicked(object sender, EventArgs e) => vm.ActiveTab = 0;
    private void TabRecurrence_Clicked(object sender, EventArgs e) => vm.ActiveTab = 1;
    private void TabCombos_Clicked(object sender, EventArgs e) => vm.ActiveTab = 2;

    private async void BtnMore_Clicked(object sender, EventArgs e)
    {
        string action = await DisplayActionSheet("More", "Cancel", null,
            "History", "Recurrence", "Combos", "View Sets", "Save", "Archive", "Data Files", "Export Sets");
        switch (action)
        {
            case "History":      vm.ActiveTab = 0; break;
            case "Recurrence":   vm.ActiveTab = 1; break;
            case "Combos":       vm.ActiveTab = 2; break;
            case "View Sets":    BtnViewSets_Clicked(sender, e); break;
            case "Save":         BtnSave_Clicked(sender, e); break;
            case "Archive":      BtnArchive_Clicked(sender, e); break;
            case "Data Files":   await Shell.Current.GoToAsync(nameof(DataViewerPage), false); break;
            case "Export Sets":  await Task.Delay(300); await ExportAllSetsAsync(); break;
        }
    }

    // ── Export All Sets ───────────────────────────────────────────────────────

    static readonly (string Caption, string PrefKey, int Rows, int Cols, int SpecialCol, string SpecialLabel, string AccentColor, int Stride, bool HasTime)[] ExportGames =
    {
        ("Fantasy 5",        "f5_set_", 10, 5, -1, "",     "#FF8F00", 5, false),
        ("Super Lotto Plus", "sl_set_", 10, 6,  5, "Mega", "#7B1FA2", 6, false),
        ("Daily 3",          "d3_set_", 10, 3, -1, "",     "#1565C0", 3, false),
        ("Daily 4",          "d4_set_", 10, 4, -1, "",     "#00695C", 4, false),
        ("Powerball",        "pb_set_", 10, 6,  5, "PB",   "#C62828", 6, false),
        ("Daily Derby",      "dd_set_", 10, 3, -1, "",     "#5D4037", 4, true),
    };

    private async Task ExportAllSetsAsync()
    {
        try
        {
        // Build list of all non-empty sets
        var available = new List<(string Label, int GameIdx, int Slot)>();
        for (int g = 0; g < ExportGames.Length; g++)
        {
            var game = ExportGames[g];
            for (int slot = 0; slot < 10; slot++)
            {
                string raw = Preferences.Get($"{game.PrefKey}{slot}", "");
                if (string.IsNullOrEmpty(raw)) continue;
                if (raw.Replace("|", "").Trim().Length == 0) continue;
                available.Add(($"{game.Caption} — Set {slot + 1}", g, slot));
            }
        }

        if (available.Count == 0) { vm.StatusMessage = "No sets saved to export"; return; }

        // Pick which set(s)
        var setOptions = available.Select(a => a.Label).Prepend("All Sets").ToArray();
        string? picked = await DisplayActionSheet("Select Set to Export", "Cancel", null, setOptions);
        if (picked == null || picked == "Cancel") return;

        // Pick format
        string? format = await DisplayActionSheet("Export Format", "Cancel", null,
            "Share as Text", "Print / Save PDF");
        if (format == null || format == "Cancel") return;

        // Determine which sets to export
        var toExport = picked == "All Sets"
            ? available
            : available.Where(a => a.Label == picked).ToList();

        if (format == "Share as Text")
        {
            var sb = new System.Text.StringBuilder();
            foreach (var (label, gameIdx, slot) in toExport)
            {
                var game = ExportGames[gameIdx];
                string raw = Preferences.Get($"{game.PrefKey}{slot}", "");
                var vals = raw.Split('|');
                int mainCols = game.SpecialCol >= 0 ? game.SpecialCol : game.Cols;

                sb.AppendLine(label);
                sb.AppendLine(new string('-', 32));
                for (int r = 0; r < game.Rows; r++)
                {
                    bool hasData = false;
                    for (int c = 0; c < game.Cols; c++)
                    {
                        int idx = r * game.Stride + c;
                        if (idx < vals.Length && !string.IsNullOrWhiteSpace(vals[idx])) { hasData = true; break; }
                    }
                    if (!hasData) continue;

                    sb.Append($"Row {r + 1,2}:  ");
                    for (int c = 0; c < mainCols; c++)
                    {
                        int idx = r * game.Stride + c;
                        string v = idx < vals.Length && !string.IsNullOrWhiteSpace(vals[idx]) ? vals[idx] : "-";
                        sb.Append($"{v,3}");
                    }
                    if (game.SpecialCol >= 0)
                    {
                        int idx = r * game.Stride + game.SpecialCol;
                        string v = idx < vals.Length && !string.IsNullOrWhiteSpace(vals[idx]) ? vals[idx] : "-";
                        sb.Append($"  | {game.SpecialLabel}: {v,2}");
                    }
                    if (game.HasTime)
                    {
                        int idx = r * game.Stride + 3;
                        if (idx < vals.Length && !string.IsNullOrWhiteSpace(vals[idx]))
                            sb.Append($"  Time: {vals[idx]}");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            string fileName = toExport.Count == 1 ? $"{toExport[0].Label.Replace(" — ", "_").Replace(" ", "_")}.txt" : "LotterySets.txt";
            string path = Path.Combine(FileSystem.CacheDirectory, fileName);
            await File.WriteAllTextAsync(path, sb.ToString());
            await Share.RequestAsync(new ShareFileRequest
            {
                Title = picked == "All Sets" ? "All Lottery Sets" : picked,
                File = new ShareFile(path, "text/plain")
            });
        }
        else if (format == "Print / Save PDF")
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(@"<!DOCTYPE html><html><head><meta charset='utf-8'><style>
body{font-family:Arial,sans-serif;padding:20px;color:#1E2733}
h2{padding-bottom:6px;margin-top:0}
table{border-collapse:collapse;width:100%;max-width:420px;margin-bottom:8px}
td{padding:6px 5px;text-align:center;font-size:15px;font-weight:bold}
.rownum{color:#9CA3AF;font-size:11px;font-weight:normal;text-align:right;width:26px}
.num{background:#F0F4F8;border-radius:4px;min-width:34px}
.special{color:white;border-radius:4px;min-width:34px}
</style></head><body>");

            foreach (var (label, gameIdx, slot) in toExport)
            {
                var game = ExportGames[gameIdx];
                string raw = Preferences.Get($"{game.PrefKey}{slot}", "");
                var vals = raw.Split('|');
                int mainCols = game.SpecialCol >= 0 ? game.SpecialCol : game.Cols;

                sb.Append($"<h2 style='color:{game.AccentColor};border-bottom:2px solid {game.AccentColor}'>{System.Net.WebUtility.HtmlEncode(label)}</h2><table>");
                for (int r = 0; r < game.Rows; r++)
                {
                    bool hasData = false;
                    for (int c = 0; c < game.Cols; c++)
                    {
                        int idx = r * game.Stride + c;
                        if (idx < vals.Length && !string.IsNullOrWhiteSpace(vals[idx])) { hasData = true; break; }
                    }
                    if (!hasData) continue;

                    sb.Append($"<tr><td class='rownum'>{r + 1}.</td>");
                    for (int c = 0; c < mainCols; c++)
                    {
                        int idx = r * game.Stride + c;
                        string v = idx < vals.Length && !string.IsNullOrWhiteSpace(vals[idx]) ? vals[idx] : "&mdash;";
                        sb.Append($"<td class='num'>{v}</td>");
                    }
                    if (game.SpecialCol >= 0)
                    {
                        int idx = r * game.Stride + game.SpecialCol;
                        string v = idx < vals.Length && !string.IsNullOrWhiteSpace(vals[idx]) ? vals[idx] : "&mdash;";
                        sb.Append($"<td class='special' style='background:{game.AccentColor}'>{v}</td>");
                    }
                    sb.Append("</tr>");
                }
                sb.Append("</table>");
            }

            sb.Append("</body></html>");
            string jobName = toExport.Count == 1 ? toExport[0].Label : "Lottery Sets";
#if ANDROID
            PrintHelper.PrintHtml(sb.ToString(), jobName);
#else
            await DisplayAlert("Print", "Printing is not supported on this platform.", "OK");
#endif
        }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Export Error", ex.Message, "OK");
        }
    }

    // ── Status bar: tap to copy ───────────────────────────────────────────────

    private async void StatusBar_Tapped(object sender, TappedEventArgs e)
    {
        string log = await Logger.ReadLogAsync();
        await Clipboard.Default.SetTextAsync(log);
        var orig = lblStatus.Text;
        lblStatus.Text = "Log copied to clipboard";
        await Task.Delay(1500);
        lblStatus.Text = orig;
    }

    // ── Insert Combos → checker page slots ───────────────────────────────────

    private async void BtnInsertToWinner_Clicked(object sender, EventArgs e)
    {
        if (vm.Combinations.Count == 0)
        {
            vm.StatusMessage = "No combos in list — generate combos first";
            return;
        }

        const int WRows = 10;
        const int TotalSlots = 10;
        int wCols         = _mode == 1 ? 6 : _mode == 0 ? 5 : 3;
        string slotPrefix = _mode == 0 ? "f5_set_" : _mode == 1 ? "sl_set_" : "d3_set_";
        string pageName   = _mode == 0 ? "F5 Winner" : _mode == 1 ? "Super Lotto" : "Daily 3";

        var combos = vm.Combinations.ToList();
        int comboIndex = 0;
        int insertedRows = 0;
        int insertedSlots = 0;

        for (int slot = 0; slot < TotalSlots && comboIndex < combos.Count; slot++)
        {
            string existing = Preferences.Get($"{slotPrefix}{slot}", "");
            bool isEmpty = string.IsNullOrEmpty(existing) ||
                           existing.Replace("|", "").Trim().Length == 0;
            if (!isEmpty) continue;

            var vals = new string[WRows * wCols];
            for (int i = 0; i < vals.Length; i++) vals[i] = "";

            int rowsFilled = 0;
            while (rowsFilled < WRows && comboIndex < combos.Count)
            {
                var parts = combos[comboIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int col = 0; col < wCols && col < parts.Length; col++)
                    vals[rowsFilled * wCols + col] = parts[col];
                rowsFilled++;
                comboIndex++;
                insertedRows++;
            }

            Preferences.Set($"{slotPrefix}{slot}", string.Join("|", vals));
            insertedSlots++;
        }

        string msg = insertedRows > 0
            ? $"Inserted {insertedRows} combos into {insertedSlots} set(s) on {pageName} page"
            : $"No empty sets available on {pageName} page";

        vm.StatusMessage = msg;

        if (sender is Button btn)
        {
            var orig = btn.Text;
            var origColor = btn.BackgroundColor;
            btn.Text = insertedRows > 0 ? $"✓ {insertedRows} inserted" : "No empty sets";
            btn.BackgroundColor = insertedRows > 0 ? Color.FromArgb("#2E7D32") : Color.FromArgb("#B71C1C");
            await Task.Delay(2000);
            btn.Text = orig;
            btn.BackgroundColor = origColor;
        }
    }

    // ── Permutations helper ───────────────────────────────────────────────────

    // All ordered arrangements of `pick` items from `pool` (e.g. 3-from-5)
    static List<string> GetOrderedPerms(string[] pool, int pick, string sep)
    {
        var results = new HashSet<string>();
        var current = new string[pick];
        var used = new bool[pool.Length];
        OrderedPermute(pool, pick, 0, used, current, results, sep);
        return results.ToList();
    }

    static void OrderedPermute(string[] pool, int pick, int depth, bool[] used, string[] current, HashSet<string> results, string sep)
    {
        if (depth == pick) { results.Add(string.Join(sep, current)); return; }
        for (int i = 0; i < pool.Length; i++)
        {
            if (used[i]) continue;
            used[i] = true;
            current[depth] = pool[i];
            OrderedPermute(pool, pick, depth + 1, used, current, results, sep);
            used[i] = false;
        }
    }

    // ── Generate Numbers ─────────────────────────────────────────────────────

    private async void BtnGenerateNumbers_Clicked(object sender, EventArgs e)
    {
        AppShell.GeneratePageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(GeneratePage), false);
    }

    // ── Archive ───────────────────────────────────────────────────────────────

    private async void BtnArchive_Clicked(object sender, EventArgs e)
    {
        var archives = ArchiveService.Load();
        bool hasArchives = archives.Count > 0;

        string action = await DisplayActionSheet(
            "Archive Sets", "Cancel", null,
            "Archive (save & clear sets)",
            hasArchives ? "Restore" : "Restore  (no archives)");

        if (action == null || action == "Cancel") return;

        if (action.StartsWith("Restore"))
        {
            if (!hasArchives) return; // nothing to restore
            await Shell.Current.GoToAsync(nameof(ArchivePage), false);
            return;
        }

        if (action == "Archive (save & clear sets)")
        {
            SavePreferences();
            ArchiveService.Archive($"Archive {DateTime.Now:MMM d, yyyy h:mm tt}");

            if (sender is Button btn)
            {
                var orig = btn.Text;
                var origColor = btn.BackgroundColor;
                btn.Text = "Archived!";
                btn.BackgroundColor = Color.FromArgb("#1B5E20");
                await Task.Delay(1000);
                btn.Text = orig;
                btn.BackgroundColor = origColor;
            }

            await Shell.Current.GoToAsync(nameof(ArchivePage), false);
        }
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private async void BtnSave_Clicked(object sender, EventArgs e)
    {
        SavePreferences();

        if (vm.ActiveTab == 2 && vm.Combinations.Count > 0)
        {
            try
            {
                var path = Path.Combine(FileSystem.AppDataDirectory, "myCombos.txt");
                var boxValues = string.Join(",", _boxes.Select(b => b.Text ?? ""));
                var lines = new List<string>
                {
                    $"#boxes:{boxValues}",
                    $"#params:{MaxNum.Text},{HowMany.Text}"
                };
                lines.AddRange(vm.Combinations);
                await File.WriteAllLinesAsync(path, lines);
                vm.StatusMessage = $"Saved {vm.Combinations.Count:N0} combos to myCombos.txt";
            }
            catch (Exception ex)
            {
                vm.StatusMessage = "Save error: " + ex.Message;
            }
        }

        if (sender is Button btn)
        {
            var orig = btn.Text;
            var origColor = btn.BackgroundColor;
            btn.Text = "Saved";
            btn.BackgroundColor = Color.FromArgb("#1B5E20");
            await Task.Delay(1200);
            btn.Text = orig;
            btn.BackgroundColor = origColor;
        }
    }
}
