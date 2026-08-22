using System.Text.Json;
using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public class WinningRecord
{
    public string  Game        { get; set; } = "";
    public string  Date        { get; set; } = "";   // "yyyy-MM-dd"
    public string  Numbers     { get; set; } = "";
    public decimal Amount      { get; set; }
    public bool    IsFreeTicket{ get; set; }
    public string  Note        { get; set; } = "";   // e.g. "3/5", "Straight", "Exacta"
    public string  SourceKey   { get; set; } = "";   // "coll_F5_1_1_2026-06-24" — links to Results checkbox
}

public partial class SummaryPage : ContentPage
{
    public static bool NeedsRefresh { get; set; }
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
        // Was missing entirely, confirmed live 2026-08-09 — Hot Spot wins/spending are
        // correctly written to winnings_log.json/spending_log.json (verified directly via
        // file pull in an earlier session), but this page's own GameDefs never included
        // "HS", so the `foreach (var (key, name, color) in GameDefs)` loop below silently
        // skipped every Hot Spot section — a real win or spend here was invisible on this
        // page even though the underlying data was always correct. Same class of bug just
        // fixed on TicketLogPage.xaml.cs's separate GameDefs list.
        ("HS", "Hot Spot",      "#E65100"),
    ];

    static string DataPath    => Path.Combine(FileSystem.AppDataDirectory, "winnings_log.json");
    static string BlockedPath => Path.Combine(FileSystem.AppDataDirectory, "winnings_blocked.json");

    // Serializes every read-modify-write of winnings_log.json. AddWinAsync is called once per
    // qualifying winner from a foreach loop (sometimes 10+ in a single Results page refresh,
    // and the page auto-refreshes every ~11s) — without this lock, concurrent calls each load
    // the file, decide to append their own win, and save independently; whichever save runs
    // last wins and silently discards every other win added in between. Confirmed root cause
    // of a 2026-08-01 incident where only 4 of 14 real Fantasy 5 wins ($60 of $1,054) ever
    // made it into winnings_log.json / Wins & Spending / Ticket Calendar.
    static readonly SemaphoreSlim _fileLock = new(1, 1);

    List<WinningRecord>  _records         = new();
    List<SpendingRecord> _spendingRecords = new();
    bool _dailyLogEnabled = false;

    readonly Dictionary<string, bool> _ftExpanded    = new();
    readonly Dictionary<string, bool> _spndExpanded  = new();
    readonly Dictionary<string, bool> _gameCollapsed = new();

    bool _selectMode = false;
    readonly HashSet<WinningRecord> _selectedRecords = new();

    // ── Win Add/Edit overlay ──────────────────────────────────────────────────
    Grid?          _addOverlay;
    Label?         _addGameLabel;
    DatePicker?    _addDatePicker;
    Entry?         _addNumbersEntry;
    Entry?         _addAmountEntry;
    Switch?        _addFreeSwitch;
    Entry?         _addNoteEntry;
    string         _addGame    = "";
    WinningRecord? _editingRec = null;

    // ── Busy overlay (spinner shown while the page loads) — local Grid toggled via
    // Opacity, not a shared modal push (a global modal push/pop broke badly if anything
    // else on screen pushed its own modal in between).
    Grid?               _busyOverlay;
    ActivityIndicator?  _busySpinner;
    Label?              _busyStatusLabel;

    static readonly System.Diagnostics.Stopwatch _swSummary = System.Diagnostics.Stopwatch.StartNew();

    public SummaryPage()
    {
        System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG] {_swSummary.ElapsedMilliseconds}ms: ctor start");
        InitializeComponent();
        System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG] {_swSummary.ElapsedMilliseconds}ms: InitializeComponent done");
        BuildAddOverlay();
        System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG] {_swSummary.ElapsedMilliseconds}ms: BuildAddOverlay done");
        BuildBusyOverlay();
        System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG] {_swSummary.ElapsedMilliseconds}ms: ctor done");
        EnhanceModeService.SettingChanged += () =>
            MainThread.BeginInvokeOnMainThread(BuildUI);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG] {_swSummary.ElapsedMilliseconds}ms: OnAppearing start");
        _ = LoadAndBuild();
    }

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

    // ── Static helpers called from ResultsPage checkbox ───────────────────────

    // Returns true only when the win was actually newly added — false for a blocked/dedup
    // no-op. Added so callers whose win isn't discovered by the background win-check pipeline
    // (Hot Spot's manual Record Win — see HotSpotPage.cs) can tell a genuinely new win apart
    // from a re-tap on an already-recorded one, and only fire a push notification for the
    // former.
    public static async Task<bool> AddWinAsync(WinningRecord record)
    {
        await _fileLock.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(record.SourceKey))
            {
                var blocked = await LoadBlockedKeysAsync();
                if (blocked.Contains(record.SourceKey)) return false;
            }
            var records = await LoadAllAsync();
            if (!string.IsNullOrEmpty(record.SourceKey) &&
                records.Any(r => r.SourceKey == record.SourceKey)) return false;
            records.Add(record);
            await SaveAllAsync(records);
            return true;
        }
        catch (Exception ex)
        {
            try { await Logger.LogAsync($"AddWinAsync ERROR: {ex.GetType().Name}: {ex.Message} (SourceKey={record.SourceKey})"); } catch { }
            return false;
        }
        finally { _fileLock.Release(); }
    }

    public static async Task RemoveWinByKeyAsync(string sourceKey)
    {
        await _fileLock.WaitAsync();
        try
        {
            var records = await LoadAllAsync();
            if (records.RemoveAll(r => r.SourceKey == sourceKey) > 0)
            {
                await SaveAllAsync(records);
                if (!string.IsNullOrEmpty(sourceKey))
                {
                    var blocked = await LoadBlockedKeysAsync();
                    blocked.Add(sourceKey);
                    await SaveBlockedKeysAsync(blocked);
                }
            }
        }
        catch (Exception ex)
        {
            try { await Logger.LogAsync($"RemoveWinByKeyAsync ERROR: {ex.GetType().Name}: {ex.Message}"); } catch { }
        }
        finally { _fileLock.Release(); }
    }

    internal static async Task<List<WinningRecord>> LoadAllAsync()
    {
        try
        {
            if (!File.Exists(DataPath)) return new();
            string json = await File.ReadAllTextAsync(DataPath);
            return JsonSerializer.Deserialize<List<WinningRecord>>(json) ?? new();
        }
        catch { return new(); }
    }

    static async Task<HashSet<string>> LoadBlockedKeysAsync()
    {
        try
        {
            if (!File.Exists(BlockedPath)) return new();
            string json = await File.ReadAllTextAsync(BlockedPath);
            var list = JsonSerializer.Deserialize<List<string>>(json) ?? new();
            return new HashSet<string>(list);
        }
        catch { return new(); }
    }

    static async Task SaveBlockedKeysAsync(HashSet<string> keys)
    {
        try
        {
            string json = JsonSerializer.Serialize(keys.ToList(),
                new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(BlockedPath, json);
        }
        catch { }
    }

    internal static async Task SaveAllAsync(List<WinningRecord> records)
    {
        string json = JsonSerializer.Serialize(records,
            new JsonSerializerOptions { WriteIndented = false });
        await File.WriteAllTextAsync(DataPath, json);
    }

    // ── Data load / save ─────────────────────────────────────────────────────

    async Task LoadAndBuild()
    {
        System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG] {_swSummary.ElapsedMilliseconds}ms: LoadAndBuild start, calling ShowBusyAsync");
        await ShowBusyAsync("Loading summary...");
        System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG] {_swSummary.ElapsedMilliseconds}ms: busy shown, starting loads");
        await Task.WhenAll(LoadRecordsAsync(), LoadSpendingAsync());
        System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG] {_swSummary.ElapsedMilliseconds}ms: loads done ({_records.Count} records, {_spendingRecords.Count} spending), building UI");
        BuildUI();
        System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG] {_swSummary.ElapsedMilliseconds}ms: BuildUI done");
        await UpdateDailyLogStateAsync();
        System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG] {_swSummary.ElapsedMilliseconds}ms: UpdateDailyLogStateAsync done");
        HideBusy();
    }

    async Task UpdateDailyLogStateAsync()
    {
        string todayStr = DateTime.Today.ToString("yyyy-MM-dd");
        var tlAll = await TicketLogService.LoadAllAsync();
        bool hasTickets = tlAll.Any(x => x.Date == todayStr);

        _dailyLogEnabled = hasTickets;
    }

    async Task LoadRecordsAsync()
    {
        try
        {
            if (!File.Exists(DataPath)) { _records = new(); return; }
            string json = await File.ReadAllTextAsync(DataPath);
            _records = JsonSerializer.Deserialize<List<WinningRecord>>(json) ?? new();
        }
        catch { _records = new(); }
    }

    async Task LoadSpendingAsync()
    {
        _spendingRecords = await SpendingTracker.LoadAllAsync();
    }

    async Task SaveRecordsAsync()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = false };
            string json = JsonSerializer.Serialize(_records, options);
            await File.WriteAllTextAsync(DataPath, json);
        }
        catch { }
    }

    // ── Build UI ──────────────────────────────────────────────────────────────

    void BuildUI()
    {
        mainContainer.Children.Clear();
        decimal grandWon         = 0;
        int     totalFreeTickets = 0;
        decimal grandSpent       = SpendingTracker.TotalSpentAll(_spendingRecords);

        foreach (var (key, name, color) in GameDefs)
        {
            System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG]   {_swSummary.ElapsedMilliseconds}ms: game loop start {key}");
            var gameRecords = _records.Where(r => r.Game == key).ToList();
            decimal gameWon = gameRecords.Sum(r => r.Amount);
            int     gameFree= gameRecords.Count(r => r.IsFreeTicket);
            decimal gameSpent = SpendingTracker.TotalSpentForGame(_spendingRecords, key);
            grandWon         += gameWon;
            totalFreeTickets += gameFree;

            var accent = Color.FromArgb(color);

            // ── Section header ────────────────────────────────────────────────
            string capturedKey  = key;
            string capturedName = name;

            if (!_gameCollapsed.ContainsKey(key))
                _gameCollapsed[key] = Preferences.Get($"sum_game_{key}_collapsed", false);
            bool gameCollapsed = _gameCollapsed[key];

            var header = new Grid
            {
                BackgroundColor = accent,
                Padding = new Thickness(10, 10),
                MinimumHeightRequest = 44,
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),  // chevron
                    new ColumnDefinition(GridLength.Star),  // game name
                    new ColumnDefinition(GridLength.Auto),  // winnings badge
                    new ColumnDefinition(GridLength.Auto),  // + Win button
                }
            };

            var gameChevron = new Label
            {
                Text = gameCollapsed ? "▶" : "▼",
                FontSize = 13, FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            header.Children.Add(gameChevron);

            var nameLbl = new Label
            {
                Text = name,
                FontSize = 14, FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White, VerticalOptions = LayoutOptions.Center
            };
            Grid.SetColumn(nameLbl, 1);
            header.Children.Add(nameLbl);

            // Winnings badge (only if nonzero) — pill chip for contrast against saturated header
            if (gameWon > 0 || gameFree > 0)
            {
                string subText = gameWon > 0
                    ? $"${gameWon:N0}" + (gameFree > 0 ? $" +{gameFree}🎟" : "")
                    : $"{gameFree}🎟";
                var subBadge = new Border
                {
                    BackgroundColor = Color.FromArgb("#33FFFFFF"),
                    Stroke = new SolidColorBrush(Colors.Transparent),
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                    Padding = new Thickness(7, 3),
                    VerticalOptions = LayoutOptions.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                    Content = new Label
                    {
                        Text = subText,
                        FontSize = 11, FontAttributes = FontAttributes.Bold,
                        TextColor = Colors.White,
                        VerticalOptions = LayoutOptions.Center,
                    }
                };
                Grid.SetColumn(subBadge, 2);
                header.Children.Add(subBadge);
            }

            // + Add Win button
            var addLbl = new Label
            {
                Text = "+ Win",
                FontSize = 11, FontAttributes = FontAttributes.Bold,
                TextColor = accent,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
            };
            var addBtn = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = new SolidColorBrush(Colors.Transparent),
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(10, 5),
                MinimumHeightRequest = 30,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.End,
                Content = addLbl,
            };
            addBtn.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => ShowAddOverlay(capturedKey, capturedName))
            });
            Grid.SetColumn(addBtn, 3);
            header.Children.Add(addBtn);

            // ── Game body (collapsible) ───────────────────────────────────────
            var gameBody = new VerticalStackLayout { IsVisible = !gameCollapsed };

            var headerTap = new TapGestureRecognizer();
            headerTap.Tapped += (_, _) =>
            {
                _gameCollapsed[capturedKey] = !_gameCollapsed[capturedKey];
                Preferences.Set($"sum_game_{capturedKey}_collapsed", _gameCollapsed[capturedKey]);
                gameChevron.Text   = _gameCollapsed[capturedKey] ? "▶" : "▼";
                gameBody.IsVisible = !_gameCollapsed[capturedKey];
            };
            header.GestureRecognizers.Add(headerTap);

            System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG]     {_swSummary.ElapsedMilliseconds}ms: {key} before stats bar");
            // ── Stats bar: Spent / Won / Net — light neutral band, dark readable text ──
            if (gameSpent > 0 || gameWon > 0)
            {
                decimal gameNet  = gameWon - gameSpent;
                string  netSign  = gameNet >= 0 ? "+" : "";
                string  netColor = gameNet >= 0 ? "#2E7D32" : "#C62828";

                var statsBar = new Grid
                {
                    BackgroundColor = Color.FromArgb("#F4F6F8"),
                    Padding = new Thickness(10, 8),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Star),
                    }
                };
                statsBar.Children.Add(new Label
                {
                    Text = $"Spent: ${gameSpent:N2}",
                    FontSize = 12, TextColor = Color.FromArgb("#C62828"),
                    HorizontalTextAlignment = TextAlignment.Start,
                    VerticalOptions = LayoutOptions.Center
                });
                var wonLbl = new Label
                {
                    Text = $"Won: ${gameWon:N2}",
                    FontSize = 12, TextColor = Color.FromArgb("#2E7D32"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalOptions = LayoutOptions.Center
                };
                Grid.SetColumn(wonLbl, 1);
                statsBar.Children.Add(wonLbl);

                var netLbl = new Label
                {
                    Text = $"Net: {netSign}${gameNet:N2}",
                    FontSize = 12, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb(netColor),
                    HorizontalTextAlignment = TextAlignment.End,
                    VerticalOptions = LayoutOptions.Center
                };
                Grid.SetColumn(netLbl, 2);
                statsBar.Children.Add(netLbl);

                gameBody.Children.Add(statsBar);
            }

            System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG]     {_swSummary.ElapsedMilliseconds}ms: {key} before spending entries");
            // ── Spending entries (collapsible) — light tint, no more dark-navy band ──
            var gameSpending = _spendingRecords.Where(r => r.Game == key)
                                               .OrderByDescending(r => r.Date).ToList();
            if (gameSpending.Count > 0)
            {
                string capturedKey2 = key;
                if (!_spndExpanded.ContainsKey(capturedKey2)) _spndExpanded[capturedKey2] = Preferences.Get($"sum_spnd_{capturedKey2}", false);
                bool spndOpen = _spndExpanded[capturedKey2];

                var spndToggle = new Grid
                {
                    BackgroundColor = Color.FromArgb("#FDEDEA"),
                    Padding = new Thickness(10, 8),
                    MinimumHeightRequest = 40,
                };
                var spndChevron = new Label
                {
                    Text = spndOpen ? "▼" : "▶",
                    FontSize = 12, TextColor = Color.FromArgb("#C62828"),
                    VerticalOptions = LayoutOptions.Center
                };
                var spndLabel = new Label
                {
                    Text = $"  Spending Entries ({gameSpending.Count})  —  ${gameSpent:N2} total",
                    FontSize = 12, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#C62828"),
                    VerticalOptions = LayoutOptions.Center
                };
                spndToggle.Children.Add(new HorizontalStackLayout { Children = { spndChevron, spndLabel } });

                // Built lazily — only when actually expanded. This section defaults to
                // collapsed, but rows were previously built eagerly every time regardless of
                // visibility. D3 tickets get logged twice a day (Midday + Evening), so its
                // spending list badly outgrows every other game's; building it unconditionally
                // on every BuildUI() was the single biggest cost in this whole page.
                var spndContainer = new VerticalStackLayout { IsVisible = spndOpen };
                if (spndOpen)
                    foreach (var srec in gameSpending)
                        spndContainer.Children.Add(BuildSpendingRow(srec, accent));

                spndToggle.GestureRecognizers.Add(new TapGestureRecognizer
                {
                    Command = new Command(() =>
                    {
                        _spndExpanded[capturedKey2]  = !_spndExpanded[capturedKey2];
                        Preferences.Set($"sum_spnd_{capturedKey2}", _spndExpanded[capturedKey2]);
                        spndChevron.Text             = _spndExpanded[capturedKey2] ? "▼" : "▶";
                        spndContainer.IsVisible      = _spndExpanded[capturedKey2];
                        if (_spndExpanded[capturedKey2] && spndContainer.Children.Count == 0)
                            foreach (var srec in gameSpending)
                                spndContainer.Children.Add(BuildSpendingRow(srec, accent));
                    })
                });

                gameBody.Children.Add(spndToggle);
                gameBody.Children.Add(spndContainer);
            }

            System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG]     {_swSummary.ElapsedMilliseconds}ms: {key} before column labels ({gameSpending.Count} spending recs)");
            // ── Column labels (only if winning records exist) ─────────────────
            if (gameRecords.Count > 0)
            {
                var colHdr = new Grid
                {
                    BackgroundColor = Color.FromArgb("#F5F5F5"),
                    Padding = new Thickness(0, 6, 8, 6),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(new GridLength(4)),   // strip
                        new ColumnDefinition(new GridLength(66)),  // date
                        new ColumnDefinition(GridLength.Star),     // numbers
                        new ColumnDefinition(new GridLength(44)),  // match
                        new ColumnDefinition(new GridLength(56)),  // prize
                        new ColumnDefinition(new GridLength(34)),  // del
                    }
                };
                colHdr.Children.Add(ColHdrLabel("Date",    1, TextAlignment.Start, new Thickness(4, 0, 0, 0)));
                colHdr.Children.Add(ColHdrLabel("Numbers", 2, TextAlignment.Start, new Thickness(4, 0, 0, 0)));
                colHdr.Children.Add(ColHdrLabel("Match",   3, TextAlignment.Center));
                colHdr.Children.Add(ColHdrLabel("Prize",   4, TextAlignment.End));
                gameBody.Children.Add(colHdr);
            }

            System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG]     {_swSummary.ElapsedMilliseconds}ms: {key} before winning entry rows");
            // ── Winning entry rows ────────────────────────────────────────────
            var cashRecs = gameRecords.Where(r => !r.IsFreeTicket).ToList();
            var freeRecs = gameRecords.Where(r =>  r.IsFreeTicket).ToList();

            if (gameRecords.Count == 0)
            {
                gameBody.Children.Add(new Label
                {
                    Text = "  No winning entries — tap + Win to record one",
                    FontSize = 12, TextColor = Color.FromArgb("#9AA5B1"),
                    BackgroundColor = Colors.White,
                    Padding = new Thickness(10, 14)
                });
            }
            else
            {
                foreach (var rec in cashRecs)
                    gameBody.Children.Add(BuildRecordRow(rec, accent));

                if (freeRecs.Count > 0)
                {
                    string capturedKey3 = key;
                    if (!_ftExpanded.ContainsKey(capturedKey3)) _ftExpanded[capturedKey3] = Preferences.Get($"sum_ft_{capturedKey3}", false);
                    bool expanded = _ftExpanded[capturedKey3];

                    var ftToggleRow = new Grid
                    {
                        BackgroundColor = Color.FromArgb("#E3F2FD"),
                        Padding = new Thickness(10, 8),
                        MinimumHeightRequest = 40,
                    };
                    var ftChevron = new Label
                    {
                        Text = expanded ? "▼" : "▶",
                        FontSize = 12, TextColor = Color.FromArgb("#1565C0"),
                        VerticalOptions = LayoutOptions.Center
                    };
                    var ftLabel = new Label
                    {
                        Text = $"  Free Tickets ({freeRecs.Count})",
                        FontSize = 12, FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#1565C0"),
                        VerticalOptions = LayoutOptions.Center
                    };
                    ftToggleRow.Children.Add(new HorizontalStackLayout { Children = { ftChevron, ftLabel } });

                    // Also built lazily — same reasoning as the spending section above.
                    var ftContainer = new VerticalStackLayout { IsVisible = expanded };
                    if (expanded)
                        foreach (var rec in freeRecs)
                            ftContainer.Children.Add(BuildRecordRow(rec, accent));

                    ftToggleRow.GestureRecognizers.Add(new TapGestureRecognizer
                    {
                        Command = new Command(() =>
                        {
                            _ftExpanded[capturedKey3] = !_ftExpanded[capturedKey3];
                            Preferences.Set($"sum_ft_{capturedKey3}", _ftExpanded[capturedKey3]);
                            ftChevron.Text            = _ftExpanded[capturedKey3] ? "▼" : "▶";
                            ftContainer.IsVisible     = _ftExpanded[capturedKey3];
                            if (_ftExpanded[capturedKey3] && ftContainer.Children.Count == 0)
                                foreach (var rec in freeRecs)
                                    ftContainer.Children.Add(BuildRecordRow(rec, accent));
                        })
                    });

                    gameBody.Children.Add(ftToggleRow);
                    gameBody.Children.Add(ftContainer);
                }

                System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG]     {_swSummary.ElapsedMilliseconds}ms: {key} after cash+free rows, before subtotal");
                // Game subtotal row
                var subtotalRow = new Grid
                {
                    BackgroundColor = Color.FromArgb("#FAFAFA"),
                    Padding = new Thickness(10, 10),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto),
                    }
                };
                string subtotalDetail = gameWon > 0 && gameFree > 0
                    ? $"${gameWon:N0} cash + {gameFree} free ticket{(gameFree > 1 ? "s" : "")}"
                    : gameWon > 0 ? $"Subtotal: ${gameWon:N0}"
                    : $"Subtotal: {gameFree} free ticket{(gameFree > 1 ? "s" : "")}";
                subtotalRow.Children.Add(new Label
                {
                    Text = subtotalDetail,
                    FontSize = 13, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#444"),
                    VerticalOptions = LayoutOptions.Center,
                    Margin = new Thickness(4, 0)
                });
                if (gameWon > 0)
                {
                    var subAmt = new Label
                    {
                        Text = $"${gameWon:N0}",
                        FontSize = 14, FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#1B5E20"),
                        VerticalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, 0, 36, 0)
                    };
                    Grid.SetColumn(subAmt, 1);
                    subtotalRow.Children.Add(subAmt);
                }
                gameBody.Children.Add(subtotalRow);
            }

            // ── Wrap header + body into one elevated card ─────────────────────
            // No Shadow here — Border.Shadow triggers an expensive offscreen blur render
            // pass per instance on Android; with one card per game that alone was adding
            // seconds to BuildUI(). The Stroke still gives the card a visible edge.
            var cardContent = new VerticalStackLayout { Spacing = 0, Children = { header, gameBody } };
            var card = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = new SolidColorBrush(Color.FromArgb("#E1E5EA")),
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                Margin = new Thickness(10, 6),
                Content = cardContent,
            };
            mainContainer.Children.Add(card);
            System.Diagnostics.Debug.WriteLine($"[SUMMARY-DEBUG]   {_swSummary.ElapsedMilliseconds}ms: card added for {key} ({gameRecords.Count} win recs)");
        }

        // ── Update footer labels ──────────────────────────────────────────────
        decimal grandNet = grandWon - grandSpent;
        string  netSign2 = grandNet >= 0 ? "+" : "";

        string wonText = $"${grandWon:N2}";
        if (totalFreeTickets > 0) wonText += $" +{totalFreeTickets}🎟";
        lblTotalWon.Text   = wonText;
        lblTotalSpent.Text = $"${grandSpent:N2}";
        lblNetTotal.Text   = $"{netSign2}${grandNet:N2}";
        lblNetTotal.TextColor = grandNet >= 0
            ? Color.FromArgb("#66BB6A")
            : Color.FromArgb("#EF5350");
    }

    static Label ColHdrLabel(string text, int col, TextAlignment align = TextAlignment.Start,
                              Thickness margin = default)
    {
        var lbl = new Label
        {
            Text = text, FontSize = 10, TextColor = Color.FromArgb("#888"),
            VerticalOptions = LayoutOptions.Center, HorizontalTextAlignment = align,
            Margin = margin,
        };
        Grid.SetColumn(lbl, col);
        return lbl;
    }

    View BuildRecordRow(WinningRecord rec, Color accent)
    {
        bool   dateParsed = DateTime.TryParse(rec.Date, out var dt);
        string dateStr    = dateParsed ? dt.ToString("M/d/yy") : rec.Date;
        bool   enhanced   = EnhanceModeService.IsEnhanced(EnhanceModeService.SummaryPageKey);

        var row = new Grid
        {
            BackgroundColor = Colors.White,
            Padding         = new Thickness(0, 0, 10, 0),
            Margin          = new Thickness(0, 2),
            MinimumHeightRequest = 44,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(4)),   // accent strip
                new ColumnDefinition(new GridLength(66)),  // date
                new ColumnDefinition(GridLength.Star),     // numbers
                new ColumnDefinition(new GridLength(44)),  // match badge
                new ColumnDefinition(new GridLength(62)),  // prize
                new ColumnDefinition(new GridLength(36)),  // delete
            }
        };

        // Accent strip
        var strip = new BoxView
        {
            Color             = accent,
            VerticalOptions   = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill,
        };
        row.Children.Add(strip);

        // Date
        var dateLbl = new Label
        {
            Text            = dateStr,
            FontSize        = 11,
            TextColor       = Color.FromArgb("#555"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode   = LineBreakMode.NoWrap,
            MaxLines        = 1,
            Padding         = new Thickness(4, 10),
        };
        Grid.SetColumn(dateLbl, 1);
        row.Children.Add(dateLbl);

        // Numbers — renderer-driven
        string numsText  = string.IsNullOrEmpty(rec.Numbers) ? "—" : rec.Numbers;
        var    numsView  = enhanced
            ? SummaryRendererEnhanced.MakeNumbersView(numsText, accent, rec.Game)
            : SummaryRendererClassic .MakeNumbersView(numsText, accent, rec.Game);
        numsView.VerticalOptions  = LayoutOptions.Center;
        numsView.Margin           = new Thickness(4, 6);
        Grid.SetColumn(numsView, 2);
        row.Children.Add(numsView);

        // Match/Note badge — plain Grid instead of Border+RoundRectangle: shaped Borders are
        // expensive per-instance on Android (each needs its own Skia-drawn native view), and
        // at dozens of rows that alone added seconds to BuildUI().
        if (!string.IsNullOrEmpty(rec.Note))
        {
            var noteBadge = new Grid
            {
                BackgroundColor   = Color.FromArgb("#FFF3E0"),
                Padding           = new Thickness(5, 3),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text                    = rec.Note,
                        FontSize                = 10,
                        FontAttributes          = FontAttributes.Bold,
                        TextColor               = Color.FromArgb("#E65100"),
                        HorizontalOptions       = LayoutOptions.Center,
                        VerticalOptions         = LayoutOptions.Center,
                        HorizontalTextAlignment = TextAlignment.Center,
                        LineBreakMode           = LineBreakMode.NoWrap,
                    }
                }
            };
            Grid.SetColumn(noteBadge, 3);
            row.Children.Add(noteBadge);
        }

        // Prize amount — cash as bold text, free tickets as a pill chip (matches Match badge language)
        if (rec.IsFreeTicket)
        {
            var freeBadge = new Grid
            {
                BackgroundColor   = Color.FromArgb("#E3F2FD"),
                Padding           = new Thickness(6, 3),
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions   = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text            = "🎟 Free",
                        FontSize        = 11,
                        FontAttributes  = FontAttributes.Bold,
                        TextColor       = Color.FromArgb("#1565C0"),
                        LineBreakMode   = LineBreakMode.NoWrap,
                    }
                }
            };
            Grid.SetColumn(freeBadge, 4);
            row.Children.Add(freeBadge);
        }
        else
        {
            var amtLbl = new Label
            {
                Text                    = rec.Amount > 0 ? $"${rec.Amount:N0}" : "—",
                FontSize                = 12,
                FontAttributes          = FontAttributes.Bold,
                TextColor               = Color.FromArgb("#1B5E20"),
                VerticalOptions         = LayoutOptions.Center,
                LineBreakMode           = LineBreakMode.NoWrap,
                HorizontalTextAlignment = TextAlignment.End,
            };
            Grid.SetColumn(amtLbl, 4);
            row.Children.Add(amtLbl);
        }

        var capturedRec = rec;

        if (_selectMode)
        {
            // ── Selection checkbox — swapped in for the delete button while _selectMode is on.
            // Toggling mutates this row's own visuals in place instead of calling BuildUI(),
            // since a full page rebuild per checkbox tap would be sluggish when the whole point
            // is fast multi-select during testing.
            bool isSelected = _selectedRecords.Contains(capturedRec);
            row.BackgroundColor = isSelected ? Color.FromArgb("#E3F2FD") : Colors.White;

            var checkLabel = new Label
            {
                Text              = isSelected ? "✓" : "",
                FontSize          = 13,
                FontAttributes    = FontAttributes.Bold,
                TextColor         = Colors.White,
                VerticalOptions   = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
            };
            var checkBox = new Border
            {
                BackgroundColor   = isSelected ? Color.FromArgb("#1976D2") : Colors.Transparent,
                Stroke            = new SolidColorBrush(Color.FromArgb("#90A4AE")),
                StrokeThickness   = 1.5,
                StrokeShape       = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 },
                WidthRequest      = 22,
                HeightRequest     = 22,
                Padding           = 0,
                VerticalOptions   = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                Content           = checkLabel,
            };
            Grid.SetColumn(checkBox, 5);
            row.Children.Add(checkBox);

            // Tapping the date selects/deselects every entry (any game) sharing that exact
            // date in one go — the fast path for clearing out a whole day of test data instead
            // of checking rows one at a time.
            dateLbl.TextColor      = Color.FromArgb("#1976D2");
            dateLbl.FontAttributes = FontAttributes.Bold;
            dateLbl.TextDecorations = TextDecorations.Underline;
            var dateGesture = new TapGestureRecognizer();
            dateGesture.Tapped += (_, _) =>
            {
                var sameDate = _records.Where(r => r.Date == capturedRec.Date).ToList();
                bool allSelected = sameDate.All(r => _selectedRecords.Contains(r));
                foreach (var r in sameDate)
                {
                    if (allSelected) _selectedRecords.Remove(r);
                    else             _selectedRecords.Add(r);
                }
                UpdateSelectCount();
                BuildUI();
            };
            dateLbl.GestureRecognizers.Add(dateGesture);

            row.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    bool nowSelected = !_selectedRecords.Contains(capturedRec);
                    if (nowSelected) _selectedRecords.Add(capturedRec);
                    else             _selectedRecords.Remove(capturedRec);

                    row.BackgroundColor    = nowSelected ? Color.FromArgb("#E3F2FD") : Colors.White;
                    checkBox.BackgroundColor = nowSelected ? Color.FromArgb("#1976D2") : Colors.Transparent;
                    checkLabel.Text        = nowSelected ? "✓" : "";
                    UpdateSelectCount();
                })
            });

            return row;
        }

        // Tap anywhere on the row (not the ✕ button) to edit
        row.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => ShowEditOverlay(capturedRec))
        });

        // Delete button — plain Grid instead of Border+RoundRectangle (see BuildRecordRow comment above)
        var delLabel = new Label
        {
            Text              = "✕",
            FontSize          = 12,
            FontAttributes    = FontAttributes.Bold,
            TextColor         = Colors.White,
            VerticalOptions   = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
        };
        var delBorder = new Grid
        {
            BackgroundColor   = Color.FromArgb("#C62828"),
            Padding           = new Thickness(6, 4),
            VerticalOptions   = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Children = { delLabel }
        };
        // User's explicit ask 2026-08-17: confirmed live that removing an entry (blocked-keys
        // load/save + records save, all real file I/O) has no visible feedback between the tap
        // and BuildUI() finishing — read as "did this even work?" on a slower save. Swap the ✕
        // to a spinner glyph and block a repeat tap for the duration; no need to restore either
        // afterward since BuildUI() throws this whole row away regardless of outcome.
        bool deleteBusy = false;
        delBorder.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                if (deleteBusy) return;
                bool ok = await DisplayAlert("Remove Entry", "Remove this entry?", "Yes", "Cancel");
                if (!ok) return;
                deleteBusy = true;
                delLabel.Text = "⏳";
                if (!string.IsNullOrEmpty(capturedRec.SourceKey))
                {
                    var blocked = await LoadBlockedKeysAsync();
                    blocked.Add(capturedRec.SourceKey);
                    await SaveBlockedKeysAsync(blocked);
                }
                _records.Remove(capturedRec);
                await SaveRecordsAsync();
                BuildUI();
            })
        });
        Grid.SetColumn(delBorder, 5);
        row.Children.Add(delBorder);

        return row;
    }

    View BuildSpendingRow(SpendingRecord srec, Color accent)
    {
        bool   dateParsed = DateTime.TryParse(srec.Date, out var dt);
        string dateStr    = dateParsed ? dt.ToString("M/d/yy") : srec.Date;

        var row = new Grid
        {
            BackgroundColor = Colors.White,
            Padding = new Thickness(8, 8),
            Margin = new Thickness(0, 1),
            MinimumHeightRequest = 40,
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(64)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(60)),
                new ColumnDefinition(new GridLength(32)),
            }
        };

        var strip = new BoxView { Color = accent, WidthRequest = 3,
            VerticalOptions = LayoutOptions.Fill, HorizontalOptions = LayoutOptions.Start };
        row.Children.Add(strip);

        row.Children.Add(new Label
        {
            Text = dateStr, FontSize = 11, TextColor = Color.FromArgb("#666666"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.NoWrap,
            MaxLines = 1,
            Margin = new Thickness(7, 0, 0, 0)
        });

        string detail = string.IsNullOrEmpty(srec.Note)
            ? $"{srec.TicketCount} ticket{(srec.TicketCount != 1 ? "s" : "")} × ${srec.CostEach:N2}"
            : $"{srec.TicketCount}t × ${srec.CostEach:N2}  {srec.Note}";
        var detailLbl = new Label
        {
            Text = detail, FontSize = 11, TextColor = Color.FromArgb("#555555"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        Grid.SetColumn(detailLbl, 1);
        row.Children.Add(detailLbl);

        var totalLbl = new Label
        {
            Text = $"${srec.TotalCost:N2}", FontSize = 12, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#C62828"),
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.End
        };
        Grid.SetColumn(totalLbl, 2);
        row.Children.Add(totalLbl);

        var capturedSrec = srec;
        var delBorder = new Grid
        {
            BackgroundColor = Color.FromArgb("#C62828"),
            Padding = new Thickness(6, 4),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "✕", FontSize = 11, FontAttributes = FontAttributes.Bold,
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
                bool ok = await DisplayAlert("Remove Spending",
                    $"Remove {capturedSrec.TicketCount} ticket(s) on {dateStr}?", "Yes", "Cancel");
                if (!ok) return;
                _spendingRecords.Remove(capturedSrec);
                await SpendingTracker.SaveAllAsync(_spendingRecords);
                BuildUI();
            })
        });
        Grid.SetColumn(delBorder, 3);
        row.Children.Add(delBorder);

        return row;
    }

    // ── Win Add Overlay ───────────────────────────────────────────────────────

    void BuildAddOverlay()
    {
        _addGameLabel = new Label
        {
            FontSize = 16, FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center
        };
        _addDatePicker = new DatePicker
        {
            Format = "MMM d, yyyy", FontSize = 14, Date = DateTime.Today,
            MinimumDate = new DateTime(2020, 1, 1), MaximumDate = new DateTime(2035, 12, 31),
            TextColor = Colors.White,
        };
        _addNumbersEntry = new Entry
        {
            Placeholder = "Numbers played (optional)",
            PlaceholderColor = Color.FromArgb("#8B9DC3"),
            TextColor = Colors.White, FontSize = 13,
            BackgroundColor = Color.FromArgb("#2D3E55"),
        };
        _addAmountEntry = new Entry
        {
            Placeholder = "Prize amount (e.g. 25)",
            PlaceholderColor = Color.FromArgb("#8B9DC3"),
            TextColor = Colors.White, FontSize = 13,
            Keyboard = Keyboard.Numeric,
            BackgroundColor = Color.FromArgb("#2D3E55"),
        };
        _addFreeSwitch = new Switch { OnColor = Color.FromArgb("#43A047"), ThumbColor = Colors.White };
        _addNoteEntry = new Entry
        {
            Placeholder = "Match label (e.g. 3/5, Straight)",
            PlaceholderColor = Color.FromArgb("#8B9DC3"),
            TextColor = Colors.White, FontSize = 13,
            BackgroundColor = Color.FromArgb("#2D3E55"),
        };

        _addFreeSwitch.Toggled += (_, e) =>
        {
            _addAmountEntry!.IsEnabled  = !e.Value;
            _addAmountEntry.Placeholder = e.Value ? "N/A (Free Ticket)" : "Prize amount (e.g. 25)";
        };

        var btnCancel = new Button { Text = "Cancel", BackgroundColor = Color.FromArgb("#4B5563"),
            TextColor = Colors.White, CornerRadius = 10, HeightRequest = 44, FontSize = 14 };
        var btnSave   = new Button { Text = "Save",   BackgroundColor = Color.FromArgb("#2563EB"),
            TextColor = Colors.White, CornerRadius = 10, HeightRequest = 44, FontSize = 14,
            FontAttributes = FontAttributes.Bold };

        btnCancel.Clicked += (_, _) => _addOverlay!.IsVisible = false;
        btnSave.Clicked   += OnSaveEntry;

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
                    _addGameLabel,
                    new Label { Text = "Draw Date", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                    _addDatePicker,
                    new Label { Text = "Numbers Played", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                    _addNumbersEntry,
                    new Label { Text = "Match / Note (e.g. 3/5, Straight)", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                    _addNoteEntry,
                    new Label { Text = "Prize Amount ($)", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") },
                    _addAmountEntry,
                    new Grid
                    {
                        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                        VerticalOptions = LayoutOptions.Center,
                        Children =
                        {
                            new Label { Text = "Free Ticket?", FontSize = 13, TextColor = Color.FromArgb("#A0AEC0"),
                                VerticalOptions = LayoutOptions.Center },
                            new ContentView { Content = _addFreeSwitch, HorizontalOptions = LayoutOptions.End }
                                .WithColumn(1),
                        }
                    },
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

    void ShowAddOverlay(string gameKey, string gameName)
    {
        _editingRec = null;
        _addGame = gameKey;
        _addGameLabel!.Text = $"Add Winning — {gameName}";
        var def = GameDefs.FirstOrDefault(g => g.Key == gameKey);
        _addGameLabel.TextColor = string.IsNullOrEmpty(def.Color)
            ? Colors.White : Color.FromArgb(def.Color);

        _addDatePicker!.Date      = DateTime.Today;
        _addNumbersEntry!.Text    = "";
        _addAmountEntry!.Text     = "";
        _addNoteEntry!.Text       = "";
        _addFreeSwitch!.IsToggled = false;
        _addAmountEntry.IsEnabled = true;
        _addOverlay!.IsVisible    = true;
    }

    void ShowEditOverlay(WinningRecord rec)
    {
        _editingRec = rec;
        _addGame    = rec.Game;
        var def = GameDefs.FirstOrDefault(g => g.Key == rec.Game);
        _addGameLabel!.Text = $"Edit Winning — {(string.IsNullOrEmpty(def.Name) ? rec.Game : def.Name)}";
        _addGameLabel.TextColor = string.IsNullOrEmpty(def.Color)
            ? Colors.White : Color.FromArgb(def.Color);

        if (DateTime.TryParse(rec.Date, out var dt))
            _addDatePicker!.Date = dt;
        _addNumbersEntry!.Text    = rec.Numbers;
        _addAmountEntry!.Text     = rec.IsFreeTicket ? "" : (rec.Amount > 0 ? rec.Amount.ToString("0.##") : "");
        _addNoteEntry!.Text       = rec.Note;
        _addFreeSwitch!.IsToggled = rec.IsFreeTicket;
        _addAmountEntry.IsEnabled = !rec.IsFreeTicket;
        _addOverlay!.IsVisible    = true;
    }

    async void OnSaveEntry(object? sender, EventArgs e)
    {
        string amtText = _addAmountEntry!.Text?.Trim() ?? "";
        bool   isFree  = _addFreeSwitch!.IsToggled;
        decimal amount = 0;

        if (!isFree && !string.IsNullOrEmpty(amtText))
        {
            if (!decimal.TryParse(amtText, out amount) || amount < 0)
            {
                await DisplayAlert("Invalid Amount", "Enter a valid prize amount (numbers only).", "OK");
                return;
            }
        }

        var rec = new WinningRecord
        {
            Game         = _addGame,
            Date         = $"{_addDatePicker!.Date:yyyy-MM-dd}",
            Numbers      = _addNumbersEntry!.Text?.Trim() ?? "",
            Amount       = isFree ? 0 : amount,
            IsFreeTicket = isFree,
            Note         = _addNoteEntry!.Text?.Trim() ?? "",
        };

        if (_editingRec != null)
        {
            int idx = _records.IndexOf(_editingRec);
            if (idx >= 0)
                _records[idx] = rec;
            else
                _records.Add(rec);
            _editingRec = null;
        }
        else
        {
            _records.Add(rec);
        }
        await SaveRecordsAsync();
        _addOverlay!.IsVisible = false;
        BuildUI();
    }

    // ── Multi-select delete ──────────────────────────────────────────────────

    async void BtnSelectMode_Clicked(object sender, EventArgs e)
    {
        _selectMode = true;
        _selectedRecords.Clear();
        normalButtonsStack.IsVisible = false;
        selectModeStack.IsVisible    = true;
        UpdateSelectCount();
        // BuildUI() rebuilds every card on the page (headers, stats bars, spending rows,
        // etc.), not just the checkbox column — that full rebuild is the page's known slow
        // path (also hit on load), so show the spinner rather than let the screen sit frozen.
        await ShowBusyAsync("Loading...");
        BuildUI();
        HideBusy();
    }

    async void BtnCancelSelect_Clicked(object sender, EventArgs e)
    {
        _selectMode = false;
        _selectedRecords.Clear();
        normalButtonsStack.IsVisible = true;
        selectModeStack.IsVisible    = false;
        await ShowBusyAsync("Loading...");
        BuildUI();
        HideBusy();
    }

    void UpdateSelectCount()
    {
        deleteSelectedLabel.Text = $"Delete ({_selectedRecords.Count})";
    }

    async void BtnDeleteSelected_Clicked(object sender, EventArgs e)
    {
        if (_selectedRecords.Count == 0) return;
        bool ok = await DisplayAlert("Remove Entries",
            $"Remove {_selectedRecords.Count} selected entr{(_selectedRecords.Count == 1 ? "y" : "ies")}?",
            "Yes", "Cancel");
        if (!ok) return;

        await ShowBusyAsync($"Deleting {_selectedRecords.Count} entries...");
        try
        {
            var blocked = await LoadBlockedKeysAsync();
            foreach (var rec in _selectedRecords)
                if (!string.IsNullOrEmpty(rec.SourceKey))
                    blocked.Add(rec.SourceKey);
            await SaveBlockedKeysAsync(blocked);

            _records.RemoveAll(r => _selectedRecords.Contains(r));
            await SaveRecordsAsync();

            _selectMode = false;
            _selectedRecords.Clear();
            normalButtonsStack.IsVisible = true;
            selectModeStack.IsVisible    = false;
            BuildUI();
        }
        finally { HideBusy(); }
    }

    // ── Buttons ───────────────────────────────────────────────────────────────

    async void BtnMenu_Clicked(object sender, EventArgs e)
    {
        string dailyLogOption = _dailyLogEnabled ? "Daily Log" : "Daily Log (no tickets today)";
        string choice = await DisplayActionSheet("Wins & Spending", "Cancel", null,
            dailyLogOption, "Select", "Delete by Date", "Clear All");

        if (choice == dailyLogOption) BtnSpendingLog_Clicked(sender, e);
        else if (choice == "Select")   BtnSelectMode_Clicked(sender, e);
        else if (choice == "Delete by Date") await BtnDeleteByDateAsync();
        else if (choice == "Clear All") BtnClearAll_Clicked(sender, e);
    }

    // User's explicit ask 2026-08-20: too many test Hot Spot entries mixed in with other
    // games on the same dates — needed to narrow "Delete by Date" to one game instead of
    // wiping every game on that date. First asks which game (or "All Games" for the original
    // 2026-08-19 behavior below), then hands off accordingly.
    async Task BtnDeleteByDateAsync()
    {
        var gamesWithData = GameDefs
            .Where(g => _records.Any(r => r.Game == g.Key) || _spendingRecords.Any(s => s.Game == g.Key))
            .ToList();
        if (gamesWithData.Count == 0)
        {
            await DisplayAlert("Delete by Date", "No win or spending entries to delete.", "OK");
            return;
        }

        const string allGames = "All Games";
        var gameOptions = new[] { allGames }.Concat(gamesWithData.Select(g => g.Name)).ToArray();
        string gameChoice = await DisplayActionSheet("Delete by Date — which game?", "Cancel", null, gameOptions);
        if (string.IsNullOrEmpty(gameChoice) || gameChoice == "Cancel") return;

        if (gameChoice == allGames)
        {
            await DeleteByDateAllGamesAsync();
            return;
        }

        string gameKey = gamesWithData.First(g => g.Name == gameChoice).Key;
        await DeleteByDateForGameAsync(gameKey, gameChoice);
    }

    // Original 2026-08-19 behavior, unchanged — wins only, not spending, across every game.
    // Lists every distinct date that has at least one win entry (most recent first, with a
    // count), picks one via ActionSheet, confirms once, then removes every win entry on that
    // date in one go — same blocked-keys mechanism Select-mode delete and Clear All already
    // use (see BtnDeleteSelected_Clicked), so a deleted win's SourceKey can never get silently
    // re-added by the background auto-recorder.
    async Task DeleteByDateAllGamesAsync()
    {
        var dates = _records
            .Select(r => r.Date)
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();
        if (dates.Count == 0)
        {
            await DisplayAlert("Delete by Date", "No win entries to delete.", "OK");
            return;
        }

        var optionLabels = dates.Select(d =>
        {
            int count = _records.Count(r => r.Date == d);
            string label = DateTime.TryParse(d, out var dt) ? dt.ToString("M/d/yy") : d;
            return $"{label} ({count} win{(count == 1 ? "" : "s")})";
        }).ToArray();

        string choice = await DisplayActionSheet("Delete which date?", "Cancel", null, optionLabels);
        if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;

        int pickedIndex = Array.IndexOf(optionLabels, choice);
        if (pickedIndex < 0) return;
        string pickedDate = dates[pickedIndex];
        int pickedCount = _records.Count(r => r.Date == pickedDate);

        bool ok = await DisplayAlert("Delete by Date",
            $"Remove all {pickedCount} win entr{(pickedCount == 1 ? "y" : "ies")} from {optionLabels[pickedIndex].Split(' ')[0]}? This cannot be undone.",
            "Delete", "Cancel");
        if (!ok) return;

        await ShowBusyAsync($"Deleting {pickedCount} entries...");
        try
        {
            var toRemove = _records.Where(r => r.Date == pickedDate).ToList();
            var blocked = await LoadBlockedKeysAsync();
            foreach (var rec in toRemove)
                if (!string.IsNullOrEmpty(rec.SourceKey))
                    blocked.Add(rec.SourceKey);
            await SaveBlockedKeysAsync(blocked);

            _records.RemoveAll(r => r.Date == pickedDate);
            await SaveRecordsAsync();
            BuildUI();
        }
        finally { HideBusy(); }
    }

    // User's explicit ask 2026-08-20: a single game's "Delete by Date" removes BOTH its win
    // entries and its spending entries for that date together (unlike DeleteByDateAllGamesAsync
    // above, which stays wins-only per the earlier 2026-08-19 ask). Dates offered are the union
    // of dates that have either kind of entry for this game.
    async Task DeleteByDateForGameAsync(string gameKey, string gameName)
    {
        var dates = _records.Where(r => r.Game == gameKey).Select(r => r.Date)
            .Concat(_spendingRecords.Where(s => s.Game == gameKey).Select(s => s.Date))
            .Distinct()
            .OrderByDescending(d => d)
            .ToList();
        if (dates.Count == 0)
        {
            await DisplayAlert("Delete by Date", $"No {gameName} entries to delete.", "OK");
            return;
        }

        var optionLabels = dates.Select(d =>
        {
            int winCount = _records.Count(r => r.Game == gameKey && r.Date == d);
            int spendCount = _spendingRecords.Count(s => s.Game == gameKey && s.Date == d);
            string label = DateTime.TryParse(d, out var dt) ? dt.ToString("M/d/yy") : d;
            var parts = new List<string>();
            if (winCount > 0) parts.Add($"{winCount} win{(winCount == 1 ? "" : "s")}");
            if (spendCount > 0) parts.Add($"{spendCount} spending");
            return $"{label} ({string.Join(", ", parts)})";
        }).ToArray();

        string choice = await DisplayActionSheet($"Delete which date? ({gameName})", "Cancel", null, optionLabels);
        if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;

        int pickedIndex = Array.IndexOf(optionLabels, choice);
        if (pickedIndex < 0) return;
        string pickedDate = dates[pickedIndex];

        var winsToRemove = _records.Where(r => r.Game == gameKey && r.Date == pickedDate).ToList();
        var spendToRemove = _spendingRecords.Where(s => s.Game == gameKey && s.Date == pickedDate).ToList();

        bool ok = await DisplayAlert("Delete by Date",
            $"Remove all {gameName} entries from {optionLabels[pickedIndex].Split(' ')[0]}? " +
            $"({winsToRemove.Count} win{(winsToRemove.Count == 1 ? "" : "s")}, {spendToRemove.Count} spending) This cannot be undone.",
            "Delete", "Cancel");
        if (!ok) return;

        await ShowBusyAsync($"Deleting {winsToRemove.Count + spendToRemove.Count} entries...");
        try
        {
            if (winsToRemove.Count > 0)
            {
                var blocked = await LoadBlockedKeysAsync();
                foreach (var rec in winsToRemove)
                    if (!string.IsNullOrEmpty(rec.SourceKey))
                        blocked.Add(rec.SourceKey);
                await SaveBlockedKeysAsync(blocked);

                _records.RemoveAll(r => r.Game == gameKey && r.Date == pickedDate);
                await SaveRecordsAsync();
            }

            if (spendToRemove.Count > 0)
            {
                _spendingRecords.RemoveAll(s => s.Game == gameKey && s.Date == pickedDate);
                await SpendingTracker.SaveAllAsync(_spendingRecords);
            }

            BuildUI();
        }
        finally { HideBusy(); }
    }

    async void BtnSpendingLog_Clicked(object sender, EventArgs e)
    {
        if (!_dailyLogEnabled) return;
        await Shell.Current.GoToAsync(nameof(SpendingLogPage), false);
    }

    private async void BtnBack_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..", false);
    }

    private async void BtnClearAll_Clicked(object sender, EventArgs e)
    {
        if (_records.Count == 0 && _spendingRecords.Count == 0) return;
        bool ok = await DisplayAlert("Clear All",
            "Remove all winnings AND spending entries?", "Yes", "Cancel");
        if (!ok) return;
        bool ok2 = await DisplayAlert("Are You Sure?",
            "This cannot be undone. All wins and spending history will be permanently deleted.", "DELETE ALL", "Cancel");
        if (!ok2) return;
        var blocked = await LoadBlockedKeysAsync();
        foreach (var rec in _records)
            if (!string.IsNullOrEmpty(rec.SourceKey))
                blocked.Add(rec.SourceKey);
        await SaveBlockedKeysAsync(blocked);
        _records.Clear();
        _spendingRecords.Clear();
        await Task.WhenAll(SaveRecordsAsync(), SpendingTracker.SaveAllAsync(_spendingRecords));
        BuildUI();
    }
}

// Helper extension to set Grid column inline
file static class ViewExtensions
{
    public static T WithColumn<T>(this T view, int col) where T : View
    {
        Grid.SetColumn(view, col);
        return view;
    }
}
