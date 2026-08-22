using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

public partial class ComboFilterPage : ContentPage
{
    // ── Game config ───────────────────────────────────────────────

    record GameDef(string Name, int Pick, int MaxBall, int MinBall, string AccentHex);

    static readonly GameDef[] _games =
    [
        new("Fantasy 5",     5, 39,  1, "#FF8F00"),
        new("Super Lotto",   5, 47,  1, "#7B1FA2"),
        new("Powerball",     5, 69,  1, "#C62828"),
        new("Mega Millions", 5, 70,  1, "#E65100"),
    ];

    // ── State ─────────────────────────────────────────────────────

    int      _selGame    = 0;
    int[]    _numState   = [];
    Border[] _numBorders = [];
    Label[]  _numLabels  = [];

    // Filter toggles
    bool _filterSumOn    = true;
    bool _filterOddOn    = false;
    bool _filterHighOn   = false;
    bool _filterConsecOn = false;
    bool _filterGroupOn  = false;
    bool _filterRootOn   = false;

    // Stepper values
    int _maxConsec = 1;
    int _minGroups = 2;

    // Digital root selection (1-9, all allowed by default)
    HashSet<int> _allowedRoots = [1, 2, 3, 4, 5, 6, 7, 8, 9];
    Border[]     _rootBorders  = new Border[10]; // index 1–9

    // Last filtered results (for copy-all)
    List<int[]> _filtered = [];

    public static string PresetGame { get; set; } = "Fantasy 5";

    // ── Constructor ───────────────────────────────────────────────

    public ComboFilterPage()
    {
        InitializeComponent();

        foreach (var g in _games)
            gamePicker.Items.Add(g.Name);

        gamePicker.SelectedIndex = 0;
        UpdateGameLogo();
        BuildRootButtons();
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
        SetDefaultSumRange();
        UpdateMidpointLabel();
        UpdateEstimate();
        resultsContainer.Children.Clear();
        _filtered.Clear();
        lblStatus.Text = $"Pick numbers for {_games[i].Name}.";
        UpdateGameLogo();
    }

    // ── Number grid ───────────────────────────────────────────────

    void BuildNumberGrid()
    {
        poolGrid.Children.Clear();
        var game  = _games[_selGame];
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
                BackgroundColor   = Color.FromArgb("#E5E7EB"),
                StrokeThickness   = 0,
                StrokeShape       = new RoundRectangle { CornerRadius = 20 },
                WidthRequest      = 40,
                HeightRequest     = 40,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
                Margin            = new Thickness(2),
                Content           = lbl,
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
        _numState[idx] = _numState[idx] == 0 ? 1 : 0;
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

        if (_numState[idx] == 0)
        {
            border.BackgroundColor = Color.FromArgb("#E5E7EB");
            lbl.TextColor          = Color.FromArgb("#374151");
            lbl.Text               = num.ToString();
        }
        else
        {
            border.BackgroundColor = Color.FromArgb(game.AccentHex);
            lbl.TextColor          = Colors.White;
            lbl.Text               = num.ToString();
        }
    }

    void UpdatePoolCount()
    {
        int n = _numState.Count(s => s == 1);
        lblPoolCount.Text = $"Pool: {n} numbers selected";
    }

    List<int> GetPoolNums() =>
        _numState
            .Select((s, i) => (s, i))
            .Where(x => x.s == 1)
            .Select(x => _games[_selGame].MinBall + x.i)
            .ToList();

    // ── Default sum ranges ────────────────────────────────────────

    void SetDefaultSumRange()
    {
        var (lo, hi) = _games[_selGame].Name switch
        {
            "Fantasy 5"     => (72,  130),
            "Super Lotto"   => (83,  168),
            "Powerball"     => (121, 246),
            "Mega Millions" => (122, 250),
            _               => (0,   999),
        };
        entrySumMin.Text = lo.ToString();
        entrySumMax.Text = hi.ToString();
    }

    void UpdateMidpointLabel()
    {
        var game = _games[_selGame];
        int mid  = (game.MaxBall + game.MinBall) / 2;
        lblHighTitle.Text = $"High Count  (above {mid})";
    }

