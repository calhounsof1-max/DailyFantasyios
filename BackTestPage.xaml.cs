using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

public partial class BackTestPage : ContentPage
{
    // ── Game config ───────────────────────────────────────────────

    enum InputMode { Digits3, Digits4, Numbers5, Numbers5Plus1, Numbers3Ordered }

    record GameDef(
        string    Name,
        string    AssetName,   // "" = API-only
        string    LocalFile,   // "" = API-only
        InputMode Mode,
        int       MainCount,
        int       MaxBall,
        int       MinBall,
        bool      HasSpecial,
        int       SpecialMax,
        Color     BallColor,
        string    AccentHex,
        int       ApiGameId = 0   // 0 = CSV-only
    );

    static readonly GameDef[] _games =
    [
        new("Fantasy 5",    "data/myFantasy5.csv",  "myFantasy5.csv",  InputMode.Numbers5,      5, 39,  1, false,  0, Color.FromArgb("#FF8F00"), "#FF8F00"),
        new("Super Lotto",  "data/mySuperlotto.csv","mySuperlotto.csv",InputMode.Numbers5Plus1, 5, 47,  1, true,  27, Color.FromArgb("#7B1FA2"), "#7B1FA2"),
        new("Powerball",    "data/myPowerball.csv",  "myPowerball.csv",  InputMode.Numbers5Plus1, 5, 69,  1, true,  26, Color.FromArgb("#C62828"), "#C62828"),
        new("Mega Millions","data/myMegaMillions.csv","myMegaMillions.csv",InputMode.Numbers5Plus1, 5, 70,  1, true,  25, Color.FromArgb("#E65100"), "#E65100"),
        new("Daily 3",      "data/myDaily3.csv",    "myDaily3.csv",    InputMode.Digits3,       3,  9,  0, false,  0, Color.FromArgb("#1565C0"), "#1565C0"),
        new("Daily 4",      "data/myDaily4.csv",     "myDaily4.csv",     InputMode.Digits4,       4,  9,  0, false,  0, Color.FromArgb("#0D47A1"), "#0D47A1"),
        new("Daily Derby",  "", "",                                     InputMode.Numbers3Ordered,3,12,  1, false,  0, Color.FromArgb("#4A148C"), "#4A148C", ApiGameId: 11),
    ];

    int  _selGame        = 0;
    bool _numbersVisible = true;

    // Stored for re-filtering without re-running
    List<MatchResult> _lastResults = [];
    List<(int[] Main, int Special, string Label)> _lastPicks = [];
    GameDef? _lastGame;
    int _activeFilter = -1; // -1 = All

    public static string PresetGame { get; set; } = "Fantasy 5";

    // ── ctor ──────────────────────────────────────────────────────

    public BackTestPage()
    {
        InitializeComponent();
        foreach (var g in _games)
            gamePicker.Items.Add(g.Name);
        gamePicker.SelectedIndex = 0;
        UpdateGameLogo();
        AddNumberRow(scroll: false);
        AddNumberRow(scroll: false);
        AddNumberRow(scroll: false);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        int idx = Array.FindIndex(_games, g => g.Name == PresetGame);
        if (idx >= 0)
        {
            _selGame = idx;
            gamePicker.SelectedIndex = idx;
        }
        UpdateGameUI();
        UpdateGameLogo();
    }

    // ── Collapse / expand numbers ─────────────────────────────────

    private void NumbersHeader_Tapped(object? sender, TappedEventArgs e)
    {
        _numbersVisible = !_numbersVisible;
        numbersScrollView.IsVisible = _numbersVisible;
        lblChevron.Text = _numbersVisible ? "▼" : "▶";
        lblNumsHeader.Text = _numbersVisible
            ? "Your Picks to Test (tap to hide)"
            : "Your Picks to Test (tap to show)";
    }

    // ── Add / clear number rows ───────────────────────────────────

    private void BtnAddRow_Clicked(object? sender, EventArgs e) => AddNumberRow(scroll: true);

    private void BtnClearRows_Clicked(object? sender, EventArgs e)
    {
        int rowCount = Math.Max(numbersContainer.Children.Count, 3);
        numbersContainer.Children.Clear();
        for (int i = 0; i < rowCount; i++)
            AddNumberRow(scroll: false);
        ClearResults();
    }

