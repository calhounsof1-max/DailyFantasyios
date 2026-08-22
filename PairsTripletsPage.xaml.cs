using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

public partial class PairsTripletsPage : ContentPage
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
        int       SpecialMax,
        Color     BallColor,
        string    AccentHex,
        int       ApiGameId = 0
    );

    static readonly GameDef[] _games =
    [
        new("Fantasy 5",    "data/myFantasy5.csv",  "myFantasy5.csv",  InputMode.Numbers5,      5, 39,  1, false,  0, Color.FromArgb("#FF8F00"), "#FF8F00"),
        new("Super Lotto",  "data/mySuperlotto.csv","mySuperlotto.csv",InputMode.Numbers5Plus1, 5, 47,  1, true,  27, Color.FromArgb("#7B1FA2"), "#7B1FA2"),
        new("Powerball",    "", "",                                     InputMode.Numbers5Plus1, 5, 69,  1, true,  26, Color.FromArgb("#C62828"), "#C62828", ApiGameId: 12),
        new("Mega Millions","data/myMegaMillions.csv","myMegaMillions.csv",InputMode.Numbers5Plus1, 5, 70,  1, true,  25, Color.FromArgb("#E65100"), "#E65100", ApiGameId: 4),
        new("Daily 3",      "data/myDaily3.csv",    "myDaily3.csv",    InputMode.Digits3,       3,  9,  0, false,  0, Color.FromArgb("#1565C0"), "#1565C0"),
        new("Daily 4",      "", "",                                     InputMode.Digits4,       4,  9,  0, false,  0, Color.FromArgb("#0D47A1"), "#0D47A1", ApiGameId: 14),
        new("Daily Derby",  "", "",                                     InputMode.Numbers3Ordered,3,12,  1, false,  0, Color.FromArgb("#4A148C"), "#4A148C", ApiGameId: 11),
    ];

    // ── Period options ─────────────────────────────────────────────

    static readonly (string Label, int Draws)[] _periods =
    [
        ("Last 30",  30),
        ("Last 60",  60),
        ("Last 90",  90),
        ("Last 180", 180),
        ("All",      0),
    ];

    int  _selPeriod    = 90;
    int  _selGame      = 0;
    bool _showingPairs = true;

    Dictionary<(int, int), int>      _pairCounts    = new();
    Dictionary<(int, int, int), int> _tripletCounts = new();
    int _totalDraws = 0;

    readonly List<Entry> _entries = [];

    public static string PresetGame { get; set; } = "Fantasy 5";

    // ── ctor ──────────────────────────────────────────────────────

    public PairsTripletsPage()
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
            // SelectedIndexChanged fires and calls LoadAndRender
        }
        else if (idx >= 0)
        {
            _ = LoadAndRender();
        }
        else
        {
            _ = LoadAndRender();
        }
    }

    // ── Period bar ────────────────────────────────────────────────

    void BuildPeriodBar()
    {
        periodBar.Children.Clear();
        foreach (var (label, draws) in _periods)
        {
            int capturedDraws = draws;
            bool isActive = capturedDraws == _selPeriod;

            var pill = new Border
            {
                BackgroundColor = isActive ? Color.FromArgb("#1565C0") : Color.FromArgb("#E5E7EB"),
                StrokeThickness = 0,
                StrokeShape     = new RoundRectangle { CornerRadius = 12 },
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
                _ = LoadAndRender();
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
        _selGame = i;
        BuildEntryBoxes(_games[i]);
        _ = LoadAndRender();
        UpdateGameLogo();
    }

    private async void TabPairs_Tapped(object? sender, TappedEventArgs e)
    {
        _showingPairs = true;
        UpdateTabColors();
        await BuildResultsAsync();
    }

    private async void TabTriplets_Tapped(object? sender, TappedEventArgs e)
    {
        _showingPairs = false;
        UpdateTabColors();
        await BuildResultsAsync();
    }

    private async void EntrySearch_Changed(object? sender, TextChangedEventArgs e)
    {
        if (_totalDraws == 0) return;
        await BuildResultsAsync();
    }

    private async void BtnClearSearch_Clicked(object? sender, EventArgs e)
    {
        foreach (var en in _entries)
            en.Text = "";
        await BuildResultsAsync();
    }

    private async void BtnGoHome_Clicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//MainPage");

    // ── Tab colors ────────────────────────────────────────────────

    void UpdateTabColors()
    {
        tabPairsBorder.BackgroundColor    = _showingPairs ? Color.FromArgb("#1565C0") : Color.FromArgb("#E3F2FD");
        tabPairsLabel.TextColor           = _showingPairs ? Colors.White : Color.FromArgb("#1565C0");
        tabTripletsBorder.BackgroundColor = _showingPairs ? Color.FromArgb("#E3F2FD") : Color.FromArgb("#1565C0");
        tabTripletsLabel.TextColor        = _showingPairs ? Color.FromArgb("#1565C0") : Colors.White;
    }

    // ── Entry boxes ───────────────────────────────────────────────

    void BuildEntryBoxes(GameDef game)
    {
        entryBoxesLayout.Children.Clear();
        _entries.Clear();

        bool isDigit = game.Mode is InputMode.Digits3 or InputMode.Digits4;
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

            entry.TextChanged += async (s, ev) =>
            {
                string txt = entry.Text ?? "";
                bool shouldAdvance =
                    (isDigit  && txt.Length >= 1) ||
                    (!isDigit && txt.Length >= 2);

                if (shouldAdvance)
                    Dispatcher.Dispatch(() => MoveToNextEntry(capturedIdx));

                if (_totalDraws > 0)
                    await BuildResultsAsync();
            };

            _entries.Add(entry);
            entryBoxesLayout.Children.Add(entry);
        }

        lblEntryHint.Text = $"Enter numbers ({game.MinBall}–{game.MaxBall}) to filter:";
    }

    void MoveToNextEntry(int currentIdx)
    {
        int next = currentIdx + 1;
        if (next < _entries.Count)
            _entries[next].Focus();
    }

    HashSet<int> GetSearchNums()
    {
        var nums = new HashSet<int>();
        foreach (var e in _entries)
            if (int.TryParse(e.Text?.Trim(), out int v))
                nums.Add(v);
        return nums;
    }

    // ── Main load + render ────────────────────────────────────────

    async Task LoadAndRender()
    {
        var game = _games[_selGame];

        MainThread.BeginInvokeOnMainThread(() =>
        {
            loadingOverlay.IsVisible = true;
            lblLoadingMsg.Text       = "Analyzing pairs...";
            contentContainer.Children.Clear();
        });

        try
        {
            List<DrawEntry> draws;

            if (game.ApiGameId > 0)
            {
                SetMsg("Fetching draws from CA Lottery...");
                draws = await LoadApiAsync(game);
            }
            else
            {
                draws = await LoadCsvAsync(game);
            }

            // Apply period filter
            if (game.ApiGameId > 0)
            {
                if (_selPeriod > 0 && draws.Count > _selPeriod)
                    draws = draws.Take(_selPeriod).ToList();
            }
            else
            {
                draws = [.. draws.AsEnumerable().Reverse()];
                if (_selPeriod > 0 && draws.Count > _selPeriod)
                    draws = draws.Take(_selPeriod).ToList();
            }

            _totalDraws = draws.Count;

            if (_totalDraws == 0)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    loadingOverlay.IsVisible = false;
                    lblStatus.Text = $"{game.Name} — No data available.";
                    contentContainer.Children.Clear();
                    contentContainer.Children.Add(new Label
                    {
                        Text                    = "No data available for this game/period.",
                        TextColor               = Color.FromArgb("#6B7280"),
                        FontSize                = 14,
                        HorizontalOptions       = LayoutOptions.Center,
                        HorizontalTextAlignment = TextAlignment.Center,
                        Margin                  = new Thickness(20, 32)
                    });
                });
                return;
            }

            // Compute pair counts
            _pairCounts = new Dictionary<(int, int), int>();
            foreach (var d in draws)
            {
                var nums = d.Main;
                for (int i = 0; i < nums.Length - 1; i++)
                {
                    for (int j = i + 1; j < nums.Length; j++)
                    {
                        int a = Math.Min(nums[i], nums[j]);
                        int b = Math.Max(nums[i], nums[j]);
                        var key = (a, b);
                        _pairCounts[key] = _pairCounts.GetValueOrDefault(key) + 1;
                    }
                }
            }

            // Compute triplet counts
            _tripletCounts = new Dictionary<(int, int, int), int>();
            foreach (var d in draws)
            {
                var nums = d.Main;
                for (int i = 0; i < nums.Length - 2; i++)
                {
                    for (int j = i + 1; j < nums.Length - 1; j++)
                    {
                        for (int k = j + 1; k < nums.Length; k++)
                        {
                            var sorted = new[] { nums[i], nums[j], nums[k] };
                            Array.Sort(sorted);
                            var key = (sorted[0], sorted[1], sorted[2]);
                            _tripletCounts[key] = _tripletCounts.GetValueOrDefault(key) + 1;
                        }
                    }
                }
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                BuildEntryBoxes(game);
                _ = BuildResultsAsync();
                lblStatus.Text           = $"{game.Name} — {_totalDraws} draws · {_pairCounts.Count} unique pairs";
                loadingOverlay.IsVisible = false;
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

    // ── Build results ─────────────────────────────────────────────

    async Task BuildResultsAsync()
    {
        contentContainer.Children.Clear();
        await Task.Yield();
        if (_showingPairs)
            BuildPairsView();
        else
            BuildTripletsView();
    }

    // ── Pairs view ────────────────────────────────────────────────

    void BuildPairsView()
    {
        var searchNums = GetSearchNums();

        List<KeyValuePair<(int, int), int>> pairs;
        string title;

        if (searchNums.Count == 0)
        {
            pairs = _pairCounts.OrderByDescending(kv => kv.Value).Take(30).ToList();
            title = "Top 30 Most Common Pairs";
        }
        else if (searchNums.Count == 1)
        {
            int n = searchNums.First();
            pairs = _pairCounts.Where(kv => kv.Key.Item1 == n || kv.Key.Item2 == n)
                               .OrderByDescending(kv => kv.Value).ToList();
            title = $"Pairs containing #{n}";
        }
        else
        {
            pairs = _pairCounts.Where(kv => searchNums.Contains(kv.Key.Item1) && searchNums.Contains(kv.Key.Item2))
                               .OrderByDescending(kv => kv.Value).ToList();
            title = $"Pairs within your {searchNums.Count} numbers";
        }
        contentContainer.Children.Add(new Label { Text = title, FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#1E2733"), Margin = new Thickness(2, 0, 0, 4) });

        if (pairs.Count == 0)
        {
            contentContainer.Children.Add(new Label { Text = "No pairs found.", TextColor = Color.FromArgb("#6B7280"), FontSize = 13, Margin = new Thickness(4, 8) });
            return;
        }

        int maxCount = Math.Max(1, pairs.Max(kv => kv.Value));
        string accentHex = _games[_selGame].AccentHex;

        foreach (var kv in pairs)
        {
            double barWidth = Math.Max(4, Math.Min(130, (double)kv.Value / maxCount * 130.0));
            double pct = _totalDraws > 0 ? (double)kv.Value / _totalDraws * 100.0 : 0;

            var row = new Grid { ColumnDefinitions = [
                new ColumnDefinition { Width = new GridLength(72) },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
            ], Margin = new Thickness(0, 0, 0, 3) };

            row.Add(new Label { Text = $"{kv.Key.Item1}+{kv.Key.Item2}",
                FontSize = 13, FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb(accentHex), VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.NoWrap, Margin = new Thickness(0, 0, 8, 0) }, 0, 0);

            row.Add(new BoxView { Color = Color.FromArgb("#1565C0"), HeightRequest = 14,
                WidthRequest = barWidth, CornerRadius = 3,
                HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center }, 1, 0);

            row.Add(new Label { Text = $"{kv.Value}x  {pct:F1}%",
                FontSize = 11, TextColor = Color.FromArgb("#374151"),
                VerticalOptions = LayoutOptions.Center, Margin = new Thickness(6, 0, 0, 0),
                LineBreakMode = LineBreakMode.NoWrap }, 2, 0);

            contentContainer.Children.Add(row);
        }
    }

    // ── Triplets view ─────────────────────────────────────────────

    void BuildTripletsView()
    {
        var searchNums = GetSearchNums();

        List<KeyValuePair<(int, int, int), int>> triplets;
        string title;

        if (searchNums.Count == 0)
        {
            triplets = _tripletCounts.OrderByDescending(kv => kv.Value).Take(20).ToList();
            title = "Top 20 Most Common Triplets";
        }
        else if (searchNums.Count == 1)
        {
            int n = searchNums.First();
            triplets = _tripletCounts.Where(kv => kv.Key.Item1 == n || kv.Key.Item2 == n || kv.Key.Item3 == n)
                                     .OrderByDescending(kv => kv.Value).ToList();
            title = $"Triplets containing #{n}";
        }
        else
        {
            triplets = _tripletCounts.Where(kv => searchNums.Contains(kv.Key.Item1) && searchNums.Contains(kv.Key.Item2) && searchNums.Contains(kv.Key.Item3))
                                     .OrderByDescending(kv => kv.Value).ToList();
            title = $"Triplets within your {searchNums.Count} numbers";
        }
        contentContainer.Children.Add(new Label { Text = title, FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#1E2733"), Margin = new Thickness(2, 0, 0, 4) });

        if (triplets.Count == 0)
        {
            contentContainer.Children.Add(new Label { Text = "No triplets found.", TextColor = Color.FromArgb("#6B7280"), FontSize = 13, Margin = new Thickness(4, 8) });
            return;
        }

        int maxCount = Math.Max(1, triplets.Max(kv => kv.Value));
        string accentHex = _games[_selGame].AccentHex;

        foreach (var kv in triplets)
        {
            double barWidth = Math.Max(4, Math.Min(100, (double)kv.Value / maxCount * 100.0));
            double pct = _totalDraws > 0 ? (double)kv.Value / _totalDraws * 100.0 : 0;

            var row = new Grid { ColumnDefinitions = [
                new ColumnDefinition { Width = new GridLength(110) },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
            ], Margin = new Thickness(0, 0, 0, 3) };

            row.Add(new Label { Text = $"{kv.Key.Item1}+{kv.Key.Item2}+{kv.Key.Item3}",
                FontSize = 13, FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb(accentHex), VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.NoWrap, Margin = new Thickness(0, 0, 8, 0) }, 0, 0);

            row.Add(new BoxView { Color = Color.FromArgb("#1565C0"), HeightRequest = 14,
                WidthRequest = barWidth, CornerRadius = 3,
                HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center }, 1, 0);

            row.Add(new Label { Text = $"{kv.Value}x  {pct:F1}%",
                FontSize = 11, TextColor = Color.FromArgb("#374151"),
                VerticalOptions = LayoutOptions.Center, Margin = new Thickness(6, 0, 0, 0),
                LineBreakMode = LineBreakMode.NoWrap }, 2, 0);

            contentContainer.Children.Add(row);
        }
    }

    // ── DrawEntry record ──────────────────────────────────────────

    record DrawEntry(int[] Main, int Special);

    // ── CSV loading ───────────────────────────────────────────────

    async Task<List<DrawEntry>> LoadCsvAsync(GameDef game)
    {
        string localPath = System.IO.Path.Combine(FileSystem.AppDataDirectory, "data", game.LocalFile);
        Stream stream = File.Exists(localPath)
            ? File.OpenRead(localPath)
            : await FileSystem.OpenAppPackageFileAsync(game.AssetName);

        var draws = new List<DrawEntry>();
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
                if (!DateTime.TryParse(dateRaw, out _)) continue;

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

                draws.Add(new DrawEntry(main, special));
            }
        }
        return draws;
    }

    // ── API loading ───────────────────────────────────────────────

    async Task<List<DrawEntry>> LoadApiAsync(GameDef game)
    {
        bool hasSpecial = game.HasSpecial;
        int  mainCount  = game.MainCount;

        var draws = new List<DrawEntry>();
        int page  = 1;
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
            SetMsg($"Fetching page {page} of draws...");
            string url  = "https://www.calottery.com/api/DrawGameApi/" +
                          $"DrawGamePastDrawResults/{game.ApiGameId}/{page}/{pageSize}";
            string json = await client.GetStringAsync(url).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("PreviousDraws", out var drawsEl) ||
                drawsEl.GetArrayLength() == 0)
                break;

            if (_selPeriod > 0 && draws.Count >= _selPeriod)
                break;

            foreach (var d in drawsEl.EnumerateArray())
            {
                string dateRaw = d.TryGetProperty("DrawDate", out var de) ? de.GetString() ?? "" : "";
                if (!DateTime.TryParse(dateRaw, out _)) continue;

                var (main, special) = ParseApiNumbers(d, mainCount, hasSpecial);
                if (main.Length == mainCount)
                    draws.Add(new DrawEntry(main, special));
            }

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

    void SetMsg(string msg) =>
        MainThread.BeginInvokeOnMainThread(() => lblLoadingMsg.Text = msg);
}
