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

    int[] _winningMainNums = Array.Empty<int>();
    int   _winningPB       = 0;
    List<(DateTime Date, string Label, int[] MainNumbers, int PBNumber)> _allDraws = new();
    bool _drawsLoaded = false;
    bool _isPanning   = false;

    // "sl" = came via carousel from SuperLotto; "main" = navigated directly
    internal static string ComingFrom { get; set; } = "sl";

    public PowerballPage()
    {
        InitializeComponent();
        _wBorders = new[] { W1, W2, W3, W4, W5 };
        _wLabels  = new[] { lblW1, lblW2, lblW3, lblW4, lblW5 };
        BuildRows();
        BuildSlotPicker();
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
                    Daily3Page.ComingFrom = "pb";
                    AppShell.Daily3PageInstance.PrePosition(true);
                    await Shell.Current.GoToAsync(nameof(Daily3Page), false);
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

    private async void BtnGoBack_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        await GoBackAsync();
    }

    private async void BtnGoD3_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        Daily3Page.ComingFrom = "pb";
        AppShell.Daily3PageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(Daily3Page), false);
    }

    internal void PrePosition(bool fromRight)
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        TranslationX = fromRight ? w : -w;
    }

    protected override void OnAppearing()
    {
        this.TranslateTo(0, 0, 220, Easing.CubicOut);
        if (ComingFrom == "main")
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
            UpdateSlotPicker();
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
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
    }

    private bool SlotHasData(int slot) =>
        !string.IsNullOrEmpty(Preferences.Get(SetKey(slot), ""));

    private void BuildSlotPicker()
    {
        for (int i = 0; i < 10; i++)
            slotPicker.Items.Add(SlotLabel(i));
    }

    private string SlotLabel(int slot) =>
        SlotHasData(slot) ? $"Set {slot + 1}  ✓" : $"Set {slot + 1}";

    private void UpdateSlotPicker()
    {
        _suppressPickerEvent = true;
        for (int i = 0; i < 10; i++)
            slotPicker.Items[i] = SlotLabel(i);
        slotPicker.SelectedIndex = _activeSlot;
        _suppressPickerEvent = false;
    }

    private void SlotPicker_Changed(object sender, EventArgs e)
    {
        if (_suppressPickerEvent) return;
        int slot = slotPicker.SelectedIndex;
        if (slot < 0) return;
        _activeSlot = slot;
        Preferences.Set("pb_active_slot", slot);
        ClearAllEntries();
        if (SlotHasData(slot)) FillFromSlot(slot);
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
                FontSize = 13,
                TextColor = Color.FromArgb("#FF7043"),
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 24,
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
                int row_ = r, col_ = c;
                entry.TextChanged += (_, _) =>
                {
                    if (_entries[row_, col_].Text?.Length == 2) AdvanceFocus(row_, col_);
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
            int mrow = r;
            pbEntry.TextChanged += (_, _) =>
            {
                if (_entries[mrow, PBCol].Text?.Length == 2) AdvanceFocus(mrow, PBCol);
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
                WidthRequest = 40,
                HorizontalTextAlignment = TextAlignment.Center,
            };
            _results[r] = result;
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
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.Black,
            BackgroundColor = bg,
            HorizontalTextAlignment = TextAlignment.Center,
            HeightRequest = 44,
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
        lblDrawDate.Text = match.Label;

        for (int i = 0; i < _wLabels.Length; i++)
            _wLabels[i].Text = match.MainNumbers[i].ToString();
        lblWPB.Text = match.PBNumber.ToString();

        if (_results[0].Text != "") CheckAll();
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

    private async void BtnSave_Clicked(object sender, EventArgs e)
    {
        SaveEntries();
        if (_activeSlot >= 0) SaveSet(_activeSlot);
        if (sender is Button btn)
        {
            var orig = btn.Text; var origColor = btn.BackgroundColor;
            btn.Text = _activeSlot >= 0 ? $"SET {_activeSlot + 1} ✓" : "SAVED";
            btn.BackgroundColor = Color.FromArgb("#1B5E20");
            await Task.Delay(1200);
            btn.Text = orig; btn.BackgroundColor = origColor;
        }
    }

}