    void AddNumberRow(bool scroll = true)
    {
        int num  = numbersContainer.Children.Count + 1;
        var game = _games[_selGame];

        bool isDigit    = game.Mode is InputMode.Digits3 or InputMode.Digits4;
        bool isDerby    = game.Mode == InputMode.Numbers3Ordered;
        int  mainCount  = game.MainCount;
        bool hasSpecial = game.HasSpecial;
        int  totalBoxes = mainCount + (hasSpecial ? 1 : 0);
        int  maxLen     = isDigit ? 1 : 2;
        int  maxBall    = game.MaxBall;

        var numLabel = new Label
        {
            Text           = $"#{num}",
            FontSize       = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor      = Color.FromArgb("#6B7280"),
            VerticalOptions= LayoutOptions.Center,
            WidthRequest   = 36,
            LineBreakMode  = LineBreakMode.NoWrap,
            Margin         = new Thickness(0, 0, 4, 0)
        };

        var boxRow  = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        var entries = new List<Entry>();
        Grid? rowGrid = null;

        for (int i = 0; i < totalBoxes; i++)
        {
            bool isSpecial = hasSpecial && i == mainCount;
            if (isSpecial)
                boxRow.Children.Add(new Label
                {
                    Text = "+", FontSize = 14, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#9CA3AF"),
                    VerticalOptions = LayoutOptions.Center,
                    Margin = new Thickness(2, 0)
                });

            var entry = new Entry
            {
                Keyboard               = Keyboard.Numeric,
                WidthRequest           = isDigit ? 48 : hasSpecial ? 46 : 54,
                HeightRequest          = 48,
                HorizontalTextAlignment= TextAlignment.Center,
                BackgroundColor        = isSpecial
                                             ? Color.FromArgb("#EDE7F6")
                                             : Color.FromArgb("#E3F2FD"),
                TextColor              = Color.FromArgb("#1A1A1A"),
                FontSize               = 20,
                FontAttributes         = FontAttributes.Bold,
            };

            int capturedIdx = i;
            entry.TextChanged += (s, ev) =>
            {
                var en  = (Entry)s!;
                string txt = ev.NewTextValue ?? "";

                // Enforce max length
                if (txt.Length > maxLen) { en.Text = txt[..maxLen]; return; }

                // For digit games (D3/D4): clamp to 0–9
                if (isDigit && txt.Length == 1 && int.TryParse(txt, out int dv) && dv > 9)
                    { en.Text = "9"; return; }

                // For Derby: clamp value to 1–12; if first digit > 1, it must be single-digit horse
                if (isDerby && txt.Length > 0 && int.TryParse(txt, out int val))
                {
                    if (val > maxBall) { en.Text = maxBall.ToString(); return; }
                    // If first digit is 2–9, it's a complete single-digit number (no valid 2-digit horse starts with 2+)
                    if (txt.Length == 1 && val >= 2)
                    {
                        // Auto-advance: single digit > 1 is a complete horse number
                        void Advance()
                        {
                            if (capturedIdx < entries.Count - 1)
                                entries[capturedIdx + 1].Focus();
                        }
                        MainThread.BeginInvokeOnMainThread(Advance);
                        return;
                    }
                }

                if (txt.Length == maxLen)
                {
                    if (capturedIdx < entries.Count - 1)
                        entries[capturedIdx + 1].Focus();
                    else if (rowGrid != null)
                    {
                        int rowIdx = numbersContainer.Children.IndexOf(rowGrid);
                        if (rowIdx >= 0 && rowIdx + 1 < numbersContainer.Children.Count &&
                            numbersContainer.Children[rowIdx + 1] is Grid nextGrid)
                        {
                            foreach (var c in nextGrid.Children)
                                if (c is HorizontalStackLayout hsl)
                                {
                                    hsl.Children.OfType<Entry>().FirstOrDefault()?.Focus();
                                    break;
                                }
                        }
                    }
                }
            };

            entries.Add(entry);
            boxRow.Children.Add(entry);
        }

        var removeVisual = new Border
        {
            BackgroundColor = Color.FromArgb("#E5E7EB"),
            StrokeThickness = 0,
            StrokeShape     = new RoundRectangle { CornerRadius = 4 },
            WidthRequest    = 30, HeightRequest = 30,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions   = LayoutOptions.Center,
            Padding = new Thickness(0),
            Content = new Label
            {
                Text                    = "×",
                FontSize                = 17,
                FontAttributes          = FontAttributes.Bold,
                TextColor               = Color.FromArgb("#6B7280"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment   = TextAlignment.Center,
            }
        };
        var removeBtn = new Grid
        {
            WidthRequest    = 44, HeightRequest = 44,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Colors.Transparent,
        };
        removeBtn.Children.Add(removeVisual);

        rowGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Padding         = new Thickness(4, 3),
            BackgroundColor = Colors.White
        };
        rowGrid.Add(numLabel,  0, 0);
        rowGrid.Add(boxRow,    1, 0);
        rowGrid.Add(removeBtn, 2, 0);

        var removeTap = new TapGestureRecognizer();
        removeTap.Tapped += (s, e) =>
        {
            numbersContainer.Children.Remove(rowGrid);
            RenumberRows();
        };
        removeBtn.GestureRecognizers.Add(removeTap);

        numbersContainer.Children.Add(rowGrid);

        if (scroll)
            MainThread.BeginInvokeOnMainThread(async () =>
                await numbersScrollView.ScrollToAsync(0, numbersScrollView.ContentSize.Height, false));
    }

    void RenumberRows()
    {
        int i = 1;
        foreach (var child in numbersContainer.Children)
            if (child is Grid g && g.Children.FirstOrDefault() is Label lbl)
                lbl.Text = $"#{i++}";
    }

