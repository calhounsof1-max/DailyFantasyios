using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class WinnerPage : ContentPage
{
    const int Rows = 10;
    const int Cols = 5;

    readonly Border[] _wBorders;
    readonly Label[]  _wLabels;

    // [row, col] entries and per-row result label
    readonly Entry[,] _entries  = new Entry[Rows, Cols];
    readonly Label[]  _results  = new Label[Rows];
    int _activeSlot = -1;
    bool _suppressPickerEvent = false;
    bool _suppressExcl = false;
    readonly Dictionary<int, string> _slotCache = new();
    View? _highlightedView;

    int[] _winningNumbers = Array.Empty<int>();
    List<(DateTime Date, string Label, int[] Numbers)> _allDraws = new();
    bool _drawsLoaded = false;
    bool _isPanning = false;
    bool _voiceOn = false;
    bool _voiceSettingText = false;
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

    private async void BtnGames_Clicked(object sender, EventArgs e) => await GameNavHelper.ShowGamesDropdown(this);

    private async void BtnGoSL_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        SuperLottoPage.ComingFrom = "f5";
        AppShell.SuperLottoPageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(SuperLottoPage), false);
    }

    internal void PrePosition(bool fromRight)
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        TranslationX = fromRight ? w : -w;
    }

    protected override void OnAppearing()
    {
        // TranslationX was pre-set by PrePosition before navigation — just animate in
        this.TranslateTo(0, 0, 220, Easing.CubicOut);

        btnBack.Text = ComingFrom == "results" ? "← RESULTS" : "← HOME";

        base.OnAppearing();
#if IOS
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () =>
        {
            var ph = Handler as Microsoft.Maui.Handlers.PageHandler;
            var vc = ph?.ViewController;
            var vf = vc?.View?.Frame;
            // Convert view origin to window coordinates = absolute screen position
            var winPt = vc?.View?.ConvertPointToView(CoreGraphics.CGPoint.Empty, null);
            var safeTop = vc?.View?.SafeAreaInsets.Top ?? -1;
            var winH = vc?.View?.Window?.Frame.Height ?? -1;
            lblStatus.Text = $"winY={winPt?.Y:F0} vH={vf?.Height:F0} winH={winH:F0} safe={safeTop:F0} pad={Padding.Top:F0}";
        });
