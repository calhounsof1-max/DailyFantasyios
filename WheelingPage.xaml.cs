using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

public partial class WheelingPage : ContentPage
{
    // ── Game config ───────────────────────────────────────────────

    enum WheelMode { Full, Abbreviated, Key }

    record GameDef(string Name, int Pick, int MaxBall, int MinBall, string AccentHex);

    static readonly GameDef[] _games =
    [
        new("Fantasy 5",     5, 39,  1, "#FF8F00"),
        new("Super Lotto",   5, 47,  1, "#7B1FA2"),
        new("Powerball",     5, 69,  1, "#C62828"),
        new("Mega Millions", 5, 70,  1, "#E65100"),
        new("Daily 3",       3,  9,  0, "#1565C0"),
        new("Daily 4",       4,  9,  0, "#0D47A1"),
        new("Daily Derby",   3, 12,  1, "#4A148C"),
    ];

    // Guarantee presets: (matchNeeded, ifThisMany, label)
    static (int Match, int If, string Label)[] GuaranteesFor(int pick) => pick switch
    {
        5 =>
        [
            (3, 3, "3-match if any 3 drawn are in your pool"),
            (3, 4, "3-match if any 4 drawn are in your pool"),
            (3, 5, "3-match if all 5 drawn are in your pool"),
            (4, 4, "4-match if any 4 drawn are in your pool"),
            (4, 5, "4-match if all 5 drawn are in your pool"),
        ],
        4 =>
        [
            (2, 2, "2-match if any 2 drawn are in your pool"),
            (3, 3, "3-match if any 3 drawn are in your pool"),
            (3, 4, "3-match if all 4 drawn are in your pool"),
        ],
        3 =>
        [
            (2, 2, "2-match if any 2 drawn are in your pool"),
            (2, 3, "2-match if all 3 drawn are in your pool"),
            (3, 3, "3-match (exact) if all 3 drawn are in your pool"),
        ],
        _ => [(3, 5, "3-match if all drawn are in your pool")]
    };

    // ── State ─────────────────────────────────────────────────────

    int      _selGame      = 0;
    WheelMode _mode        = WheelMode.Full;
    int      _selGuarantee = 2; // default to 3-if-5 (fewest tickets)

    // per-number states: 0=not in pool, 1=in pool, 2=key number
    int[]    _numState   = [];
    Border[] _numBorders = [];
    Label[]  _numLabels  = [];

    List<int[]> _generatedTickets = [];

    public static string PresetGame { get; set; } = "Fantasy 5";

    // ── ctor ──────────────────────────────────────────────────────

    public WheelingPage()
    {
        InitializeComponent();

        foreach (var g in _games)
            gamePicker.Items.Add(g.Name);

        gamePicker.SelectedIndex = 0;
        UpdateGameLogo();
    }