    // ── Estimate ──────────────────────────────────────────────────

    void UpdateEstimate()
    {
        var game = _games[_selGame];
        var pool = GetPoolNums();

        if (pool.Count < game.Pick)
        {
            lblEstimate.Text = $"Select at least {game.Pick} numbers to filter.";
            btnApply.IsEnabled = false;
            return;
        }

        long total = CombCount(pool.Count, game.Pick);
        btnApply.IsEnabled = total <= 100_000;

        if (total > 100_000)
            lblEstimate.Text = $"Pool → {total:N0} combinations (max 100k). Reduce pool.";
        else
            lblEstimate.Text = $"Pool → {total:N0} combinations to filter.";
    }

    static long CombCount(int n, int k)
    {
        if (k > n || k < 0) return 0;
        if (k == 0 || k == n) return 1;
        k = Math.Min(k, n - k);
        long r = 1;
        for (int i = 0; i < k; i++)
        {
            r = r * (n - i) / (i + 1);
            if (r > 100_001) return r;
        }
        return r;
    }

    // ── Filter toggles ────────────────────────────────────────────

    private void ToggleSum_Tapped(object? sender, TappedEventArgs e)
    {
        _filterSumOn = !_filterSumOn;
        toggleSumBorder.BackgroundColor = _filterSumOn
            ? Color.FromArgb("#1565C0") : Color.FromArgb("#6B7280");
        toggleSumLabel.Text = _filterSumOn ? "ON" : "OFF";
        sumPanel.IsVisible  = _filterSumOn;
    }

    private void ToggleOdd_Tapped(object? sender, TappedEventArgs e)
    {
        _filterOddOn = !_filterOddOn;
        toggleOddBorder.BackgroundColor = _filterOddOn
            ? Color.FromArgb("#1565C0") : Color.FromArgb("#6B7280");
        toggleOddLabel.Text = _filterOddOn ? "ON" : "OFF";
        oddPanel.IsVisible  = _filterOddOn;
    }

    private void ToggleHigh_Tapped(object? sender, TappedEventArgs e)
    {
        _filterHighOn = !_filterHighOn;
        toggleHighBorder.BackgroundColor = _filterHighOn
            ? Color.FromArgb("#1565C0") : Color.FromArgb("#6B7280");
        toggleHighLabel.Text = _filterHighOn ? "ON" : "OFF";
        highPanel.IsVisible  = _filterHighOn;
    }

    private void ToggleConsec_Tapped(object? sender, TappedEventArgs e)
    {
        _filterConsecOn = !_filterConsecOn;
        toggleConsecBorder.BackgroundColor = _filterConsecOn
            ? Color.FromArgb("#1565C0") : Color.FromArgb("#6B7280");
        toggleConsecLabel.Text = _filterConsecOn ? "ON" : "OFF";
        consecPanel.IsVisible  = _filterConsecOn;
    }

    private void ToggleGroup_Tapped(object? sender, TappedEventArgs e)
    {
        _filterGroupOn = !_filterGroupOn;
        toggleGroupBorder.BackgroundColor = _filterGroupOn
            ? Color.FromArgb("#1565C0") : Color.FromArgb("#6B7280");
        toggleGroupLabel.Text = _filterGroupOn ? "ON" : "OFF";
        groupPanel.IsVisible  = _filterGroupOn;
    }

    private void ToggleRoot_Tapped(object? sender, TappedEventArgs e)
    {
        _filterRootOn = !_filterRootOn;
        toggleRootBorder.BackgroundColor = _filterRootOn
            ? Color.FromArgb("#1565C0") : Color.FromArgb("#6B7280");
        toggleRootLabel.Text = _filterRootOn ? "ON" : "OFF";
        rootPanel.IsVisible  = _filterRootOn;
    }

    // ── Digital root button grid ──────────────────────────────────

