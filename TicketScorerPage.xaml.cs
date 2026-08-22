using System.Text.Json;

namespace DailyFantasyMAUI;

public partial class TicketScorerPage : ContentPage
{
    // ── Game config ───────────────────────────────────────────────

    enum InputMode { Digits3, Digits4, Numbers5, Numbers5Plus1, Numbers3Ordered }

    record GameDef(
        string    Name,
        string    AssetName,
        string    LocalFile,
        InputMode Mode,
        int       MainCount,
        int       MaxBall,
        int       MinBall,
        bool      HasSpecial,
        string    AccentHex,
        int       ApiGameId = 0
    );

    static readonly GameDef[] _games =
    [
        new("Fantasy 5",    "data/myFantasy5.csv",   "myFantasy5.csv",   InputMode.Numbers5,      5, 39,  1, false, "#FF8F00"),
        new("Super Lotto",  "data/mySuperlotto.csv", "mySuperlotto.csv", InputMode.Numbers5Plus1, 5, 47,  1, true,  "#7B1FA2"),
        new("Powerball",    "", "",                                        InputMode.Numbers5Plus1, 5, 69,  1, true,  "#C62828", ApiGameId: 12),
        new("Mega Millions","data/myMegaMillions.csv","myMegaMillions.csv",InputMode.Numbers5Plus1, 5, 70,  1, true,  "#E65100", ApiGameId: 4),
        new("Daily 3",      "data/myDaily3.csv",     "myDaily3.csv",     InputMode.Digits3,       3,  9,  0, false, "#1565C0"),
        new("Daily 4",      "", "",                                        InputMode.Digits4,       4,  9,  0, false, "#0D47A1", ApiGameId: 14),
        new("Daily Derby",  "", "",                                        InputMode.Numbers3Ordered,3,12,  1, false, "#4A148C", ApiGameId: 11),
    ];

    static readonly (string Label, int Draws)[] _periods =
    [
        ("Last 30",  30),
        ("Last 60",  60),
        ("Last 90",  90),
        ("Last 180", 180),
        ("All",      0),
    ];

    // ── State ─────────────────────────────────────────────────────

    int  _selPeriod = 90;
    int  _selGame   = 0;
    bool _statsReady = false;

    // Computed stats
    Dictionary<int, int>      _freq       = new();
    Dictionary<(int,int), int> _pairCounts = new();
    int[]                     _sortedSums = [];
    int                       _totalDraws = 0;
    Dictionary<int, int>      _lastSeenAgo = new();
    Dictionary<int, double>   _avgGap      = new();
    double                    _medianPairCount = 0;

    readonly List<Entry> _entries = [];

    public static string PresetGame { get; set; } = "Fantasy 5";

    // ── ctor ──────────────────────────────────────────────────────

    public TicketScorerPage()
    {
        InitializeComponent();

        foreach (var g in _games)
            gamePicker.Items.Add(g.Name);

        BuildPeriodBar();
        _selPeriod = 90;
        gamePicker.SelectedIndex = 0;
        UpdateGameLogo();
    }

