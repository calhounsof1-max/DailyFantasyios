using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class SpendingLogPage : ContentPage
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
    ];

    List<SpendingRecord> _records  = new();
    List<WinningRecord>  _winnings = new();
    string? _filterMonth;  // "2026-07" or null = show all

    readonly Dictionary<string, bool> _dateCollapsed = new();
    readonly Dictionary<string, bool> _gameCollapsed = new();

    // ── Add overlay ───────────────────────────────────────────────────────────
    Grid?       _addOverlay;
    Label?      _addTitleLabel;
    DatePicker? _addDatePicker;
    Picker?     _addGamePicker;
    Entry?      _addCountEntry;
    Entry?      _addCostEntry;
    Entry?      _addNoteEntry;

    // ── Verify overlay ───────────────────────────────────────────────────────
    Grid? _verifyOverlay;

    // ── Busy overlay (spinner shown while the page loads or Log Today runs) ───
    // Local to this page (a Grid toggled via Opacity), not a shared modal push — a modal
    // pushed/popped from a global place broke badly if anything else on screen pushed its
    // own modal in between (the pop would remove the wrong page and strand the spinner).
    Grid?               _busyOverlay;
    ActivityIndicator?  _busySpinner;
    Label?              _busyStatusLabel;

    // ── Log Today overlay ─────────────────────────────────────────────────────
    Grid?                      _todayOverlay;
    DatePicker?                _todayDatePicker;
    VerticalStackLayout?       _todayRowsContainer;
    Label?                     _todayTotalLabel;
    Label?                     _todaySavedBanner;
    Entry?                     _f5ReplayEntry;   // replays are free — subtracted from F5 cost
    Entry?                     _d3MidEntry;      // D3 Midday count
    Entry?                     _d3EveEntry;      // D3 Evening count
    Entry?                     _d3BothEntry;     // D3 Both count
    Entry?                     _scPriceEntry;    // SC price per ticket (varies)
    Entry?                     _hsCostEntry;     // HS today's total cost (varies per ticket — wager × draws × bullseye mult.)
    Label?                     _hsCountLabel;    // HS ticket count today — was missing entirely, only the $ total showed
    // Per-game entry rows in the overlay
    readonly List<(string Key, Entry CountEntry)> _todayEntries = new();
    // Warning shown under a game's row when it has an advance ticket already logged on an
    // earlier day but still visibly active — prevents re-typing a count for it out of habit.
    readonly Dictionary<string, Label> _todayWarnings = new();
    readonly Dictionary<string, Label> _todayBreakdowns = new();

    public SpendingLogPage()
    {
        InitializeComponent();
        BuildAddOverlay();
        BuildLogTodayOverlay();
        BuildVerifyOverlay();
        BuildBusyOverlay();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadAndBuild();
    }

    async Task LoadAndBuild()
    {
        await ShowBusyAsync("Loading spending log...");
        await Task.WhenAll(LoadSpendingAsync(), LoadWinningsAsync());
        BuildMonthFilter();
        BuildUI();
        HideBusy();
    }

    async Task LoadSpendingAsync()
    {
        // Keep today's auto-computed rows (F5/SL/PB/MM/D4/DD/D3) in sync with what's actually
        // on the game pages before loading — Ticket Log is auto-scanned on every visit, but
        // spending_log.json only reflected today's tickets if something had already called
        // AutoSyncTodayAsync (previously only Ticket Calendar did). Without this, a ticket
        // entered after this page's last load — or entered while a different page was open —
        // shows in Ticket Log but not here, producing a false "discrepancy" in Verify.
        await SpendingTracker.AutoSyncTodayAsync();
        _records = await SpendingTracker.LoadAllAsync();
    }

    async Task LoadWinningsAsync()
    {
        _winnings = await SummaryPage.LoadAllAsync();
    }

    // ── Month filter bar ──────────────────────────────────────────────────────

    void BuildMonthFilter()
    {
        monthFilterBar.Children.Clear();

        // Collect distinct months from spending
        var months = _records
            .Select(r => r.Date.Length >= 7 ? r.Date[..7] : "")
            .Where(m => !string.IsNullOrEmpty(m))
            .Distinct()
            .OrderByDescending(m => m)
            .ToList();

        if (months.Count == 0) return;

        // "All" button
        var allBtn = MakeFilterBtn("All", _filterMonth == null);
        allBtn.Clicked += (_, _) => { _filterMonth = null; BuildMonthFilter(); BuildUI(); };
        monthFilterBar.Children.Add(allBtn);

        foreach (var month in months)
        {
            string capturedMonth = month;
            bool   active        = _filterMonth == capturedMonth;
            var btn = MakeFilterBtn(FormatMonth(month), active);
            btn.Clicked += (_, _) =>
            {
                _filterMonth = capturedMonth;
                BuildMonthFilter();
                BuildUI();
            };
            monthFilterBar.Children.Add(btn);
        }
    }

    static Button MakeFilterBtn(string text, bool active) => new Button
    {
        Text = text,
        FontSize = 11, FontAttributes = FontAttributes.Bold,
        BackgroundColor = active ? Color.FromArgb("#2563EB") : Color.FromArgb("#2D3E55"),
        TextColor = Colors.White,
        CornerRadius = 14,
        HeightRequest = 28, Padding = new Thickness(10, 0),
    };

    static string FormatMonth(string ym)
    {
        if (DateTime.TryParseExact(ym + "-01", "yyyy-MM-dd",
                null, System.Globalization.DateTimeStyles.None, out var dt))
            return dt.ToString("MMM yyyy");
        return ym;
    }

    // ── Build main UI ─────────────────────────────────────────────────────────

    void BuildUI()
    {
        mainContainer.Children.Clear();

        var filtered = _filterMonth == null
            ? _records
            : _records.Where(r => r.Date.StartsWith(_filterMonth)).ToList();

        if (filtered.Count == 0)
        {
            mainContainer.Children.Add(new Label
            {
                Text = "No spending entries yet.\nTap + Add or use the Log button on any game in Summary.",
                FontSize = 13, TextColor = Color.FromArgb("#888"),
                HorizontalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                Padding = new Thickness(20, 40)
            });
            UpdateTotals(0, 0);
            return;
        }

        // Group by date descending
        var byDate = filtered
            .GroupBy(r => r.Date)
            .OrderByDescending(g => g.Key)
            .ToList();

        decimal grandSpent = 0;
        decimal grandWon   = 0;

        foreach (var group in byDate)
        {
            string date     = group.Key;
            bool   dateParsed = DateTime.TryParse(date, out var dt);
            string dateLabel  = dateParsed ? dt.ToString("dddd, MMM d, yyyy") : date;
            decimal daySpent  = group.Sum(r => r.TotalCost);

            // Winnings for this day (across all games)
            decimal dayWon = _winnings
                .Where(w => w.Date == date && !w.IsFreeTicket)
                .Sum(w => w.Amount);

            grandSpent += daySpent;
            grandWon   += dayWon;

            decimal dayNet   = dayWon - daySpent;
            string  netSign  = dayNet >= 0 ? "+" : "";
            string  netColor = dayNet >= 0 ? "#A5D6A7" : "#EF9A9A";

            // ── Collapse state ────────────────────────────────────────────────
            string capturedDate = date;
            if (!_dateCollapsed.ContainsKey(date))
                _dateCollapsed[date] = Preferences.Get($"sl_collapsed_{date}", false);
            bool collapsed = _dateCollapsed[date];

            // ── Day header ────────────────────────────────────────────────────
            var dayHeader = new Grid
            {
                BackgroundColor = Color.FromArgb("#1E2F40"),
                Padding = new Thickness(12, 7),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),  // chevron
                    new ColumnDefinition(GridLength.Star),  // date label
                    new ColumnDefinition(GridLength.Auto),  // net
                }
            };
            var chevron = new Label
            {
                Text = collapsed ? "▶" : "▼",
                FontSize = 12, FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#90CAF9"),
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 6, 0),
            };
            dayHeader.Children.Add(chevron);
            dayHeader.Children.Add(new Label
            {
                Text = dateLabel,
                FontSize = 13, FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#90CAF9"),
                VerticalOptions = LayoutOptions.Center
            }.WithCol(1));
            dayHeader.Children.Add(new Label
            {
                Text = $"Net: {netSign}${dayNet:N2}",
                FontSize = 12, FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb(netColor),
                VerticalOptions = LayoutOptions.Center,
            }.WithCol(2));

            // ── Day body (collapsible) ─────────────────────────────────────────
            var dateBody = new VerticalStackLayout { IsVisible = !collapsed };

            // Stats row
            var statsRow = new Grid
            {
                BackgroundColor = Color.FromArgb("#162230"),
                Padding = new Thickness(12, 4),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Star),
                }
            };
            statsRow.Children.Add(new Label
            {
                Text = $"Spent: ${daySpent:N2}",
                FontSize = 11, TextColor = Color.FromArgb("#EF9A9A"),
                VerticalOptions = LayoutOptions.Center
            });
            statsRow.Children.Add(new Label
            {
                Text = $"Won: ${dayWon:N2}",
                FontSize = 11, TextColor = Color.FromArgb("#A5D6A7"),
                HorizontalTextAlignment = TextAlignment.End,
                VerticalOptions = LayoutOptions.Center
            }.WithCol(1));
            dateBody.Children.Add(statsRow);

            // Individual ticket entries — grouped by game, each collapsible
            var recsByGame = group.ToLookup(r => r.Game);

            // Extracted from the GameDefs loop below so Hot Spot — deliberately NOT in
            // GameDefs (see the "Hot Spot" comments elsewhere in this file: its cost is a
            // whole-day total, not count × fixed-price, so it can't safely join the Add-Game
            // picker's or Log Today's assumptions) — can still render its own collapsible
            // group here using the exact same look. Confirmed live 2026-08-09: without this,
            // today's $30 Hot Spot spend was counted in the day's total up top but had no
            // visible row at all explaining which game it came from.
            void RenderGameGroup(string gameKey, string gameName, string gameColor)
            {
                var recs = recsByGame[gameKey].ToList();
                if (recs.Count == 0) return;

                string capturedGameKey = gameKey;
                if (!_gameCollapsed.ContainsKey(gameKey))
                    _gameCollapsed[gameKey] = Preferences.Get($"sl_game_{gameKey}_collapsed", false);
                bool gameCollapsed = _gameCollapsed[gameKey];

                var accent = Color.FromArgb(gameColor);
                decimal gameTotal = recs.Sum(r => r.TotalCost);

                // Game header row
                var gameHeader = new Grid
                {
                    BackgroundColor = Color.FromArgb("#0F1D2A"),
                    Padding = new Thickness(14, 5),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Auto),  // chevron
                        new ColumnDefinition(GridLength.Star),  // game name
                        new ColumnDefinition(GridLength.Auto),  // total
                    }
                };
                var gameChevron = new Label
                {
                    Text = gameCollapsed ? "▶" : "▼",
                    FontSize = 11, FontAttributes = FontAttributes.Bold,
                    TextColor = accent,
                    VerticalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 0, 6, 0),
                };
                gameHeader.Children.Add(gameChevron);
                gameHeader.Children.Add(new Label
                {
                    Text = $"{gameName}  ({recs.Count})",
                    FontSize = 11, FontAttributes = FontAttributes.Bold,
                    TextColor = accent,
                    VerticalOptions = LayoutOptions.Center,
                }.WithCol(1));
                gameHeader.Children.Add(new Label
                {
                    Text = $"${gameTotal:N2}",
                    FontSize = 11, TextColor = Color.FromArgb("#EF9A9A"),
                    VerticalOptions = LayoutOptions.Center,
                }.WithCol(2));

                var gameBody = new VerticalStackLayout { IsVisible = !gameCollapsed };
                foreach (var rec in recs)
                    gameBody.Children.Add(BuildEntryRow(rec));

                var gameTap = new TapGestureRecognizer();
                gameTap.Tapped += (_, _) =>
                {
                    _gameCollapsed[capturedGameKey] = !_gameCollapsed[capturedGameKey];
                    Preferences.Set($"sl_game_{capturedGameKey}_collapsed", _gameCollapsed[capturedGameKey]);
                    gameChevron.Text  = _gameCollapsed[capturedGameKey] ? "▶" : "▼";
                    gameBody.IsVisible = !_gameCollapsed[capturedGameKey];
                };
                gameHeader.GestureRecognizers.Add(gameTap);

                dateBody.Children.Add(gameHeader);
                dateBody.Children.Add(gameBody);
            }

            foreach (var (gameKey, gameName, gameColor) in GameDefs)
                RenderGameGroup(gameKey, gameName, gameColor);
            RenderGameGroup("HS", "Hot Spot", "#E65100");

            // Spacer
            dateBody.Children.Add(new BoxView
            {
                HeightRequest = 6, BackgroundColor = Color.FromArgb("#0A1520")
            });

            // Tap to collapse/expand
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                _dateCollapsed[capturedDate] = !_dateCollapsed[capturedDate];
                Preferences.Set($"sl_collapsed_{capturedDate}", _dateCollapsed[capturedDate]);
                chevron.Text       = _dateCollapsed[capturedDate] ? "▶" : "▼";
                dateBody.IsVisible = !_dateCollapsed[capturedDate];
            };
            dayHeader.GestureRecognizers.Add(tap);

            mainContainer.Children.Add(dayHeader);
            mainContainer.Children.Add(dateBody);
        }

        UpdateTotals(grandSpent, grandWon);
    }

    void UpdateTotals(decimal spent, decimal won)
    {
        decimal net = won - spent;
        string  sign = net >= 0 ? "+" : "";
        lblTotalSpent.Text  = $"SPENT: ${spent:N2}";
        lblTotalWon.Text    = $"WON: ${won:N2}";
        lblNet.Text         = $"NET: {sign}${net:N2}";
        lblNet.TextColor    = net >= 0 ? Color.FromArgb("#66BB6A") : Color.FromArgb("#EF5350");
    }

    View BuildEntryRow(SpendingRecord rec)
    {
        var gameDef = GameDefs.FirstOrDefault(g => g.Key == rec.Game);
        var accent  = string.IsNullOrEmpty(gameDef.Color)
            ? Color.FromArgb("#888888")
            : Color.FromArgb(gameDef.Color);
        string gameName = string.IsNullOrEmpty(gameDef.Name) ? rec.Game : gameDef.Name;

        var row = new Grid
        {
            BackgroundColor = Color.FromArgb("#111D29"),
            Padding = new Thickness(8, 5),
            Margin  = new Thickness(0, 1),
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(4)),    // accent strip
                new ColumnDefinition(new GridLength(80)),   // game name
                new ColumnDefinition(GridLength.Star),      // tickets × cost + note
                new ColumnDefinition(new GridLength(58)),   // total
                new ColumnDefinition(new GridLength(30)),   // delete
            }
        };

        // Accent strip
        row.Children.Add(new BoxView
        {
            Color = accent, WidthRequest = 4,
            VerticalOptions = LayoutOptions.Fill, HorizontalOptions = LayoutOptions.Start
        });

        // Game name
        row.Children.Add(new Label
        {
            Text = gameName,
            FontSize = 11, FontAttributes = FontAttributes.Bold,
            TextColor = accent,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(8, 0, 4, 0)
        }.WithCol(1));

        // Detail
        string detail = $"{rec.TicketCount} × ${rec.CostEach:N2}";
        if (!string.IsNullOrEmpty(rec.Note)) detail += $"  {rec.Note}";
        row.Children.Add(new Label
        {
            Text = detail, FontSize = 10,
            TextColor = Color.FromArgb("#BBBBBB"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        }.WithCol(2));

        // Total cost
        row.Children.Add(new Label
        {
            Text = $"${rec.TotalCost:N2}",
            FontSize = 11, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#EF9A9A"),
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.End
        }.WithCol(3));

        // Delete
        var capturedRec = rec;
        bool dateParsed = DateTime.TryParse(rec.Date, out var dt);
        string dateStr  = dateParsed ? dt.ToString("M/d/yy") : rec.Date;

        // Plain Grid instead of Border+RoundRectangle — per-row shaped Borders are expensive
        // to create on Android (each needs its own Skia-drawn native view), and at 50+ rows
        // that alone added several seconds to BuildUI().
        var delBorder = new Grid
        {
            BackgroundColor = Color.FromArgb("#7B1A1A"),
            Padding = new Thickness(4, 2),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "✕", FontSize = 10, FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center,
                }
            }
        };
        delBorder.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                bool ok = await DisplayAlert("Remove Entry",
                    $"Remove {capturedRec.TicketCount} {gameName} ticket(s) on {dateStr}?",
                    "Yes", "Cancel");
                if (!ok) return;
                _records.Remove(capturedRec);
                await SpendingTracker.SaveAllAsync(_records);
                BuildMonthFilter();
                BuildUI();
            })
        });
        row.Children.Add(delBorder.WithCol(4));

        return row;
    }

    // ── Add Overlay ───────────────────────────────────────────────────────────

    void BuildAddOverlay()
    {
        _addTitleLabel = new Label
        {
            Text = "Log Ticket Spending",
            FontSize = 15, FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center
        };
        _addDatePicker = new DatePicker
        {
            Format = "MMM d, yyyy", FontSize = 14, Date = DateTime.Today,
            MinimumDate = new DateTime(2020, 1, 1), MaximumDate = new DateTime(2035, 12, 31),
            TextColor = Colors.White,
        };
        _addGamePicker = new Picker
        {
            Title = "Select Game",
            TitleColor = Color.FromArgb("#8B9DC3"),
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#2D3E55"),
            FontSize = 13,
        };
        foreach (var (_, name, _) in GameDefs)
            _addGamePicker.Items.Add(name);

        _addCountEntry = new Entry
        {
            Placeholder = "Number of tickets (rows) played",
            PlaceholderColor = Color.FromArgb("#8B9DC3"),
            TextColor = Colors.White, FontSize = 13,
            Keyboard = Keyboard.Numeric,
            BackgroundColor = Color.FromArgb("#2D3E55"),
        };
        _addCostEntry = new Entry
        {
            PlaceholderColor = Color.FromArgb("#8B9DC3"),
            TextColor = Colors.White, FontSize = 13,
            Keyboard = Keyboard.Numeric,
            BackgroundColor = Color.FromArgb("#2D3E55"),
        };
        _addNoteEntry = new Entry
        {
            Placeholder = "Note (optional)",
            PlaceholderColor = Color.FromArgb("#8B9DC3"),
            TextColor = Colors.White, FontSize = 13,
            BackgroundColor = Color.FromArgb("#2D3E55"),
        };

        // Auto-fill cost when game changes
        _addGamePicker.SelectedIndexChanged += (_, _) =>
        {
            if (_addGamePicker.SelectedIndex >= 0)
            {
                string gameKey = GameDefs[_addGamePicker.SelectedIndex].Key;
                decimal cost = SpendingTracker.TicketCost(gameKey);
                _addCostEntry!.Text = cost.ToString("0.##");
                _addCostEntry.Placeholder = $"Default: ${cost:N2}";
            }
        };

        var btnCancel = new Button { Text = "Cancel", BackgroundColor = Color.FromArgb("#4B5563"),
            TextColor = Colors.White, CornerRadius = 10, HeightRequest = 44, FontSize = 14 };
        var btnSave   = new Button { Text = "Save",   BackgroundColor = Color.FromArgb("#2563EB"),
            TextColor = Colors.White, CornerRadius = 10, HeightRequest = 44, FontSize = 14,
            FontAttributes = FontAttributes.Bold };

        btnCancel.Clicked += (_, _) => _addOverlay!.IsVisible = false;
        btnSave.Clicked   += OnSaveAdd;

        var btnRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10
        };
        Grid.SetColumn(btnCancel, 0); btnRow.Children.Add(btnCancel);
        Grid.SetColumn(btnSave,   1); btnRow.Children.Add(btnSave);

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(20, 18),
            VerticalOptions   = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 320,
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    _addTitleLabel,
                    new Label { Text = "Date Played", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                    _addDatePicker,
                    new Label { Text = "Game", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                    _addGamePicker,
                    new Label { Text = "Number of Tickets (rows)", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                    _addCountEntry,
                    new Label { Text = "Cost Per Ticket ($) — edit if wrong", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                    _addCostEntry,
                    new Label { Text = "Note (optional)", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                    _addNoteEntry,
                    btnRow,
                }
            }
        };

        _addOverlay = new Grid { BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false };
        _addOverlay.Children.Add(card);

        var rootGrid = (Grid)Content;
        Grid.SetRow(_addOverlay, 0);
        Grid.SetRowSpan(_addOverlay, 99);
        rootGrid.Children.Add(_addOverlay);
    }

    async void OnSaveAdd(object? sender, EventArgs e)
    {
        if (_addGamePicker!.SelectedIndex < 0)
        {
            await DisplayAlert("No Game", "Please select a game.", "OK");
            return;
        }

        string countText = _addCountEntry!.Text?.Trim() ?? "";
        if (!int.TryParse(countText, out int count) || count <= 0)
        {
            await DisplayAlert("Invalid Count", "Enter a valid number of tickets.", "OK");
            return;
        }

        string  gameKey  = GameDefs[_addGamePicker.SelectedIndex].Key;
        decimal costEach = SpendingTracker.TicketCost(gameKey);
        string  costText = _addCostEntry!.Text?.Trim() ?? "";
        if (!string.IsNullOrEmpty(costText) &&
            decimal.TryParse(costText, out decimal parsedCost) && parsedCost >= 0)
        {
            costEach = parsedCost;
        }

        var rec = new SpendingRecord
        {
            Game        = gameKey,
            Date        = $"{_addDatePicker!.Date:yyyy-MM-dd}",
            TicketCount = count,
            CostEach    = costEach,
            Note        = _addNoteEntry!.Text?.Trim() ?? "",
        };

        _records.Add(rec);
        await SpendingTracker.SaveAllAsync(_records);
        _addOverlay!.IsVisible = false;
        BuildMonthFilter();
        BuildUI();
    }

    // ── Buttons ───────────────────────────────────────────────────────────────

    void BtnAdd_Clicked(object sender, EventArgs e)
    {
        _addDatePicker!.Date = DateTime.Today;
        _addGamePicker!.SelectedIndex = -1;
        _addCountEntry!.Text = "";
        _addCostEntry!.Text  = "";
        _addNoteEntry!.Text  = "";
        _addOverlay!.IsVisible = true;
    }

    async void BtnLogToday_Clicked(object sender, EventArgs e)
    {
        await ShowBusyAsync("Logging today's tickets...");
        {
            // Dump raw preference data for debugging
            SpendingTracker.DumpPrefsToFile();

            // Read ticket counts from ALL slots (not just active slot) so multi-slot advance plays are included
            _todayDatePicker!.Date = DateTime.Today;

            foreach (var (key, entry) in _todayEntries)
            {
                int n = SpendingTracker.CountAllSlotsTicketsToday(key);
                entry.Text = n > 0 ? n.ToString() : "0";

                if (_todayBreakdowns.TryGetValue(key, out var breakdownLabel))
                {
                    string breakdown = SpendingTracker.BuildTodayCountBreakdown(key);
                    breakdownLabel.Text = breakdown;
                    breakdownLabel.IsVisible = !string.IsNullOrEmpty(breakdown);
                }

                if (_todayWarnings.TryGetValue(key, out var warnLabel))
                {
                    if (n == 0 && SpendingTracker.HasEarlierActiveAdvance(key, out string range))
                    {
                        warnLabel.Text = $"⚠ Already logged {range} — leave at 0, don't re-enter";
                        warnLabel.IsVisible = true;
                    }
                    else
                    {
                        warnLabel.IsVisible = false;
                    }
                }
            }

            // Auto-count FP-flagged F5 rows across ALL slots (same as ticket count scan)
            if (_f5ReplayEntry != null)
            {
                int activeSlot = Preferences.Get("f5_active_slot", -1);
                int fpCount = 0;
                for (int s = 0; s < 10; s++)
                {
                    string entries = s == activeSlot
                        ? Preferences.Get("f5_entries", "")
                        : Preferences.Get($"f5_set_{s}", "");
                    if (string.IsNullOrEmpty(entries)) continue;
                    string fpRaw = Preferences.Get($"f5_freeplay_{s}", "");
                    string adv   = Preferences.Get($"f5_adv_{s}", "");
                    fpCount += WinnerPage.CountFreePlayRowsToday(entries, fpRaw, adv);
                }
                _f5ReplayEntry.Text = fpCount > 0 ? fpCount.ToString() : "0";
            }

            // Populate D3 Midday / Evening counts across all slots
            var (d3Mid, d3Eve, _) = SpendingTracker.CountD3AllSlotsTodayByFilter();
            if (_d3MidEntry != null) _d3MidEntry.Text = d3Mid.ToString();
            if (_d3EveEntry != null) _d3EveEntry.Text = d3Eve.ToString();

            if (_hsCostEntry != null) _hsCostEntry.Text = SpendingTracker.SumHotSpotCostToday().ToString("F2");
            if (_hsCountLabel != null)
            {
                int hsCount = SpendingTracker.CountHotSpotTicketsToday();
                _hsCountLabel.Text = $"{hsCount} ticket{(hsCount == 1 ? "" : "s")}";
            }

            UpdateTodayTotal();

            // Save immediately with the auto-computed counts — a single tap of "Log Today" now
            // always writes to disk. Previously this only opened a review screen and a SEPARATE
            // "Log All" tap inside it was required to actually save; skipping that second tap
            // (easy to do, since "Log Today" sounds complete on its own) silently logged nothing.
            bool saved = await SaveTodayLogAsync(silent: true);
            if (_todaySavedBanner != null)
            {
                _todaySavedBanner.Text = saved ? "✓ Saved — edit any count below, then tap Update to re-save" : "Nothing found to log yet for this date";
                _todaySavedBanner.TextColor = saved ? Color.FromArgb("#4CAF7D") : Color.FromArgb("#8B9DC3");
            }
        }
        HideBusy();
        _todayOverlay!.IsVisible = true;
    }

    void UpdateTodayTotal()
    {
        int f5Replays = int.TryParse(_f5ReplayEntry?.Text, out int rp) ? rp : 0;
        decimal scPrice = decimal.TryParse(_scPriceEntry?.Text, out decimal sp) ? sp : 0m;
        decimal total = 0;
        foreach (var (key, entry) in _todayEntries)
        {
            if (!int.TryParse(entry.Text, out int n) || n <= 0) continue;
            int paid = key == "F5" ? Math.Max(0, n - f5Replays) : n;
            decimal costEa = key == "SC" ? scPrice : SpendingTracker.TicketCost(key);
            total += paid * costEa;
        }
        // D3 sub-entries (Midday + Evening)
        if (int.TryParse(_d3MidEntry?.Text, out int dm) && dm > 0) total += dm * 1m;
        if (int.TryParse(_d3EveEntry?.Text, out int de) && de > 0) total += de * 1m;
        if (decimal.TryParse(_hsCostEntry?.Text, out decimal hs) && hs > 0) total += hs;
        _todayTotalLabel!.Text = $"Today's Total:  ${total:N2}";
    }

    async void OnConfirmLogToday(object? sender, EventArgs e)
    {
        bool saved = await SaveTodayLogAsync(silent: false);
        if (!saved) return; // "Nothing to log" alert already shown
        _todayOverlay!.IsVisible = false;
    }

    /// <summary>
    /// Builds today's records from whatever is currently in the overlay's entry fields and
    /// saves them, replacing any previously auto-logged ("auto"/"M"/"E") rows for that date.
    /// Called both by the initial "Log Today" tap (silent — no dialogs, since counts may
    /// legitimately be all zero) and by the overlay's "Update" button (not silent, so an
    /// explicit tap with nothing to log still tells the user why nothing happened).
    /// Returns true if anything was saved.
    /// </summary>
    async Task<bool> SaveTodayLogAsync(bool silent)
    {
        string date      = $"{_todayDatePicker!.Date:yyyy-MM-dd}";
        int    f5Replays = int.TryParse(_f5ReplayEntry?.Text, out int rp) ? rp : 0;
        decimal scPrice  = decimal.TryParse(_scPriceEntry?.Text, out decimal sp) && sp > 0 ? sp : 0m;

        // Build new records first — never touch _records until we know there's something to log
        var toAdd = new List<SpendingRecord>();

        foreach (var (key, entry) in _todayEntries)
        {
            if (!int.TryParse(entry.Text, out int n) || n <= 0) continue;
            int paid = key == "F5" ? Math.Max(0, n - f5Replays) : n;
            if (paid <= 0) continue;
            decimal costEa = key == "SC" ? scPrice : SpendingTracker.TicketCost(key);
            if (key == "SC" && costEa <= 0)
            {
                if (!silent) await DisplayAlert("Price Required", "Enter the price per scratcher ticket.", "OK");
                return false;
            }
            toAdd.Add(new SpendingRecord
            {
                Game        = key,
                Date        = date,
                TicketCount = paid,
                CostEach    = costEa,
                Note        = "auto",
            });
        }

        // D3 sub-entries (M = Midday, E = Evening)
        foreach (var (entry, note) in new[] { (_d3MidEntry, "M"), (_d3EveEntry, "E") })
        {
            if (entry == null || !int.TryParse(entry.Text, out int n) || n <= 0) continue;
            toAdd.Add(new SpendingRecord { Game = "D3", Date = date, TicketCount = n, CostEach = 1m, Note = note });
        }

        // Hot Spot: cost is the editable total itself, and TicketCount MUST stay 1 — TotalCost
        // is TicketCount × CostEach, so setting TicketCount to the real ticket count (tried
        // earlier tonight, reverted) silently multiplied every Hot Spot total, confirmed live
        // 2026-08-09 as a real $30 becoming $60 with 2 tickets. Reverted — see the matching
        // comment in SpendingTracker.AutoSyncTodayAsync.
        if (decimal.TryParse(_hsCostEntry?.Text, out decimal hsCost) && hsCost > 0)
            toAdd.Add(new SpendingRecord { Game = "HS", Date = date, TicketCount = 1, CostEach = hsCost, Note = "auto" });

        if (toAdd.Count == 0)
        {
            if (!silent) await DisplayAlert("Nothing to log", "All counts are 0.", "OK");
            return false;
        }

        // Safe to replace now — we confirmed we have records to add
        _records.RemoveAll(r => r.Date == date &&
            (r.Note == "auto" || r.Note == "M" || r.Note == "E"));
        _records.AddRange(toAdd);

        await SpendingTracker.SaveAllAsync(_records);
        _filterMonth = null;
        BuildMonthFilter();
        BuildUI();
        return true;
    }

    // ── Build Log Today overlay ───────────────────────────────────────────────

    void BuildLogTodayOverlay()
    {
        _todayDatePicker = new DatePicker
        {
            Format = "MMM d, yyyy", FontSize = 14, Date = DateTime.Today,
            MinimumDate = new DateTime(2020, 1, 1), MaximumDate = new DateTime(2035, 12, 31),
            TextColor = Colors.White,
        };

        _todayRowsContainer = new VerticalStackLayout { Spacing = 6 };
        _todayEntries.Clear();
        _todayWarnings.Clear();
        _f5ReplayEntry = null;
        _d3MidEntry = null; _d3EveEntry = null; _d3BothEntry = null;
        _scPriceEntry = null;
        _hsCostEntry = null;
        _hsCountLabel = null;

        foreach (var (key, name, color) in GameDefs)
        {
            // Scratchers are logged from the Scratchers page, not here
            if (key == "SC") continue;

            decimal cost   = SpendingTracker.TicketCost(key);
            var     accent = Color.FromArgb(color);

            // D3 gets two sub-rows: Midday (M) and Evening (E)
            if (key == "D3")
            {
                _todayRowsContainer.Children.Add(new Label
                {
                    Text = "Daily 3", FontSize = 12, FontAttributes = FontAttributes.Bold,
                    TextColor = accent, Margin = new Thickness(0, 4, 0, 0)
                });
                var d3Subs = new[] { ("M  Midday", Color.FromArgb("#42A5F5")), ("E  Evening", Color.FromArgb("#EF5350")) };
                for (int si = 0; si < 2; si++)
                {
                    var (subLabel, subColor) = d3Subs[si];
                    var subEntry = new Entry
                    {
                        Text = "0", TextColor = Colors.White, FontSize = 13,
                        Keyboard = Keyboard.Numeric,
                        BackgroundColor = Color.FromArgb("#2D3E55"),
                        HorizontalTextAlignment = TextAlignment.End, WidthRequest = 60,
                    };
                    subEntry.TextChanged += (_, _) => UpdateTodayTotal();
                    if (si == 0) _d3MidEntry = subEntry;
                    else         _d3EveEntry = subEntry;

                    var subRow = new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(GridLength.Star),
                            new ColumnDefinition(new GridLength(70)),
                            new ColumnDefinition(new GridLength(60)),
                        },
                        Margin = new Thickness(14, 0, 0, 0),
                    };
                    subRow.Children.Add(new Label
                    {
                        Text = subLabel, FontSize = 11,
                        TextColor = subColor, VerticalOptions = LayoutOptions.Center
                    });
                    subRow.Children.Add(new Label
                    {
                        Text = "$1.00/ea", FontSize = 11, TextColor = Color.FromArgb("#AAAAAA"),
                        VerticalOptions = LayoutOptions.Center,
                        HorizontalTextAlignment = TextAlignment.End,
                    }.WithCol(1));
                    subRow.Children.Add(subEntry.WithCol(2));
                    _todayRowsContainer.Children.Add(subRow);
                }
                continue;
            }

            var countEntry = new Entry
            {
                Text = "0",
                TextColor = Colors.White, FontSize = 13,
                Keyboard = Keyboard.Numeric,
                BackgroundColor = Color.FromArgb("#2D3E55"),
                HorizontalTextAlignment = TextAlignment.End,
                WidthRequest = 60,
            };
            countEntry.TextChanged += (_, _) => UpdateTodayTotal();

            // Scratchers: price varies — show a price entry instead of fixed label
            if (key == "SC")
            {
                _scPriceEntry = new Entry
                {
                    Placeholder = "$0", PlaceholderColor = Color.FromArgb("#6B8FAF"),
                    TextColor = Colors.White, FontSize = 13,
                    Keyboard = Keyboard.Numeric,
                    BackgroundColor = Color.FromArgb("#2D3E55"),
                    HorizontalTextAlignment = TextAlignment.End,
                    WidthRequest = 60,
                };
                _scPriceEntry.TextChanged += (_, _) => UpdateTodayTotal();

                // Two-row layout: game name + count on row 0, price label + price entry on row 1
                var scGrid = new Grid
                {
                    RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(new GridLength(60)),
                    },
                    RowSpacing = 4,
                };
                scGrid.Children.Add(new Label
                {
                    Text = name, FontSize = 12, FontAttributes = FontAttributes.Bold,
                    TextColor = accent, VerticalOptions = LayoutOptions.Center
                });
                scGrid.Add(countEntry, 1, 0);
                scGrid.Add(new Label
                {
                    Text = "price/ea ($)",
                    FontSize = 11, TextColor = Color.FromArgb("#AAAAAA"),
                    VerticalOptions = LayoutOptions.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                }, 0, 1);
                scGrid.Add(_scPriceEntry, 1, 1);

                _todayRowsContainer.Children.Add(scGrid);
                _todayEntries.Add((key, countEntry));
                continue;
            }

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(70)),
                    new ColumnDefinition(new GridLength(60)),
                }
            };
            row.Children.Add(new Label
            {
                Text = name, FontSize = 12, FontAttributes = FontAttributes.Bold,
                TextColor = accent, VerticalOptions = LayoutOptions.Center
            });
            row.Children.Add(new Label
            {
                Text = $"${cost:N2}/ea",
                FontSize = 11, TextColor = Color.FromArgb("#AAAAAA"),
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.End,
            }.WithCol(1));
            row.Children.Add(countEntry.WithCol(2));

            _todayRowsContainer.Children.Add(row);
            _todayEntries.Add((key, countEntry));

            // Hidden by default — BtnLogToday_Clicked shows this if an advance ticket for
            // this game is already running from an earlier day (so don't re-type a count).
            var warnLabel = new Label
            {
                Text = "", FontSize = 10, TextColor = Color.FromArgb("#FFB74D"),
                Margin = new Thickness(0, 0, 0, 2),
                IsVisible = false,
            };
            _todayRowsContainer.Children.Add(warnLabel);
            _todayWarnings[key] = warnLabel;

            // Hidden by default — shows the math behind the count whenever an advance-play
            // row contributes more than 1 to today's total (e.g. "9 tickets + 1 advance (5
            // draws) = 14"), so the number is verifiable at a glance instead of unexplained.
            var breakdownLabel = new Label
            {
                Text = "", FontSize = 10, TextColor = Color.FromArgb("#8B9DC3"),
                Margin = new Thickness(0, 0, 0, 2),
                IsVisible = false,
            };
            _todayRowsContainer.Children.Add(breakdownLabel);
            _todayBreakdowns[key] = breakdownLabel;

            // Fantasy 5 only: replay row (free tickets — subtracted from cost)
            if (key == "F5")
            {
                _f5ReplayEntry = new Entry
                {
                    Text = "0",
                    TextColor = Color.FromArgb("#AAAAAA"), FontSize = 12,
                    Keyboard = Keyboard.Numeric,
                    BackgroundColor = Color.FromArgb("#1A2840"),
                    HorizontalTextAlignment = TextAlignment.End,
                    WidthRequest = 60,
                };
                _f5ReplayEntry.TextChanged += (_, _) => UpdateTodayTotal();

                var replayRow = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(new GridLength(70)),
                        new ColumnDefinition(new GridLength(60)),
                    }
                };
                replayRow.Children.Add(new Label
                {
                    Text = "  ↳ Replays (free)", FontSize = 11,
                    TextColor = Color.FromArgb("#78909C"),
                    VerticalOptions = LayoutOptions.Center
                });
                replayRow.Children.Add(new Label
                {
                    Text = "subtract",
                    FontSize = 10, TextColor = Color.FromArgb("#546E7A"),
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.End,
                }.WithCol(1));
                replayRow.Children.Add(_f5ReplayEntry.WithCol(2));
                _todayRowsContainer.Children.Add(replayRow);
            }
        }

        // Hot Spot: not in GameDefs (its cost is wager × draws × Bulls-eye multiplier for a
        // single active ticket, not "N tickets × one fixed price" like every other game), so
        // it doesn't fit the loop above — shown as its own row with an editable total-cost
        // entry instead of a count, mirroring how Scratchers already gets an editable-price
        // row instead of a fixed one.
        _hsCostEntry = new Entry
        {
            Text = "0", TextColor = Colors.White, FontSize = 13,
            Keyboard = Keyboard.Numeric,
            BackgroundColor = Color.FromArgb("#2D3E55"),
            HorizontalTextAlignment = TextAlignment.End, WidthRequest = 60,
        };
        _hsCostEntry.TextChanged += (_, _) => UpdateTodayTotal();
        var hsRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(70)),
                new ColumnDefinition(new GridLength(60)),
            }
        };
        hsRow.Children.Add(new Label
        {
            Text = "Hot Spot", FontSize = 12, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#E65100"), VerticalOptions = LayoutOptions.Center
        });
        // Was a static "total ($)" caption — never showed how many Hot Spot tickets the $
        // total actually came from, unlike every other game's row. CountHotSpotTicketsToday()
        // already existed for exactly this but was never called from here.
        _hsCountLabel = new Label
        {
            Text = "0 tickets", FontSize = 11, TextColor = Color.FromArgb("#AAAAAA"),
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.End,
        };
        hsRow.Children.Add(_hsCountLabel.WithCol(1));
        hsRow.Children.Add(_hsCostEntry.WithCol(2));
        _todayRowsContainer.Children.Add(hsRow);

        _todayTotalLabel = new Label
        {
            Text = "Today's Total:  $0.00",
            FontSize = 14, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#FFD54F"),
            HorizontalOptions = LayoutOptions.Center,
        };

        var btnCancel = new Button
        {
            Text = "Close", BackgroundColor = Color.FromArgb("#4B5563"),
            TextColor = Colors.White, CornerRadius = 10, HeightRequest = 44, FontSize = 14
        };
        var btnConfirm = new Button
        {
            Text = "Update", BackgroundColor = Color.FromArgb("#2563EB"),
            TextColor = Colors.White, CornerRadius = 10, HeightRequest = 44, FontSize = 14,
            FontAttributes = FontAttributes.Bold
        };
        btnCancel.Clicked  += (_, _) => _todayOverlay!.IsVisible = false;
        btnConfirm.Clicked += OnConfirmLogToday;

        var btnRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10
        };
        Grid.SetColumn(btnCancel,  0); btnRow.Children.Add(btnCancel);
        Grid.SetColumn(btnConfirm, 1); btnRow.Children.Add(btnConfirm);

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(20, 14),
            VerticalOptions   = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 340,
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    // Fixed header row: title + home icon
                    new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(GridLength.Star),
                            new ColumnDefinition(GridLength.Auto),
                        },
                        Children =
                        {
                            ColHelper.MakeBallView(),
                            new Label
                            {
                                Text = "Log Today's Tickets",
                                FontSize = 15, FontAttributes = FontAttributes.Bold,
                                TextColor = Color.FromArgb("#FFD54F"),
                                HorizontalOptions = LayoutOptions.Center,
                                VerticalOptions = LayoutOptions.Center
                            }.WithCol(0),
                            new Label
                            {
                                Text = "⌂", FontSize = 20,
                                TextColor = Colors.White,
                                VerticalOptions = LayoutOptions.Center,
                                HorizontalOptions = LayoutOptions.End,
                                Padding = new Thickness(8, 0, 0, 0),
                                GestureRecognizers =
                                {
                                    new TapGestureRecognizer
                                    {
                                        Command = new Command(async () =>
                                        {
                                            _todayOverlay!.IsVisible = false;
                                            await Shell.Current.GoToAsync("//MainPage", false);
                                        })
                                    }
                                }
                            }.WithCol(1),
                        }
                    },
                    (_todaySavedBanner = new Label
                    {
                        Text = "", FontSize = 12, FontAttributes = FontAttributes.Bold,
                        HorizontalOptions = LayoutOptions.Center,
                    }),
                    // Header + game rows scroll independently
                    new ScrollView
                    {
                        MaximumHeightRequest = 500,
                        Content = new VerticalStackLayout
                        {
                            Spacing = 10,
                            Children =
                            {
                                new Label { Text = "Date", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                                _todayDatePicker,
                                new Label { Text = "Tickets played per game (edit if needed):", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                                _todayRowsContainer,
                            }
                        }
                    },
                    // Total and buttons always visible at the bottom
                    new BoxView { HeightRequest = 1, BackgroundColor = Color.FromArgb("#334155") },
                    _todayTotalLabel,
                    btnRow,
                }
            }
        };

        _todayOverlay = new Grid { BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false };
        _todayOverlay.Children.Add(card);

        var rootGrid = (Grid)Content;
        Grid.SetRow(_todayOverlay, 0);
        Grid.SetRowSpan(_todayOverlay, 99);
        rootGrid.Children.Add(_todayOverlay);
    }

    // ── Verify overlay ────────────────────────────────────────────────────────

    void BuildVerifyOverlay()
    {
        _verifyOverlay = new Grid { BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false };
        var rootGrid = (Grid)Content;
        Grid.SetRow(_verifyOverlay, 0);
        Grid.SetRowSpan(_verifyOverlay, 99);
        rootGrid.Children.Add(_verifyOverlay);
    }

    // ── Busy overlay ──────────────────────────────────────────────────────────

    void BuildBusyOverlay()
    {
        _busySpinner = new ActivityIndicator
        {
            IsRunning = false, Color = Color.FromArgb("#90CAF9"),
            WidthRequest = 40, HeightRequest = 40,
        };

        _busyStatusLabel = new Label
        {
            Text = "Loading...", FontSize = 13,
            TextColor = Color.FromArgb("#90CAF9"), HorizontalTextAlignment = TextAlignment.Center,
        };

        _busyOverlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#000000"), Opacity = 0.001,
            InputTransparent = true,
        };
        _busyOverlay.Children.Add(new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center,
            Spacing = 12,
            Children = { _busySpinner, _busyStatusLabel }
        });

        var rootGrid = (Grid)Content;
        Grid.SetRow(_busyOverlay, 0);
        Grid.SetRowSpan(_busyOverlay, 99);
        rootGrid.Children.Add(_busyOverlay);
    }

    async Task ShowBusyAsync(string message)
    {
        _busyStatusLabel!.Text = message;
        _busyOverlay!.Opacity = 1;
        _busyOverlay.InputTransparent = false;
        _busySpinner!.IsRunning = true;
        await Task.Delay(80);  // guarantee a render pass before the CPU-bound work runs
    }

    void HideBusy()
    {
        _busyOverlay!.Opacity = 0.001;
        _busyOverlay.InputTransparent = true;
        _busySpinner!.IsRunning = false;
    }

    async void BtnVerify_Clicked(object sender, EventArgs e)
    {
        string todayStr = DateTime.Today.ToString("yyyy-MM-dd");

        // Force a fresh scan/sync on both sides so Verify always compares current state, not
        // whatever was loaded when the page first opened.
        await TicketLogService.ForceSyncTodayAsync();
        await SpendingTracker.AutoSyncTodayAsync();
        _records = await SpendingTracker.LoadAllAsync();

        var tlAll     = await TicketLogService.LoadAllAsync();
        var tlToday   = tlAll.Where(x => x.Date == todayStr).ToList();
        var slToday   = _records.Where(r => r.Date == todayStr).ToList();

        _verifyOverlay!.Children.Clear();

        var rows = new VerticalStackLayout { Spacing = 5 };

        rows.Children.Add(new Label
        {
            Text = "Verify vs Ticket Log",
            FontSize = 15, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#FFD54F"),
            HorizontalOptions = LayoutOptions.Center
        });
        rows.Children.Add(new Label
        {
            Text = DateTime.Today.ToString("ddd, MMM d, yyyy"),
            FontSize = 11, TextColor = Color.FromArgb("#8B9DC3"),
            HorizontalOptions = LayoutOptions.Center
        });
        rows.Children.Add(new BoxView { HeightRequest = 1, BackgroundColor = Color.FromArgb("#334155"), Margin = new Thickness(0, 4) });

        // Column headers
        rows.Children.Add(MakeVerifyHeader());

        bool anyMismatch = false;
        decimal totalTL = 0, totalSL = 0;

        foreach (var (key, name, color) in GameDefs)
        {
            // Skip games that don't draw today
            if (!SpendingTracker.GameDrawsOnDate(key, DateTime.Today)) continue;

            var accent = Color.FromArgb(color);

            if (key == "D3")
            {
                // D3: compare M and E separately
                bool IsM(TicketLogEntry x) => x.Game == "D3" && (x.Extra == "M" || x.Extra.EndsWith("|M", StringComparison.OrdinalIgnoreCase));
                bool IsE(TicketLogEntry x) => x.Game == "D3" && (x.Extra == "E" || x.Extra.EndsWith("|E", StringComparison.OrdinalIgnoreCase));

                decimal tlM = tlToday.Where(IsM).Sum(x => DrawsFromEntry(x) * 1m);
                decimal tlE = tlToday.Where(IsE).Sum(x => DrawsFromEntry(x) * 1m);
                decimal slM = slToday.Where(r => r.Game == "D3" && r.Note == "M").Sum(r => r.TotalCost);
                decimal slE = slToday.Where(r => r.Game == "D3" && r.Note == "E").Sum(r => r.TotalCost);


                if (tlM > 0 || slM > 0)
                {
                    bool ok = Math.Abs(tlM - slM) <= 1m; // ±$1 tolerance for multi-draw rounding
                    if (!ok) anyMismatch = true;
                    rows.Children.Add(MakeVerifyRow("Daily 3 M", Color.FromArgb("#42A5F5"), tlM, slM, ok));
                    totalTL += tlM; totalSL += slM;
                }
                if (tlE > 0 || slE > 0)
                {
                    bool ok = Math.Abs(tlE - slE) <= 1m;
                    if (!ok) anyMismatch = true;
                    rows.Children.Add(MakeVerifyRow("Daily 3 E", Color.FromArgb("#EF5350"), tlE, slE, ok));
                    totalTL += tlE; totalSL += slE;
                }
                continue;
            }

            int     tlCount = tlToday.Count(x => x.Game == key && !x.IsFreePlay);
            decimal price   = SpendingTracker.TicketCost(key);
            // For advance tickets, multiply each entry by its draw count (e.g. 12-day advance = 12 draws)
            decimal tlCost = key == "SC"
                ? tlToday.Where(x => x.Game == "SC")
                         .Sum(x => decimal.TryParse(x.Extra.TrimStart('$'), out var v) ? v : 0m)
                : tlToday.Where(x => x.Game == key && !x.IsFreePlay)
                         .Sum(x => DrawsFromEntry(x) * price);
            decimal slCost = slToday.Where(r => r.Game == key).Sum(r => r.TotalCost);

            if (tlCount == 0 && slCost == 0) continue;

            bool match = tlCost == slCost;
            if (!match) anyMismatch = true;
            rows.Children.Add(MakeVerifyRow(name, accent, tlCost, slCost, match));
            totalTL += tlCost; totalSL += slCost;
        }

        // Hot Spot: deliberately not in GameDefs (its cost is wager × draws × Bulls-eye
        // multiplier per ticket, not "N tickets × one fixed price" like the generic loop
        // above assumes — same reason it gets its own dedicated row on the Log Today overlay
        // instead of joining that loop too), so it needs its own block here rather than being
        // added to GameDefs itself. Confirmed live 2026-08-09: without this, Hot Spot was
        // completely invisible to Verify — both totals silently excluded it, so "All match"
        // could show a green checkmark on a day where Hot Spot was the only thing purchased.
        var hsToday = tlToday.Where(x => x.Game == "HS").ToList();
        if (hsToday.Count > 0 || slToday.Any(r => r.Game == "HS"))
        {
            decimal hsTlCost = hsToday.Sum(x =>
            {
                decimal wager = 1m; bool bullseye = false;
                foreach (var part in x.Extra.Split('|'))
                {
                    var kv = part.Split(':');
                    if (kv.Length != 2) continue;
                    if (kv[0] == "W") decimal.TryParse(kv[1], out wager);
                    else if (kv[0] == "BE") bullseye = kv[1] == "1";
                }
                int draws = Math.Max(1, x.DrawCount);
                return wager * draws * (bullseye ? 2 : 1);
            });
            decimal hsSlCost = slToday.Where(r => r.Game == "HS").Sum(r => r.TotalCost);
            bool hsMatch = Math.Abs(hsTlCost - hsSlCost) <= 1m;
            if (!hsMatch) anyMismatch = true;
            rows.Children.Add(MakeVerifyRow("Hot Spot", Color.FromArgb("#E65100"), hsTlCost, hsSlCost, hsMatch));
            totalTL += hsTlCost; totalSL += hsSlCost;
        }

        rows.Children.Add(new BoxView { HeightRequest = 1, BackgroundColor = Color.FromArgb("#334155"), Margin = new Thickness(0, 4) });
        rows.Children.Add(MakeVerifyRow("Total", Color.FromArgb("#FFD54F"), totalTL, totalSL, Math.Abs(totalTL - totalSL) <= 1m));
        rows.Children.Add(new BoxView { HeightRequest = 1, BackgroundColor = Color.FromArgb("#334155"), Margin = new Thickness(0, 4) });
        rows.Children.Add(new Label
        {
            Text = anyMismatch ? "⚠  Discrepancies found" : "✓  All match",
            FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = anyMismatch ? Color.FromArgb("#EF5350") : Color.FromArgb("#66BB6A"),
            HorizontalOptions = LayoutOptions.Center
        });

        var btnClose = new Border
        {
            BackgroundColor = Color.FromArgb("#4B5563"),
            Stroke = Colors.Transparent, StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(20, 8),
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 6, 0, 0),
            Content = new Label { Text = "Close", TextColor = Colors.White, FontSize = 14, HorizontalOptions = LayoutOptions.Center }
        };
        btnClose.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => _verifyOverlay!.IsVisible = false) });
        rows.Children.Add(btnClose);

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(20, 18),
            VerticalOptions   = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 340,
            Content = new ScrollView { MaximumHeightRequest = 500, Content = rows }
        };

        _verifyOverlay.Children.Add(card);
        _verifyOverlay.IsVisible = true;

    }

    static View MakeVerifyHeader()
    {
        var g = new Grid
        {
            Padding = new Thickness(0, 0, 0, 2),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(65)),
                new ColumnDefinition(new GridLength(65)),
                new ColumnDefinition(new GridLength(22)),
            }
        };
        g.Children.Add(VLbl("Game",         10, "#8B9DC3", TextAlignment.Start));
        g.Children.Add(VLbl("Ticket Log",   10, "#8B9DC3", TextAlignment.End  ).WithCol(1));
        g.Children.Add(VLbl("Spending Log", 10, "#8B9DC3", TextAlignment.End  ).WithCol(2));
        return g;
    }

    static View MakeVerifyRow(string name, Color accent, decimal tlCost, decimal slCost, bool match)
    {
        var g = new Grid
        {
            Padding = new Thickness(0, 3),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(65)),
                new ColumnDefinition(new GridLength(65)),
                new ColumnDefinition(new GridLength(22)),
            }
        };
        g.Children.Add(new Label { Text = name, FontSize = 12, FontAttributes = FontAttributes.Bold,
            TextColor = accent, VerticalOptions = LayoutOptions.Center });
        g.Children.Add(VLbl($"${tlCost:N2}", 12, "#BBBBBB", TextAlignment.End).WithCol(1));
        g.Children.Add(VLbl(slCost > 0 ? $"${slCost:N2}" : "—", 12, "#BBBBBB", TextAlignment.End).WithCol(2));
        g.Children.Add(VLbl(match ? "✓" : "✗", 13, match ? "#66BB6A" : "#EF5350", TextAlignment.Center).WithCol(3));
        return g;
    }

    static Label VLbl(string text, double size, string color, TextAlignment align) => new Label
    {
        Text = text, FontSize = size, TextColor = Color.FromArgb(color),
        HorizontalTextAlignment = align, VerticalOptions = LayoutOptions.Center
    };

    // Returns how many draws a Ticket Log entry covers.
    // D3: ALWAYS use DrawCount (draw# range); never fall back to date range.
    // Other games: prefer DrawCount, fall back to date range.
    static int DrawsFromEntry(TicketLogEntry entry)
    {
        if (entry.DrawCount > 0) return entry.DrawCount;
        if (entry.Game == "D3") return 1; // D3 must use draw numbers, not days
        if (string.IsNullOrEmpty(entry.PlayFrom)) return 1;
        var from = ParsePlayDate(entry.PlayFrom);
        if (from == null) return 1;
        var to = string.IsNullOrEmpty(entry.PlayTo) ? from : ParsePlayDate(entry.PlayTo);
        if (to == null) to = from;
        return Math.Max(1, (to.Value - from.Value).Days + 1);
    }

    static DateTime? ParsePlayDate(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTime.TryParseExact($"{s}/{DateTime.Today.Year}", "M/d/yyyy",
            null, System.Globalization.DateTimeStyles.None, out var dt))
            return dt;
        return null;
    }

    async void BtnBack_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..", false);
    }

    async void BtnHome_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//MainPage", false);
    }
}

// Column helper
file static class ColHelper
{
    public static T WithCol<T>(this T view, int col) where T : View
    {
        Grid.SetColumn(view, col);
        return view;
    }

    public static GraphicsView MakeBallView()
    {
        var gv = new GraphicsView
        {
            Drawable = new BallWatermark(),
            Opacity = 0.70,
            HeightRequest = 54, WidthRequest = 54,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };
        Grid.SetColumn(gv, 0);
        Grid.SetColumnSpan(gv, 2);
        return gv;
    }
}
