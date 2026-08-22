using System.Text.Json;

namespace DailyFantasyMAUI;

public partial class GapTrackerPage : ContentPage
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
        string    AccentHex,
        int       ApiGameId = 0
    );

    static readonly GameDef[] _games =
    [
        new("Fantasy 5",    "data/myFantasy5.csv",   "myFantasy5.csv",   InputMode.Numbers5,      5, 39,  1, false,  0, "#FF8F00"),
        new("Super Lotto",  "data/mySuperlotto.csv", "mySuperlotto.csv", InputMode.Numbers5Plus1, 5, 47,  1, true,  27, "#7B1FA2"),
        new("Powerball",    "", "",                                        InputMode.Numbers5Plus1, 5, 69,  1, true,  26, "#C62828", ApiGameId: 12),
        new("Mega Millions","data/myMegaMillions.csv","myMegaMillions.csv",InputMode.Numbers5Plus1, 5, 70,  1, true,  25, "#E65100", ApiGameId: 4),
        new("Daily 3",      "data/myDaily3.csv",     "myDaily3.csv",     InputMode.Digits3,       3,  9,  0, false,  0, "#1565C0"),
        new("Daily 4",      "", "",                                        InputMode.Digits4,       4,  9,  0, false,  0, "#0D47A1", ApiGameId: 14),
        new("Daily Derby",  "", "",                                        InputMode.Numbers3Ordered,3,12,  1, false,  0, "#4A148C", ApiGameId: 11),
    ];

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

    public GapTrackerPage()
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
            lblLoadingMsg.Text       = "Analyzing gaps...";
            summaryStrip.IsVisible   = false;
            contentContainer.Children.Clear();
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

            // Apply period filter (most-recent first)
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

            int totalDraws = draws.Count;
            if (totalDraws == 0)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    loadingOverlay.IsVisible = false;
                    lblStatus.Text = $"{game.Name} — No data available.";
                });
                return;
            }

            // Compute gap stats for each ball
            // draws[0] = most recent draw
            var lastSeen   = new Dictionary<int, int>(); // number → draws ago (0 = in last draw)
            var appearances = new Dictionary<int, int>();

            for (int drawIdx = 0; drawIdx < draws.Count; drawIdx++)
            {
                foreach (int num in draws[drawIdx])
                {
                    if (!lastSeen.ContainsKey(num))
                        lastSeen[num] = drawIdx;      // first time found = how far back

                    appearances[num] = appearances.GetValueOrDefault(num) + 1;
                }
            }

            // Numbers that never appeared in period: lastSeen = totalDraws (never seen)
            int minBall = game.MinBall;
            int maxBall = game.MaxBall;
            for (int n = minBall; n <= maxBall; n++)
            {
                if (!lastSeen.ContainsKey(n))
                {
                    lastSeen[n]    = totalDraws;
                    appearances[n] = 0;
                }
            }

            // Build gap info list
            var infos = new List<(int Num, int LastSeenAgo, double AvgGap, double DueScore, int Appearances)>();
            for (int n = minBall; n <= maxBall; n++)
            {
                int apps     = appearances.GetValueOrDefault(n, 0);
                int lsAgo    = lastSeen.GetValueOrDefault(n, totalDraws);
                double avgGap = apps > 0 ? (double)totalDraws / apps : totalDraws;
                double score  = avgGap > 0 ? lsAgo / avgGap : 0;
                infos.Add((n, lsAgo, avgGap, score, apps));
            }

            // Sort: most overdue first
            var sorted = infos.OrderByDescending(x => x.DueScore).ToList();

            // Summary: top 5 overdue and top 5 recent
            var top5Overdue = sorted.Take(5).Select(x => x.Num.ToString()).ToList();
            var top5Recent  = sorted.TakeLast(5).Select(x => x.Num.ToString()).Reverse().ToList();

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                lblOverdueNums.Text = string.Join("  ", top5Overdue);
                lblRecentNums.Text  = string.Join("  ", top5Recent);
                summaryStrip.IsVisible = true;
                lblStatus.Text = $"{game.Name} — {totalDraws} draws · {sorted.Count} numbers tracked";

                contentContainer.Children.Clear();

                // Section headers + rows
                bool shownOverdue  = false;
                bool shownNormal   = false;
                bool shownRecent   = false;

                foreach (var info in sorted)
                {
                    // Section header
                    if (!shownOverdue && info.DueScore >= 1.0)
                    {
                        shownOverdue = true;
                        contentContainer.Children.Add(MakeSectionHeader("Overdue (score >= 1.0)", "#C62828"));
                    }
                    else if (!shownNormal && info.DueScore < 1.0 && info.DueScore >= 0.4)
                    {
                        shownNormal = true;
                        contentContainer.Children.Add(MakeSectionHeader("Normal", "#607D8B"));
                    }
                    else if (!shownRecent && info.DueScore < 0.4)
                    {
                        shownRecent = true;
                        contentContainer.Children.Add(MakeSectionHeader("Recent (appeared often)", "#2E7D32"));
                    }

                    contentContainer.Children.Add(BuildRow(info.Num, info.LastSeenAgo, info.AvgGap, info.DueScore, info.Appearances));

                    // Yield every 10 rows to keep UI responsive
                    if (contentContainer.Children.Count % 10 == 0)
                        await Task.Yield();
                }

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

    // ── Build a single row ────────────────────────────────────────

    static View BuildRow(int num, int lastSeenAgo, double avgGap, double dueScore, int appearances)
    {
        Color barColor = dueScore >= 2.0 ? Color.FromArgb("#C62828") :
                         dueScore >= 1.0 ? Color.FromArgb("#FF6F00") :
                         dueScore >= 0.4 ? Color.FromArgb("#607D8B") :
                                           Color.FromArgb("#2E7D32");

        double barWidth = Math.Max(4, Math.Min(160, dueScore / 3.0 * 160.0));

        var row = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition { Width = new GridLength(44) },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
            ],
            Margin = new Thickness(0, 0, 0, 2)
        };

        // Number badge
        row.Add(new Label
        {
            Text                    = num.ToString(),
            FontSize                = 12,
            FontAttributes          = FontAttributes.Bold,
            TextColor               = Colors.White,
            BackgroundColor         = barColor,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment   = TextAlignment.Center,
            WidthRequest            = 36,
            HeightRequest           = 24,
            VerticalOptions         = LayoutOptions.Center,
        }, 0, 0);

        // Bar
        row.Add(new BoxView
        {
            Color             = barColor,
            HeightRequest     = 13,
            WidthRequest      = barWidth,
            CornerRadius      = 3,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions   = LayoutOptions.Center,
            Margin            = new Thickness(8, 0),
        }, 1, 0);

        // Info label
        string avgStr = avgGap < 10 ? $"{avgGap:F1}" : $"{avgGap:F0}";
        string agoStr = lastSeenAgo == 0 ? "latest" : $"{lastSeenAgo} ago";
        row.Add(new Label
        {
            Text              = $"{agoStr} · avg {avgStr} · {dueScore:F1}x",
            FontSize          = 11,
            TextColor         = barColor,
            VerticalOptions   = LayoutOptions.Center,
            LineBreakMode     = LineBreakMode.NoWrap,
        }, 2, 0);

        return row;
    }

    static Label MakeSectionHeader(string text, string colorHex) => new Label
    {
        Text           = text,
        FontSize       = 12,
        FontAttributes = FontAttributes.Bold,
        TextColor      = Color.FromArgb(colorHex),
        Margin         = new Thickness(0, 8, 0, 4),
    };

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

                string dateRaw = p.Length > 0 ? p[0].Trim() : "";
                if (!DateTime.TryParse(dateRaw, out _)) continue;

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

        var main = new List<int>();
        for (int i = start; i < start + mainCount + 1 && main.Count < mainCount; i++)
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