#endif
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
                    LoadEntries();
            }
            UpdateSlotPicker();
            if (pendingRow >= 0)
                _ = HighlightRow(pendingRow);
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (_voiceOn) StopVoice();
        if (_highlightedView != null) { _highlightedView.BackgroundColor = Colors.White; _highlightedView = null; }
        SaveEntries();
        if (_activeSlot >= 0)
            Preferences.Set("f5_active_slot", _activeSlot);
    }

    private void SaveEntries()
    {
        var vals = new string[Rows * Cols];
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                vals[r * Cols + c] = _entries[r, c].Text ?? "";
        Preferences.Set("f5_entries", string.Join("|", vals));
    }

    private void LoadEntries()
    {
        var saved = Preferences.Get("f5_entries", "");
        if (string.IsNullOrEmpty(saved)) return;
        var vals = saved.Split('|');
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                int idx = r * Cols + c;
                if (idx < vals.Length)
                    _entries[r, c].Text = vals[idx];
            }
    }

    // ── Set slots (save/load 10 named sets) ─────────────────────────────────

    private string SetKey(int slot) => $"f5_set_{slot}";

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
    }

    private void ClearRow(int r)
    {
        for (int c = 0; c < Cols; c++)
        {
            _entries[r, c].Text = "";
            _entries[r, c].BackgroundColor = Color.FromArgb("#F5F5F5");
        }
        _results[r].Text = "";
        SaveEntries();
    }

    private void SaveSet(int slot)
    {
        string data = GetCurrentEntryString();
        bool isEmpty = data.Replace("|", "").Trim().Length == 0;
        if (isEmpty)
            Preferences.Remove(SetKey(slot));   // saving empty clears the slot
        else
            Preferences.Set(SetKey(slot), data);
        UpdateSlotPicker();
    }

    private void FillFromSlot(int slot)
    {
        var saved = Preferences.Get(SetKey(slot), "");
        if (string.IsNullOrEmpty(saved)) return;
        var vals = saved.Split('|');
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                int idx = r * Cols + c;
                _entries[r, c].Text = idx < vals.Length ? vals[idx] : "";
            }
        CheckAll();
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
        _suppressPickerEvent = false;
        UpdateExclCheckbox();
    }

    private void SlotPicker_Changed(object sender, EventArgs e)
    {
        if (_suppressPickerEvent) return;
        int slot = slotPicker.SelectedIndex;
        if (slot < 0) return;
        // Cache current slot before switching
        if (_activeSlot >= 0)
            _slotCache[_activeSlot] = GetCurrentEntryString();
        _activeSlot = slot;
        Preferences.Set("f5_active_slot", slot);
        ClearAllEntries();
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
        }
        else if (SlotHasData(slot))
            FillFromSlot(slot);
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
                },
                ColumnSpacing = 4,
                BackgroundColor = Colors.White,
                Margin = new Thickness(0, 1),
                Padding = new Thickness(4, 2),
            };

            // Row number — tap to clear this row
            int rowIdx = r;
            var rowNum = new Label
            {
                Text = $"{r + 1,2}.",
                FontSize = 10,
                TextColor = Color.FromArgb("#FF7043"),
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 18,
            };
            rowNum.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => ClearRow(rowIdx))
            });
            Grid.SetColumn(rowNum, 0);
            row.Children.Add(rowNum);

            // 5 Entry boxes
            for (int c = 0; c < Cols; c++)
            {
                var entry = new Entry
                {
                    Keyboard = Keyboard.Numeric,
                    FontSize = 11,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.Black,
                    BackgroundColor = Color.FromArgb("#F5F5F5"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    HeightRequest = 22,
                    MaxLength = 2,
                };
                entry.HandlerChanged += ForceBlackText;
                AttachMaxClamp(entry, 39);

                // Capture row/col for the lambda
                int row_ = r, col_ = c;
                entry.TextChanged += (_, _) =>
                {
                    if (!_voiceSettingText && _entries[row_, col_].Text?.Length == 2)
                        AdvanceFocus(row_, col_);
                    SaveEntries();
                    // Auto-check once all 5 entries in the row are filled
                    bool rowFull = true;
                    for (int ci = 0; ci < Cols; ci++)
                        if (string.IsNullOrEmpty(_entries[row_, ci].Text)) { rowFull = false; break; }
                    if (rowFull) CheckAll();
                };

                _entries[r, c] = entry;
                Grid.SetColumn(entry, c + 1);
                row.Children.Add(entry);
            }

            // Result label
            var result = new Label
            {
                Text = "",
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#888"),
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 36,
                HorizontalTextAlignment = TextAlignment.Center,
            };
            _results[r] = result;
            Grid.SetColumn(result, 6);
            row.Children.Add(result);

            rowsContainer.Children.Add(row);
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

    static void AttachMaxClamp(Entry entry, int max)
    {
        entry.TextChanged += (s, e) =>
        {
            if (int.TryParse(e.NewTextValue, out int v) && v > max)
                ((Entry)s!).Text = e.OldTextValue ?? "";
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
        _highlightedView = rowView;
        rowView.BackgroundColor = Color.FromArgb("#FFF176");
        if (rowsContainer.Parent is ScrollView sv)
            await sv.ScrollToAsync(rowView, ScrollToPosition.MakeVisible, true);
        await Task.Delay(2000);
        rowView.BackgroundColor = Colors.White;
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
            if (hasTodayDraw) return;
            _drawsLoaded = false;
            _allDraws.Clear();
        }

        spinner.IsVisible = true;
        spinner.IsRunning = true;
        lblDrawDate.Text = "Fetching draws from calottery.com...";

        var raw = await GetDataEntry.GetPastDraws(30);

        spinner.IsVisible = false;
        spinner.IsRunning = false;

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
            drawDatePicker.MaximumDate = _allDraws.First().Date.Date;
            var targetDate = defaultDraw.Date != default ? defaultDraw.Date.Date : _allDraws.First().Date.Date;
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

        _winningNumbers = match.Numbers;
        lblDrawDate.Text = match.Label;
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
    }

    // ── Button handlers ──────────────────────────────────────────────────────

    private void BtnCheck_Clicked(object sender, EventArgs e) => CheckAll();

    private void BtnClearAll_Clicked(object sender, EventArgs e)
    {
        ClearAllEntries();
        SaveEntries();
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

        for (int i = 0; i < 10; i++)
            Preferences.Remove(SetKey(i));

        ClearAllEntries();
        UpdateSlotPicker();

        if (sender is Button btn)
        {
            var orig = btn.Text;
            var origColor = btn.BackgroundColor;
            btn.Text = "Cleared";
            btn.BackgroundColor = Color.FromArgb("#1B5E20");
            await Task.Delay(1200);
            btn.Text = orig;
            btn.BackgroundColor = origColor;
        }
    }

    private void BtnVoice_Clicked(object sender, EventArgs e)
    {
#if ANDROID
        if (!Services.VoiceNumberService.IsAvailable) { lblStatus.Text = "Speech recognition not available"; return; }
        if (_voiceOn) StopVoice(); else StartVoice();
#endif
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
#if ANDROID
        Services.VoiceNumberService.StatusUpdate += OnVoiceStatus;
        Services.VoiceNumberService.StartContinuous(OnVoiceNumbers);
#endif
    }

    void StopVoice()
    {
        _voiceOn = false;
        ClearVoiceTarget();
#if ANDROID
        Services.VoiceNumberService.StatusUpdate -= OnVoiceStatus;
        Services.VoiceNumberService.Stop();
#endif
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

}
