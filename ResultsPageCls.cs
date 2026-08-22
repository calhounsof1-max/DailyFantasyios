using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public class WinnerEntry
{
    public string Game         { get; set; } = "";
    public int    SetNumber    { get; set; }
    public int    RowNumber    { get; set; }
    public string Numbers      { get; set; } = "";
    public string MatchLabel   { get; set; } = "";
    public string Prize        { get; set; } = "";
    // True when the ticket is still within its advance-play date range but did not win today
    public bool   IsActiveNoWin { get; set; } = false;
    // The actual draw date this entry is for (populated for past-advance wins so UI can strikethrough correctly)
    public string DrawDate      { get; set; } = "";
    // The advance "play from" start date (M/d format), shown as range "6/30-7/11"
    public string PlayFromDate  { get; set; } = "";
    // The advance "play to" end date (M/d format), shown instead of "In play" when available
    public string PlayToDate    { get; set; } = "";
    // The effective end draw number from the advance-play entry (0 if not set)
    public int    DrawEnd       { get; set; }
    // The actual draw# that produced this win (0 if not a win / unknown)
    public int    MatchDrawNum  { get; set; }
    // The bet type for D3/D4 rows ("S", "B", "S&B")
    public string BetType       { get; set; } = "";
    // The per-row draw-target filter for D3 rows ("B"=both, "M"=midday, "E"=evening)
    public string DrawFilter    { get; set; } = "";
}

public class DateResultData
{
    public DateTime Date     { get; set; }
    public string DateLabel  { get; set; } = "";

    public int[]  F5Numbers  { get; set; } = Array.Empty<int>();
    public int[]  SLMain     { get; set; } = Array.Empty<int>();
    public int    SLMega     { get; set; }
    public int[]  PBMain     { get; set; } = Array.Empty<int>();
    public int    PBBall     { get; set; }
    public int[]  MMMain     { get; set; } = Array.Empty<int>();
    public int    MMBall     { get; set; }
    public int[]? D3Midday   { get; set; }
    public int[]? D3Evening  { get; set; }
    public int[]? D4Numbers  { get; set; }
    public int[]? DDHorses   { get; set; }
    public string DDRaceTime { get; set; } = "";

    public int    F5DrawNumber     { get; set; }
    public int    SLDrawNumber     { get; set; }
    public int    PBDrawNumber     { get; set; }
    public int    MMDrawNumber     { get; set; }
    public int    D4DrawNumber     { get; set; }
    public int    DDDrawNumber     { get; set; }
    public int    D3MiddayDrawNum   { get; set; }
    public int    D3EveningDrawNum  { get; set; }
    public string D3MiddayDateLabel  { get; set; } = "";
    public string D3EveningDateLabel { get; set; } = "";

    public string F5DrawDate  { get; set; } = "";
    public string SLDrawDate  { get; set; } = "";
    public string PBDrawDate  { get; set; } = "";
    public string MMDrawDate  { get; set; } = "";
    public string D4DrawDate  { get; set; } = "";
    public string DDDrawDate  { get; set; } = "";

    public List<WinnerEntry> Winners { get; set; } = new();
    public string Error { get; set; } = "";

    // A win is only "current" if its own ticket hasn't expired yet — same rule the
    // Results page uses to decide what to display (ResultsPage.xaml.cs.IsRowStillVisible,
    // added 2026-07-30). Anything that fails this is a stale win from a ticket that's
    // already run its course; it must not count toward "today"'s alert total either.
    public bool IsWinnerCurrent(WinnerEntry w)
    {
        if (w.Game == "D3")
        {
            // Draw-number check alone isn't enough: before today's own draws post (e.g. 6am,
            // hours before the 1pm midday draw), a win from yesterday is never "superseded" by
            // a newer draw and would read as current indefinitely. Pass real dates so the
            // built-in 6am cutoff applies — matches the standing "nothing from a prior day
            // shows past 6am" rule and the dedicated D3RetireReceiver that fires at 6am.
            DateTime.TryParse(w.DrawDate, out var ticketDrawDate);
            return D3TimingRules.IsStillVisible(D3MiddayDrawNum, D3EveningDrawNum, w.DrawEnd,
                ticketDrawDate == default ? null : ticketDrawDate, DateTime.Today, DateTime.Now.TimeOfDay);
        }

        // A WIN is only current if it happened today — its own DrawDate, not whether the
        // advance ticket it came from is still active for future draws. PlayToDate/DrawEnd
        // answer "is this ticket still in play," a different question from "did this specific
        // win happen today." Conflating them let an 8/1 win from a still-running 8/3 ticket
        // keep showing on 8/2 — wrong per explicit instruction: only today's wins show.
        return string.IsNullOrEmpty(w.DrawDate) ||
               !DateTime.TryParse(w.DrawDate, out var wd) ||
               wd.Date >= DateTime.Today;
    }
}

public static class ResultsPageCls
{
    const int Rows = 10;