    void BuildRootButtons()
    {
        rootButtonRow.Children.Clear();
        for (int root = 1; root <= 9; root++)
        {
            int captured = root;
            bool sel = _allowedRoots.Contains(root);

            var lbl = new Label
            {
                Text                    = root.ToString(),
                FontSize                = 16,
                FontAttributes          = FontAttributes.Bold,
                TextColor               = sel ? Colors.White : Color.FromArgb("#374151"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment   = TextAlignment.Center,
            };

            var border = new Border
            {
                BackgroundColor = sel ? Color.FromArgb("#1565C0") : Color.FromArgb("#E5E7EB"),
                StrokeThickness = 0,
                StrokeShape     = new RoundRectangle { CornerRadius = 22 },
                WidthRequest    = 44,
                HeightRequest   = 44,
                Content         = lbl,
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, ev) => ToggleRootButton(captured);
            border.GestureRecognizers.Add(tap);

            _rootBorders[root] = border;
            rootButtonRow.Children.Add(border);
        }
    }

    void ToggleRootButton(int root)
    {
        if (_allowedRoots.Contains(root)) _allowedRoots.Remove(root);
        else                              _allowedRoots.Add(root);

        bool sel    = _allowedRoots.Contains(root);
        var  border = _rootBorders[root];
        border.BackgroundColor = sel ? Color.FromArgb("#1565C0") : Color.FromArgb("#E5E7EB");
        if (border.Content is Label lbl)
            lbl.TextColor = sel ? Colors.White : Color.FromArgb("#374151");
    }

    // ── Stepper handlers ──────────────────────────────────────────

    private void BtnConsecMinus_Clicked(object? sender, EventArgs e)
    {
        if (_maxConsec > 0) _maxConsec--;
        lblConsecVal.Text = _maxConsec.ToString();
    }

    private void BtnConsecPlus_Clicked(object? sender, EventArgs e)
    {
        int max = _games[_selGame].Pick - 1;
        if (_maxConsec < max) _maxConsec++;
        lblConsecVal.Text = _maxConsec.ToString();
    }

    private void BtnGroupMinus_Clicked(object? sender, EventArgs e)
    {
        if (_minGroups > 1) _minGroups--;
        lblGroupVal.Text = _minGroups.ToString();
    }

    private void BtnGroupPlus_Clicked(object? sender, EventArgs e)
    {
        if (_minGroups < _games[_selGame].Pick) _minGroups++;
        lblGroupVal.Text = _minGroups.ToString();
    }

    private void FilterEntry_Changed(object? sender, TextChangedEventArgs e) => UpdateEstimate();

    private void BtnResetDefaults_Clicked(object? sender, EventArgs e)
    {
        SetDefaultSumRange();

        // Reset odd/even to off
        if (_filterOddOn)  { _filterOddOn  = false; oddPanel.IsVisible  = false; toggleOddBorder.BackgroundColor  = Color.FromArgb("#6B7280"); toggleOddLabel.Text  = "OFF"; }
        if (_filterHighOn) { _filterHighOn = false; highPanel.IsVisible = false; toggleHighBorder.BackgroundColor = Color.FromArgb("#6B7280"); toggleHighLabel.Text = "OFF"; }
        if (_filterConsecOn) { _filterConsecOn = false; consecPanel.IsVisible = false; toggleConsecBorder.BackgroundColor = Color.FromArgb("#6B7280"); toggleConsecLabel.Text = "OFF"; }
        if (_filterGroupOn)  { _filterGroupOn  = false; groupPanel.IsVisible  = false; toggleGroupBorder.BackgroundColor  = Color.FromArgb("#6B7280"); toggleGroupLabel.Text  = "OFF"; }
        if (_filterRootOn)   { _filterRootOn   = false; rootPanel.IsVisible   = false; toggleRootBorder.BackgroundColor   = Color.FromArgb("#6B7280"); toggleRootLabel.Text   = "OFF"; }
        _allowedRoots = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        BuildRootButtons();

        _maxConsec = 1; lblConsecVal.Text = "1";
        _minGroups = 2; lblGroupVal.Text  = "2";

        // Ensure Sum is ON
        if (!_filterSumOn)
        {
            _filterSumOn = true;
            sumPanel.IsVisible = true;
            toggleSumBorder.BackgroundColor = Color.FromArgb("#1565C0");
            toggleSumLabel.Text = "ON";
        }
    }

    // ── Apply filters ─────────────────────────────────────────────

    private async void BtnApply_Clicked(object? sender, EventArgs e)
    {
        var game = _games[_selGame];
        var pool = GetPoolNums();

        if (pool.Count < game.Pick)
        {
            lblStatus.Text = $"Select at least {game.Pick} numbers.";
            return;
        }

        long total = CombCount(pool.Count, game.Pick);
        if (total > 100_000)
        {
            lblStatus.Text = $"Too many combinations ({total:N0}). Reduce pool first.";
            return;
        }

        // Parse filter values
        int sumMin = 0, sumMax = 0;
        int oddMin = 0, oddMax = 0;
        int highMin = 0, highMax = 0;

        if (_filterSumOn)
        {
            if (!int.TryParse(entrySumMin.Text, out sumMin) ||
                !int.TryParse(entrySumMax.Text, out sumMax) ||
                sumMin > sumMax)
            {
                lblStatus.Text = "Sum Range: enter valid Min ≤ Max.";
                return;
            }
        }

        if (_filterOddOn)
        {
            if (!int.TryParse(entryOddMin.Text, out oddMin) ||
                !int.TryParse(entryOddMax.Text, out oddMax) ||
                oddMin > oddMax || oddMin < 0 || oddMax > game.Pick)
            {
                lblStatus.Text = $"Odd Count: enter 0–{game.Pick}.";
                return;
            }
        }

        if (_filterHighOn)
        {
            if (!int.TryParse(entryHighMin.Text, out highMin) ||
                !int.TryParse(entryHighMax.Text, out highMax) ||
                highMin > highMax || highMin < 0 || highMax > game.Pick)
            {
                lblStatus.Text = $"High Count: enter 0–{game.Pick}.";
                return;
            }
        }

        if (_filterRootOn && _allowedRoots.Count == 0)
        {
            lblStatus.Text = "Digital Root: select at least one root value.";
            return;
        }

        // Snapshot for background thread
        bool fSum    = _filterSumOn;
        bool fOdd    = _filterOddOn;
        bool fHigh   = _filterHighOn;
        bool fConsec = _filterConsecOn;
        bool fGroup  = _filterGroupOn;
        bool fRoot   = _filterRootOn;
        int  pick    = game.Pick;
        int  maxC    = _maxConsec;
        int  minG    = _minGroups;
        int  mid     = (game.MaxBall + game.MinBall) / 2;
        int  sm      = sumMin,  sx = sumMax;
        int  om      = oddMin,  ox = oddMax;
        int  hm      = highMin, hx = highMax;
        var  allowedR = fRoot ? new HashSet<int>(_allowedRoots) : null;

        loadingOverlay.IsVisible = true;
        lblLoadingMsg.Text       = $"Filtering {total:N0} combinations…";
        resultsContainer.Children.Clear();

        try
        {
            var result = await Task.Run(() =>
            {
                var passed = new List<int[]>();
                foreach (var combo in Combinations(pool, pick))
                {
                    // Compute sum once — used by Sum Range and Digital Root
                    int comboSum = 0;
                    if (fSum || fRoot)
                        foreach (int n in combo) comboSum += n;

                    if (fSum && (comboSum < sm || comboSum > sx)) continue;

                    if (fOdd)
                    {
                        int o = 0;
                        foreach (int n in combo) if (n % 2 != 0) o++;
                        if (o < om || o > ox) continue;
                    }
                    if (fHigh)
                    {
                        int h = 0;
                        foreach (int n in combo) if (n > mid) h++;
                        if (h < hm || h > hx) continue;
                    }
                    if (fConsec)
                    {
                        // combo is already sorted ascending from Combinations()
                        int pairs = 0;
                        for (int i = 1; i < combo.Length; i++)
                            if (combo[i] == combo[i - 1] + 1) pairs++;
                        if (pairs > maxC) continue;
                    }
                    if (fGroup)
                    {
                        // Decade groups: 1-9, 10-19, 20-29, etc.
                        int prevGroup = combo[0] / 10;
                        int groups = 1;
                        for (int i = 1; i < combo.Length; i++)
                        {
                            int g = combo[i] / 10;
                            if (g != prevGroup) { groups++; prevGroup = g; }
                        }
                        if (groups < minG) continue;
                    }
                    if (fRoot)
                    {
                        int dr = comboSum % 9;
                        if (dr == 0) dr = 9;
                        if (!allowedR!.Contains(dr)) continue;
                    }
                    passed.Add(combo);
                }
                return passed;
            });

            _filtered = result;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                loadingOverlay.IsVisible = false;
                BuildResults(game, result, total);
                lblStatus.Text = $"{result.Count:N0} of {total:N0} combinations passed filters · {game.Name}";
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

    // ── Results display ───────────────────────────────────────────

    void BuildResults(GameDef game, List<int[]> combos, long totalBefore)
    {
        resultsContainer.Children.Clear();

        if (combos.Count == 0)
        {
            resultsContainer.Children.Add(new Label
            {
                Text      = "No combinations passed. Try relaxing your filter values.",
                TextColor = Color.FromArgb("#6B7280"),
                FontSize  = 13,
                Margin    = new Thickness(4, 8),
            });
            return;
        }

        const int maxDisplay = 300;
        bool truncated = combos.Count > maxDisplay;

        // Header
        var header = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
            ],
            Margin = new Thickness(0, 0, 0, 6),
        };

        double pct = totalBefore > 0 ? (double)combos.Count / totalBefore * 100.0 : 0;
        string title = truncated
            ? $"Showing 1–{maxDisplay} of {combos.Count:N0}  ({pct:F1}% passed)"
            : $"{combos.Count:N0} combinations  ({pct:F1}% passed)";

        header.Add(new Label
        {
            Text            = title,
            FontSize        = 13,
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

        var accent = Color.FromArgb(game.AccentHex);
        int displayCount = Math.Min(combos.Count, maxDisplay);

        for (int t = 0; t < displayCount; t++)
        {
            var combo = combos[t];

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
                Direction  = Microsoft.Maui.Layouts.FlexDirection.Row,
                Wrap       = Microsoft.Maui.Layouts.FlexWrap.Wrap,
                AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            };

            foreach (int n in combo)
            {
                chips.Children.Add(new Border
                {
                    BackgroundColor = accent,
                    StrokeThickness = 0,
                    StrokeShape     = new RoundRectangle { CornerRadius = 16 },
                    Padding         = new Thickness(10, 4),
                    Margin          = new Thickness(2),
                    Content = new Label
                    {
                        Text           = n.ToString(),
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

        if (truncated)
        {
            resultsContainer.Children.Add(new Label
            {
                Text                    = $"… {combos.Count - maxDisplay:N0} more not shown — use Copy All to get everything.",
                TextColor               = Color.FromArgb("#6B7280"),
                FontSize                = 12,
                Margin                  = new Thickness(4, 8),
                HorizontalTextAlignment = TextAlignment.Center,
            });
        }
    }

    // ── Copy all ──────────────────────────────────────────────────

    private async void BtnCopyAll_Clicked(object? sender, EventArgs e)
    {
        if (_filtered.Count == 0) return;
        string text = string.Join("\n", _filtered.Select(c => string.Join(" ", c)));
        await Clipboard.SetTextAsync(text);
        lblStatus.Text = $"Copied {_filtered.Count:N0} combinations to clipboard.";
    }

    // ── Clear pool ────────────────────────────────────────────────

    private void BtnClearPool_Clicked(object? sender, EventArgs e)
    {
        Array.Fill(_numState, 0);
        for (int i = 0; i < _numBorders.Length; i++) RefreshButton(i);
        UpdatePoolCount();
        UpdateEstimate();
        resultsContainer.Children.Clear();
        _filtered.Clear();
    }

    // ── Home ──────────────────────────────────────────────────────

    private async void BtnGoHome_Clicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//MainPage");

    // ── Combination iterator (lexicographic order, sorted asc) ────

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
            _              => "logo_fantasy5.png"
        };
    }
}