    // ── Lifecycle ─────────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();
        int idx = Array.FindIndex(_games, g => g.Name == PresetGame);
        if (idx >= 0 && idx != gamePicker.SelectedIndex)
        {
            _selGame = idx;
            gamePicker.SelectedIndex = idx;
        }
        else
        {
            _ = LoadStats();
        }
    }

    // ── Period bar ────────────────────────────────────────────────

    void BuildPeriodBar()
    {
        periodBar.Children.Clear();
        foreach (var (label, draws) in _periods)
        {
            int  capturedDraws = draws;
            bool isActive      = capturedDraws == _selPeriod;

            var pill = new Border
            {
                BackgroundColor = isActive ? Color.FromArgb("#1565C0") : Color.FromArgb("#E5E7EB"),
                StrokeThickness = 0,
                StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                Padding         = new Thickness(12, 4),
                VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text                    = label,
                    FontSize                = 12,
                    FontAttributes          = FontAttributes.Bold,
                    TextColor               = isActive ? Colors.White : Color.FromArgb("#374151"),
                    VerticalOptions         = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center,
                }
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) =>
            {
                _selPeriod = capturedDraws;
                BuildPeriodBar();
                resultsContainer.Children.Clear();
                _ = LoadStats();
            };
            pill.GestureRecognizers.Add(tap);
            periodBar.Children.Add(pill);
        }
    }

    // ── Events ────────────────────────────────────────────────────

    private void GamePicker_Changed(object? sender, EventArgs e)
    {
        int i = gamePicker.SelectedIndex;
        if (i < 0) return;
        _selGame   = i;
        _statsReady = false;
        resultsContainer.Children.Clear();
        BuildEntryBoxes(_games[i]);
        _ = LoadStats();
        UpdateGameLogo();
    }

    private async void BtnGoHome_Clicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//MainPage");

    private void BtnScore_Clicked(object? sender, EventArgs e)
    {
        if (!_statsReady)
        {
            lblStatus.Text = "Still loading draw history — try again in a moment.";
            return;
        }

        var game = _games[_selGame];
        var nums = new List<int>();

        foreach (var en in _entries)
        {
            if (!int.TryParse(en.Text?.Trim(), out int val))
            {
                lblStatus.Text = "Please fill in all number boxes before scoring.";
                return;
            }
            if (val < game.MinBall || val > game.MaxBall)
            {
                lblStatus.Text = $"Numbers must be between {game.MinBall} and {game.MaxBall}.";
                return;
            }
            nums.Add(val);
        }

        // Duplicate check for non-digit games
        bool isDigit = game.Mode is InputMode.Digits3 or InputMode.Digits4;
        if (!isDigit && nums.Distinct().Count() != nums.Count)
        {
            lblStatus.Text = "Duplicate numbers detected — each number must be unique.";
            return;
        }

        ScoreAndDisplay(nums, game);
    }

    // ── Entry boxes ───────────────────────────────────────────────

    void BuildEntryBoxes(GameDef game)
    {
        entryBoxesLayout.Children.Clear();
        _entries.Clear();

        bool isDigit = game.Mode is InputMode.Digits3 or InputMode.Digits4;
        bool isDerby = game.Mode == InputMode.Numbers3Ordered;
        int  maxLen  = isDigit ? 1 : 2;

        for (int i = 0; i < game.MainCount; i++)
        {
            int capturedIdx = i;
            var entry = new Entry
            {
                Placeholder             = isDigit ? "0" : "--",
                WidthRequest            = isDigit ? 44 : 52,
                HeightRequest           = 44,
                MaxLength               = maxLen,
                Keyboard                = Keyboard.Numeric,
                HorizontalTextAlignment = TextAlignment.Center,
                FontSize                = 16,
                FontAttributes          = FontAttributes.Bold,
                BackgroundColor         = Color.FromArgb("#EFF6FF"),
            };

            entry.TextChanged += (s, ev) =>
            {
                string txt = entry.Text ?? "";
                if (txt.Length == 0) return;

                bool shouldAdvance =
                    (isDigit  && txt.Length >= 1) ||
                    (isDerby  && txt.Length == 1 && int.TryParse(txt, out int v) && v >= 2) ||
                    (!isDigit && txt.Length >= 2);

                // Defer focus move so Android IME fully commits text first
                if (shouldAdvance)
                    Dispatcher.Dispatch(() => MoveToNextEntry(capturedIdx));
            };

            _entries.Add(entry);
            entryBoxesLayout.Children.Add(entry);
        }

        lblEntryHint.Text = $"Enter {game.MainCount} numbers ({game.MinBall}–{game.MaxBall}):";
    }

    void MoveToNextEntry(int currentIdx)
    {
        int next = currentIdx + 1;
        if (next < _entries.Count)
            _entries[next].Focus();
    }

    // ── Score + display ───────────────────────────────────────────

    void ScoreAndDisplay(List<int> nums, GameDef game)
    {
        int[]  arr      = [.. nums];
        string accentHex = game.AccentHex;

        // ── 1. Sum Score (20 pts) ─────────────────────────────────
        int userSum = arr.Sum();
        double sumScore;
        string sumDetail;
        if (_sortedSums.Length > 0)
        {
            int rank     = Array.BinarySearch(_sortedSums, userSum);
            if (rank < 0) rank = ~rank;
            double pct = (double)rank / _sortedSums.Length * 100;

            if (pct >= 20 && pct <= 80)      { sumScore = 20; sumDetail = "Sweet spot"; }
            else if (pct >= 10 && pct <= 90) { sumScore = 12; sumDetail = "Borderline"; }
            else                             { sumScore =  5; sumDetail = "Outlier sum"; }
        }
        else { sumScore = 10; sumDetail = "No data"; }

        // ── 2. Balance Score (20 pts) ─────────────────────────────
        bool isDigitGame = game.Mode is InputMode.Digits3 or InputMode.Digits4;
        int oddCount  = arr.Count(n => n % 2 != 0);
        double oddRatio = (double)oddCount / arr.Length;
        double balanceScore = 20.0 * (1.0 - Math.Abs(oddRatio - 0.5) * 2.0);
        balanceScore = Math.Max(0, balanceScore);
        string balDetail = $"{oddCount} odd / {arr.Length - oddCount} even";

        // ── 3. Hot Numbers Score (20 pts) ─────────────────────────
        double hotScore;
        string hotDetail;
        if (_freq.Count > 0)
        {
            var sorted = _freq.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
            double avgPct = arr.Average(n =>
            {
                int idx = sorted.IndexOf(n);
                return idx < 0 ? 0.5 : (double)idx / sorted.Count;
            });
            hotScore  = avgPct * 20.0;
            int hotCount = arr.Count(n => sorted.IndexOf(n) >= sorted.Count * 0.7);
            hotDetail = $"{hotCount} of {arr.Length} picks are hot";
        }
        else { hotScore = 10; hotDetail = "No data"; }

        // ── 4. Gap Score (20 pts) ─────────────────────────────────
        double gapScore;
        string gapDetail;
        if (_avgGap.Count > 0)
        {
            double avgCloseness = arr.Average(n =>
            {
                int  lsAgo  = _lastSeenAgo.GetValueOrDefault(n, _totalDraws);
                double ag   = _avgGap.GetValueOrDefault(n, _totalDraws);
                double due  = ag > 0 ? lsAgo / ag : 1.0;
                return Math.Max(0.0, 1.0 - Math.Abs(due - 1.0) / 2.0);
            });
            gapScore  = avgCloseness * 20.0;
            int dueCount = arr.Count(n =>
            {
                int    lsAgo = _lastSeenAgo.GetValueOrDefault(n, _totalDraws);
                double ag    = _avgGap.GetValueOrDefault(n, _totalDraws);
                double due   = ag > 0 ? lsAgo / ag : 1.0;
                return due >= 0.8 && due <= 1.5;
            });
            gapDetail = $"{dueCount} of {arr.Length} picks are on schedule";
        }
        else { gapScore = 10; gapDetail = "No data"; }

        // ── 5. Pair Strength Score (20 pts) ──────────────────────
        double pairScore;
        string pairDetail;
        if (_pairCounts.Count > 0 && arr.Length >= 2)
        {
            var userPairs = new List<int>();
            for (int i = 0; i < arr.Length - 1; i++)
                for (int j = i + 1; j < arr.Length; j++)
                {
                    int a = Math.Min(arr[i], arr[j]);
                    int b = Math.Max(arr[i], arr[j]);
                    userPairs.Add(_pairCounts.GetValueOrDefault((a, b), 0));
                }

            int aboveMedian = userPairs.Count(c => c >= _medianPairCount);
            pairScore  = userPairs.Count > 0 ? (double)aboveMedian / userPairs.Count * 20.0 : 10;
            pairDetail = $"{aboveMedian} of {userPairs.Count} pairs above average";
        }
        else { pairScore = 10; pairDetail = "N/A for this game"; }

        // ── Total ─────────────────────────────────────────────────
        int total = (int)Math.Round(sumScore + balanceScore + hotScore + gapScore + pairScore);
        total = Math.Clamp(total, 0, 100);

        string rating = total >= 80 ? "Excellent" :
                        total >= 60 ? "Good"      :
                        total >= 40 ? "Fair"       : "Weak";

        Color ratingColor = total >= 80 ? Color.FromArgb("#2E7D32") :
                            total >= 60 ? Color.FromArgb("#1565C0") :
                            total >= 40 ? Color.FromArgb("#FF8F00") :
                                          Color.FromArgb("#C62828");

        // ── Build result view ─────────────────────────────────────
        resultsContainer.Children.Clear();

        // Score banner
        var banner = new Border
        {
            BackgroundColor = ratingColor,
            StrokeThickness = 0,
            StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding         = new Thickness(16, 14),
            Margin          = new Thickness(0, 0, 0, 10),
        };
        var bannerGrid = new Grid { ColumnDefinitions = [
            new ColumnDefinition { Width = GridLength.Auto },
            new ColumnDefinition { Width = GridLength.Star },
        ]};
        bannerGrid.Add(new Label
        {
            Text           = total.ToString(),
            FontSize       = 48,
            FontAttributes = FontAttributes.Bold,
            TextColor      = Colors.White,
            VerticalOptions = LayoutOptions.Center,
            Margin         = new Thickness(0, 0, 16, 0),
        }, 0, 0);
        var bannerRight = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        bannerRight.Add(new Label { Text = "out of 100", FontSize = 12, TextColor = Color.FromArgb("#FFFFFFCC") });
        bannerRight.Add(new Label { Text = rating, FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Colors.White });
        bannerRight.Add(new Label { Text = $"Based on {_totalDraws} draws", FontSize = 11, TextColor = Color.FromArgb("#FFFFFFAA") });
        bannerGrid.Add(bannerRight, 1, 0);
        banner.Content = bannerGrid;
        resultsContainer.Children.Add(banner);

        // Category rows
        resultsContainer.Children.Add(MakeSectionLabel("Score Breakdown"));
        resultsContainer.Children.Add(MakeCategoryRow("Sum Position",    (int)Math.Round(sumScore),     sumDetail,     "#1565C0"));
        resultsContainer.Children.Add(MakeCategoryRow("Odd/Even Balance",(int)Math.Round(balanceScore), balDetail,     "#7B1FA2"));
        resultsContainer.Children.Add(MakeCategoryRow("Hot Numbers",     (int)Math.Round(hotScore),     hotDetail,     "#FF6F00"));
        resultsContainer.Children.Add(MakeCategoryRow("Gap Score",       (int)Math.Round(gapScore),     gapDetail,     "#2E7D32"));
        resultsContainer.Children.Add(MakeCategoryRow("Pair Strength",   (int)Math.Round(pairScore),    pairDetail,    "#C62828"));

        // Numbers entered
        resultsContainer.Children.Add(MakeSectionLabel("Your Numbers"));
        resultsContainer.Children.Add(MakeNumberChips(arr, accentHex));

        lblStatus.Text = $"{game.Name} · {_totalDraws} draws · Score: {total}/100 — {rating}";
    }

    static Label MakeSectionLabel(string text) => new Label
    {
        Text           = text,
        FontSize       = 12,
        FontAttributes = FontAttributes.Bold,
        TextColor      = Color.FromArgb("#6B7280"),
        Margin         = new Thickness(0, 8, 0, 4),
    };

    static View MakeCategoryRow(string name, int pts, string detail, string colorHex)
    {
        double barW = Math.Max(4, pts / 20.0 * 150.0);
        var color   = Color.FromArgb(colorHex);

        var outer = new VerticalStackLayout { Spacing = 2, Margin = new Thickness(0, 0, 0, 6) };

        var row = new Grid { ColumnDefinitions = [
            new ColumnDefinition { Width = new GridLength(140) },
            new ColumnDefinition { Width = GridLength.Star },
            new ColumnDefinition { Width = new GridLength(50) },
        ]};

        row.Add(new Label { Text = name, FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = color, VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.NoWrap }, 0, 0);

        row.Add(new BoxView { Color = color, HeightRequest = 14, WidthRequest = barW,
            CornerRadius = 3, HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Center, Margin = new Thickness(4, 0) }, 1, 0);

        row.Add(new Label { Text = $"{pts}/20", FontSize = 12, FontAttributes = FontAttributes.Bold,
            TextColor = color, VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.End,
            LineBreakMode = LineBreakMode.NoWrap }, 2, 0);

        outer.Add(row);
        outer.Add(new Label { Text = detail, FontSize = 11, TextColor = Color.FromArgb("#6B7280"),
            Margin = new Thickness(0, 0, 0, 0) });
        return outer;
    }

    static View MakeNumberChips(int[] nums, string colorHex)
    {
        var layout = new FlexLayout
        {
            Wrap           = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Direction      = Microsoft.Maui.Layouts.FlexDirection.Row,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
            AlignItems     = Microsoft.Maui.Layouts.FlexAlignItems.Center,
        };
        foreach (int n in nums)
        {
            layout.Children.Add(new Border
            {
                BackgroundColor = Color.FromArgb(colorHex),
                StrokeThickness = 0,
                StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
                Padding         = new Thickness(12, 6),
                Margin          = new Thickness(4),
                Content         = new Label { Text = n.ToString(), FontSize = 14,
                    FontAttributes = FontAttributes.Bold, TextColor = Colors.White }
            });
        }
        return layout;
    }

    // ── Load stats ────────────────────────────────────────────────

    async Task LoadStats()
    {
        var game = _games[_selGame];
        _statsReady = false;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            loadingOverlay.IsVisible = true;
            lblLoadingMsg.Text       = "Loading draw history...";
            resultsContainer.Children.Clear();
        });

        try
        {
            List<int[]> draws;

            if (game.ApiGameId > 0)
            {
                SetMsg("Fetching draws from CA Lottery...");
                draws = await LoadApiAsync(game);
            }
            else
            {
                draws = await LoadCsvAsync(game);
            }

            // Most-recent first + period filter
            if (game.ApiGameId > 0)
            {
                if (_selPeriod > 0 && draws.Count > _selPeriod)
                    draws = draws.Take(_selPeriod).ToList();
            }
            else
            {
                draws.Reverse();
                if (_selPeriod > 0 && draws.Count > _selPeriod)
                    draws = draws.Take(_selPeriod).ToList();
            }

            _totalDraws = draws.Count;

            // Frequency
            _freq.Clear();
            foreach (var d in draws)
                foreach (int n in d)
                    _freq[n] = _freq.GetValueOrDefault(n) + 1;

            // Pair counts
            _pairCounts.Clear();
            foreach (var d in draws)
                for (int i = 0; i < d.Length - 1; i++)
                    for (int j = i + 1; j < d.Length; j++)
                    {
                        int a = Math.Min(d[i], d[j]);
                        int b = Math.Max(d[i], d[j]);
                        var key = (a, b);
                        _pairCounts[key] = _pairCounts.GetValueOrDefault(key) + 1;
                    }

            // Median pair count
            if (_pairCounts.Count > 0)
            {
                var vals = _pairCounts.Values.OrderBy(v => v).ToArray();
                _medianPairCount = vals.Length % 2 == 0
                    ? (vals[vals.Length / 2 - 1] + vals[vals.Length / 2]) / 2.0
                    : vals[vals.Length / 2];
            }

            // Sorted sums
            _sortedSums = draws.Select(d => d.Sum()).OrderBy(s => s).ToArray();

            // Gap stats (draws[0] = most recent)
            _lastSeenAgo.Clear();
            _avgGap.Clear();
            var appearances = new Dictionary<int, int>();

            for (int drawIdx = 0; drawIdx < draws.Count; drawIdx++)
                foreach (int n in draws[drawIdx])
                {
                    if (!_lastSeenAgo.ContainsKey(n))
                        _lastSeenAgo[n] = drawIdx;
                    appearances[n] = appearances.GetValueOrDefault(n) + 1;
                }

            foreach (var kv in appearances)
                _avgGap[kv.Key] = kv.Value > 0 ? (double)_totalDraws / kv.Value : _totalDraws;

            _statsReady = true;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                loadingOverlay.IsVisible = false;
                lblStatus.Text = $"{game.Name} — {_totalDraws} draws loaded. Enter your numbers and tap Score.";
                BuildEntryBoxes(game);
            });
        }
        catch (Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                loadingOverlay.IsVisible = false;
                lblStatus.Text = $"Error: {ex.Message}";
            });
        }
    }

    // ── CSV loading ───────────────────────────────────────────────

    async Task<List<int[]>> LoadCsvAsync(GameDef game)
    {
        string localPath = System.IO.Path.Combine(FileSystem.AppDataDirectory, "data", game.LocalFile);
        Stream stream = File.Exists(localPath)
            ? File.OpenRead(localPath)
            : await FileSystem.OpenAppPackageFileAsync(game.AssetName);

        var draws = new List<int[]>();
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
                if (p.Length < 1 || !DateTime.TryParse(p[0].Trim(), out _)) continue;

                int[] nums = game.Mode switch
                {
                    InputMode.Digits3 when p.Length >= 5 &&
                        int.TryParse(p[2].Trim(), out int d1) &&
                        int.TryParse(p[3].Trim(), out int d2) &&
                        int.TryParse(p[4].Trim(), out int d3)
                        => [d1, d2, d3],

                    InputMode.Numbers5 when p.Length >= 7 &&
                        int.TryParse(p[2].Trim(), out int n1) &&
                        int.TryParse(p[3].Trim(), out int n2) &&
                        int.TryParse(p[4].Trim(), out int n3) &&
                        int.TryParse(p[5].Trim(), out int n4) &&
                        int.TryParse(p[6].Trim(), out int n5)
                        => [n1, n2, n3, n4, n5],

                    InputMode.Numbers5Plus1 when p.Length >= 7 &&
                        int.TryParse(p[2].Trim(), out int m1) &&
                        int.TryParse(p[3].Trim(), out int m2) &&
                        int.TryParse(p[4].Trim(), out int m3) &&
                        int.TryParse(p[5].Trim(), out int m4) &&
                        int.TryParse(p[6].Trim(), out int m5)
                        => [m1, m2, m3, m4, m5],

                    _ => []
                };

                if (nums.Length > 0) draws.Add(nums);
            }
        }
        return draws;
    }

    // ── API loading ───────────────────────────────────────────────

    async Task<List<int[]>> LoadApiAsync(GameDef game)
    {
        var draws    = new List<int[]>();
        int page     = 1;
        const int pageSize = 50;
        const int maxPages = 60;

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("Accept", "application/json, */*");
        client.DefaultRequestHeaders.Add("Referer", "https://www.calottery.com/");

        while (page <= maxPages)
        {
            SetMsg($"Fetching page {page}...");
            string url  = "https://www.calottery.com/api/DrawGameApi/" +
                          $"DrawGamePastDrawResults/{game.ApiGameId}/{page}/{pageSize}";
            string json = await client.GetStringAsync(url).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("PreviousDraws", out var drawsEl) ||
                drawsEl.GetArrayLength() == 0) break;

            if (_selPeriod > 0 && draws.Count >= _selPeriod) break;

            foreach (var d in drawsEl.EnumerateArray())
            {
                string dateRaw = d.TryGetProperty("DrawDate", out var de) ? de.GetString() ?? "" : "";
                if (!DateTime.TryParse(dateRaw, out _)) continue;
                var nums = ParseApiMainNumbers(d, game.MainCount);
                if (nums.Length == game.MainCount) draws.Add(nums);
            }

            page++;
            await Task.Delay(300).ConfigureAwait(false);
        }
        return draws;
    }

    static int[] ParseApiMainNumbers(JsonElement draw, int mainCount)
    {
        if (!draw.TryGetProperty("WinningNumbers", out var wn) ||
            wn.ValueKind != JsonValueKind.Object) return [];

        bool zeroBased = wn.TryGetProperty("0", out _);
        int  start     = zeroBased ? 0 : 1;
        var  main      = new List<int>();

        for (int i = start; i < start + mainCount + 1 && main.Count < mainCount; i++)
        {
            if (!wn.TryGetProperty(i.ToString(), out var el)) break;
            int n = 0; bool isSpecial = false;
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

            if (!isSpecial) main.Add(n);
        }
        return [.. main];
    }

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

    void SetMsg(string msg) =>
        MainThread.BeginInvokeOnMainThread(() => lblLoadingMsg.Text = msg);
}
