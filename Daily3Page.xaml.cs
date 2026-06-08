using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class Daily3Page : ContentPage
{
    const int Rows = 10;
    const int Cols = 3;

    readonly Label[] _wLabels;   // midday winning numbers
    readonly Label[] _eLabels;   // evening winning numbers

    readonly Entry[,] _entries = new Entry[Rows, Cols];
    readonly Label[]  _results = new Label[Rows];
    readonly Button[] _betTypeBtns = new Button[Rows];
    readonly CheckBox[] _permChks = new CheckBox[Rows];
    readonly Label[]    _permLabels = new Label[Rows];
    string[] _betTypes = Enumerable.Repeat("S", Rows).ToArray();
    static readonly string[] BetCycle = ["S", "B", "S&B"];
    int _activeSlot = -1;
    bool _suppressPickerEvent = false;
    bool _loading = false;

    // Grouped by date: each entry = (dateLabel, midday[], evening[])
    List<(string DateLabel, int[]? Midday, int[]? Evening)> _drawsByDate = new();
    bool _drawsLoaded = false;

    int[]? _middayNums;
    int[]? _eveningNums;
    bool _isPanning = false;

    // "pb" = came via carousel from Powerball; "main" = navigated directly from MainPage
    internal static string ComingFrom { get; set; } = "pb";

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
        _ = GoBackWithSlide();
        return true;
    }

    private async Task GoBack()
    {
        if (ComingFrom == "pb")
            AppShell.PowerballPageInstance.PrePosition(false);
        else // "main"
        {
            double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
            Shell.Current.CurrentPage.TranslationX = -w;
        }
        await Shell.Current.GoToAsync("..", false);
    }

    private async Task GoBackWithSlide()
    {
        if (_isPanning) return;
        await GoBack();
    }

    private async void BtnGoBack_Clicked(object sender, EventArgs e) => await GoBackWithSlide();

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
        if (ComingFrom == "main")
        {
            btnBack.Text = "← HOME";
            btnBack.BackgroundColor = Color.FromArgb("#FF8F00");
        }
        else
        {
            btnBack.Text = "← PB";
            btnBack.BackgroundColor = Color.FromArgb("#C62828");
        }

        base.OnAppearing();
        _ = LoadAllDraws();
        Dispatcher.Dispatch(() =>
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
            UpdateSlotPicker();
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
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

    private void LoadBetTypes(string key)
    {
        var parts = Preferences.Get(key, "").Split('|');
        for (int r = 0; r < Rows; r++)
        {
            var val = r < parts.Length ? parts[r] : "S";
            if (val == "S+B") val = "S&B"; // normalize old saved format
            _betTypes[r] = BetCycle.Contains(val) ? val : "S";
            UpdateBetBtn(r);
        }
    }

    // ── Saved Number slots ───────────────────────────────────────────────────

    private string SetKey(int slot)  => $"d3_set_{slot}";
    private string BetKey(int slot)  => $"d3_btypes_{slot}";

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

    private string SlotLabel(int slot) =>
        SlotHasData(slot) ? $"Set {slot + 1}  ✓" : $"Set {slot + 1}";

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
    }

    private void SlotPicker_Changed(object sender, EventArgs e)
    {
        if (_suppressPickerEvent) return;
        int slot = slotPicker.SelectedIndex;
        if (slot < 0) return;
        _activeSlot = slot;
        Preferences.Set("d3_active_slot", slot);
        ClearAllEntries();
        if (SlotHasData(slot))
            FillFromSlot(slot);
        UpdateSlotPicker();
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

    private void AllBetPicker_Changed(object sender, EventArgs e)
    {
        if (_suppressPickerEvent) return;
        int idx = allBetPicker.SelectedIndex;
        if (idx < 0) return;
        string bt = BetCycle[idx];
        string allBets = string.Join("|", Enumerable.Repeat(bt, Rows));
        // Apply to current rows
        for (int r = 0; r < Rows; r++)
        {
            _betTypes[r] = bt;
            UpdateBetBtn(r);
        }
        // Apply to all saved slots that have data
        for (int s = 0; s < 10; s++)
        {
            if (SlotHasData(s))
                Preferences.Set(BetKey(s), allBets);
        }
        SaveEntries();
        CheckAll();
    }

    private void FromEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(fromEntry.Text, out int n))
            HighlightRows(n);
        else
            HighlightRows(0);
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
                    if (_entries[row_, col_].Text?.Length == 1)
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
                    // EvaluateJavaScriptAsync may wrap the string in quotes with escape chars
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

    // ── Load draws ───────────────────────────────────────────────────────────

    private async Task LoadAllDraws()
    {
        // Re-fetch if today's midday hasn't been loaded yet (e.g. cached before 1pm draw)
        if (_drawsLoaded)
        {
            bool hasTodayMidday = _drawsByDate.Any(d =>
                DateTime.TryParse(d.DateLabel, out var dt) &&
                dt.Date == DateTime.Today && d.Midday != null);
            if (hasTodayMidday) return;
            _drawsLoaded = false;
            _drawsByDate.Clear();
        }

        spinner.IsVisible = true;
        spinner.IsRunning = true;
        lblDrawDate.Text = "Fetching Daily 3 draws from calottery.com...";

        var raw = await GetDataEntry.GetDaily3Draws(50);

        spinner.IsVisible = false;
        spinner.IsRunning = false;

        if (raw.Count == 0)
        {
            string errMsg = string.IsNullOrEmpty(GetDataEntry.LastError)
                ? "Daily3: Could not fetch — check internet connection"
                : $"Daily3: {GetDataEntry.LastError}";
            lblDrawDate.Text = errMsg;
            lblStatus.Text = errMsg;
            _ = Logger.LogAsync(errMsg);
            return;
        }

        // Group by date label; within same date, lower drawNum = midday, higher = evening
        var groups = raw
            .GroupBy(d => d.DrawDate)
            .Select(g =>
            {
                var ordered = g.OrderBy(d => d.DrawNumber).ToList();
                int[]? midday  = ordered.Count >= 1 ? ordered[0].Numbers : null;
                int[]? evening = ordered.Count >= 2 ? ordered[1].Numbers : null;
                return (DateLabel: g.Key, Midday: midday, Evening: evening,
                        Date: DateTime.TryParse(g.First().DrawDate, out var dt) ? dt : DateTime.MinValue);
            })
            .OrderByDescending(g => g.Date)
            .ToList();

        _drawsByDate = groups.Select(g => (g.DateLabel, g.Midday, g.Evening)).ToList();
        _drawsLoaded = true;

        bool todayAvailable = groups.Any(g => g.Date.Date == DateTime.Today);
        var defaultGroup = todayAvailable
            ? groups.First()
            : groups.FirstOrDefault(g => g.Date.Date < DateTime.Today);

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var dates = groups.Select(g => g.Date.Date).ToList();
            drawDatePicker.MinimumDate = dates.Last();
            drawDatePicker.MaximumDate = dates.First();
            var targetDate = defaultGroup.Date != default ? defaultGroup.Date.Date : dates.First();
            if (drawDatePicker.Date == targetDate)
                ShowDrawForDate(targetDate); // DateSelected won't fire if date unchanged
            else
                drawDatePicker.Date = targetDate;
        });
    }

    private void ShowDrawForDate(DateTime date)
    {
        // Find the most recent midday draw on or before the selected date
        var middayMatch = _drawsByDate.FirstOrDefault(d =>
            d.Midday != null &&
            DateTime.TryParse(d.DateLabel, out var dt) && dt.Date <= date.Date);

        // Find the most recent evening draw on or before the selected date
        var eveningMatch = _drawsByDate.FirstOrDefault(d =>
            d.Evening != null &&
            DateTime.TryParse(d.DateLabel, out var dt) && dt.Date <= date.Date);

        if (middayMatch.DateLabel == null && eveningMatch.DateLabel == null) return;

        _middayNums  = middayMatch.Midday;
        _eveningNums = eveningMatch.Evening;

        // Build date label: show separate dates if midday and evening are from different days
        if (middayMatch.DateLabel != null && eveningMatch.DateLabel != null &&
            middayMatch.DateLabel != eveningMatch.DateLabel)
            lblDrawDate.Text = $"Mid: {middayMatch.DateLabel}  Eve: {eveningMatch.DateLabel}";
        else
            lblDrawDate.Text = middayMatch.DateLabel ?? eveningMatch.DateLabel ?? "";

        // Midday row
        for (int i = 0; i < 3; i++)
            _wLabels[i].Text = _middayNums != null ? _middayNums[i].ToString() : "?";

        // Evening row
        for (int i = 0; i < 3; i++)
            _eLabels[i].Text = _eveningNums != null ? _eveningNums[i].ToString() : "-";

        if (_results[0].Text != "") CheckAll();
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

            var sorted = userNums.OrderBy(x => x).ToArray();
            string bt = _betTypes[r]; // "S", "B", or "S&B"

            // Determine raw draw match per session
            string? dayWin = null;
            if (_middayNums != null)
            {
                if (userNums.SequenceEqual(_middayNums))
                    dayWin = "Straight";
                else if (sorted.SequenceEqual(_middayNums.OrderBy(x => x)))
                    dayWin = "Box";
            }

            string? ngtWin = null;
            if (_eveningNums != null)
            {
                if (userNums.SequenceEqual(_eveningNums))
                    ngtWin = "Straight";
                else if (sorted.SequenceEqual(_eveningNums.OrderBy(x => x)))
                    ngtWin = "Box";
            }

            // Apply bet type filter
            if (bt == "S")
            {
                if (dayWin != "Straight") dayWin = null;
                if (ngtWin != "Straight") ngtWin = null;
            }
            // "B" and "S&B": all matches count (straight also wins as box for "B")

            bool anyWin = dayWin != null || ngtWin != null;
            bool anyStr = bt != "B" && (dayWin == "Straight" || ngtWin == "Straight");

            // Entry background
            var bg = !anyWin ? Color.FromArgb("#FFCDD2")
                   : anyStr  ? Color.FromArgb("#F9A825")
                             : Color.FromArgb("#FFF9C4");
            for (int c = 0; c < Cols; c++)
                _entries[r, c].BackgroundColor = bg;

            // Result label
            if (!anyWin)
            {
                _results[r].Text = "✗";
                _results[r].TextColor = Color.FromArgb("#C62828");
            }
            else
            {
                string WinLabel(string? w) =>
                    w == null ? "" :
                    bt == "S&B" && w == "Straight" ? "S&B!" :
                    bt == "B" ? "Box" : w;

                string text;
                string dl = WinLabel(dayWin), nl = WinLabel(ngtWin);
                if (dayWin != null && ngtWin != null)
                    text = dl == nl ? $"D+N\n{dl}" : $"D:{dl}\nN:{nl}";
                else if (dayWin != null)
                    text = $"Day\n{dl}";
                else
                    text = $"Eve\n{nl}";

                _results[r].Text = text;
                _results[r].TextColor = anyStr ? Color.FromArgb("#1B5E20") : Color.FromArgb("#E65100");
            }
        }
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
        _drawsByDate.Clear();
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
            var orig = btn.Text;
            var origColor = btn.BackgroundColor;
            btn.Text = "Cleared";
            btn.BackgroundColor = Color.FromArgb("#1B5E20");
            await Task.Delay(1200);
            btn.Text = orig;
            btn.BackgroundColor = origColor;
        }
    }

    private async void BtnSave_Clicked(object sender, EventArgs e)
    {
        SaveEntries();
        if (_activeSlot >= 0)
            SaveSet(_activeSlot);
        if (sender is Button btn)
        {
            var orig = btn.Text;
            var origColor = btn.BackgroundColor;
            btn.Text = _activeSlot >= 0 ? $"SET {_activeSlot + 1} ✓" : "SAVED";
            btn.BackgroundColor = Color.FromArgb("#1B5E20");
            await Task.Delay(1200);
            btn.Text = orig;
            btn.BackgroundColor = origColor;
        }
    }

}