    // ── Events ────────────────────────────────────────────────────

    private void GamePicker_Changed(object? sender, EventArgs e)
    {
        int i = gamePicker.SelectedIndex;
        if (i < 0) return;
        _selGame = i;
        UpdateGameUI();
        ClearResults();
        UpdateGameLogo();
    }

    private async void BtnGoHome_Clicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..", false);

    private async void BtnRun_Clicked(object? sender, EventArgs e)
    {
        var game = _games[_selGame];

        // Collect number rows
        var picks = new List<(int[] Main, int Special, string Label)>();
        foreach (var child in numbersContainer.Children)
        {
            if (child is not Grid g) continue;
            HorizontalStackLayout? boxRow = null;
            foreach (var c in g.Children)
                if (c is HorizontalStackLayout hsl) { boxRow = hsl; break; }
            if (boxRow == null) continue;

            var vals = boxRow.Children.OfType<Entry>()
                             .Select(en => en.Text?.Trim() ?? "")
                             .ToList();
            if (vals.All(string.IsNullOrWhiteSpace)) continue;

            string combined = game.Mode is InputMode.Digits3 or InputMode.Digits4
                ? string.Join("", vals)
                : string.Join(" ", vals);

            var (main, special, err) = ParseInput(game, combined);
            if (err != null) { lblStatus.Text = $"Row #{picks.Count + 1}: {err}"; return; }
            picks.Add((main, special, combined));
        }

        if (picks.Count == 0)
        {
            lblStatus.Text = "Enter at least one number row to test.";
            return;
        }

        // Date range from month/year pickers
        int fromYear  = pickerFromYear.SelectedIndex  >= 0 ? int.Parse(pickerFromYear.Items[pickerFromYear.SelectedIndex])   : 1992;
        int fromMonth = pickerFromMonth.SelectedIndex >= 0 ? pickerFromMonth.SelectedIndex + 1 : 1;
        int toYear    = pickerToYear.SelectedIndex    >= 0 ? int.Parse(pickerToYear.Items[pickerToYear.SelectedIndex])       : DateTime.Today.Year;
        int toMonth   = pickerToMonth.SelectedIndex   >= 0 ? pickerToMonth.SelectedIndex + 1   : DateTime.Today.Month;
        DateTime fromDate = new DateTime(fromYear, fromMonth, 1);
        DateTime toDate   = new DateTime(toYear,   toMonth,   DateTime.DaysInMonth(toYear, toMonth));
        if (fromDate > toDate) (fromDate, toDate) = (toDate, fromDate);

        btnRun.IsEnabled = false;
        ClearResults();
        loadingOverlay.IsVisible = true;
        SetMsg("Loading draw history...");

        try
        {
            List<CsvDraw> draws;
            if (game.ApiGameId > 0)
            {
                SetMsg("Fetching draws from CA Lottery...");
                draws = await LoadApiAsync(game, fromDate, toDate);
            }
            else
            {
                draws = await LoadCsvAsync(game, fromDate, toDate);
            }

            if (draws.Count == 0)
            {
                lblStatus.Text = "No draw data found for the selected game/range.";
                return;
            }

            SetMsg($"Analyzing {draws.Count:N0} draws...");
            await Task.Yield();

            var results = await Task.Run(() => RunBackTest(game, draws, picks));
            _lastResults  = results;
            _lastPicks    = picks;
            _lastGame     = game;
            _activeFilter = -1;
            BuildStatsCard(game, draws.Count, results, fromDate, toDate);
            BuildFilterBar(game);
            BuildResultsList(game, results, picks);

            // Auto-collapse the picks section so results are visible
            _numbersVisible = false;
            numbersScrollView.IsVisible = false;
            lblChevron.Text    = "▶";
            lblNumsHeader.Text = "Your Picks to Test (tap to show)";

            lblStatus.Text = $"{draws.Count:N0} draws analyzed  •  {picks.Count} pick set(s)  •  {game.Name}";
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            btnRun.IsEnabled         = true;
            loadingOverlay.IsVisible = false;
        }
    }

    // ── CSV loading ───────────────────────────────────────────────

    record CsvDraw(string DateRaw, int Year, int[] Main, int Special);

