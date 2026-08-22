using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

public partial class HotColdPage : ContentPage
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
        ("30 Draws",  30),
        ("60 Draws",  60),
        ("90 Draws",  90),
        ("180 Draws", 180),
        ("All Draws", 0),
    ];

    int _selPeriod = 90;
    int _selGame   = 0;

    public static string PresetGame { get; set; } = "Fantasy 5";

    // ── ctor ──────────────────────────────────────────────────────

    public HotColdPage()
    {
        InitializeComponent();

        foreach (var g in _games)
            gamePicker.Items.Add(g.Name);

        BuildPeriodBar();

        gamePicker.SelectedIndex = 0;
        UpdateGameLogo();
    }

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
            // Same game, just reload in case period changed
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
        _ = LoadAndRender();
        UpdateGameLogo();
    }

    private async void BtnGoHome_Clicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//MainPage");

    // ── Main load + render ────────────────────────────────────────

    async Task LoadAndRender()
    {
        var game = _games[_selGame];

        MainThread.BeginInvokeOnMainThread(() =>
        {
            loadingOverlay.IsVisible = true;
            lblLoadingMsg.Text       = "Loading draws...";
            summaryStrip.IsVisible   = false;
            gridContainer.Children.Clear();
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

            // Apply period filter: draws from CSV are sorted oldest-first, reverse then take N
            // API returns newest-first, so just take N
            if (game.ApiGameId > 0)
            {
                if (_selPeriod > 0 && draws.Count > _selPeriod)
                    draws = draws.Take(_selPeriod).ToList();
            }
            else
            {
                // CSV: reverse to get newest-first, take N, then we have our set
                draws = [.. draws.AsEnumerable().Reverse()];
                if (_selPeriod > 0 && draws.Count > _selPeriod)
                    draws = draws.Take(_selPeriod).ToList();
            }

            int analyzedCount = draws.Count;

            if (analyzedCount == 0)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    loadingOverlay.IsVisible = false;
                    lblStatus.Text = $"{game.Name} — No data available.";
                    summaryStrip.IsVisible = false;
                    gridContainer.Children.Clear();
                    gridContainer.Children.Add(new Label
                    {
                        Text                    = "No data available for the selected game and period.",
                        TextColor               = Color.FromArgb("#6B7280"),
                        FontSize                = 14,
                        HorizontalOptions       = LayoutOptions.Center,
                        HorizontalTextAlignment = TextAlignment.Center,
                        Margin                  = new Thickness(20, 32)
                    });
                });
                return;
            }

            // Compute frequencies off main thread
            var (mainFreq, specialFreq, positionalFreqs) =
                await Task.Run(() => ComputeFrequencies(game, draws));

            // Build date range string from draws
            string dateRange = BuildDateRange(draws);

            string periodLabel = _selPeriod > 0
                ? $"{analyzedCount} draws analyzed"
                : $"All {analyzedCount} draws analyzed";

            MainThread.BeginInvokeOnMainThread(() =>
            {
                BuildGrid(game, mainFreq, specialFreq, positionalFreqs, analyzedCount);
                UpdateSummaryStrip(game, mainFreq);
                lblStatus.Text = $"{game.Name} — {periodLabel}{(dateRange.Length > 0 ? $" ({dateRange})" : "")}";
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

    // ── Frequency computation ─────────────────────────────────────

    record DrawEntry(int[] Main, int Special);

    // Returns (mainFreq dict, specialFreq dict, positionalFreqs per position)
    (Dictionary<int,int> MainFreq, Dictionary<int,int> SpecialFreq, List<Dictionary<int,int>> PositionalFreqs)
        ComputeFrequencies(GameDef game, List<DrawEntry> draws)
    {
        var mainFreq    = new Dictionary<int, int>();
        var specialFreq = new Dictionary<int, int>();
        var positionalFreqs = new List<Dictionary<int, int>>();

        bool isPositional = game.Mode is InputMode.Digits3 or InputMode.Digits4
                         or InputMode.Numbers3Ordered;

        if (isPositional)
        {
            // Per-position frequency
            for (int pos = 0; pos < game.MainCount; pos++)
                positionalFreqs.Add(new Dictionary<int, int>());

            foreach (var draw in draws)
            {
                for (int pos = 0; pos < draw.Main.Length && pos < game.MainCount; pos++)
                {
                    int num = draw.Main[pos];
                    var dict = positionalFreqs[pos];
                    dict[num] = dict.GetValueOrDefault(num) + 1;
                }
            }
        }
        else
        {
            // Standard: count how many draws each number appeared in
            for (int n = game.MinBall; n <= game.MaxBall; n++)
                mainFreq[n] = 0;

            foreach (var draw in draws)
            {
                foreach (int num in draw.Main)
                    if (mainFreq.ContainsKey(num))
                        mainFreq[num]++;

                if (game.HasSpecial && draw.Special > 0)
                    specialFreq[draw.Special] = specialFreq.GetValueOrDefault(draw.Special) + 1;
            }

            // Ensure all special balls have an entry
            if (game.HasSpecial)
                for (int n = 1; n <= game.SpecialMax; n++)
                    if (!specialFreq.ContainsKey(n))
                        specialFreq[n] = 0;
        }

        return (mainFreq, specialFreq, positionalFreqs);
    }

    // ── Heat level (0=very cold .. 4=very hot) ────────────────────

    // Returns a parallel dict: number → heat level (0–4)
    static Dictionary<int, int> ComputeHeatLevels(Dictionary<int, int> freq)
    {
        if (freq.Count == 0) return [];

        var sorted = freq.Values.OrderBy(v => v).ToList();
        int total  = sorted.Count;

        // Percentile cutoffs by index
        int p20 = sorted[(int)(total * 0.20)];
        int p40 = sorted[(int)(total * 0.40)];
        int p60 = sorted[(int)(total * 0.60)];
        int p80 = sorted[(int)(total * 0.80)];

        var levels = new Dictionary<int, int>();
        foreach (var (num, cnt) in freq)
        {
            int level = cnt <= p20 ? 0
                      : cnt <= p40 ? 1
                      : cnt <= p60 ? 2
                      : cnt <= p80 ? 3
                      : 4;
            levels[num] = level;
        }
        return levels;
    }

    static (Color Bg, Color Text) HeatColors(int level) => level switch
    {
        4 => (Color.FromArgb("#C62828"), Colors.White),           // Very Hot
        3 => (Color.FromArgb("#EF9A9A"), Color.FromArgb("#7F0000")), // Hot
        2 => (Color.FromArgb("#F5F5F5"), Color.FromArgb("#424242")), // Average
        1 => (Color.FromArgb("#90CAF9"), Color.FromArgb("#0D47A1")), // Cold
        _ => (Color.FromArgb("#1565C0"), Colors.White),           // Very Cold
    };

    // ── Build grid UI ─────────────────────────────────────────────

    void BuildGrid(
        GameDef game,
        Dictionary<int, int> mainFreq,
        Dictionary<int, int> specialFreq,
        List<Dictionary<int, int>> positionalFreqs,
        int analyzedCount)
    {
        gridContainer.Children.Clear();

        bool isPositional = game.Mode is InputMode.Digits3 or InputMode.Digits4
                         or InputMode.Numbers3Ordered;

        string periodSuffix = _selPeriod > 0
            ? $"{analyzedCount} draws analyzed"
            : $"All {analyzedCount} draws analyzed";

        if (isPositional)
        {
            for (int pos = 0; pos < game.MainCount; pos++)
            {
                var posDict = positionalFreqs[pos];
                string posLabel = game.Mode == InputMode.Numbers3Ordered
                    ? pos switch { 0 => "1st Place Horse", 1 => "2nd Place Horse", _ => "3rd Place Horse" }
                    : $"Position {pos + 1}";

                gridContainer.Children.Add(BuildSection(
                    $"{posLabel} — {periodSuffix}",
                    posDict,
                    game.MinBall,
                    game.MaxBall));
            }
        }
        else
        {
            gridContainer.Children.Add(BuildSection(
                $"Main Numbers — {periodSuffix}",
                mainFreq,
                game.MinBall,
                game.MaxBall));

            if (game.HasSpecial && specialFreq.Count > 0)
            {
                gridContainer.Children.Add(BuildSection(
                    $"Special Ball — {periodSuffix}",
                    specialFreq,
                    1,
                    game.SpecialMax));
            }
        }
    }

    View BuildSection(string title, Dictionary<int, int> freq, int minBall, int maxBall)
    {
        var heatLevels = ComputeHeatLevels(freq);

        var sectionTitle = new Label
        {
            Text           = title,
            FontSize       = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor      = Color.FromArgb("#374151"),
            Margin         = new Thickness(0, 0, 0, 6)
        };

        var flex = new FlexLayout
        {
            Wrap            = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Direction       = Microsoft.Maui.Layouts.FlexDirection.Row,
            JustifyContent  = Microsoft.Maui.Layouts.FlexJustify.Start,
            AlignItems      = Microsoft.Maui.Layouts.FlexAlignItems.Start,
        };

        // Gap via margin on chips
        foreach (var num in freq.Keys.OrderBy(n => n))
        {
            int cnt   = freq.GetValueOrDefault(num);
            int level = heatLevels.GetValueOrDefault(num, 2);
            flex.Children.Add(BuildChip(num, cnt, level));
        }

        // Compact color legend
        var legend = new HorizontalStackLayout { Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        var legendItems = new[]
        {
            ("#1565C0", "Very Cold"),
            ("#90CAF9", "Cold"),
            ("#F5F5F5", "Average"),
            ("#EF9A9A", "Hot"),
            ("#C62828", "Very Hot"),
        };
        foreach (var (hex, label) in legendItems)
        {
            var dot = new Border
            {
                WidthRequest = 12, HeightRequest = 12,
                StrokeThickness = 1,
                Stroke = new SolidColorBrush(Color.FromArgb("#CCCCCC")),
                StrokeShape = new RoundRectangle { CornerRadius = 3 },
                BackgroundColor = Color.FromArgb(hex),
                VerticalOptions = LayoutOptions.Center,
            };
            legend.Children.Add(dot);
            legend.Children.Add(new Label
            {
                Text = label, FontSize = 9,
                TextColor = Color.FromArgb("#9CA3AF"),
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });
        }

        var container = new VerticalStackLayout
        {
            Spacing         = 0,
            BackgroundColor = Colors.White,
            Padding         = new Thickness(10, 10)
        };
        container.Children.Add(sectionTitle);
        container.Children.Add(legend);
        container.Children.Add(flex);

        return new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape     = new RoundRectangle { CornerRadius = 10 },
            Content         = container
        };
    }

    static View BuildChip(int number, int count, int heatLevel)
    {
        var (bgColor, textColor) = HeatColors(heatLevel);

        var numLabel = new Label
        {
            Text                    = number.ToString(),
            FontSize                = 16,
            FontAttributes          = FontAttributes.Bold,
            TextColor               = textColor,
            HorizontalOptions       = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
        };

        var countLabel = new Label
        {
            Text                    = $"{count}x",
            FontSize                = 9,
            TextColor               = textColor,
            HorizontalOptions       = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            Opacity                 = 0.85
        };

        var stack = new VerticalStackLayout
        {
            Spacing          = 1,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions   = LayoutOptions.Center,
        };
        stack.Children.Add(numLabel);
        stack.Children.Add(countLabel);

        return new Border
        {
            BackgroundColor = bgColor,
            StrokeThickness = 0,
            StrokeShape     = new RoundRectangle { CornerRadius = 8 },
            WidthRequest    = 52,
            HeightRequest   = 52,
            Margin          = new Thickness(3),
            Content         = stack
        };
    }

    // ── Summary strip ─────────────────────────────────────────────

    void UpdateSummaryStrip(GameDef game, Dictionary<int, int> mainFreq)
    {
        if (mainFreq.Count == 0)
        {
            summaryStrip.IsVisible = false;
            return;
        }

        var sorted = mainFreq.OrderByDescending(kv => kv.Value).ToList();
        string hotNums  = string.Join("  ", sorted.Take(5).Select(kv => $"{kv.Key} ({kv.Value}×)"));
        string coldNums = string.Join("  ", sorted.AsEnumerable().Reverse().Take(5).Select(kv => $"{kv.Key} ({kv.Value}×)"));

        lblHotNums.Text        = hotNums;
        lblColdNums.Text       = coldNums;
        summaryStrip.IsVisible = true;
    }

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

        // We fetch all available draws; period filtering is applied after
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

            // Stop early if we have enough draws for the period
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

    static string BuildDateRange(List<DrawEntry> draws)
    {
        // We don't store dates in DrawEntry, so just return empty
        // (date range would require storing dates, which we skip to keep it simple)
        return "";
    }
}
