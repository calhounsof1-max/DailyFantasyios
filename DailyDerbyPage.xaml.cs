using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class DailyDerbyPage : ContentPage
{
    const int Rows      = 10;
    const int HorseCols = 3;

    // Horse name lookup (1-12)
    static readonly string[] HorseNames =
    {
        "",              // 0 unused
        "Gold Rush",     // 1
        "Lucky Star",    // 2
        "Hot Shot",      // 3
        "Big Ben",       // 4
        "CA Classic",    // 5
        "Whirl Win",     // 6
        "Eureka",        // 7
        "Gorgeous Geo.", // 8
        "Win Spirit",    // 9
        "Solid Gold",    // 10
        "Money Bags",    // 11
        "Lucky Charms",  // 12
    };

    static string HorseName(int num) =>
        num >= 1 && num <= 12 ? HorseNames[num] : "";

    readonly Label[] _wHorseLabels;
    readonly Label[] _wNameLabels;

    readonly Entry[,] _horseEntries = new Entry[Rows, HorseCols];
    readonly Entry[]  _timeEntries  = new Entry[Rows];
    readonly Label[]  _results      = new Label[Rows];
    readonly CheckBox[] _permChks   = new CheckBox[Rows];
    readonly Label[]    _permLabels = new Label[Rows];

    int  _activeSlot = -1;
    bool _suppressPickerEvent = false;
    bool _suppressExcl = false;
    bool _loading = false;
    readonly Dictionary<int, string> _slotCache = new();
    View? _highlightedView;

    int[]?  _winHorses;
    string  _winTime = "";
    List<(string DateLabel, int[] Horses, string RaceTime)> _draws = new();
    bool _drawsLoaded = false;
    bool _isPanning   = false;
    bool _voiceOn = false;
    bool _voiceSettingText = false;
    int  _voiceRow = 0, _voiceCol = 0;
    Entry? _voiceTarget = null;
    Color _voiceTargetOldColor = Colors.White;

    // "d4" = came via carousel from Daily4; "main" = navigated directly
    internal static string ComingFrom { get; set; } = "d4";

    public DailyDerbyPage()
    {
        InitializeComponent();
        _wHorseLabels = new[] { lblWH1, lblWH2, lblWH3 };
        _wNameLabels  = new[] { lblWN1, lblWN2, lblWN3 };
        BuildRows();
        BuildSlotPicker();
    }

    // ── Pan gesture ──────────────────────────────────────────────────────────

    double _panPeak;

    private async void OnPagePan(object? sender, PanUpdatedEventArgs e)
    {
        if (_isPanning) return;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panPeak = 0;
                break;
            case GestureStatus.Running:
                if (e.TotalX > _panPeak) _panPeak = e.TotalX;
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_panPeak > 40)
                {
                    _isPanning = true;
                    await GoBack();
                    _isPanning = false;
                }
                _panPeak = 0;
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
        if (ComingFrom == "d4")
            AppShell.Daily4PageInstance.PrePosition(false);
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

    private async void BtnBack_Clicked(object sender, EventArgs e)
    {
        if (_isPanning) return;
        await GoBack();
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
        else
        {
            btnBack.Text = "← D4";
            btnBack.BackgroundColor = Color.FromArgb("#00695C");
        }

        _ = LoadAllDraws();
        Dispatcher.Dispatch(() =>
        {
            int pendingRow = -1;
            if (PendingHighlight.HasPending && PendingHighlight.Game == "DD")
            {
                _activeSlot = PendingHighlight.Slot;
                pendingRow  = PendingHighlight.Row;
                PendingHighlight.Clear();
                Preferences.Set("dd_active_slot", _activeSlot);
                FillFromSlot(_activeSlot);
            }
            else
            {
                _activeSlot = Preferences.Get("dd_active_slot", -1);
                if (_activeSlot < 0)
                {
                    var current = Preferences.Get("dd_entries", "");
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
            Preferences.Set("dd_active_slot", _activeSlot);
    }

    // ── Entry persistence ────────────────────────────────────────────────────

    private void SaveEntries()
    {
        var vals = new string[Rows * 4];
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < HorseCols; c++)
                vals[r * 4 + c] = _horseEntries[r, c].Text ?? "";
            vals[r * 4 + 3] = _timeEntries[r].Text ?? "";
        }
        Preferences.Set("dd_entries", string.Join("|", vals));
        Preferences.Set("dd_from", fromEntry.Text ?? "");
    }

    private void LoadEntries()
    {
        _loading = true;
        var saved = Preferences.Get("dd_entries", "");
        if (!string.IsNullOrEmpty(saved))
            LoadFromValues(saved.Split('|'));
        fromEntry.Text = Preferences.Get("dd_from", "");
        _loading = false;
        if (int.TryParse(fromEntry.Text, out int n)) HighlightRows(n);
    }

    // Supports both 3-per-row (horse only, from ScanSlipPage) and 4-per-row (horse+time)
    private void LoadFromValues(string[] vals)
    {
        int perRow = vals.Length <= Rows * HorseCols ? HorseCols : 4;
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < HorseCols; c++)
            {
                int idx = r * perRow + c;
                _horseEntries[r, c].Text = idx < vals.Length ? vals[idx] : "";
            }
            int tIdx = r * perRow + 3;
            _timeEntries[r].Text = (perRow == 4 && tIdx < vals.Length) ? vals[tIdx] : "";
        }
    }

    private string GetCurrentEntryString()
    {
        var vals = new string[Rows * 4];
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < HorseCols; c++)
                vals[r * 4 + c] = _horseEntries[r, c].Text ?? "";
            vals[r * 4 + 3] = _timeEntries[r].Text ?? "";
        }
        return string.Join("|", vals);
    }

    private void ClearAllEntries()
    {
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < HorseCols; c++) _horseEntries[r, c].Text = "";
            _timeEntries[r].Text = "";
            _results[r].Text = "";
        }
        if (int.TryParse(fromEntry.Text, out int n)) HighlightRows(n);
    }

    private void ClearRow(int r)
    {
        for (int c = 0; c < HorseCols; c++) _horseEntries[r, c].Text = "";
        _timeEntries[r].Text = "";
        _results[r].Text = "";
        if (_permChks[r] != null) _permChks[r].IsChecked = false;
        _permLabels[r].IsVisible = false;
        SaveEntries();
    }

    // ── Saved Number slots ───────────────────────────────────────────────────

    private string SetKey(int slot) => $"dd_set_{slot}";

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
        _loading = true;
        LoadFromValues(saved.Split('|'));
        _loading = false;
        CheckAll();
    }

    private bool SlotHasData(int slot) =>
        !string.IsNullOrEmpty(Preferences.Get(SetKey(slot), ""));

    private string ExclKey(int slot) => $"excl_set_dd_{slot}";

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
            _slotCache[_activeSlot] = GetCurrentEntryString();
        _activeSlot = slot;
        Preferences.Set("dd_active_slot", slot);
        ClearAllEntries();
        if (_slotCache.TryGetValue(slot, out var cached))
        {
            _loading = true;
            LoadFromValues(cached.Split('|'));
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
            for (int c = 0; c < HorseCols; c++)
                _horseEntries[r, c].BackgroundColor = color;
            _timeEntries[r].BackgroundColor = color;
        }
    }

    private void FromEntry_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (int.TryParse(fromEntry.Text, out int n)) HighlightRows(n);
        else HighlightRows(0);
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
                    new ColumnDefinition(GridLength.Auto),            // row #
                    new ColumnDefinition(GridLength.Star),            // H1
                    new ColumnDefinition(GridLength.Star),            // H2
                    new ColumnDefinition(GridLength.Star),            // H3
                    new ColumnDefinition(GridLength.Auto),            // perm checkbox
                    new ColumnDefinition(new GridLength(60)),         // Time (3 digits)
                    new ColumnDefinition(GridLength.Auto),            // Result
                },
                ColumnSpacing = 3,
                BackgroundColor = Colors.White,
                Margin = new Thickness(0, 1),
                Padding = new Thickness(4, 2),
            };

            int rowIdx = r;
            var rowNum = new Label
            {
                Text = $"{r + 1,2}.",
                FontSize = 11,
                TextColor = Color.FromArgb("#5D4037"),
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 18,
            };
            rowNum.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => ClearRow(rowIdx))
            });
            Grid.SetColumn(rowNum, 0);
            row.Children.Add(rowNum);

            // Horse entries (H1, H2, H3)
            for (int c = 0; c < HorseCols; c++)
            {
                var entry = new Entry
                {
                    Keyboard = Keyboard.Numeric,
                    FontSize = 20,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.Black,
                    BackgroundColor = Color.FromArgb("#F5F5F5"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    HeightRequest = 50,
                    MaxLength = 2,
                };
                entry.HandlerChanged += ForceBlackText;
                AttachMaxClamp(entry, 12);

                int row_ = r, col_ = c;
                entry.Focused += (_, _) => { if (!_loading) Dispatcher.Dispatch(() => _horseEntries[row_, col_].Text = ""); };
                entry.TextChanged += (_, _) =>
                {
                    if (_loading) return;
                    if ((_horseEntries[row_, col_].Text?.Length ?? 0) == 2)
                        AdvanceHorseFocus(row_, col_);
                    SaveEntries();
                    CheckAll();
                    UpdatePermsIfChecked(row_);
                };

                _horseEntries[r, c] = entry;
                Grid.SetColumn(entry, c + 1);
                row.Children.Add(entry);
            }

            int chkRow = r;
            var permChk = new CheckBox
            {
                Color = Color.FromArgb("#5D4037"),
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(2, 0),
            };
            permChk.CheckedChanged += (_, _) => UpdatePermsIfChecked(chkRow);
            _permChks[r] = permChk;
            Grid.SetColumn(permChk, 4);
            row.Children.Add(permChk);

            // Time entry — 3-digit last-3-digits-of-race-time pick
            var timeEntry = new Entry
            {
                Keyboard = Keyboard.Numeric,
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.Black,
                BackgroundColor = Color.FromArgb("#F5F5F5"),
                HorizontalTextAlignment = TextAlignment.Center,
                HeightRequest = 50,
                MaxLength = 3,
                Placeholder = "000",
                PlaceholderColor = Color.FromArgb("#BDBDBD"),
            };
            timeEntry.HandlerChanged += ForceBlackText;

            int rowT = r;
            timeEntry.Focused += (_, _) => { if (!_loading) Dispatcher.Dispatch(() => _timeEntries[rowT].Text = ""); };
            timeEntry.TextChanged += (_, _) =>
            {
                if (_loading) return;
                if ((_timeEntries[rowT].Text?.Length ?? 0) == 3 && rowT + 1 < Rows)
                {
                    _horseEntries[rowT + 1, 0].Text = "";
                    Dispatcher.Dispatch(() => _horseEntries[rowT + 1, 0].Focus());
                }
                SaveEntries();
                CheckAll();
            };
            _timeEntries[r] = timeEntry;
            Grid.SetColumn(timeEntry, 5);
            row.Children.Add(timeEntry);

            // Result label
            var result = new Label
            {
                Text = "",
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#888"),
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 56,
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
                TextColor = Color.FromArgb("#5D4037"),
                BackgroundColor = Color.FromArgb("#EFEBE9"),
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
        var parts = new string[HorseCols];
        for (int c = 0; c < HorseCols; c++) parts[c] = _horseEntries[r, c].Text ?? "";
        if (parts.Any(p => string.IsNullOrEmpty(p)))
        {
            _permLabels[r].IsVisible = false;
            return;
        }
        var perms = GetUniquePerms(parts, " ");
        _permLabels[r].Text = string.Join("\n", perms);
        _permLabels[r].IsVisible = true;
    }

    static List<string> GetUniquePerms(string[] parts, string sep)
    {
        var set = new HashSet<string>();
        var arr = (string[])parts.Clone();
        PermuteDD(arr, 0, set, sep);
        return set.ToList();
    }

    static void PermuteDD(string[] arr, int start, HashSet<string> results, string sep)
    {
        if (start == arr.Length - 1) { results.Add(string.Join(sep, arr)); return; }
        for (int i = start; i < arr.Length; i++)
        {
            (arr[start], arr[i]) = (arr[i], arr[start]);
            PermuteDD(arr, start + 1, results, sep);
            (arr[start], arr[i]) = (arr[i], arr[start]);
        }
    }

    private void AdvanceHorseFocus(int row, int col)
    {
        if (col + 1 < HorseCols)
        {
            _horseEntries[row, col + 1].Text = "";
            Dispatcher.Dispatch(() => _horseEntries[row, col + 1].Focus());
        }
        else
        {
            // H3 → time entry for this row
            _timeEntries[row].Text = "";
            Dispatcher.Dispatch(() => _timeEntries[row].Focus());
        }
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
        lblDrawDate.Text = "Fetching Daily Derby draws from calottery.com...";

        var raw = await GetDataEntry.GetDailyDerbyDraws(50);

        spinner.IsVisible = false;
        spinner.IsRunning = false;

        if (raw.Count == 0)
        {
            string errMsg = string.IsNullOrEmpty(GetDataEntry.LastError)
                ? "Daily Derby: Could not fetch — check internet connection"
                : $"Daily Derby: {GetDataEntry.LastError}";
            lblDrawDate.Text = errMsg;
            lblStatus.Text   = errMsg;
            _ = Logger.LogAsync(errMsg);
            return;
        }

        _draws = raw
            .Select(d => (d.DrawDate,
                          Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                          d.Horses, d.RaceTime))
            .OrderByDescending(d => d.Date)
            .Select(d => (d.DrawDate, d.Horses, d.RaceTime))
            .ToList();

        _drawsLoaded = true;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var dates = _draws
                .Select(d => DateTime.TryParse(d.DateLabel, out var dt) ? dt.Date : DateTime.MinValue)
                .Where(d => d != DateTime.MinValue)
                .ToList();
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

        _winHorses = match.Horses;
        _winTime   = match.RaceTime ?? "";
        lblDrawDate.Text = match.DateLabel;

        for (int i = 0; i < 3; i++)
        {
            int num = _winHorses != null ? _winHorses[i] : 0;
            _wHorseLabels[i].Text = num > 0 ? num.ToString() : "?";
            _wNameLabels[i].Text  = num > 0 ? HorseName(num) : "";
        }
        if (!string.IsNullOrEmpty(_winTime))
        {
            string norm = NormalizeTime(_winTime);
            string last3 = norm.Length >= 3 ? norm[^3..] : norm;
            lblWTime.Text = $"{_winTime}  [{last3}]";
        }
        else
        {
            lblWTime.Text = "?:??.??";
        }

        CheckAll();
    }

    private void DrawDatePicker_DateSelected(object sender, DateChangedEventArgs e) =>
        ShowDrawForDate(e.NewDate ?? DateTime.Today);

    // ── Check ────────────────────────────────────────────────────────────────

    static string NormalizeTime(string t) =>
        new string(t.Where(char.IsDigit).ToArray());

    private void CheckAll()
    {
        string winTimeNorm = NormalizeTime(_winTime);
        string winTimeLast3 = winTimeNorm.Length >= 3 ? winTimeNorm[^3..] : winTimeNorm;

        for (int r = 0; r < Rows; r++)
        {
            var userHorses = new int[HorseCols];
            bool horsesAllFilled = true;
            for (int c = 0; c < HorseCols; c++)
            {
                if (!int.TryParse(_horseEntries[r, c].Text, out userHorses[c]))
                    horsesAllFilled = false;
            }

            string userTime = new string((_timeEntries[r].Text ?? "").Where(char.IsDigit).ToArray());
            bool timeEntered = userTime.Length == 3;

            // Nothing entered at all
            if (!horsesAllFilled && !timeEntered)
            {
                for (int c = 0; c < HorseCols; c++)
                    _horseEntries[r, c].BackgroundColor = Color.FromArgb("#F5F5F5");
                _timeEntries[r].BackgroundColor = Color.FromArgb("#F5F5F5");
                _results[r].Text = "";
                continue;
            }

            // No winning data yet
            if (_winHorses == null)
            {
                _results[r].Text = "";
                continue;
            }

            bool timeMatch = timeEntered && !string.IsNullOrEmpty(winTimeLast3)
                             && userTime == winTimeLast3;

            // Auto-detect all wins — CA Lottery DD has no separate Trifecta tier
            // Matching all 3 horses (no time) = Exacta prize; Grand Prize needs all 3 + time
            string? winType = null;
            if (horsesAllFilled)
            {
                bool h1 = userHorses[0] == _winHorses[0];
                bool h2 = userHorses[1] == _winHorses[1];
                bool h3 = userHorses[2] == _winHorses[2];

                if      (h1 && h2 && h3 && timeMatch) winType = "GRAND!";
                else if (h1 && h2 && timeMatch)       winType = "Exa+Time"; // covers all-3 case too
                else if (h1 && h2)                    winType = "Exacta";   // covers all-3 case too
                else if (h1 && timeMatch)             winType = "Win+Time";
                else if (h1)                          winType = "Win";
                else if (timeMatch)                   winType = "Time";
            }
            else if (timeMatch)
            {
                winType = "Time";
            }

            // Apply colors
            Color horseBg, timeBg;
            if (winType == null)
            {
                horseBg = horsesAllFilled ? Color.FromArgb("#FFCDD2") : Color.FromArgb("#F5F5F5");
                timeBg  = timeEntered ? Color.FromArgb("#FFCDD2") : Color.FromArgb("#F5F5F5");
            }
            else if (winType == "GRAND!")
            {
                horseBg = Color.FromArgb("#FFF9C4");
                timeBg  = Color.FromArgb("#FFF9C4");
            }
            else if (winType == "Exa+Time" || winType == "Exacta")
            {
                horseBg = Color.FromArgb("#BBDEFB");
                timeBg  = winType == "Exa+Time" ? Color.FromArgb("#E1BEE7") : Color.FromArgb("#F5F5F5");
            }
            else if (winType == "Win+Time" || winType == "Win")
            {
                horseBg = Color.FromArgb("#FFE0B2");
                timeBg  = winType == "Win+Time" ? Color.FromArgb("#E1BEE7") : Color.FromArgb("#F5F5F5");
            }
            else // "Time"
            {
                horseBg = horsesAllFilled ? Color.FromArgb("#FFCDD2") : Color.FromArgb("#F5F5F5");
                timeBg  = Color.FromArgb("#E1BEE7");
            }

            for (int c = 0; c < HorseCols; c++)
                _horseEntries[r, c].BackgroundColor = horseBg;
            _timeEntries[r].BackgroundColor = timeBg;

            if (winType == null)
            {
                _results[r].Text      = "✗";
                _results[r].TextColor = Color.FromArgb("#C62828");
            }
            else
            {
                _results[r].Text = winType;
                _results[r].TextColor = winType switch
                {
                    "GRAND!"              => Color.FromArgb("#E65100"),
                    "Exa+Time" or "Exacta"=> Color.FromArgb("#0D47A1"),
                    "Win+Time" or "Win"   => Color.FromArgb("#BF360C"),
                    "Time"                => Color.FromArgb("#6A1B9A"),
                    _                     => Color.FromArgb("#888")
                };
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
            for (int c = 0; c < HorseCols; c++)
                if (!string.IsNullOrEmpty(_horseEntries[r, c].Text)) { empty = false; break; }
            if (!empty) continue;

            var horses = Enumerable.Range(1, 12).OrderBy(_ => rng.Next()).Take(HorseCols).ToList();
            for (int c = 0; c < HorseCols; c++)
                _horseEntries[r, c].Text = horses[c].ToString();
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
            for (int i = 0; i < 10; i++) Preferences.Remove(SetKey(i));
            ClearAllEntries();
        }
        else
        {
            if (_activeSlot < 0) return;
            Preferences.Remove(SetKey(_activeSlot));
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
#if ANDROID
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
            _voiceTarget = _horseEntries[_voiceRow, _voiceCol];
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
            if (n >= 1 && n <= 12)
            {
                _voiceSettingText = true;
                _horseEntries[_voiceRow, _voiceCol].Text = n.ToString();
                _voiceSettingText = false;
                _voiceCol++;
                if (_voiceCol >= HorseCols) { _voiceCol = 0; _voiceRow++; }
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
        while (_voiceRow < Rows && !string.IsNullOrEmpty(_horseEntries[_voiceRow, _voiceCol].Text))
        {
            _voiceCol++;
            if (_voiceCol >= HorseCols) { _voiceCol = 0; _voiceRow++; }
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
                "Daily Derby", "dd_set_", _activeSlot < 0 ? 0 : _activeSlot, GetCurrentEntryString());
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
