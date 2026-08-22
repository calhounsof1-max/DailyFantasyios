using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

// ── Self-contained Ticket Calendar feature ──────────────────────────────────
// Everything for this feature lives in this one file. It only READS the
// existing SpendingRecord / WinningRecord / TicketLogEntry logs (via
// SpendingTracker / SummaryPage / TicketLogService's public loaders) — it
// never modifies any other file's data or logic. The rest of the app only
// knows about this page through a single menu entry + a route registration
// (both added, not rewritten).
public class TicketCalendarPage : ContentPage
{
    readonly record struct GameDef(string Key, string Name, string Color, string Emoji);

    static readonly GameDef[] Games =
    [
        new("F5", "Fantasy 5",     "#FF8F00", "5️⃣"),
        new("SL", "Super Lotto",   "#7B1FA2", "🍀"),
        new("PB", "Powerball",     "#C62828", "🔴"),
        new("MM", "Mega Millions", "#F57F17", "🟡"),
        new("D3", "Daily 3",       "#1565C0", "3️⃣"),
        new("D4", "Daily 4",       "#00695C", "4️⃣"),
        new("DD", "Daily Derby",   "#5D4037", "🐎"),
        new("SC", "Scratchers",    "#2E7D32", "🎫"),
        // Was missing entirely (same bug just found/fixed on SummaryPage.xaml.cs and
        // TicketLogPage.xaml.cs's own separate GameDefs lists) — the day-detail dialog's
        // `foreach (var g in Games)` loop below silently skipped Hot Spot spending, which
        // also meant that dialog's own totalSpent came up short by the HS amount versus
        // what Log Today / the day cell showed elsewhere.
        new("HS", "Hot Spot",      "#E65100", "🔥"),
    ];

    static GameDef GameDefFor(string key)
    {
        foreach (var g in Games) if (g.Key == key) return g;
        return new GameDef(key, key, "#607D8B", "🎟");
    }

