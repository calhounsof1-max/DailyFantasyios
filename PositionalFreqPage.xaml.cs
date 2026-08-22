using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

public partial class PositionalFreqPage : ContentPage
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

    int _selPeriod = 90;
    int _selGame   = 0;

    public static string PresetGame { get; set; } = "Fantasy 5";

    // ── ctor ──────────────────────────────────────────────────────

    public PositionalFreqPage()
    {
        InitializeComponent();

        foreach (var g in _games)
            gamePicker.Items.Add(g.Name);

        BuildPeriodBar();

        _selPeriod = 90;
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
            // CSV: oldest-first → reverse → take N
            // API: newest-first → take N
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

            int analyzedCount = draws.Count;

            if (analyzedCount == 0)
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

            // Compute positional frequencies off main thread
            var positionalFreqs = await Task.Run(() => ComputePositionalFreqs(game, draws));
            Dictionary<int, int>? specialFreq = null;
            if (game.HasSpecial)
                specialFreq = await Task.Run(() => ComputeSpecialFreq(game, draws));

            string periodLabel = _selPeriod > 0
                ? $"{analyzedCount} draws analyzed"
                : $"All {analyzedCount} draws analyzed";

            MainThread.BeginInvokeOnMainThread(() =>
            {
                BuildUI(game, positionalFreqs, specialFreq, analyzedCount);
                lblStatus.Text = $"{game.Name} — {periodLabel}";
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

    // For digit/ordered games: per-position count of each digit/number
    // For standard games: per-position count of numbers when draw sorted ascending
    List<Dictionary<int, int>> ComputePositionalFreqs(GameDef game, List<DrawEntry> draws)
    {
        var positionalFreqs = new List<Dictionary<int, int>>();
        for (int pos = 0; pos < game.MainCount; pos++)
            positionalFreqs.Add(new Dictionary<int, int>());

        bool isOrdered = game.Mode is InputMode.Digits3 or InputMode.Digits4
                      or InputMode.Numbers3Ordered;

        foreach (var draw in draws)
        {
            int[] numbers = isOrdered
                ? draw.Main
                : [.. draw.Main.OrderBy(n => n)];   // sort ascending for standard games

            for (int pos = 0; pos < numbers.Length && pos < game.MainCount; pos++)
            {
                int num = numbers[pos];
                var dict = positionalFreqs[pos];
                dict[num] = dict.GetValueOrDefault(num) + 1;
            }
        }

        return positionalFreqs;
    }

    Dictionary<int, int> ComputeSpecialFreq(GameDef game, List<DrawEntry> draws)
    {
        var freq = new Dictionary<int, int>();
        for (int n = 1; n <= game.SpecialMax; n++)
            freq[n] = 0;
        foreach (var draw in draws)
            if (draw.Special > 0)
                freq[draw.Special] = freq.GetValueOrDefault(draw.Special) + 1;
        return freq;
    }

    // ── Build UI ──────────────────────────────────────────────────

    void BuildUI(
        GameDef game,
        List<Dictionary<int, int>> positionalFreqs,
        Dictionary<int, int>? specialFreq,
        int analyzedCount)
    {
        contentContainer.Children.Clear();

        bool isDigit   = game.Mode is InputMode.Digits3 or InputMode.Digits4;
        bool isOrdered = game.Mode == InputMode.Numbers3Ordered;
        bool isStandard = game.Mode is InputMode.Numbers5 or InputMode.Numbers5Plus1;

        for (int pos = 0; pos < game.MainCount; pos++)
        {
            string posTitle = GetPositionTitle(game, pos);
            string? subtitle = isStandard
                ? "(sorted ascending — lower numbers appear here most)"
                : null;

            // For digit games show all 10 digits (0–9), for Derby show 1–12,
            // for standard games show top 15 by frequency
            var posDict = positionalFreqs[pos];
            List<KeyValuePair<int, int>> ranked;

            if (isDigit)
            {
                // Ensure all digits 0-9 present
                var full = new Dictionary<int, int>();
                for (int d = 0; d <= 9; d++)
                    full[d] = posDict.GetValueOrDefault(d);
                ranked = [.. full.OrderByDescending(kv => kv.Value)];
            }
            else if (isOrdered)
            {
                // Derby horses 1–12; show all, sorted by count desc
                var full = new Dictionary<int, int>();
                for (int h = game.MinBall; h <= game.MaxBall; h++)
                    full[h] = posDict.GetValueOrDefault(h);
                ranked = [.. full.OrderByDescending(kv => kv.Value)];
            }
            else
            {
                // Standard: top 15
                ranked = [.. posDict.OrderByDescending(kv => kv.Value).Take(15)];
            }

            contentContainer.Children.Add(
                BuildPositionCard(posTitle, subtitle, ranked));
        }

        // Special ball card
        if (game.HasSpecial && specialFreq != null && specialFreq.Count > 0)
        {
            var ranked = specialFreq.OrderByDescending(kv => kv.Value).Take(15).ToList();
            contentContainer.Children.Add(
                BuildPositionCard("Special Ball", null, ranked));
        }
    }

    static string GetPositionTitle(GameDef game, int pos) => game.Mode switch
    {
        InputMode.Digits3 or InputMode.Digits4
            => $"Position {pos + 1}",
        InputMode.Numbers3Ordered
            => pos switch { 0 => "1st Place 🏇", 1 => "2nd Place 🏇", _ => "3rd Place 🏇" },
        InputMode.Numbers5 or InputMode.Numbers5Plus1
            => pos switch
            {
                0 => "Position 1 (Lowest)",
                4 => "Position 5 (Highest)",
                _ => $"Position {pos + 1}"
            },
        _ => $"Position {pos + 1}"
    };

    // ── Bar color by rank ─────────────────────────────────────────

    static Color BarColor(int rank) => rank switch
    {
        0     => Color.FromArgb("#C62828"),   // Rank 1 — deep red
        1 or 2 => Color.FromArgb("#EF5350"),  // Rank 2-3 — red
        3 or 4 or 5 => Color.FromArgb("#FF8F00"), // Rank 4-6 — amber
        _     => Color.FromArgb("#90A4AE"),   // Rank 7+ — gray-blue
    };

    // ── Build a position card with bar chart ──────────────────────

    View BuildPositionCard(
        string title,
        string? subtitle,
        List<KeyValuePair<int, int>> ranked)
    {
        int maxCount = ranked.Count > 0 ? ranked.Max(kv => kv.Value) : 1;
        if (maxCount == 0) maxCount = 1;

        var vsl = new VerticalStackLayout { Spacing = 0 };

        // Title
        vsl.Children.Add(new Label
        {
            Text           = title,
            FontSize       = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor      = Color.FromArgb("#1E2733"),
            Margin         = new Thickness(0, 0, 0, subtitle != null ? 2 : 8)
        });

        // Subtitle (only for standard games)
        if (subtitle != null)
        {
            vsl.Children.Add(new Label
            {
                Text      = subtitle,
                FontSize  = 10,
                TextColor = Color.FromArgb("#6B7280"),
                Margin    = new Thickness(0, 0, 0, 8)
            });
        }

        // Bar rows
        for (int i = 0; i < ranked.Count; i++)
        {
            var (num, count) = (ranked[i].Key, ranked[i].Value);
            Color barColor = BarColor(i);

            double fillFraction = maxCount > 0 ? (double)count / maxCount : 0;
            double barWidth = Math.Max(2, Math.Min(200, fillFraction * 200));

            var numLabel = new Label
            {
                Text                    = num.ToString(),
                FontSize                = 13,
                FontAttributes          = FontAttributes.Bold,
                TextColor               = barColor,
                HorizontalOptions       = LayoutOptions.End,
                VerticalOptions         = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.End,
                WidthRequest            = 36,
            };

            var bar = new BoxView
            {
                Color             = barColor,
                HeightRequest     = 18,
                WidthRequest      = barWidth,
                CornerRadius      = 4,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions   = LayoutOptions.Center,
            };

            var countLabel = new Label
            {
                Text            = $"{count}x",
                FontSize        = 11,
                TextColor       = Color.FromArgb("#6B7280"),
                VerticalOptions = LayoutOptions.Center,
                Margin          = new Thickness(4, 0, 0, 0)
            };

            var row = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition { Width = 36 },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                ],
                Margin = new Thickness(0, 0, 0, 4),
            };

            row.Add(numLabel,   0, 0);
            row.Add(bar,        1, 0);
            row.Add(countLabel, 2, 0);

            vsl.Children.Add(row);
        }

        return new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape     = new RoundRectangle { CornerRadius = 10 },
            Padding         = new Thickness(12),
            Margin          = new Thickness(0, 0, 0, 4),
            Content         = vsl
        };
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
