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
    bool _boxesDirty = false; // true only after user edits a box; cleared after Save adds to list
    bool _savingPick = false; // suppresses History_SelectionChanged during Save
    Entry[] _boxes = null!;
    System.Collections.ObjectModel.ObservableCollection<string>? _picksRef;

    public MainPage()
    {
        InitializeComponent();
        BindingContext = vm;
        cmbRecurrence.SelectedIndex = 0;

        foreach (var entry in new[] { Box1, Box2, Box3, Box4, Box5, Box6, Box7,
                                      Box8, Box9, Box10, Box11, Box12, Box13, Box14, Box15,
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

    string PicksAutoSavePath => Path.Combine(FileSystem.AppDataDirectory,
        $"_autosave_{(_mode == 1 ? "SL" : _mode == 2 ? "D3" : "F5")}.txt");

    void ReHookPicksAutoSave()
    {
        if (_picksRef != null)
            _picksRef.CollectionChanged -= OnPicksAutoSave;
        _picksRef = vm.Picks;
        _picksRef.CollectionChanged += OnPicksAutoSave;
    }

    void OnPicksAutoSave(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (_showingDraws) return;
        try { File.WriteAllLines(PicksAutoSavePath, vm.Picks); } catch { }
    }

    static void HideKeyboard()
    {
#if ANDROID
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            activity?.CurrentFocus?.ClearFocus();
            var imm = activity?.GetSystemService(Android.Content.Context.InputMethodService)
                      as Android.Views.InputMethods.InputMethodManager;
            var token = activity?.Window?.DecorView?.WindowToken;
            if (token != null) imm?.HideSoftInputFromWindow(token, 0);
            activity?.Window?.DecorView?.RequestFocus();
        });
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_initialized)
        {
            // TranslationX was pre-set by the caller before navigating back — just animate in
            await this.TranslateTo(0, 0, 220, Easing.CubicOut);
            HideKeyboard();
            return;
        }
        _initialized = true;

        // Auto-purge expired advance plays on every app open
        _ = CheckAutoPurgeOnStartupAsync();

        // One-time: clear old bundled/hardcoded picks from autosave files
        if (!Preferences.Get("picks_cleared_v2", false))
        {
            foreach (var name in new[] { "_autosave_F5.txt", "_autosave_SL.txt", "_autosave_D3.txt" })
            {
                var path = Path.Combine(FileSystem.AppDataDirectory, name);
                if (File.Exists(path)) File.Delete(path);
            }
            Preferences.Set("picks_cleared_v2", true);
        }

        _boxes = new[] { Box1, Box2, Box3, Box4, Box5, Box6, Box7, Box8, Box9, Box10, Box11, Box12, Box13, Box14, Box15 };
        for (int i = 0; i < _boxes.Length; i++)
        {
            int idx = i;
            _boxes[i].TextChanged += (_, _) =>
            {
                if (_isRestoring) return;
                _boxesDirty = true;
                SavePreferences();
                int advanceLen = _mode == 2 ? 1 : 2;
                if (_boxes[idx].Text?.Length == advanceLen && idx + 1 < _boxes.Length)
                    _boxes[idx + 1].Focus();
            };
        }
        MaxNum.TextChanged += (_, _) => SavePreferences();
        HowMany.TextChanged += (_, _) => SavePreferences();
        _isRestoring = true;
        RestorePreferences();
        _isRestoring = false;
        await vm.LoadDataAsync();
        // Pre-warm Results cache in background so Results page loads instantly
        _ = ResultsPageCls.LoadAllDrawsAsync();
        // Load only the user's previously saved picks (autosave); start empty if none
        if (File.Exists(PicksAutoSavePath))
        {
            var saved = await File.ReadAllLinesAsync(PicksAutoSavePath);
            vm.Picks = new System.Collections.ObjectModel.ObservableCollection<string>(saved);
        }
        ReHookPicksAutoSave();
        // Enforce mode-correct PICK/FROM
        if (_mode == 2) { MaxNum.Text = "3"; }
        else if (_mode == 1) { MaxNum.Text = "6"; }
        UpdateBoxMaxLength(_mode);
        UpdateCombosLabel();
        if (int.TryParse(HowMany.Text, out int from)) HighlightBoxes(from);
        btnInsertToWinner.Text = _mode == 0 ? "Insert Combos → F5 Winner"
                                : _mode == 1 ? "Insert Combos → SL Winner"
                                :              "Insert Combos → Daily 3";
        UpdateModeButtons();
        UpdateRecurrencePicker(_mode);
        vm.ActiveTab = 0;
        await Task.Delay(300);
        foreach (var e in _boxes) e.Unfocus();
        MaxNum.Unfocus();
        HowMany.Unfocus();
        HideKeyboard();

        // Show in-app alert for advance tickets expiring today or tomorrow (launch only)
        AdvancePlayNotificationService.CheckAndNotify();
        if (AdvancePlayNotificationService.PendingLaunchTitle != null)
        {
            string title = AdvancePlayNotificationService.PendingLaunchTitle;
            string body  = AdvancePlayNotificationService.PendingLaunchBody ?? "";
            AdvancePlayNotificationService.ClearLaunchAlert();
            try { await DisplayAlert(title, body, "OK"); } catch { }
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SavePreferences();
        try { File.WriteAllLines(PicksAutoSavePath, vm.Picks); } catch { }
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
        Preferences.Set("gameMode", _mode);
        UpdateModeButtons();
        UpdateRecurrencePicker(mode);
        UpdateBoxMaxLength(mode);
        btnInsertToWinner.Text = mode == 0 ? "Insert Combos → F5 Winner"
                                : mode == 1 ? "Insert Combos → SL Winner"
                                :              "Insert Combos → Daily 3";
        if (File.Exists(PicksAutoSavePath))
        {
            var saved = await File.ReadAllLinesAsync(PicksAutoSavePath);
            vm.Picks = new System.Collections.ObjectModel.ObservableCollection<string>(saved);
        }
        else
        {
            vm.Picks = new System.Collections.ObjectModel.ObservableCollection<string>();
        }
        ReHookPicksAutoSave();
        UpdateCombosLabel();
    }

    private async void BtnModeF5_Clicked(object sender, EventArgs e) => await SwitchMode(0);
    private async void BtnModeSL_Clicked(object sender, EventArgs e) => await SwitchMode(1);
    private async void BtnModeD3_Clicked(object sender, EventArgs e) => await SwitchMode(2);

    // ── History selection → fill boxes ───────────────────────────────────────

    private void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_savingPick) return;
        if (_deleteMode)
        {
            int count = lstHistory.SelectedItems?.Count ?? 0;
            lblDeleteCount.Text = count == 0 ? "Tap items to select" : $"{count} selected";
            return;
        }
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
            _boxesDirty = false; // selecting an item to view doesn't arm Save
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
        if (_deletingCombos)
        {
            int count = lstCombos.SelectedItems?.Count ?? 0;
            lblDeleteCount.Text = count == 0 ? "Tap items to select" : $"{count} selected";
            return;
        }
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

    // ── Delete from overlay (single item) ────────────────────────────────────

    private void DeleteFromOverlay_Clicked(object sender, EventArgs e)
    {
        if (_reorderCollection != null && _reorderListIdx >= 0 && _reorderListIdx < _reorderCollection.Count)
        {
            string deleted = _reorderCollection[_reorderListIdx];
            _reorderCollection.RemoveAt(_reorderListIdx);
            vm.StatusMessage = $"Deleted: {deleted.Trim()}";
        }
        reorderOverlay.IsVisible = false;
        _reorderSourceList?.ClearValue(CollectionView.SelectedItemProperty);
    }

    // ── Multi-delete mode ─────────────────────────────────────────────────────

    bool _deleteMode = false;
    bool _deletingCombos = false;

    void EnterDeleteMode()
    {
        if (_showingDraws) { vm.StatusMessage = "Switch to My Picks to delete items"; return; }
        _deleteMode = true;
        _deletingCombos = vm.ActiveTab == 2;
        if (_deletingCombos)
        {
            lstCombos.SelectionMode = SelectionMode.Multiple;
        }
        else
        {
            vm.ActiveTab = 0;
            lstHistory.SelectionMode = SelectionMode.Multiple;
        }
        lblDeleteCount.Text = "Tap items to select";
        deleteModeBar.IsVisible = true;
    }

    private void ExitDeleteMode_Clicked(object sender, EventArgs e)
    {
        _deleteMode = false;
        if (_deletingCombos)
        {
            lstCombos.SelectionMode = SelectionMode.Single;
            lstCombos.ClearValue(CollectionView.SelectedItemsProperty);
        }
        else
        {
            lstHistory.SelectionMode = SelectionMode.Single;
            lstHistory.ClearValue(CollectionView.SelectedItemsProperty);
        }
        _deletingCombos = false;
        deleteModeBar.IsVisible = false;
    }

    private async void DeleteSelectedItems_Clicked(object sender, EventArgs e)
    {
        if (_deletingCombos)
        {
            var selected = lstCombos.SelectedItems?.Cast<string>().ToList();
            if (selected == null || selected.Count == 0)
            {
                vm.StatusMessage = "Tap items in the list to select them first";
                return;
            }
            bool confirm = await DisplayAlert("Delete", $"Delete {selected.Count} combo(s)?", "Delete", "Cancel");
            if (!confirm) return;

            foreach (var item in selected)
                vm.Combinations.Remove(item);

            vm.StatusMessage = $"Deleted {selected.Count} combo(s) — {vm.Combinations.Count} remaining";
            lstCombos.ClearValue(CollectionView.SelectedItemsProperty);
            lblDeleteCount.Text = "Tap items to select";
            return;
        }

        var selectedPicks = lstHistory.SelectedItems?.Cast<string>().ToList();
        if (selectedPicks == null || selectedPicks.Count == 0)
        {
            vm.StatusMessage = "Tap items in the list to select them first";
            return;
        }
        bool confirmPicks = await DisplayAlert("Delete", $"Delete {selectedPicks.Count} item(s)?", "Delete", "Cancel");
        if (!confirmPicks) return;

        foreach (var item in selectedPicks)
            vm.Picks.Remove(item);

        vm.StatusMessage = $"Deleted {selectedPicks.Count} item(s) — {vm.Picks.Count} remaining";
        lstHistory.ClearValue(CollectionView.SelectedItemsProperty);
        lblDeleteCount.Text = "Tap items to select";
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
        if (int.TryParse(HowMany.Text, out int n))
        {
            if (n > 15)
            {
                HowMany.Text = "15";
                return;
            }
            HighlightBoxes(n);
        }
        else
            HighlightBoxes(0);
        UpdateCombosLabel();
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
        {
            _boxes[i].BackgroundColor = (i < count) ? Color.FromArgb("#FFF176") : Color.FromArgb("#D6E8FF");
            _boxes[i].TextColor       = Colors.Black;
        }
    }

    // ── Clear ─────────────────────────────────────────────────────────────────

    private void BtnClear_Clicked(object sender, EventArgs e)
    {
        if (_boxes == null) return;
        foreach (var box in _boxes) box.Text = "";
        int.TryParse(HowMany.Text, out int from);
        HighlightBoxes(from);
    }

    // ── Smart Quick Pick ──────────────────────────────────────────────────────

    int _qpStrategy = 0; // 0=Hot, 1=Cold, 2=MostFreq, 3=LeastFreq, 4=Gap

    private void BtnQuickPick_Clicked(object sender, EventArgs e)
    {
        if (_boxes == null) return;
        string gameName = _mode == 0 ? "Fantasy 5" : _mode == 1 ? "Super Lotto" : "Daily 3";
        qpTitle.Text = $"🎲 Smart Quick Pick — {gameName}";
        _qpStrategy = 0;
        UpdateQpStrategyButtons();
        // FROM box drives how many numbers to generate — mirror it into the dialog
        qpFrom.Text = HowMany.Text;
        qpProgressArea.IsVisible = false;
        qpSpinner.IsRunning = false;
        qpGenerateBtn.IsEnabled = true;
        qpCancelBtn.IsEnabled = true;
        qpOverlay.IsVisible = true;
    }

    private void UpdateQpStrategyButtons()
    {
        var btns = new[] { qpBtnHot, qpBtnCold, qpBtnFreq, qpBtnRare, qpBtnGap };
        for (int i = 0; i < btns.Length; i++)
        {
            btns[i].BackgroundColor = i == _qpStrategy
                ? Color.FromArgb("#6D28D9") : Color.FromArgb("#1E3A5F");
            btns[i].TextColor = i == _qpStrategy
                ? Colors.White : Color.FromArgb("#94B8D8");
        }
        qpLastNRow.IsVisible = (_qpStrategy == 0);
    }

    private void QpStrategy_Hot(object sender, EventArgs e)  { _qpStrategy = 0; UpdateQpStrategyButtons(); }
    private void QpStrategy_Cold(object sender, EventArgs e) { _qpStrategy = 1; UpdateQpStrategyButtons(); }
    private void QpStrategy_Freq(object sender, EventArgs e) { _qpStrategy = 2; UpdateQpStrategyButtons(); }
    private void QpStrategy_Rare(object sender, EventArgs e) { _qpStrategy = 3; UpdateQpStrategyButtons(); }
    private void QpStrategy_Gap(object sender, EventArgs e)  { _qpStrategy = 4; UpdateQpStrategyButtons(); }

    private void QpCancel_Clicked(object sender, EventArgs e) => qpOverlay.IsVisible = false;

    private async void QpGenerate_Clicked(object sender, EventArgs e)
    {
        qpGenerateBtn.IsEnabled = false;
        qpCancelBtn.IsEnabled  = false;
        qpProgressArea.IsVisible = true;
        qpSpinner.IsRunning = true;

        // FROM box = how many numbers to generate; game range is always fixed (F5=39, SL=47, D3=9)
        if (!int.TryParse(qpFrom.Text, out int pickCount) || pickCount <= 0)
            pickCount = _mode == 2 ? 3 : _mode == 1 ? 6 : 5;
        int gameMax = _mode == 1 ? 47 : _mode == 2 ? 9 : 39;
        pickCount = Math.Min(pickCount, _boxes.Length);

        int.TryParse(qpLastN.Text, out int lastN);
        if (lastN <= 0) lastN = 100;

        var start = DateTime.UtcNow;
        List<int>? result = null;

        try
        {
            result = await SmartQuickPick.RunAsync(
                _mode, (SmartQuickPick.Strategy)_qpStrategy, pickCount, gameMax, lastN,
                msg => qpProgressMsg.Text = msg);
        }
        catch (Exception ex)
        {
            qpSpinner.IsRunning = false;
            qpOverlay.IsVisible = false;
            await DisplayAlert("QP Error", ex.Message, "OK");
            return;
        }

        // Guarantee at least 10 seconds elapsed since the user hit Generate
        var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;
        if (elapsed < 10000)
        {
            qpProgressMsg.Text = "Finalizing selection...";
            await Task.Delay((int)(10000 - elapsed));
        }

        qpSpinner.IsRunning = false;
        qpOverlay.IsVisible = false;

        _isRestoring = true;
        for (int i = 0; i < _boxes.Length; i++)
            _boxes[i].Text = i < result.Count ? result[i].ToString() : "";
        _isRestoring = false;
        _boxesDirty = true;
        SavePreferences();

        string stratLabel = _qpStrategy switch
        {
            0 => $"Hot (last {lastN} draws)",
            1 => "Cold/Overdue",
            2 => "Most Frequent",
            3 => "Least Drawn",
            4 => "Longest Gap",
            _ => "Smart"
        };
        vm.StatusMessage = $"Smart QP [{stratLabel}]: {string.Join(", ", result)}";
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
        string matchCount = cmbRecurrence.SelectedIndex >= 0
            ? cmbRecurrence.Items[cmbRecurrence.SelectedIndex]
            : "2";

        if (_mode == 1) // Super Lotto: box1–5 main, box6 = Mega/bonus
        {
            string numbers = $"{Box1.Text} {Box2.Text} {Box3.Text} {Box4.Text} {Box5.Text} {Box6.Text}";
            vm.SearchRecurrenceSL(numbers, matchCount);
        }
        else if (_mode == 2) // Daily 3: box1–3
        {
            string numbers = $"{Box1.Text} {Box2.Text} {Box3.Text}";
            vm.SearchRecurrenceD3(numbers, matchCount);
        }
        else // Fantasy 5: box1–5
        {
            string numbers = $"{Box1.Text} {Box2.Text} {Box3.Text} {Box4.Text} {Box5.Text}";
            vm.SearchRecurrence(numbers, matchCount);
        }

        vm.StatusMessage = $"Complete — {vm.NumberInList} matches found";
        vm.ActiveTab = 1;
    }

    private void UpdateBoxMaxLength(int mode)
    {
        if (_boxes == null) return;
        int maxLen = mode == 2 ? 1 : 2;
        foreach (var box in _boxes)
            box.MaxLength = maxLen;
    }

    private void UpdateRecurrencePicker(int mode)
    {
        string current = cmbRecurrence.SelectedIndex >= 0
            ? cmbRecurrence.Items[cmbRecurrence.SelectedIndex] : "2";
        cmbRecurrence.Items.Clear();
        cmbRecurrence.Items.Add("2");
        cmbRecurrence.Items.Add("3");
        if (mode != 2) cmbRecurrence.Items.Add("4"); // D3 max is 3
        if (mode != 2) cmbRecurrence.Items.Add("5");
        if (mode == 1) cmbRecurrence.Items.Add("6");
        int idx = cmbRecurrence.Items.IndexOf(current);
        cmbRecurrence.SelectedIndex = idx >= 0 ? idx : 0;
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
        vm.IsLoading = true;
        await Task.Yield(); // let spinner render before navigation work starts
        AppShell.WinnerPageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(WinnerPage), false);
        vm.IsLoading = false;
    }

    private async void BtnSuperLotto_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        SavePreferences();
        vm.IsLoading = true;
        await Task.Yield();
        SuperLottoPage.ComingFrom = "main";
        AppShell.SuperLottoPageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(SuperLottoPage), false);
        vm.IsLoading = false;
    }

    private async void BtnDaily3_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        SavePreferences();
        vm.IsLoading = true;
        await Task.Yield();
        Daily3Page.ComingFrom = "main";
        AppShell.Daily3PageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(Daily3Page), false);
        vm.IsLoading = false;
    }

    private async void BtnPowerball_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        SavePreferences();
        vm.IsLoading = true;
        await Task.Yield();
        AppShell.PowerballPageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(PowerballPage), false);
        vm.IsLoading = false;
    }

    private async void BtnNavDropdown_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        string result = await DisplayActionSheet(null, "Cancel", null,
            "Fantasy 5", "Super Lotto", "Daily 3", "Daily 4", "Powerball", "Mega Millions", "Daily Derby", "Jackpot Winners");
        if (result == null || result == "Cancel") return;
        SavePreferences();
        vm.IsLoading = true;
        await Task.Yield(); // let spinner render before navigation work starts
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
            case "Mega Millions":
                MegaMillionsPage.ComingFrom = "main";
                AppShell.MegaMillionsPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(MegaMillionsPage), false);
                break;
            case "Daily Derby":
                DailyDerbyPage.ComingFrom = "main";
                AppShell.DailyDerbyPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(DailyDerbyPage), false);
                break;
            case "Jackpot Winners":
                AppShell.JackpotPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(JackpotPage), false);
                break;
        }
        vm.IsLoading = false;
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

    private async void BtnOptions_Clicked(object sender, EventArgs e)
    {
        string action = await DisplayActionSheet("Options", "Cancel", null,
            "Data Files", "My Favorites", "Load Picks", "Generate Numbers",
            "Clear List", "Delete Multiple...", "Search Sets", "Check Wins for Draw#");
        switch (action)
        {
            case "Data Files":              await Shell.Current.GoToAsync(nameof(DataViewerPage), false); break;
            case "My Favorites":            await Shell.Current.GoToAsync(nameof(MyFavoritePage), false); break;
            case "Load Picks":              await Task.Delay(300); await LoadPicksFromFileAsync(); break;
            case "Generate Numbers":        BtnGenerateNumbers_Clicked(sender, e); break;
            case "Search Sets":             await Task.Delay(300); await SearchSetsAsync(); break;
            case "Check Wins for Draw#":    DrawSearchPage.PresetGame = "Daily 3"; await Shell.Current.GoToAsync(nameof(DrawSearchPage), false); break;
            case "Clear List":
                if (vm.Picks.Count == 0) { vm.StatusMessage = "List is already empty"; break; }
                bool confirmClear = await DisplayAlert("Clear List", $"Remove all {vm.Picks.Count} items from the list?", "Clear All", "Cancel");
                if (confirmClear)
                {
                    vm.Picks.Clear();
                    _boxesDirty = false;
                    try { File.WriteAllLines(PicksAutoSavePath, vm.Picks); } catch { }
                    vm.StatusMessage = "List cleared";
                }
                break;
            case "Delete Multiple...":
                EnterDeleteMode();
                break;
        }
    }

    private async void BtnSummary_Clicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync(nameof(SummaryPage), false);

    private async void BtnAdvance_Clicked(object sender, EventArgs e)
    {
        string action = await DisplayActionSheet("Advance", "Cancel", null,
            "View Sets", "Archive", "Export Sets", "Refresh Data",
            "Voice Settings", "Notifications", "Games Expiration", "Purge All Games",
            "Backup Data", "Restore Data");
        switch (action)
        {
            case "View Sets":        BtnViewSets_Clicked(sender, e); break;
            case "Archive":          BtnArchive_Clicked(sender, e); break;
            case "Export Sets":      await Task.Delay(300); await ExportAllSetsAsync(); break;
            case "Refresh Data":     await vm.RefreshAllDataAsync(); break;
            case "Voice Settings":   await ShowVoiceSettingsAsync(); break;
            case "Notifications":    await Shell.Current.GoToAsync(nameof(NotificationsPage), false); break;
            case "Games Expiration": await Shell.Current.GoToAsync(nameof(AdvanceGamesPage), false); break;
            case "Purge All Games":  await PurgeAllGamesAsync(); break;
            case "Backup Data":      await BackupDataAsync(); break;
            case "Restore Data":     await RestoreDataAsync(); break;
        }
    }

    // ── Purge helpers ─────────────────────────────────────────────────────────

    static readonly (string prefix, int cols, string[] extra, string[] tempKeys)[] _purgeGames =
    [
        ("f5", 5,  Array.Empty<string>(),                         new[] { "f5_entries" }),
        ("sl", 6,  Array.Empty<string>(),                         new[] { "sl_entries" }),
        ("pb", 6,  Array.Empty<string>(),                         new[] { "pb_entries" }),
        ("mm", 6,  Array.Empty<string>(),                         new[] { "mm_entries" }),
        ("d3", 3,  new[] { "d3_btypes_", "d3_drawfilters_" },    new[] { "d3_entries", "d3_bettypes", "d3_drawfilters" }),
        ("d4", 4,  new[] { "d4_btypes_" },                       new[] { "d4_entries", "d4_bettypes" }),
        ("dd", 4,  Array.Empty<string>(),                         new[] { "dd_entries" }),
    ];

    /// <summary>
    /// Deletes all expired rows (Play To &lt; today, or draw# &lt; calottery current) that have no recorded win.
    /// Returns the number of rows deleted.
    /// </summary>
    static async Task<int> ExecutePurgeAsync(Dictionary<string, int>? currentDrawNumbers = null)
    {
        var winRecords = await SummaryPage.LoadAllAsync();
        var today = DateTime.Today;
        int deleted = 0;

        foreach (var (prefix, cols, extra, tempKeys) in _purgeGames)
        {
            string gameKey = prefix.ToUpper();
            int activeSlot = Preferences.Get($"{prefix}_active_slot", -1);
            int currentDn = 0;
            currentDrawNumbers?.TryGetValue(prefix, out currentDn);

            for (int i = 0; i < 10; i++)
            {
                string setKey = $"{prefix}_set_{i}";
                string advKey = $"{prefix}_adv_{i}";
                string raw = Preferences.Get(setKey, "");
                if (string.IsNullOrEmpty(raw)) continue;

                var vals = raw.Split('|');
                string advRaw = Preferences.Get(advKey, "");
                var advParts = string.IsNullOrEmpty(advRaw) ? new string[10] : advRaw.Split('|');
                if (advParts.Length < 10) Array.Resize(ref advParts, 10);

                bool anyKept = false;
                for (int r = 0; r < 10; r++)
                {
                    // Default: keep everything unless it has an expired advance date or past draw#
                    bool keep = true;
                    string winPrefix = $"auto_{gameKey}_{i + 1}_{r + 1}_";
                    bool hasWin = winRecords.Any(w => w.SourceKey.StartsWith(winPrefix));

                    if (r < advParts.Length && advParts[r] != null)
                    {
                        var pair = advParts[r].Split('~');
                        if (pair.Length >= 2)
                        {
                            DateTime? end = null, start = null;
                            if (DateTime.TryParseExact(pair[1], "yyyyMMdd", null,
                                    System.Globalization.DateTimeStyles.None, out var ed)) end = ed;
                            if (DateTime.TryParseExact(pair[0], "yyyyMMdd", null,
                                    System.Globalization.DateTimeStyles.None, out var sd)) start = sd;
                            var refDate = end ?? start;
                            if (refDate.HasValue)
                                // Delete all expired rows — wins are pre-saved to SummaryPage before purge runs
                                keep = refDate.Value.Date >= today;

                            // Also purge if the stored draw# (higher of start/end) is already past — but never purge winners.
                            // If no draw# is stored, skip this check and rely on date only.
                            if (keep && !hasWin && currentDn > 0 && pair.Length >= 3)
                            {
                                string storedDnStr = pair.Length >= 4 && !string.IsNullOrWhiteSpace(pair[3])
                                    ? pair[3] : pair[2];
                                if (!string.IsNullOrWhiteSpace(storedDnStr) &&
                                    int.TryParse(storedDnStr, out int storedDn) && storedDn > 0 && storedDn < currentDn)
                                    keep = false;
                            }
                        }
                    }

                    if (!keep)
                    {
                        deleted++;
                        for (int c = 0; c < cols; c++)
                            if (r * cols + c < vals.Length) vals[r * cols + c] = "";
                        if (r < advParts.Length) advParts[r] = "~";
                    }
                    else anyKept = true;
                }

                string newData = string.Join("|", vals);
                if (newData.Replace("|", "").Trim().Length == 0)
                {
                    Preferences.Remove(setKey);
                    Preferences.Remove(advKey);
                    foreach (var ex in extra) Preferences.Remove($"{ex}{i}");
                    // If this was the active slot, clear the temp in-memory keys too
                    // so the game page doesn't restore stale data from its temp cache
                    if (i == activeSlot)
                        foreach (var tk in tempKeys) Preferences.Remove(tk);
                }
                else
                {
                    Preferences.Set(setKey, newData);
                    Preferences.Set(advKey, string.Join("|", advParts));
                }
            }
        }

        return deleted;
    }

    /// <summary>
    /// Scans the last 30 days for any advance-ticket wins and saves them to SummaryPage
    /// BEFORE the purge runs, so the purge's hasWin check is accurate.
    /// </summary>
    static async Task PreSaveAdvanceWinsAsync()
    {
        ResultsPageCls.ClearCache();
        for (int d = 0; d <= 30; d++)
        {
            var date = DateTime.Today.AddDays(-d);
            try
            {
                var result = await ResultsPageCls.ProcessDateAsync(date);
                foreach (var aw in result.Winners.Where(w => !w.IsActiveNoWin && !string.IsNullOrEmpty(w.Prize)))
                {
                    if (!DateTime.TryParse(aw.DrawDate, out var awDate) || awDate == default)
                        awDate = date;
                    string sk = $"auto_{aw.Game}_{aw.SetNumber}_{aw.RowNumber}_{awDate:yyyyMMdd}";
                    var (awAmt, _, _, awFree) = ResultsPage.ParsePrize(aw.Prize);
                    await SummaryPage.AddWinAsync(new WinningRecord
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
            }
            catch { }
        }
    }

    /// <summary>
    /// Checks on app open for expired plays and auto-purges them, showing a popup if anything was removed.
    /// </summary>
    async Task CheckAutoPurgeOnStartupAsync()
    {
        await PreSaveAdvanceWinsAsync();   // detect wins BEFORE purging
        var currentDrawNumbers = await GetDataEntry.GetCurrentDrawNumbersAsync();
        int deleted = await ExecutePurgeAsync(currentDrawNumbers);
        if (deleted > 0)
            await DisplayAlert("Auto-Purge",
                $"{deleted} expired play{(deleted == 1 ? "" : "s")} with no win were automatically removed.",
                "OK");
    }

    private async Task PurgeAllGamesAsync()
    {
        bool confirm = await DisplayAlert("Purge All Games",
            "Clear all rows in every game? Rows with future advance dates or recorded wins will be preserved.",
            "Yes, Purge", "Cancel");
        if (!confirm) return;

        var currentDrawNumbers = await GetDataEntry.GetCurrentDrawNumbersAsync();
        int deleted = await ExecutePurgeAsync(currentDrawNumbers);
        string msg = deleted > 0
            ? $"Done. {deleted} expired row{(deleted == 1 ? "" : "s")} removed."
            : "Nothing to purge — all rows are active or have wins.";
        await DisplayAlert("Purge Complete", msg, "OK");
    }

    // ── Backup / Restore ──────────────────────────────────────────

    static readonly (string prefix, int cols, string[] extras)[] _backupGames =
    [
        ("f5", 5,  Array.Empty<string>()),
        ("sl", 6,  Array.Empty<string>()),
        ("pb", 6,  Array.Empty<string>()),
        ("mm", 6,  Array.Empty<string>()),
        ("d3", 3,  new[] { "d3_btypes_", "d3_drawfilters_" }),
        ("d4", 4,  new[] { "d4_btypes_" }),
        ("dd", 4,  Array.Empty<string>()),
    ];

    static readonly string[] _scalarKeys =
    [
        "gameMode", "maxnum", "howmany",
        "voice_silence_ms", "voice_min_ms", "voice_post_ms", "voice_mute_beep",
        "notif_enabled", "notif_phone", "notif_sms_enabled", "notif_times",
        "win_alert_enabled", "win_check_times",
        "win_interval_enabled", "win_interval_start", "win_interval_end",
        "win_interval_minutes", "win_min_amount",
        "fantasy5_game_id", "sl_game_id", "mm_game_id",
    ];

    private async Task BackupDataAsync()
    {
        try
        {
            var root = new System.Text.Json.Nodes.JsonObject();

            // Per-game slot data
            var gamesNode = new System.Text.Json.Nodes.JsonObject();
            foreach (var (prefix, _, extras) in _backupGames)
            {
                var slots = new System.Text.Json.Nodes.JsonObject();
                for (int i = 0; i < 10; i++)
                {
                    string setKey = $"{prefix}_set_{i}";
                    string advKey = $"{prefix}_adv_{i}";
                    string setRaw = Preferences.Get(setKey, "");
                    string advRaw = Preferences.Get(advKey, "");
                    if (string.IsNullOrEmpty(setRaw) && string.IsNullOrEmpty(advRaw)) continue;

                    var slot = new System.Text.Json.Nodes.JsonObject();
                    if (!string.IsNullOrEmpty(setRaw)) slot["set"] = setRaw;
                    if (!string.IsNullOrEmpty(advRaw)) slot["adv"] = advRaw;

                    foreach (var ex in extras)
                    {
                        string exRaw = Preferences.Get($"{ex}{i}", "");
                        if (!string.IsNullOrEmpty(exRaw))
                            slot[ex.TrimEnd('_')] = exRaw;
                    }
                    slots[$"{i}"] = slot;
                }
                if (slots.Count > 0) gamesNode[prefix] = slots;
            }
            root["games"] = gamesNode;

            // Scalar prefs
            var scalars = new System.Text.Json.Nodes.JsonObject();
            foreach (var key in _scalarKeys)
            {
                // Try int, bool, string in that order
                try   { scalars[key] = Preferences.Get(key, int.MinValue); if ((int)scalars[key]! == int.MinValue) scalars.Remove(key); continue; } catch { }
                try   { scalars[key] = Preferences.Get(key, false);        continue; } catch { }
                      { var s = Preferences.Get(key, ""); if (!string.IsNullOrEmpty(s)) scalars[key] = s; }
            }
            root["scalars"] = scalars;

            root["version"]   = 1;
            root["backed_up"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            string json     = root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            string fileName = $"DailyFantasy_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json";

            // Write to cache dir then share — no storage permission needed on any Android version
            string path = Path.Combine(FileSystem.CacheDirectory, fileName);
            await File.WriteAllTextAsync(path, json);

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "Save Backup",
                File  = new ShareFile(path, "application/json"),
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Backup Failed", ex.Message, "OK");
        }
    }

    private async Task RestoreDataAsync()
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle    = "Select backup JSON file",
                FileTypes      = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "application/json", "*/*" } },
                }),
            });

            if (result == null) return;

            bool confirm = await DisplayAlert("Restore Data",
                $"This will overwrite your current data with the backup.\n\nFile: {result.FileName}\n\nContinue?",
                "Yes, Restore", "Cancel");
            if (!confirm) return;

            string json = await File.ReadAllTextAsync(result.FullPath);
            var root    = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject();
            if (root == null) { await DisplayAlert("Error", "Invalid backup file.", "OK"); return; }

            int restored = 0;

            // Per-game slot data
            if (root["games"] is System.Text.Json.Nodes.JsonObject gamesNode)
            {
                foreach (var (prefix, _, extras) in _backupGames)
                {
                    if (gamesNode[prefix] is not System.Text.Json.Nodes.JsonObject slots) continue;
                    foreach (var (idxStr, slotNode) in slots)
                    {
                        if (!int.TryParse(idxStr, out int i)) continue;
                        if (slotNode is not System.Text.Json.Nodes.JsonObject slot) continue;

                        if (slot["set"]?.GetValue<string>() is string setRaw)
                        { Preferences.Set($"{prefix}_set_{i}", setRaw); restored++; }
                        if (slot["adv"]?.GetValue<string>() is string advRaw)
                        { Preferences.Set($"{prefix}_adv_{i}", advRaw); restored++; }

                        foreach (var ex in extras)
                        {
                            string exKey = ex.TrimEnd('_');
                            if (slot[exKey]?.GetValue<string>() is string exRaw)
                                Preferences.Set($"{ex}{i}", exRaw);
                        }
                    }
                }
            }

            // Scalar prefs — store as strings; each page re-reads with its own default type
            if (root["scalars"] is System.Text.Json.Nodes.JsonObject scalars)
            {
                foreach (var (key, val) in scalars)
                {
                    if (val == null) continue;
                    var kind = val.GetValue<System.Text.Json.JsonElement>().ValueKind;
                    if (kind == System.Text.Json.JsonValueKind.True || kind == System.Text.Json.JsonValueKind.False)
                        Preferences.Set(key, val.GetValue<bool>());
                    else if (kind == System.Text.Json.JsonValueKind.Number)
                        Preferences.Set(key, val.GetValue<int>());
                    else
                        Preferences.Set(key, val.GetValue<string>() ?? "");
                    restored++;
                }
            }

            await DisplayAlert("Restore Complete", $"Restored {restored} items. Restart the app for all changes to take effect.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Restore Failed", ex.Message, "OK");
        }
    }

    async Task ShowVoiceSettingsAsync()
    {
        int silenceMs = Preferences.Get("voice_silence_ms", 150);
        int minMs     = Preferences.Get("voice_min_ms", 100);
        int postMs    = Preferences.Get("voice_post_ms", 50);
        bool muteBeep = Preferences.Get("voice_mute_beep", true);

        string? setting = await DisplayActionSheet("🎤 Voice Settings", "Done", null,
            $"Silence Timeout: {silenceMs}ms",
            $"Min Speech: {minMs}ms",
            $"Post-Number Delay: {postMs}ms",
            $"Mute Beep: {(muteBeep ? "ON ✓" : "OFF")}",
            "Reset to Defaults");

        if (setting == null || setting == "Done") return;

        if (setting.StartsWith("Silence Timeout"))
        {
            string? v = await DisplayActionSheet("Silence Timeout\n(how long after you stop speaking)", "Cancel", null,
                "150ms — fastest", "200ms", "300ms — default", "400ms", "500ms", "700ms", "1000ms — slowest");
            if (v != null && v != "Cancel")
                Preferences.Set("voice_silence_ms", int.Parse(v.Split('m')[0]));
        }
        else if (setting.StartsWith("Min Speech"))
        {
            string? v = await DisplayActionSheet("Min Speech Length\n(minimum time to listen)", "Cancel", null,
                "50ms", "100ms — default", "200ms", "300ms", "500ms");
            if (v != null && v != "Cancel")
                Preferences.Set("voice_min_ms", int.Parse(v.Split('m')[0]));
        }
        else if (setting.StartsWith("Post-Number"))
        {
            string? v = await DisplayActionSheet("Post-Number Delay\n(gap before listening again)", "Cancel", null,
                "0ms", "50ms — default", "100ms", "200ms", "300ms");
            if (v != null && v != "Cancel")
                Preferences.Set("voice_post_ms", int.Parse(v.Split('m')[0]));
        }
        else if (setting.StartsWith("Mute Beep"))
        {
            Preferences.Set("voice_mute_beep", !muteBeep);
        }
        else if (setting == "Reset to Defaults")
        {
            Preferences.Set("voice_silence_ms", 300);
            Preferences.Set("voice_min_ms", 100);
            Preferences.Set("voice_post_ms", 50);
            Preferences.Set("voice_mute_beep", true);
        }

        await ShowVoiceSettingsAsync();
    }

    // ── Search Sets ───────────────────────────────────────────────────────────

    private async Task SearchSetsAsync()
    {
        // Pick which game to search
        string game = await DisplayActionSheet("Search which game?", "Cancel", null,
            "All Games",
            "Fantasy 5", "Super Lotto Plus", "Daily 3", "Daily 4",
            "Powerball", "Mega Millions", "Daily Derby");
        if (string.IsNullOrEmpty(game) || game == "Cancel") return;

        string lastInput = "";

        while (true)
        {
        // Prompt for numbers to search (pre-filled with last search)
        string? input = await DisplayPromptAsync(
            "Search Sets",
            "Enter numbers (use ; to search multiple rows)\n(e.g.  5 12 27  or  5 12; 8 15 33)",
            "Search", "Cancel",
            initialValue: lastInput,
            placeholder: "e.g. 5 12 27",
            keyboard: Keyboard.Default);

        if (string.IsNullOrWhiteSpace(input)) return;
        lastInput = input;

        // Split by ";" to allow multiple searches in one go
        var searches = input.Split(';', StringSplitOptions.RemoveEmptyEntries);

        var gamesToSearch = game == "All Games"
            ? ExportGames
            : ExportGames.Where(g => g.Caption == game).ToArray();

        var results = new System.Text.StringBuilder();
        int totalHits = 0;

        foreach (var search in searches)
        {
            var searchNums = search
                .Split(new[] { ' ', ',', '-', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim().TrimStart('0'))
                .Where(s => s.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (searchNums.Count == 0) continue;

            results.AppendLine($"══ {string.Join(", ", searchNums)} ══");
            int searchHits = 0;

            foreach (var (caption, prefKey, rows, cols, specialCol, specialLabel, _, stride, hasTime) in gamesToSearch)
            {
                var gameHits = new List<string>();

                for (int slot = 0; slot < rows; slot++)
                {
                    string raw = Preferences.Get($"{prefKey}{slot}", "");
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    var parts = raw.Split('|');
                    int rowCount = parts.Length / stride;
                    for (int r = 0; r < rowCount; r++)
                    {
                        var rowNums = parts
                            .Skip(r * stride)
                            .Take(stride)
                            .Select(v => v.Trim().TrimStart('0'))
                            .Where(v => v.Length > 0)
                            .ToList();

                        var matched = rowNums.Where(n => searchNums.Contains(n)).ToList();
                        if (matched.Count < searchNums.Count) continue;

                        string rowDisplay = string.Join("  ", rowNums.Select(n =>
                            matched.Contains(n) ? $"[{n}]" : n));
                        gameHits.Add($"  Set {slot + 1}, Row {r + 1}: {rowDisplay}");
                        searchHits++;
                        totalHits++;
                    }
                }

                string? autoKey = prefKey switch
                {
                    "f5_set_" => "F5",
                    "sl_set_" => "SL",
                    "d3_set_" => "D3",
                    _ => null
                };
                if (autoKey != null)
                {
                    string autoPath = Path.Combine(FileSystem.AppDataDirectory, $"_autosave_{autoKey}.txt");
                    if (File.Exists(autoPath))
                    {
                        var lines = await File.ReadAllLinesAsync(autoPath);
                        for (int li = 0; li < lines.Length; li++)
                        {
                            var rowNums = lines[li]
                                .Split(new[] { ' ', '|', '-', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(v => v.Trim().TrimStart('0'))
                                .Where(v => v.Length > 0)
                                .ToList();
                            var matched = rowNums.Where(n => searchNums.Contains(n)).ToList();
                            if (matched.Count < searchNums.Count) continue;
                            string rowDisplay = string.Join("  ", rowNums.Select(n =>
                                matched.Contains(n) ? $"[{n}]" : n));
                            gameHits.Add($"  Picks #{li + 1}: {rowDisplay}");
                            searchHits++;
                            totalHits++;
                        }
                    }
                }

                if (gameHits.Count > 0)
                {
                    results.AppendLine($"── {caption} ──");
                    foreach (var h in gameHits) results.AppendLine(h);
                }
            }

            results.AppendLine(searchHits == 0 ? "  (no matches)" : $"  → {searchHits} match{(searchHits == 1 ? "" : "es")}");
            results.AppendLine();
        }

        if (results.Length == 0)
        {
            await DisplayAlert("Search Sets", "No valid numbers entered.", "OK");
            continue;
        }

        bool again = await DisplayAlert("Search Results", results.ToString(), "Search Again", "Done");
        if (!again) return;
        } // end while
    }

    // ── Export All Sets ───────────────────────────────────────────────────────

    static readonly (string Caption, string PrefKey, int Rows, int Cols, int SpecialCol, string SpecialLabel, string AccentColor, int Stride, bool HasTime)[] ExportGames =
    {
        ("Fantasy 5",        "f5_set_", 10, 5, -1, "",     "#FF8F00", 5, false),
        ("Super Lotto Plus", "sl_set_", 10, 6,  5, "Mega", "#7B1FA2", 6, false),
        ("Daily 3",          "d3_set_", 10, 3, -1, "",     "#1565C0", 3, false),
        ("Daily 4",          "d4_set_", 10, 4, -1, "",     "#00695C", 4, false),
        ("Powerball",        "pb_set_", 10, 6,  5, "PB",   "#C62828", 6, false),
        ("Mega Millions",    "mm_set_", 10, 6,  5, "MB",   "#F57F17", 6, false),
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
            "Share as Text", "Save to MyFavorite", "Print / Save PDF");
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
        else if (format == "Save to MyFavorite")
        {
            var sets = new System.Text.Json.Nodes.JsonArray();
            foreach (var (label, gameIdx, slot) in toExport)
            {
                var game = ExportGames[gameIdx];
                string raw  = Preferences.Get($"{game.PrefKey}{slot}", "");
                string bkey = $"{game.PrefKey.Replace("set_", "btypes_")}{slot}";
                string braw = Preferences.Get(bkey, "");
                var node = new System.Text.Json.Nodes.JsonObject
                {
                    ["label"]    = label,
                    ["caption"]  = game.Caption,
                    ["prefKey"]  = game.PrefKey,
                    ["slot"]     = slot,
                    ["data"]     = raw,
                    ["betTypes"] = braw,
                };
                sets.Add(node);
            }
            var root = new System.Text.Json.Nodes.JsonObject
            {
                ["saved"] = DateTime.Now.ToString("o"),
                ["sets"]  = sets,
            };
            string json = root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            string favDir = GetMyFavoriteDir();
            Directory.CreateDirectory(favDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string favName = toExport.Count == 1
                ? $"{toExport[0].Label.Replace(" — ", "_").Replace(" ", "_")}_{stamp}.json"
                : $"LotterySets_{stamp}.json";
            string favPath = Path.Combine(favDir, favName);
            await File.WriteAllTextAsync(favPath, json);

            // Share so user can save to Downloads, Drive, etc.
            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "Save to MyFavorite",
                File  = new ShareFile(favPath, "application/json")
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
            PrintHelper.PrintHtml(sb.ToString(), jobName);
        }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Export Error", ex.Message, "OK");
        }
    }

    // ── Load Picks from file ──────────────────────────────────────────────────

    private async Task LoadPicksFromFileAsync()
    {
        // Find all manually saved pick and combo files (timestamped, excludes autosave/_latest)
        var appDir = FileSystem.AppDataDirectory;
        var savedFiles = Directory.GetFiles(appDir, "my*_*.txt")
            .Where(p => { var n = Path.GetFileName(p); return !n.EndsWith("_latest.txt") && !n.StartsWith("_") && (n.Contains("_picks_") || n.Contains("_combos_")); })
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTime)
            .Select(f => (Label: $"{f.Name}  ({f.LastWriteTime:MMM d  h:mm tt})", Path: f.FullName))
            .ToList();

        var options = new List<string>();
        options.AddRange(savedFiles.Select(f => f.Label));
        options.Add("✏️ Rename a file...");
        options.Add("🗑 Delete a file...");
        options.Add("Browse for file...");

        string? choice = await DisplayActionSheet(
            savedFiles.Count == 0 ? "No saved files — browse or save first" : "Load Picks",
            "Cancel", null, options.ToArray());
        if (choice == null || choice == "Cancel") return;

        // Rename mode
        if (choice == "✏️ Rename a file...")
        {
            if (savedFiles.Count == 0) { vm.StatusMessage = "No saved files to rename"; return; }
            string? renChoice = await DisplayActionSheet("Rename which file?", "Cancel", null,
                savedFiles.Select(f => f.Label).ToArray());
            if (renChoice == null || renChoice == "Cancel") return;
            var toRename = savedFiles.First(f => f.Label == renChoice);
            string oldName = Path.GetFileNameWithoutExtension(toRename.Path);
            string? newName = await DisplayPromptAsync("Rename File", "Enter new name:", initialValue: oldName, maxLength: 60);
            if (string.IsNullOrWhiteSpace(newName) || newName == oldName) return;
            // keep .txt, sanitize
            newName = string.Concat(newName.Where(c => c != '/' && c != '\\' && c != ':' && c != '*' && c != '?' && c != '"' && c != '<' && c != '>' && c != '|'));
            string newPath = Path.Combine(appDir, newName + ".txt");
            File.Move(toRename.Path, newPath);
            vm.StatusMessage = $"Renamed to {newName}.txt";
            return;
        }

        // Delete mode
        if (choice == "🗑 Delete a file...")
        {
            if (savedFiles.Count == 0) { vm.StatusMessage = "No saved files to delete"; return; }
            string? delChoice = await DisplayActionSheet("Delete which file?", "Cancel", null,
                savedFiles.Select(f => f.Label).ToArray());
            if (delChoice == null || delChoice == "Cancel") return;
            var toDelete = savedFiles.First(f => f.Label == delChoice);
            bool confirm = await DisplayAlert("Delete", $"Delete {Path.GetFileName(toDelete.Path)}?", "Delete", "Cancel");
            if (!confirm) return;
            File.Delete(toDelete.Path);
            vm.StatusMessage = $"Deleted {Path.GetFileName(toDelete.Path)}";
            return;
        }

        string? filePath = null;

        if (choice == "Browse for file...")
        {
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Select picks .txt file",
                FileTypes   = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "text/plain", "*/*" } },
                    { DevicePlatform.iOS,     new[] { "public.plain-text" } },
                    { DevicePlatform.WinUI,   new[] { ".txt" } },
                })
            });
            if (result == null) return;
            filePath = result.FullPath;
        }
        else
        {
            filePath = savedFiles.First(f => f.Label == choice).Path;
        }

        try
        {
            var lines = (await File.ReadAllLinesAsync(filePath))
                        .Where(l => !l.StartsWith("#") && !string.IsNullOrWhiteSpace(l))
                        .ToList();

            if (lines.Count == 0) { vm.StatusMessage = "File is empty or unrecognized format"; return; }

            // Preview before loading
            var preview = string.Join("\n", lines.Take(25));
            if (lines.Count > 25) preview += $"\n... and {lines.Count - 25} more";
            bool doLoad = await DisplayAlert($"Preview — {lines.Count} picks", preview, "Load", "Cancel");
            if (!doLoad) return;

            vm.Picks = new System.Collections.ObjectModel.ObservableCollection<string>(lines);
            ReHookPicksAutoSave();
            await File.WriteAllLinesAsync(PicksAutoSavePath, vm.Picks); // save immediately as latest
            vm.ActiveTab = 0; // switch to History tab to show the list
            vm.StatusMessage = $"Loaded {lines.Count} picks from {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            await DisplayAlert("Load Error", ex.Message, "OK");
        }
    }

    // ── MyFavorite folder path ────────────────────────────────────────────────

    static string GetMyFavoriteDir()
    {
#if ANDROID
        // External files dir — accessible in file manager at
        // Android/data/com.calho.dailyfantasy/files/MyFavorite/
        var extDir = Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
                     ?? FileSystem.AppDataDirectory;
        return Path.Combine(extDir, "MyFavorite");
#else
        return Path.Combine(FileSystem.AppDataDirectory, "data", "MyFavorite");
#endif
    }

    // ── Load from MyFavorite ─────────────────────────────────────────────────

    private async Task LoadFromMyFavoriteAsync()
    {
        try
        {
            // Use system file picker — works from Downloads, Drive, anywhere
            var result = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Select a MyFavorite .json file",
                FileTypes   = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "application/json", "*/*" } },
                    { DevicePlatform.iOS,     new[] { "public.json" } },
                    { DevicePlatform.WinUI,   new[] { ".json" } },
                })
            });
            if (result == null) return;

            string json = await File.ReadAllTextAsync(result.FullPath);
            var root = System.Text.Json.Nodes.JsonNode.Parse(json)?.AsObject();
            var sets = root?["sets"]?.AsArray();
            if (sets == null || sets.Count == 0)
            {
                await DisplayAlert("Load Error", "File contains no sets.", "OK");
                return;
            }

            // Pick which set to load if multiple
            System.Text.Json.Nodes.JsonObject? setNode;
            if (sets.Count == 1)
            {
                setNode = sets[0]?.AsObject();
            }
            else
            {
                var labels = sets.Select(s => s?["label"]?.GetValue<string>() ?? "?").ToArray();
                string? chosenLabel = await DisplayActionSheet("Select Set to Load", "Cancel", null, labels);
                if (chosenLabel == null || chosenLabel == "Cancel") return;
                setNode = sets.FirstOrDefault(s => s?["label"]?.GetValue<string>() == chosenLabel)?.AsObject();
            }
            if (setNode == null) return;

            string prefKey  = setNode["prefKey"]?.GetValue<string>() ?? "";
            string data     = setNode["data"]?.GetValue<string>()    ?? "";
            string betTypes = setNode["betTypes"]?.GetValue<string>() ?? "";
            string caption  = setNode["caption"]?.GetValue<string>() ?? "this game";
            int    origSlot = setNode["slot"]?.GetValue<int>() ?? 0;

            if (string.IsNullOrEmpty(prefKey) || string.IsNullOrEmpty(data))
            {
                await DisplayAlert("Load Error", "Set data is missing.", "OK");
                return;
            }

            // Pick destination slot
            var slotOptions = Enumerable.Range(0, 10).Select(i => $"Set {i + 1}").ToArray();
            string? destLabel = await DisplayActionSheet($"Load into which slot? ({caption})", "Cancel", null, slotOptions);
            if (destLabel == null || destLabel == "Cancel") return;
            int destSlot = int.Parse(destLabel.Replace("Set ", "")) - 1;

            // Write to preferences
            Preferences.Set($"{prefKey}{destSlot}", data);
            if (!string.IsNullOrEmpty(betTypes))
            {
                string bkey = prefKey.Replace("set_", "btypes_");
                Preferences.Set($"{bkey}{destSlot}", betTypes);
            }

            await DisplayAlert("Loaded", $"Loaded into {caption} — Set {destSlot + 1}.\nOpen that game to see the numbers.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Load Error", ex.Message, "OK");
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

        string pageName0 = _mode == 0 ? "F5 Winner" : _mode == 1 ? "Super Lotto" : "Daily 3";
        string insertMode = await DisplayActionSheet(
            $"Insert {vm.Combinations.Count:N0} combos into {pageName0}?",
            "Cancel", null,
            "Clear all sets & insert",
            "Empty slots only");
        if (insertMode == null || insertMode == "Cancel") return;

        const int WRows = 10;
        const int TotalSlots = 10;
        int wCols         = _mode == 1 ? 6 : _mode == 0 ? 5 : 3;
        string slotPrefix = _mode == 0 ? "f5_set_" : _mode == 1 ? "sl_set_" : "d3_set_";
        string pageName   = _mode == 0 ? "F5 Winner" : _mode == 1 ? "Super Lotto" : "Daily 3";

        // Clear all slots first if user chose "Clear all sets & insert"
        // Preserve/nuke/restore: collect advance rows → nuke all slots → restore advance rows
        if (insertMode == "Clear all sets & insert")
        {
            // Step 0: flush active page's in-memory advance dates to Preferences first
            if (_mode == 0) AppShell.WinnerPageInstance.FlushAdvanceDates();
            else if (_mode == 1) AppShell.SuperLottoPageInstance.FlushAdvanceDates();
            else AppShell.Daily3PageInstance.FlushAdvanceDates();

            string advPrefix  = _mode == 0 ? "f5_adv_"  : _mode == 1 ? "sl_adv_"  : "d3_adv_";
            string[] extraDel = _mode == 2 ? new[] { "d3_btypes_", "d3_drawfilters_" } : Array.Empty<string>();
            var today = DateTime.Today;

            // Step 1: collect all advance-ticket rows keyed by (slot, row)
            // preservedSetRows[slot][row] = string[wCols] of cell values
            // preservedAdvRows[slot][row] = adv date string "start~end"
            var preservedSetRows = new Dictionary<int, Dictionary<int, string[]>>();
            var preservedAdvRows = new Dictionary<int, Dictionary<int, string>>();

            for (int s = 0; s < TotalSlots; s++)
            {
                string setKey = $"{slotPrefix}{s}";
                string advKey = $"{advPrefix}{s}";
                string raw    = Preferences.Get(setKey, "");
                string advRaw = Preferences.Get(advKey, "");
                if (string.IsNullOrEmpty(advRaw)) continue;

                var vals     = string.IsNullOrEmpty(raw) ? new string[WRows * wCols] : raw.Split('|');
                if (vals.Length < WRows * wCols) Array.Resize(ref vals, WRows * wCols);
                var advParts = advRaw.Split('|');
                if (advParts.Length < WRows) Array.Resize(ref advParts, WRows);

                for (int r = 0; r < WRows; r++)
                {
                    var pair = advParts[r]?.Split('~');
                    if (pair == null || pair.Length != 2) continue;
                    DateTime? end = null, start = null;
                    if (DateTime.TryParseExact(pair[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var ed)) end = ed;
                    if (DateTime.TryParseExact(pair[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var sd)) start = sd;
                    var refDate = end ?? start;
                    if (!refDate.HasValue || refDate.Value < today) continue;

                    // This row has a future advance date — preserve it
                    if (!preservedSetRows.ContainsKey(s)) preservedSetRows[s] = new Dictionary<int, string[]>();
                    if (!preservedAdvRows.ContainsKey(s)) preservedAdvRows[s] = new Dictionary<int, string>();
                    var rowCells = new string[wCols];
                    for (int c = 0; c < wCols; c++)
                        rowCells[c] = r * wCols + c < vals.Length ? (vals[r * wCols + c] ?? "") : "";
                    preservedSetRows[s][r] = rowCells;
                    preservedAdvRows[s][r] = advParts[r];
                }
            }

            // Step 2: nuke ALL slots completely
            for (int s = 0; s < TotalSlots; s++)
            {
                Preferences.Remove($"{slotPrefix}{s}");
                Preferences.Remove($"{advPrefix}{s}");
                foreach (var ex in extraDel) Preferences.Remove($"{ex}{s}");
            }

            // Step 3: restore preserved advance-ticket rows back to their exact slot/row positions
            foreach (var (s, rowMap) in preservedSetRows)
            {
                var newVals = new string[WRows * wCols];
                for (int i = 0; i < newVals.Length; i++) newVals[i] = "";
                var newAdv  = new string[WRows];
                for (int i = 0; i < newAdv.Length; i++) newAdv[i] = "~";

                foreach (var (r, cells) in rowMap)
                {
                    for (int c = 0; c < wCols; c++)
                        newVals[r * wCols + c] = cells[c];
                }
                if (preservedAdvRows.TryGetValue(s, out var advRowMap))
                    foreach (var (r, advStr) in advRowMap) newAdv[r] = advStr;

                Preferences.Set($"{slotPrefix}{s}", string.Join("|", newVals));
                Preferences.Set($"{advPrefix}{s}",  string.Join("|", newAdv));
            }
        }

        var combos = vm.Combinations.ToList();
        int comboIndex = 0;
        int insertedRows = 0;
        int insertedSlots = 0;

        for (int slot = 0; slot < TotalSlots && comboIndex < combos.Count; slot++)
        {
            string existing = Preferences.Get($"{slotPrefix}{slot}", "");
            bool hasData = !string.IsNullOrEmpty(existing) &&
                           existing.Replace("|", "").Trim().Length > 0;

            // For "Empty slots only": skip slots that have any data
            if (insertMode == "Empty slots only" && hasData) continue;

            // Load existing values (or blank slate)
            var vals = hasData
                ? existing.Split('|')
                : new string[WRows * wCols];
            if (vals.Length < WRows * wCols) Array.Resize(ref vals, WRows * wCols);
            if (!hasData) for (int i = 0; i < vals.Length; i++) vals[i] = "";

            int rowsFilled = 0;
            for (int r = 0; r < WRows && comboIndex < combos.Count; r++)
            {
                // Skip rows that already have data (advance ticket rows)
                bool rowHasData = false;
                for (int c = 0; c < wCols; c++)
                    if (!string.IsNullOrEmpty(vals[r * wCols + c])) { rowHasData = true; break; }
                if (rowHasData) continue;

                var parts = combos[comboIndex].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                for (int col = 0; col < wCols && col < parts.Length; col++)
                    vals[r * wCols + col] = parts[col];
                rowsFilled++;
                comboIndex++;
                insertedRows++;
            }

            if (rowsFilled > 0)
            {
                Preferences.Set($"{slotPrefix}{slot}", string.Join("|", vals));
                insertedSlots++;
            }
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

    private async void BtnJackpot_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        AppShell.JackpotPageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(JackpotPage), false);
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

            // Clear main page boxes
            _isRestoring = true;
            if (_boxes != null)
                foreach (var box in _boxes) box.Text = "";
            _isRestoring = false;
            for (int i = 1; i <= 15; i++)
                Preferences.Remove($"box{i}");

            // Clear all game pages (entries + slot caches)
            AppShell.WinnerPageInstance.ClearForArchive();
            AppShell.SuperLottoPageInstance.ClearForArchive();
            AppShell.PowerballPageInstance.ClearForArchive();
            AppShell.MegaMillionsPageInstance.ClearForArchive();
            AppShell.Daily3PageInstance.ClearForArchive();
            AppShell.Daily4PageInstance.ClearForArchive();
            AppShell.DailyDerbyPageInstance.ClearForArchive();

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

        int count;
        if (vm.ActiveTab == 2)
        {
            // Saving combos: write file AND sync vm.Picks so OnDisappearing/autosave stay correct
            var snapshot = vm.Combinations.ToList();
            try { File.WriteAllLines(PicksAutoSavePath, snapshot); } catch { }
            // Silently replace Picks with combos (unhook to avoid double-write)
            if (_picksRef != null) _picksRef.CollectionChanged -= OnPicksAutoSave;
            vm.Picks = new System.Collections.ObjectModel.ObservableCollection<string>(snapshot);
            ReHookPicksAutoSave();
            count = snapshot.Count;
        }
        else
        {
            try { File.WriteAllLines(PicksAutoSavePath, vm.Picks); } catch { }
            count = vm.Picks.Count;
        }

        vm.StatusMessage = count > 0 ? $"Saved {count} items" : "Saved (list is empty)";

        if (sender is Button btn)
        {
            var orig = btn.Text; var origColor = btn.BackgroundColor;
            btn.Text = "Saved!"; btn.BackgroundColor = Color.FromArgb("#1B5E20");
            await Task.Delay(1200);
            btn.Text = orig; btn.BackgroundColor = origColor;
        }
    }
}
