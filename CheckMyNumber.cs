using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.Maui.Layouts;

namespace DailyFantasyMAUI;

// ── Self-contained "Check My Numbers" feature ────────────────────────────────
// Modeled on calottery.com/en/lucky-numbers: pick a game, type your numbers, pick
// a time period, and see every past draw in that window with your numbers
// highlighted and a match count. Everything for this feature lives in THIS ONE
// FILE, same convention as HotSpotPage — user's explicit ask, so it can be pulled
// out cleanly without touching any other page's code.
//
// Deliberately built on this app's OWN already-cached local draw history
// (Data/my*.csv, same files GetDataEntry.cs keeps updated for every other game's
// stats pages) rather than hitting calottery.com's lucky-numbers page live —
// there's no public API behind that page to call (it's a client-rendered form
// over the site's own draw-history data), and the app already has years of the
// same underlying draw data sitting on-device for free. Works fully offline.
//
// Match-count rule (documented here since it's a simplification, not official
// prize-tier adjudication): for the 5-number lotto games (F5/SL/PB/MM), it's a
// straight set-overlap count of your numbers against that draw's numbers,
// bonus ball checked separately. For the digit games (D3/D4), it's a "Box"-style
// multiset overlap (each of your digits can only match one drawn digit, even if
// you or the draw repeats a digit) — this app makes no claim about what would
// have actually won money; it only answers "how many of your numbers showed up."
//
// The only touches OUTSIDE this file: AppShell.xaml.cs (singleton instance field
// + one route registration) and MainPage.xaml.cs (one nav-dropdown entry) — the
// minimum MAUI needs to make a page reachable at all, not feature logic.
public class CheckMyNumber : ContentPage
{
    // Key, Label, csv filename (under FileSystem.AppDataDirectory/data/ or the
    // bundled "data/" app-package asset as a fallback — same two-tier lookup
    // GetDataEntry.cs already uses for these exact files), how many main numbers,
    // main-number range, bonus-ball max (0 = no bonus ball), bonus label.
    static readonly (string Key, string Label, string Csv, int Count, int MinN, int MaxN, int BonusMax, string BonusLabel)[] Games =
    [
        ("F5", "Fantasy 5",        "myFantasy5.csv",     5, 1, 39, 0,  ""),
        ("SL", "SuperLotto Plus",  "mySuperlotto.csv",   5, 1, 47, 27, "Mega"),
        ("PB", "Powerball",        "myPowerball.csv",    5, 1, 69, 26, "Powerball"),
        ("MM", "Mega Millions",    "myMegaMillions.csv", 5, 1, 70, 24, "Mega Ball"),
        ("D4", "Daily 4",          "myDaily4.csv",       4, 0, 9,  0,  ""),
        ("D3", "Daily 3",          "myDaily3.csv",       3, 0, 9,  0,  ""),
    ];

    static readonly (string Label, int? Months, int? Years)[] Periods =
    [
        ("5 Years",  null, 5),
        ("2 Years",  null, 2),
        ("1 Year",   null, 1),
        ("6 Months", 6,    null),
    ];

    int _gameIdx = 0;
    int _periodIdx = 0;
    Picker _gamePicker = null!;
    Picker _periodPicker = null!;
    HorizontalStackLayout _numberRow = null!;
    Entry[] _numberEntries = [];
    Entry? _bonusEntry;
    Label _bonusLabel = null!;
    Label _rangeLabel = null!;
    Label _statusLabel = null!;
    Label _summaryLabel = null!;
    CollectionView _resultsView = null!;
    ObservableCollection<ResultRow> _results = new();
    ActivityIndicator _spinner = null!;

    // Pagination — user's explicit ask: page through results with ◀/▶ instead of one
    // long scroll. _allResults holds every matching row from the last check; _results
    // (bound to _resultsView) only ever holds the current page's slice.
    const int PageSize = 15;
    List<ResultRow> _allResults = new();
    int _pageIndex = 0;
    Label _pageLabel = null!;
    Button _btnPrevPage = null!;
    Button _btnNextPage = null!;
    Grid _pagerRow = null!;

