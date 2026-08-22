using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

public partial class BalanceCheckPage : ContentPage
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
    List<(int[] Main, int Special)> _draws = [];
    readonly List<Entry> _entries = [];

    public static string PresetGame { get; set; } = "Fantasy 5";

    // ── ctor ──────────────────────────────────────────────────────

    public BalanceCheckPage()
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

            // Store draws
            _draws = draws.Select(d => (d.Main, d.Special)).ToList();

            string periodLabel = _selPeriod > 0
                ? $"{analyzedCount} draws analyzed"
                : $"All {analyzedCount} draws analyzed";

            MainThread.BeginInvokeOnMainThread(() =>
            {
                BuildEntryBoxes(game);
                BuildCharts(userNums: null);
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

    // ── Build charts ──────────────────────────────────────────────

    void BuildCharts(int[]? userNums)
    {
        contentContainer.Children.Clear();

        if (_draws.Count == 0) return;

        var game = _games[_selGame];

        if (game.Mode == InputMode.Digits3 || game.Mode == InputMode.Digits4)
        {
            // Digit games: odd/even digit split and high/low digit split
            BuildDigitOddEvenSection(game, userNums);
            BuildDigitHighLowSection(game, userNums);
        }
        else if (game.Mode == InputMode.Numbers3Ordered)
        {
            // Daily Derby: only odd/even split for the 3 main horses
            BuildOddEvenSection(game, userNums);
        }
        else
        {
            // Standard games (Numbers5, Numbers5Plus1)
            BuildOddEvenSection(game, userNums);
            BuildHighLowSection(game, userNums);
        }
    }

    // ── Odd/Even section (standard ball games) ────────────────────

    void BuildOddEvenSection(GameDef game, int[]? userNums)
    {
        int totalDraws = _draws.Count;
        int mainCount  = game.MainCount;

        // Count odd numbers per draw
        var freq = new Dictionary<int, int>();
        for (int k = 0; k <= mainCount; k++) freq[k] = 0;

        foreach (var (main, _) in _draws)
        {
            int oddCount = main.Count(n => n % 2 != 0);
            freq[oddCount] = freq.GetValueOrDefault(oddCount) + 1;
        }

        int? userOddCount = null;
        if (userNums != null && userNums.Length == mainCount)
            userOddCount = userNums.Count(n => n % 2 != 0);

        int maxCount = freq.Values.Max();
        if (maxCount == 0) maxCount = 1;

        var card = MakeCard();
        var stack = (VerticalStackLayout)((Border)card).Content;

        stack.Children.Add(new Label
        {
            Text           = "Odd / Even Split",
            FontSize       = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor      = Color.FromArgb("#1E2733"),
        });
        stack.Children.Add(new Label
        {
            Text      = "How many odd numbers appear in winning draws",
            FontSize  = 11,
            TextColor = Color.FromArgb("#6B7280"),
            Margin    = new Thickness(0, 0, 0, 8),
        });

        for (int k = 0; k <= mainCount; k++)
        {
            int count  = freq[k];
            double pct = totalDraws > 0 ? (double)count / totalDraws * 100.0 : 0;
            bool isUser = userOddCount.HasValue && userOddCount.Value == k;
            bool isBest = count == maxCount && count > 0;
            string splitLabel = $"{k} Odd / {mainCount - k} Even";

            stack.Children.Add(BuildSplitRow(splitLabel, count, pct, maxCount, isUser, isBest));
        }

        stack.Children.Add(BuildLegend());
        contentContainer.Children.Add(card);
    }

    // ── High/Low section (standard ball games) ────────────────────

    void BuildHighLowSection(GameDef game, int[]? userNums)
    {
        int totalDraws = _draws.Count;
        int mainCount  = game.MainCount;
        double midpoint = (game.MinBall + game.MaxBall) / 2.0;
        int midInt = (int)Math.Floor(midpoint);

        var freq = new Dictionary<int, int>();
        for (int k = 0; k <= mainCount; k++) freq[k] = 0;

        foreach (var (main, _) in _draws)
        {
            int lowCount = main.Count(n => n <= midpoint);
            freq[lowCount] = freq.GetValueOrDefault(lowCount) + 1;
        }

        int? userLowCount = null;
        if (userNums != null && userNums.Length == mainCount)
            userLowCount = userNums.Count(n => n <= midpoint);

        int maxCount = freq.Values.Max();
        if (maxCount == 0) maxCount = 1;

        var card = MakeCard();
        var stack = (VerticalStackLayout)((Border)card).Content;

        stack.Children.Add(new Label
        {
            Text           = "High / Low Split",
            FontSize       = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor      = Color.FromArgb("#1E2733"),
        });
        stack.Children.Add(new Label
        {
            Text      = $"Low = {game.MinBall}–{midInt}, High = {midInt + 1}–{game.MaxBall}",
            FontSize  = 11,
            TextColor = Color.FromArgb("#6B7280"),
            Margin    = new Thickness(0, 0, 0, 8),
        });

        for (int k = 0; k <= mainCount; k++)
        {
            int count  = freq[k];
            double pct = totalDraws > 0 ? (double)count / totalDraws * 100.0 : 0;
            bool isUser = userLowCount.HasValue && userLowCount.Value == k;
            bool isBest = count == maxCount && count > 0;
            string splitLabel = $"{k} Low / {mainCount - k} High";

            stack.Children.Add(BuildSplitRow(splitLabel, count, pct, maxCount, isUser, isBest));
        }

        stack.Children.Add(BuildLegend());
        contentContainer.Children.Add(card);
    }

    // ── Digit odd/even section ────────────────────────────────────

    void BuildDigitOddEvenSection(GameDef game, int[]? userNums)
    {
        int totalDraws = _draws.Count;
        int mainCount  = game.MainCount;

        var freq = new Dictionary<int, int>();
        for (int k = 0; k <= mainCount; k++) freq[k] = 0;

        foreach (var (main, _) in _draws)
        {
            // digits: 1,3,5,7,9 = odd; 0,2,4,6,8 = even
            int oddCount = main.Count(n => n % 2 != 0);
            freq[oddCount] = freq.GetValueOrDefault(oddCount) + 1;
        }

        int? userOddCount = null;
        if (userNums != null && userNums.Length == mainCount)
            userOddCount = userNums.Count(n => n % 2 != 0);

        int maxCount = freq.Values.Max();
        if (maxCount == 0) maxCount = 1;

        var card = MakeCard();
        var stack = (VerticalStackLayout)((Border)card).Content;

        stack.Children.Add(new Label
        {
            Text           = "Odd / Even Digit Split",
            FontSize       = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor      = Color.FromArgb("#1E2733"),
        });
        stack.Children.Add(new Label
        {
            Text      = "Odd digits: 1,3,5,7,9  /  Even digits: 0,2,4,6,8",
            FontSize  = 11,
            TextColor = Color.FromArgb("#6B7280"),
            Margin    = new Thickness(0, 0, 0, 8),
        });

        for (int k = 0; k <= mainCount; k++)
        {
            int count  = freq[k];
            double pct = totalDraws > 0 ? (double)count / totalDraws * 100.0 : 0;
            bool isUser = userOddCount.HasValue && userOddCount.Value == k;
            bool isBest = count == maxCount && count > 0;
            string splitLabel = $"{k} Odd / {mainCount - k} Even";

            stack.Children.Add(BuildSplitRow(splitLabel, count, pct, maxCount, isUser, isBest));
        }

        stack.Children.Add(BuildLegend());
        contentContainer.Children.Add(card);
    }

    // ── Digit high/low section ────────────────────────────────────

    void BuildDigitHighLowSection(GameDef game, int[]? userNums)
    {
        int totalDraws = _draws.Count;
        int mainCount  = game.MainCount;
        // digits 0–4 = low, 5–9 = high
        const int digitMid = 4;

        var freq = new Dictionary<int, int>();
        for (int k = 0; k <= mainCount; k++) freq[k] = 0;

        foreach (var (main, _) in _draws)
        {
            int lowCount = main.Count(n => n <= digitMid);
            freq[lowCount] = freq.GetValueOrDefault(lowCount) + 1;
        }

        int? userLowCount = null;
        if (userNums != null && userNums.Length == mainCount)
            userLowCount = userNums.Count(n => n <= digitMid);

        int maxCount = freq.Values.Max();
        if (maxCount == 0) maxCount = 1;

        var card = MakeCard();
        var stack = (VerticalStackLayout)((Border)card).Content;

        stack.Children.Add(new Label
        {
            Text           = "High / Low Digit Split",
            FontSize       = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor      = Color.FromArgb("#1E2733"),
        });
        stack.Children.Add(new Label
        {
            Text      = "Low digits: 0–4  /  High digits: 5–9",
            FontSize  = 11,
            TextColor = Color.FromArgb("#6B7280"),
            Margin    = new Thickness(0, 0, 0, 8),
        });

        for (int k = 0; k <= mainCount; k++)
        {
            int count  = freq[k];
            double pct = totalDraws > 0 ? (double)count / totalDraws * 100.0 : 0;
            bool isUser = userLowCount.HasValue && userLowCount.Value == k;
            bool isBest = count == maxCount && count > 0;
            string splitLabel = $"{k} Low / {mainCount - k} High";

            stack.Children.Add(BuildSplitRow(splitLabel, count, pct, maxCount, isUser, isBest));
        }

        stack.Children.Add(BuildLegend());
        contentContainer.Children.Add(card);
    }

    // ── Bar row builder ───────────────────────────────────────────

    View BuildSplitRow(string splitLabel, int count, double pct, int maxCount, bool isUser, bool isBest)
    {
        double barWidth = Math.Max(2, (double)count / maxCount * 180.0);

        Color barColor;
        if (isUser)       barColor = Color.FromArgb("#7B1FA2");
        else if (isBest)  barColor = Color.FromArgb("#2E7D32");
        else              barColor = Color.FromArgb("#1565C0");

        var labelCol = new Label
        {
            Text                    = splitLabel,
            FontSize                = 12,
            TextColor               = isUser ? Color.FromArgb("#7B1FA2")
                                    : isBest ? Color.FromArgb("#2E7D32")
                                    :          Color.FromArgb("#374151"),
            HorizontalOptions       = LayoutOptions.End,
            VerticalOptions         = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.End,
            WidthRequest            = 110,
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

        var pctLabel = new Label
        {
            Text            = $"{pct:F0}%",
            FontSize        = 11,
            TextColor       = Color.FromArgb("#6B7280"),
            VerticalOptions = LayoutOptions.Center,
            WidthRequest    = 44,
        };

        var userMarker = new Label
        {
            Text            = isUser ? "◀ YOUR PICK" : "",
            FontSize        = 10,
            TextColor       = Color.FromArgb("#7B1FA2"),
            FontAttributes  = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            IsVisible       = isUser,
        };

        var row = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition { Width = 110 },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = 44 },
                new ColumnDefinition { Width = GridLength.Auto },
            ],
            HeightRequest = 26,
            Margin        = new Thickness(0, 2),
        };

        row.Add(labelCol,   0, 0);
        row.Add(bar,        1, 0);
        row.Add(pctLabel,   2, 0);
        row.Add(userMarker, 3, 0);

        return row;
    }

    // ── Legend ────────────────────────────────────────────────────

    View BuildLegend()
    {
        return new Label
        {
            Margin = new Thickness(0, 10, 0, 0),
            FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = "■ ", TextColor = Color.FromArgb("#2E7D32"), FontSize = 12 },
                    new Span { Text = "Most frequent   ", TextColor = Color.FromArgb("#6B7280"), FontSize = 10 },
                    new Span { Text = "■ ", TextColor = Color.FromArgb("#7B1FA2"), FontSize = 12 },
                    new Span { Text = "Your pick   ", TextColor = Color.FromArgb("#6B7280"), FontSize = 10 },
                    new Span { Text = "■ ", TextColor = Color.FromArgb("#1565C0"), FontSize = 12 },
                    new Span { Text = "Other", TextColor = Color.FromArgb("#6B7280"), FontSize = 10 },
                }
            }
        };
    }

    // ── Card builder ──────────────────────────────────────────────

    static View MakeCard()
    {
        var stack = new VerticalStackLayout { Spacing = 4 };
        return new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape     = new RoundRectangle { CornerRadius = 10 },
            Padding         = new Thickness(12),
            Margin          = new Thickness(0),
            Content         = stack,
        };
    }

    // ── Balance badge ─────────────────────────────────────────────

    void UpdateBadge(GameDef game, int[]? userNums)
    {
        if (userNums == null || userNums.Length != game.MainCount)
        {
            balanceBadge.IsVisible = false;
            return;
        }

        int mainCount = game.MainCount;

        // Compute odd/even and high/low splits
        int oddCount = userNums.Count(n => n % 2 != 0);
        int evenCount = mainCount - oddCount;

        int lowCount, highCount;
        if (game.Mode == InputMode.Digits3 || game.Mode == InputMode.Digits4)
        {
            lowCount  = userNums.Count(n => n <= 4);
            highCount = mainCount - lowCount;
        }
        else
        {
            double midpoint = (game.MinBall + game.MaxBall) / 2.0;
            lowCount  = userNums.Count(n => n <= midpoint);
            highCount = mainCount - lowCount;
        }

        // For digit games (range 0–9), "balanced" = within 1 of 50/50
        // For standard games (mainCount=5), best is 2/3 or 3/2
        bool oddEvenPoor  = oddCount == 0 || evenCount == 0;
        bool highLowPoor  = lowCount == 0 || highCount == 0;

        bool oddEvenGood;
        bool highLowGood;

        if (mainCount % 2 == 0)
        {
            // Even count: perfect = exactly half
            oddEvenGood = Math.Abs(oddCount - mainCount / 2) <= 1;
            highLowGood = Math.Abs(lowCount - mainCount / 2) <= 1;
        }
        else
        {
            // Odd count (e.g. 5): perfect = 2/3 or 3/2
            int half = mainCount / 2;
            oddEvenGood = oddCount == half || oddCount == half + 1;
            highLowGood = lowCount == half || lowCount == half + 1;
        }

        string scoreText;
        Color badgeColor;

        if (oddEvenPoor || highLowPoor)
        {
            scoreText  = "✗ Unbalanced";
            badgeColor = Color.FromArgb("#C62828");
        }
        else if (oddEvenGood && highLowGood)
        {
            scoreText  = "✓ Balanced";
            badgeColor = Color.FromArgb("#2E7D32");
        }
        else
        {
            scoreText  = "~ Borderline";
            badgeColor = Color.FromArgb("#F57C00");
        }

        lblBalanceScore.Text          = scoreText;
        balanceBadge.BackgroundColor  = badgeColor;
        balanceBadge.IsVisible        = true;
    }

    // ── Entry picks changed ───────────────────────────────────────

    private void EntryPicks_Changed(object? sender, TextChangedEventArgs e)
    {
        if (_draws.Count == 0) return;

        var game     = _games[_selGame];
        int[]? nums  = ParseUserNums(game);

        UpdateBadge(game, nums);
        BuildCharts(nums);
    }

    int[]? ParseUserNums(GameDef game)
    {
        if (_entries.Count != game.MainCount) return null;

        var nums = new List<int>();
        foreach (var en in _entries)
        {
            if (!int.TryParse(en.Text?.Trim(), out int n)) return null;
            nums.Add(n);
        }

        return [.. nums];
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