    // PB / MM get a colored ball with a letter (matches the real Powerball/Mega Ball look);
    // every other game keeps its emoji.
    static View BuildIcon(GameDef g, double size)
    {
        if (g.Key == "PB" || g.Key == "MM")
        {
            return new Border
            {
                WidthRequest = size, HeightRequest = size,
                BackgroundColor = Color.FromArgb(g.Color),
                Stroke = Colors.Transparent, StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = size / 2 },
                Padding = new Thickness(0),
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = g.Key == "PB" ? "P" : "M",
                    TextColor = Colors.White, FontAttributes = FontAttributes.Bold,
                    FontSize = size * 0.55,
                    HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center,
                }
            };
        }
        return new Label
        {
            Text = g.Emoji, FontSize = size * 0.85,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
        };
    }

    List<SpendingRecord>  _spending  = new();
    List<WinningRecord>   _wins      = new();
    List<TicketLogEntry>  _ticketLog = new();
    DateTime _monthCursor = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    Label _monthLabel    = null!;
    Grid  _gridDays      = null!;
    Label _lblMonthSpent = null!;
    Label _lblMonthWon   = null!;
    Label _lblMonthNet   = null!;
    Label _lblAllSpent   = null!;
    Label _lblAllWon     = null!;
    Label _lblAllNet     = null!;
    Grid  _detailOverlay = null!;
    Grid  _loadingOverlay = null!;
    ActivityIndicator _loadingSpinner = null!;
    Label _loadingLabel = null!;

    public TicketCalendarPage()
    {
        Shell.SetNavBarIsVisible(this, false);
        BackgroundColor = Color.FromArgb("#0F1923");
        BuildLayout();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadAndBuildAsync();
    }

    async Task LoadAndBuildAsync()
    {
        _loadingOverlay.IsVisible = true;
        _loadingSpinner.IsRunning = true;
        try
        {
            if (TicketLogService.PendingWriteTask != null)
            {
                await TicketLogService.PendingWriteTask;
                TicketLogService.PendingWriteTask = null;
            }
            await TicketLogService.ScanAndLogTodayAsync();
            await SpendingTracker.AutoSyncTodayAsync();

            _spending  = await SpendingTracker.LoadAllAsync();
            _wins      = await SummaryPage.LoadAllAsync();
            _ticketLog = await TicketLogService.LoadAllAsync();
            BuildMonthGrid();
        }
        finally
        {
            _loadingSpinner.IsRunning = false;
            _loadingOverlay.IsVisible = false;
        }
    }

    // ── Layout scaffold ─────────────────────────────────────────────────────

    void BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),  // header
                new RowDefinition(GridLength.Auto),  // month nav
                new RowDefinition(GridLength.Auto),  // weekday header
                new RowDefinition(GridLength.Star),  // day grid
                new RowDefinition(GridLength.Auto),  // legend
                new RowDefinition(GridLength.Auto),  // totals bar (this month)
                new RowDefinition(GridLength.Auto),  // totals bar (all months)
            }
        };

        var header = BuildHeader();
        AtRow(header, 0);

        var nav = BuildMonthNav();
        AtRow(nav, 1);

        var weekdayHdr = BuildWeekdayHeader();
        AtRow(weekdayHdr, 2);

        _gridDays = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            },
            RowSpacing = 3, ColumnSpacing = 3,
            Padding = new Thickness(6, 4),
        };
        var scroll = new ScrollView { Content = _gridDays };
        AtRow(scroll, 3);

        var legend = BuildLegend();
        AtRow(legend, 4);

        var totals = BuildTotalsBar();
        AtRow(totals, 5);

        var allTotals = BuildAllTimeTotalsBar();
        AtRow(allTotals, 6);

        root.Children.Add(header);
        root.Children.Add(nav);
        root.Children.Add(weekdayHdr);
        root.Children.Add(scroll);
        root.Children.Add(legend);
        root.Children.Add(totals);
        root.Children.Add(allTotals);

        _detailOverlay = new Grid { BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false };
        AtRow(_detailOverlay, 0);
        Grid.SetRowSpan(_detailOverlay, 7);
        root.Children.Add(_detailOverlay);

        _loadingSpinner = new ActivityIndicator { IsRunning = true, Color = Color.FromArgb("#D4A94A"), WidthRequest = 44, HeightRequest = 44 };
        _loadingLabel = new Label { Text = "Loading Ticket Calendar…", FontSize = 13, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center };
        _loadingOverlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#E60F1923"),
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 12, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                    Children = { _loadingSpinner, _loadingLabel }
                }
            }
        };
        AtRow(_loadingOverlay, 0);
        Grid.SetRowSpan(_loadingOverlay, 7);
        root.Children.Add(_loadingOverlay);

        Content = root;
    }

    View BuildHeader()
    {
        var grid = new Grid
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Padding = new Thickness(4, 8),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
            }
        };

        var btnBack = new Button
        {
            Text = "← Back", BackgroundColor = Colors.Transparent,
            TextColor = Colors.White, FontSize = 12, Padding = new Thickness(2, 0),
        };
        btnBack.Clicked += async (_, _) => await Shell.Current.GoToAsync("..", false);

        var title = new Label
        {
            Text = "🗓 Ticket Calendar",
            FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
        };

        var exportLbl = new Label
        {
            Text = "💾", FontSize = 20, TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center, Padding = new Thickness(8, 0),
        };
        exportLbl.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await OnExportTapped()) });

        var homeLbl = new Label
        {
            Text = "⌂", FontSize = 20, TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center, Padding = new Thickness(8, 0),
        };
        homeLbl.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await Shell.Current.GoToAsync("//MainPage", false))
        });

        grid.Children.Add(AtCol(btnBack, 0));
        grid.Children.Add(AtCol(title, 1));
        grid.Children.Add(AtCol(exportLbl, 2));
        grid.Children.Add(AtCol(homeLbl, 3));
        return grid;
    }

    View BuildMonthNav()
    {
        var grid = new Grid
        {
            BackgroundColor = Color.FromArgb("#162230"),
            Padding = new Thickness(10, 6),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            }
        };

        var btnToday = new Border
        {
            BackgroundColor = Color.FromArgb("#2563EB"),
            Stroke = Colors.Transparent, StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 6 },
            Padding = new Thickness(10, 5),
            Content = new Label { Text = "Today", FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
        };
        btnToday.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                _monthCursor = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                BuildMonthGrid();
            })
        });

        var btnPrev = new Label
        {
            Text = "◀", FontSize = 16, TextColor = Color.FromArgb("#90CAF9"),
            VerticalOptions = LayoutOptions.Center, Padding = new Thickness(10, 0),
        };
        btnPrev.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => { _monthCursor = _monthCursor.AddMonths(-1); BuildMonthGrid(); })
        });

        _monthLabel = new Label
        {
            Text = _monthCursor.ToString("MMMM yyyy"),
            FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
        };

        var btnNext = new Label
        {
            Text = "▶", FontSize = 16, TextColor = Color.FromArgb("#90CAF9"),
            VerticalOptions = LayoutOptions.Center, Padding = new Thickness(10, 0),
        };
        btnNext.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => { _monthCursor = _monthCursor.AddMonths(1); BuildMonthGrid(); })
        });

        grid.Children.Add(AtCol(btnToday, 0));
        grid.Children.Add(AtCol(btnPrev, 1));
        grid.Children.Add(AtCol(_monthLabel, 2));
        grid.Children.Add(AtCol(btnNext, 3));
        return grid;
    }

    static View BuildWeekdayHeader()
    {
        var grid = new Grid
        {
            BackgroundColor = Color.FromArgb("#1565C0"),
            Padding = new Thickness(6, 4),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
            }
        };
        string[] names = ["S", "M", "T", "W", "T", "F", "S"];
        for (int i = 0; i < 7; i++)
            grid.Children.Add(AtCol(new Label
            {
                Text = names[i], FontSize = 12, FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center,
            }, i));
        return grid;
    }

    static View BuildLegend()
    {
        var grid = new Grid
        {
            BackgroundColor = Color.FromArgb("#16232F"),
            Padding = new Thickness(10, 8),
            RowSpacing = 6, ColumnSpacing = 2,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
            },
        };

        for (int i = 0; i < Games.Length; i++)
        {
            var g = Games[i];
            var iconSlot = new Grid
            {
                WidthRequest = 18, HeightRequest = 18,
                Children = { BuildIcon(g, 15) },
            };
            var chip = new HorizontalStackLayout
            {
                Spacing = 4, HorizontalOptions = LayoutOptions.Start,
                Children =
                {
                    iconSlot,
                    new Label { Text = g.Name, FontSize = 9, FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb(g.Color), VerticalOptions = LayoutOptions.Center },
                }
            };
            Grid.SetRow(chip, i / 4);
            Grid.SetColumn(chip, i % 4);
            grid.Children.Add(chip);
        }
        return grid;
    }

    View BuildTotalsBar()
    {
        var grid = new Grid
        {
            BackgroundColor = Color.FromArgb("#263545"),
            Padding = new Thickness(16, 8),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
        };
        _lblMonthSpent = new Label { FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#EF9A9A"), HorizontalOptions = LayoutOptions.Center };
        _lblMonthWon   = new Label { FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#A5D6A7"), HorizontalOptions = LayoutOptions.Center };
        _lblMonthNet   = new Label { FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center };
        grid.Children.Add(AtCol(_lblMonthSpent, 0));
        grid.Children.Add(AtCol(_lblMonthWon, 1));
        grid.Children.Add(AtCol(_lblMonthNet, 2));
        return grid;
    }

    View BuildAllTimeTotalsBar()
    {
        var grid = new Grid
        {
            BackgroundColor = Color.FromArgb("#1C2A38"),
            Padding = new Thickness(16, 6, 16, 4),
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
        };
        _lblAllSpent = new Label { FontSize = 8, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#C48B8B"), HorizontalOptions = LayoutOptions.Center };
        _lblAllWon   = new Label { FontSize = 8, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#8FB891"), HorizontalOptions = LayoutOptions.Center };
        _lblAllNet   = new Label { FontSize = 8, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#B0B8C0"), HorizontalOptions = LayoutOptions.Center };
        grid.Children.Add(AtCol(_lblAllSpent, 0));
        grid.Children.Add(AtCol(_lblAllWon, 1));
        grid.Children.Add(AtCol(_lblAllNet, 2));

        var hint = new Label
        {
            Text = "📄 Tap for monthly breakdown & save to PDF for full ticket details",
            FontSize = 8, FontAttributes = FontAttributes.Italic,
            TextColor = Color.FromArgb("#6B7A8C"), HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 2, 0, 0),
        };
        Grid.SetRow(hint, 1);
        Grid.SetColumnSpan(hint, 3);
        grid.Children.Add(hint);

        grid.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(ShowMonthlyBreakdown) });

        return grid;
    }

    // ── All-time monthly breakdown overlay ──────────────────────────────────

    void ShowMonthlyBreakdown()
    {
        var content = new VerticalStackLayout { Spacing = 10 };

        content.Children.Add(new Label
        {
            Text = "All-Time Monthly Breakdown",
            FontSize = 15, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#FFD54F"), HorizontalOptions = LayoutOptions.Center,
        });

        var months = _spending.Select(r => r.Date.Substring(0, 7))
            .Concat(_wins.Select(w => w.Date.Substring(0, 7)))
            .Distinct()
            .OrderByDescending(ym => ym)
            .ToList();

        if (months.Count == 0)
        {
            content.Children.Add(new Label
            {
                Text = "No data logged yet.",
                FontSize = 12, TextColor = Color.FromArgb("#8B9DC3"),
                HorizontalOptions = LayoutOptions.Center,
            });
        }
        var breakdown = new List<(string MonthLabel, decimal Spent, decimal Won, decimal Net, List<TicketLogEntry> Tickets)>();
        decimal grandSpent = 0, grandWon = 0;

        if (months.Count > 0)
        {
            foreach (var ym in months)
            {
                decimal spent = _spending.Where(r => r.Date.StartsWith(ym)).Sum(r => r.TotalCost);
                decimal won   = _wins.Where(w => w.Date.StartsWith(ym) && !w.IsFreeTicket).Sum(w => w.Amount);
                decimal net   = won - spent;
                grandSpent += spent;
                grandWon   += won;

                string monthLabel = DateTime.ParseExact(ym, "yyyy-MM", null).ToString("MMMM yyyy");
                var monthTickets = _ticketLog.Where(t => t.Date.StartsWith(ym))
                    .OrderBy(t => t.Date).ThenBy(t => t.Game).ThenBy(t => t.Slot).ThenBy(t => t.Row).ToList();
                breakdown.Add((monthLabel, spent, won, net, monthTickets));

                var headerRow = new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                    // A Grid with no background often won't register taps on Android at all —
                    // it needs *some* background, even fully transparent, for the native view
                    // to intercept touch and fire GestureRecognizers.
                    BackgroundColor = Colors.Transparent,
                };
                headerRow.Children.Add(AtCol(new Label
                {
                    Text = monthLabel, FontSize = 13, FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White, VerticalOptions = LayoutOptions.Center,
                }, 0));
                headerRow.Children.Add(AtCol(new Label
                {
                    Text = $"Net: {(net >= 0 ? "+" : "")}${net:N2}",
                    FontSize = 13, FontAttributes = FontAttributes.Bold,
                    TextColor = net >= 0 ? Color.FromArgb("#66BB6A") : Color.FromArgb("#EF5350"),
                    VerticalOptions = LayoutOptions.Center,
                }, 1));
                var monthBlock = new VerticalStackLayout
                {
                    // Explicit transparent background — a Layout with none often won't
                    // register taps on Android at all (the native view needs *some*
                    // background, even fully transparent, to intercept touch).
                    BackgroundColor = Colors.Transparent,
                    Children =
                    {
                        headerRow,
                        new Label
                        {
                            Text = $"Spent: ${spent:N2}    Won: ${won:N2}",
                            FontSize = 11, TextColor = Color.FromArgb("#8FA3B8"),
                        },
                        new Label
                        {
                            Text = "📄 Tap to open this month's PDF",
                            FontSize = 9, FontAttributes = FontAttributes.Italic,
                            TextColor = Color.FromArgb("#D4A94A"),
                        },
                    }
                };
                // Tap a single month to open just that month's own PDF — unlike the combined
                // all-time report below, this file physically contains only this month, so
                // there's nothing to scroll into.
                monthBlock.GestureRecognizers.Add(new TapGestureRecognizer
                {
                    Command = new Command(async () => await OpenSingleMonthPdfAsync(monthLabel, spent, won, net, monthTickets))
                });
                content.Children.Add(monthBlock);

                content.Children.Add(new BoxView { HeightRequest = 1, BackgroundColor = Color.FromArgb("#334155"), Margin = new Thickness(0, 2, 0, 0) });
            }
        }

        decimal grandNet = grandWon - grandSpent;
        if (months.Count > 0)
        {
            content.Children.Add(new Label
            {
                Text = $"ALL-TIME NET: {(grandNet >= 0 ? "+" : "")}${grandNet:N2}",
                FontSize = 14, FontAttributes = FontAttributes.Bold,
                TextColor = grandNet >= 0 ? Color.FromArgb("#66BB6A") : Color.FromArgb("#EF5350"),
                HorizontalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        content.Children.Add(new Label
        {
            Text = "Tap a month above to open just that month's PDF, or use the buttons below for the combined all-time report",
            FontSize = 10, FontAttributes = FontAttributes.Italic,
            TextColor = Color.FromArgb("#8B9DC3"), HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 8, 0, 0),
        });

        var btnSavePdf = new Border
        {
            BackgroundColor = Color.FromArgb("#2D3E55"),
            Stroke = Colors.Transparent, StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(16, 8),
            Content = new Label { Text = "📄 Save PDF", TextColor = Colors.White, FontSize = 13, HorizontalOptions = LayoutOptions.Center },
        };
        btnSavePdf.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await ExportMonthlyBreakdownPdfAsync(breakdown, grandSpent, grandWon, grandNet))
        });

        var btnOpenPdf = new Border
        {
            BackgroundColor = Color.FromArgb("#2D3E55"),
            Stroke = Colors.Transparent, StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(16, 8),
            Content = new Label { Text = "🌐 Open", TextColor = Colors.White, FontSize = 13, HorizontalOptions = LayoutOptions.Center },
        };
        btnOpenPdf.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await OpenMonthlyBreakdownPdfAsync(breakdown, grandSpent, grandWon, grandNet))
        });

        var btnClose = new Border
        {
            BackgroundColor = Color.FromArgb("#4B5563"),
            Stroke = Colors.Transparent, StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(16, 8),
            Content = new Label { Text = "Close", TextColor = Colors.White, FontSize = 13, HorizontalOptions = LayoutOptions.Center },
        };
        btnClose.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => _detailOverlay.IsVisible = false) });

        var btnRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8, Margin = new Thickness(0, 6, 0, 0),
        };
        btnRow.Children.Add(AtCol(btnSavePdf, 0));
        btnRow.Children.Add(AtCol(btnOpenPdf, 1));
        btnRow.Children.Add(AtCol(btnClose, 2));
        content.Children.Add(btnRow);

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(20, 18),
            VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 340,
            Content = new ScrollView { MaximumHeightRequest = 520, Content = content },
        };

        _detailOverlay.Children.Clear();
        _detailOverlay.Children.Add(card);
        _detailOverlay.IsVisible = true;
    }

    void UpdateAllTimeTotals()
    {
        decimal allSpent = _spending.Sum(r => r.TotalCost);
        decimal allWon   = _wins.Where(w => !w.IsFreeTicket).Sum(w => w.Amount);
        decimal allNet   = allWon - allSpent;
        _lblAllSpent.Text = $"ALL-TIME SPENT: ${allSpent:N2}";
        _lblAllWon.Text   = $"ALL-TIME WON: ${allWon:N2}";
        _lblAllNet.Text   = $"ALL-TIME NET: {(allNet >= 0 ? "+" : "")}${allNet:N2}";
        _lblAllNet.TextColor = allNet >= 0 ? Color.FromArgb("#66BB6A") : Color.FromArgb("#EF5350");
    }

    // ── Month grid build ─────────────────────────────────────────────────────

    void BuildMonthGrid()
    {
        _monthLabel.Text = _monthCursor.ToString("MMMM yyyy");
        _gridDays.Children.Clear();
        _gridDays.RowDefinitions.Clear();

        var firstOfMonth = _monthCursor;
        var lastOfMonth  = firstOfMonth.AddMonths(1).AddDays(-1);
        var firstCell    = firstOfMonth.AddDays(-(int)firstOfMonth.DayOfWeek);
        var lastCell     = lastOfMonth.AddDays(6 - (int)lastOfMonth.DayOfWeek);
        int totalDays    = (lastCell - firstCell).Days + 1;
        int weeks        = totalDays / 7;

        for (int w = 0; w < weeks; w++)
            _gridDays.RowDefinitions.Add(new RowDefinition(new GridLength(92)));

        for (int i = 0; i < totalDays; i++)
        {
            var date = firstCell.AddDays(i);
            bool inMonth = date.Month == firstOfMonth.Month;
            var cell = BuildDayCell(date, inMonth);
            Grid.SetRow(cell, i / 7);
            Grid.SetColumn(cell, i % 7);
            _gridDays.Children.Add(cell);
        }

        string ym = _monthCursor.ToString("yyyy-MM");
        decimal monthSpent = _spending.Where(r => r.Date.StartsWith(ym)).Sum(r => r.TotalCost);
        decimal monthWon   = _wins.Where(w => w.Date.StartsWith(ym) && !w.IsFreeTicket).Sum(w => w.Amount);
        decimal net = monthWon - monthSpent;
        _lblMonthSpent.Text = $"SPENT: ${monthSpent:N2}";
        _lblMonthWon.Text   = $"WON: ${monthWon:N2}";
        _lblMonthNet.Text   = $"NET: {(net >= 0 ? "+" : "")}${net:N2}";
        _lblMonthNet.TextColor = net >= 0 ? Color.FromArgb("#66BB6A") : Color.FromArgb("#EF5350");

        UpdateAllTimeTotals();
    }

    View BuildDayCell(DateTime date, bool inMonth)
    {
        string dateStr  = date.ToString("yyyy-MM-dd");
        var daySpend    = _spending.Where(r => r.Date == dateStr).ToList();
        var dayWins     = _wins.Where(w => w.Date == dateStr).ToList();
        decimal spent   = daySpend.Sum(r => r.TotalCost);
        decimal won     = dayWins.Where(w => !w.IsFreeTicket).Sum(w => w.Amount);
        bool hasWin     = won > 0;
        bool isToday    = date == DateTime.Today;

        var border = new Border
        {
            BackgroundColor = hasWin
                ? Color.FromArgb("#123B24")
                : (inMonth ? Color.FromArgb("#16232F") : Color.FromArgb("#0E1720")),
            Stroke = new SolidColorBrush(isToday ? Color.FromArgb("#42A5F5") : Color.FromArgb("#22303E")),
            StrokeThickness = isToday ? 2 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(4, 3),
        };

        var stack = new VerticalStackLayout { Spacing = 1 };

        stack.Children.Add(new Label
        {
            Text = date.Day.ToString(),
            FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = !inMonth ? Color.FromArgb("#4A5A6A") : (isToday ? Color.FromArgb("#42A5F5") : Colors.White),
        });

        if (daySpend.Count > 0)
        {
            var distinctGames = daySpend.Select(r => r.Game).Distinct().ToList();
            var iconRow = new HorizontalStackLayout { Spacing = 1 };
            foreach (var gk in distinctGames.Take(4))
                iconRow.Children.Add(BuildIcon(GameDefFor(gk), 13));
            if (distinctGames.Count > 4)
                iconRow.Children.Add(new Label { Text = "+", FontSize = 10, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center });
            stack.Children.Add(iconRow);

            int totalTickets = daySpend.Sum(r => r.TicketCount);
            stack.Children.Add(new Label { Text = $"{totalTickets} tix", FontSize = 8, TextColor = Color.FromArgb("#8B9DC3") });

            stack.Children.Add(new Label { Text = $"${spent:N2}", FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#EF9A9A") });
        }

        if (hasWin)
        {
            stack.Children.Add(new Label { Text = $"💵${won:N2}", FontSize = 10, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#66BB6A") });
        }

        border.Content = stack;

        var capturedDate = date;
        border.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => ShowDayDetail(capturedDate)) });

        return border;
    }

    // ── Day detail overlay ───────────────────────────────────────────────────

    void ShowDayDetail(DateTime date)
    {
        string dateStr  = date.ToString("yyyy-MM-dd");
        var daySpend    = _spending.Where(r => r.Date == dateStr).ToList();
        var dayWins     = _wins.Where(w => w.Date == dateStr).ToList();

        var content = new VerticalStackLayout { Spacing = 8 };

        content.Children.Add(new Label
        {
            Text = date.ToString("dddd, MMMM d, yyyy"),
            FontSize = 15, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#FFD54F"), HorizontalOptions = LayoutOptions.Center,
        });

        decimal totalSpent = 0;
        decimal totalWon   = 0;

        if (daySpend.Count == 0 && dayWins.Count == 0)
        {
            content.Children.Add(new Label
            {
                Text = "No tickets logged this day.",
                FontSize = 12, TextColor = Color.FromArgb("#8B9DC3"),
                HorizontalOptions = LayoutOptions.Center,
            });
        }
        else
        {
            foreach (var g in Games)
            {
                var recs = daySpend.Where(r => r.Game == g.Key).ToList();
                if (recs.Count == 0) continue;
                decimal gameTotal = recs.Sum(r => r.TotalCost);
                int count = recs.Sum(r => r.TicketCount);
                totalSpent += gameTotal;

                var accent = Color.FromArgb(g.Color);
                var row = new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                    Padding = new Thickness(0, 2),
                };
                row.Children.Add(AtCol(new HorizontalStackLayout
                {
                    Spacing = 6, VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        BuildIcon(g, 18),
                        new Label { Text = $"{g.Name}  —  {count} ticket(s)", FontSize = 12, TextColor = accent, VerticalOptions = LayoutOptions.Center },
                    }
                }, 0));
                row.Children.Add(AtCol(new Label
                {
                    Text = $"${gameTotal:N2}",
                    FontSize = 12, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#EF9A9A"), VerticalOptions = LayoutOptions.Center,
                }, 1));
                content.Children.Add(row);

                // Every individual ticket purchased this day for this game (actual numbers played) —
                // same source data as the Ticket Log page.
                var tickets = _ticketLog
                    .Where(t => t.Date == dateStr && t.Game == g.Key)
                    .OrderBy(t => t.Slot).ThenBy(t => t.Row)
                    .ToList();
                foreach (var entry in tickets)
                {
                    string extraTxt = FormatExtra(g.Key, entry.Extra);
                    string rangeTxt = !string.IsNullOrEmpty(entry.PlayFrom)
                        ? $"  ({entry.PlayFrom}–{(string.IsNullOrEmpty(entry.PlayTo) ? entry.PlayFrom : entry.PlayTo)})"
                        : "";
                    string freeTxt = entry.IsFreePlay ? "  🎟 free" : "";
                    string line = $"    • {entry.Numbers}{(string.IsNullOrEmpty(extraTxt) ? "" : $"  +{extraTxt}")}{rangeTxt}{freeTxt}";
                    content.Children.Add(new Label { Text = line, FontSize = 11, TextColor = Color.FromArgb("#8FA3B8") });
                }
            }

            content.Children.Add(new BoxView { HeightRequest = 1, BackgroundColor = Color.FromArgb("#334155"), Margin = new Thickness(0, 4) });
            content.Children.Add(new Label
            {
                Text = $"Total Spent: ${totalSpent:N2}",
                FontSize = 13, FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#EF9A9A"), HorizontalOptions = LayoutOptions.Center,
            });

            if (dayWins.Count > 0)
            {
                content.Children.Add(new Label
                {
                    Text = "Wins",
                    FontSize = 13, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#66BB6A"), Margin = new Thickness(0, 6, 0, 0),
                });
                foreach (var w in dayWins)
                {
                    var g = GameDefFor(w.Game);
                    totalWon += w.IsFreeTicket ? 0 : w.Amount;
                    string amountText = w.IsFreeTicket ? "🎟 Free Ticket" : $"💵 ${w.Amount:N2}";
                    string noteText = string.IsNullOrEmpty(w.Note) ? "" : $"  ({w.Note})";
                    content.Children.Add(new HorizontalStackLayout
                    {
                        Spacing = 6,
                        Children =
                        {
                            BuildIcon(g, 16),
                            new Label { Text = $"{g.Name}: {amountText}{noteText}", FontSize = 12, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center },
                        }
                    });
                }
                content.Children.Add(new Label
                {
                    Text = $"Total Won: ${totalWon:N2}",
                    FontSize = 13, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#66BB6A"), HorizontalOptions = LayoutOptions.Center,
                });
            }

            decimal net = totalWon - totalSpent;
            content.Children.Add(new Label
            {
                Text = $"Net: {(net >= 0 ? "+" : "")}${net:N2}",
                FontSize = 13, FontAttributes = FontAttributes.Bold,
                TextColor = net >= 0 ? Color.FromArgb("#66BB6A") : Color.FromArgb("#EF5350"),
                HorizontalOptions = LayoutOptions.Center,
            });
        }

        var btnExportDay = new Border
        {
            BackgroundColor = Color.FromArgb("#2D3E55"),
            Stroke = Colors.Transparent, StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(16, 8),
            Content = new Label { Text = "💾 Save Day", TextColor = Colors.White, FontSize = 13, HorizontalOptions = LayoutOptions.Center },
        };
        btnExportDay.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await ExportDayAsync(date, daySpend, dayWins))
        });

        var btnClose = new Border
        {
            BackgroundColor = Color.FromArgb("#4B5563"),
            Stroke = Colors.Transparent, StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(16, 8),
            Content = new Label { Text = "Close", TextColor = Colors.White, FontSize = 13, HorizontalOptions = LayoutOptions.Center },
        };
        btnClose.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => _detailOverlay.IsVisible = false) });

        var btnRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10, Margin = new Thickness(0, 6, 0, 0),
        };
        btnRow.Children.Add(AtCol(btnExportDay, 0));
        btnRow.Children.Add(AtCol(btnClose, 1));
        content.Children.Add(btnRow);

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(20, 18),
            VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 340,
            Content = new ScrollView { MaximumHeightRequest = 520, Content = content },
        };

        _detailOverlay.Children.Clear();
        _detailOverlay.Children.Add(card);
        _detailOverlay.IsVisible = true;
    }

    // ── Export / Save anywhere ───────────────────────────────────────────────

    async Task OnExportTapped()
    {
        string monthLabel = _monthCursor.ToString("MMMM yyyy");
        string? choice = await DisplayActionSheet("Save Calendar Data", "Cancel", null,
            $"This Month ({monthLabel})", "All Time");
        if (string.IsNullOrEmpty(choice) || choice == "Cancel") return;
        await ExportAsync(wholeMonth: choice.StartsWith("This Month"));
    }

    async Task ExportAsync(bool wholeMonth)
    {
        try
        {
            List<SpendingRecord>  spend;
            List<WinningRecord>   wins;
            List<TicketLogEntry>  tickets;
            string label, title, subtitle;

            if (wholeMonth)
            {
                string ym = _monthCursor.ToString("yyyy-MM");
                spend    = _spending.Where(r => r.Date.StartsWith(ym)).ToList();
                wins     = _wins.Where(w => w.Date.StartsWith(ym)).ToList();
                tickets  = _ticketLog.Where(t => t.Date.StartsWith(ym)).ToList();
                label    = _monthCursor.ToString("MMMM_yyyy");
                title    = "Monthly Ticket Report";
                subtitle = _monthCursor.ToString("MMMM yyyy");
            }
            else
            {
                spend    = _spending;
                wins     = _wins;
                tickets  = _ticketLog;
                label    = "AllTime";
                title    = "All-Time Ticket Report";
                subtitle = $"Generated {DateTime.Now:MMMM d, yyyy \\a\\t h:mm tt}";
            }

            string fileName = $"ticket_calendar_{label}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string path = Path.Combine(FileSystem.CacheDirectory, fileName);
            await PdfExportService.GenerateTicketExportReportAsync(path, title, subtitle, spend, tickets, wins, key => GameDefFor(key).Name);

            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "Save Ticket Calendar",
                File  = new ShareFile(path, "application/pdf"),
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Export Error", ex.Message, "OK");
        }
    }

    async Task ExportDayAsync(DateTime date, List<SpendingRecord> daySpend, List<WinningRecord> dayWins)
    {
        try
        {
            string dateStr = date.ToString("yyyy-MM-dd");
            var dayTickets = _ticketLog.Where(t => t.Date == dateStr).ToList();
            string fileName = $"ticket_calendar_{date:yyyy-MM-dd}.pdf";
            string path = Path.Combine(FileSystem.CacheDirectory, fileName);
            await PdfExportService.GenerateTicketExportReportAsync(path, "Daily Ticket Report", date.ToString("dddd, MMMM d, yyyy"),
                daySpend, dayTickets, dayWins, key => GameDefFor(key).Name);

            await Share.RequestAsync(new ShareFileRequest
            {
                Title = $"Save {date:MMM d, yyyy} Tickets",
                File  = new ShareFile(path, "application/pdf"),
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Export Error", ex.Message, "OK");
        }
    }

    /// Opens a single month's own PDF — unlike the combined all-time report, this file
    /// physically contains only this one month, so scrolling can never spill into a
    /// neighboring month (a real PDF limitation the combined report can't avoid: any
    /// viewer lets you scroll past a bookmarked page into whatever comes next in the
    /// same file). Same content:// Launcher approach as the combined report's Open button.
    async Task OpenSingleMonthPdfAsync(string monthLabel, decimal spent, decimal won, decimal net, List<TicketLogEntry> tickets)
    {
        _loadingLabel.Text = $"Opening {monthLabel}…";
        _loadingOverlay.IsVisible = true;
        try
        {
            string safeLabel = monthLabel.Replace(" ", "_");
            string fileName = $"ticket_calendar_{safeLabel}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string path = Path.Combine(FileSystem.CacheDirectory, fileName);
            await PdfExportService.GenerateSingleMonthReportAsync(path, monthLabel, spent, won, net, tickets, key => GameDefFor(key).Name);

            await Launcher.Default.OpenAsync(new OpenFileRequest($"Open {monthLabel}", new ReadOnlyFile(path, "application/pdf")));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Open Error", ex.Message, "OK");
        }
        finally
        {
            _loadingOverlay.IsVisible = false;
            _loadingLabel.Text = "Loading Ticket Calendar…";
        }
    }

    async Task ExportMonthlyBreakdownPdfAsync(List<(string MonthLabel, decimal Spent, decimal Won, decimal Net, List<TicketLogEntry> Tickets)> breakdown, decimal grandSpent, decimal grandWon, decimal grandNet)
    {
        try
        {
            string fileName = $"ticket_calendar_monthly_breakdown_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string path = Path.Combine(FileSystem.CacheDirectory, fileName);
            await PdfExportService.GenerateMonthlyBreakdownReportAsync(path, breakdown, grandSpent, grandWon, grandNet, key => GameDefFor(key).Name);

            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "Save Monthly Breakdown",
                File  = new ShareFile(path, "application/pdf"),
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Export Error", ex.Message, "OK");
        }
    }

    /// Opens the PDF directly (Android's own "Open with" chooser — every installed PDF-capable
    /// app is a valid option, not just Save PDF's share-target list) instead of sharing it.
    /// Uses Launcher's own content:// URI, which — unlike a raw file:// path — every viewer
    /// (Chrome, Edge, Adobe, etc.) is actually permitted to read.
    async Task OpenMonthlyBreakdownPdfAsync(List<(string MonthLabel, decimal Spent, decimal Won, decimal Net, List<TicketLogEntry> Tickets)> breakdown, decimal grandSpent, decimal grandWon, decimal grandNet)
    {
        try
        {
            string fileName = $"ticket_calendar_monthly_breakdown_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";
            string path = Path.Combine(FileSystem.CacheDirectory, fileName);
            await PdfExportService.GenerateMonthlyBreakdownReportAsync(path, breakdown, grandSpent, grandWon, grandNet, key => GameDefFor(key).Name);

            await Launcher.Default.OpenAsync(new OpenFileRequest("Open Monthly Breakdown", new ReadOnlyFile(path, "application/pdf")));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Open Error", ex.Message, "OK");
        }
    }

    // Turns TicketLogEntry.Extra ("M:07","PB:14","MB:09","M"/"E" for D3) into a readable suffix.
    static string FormatExtra(string game, string extra)
    {
        if (string.IsNullOrEmpty(extra)) return "";
        if (game == "D3")
        {
            string suf = extra.Contains('|') ? extra.Split('|').Last() : extra;
            return suf.Equals("M", StringComparison.OrdinalIgnoreCase) ? "Midday"
                 : suf.Equals("E", StringComparison.OrdinalIgnoreCase) ? "Evening"
                 : suf;
        }
        if (extra.StartsWith("M:"))  return $"Mega {extra[2..]}";
        if (extra.StartsWith("PB:")) return $"Powerball {extra[3..]}";
        if (extra.StartsWith("MB:")) return $"Mega Ball {extra[3..]}";
        return extra;
    }

    // ── Small layout helpers (kept local so this feature stays self-contained) ─
    static T AtCol<T>(T v, int col) where T : View { Grid.SetColumn(v, col); return v; }
    static T AtRow<T>(T v, int row) where T : View { Grid.SetRow(v, row); return v; }
}
