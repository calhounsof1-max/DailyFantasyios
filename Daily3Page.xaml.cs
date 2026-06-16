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
    readonly CheckBox[] _permChks = new CheckBox[Rows];
    readonly Label[]    _permLabels = new Label[Rows];
    string[] _betTypes = Enumerable.Repeat("S", Rows).ToArray();
    static readonly string[] BetCycle = ["S", "B", "S&B"];
    int  _activeSlot = -1;
    bool _suppressPickerEvent = false;
    bool _suppressExcl = false;
    bool _loading = false;
    readonly Dictionary<int, (string entries, string betTypes)> _slotCache = new();
    View? _highlightedView;

    List<(string DateLabel, int[] Midday, int[] Evening)> _draws = new();
    bool _drawsLoaded = false;

    int[]? _winMidday;
    int[]? _winEvening;
    bool _isPanning = false;
    bool _voiceOn = false;
    bool _voiceSettingText = false;
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
        foreach (var bt in BetCycle) allBetPicker.Items.Add(bt);
        _suppressPickerEvent = true;
        allBetPicker.SelectedIndex = 0;
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

    private async void BtnGoBack_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        await GoBack();
    }

    private async void BtnGoHome_Clicked(object sender, EventArgs e) =>
        await Shell.Current.Navigation.PopToRootAsync(false);

    private async void BtnGames_Clicked(object sender, EventArgs e) => await GameNavHelper.ShowGamesDropdown(this);

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
    }

    protected override void OnAppearing()
    {
        this.TranslateTo(0, 0, 220, Easing.CubicOut);
        base.OnAppearing();

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
        else if (ComingFrom == "mm")
        {
            btnBack.Text = "← MM";
            btnBack.BackgroundColor = Color.FromArgb("#F57F17");
        }
        else
        {
            btnBack.Text = "← PB";
            btnBack.BackgroundColor = Color.FromArgb("#C62828");
        }

        _ = LoadAllDraws();
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
            Preferences.Set("d3_active_slot", _activeSlot);
    }

    private void SaveEntries()
    {
        var vals = new string[Rows * Cols];
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
                vals[r * Cols + c] = _entries[r, c].Text ?? "";
        Preferences.Set("d3_entries", string.Join("|", vals));
        Preferences.Set("d3_bettypes", string.Join("|", _betTypes));
        Preferences.Set("d3_from", fromEntry.Text ?? "");
    }

    private void LoadEntries()
    {
        _loading = true;
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
        fromEntry.Text = Preferences.Get("d3_from", "");
        _loading = false;
        if (int.TryParse(fromEntry.Text, out int n))
            HighlightRows(n);
    }

    private void LoadBetTypes(string key) =>
        LoadBetTypes_FromString(Preferences.Get(key, ""));

    private void LoadBetTypes_FromString(string value)
    {
        var parts = value.Split('|');
        for (int r = 0; r < Rows; r++)
        {
            var val = r < parts.Length ? parts[r] : "S";
            if (val == "S+B") val = "S&B";
            _betTypes[r] = BetCycle.Contains(val) ? val : "S";
            UpdateBetBtn(r);
        }
    }

    // ── Saved Number slots ───────────────────────────────────────────────────

    private string SetKey(int slot) => $"d3_set_{slot}";
    private string BetKey(int slot) => $"d3_btypes_{slot}";

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
        {
            for (int c = 0; c < Cols; c++)
                _entries[r, c].Text = "";
            _betTypes[r] = "S";
            UpdateBetBtn(r);
        }
        foreach (var lbl in _results) lbl.Text = "";
        if (int.TryParse(fromEntry.Text, out int n)) HighlightRows(n);
    }

    private void ClearRow(int r)
    {
        for (int c = 0; c < Cols; c++)
            _entries[r, c].Text = "";
        _results[r].Text = "";
        _betTypes[r] = "S";
        UpdateBetBtn(r);
        if (_permChks[r] != null) _permChks[r].IsChecked = false;
        _permLabels[r].IsVisible = false;
        SaveEntries();
    }

    private void SaveSet(int slot)
    {
        string data = GetCurrentEntryString();
        bool isEmpty = data.Replace("|", "").Trim().Length == 0;
        if (isEmpty)
        {
            Preferences.Remove(SetKey(slot));
            Preferences.Remove(BetKey(slot));
        }
        else
        {
            Preferences.Set(SetKey(slot), data);
            Preferences.Set(BetKey(slot), string.Join("|", _betTypes));
        }
        UpdateSlotPicker();
    }

    private void FillFromSlot(int slot)
    {
        var saved = Preferences.Get(SetKey(slot), "");
        if (string.IsNullOrEmpty(saved)) return;
        _loading = true;
        var vals = saved.Split('|');
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Cols; c++)
            {
                int idx = r * Cols + c;
                _entries[r, c].Text = idx < vals.Length ? vals[idx] : "";
            }
        LoadBetTypes(BetKey(slot));
        _loading = false;
        CheckAll();
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
            _slotCache[_activeSlot] = (GetCurrentEntryString(), string.Join("|", _betTypes));
        _activeSlot = slot;
        Preferences.Set("d3_active_slot", slot);
        ClearAllEntries();
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
            _loading = false;
            CheckAll();
        }
        else if (SlotHasData(slot))
            FillFromSlot(slot);
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
        var wrapper = rowsContainer.Children[rowIndex] as Layout;
        if (wrapper == null || wrapper.Children.Count < 1) return;
        var rowView = wrapper.Children[0] as View;
        if (rowView == null) return;
        _highlightedView = rowView;
        rowView.BackgroundColor = Color.FromArgb("#FFF176");
        if (rowsContainer.Parent is ScrollView sv)
            await sv.ScrollToAsync(wrapper, ScrollToPosition.MakeVisible, true);
        await Task.Delay(2000);
        rowView.BackgroundColor = Colors.White;
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
                    new ColumnDefinition(GridLength.Auto),       // 6: result
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
                FontSize = 11,
                TextColor = Color.FromArgb("#1565C0"),
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 18,
            };
            rowNum.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => ClearRow(rowIdx))
            });
            Grid.SetColumn(rowNum, 0);
            row.Children.Add(rowNum);

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
                entry.Focused += (_, _) => { if (!_loading) Dispatcher.Dispatch(() => _entries[row_, col_].Text = ""); };
                entry.TextChanged += (_, _) =>
                {
                    if (_loading) return;
                    if (!_voiceSettingText && _entries[row_, col_].Text?.Length == 1)
                        AdvanceFocus(row_, col_);
                    SaveEntries();
                    CheckAll();
                    UpdatePermsIfChecked(row_);
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
                Text = "S",
                FontSize = 9,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#1565C0"),
                WidthRequest = 38,
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

            var result = new Label
            {
                Text = "",
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#888"),
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 58,
                HorizontalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap,
            };
            _results[r] = result;
            Grid.SetColumn(result, 6);
            row.Children.Add(result);

            var permsLabel = new Label
            {
                IsVisible = false,
                FontSize = 10,
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
            rowsContainer.Children.Add(wrapper);
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
        btn.Text = _betTypes[row];
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
        string bt = BetCycle[idx];
        string allBets = string.Join("|", Enumerable.Repeat(bt, Rows));
        for (int r = 0; r < Rows; r++)
        {
            _betTypes[r] = bt;
            UpdateBetBtn(r);
        }
        for (int s = 0; s < 10; s++)
        {
            if (SlotHasData(s))
                Preferences.Set(BetKey(s), allBets);
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
        {
            _entries[nextRow, nextCol].Text = "";
            Dispatcher.Dispatch(() => _entries[nextRow, nextCol].Focus());
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

    // ── Load draws ───────────────────────────────────────────────────────────

    private async Task LoadAllDraws()
    {
        if (_drawsLoaded)
        {
            bool hasTodayDraw = _draws.Any(d =>
                DateTime.TryParse(d.DateLabel, out var dt) && dt.Date == DateTime.Today);
            if (hasTodayDraw) return;
            _drawsLoaded = false;
            _draws.Clear();
        }

        spinner.IsVisible = true;
        spinner.IsRunning = true;
        lblDrawDate.Text = "Fetching Daily 3 draws...";

        var raw = await GetDataEntry.LoadD3CsvDraws();

        spinner.IsVisible = false;
        spinner.IsRunning = false;

        if (raw.Count == 0)
        {
            string errMsg = string.IsNullOrEmpty(GetDataEntry.LastError)
                ? "Daily3: Could not load draw history"
                : $"Daily3: {GetDataEntry.LastError}";
            lblDrawDate.Text = errMsg;
            lblStatus.Text = errMsg;
            return;
        }

        // Group by date: each date may have Midday and/or Evening
        var grouped = raw
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
                        Midday:  midday.Numbers  ?? Array.Empty<int>(),
                        Evening: evening.Numbers ?? Array.Empty<int>());
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
            drawDatePicker.MaximumDate = dates.First();
            var targetDate = dates.First();
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
            string? win = CheckWin(userNums, _winMidday, bt);
            if (win == null) win = CheckWin(userNums, _winEvening, bt);

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
                _results[r].Text = win;
                _results[r].TextColor = isStrWin
                    ? Color.FromArgb("#1B5E20")
                    : Color.FromArgb("#E65100");
            }
        }
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

        if (choice.StartsWith("Clear ALL"))
        {
            bool confirm = await DisplayAlert("Clear All Sets", "Remove all 10 saved sets?", "Yes", "Cancel");
            if (!confirm) return;
            for (int i = 0; i < 10; i++)
            {
                Preferences.Remove(SetKey(i));
                Preferences.Remove(BetKey(i));
            }
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
            btn.Text = "Cleared"; btn.BackgroundColor = Color.FromArgb("#1B5E20");
            await Task.Delay(1200);
            btn.Text = orig; btn.BackgroundColor = origColor;
        }
    }

    private void BtnVoice_Clicked(object sender, EventArgs e)
    {
#if IOS
        if (!Services.VoiceNumberService.IsAvailable) { lblStatus.Text = "Speech recognition not available"; return; }
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
            _slotCache[_activeSlot] = (GetCurrentEntryString(), string.Join("|", _betTypes));
        SaveEntries();
        foreach (var (slot, (entries, betTypes)) in _slotCache)
        {
            bool isEmpty = entries.Replace("|", "").Trim().Length == 0;
            if (isEmpty)
            {
                Preferences.Remove(SetKey(slot));
                Preferences.Remove(BetKey(slot));
            }
            else
            {
                Preferences.Set(SetKey(slot), entries);
                Preferences.Set(BetKey(slot), betTypes);
            }
        }
        UpdateSlotPicker();
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

}
