using System.Text.Json;

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
    static readonly (string Key, string Name, string Color)[] GameDefs =
    [
        ("F5", "Fantasy 5",     "#FF8F00"),
        ("SL", "Super Lotto",   "#7B1FA2"),
        ("PB", "Powerball",     "#C62828"),
        ("MM", "Mega Millions", "#F57F17"),
        ("D3", "Daily 3",       "#1565C0"),
        ("D4", "Daily 4",       "#00695C"),
        ("DD", "Daily Derby",   "#5D4037"),
    ];

    static string DataPath => Path.Combine(FileSystem.AppDataDirectory, "winnings_log.json");

    List<WinningRecord> _records = new();
    readonly Dictionary<string, bool> _ftExpanded = new();

    // Add/Edit overlay
    Grid?       _addOverlay;
    Label?      _addGameLabel;
    DatePicker? _addDatePicker;
    Entry?      _addNumbersEntry;
    Entry?      _addAmountEntry;
    Switch?     _addFreeSwitch;
    Entry?      _addNoteEntry;
    string      _addGame = "";

    public SummaryPage()
    {
        InitializeComponent();
        BuildAddOverlay();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadAndBuild();
    }

    // ── Static helpers called from ResultsPage checkbox ───────────────────────

    public static async Task AddWinAsync(WinningRecord record)
    {
        try
        {
            var records = await LoadAllAsync();
            if (!string.IsNullOrEmpty(record.SourceKey) &&
                records.Any(r => r.SourceKey == record.SourceKey)) return;
            records.Add(record);
            await SaveAllAsync(records);
        }
        catch { }
    }

    public static async Task RemoveWinByKeyAsync(string sourceKey)
    {
        try
        {
            var records = await LoadAllAsync();
            if (records.RemoveAll(r => r.SourceKey == sourceKey) > 0)
                await SaveAllAsync(records);
        }
        catch { }
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

    static async Task SaveAllAsync(List<WinningRecord> records)
    {
        string json = JsonSerializer.Serialize(records,
            new JsonSerializerOptions { WriteIndented = false });
        await File.WriteAllTextAsync(DataPath, json);
    }

    // ── Data load / save ─────────────────────────────────────────────────────

    async Task LoadAndBuild()
    {
        await LoadRecordsAsync();
        BuildUI();
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
        decimal grandTotal      = 0;
        int     totalFreeTickets = 0;

        foreach (var (key, name, color) in GameDefs)
        {
            var gameRecords = _records.Where(r => r.Game == key).ToList();
            decimal gameTotal = gameRecords.Sum(r => r.Amount);
            int     gameFree  = gameRecords.Count(r => r.IsFreeTicket);
            grandTotal       += gameTotal;
            totalFreeTickets += gameFree;

            var accent = Color.FromArgb(color);

            // ── Section header ────────────────────────────────────────────────
            var header = new Grid
            {
                BackgroundColor = accent,
                Padding = new Thickness(12, 8),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                }
            };

            // Game name caption
            header.Children.Add(new Label
            {
                Text = name,
                FontSize = 14, FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White, VerticalOptions = LayoutOptions.Center
            });

            // Subtotal
            string subText = gameTotal > 0
                ? $"${gameTotal:N0}" + (gameFree > 0 ? $"  +{gameFree} free" : "")
                : gameFree > 0 ? $"{gameFree} free ticket{(gameFree > 1 ? "s" : "")}"
                : "No entries";
            var subLbl = new Label
            {
                Text = subText,
                FontSize = 12, FontAttributes = FontAttributes.Bold,
                TextColor = (gameTotal > 0 || gameFree > 0) ? Color.FromArgb("#FFFF66") : Color.FromArgb("#BBBBBB"),
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(subLbl, 1);
            header.Children.Add(subLbl);

            // + Add button
            string capturedKey  = key;
            string capturedName = name;
            var addBtn = new Button
            {
                Text = "+ Add",
                FontSize = 11, FontAttributes = FontAttributes.Bold,
                BackgroundColor = Colors.White,
                TextColor = accent,
                CornerRadius = 10,
                HeightRequest = 30, Padding = new Thickness(10, 0),
            };
            addBtn.Clicked += (_, _) => ShowAddOverlay(capturedKey, capturedName);
            Grid.SetColumn(addBtn, 2);
            header.Children.Add(addBtn);

            mainContainer.Children.Add(header);

            // ── Column labels (only if there are records) ─────────────────────
            if (gameRecords.Count > 0)
            {
                var colHdr = new Grid
                {
                    BackgroundColor = Color.FromArgb("#F5F5F5"),
                    Padding = new Thickness(8, 3),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(new GridLength(58)),
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(new GridLength(44)),
                        new ColumnDefinition(new GridLength(60)),
                        new ColumnDefinition(new GridLength(34)),
                    }
                };
                colHdr.Children.Add(ColHdrLabel("Date",    0));
                colHdr.Children.Add(ColHdrLabel("Numbers", 1));
                colHdr.Children.Add(ColHdrLabel("Match",   2, TextAlignment.Center));
                colHdr.Children.Add(ColHdrLabel("Prize",   3, TextAlignment.End));
                mainContainer.Children.Add(colHdr);
            }

            // ── Entry rows ────────────────────────────────────────────────────
            var cashRecs = gameRecords.Where(r => !r.IsFreeTicket).ToList();
            var freeRecs = gameRecords.Where(r =>  r.IsFreeTicket).ToList();

            if (gameRecords.Count == 0)
            {
                mainContainer.Children.Add(new Label
                {
                    Text = "  No entries — tap + Add to record a win",
                    FontSize = 12, TextColor = Color.FromArgb("#AAA"),
                    BackgroundColor = Colors.White,
                    Padding = new Thickness(12, 10)
                });
            }
            else
            {
                // Cash wins — shown individually as before
                foreach (var rec in cashRecs)
                    mainContainer.Children.Add(BuildRecordRow(rec, accent));

                // Free tickets — collapsed into a toggle button
                if (freeRecs.Count > 0)
                {
                    string capturedKey2 = key;
                    if (!_ftExpanded.ContainsKey(capturedKey2)) _ftExpanded[capturedKey2] = false;
                    bool expanded = _ftExpanded[capturedKey2];

                    // Toggle button row
                    var ftToggleRow = new Grid
                    {
                        BackgroundColor = Color.FromArgb("#E3F2FD"),
                        Padding = new Thickness(10, 6),
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(GridLength.Star),
                            new ColumnDefinition(GridLength.Auto),
                        }
                    };
                    var ftChevron = new Label
                    {
                        Text = expanded ? "▼" : "▶",
                        FontSize = 11, TextColor = Color.FromArgb("#1565C0"),
                        VerticalOptions = LayoutOptions.Center
                    };
                    var ftLabel = new Label
                    {
                        Text = $"  Free Tickets ({freeRecs.Count})",
                        FontSize = 12, FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#1565C0"),
                        VerticalOptions = LayoutOptions.Center
                    };
                    var ftInner = new HorizontalStackLayout { Children = { ftChevron, ftLabel } };
                    ftToggleRow.Children.Add(ftInner);

                    // Free ticket rows container (shown/hidden by toggle)
                    var ftContainer = new VerticalStackLayout { IsVisible = expanded };
                    foreach (var rec in freeRecs)
                        ftContainer.Children.Add(BuildRecordRow(rec, accent));

                    ftToggleRow.GestureRecognizers.Add(new TapGestureRecognizer
                    {
                        Command = new Command(() =>
                        {
                            _ftExpanded[capturedKey2] = !_ftExpanded[capturedKey2];
                            ftChevron.Text      = _ftExpanded[capturedKey2] ? "▼" : "▶";
                            ftContainer.IsVisible = _ftExpanded[capturedKey2];
                        })
                    });

                    mainContainer.Children.Add(ftToggleRow);
                    mainContainer.Children.Add(ftContainer);
                }

                // ── Game subtotal row ─────────────────────────────────────────
                if (cashRecs.Count > 0 || freeRecs.Count > 0)
                {
                    var subtotalRow = new Grid
                    {
                        BackgroundColor = Color.FromArgb("#FAFAFA"),
                        Padding = new Thickness(8, 5),
                        ColumnDefinitions =
                        {
                            new ColumnDefinition(GridLength.Star),
                            new ColumnDefinition(GridLength.Auto),
                        }
                    };
                    string subtotalDetail = gameTotal > 0 && gameFree > 0
                        ? $"${gameTotal:N0} cash + {gameFree} free ticket{(gameFree > 1 ? "s" : "")}"
                        : gameTotal > 0 ? $"Subtotal: ${gameTotal:N0}"
                        : $"Subtotal: {gameFree} free ticket{(gameFree > 1 ? "s" : "")}";
                    subtotalRow.Children.Add(new Label
                    {
                        Text = subtotalDetail,
                        FontSize = 12, FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#444"),
                        VerticalOptions = LayoutOptions.Center,
                        Margin = new Thickness(4, 0)
                    });
                    var subTotal2 = new Label
                    {
                        Text = gameTotal > 0 ? $"${gameTotal:N0}" : "",
                        FontSize = 13, FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#1B5E20"),
                        VerticalOptions = LayoutOptions.Center,
                        Margin = new Thickness(0, 0, 36, 0)
                    };
                    Grid.SetColumn(subTotal2, 1);
                    subtotalRow.Children.Add(subTotal2);
                    mainContainer.Children.Add(subtotalRow);
                }
            }

            // Spacer between sections
            mainContainer.Children.Add(new BoxView
            {
                HeightRequest = 8, BackgroundColor = Color.FromArgb("#D0D5DA")
            });
        }

        // Update grand total footer
        string grandStr;
        if (grandTotal > 0 && totalFreeTickets > 0)
            grandStr = $"GRAND TOTAL:  ${grandTotal:N0}  +{totalFreeTickets} free ticket{(totalFreeTickets > 1 ? "s" : "")}";
        else if (grandTotal > 0)
            grandStr = $"GRAND TOTAL:  ${grandTotal:N0}";
        else if (totalFreeTickets > 0)
            grandStr = $"GRAND TOTAL:  {totalFreeTickets} free ticket{(totalFreeTickets > 1 ? "s" : "")}";
        else
            grandStr = "GRAND TOTAL:  $0  —  No winnings recorded yet";
        lblGrandTotal.Text = grandStr;
    }

    static Label ColHdrLabel(string text, int col, TextAlignment align = TextAlignment.Start)
    {
        var lbl = new Label
        {
            Text = text, FontSize = 10, TextColor = Color.FromArgb("#888"),
            VerticalOptions = LayoutOptions.Center, HorizontalTextAlignment = align
        };
        Grid.SetColumn(lbl, col);
        return lbl;
    }

    View BuildRecordRow(WinningRecord rec, Color accent)
    {
        bool   dateParsed = DateTime.TryParse(rec.Date, out var dt);
        string dateStr    = dateParsed ? dt.ToString("M/d/yy") : rec.Date;
        string amtStr     = rec.IsFreeTicket ? "Free Ticket" : (rec.Amount > 0 ? $"${rec.Amount:N0}" : "—");

        var row = new Grid
        {
            BackgroundColor = Colors.White,
            Padding = new Thickness(8, 5),
            Margin = new Thickness(0, 1),
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(58)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(44)),
                new ColumnDefinition(new GridLength(60)),
                new ColumnDefinition(new GridLength(34)),
            }
        };

        // Left accent strip (3px colored line)
        var strip = new BoxView { Color = accent, WidthRequest = 3,
            VerticalOptions = LayoutOptions.Fill, HorizontalOptions = LayoutOptions.Start };
        // Place in col 0 but overlaid via absolute — easier: just color the date label
        row.Children.Add(strip); // will show at col 0 start

        // Date
        var dateLbl = new Label
        {
            Text = dateStr, FontSize = 10,
            TextColor = Color.FromArgb("#555"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.NoWrap,
            Margin = new Thickness(6, 0, 0, 0)
        };
        row.Children.Add(dateLbl);

        // Numbers
        var numsLbl = new Label
        {
            Text = string.IsNullOrEmpty(rec.Numbers) ? "—" : rec.Numbers,
            FontSize = 10, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#222"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        Grid.SetColumn(numsLbl, 1);
        row.Children.Add(numsLbl);

        // Match / Note
        var noteLbl = new Label
        {
            Text = rec.Note, FontSize = 10, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#E65100"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.NoWrap,
            HorizontalTextAlignment = TextAlignment.Center
        };
        Grid.SetColumn(noteLbl, 2);
        row.Children.Add(noteLbl);

        // Prize
        var amtLbl = new Label
        {
            Text = amtStr, FontSize = 11, FontAttributes = FontAttributes.Bold,
            TextColor = rec.IsFreeTicket ? Color.FromArgb("#1565C0") : Color.FromArgb("#1B5E20"),
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.NoWrap,
            HorizontalTextAlignment = TextAlignment.End
        };
        Grid.SetColumn(amtLbl, 3);
        row.Children.Add(amtLbl);

        // Delete — small red ✕ label, no button chrome
        var capturedRec = rec;
        var delLbl = new Border
        {
            BackgroundColor = Color.FromArgb("#C62828"),
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
            Padding = new Thickness(4, 2),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "✕", FontSize = 11, FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
            }
        };
        delLbl.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                bool ok = await DisplayAlert("Remove Entry", "Remove this entry?", "Yes", "Cancel");
                if (!ok) return;
                _records.Remove(capturedRec);
                await SaveRecordsAsync();
                BuildUI();
            })
        });
        Grid.SetColumn(delLbl, 4);
        row.Children.Add(delLbl);

        return row;
    }

    // ── Add Entry Overlay ─────────────────────────────────────────────────────

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

        // Disable amount when Free Ticket is on
        _addFreeSwitch.Toggled += (_, e) =>
        {
            _addAmountEntry!.IsEnabled = !e.Value;
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
        _addGame = gameKey;
        _addGameLabel!.Text = $"Add Winning — {gameName}";

        // Apply game accent color to label
        var def = GameDefs.FirstOrDefault(g => g.Key == gameKey);
        _addGameLabel.TextColor = string.IsNullOrEmpty(def.Color)
            ? Colors.White : Color.FromArgb(def.Color);

        _addDatePicker!.Date    = DateTime.Today;
        _addNumbersEntry!.Text  = "";
        _addAmountEntry!.Text   = "";
        _addNoteEntry!.Text     = "";
        _addFreeSwitch!.IsToggled = false;
        _addAmountEntry.IsEnabled = true;
        _addOverlay!.IsVisible  = true;
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
            Game        = _addGame,
            Date        = $"{_addDatePicker!.Date:yyyy-MM-dd}",
            Numbers     = _addNumbersEntry!.Text?.Trim() ?? "",
            Amount      = isFree ? 0 : amount,
            IsFreeTicket= isFree,
            Note        = _addNoteEntry!.Text?.Trim() ?? "",
        };

        _records.Add(rec);
        await SaveRecordsAsync();
        _addOverlay!.IsVisible = false;
        BuildUI();
    }

    // ── Buttons ───────────────────────────────────────────────────────────────

    private async void BtnBack_Clicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..", false);
    }

    private async void BtnClearAll_Clicked(object sender, EventArgs e)
    {
        if (_records.Count == 0) return;
        bool ok = await DisplayAlert("Clear All", "Remove all winnings entries?", "Yes", "Cancel");
        if (!ok) return;
        _records.Clear();
        await SaveRecordsAsync();
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
