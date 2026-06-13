using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class GeneratePage : ContentPage
{
    // ── Game definitions ─────────────────────────────────────────────────────

    record GameDef(
        string Name, string Short, string SetPrefix,
        int MainMin, int MainMax, int MainPick,
        int SpecialMin, int SpecialMax,       // SpecialMax=0 means no special
        int SetStride,                         // values per row in storage
        string AccentColor, string SpecialColor,
        bool SortMain = true,                  // false = ordered pick (D3/D4/DD)
        bool HasTime  = false
    );

    static readonly GameDef[] Games =
    [
        new("Fantasy 5",        "F5", "f5_set_", 1, 39, 5,  0,  0, 5, "#FF8F00", ""),
        new("Super Lotto Plus", "SL", "sl_set_", 1, 47, 5,  1, 27, 6, "#7B1FA2", "#CE93D8"),
        new("Powerball",        "PB", "pb_set_", 1, 69, 5,  1, 26, 6, "#C62828", "#EF9A9A"),
        new("Daily 3",          "D3", "d3_set_", 0,  9, 3,  0,  0, 3, "#1565C0", "", SortMain: false),
        new("Daily 4",          "D4", "d4_set_", 0,  9, 4,  0,  0, 4, "#00695C", "", SortMain: false),
        new("Daily Derby",      "DD", "dd_set_", 1, 12, 3,  0,  0, 4, "#5D4037", "", SortMain: false, HasTime: true),
    ];

    // ── Per-game frequency caches ─────────────────────────────────────────────

    static Dictionary<int, int>? _f5Freq;
    static Dictionary<int, int>? _slMainFreq, _slMegaFreq;
    static Dictionary<int, int>? _pbMainFreq, _pbBallFreq;
    static Dictionary<int, int>? _d3Freq;
    static Dictionary<int, int>? _d4Freq;
    static Dictionary<int, int>? _ddHorseFreq;
    static List<string>          _ddTimePool = [];

    // Smart generation data (populated alongside freq caches)
    static Dictionary<int, int>? _f5Smart;
    static Dictionary<int, int>? _slMainSmart, _slMegaSmart;
    static (double Mean, double Std) _f5SumStats;
    static (double Mean, double Std) _slSumStats;

    // ── State ─────────────────────────────────────────────────────────────────

    int      _gameIdx = 0;
    int      _count   = 5;
    Button[] _gameBtns = [];

    record GeneratedRow(int[] Main, int Special, string Time);
    List<GeneratedRow> _generated = [];

    // Edit row state
    int          _editRowIdx = -1;
    List<Entry>  _editRowEntries = [];
    Entry?       _editRowTimeEntry;

    // Latest draw for the current game (fetched after generate)
    record LatestDraw(int[] Main, int Special, string Time, string Label, DailyFantasyMAUI.Services.DrawPrizeTier[] Prizes);
    LatestDraw? _latestDraw;

    GameDef Game => Games[_gameIdx];

    // ── Constructor ───────────────────────────────────────────────────────────

    public GeneratePage()
    {
        InitializeComponent();
    }

    // ── Game buttons ──────────────────────────────────────────────────────────

    void BuildGameButtons()
    {
        stkGames.Children.Clear();
        _gameBtns = new Button[Games.Length];
        for (int i = 0; i < Games.Length; i++)
        {
            int idx = i;
            var btn = new Button
            {
                Text = Games[i].Short,
                FontSize = 13, FontAttributes = FontAttributes.Bold,
                BackgroundColor = i == _gameIdx
                    ? Color.FromArgb(Games[i].AccentColor)
                    : Color.FromArgb("#2D3D50"),
                TextColor = Colors.White,
                WidthRequest = 54, HeightRequest = 40,
                CornerRadius = 20, Padding = Thickness.Zero
            };
            btn.Clicked += (_, _) => SelectGame(idx);
            _gameBtns[i] = btn;
            stkGames.Children.Add(btn);
        }
    }

    void SelectGame(int idx)
    {
        _gameIdx = idx;
        for (int i = 0; i < _gameBtns.Length; i++)
            _gameBtns[i].BackgroundColor = i == idx
                ? Color.FromArgb(Games[i].AccentColor)
                : Color.FromArgb("#2D3D50");

        btnGenerate.BackgroundColor = Color.FromArgb(Game.AccentColor);
        ClearResults();
        RefreshSetPicker();
    }

    // ── Count +/− ─────────────────────────────────────────────────────────────

    void BtnMinus_Clicked(object sender, EventArgs e)
    {
        if (_count > 1) { _count--; lblCount.Text = _count.ToString(); }
    }

    void BtnPlus_Clicked(object sender, EventArgs e)
    {
        if (_count < 10) { _count++; lblCount.Text = _count.ToString(); }
    }

    // ── Generate ──────────────────────────────────────────────────────────────

    async void BtnGenerate_Clicked(object sender, EventArgs e)
    {
        btnGenerate.IsEnabled = false;
        spinner.IsVisible = true; spinner.IsRunning = true;
        lblStatus.Text = "Loading past draw data…";
        ClearResults();

        try
        {
            await LoadFrequenciesAsync();
            GenerateRows();
            await FetchLatestDrawAsync();
            DisplayResults();
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            spinner.IsVisible = false; spinner.IsRunning = false;
            btnGenerate.IsEnabled = true;
        }
    }

    // ── Load past-draw frequency data ─────────────────────────────────────────

    async Task LoadFrequenciesAsync()
    {
        var game = Game;

        switch (game.Short)
        {
            case "F5" when _f5Freq == null:
            {
                lblStatus.Text = "Loading Fantasy 5 history…";
                var draws = await GetDataEntry.LoadF5CsvDraws();
                var allNums = draws.Select(d => d.Numbers).ToList(); // newest-first
                _f5Freq    = BuildFreq(allNums.SelectMany(n => n), 1, 39);
                _f5SumStats = ComputeSumStats(allNums);
                _f5Smart    = BuildSmartWeights(_f5Freq, allNums.Take(100).ToList());
                break;
            }
            case "SL" when _slMainFreq == null:
            {
                lblStatus.Text = "Loading Super Lotto draws…";
                var draws = await GetDataEntry.LoadSLCsvDraws();
                var allMain = draws.Select(d => d.MainNumbers).ToList();
                var allMega = draws.Select(d => d.Mega).ToList();
                _slMainFreq = BuildFreq(allMain.SelectMany(n => n), 1, 47);
                _slMegaFreq = BuildFreq(allMega, 1, 27);
                _slSumStats  = ComputeSumStats(allMain);
                _slMainSmart = BuildSmartWeights(_slMainFreq, allMain.Take(100).ToList());
                _slMegaSmart = BuildSmartMegaWeights(_slMegaFreq, allMega.Take(100).ToList());
                break;
            }
            case "PB" when _pbMainFreq == null:
            {
                lblStatus.Text = "Loading Powerball draws…";
                var draws = await GetDataEntry.GetPowerballDraws(200);
                _pbMainFreq = BuildFreq(draws.SelectMany(d => d.MainNumbers), 1, 69);
                _pbBallFreq = BuildFreq(draws.Select(d => d.PBNumber), 1, 26);
                break;
            }
            case "D3" when _d3Freq == null:
            {
                lblStatus.Text = "Loading Daily 3 draws…";
                var draws = await GetDataEntry.GetDaily3Draws(200);
                _d3Freq = BuildFreq(draws.SelectMany(d => d.Numbers), 0, 9);
                break;
            }
            case "D4" when _d4Freq == null:
            {
                lblStatus.Text = "Loading Daily 4 draws…";
                var draws = await GetDataEntry.GetDaily4Draws(200);
                _d4Freq = BuildFreq(draws.SelectMany(d => d.Numbers), 0, 9);
                break;
            }
            case "DD" when _ddHorseFreq == null:
            {
                lblStatus.Text = "Loading Daily Derby draws…";
                var draws = await GetDataEntry.GetDailyDerbyDraws(200);
                _ddHorseFreq = BuildFreq(draws.SelectMany(d => d.Horses), 1, 12);
                _ddTimePool = draws
                    .Where(d => !string.IsNullOrEmpty(d.RaceTime))
                    .Select(d =>
                    {
                        string n = new string(d.RaceTime.Where(char.IsDigit).ToArray());
                        return n.Length >= 3 ? n[^3..] : "";
                    })
                    .Where(t => t.Length == 3)
                    .ToList();
                if (_ddTimePool.Count == 0) // fallback
                    _ddTimePool = Enumerable.Range(0, 1000).Select(i => i.ToString("D3")).ToList();
                break;
            }
        }

        lblStatus.Text = "";
    }

    // Build frequency dict: every number in [min..max] gets at least weight 1
    static Dictionary<int, int> BuildFreq(IEnumerable<int> values, int min, int max)
    {
        var freq = new Dictionary<int, int>();
        for (int n = min; n <= max; n++) freq[n] = 1; // base weight
        foreach (int v in values)
            if (v >= min && v <= max)
                freq[v] = freq.GetValueOrDefault(v, 1) + 1;
        return freq;
    }

    // Compute mean and std of draw sums from historical draws
    static (double Mean, double Std) ComputeSumStats(IEnumerable<int[]> draws)
    {
        var sums = draws.Select(d => (double)d.Sum()).ToList();
        if (sums.Count == 0) return (0, 0);
        double mean = sums.Average();
        double std  = Math.Sqrt(sums.Average(s => (s - mean) * (s - mean)));
        return (mean, std);
    }

    // Build smart weights: combines all-time frequency with a "due" bonus for
    // numbers absent from the most recent draws.
    // recentDraws: newest-first list of int[] (up to 100 draws used).
    static Dictionary<int, int> BuildSmartWeights(Dictionary<int, int> allFreq, List<int[]> recentDraws)
    {
        // Count appearances in recent window
        var recentCnt = new Dictionary<int, int>();
        for (int i = 0; i < recentDraws.Count; i++)
            foreach (int n in recentDraws[i])
                recentCnt[n] = recentCnt.GetValueOrDefault(n, 0) + 1;

        // Average frequency across all numbers (for scaling due bonus)
        double avgFreq = allFreq.Values.Average();
        int window = Math.Max(recentDraws.Count, 1);

        var smart = new Dictionary<int, int>();
        foreach (var (n, freq) in allFreq)
        {
            int rc = recentCnt.GetValueOrDefault(n, 0);
            // Due factor: 0 = appeared recently, 1 = not seen in full window
            double dueFactor = 1.0 - (rc / (double)window * (allFreq.Count / 5.0));
            dueFactor = Math.Clamp(dueFactor, 0.0, 1.0);
            // Smart weight = 60% historical + 40% due bonus
            double w = freq * 0.6 + avgFreq * dueFactor * 0.8;
            smart[n] = Math.Max(1, (int)w);
        }
        return smart;
    }

    // Smart weights for a single special ball (Mega/PB) using skip analysis
    static Dictionary<int, int> BuildSmartMegaWeights(Dictionary<int, int> allFreq, List<int> recentMegas)
    {
        var recentCnt = new Dictionary<int, int>();
        foreach (int m in recentMegas)
            recentCnt[m] = recentCnt.GetValueOrDefault(m, 0) + 1;

        double avgFreq = allFreq.Values.Average();
        int window = Math.Max(recentMegas.Count, 1);
        var smart = new Dictionary<int, int>();
        foreach (var (n, freq) in allFreq)
        {
            int rc = recentCnt.GetValueOrDefault(n, 0);
            double dueFactor = Math.Clamp(1.0 - rc / (double)window * (allFreq.Count / 1.0), 0.0, 1.0);
            double w = freq * 0.6 + avgFreq * dueFactor * 0.8;
            smart[n] = Math.Max(1, (int)w);
        }
        return smart;
    }

    // Smart sample: uses smart weights and retries until the combination passes
    // sum-range and odd/even balance checks (up to 80 attempts then falls back).
    static List<int> SmartSample(Dictionary<int, int> smartWeights, int count, (double Mean, double Std) sumStats)
    {
        bool hasSumStats = sumStats.Std > 0;
        for (int attempt = 0; attempt < 80; attempt++)
        {
            var picked = WeightedSample(smartWeights, count, sort: true);
            int sum  = picked.Sum();
            int odds = picked.Count(n => n % 2 != 0);
            bool sumOk = !hasSumStats || Math.Abs(sum - sumStats.Mean) <= 2.0 * sumStats.Std;
            bool balanceOk = odds >= 1 && odds <= count - 1; // at least 1 odd and 1 even
            if (sumOk && balanceOk) return picked;
        }
        // Fallback: any weighted sample sorted
        return WeightedSample(smartWeights, count, sort: true);
    }

    // ── Row generation ────────────────────────────────────────────────────────

    void GenerateRows()
    {
        _generated.Clear();
        var game = Game;

        var (mainFreq, specialFreq) = game.Short switch
        {
            "F5" => (_f5Freq!,     (Dictionary<int,int>?)null),
            "SL" => (_slMainFreq!, _slMegaFreq),
            "PB" => (_pbMainFreq!, _pbBallFreq),
            "D3" => (_d3Freq!,     null),
            "D4" => (_d4Freq!,     null),
            "DD" => (_ddHorseFreq!, null),
            _    => throw new InvalidOperationException()
        };

        for (int i = 0; i < _count; i++)
        {
            List<int> main;
            if (game.Short == "F5")
                main = SmartSample(_f5Smart ?? mainFreq, game.MainPick, _f5SumStats);
            else if (game.Short == "SL")
                main = SmartSample(_slMainSmart ?? mainFreq, game.MainPick, _slSumStats);
            else if (game.Short is "D3" or "D4")
                main = WeightedSampleWithReplacement(mainFreq, game.MainPick);
            else
                main = WeightedSample(mainFreq, game.MainPick, sort: game.SortMain);

            int special = 0;
            if (specialFreq != null)
            {
                var sfDict = (game.Short == "SL" && _slMegaSmart != null) ? _slMegaSmart : specialFreq;
                special = WeightedSample(sfDict, 1, sort: false)[0];
            }
            string time = game.HasTime ? _ddTimePool[Random.Shared.Next(_ddTimePool.Count)] : "";
            _generated.Add(new GeneratedRow(main.ToArray(), special, time));
        }
    }

    // Weighted random sample without replacement
    static List<int> WeightedSample(Dictionary<int, int> freq, int count, bool sort)
    {
        var pool   = new Dictionary<int, int>(freq);
        var result = new List<int>();
        var rng    = Random.Shared;

        while (result.Count < count && pool.Count > 0)
        {
            int total = pool.Values.Sum();
            int r     = rng.Next(total > 0 ? total : 1);
            int cum   = 0;
            int chosen = pool.Keys.First();
            foreach (var (n, w) in pool)
            {
                cum += w;
                if (r < cum) { chosen = n; break; }
            }
            result.Add(chosen);
            pool.Remove(chosen);
        }

        return sort ? [.. result.OrderBy(x => x)] : result;
    }

    // ── Fetch latest draw for current game ───────────────────────────────────

    async Task FetchLatestDrawAsync()
    {
        _latestDraw = null;
        try
        {
            lblStatus.Text = "Checking latest draw…";
            var game = Game;
            switch (game.Short)
            {
                case "F5":
                {
                    var draws = await GetDataEntry.GetPastDraws(1);
                    if (draws.Count > 0)
                        _latestDraw = new LatestDraw(draws[0].Numbers, 0, "", draws[0].DrawDate, draws[0].Prizes);
                    break;
                }
                case "SL":
                {
                    var draws = await GetDataEntry.GetSuperLottoDraws(1);
                    if (draws.Count > 0)
                        _latestDraw = new LatestDraw(draws[0].MainNumbers, draws[0].MegaNumber, "", draws[0].DrawDate, draws[0].Prizes);
                    break;
                }
                case "PB":
                {
                    var draws = await GetDataEntry.GetPowerballDraws(1);
                    if (draws.Count > 0)
                        _latestDraw = new LatestDraw(draws[0].MainNumbers, draws[0].PBNumber, "", draws[0].DrawDate, draws[0].Prizes);
                    break;
                }
                case "D3":
                {
                    var draws = await GetDataEntry.GetDaily3Draws(1);
                    if (draws.Count > 0)
                        _latestDraw = new LatestDraw(draws[0].Numbers, 0, "", draws[0].DrawDate, draws[0].Prizes);
                    break;
                }
                case "D4":
                {
                    var draws = await GetDataEntry.GetDaily4Draws(1);
                    if (draws.Count > 0)
                        _latestDraw = new LatestDraw(draws[0].Numbers, 0, "", draws[0].DrawDate, draws[0].Prizes);
                    break;
                }
                case "DD":
                {
                    var draws = await GetDataEntry.GetDailyDerbyDraws(1);
                    if (draws.Count > 0)
                    {
                        string norm = new string(draws[0].RaceTime.Where(char.IsDigit).ToArray());
                        string last3 = norm.Length >= 3 ? norm[^3..] : norm;
                        _latestDraw = new LatestDraw(draws[0].Horses, 0, last3, draws[0].DrawDate, draws[0].Prizes);
                    }
                    break;
                }
            }
        }
        catch { /* silently ignore — match display is optional */ }
    }

    static string PrizeFromTier(DailyFantasyMAUI.Services.DrawPrizeTier[] prizes, int tier, string fallback)
    {
        var p = prizes.FirstOrDefault(x => x.Tier == tier);
        if (p != null && p.Amount > 0) return FormatPrize(p.Amount);
        return fallback;
    }

    static string FormatPrize(decimal amount)
    {
        if (amount >= 1_000_000) return $"${amount / 1_000_000:F2}M";
        if (amount >= 1_000)     return $"${amount:N0}";
        return $"${amount:N0}";
    }

    // Returns a prize label string (empty = no win) based on CA Lottery rules
    string CheckWin(GeneratedRow row)
    {
        if (_latestDraw == null) return "";
        var game = Game;
        int[] win  = _latestDraw.Main;
        int[] pick = row.Main;

        switch (game.Short)
        {
            case "F5":
            {
                int m = pick.Intersect(win).Count();
                if (m < 2) return "";
                int tier = 6 - m; // 5→1, 4→2, 3→3, 2→4
                return PrizeFromTier(_latestDraw.Prizes, tier, tier == 1 ? "JACKPOT" : tier == 4 ? "Free Tkt" : "");
            }
            case "SL":
            {
                int m    = pick.Intersect(win).Count();
                bool meg = row.Special > 0 && row.Special == _latestDraw.Special;
                int tier = (m, meg) switch
                {
                    (5, true)  => 1, (5, false) => 2,
                    (4, true)  => 3, (4, false) => 4,
                    (3, true)  => 5, (3, false) => 6,
                    (2, true)  => 7, (1, true)  => 8,
                    (0, true)  => 9, _           => 0
                };
                if (tier == 0) return "";
                return PrizeFromTier(_latestDraw.Prizes, tier, tier == 1 ? "JACKPOT" : "Free Tkt");
            }
            case "PB":
            {
                int m   = pick.Intersect(win).Count();
                bool pb = row.Special > 0 && row.Special == _latestDraw.Special;
                int tier = (m, pb) switch
                {
                    (5, true)  => 1, (5, false) => 2,
                    (4, true)  => 3, (4, false) => 4,
                    (3, true)  => 5, (3, false) => 6,
                    (2, true)  => 7, (1, true)  => 8,
                    (0, true)  => 9, _           => 0
                };
                if (tier == 0) return "";
                return PrizeFromTier(_latestDraw.Prizes, tier, tier == 1 ? "JACKPOT" : "Free Tkt");
            }
            case "D3":
            {
                // Tier 1=Straight, Tier 2=3-way Box, Tier 4=6-way Box Only
                bool straight = pick.Length == win.Length &&
                    pick.Zip(win, (a, b) => a == b).All(m => m);
                if (straight) return PrizeFromTier(_latestDraw.Prizes, 1, "$566");
                bool allSame = pick.Distinct().Count() == 1;
                if (!allSame)
                {
                    var pickSorted = pick.OrderBy(x => x).ToArray();
                    var winSorted  = win.OrderBy(x => x).ToArray();
                    if (pickSorted.SequenceEqual(winSorted))
                    {
                        // 3-way box (has a pair) = tier 2 (~$90), 6-way box (all different) = tier 4 (~$45)
                        bool hasPair = pick[0] == pick[1] || pick[1] == pick[2] || pick[0] == pick[2];
                        return hasPair
                            ? PrizeFromTier(_latestDraw.Prizes, 2, "$90")
                            : PrizeFromTier(_latestDraw.Prizes, 4, "$45");
                    }
                }
                return "";
            }
            case "D4":
            {
                // Tier 1=Straight, Tier 4=Box Only (from CA Lottery API)
                bool straight = pick.Length == win.Length &&
                    pick.Zip(win, (a, b) => a == b).All(m => m);
                if (straight) return PrizeFromTier(_latestDraw.Prizes, 1, "$5,000");
                if (pick.Distinct().Count() == 1) return ""; // four-of-a-kind: straight only
                var pickSorted = pick.OrderBy(x => x).ToArray();
                var winSorted  = win.OrderBy(x => x).ToArray();
                if (pickSorted.SequenceEqual(winSorted))
                    return PrizeFromTier(_latestDraw.Prizes, 4, "$261");
                return "";
            }
            case "DD":
            {
                // Tier 1=Win, 2=Exacta, 3=Trifecta, 4=RaceTime, 5=Win/RT, 6=Exacta/RT, 7=Grand Prize
                bool h1 = pick.Length >= 1 && win.Length >= 1 && pick[0] == win[0];
                bool h2 = pick.Length >= 2 && win.Length >= 2 && pick[1] == win[1];
                bool h3 = pick.Length >= 3 && win.Length >= 3 && pick[2] == win[2];
                bool t  = !string.IsNullOrEmpty(row.Time) &&
                          !string.IsNullOrEmpty(_latestDraw.Time) &&
                          row.Time == _latestDraw.Time;

                var p = _latestDraw.Prizes;
                if (h1 && h2 && h3 && t) return PrizeFromTier(p, 7, "Grand Prize");
                if (h1 && h2 && h3)      return PrizeFromTier(p, 3, "$398");
                if (h1 && h2 && t)       return PrizeFromTier(p, 6, "$92");
                if (h1 && h2)            return PrizeFromTier(p, 2, "$23");
                if (h1 && t)             return PrizeFromTier(p, 5, "$72");
                if (h1)                  return PrizeFromTier(p, 1, "$3");
                if (t)                   return PrizeFromTier(p, 4, "$69");
                return "";
            }
        }
        return "";
    }

    // Weighted random sample WITH replacement (for D3/D4 where digits can repeat)
    static List<int> WeightedSampleWithReplacement(Dictionary<int, int> freq, int count)
    {
        var result = new List<int>();
        var keys   = freq.Keys.ToArray();
        var weights = keys.Select(k => freq[k]).ToArray();
        int total  = weights.Sum();
        var rng    = Random.Shared;

        for (int i = 0; i < count; i++)
        {
            int r = rng.Next(total > 0 ? total : 1), cum = 0;
            int chosen = keys[0];
            for (int j = 0; j < keys.Length; j++)
            {
                cum += weights[j];
                if (r < cum) { chosen = keys[j]; break; }
            }
            result.Add(chosen);
        }
        return result;
    }

    // Returns true for jackpots / grand prizes (shown in gold)
    static bool IsBigPrize(string prize)
    {
        if (string.IsNullOrEmpty(prize)) return false;
        if (prize.Contains("JACKPOT") || prize.Contains("Grand") || prize.Contains("M")) return true;
        // Parse amount: gold if >= $5,000
        if (prize.StartsWith("$") &&
            decimal.TryParse(prize[1..].Replace(",", ""), out var amt) && amt >= 5_000)
            return true;
        return false;
    }

    // ── Display ───────────────────────────────────────────────────────────────

    void DisplayResults()
    {
        resultsStack.Children.Clear();
        var game   = Game;
        var accent = Color.FromArgb(game.AccentColor);

        for (int i = 0; i < _generated.Count; i++)
        {
            var row = _generated[i];

            string prize = CheckWin(row);
            bool isWin = !string.IsNullOrEmpty(prize);

            var rowGrid = new Grid
            {
                BackgroundColor = isWin
                    ? Color.FromArgb("#1A3A1A")
                    : Color.FromArgb("#1A2333"),
                Padding = new Thickness(8, 6),
                Margin  = new Thickness(0, 2),
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(26)), // row #
                    new ColumnDefinition(GridLength.Star),    // bubbles
                    new ColumnDefinition(new GridLength(48)), // prize badge
                    new ColumnDefinition(new GridLength(30)), // edit
                    new ColumnDefinition(new GridLength(30)), // delete
                }
            };

            rowGrid.Children.Add(new Label
            {
                Text = $"{i + 1}.",
                FontSize = 12, FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#6B7280"),
                VerticalOptions = LayoutOptions.Center
            });

            var bubbleRow = new HorizontalStackLayout { Spacing = 5, VerticalOptions = LayoutOptions.Center };

            // Main number bubbles
            foreach (int n in row.Main)
                bubbleRow.Children.Add(MakeBubble(n.ToString(), Colors.White, accent));

            // Special ball
            if (row.Special > 0)
            {
                bubbleRow.Children.Add(new BoxView
                {
                    BackgroundColor = Color.FromArgb("#C62828"),
                    WidthRequest = 2, VerticalOptions = LayoutOptions.Fill,
                    Margin = new Thickness(3, 4)
                });
                var specColor = string.IsNullOrEmpty(game.SpecialColor)
                    ? Color.FromArgb("#C62828")
                    : Color.FromArgb(game.SpecialColor);
                bubbleRow.Children.Add(MakeBubble(row.Special.ToString(), Colors.White, specColor));
            }

            // Race time
            if (!string.IsNullOrEmpty(row.Time))
            {
                bubbleRow.Children.Add(new Label
                {
                    Text = $"⏱[{row.Time}]",
                    FontSize = 13, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#FFCCBC"),
                    VerticalOptions = LayoutOptions.Center,
                    Margin = new Thickness(6, 0, 0, 0)
                });
            }

            var bubbleContainer = new ContentView { Content = bubbleRow };
            Grid.SetColumn(bubbleContainer, 1);
            rowGrid.Children.Add(bubbleContainer);

            // Prize badge (only shown if actual win)
            var matchLbl = new Label
            {
                Text = prize,
                FontSize = 10, FontAttributes = FontAttributes.Bold,
                TextColor = IsBigPrize(prize)
                    ? Color.FromArgb("#FFD700")
                    : Color.FromArgb("#AAFFAA"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalOptions = LayoutOptions.Center,
            };
            Grid.SetColumn(matchLbl, 2);
            rowGrid.Children.Add(matchLbl);

            // Edit button
            int capturedIdx = i;
            var editBtn = new Button
            {
                Text = "✎",
                FontSize = 14,
                BackgroundColor = Color.FromArgb("#1565C0"),
                TextColor = Colors.White,
                WidthRequest = 26, HeightRequest = 26,
                CornerRadius = 13, Padding = Thickness.Zero,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center
            };
            editBtn.Clicked += (_, _) => ShowEditRow(capturedIdx);
            Grid.SetColumn(editBtn, 3);
            rowGrid.Children.Add(editBtn);

            // Delete button
            var delBtn = new Button
            {
                Text = "✕",
                FontSize = 12,
                BackgroundColor = Color.FromArgb("#7B1111"),
                TextColor = Colors.White,
                WidthRequest = 26, HeightRequest = 26,
                CornerRadius = 13, Padding = Thickness.Zero,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center
            };
            delBtn.Clicked += (_, _) =>
            {
                _generated.RemoveAt(capturedIdx);
                DisplayResults();
            };
            Grid.SetColumn(delBtn, 4);
            rowGrid.Children.Add(delBtn);

            resultsStack.Children.Add(rowGrid);
        }

        // Latest draw banner
        if (_latestDraw != null)
        {
            string winNums = string.Join("  ", _latestDraw.Main.Select(n => n.ToString("D2")));
            string winSpecial = _latestDraw.Special > 0 ? $"  +{_latestDraw.Special:D2}" : "";
            string winTime = !string.IsNullOrEmpty(_latestDraw.Time) ? $"  ⏱[{_latestDraw.Time}]" : "";
            resultsStack.Children.Insert(0, new Label
            {
                Text = $"Latest draw ({_latestDraw.Label}): {winNums}{winSpecial}{winTime}",
                FontSize = 12, FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                BackgroundColor = Color.FromArgb("#0D3B66"),
                Padding = new Thickness(10, 6),
                LineBreakMode = LineBreakMode.WordWrap
            });
        }

        lblStatus.Text = $"{_generated.Count} row{(_generated.Count == 1 ? "" : "s")} generated for {game.Name}";
        saveBar.IsVisible = _generated.Count > 0;
    }

    static View MakeBubble(string text, Color textColor, Color bgColor)
    {
        return new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 5 },
            StrokeThickness = 0,
            BackgroundColor = bgColor,
            WidthRequest = 36, HeightRequest = 34,
            Content = new Label
            {
                Text = text,
                FontSize = 13, FontAttributes = FontAttributes.Bold,
                TextColor = textColor,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center
            }
        };
    }

    void BtnClearAll_Clicked(object sender, EventArgs e) => ClearResults();

    void ClearResults()
    {
        _generated.Clear();
        resultsStack.Children.Clear();
        saveBar.IsVisible = false;
        lblStatus.Text = "";
    }

    // ── Set picker ────────────────────────────────────────────────────────────

    void RefreshSetPicker()
    {
        pickerSet.Items.Clear();
        var game = Game;
        int stride = game.SetStride;

        for (int i = 0; i < 10; i++)
        {
            string raw     = Preferences.Get($"{game.SetPrefix}{i}", "");
            bool   isEmpty = string.IsNullOrEmpty(raw) ||
                             raw.Split('|').All(v => string.IsNullOrWhiteSpace(v));
            int usedRows = 0;
            if (!isEmpty)
            {
                var seg = raw.Split('|');
                for (int r = 0; r < 10; r++)
                    if (Enumerable.Range(0, game.MainPick).Any(c =>
                    {
                        int idx = r * stride + c;
                        return idx < seg.Length && !string.IsNullOrWhiteSpace(seg[idx]);
                    }))
                        usedRows++;
            }
            pickerSet.Items.Add(isEmpty
                ? $"Set {i + 1}  ✓ empty"
                : $"Set {i + 1}  ({usedRows}/10 rows used)");
        }

        // Default to first empty slot
        for (int i = 0; i < 10; i++)
        {
            string raw = Preferences.Get($"{game.SetPrefix}{i}", "");
            if (string.IsNullOrEmpty(raw) || raw.Split('|').All(v => string.IsNullOrWhiteSpace(v)))
            { pickerSet.SelectedIndex = i; break; }
        }
        if (pickerSet.SelectedIndex < 0) pickerSet.SelectedIndex = 0;
    }

    // ── Save generated rows to set ────────────────────────────────────────────

    async void BtnSave_Clicked(object sender, EventArgs e)
    {
        if (_generated.Count == 0) return;
        if (pickerSet.SelectedIndex < 0)
        {
            await DisplayAlert("Choose a Set", "Select a destination set first.", "OK");
            return;
        }

        var game   = Game;
        int slot   = pickerSet.SelectedIndex;
        int stride = game.SetStride;
        string key = $"{game.SetPrefix}{slot}";

        string existing = Preferences.Get(key, "");
        string[] vals;
        if (string.IsNullOrEmpty(existing))
        { vals = new string[10 * stride]; Array.Fill(vals, ""); }
        else
        {
            vals = existing.Split('|');
            if (vals.Length < 10 * stride) Array.Resize(ref vals, 10 * stride);
        }

        // Find first empty row
        int startRow = -1;
        for (int r = 0; r < 10; r++)
        {
            bool rowEmpty = Enumerable.Range(0, game.MainPick)
                .All(c => string.IsNullOrWhiteSpace(vals[r * stride + c]));
            if (rowEmpty) { startRow = r; break; }
        }

        int canFit = startRow < 0 ? 0 : 10 - startRow;
        if (canFit <= 0)
        {
            await DisplayAlert("Set Full", $"Set {slot + 1} is full. Choose a different set.", "OK");
            return;
        }

        int toWrite = Math.Min(_generated.Count, canFit);
        string preview = string.Join("\n", _generated.Take(toWrite).Select((row, i) =>
        {
            string nums = string.Join(" ", row.Main.Select(n => n.ToString("D2")));
            string sp   = row.Special > 0 ? $" +{row.Special:D2}" : "";
            string t    = !string.IsNullOrEmpty(row.Time) ? $" [{row.Time}]" : "";
            return $"  Row {startRow + i + 1}: {nums}{sp}{t}";
        }));

        bool confirm = await DisplayAlert($"Save to {game.Name}?",
            $"{preview}\n\n→ Set {slot + 1}, starting at row {startRow + 1}",
            "Save", "Cancel");
        if (!confirm) return;

        for (int i = 0; i < toWrite; i++)
        {
            var row = _generated[i];
            int r   = startRow + i;
            for (int c = 0; c < game.MainPick; c++)
                vals[r * stride + c] = row.Main[c].ToString();
            if (game.SpecialMax > 0 && row.Special > 0)
                vals[r * stride + game.MainPick] = row.Special.ToString();
            if (game.HasTime)
                vals[r * stride + 3] = row.Time;
        }

        Preferences.Set(key, string.Join("|", vals));

        lblStatus.Text = $"Saved {toWrite} row(s) to {game.Name} Set {slot + 1}";
        RefreshSetPicker();

        if (toWrite < _generated.Count)
            lblStatus.Text += $"  ({_generated.Count - toWrite} skipped — set full)";
    }

    // ── Edit row overlay ──────────────────────────────────────────────────────

    void ShowEditRow(int idx)
    {
        _editRowIdx = idx;
        var row  = _generated[idx];
        var game = Game;

        editEntryRow.Children.Clear();
        _editRowEntries.Clear();
        _editRowTimeEntry = null;

        int mainCount = game.Short == "DD" ? 3 : game.MainPick;
        for (int c = 0; c < mainCount; c++)
        {
            var entry = new Entry
            {
                Text = row.Main[c].ToString(),
                Keyboard = Keyboard.Numeric,
                MaxLength = 2,
                WidthRequest = 52, HeightRequest = 44,
                FontSize = 18, FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                BackgroundColor = Color.FromArgb("#2D3D50"),
                TextColor = Colors.White,
            };
            AttachMaxClamp(entry, game.MainMax);
            entry.Focused += SelectAllOnFocus;
            _editRowEntries.Add(entry);
            editEntryRow.Children.Add(entry);
        }

        if (game.SpecialMax > 0)
        {
            editEntryRow.Children.Add(new BoxView
            {
                BackgroundColor = Color.FromArgb("#C62828"),
                WidthRequest = 2, VerticalOptions = LayoutOptions.Fill,
                Margin = new Thickness(4, 6)
            });
            var spEntry = new Entry
            {
                Text = row.Special.ToString(),
                Keyboard = Keyboard.Numeric, MaxLength = 2,
                WidthRequest = 52, HeightRequest = 44,
                FontSize = 18, FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                BackgroundColor = Color.FromArgb("#3B0000"), TextColor = Colors.White,
            };
            AttachMaxClamp(spEntry, game.SpecialMax);
            spEntry.Focused += SelectAllOnFocus;
            _editRowEntries.Add(spEntry);
            editEntryRow.Children.Add(spEntry);
        }

        if (game.HasTime)
        {
            _editRowTimeEntry = new Entry
            {
                Text = row.Time,
                Keyboard = Keyboard.Numeric, MaxLength = 3,
                WidthRequest = 60, HeightRequest = 44,
                FontSize = 18, FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                BackgroundColor = Color.FromArgb("#3E1F00"), TextColor = Colors.White,
                Placeholder = "000",
            };
            _editRowTimeEntry.Focused += SelectAllOnFocus;
            editEntryRow.Children.Add(_editRowTimeEntry);
        }

        editRowTitle.Text = $"Edit Row {idx + 1}";
        editOverlay.IsVisible = true;
    }

    static void AttachMaxClamp(Entry entry, int max)
    {
        entry.TextChanged += (s, e) =>
        {
            if (int.TryParse(e.NewTextValue, out int v) && v > max)
                ((Entry)s!).Text = e.OldTextValue ?? "";
        };
    }

    static void SelectAllOnFocus(object? sender, FocusEventArgs e)
    {
        if (sender is not Entry en) return;
        // Delay so Android keyboard is up before we set selection
        Task.Delay(150).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            en.CursorPosition = 0;
            en.SelectionLength = en.Text?.Length ?? 0;
        }));
    }

    void BtnEditRowCancel_Clicked(object sender, EventArgs e)
        => editOverlay.IsVisible = false;

    void BtnEditRowApply_Clicked(object sender, EventArgs e)
    {
        if (_editRowIdx < 0 || _editRowIdx >= _generated.Count) return;
        var game = Game;
        int mainCount = game.Short == "DD" ? 3 : game.MainPick;
        var main = new int[mainCount];
        for (int c = 0; c < mainCount && c < _editRowEntries.Count; c++)
            int.TryParse(_editRowEntries[c].Text, out main[c]);
        int special = 0;
        if (game.SpecialMax > 0 && _editRowEntries.Count > mainCount)
            int.TryParse(_editRowEntries[mainCount].Text, out special);
        string time = _editRowTimeEntry?.Text?.Trim() ?? "";
        _generated[_editRowIdx] = new GeneratedRow(main, special, time);
        editOverlay.IsVisible = false;
        DisplayResults();
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    async void BtnBack_Clicked(object sender, EventArgs e)
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        Shell.Current.CurrentPage.TranslationX = -w;
        await Shell.Current.GoToAsync("..", false);
    }

    protected override bool OnBackButtonPressed()
    {
        _ = BtnBack_ClickedAsync();
        return true;
    }

    async Task BtnBack_ClickedAsync()
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        Shell.Current.CurrentPage.TranslationX = -w;
        await Shell.Current.GoToAsync("..", false);
    }

    internal void PrePosition(bool fromRight)
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        TranslationX = fromRight ? w : -w;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = this.TranslateTo(0, 0, 220, Easing.CubicOut);
        if (_gameBtns.Length == 0) BuildGameButtons();
        RefreshSetPicker();
    }
}