    static List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)>                          _f5 = new();
    static List<(string DrawDate, int DrawNumber, int[] MainNumbers, int MegaNumber, DrawPrizeTier[] Prizes)>      _sl = new();
    static List<(string DrawDate, int DrawNumber, int[] MainNumbers, int PBNumber,   DrawPrizeTier[] Prizes)>      _pb = new();
    static List<(string DrawDate, int DrawNumber, int[] MainNumbers, int MegaNumber, DrawPrizeTier[] Prizes)>      _mm = new();
    static List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)>                          _d3 = new();
    static List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)>                          _d4 = new();
    static List<(string DrawDate, int DrawNumber, int[] Horses, string RaceTime, DrawPrizeTier[] Prizes)>          _dd = new();
    static bool _loaded = false;
    static DateTime _lastLiveFetch = DateTime.MinValue;
    const int LiveFetchThrottleSeconds = 90;

    public static void ClearCache()
    {
        _f5.Clear(); _sl.Clear(); _pb.Clear(); _mm.Clear(); _d3.Clear(); _d4.Clear(); _dd.Clear();
        _loaded = false;
    }

    public static async Task LoadAllDrawsAsync()
    {
        // Pull fresh results from calottery.com into the local CSVs first — the Load*CsvDraws
        // calls below only ever read whatever's already on disk, so without this the background
        // win-check (WinCheckReceiver / "Run Win Check Now") can never see a draw that posted
        // since the last time a game page or MainPage happened to be opened in the foreground.
        // Best-effort: a network failure here just means today's Load falls back to the last
        // known CSV contents, same as before this fix existed.
        //
        // Throttled to once per LiveFetchThrottleSeconds regardless of caller — the Results
        // page's 15-second auto-refresh calls ClearCache()+this every tick, and without a
        // throttle that meant 6 sequential live HTTP fetches every 15 seconds just from having
        // the page open, which made the page feel persistently slow. New draws only post once
        // or twice a day, so a live check every 90s is still effectively immediate.
        if ((DateTime.Now - _lastLiveFetch).TotalSeconds >= LiveFetchThrottleSeconds)
        {
            try
            {
                await GetDataEntry.UpdateF5CsvAsync();
                await GetDataEntry.UpdateSLCsvAsync();
                await GetDataEntry.UpdateD3CsvAsync();
                await GetDataEntry.UpdateMMCsvAsync();
                await GetDataEntry.UpdatePBCsvAsync();
                await GetDataEntry.UpdateD4CsvAsync();
            }
            catch { }
            _lastLiveFetch = DateTime.Now;
        }

        var f5Task = GetDataEntry.LoadF5CsvDraws();
        var slTask = GetDataEntry.LoadSLCsvDraws();
        var pbTask = GetDataEntry.LoadPBCsvDraws();
        var mmTask = GetDataEntry.LoadMMCsvDraws();
        var d3Task = GetDataEntry.LoadD3CsvDraws();
        var d4Task = GetDataEntry.LoadD4CsvDraws();
        var ddTask = GetDataEntry.GetDailyDerbyDraws(50);
        await Task.WhenAll(f5Task, slTask, pbTask, mmTask, d3Task, d4Task, ddTask);
        _f5 = f5Task.Result;
        _sl = slTask.Result.Select(d => (d.DrawDate, d.DrawNumber, d.MainNumbers, d.Mega, d.Prizes)).ToList();
        _pb = pbTask.Result;
        _mm = mmTask.Result.Select(d => (d.DrawDate, d.DrawNumber, d.MainNumbers, d.Mega, d.Prizes)).ToList();
        _d3 = d3Task.Result.Select(d => (d.DrawDate, d.DrawNumber, d.Numbers, Array.Empty<DrawPrizeTier>())).ToList();
        _d4 = d4Task.Result;
        _dd = ddTask.Result;

        // The CSV history above never carries real prize amounts (numbers only).
        // Pull the live per-draw payout from calottery.com and splice it in for
        // the recent draws — retrying if the site hasn't posted it yet — rather
        // than ever displaying a guessed number.
        await MergeLivePrizesAsync();

        // Only mark loaded once the real prize data has actually been merged in —
        // MainPage kicks this off in the background (fire-and-forget), so if we
        // flip this flag early, the Results page can race ahead and read the
        // pre-merge (all "n/a") data before the live fetch finishes.
        _loaded = true;
    }

    // Keeps checking calottery.com until the actual payout for the most recent
    // draw of each game comes back, up to a bounded number of attempts.
    // Note: CA Lottery doesn't post evening-draw prize amounts until after
    // 7pm for Daily 3, Fantasy 5, Daily 4, and Daily Derby, or until after
    // 8pm for SuperLotto, Powerball, and Mega Millions — "n/a" before then
    // is expected (the site itself doesn't have it yet), not a bug.
    static async Task MergeLivePrizesAsync()
    {
        const int maxAttempts = 4;
        const int retryDelayMs = 2000;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var f5Task = GetDataEntry.GetPastDraws(5);
            var slTask = GetDataEntry.GetSuperLottoDraws(5);
            var pbTask = GetDataEntry.GetPowerballDraws(5);
            var mmTask = GetDataEntry.GetMegaMillionsDraws(5);
            var d3Task = GetDataEntry.GetDaily3Draws(5);
            var d4Task = GetDataEntry.GetDaily4Draws(5);
            await Task.WhenAll(f5Task, slTask, pbTask, mmTask, d3Task, d4Task);

            bool f5Ok = MergeF5(f5Task.Result);
            bool slOk = MergeSL(slTask.Result);
            bool pbOk = MergePB(pbTask.Result);
            bool mmOk = MergeMM(mmTask.Result);
            bool d3Ok = MergeD3(d3Task.Result);
            bool d4Ok = MergeD4(d4Task.Result);

            if (f5Ok && slOk && pbOk && mmOk && d3Ok && d4Ok) return;
            if (attempt < maxAttempts) await Task.Delay(retryDelayMs);
        }
    }

    // Each Merge* splices live-fetched Prizes into the matching (by DrawDate)
    // local entry, and reports whether the newest draw actually got real prize
    // data — an empty live result (offline) also counts as "done", so we don't
    // burn every retry against a dead network.
    static bool MergeF5(List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)> live)
    {
        foreach (var ld in live)
        {
            if (ld.Prizes.Length == 0) continue;
            int idx = _f5.FindIndex(x => x.DrawDate == ld.DrawDate);
            if (idx >= 0) _f5[idx] = (_f5[idx].DrawDate, _f5[idx].DrawNumber, _f5[idx].Numbers, ld.Prizes);
        }
        return live.Count == 0 || live[0].Prizes.Length > 0;
    }

    static bool MergeSL(List<(string DrawDate, int DrawNumber, int[] MainNumbers, int MegaNumber, DrawPrizeTier[] Prizes)> live)
    {
        foreach (var ld in live)
        {
            if (ld.Prizes.Length == 0) continue;
            int idx = _sl.FindIndex(x => x.DrawDate == ld.DrawDate);
            if (idx >= 0) _sl[idx] = (_sl[idx].DrawDate, _sl[idx].DrawNumber, _sl[idx].MainNumbers, _sl[idx].MegaNumber, ld.Prizes);
        }
        return live.Count == 0 || live[0].Prizes.Length > 0;
    }

    static bool MergePB(List<(string DrawDate, int DrawNumber, int[] MainNumbers, int PBNumber, DrawPrizeTier[] Prizes)> live)
    {
        foreach (var ld in live)
        {
            if (ld.Prizes.Length == 0) continue;
            int idx = _pb.FindIndex(x => x.DrawDate == ld.DrawDate);
            if (idx >= 0) _pb[idx] = (_pb[idx].DrawDate, _pb[idx].DrawNumber, _pb[idx].MainNumbers, _pb[idx].PBNumber, ld.Prizes);
        }
        return live.Count == 0 || live[0].Prizes.Length > 0;
    }

    static bool MergeMM(List<(string DrawDate, int DrawNumber, int[] MainNumbers, int MegaNumber, DrawPrizeTier[] Prizes)> live)
    {
        foreach (var ld in live)
        {
            if (ld.Prizes.Length == 0) continue;
            int idx = _mm.FindIndex(x => x.DrawDate == ld.DrawDate);
            if (idx >= 0) _mm[idx] = (_mm[idx].DrawDate, _mm[idx].DrawNumber, _mm[idx].MainNumbers, _mm[idx].MegaNumber, ld.Prizes);
        }
        return live.Count == 0 || live[0].Prizes.Length > 0;
    }

    static bool MergeD3(List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)> live)
    {
        // D3 draws twice a day (midday + evening) — DrawDate alone is ambiguous
        // between the two, so match on the unique DrawNumber instead.
        foreach (var ld in live)
        {
            if (ld.Prizes.Length == 0) continue;
            int idx = ld.DrawNumber != 0 ? _d3.FindIndex(x => x.DrawNumber == ld.DrawNumber) : -1;
            if (idx < 0) idx = _d3.FindIndex(x => x.DrawDate == ld.DrawDate && x.Numbers.SequenceEqual(ld.Numbers));
            if (idx >= 0) _d3[idx] = (_d3[idx].DrawDate, _d3[idx].DrawNumber, _d3[idx].Numbers, ld.Prizes);
        }
        return live.Count == 0 || live[0].Prizes.Length > 0;
    }

    static bool MergeD4(List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)> live)
    {
        foreach (var ld in live)
        {
            if (ld.Prizes.Length == 0) continue;
            int idx = ld.DrawNumber != 0 ? _d4.FindIndex(x => x.DrawNumber == ld.DrawNumber) : -1;
            if (idx < 0) idx = _d4.FindIndex(x => x.DrawDate == ld.DrawDate && x.Numbers.SequenceEqual(ld.Numbers));
            if (idx >= 0) _d4[idx] = (_d4[idx].DrawDate, _d4[idx].DrawNumber, _d4[idx].Numbers, ld.Prizes);
        }
        return live.Count == 0 || live[0].Prizes.Length > 0;
    }

    // ── Prize lookup using ACTUAL CA Lottery data ─────────────────────────────
    //
    // Fantasy 5 (4 tiers, Tier 1 = jackpot):
    //   Tier 1 = 5/5   Tier 2 = 4/5   Tier 3 = 3/5   Tier 4 = 2/5
    //   tier = 6 - matchCount
    //
    // SuperLotto Plus (9 tiers):
    //   (5,M)→1  (5,0)→2  (4,M)→3  (4,0)→4  (3,M)→5
    //   (3,0)→6  (2,M)→7  (1,M)→8  (0,M)→9

    static string F5Prize(int matches, DrawPrizeTier[] prizes)
    {
        int tier = 6 - matches;  // 5→1, 4→2, 3→3, 2→4
        if (tier < 1 || tier > 4) return "";  // 0/1 matches never pay — not a win at all
        if (prizes.Length > 0)
        {
            var p = prizes.FirstOrDefault(x => x.Tier == tier);
            if (p != null)
            {
                if (p.Amount > 0) return FormatPrize(p.Amount);
                // Tier 1 jackpot — data in API shows 0 (pending or rolled)
                if (tier == 1) return "JACKPOT*";
                // Other tiers with 0 amount — rare
                return tier == 4 ? "Free Ticket" : "n/a";
            }
        }
        // No fallback guesses — only the actual fetched amount is ever shown.
        return tier == 1 ? "JACKPOT*" : "n/a";
    }

    static string SLPrize(int main, bool mega, DrawPrizeTier[] prizes)
    {
        int tier = (main, mega) switch
        {
            (5, true)  => 1,
            (5, false) => 2,
            (4, true)  => 3,
            (4, false) => 4,
            (3, true)  => 5,
            (3, false) => 6,
            (2, true)  => 7,
            (1, true)  => 8,
            (0, true)  => 9,
            _          => 0
        };
        if (tier == 0) return "";  // not a paying combo at all — not a win
        if (prizes.Length > 0)
        {
            var p = prizes.FirstOrDefault(x => x.Tier == tier);
            if (p != null)
                return p.Amount > 0 ? FormatPrize(p.Amount) : (tier == 1 ? "JACKPOT" : "Free Ticket");
        }
        // No fallback guesses — only the actual fetched amount is ever shown.
        return tier == 1 ? "JACKPOT*" : "n/a";
    }

    static string PBPrize(int main, bool pb, DrawPrizeTier[] prizes)
    {
        int tier = (main, pb) switch
        {
            (5, true)  => 1,
            (5, false) => 2,
            (4, true)  => 3,
            (4, false) => 4,
            (3, true)  => 5,
            (3, false) => 6,
            (2, true)  => 7,
            (1, true)  => 8,
            (0, true)  => 9,
            _          => 0
        };
        if (tier == 0) return "";  // not a paying combo at all — not a win
        if (prizes.Length > 0)
        {
            var p = prizes.FirstOrDefault(x => x.Tier == tier);
            if (p != null)
                return p.Amount > 0 ? FormatPrize(p.Amount) : (tier == 1 ? "JACKPOT" : "Free Ticket");
        }
        // No fallback guesses — only the actual fetched amount is ever shown.
        return tier == 1 ? "JACKPOT*" : "n/a";
    }

    static string MMPrize(int main, bool mb, DrawPrizeTier[] prizes)
    {
        int tier = (main, mb) switch
        {
            (5, true)  => 1,
            (5, false) => 2,
            (4, true)  => 3,
            (4, false) => 4,
            (3, true)  => 5,
            (3, false) => 6,
            (2, true)  => 7,
            (1, true)  => 8,
            (0, true)  => 9,
            _          => 0
        };
        if (tier == 0) return "";  // not a paying combo at all — not a win
        if (prizes.Length > 0)
        {
            var p = prizes.FirstOrDefault(x => x.Tier == tier);
            if (p != null)
                return p.Amount > 0 ? FormatPrize(p.Amount) : (tier == 1 ? "JACKPOT" : "Free Ticket");
        }
        // No fallback guesses — only the actual fetched amount is ever shown.
        return tier == 1 ? "JACKPOT*" : "n/a";
    }

    // Daily 3 — tiers: 1=Straight, 2=Box, 3=S&B(exact), 4=S&B(box-only)
    static string D3PrizeCalc(string winType, string boxType, DrawPrizeTier[] prizes)
    {
        int tier = winType switch
        {
            "Straight"    => 1,
            "Box3"        => 2,
            "Box6"        => 2,
            "SB_Straight" => 3,
            "SB_Box"      => 4,
            _ => 0
        };
        if (tier > 0 && prizes.Length > 0)
        {
            var p = prizes.FirstOrDefault(x => x.Tier == tier);
            if (p != null && p.Amount > 0) return $"${p.Amount:N0}";
        }
        // No hardcoded guesses — D3 is pari-mutuel and varies by draw.
        // Only ever show the actual amount fetched for this draw.
        return "n/a";
    }

    static string D3BoxType(int[] nums)
    {
        if (nums[0] == nums[1] && nums[1] == nums[2]) return ""; // triple — no box prize
        bool hasPair = nums[0] == nums[1] || nums[1] == nums[2] || nums[0] == nums[2];
        return hasPair ? "Box3" : "Box6";
    }

    // Daily 4 — pari-mutuel; tiers: 1=Straight, 2=Box, 3=S&B(exact), 4=S&B(box-only)
    // Observed Jun 5 2026 (7-2-1-4, 24-way): Straight=$3,376, Box=$134, S&B=$1,755, BoxOnly=$67
    static string D4PrizeCalc(string winType, string betType, DrawPrizeTier[] prizes)
    {
        int tier = winType switch
        {
            "Straight"    => betType == "S&B" ? 0 : 1, // S&B straight handled by SB_Straight
            "Box24" or "Box12" or "Box6" or "Box4" => betType == "S&B" ? 4 : 2,
            "SB_Straight" => 3,
            _ => 0
        };
        if (tier > 0 && prizes.Length > 0)
        {
            var p = prizes.FirstOrDefault(x => x.Tier == tier);
            if (p != null && p.Amount > 0) return $"${p.Amount:N0}";
        }
        // No fallback guesses — D4 is pari-mutuel and varies by draw.
        // Only ever show the actual amount fetched for this draw.
        return "n/a";
    }

    static string[] ReadD4BetTypes(int slot)
    {
        string[] valid = ["S", "B", "S&B"];
        var parts = Preferences.Get($"d4_btypes_{slot}", "").Split('|');
        var result = new string[Rows];
        for (int r = 0; r < Rows; r++)
        {
            var val = r < parts.Length ? parts[r] : "S";
            if (val == "S+B") val = "S&B";
            result[r] = valid.Contains(val) ? val : "S";
        }
        return result;
    }

    static string D4BoxType(int[] nums)
    {
        var freq = nums.GroupBy(x => x).Select(g => g.Count()).OrderByDescending(c => c).ToArray();
        return freq[0] switch
        {
            4 => "",      // four of a kind — straight only, no box
            3 => "Box4",  // three of a kind
            2 => freq.Count(f => f == 2) == 2 ? "Box6" : "Box12",  // two pairs vs one pair
            _ => "Box24"  // all different
        };
    }

    // Daily Derby — pari-mutuel; CA Lottery tiers (confirmed from live data):
    //   1=Grand Prize, 2=Exacta, 4=Race Time
    //   Win prize is fixed ~$3; combos = sum of individual prizes shown as one total
    static decimal DDPrizeAmount(string winType, DrawPrizeTier[] prizes)
    {
        int tier = winType switch
        {
            "GRAND!"   => 1,
            "Exacta"   => 2,
            "Time"     => 4,
            _ => 0
        };
        if (tier > 0 && prizes.Length > 0)
        {
            var p = prizes.FirstOrDefault(x => x.Tier == tier);
            if (p != null && p.Amount > 0) return p.Amount;
        }
        // No hardcoded guesses — only the actual fetched amount is ever shown.
        return 0m;
    }

    static string DDPrize(string winType, DrawPrizeTier[] prizes)
    {
        decimal amt = DDPrizeAmount(winType, prizes);
        return amt > 0 ? $"${amt:N0}" : "n/a";
    }

    // Combo prize = sum of two individual prizes as a single total
    static string DDComboPrize(string w1, string w2, DrawPrizeTier[] prizes)
    {
        decimal amt1 = DDPrizeAmount(w1, prizes);
        decimal amt2 = DDPrizeAmount(w2, prizes);
        // Both legs must have a confirmed actual amount — a partial sum would misrepresent the real payout.
        return amt1 > 0 && amt2 > 0 ? $"${amt1 + amt2:N0}" : "n/a";
    }


    static string NormalizeTime(string t) =>
        new string(t.Where(char.IsDigit).ToArray());

    static string FormatPrize(decimal amount)
    {
        if (amount >= 1_000_000)
            return $"${amount / 1_000_000:F2}M";
        if (amount >= 1_000)
            return $"${amount:N0}";
        return $"${amount:N0}";
    }

    // ── Check whether any sets are saved (and game is not excluded) ──────────

    public static bool IsGameExcluded(string exclKey) =>
        Preferences.Get($"excl_game_{exclKey}", false);

    public static bool HasSets(string prefix) =>
        !IsGameExcluded(prefix) && (
            Enumerable.Range(0, 10).Any(s => !string.IsNullOrEmpty(Preferences.Get($"{prefix}_set_{s}", "")))
            || !string.IsNullOrEmpty(Preferences.Get($"{prefix}_entries", "")));

    public static bool HasDDSets() =>
        !IsGameExcluded("dd") &&
        Enumerable.Range(0, 10).Any(s => !string.IsNullOrEmpty(Preferences.Get($"dd_set_{s}", "")));

    // ── Read non-empty sets from Preferences ─────────────────────────────────

    static List<(int slot, string[][] rows)> ReadSets(string prefix, int cols)
    {
        var result = new List<(int, string[][])>();
        for (int s = 0; s < 10; s++)
        {
            if (Preferences.Get($"excl_set_{prefix}_{s}", false)) continue;
            var data = Preferences.Get($"{prefix}_set_{s}", "");
            if (string.IsNullOrEmpty(data)) continue;

            var vals = data.Split('|');
            var rows = new string[Rows][];
            for (int r = 0; r < Rows; r++)
            {
                rows[r] = new string[cols];
                for (int c = 0; c < cols; c++)
                {
                    int idx = r * cols + c;
                    rows[r][c] = idx < vals.Length ? vals[idx] : "";
                }
            }
            if (rows.Any(row => row.Any(v => !string.IsNullOrWhiteSpace(v))))
                result.Add((s, rows));
        }

        // Fallback: if no slot data found, read live entries directly
        // (happens when user enters tickets but no named slot has been saved yet)
        if (result.Count == 0)
        {
            var data = Preferences.Get($"{prefix}_entries", "");
            if (!string.IsNullOrEmpty(data))
            {
                var vals = data.Split('|');
                var rows = new string[Rows][];
                for (int r = 0; r < Rows; r++)
                {
                    rows[r] = new string[cols];
                    for (int c = 0; c < cols; c++)
                    {
                        int idx = r * cols + c;
                        rows[r][c] = idx < vals.Length ? vals[idx] : "";
                    }
                }
                if (rows.Any(row => row.Any(v => !string.IsNullOrWhiteSpace(v))))
                    result.Add((0, rows));
            }
        }

        return result;
    }

    // ── Read DD sets (4 values per row: h1, h2, h3, raceTime) ────────────────

    static List<(int slot, (int h1, int h2, int h3, string time)[] rows)> ReadDDSets()
    {
        var result = new List<(int, (int, int, int, string)[])>();
        for (int s = 0; s < 10; s++)
        {
            if (Preferences.Get($"excl_set_dd_{s}", false)) continue;
            var data = Preferences.Get($"dd_set_{s}", "");
            if (string.IsNullOrEmpty(data)) continue;

            var vals = data.Split('|');
            var rows = new (int, int, int, string)[Rows];
            bool hasAny = false;
            for (int r = 0; r < Rows; r++)
            {
                int b = r * 4;
                string v0 = b     < vals.Length ? vals[b]     : "";
                string v1 = b + 1 < vals.Length ? vals[b + 1] : "";
                string v2 = b + 2 < vals.Length ? vals[b + 2] : "";
                string v3 = b + 3 < vals.Length ? vals[b + 3] : "";
                int h1 = int.TryParse(v0, out int a) ? a : 0;
                int h2 = int.TryParse(v1, out int b2) ? b2 : 0;
                int h3 = int.TryParse(v2, out int c) ? c : 0;
                rows[r] = (h1, h2, h3, v3);
                if (h1 > 0 || h2 > 0 || h3 > 0 || !string.IsNullOrEmpty(v3)) hasAny = true;
            }
            if (hasAny) result.Add((s, rows));
        }
        return result;
    }

    // ── Main processing ───────────────────────────────────────────────────────

    public static async Task<DateResultData> ProcessDateAsync(DateTime date)
    {
        if (!_loaded)
            await LoadAllDrawsAsync();

        var result = new DateResultData { Date = date };

        if (date.Date > DateTime.Today)
        {
            result.DateLabel = date.ToString("ddd MMM d, yyyy");
            result.Error = "Future date — no draw results available yet";
            return result;
        }

        // ── Find F5 draw on or before selected date ──────────────────────────
        var f5Draw = _f5
            .Select(d => (
                Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                d.DrawDate, d.DrawNumber, d.Numbers, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date)
            .OrderByDescending(d => d.Date)
            .FirstOrDefault();

        if (f5Draw.Numbers != null)
        {
            result.F5Numbers    = f5Draw.Numbers;
            result.F5DrawNumber = f5Draw.DrawNumber;
            result.F5DrawDate   = f5Draw.DrawDate ?? "";
            result.DateLabel    = f5Draw.DrawDate;
        }

        // ── Find SL draw on or before selected date ──────────────────────────
        var slDraw = _sl
            .Select(d => (
                Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                d.DrawDate, d.DrawNumber, d.MainNumbers, d.MegaNumber, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date)
            .OrderByDescending(d => d.Date)
            .FirstOrDefault();

        if (slDraw.MainNumbers != null)
        {
            result.SLMain       = slDraw.MainNumbers;
            result.SLMega       = slDraw.MegaNumber;
            result.SLDrawNumber = slDraw.DrawNumber;
            result.SLDrawDate   = slDraw.DrawDate ?? "";
            if (string.IsNullOrEmpty(result.DateLabel))
                result.DateLabel = slDraw.DrawDate;
        }

        // ── Find PB draw on or before selected date ──────────────────────────
        var pbDraw = _pb
            .Select(d => (
                Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                d.DrawDate, d.DrawNumber, d.MainNumbers, d.PBNumber, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date)
            .OrderByDescending(d => d.Date)
            .FirstOrDefault();

        if (pbDraw.MainNumbers != null)
        {
            result.PBMain       = pbDraw.MainNumbers;
            result.PBBall       = pbDraw.PBNumber;
            result.PBDrawNumber = pbDraw.DrawNumber;
            result.PBDrawDate   = pbDraw.DrawDate ?? "";
            if (string.IsNullOrEmpty(result.DateLabel))
                result.DateLabel = pbDraw.DrawDate;
        }

        // ── Find MM draw on or before selected date ──────────────────────────
        var mmDraw = _mm
            .Select(d => (
                Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                d.DrawDate, d.DrawNumber, d.MainNumbers, d.MegaNumber, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date)
            .OrderByDescending(d => d.Date)
            .FirstOrDefault();

        if (mmDraw.MainNumbers != null)
        {
            result.MMMain       = mmDraw.MainNumbers;
            result.MMBall       = mmDraw.MegaNumber;
            result.MMDrawNumber = mmDraw.DrawNumber;
            result.MMDrawDate   = mmDraw.DrawDate ?? "";
            if (string.IsNullOrEmpty(result.DateLabel))
                result.DateLabel = mmDraw.DrawDate;
        }

        // ── Find D3 draws on or before selected date ─────────────────────────
        var d3Groups = _d3
            .GroupBy(d => d.DrawDate)
            .Select(g =>
            {
                var ordered = g.OrderBy(d => d.DrawNumber).ToList();
                var grpDate = DateTime.TryParse(g.Key, out var dt) ? dt : DateTime.MinValue;
                return (DateLabel: g.Key, Date: grpDate,
                    Midday:          ordered.Count >= 1 ? ordered[0].Numbers : null,
                    MiddayPrizes:    ordered.Count >= 1 ? ordered[0].Prizes  : Array.Empty<DrawPrizeTier>(),
                    MiddayDrawNum:   ordered.Count >= 1 ? ordered[0].DrawNumber : 0,
                    Evening:         ordered.Count >= 2 ? ordered[1].Numbers : null,
                    EveningPrizes:   ordered.Count >= 2 ? ordered[1].Prizes  : Array.Empty<DrawPrizeTier>(),
                    EveningDrawNum:  ordered.Count >= 2 ? ordered[1].DrawNumber : 0);
            })
            .Where(g => g.Date != DateTime.MinValue && g.Date.Date <= date.Date)
            .OrderByDescending(g => g.Date)
            .ToList();

        var d3MiddayGroup  = d3Groups.FirstOrDefault(g => g.Midday  != null);
        var d3EveningGroup = d3Groups.FirstOrDefault(g => g.Evening != null);
        result.D3Midday          = d3MiddayGroup.Midday;
        result.D3MiddayDrawNum   = d3MiddayGroup.MiddayDrawNum;
        result.D3MiddayDateLabel = d3MiddayGroup.DateLabel ?? "";
        result.D3Evening         = d3EveningGroup.Evening;
        result.D3EveningDrawNum  = d3EveningGroup.EveningDrawNum;
        result.D3EveningDateLabel = d3EveningGroup.DateLabel ?? "";
        var d3MiddayPrizes  = d3MiddayGroup.MiddayPrizes  ?? Array.Empty<DrawPrizeTier>();
        var d3EveningPrizes = d3EveningGroup.EveningPrizes ?? Array.Empty<DrawPrizeTier>();

        // ── Find D4 draw on or before selected date ──────────────────────────
        var d4Draw = _d4
            .Select(d => (Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue, d.DrawDate, d.DrawNumber, d.Numbers, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date)
            .OrderByDescending(d => d.Date)
            .FirstOrDefault();

        result.D4Numbers    = d4Draw.Numbers;
        result.D4DrawNumber = d4Draw.DrawNumber;
        result.D4DrawDate   = d4Draw.DrawDate ?? "";
        var d4Prizes = d4Draw.Prizes ?? Array.Empty<DrawPrizeTier>();

        // ── Find DD draw on or before selected date ──────────────────────────
        var ddDraw = _dd
            .Select(d => (Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue, d.DrawDate, d.DrawNumber, d.Horses, d.RaceTime, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date)
            .OrderByDescending(d => d.Date)
            .FirstOrDefault();

        result.DDHorses     = ddDraw.Horses;
        result.DDRaceTime   = ddDraw.RaceTime ?? "";
        result.DDDrawNumber = ddDraw.DrawNumber;
        result.DDDrawDate   = ddDraw.DrawDate ?? "";
        var ddPrizes = ddDraw.Prizes ?? Array.Empty<DrawPrizeTier>();

        if (string.IsNullOrEmpty(result.DateLabel))
            result.DateLabel = date.ToString("ddd MMM d, yyyy");

        // ── Check F5 sets ────────────────────────────────────────────────────
        if (result.F5Numbers.Length == 5)
        {
            var winSet = new HashSet<int>(result.F5Numbers);
            var prizes = f5Draw.Prizes ?? Array.Empty<DrawPrizeTier>();

            foreach (var (slot, rows) in ReadSets("f5", 5))
            {
                var advDates = ReadAdvanceDates("f5", slot);
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r])) continue;
                    bool f5Valid = IsRowValidForDate(advDates[r].start, advDates[r].end, f5Draw.Date, selectedDate: date);
                    if (!f5Valid)
                    {
                        bool hasAdvF5pre = advDates[r].start.HasValue || advDates[r].end.HasValue || advDates[r].drawStart > 0;
                        bool f5Future    = hasAdvF5pre && (
                            (advDates[r].drawStart > 0 && result.F5DrawNumber < advDates[r].drawStart) ||
                            (advDates[r].start.HasValue && f5Draw.Date.Date < advDates[r].start.Value.Date));
                        if (f5Future)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "F5", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers    = string.Join("  ", rows[r].Select(v => int.Parse(v).ToString("D2"))),
                                MatchLabel = "", Prize = "", DrawDate = "",
                                IsActiveNoWin = true,
                                PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                                PlayToDate = PlayToStr(advDates[r], f5Draw.Date, result.F5DrawNumber, 1.0),
                                DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0)
                            });
                        continue;
                    }
                    var nums    = rows[r].Select(int.Parse).ToArray();
                    int matches = nums.Count(n => winSet.Contains(n));
                    string prize = F5Prize(matches, prizes);
                    if (string.IsNullOrEmpty(prize))
                    {
                        bool hasAdvF5   = advDates[r].start.HasValue || advDates[r].end.HasValue || advDates[r].drawStart > 0;
                        bool f5StillOpen = (advDates[r].end.HasValue && advDates[r].end.Value.Date >= DateTime.Today)
                            || (advDates[r].drawStart > 0 && result.F5DrawNumber <= (advDates[r].drawEnd > 0 ? advDates[r].drawEnd : advDates[r].drawStart));
                        if (f5StillOpen)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "F5", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers    = string.Join("  ", nums.Select(n => n.ToString("D2"))),
                                MatchLabel = $"{matches}/5", Prize = "", DrawDate = f5Draw.DrawDate ?? "",
                                IsActiveNoWin = true,
                                PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                                PlayToDate = PlayToStr(advDates[r], f5Draw.Date, result.F5DrawNumber, 1.0),
                                DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0)
                            });
                        else if (!hasAdvF5)
                            result.Winners.Add(new WinnerEntry  // regular ticket, no advance dates — show as active upcoming
                            {
                                Game = "F5", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers    = string.Join("  ", nums.Select(n => n.ToString("D2"))),
                                MatchLabel = $"{matches}/5", Prize = "", DrawDate = "",
                                IsActiveNoWin = true,
                                PlayToDate = DateTime.Today.ToString("MM/dd")
                            });
                        // expired advance ticket with no prize — skip
                        continue;
                    }

                    result.Winners.Add(new WinnerEntry
                    {
                        Game       = "F5",
                        SetNumber  = slot + 1,
                        RowNumber  = r + 1,
                        Numbers    = string.Join("  ", nums.Select(n => n.ToString("D2"))),
                        MatchLabel = $"{matches}/5",
                        Prize      = prize,
                        DrawDate   = f5Draw.DrawDate ?? "",
                        PlayToDate = PlayToStr(advDates[r], f5Draw.Date, result.F5DrawNumber, 1.0),
                        DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                        MatchDrawNum = result.F5DrawNumber
                    });

                    // Multi-day advance ticket that still has range left after this win — also
                    // show it as still active/tracking so it doesn't disappear entirely once this
                    // win's own day ages out of view (found 2026-08-02: a ticket that won on day 1
                    // of a 7/25-8/3 range vanished completely on day 2, before that day's own draw
                    // had even posted, because a win only ever produced ONE entry).
                    bool f5ContinuesAfterWin = (advDates[r].end.HasValue && advDates[r].end.Value.Date > f5Draw.Date.Date)
                        || (advDates[r].drawEnd > 0 && advDates[r].drawEnd > result.F5DrawNumber);
                    if (f5ContinuesAfterWin)
                        result.Winners.Add(new WinnerEntry
                        {
                            Game = "F5", SetNumber = slot + 1, RowNumber = r + 1,
                            Numbers    = string.Join("  ", nums.Select(n => n.ToString("D2"))),
                            MatchLabel = $"{matches}/5", Prize = "", DrawDate = "",
                            IsActiveNoWin = true,
                            PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                            PlayToDate = PlayToStr(advDates[r], f5Draw.Date, result.F5DrawNumber, 1.0),
                            DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0)
                        });
                }
            }
        }

        // ── Check SL sets ────────────────────────────────────────────────────
        if (result.SLMain.Length == 5)
        {
            var mainSet = new HashSet<int>(result.SLMain);
            var prizes  = slDraw.Prizes ?? Array.Empty<DrawPrizeTier>();

            foreach (var (slot, rows) in ReadSets("sl", 6))
            {
                var advDates = ReadAdvanceDates("sl", slot);
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r])) continue;
                    bool slValid = IsRowValidForDate(advDates[r].start, advDates[r].end, slDraw.Date, selectedDate: date);
                    if (!slValid)
                    {
                        bool hasAdvSLpre = advDates[r].start.HasValue || advDates[r].end.HasValue || advDates[r].drawStart > 0;
                        bool slFuture    = hasAdvSLpre && (
                            (advDates[r].drawStart > 0 && result.SLDrawNumber < advDates[r].drawStart) ||
                            (advDates[r].start.HasValue && slDraw.Date.Date < advDates[r].start.Value.Date));
                        if (slFuture)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "SL", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers    = string.Join("  ", rows[r].Take(5).Select(v => int.Parse(v).ToString("D2")))
                                             + "  |M:" + int.Parse(rows[r][5]).ToString("D2"),
                                MatchLabel = "", Prize = "", DrawDate = "",
                                IsActiveNoWin = true,
                                PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                                PlayToDate = PlayToStr(advDates[r], slDraw.Date, result.SLDrawNumber, 3.5),
                                DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0)
                            });
                        continue;
                    }
                    var nums        = rows[r].Select(int.Parse).ToArray();
                    int mainMatches = nums.Take(5).Count(n => mainSet.Contains(n));
                    bool megaMatch  = nums.Length == 6 && result.SLMega > 0 && nums[5] == result.SLMega;

                    string matchLabel  = megaMatch ? $"{mainMatches}+M" : $"{mainMatches}/5";
                    string numsDisplay = string.Join("  ", nums.Take(5).Select(n => n.ToString("D2")))
                                        + "  |M:" + nums[5].ToString("D2");
                    string prize = SLPrize(mainMatches, megaMatch, prizes);
                    if (string.IsNullOrEmpty(prize))
                    {
                        bool hasAdvSL   = advDates[r].start.HasValue || advDates[r].end.HasValue || advDates[r].drawStart > 0;
                        bool slStillOpen = (advDates[r].end.HasValue && advDates[r].end.Value.Date >= DateTime.Today)
                            || (advDates[r].drawStart > 0 && result.SLDrawNumber <= (advDates[r].drawEnd > 0 ? advDates[r].drawEnd : advDates[r].drawStart));
                        if (slStillOpen)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "SL", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers = numsDisplay, MatchLabel = matchLabel, Prize = "",
                                DrawDate = slDraw.DrawDate ?? "", IsActiveNoWin = true,
                                PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                                PlayToDate = PlayToStr(advDates[r], slDraw.Date, result.SLDrawNumber, 3.5),
                                DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0)
                            });
                        else if (!hasAdvSL)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "SL", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers = numsDisplay, MatchLabel = matchLabel, Prize = "", DrawDate = "",
                                IsActiveNoWin = true, PlayToDate = DateTime.Today.ToString("MM/dd")
                            });
                        // expired advance ticket with no prize — skip
                        continue;
                    }

                    result.Winners.Add(new WinnerEntry
                    {
                        Game       = "SL",
                        SetNumber  = slot + 1,
                        RowNumber  = r + 1,
                        Numbers    = numsDisplay,
                        MatchLabel = matchLabel,
                        Prize      = prize,
                        DrawDate   = slDraw.DrawDate ?? "",
                        PlayToDate = PlayToStr(advDates[r], slDraw.Date, result.SLDrawNumber, 3.5),
                        DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                        MatchDrawNum = result.SLDrawNumber
                    });

                    // Same "still active after a win" companion entry as F5 — see comment there.
                    bool slContinuesAfterWin = (advDates[r].end.HasValue && advDates[r].end.Value.Date > slDraw.Date.Date)
                        || (advDates[r].drawEnd > 0 && advDates[r].drawEnd > result.SLDrawNumber);
                    if (slContinuesAfterWin)
                        result.Winners.Add(new WinnerEntry
                        {
                            Game = "SL", SetNumber = slot + 1, RowNumber = r + 1,
                            Numbers = numsDisplay, MatchLabel = matchLabel, Prize = "", DrawDate = "",
                            IsActiveNoWin = true,
                            PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                            PlayToDate = PlayToStr(advDates[r], slDraw.Date, result.SLDrawNumber, 3.5),
                            DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0)
                        });
                }
            }
        }

        // ── Check PB sets ────────────────────────────────────────────────────
        if (result.PBMain.Length == 5)
        {
            var mainSet = new HashSet<int>(result.PBMain);
            var prizes  = pbDraw.Prizes ?? Array.Empty<DrawPrizeTier>();

            foreach (var (slot, rows) in ReadSets("pb", 6))
            {
                var advDates = ReadAdvanceDates("pb", slot);
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r])) continue;
                    bool pbValid = IsRowValidForDate(advDates[r].start, advDates[r].end, pbDraw.Date, selectedDate: date);
                    if (!pbValid)
                    {
                        bool hasAdvPBpre = advDates[r].start.HasValue || advDates[r].end.HasValue || advDates[r].drawStart > 0;
                        bool pbFuture    = hasAdvPBpre && (
                            (advDates[r].drawStart > 0 && result.PBDrawNumber < advDates[r].drawStart) ||
                            (advDates[r].start.HasValue && pbDraw.Date.Date < advDates[r].start.Value.Date));
                        if (pbFuture)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "PB", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers    = string.Join("  ", rows[r].Take(5).Select(v => int.Parse(v).ToString("D2")))
                                             + "  |PB:" + int.Parse(rows[r][5]).ToString("D2"),
                                MatchLabel = "", Prize = "", DrawDate = "",
                                IsActiveNoWin = true,
                                PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                                PlayToDate = PlayToStr(advDates[r], pbDraw.Date, result.PBDrawNumber, 2.5),
                                DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0)
                            });
                        continue;
                    }
                    var nums        = rows[r].Select(int.Parse).ToArray();
                    int mainMatches = nums.Take(5).Count(n => mainSet.Contains(n));
                    bool pbMatch    = nums.Length == 6 && result.PBBall > 0 && nums[5] == result.PBBall;

                    string matchLabel  = pbMatch ? $"{mainMatches}+PB" : $"{mainMatches}/5";
                    string numsDisplay = string.Join("  ", nums.Take(5).Select(n => n.ToString("D2")))
                                        + "  |PB:" + nums[5].ToString("D2");
                    string prize = PBPrize(mainMatches, pbMatch, prizes);
                    if (string.IsNullOrEmpty(prize))
                    {
                        bool hasAdvPB   = advDates[r].start.HasValue || advDates[r].end.HasValue || advDates[r].drawStart > 0;
                        bool pbStillOpen = (advDates[r].end.HasValue && advDates[r].end.Value.Date >= DateTime.Today)
                            || (advDates[r].drawStart > 0 && result.PBDrawNumber <= (advDates[r].drawEnd > 0 ? advDates[r].drawEnd : advDates[r].drawStart));
                        if (pbStillOpen)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "PB", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers = numsDisplay, MatchLabel = matchLabel, Prize = "",
                                DrawDate = pbDraw.DrawDate ?? "", IsActiveNoWin = true,
                                PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                                PlayToDate = PlayToStr(advDates[r], pbDraw.Date, result.PBDrawNumber, 2.5),
                                DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0)
                            });
                        else if (!hasAdvPB)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "PB", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers = numsDisplay, MatchLabel = matchLabel, Prize = "", DrawDate = "",
                                IsActiveNoWin = true, PlayToDate = DateTime.Today.ToString("MM/dd")
                            });
                        // expired advance ticket with no prize — skip
                        continue;
                    }

                    result.Winners.Add(new WinnerEntry
                    {
                        Game       = "PB",
                        SetNumber  = slot + 1,
                        RowNumber  = r + 1,
                        Numbers    = numsDisplay,
                        MatchLabel = matchLabel,
                        Prize      = prize,
                        DrawDate   = pbDraw.DrawDate ?? "",
                        PlayToDate = PlayToStr(advDates[r], pbDraw.Date, result.PBDrawNumber, 2.5),
                        DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                        MatchDrawNum = result.PBDrawNumber
                    });

                    // Same "still active after a win" companion entry as F5 — see comment there.
                    bool pbContinuesAfterWin = (advDates[r].end.HasValue && advDates[r].end.Value.Date > pbDraw.Date.Date)
                        || (advDates[r].drawEnd > 0 && advDates[r].drawEnd > result.PBDrawNumber);
                    if (pbContinuesAfterWin)
                        result.Winners.Add(new WinnerEntry
                        {
                            Game = "PB", SetNumber = slot + 1, RowNumber = r + 1,
                            Numbers = numsDisplay, MatchLabel = matchLabel, Prize = "", DrawDate = "",
                            IsActiveNoWin = true,
                            PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                            PlayToDate = PlayToStr(advDates[r], pbDraw.Date, result.PBDrawNumber, 2.5),
                            DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0)
                        });
                }
            }
        }

        // ── Check MM sets ────────────────────────────────────────────────────
        if (result.MMMain.Length == 5)
        {
            var mainSet = new HashSet<int>(result.MMMain);
            var prizes  = mmDraw.Prizes ?? Array.Empty<DrawPrizeTier>();

            foreach (var (slot, rows) in ReadSets("mm", 6))
            {
                var advDates = ReadAdvanceDates("mm", slot);
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r])) continue;
                    bool mmValid = IsRowValidForDate(advDates[r].start, advDates[r].end, mmDraw.Date, selectedDate: date);
                    if (!mmValid)
                    {
                        bool hasAdvMMpre = advDates[r].start.HasValue || advDates[r].end.HasValue || advDates[r].drawStart > 0;
                        bool mmFuture    = hasAdvMMpre && (
                            (advDates[r].drawStart > 0 && result.MMDrawNumber < advDates[r].drawStart) ||
                            (advDates[r].start.HasValue && mmDraw.Date.Date < advDates[r].start.Value.Date));
                        if (mmFuture)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "MM", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers    = string.Join("  ", rows[r].Take(5).Select(v => int.Parse(v).ToString("D2")))
                                             + "  |MB:" + int.Parse(rows[r][5]).ToString("D2"),
                                MatchLabel = "", Prize = "", DrawDate = "",
                                IsActiveNoWin = true,
                                PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                                PlayToDate = PlayToStr(advDates[r], mmDraw.Date, result.MMDrawNumber, 3.5),
                                DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0)
                            });
                        continue;
                    }
                    var nums        = rows[r].Select(int.Parse).ToArray();
                    int mainMatches = nums.Take(5).Count(n => mainSet.Contains(n));
                    bool mbMatch    = nums.Length == 6 && result.MMBall > 0 && nums[5] == result.MMBall;

                    string matchLabel  = mbMatch ? $"{mainMatches}+MB" : $"{mainMatches}/5";
                    string numsDisplay = string.Join("  ", nums.Take(5).Select(n => n.ToString("D2")))
                                        + "  |MB:" + nums[5].ToString("D2");
                    string prize = MMPrize(mainMatches, mbMatch, prizes);
                    if (string.IsNullOrEmpty(prize))
                    {
                        bool hasAdvMM   = advDates[r].start.HasValue || advDates[r].end.HasValue || advDates[r].drawStart > 0;
                        bool mmStillOpen = (advDates[r].end.HasValue && advDates[r].end.Value.Date >= DateTime.Today)
                            || (advDates[r].drawStart > 0 && result.MMDrawNumber <= (advDates[r].drawEnd > 0 ? advDates[r].drawEnd : advDates[r].drawStart));
                        if (mmStillOpen)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "MM", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers = numsDisplay, MatchLabel = matchLabel, Prize = "",
                                DrawDate = mmDraw.DrawDate ?? "", IsActiveNoWin = true,
                                PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                                PlayToDate = PlayToStr(advDates[r], mmDraw.Date, result.MMDrawNumber, 3.5),
                                DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0)
                            });
                        else if (!hasAdvMM)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "MM", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers = numsDisplay, MatchLabel = matchLabel, Prize = "", DrawDate = "",
                                IsActiveNoWin = true, PlayToDate = DateTime.Today.ToString("MM/dd")
                            });
                        // expired advance ticket with no prize — skip
                        continue;
                    }

                    result.Winners.Add(new WinnerEntry
                    {
                        Game       = "MM",
                        SetNumber  = slot + 1,
                        RowNumber  = r + 1,
                        Numbers    = numsDisplay,
                        MatchLabel = matchLabel,
                        Prize      = prize,
                        DrawDate   = mmDraw.DrawDate ?? "",
                        PlayToDate = PlayToStr(advDates[r], mmDraw.Date, result.MMDrawNumber, 3.5),
                        DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                        MatchDrawNum = result.MMDrawNumber
                    });

                    // Same "still active after a win" companion entry as F5 — see comment there.
                    bool mmContinuesAfterWin = (advDates[r].end.HasValue && advDates[r].end.Value.Date > mmDraw.Date.Date)
                        || (advDates[r].drawEnd > 0 && advDates[r].drawEnd > result.MMDrawNumber);
                    if (mmContinuesAfterWin)
                        result.Winners.Add(new WinnerEntry
                        {
                            Game = "MM", SetNumber = slot + 1, RowNumber = r + 1,
                            Numbers = numsDisplay, MatchLabel = matchLabel, Prize = "", DrawDate = "",
                            IsActiveNoWin = true,
                            PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                            PlayToDate = PlayToStr(advDates[r], mmDraw.Date, result.MMDrawNumber, 3.5),
                            DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0)
                        });
                }
            }
        }

        // ── Check D3 sets ────────────────────────────────────────────────────
        if (result.D3Midday != null || result.D3Evening != null)
        {
            foreach (var (slot, rows) in ReadSets("d3", 3))
            {
                var betTypes    = ReadD3BetTypes(slot);
                var drawFilters = ReadD3DrawFilters(slot);
                var advDates    = ReadAdvanceDates("d3", slot);
                var d3MiddayDate  = DateTime.TryParse(result.D3MiddayDateLabel,  out var dmd) ? dmd : DateTime.Today;
                var d3EveningDate = DateTime.TryParse(result.D3EveningDateLabel, out var ded) ? ded : DateTime.Today;
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r], 0)) continue;

                    // Retire tickets whose play window has fully ended — by DATE and by DRAW NUMBER.
                    // D3 draws twice a day, so at any time before today's evening draw posts, the
                    // "current" evening draw the rest of this block checks against is still YESTERDAY's
                    // (see D3EveningDateLabel). Without this gate, a ticket whose end date was yesterday
                    // keeps matching that stale evening draw all day today and re-surfaces an old win.
                    // The 6am grace window (below) is the one deliberate exception: it gives the last
                    // evening draw time to post before we retire a ticket that just ended.
                    string df = drawFilters[r]; // "B", "M", or "E" — needed below before the retirement check

                    bool d3ShouldRetire = D3TimingRules.ShouldRetire(
                        advDates[r].end, advDates[r].start, advDates[r].drawEnd,
                        df, result.D3MiddayDrawNum, result.D3EveningDrawNum,
                        DateTime.Today, DateTime.Now.TimeOfDay);
                    if (d3ShouldRetire) continue;

                    var userNums   = rows[r].Select(int.Parse).ToArray();
                    var sortedUser = userNums.OrderBy(x => x).ToArray();
                    string bt      = betTypes[r];    // "S", "B", or "S&B"
                    string boxType = D3BoxType(userNums); // "Box3", "Box6", or "" (triple)

                    // Raw draw matches — only check draws allowed by per-row filter and advance date
                    bool middayValid  = IsRowValidForDate(advDates[r].start, advDates[r].end, d3MiddayDate, selectedDate: date);
                    bool eveningValid = IsRowValidForDate(advDates[r].start, advDates[r].end, d3EveningDate, selectedDate: date);
                    bool isDayStr = df != "E" && middayValid  && result.D3Midday  != null && userNums.SequenceEqual(result.D3Midday);
                    bool isDayBox = !isDayStr && df != "E" && middayValid  && result.D3Midday  != null &&
                                   sortedUser.SequenceEqual(result.D3Midday.OrderBy(x => x));
                    bool isEveStr = df != "M" && eveningValid && result.D3Evening != null && userNums.SequenceEqual(result.D3Evening);
                    bool isEveBox = !isEveStr && df != "M" && eveningValid && result.D3Evening != null &&
                                   sortedUser.SequenceEqual(result.D3Evening.OrderBy(x => x));

                    // Apply bet type to determine win type
                    string? dayWin = null, eveWin = null;
                    if (bt == "S")
                    {
                        if (isDayStr) dayWin = "Straight";
                        if (isEveStr) eveWin = "Straight";
                    }
                    else if (bt == "B")
                    {
                        // Box bet wins on any arrangement (including exact order)
                        if ((isDayStr || isDayBox) && !string.IsNullOrEmpty(boxType)) dayWin = boxType;
                        if ((isEveStr || isEveBox) && !string.IsNullOrEmpty(boxType)) eveWin = boxType;
                    }
                    else // "S&B"
                    {
                        if (isDayStr) dayWin = "SB_Straight"; // tier 3: both portions
                        else if (isDayBox && !string.IsNullOrEmpty(boxType)) dayWin = "SB_Box"; // tier 4: box only
                        if (isEveStr) eveWin = "SB_Straight";
                        else if (isEveBox && !string.IsNullOrEmpty(boxType)) eveWin = "SB_Box";
                    }

                    if (dayWin == null && eveWin == null)
                    {
                        bool d3InDateRange = advDates[r].start.HasValue && advDates[r].end.HasValue &&
                                             date.Date >= advDates[r].start.Value.Date && date.Date <= advDates[r].end.Value.Date;
                        bool d3HasDrawRange = advDates[r].drawStart > 0;
                        bool d3DrawInRange  = d3HasDrawRange && (middayValid || eveningValid);
                        // Grace window: keep D3 visible until 6am the morning after its last valid day,
                        // so the overnight/last-evening draw result has time to be checked.
                        bool d3Grace = D3TimingRules.IsGraceWindow(advDates[r].end, DateTime.Today, DateTime.Now.TimeOfDay);
                        bool d3IsActive     = middayValid || eveningValid || d3InDateRange || d3DrawInRange || d3Grace;
                        bool d3StillOpen    = (advDates[r].end.HasValue && advDates[r].end.Value.Date >= DateTime.Today)
                                              || d3DrawInRange || d3Grace;
                        bool hasAdvD3 = advDates[r].start.HasValue || advDates[r].end.HasValue || advDates[r].drawStart > 0;
                        if (d3IsActive && d3StillOpen)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "D3", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers = string.Join("-", userNums), MatchLabel = "✗", Prize = "",
                                DrawDate = result.D3MiddayDateLabel, IsActiveNoWin = true,
                                PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                                PlayToDate = PlayToStr(advDates[r], d3MiddayDate, Math.Max(result.D3MiddayDrawNum, result.D3EveningDrawNum), 0.5),
                                DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                                BetType = bt,
                                DrawFilter = df
                            });
                        else if (!hasAdvD3)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "D3", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers = string.Join("-", userNums), MatchLabel = "✗", Prize = "",
                                DrawDate = result.D3MiddayDateLabel, IsActiveNoWin = true,
                                PlayToDate = DateTime.Today.ToString("MM/dd"),
                                BetType = bt,
                                DrawFilter = df
                            });
                        // expired advance D3 ticket with no win — skip
                        continue;
                    }

                    string matchLabel, prize, d3DrawDate;
                    int d3MatchDrawNum;
                    if (dayWin != null && eveWin != null)
                    {
                        matchLabel  = $"m-{D3Label(dayWin)} e-{D3Label(eveWin)}";
                        prize       = $"{D3PrizeCalc(dayWin, boxType, d3MiddayPrizes)} + {D3PrizeCalc(eveWin, boxType, d3EveningPrizes)}";
                        d3DrawDate  = result.D3MiddayDateLabel;
                        d3MatchDrawNum = result.D3MiddayDrawNum;
                    }
                    else if (dayWin != null)
                    {
                        matchLabel  = $"m-{D3Label(dayWin)}";
                        prize       = D3PrizeCalc(dayWin, boxType, d3MiddayPrizes);
                        d3DrawDate  = result.D3MiddayDateLabel;
                        d3MatchDrawNum = result.D3MiddayDrawNum;
                    }
                    else
                    {
                        matchLabel  = $"e-{D3Label(eveWin!)}";
                        prize       = D3PrizeCalc(eveWin!, boxType, d3EveningPrizes);
                        d3DrawDate  = result.D3EveningDateLabel;
                        d3MatchDrawNum = result.D3EveningDrawNum;
                    }

                    result.Winners.Add(new WinnerEntry
                    {
                        Game       = "D3",
                        SetNumber  = slot + 1,
                        RowNumber  = r + 1,
                        Numbers    = string.Join("-", userNums),
                        MatchLabel = matchLabel,
                        Prize      = prize,
                        DrawDate   = d3DrawDate,
                        PlayToDate = PlayToStr(advDates[r], d3MiddayDate, Math.Max(result.D3MiddayDrawNum, result.D3EveningDrawNum), 0.5),
                        DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                        BetType = bt,
                        DrawFilter = df,
                        MatchDrawNum = d3MatchDrawNum
                    });
                }
            }
        }

        // ── Check D4 sets ────────────────────────────────────────────────────
        if (result.D4Numbers != null)
        {
            var sortedWin = result.D4Numbers.OrderBy(x => x).ToArray();
            foreach (var (slot, rows) in ReadSets("d4", 4))
            {
                var betTypes = ReadD4BetTypes(slot);
                var advDates = ReadAdvanceDates("d4", slot);
                var d4Date   = DateTime.TryParse(result.D4DrawDate, out var d4dt) ? d4dt : DateTime.Today;
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r], 0)) continue;
                    string bt      = betTypes[r];
                    bool d4Valid = IsRowValidForDate(advDates[r].start, advDates[r].end, d4Date, selectedDate: date);
                    if (!d4Valid)
                    {
                        bool d4InDateRange = advDates[r].start.HasValue && advDates[r].end.HasValue &&
                                             date.Date >= advDates[r].start.Value.Date && date.Date <= advDates[r].end.Value.Date;
                        bool d4DrawInRange = advDates[r].drawStart > 0 &&
                                             result.D4DrawNumber >= advDates[r].drawStart &&
                                             result.D4DrawNumber <= (advDates[r].drawEnd > 0 ? advDates[r].drawEnd : advDates[r].drawStart);
                        bool d4IsActive       = d4InDateRange || d4DrawInRange;
                        bool d4StillOpenEarly = (advDates[r].end.HasValue && advDates[r].end.Value.Date >= DateTime.Today) || d4DrawInRange;
                        if (d4IsActive && d4StillOpenEarly)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "D4", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers = string.Join("-", rows[r].Select(int.Parse)),
                                MatchLabel = "✗", Prize = "",
                                DrawDate = result.D4DrawDate, IsActiveNoWin = true,
                                PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                                PlayToDate = PlayToStr(advDates[r], d4Date, result.D4DrawNumber, 1.0),
                                DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                                BetType = bt
                            });
                        continue;
                    }
                    var userNums   = rows[r].Select(int.Parse).ToArray();
                    var sortedUser = userNums.OrderBy(x => x).ToArray();
                    string boxType = D4BoxType(userNums); // "Box24","Box12","Box6","Box4", or ""

                    bool isStr = userNums.SequenceEqual(result.D4Numbers);
                    bool isBox = !isStr && sortedUser.SequenceEqual(sortedWin);

                    string? winType = null;
                    string matchLabel, prize;

                    if (bt == "S")
                    {
                        if (isStr) winType = "Straight";
                    }
                    else if (bt == "B")
                    {
                        // Box bet wins on any arrangement (including exact)
                        if ((isStr || isBox) && !string.IsNullOrEmpty(boxType)) winType = boxType;
                    }
                    else // S&B
                    {
                        if (isStr)      winType = "SB_Straight";
                        else if (isBox && !string.IsNullOrEmpty(boxType)) winType = boxType;
                    }

                    if (winType == null)
                    {
                        bool d4StillOpen = (advDates[r].end.HasValue && advDates[r].end.Value.Date > DateTime.Today)
                            || (advDates[r].drawStart > 0 && result.D4DrawNumber <= (advDates[r].drawEnd > 0 ? advDates[r].drawEnd : advDates[r].drawStart));
                        if (d4StillOpen)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "D4", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers = string.Join("-", userNums), MatchLabel = "✗", Prize = "",
                                DrawDate = result.D4DrawDate, IsActiveNoWin = true,
                                PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                                PlayToDate = PlayToStr(advDates[r], d4Date, result.D4DrawNumber, 1.0),
                                DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                                BetType = bt
                            });
                        continue;
                    }

                    if (winType == "SB_Straight")
                    {
                        matchLabel = "S&B!";
                        prize      = D4PrizeCalc("SB_Straight", bt, d4Prizes);
                    }
                    else if (winType == "Straight")
                    {
                        matchLabel = "Straight";
                        prize      = D4PrizeCalc("Straight", bt, d4Prizes);
                    }
                    else
                    {
                        matchLabel = "Box";
                        prize      = D4PrizeCalc(winType, bt, d4Prizes);
                    }

                    result.Winners.Add(new WinnerEntry
                    {
                        Game       = "D4",
                        SetNumber  = slot + 1,
                        RowNumber  = r + 1,
                        Numbers    = string.Join("-", userNums),
                        MatchLabel = matchLabel,
                        Prize      = prize,
                        DrawDate   = result.D4DrawDate,
                        PlayToDate = PlayToStr(advDates[r], d4Date, result.D4DrawNumber, 1.0),
                        DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                        MatchDrawNum = result.D4DrawNumber,
                        BetType = bt
                    });
                }
            }
        }

        // ── Check DD sets ────────────────────────────────────────────────────
        if (result.DDHorses != null && result.DDHorses.Length == 3)
        {
            string winTimeNorm = NormalizeTime(result.DDRaceTime);
            string winTimeLast3 = winTimeNorm.Length >= 3 ? winTimeNorm[^3..] : winTimeNorm;
            var ddAdvSlotCache = new Dictionary<int, (DateTime?, DateTime?)[]>();
            var ddDate = DateTime.TryParse(result.DDDrawDate, out var dddt) ? dddt : DateTime.Today;
            foreach (var (slot, rows) in ReadDDSets())
            {
                var advDates = ReadAdvanceDates("dd", slot);
                for (int r = 0; r < Rows; r++)
                {
                    var (h1, h2, h3, userTime) = rows[r];
                    bool horsesSet = h1 > 0 && h2 > 0 && h3 > 0;
                    bool ddValid = IsRowValidForDate(advDates[r].start, advDates[r].end, ddDate, selectedDate: date);
                    if (!ddValid) continue;
                    string userTimeDigits = new string(userTime.Where(char.IsDigit).ToArray());
                    bool timeEntered = userTimeDigits.Length == 3;

                    if (!horsesSet && !timeEntered) continue;

                    bool timeMatch = timeEntered && !string.IsNullOrEmpty(winTimeLast3)
                                     && userTimeDigits == winTimeLast3;

                    // Auto-detect all wins — combine horse + time prizes when both match
                    // CA Lottery DD has no separate Trifecta tier; 3-horse match = Exacta prize
                    string? winType = null;
                    if (horsesSet)
                    {
                        bool m1 = h1 == result.DDHorses[0];
                        bool m2 = h2 == result.DDHorses[1];
                        bool m3 = h3 == result.DDHorses[2];
                        if      (m1 && m2 && m3 && timeMatch) winType = "GRAND!";
                        else if (m1 && m2 && timeMatch)       winType = "Exa+Time"; // includes all-3 case
                        else if (m1 && m2)                    winType = "Exacta";   // includes all-3 case
                        else if (m1 && timeMatch)             winType = "Win+Time";
                        else if (m1)                          winType = "Win";
                        else if (timeMatch)                   winType = "Time";
                    }
                    else if (timeMatch) winType = "Time";

                    string ddNumsDisplay = horsesSet
                        ? $"{h1}-{h2}-{h3}" + (timeEntered ? $"  ⏱{userTime}" : "")
                        : $"⏱{userTime}";

                    if (winType == null)
                    {
                        bool ddStillOpen = (advDates[r].end.HasValue && advDates[r].end.Value.Date > DateTime.Today)
                            || (advDates[r].drawStart > 0 && result.DDDrawNumber <= (advDates[r].drawEnd > 0 ? advDates[r].drawEnd : advDates[r].drawStart));
                        if (ddStillOpen)
                            result.Winners.Add(new WinnerEntry
                            {
                                Game = "DD", SetNumber = slot + 1, RowNumber = r + 1,
                                Numbers = ddNumsDisplay, MatchLabel = "✗", Prize = "",
                                DrawDate = result.DDDrawDate, IsActiveNoWin = true,
                                PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                                PlayToDate = PlayToStr(advDates[r], ddDate, result.DDDrawNumber, 1.0),
                                DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0)
                            });
                        continue;
                    }

                    string matchLabel = winType;
                    string prize = winType switch
                    {
                        "Win+Time" => DDComboPrize("Win",    "Time", ddPrizes),
                        "Exa+Time" => DDComboPrize("Exacta", "Time", ddPrizes),
                        _          => DDPrize(winType, ddPrizes)
                    };

                    result.Winners.Add(new WinnerEntry
                    {
                        Game       = "DD",
                        SetNumber  = slot + 1,
                        RowNumber  = r + 1,
                        Numbers    = ddNumsDisplay,
                        MatchLabel = matchLabel,
                        Prize      = prize,
                        DrawDate   = result.DDDrawDate,
                        PlayToDate = PlayToStr(advDates[r], ddDate, result.DDDrawNumber, 1.0),
                        DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                        MatchDrawNum = result.DDDrawNumber
                    });
                }
            }
        }

        // ── Scan past draws for active advance tickets ───────────────────────────
        ScanAdvancePastWins(date, result);

        // ── Ensure active advance tickets always appear even when draw data is missing ──
        // Covers case where fetch failed or today's draw isn't published yet.
        ScanActiveAdvanceTickets(date, result);

        return result;
    }

    // Adds "in play" entries for any active advance ticket rows covering the selected date
    // that weren't already added by the primary scan or ScanAdvancePastWins.
    static void ScanActiveAdvanceTickets(DateTime date, DateResultData result)
    {
        bool AlreadyAdded(string game, int slot, int row) =>
            result.Winners.Any(w => w.Game == game && w.SetNumber == slot + 1 && w.RowNumber == row + 1);

        void AddIfMissing(string game, string prefix, int cols, Func<int[], string> numsDisplay,
            int currentDrawNum = 0, DateTime currentDrawDate = default, double daysPerDraw = 1.0)
        {
            foreach (var (slot, rows) in ReadSets(prefix, cols))
            {
                var advDates = ReadAdvanceDates(prefix, slot);
                // Fallback path only fires for rows the primary scan skipped, but D3/D4 rows still
                // need their bet-type/draw-filter tags on the Results page — read them here too so
                // a row that falls through never silently loses its "S&B"/"M+E" tag (or the D3
                // midday-strikethrough, which depends on DrawFilter being set).
                string[] betTypes    = game == "D3" ? ReadD3BetTypes(slot)    : game == "D4" ? ReadD4BetTypes(slot) : null;
                string[] drawFilters = game == "D3" ? ReadD3DrawFilters(slot) : null;
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r])) continue;
                    bool hasDateRange = advDates[r].start.HasValue;
                    bool hasDrawRange = advDates[r].drawStart > 0;
                    if (!hasDateRange && !hasDrawRange) continue;
                    if (hasDateRange)
                    {
                        if (date.Date < advDates[r].start.Value.Date) continue;
                        if (advDates[r].end.HasValue && date.Date > advDates[r].end.Value.Date) continue;
                        if (advDates[r].end.HasValue && advDates[r].end.Value.Date < DateTime.Today) continue;
                    }
                    if (hasDrawRange && !hasDateRange)
                    {
                        // Draw# only ticket — verify current draw is in range
                        if (currentDrawNum == 0) continue;
                        int effEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : advDates[r].drawStart;
                        if (currentDrawNum < advDates[r].drawStart || currentDrawNum > effEnd) continue;
                    }
                    if (AlreadyAdded(game, slot, r)) continue;
                    var nums = rows[r].Select(int.Parse).ToArray();
                    result.Winners.Add(new WinnerEntry
                    {
                        Game = game, SetNumber = slot + 1, RowNumber = r + 1,
                        Numbers = numsDisplay(nums), MatchLabel = "", Prize = "", DrawDate = "",
                        IsActiveNoWin = true,
                        PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                        PlayToDate = PlayToStr(advDates[r], currentDrawDate, currentDrawNum, daysPerDraw),
                        DrawEnd = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                        BetType = betTypes?[r] ?? "",
                        DrawFilter = drawFilters?[r] ?? ""
                    });
                }
            }
        }

        // Parse draw dates for each game (needed for approximate end-date calculation)
        var f5DrawDate = DateTime.TryParse(result.F5DrawDate, out var f5dd) ? f5dd : default;
        var slDrawDate = DateTime.TryParse(result.SLDrawDate, out var sldd) ? sldd : default;
        var pbDrawDate = DateTime.TryParse(result.PBDrawDate, out var pbdd) ? pbdd : default;
        var mmDrawDate = DateTime.TryParse(result.MMDrawDate, out var mmdd) ? mmdd : default;
        var d3DrawDate = DateTime.TryParse(result.D3MiddayDateLabel, out var d3dd) ? d3dd : default;
        var d4DrawDate = DateTime.TryParse(result.D4DrawDate, out var d4dd) ? d4dd : default;

        // Use max draw# for D3 (either midday or evening — whichever is latest)
        int d3DrawNum = Math.Max(result.D3MiddayDrawNum, result.D3EveningDrawNum);
        AddIfMissing("F5", "f5", 5, n => string.Join("  ", n.Select(x => x.ToString("D2"))), result.F5DrawNumber, f5DrawDate, 1.0);
        AddIfMissing("SL", "sl", 6, n => string.Join("  ", n.Take(5).Select(x => x.ToString("D2"))) + "  |M:" + n[5].ToString("D2"), result.SLDrawNumber, slDrawDate, 3.5);
        AddIfMissing("PB", "pb", 6, n => string.Join("  ", n.Take(5).Select(x => x.ToString("D2"))) + "  |PB:" + n[5].ToString("D2"), result.PBDrawNumber, pbDrawDate, 2.5);
        AddIfMissing("MM", "mm", 6, n => string.Join("  ", n.Take(5).Select(x => x.ToString("D2"))) + "  |MB:" + n[5].ToString("D2"), result.MMDrawNumber, mmDrawDate, 3.5);
        AddIfMissing("D3", "d3", 3, n => string.Join("-", n), d3DrawNum, d3DrawDate, 0.5);
        AddIfMissing("D4", "d4", 4, n => string.Join("-", n), result.D4DrawNumber, d4DrawDate, 1.0);
        // DD handled separately (different storage format — skip here)
    }

    // For each game, look through ALL cached draws BEFORE the selected date and surface
    // any wins by active advance-play tickets that were not caught by the primary scan.
    // This keeps past wins visible on the results page as long as the ticket is still running.
    static void ScanAdvancePastWins(DateTime date, DateResultData result)
    {
        // Only look back 7 days — prevents old advance wins cluttering today's view
        var cutoff = date.Date.AddDays(-7);

        // ── F5 ──────────────────────────────────────────────────────────────────
        var f5Past = _f5
            .Select(d => (Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                          d.DrawDate, d.DrawNumber, d.Numbers, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date && d.Date.Date >= cutoff)
            .OrderByDescending(d => d.Date)
            .ToList();

        if (f5Past.Count > 0)
        {
            foreach (var (slot, rows) in ReadSets("f5", 5))
            {
                var advDates = ReadAdvanceDates("f5", slot);
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r])) continue;
                    if (!advDates[r].start.HasValue && !advDates[r].end.HasValue && advDates[r].drawStart == 0) continue;
                    // Skip expired tickets (end date already passed)
                    if (advDates[r].end.HasValue && advDates[r].end.Value.Date < DateTime.Today) continue;
                    if (!advDates[r].end.HasValue && advDates[r].start.HasValue && advDates[r].start.Value.Date < DateTime.Today) continue;
                    var nums = rows[r].Select(int.Parse).ToArray();
                    foreach (var draw in f5Past)
                    {
                        if (!IsRowValidForDate(advDates[r].start, advDates[r].end, draw.Date)) continue;
                        if (result.Winners.Any(w => w.Game == "F5" && w.SetNumber == slot + 1 && w.RowNumber == r + 1 && w.DrawDate == draw.DrawDate && !w.IsActiveNoWin)) continue;
                        var winSet  = new HashSet<int>(draw.Numbers);
                        int matches = nums.Count(n => winSet.Contains(n));
                        string prize = F5Prize(matches, draw.Prizes ?? Array.Empty<DrawPrizeTier>());
                        if (string.IsNullOrEmpty(prize)) continue;
                        result.Winners.Add(new WinnerEntry
                        {
                            Game = "F5", SetNumber = slot + 1, RowNumber = r + 1,
                            Numbers    = string.Join("  ", nums.Select(n => n.ToString("D2"))),
                            MatchLabel = $"{matches}/5", Prize = prize, DrawDate = draw.DrawDate ?? "",
                            PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                            PlayToDate   = PlayToStr(advDates[r], draw.Date, draw.DrawNumber, 1.0),
                            DrawEnd      = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                            MatchDrawNum = draw.DrawNumber
                        });
                    }
                }
            }
        }

        // ── SL ──────────────────────────────────────────────────────────────────
        var slPast = _sl
            .Select(d => (Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                          d.DrawDate, d.DrawNumber, d.MainNumbers, d.MegaNumber, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date && d.Date.Date >= cutoff)
            .OrderByDescending(d => d.Date)
            .ToList();

        if (slPast.Count > 0)
        {
            foreach (var (slot, rows) in ReadSets("sl", 6))
            {
                var advDates = ReadAdvanceDates("sl", slot);
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r])) continue;
                    if (!advDates[r].start.HasValue && !advDates[r].end.HasValue && advDates[r].drawStart == 0) continue;
                    // Skip expired tickets (end date already passed)
                    if (advDates[r].end.HasValue && advDates[r].end.Value.Date < DateTime.Today) continue;
                    if (!advDates[r].end.HasValue && advDates[r].start.HasValue && advDates[r].start.Value.Date < DateTime.Today) continue;
                    var nums = rows[r].Select(int.Parse).ToArray();
                    foreach (var draw in slPast)
                    {
                        if (!IsRowValidForDate(advDates[r].start, advDates[r].end, draw.Date)) continue;
                        if (result.Winners.Any(w => w.Game == "SL" && w.SetNumber == slot + 1 && w.RowNumber == r + 1 && w.DrawDate == draw.DrawDate && !w.IsActiveNoWin)) continue;
                        var mainSet     = new HashSet<int>(draw.MainNumbers);
                        int mainMatches = nums.Take(5).Count(n => mainSet.Contains(n));
                        bool megaMatch  = nums.Length == 6 && draw.MegaNumber > 0 && nums[5] == draw.MegaNumber;
                        string prize    = SLPrize(mainMatches, megaMatch, draw.Prizes ?? Array.Empty<DrawPrizeTier>());
                        if (string.IsNullOrEmpty(prize)) continue;
                        string ml  = megaMatch ? $"{mainMatches}+M" : $"{mainMatches}/5";
                        string nds = string.Join("  ", nums.Take(5).Select(n => n.ToString("D2"))) + "  |M:" + nums[5].ToString("D2");
                        result.Winners.Add(new WinnerEntry
                        {
                            Game = "SL", SetNumber = slot + 1, RowNumber = r + 1,
                            Numbers = nds, MatchLabel = ml, Prize = prize, DrawDate = draw.DrawDate ?? "",
                            PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                            PlayToDate   = PlayToStr(advDates[r], draw.Date, draw.DrawNumber, 3.5),
                            DrawEnd      = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                            MatchDrawNum = draw.DrawNumber
                        });
                    }
                }
            }
        }

        // ── PB ──────────────────────────────────────────────────────────────────
        var pbPast = _pb
            .Select(d => (Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                          d.DrawDate, d.DrawNumber, d.MainNumbers, d.PBNumber, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date && d.Date.Date >= cutoff)
            .OrderByDescending(d => d.Date)
            .ToList();

        if (pbPast.Count > 0)
        {
            foreach (var (slot, rows) in ReadSets("pb", 6))
            {
                var advDates = ReadAdvanceDates("pb", slot);
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r])) continue;
                    if (!advDates[r].start.HasValue && !advDates[r].end.HasValue && advDates[r].drawStart == 0) continue;
                    // Skip expired tickets (end date already passed)
                    if (advDates[r].end.HasValue && advDates[r].end.Value.Date < DateTime.Today) continue;
                    if (!advDates[r].end.HasValue && advDates[r].start.HasValue && advDates[r].start.Value.Date < DateTime.Today) continue;
                    var nums = rows[r].Select(int.Parse).ToArray();
                    foreach (var draw in pbPast)
                    {
                        if (!IsRowValidForDate(advDates[r].start, advDates[r].end, draw.Date)) continue;
                        if (result.Winners.Any(w => w.Game == "PB" && w.SetNumber == slot + 1 && w.RowNumber == r + 1 && w.DrawDate == draw.DrawDate && !w.IsActiveNoWin)) continue;
                        var mainSet     = new HashSet<int>(draw.MainNumbers);
                        int mainMatches = nums.Take(5).Count(n => mainSet.Contains(n));
                        bool pbMatch    = nums.Length == 6 && draw.PBNumber > 0 && nums[5] == draw.PBNumber;
                        string prize    = PBPrize(mainMatches, pbMatch, draw.Prizes ?? Array.Empty<DrawPrizeTier>());
                        if (string.IsNullOrEmpty(prize)) continue;
                        string ml  = pbMatch ? $"{mainMatches}+PB" : $"{mainMatches}/5";
                        string nds = string.Join("  ", nums.Take(5).Select(n => n.ToString("D2"))) + "  |PB:" + nums[5].ToString("D2");
                        result.Winners.Add(new WinnerEntry
                        {
                            Game = "PB", SetNumber = slot + 1, RowNumber = r + 1,
                            Numbers = nds, MatchLabel = ml, Prize = prize, DrawDate = draw.DrawDate ?? "",
                            PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                            PlayToDate   = PlayToStr(advDates[r], draw.Date, draw.DrawNumber, 2.5),
                            DrawEnd      = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                            MatchDrawNum = draw.DrawNumber
                        });
                    }
                }
            }
        }

        // ── MM ──────────────────────────────────────────────────────────────────
        var mmPast = _mm
            .Select(d => (Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                          d.DrawDate, d.DrawNumber, d.MainNumbers, d.MegaNumber, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date && d.Date.Date >= cutoff)
            .OrderByDescending(d => d.Date)
            .ToList();

        if (mmPast.Count > 0)
        {
            foreach (var (slot, rows) in ReadSets("mm", 6))
            {
                var advDates = ReadAdvanceDates("mm", slot);
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r])) continue;
                    if (!advDates[r].start.HasValue && !advDates[r].end.HasValue && advDates[r].drawStart == 0) continue;
                    // Skip expired tickets (end date already passed)
                    if (advDates[r].end.HasValue && advDates[r].end.Value.Date < DateTime.Today) continue;
                    if (!advDates[r].end.HasValue && advDates[r].start.HasValue && advDates[r].start.Value.Date < DateTime.Today) continue;
                    var nums = rows[r].Select(int.Parse).ToArray();
                    foreach (var draw in mmPast)
                    {
                        if (!IsRowValidForDate(advDates[r].start, advDates[r].end, draw.Date)) continue;
                        if (result.Winners.Any(w => w.Game == "MM" && w.SetNumber == slot + 1 && w.RowNumber == r + 1 && w.DrawDate == draw.DrawDate && !w.IsActiveNoWin)) continue;
                        var mainSet     = new HashSet<int>(draw.MainNumbers);
                        int mainMatches = nums.Take(5).Count(n => mainSet.Contains(n));
                        bool mbMatch    = nums.Length == 6 && draw.MegaNumber > 0 && nums[5] == draw.MegaNumber;
                        string prize    = MMPrize(mainMatches, mbMatch, draw.Prizes ?? Array.Empty<DrawPrizeTier>());
                        if (string.IsNullOrEmpty(prize)) continue;
                        string ml  = mbMatch ? $"{mainMatches}+MB" : $"{mainMatches}/5";
                        string nds = string.Join("  ", nums.Take(5).Select(n => n.ToString("D2"))) + "  |MB:" + nums[5].ToString("D2");
                        result.Winners.Add(new WinnerEntry
                        {
                            Game = "MM", SetNumber = slot + 1, RowNumber = r + 1,
                            Numbers = nds, MatchLabel = ml, Prize = prize, DrawDate = draw.DrawDate ?? "",
                            PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                            PlayToDate   = PlayToStr(advDates[r], draw.Date, draw.DrawNumber, 3.5),
                            DrawEnd      = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                            MatchDrawNum = draw.DrawNumber
                        });
                    }
                }
            }
        }

        // ── D3 ──────────────────────────────────────────────────────────────────
        var d3PastGroups = _d3
            .GroupBy(d => d.DrawDate)
            .Select(g =>
            {
                var ordered = g.OrderBy(d => d.DrawNumber).ToList();
                var grpDate = DateTime.TryParse(g.Key, out var dt) ? dt : DateTime.MinValue;
                return (DateLabel: g.Key, Date: grpDate,
                    Midday:          ordered.Count >= 1 ? ordered[0].Numbers : null,
                    MiddayPrizes:    ordered.Count >= 1 ? ordered[0].Prizes  : Array.Empty<DrawPrizeTier>(),
                    MiddayDrawNum:   ordered.Count >= 1 ? ordered[0].DrawNumber : 0,
                    Evening:         ordered.Count >= 2 ? ordered[1].Numbers : null,
                    EveningPrizes:   ordered.Count >= 2 ? ordered[1].Prizes  : Array.Empty<DrawPrizeTier>(),
                    EveningDrawNum:  ordered.Count >= 2 ? ordered[1].DrawNumber : 0);
            })
            .Where(g => g.Date != DateTime.MinValue && g.Date.Date <= date.Date && g.Date.Date >= cutoff)
            .OrderByDescending(g => g.Date)
            .ToList();

        if (d3PastGroups.Count > 0)
        {
            foreach (var (slot, rows) in ReadSets("d3", 3))
            {
                var betTypes = ReadD3BetTypes(slot);
                var drawFilters = ReadD3DrawFilters(slot);
                var advDates = ReadAdvanceDates("d3", slot);
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r], 0)) continue;
                    if (!advDates[r].start.HasValue && !advDates[r].end.HasValue && advDates[r].drawStart == 0) continue;
                    // Skip expired tickets (end date already passed)
                    if (advDates[r].end.HasValue && advDates[r].end.Value.Date < DateTime.Today) continue;
                    if (!advDates[r].end.HasValue && advDates[r].start.HasValue && advDates[r].start.Value.Date < DateTime.Today) continue;
                    var userNums   = rows[r].Select(int.Parse).ToArray();
                    var sortedUser = userNums.OrderBy(x => x).ToArray();
                    string bt      = betTypes[r];
                    string df      = drawFilters[r];
                    string boxType = D3BoxType(userNums);
                    foreach (var draw in d3PastGroups)
                    {
                        bool dValid = IsRowValidForDate(advDates[r].start, advDates[r].end, draw.Date);
                        if (!dValid) continue;
                        if (result.Winners.Any(w => w.Game == "D3" && w.SetNumber == slot + 1 && w.RowNumber == r + 1 && w.DrawDate == draw.DateLabel && !w.IsActiveNoWin)) continue;
                        bool isDayStr = df != "E" && draw.Midday  != null && userNums.SequenceEqual(draw.Midday);
                        bool isDayBox = !isDayStr && df != "E" && draw.Midday  != null && sortedUser.SequenceEqual(draw.Midday.OrderBy(x => x));
                        bool isEveStr = df != "M" && draw.Evening != null && userNums.SequenceEqual(draw.Evening);
                        bool isEveBox = !isEveStr && df != "M" && draw.Evening != null && sortedUser.SequenceEqual(draw.Evening.OrderBy(x => x));
                        string? dayWin = null, eveWin = null;
                        if (bt == "S") { if (isDayStr) dayWin = "Straight"; if (isEveStr) eveWin = "Straight"; }
                        else if (bt == "B")
                        {
                            if ((isDayStr || isDayBox) && !string.IsNullOrEmpty(boxType)) dayWin = boxType;
                            if ((isEveStr || isEveBox) && !string.IsNullOrEmpty(boxType)) eveWin = boxType;
                        }
                        else
                        {
                            if (isDayStr) dayWin = "SB_Straight"; else if (isDayBox && !string.IsNullOrEmpty(boxType)) dayWin = "SB_Box";
                            if (isEveStr) eveWin = "SB_Straight"; else if (isEveBox && !string.IsNullOrEmpty(boxType)) eveWin = "SB_Box";
                        }
                        if (dayWin == null && eveWin == null) continue;
                        string ml, p;
                        if (dayWin != null && eveWin != null)
                        { ml = $"Md-{D3Label(dayWin)} Ev-{D3Label(eveWin)}"; p = $"{D3PrizeCalc(dayWin, boxType, draw.MiddayPrizes)} + {D3PrizeCalc(eveWin, boxType, draw.EveningPrizes)}"; }
                        else if (dayWin != null)
                        { ml = $"Md-{D3Label(dayWin)}"; p = D3PrizeCalc(dayWin, boxType, draw.MiddayPrizes); }
                        else
                        { ml = $"Ev-{D3Label(eveWin!)}"; p = D3PrizeCalc(eveWin!, boxType, draw.EveningPrizes); }
                        result.Winners.Add(new WinnerEntry
                        {
                            Game = "D3", SetNumber = slot + 1, RowNumber = r + 1,
                            Numbers = string.Join("-", userNums), MatchLabel = ml, Prize = p,
                            DrawDate = draw.DateLabel ?? "",
                            BetType = bt,
                            DrawFilter = df
                        });
                    }
                }
            }
        }

        // ── D4 ──────────────────────────────────────────────────────────────────
        var d4Past = _d4
            .Select(d => (Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                          d.DrawDate, d.DrawNumber, d.Numbers, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date && d.Date.Date >= cutoff)
            .OrderByDescending(d => d.Date)
            .ToList();

        if (d4Past.Count > 0)
        {
            foreach (var (slot, rows) in ReadSets("d4", 4))
            {
                var betTypes = ReadD4BetTypes(slot);
                var advDates = ReadAdvanceDates("d4", slot);
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r], 0)) continue;
                    if (!advDates[r].start.HasValue && !advDates[r].end.HasValue && advDates[r].drawStart == 0) continue;
                    // Skip expired tickets (end date already passed)
                    if (advDates[r].end.HasValue && advDates[r].end.Value.Date < DateTime.Today) continue;
                    if (!advDates[r].end.HasValue && advDates[r].start.HasValue && advDates[r].start.Value.Date < DateTime.Today) continue;
                    var userNums   = rows[r].Select(int.Parse).ToArray();
                    var sortedUser = userNums.OrderBy(x => x).ToArray();
                    string bt = betTypes[r];
                    string boxType = D4BoxType(userNums);
                    foreach (var draw in d4Past)
                    {
                        if (!IsRowValidForDate(advDates[r].start, advDates[r].end, draw.Date)) continue;
                        if (result.Winners.Any(w => w.Game == "D4" && w.SetNumber == slot + 1 && w.RowNumber == r + 1 && w.DrawDate == draw.DrawDate && !w.IsActiveNoWin)) continue;
                        var sortedWin = draw.Numbers.OrderBy(x => x).ToArray();
                        bool isStr = userNums.SequenceEqual(draw.Numbers);
                        bool isBox = !isStr && sortedUser.SequenceEqual(sortedWin);
                        string? wt = null;
                        if (bt == "S") { if (isStr) wt = "Straight"; }
                        else if (bt == "B") { if ((isStr || isBox) && !string.IsNullOrEmpty(boxType)) wt = boxType; }
                        else { if (isStr) wt = "SB_Straight"; else if (isBox && !string.IsNullOrEmpty(boxType)) wt = boxType; }
                        if (wt == null) continue;
                        string ml = wt == "SB_Straight" ? "S&B!" : wt == "Straight" ? "Straight" : "Box";
                        string p  = D4PrizeCalc(wt, bt, draw.Prizes ?? Array.Empty<DrawPrizeTier>());
                        result.Winners.Add(new WinnerEntry
                        {
                            Game = "D4", SetNumber = slot + 1, RowNumber = r + 1,
                            Numbers = string.Join("-", userNums), MatchLabel = ml, Prize = p,
                            DrawDate = draw.DrawDate ?? "",
                            BetType = bt,
                            PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                            PlayToDate   = PlayToStr(advDates[r], draw.Date, draw.DrawNumber, 1.0),
                            DrawEnd      = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                            MatchDrawNum = draw.DrawNumber
                        });
                    }
                }
            }
        }

        // ── DD ──────────────────────────────────────────────────────────────────
        var ddPast = _dd
            .Select(d => (Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                          d.DrawDate, d.DrawNumber, d.Horses, d.RaceTime, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date && d.Date.Date >= cutoff)
            .OrderByDescending(d => d.Date)
            .ToList();

        if (ddPast.Count > 0)
        {
            foreach (var (slot, rows) in ReadDDSets())
            {
                var advDates = ReadAdvanceDates("dd", slot);
                for (int r = 0; r < Rows; r++)
                {
                    var (h1, h2, h3, userTime) = rows[r];
                    bool horsesSet = h1 > 0 && h2 > 0 && h3 > 0;
                    if (!advDates[r].start.HasValue && !advDates[r].end.HasValue && advDates[r].drawStart == 0) continue;
                    // Skip expired tickets (end date already passed)
                    if (advDates[r].end.HasValue && advDates[r].end.Value.Date < DateTime.Today) continue;
                    if (!advDates[r].end.HasValue && advDates[r].start.HasValue && advDates[r].start.Value.Date < DateTime.Today) continue;
                    string userTimeDigits = new string(userTime.Where(char.IsDigit).ToArray());
                    bool timeEntered = userTimeDigits.Length == 3;
                    if (!horsesSet && !timeEntered) continue;
                    string numsDisplay = horsesSet ? $"{h1}-{h2}-{h3}" + (timeEntered ? $"  ⏱{userTime}" : "") : $"⏱{userTime}";
                    foreach (var draw in ddPast)
                    {
                        if (!IsRowValidForDate(advDates[r].start, advDates[r].end, draw.Date)) continue;
                        if (result.Winners.Any(w => w.Game == "DD" && w.SetNumber == slot + 1 && w.RowNumber == r + 1 && w.DrawDate == draw.DrawDate && !w.IsActiveNoWin)) continue;
                        string winTimeNorm = NormalizeTime(draw.RaceTime ?? "");
                        string winTimeLast3 = winTimeNorm.Length >= 3 ? winTimeNorm[^3..] : winTimeNorm;
                        bool timeMatch = timeEntered && !string.IsNullOrEmpty(winTimeLast3) && userTimeDigits == winTimeLast3;
                        string? wt = null;
                        if (horsesSet)
                        {
                            bool m1 = h1 == draw.Horses[0]; bool m2 = h2 == draw.Horses[1]; bool m3 = h3 == draw.Horses[2];
                            if      (m1 && m2 && m3 && timeMatch) wt = "GRAND!";
                            else if (m1 && m2 && timeMatch)       wt = "Exa+Time";
                            else if (m1 && m2)                    wt = "Exacta";
                            else if (m1 && timeMatch)             wt = "Win+Time";
                            else if (m1)                          wt = "Win";
                            else if (timeMatch)                   wt = "Time";
                        }
                        else if (timeMatch) wt = "Time";
                        if (wt == null) continue;
                        string p = wt switch
                        {
                            "Win+Time" => DDComboPrize("Win",    "Time", draw.Prizes ?? Array.Empty<DrawPrizeTier>()),
                            "Exa+Time" => DDComboPrize("Exacta", "Time", draw.Prizes ?? Array.Empty<DrawPrizeTier>()),
                            _          => DDPrize(wt, draw.Prizes ?? Array.Empty<DrawPrizeTier>())
                        };
                        result.Winners.Add(new WinnerEntry
                        {
                            Game = "DD", SetNumber = slot + 1, RowNumber = r + 1,
                            Numbers = numsDisplay, MatchLabel = wt, Prize = p,
                            DrawDate = draw.DrawDate ?? "",
                            PlayFromDate = advDates[r].start?.ToString("MM/dd") ?? "",
                            PlayToDate   = PlayToStr(advDates[r], draw.Date, draw.DrawNumber, 1.0),
                            DrawEnd      = advDates[r].drawEnd > 0 ? advDates[r].drawEnd : (advDates[r].drawStart > 0 ? advDates[r].drawStart : 0),
                            MatchDrawNum = draw.DrawNumber
                        });
                    }
                }
            }
        }
    }

    static bool RowAllFilled(string[] row, int minValue = 1) =>
        row.All(v => !string.IsNullOrWhiteSpace(v) && int.TryParse(v, out int n) && n >= minValue);

    // Approximate calendar end date for a draw#-only advance ticket.
    // daysPerDraw: F5≈1, SL≈3.5, PB≈2.5, MM≈3.5, D3≈0.5, D4≈1, DD≈1
    static string ApproxEndDateStr(DateTime drawDate, int drawNum, int targetDrawNum, double daysPerDraw)
    {
        if (targetDrawNum <= 0 || drawNum <= 0 || drawDate == default) return "";
        int diff = targetDrawNum - drawNum;
        if (diff < 0) return "";
        return drawDate.AddDays(Math.Round(diff * daysPerDraw)).ToString("MM/dd");
    }

    static string PlayToStr((DateTime? start, DateTime? end, int drawStart, int drawEnd) adv,
        DateTime drawDate, int drawNum, double daysPerDraw)
    {
        if (adv.end.HasValue) return adv.end.Value.ToString("MM/dd");
        int target = adv.drawEnd > 0 ? adv.drawEnd : adv.drawStart;
        return ApproxEndDateStr(drawDate, drawNum, target, daysPerDraw);
    }

    // Returns (playStart, playEnd, drawStart, drawEnd) per row for a given slot.
    // Format in prefs: "{prefix}_adv_{slot}" = "yyyyMMdd~yyyyMMdd~drawStart~drawEnd|..." one entry per row.
    static (DateTime? start, DateTime? end, int drawStart, int drawEnd)[] ReadAdvanceDates(string prefix, int slot)
    {
        var result = new (DateTime?, DateTime?, int, int)[Rows];
        string raw = Preferences.Get($"{prefix}_adv_{slot}", "");
        if (string.IsNullOrEmpty(raw)) return result;
        var parts = raw.Split('|');
        for (int r = 0; r < Rows && r < parts.Length; r++)
        {
            var pair = parts[r].Split('~');
            if (pair.Length < 2) continue;
            DateTime? start = null, end = null;
            if (DateTime.TryParseExact(pair[0], "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var sd)) start = sd;
            if (DateTime.TryParseExact(pair[1], "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var ed)) end = ed;
            int drawStart = pair.Length > 2 && int.TryParse(pair[2], out int ds) ? ds : 0;
            int drawEnd   = pair.Length > 3 && int.TryParse(pair[3], out int de) ? de : 0;
            result[r] = (start, end, drawStart, drawEnd);
        }
        return result;
    }

    // No advance dates/draw# = regular ticket, always check against the fetched draw.
    // With date range = draw date must fall within [start, end].
    // With draw# range = draw number must fall within [drawStart, drawEnd] (drawEnd optional, defaults to drawStart).
    static bool IsRowValidForDate(DateTime? playStart, DateTime? playEnd, DateTime drawDate,
        int drawNum = 0, int drawStart = 0, int drawEnd = 0, DateTime? selectedDate = null)
    {
        bool hasDate    = playStart.HasValue || playEnd.HasValue;
        bool hasDrawNum = drawStart > 0;

        if (!hasDate && !hasDrawNum)
            return true; // Regular ticket — always check against the most recent draw fetched

        if (hasDate)
        {
            DateTime start = playStart?.Date ?? DateTime.MinValue;
            DateTime end   = playEnd?.Date   ?? DateTime.MaxValue;
            if (drawDate.Date < start || drawDate.Date > end) return false;
        }

        if (hasDrawNum && drawNum > 0)
        {
            int effEnd = drawEnd > 0 ? drawEnd : drawStart;
            if (drawNum < drawStart || drawNum > effEnd) return false;
        }

        return true;
    }

    static string D3Label(string winType) => winType switch
    {
        "Straight"    => "S",
        "SB_Straight" => "S/B",
        "SB_Box"      => "S/B",
        "Box3"        => "B",
        "Box6"        => "B",
        _ => winType
    };

    static string[] ReadD3BetTypes(int slot)
    {
        string[] valid = ["S", "B", "S&B"];
        var parts = Preferences.Get($"d3_btypes_{slot}", "").Split('|');
        var result = new string[Rows];
        for (int r = 0; r < Rows; r++)
        {
            var val = r < parts.Length ? parts[r] : "S";
            if (val == "S+B") val = "S&B";
            result[r] = valid.Contains(val) ? val : "S";
        }
        return result;
    }

    internal static string[] ReadD3DrawFilters(int slot)
    {
        string[] valid = ["B", "M", "E"];
        // Active slot uses "d3_drawfilters" (no suffix); named slots use "d3_drawfilters_{slot}"
        int activeSlot = Preferences.Get("d3_active_slot", -1);
        string raw = (slot == activeSlot || slot == 0 && activeSlot < 0)
            ? Preferences.Get("d3_drawfilters", "")
            : Preferences.Get($"d3_drawfilters_{slot}", "");
        var parts = raw.Split('|');
        var result = new string[Rows];
        for (int r = 0; r < Rows; r++)
        {
            var val = r < parts.Length ? parts[r] : "B";
            result[r] = valid.Contains(val) ? val : "B";
        }
        return result;
    }
}