    // ── Lifecycle ─────────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();
        int idx = Array.FindIndex(_games, g => g.Name == PresetGame);
        if (idx >= 0 && idx != gamePicker.SelectedIndex)
            gamePicker.SelectedIndex = idx;
    }

    // ── Game picker ───────────────────────────────────────────────

    private void GamePicker_Changed(object? sender, EventArgs e)
    {
        int i = gamePicker.SelectedIndex;
        if (i < 0) return;
        _selGame = i;
        BuildNumberGrid();
        UpdateGuaranteePicker();
        UpdateEstimate();
        resultsContainer.Children.Clear();
        lblStatus.Text = $"Pick numbers for your {_games[i].Name} wheel.";
        UpdateGameLogo();
    }

    // ── Number grid ───────────────────────────────────────────────

    void BuildNumberGrid()
    {
        poolGrid.Children.Clear();
        var game = _games[_selGame];
        int count = game.MaxBall - game.MinBall + 1;

        _numState   = new int[count];
        _numBorders = new Border[count];
        _numLabels  = new Label[count];

        for (int i = 0; i < count; i++)
        {
            int capturedI = i;
            int num = game.MinBall + i;

            var lbl = new Label
            {
                Text                    = num.ToString(),
                FontSize                = 12,
                FontAttributes          = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment   = TextAlignment.Center,
                TextColor               = Color.FromArgb("#374151"),
            };

            var border = new Border
            {
                BackgroundColor = Color.FromArgb("#E5E7EB"),
                StrokeThickness = 0,
                StrokeShape     = new RoundRectangle { CornerRadius = 20 },
                WidthRequest    = 40,
                HeightRequest   = 40,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
                Margin  = new Thickness(2),
                Content = lbl,
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, ev) => OnNumberTapped(capturedI);
            border.GestureRecognizers.Add(tap);

            _numBorders[i] = border;
            _numLabels[i]  = lbl;
            poolGrid.Children.Add(border);
        }

        UpdatePoolCount();
    }

    void OnNumberTapped(int idx)
    {
        if (_mode == WheelMode.Key)
            _numState[idx] = (_numState[idx] + 1) % 3; // 0→1→2→0
        else
            _numState[idx] = _numState[idx] == 0 ? 1 : 0; // 0→1→0

        RefreshButton(idx);
        UpdatePoolCount();
        UpdateEstimate();
    }

    void RefreshButton(int idx)
    {
        var game   = _games[_selGame];
        var border = _numBorders[idx];
        var lbl    = _numLabels[idx];
        int num    = game.MinBall + idx;

        switch (_numState[idx])
        {
            case 0:
                border.BackgroundColor = Color.FromArgb("#E5E7EB");
                lbl.TextColor          = Color.FromArgb("#374151");
                lbl.Text               = num.ToString();
                break;
            case 1:
                border.BackgroundColor = Color.FromArgb(game.AccentHex);
                lbl.TextColor          = Colors.White;
                lbl.Text               = num.ToString();
                break;
            case 2: // key number
                border.BackgroundColor = Color.FromArgb("#E65100");
                lbl.TextColor          = Colors.White;
                lbl.Text               = "★" + num;
                break;
        }
    }

    void UpdatePoolCount()
    {
        int poolCount = _numState.Count(s => s >= 1);
        int keyCount  = _numState.Count(s => s == 2);
        lblPoolCount.Text = keyCount > 0
            ? $"Pool: {poolCount} numbers  ({keyCount} key ★)"
            : $"Pool: {poolCount} numbers selected";
    }

    // ── Guarantee picker ──────────────────────────────────────────

    void UpdateGuaranteePicker()
    {
        var game       = _games[_selGame];
        var guarantees = GuaranteesFor(game.Pick);
        guaranteePicker.Items.Clear();
        foreach (var g in guarantees)
            guaranteePicker.Items.Add(g.Label);
        _selGuarantee = Math.Clamp(_selGuarantee, 0, guarantees.Length - 1);
        guaranteePicker.SelectedIndex = _selGuarantee;
    }

    private void GuaranteePicker_Changed(object? sender, EventArgs e)
    {
        if (guaranteePicker.SelectedIndex >= 0)
        {
            _selGuarantee = guaranteePicker.SelectedIndex;
            UpdateEstimate();
        }
    }

    // ── Mode tabs ─────────────────────────────────────────────────

    private void TabFull_Tapped(object? sender, TappedEventArgs e)   => SetMode(WheelMode.Full);
    private void TabAbbrev_Tapped(object? sender, TappedEventArgs e) => SetMode(WheelMode.Abbreviated);
    private void TabKey_Tapped(object? sender, TappedEventArgs e)    => SetMode(WheelMode.Key);

    void SetMode(WheelMode mode)
    {
        // When leaving Key mode, demote key numbers back to plain pool
        if (_mode == WheelMode.Key && mode != WheelMode.Key)
        {
            for (int i = 0; i < _numState.Length; i++)
                if (_numState[i] == 2) { _numState[i] = 1; RefreshButton(i); }
        }

        _mode = mode;
        UpdateModeUI();
        UpdatePoolCount();
        UpdateEstimate();
    }

    void UpdateModeUI()
    {
        bool isFull   = _mode == WheelMode.Full;
        bool isAbbrev = _mode == WheelMode.Abbreviated;
        bool isKey    = _mode == WheelMode.Key;

        tabFullBorder.BackgroundColor   = isFull   ? Color.FromArgb("#1565C0") : Color.FromArgb("#E3F2FD");
        tabFullLabel.TextColor          = isFull   ? Colors.White : Color.FromArgb("#1565C0");
        tabAbbrevBorder.BackgroundColor = isAbbrev ? Color.FromArgb("#1565C0") : Color.FromArgb("#E3F2FD");
        tabAbbrevLabel.TextColor        = isAbbrev ? Colors.White : Color.FromArgb("#1565C0");
        tabKeyBorder.BackgroundColor    = isKey    ? Color.FromArgb("#1565C0") : Color.FromArgb("#E3F2FD");
        tabKeyLabel.TextColor           = isKey    ? Colors.White : Color.FromArgb("#1565C0");

        abbrevPanel.IsVisible  = isAbbrev;
        keyHintPanel.IsVisible = isKey;
    }

    // ── Estimate ──────────────────────────────────────────────────

    void UpdateEstimate()
    {
        var game  = _games[_selGame];
        var pool  = GetPoolNums();
        var keys  = GetKeyNums();
        int pick  = game.Pick;

        if (pool.Count < pick)
        {
            lblEstimate.Text = $"Select at least {pick} numbers to generate tickets.";
            return;
        }

        switch (_mode)
        {
            case WheelMode.Full:
                long full = CombCount(pool.Count, pick);
                lblEstimate.Text = full > 500
                    ? $"Full wheel: {full:N0} tickets (reduce pool to generate)"
                    : $"Full wheel: {full:N0} tickets";
                break;

            case WheelMode.Key:
                if (keys.Count == 0 || keys.Count >= pick)
                {
                    lblEstimate.Text = "Tap a pool number again to make it a Key ★.";
                }
                else
                {
                    long kw = CombCount(pool.Count - keys.Count, pick - keys.Count);
                    lblEstimate.Text = $"Key wheel: {kw:N0} tickets  ({keys.Count} key number{(keys.Count > 1 ? "s" : "")})";
                }
                break;

            case WheelMode.Abbreviated:
                lblEstimate.Text = "Abbreviated: ticket count computed after generation.";
                break;
        }
    }

    // ── Pool helpers ──────────────────────────────────────────────

    List<int> GetPoolNums() =>
        _numState
            .Select((s, i) => (s, i))
            .Where(x => x.s >= 1)
            .Select(x => _games[_selGame].MinBall + x.i)
            .ToList();

    List<int> GetKeyNums() =>
        _numState
            .Select((s, i) => (s, i))
            .Where(x => x.s == 2)
            .Select(x => _games[_selGame].MinBall + x.i)
            .ToList();

    // ── Generate ──────────────────────────────────────────────────

    private async void BtnGenerate_Clicked(object? sender, EventArgs e)
    {
        var game = _games[_selGame];
        var pool = GetPoolNums();
        var keys = GetKeyNums();
        int pick = game.Pick;

        if (pool.Count < pick)
        {
            lblStatus.Text = $"Select at least {pick} numbers for your pool.";
            return;
        }

        if (_mode == WheelMode.Key)
        {
            if (keys.Count == 0)
            { lblStatus.Text = "Tap a pool number again (it turns orange) to mark it as Key."; return; }
            if (keys.Count >= pick)
            { lblStatus.Text = $"You need fewer key numbers than your pick count ({pick})."; return; }
        }

        if (_mode == WheelMode.Full)
        {
            long est = CombCount(pool.Count, pick);
            if (est > 500)
            {
                lblStatus.Text = $"Full wheel would be {est:N0} tickets — reduce pool size first.";
                return;
            }
        }

        if (_mode == WheelMode.Abbreviated && pool.Count > 20)
        {
            lblStatus.Text = "Abbreviated wheel: max 20 numbers in pool.";
            return;
        }

        loadingOverlay.IsVisible = true;
        lblLoadingMsg.Text       = "Generating wheel...";
        resultsContainer.Children.Clear();

        try
        {
            List<int[]> tickets;

            if (_mode == WheelMode.Full)
            {
                tickets = await Task.Run(() => FullWheel(pool, pick));
            }
            else if (_mode == WheelMode.Key)
            {
                tickets = await Task.Run(() => KeyWheel(pool, keys, pick));
            }
            else
            {
                var guarantees = GuaranteesFor(pick);
                int gi = Math.Clamp(_selGuarantee, 0, guarantees.Length - 1);
                var (gMatch, gIf, gLabel) = guarantees[gi];
                SetMsg($"Computing {gLabel}...");
                tickets = await Task.Run(() => AbbrevWheel(pool, pick, gMatch, gIf));
            }

            _generatedTickets = tickets;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                loadingOverlay.IsVisible = false;
                BuildResults(game, tickets, keys);
                lblStatus.Text = $"{tickets.Count} tickets · {game.Name} · Pool: {pool.Count} numbers";
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

    // ── Results view ──────────────────────────────────────────────

    void BuildResults(GameDef game, List<int[]> tickets, List<int> keyNums)
    {
        resultsContainer.Children.Clear();

        if (tickets.Count == 0)
        {
            resultsContainer.Children.Add(new Label
            {
                Text      = "No tickets generated. Try a larger pool or a different guarantee level.",
                TextColor = Color.FromArgb("#6B7280"),
                FontSize  = 13,
                Margin    = new Thickness(4, 8),
            });
            return;
        }

        // Header row
        var header = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
            ],
            Margin = new Thickness(0, 0, 0, 6),
        };

        header.Add(new Label
        {
            Text            = $"{tickets.Count} Tickets Generated",
            FontSize        = 14,
            FontAttributes  = FontAttributes.Bold,
            TextColor       = Color.FromArgb("#1E2733"),
            VerticalOptions = LayoutOptions.Center,
        }, 0, 0);

        var btnCopy = new Button
        {
            Text            = "Copy All",
            FontSize        = 12,
            BackgroundColor = Color.FromArgb("#2E7D32"),
            TextColor       = Colors.White,
            HeightRequest   = 34,
            CornerRadius    = 8,
            Padding         = new Thickness(12, 0),
        };
        btnCopy.Clicked += BtnCopyAll_Clicked;
        header.Add(btnCopy, 1, 0);
        resultsContainer.Children.Add(header);

        var keySet = new HashSet<int>(keyNums);

        for (int t = 0; t < tickets.Count; t++)
        {
            var ticket = tickets[t];

            var row = new Border
            {
                BackgroundColor = Colors.White,
                StrokeThickness = 0,
                StrokeShape     = new RoundRectangle { CornerRadius = 8 },
                Padding         = new Thickness(10, 6),
                Margin          = new Thickness(0, 0, 0, 3),
            };

            var inner = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition { Width = new GridLength(34) },
                    new ColumnDefinition { Width = GridLength.Star },
                ]
            };

            inner.Add(new Label
            {
                Text            = $"{t + 1}.",
                FontSize        = 12,
                TextColor       = Color.FromArgb("#9CA3AF"),
                VerticalOptions = LayoutOptions.Center,
            }, 0, 0);

            var chips = new FlexLayout
            {
                Direction   = Microsoft.Maui.Layouts.FlexDirection.Row,
                Wrap        = Microsoft.Maui.Layouts.FlexWrap.Wrap,
                AlignItems  = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            };

            foreach (int n in ticket)
            {
                bool isKey = keySet.Contains(n);
                chips.Children.Add(new Border
                {
                    BackgroundColor = isKey
                        ? Color.FromArgb("#E65100")
                        : Color.FromArgb(game.AccentHex),
                    StrokeThickness = 0,
                    StrokeShape     = new RoundRectangle { CornerRadius = 16 },
                    Padding         = new Thickness(10, 4),
                    Margin          = new Thickness(2),
                    Content = new Label
                    {
                        Text           = isKey ? $"★{n}" : n.ToString(),
                        FontSize       = 13,
                        FontAttributes = FontAttributes.Bold,
                        TextColor      = Colors.White,
                    }
                });
            }

            inner.Add(chips, 1, 0);
            row.Content = inner;
            resultsContainer.Children.Add(row);
        }
    }

    // ── Button handlers ───────────────────────────────────────────

    private async void BtnCopyAll_Clicked(object? sender, EventArgs e)
    {
        if (_generatedTickets.Count == 0) return;
        string text = string.Join("\n", _generatedTickets.Select(t => string.Join(" ", t)));
        await Clipboard.SetTextAsync(text);
        lblStatus.Text = $"Copied {_generatedTickets.Count} tickets to clipboard.";
    }

    private void BtnClearPool_Clicked(object? sender, EventArgs e)
    {
        Array.Fill(_numState, 0);
        for (int i = 0; i < _numBorders.Length; i++) RefreshButton(i);
        UpdatePoolCount();
        UpdateEstimate();
        resultsContainer.Children.Clear();
        _generatedTickets.Clear();
    }

    private async void BtnGoHome_Clicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//MainPage");

    void SetMsg(string msg) =>
        MainThread.BeginInvokeOnMainThread(() => lblLoadingMsg.Text = msg);

    // ── Wheel algorithms ──────────────────────────────────────────

    // Iterate all k-combinations of src in lexicographic order
    static IEnumerable<int[]> Combinations(IList<int> src, int k)
    {
        int n = src.Count;
        if (k > n || k <= 0) yield break;
        var idx = Enumerable.Range(0, k).ToArray();
        while (true)
        {
            yield return idx.Select(i => src[i]).ToArray();
            int i2 = k - 1;
            while (i2 >= 0 && idx[i2] == i2 + n - k) i2--;
            if (i2 < 0) yield break;
            idx[i2]++;
            for (int j = i2 + 1; j < k; j++) idx[j] = idx[j - 1] + 1;
        }
    }

    // C(n, k) — returns early once result exceeds 100k
    static long CombCount(int n, int k)
    {
        if (k > n || k < 0) return 0;
        if (k == 0 || k == n) return 1;
        k = Math.Min(k, n - k);
        long r = 1;
        for (int i = 0; i < k; i++)
        {
            r = r * (n - i) / (i + 1);
            if (r > 100_000) return r;
        }
        return r;
    }

    // Full wheel: every possible pick-size combination of pool
    static List<int[]> FullWheel(List<int> pool, int pick) =>
        [.. Combinations(pool, pick)];

    // Key wheel: key numbers on every ticket + full wheel of remaining
    static List<int[]> KeyWheel(List<int> pool, List<int> keys, int pick)
    {
        int needed = pick - keys.Count;
        if (needed <= 0 || needed > pool.Count - keys.Count) return [];
        var rest = pool.Except(keys).ToList();
        return Combinations(rest, needed)
            .Select(c => keys.Concat(c).OrderBy(x => x).ToArray())
            .ToList();
    }

    // Abbreviated wheel using greedy set cover.
    // Guarantees: for every (guaranteeIf)-subset of pool,
    // at least one ticket shares >= guaranteeMatch numbers with it.
    static List<int[]> AbbrevWheel(List<int> pool, int pick, int gMatch, int gIf)
    {
        if (gIf < gMatch || gIf > pick || pool.Count < pick) return [];

        var allTickets = Combinations(pool, pick).ToArray();
        var targets    = Combinations(pool, gIf).Select(t => new HashSet<int>(t)).ToArray();

        // Precompute: for each ticket, which targets it covers
        var coverage = new List<int>[allTickets.Length];
        for (int i = 0; i < allTickets.Length; i++)
        {
            coverage[i] = [];
            var ts = new HashSet<int>(allTickets[i]);
            for (int t = 0; t < targets.Length; t++)
                if (ts.Count(n => targets[t].Contains(n)) >= gMatch)
                    coverage[i].Add(t);
        }

        var covered    = new bool[targets.Length];
        var used       = new bool[allTickets.Length];
        int coveredN   = 0;
        var wheel      = new List<int[]>();

        // Track cover counts so we can quickly find the best ticket
        // uncoveredScore[i] = # uncovered targets that ticket i covers
        var score = coverage.Select(c => c.Count).ToArray();

        while (coveredN < targets.Length)
        {
            // Find unused ticket with highest uncovered score
            int bestIdx = -1, bestScore = 0;
            for (int i = 0; i < allTickets.Length; i++)
            {
                if (!used[i] && score[i] > bestScore)
                { bestScore = score[i]; bestIdx = i; }
            }
            if (bestIdx < 0 || bestScore == 0) break;

            wheel.Add(allTickets[bestIdx]);
            used[bestIdx] = true;

            // Mark covered targets; update scores of other tickets
            foreach (int t in coverage[bestIdx])
            {
                if (covered[t]) continue;
                covered[t] = true;
                coveredN++;
                // Decrement score for every ticket that listed this target
                for (int i = 0; i < allTickets.Length; i++)
                    if (!used[i] && coverage[i].Contains(t))
                        score[i]--;
            }
        }

        return wheel;
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
}
