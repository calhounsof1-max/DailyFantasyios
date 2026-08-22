using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

public partial class SumRangePage : ContentPage
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
    List<int> _historicalSums = [];
    int _bucketSize = 5;
    int _sweetLow = 0, _sweetHigh = 0;
    readonly List<Entry> _entries = [];

    public static string PresetGame { get; set; } = "Fantasy 5";

    // ── ctor ──────────────────────────────────────────────────────

    public SumRangePage()
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
        BuildEntryBoxes(_games[i]);
        _ = LoadAndRender();
        UpdateGameLogo();
    }

    private async void BtnGoHome_Clicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//MainPage");

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

            entry.TextChanged += (s, ev) =>
            {
                string txt = entry.Text ?? "";
                bool shouldAdvance =
                    (isDigit  && txt.Length >= 1) ||
                    (!isDigit && txt.Length >= 2);

                if (shouldAdvance)
                    Dispatcher.Dispatch(() => MoveToNextEntry(capturedIdx));

                EntryPicks_Changed(s, ev);
            };

            _entries.Add(entry);
            entryBoxesLayout.Children.Add(entry);
        }

        lblEntryHint.Text = $"Enter your numbers ({game.MinBall}–{game.MaxBall}):";
    }

    void MoveToNextEntry(int currentIdx)
    {
        int next = currentIdx + 1;
        if (next < _entries.Count)
            _entries[next].Focus();
    }

    // ── Main load + render ────────────────────────────────────────

    async Task LoadAndRender()
    {
        var game = _games[_selGame];

        MainThread.BeginInvokeOnMainThread(() =>
        {
            loadingOverlay.IsVisible = true;
            lblLoadingMsg.Text       = "Loading draws...";
            histContainer.Children.Clear();
            statsStrip.IsVisible = false;
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

            int analyzedCount = draws.Count;

            if (analyzedCount == 0)
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    loadingOverlay.IsVisible = false;
                    lblStatus.Text = $"{game.Name} — No data available.";
                    histContainer.Children.Clear();
                    histContainer.Children.Add(new Label
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

            // Compute sums off main thread
            var (sums, bucketSize, sweetLow, sweetHigh) = await Task.Run(() =>
            {
                var computedSums = draws
                    .Select(d => d.Main.Sum())
                    .ToList();

                int minS = computedSums.Min();
                int maxS = computedSums.Max();
                int bSize = (maxS - minS) <= 30 ? 1 : 5;

                var (lo, hi) = ComputeSweetSpot(computedSums);
                return (computedSums, bSize, lo, hi);
            });

            _historicalSums = sums;
            _bucketSize     = bucketSize;
            _sweetLow       = sweetLow;
            _sweetHigh      = sweetHigh;

            int minSum = sums.Min();
            int maxSum = sums.Max();
            double avgSum = sums.Average();

            string periodLabel = _selPeriod > 0
                ? $"{analyzedCount} draws analyzed"
                : $"All {analyzedCount} draws analyzed";

            MainThread.BeginInvokeOnMainThread(() =>
            {
                BuildEntryBoxes(game);
                int? userSum = ParseUserSum(game);

                // Update stats strip
                lblStatMin.Text   = minSum.ToString();
                lblStatAvg.Text   = avgSum.ToString("F0");
                lblStatMax.Text   = maxSum.ToString();
                lblStatSweet.Text = $"{_sweetLow}–{_sweetHigh}";
                statsStrip.IsVisible = true;

                BuildHistogram(userSum);
                UpdateSumBadge(game, userSum);

                lblStatus.Text           = $"{game.Name} — {periodLabel}";
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

    // ── Sweet spot computation ────────────────────────────────────

    static (int Low, int High) ComputeSweetSpot(List<int> sums)
    {
        if (sums.Count == 0) return (0, 0);
        var sorted = sums.OrderBy(s => s).ToList();
        int n = sorted.Count;
        int loIdx = (int)Math.Floor(n * 0.20);
        int hiIdx = (int)Math.Ceiling(n * 0.80) - 1;
        if (hiIdx >= n) hiIdx = n - 1;
        if (loIdx > hiIdx) loIdx = hiIdx;
        return (sorted[loIdx], sorted[hiIdx]);
    }

    // ── Build histogram ───────────────────────────────────────────

    void BuildHistogram(int? userSum)
    {
        histContainer.Children.Clear();

        if (_historicalSums.Count == 0) return;

        int minSum = _historicalSums.Min();
        int maxSum = _historicalSums.Max();

        // Title label
        string periodText = _selPeriod > 0 ? $"Last {_selPeriod}" : "All";
        histContainer.Children.Add(new Label
        {
            Text           = $"Sum Distribution — {periodText} draws",
            FontSize       = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor      = Color.FromArgb("#1E2733"),
            Margin         = new Thickness(0, 0, 0, 8)
        });

        // Build buckets
        var buckets = new Dictionary<int, int>();
        int bs = _bucketSize;

        // Align bucket starts to multiples of bucketSize
        int firstBucket = (minSum / bs) * bs;
        int lastBucket  = (maxSum / bs) * bs;

        for (int b = firstBucket; b <= lastBucket; b += bs)
            buckets[b] = 0;

        foreach (int s in _historicalSums)
        {
            int key = (s / bs) * bs;
            buckets[key] = buckets.GetValueOrDefault(key) + 1;
        }

        var sortedBuckets = buckets.OrderBy(kv => kv.Key).ToList();
        int maxCount = sortedBuckets.Max(kv => kv.Value);
        if (maxCount == 0) maxCount = 1;

        foreach (var (bucketStart, count) in sortedBuckets)
        {
            if (count == 0) continue;

            int bucketEnd = bucketStart + bs - 1;
            string bucketLabel = bs == 1 ? bucketStart.ToString() : $"{bucketStart}–{bucketEnd}";

            bool isUserBucket = userSum.HasValue
                && userSum.Value >= bucketStart
                && userSum.Value <= bucketEnd;

            bool isSweetSpot = bucketStart >= _sweetLow && bucketEnd <= _sweetHigh;

            double barWidth = Math.Max(2, (double)count / maxCount * 220);

            Color barColor;
            if (isUserBucket)
                barColor = Color.FromArgb("#7B1FA2");
            else if (isSweetSpot && count == maxCount)
                barColor = Color.FromArgb("#2E7D32");
            else if (isSweetSpot)
                barColor = Color.FromArgb("#43A047");
            else
                barColor = Color.FromArgb("#1565C0");

            var bucketLbl = new Label
            {
                Text                    = bucketLabel,
                FontSize                = 11,
                TextColor               = isUserBucket ? Color.FromArgb("#7B1FA2")
                                        : isSweetSpot  ? Color.FromArgb("#2E7D32")
                                        :                Color.FromArgb("#374151"),
                HorizontalOptions       = LayoutOptions.End,
                VerticalOptions         = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.End,
                WidthRequest            = 52,
            };

            var bar = new BoxView
            {
                Color             = barColor,
                HeightRequest     = 18,
                WidthRequest      = barWidth,
                CornerRadius      = 3,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions   = LayoutOptions.Center,
            };

            // Count label + optional "◀ YOUR SUM" marker
            string countText = $"{count}x";
            if (isUserBucket) countText += "  ◀ YOUR SUM";

            var countLbl = new Label
            {
                Text            = countText,
                FontSize        = 10,
                TextColor       = isUserBucket ? Color.FromArgb("#7B1FA2") : Color.FromArgb("#6B7280"),
                FontAttributes  = isUserBucket ? FontAttributes.Bold : FontAttributes.None,
                VerticalOptions = LayoutOptions.Center,
                Margin          = new Thickness(4, 0, 0, 0)
            };

            var row = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition { Width = 52 },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                ],
                HeightRequest = 26,
                Margin        = new Thickness(0, 1),
            };

            row.Add(bucketLbl, 0, 0);
            row.Add(bar,       1, 0);
            row.Add(countLbl,  2, 0);

            histContainer.Children.Add(row);
        }

        // Legend
        histContainer.Children.Add(new Label
        {
            Text      = "■ Sweet Spot (middle 60%)   ■ Your sum   ■ Other",
            FontSize  = 10,
            TextColor = Color.FromArgb("#6B7280"),
            Margin    = new Thickness(0, 10, 0, 0),
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = "■ ", TextColor = Color.FromArgb("#43A047"), FontSize = 12 },
                    new Span { Text = "Sweet Spot (middle 60%)   ", TextColor = Color.FromArgb("#6B7280"), FontSize = 10 },
                    new Span { Text = "■ ", TextColor = Color.FromArgb("#7B1FA2"), FontSize = 12 },
                    new Span { Text = "Your sum   ", TextColor = Color.FromArgb("#6B7280"), FontSize = 10 },
                    new Span { Text = "■ ", TextColor = Color.FromArgb("#1565C0"), FontSize = 12 },
                    new Span { Text = "Other", TextColor = Color.FromArgb("#6B7280"), FontSize = 10 },
                }
            }
        });
    }

    // ── Entry picks changed ───────────────────────────────────────

    private void EntryPicks_Changed(object? sender, TextChangedEventArgs e)
    {
        if (_historicalSums.Count == 0) return;

        var game = _games[_selGame];
        int? userSum = ParseUserSum(game);

        if (userSum.HasValue)
            lblYourSum.Text = $"Sum: {userSum.Value}";
        else
            lblYourSum.Text = "Sum: —";

        UpdateSumBadge(game, userSum);
        BuildHistogram(userSum);
    }

    int? ParseUserSum(GameDef game)
    {
        if (_entries.Count != game.MainCount) return null;

        var nums = new List<int>();
        foreach (var en in _entries)
        {
            if (!int.TryParse(en.Text?.Trim(), out int n)) return null;
            nums.Add(n);
        }

        return nums.Sum();
    }

    void UpdateSumBadge(GameDef game, int? userSum)
    {
        if (userSum == null || _sweetLow == 0 && _sweetHigh == 0)
        {
            sumScoreBadge.IsVisible = false;
            return;
        }

        int s = userSum.Value;

        // Calculate range width for "borderline" threshold (10% of sweet spot range)
        int rangeWidth = Math.Max(1, _sweetHigh - _sweetLow);
        int borderline = Math.Max(1, (int)Math.Round(rangeWidth * 0.10));

        if (s >= _sweetLow && s <= _sweetHigh)
        {
            sumScoreBadge.BackgroundColor = Color.FromArgb("#2E7D32");
            lblSumScore.Text              = "✓ In Range";
        }
        else if (s >= _sweetLow - borderline && s <= _sweetHigh + borderline)
        {
            sumScoreBadge.BackgroundColor = Color.FromArgb("#F57C00");
            lblSumScore.Text              = "⚠ Borderline";
        }
        else
        {
            sumScoreBadge.BackgroundColor = Color.FromArgb("#C62828");
            lblSumScore.Text              = "✗ Out of Range";
        }

        sumScoreBadge.IsVisible = true;
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