    async Task<List<CsvDraw>> LoadCsvAsync(GameDef game, DateTime fromDate, DateTime toDate)
    {
        string localPath = System.IO.Path.Combine(FileSystem.AppDataDirectory, "data", game.LocalFile);
        Stream stream = File.Exists(localPath)
            ? File.OpenRead(localPath)
            : await FileSystem.OpenAppPackageFileAsync(game.AssetName);

        var draws = new List<CsvDraw>();
        using (stream)
        using (var reader = new StreamReader(stream))
        {
            bool header = true;
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (header) { header = false; continue; }
                if (string.IsNullOrWhiteSpace(line)) continue;
                var p = line.Split(',');

                string dateRaw = p.Length > 0 ? p[0].Trim() : "";
                if (!DateTime.TryParse(dateRaw, out var dt)) continue;
                if (dt.Date < fromDate.Date || dt.Date > toDate.Date) continue;

                int[]  main    = [];
                int    special = 0;

                if (game.Mode == InputMode.Digits3)
                {
                    if (p.Length >= 5 &&
                        int.TryParse(p[2].Trim(), out int d1) &&
                        int.TryParse(p[3].Trim(), out int d2) &&
                        int.TryParse(p[4].Trim(), out int d3))
                        main = [d1, d2, d3];
                    else continue;
                }
                else if (game.Mode == InputMode.Digits4)
                {
                    if (p.Length >= 6 &&
                        int.TryParse(p[2].Trim(), out int d1) &&
                        int.TryParse(p[3].Trim(), out int d2) &&
                        int.TryParse(p[4].Trim(), out int d3) &&
                        int.TryParse(p[5].Trim(), out int d4))
                        main = [d1, d2, d3, d4];
                    else continue;
                }
                else if (game.Mode == InputMode.Numbers5)
                {
                    if (p.Length >= 7 &&
                        int.TryParse(p[2].Trim(), out int n1) &&
                        int.TryParse(p[3].Trim(), out int n2) &&
                        int.TryParse(p[4].Trim(), out int n3) &&
                        int.TryParse(p[5].Trim(), out int n4) &&
                        int.TryParse(p[6].Trim(), out int n5))
                        main = [n1, n2, n3, n4, n5];
                    else continue;
                }
                else // Numbers5Plus1 (Super Lotto)
                {
                    if (p.Length >= 8 &&
                        int.TryParse(p[2].Trim(), out int n1) &&
                        int.TryParse(p[3].Trim(), out int n2) &&
                        int.TryParse(p[4].Trim(), out int n3) &&
                        int.TryParse(p[5].Trim(), out int n4) &&
                        int.TryParse(p[6].Trim(), out int n5) &&
                        int.TryParse(p[7].Trim(), out int mg))
                    {
                        main    = [n1, n2, n3, n4, n5];
                        special = mg;
                    }
                    else if (p.Length >= 7 &&
                        int.TryParse(p[2].Trim(), out int m1) &&
                        int.TryParse(p[3].Trim(), out int m2) &&
                        int.TryParse(p[4].Trim(), out int m3) &&
                        int.TryParse(p[5].Trim(), out int m4) &&
                        int.TryParse(p[6].Trim(), out int m5))
                        main = [m1, m2, m3, m4, m5];
                    else continue;
                }

                draws.Add(new CsvDraw(dateRaw, dt.Year, main, special));
            }
        }
        return draws;
    }

    // ── API loading (for games without CSV) ──────────────────────

    async Task<List<CsvDraw>> LoadApiAsync(GameDef game, DateTime fromDate, DateTime toDate)
    {
        bool hasSpecial = game.HasSpecial;
        int  mainCount  = game.MainCount;
        bool isDigit    = game.Mode is InputMode.Digits3 or InputMode.Digits4;

        var draws = new List<CsvDraw>();
        int page  = 1;
        const int pageSize = 50;
        const int maxPages = 60; // ~3000 draws max

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("Accept", "application/json, */*");
        client.DefaultRequestHeaders.Add("Referer", "https://www.calottery.com/");

        while (page <= maxPages)
        {
            SetMsg($"Fetching page {page} of draws...");
            string url  = "https://www.calottery.com/api/DrawGameApi/" +
                          $"DrawGamePastDrawResults/{game.ApiGameId}/{page}/{pageSize}";
            string json = await client.GetStringAsync(url).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("PreviousDraws", out var drawsEl) ||
                drawsEl.GetArrayLength() == 0)
                break;

            bool tooOld = false;
            foreach (var d in drawsEl.EnumerateArray())
            {
                string dateRaw = d.TryGetProperty("DrawDate", out var de) ? de.GetString() ?? "" : "";
                if (!DateTime.TryParse(dateRaw, out var dt)) continue;
                if (dt.Date < fromDate.Date) { tooOld = true; break; }
                if (dt.Date > toDate.Date)   continue;

                var (main, special) = ParseApiNumbers(d, mainCount, hasSpecial);
                if (main.Length == mainCount)
                    draws.Add(new CsvDraw(dateRaw, dt.Year, main, special));
            }

            if (tooOld) break;
            page++;
            await Task.Delay(300).ConfigureAwait(false);
        }

        return draws;
    }

    static (int[] Main, int Special) ParseApiNumbers(JsonElement draw, int mainCount, bool hasSpecial)
    {
        if (!draw.TryGetProperty("WinningNumbers", out var wn) ||
            wn.ValueKind != JsonValueKind.Object)
            return ([], 0);

        bool zeroBased = wn.TryGetProperty("0", out _);
        int  start     = zeroBased ? 0 : 1;
        int  total     = hasSpecial ? mainCount + 1 : mainCount;

        var main    = new List<int>();
        int special = 0;

        for (int i = start; i < start + total; i++)
        {
            if (!wn.TryGetProperty(i.ToString(), out var el)) break;
            int  n         = 0;
            bool isSpecial = false;
            if (el.ValueKind == JsonValueKind.Object)
            {
                if (!el.TryGetProperty("Number", out var np)) continue;
                if (!int.TryParse(np.GetRawText().Trim('"'), out n)) continue;
                if (el.TryGetProperty("IsSpecial", out var sp)) isSpecial = sp.GetBoolean();
            }
            else if (el.ValueKind == JsonValueKind.Number) n = el.GetInt32();
            else if (el.ValueKind == JsonValueKind.String)
            { if (!int.TryParse(el.GetString(), out n)) continue; }
            else continue;

            if (isSpecial) special = n;
            else           main.Add(n);
        }

        if (hasSpecial && special == 0 && main.Count == mainCount + 1)
        {
            special = main[mainCount];
            main.RemoveAt(mainCount);
        }

        return (main.ToArray(), special);
    }

    // ── Back-test engine ──────────────────────────────────────────

    record MatchResult(
        CsvDraw Draw,
        int     BestLevel,       // 0=miss, 1=partial, 2=win across all picks
        string  BestLabel,
        int     BestMainCount,   // max main-number matches across all picks
        List<(int Level, string Label)> PickMatches
    );

    List<MatchResult> RunBackTest(
        GameDef game,
        List<CsvDraw> draws,
        List<(int[] Main, int Special, string Label)> picks)
    {
        var results = new List<MatchResult>();

        foreach (var draw in draws)
        {
            var pickMatches = picks.Select(p =>
                Classify(game.Mode, draw.Main, draw.Special, p.Main, p.Special)).ToList();

            int    bestLevel = pickMatches.Max(m => m.Level);
            string bestLabel = pickMatches.Where(m => m.Level == bestLevel)
                                          .Select(m => m.Label).First();

            int bestMainCount = picks.Max(p =>
                game.Mode is InputMode.Digits3 or InputMode.Digits4
                    ? (draw.Main.SequenceEqual(p.Main) ? game.MainCount :
                       draw.Main.OrderBy(x => x).SequenceEqual(p.Main.OrderBy(x => x)) ? game.MainCount - 1 : 0)
                    : game.Mode == InputMode.Numbers3Ordered
                    ? p.Main.Where((n, i) => i < draw.Main.Length && draw.Main[i] == n).Count()
                    : p.Main.Count(n => draw.Main.Contains(n)));

            if (bestLevel > 0)
                results.Add(new MatchResult(draw, bestLevel, bestLabel, bestMainCount, pickMatches));
        }

        return results;
    }

    // ── Stats card ────────────────────────────────────────────────

    void BuildStatsCard(
        GameDef game,
        int totalDraws,
        List<MatchResult> results,
        DateTime fromDate, DateTime toDate)
    {
        // Title
        lblStatsTitle.Text = $"Back-Test  —  {game.Name}";
        lblStatsDraws.Text = $"{totalDraws:N0} draws";

        // Count by level label
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in results)
            foreach (var (level, label) in r.PickMatches)
                if (level > 0)
                    counts[label] = counts.GetValueOrDefault(label) + 1;

        // Build tier rows
        statsTiersHost.Children.Clear();

        var tierDefs = GetTierDefs(game.Mode);

        // Header row
        var headerGrid = new Grid
        {
            BackgroundColor = Color.FromArgb("#F3F4F6"),
            Padding = new Thickness(0),
        };
        var headerCols = new ColumnDefinitionCollection();
        for (int i = 0; i < tierDefs.Count; i++)
            headerCols.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions = headerCols;

        for (int i = 0; i < tierDefs.Count; i++)
        {
            var (tierLabel, tierColor) = tierDefs[i];
            var cell = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(4, 10, 4, 6) };

            int cnt = counts.GetValueOrDefault(tierLabel, 0);
            double pct = totalDraws > 0 ? cnt * 100.0 / totalDraws : 0;

            cell.Children.Add(new Label
            {
                Text = tierLabel, FontSize = 11, FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb(tierColor),
                HorizontalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center
            });
            cell.Children.Add(new Label
            {
                Text = $"{cnt:N0}", FontSize = 18, FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1E2733"),
                HorizontalOptions = LayoutOptions.Center
            });
            cell.Children.Add(new Label
            {
                Text = $"{pct:F2}%", FontSize = 10,
                TextColor = Color.FromArgb("#6B7280"),
                HorizontalOptions = LayoutOptions.Center
            });

            headerGrid.Add(cell, i, 0);

            // Vertical divider
            if (i < tierDefs.Count - 1)
                headerGrid.Add(new BoxView
                {
                    BackgroundColor = Color.FromArgb("#E5E7EB"),
                    WidthRequest = 1, VerticalOptions = LayoutOptions.Fill
                }, i, 0); // overlaid — fine for dividers
        }
        statsTiersHost.Children.Add(headerGrid);

        // Best match label
        if (results.Count > 0)
        {
            var best = results.OrderByDescending(r => r.BestLevel).First();
            string dateStr = best.Draw.DateRaw;
            if (DateTime.TryParse(dateStr, out var dt)) dateStr = dt.ToString("MMM d, yyyy");
            lblStatsBest.Text = $"Best match: {best.BestLabel}  •  {dateStr}  •  {results.Count:N0} draws with ≥1 match";
        }
        else
        {
            lblStatsBest.Text = "No matching draws found in the selected range.";
        }

        lblSummary.Text = $"{results.Count:N0} matching draws out of {totalDraws:N0}";
        statsCard.IsVisible = true;
    }

    static List<(string Label, string ColorHex)> GetTierDefs(InputMode mode) =>
        mode switch
        {
            InputMode.Numbers5 =>
            [
                ("5/5 ★",  "#2E7D32"),
                ("4/5 ◆",  "#1565C0"),
                ("3/5 ◆",  "#E65100"),
                ("2/5 ◆",  "#9CA3AF"),
            ],
            InputMode.Numbers5Plus1 =>
            [
                ("5+1 ★",  "#2E7D32"),
                ("5/5 ★",  "#388E3C"),
                ("4+1 ◆",  "#1565C0"),
                ("4/5 ◆",  "#1976D2"),
                ("3+1 ◆",  "#E65100"),
                ("3/5 ◆",  "#F57C00"),
            ],
            InputMode.Numbers3Ordered =>
            [
                ("EXACT ★",  "#2E7D32"),
                ("2/3 ◆",    "#1565C0"),
                ("1/3 ◆",    "#9CA3AF"),
            ],
            _ /* Digits3 / Digits4 */ =>
            [
                ("STRAIGHT ★", "#2E7D32"),
                ("BOX ◆",      "#1565C0"),
            ],
        };

    // ── Results list ──────────────────────────────────────────────

    const int MaxShown = 100;

    void BuildResultsList(
        GameDef game,
        List<MatchResult> results,
        List<(int[] Main, int Special, string Label)> picks)
    {
        resultsContainer.Children.Clear();
        lblSummary.Text = $"{results.Count:N0} matching draws";

        if (results.Count == 0)
        {
            resultsContainer.Children.Add(new Label
            {
                Text = "No matching draws found.\nTry different numbers or a wider date range.",
                TextColor = Color.FromArgb("#6B7280"), FontSize = 14,
                HorizontalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(20, 16)
            });
            return;
        }

        // Sort: best main count desc, then most recent first
        var sorted = results
            .OrderByDescending(r => r.BestMainCount)
            .ThenByDescending(r => r.Draw.DateRaw)
            .ToList();

        int shown = Math.Min(sorted.Count, MaxShown);
        for (int i = 0; i < shown; i++)
            resultsContainer.Children.Add(BuildFlatRow(game, sorted[i], picks));

        if (sorted.Count > MaxShown)
            resultsContainer.Children.Add(new Label
            {
                Text = $"… {sorted.Count - MaxShown:N0} more. Use a filter or narrow the date range.",
                TextColor = Color.FromArgb("#6B7280"), FontSize = 12,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(10, 8)
            });
    }

    // Lightweight flat row — one label line per result, no nested grids
    View BuildFlatRow(
        GameDef game,
        MatchResult result,
        List<(int[] Main, int Special, string Label)> picks)
    {
        bool isWin     = result.BestLevel == 2;
        bool isPartial = result.BestLevel == 1;

        Color rowBg  = isWin     ? Color.FromArgb("#E8F5E9")
                     : isPartial ? Color.FromArgb("#FFF8E1")
                                 : Colors.White;
        Color tagClr = isWin     ? Color.FromArgb("#2E7D32")
                     : Color.FromArgb("#F57F17");

        string dateStr = result.Draw.DateRaw;
        if (DateTime.TryParse(dateStr, out var dt)) dateStr = dt.ToString("MMM d, yyyy");

        string balls = string.Join("  ", result.Draw.Main);
        if (game.HasSpecial && result.Draw.Special > 0)
            balls += $"  +{result.Draw.Special}";

        // Best match label across all picks
        string bestPick = result.BestLabel;
        if (picks.Count > 1)
        {
            int bestIdx = result.PickMatches.IndexOf(result.PickMatches.MaxBy(m => m.Level));
            if (bestIdx >= 0)
                bestPick = $"#{bestIdx + 1}: {result.BestLabel}";
        }

        var row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            BackgroundColor = rowBg,
            Padding         = new Thickness(14, 10)
        };

        row.Add(new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = dateStr, FontSize = 11, TextColor = Color.FromArgb("#6B7280") },
                new Label { Text = balls,   FontSize = 15, FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#1A1A1A") }
            }
        }, 0, 0);

        row.Add(new Label
        {
            Text                    = bestPick,
            FontSize                = 13,
            FontAttributes          = FontAttributes.Bold,
            TextColor               = tagClr,
            HorizontalOptions       = LayoutOptions.End,
            VerticalOptions         = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.End
        }, 1, 0);

        return new Border
        {
            BackgroundColor = rowBg,
            StrokeThickness = 0,
            StrokeShape     = new RoundRectangle { CornerRadius = 8 },
            Margin          = new Thickness(0, 0, 0, 4),
            Content         = row
        };
    }

    // ── Match classify (mirrors DrawSearchPage) ───────────────────

    static (int Level, string Label) Classify(
        InputMode mode, int[] winMain, int winSp, int[] srchMain, int srchSp)
    {
        switch (mode)
        {
            case InputMode.Digits3:
            case InputMode.Digits4:
                if (winMain.SequenceEqual(srchMain))
                    return (2, "STRAIGHT ★");
                if (winMain.OrderBy(x => x).SequenceEqual(srchMain.OrderBy(x => x)))
                    return (1, "BOX ◆");
                return (0, "—");

            case InputMode.Numbers3Ordered:
            {
                if (winMain.SequenceEqual(srchMain)) return (2, "EXACT ★");
                int p = srchMain.Where((n, i) => i < winMain.Length && winMain[i] == n).Count();
                if (p > 0) return (1, $"{p}/3 ◆");
                return (0, "—");
            }

            case InputMode.Numbers5:
            {
                int m = srchMain.Count(n => winMain.Contains(n));
                if (m == 5) return (2, "5/5 ★");
                if (m == 4) return (1, "4/5 ◆");
                if (m == 3) return (1, "3/5 ◆");
                if (m == 2) return (1, "2/5 ◆");
                return (0, "—");
            }

            case InputMode.Numbers5Plus1:
            {
                int  m     = srchMain.Count(n => winMain.Contains(n));
                bool spHit = winSp > 0 && winSp == srchSp;
                if (m == 5 && spHit) return (2, "5+1 ★");
                if (m == 5)          return (2, "5/5 ★");
                if (m == 4 && spHit) return (1, "4+1 ◆");
                if (m == 4)          return (1, "4/5 ◆");
                if (m == 3 && spHit) return (1, "3+1 ◆");
                if (m == 3)          return (1, "3/5 ◆");
                if (m == 2 && spHit) return (1, "2+1 ◆");
                if (spHit)           return (1, "+1 ◆");
                return (0, "—");
            }

            default:
                return (0, "—");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────

    void UpdateGameLogo()
    {
        if (gamePicker.SelectedIndex < 0) return;
        string game = gamePicker.Items[gamePicker.SelectedIndex];
        imgGameLogo.Source = game switch
        {
            "Fantasy 5"    => "logo_fantasy5.png",
            "Super Lotto"  => "logo_superlotto.png",
            "Powerball"    => "logo_powerball.png",
            "Mega Millions"=> "logo_megamillions.png",
            "Daily 3"      => "logo_daily3.png",
            "Daily 4"      => "logo_daily4.png",
            "Daily Derby"  => "logo_dailyderby.png",
            _              => "logo_fantasy5.png"
        };
    }

    void UpdateGameUI()
    {
        int rowCount = Math.Max(numbersContainer.Children.Count, 3);
        numbersContainer.Children.Clear();
        for (int i = 0; i < rowCount; i++)
            AddNumberRow(scroll: false);

        var game = _games[_selGame];
        string hint = game.Mode switch
        {
            InputMode.Digits3        => "Daily 3 — enter each digit (0–9) in its own box.",
            InputMode.Digits4        => "Daily 4 — enter each digit (0–9) in its own box.",
            InputMode.Numbers5       => $"{game.Name} — enter 5 numbers (1–{game.MaxBall}) per row.",
            InputMode.Numbers5Plus1  => $"{game.Name} — 5 main (1–{game.MaxBall}) + special ball (1–{game.SpecialMax}).",
            InputMode.Numbers3Ordered=> "Daily Derby — enter 3 horse numbers (1–12) in finish order.",
            _                        => ""
        };
        string apiNote = game.ApiGameId > 0 ? "  (fetches recent draws from CA Lottery API)" : "";
        lblStatus.Text = hint + apiNote;

        // Set default From/To using month+year pickers
        DateTime gameStart = game.Name switch
        {
            "Fantasy 5"   => new DateTime(1992, 2, 1),
            "Super Lotto" => new DateTime(2000, 6, 1),
            "Daily 3"     => new DateTime(2018, 1, 1),
            _             => new DateTime(DateTime.Today.Year - 2, 1, 1)
        };
        SetupDatePickers(gameStart.Year, gameStart.Month, DateTime.Today.Year, DateTime.Today.Month);
    }

    static readonly string[] _monthNames = { "Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec" };

    void SetupDatePickers(int fromYear, int fromMonth, int toYear, int toMonth)
    {
        int startYear = 1992;
        int endYear   = DateTime.Today.Year;

        // Populate months
        foreach (var p in new[] { pickerFromMonth, pickerToMonth })
        {
            p.Items.Clear();
            foreach (var m in _monthNames) p.Items.Add(m);
        }
        pickerFromMonth.SelectedIndex = fromMonth - 1;
        pickerToMonth.SelectedIndex   = toMonth   - 1;

        // Populate years
        foreach (var p in new[] { pickerFromYear, pickerToYear })
        {
            p.Items.Clear();
            for (int y = startYear; y <= endYear; y++) p.Items.Add(y.ToString());
        }
        pickerFromYear.SelectedIndex = fromYear - startYear;
        pickerToYear.SelectedIndex   = toYear   - startYear;
    }

    void BuildFilterBar(GameDef game)
    {
        filterBar.Children.Clear();

        // Build filter options based on game mode
        var filters = new List<(string Label, int MinCount)>();
        filters.Add(("All", -1));

        if (game.Mode is InputMode.Digits3 or InputMode.Digits4)
        {
            filters.Add(("BOX", 1));
            filters.Add(("STRAIGHT", game.MainCount));
        }
        else if (game.Mode == InputMode.Numbers3Ordered)
        {
            filters.Add(("1+ pos", 1));
            filters.Add(("2+ pos", 2));
            filters.Add(("EXACT", 3));
        }
        else
        {
            for (int i = 2; i <= game.MainCount; i++)
                filters.Add(($"{i}+", i));
        }

        foreach (var (label, minCount) in filters)
        {
            int capturedMin = minCount;
            bool isActive   = capturedMin == _activeFilter;

            int count = capturedMin == -1
                ? _lastResults.Count
                : _lastResults.Count(r => r.BestMainCount >= capturedMin);

            var btn = new Border
            {
                BackgroundColor = isActive ? Color.FromArgb("#1565C0") : Color.FromArgb("#E3F2FD"),
                StrokeThickness = 0,
                StrokeShape     = new RoundRectangle { CornerRadius = 12 },
                Padding         = new Thickness(10, 4),
                Content         = new Label
                {
                    Text                    = $"{label}  {count:N0}",
                    FontSize                = 11,
                    FontAttributes          = FontAttributes.Bold,
                    TextColor               = isActive ? Colors.White : Color.FromArgb("#1565C0"),
                    VerticalOptions         = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center,
                }
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += async (s, e) =>
            {
                if (_lastGame == null) return;
                _activeFilter = capturedMin;
                BuildFilterBar(_lastGame);

                // Show spinner
                lblLoadingMsg.Text = "Filtering...";
                lblLoadingPct.Text = "";
                loadingOverlay.IsVisible = true;
                resultsContainer.Children.Clear();
                await Task.Yield();

                var filtered = capturedMin == -1
                    ? _lastResults
                    : _lastResults.Where(r => r.BestMainCount >= capturedMin).ToList();

                await Task.Run(() => { });
                BuildResultsList(_lastGame, filtered, _lastPicks);
                loadingOverlay.IsVisible = false;
            };
            btn.GestureRecognizers.Add(tap);
            filterBar.Children.Add(btn);
        }

        filterBarScroll.IsVisible = true;
    }

    void ClearResults()
    {
        resultsContainer.Children.Clear();
        statsTiersHost.Children.Clear();
        filterBar.Children.Clear();
        filterBarScroll.IsVisible = false;
        statsCard.IsVisible = false;
        lblSummary.Text = "";
        _lastResults  = [];
        _lastPicks    = [];
        _lastGame     = null;
        _activeFilter = -1;
    }

    void SetMsg(string msg) =>
        MainThread.BeginInvokeOnMainThread(() => lblLoadingMsg.Text = msg);

    static (int[] Main, int Special, string? Error) ParseInput(GameDef g, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ([], 0, "Enter a number.");

        if (g.Mode is InputMode.Digits3 or InputMode.Digits4)
        {
            int want = g.Mode == InputMode.Digits3 ? 3 : 4;
            string s = text.Replace(" ", "");
            if (s.Length != want || !s.All(char.IsDigit))
                return ([], 0, $"Enter exactly {want} digits.");
            return (s.Select(c => c - '0').ToArray(), 0, null);
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nums  = new List<int>();
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out int n))
                return ([], 0, $"'{p}' is not a valid number.");
            nums.Add(n);
        }

        if (g.Mode == InputMode.Numbers5)
        {
            if (nums.Count != 5) return ([], 0, "Fill all 5 number boxes.");
            return ([.. nums], 0, null);
        }
        else if (g.Mode == InputMode.Numbers5Plus1)
        {
            if (nums.Count != 6) return ([], 0, "Fill all 5 boxes + special ball.");
            return ([.. nums.Take(5)], nums[5], null);
        }
        else // Numbers3Ordered
        {
            if (nums.Count != 3) return ([], 0, "Fill all 3 horse number boxes.");
            return ([.. nums], 0, null);
        }
    }

    static Border Ball(string text, Color color, int size) => new Border
    {
        BackgroundColor = color,
        StrokeThickness = 0,
        StrokeShape     = new RoundRectangle { CornerRadius = size / 2 },
        WidthRequest    = size, HeightRequest = size,
        Content = new Label
        {
            Text              = text,
            FontSize          = size * 0.42,
            FontAttributes    = FontAttributes.Bold,
            TextColor         = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions   = LayoutOptions.Center
        }
    };
}
