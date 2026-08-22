using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class TicketLogPage : ContentPage
{
    static readonly (string Key, string Name, string Color)[] GameDefs =
    [
        ("F5", "Fantasy 5",     "#FF8F00"),
        ("SL", "Super Lotto",   "#7B1FA2"),
        ("PB", "Powerball",     "#C62828"),
        ("MM", "Mega Millions", "#F57F17"),
        ("D3", "Daily 3",       "#1565C0"),
        ("D4", "Daily 4",       "#00695C"),
        ("DD", "Daily Derby",   "#5D4037"),
        ("SC", "Scratchers",    "#2E7D32"),
        // Was missing entirely — Hot Spot rows have been written to ticket_log.json since
        // that game launched (see TicketLogService's "HS" block), but this page's own
        // separate GameDefs list (different from SummaryPage's, which was fixed the same
        // way when Hot Spot first shipped) never included them, so every Hot Spot ticket was
        // silently skipped by the `foreach (var (key, name, color) in GameDefs)` loop below.
        ("HS", "Hot Spot",      "#E65100"),
    ];

    public static bool NeedsRefresh = false;

    // "All Dates" is capped to this many days back (plus Today/Yesterday) — change this
    // number to show more or less history when you tap the ⋮ menu's date-range option.
    const int OlderDatesRangeDays = 30;

    List<TicketLogEntry> _entries = new();
    string _selectedDay = "";  // which day "Clear Day" targets
    readonly Dictionary<string, bool> _dateCollapsed = new();
    readonly Dictionary<string, (Grid Header, Label Chevron, Label DateLbl)> _dateHeaders = new();
    readonly HashSet<string> _selectedRowKeys = new();  // "date|game|slot|row"

    bool _showAllDates = false;
    bool _olderBuilt = false;      // true once olderContainer has been populated for the current _entries
    bool _olderBuilding = false;   // true while an older-dates build is in flight
    int  _dataGeneration = 0;      // bumped whenever _entries reloads so an in-flight build can bail out

    public TicketLogPage()
    {
        InitializeComponent();
        EnhanceModeService.SettingChanged += OnEnhanceModeChanged;
    }

    void OnEnhanceModeChanged()
    {
        if (!NeedsRefresh) return;
        NeedsRefresh = false;
        _ = RebuildUI();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        EnhanceModeService.SettingChanged += OnEnhanceModeChanged;
        lblToday.Text = $"{DateTime.Today:dddd, MMM d, yyyy}";
        _ = LoadAndBuild();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Preferences.Set("tl_selected_keys", string.Join("\n", _selectedRowKeys));
        EnhanceModeService.SettingChanged -= OnEnhanceModeChanged;
    }

    async Task LoadAndBuild()
    {
        try { scanOverlay.Opacity = 1; scanOverlay.InputTransparent = false; scanSpinner.IsRunning = true; lblScanStatus.Text = "Checking all games for today's purchases..."; } catch { }

        // Wait for any in-progress game page log write to finish before reading.
        if (TicketLogService.PendingWriteTask != null)
        {
            await TicketLogService.PendingWriteTask;
            TicketLogService.PendingWriteTask = null;
        }

        string todayStr = DateTime.Today.ToString("yyyy-MM-dd");
        await TicketLogService.ScanAndLogTodayAsync();

        try { lblScanStatus.Text = "Loading ticket log..."; } catch { }
        await Task.Yield();  // let UI update the label before BuildUI runs

        _entries = await TicketLogService.LoadAllAsync();
        _selectedDay = todayStr;
        _olderBuilt = false;
        _olderBuilding = false;
        ++_dataGeneration;

        // Restore persisted row selections
        _selectedRowKeys.Clear();
        var saved = Preferences.Get("tl_selected_keys", "");
        if (!string.IsNullOrEmpty(saved))
            foreach (var k in saved.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                _selectedRowKeys.Add(k);

        BuildUI();
        try { scanOverlay.Opacity = 0.001; scanOverlay.InputTransparent = true; scanSpinner.IsRunning = false; } catch { }
    }

    // Rebuild just the UI (no rescan, no reload) — used after enhance mode toggle.
    // The bubble/plain number style applies to every row, so any already-built older
    // section must be invalidated too, not just the recent one.
    async Task RebuildUI()
    {
        try { scanOverlay.Opacity = 1; scanOverlay.InputTransparent = false; scanSpinner.IsRunning = true; lblScanStatus.Text = "Loading ticket log..."; } catch { }
        await Task.Yield();

        _olderBuilt = false;
        _olderBuilding = false;
        ++_dataGeneration;
        BuildUI();

        // If "All Dates" is showing, BuildUI() just kicked off its own async rebuild (with its
        // own spinner) for the older section — let that own hiding the overlay when it finishes.
        if (!_showAllDates)
        {
            try { scanOverlay.Opacity = 0.001; scanOverlay.InputTransparent = true; scanSpinner.IsRunning = false; } catch { }
        }
    }

    // Builds the "Today + Yesterday" view (always, cheap — just two days of data).
    // Older dates are only built the first time "All Dates" is actually opened, and are
    // cached after that — toggling back and forth never rebuilds anything that already exists.
    void BuildUI()
    {
        recentContainer.Children.Clear();

        if (_entries.Count == 0)
        {
            olderContainer.Children.Clear();
            _dateHeaders.Clear();
            _olderBuilt = true;
            recentContainer.Children.Add(new Label
            {
                Text = "No tickets logged yet.\nTickets are logged automatically when you leave each game page.",
                FontSize = 13, TextColor = Color.FromArgb("#888"),
                HorizontalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                Padding = new Thickness(20, 40)
            });
            lblFooter.Text = "0 tickets logged";
            return;
        }

        // Group by date descending
        var byDate = _entries
            .GroupBy(e => e.Date)
            .OrderByDescending(g => g.Key)
            .ToList();

        string todayStr = DateTime.Today.ToString("yyyy-MM-dd");
        string yesterdayStr = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");
        string cutoffStr = DateTime.Today.AddDays(-OlderDatesRangeDays).ToString("yyyy-MM-dd");

        var recentGroups = byDate.Where(g => g.Key == todayStr || g.Key == yesterdayStr).ToList();
        var olderGroups  = byDate
            .Where(g => g.Key != todayStr && g.Key != yesterdayStr && string.Compare(g.Key, cutoffStr, StringComparison.Ordinal) >= 0)
            .ToList();

        foreach (var dateGroup in recentGroups)
        {
            _dateHeaders.Remove(dateGroup.Key);
            BuildDateSection(dateGroup, recentContainer, todayStr);
        }

        olderContainer.IsVisible = _showAllDates;

        if (olderGroups.Count == 0)
        {
            olderContainer.Children.Clear();
            _olderBuilt = true;
        }
        else if (_showAllDates && !_olderBuilt && !_olderBuilding)
        {
            // First time All Dates has been opened for this data — build it now, with a spinner,
            // yielding between sections so the UI thread stays responsive (this can be several
            // seconds of real work if there's a lot of history — cached after this one time).
            _olderBuilding = true;
            int generation = _dataGeneration;
            try { scanOverlay.Opacity = 1; scanOverlay.InputTransparent = false; scanSpinner.IsRunning = true; lblScanStatus.Text = $"Loading last {OlderDatesRangeDays} days..."; } catch { }
            _ = BuildOlderAsync(olderGroups, todayStr, generation);
        }
        // else: nothing to do — either already built & cached (just show/hide above), or a build
        // is already in flight from a previous "All Dates" tap.

        UpdateFooter(byDate.Count);
    }

    void UpdateFooter(int dayCount)
    {
        int grandTotal = _entries.Where(e => e.Game != "D3").Count()
            + _entries.Where(e => e.Game == "D3").Select(e => (e.Date, e.Slot, e.Row)).Distinct().Count();
        lblFooter.Text = _showAllDates
            ? $"{grandTotal} total tickets logged across {dayCount} day(s)  |  showing last {OlderDatesRangeDays} days — tap a date to select for Clear Day"
            : $"{grandTotal} total tickets logged across {dayCount} day(s)  |  showing Today & Yesterday — see ⋮ for more";
    }

    async Task BuildOlderAsync(List<IGrouping<string, TicketLogEntry>> olderGroups, string todayStr, int generation)
    {
        olderContainer.Children.Clear();
        foreach (var g in olderGroups) _dateHeaders.Remove(g.Key);
        await Task.Yield();  // let the spinner render before the heavy work starts

        foreach (var dateGroup in olderGroups)
        {
            if (generation != _dataGeneration) { _olderBuilding = false; return; }  // entries reloaded meanwhile — bail out
            BuildDateSection(dateGroup, olderContainer, todayStr);
            await Task.Yield();  // spread the work across frames so the UI (and spinner) stay responsive
        }

        _olderBuilding = false;
        if (generation == _dataGeneration) _olderBuilt = true;
        try { scanOverlay.Opacity = 0.001; scanOverlay.InputTransparent = true; scanSpinner.IsRunning = false; } catch { }
    }

    void BuildDateSection(IGrouping<string, TicketLogEntry> dateGroup, VerticalStackLayout container, string todayStr)
    {
        string date = dateGroup.Key;
        bool isToday = date == todayStr;
        bool isSelected = date == _selectedDay;

        string dateLabel = DateTime.TryParseExact(date, "yyyy-MM-dd", null,
            System.Globalization.DateTimeStyles.None, out var dt)
            ? dt.ToString("dddd, MMM d, yyyy") + (isToday ? "  <- TODAY" : "")
            : date;

        // Date header (tap = select for Clear Day + collapse/expand)
        string capturedDate = date;
        if (!_dateCollapsed.ContainsKey(date)) _dateCollapsed[date] = Preferences.Get($"tl_collapsed_{date}", false);
        bool collapsed = _dateCollapsed[date];

        var dateHeader = new Grid
        {
            BackgroundColor = isSelected ? Color.FromArgb("#1A3A5C") : Color.FromArgb("#0D2035"),
            Padding = new Thickness(12, 8),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),  // chevron
                new ColumnDefinition(GridLength.Star),  // date label
                new ColumnDefinition(GridLength.Auto),  // count
            }
        };
        var chevron = new Label
        {
            Text = collapsed ? "▶" : "▼",
            FontSize = 12, FontAttributes = FontAttributes.Bold,
            TextColor = isSelected ? Color.FromArgb("#90CAF9") : Color.FromArgb("#546E7A"),
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        dateHeader.Children.Add(chevron);
        var dateLbl = new Label
        {
            Text = dateLabel,
            FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = isSelected ? Color.FromArgb("#90CAF9") : Color.FromArgb("#607D8B"),
            VerticalOptions = LayoutOptions.Center,
        };
        Grid.SetColumn(dateLbl, 1);
        dateHeader.Children.Add(dateLbl);
        int dateTicketCount = dateGroup.Where(e => e.Game != "D3").Count()
            + dateGroup.Where(e => e.Game == "D3").Select(e => (e.Slot, e.Row)).Distinct().Count();
        var countLbl = new Label
        {
            Text = $"{dateTicketCount} tickets",
            FontSize = 11, TextColor = Color.FromArgb("#546E7A"),
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.End
        };
        Grid.SetColumn(countLbl, 2);
        dateHeader.Children.Add(countLbl);

        var dateBody = new VerticalStackLayout { IsVisible = !collapsed };

        _dateHeaders[date] = (dateHeader, chevron, dateLbl);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            // Deselect previous
            if (!string.IsNullOrEmpty(_selectedDay) && _selectedDay != capturedDate
                && _dateHeaders.TryGetValue(_selectedDay, out var prev))
            {
                prev.Header.BackgroundColor = Color.FromArgb("#0D2035");
                prev.Chevron.TextColor = Color.FromArgb("#546E7A");
                prev.DateLbl.TextColor = Color.FromArgb("#607D8B");
            }
            // Select this one
            _selectedDay = capturedDate;
            dateHeader.BackgroundColor = Color.FromArgb("#1A3A5C");
            chevron.TextColor = Color.FromArgb("#90CAF9");
            dateLbl.TextColor = Color.FromArgb("#90CAF9");
            // Toggle collapse
            _dateCollapsed[capturedDate] = !_dateCollapsed[capturedDate];
            Preferences.Set($"tl_collapsed_{capturedDate}", _dateCollapsed[capturedDate]);
            chevron.Text = _dateCollapsed[capturedDate] ? "▶" : "▼";
            dateBody.IsVisible = !_dateCollapsed[capturedDate];
        };
        dateHeader.GestureRecognizers.Add(tap);
        container.Children.Add(dateHeader);

        // Group by game
        foreach (var (key, name, color) in GameDefs)
        {
            var gameEntries = dateGroup.Where(e => e.Game == key).OrderBy(e => e.Slot).ThenBy(e => e.Row).ToList();
            if (gameEntries.Count == 0) continue;

            var accent = Color.FromArgb(color);

            // Game sub-header
            var gameHeader = new Grid
            {
                BackgroundColor = Color.FromArgb("#0A1A2A"),
                Padding = new Thickness(20, 5),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                }
            };
            gameHeader.Children.Add(new Label
            {
                Text = name,
                FontSize = 12, FontAttributes = FontAttributes.Bold,
                TextColor = accent, VerticalOptions = LayoutOptions.Center
            });
            int gDisplayCount = key == "D3"
                ? gameEntries.Select(e => (e.Slot, e.Row)).Distinct().Count()
                : gameEntries.Count;
            var gCountLbl = new Label
            {
                Text = $"{gDisplayCount} rows",
                FontSize = 10, TextColor = Color.FromArgb("#546E7A"),
                VerticalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.End
            };
            Grid.SetColumn(gCountLbl, 1);
            gameHeader.Children.Add(gCountLbl);
            dateBody.Children.Add(gameHeader);

            // Ticket rows
            foreach (var entry in gameEntries)
            {
                string rowKey = $"{entry.Date}|{entry.Game}|{entry.Slot}|{entry.Row}";
                bool isRowSelected = _selectedRowKeys.Contains(rowKey);
                var row = new Grid
                {
                    BackgroundColor = isRowSelected ? Color.FromArgb("#1A3A5C") : Color.FromArgb("#0D1E2E"),
                    Padding = new Thickness(28, 4, 12, 4),
                    Margin = new Thickness(0, 1),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(new GridLength(60)),  // S# R#
                        new ColumnDefinition(GridLength.Star),     // numbers
                        new ColumnDefinition(GridLength.Auto),     // extra
                    }
                };
                string capturedKey = rowKey;
                var rowTap = new TapGestureRecognizer();
                rowTap.Tapped += (_, _) =>
                {
                    if (_selectedRowKeys.Contains(capturedKey))
                    {
                        _selectedRowKeys.Remove(capturedKey);
                        row.BackgroundColor = Color.FromArgb("#0D1E2E");
                    }
                    else
                    {
                        _selectedRowKeys.Add(capturedKey);
                        row.BackgroundColor = Color.FromArgb("#1A3A5C");
                    }
                };
                row.GestureRecognizers.Add(rowTap);

                // Left color strip
                row.Children.Add(new BoxView
                {
                    Color = accent, WidthRequest = 3,
                    VerticalOptions = LayoutOptions.Fill,
                    HorizontalOptions = LayoutOptions.Start,
                    Margin = new Thickness(-16, 0, 0, 0)
                });

                row.Children.Add(new Label
                {
                    Text = $"S{entry.Slot + 1} R{entry.Row + 1}",
                    FontSize = 10, TextColor = Color.FromArgb("#546E7A"),
                    VerticalOptions = LayoutOptions.Center
                });

                // D3 Evening rows → amber bubbles to distinguish from Midday
                bool isD3Evening = entry.Game == "D3" &&
                    (entry.Extra == "E" || entry.Extra.EndsWith("|E", StringComparison.OrdinalIgnoreCase));
                Color ballColor = isD3Evening ? Color.FromArgb("#F57F17") : accent;

                bool bubblesOn = EnhanceModeService.IsEnhanced(EnhanceModeService.TicketLogPageKey);
                View numsView = entry.Game == "SC"
                    ? new Label
                    {
                        Text = entry.Numbers,
                        FontSize = 13, FontAttributes = FontAttributes.Bold,
                        TextColor = Colors.White,
                        VerticalOptions = LayoutOptions.Center,
                        LineBreakMode = LineBreakMode.TailTruncation
                    }
                    : bubblesOn
                        ? MakeBubbleNumbers(entry.Numbers, entry.Game, ballColor, entry.IsFreePlay)
                        : MakePlainNumbers(entry.Numbers, entry.Game, ballColor, entry.IsFreePlay);

                // Bet type label after numbers for D3/D4
                string betType = "";
                if (entry.Game == "D3" || entry.Game == "D4")
                {
                    string prefix = entry.Game == "D3" ? "d3" : "d4";
                    string btRaw = Preferences.Get($"{prefix}_btypes_{entry.Slot}", "");
                    var btParts = btRaw.Split('|');
                    string bt = entry.Row < btParts.Length && !string.IsNullOrEmpty(btParts[entry.Row])
                        ? btParts[entry.Row] : "S";
                    if (bt == "S+B") bt = "S&B";
                    betType = bt switch
                    {
                        "S"   => "Straight",
                        "B"   => "Box",
                        "S&B" => "Str/Box",
                        _     => bt
                    };
                }

                var numsStack = new HorizontalStackLayout { Spacing = 6, VerticalOptions = LayoutOptions.Center };
                numsStack.Children.Add(numsView);
                if (!string.IsNullOrEmpty(betType))
                {
                    numsStack.Children.Add(new Label
                    {
                        Text = betType, FontSize = 9, FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#90CAF9"),
                        VerticalOptions = LayoutOptions.Center,
                        LineBreakMode = LineBreakMode.NoWrap
                    });
                }
                Grid.SetColumn(numsStack, 1);
                row.Children.Add(numsStack);

                // Right column: extra info + play date range
                // HS's raw Extra ("SP:3|W:1|BE:0") is machine-shaped, not something to show a
                // user as-is now that Hot Spot rows actually render on this page.
                string displayExtra = entry.Game == "HS" ? FormatHsExtra(entry.Extra) : entry.Extra;
                bool hasExtra = !string.IsNullOrEmpty(displayExtra);
                bool hasDates = !string.IsNullOrEmpty(entry.PlayFrom) || !string.IsNullOrEmpty(entry.PlayTo);
                if (hasExtra || hasDates)
                {
                    string dateRange = "";
                    if (hasDates)
                    {
                        string pf = entry.PlayFrom ?? "";
                        string pt = entry.PlayTo ?? "";
                        dateRange = (pf == pt || string.IsNullOrEmpty(pt))
                            ? $"({pf})"
                            : $"({pf}-{pt})";
                    }

                    if (hasExtra && hasDates)
                    {
                        var lbl = new Label
                        {
                            Text = $"{displayExtra}  {dateRange}",
                            FontSize = 10, TextColor = Color.FromArgb("#90CAF9"),
                            VerticalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.End,
                            Margin = new Thickness(4, 0, 0, 0)
                        };
                        Grid.SetColumn(lbl, 2);
                        row.Children.Add(lbl);
                    }
                    else
                    {
                        var lbl = new Label
                        {
                            Text = hasExtra ? displayExtra : dateRange,
                            FontSize = hasExtra ? 10 : 9,
                            TextColor = hasExtra ? Color.FromArgb("#90CAF9") : Color.FromArgb("#78909C"),
                            VerticalOptions = LayoutOptions.Center,
                            HorizontalTextAlignment = TextAlignment.End,
                            Margin = new Thickness(4, 0, 0, 0)
                        };
                        Grid.SetColumn(lbl, 2);
                        row.Children.Add(lbl);
                    }
                }

                dateBody.Children.Add(row);
            }
        }

        container.Children.Add(dateBody);
        // Spacer between dates
        container.Children.Add(new BoxView { HeightRequest = 8, BackgroundColor = Color.FromArgb("#050E18") });
    }

    // Turns HotSpotPage's machine-shaped Extra ("SP:3|W:1|BE:0") into "3 spots · $1 · BE".
    static string FormatHsExtra(string extra)
    {
        if (string.IsNullOrEmpty(extra)) return "";
        int spots = 0; decimal wager = 0; bool bullseye = false;
        foreach (var part in extra.Split('|'))
        {
            var kv = part.Split(':');
            if (kv.Length != 2) continue;
            switch (kv[0])
            {
                case "SP": int.TryParse(kv[1], out spots); break;
                case "W": decimal.TryParse(kv[1], out wager); break;
                case "BE": bullseye = kv[1] == "1"; break;
            }
        }
        string text = $"{spots} spot{(spots == 1 ? "" : "s")} · ${wager:0}";
        if (bullseye) text += " · BE";
        return text;
    }

    static View MakeBubbleNumbers(string numbers, string gameKey, Color ballColor, bool isFreePlay)
    {
        double size     = gameKey == "DD" ? 26 : 22;
        double fontSize = size * 0.55;

        var layout = new HorizontalStackLayout { Spacing = 3, VerticalOptions = LayoutOptions.Center };

        foreach (var part in numbers.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("|"))
            {
                int colonIdx = part.IndexOf(':');
                if (colonIdx >= 0 && int.TryParse(part[(colonIdx + 1)..], out int bonus))
                {
                    layout.Children.Add(new Label
                    {
                        Text = "·", FontSize = 10,
                        TextColor = Color.FromArgb("#999"),
                        VerticalOptions = LayoutOptions.Center,
                    });
                    layout.Children.Add(MakeBall(bonus.ToString(), size, fontSize, Color.FromArgb("#F57F17")));
                }
                continue;
            }
            if (!int.TryParse(part, out int n)) continue;
            layout.Children.Add(MakeBall(n.ToString(), size, fontSize, ballColor));
        }

        if (isFreePlay)
            layout.Children.Add(new Label { Text = "🆓", FontSize = 12, VerticalOptions = LayoutOptions.Center, Margin = new Thickness(2, 0, 0, 0) });

        return layout;
    }

    static Border MakeBall(string text, double size, double fontSize, Color color) =>
        new Border
        {
            WidthRequest = size, HeightRequest = size,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
            BackgroundColor = color,
            Content = new Label
            {
                Text = text,
                FontSize = fontSize, FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment   = TextAlignment.Center,
            }
        };

    static View MakePlainNumbers(string numbers, string gameKey, Color ballColor, bool isFreePlay)
    {
        var parts = new List<string>();
        string? bonusStr = null;

        foreach (var part in numbers.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("|"))
            {
                int colonIdx = part.IndexOf(':');
                if (colonIdx >= 0 && int.TryParse(part[(colonIdx + 1)..], out int bonus))
                    bonusStr = bonus.ToString();
                continue;
            }
            if (int.TryParse(part, out int n))
                parts.Add(n.ToString());
        }

        string text = string.Join(" ", parts);
        if (bonusStr != null) text += $"  ·{bonusStr}";
        if (isFreePlay) text += " 🆓";

        return new Label
        {
            Text = text,
            FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = ballColor,
            VerticalOptions = LayoutOptions.Center,
        };
    }

    async void BtnMenu_Clicked(object sender, EventArgs e)
    {
        bool reopenMenu;
        do
        {
            reopenMenu = false;
            string enhanceLabel = "✨ Enhance Mode";
            string datesLabel = _showAllDates ? "📅 Recent Only (Today & Yesterday)" : $"📅 Last {OlderDatesRangeDays} Days";

            string action = await DisplayActionSheet("Ticket Log", "Cancel", null,
                "Log Today", datesLabel, enhanceLabel, "Clear Day", "Clear All");

            switch (action)
            {
                case "Log Today":
                    await Shell.Current.GoToAsync(nameof(SpendingLogPage), false);
                    break;
                case "Clear Day":
                    await ClearSelectedDayAsync();
                    break;
                case "Clear All":
                    await ClearAllEntriesAsync();
                    break;
                default:
                    if (action == datesLabel)
                    {
                        _showAllDates = !_showAllDates;
                        BuildUI();
                    }
                    else if (action == enhanceLabel)
                    {
                        bool wentBack = await EnhanceModeService.ShowDialogAsync(this);
                        if (wentBack) reopenMenu = true;
                    }
                    break;
            }
        } while (reopenMenu);
    }

    async Task ClearSelectedDayAsync()
    {
        if (string.IsNullOrEmpty(_selectedDay)) return;
        int count = _entries.Count(x => x.Date == _selectedDay);
        if (count == 0) { await DisplayAlert("Nothing to Clear", "No tickets logged for selected day.", "OK"); return; }
        bool ok = await DisplayAlert("Clear Day",
            $"Remove {count} ticket log entries for {_selectedDay}?", "Yes", "Cancel");
        if (!ok) return;
        await TicketLogService.ClearDayAsync(_selectedDay);
        _selectedDay = DateTime.Today.ToString("yyyy-MM-dd");
        _entries = await TicketLogService.LoadAllAsync();
        _olderBuilt = false;
        _olderBuilding = false;
        ++_dataGeneration;
        BuildUI();
    }

    async Task ClearAllEntriesAsync()
    {
        if (_entries.Count == 0) return;
        bool ok = await DisplayAlert("Clear All", $"Remove all {_entries.Count} ticket log entries?", "Yes", "Cancel");
        if (!ok) return;
        await TicketLogService.ClearAllAsync();
        _entries = new();
        _olderBuilt = false;
        _olderBuilding = false;
        ++_dataGeneration;
        BuildUI();
    }

    // BackupPage/BackupService haven't been ported to iOS yet — Android's version is entirely
    // Android-only (SharedPreferences export + MediaStore file I/O) with no cross-platform
    // fallback, so it needs a real iOS-native design (Files app / share sheet), not a straight
    // copy. Placeholder until that's built, so this button is disabled instead of a dead route.
    async void BtnBackups_Clicked(object sender, EventArgs e)
        => await DisplayAlert("Coming Soon", "Backups aren't available on iOS yet.", "OK");

    async void BtnBack_Clicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..", false);
}
