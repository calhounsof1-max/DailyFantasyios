using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class PowerballPage : ContentPage
{
    const int Rows      = 10;
    const int MainCols  = 5;
    const int PBCol     = 5;
    const int TotalCols = 6;

    readonly Border[] _wBorders;
    readonly Label[]  _wLabels;

    readonly Entry[,] _entries = new Entry[Rows, TotalCols];
    readonly Label[]  _results = new Label[Rows];
    int  _activeSlot = -1;
    bool _suppressPickerEvent = false;
    bool _suppressExcl = false;
    readonly Dictionary<int, string> _slotCache = new();
    View? _highlightedView;

    DateTime?[] _playStart = new DateTime?[Rows];
    DateTime?[] _playEnd   = new DateTime?[Rows];
    Grid?       _advOverlay;
    DatePicker? _advStartPicker;
    DatePicker? _advEndPicker;
    int         _advRow = -1;

    int[] _winningMainNums = Array.Empty<int>();
    int   _winningPB       = 0;
    List<(DateTime Date, string Label, int DrawNumber, int[] MainNumbers, int PBNumber)> _allDraws = new();
    bool _drawsLoaded = false;
    bool _isPanning   = false;
    bool _voiceOn = false;
    bool _voiceSettingText = false;
    int  _voiceRow = 0, _voiceCol = 0;
    Entry? _voiceTarget = null;
    Color _voiceTargetOldColor = Colors.White;

    // "sl" = came via carousel from SuperLotto; "main" = navigated directly
    internal static string ComingFrom { get; set; } = "sl";

    public PowerballPage()
    {
        InitializeComponent();
        _wBorders = new[] { W1, W2, W3, W4, W5 };
        _wLabels  = new[] { lblW1, lblW2, lblW3, lblW4, lblW5 };
        BuildRows();
        BuildSlotPicker();
        BuildAdvancePlayOverlay();
    }

    // ── Pan gesture ──────────────────────────────────────────────────────────

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
                if (_panLeft < -40)
                {
                    _isPanning = true;
                    MegaMillionsPage.ComingFrom = "pb";
                    AppShell.MegaMillionsPageInstance.PrePosition(true);
                    await Shell.Current.GoToAsync(nameof(MegaMillionsPage), false);
                    _isPanning = false;
                }
                else if (_panRight > 40)
                {
                    _isPanning = true;
                    await GoBackAsync();
                    _isPanning = false;
                }
                _panLeft = _panRight = 0;
                break;
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = GoBackAsync();
        return true;
    }

    private async Task GoBackAsync()
    {
        if (ComingFrom == "sl")
            AppShell.SuperLottoPageInstance.PrePosition(false);
        else
        {
            double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
            Shell.Current.CurrentPage.TranslationX = -w;
        }
        await Shell.Current.GoToAsync("..", false);
    }

    private async void BtnGoHome_Clicked(object sender, EventArgs e) =>
        await Shell.Current.Navigation.PopToRootAsync(false);

    private async void BtnGames_Clicked(object sender, EventArgs e) => await GameNavHelper.ShowGamesDropdown(this);

    private async void BtnGoBack_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        await GoBackAsync();
    }

    private async void BtnGoD3_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        MegaMillionsPage.ComingFrom = "pb";
        AppShell.MegaMillionsPageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(MegaMillionsPage), false);
    }

    internal void PrePosition(bool fromRight)
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        TranslationX = fromRight ? w : -w;
    }

    protected override void OnAppearing()
    {
        this.TranslateTo(0, 0, 220, Easing.CubicOut);
        if (ComingFrom == "results")
        {
            btnBack.Text = "← RESULTS";
            btnBack.BackgroundColor = Color.FromArgb("#FF8F00");
        }
        else if (ComingFrom == "main")
        {
            btnBack.Text = "← HOME";
            btnBack.BackgroundColor = Color.FromArgb("#FF8F00");
        }
        else
        {
            btnBack.Text = "← SL";
            btnBack.BackgroundColor = Color.FromArgb("#7B1FA2");
        }
        base.OnAppearing();
        _ = LoadAllDraws();
        Dispatcher.Dispatch(() =>
        {
            int pendingRow = -1;
            if (PendingHighlight.HasPending && PendingHighlight.Game == "PB")
            {
                _activeSlot = PendingHighlight.Slot;
                pendingRow  = PendingHighlight.Row;
                PendingHighlight.Clear();
                Preferences.Set("pb_active_slot", _activeSlot);
                FillFromSlot(_activeSlot);
            }
            else
            {
                _activeSlot = Preferences.Get("pb_active_slot", -1);
                if (_activeSlot < 0)
                {
                    var current = Preferences.Get("pb_entries", "");
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
        SaveAdvanceDates(_activeSlot);
        if (_voiceOn) StopVoice();
        if (_highlightedView != null) { _highlightedView.BackgroundColor = Colors.White; _highlightedView = null; }
        SaveEntries();
        if (_activeSlot >= 0)
            Preferences.Set("pb_active_slot", _activeSlot);
    }

    // ── Entry persistence ────────────────────────────────────────────────────

    private void SaveEntries()
    {
        var vals = new string[Rows * TotalCols];
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < TotalCols; c++)
                vals[r * TotalCols + c] = _entries[r, c].Text ?? "";
        Preferences.Set("pb_entries", string.Join("|", vals));
    }

    private void LoadEntries()
    {
        var saved = Preferences.Get("pb_entries", "");
        if (string.IsNullOrEmpty(saved)) return;
        var vals = saved.Split('|');
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < TotalCols; c++)
            {
                int idx = r * TotalCols + c;
                if (idx < vals.Length) _entries[r, c].Text = vals[idx];
            }
    }

    // ── Set slots ────────────────────────────────────────────────────────────

    private string SetKey(int slot) => $"pb_set_{slot}";

    private string GetCurrentEntryString()
    {
        var vals = new string[Rows * TotalCols];
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < TotalCols; c++)
                vals[r * TotalCols + c] = _entries[r, c].Text ?? "";
        return string.Join("|", vals);
    }

    private void ClearAllEntries()
    {
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < MainCols; c++)
                { _entries[r, c].Text = ""; _entries[r, c].BackgroundColor = Color.FromArgb("#F5F5F5"); }
            _entries[r, PBCol].Text = "";
            _entries[r, PBCol].BackgroundColor = Color.FromArgb("#FFF3E0");
        }
        foreach (var lbl in _results) lbl.Text = "";
        Array.Clear(_playStart, 0, Rows);
        Array.Clear(_playEnd,   0, Rows);
        UpdateAllResultBackgrounds();
    }

    private void ClearRow(int r)
    {
        for (int c = 0; c < MainCols; c++)
        {
            _entries[r, c].Text = "";
            _entries[r, c].BackgroundColor = Color.FromArgb("#F5F5F5");
        }
        _entries[r, PBCol].Text = "";
        _entries[r, PBCol].BackgroundColor = Color.FromArgb("#FFF3E0");
        _results[r].Text = "";
        _playStart[r] = null;
        _playEnd[r]   = null;
        UpdateResultBackground(r);
        SaveEntries();
    }

    private void SaveSet(int slot)
    {
        string data = GetCurrentEntryString();
        bool isEmpty = data.Replace("|", "").Trim().Length == 0;
        if (isEmpty) Preferences.Remove(SetKey(slot));
        else         Preferences.Set(SetKey(slot), data);
        UpdateSlotPicker();
    }

    private void FillFromSlot(int slot)
    {
        var saved = Preferences.Get(SetKey(slot), "");
        if (string.IsNullOrEmpty(saved)) return;
        var vals = saved.Split('|');
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < TotalCols; c++)
            {
                int idx = r * TotalCols + c;
                _entries[r, c].Text = idx < vals.Length ? vals[idx] : "";
            }
        CheckAll();
        LoadAdvanceDates(slot);
        UpdateAllResultBackgrounds();
    }

    private bool SlotHasData(int slot) =>
        !string.IsNullOrEmpty(Preferences.Get(SetKey(slot), ""));

    private void BuildSlotPicker()
    {
        for (int i = 0; i < 10; i++)
            slotPicker.Items.Add(SlotLabel(i));
    }

    private string ExclKey(int slot) => $"excl_set_pb_{slot}";

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
        SaveAdvanceDates(_activeSlot);
        _activeSlot = slot;
        Preferences.Set("pb_active_slot", slot);
        ClearAllEntries();
        if (_slotCache.TryGetValue(slot, out var cached))
        {
            var vals = cached.Split('|');
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < TotalCols; c++)
                {
                    int idx = r * TotalCols + c;
                    _entries[r, c].Text = idx < vals.Length ? vals[idx] : "";
                }
            CheckAll();
        }
        else if (SlotHasData(slot)) FillFromSlot(slot);
        LoadAdvanceDates(slot); UpdateAllResultBackgrounds();
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
                    new ColumnDefinition(GridLength.Auto),    // row number
                    new ColumnDefinition(GridLength.Star),    // main 1
                    new ColumnDefinition(GridLength.Star),    // main 2
                    new ColumnDefinition(GridLength.Star),    // main 3
                    new ColumnDefinition(GridLength.Star),    // main 4
                    new ColumnDefinition(GridLength.Star),    // main 5
                    new ColumnDefinition(new GridLength(4)),  // separator
                    new ColumnDefinition(GridLength.Star),    // powerball
                    new ColumnDefinition(GridLength.Auto),    // result
                },
                ColumnSpacing = 4,
                BackgroundColor = Colors.White,
                Margin = new Thickness(0, 1),
                Padding = new Thickness(4, 2),
            };

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

            // 5 main entry boxes
            for (int c = 0; c < MainCols; c++)
            {
                var entry = MakeEntry(Color.FromArgb("#F5F5F5"));
                AttachMaxClamp(entry, 69);
                int row_ = r, col_ = c;
                entry.TextChanged += (_, _) =>
                {
                    if (!_voiceSettingText && _entries[row_, col_].Text?.Length == 2) AdvanceFocus(row_, col_);
                    SaveEntries();
                    if (IsRowFull(row_)) CheckAll();
                };
                _entries[r, c] = entry;
                Grid.SetColumn(entry, c + 1);
                row.Children.Add(entry);
            }

            // Thin separator (orange for Powerball)
            var sep = new BoxView { BackgroundColor = Color.FromArgb("#FF6F00"), WidthRequest = 2, VerticalOptions = LayoutOptions.Fill, Margin = new Thickness(1, 4) };
            Grid.SetColumn(sep, 6);
            row.Children.Add(sep);

            // Powerball entry
            var pbEntry = MakeEntry(Color.FromArgb("#FFF3E0"));
            AttachMaxClamp(pbEntry, 26);
            int mrow = r;
            pbEntry.TextChanged += (_, _) =>
            {
                if (!_voiceSettingText && _entries[mrow, PBCol].Text?.Length == 2) AdvanceFocus(mrow, PBCol);
                SaveEntries();
                if (IsRowFull(mrow)) CheckAll();
            };
            _entries[r, PBCol] = pbEntry;
            Grid.SetColumn(pbEntry, 7);
            row.Children.Add(pbEntry);

            // Result label
            var result = new Label
            {
                Text = "",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#888"),
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 32,
                HorizontalTextAlignment = TextAlignment.Center,
            };
            _results[r] = result;
            int ri = r;
            result.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => ShowAdvancePlayOverlay(ri))
            });
            Grid.SetColumn(result, 8);
            row.Children.Add(result);

            rowsContainer.Children.Add(row);
        }
    }

    private Entry MakeEntry(Color bg)
    {
        var e = new Entry
        {
            Keyboard = Keyboard.Numeric,
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.Black,
            BackgroundColor = bg,
            HorizontalTextAlignment = TextAlignment.Center,
            HeightRequest = 22,
            MaxLength = 2,
        };
        e.HandlerChanged += ForceBlackText;
        return e;
    }

    private bool IsRowFull(int r)
    {
        for (int c = 0; c < TotalCols; c++)
            if (string.IsNullOrEmpty(_entries[r, c].Text)) return false;
        return true;
    }

    private void AdvanceFocus(int row, int col)
    {
        if (col < MainCols - 1) { _entries[row, col + 1].Focus(); return; }
        if (col == MainCols - 1) { _entries[row, PBCol].Focus(); return; }
        if (row + 1 < Rows) _entries[row + 1, 0].Focus();
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

    // ── Fetch draws ──────────────────────────────────────────────────────────

    private async Task LoadAllDraws()
    {
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

        var raw = await GetDataEntry.GetPowerballDraws(30);

        spinner.IsVisible = false;
        spinner.IsRunning = false;

        if (raw.Count == 0)
        {
            string errMsg = string.IsNullOrEmpty(GetDataEntry.LastError)
                ? "Powerball: Could not fetch — check internet"
                : $"Powerball: {GetDataEntry.LastError}";
            lblDrawDate.Text = errMsg;
            lblStatus.Text = errMsg;
            return;
        }

        _allDraws = raw
            .Select(d => (
                Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                Label: d.DrawDate,
                DrawNumber:  d.DrawNumber,
                MainNumbers: d.MainNumbers,
                PBNumber:    d.PBNumber))
            .Where(d => d.Date != DateTime.MinValue)
            .ToList();

        _drawsLoaded = true;

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
                ShowDrawForDate(targetDate);
            else
                drawDatePicker.Date = targetDate;
        });
    }

    private void ShowDrawForDate(DateTime date)
    {
        bool todayAvailable = _allDraws.Any(d => d.Date.Date == DateTime.Today);
        if (date.Date == DateTime.Today && !todayAvailable)
            date = date.AddDays(-1);

        var match = _allDraws.FirstOrDefault(d => d.Date.Date <= date.Date);
        if (match.MainNumbers == null) return;

        _winningMainNums = match.MainNumbers;
        _winningPB       = match.PBNumber;
        lblDrawDate.Text = match.DrawNumber > 0 ? $"{match.Label}  Draw #{match.DrawNumber}" : match.Label;

        for (int i = 0; i < _wLabels.Length; i++)
            _wLabels[i].Text = match.MainNumbers[i].ToString();
        lblWPB.Text = match.PBNumber.ToString();

        CheckAll();
    }

    private void DrawDatePicker_DateSelected(object sender, DateChangedEventArgs e) =>
        ShowDrawForDate(e.NewDate ?? DateTime.Today);

    // ── Check ────────────────────────────────────────────────────────────────

    private void CheckAll()
    {
        if (_winningMainNums.Length == 0) return;
        var mainSet = new HashSet<int>(_winningMainNums);

        for (int r = 0; r < Rows; r++)
        {
            int mainMatches = 0;
            bool pbMatch    = false;

            for (int c = 0; c < MainCols; c++)
            {
                if (int.TryParse(_entries[r, c].Text, out int n) && mainSet.Contains(n))
                {
                    _entries[r, c].BackgroundColor = Color.FromArgb("#F9A825");
                    mainMatches++;
                }
                else
                {
                    bool hasVal = !string.IsNullOrWhiteSpace(_entries[r, c].Text);
                    _entries[r, c].BackgroundColor = hasVal ? Color.FromArgb("#FFCDD2") : Color.FromArgb("#F5F5F5");
                }
            }

            if (int.TryParse(_entries[r, PBCol].Text, out int pb) && _winningPB > 0 && pb == _winningPB)
            {
                _entries[r, PBCol].BackgroundColor = Color.FromArgb("#F9A825");
                pbMatch = true;
            }
            else
            {
                bool hasVal = !string.IsNullOrWhiteSpace(_entries[r, PBCol].Text);
                _entries[r, PBCol].BackgroundColor = hasVal ? Color.FromArgb("#FFCDD2") : Color.FromArgb("#FFF3E0");
            }

            // Powerball has a prize if PB matches OR main >= 3
            bool hasPrize = pbMatch || mainMatches >= 3;
            if (mainMatches > 0 || pbMatch)
            {
                _results[r].Text = pbMatch ? $"{mainMatches}+PB" : $"{mainMatches}/5";
                _results[r].TextColor = hasPrize ? Color.FromArgb("#C62828") : Color.FromArgb("#888888");
            }
            else
            {
                _results[r].Text = "";
            }
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
            for (int c = 0; c < TotalCols; c++)
                if (!string.IsNullOrEmpty(_entries[r, c].Text)) { empty = false; break; }
            if (!empty) continue;

            var main = Enumerable.Range(1, 69).OrderBy(_ => rng.Next()).Take(MainCols).OrderBy(n => n).ToList();
            for (int c = 0; c < MainCols; c++)
                _entries[r, c].Text = main[c].ToString();
            _entries[r, PBCol].Text = rng.Next(1, 27).ToString();
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
        for (int i = 0; i < 10; i++) Preferences.Remove(SetKey(i));
        ClearAllEntries();
        UpdateSlotPicker();
        if (sender is Button btn)
        {
            var orig = btn.Text; var origColor = btn.BackgroundColor;
            btn.Text = "Cleared"; btn.BackgroundColor = Color.FromArgb("#1B5E20");
            await Task.Delay(1200);
            btn.Text = orig; btn.BackgroundColor = origColor;
        }
    }

    private void BtnVoice_Clicked(object sender, EventArgs e)
    {
#if IOS
        if (_voiceOn) StopVoice(); else StartVoice();
#endif
    }

    void StartVoice()
    {
        _voiceRow = 0; _voiceCol = 0;
        VoiceSkipFilled();
        if (_voiceRow >= Rows) { lblStatus.Text = "No empty cells"; return; }
        _voiceOn = true;
        btnVoice.BackgroundColor = Colors.Red;
        SetVoiceTarget();
#if IOS
        Services.VoiceNumberService.StatusUpdate += OnVoiceStatus;
        Services.VoiceNumberService.StartContinuous(OnVoiceNumbers);
#endif
    }

    void StopVoice()
    {
        _voiceOn = false;
        ClearVoiceTarget();
#if IOS
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
            int maxForCol = _voiceCol < MainCols ? 69 : 26;
            if (n >= 1 && n <= maxForCol)
            {
                _voiceSettingText = true;
                _entries[_voiceRow, _voiceCol].Text = n.ToString();
                _voiceSettingText = false;
                _voiceCol++;
                if (_voiceCol >= TotalCols) { _voiceCol = 0; _voiceRow++; }
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
            if (_voiceCol >= TotalCols) { _voiceCol = 0; _voiceRow++; }
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
                "Powerball", "pb_set_", _activeSlot < 0 ? 0 : _activeSlot, GetCurrentEntryString());
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
            Format = "MMM d, yyyy", FontSize = 14, Date = DateTime.Today,
            MinimumDate = new DateTime(2020, 1, 1), MaximumDate = new DateTime(2035, 12, 31),
            TextColor = Colors.White,
        };
        _advEndPicker = new DatePicker
        {
            Format = "MMM d, yyyy", FontSize = 14, Date = DateTime.Today,
            MinimumDate = new DateTime(2020, 1, 1), MaximumDate = new DateTime(2035, 12, 31),
            TextColor = Colors.White,
        };

        var btnClear  = new Button { Text = "Clear",  BackgroundColor = Color.FromArgb("#4B5563"), TextColor = Colors.White, CornerRadius = 10, HeightRequest = 42, FontSize = 13 };
        var btnCancel = new Button { Text = "Cancel", BackgroundColor = Color.FromArgb("#1E293B"), TextColor = Colors.White, CornerRadius = 10, HeightRequest = 42, FontSize = 13 };
        var btnOk     = new Button { Text = "OK",     BackgroundColor = Color.FromArgb("#2563EB"), TextColor = Colors.White, CornerRadius = 10, HeightRequest = 42, FontSize = 13, FontAttributes = FontAttributes.Bold };

        btnClear.Clicked += (_, _) =>
        {
            if (_advRow < 0) return;
            _playStart[_advRow] = null;
            _playEnd[_advRow]   = null;
            UpdateResultBackground(_advRow);
            SaveAdvanceDates(_activeSlot);
            _advOverlay!.IsVisible = false;
        };
        btnCancel.Clicked += (_, _) => _advOverlay!.IsVisible = false;
        btnOk.Clicked += (_, _) =>
        {
            if (_advRow < 0) return;
            _playStart[_advRow] = _advStartPicker!.Date;
            _playEnd[_advRow]   = _advEndPicker!.Date;
            UpdateResultBackground(_advRow);
            SaveAdvanceDates(_activeSlot);
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
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    new Label { Text = "Advance Play Dates", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center },
                    new Label { Text = "Play From", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                    _advStartPicker,
                    new Label { Text = "Play To",   FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                    _advEndPicker,
                    btnRow,
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

    private void ShowAdvancePlayOverlay(int row)
    {
        _advRow = row;
        _advStartPicker!.Date = _playStart[row] ?? DateTime.Today;
        _advEndPicker!.Date   = _playEnd[row]   ?? DateTime.Today;
        _advOverlay!.IsVisible = true;
    }

    private void UpdateResultBackground(int r)
    {
        bool hasDate = _playStart[r].HasValue || _playEnd[r].HasValue;
        _results[r].BackgroundColor = hasDate ? Color.FromArgb("#1A3A8A") : Colors.Transparent;
        bool showingResult = !string.IsNullOrEmpty(_results[r].Text) && _results[r].Text != "+";
        if (!showingResult)
        {
            _results[r].Text = "+";
            _results[r].TextColor = hasDate ? Colors.White : Color.FromArgb("#4B6A8A");
        }
    }

    private void UpdateAllResultBackgrounds()
    {
        for (int r = 0; r < Rows; r++) UpdateResultBackground(r);
    }

    private void SaveAdvanceDates(int slot)
    {
        if (slot < 0) return;
        var parts = new string[Rows];
        for (int r = 0; r < Rows; r++)
        {
            string s = _playStart[r].HasValue ? _playStart[r]!.Value.ToString("yyyyMMdd") : "";
            string e = _playEnd[r].HasValue   ? _playEnd[r]!.Value.ToString("yyyyMMdd")   : "";
            parts[r] = $"{s}~{e}";
        }
        Preferences.Set(AdvDatesKey(slot), string.Join("|", parts));
    }

    public void FlushAdvanceDates()
    {
        if (_activeSlot >= 0) SaveAdvanceDates(_activeSlot);
    }

    private void LoadAdvanceDates(int slot)
    {
        Array.Clear(_playStart, 0, Rows);
        Array.Clear(_playEnd,   0, Rows);
        if (slot < 0) return;
        string raw = Preferences.Get(AdvDatesKey(slot), "");
        if (string.IsNullOrEmpty(raw)) return;
        var parts = raw.Split('|');
        for (int r = 0; r < Rows && r < parts.Length; r++)
        {
            var pair = parts[r].Split('~');
            if (pair.Length == 2)
            {
                if (DateTime.TryParseExact(pair[0], "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var sd))
                    _playStart[r] = sd;
                if (DateTime.TryParseExact(pair[1], "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var ed))
                    _playEnd[r] = ed;
            }
        }
    }

    private bool SlotHasFutureAdvDate(int slot)
    {
        var today = DateTime.Today;
        if (slot == _activeSlot)
        {
            for (int r = 0; r < Rows; r++)
            {
                var refDate = _playEnd[r] ?? _playStart[r];
                if (refDate.HasValue && refDate.Value >= today) return true;
            }
            return false;
        }
        if (string.IsNullOrEmpty(Preferences.Get(SetKey(slot), ""))) return false;
        string raw = Preferences.Get(AdvDatesKey(slot), "");
        if (string.IsNullOrEmpty(raw)) return false;
        foreach (var part in raw.Split('|'))
        {
            var pair = part.Split('~');
            if (pair.Length != 2) continue;
            DateTime? end = null, start = null;
            if (DateTime.TryParseExact(pair[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var ed)) end = ed;
            if (DateTime.TryParseExact(pair[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var sd)) start = sd;
            var refDate = end ?? start;
            if (refDate.HasValue && refDate.Value >= today) return true;
        }
        return false;
    }

    private string AdvDatesKey(int slot) => $"pb_adv_{slot}";
    private void PartialClearSlot(int slot)
    {
        string raw = Preferences.Get(SetKey(slot), "");
        if (string.IsNullOrEmpty(raw)) return;
        var vals = raw.Split('|');
        string advRaw = Preferences.Get(AdvDatesKey(slot), "");
        var advParts = string.IsNullOrEmpty(advRaw) ? new string[Rows] : advRaw.Split('|');
        if (advParts.Length < Rows) Array.Resize(ref advParts, Rows);
        var today = DateTime.Today;
        for (int r = 0; r < Rows; r++)
        {
            bool keep = false;
            if (r < advParts.Length && advParts[r] != null)
            {
                var pair = advParts[r].Split('~');
                if (pair.Length == 2)
                {
                    DateTime? end = null, start = null;
                    if (DateTime.TryParseExact(pair[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var ed)) end = ed;
                    if (DateTime.TryParseExact(pair[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var sd)) start = sd;
                    var refDate = end ?? start;
                    keep = refDate.HasValue && refDate.Value >= today;
                }
            }
            if (!keep)
            {
                for (int c = 0; c < TotalCols; c++)
                    if (r * TotalCols + c < vals.Length) vals[r * TotalCols + c] = "";
                if (r < advParts.Length) advParts[r] = "~";
            }
        }
        string newData = string.Join("|", vals);
        if (newData.Replace("|", "").Trim().Length == 0)
        { Preferences.Remove(SetKey(slot)); Preferences.Remove(AdvDatesKey(slot)); }
        else
        { Preferences.Set(SetKey(slot), newData); Preferences.Set(AdvDatesKey(slot), string.Join("|", advParts)); }
    }
}
