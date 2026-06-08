using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public class WinnerEntry
{
    public string Game      { get; set; } = "";
    public int    SetNumber { get; set; }
    public int    RowNumber { get; set; }
    public string Numbers   { get; set; } = "";
    public string MatchLabel{ get; set; } = "";
    public string Prize     { get; set; } = "";
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
    public int[]? D3Midday   { get; set; }
    public int[]? D3Evening  { get; set; }
    public int[]? D4Numbers  { get; set; }
    public int[]? DDHorses   { get; set; }
    public string DDRaceTime { get; set; } = "";

    public List<WinnerEntry> Winners { get; set; } = new();
    public string Error { get; set; } = "";
}

public static class ResultsPageCls
{
    const int Rows = 10;

    static List<(string DrawDate, int[] Numbers, DrawPrizeTier[] Prizes)>                          _f5 = new();
    static List<(string DrawDate, int[] MainNumbers, int MegaNumber, DrawPrizeTier[] Prizes)>      _sl = new();
    static List<(string DrawDate, int[] MainNumbers, int PBNumber,   DrawPrizeTier[] Prizes)>      _pb = new();
    static List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)>          _d3 = new();
    static List<(string DrawDate, int[] Numbers, DrawPrizeTier[] Prizes)>                          _d4 = new();
    static List<(string DrawDate, int[] Horses, string RaceTime, DrawPrizeTier[] Prizes)>          _dd = new();
    static bool _loaded = false;

    public static void ClearCache()
    {
        _f5.Clear(); _sl.Clear(); _pb.Clear(); _d3.Clear(); _d4.Clear(); _dd.Clear();
        _loaded = false;
    }

    public static async Task LoadAllDrawsAsync()
    {
        var f5Task = GetDataEntry.GetPastDraws(30);
        var slTask = GetDataEntry.GetSuperLottoDraws(30);
        var pbTask = GetDataEntry.GetPowerballDraws(30);
        var d3Task = GetDataEntry.GetDaily3Draws(50);
        var d4Task = GetDataEntry.GetDaily4Draws(50);
        var ddTask = GetDataEntry.GetDailyDerbyDraws(50);
        await Task.WhenAll(f5Task, slTask, pbTask, d3Task, d4Task, ddTask);
        _f5 = f5Task.Result;
        _sl = slTask.Result;
        _pb = pbTask.Result;
        _d3 = d3Task.Result;
        _d4 = d4Task.Result;
        _dd = ddTask.Result;
        _loaded = true;
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
        if (prizes.Length > 0)
        {
            int tier = 6 - matches;  // 5→1, 4→2, 3→3, 2→4
            var p = prizes.FirstOrDefault(x => x.Tier == tier);
            if (p != null)
            {
                if (p.Amount > 0) return FormatPrize(p.Amount);
                // Tier 1 jackpot — data in API shows 0 (pending or rolled)
                if (tier == 1) return "JACKPOT*";
                // Other tiers with 0 amount — rare
                return tier == 4 ? "Free Ticket" : "";
            }
        }
        // Fallback estimates — API did not return prize data
        return matches switch
        {
            5 => "JACKPOT*",
            4 => "~$525 est.",
            3 => "~$25 est.",
            2 => "Free Ticket",
            _ => ""
        };
    }

    static string SLPrize(int main, bool mega, DrawPrizeTier[] prizes)
    {
        if (prizes.Length > 0)
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
            if (tier > 0)
            {
                var p = prizes.FirstOrDefault(x => x.Tier == tier);
                if (p != null)
                    return p.Amount > 0 ? FormatPrize(p.Amount) : (tier == 1 ? "JACKPOT" : "Free Ticket");
            }
        }
        // Fallback estimates
        return (main, mega) switch
        {
            (5, true)  => "JACKPOT*",
            (5, false) => "~$20,000",
            (4, true)  => "~$1,500",
            (4, false) => "~$100",
            (3, true)  => "~$50",
            (3, false) => "~$10",
            (2, true)  => "~$9",
            (1, true)  => "~$1",
            (0, true)  => "~$1",
            _          => ""
        };
    }

    static string PBPrize(int main, bool pb, DrawPrizeTier[] prizes)
    {
        if (prizes.Length > 0)
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
            if (tier > 0)
            {
                var p = prizes.FirstOrDefault(x => x.Tier == tier);
                if (p != null)
                    return p.Amount > 0 ? FormatPrize(p.Amount) : (tier == 1 ? "JACKPOT" : "Free Ticket");
            }
        }
        return (main, pb) switch
        {
            (5, true)  => "JACKPOT*",
            (5, false) => "~$1,000,000",
            (4, true)  => "~$50,000",
            (4, false) => "~$100",
            (3, true)  => "~$100",
            (3, false) => "~$7",
            (2, true)  => "~$7",
            (1, true)  => "~$4",
            (0, true)  => "~$4",
            _          => ""
        };
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
        // Fallback estimates (pari-mutuel, vary by draw)
        return winType switch
        {
            "Straight"    => "~$428",
            "SB_Straight" => boxType == "Box3" ? "~$280" : "~$248",
            "SB_Box"      => boxType == "Box3" ? "~$49"  : "~$34",
            "Box3"        => "~$130",
            "Box6"        => "~$68",
            _ => ""
        };
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
        // Fallback estimates
        return (winType, betType) switch
        {
            ("Straight",    "S&B") => "~$1,688",
            ("Straight",    _    ) => "~$3,376",
            ("SB_Straight", _    ) => "~$1,755",
            ("Box24",       "S&B") => "~$67",
            ("Box24",       _    ) => "~$134",
            ("Box12",       "S&B") => "~$134",
            ("Box12",       _    ) => "~$268",
            ("Box6",        "S&B") => "~$268",
            ("Box6",        _    ) => "~$536",
            ("Box4",        "S&B") => "~$402",
            ("Box4",        _    ) => "~$804",
            _ => ""
        };
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
        return winType switch
        {
            "GRAND!" => 60000m,
            "Exacta" => 23m,
            "Time"   => 69m,
            "Win"    => 3m,
            _ => 0m
        };
    }

    static string DDPrize(string winType, DrawPrizeTier[] prizes)
    {
        decimal amt = DDPrizeAmount(winType, prizes);
        return amt > 0 ? $"${amt:N0}" : "";
    }

    // Combo prize = sum of two individual prizes as a single total
    static string DDComboPrize(string w1, string w2, DrawPrizeTier[] prizes)
    {
        decimal total = DDPrizeAmount(w1, prizes) + DDPrizeAmount(w2, prizes);
        return total > 0 ? $"${total:N0}" : "";
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

    // ── Read non-empty sets from Preferences ─────────────────────────────────

    static List<(int slot, string[][] rows)> ReadSets(string prefix, int cols)
    {
        var result = new List<(int, string[][])>();
        for (int s = 0; s < 10; s++)
        {
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
        return result;
    }

    // ── Read DD sets (4 values per row: h1, h2, h3, raceTime) ────────────────

    static List<(int slot, (int h1, int h2, int h3, string time)[] rows)> ReadDDSets()
    {
        var result = new List<(int, (int, int, int, string)[])>();
        for (int s = 0; s < 10; s++)
        {
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
                d.DrawDate, d.Numbers, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date)
            .OrderByDescending(d => d.Date)
            .FirstOrDefault();

        if (f5Draw.Numbers != null)
        {
            result.F5Numbers = f5Draw.Numbers;
            result.DateLabel  = f5Draw.DrawDate;
        }

        // ── Find SL draw on or before selected date ──────────────────────────
        var slDraw = _sl
            .Select(d => (
                Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                d.DrawDate, d.MainNumbers, d.MegaNumber, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date)
            .OrderByDescending(d => d.Date)
            .FirstOrDefault();

        if (slDraw.MainNumbers != null)
        {
            result.SLMain = slDraw.MainNumbers;
            result.SLMega = slDraw.MegaNumber;
            if (string.IsNullOrEmpty(result.DateLabel))
                result.DateLabel = slDraw.DrawDate;
        }

        // ── Find PB draw on or before selected date ──────────────────────────
        var pbDraw = _pb
            .Select(d => (
                Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue,
                d.DrawDate, d.MainNumbers, d.PBNumber, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date)
            .OrderByDescending(d => d.Date)
            .FirstOrDefault();

        if (pbDraw.MainNumbers != null)
        {
            result.PBMain = pbDraw.MainNumbers;
            result.PBBall = pbDraw.PBNumber;
            if (string.IsNullOrEmpty(result.DateLabel))
                result.DateLabel = pbDraw.DrawDate;
        }

        // ── Find D3 draws on or before selected date ─────────────────────────
        var d3Groups = _d3
            .GroupBy(d => d.DrawDate)
            .Select(g =>
            {
                var ordered = g.OrderBy(d => d.DrawNumber).ToList();
                var grpDate = DateTime.TryParse(g.Key, out var dt) ? dt : DateTime.MinValue;
                return (DateLabel: g.Key, Date: grpDate,
                    Midday:         ordered.Count >= 1 ? ordered[0].Numbers : null,
                    MiddayPrizes:   ordered.Count >= 1 ? ordered[0].Prizes  : Array.Empty<DrawPrizeTier>(),
                    Evening:        ordered.Count >= 2 ? ordered[1].Numbers : null,
                    EveningPrizes:  ordered.Count >= 2 ? ordered[1].Prizes  : Array.Empty<DrawPrizeTier>());
            })
            .Where(g => g.Date != DateTime.MinValue && g.Date.Date <= date.Date)
            .OrderByDescending(g => g.Date)
            .ToList();

        var d3MiddayGroup  = d3Groups.FirstOrDefault(g => g.Midday  != null);
        var d3EveningGroup = d3Groups.FirstOrDefault(g => g.Evening != null);
        result.D3Midday  = d3MiddayGroup.Midday;
        result.D3Evening = d3EveningGroup.Evening;
        var d3MiddayPrizes  = d3MiddayGroup.MiddayPrizes  ?? Array.Empty<DrawPrizeTier>();
        var d3EveningPrizes = d3EveningGroup.EveningPrizes ?? Array.Empty<DrawPrizeTier>();

        // ── Find D4 draw on or before selected date ──────────────────────────
        var d4Draw = _d4
            .Select(d => (Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue, d.DrawDate, d.Numbers, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date)
            .OrderByDescending(d => d.Date)
            .FirstOrDefault();

        result.D4Numbers = d4Draw.Numbers;
        var d4Prizes = d4Draw.Prizes ?? Array.Empty<DrawPrizeTier>();

        // ── Find DD draw on or before selected date ──────────────────────────
        var ddDraw = _dd
            .Select(d => (Date: DateTime.TryParse(d.DrawDate, out var dt) ? dt : DateTime.MinValue, d.DrawDate, d.Horses, d.RaceTime, d.Prizes))
            .Where(d => d.Date != DateTime.MinValue && d.Date.Date <= date.Date)
            .OrderByDescending(d => d.Date)
            .FirstOrDefault();

        result.DDHorses   = ddDraw.Horses;
        result.DDRaceTime = ddDraw.RaceTime ?? "";
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
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r])) continue;
                    var nums    = rows[r].Select(int.Parse).ToArray();
                    int matches = nums.Count(n => winSet.Contains(n));
                    string prize = F5Prize(matches, prizes);
                    if (string.IsNullOrEmpty(prize)) continue;

                    result.Winners.Add(new WinnerEntry
                    {
                        Game       = "F5",
                        SetNumber  = slot + 1,
                        RowNumber  = r + 1,
                        Numbers    = string.Join("  ", nums.Select(n => n.ToString("D2"))),
                        MatchLabel = $"{matches}/5",
                        Prize      = prize
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
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r])) continue;
                    var nums        = rows[r].Select(int.Parse).ToArray();
                    int mainMatches = nums.Take(5).Count(n => mainSet.Contains(n));
                    bool megaMatch  = nums.Length == 6 && result.SLMega > 0 && nums[5] == result.SLMega;

                    string prize = SLPrize(mainMatches, megaMatch, prizes);
                    if (string.IsNullOrEmpty(prize)) continue;

                    string matchLabel  = megaMatch ? $"{mainMatches}+M" : $"{mainMatches}/5";
                    string numsDisplay = string.Join("  ", nums.Take(5).Select(n => n.ToString("D2")))
                                        + "  |M:" + nums[5].ToString("D2");

                    result.Winners.Add(new WinnerEntry
                    {
                        Game       = "SL",
                        SetNumber  = slot + 1,
                        RowNumber  = r + 1,
                        Numbers    = numsDisplay,
                        MatchLabel = matchLabel,
                        Prize      = prize
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
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r])) continue;
                    var nums        = rows[r].Select(int.Parse).ToArray();
                    int mainMatches = nums.Take(5).Count(n => mainSet.Contains(n));
                    bool pbMatch    = nums.Length == 6 && result.PBBall > 0 && nums[5] == result.PBBall;

                    string prize = PBPrize(mainMatches, pbMatch, prizes);
                    if (string.IsNullOrEmpty(prize)) continue;

                    string matchLabel  = pbMatch ? $"{mainMatches}+PB" : $"{mainMatches}/5";
                    string numsDisplay = string.Join("  ", nums.Take(5).Select(n => n.ToString("D2")))
                                        + "  |PB:" + nums[5].ToString("D2");

                    result.Winners.Add(new WinnerEntry
                    {
                        Game       = "PB",
                        SetNumber  = slot + 1,
                        RowNumber  = r + 1,
                        Numbers    = numsDisplay,
                        MatchLabel = matchLabel,
                        Prize      = prize
                    });
                }
            }
        }

        // ── Check D3 sets ────────────────────────────────────────────────────
        if (result.D3Midday != null || result.D3Evening != null)
        {
            foreach (var (slot, rows) in ReadSets("d3", 3))
            {
                var betTypes = ReadD3BetTypes(slot);
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r])) continue;
                    var userNums   = rows[r].Select(int.Parse).ToArray();
                    var sortedUser = userNums.OrderBy(x => x).ToArray();
                    string bt      = betTypes[r]; // "S", "B", or "S&B"
                    string boxType = D3BoxType(userNums); // "Box3", "Box6", or "" (triple)

                    // Raw draw matches
                    bool isDayStr = result.D3Midday  != null && userNums.SequenceEqual(result.D3Midday);
                    bool isDayBox = !isDayStr && result.D3Midday  != null &&
                                   sortedUser.SequenceEqual(result.D3Midday.OrderBy(x => x));
                    bool isEveStr = result.D3Evening != null && userNums.SequenceEqual(result.D3Evening);
                    bool isEveBox = !isEveStr && result.D3Evening != null &&
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

                    if (dayWin == null && eveWin == null) continue;

                    string matchLabel, prize;
                    if (dayWin != null && eveWin != null)
                    {
                        matchLabel = $"D:{D3Label(dayWin)} N:{D3Label(eveWin)}";
                        prize      = $"{D3PrizeCalc(dayWin, boxType, d3MiddayPrizes)} + {D3PrizeCalc(eveWin, boxType, d3EveningPrizes)}";
                    }
                    else if (dayWin != null)
                    {
                        matchLabel = $"Day {D3Label(dayWin)}";
                        prize      = D3PrizeCalc(dayWin, boxType, d3MiddayPrizes);
                    }
                    else
                    {
                        matchLabel = $"Eve {D3Label(eveWin!)}";
                        prize      = D3PrizeCalc(eveWin!, boxType, d3EveningPrizes);
                    }

                    result.Winners.Add(new WinnerEntry
                    {
                        Game       = "D3",
                        SetNumber  = slot + 1,
                        RowNumber  = r + 1,
                        Numbers    = string.Join("-", userNums),
                        MatchLabel = matchLabel,
                        Prize      = prize
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
                for (int r = 0; r < Rows; r++)
                {
                    if (!RowAllFilled(rows[r])) continue;
                    var userNums   = rows[r].Select(int.Parse).ToArray();
                    var sortedUser = userNums.OrderBy(x => x).ToArray();
                    string bt      = betTypes[r];
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

                    if (winType == null) continue;

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
                        Prize      = prize
                    });
                }
            }
        }

        // ── Check DD sets ────────────────────────────────────────────────────
        if (result.DDHorses != null && result.DDHorses.Length == 3)
        {
            string winTimeNorm = NormalizeTime(result.DDRaceTime);
            string winTimeLast3 = winTimeNorm.Length >= 3 ? winTimeNorm[^3..] : winTimeNorm;
            foreach (var (slot, rows) in ReadDDSets())
            {
                for (int r = 0; r < Rows; r++)
                {
                    var (h1, h2, h3, userTime) = rows[r];
                    bool horsesSet = h1 > 0 && h2 > 0 && h3 > 0;
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

                    if (winType == null) continue;

                    string numsDisplay = horsesSet
                        ? $"{h1}-{h2}-{h3}" + (timeEntered ? $"  ⏱{userTime}" : "")
                        : $"⏱{userTime}";

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
                        Numbers    = numsDisplay,
                        MatchLabel = matchLabel,
                        Prize      = prize
                    });
                }
            }
        }

        return result;
    }

    static bool RowAllFilled(string[] row) =>
        row.All(v => !string.IsNullOrWhiteSpace(v) && int.TryParse(v, out _));

    static string D3Label(string winType) => winType switch
    {
        "Straight"    => "Str",
        "SB_Straight" => "S&B!",
        "Box3"        => "Box3",
        "Box6"        => "Box6",
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
}
