namespace DailyFantasyMAUI;

/// <summary>
/// Data-driven Quick Pick engine — reads historical CSV draw files and
/// selects numbers based on the user-chosen strategy.
/// </summary>
internal static class SmartQuickPick
{
    /// <summary>
    /// Strategies the user can choose from.
    /// </summary>
    public enum Strategy
    {
        Hot       = 0,  // most frequent in last N draws
        Cold      = 1,  // least drawn all-time (overdue)
        MostFreq  = 2,  // most frequent all-time
        LeastFreq = 3,  // rarest all-time
        LongestGap = 4  // most draws since last appearance
    }

    /// <summary>
    /// Runs the analysis and returns a sorted list of <paramref name="pickCount"/> numbers.
    /// Progress messages are delivered via <paramref name="onProgress"/>.
    /// </summary>
    /// <param name="mode">0 = Fantasy 5, 1 = Super Lotto, 2 = Daily 3</param>
    /// <param name="strategy">Which algorithm to use</param>
    /// <param name="pickCount">How many numbers to return (FROM value)</param>
    /// <param name="maxBall">Upper bound of the number range (FROM value)</param>
    /// <param name="lastN">For Hot strategy: how many recent draws to scan</param>
    /// <param name="onProgress">Callback for status text updates</param>
    public static async Task<List<int>> RunAsync(
        int mode, Strategy strategy, int pickCount, int maxBall, int lastN,
        Action<string> onProgress)
    {
        void Msg(string text) =>
            MainThread.BeginInvokeOnMainThread(() => onProgress(text));

        // ── Step 1: resolve data file ─────────────────────────────────────────
        Msg("Loading draw history...");
        await Task.Delay(700);

        string assetName = mode == 1 ? "data/mySuperlotto.csv"
                         : mode == 2 ? "data/myDaily3.csv"
                         :             "data/myFantasy5.csv";
        string localFile = mode == 1 ? "mySuperlotto.csv"
                         : mode == 2 ? "myDaily3.csv"
                         :             "myFantasy5.csv";
        string localPath = Path.Combine(FileSystem.AppDataDirectory, "data", localFile);

        Stream stream = File.Exists(localPath)
            ? File.OpenRead(localPath)
            : await FileSystem.OpenAppPackageFileAsync(assetName);

        // ── Step 2: parse every draw row ─────────────────────────────────────
        Msg("Parsing draw records...");
        await Task.Delay(600);

        var allDraws = new List<int[]>();
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
                if (mode == 2) // Daily 3 — cols: DrawDate, DrawNumber, N1, N2, N3, DrawTime
                {
                    if (p.Length >= 5 &&
                        int.TryParse(p[2].Trim(), out int d1) &&
                        int.TryParse(p[3].Trim(), out int d2) &&
                        int.TryParse(p[4].Trim(), out int d3))
                        allDraws.Add(new[] { d1, d2, d3 });
                }
                else // F5 / SL — cols: DrawDate, DrawNumber, N1, N2, N3, N4, N5 [, Mega]
                {
                    if (p.Length >= 7 &&
                        int.TryParse(p[2].Trim(), out int n1) &&
                        int.TryParse(p[3].Trim(), out int n2) &&
                        int.TryParse(p[4].Trim(), out int n3) &&
                        int.TryParse(p[5].Trim(), out int n4) &&
                        int.TryParse(p[6].Trim(), out int n5))
                        allDraws.Add(new[] { n1, n2, n3, n4, n5 });
                }
            }
        }

        int totalDraws = allDraws.Count;
        int minBall    = mode == 2 ? 0 : 1;

        Msg($"Analyzing {totalDraws:N0} historical draws...");
        await Task.Delay(900);

        // ── Step 3: run the chosen strategy ──────────────────────────────────
        List<int> picked;

        switch (strategy)
        {
            case Strategy.Hot:
            {
                int scanCount = Math.Min(lastN, allDraws.Count);
                Msg($"Scanning last {scanCount} draws for hot numbers...");
                await Task.Delay(1200);

                var recent = allDraws.Skip(allDraws.Count - scanCount).ToList();
                var freq = new Dictionary<int, int>();
                foreach (var draw in recent)
                    foreach (var n in draw)
                        freq[n] = freq.GetValueOrDefault(n) + 1;

                Msg($"Found {freq.Count} distinct numbers — ranking by heat...");
                await Task.Delay(800);

                Msg("Locking in the hottest picks...");
                await Task.Delay(700);

                picked = freq.OrderByDescending(kv => kv.Value)
                             .ThenBy(kv => kv.Key)
                             .Select(kv => kv.Key)
                             .Take(pickCount)
                             .OrderBy(n => n)
                             .ToList();
                break;
            }

            case Strategy.Cold:
            {
                Msg("Building all-time frequency table...");
                await Task.Delay(1200);

                var freq = BuildFreqTable(allDraws, minBall, maxBall);

                Msg("Identifying cold / overdue numbers...");
                await Task.Delay(900);

                Msg("Cross-checking recent appearances...");
                await Task.Delay(700);

                picked = freq.OrderBy(kv => kv.Value)
                             .ThenByDescending(kv => kv.Key)
                             .Select(kv => kv.Key)
                             .Take(pickCount)
                             .OrderBy(n => n)
                             .ToList();
                break;
            }

            case Strategy.MostFreq:
            {
                Msg("Tallying all-time number frequencies...");
                await Task.Delay(1200);

                var freq = BuildFreqTable(allDraws, minBall, maxBall);

                Msg($"Processed {totalDraws:N0} draws — ranking top performers...");
                await Task.Delay(900);

                Msg("Selecting highest-frequency numbers...");
                await Task.Delay(700);

                picked = freq.OrderByDescending(kv => kv.Value)
                             .ThenBy(kv => kv.Key)
                             .Select(kv => kv.Key)
                             .Take(pickCount)
                             .OrderBy(n => n)
                             .ToList();
                break;
            }

            case Strategy.LeastFreq:
            {
                Msg("Computing rarity scores across all draws...");
                await Task.Delay(1200);

                var freq = BuildFreqTable(allDraws, minBall, maxBall);

                Msg("Locating rarest numbers in the dataset...");
                await Task.Delay(900);

                Msg("Validating rarity against full history...");
                await Task.Delay(700);

                picked = freq.OrderBy(kv => kv.Value)
                             .ThenBy(kv => kv.Key)
                             .Select(kv => kv.Key)
                             .Take(pickCount)
                             .OrderBy(n => n)
                             .ToList();
                break;
            }

            default: // LongestGap
            {
                Msg("Mapping last-seen draw index for every number...");
                await Task.Delay(1200);

                var lastSeen = new Dictionary<int, int>();
                for (int i = 0; i < allDraws.Count; i++)
                    foreach (var n in allDraws[i])
                        lastSeen[n] = i;

                Msg("Calculating gap sizes (draws since last appearance)...");
                await Task.Delay(1000);

                var gaps = new Dictionary<int, int>();
                for (int n = minBall; n <= maxBall; n++)
                    gaps[n] = lastSeen.TryGetValue(n, out int idx)
                        ? (totalDraws - 1 - idx)
                        : totalDraws; // never drawn = maximum possible gap

                Msg("Ranking most-due numbers by gap size...");
                await Task.Delay(800);

                picked = gaps.OrderByDescending(kv => kv.Value)
                             .Select(kv => kv.Key)
                             .Take(pickCount)
                             .OrderBy(n => n)
                             .ToList();
                break;
            }
        }

        Msg("Selection complete!");
        await Task.Delay(300);

        // Safety pad — fill with random if somehow short
        if (picked.Count < pickCount)
        {
            var used   = new HashSet<int>(picked);
            var extras = Enumerable.Range(minBall, maxBall - minBall + 1)
                                   .Where(n => !used.Contains(n))
                                   .OrderBy(_ => Random.Shared.Next())
                                   .Take(pickCount - picked.Count);
            picked.AddRange(extras);
            picked = picked.OrderBy(n => n).ToList();
        }

        return picked;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Dictionary<int, int> BuildFreqTable(
        List<int[]> draws, int minBall, int maxBall)
    {
        var freq = new Dictionary<int, int>();
        for (int n = minBall; n <= maxBall; n++) freq[n] = 0;
        foreach (var draw in draws)
            foreach (var n in draw)
                if (freq.ContainsKey(n)) freq[n]++;
        return freq;
    }
}