    public CheckMyNumber()
    {
        Shell.SetNavBarIsVisible(this, false);
        BackgroundColor = Color.FromArgb("#0F1923");
        BuildLayout();
        BuildNumberEntries(); // initial entry boxes for the default game
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    void BuildLayout()
    {
        // 3 rows, not 2 — the results CollectionView gets its OWN row (GridLength.Star)
        // rather than sharing a ScrollView with the form above it. A CollectionView
        // nested inside a ScrollView is a known MAUI trap: it gets an unconstrained
        // height from the outer ScrollView and silently renders nothing (confirmed
        // live 2026-08-12 — 1831 rows were added to the ObservableCollection, the
        // summary line above it updated correctly, but the list itself stayed
        // completely blank no matter how far down you scrolled). Giving it its own
        // Star row lets it size to the remaining screen space and virtualize/scroll
        // on its own, which is what CollectionView actually expects.
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),  // header
                new RowDefinition(GridLength.Auto),  // form (compact, never needs its own scroll)
                new RowDefinition(GridLength.Auto),  // ◀ Page X of Y ▶
                new RowDefinition(GridLength.Star),  // results list — self-scrolling
            }
        };

        var header = new Grid
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Padding = new Thickness(4, 8),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            }
        };
        var btnBack = new Button { Text = "← Back", BackgroundColor = Colors.Transparent, TextColor = Colors.White, FontSize = 12, Padding = new Thickness(2, 0) };
        btnBack.Clicked += async (_, _) => await Shell.Current.GoToAsync("..", false);
        var title = new Label { Text = "🍀 Check My Numbers", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        var homeLbl = new Label { Text = "⌂", FontSize = 20, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center, Padding = new Thickness(8, 0) };
        homeLbl.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Shell.Current.GoToAsync("//MainPage", false)) });
        header.Add(btnBack, 0, 0);
        header.Add(title, 1, 0);
        header.Add(homeLbl, 2, 0);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _gamePicker = new Picker { Title = "Select Game", TextColor = Colors.White, TitleColor = Color.FromArgb("#8B9DC3"), FontSize = 14 };
        foreach (var g in Games) _gamePicker.Items.Add(g.Label);
        _gamePicker.SelectedIndex = 0;
        _gamePicker.SelectedIndexChanged += (_, _) =>
        {
            if (_gamePicker.SelectedIndex < 0) return;
            _gameIdx = _gamePicker.SelectedIndex;
            BuildNumberEntries();
            ClearResults();
        };

        _rangeLabel = new Label { FontSize = 11, TextColor = Color.FromArgb("#8B9DC3") };

        _numberRow = new HorizontalStackLayout { Spacing = 6, HorizontalOptions = LayoutOptions.Center };

        _bonusLabel = new Label { FontSize = 11, TextColor = Color.FromArgb("#8B9DC3"), IsVisible = false };

        _periodPicker = new Picker { Title = "Time Period", TextColor = Colors.White, TitleColor = Color.FromArgb("#8B9DC3"), FontSize = 14 };
        foreach (var p in Periods) _periodPicker.Items.Add(p.Label);
        _periodPicker.SelectedIndex = 0;
        _periodPicker.SelectedIndexChanged += (_, _) => { if (_periodPicker.SelectedIndex >= 0) _periodIdx = _periodPicker.SelectedIndex; };

        var checkBtn = new Button
        {
            Text = "Check My Numbers", BackgroundColor = Color.FromArgb("#D4A94A"), TextColor = Color.FromArgb("#0F1923"),
            FontAttributes = FontAttributes.Bold, FontSize = 13, CornerRadius = 18,
            Padding = new Thickness(0, 2), HeightRequest = 36, MinimumHeightRequest = 0,
            Margin = new Thickness(0, 6, 0, 2),
        };
        checkBtn.Clicked += async (_, _) => await OnCheckClickedAsync();

        _statusLabel = new Label { FontSize = 12, TextColor = Color.FromArgb("#E0965A"), HorizontalTextAlignment = TextAlignment.Center, IsVisible = false };
        _summaryLabel = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#4CAF7D"), HorizontalTextAlignment = TextAlignment.Center, IsVisible = false, Margin = new Thickness(0, 4) };
        _spinner = new ActivityIndicator { IsRunning = false, IsVisible = false, Color = Color.FromArgb("#D4A94A") };

        _resultsView = new CollectionView
        {
            ItemsSource = _results,
            ItemTemplate = BuildResultTemplate(),
            VerticalOptions = LayoutOptions.Fill,
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical) { ItemSpacing = 6 },
        };

        var formPanel = new VerticalStackLayout
        {
            Spacing = 2, Padding = new Thickness(16, 8),
            Children =
            {
                new Label { Text = "Select Game", FontSize = 12, TextColor = Color.FromArgb("#8B9DC3") },
                _gamePicker,
                new BoxView { HeightRequest = 1, Color = Color.FromArgb("#2A3446"), Margin = new Thickness(0, 3) },
                new Label { Text = "Type Your Numbers", FontSize = 12, TextColor = Color.FromArgb("#8B9DC3") },
                _rangeLabel,
                _numberRow,
                _bonusLabel,
                new BoxView { HeightRequest = 1, Color = Color.FromArgb("#2A3446"), Margin = new Thickness(0, 3) },
                new Label { Text = "Time Period", FontSize = 12, TextColor = Color.FromArgb("#8B9DC3") },
                _periodPicker,
                checkBtn,
                _statusLabel,
                _spinner,
                _summaryLabel,
            }
        };

        // No ScrollView wrapper here — a bare ScrollView placed in a Grid's Auto row
        // measures as wanting to fill all available space rather than just its
        // content's natural height, which starved the results row below down to
        // zero (confirmed live 2026-08-12: the list stayed blank on scroll even
        // after moving it to its own row). formPanel is a plain VerticalStackLayout,
        // which sizes to its actual content height in an Auto row correctly, and
        // it's short enough on any real phone screen to never need to scroll on
        // its own anyway.
        Grid.SetRow(formPanel, 1);
        root.Children.Add(formPanel);

        _pagerRow = BuildPagerRow();
        Grid.SetRow(_pagerRow, 2);
        root.Children.Add(_pagerRow);

        Grid.SetRow(_resultsView, 3);
        root.Children.Add(_resultsView);

        Content = root;
    }

    // ◀ Page X of Y ▶ — sits between the form and the results list, hidden whenever
    // there's nothing to page through (no check run yet, or every matching row fits
    // on one page already).
    Grid BuildPagerRow()
    {
        var grid = new Grid
        {
            IsVisible = false,
            Padding = new Thickness(16, 4),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            }
        };

        _btnPrevPage = new Button
        {
            Text = "◀", BackgroundColor = Color.FromArgb("#2A3446"), TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold, CornerRadius = 8, WidthRequest = 44, HeightRequest = 34,
            Padding = new Thickness(0), MinimumWidthRequest = 0, MinimumHeightRequest = 0,
        };
        _btnPrevPage.Clicked += (_, _) => { if (_pageIndex > 0) { _pageIndex--; ShowPage(); } };

        _pageLabel = new Label
        {
            TextColor = Color.FromArgb("#8B9DC3"), FontSize = 12,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
        };

        _btnNextPage = new Button
        {
            Text = "▶", BackgroundColor = Color.FromArgb("#2A3446"), TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold, CornerRadius = 8, WidthRequest = 44, HeightRequest = 34,
            Padding = new Thickness(0), MinimumWidthRequest = 0, MinimumHeightRequest = 0,
        };
        _btnNextPage.Clicked += (_, _) =>
        {
            int totalPages = Math.Max(1, (_allResults.Count + PageSize - 1) / PageSize);
            if (_pageIndex + 1 < totalPages) { _pageIndex++; ShowPage(); }
        };

        Grid.SetColumn(_btnPrevPage, 0);
        Grid.SetColumn(_pageLabel, 1);
        Grid.SetColumn(_btnNextPage, 2);
        grid.Children.Add(_btnPrevPage);
        grid.Children.Add(_pageLabel);
        grid.Children.Add(_btnNextPage);
        return grid;
    }

    // Slices _allResults down to the current page and pushes just that slice into
    // _results (the CollectionView's bound source) — the full list never gets
    // rendered at once. Also updates the page label and arrow enabled-state, and
    // scrolls the list back to its own top so paging forward/back doesn't leave the
    // view scrolled partway down into the new page's rows.
    void ShowPage()
    {
        int totalPages = Math.Max(1, (_allResults.Count + PageSize - 1) / PageSize);
        _pagerRow.IsVisible = totalPages > 1;

        var pageRows = _allResults.Skip(_pageIndex * PageSize).Take(PageSize);
        _results = new ObservableCollection<ResultRow>(pageRows);
        _resultsView.ItemsSource = _results;
        if (_results.Count > 0) _resultsView.ScrollTo(0, position: ScrollToPosition.Start, animate: false);

        _pageLabel.Text = $"Page {_pageIndex + 1} of {totalPages}";
        _btnPrevPage.IsEnabled = _pageIndex > 0;
        _btnNextPage.IsEnabled = _pageIndex + 1 < totalPages;
        _btnPrevPage.Opacity = _btnPrevPage.IsEnabled ? 1.0 : 0.4;
        _btnNextPage.Opacity = _btnNextPage.IsEnabled ? 1.0 : 0.4;
    }

    // Numeric entry boxes are rebuilt from scratch whenever the game changes,
    // since the count/range/bonus differ per game — simpler than trying to
    // reuse/resize a fixed set of entries.
    void BuildNumberEntries()
    {
        var g = Games[_gameIdx];
        _rangeLabel.Text = $"Range: {g.MinN}-{g.MaxN}" + (g.Count > 1 ? $" ({g.Count} numbers, no repeats)" : "");

        _numberRow.Children.Clear();
        _numberEntries = new Entry[g.Count];
        for (int i = 0; i < g.Count; i++)
        {
            var entry = new Entry
            {
                WidthRequest = 46, HeightRequest = 42, Keyboard = Keyboard.Numeric,
                HorizontalTextAlignment = TextAlignment.Center, FontAttributes = FontAttributes.Bold,
                BackgroundColor = Color.FromArgb("#1A2230"), TextColor = Colors.White,
                MaxLength = g.MaxN >= 10 ? 2 : 1,
            };
            int i_ = i;
            EntryHelper.AttachBackspace(entry, () => RetreatFocus(i_));
            entry.TextChanged += (s, e) =>
            {
                string nv = e.NewTextValue ?? "";
                if (nv.Length == entry.MaxLength && int.TryParse(nv, out int cv) && cv >= 1) AdvanceFocus(i_);
            };
            _numberEntries[i] = entry;
            _numberRow.Children.Add(entry);
        }

        if (g.BonusMax > 0)
        {
            _bonusEntry = new Entry
            {
                WidthRequest = 52, HeightRequest = 42, Keyboard = Keyboard.Numeric,
                HorizontalTextAlignment = TextAlignment.Center, FontAttributes = FontAttributes.Bold,
                BackgroundColor = Color.FromArgb("#2E2338"), TextColor = Color.FromArgb("#CE93D8"),
                MaxLength = 2,
            };
            EntryHelper.AttachBackspace(_bonusEntry, () => RetreatFocus(g.Count));
            _numberRow.Children.Add(_bonusEntry);
            _bonusLabel.Text = $"{g.BonusLabel} range: 1-{g.BonusMax}";
            _bonusLabel.IsVisible = true;
        }
        else
        {
            _bonusEntry = null;
            _bonusLabel.IsVisible = false;
        }
    }

    // index i runs 0..(_numberEntries.Length-1) for the main boxes, then _numberEntries.Length
    // for the bonus box (if present) — same linear-sequence convention the rest of the app's
    // number-entry rows use (see SuperLottoPage's AdvanceFocus/RetreatFocus).
    void AdvanceFocus(int i)
    {
        if (i + 1 < _numberEntries.Length) { _numberEntries[i + 1].Focus(); return; }
        if (i == _numberEntries.Length - 1 && _bonusEntry != null) _bonusEntry.Focus();
    }

    void RetreatFocus(int i)
    {
        if (i == _numberEntries.Length && _numberEntries.Length > 0) { EntryHelper.SelectAll(_numberEntries[^1]); return; }
        if (i > 0) EntryHelper.SelectAll(_numberEntries[i - 1]);
    }

    void ClearResults()
    {
        _allResults.Clear();
        _pageIndex = 0;
        _results.Clear();
        _pagerRow.IsVisible = false;
        _summaryLabel.IsVisible = false;
        _statusLabel.IsVisible = false;
    }

    // ── Check ─────────────────────────────────────────────────────────────────

    async Task OnCheckClickedAsync()
    {
        _statusLabel.IsVisible = false;
        var g = Games[_gameIdx];

        if (!TryParseNumbers(g, out int[] main, out int bonus, out string error))
        {
            _statusLabel.Text = error;
            _statusLabel.IsVisible = true;
            return;
        }

        _spinner.IsRunning = true;
        _spinner.IsVisible = true;
        ClearResults();

        try
        {
            var draws = await LoadDrawsAsync(g);
            if (draws.Count == 0)
            {
                _statusLabel.Text = "No draw history found on this device for this game yet.";
                _statusLabel.IsVisible = true;
                return;
            }

            var period = Periods[_periodIdx];
            var cutoff = period.Years.HasValue ? DateTime.Today.AddYears(-period.Years.Value) : DateTime.Today.AddMonths(-period.Months!.Value);
            var inRange = draws.Where(d => d.Date >= cutoff).OrderByDescending(d => d.Date).ThenByDescending(d => d.Session).ToList();

            // Built as a plain List first and assigned to a fresh ObservableCollection
            // in one shot, not via 1000+ individual .Add() calls — cheaper, and avoids
            // firing that many CollectionChanged notifications at once.
            // Stats (bestMatch/exactCount) are computed over every draw actually
            // checked, but rows with zero main-number matches (0/5, 0/3, etc.) are
            // never added to the displayed list — user's explicit call: a draw where
            // none of your numbers showed up isn't worth scrolling past.
            int bestMatch = 0, exactCount = 0;
            var rows = new List<ResultRow>(inRange.Count);
            foreach (var d in inRange)
            {
                var row = BuildResultRow(g, d, main, bonus);
                if (row.MatchCount > 0) rows.Add(row);
                if (row.MatchCount > bestMatch) bestMatch = row.MatchCount;
                if (row.MatchCount == g.Count && (g.BonusMax == 0 || row.BonusMatched)) exactCount++;
            }
            _allResults = rows;
            _pageIndex = 0;
            ShowPage();

            _summaryLabel.Text = $"Checked {inRange.Count} draws over {period.Label} — best match: {bestMatch}/{g.Count}{(g.BonusMax > 0 ? "+bonus" : "")} · exact match {g.Count}/{g.Count}{(g.BonusMax > 0 ? "+bonus" : "")}: {exactCount} time{(exactCount == 1 ? "" : "s")}";
            _summaryLabel.IsVisible = true;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Couldn't read draw history — {ex.Message}";
            _statusLabel.IsVisible = true;
        }
        finally
        {
            _spinner.IsRunning = false;
            _spinner.IsVisible = false;
        }
    }

    bool TryParseNumbers((string Key, string Label, string Csv, int Count, int MinN, int MaxN, int BonusMax, string BonusLabel) g, out int[] main, out int bonus, out string error)
    {
        main = new int[g.Count];
        bonus = 0;
        error = "";

        var seen = new HashSet<int>();
        for (int i = 0; i < g.Count; i++)
        {
            string text = _numberEntries[i].Text?.Trim() ?? "";
            if (!int.TryParse(text, out int n) || n < g.MinN || n > g.MaxN)
            {
                error = $"Number {i + 1} must be {g.MinN}-{g.MaxN}.";
                return false;
            }
            // D3/D4 digits are allowed to repeat (e.g. "7 7 3"); lotto games are not.
            if (g.MaxN >= 10 && !seen.Add(n))
            {
                error = "Numbers can't repeat.";
                return false;
            }
            main[i] = n;
        }

        if (g.BonusMax > 0)
        {
            string text = _bonusEntry?.Text?.Trim() ?? "";
            if (!int.TryParse(text, out bonus) || bonus < 1 || bonus > g.BonusMax)
            {
                error = $"{g.BonusLabel} must be 1-{g.BonusMax}.";
                return false;
            }
        }

        return true;
    }

    // ── Local draw history (this app's own cached copy — see file header) ──────

    record DrawRecord(DateTime Date, string? Session, int[] Main, int Bonus);

    static async Task<List<DrawRecord>> LoadDrawsAsync((string Key, string Label, string Csv, int Count, int MinN, int MaxN, int BonusMax, string BonusLabel) g)
    {
        string localPath = Path.Combine(FileSystem.AppDataDirectory, "data", g.Csv);
        string text;
        if (File.Exists(localPath))
        {
            text = await File.ReadAllTextAsync(localPath);
        }
        else
        {
            using var asset = await FileSystem.OpenAppPackageFileAsync("data/" + g.Csv);
            using var reader = new StreamReader(asset);
            text = await reader.ReadToEndAsync();
        }

        // One ReadAllTextAsync + in-memory split, not an awaited ReadLineAsync per
        // line — confirmed live 2026-08-12 that awaiting line-by-line on an 11,800+
        // row file (Fantasy 5's 5-year window alone is ~1,800 rows) made "Check My
        // Numbers" take 15-20+ seconds; each `await` carries real per-call overhead
        // that adds up fast at this row count. This version parses the same file in
        // well under a second.
        var lines = text.Split('\n');
        var list = new List<DrawRecord>(lines.Length);
        bool hasSession = lines.Length > 0 && lines[0].Contains("DrawTime");

        for (int li = 1; li < lines.Length; li++)
        {
            string line = lines[li].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(',');
            // Layout: DrawDate,DrawNumber,N1..N{Count}[,Bonus][,DrawTime]
            if (parts.Length < 2 + g.Count) continue;
            if (!DateTime.TryParse(parts[0], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;

            var main = new int[g.Count];
            bool ok = true;
            for (int i = 0; i < g.Count; i++)
            {
                if (!int.TryParse(parts[2 + i], out main[i])) { ok = false; break; }
            }
            if (!ok) continue;

            int bonus = 0;
            if (g.BonusMax > 0 && parts.Length > 2 + g.Count)
                int.TryParse(parts[2 + g.Count], out bonus);

            string? session = hasSession && parts.Length > 2 + g.Count ? parts[^1].Trim() : null;

            list.Add(new DrawRecord(date, session, main, bonus));
        }
        return list;
    }

    // ── Result rows ──────────────────────────────────────────────────────────

    // Real auto-properties, not fields — MAUI's SetBinding resolves a string path
    // via PropertyInfo reflection and silently no-ops on fields (no error, no
    // exception, just an empty bound Label forever). Confirmed live 2026-08-12:
    // this was the actual reason the results list rendered as 1831 real, correctly
    // laid-out RecyclerView rows (visible in a uiautomator dump, right bounds,
    // right count) that were all just blank — every TextView's text="" once these
    // were fields instead of properties.
    // Plain data, not Views — BuildResultRow ran for all ~1800 draws up front
    // building a real Border+Ellipse+Label tree per ball (confirmed live
    // 2026-08-12: this alone pushed "Check My Numbers" back over several
    // seconds, since it defeats the whole point of the CollectionView only
    // virtualizing what's actually on screen). Keeping rows as cheap POCOs and
    // letting BindableLayout build the actual ball Views only when a row's
    // DataTemplate is instantiated (i.e. only for on-screen + buffer rows)
    // restores the fast path.
    public class BallInfo
    {
        public string Text { get; set; } = "";
        public Color Fill { get; set; } = Color.FromArgb("#2A3446");
        public Color Ink { get; set; } = Color.FromArgb("#8B9DC3");
        public Thickness Margin { get; set; } = new Thickness(0);
    }

    public class ResultRow
    {
        public string DateText { get; set; } = "";
        public List<BallInfo> Balls { get; set; } = new();
        public string MatchText { get; set; } = "";
        public int MatchCount { get; set; }
        public bool BonusMatched { get; set; }
    }

    // Same Border+Ellipse ball look every other page in this app uses for drawn
    // numbers (see ResultsRendererEnhanced.MakePlayerBalls/MakeWinningBalls) —
    // filled circle, centered bold white digits. Colors here are this page's own
    // (green = your match, purple = bonus match, matching the "🟢 Green = Win"
    // convention used elsewhere), not a copy of that file's specific palette —
    // the shape/style is what needed to match, not the exact hex values.
    static DataTemplate BuildBallTemplate() => new(() =>
    {
        var label = new Label
        {
            FontSize = 14, FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center,
        };
        label.SetBinding(Label.TextProperty, nameof(BallInfo.Text));
        label.SetBinding(Label.TextColorProperty, nameof(BallInfo.Ink));

        var border = new Border
        {
            WidthRequest = 32, HeightRequest = 32,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.Ellipse(),
            Content = label,
        };
        border.SetBinding(Border.BackgroundColorProperty, nameof(BallInfo.Fill));
        border.SetBinding(View.MarginProperty, nameof(BallInfo.Margin));
        return border;
    });

    static ResultRow BuildResultRow((string Key, string Label, string Csv, int Count, int MinN, int MaxN, int BonusMax, string BonusLabel) g, DrawRecord d, int[] userMain, int userBonus)
    {
        var row = new ResultRow
        {
            DateText = d.Date.ToString("M/d/yy") + (d.Session != null ? $" ({d.Session})" : ""),
        };

        // "Box"-style multiset match for digit games (a drawn duplicate can only
        // satisfy one of your matching digits) — see file header comment. Also
        // correct for the non-repeating lotto games, since it's just a plain
        // set-overlap count when neither side has duplicates.
        var matchedIdx = new bool[d.Main.Length];
        foreach (int u in userMain)
        {
            for (int k = 0; k < d.Main.Length; k++)
            {
                if (!matchedIdx[k] && d.Main[k] == u) { matchedIdx[k] = true; row.MatchCount++; break; }
            }
        }

        for (int k = 0; k < d.Main.Length; k++)
        {
            row.Balls.Add(matchedIdx[k]
                ? new BallInfo { Text = d.Main[k].ToString("00"), Fill = Color.FromArgb("#4CAF7D"), Ink = Colors.White }
                : new BallInfo { Text = d.Main[k].ToString("00") });
        }

        if (g.BonusMax > 0)
        {
            row.BonusMatched = d.Bonus == userBonus;
            // Extra left margin stands in for the "·" separator the other
            // renderers use — cheaper than adding a separate label item, and
            // reads the same visually (bonus ball set apart from the main run).
            row.Balls.Add(row.BonusMatched
                ? new BallInfo { Text = d.Bonus.ToString("00"), Fill = Color.FromArgb("#CE93D8"), Ink = Colors.White, Margin = new Thickness(6, 0, 0, 0) }
                : new BallInfo { Text = d.Bonus.ToString("00"), Margin = new Thickness(6, 0, 0, 0) });
        }

        row.MatchText = $"{row.MatchCount}/{g.Count}" + (row.BonusMatched ? "+B" : "");
        return row;
    }

    static DataTemplate BuildResultTemplate() => new(() =>
    {
        var dateLabel = new Label { FontSize = 11, TextColor = Color.FromArgb("#8B9DC3"), WidthRequest = 60, VerticalTextAlignment = TextAlignment.Center };
        dateLabel.SetBinding(Label.TextProperty, nameof(ResultRow.DateText));

        var numbersHost = new FlexLayout
        {
            Direction = FlexDirection.Row,
            JustifyContent = FlexJustify.SpaceEvenly,
            AlignItems = FlexAlignItems.Center,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
        };
        BindableLayout.SetItemTemplate(numbersHost, BuildBallTemplate());
        numbersHost.SetBinding(BindableLayout.ItemsSourceProperty, nameof(ResultRow.Balls));

        var matchLabel = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#D4A94A"), WidthRequest = 44, HorizontalTextAlignment = TextAlignment.End, VerticalTextAlignment = TextAlignment.Center };
        matchLabel.SetBinding(Label.TextProperty, nameof(ResultRow.MatchText));

        var grid = new Grid
        {
            Padding = new Thickness(12, 7),
            ColumnSpacing = 6,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        grid.Add(dateLabel, 0, 0);
        grid.Add(numbersHost, 1, 0);
        grid.Add(matchLabel, 2, 0);

        var border = new Border
        {
            Content = grid, StrokeThickness = 0,
            Background = Colors.Transparent,
            Stroke = Color.FromArgb("#1E2733"),
        };
        return border;
    });
}
