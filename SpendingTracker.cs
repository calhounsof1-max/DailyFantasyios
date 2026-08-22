using System.Text.Json;
using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

// ── Model ─────────────────────────────────────────────────────────────────────

public class SpendingRecord
{
    public string  Game        { get; set; } = "";   // "F5", "SL", "PB", etc.
    public string  Date        { get; set; } = "";   // "yyyy-MM-dd"
    public int     TicketCount { get; set; }          // rows/tickets played
    public decimal CostEach    { get; set; }          // stored so manual edits keep original price
    public string  Note        { get; set; } = "";

    // Computed — not stored in JSON
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal TotalCost => TicketCount * CostEach;
}

// ── Tracker ───────────────────────────────────────────────────────────────────

public static class SpendingTracker
{
    // Ticket prices per game key
    // Edit these here if prices ever change
    static readonly Dictionary<string, decimal> TicketPrices = new()
    {
        ["F5"] = 1m,   // Fantasy 5
        ["SL"] = 1m,   // Super Lotto
        ["PB"] = 2m,   // Powerball
        ["MM"] = 5m,   // Mega Millions
        ["D3"] = 1m,   // Daily 3
        ["D4"] = 1m,   // Daily 4
        ["DD"] = 2m,   // Daily Derby ($2 per play per calottery.com)
    };

    static string DataPath =>
        Path.Combine(FileSystem.AppDataDirectory, "spending_log.json");

    public static decimal TicketCost(string gameKey) =>
        TicketPrices.TryGetValue(gameKey, out var p) ? p : 1m;

    public static async Task<List<SpendingRecord>> LoadAllAsync()
    {
        try
        {
            if (!File.Exists(DataPath)) return new();
            string json = await File.ReadAllTextAsync(DataPath);
            return JsonSerializer.Deserialize<List<SpendingRecord>>(json) ?? new();
        }
        catch { return new(); }
    }

    public static async Task SaveAllAsync(List<SpendingRecord> records)
    {
        try
        {
            string json = JsonSerializer.Serialize(records,
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(DataPath, json);
        }
        catch { }
    }

    /// <summary>
    /// Auto-computes today's spending records the same way the Spending Log page's
    /// "Log Today" button does (every field there is already auto-filled — nothing
    /// requires manual judgment except Scratchers price, which is skipped here since
    /// Scratchers entries are written directly by ScatchersPage at entry time).
    /// Lets pages like the Ticket Calendar reflect today's tickets without requiring
    /// the user to separately visit Spending Log and tap "Log Today" first.
    /// Only replaces today's previously auto-computed rows (Note "auto"/"M"/"E") —
    /// never touches manually-added or other-day records.
    /// </summary>
    public static async Task AutoSyncTodayAsync()
    {
        try
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            var toAdd = new List<SpendingRecord>();

            foreach (var key in new[] { "F5", "SL", "PB", "MM", "D4", "DD" })
            {
                int n = CountAllSlotsTicketsToday(key);
                if (key == "F5")
                    n = Math.Max(0, n - CountF5FreePlayRowsToday());
                if (n > 0)
                    toAdd.Add(new SpendingRecord { Game = key, Date = today, TicketCount = n, CostEach = TicketCost(key), Note = "auto" });
            }

            var (d3Mid, d3Eve, _) = CountD3AllSlotsTodayByFilter();
            if (d3Mid > 0) toAdd.Add(new SpendingRecord { Game = "D3", Date = today, TicketCount = d3Mid, CostEach = 1m, Note = "M" });
            if (d3Eve > 0) toAdd.Add(new SpendingRecord { Game = "D3", Date = today, TicketCount = d3Eve, CostEach = 1m, Note = "E" });

            decimal hsCost = SumHotSpotCostToday();
            if (hsCost > 0)
                // TicketCount MUST stay 1 here — TotalCost is TicketCount × CostEach (see
                // SpendingRecord above), and hsCost/CostEach is already the WHOLE DAY's total
                // across every Hot Spot ticket, not a single ticket's price. Setting this to
                // the real ticket count (tried earlier tonight, reverted) silently doubled
                // (or worse) every Hot Spot total — confirmed live 2026-08-09 as $30 becoming
                // $60 with 2 real tickets. If a real per-day Hot Spot ticket count is ever
                // needed elsewhere (e.g. Ticket Calendar), use CountHotSpotTicketsToday()
                // directly there — don't repurpose this field again.
                toAdd.Add(new SpendingRecord { Game = "HS", Date = today, TicketCount = 1, CostEach = hsCost, Note = "auto" });

            var records = await LoadAllAsync();
            records.RemoveAll(r => r.Date == today && (r.Note == "auto" || r.Note == "M" || r.Note == "E"));
            records.AddRange(toAdd);
            await SaveAllAsync(records);
        }
        catch { }
    }

    // Hot Spot: 10-slot system (see HotSpotPage.cs — was a single active ticket before
    // 2026-08-09). Each ticket's cost varies (wager × draws × Bulls-eye multiplier) instead
    // of the "N tickets × one fixed price" model every other game uses via TicketPrices/
    // TicketCost, so it can't go through the generic per-game loop in AutoSyncTodayAsync
    // above — sums across whichever of the 10 slots were purchased today.
    public static int CountHotSpotTicketsToday()
    {
        string today = DateTime.Today.ToString("yyyyMMdd");
        int count = 0;
        for (int s = 0; s < HotSpotPage.SlotCount; s++)
        {
            if (Preferences.Get($"hs_purchased_date_{s}", "") != today) continue;
            // A slot can end up "purchased today" but with blank numbers if Clear was tapped
            // on an already-saved ticket and the empty selection got auto-persisted back over
            // it on the next slot switch/page exit (PersistCurrentSlotRaw in HotSpotPage.cs
            // never touches PurchasedDate) — skip it, same guard TicketLogService already uses.
            if (string.IsNullOrWhiteSpace(Preferences.Get($"hs_numbers_{s}", ""))) continue;
            count++;
        }
        return count;
    }

    public static decimal SumHotSpotCostToday()
    {
        string today = DateTime.Today.ToString("yyyyMMdd");
        decimal total = 0m;
        for (int s = 0; s < HotSpotPage.SlotCount; s++)
        {
            if (Preferences.Get($"hs_purchased_date_{s}", "") != today) continue;
            if (string.IsNullOrWhiteSpace(Preferences.Get($"hs_numbers_{s}", ""))) continue; // see CountHotSpotTicketsToday
            decimal wager = (decimal)Preferences.Get($"hs_wager_{s}", 1.0);
            int draws     = Preferences.Get($"hs_draws_{s}", 1);
            bool bullseye = Preferences.Get($"hs_bullseye_{s}", false);
            total += wager * draws * (bullseye ? 2 : 1);
        }
        return total;
    }

    // Same free-play-row count "Log Today" uses for F5, across all slots.
    static int CountF5FreePlayRowsToday()
    {
        int activeSlot = Preferences.Get("f5_active_slot", -1);
        int fpCount = 0;
        for (int s = 0; s < 10; s++)
        {
            string entries = s == activeSlot
                ? Preferences.Get("f5_entries", "")
                : Preferences.Get($"f5_set_{s}", "");
            if (string.IsNullOrEmpty(entries)) continue;
            string fpRaw = Preferences.Get($"f5_freeplay_{s}", "");
            string adv   = Preferences.Get($"f5_adv_{s}", "");
            fpCount += WinnerPage.CountFreePlayRowsToday(entries, fpRaw, adv);
        }
        return fpCount;
    }

    public static decimal TotalSpentForGame(IEnumerable<SpendingRecord> records, string gameKey) =>
        records.Where(r => r.Game == gameKey).Sum(r => r.TotalCost);

    public static decimal TotalSpentAll(IEnumerable<SpendingRecord> records) =>
        records.Sum(r => r.TotalCost);

    // ── Count active tickets from Preferences ─────────────────────────────────
    // Each game stores the ACTIVE slot in "{prefix}_entries" (always up-to-date).
    // Named slots are in "{prefix}_set_N" for N != active slot.
    // Advance-play rows are multiplied by their draw count (draw# range or date range).
    // D3 with Both filter counts as 2 draws per day.

    public static int CountActiveTickets(string gameKey)
    {
        int stride = gameKey switch
        {
            "F5" => 5,
            "SL" => 6,
            "PB" => 6,
            "MM" => 6,
            "D3" => 3,
            "D4" => 4,
            "DD" => 4,
            _    => 0,
        };
        if (stride == 0) return 0;

        string prefix     = gameKey.ToLower();
        int    activeSlot = Preferences.Get($"{prefix}_active_slot", -1);

        string live        = Preferences.Get($"{prefix}_entries", "");
        string advDates    = activeSlot >= 0 ? Preferences.Get($"{prefix}_adv_{activeSlot}", "") : "";
        string drawFilters = gameKey == "D3" ? Preferences.Get("d3_drawfilters", "") : "";

        return CountRowsToday(live, advDates, drawFilters, stride, gameKey);
    }

    // A row only counts as a real ticket if EVERY number column is filled — matches
    // TicketLogService's "full" check. Checking only the first column let stray/partial
    // leftover rows (e.g. a D3 row with 2 of 3 digits entered) get counted as extra
    // tickets, inflating Verify/Log Today totals above what Ticket Log (and the receipt) show.
    static bool RowFilled(string[] vals, int idx, int stride)
    {
        if (idx + stride > vals.Length) return false;
        for (int c = 0; c < stride; c++)
            if (string.IsNullOrWhiteSpace(vals[idx + c])) return false;
        return true;
    }

    // Count filled rows, multiplying each by the number of draws it covers (used by debug).
    static int CountRowsAdvance(string entries, string advDates, string dfRaw, int stride, string gameKey)
    {
        if (string.IsNullOrEmpty(entries)) return 0;
        var vals    = entries.Split('|');
        var advRows = string.IsNullOrEmpty(advDates) ? Array.Empty<string>() : advDates.Split('|');
        var dfParts = string.IsNullOrEmpty(dfRaw)    ? Array.Empty<string>() : dfRaw.Split('|');

        int total = 0;
        for (int r = 0; r < 10; r++)
        {
            int idx = r * stride;
            if (!RowFilled(vals, idx, stride)) continue;
            total += PlaysForRow(r, advRows, dfParts, gameKey);
        }
        return total;
    }

    // Count filled rows for TODAY only: advance plays count only if today is within their range.
    static int CountRowsToday(string entries, string advDates, string dfRaw, int stride, string gameKey)
    {
        if (string.IsNullOrEmpty(entries)) return 0;
        var vals    = entries.Split('|');
        var advRows = string.IsNullOrEmpty(advDates) ? Array.Empty<string>() : advDates.Split('|');
        var dfParts = string.IsNullOrEmpty(dfRaw)    ? Array.Empty<string>() : dfRaw.Split('|');

        int total = 0;
        for (int r = 0; r < 10; r++)
        {
            int idx = r * stride;
            if (!RowFilled(vals, idx, stride)) continue;
            total += PlaysForRowToday(r, advRows, dfParts, gameKey);
        }
        return total;
    }

    // CA Lottery's own advance-play limits per game (draws, not days) — confirmed against
    // calottery.com's per-game pages on 2026-08-08. A Draw # range implying more draws than
    // this for a given game is never legitimate, regardless of the date range picked.
    static readonly Dictionary<string, int> AdvancePlayCaps = new()
    {
        ["F5"] = 12, // Fantasy 5: up to 12 consecutive draws
        ["SL"] = 20, // Super Lotto Plus: up to 20 consecutive draws (2,3,4,5,6,7,8,16,20)
        ["PB"] = 10, // Powerball: up to 10 consecutive draws (2,3,4,5,6,7,8,10)
        ["MM"] = 10, // Mega Millions: up to 10 consecutive draws (2,3,4,5,6,7,8,10)
        ["D3"] = 14, // Daily 3: up to 14 consecutive draws (2/day = 7 days)
        ["D4"] = 14, // Daily 4: up to 14 consecutive draws (1/day = 14 days)
        ["DD"] = 14, // Daily Derby: up to 14 consecutive draws
    };

    // Sanity-checks a Draw # range against the Play From/To date range, against CA Lottery's
    // own advance-play cap for the game (where known), and against the real live draw number
    // for this game, before it's saved. Catches fat-finger / stray-digit typos (e.g. "16556"
    // instead of "1656", or "2121333" instead of "21213") that would otherwise silently
    // inflate Log Today / Spending Log by thousands of tickets — this exact failure mode
    // logged a $29,812 Powerball entry for a single day (2026-08-08).
    // currentDrawNumber should come from DrawNumberService.GetNextDraw(gameName) — pass 0
    // if not available (e.g. live fetch hasn't completed) to skip that half of the check.
    // Returns null if the range looks plausible, otherwise a user-facing warning string.
    public static string? CheckDrawRangeWarning(
        string gameKey, DateTime from, DateTime to, string drawStartText, string drawEndText,
        string drawFilter = "B", int currentDrawNumber = 0)
    {
        if (!int.TryParse(drawStartText, out int ds) || ds <= 0)
            return null; // no draw # entered — nothing to check

        int de = ds;
        if (!string.IsNullOrWhiteSpace(drawEndText) && !int.TryParse(drawEndText, out de))
            return null; // non-numeric — let normal parsing handle it elsewhere
        if (string.IsNullOrWhiteSpace(drawEndText)) de = ds;

        if (de < ds)
            return $"⚠ End draw #{de} is before start draw #{ds}.";

        int implied = de - ds + 1;

        // Compare against the real current draw number — a range that's way beyond what
        // the actual lottery is currently drawing is almost certainly a mistyped digit,
        // regardless of what date range was picked.
        if (currentDrawNumber > 0)
        {
            int maxReasonable = currentDrawNumber + 200; // generous: months of future draws
            if (ds > maxReasonable || de > maxReasonable)
                return $"⚠ Draw #{(de > ds ? $"{ds}–{de}" : $"{ds}")} is far beyond the actual current draw number (#{currentDrawNumber}). Check for a typo before saving.";
        }

        // Hard cap from CA Lottery's own advance-play rules, independent of dates picked.
        if (AdvancePlayCaps.TryGetValue(gameKey, out int cap) && implied > cap)
            return $"⚠ Draw #{ds}–{de} is {implied} draws, but CA Lottery only allows advance play up to {cap} draws for this game. Check for a typo before saving.";

        int days = Math.Max(1, (to.Date - from.Date).Days + 1);
        int maxDrawsPerDay = gameKey == "D3" ? (drawFilter == "B" ? 2 : 1) : 1;
        int maxPlausible = days * maxDrawsPerDay;

        if (implied > maxPlausible)
            return $"⚠ Draw #{ds}–{de} implies {implied} tickets over {days} day(s) (max possible ~{maxPlausible}). Check for a typo before saving.";

        return null;
    }

    // Returns how many draws a single row covers (used by debug/advance total).
    static int PlaysForRow(int r, string[] advRows, string[] dfParts, string gameKey)
    {
        int baseDraws = gameKey == "D3" ? DrawsPerDayD3(r, dfParts) : 1;

        if (r >= advRows.Length || string.IsNullOrEmpty(advRows[r]))
            return baseDraws;

        var pair = advRows[r].Split('~');

        // Draw# range: exact count — each number in the range = one draw purchased
        string dsStr = pair.Length > 2 ? pair[2] : "";
        string deStr = pair.Length > 3 ? pair[3] : "";
        if (int.TryParse(dsStr, out int ds) && int.TryParse(deStr, out int de) && ds > 0 && de >= ds)
            return Math.Abs(de - ds) + 1;

        // D3: never fall back to date range — draw numbers must be used
        if (gameKey == "D3") return baseDraws;

        // Date range (non-D3 games only)
        if (pair.Length > 1 &&
            DateTime.TryParseExact(pair[0], "yyyyMMdd", null,
                System.Globalization.DateTimeStyles.None, out var start) &&
            DateTime.TryParseExact(pair[1], "yyyyMMdd", null,
                System.Globalization.DateTimeStyles.None, out var end) &&
            end >= start)
        {
            if (end < DateTime.Today) return 0;
            int days = (end - start).Days + 1;
            return days;
        }

        return baseDraws;
    }

    // Returns how many draws this row covers for "Log Today" (same-day purchases only).
    // Rules:
    //   • No advance dates → count (regular single-draw ticket for today)
    //   • Advance end date already past → skip (expired ticket from a previous period)
    //   • Advance start date = today → count (purchased today, advance period starts now)
    //   • Advance start date ≠ today → skip (purchased on a different day)
    static int PlaysForRowToday(int r, string[] advRows, string[] dfParts, string gameKey)
    {
        if (r < advRows.Length && !string.IsNullOrEmpty(advRows[r]) && advRows[r] != "~~~" && advRows[r] != "~")
        {
            var pair = advRows[r].Split('~');

            // If end date is set and already past, ticket is expired — don't count
            if (pair.Length >= 2 && !string.IsNullOrEmpty(pair[1])
                && DateTime.TryParseExact(pair[1], "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var ed)
                && ed.Date < DateTime.Today)
                return 0;

            // Only count if start date is today (purchased today)
            bool startIsToday = pair.Length >= 1
                && !string.IsNullOrEmpty(pair[0])
                && DateTime.TryParseExact(pair[0], "yyyyMMdd", null,
                    System.Globalization.DateTimeStyles.None, out var sd)
                && sd.Date == DateTime.Today;

            if (!startIsToday)
                return 0;

            // Advance play starting today — return total draw count (e.g. 12 for a 12-day advance)
            return PlaysForRow(r, advRows, dfParts, gameKey);
        }

        // No advance dates = regular single-draw ticket for today's draw
        return 1;
    }

    // Returns true if this game has an advance ticket, in any slot, that's still running
    // (end date >= today) but started on an earlier day — i.e. it was already logged once
    // on its actual purchase date and must NOT be re-entered in Log Today just because it's
    // still visible/filled-in on the game page. rangeLabel gives a human-readable "M/d–M/d"
    // for the first one found, for a UI warning.
    public static bool HasEarlierActiveAdvance(string gameKey, out string rangeLabel)
    {
        rangeLabel = "";
        int stride = gameKey switch
        {
            "F5" => 5, "SL" => 6, "PB" => 6, "MM" => 6,
            "D3" => 3, "D4" => 4, "DD" => 4, _ => 0,
        };
        if (stride == 0) return false;

        string prefix     = gameKey.ToLower();
        int    activeSlot = Preferences.Get($"{prefix}_active_slot", -1);

        for (int s = 0; s < 10; s++)
        {
            string entries = (s == activeSlot)
                ? Preferences.Get($"{prefix}_entries", "")
                : Preferences.Get($"{prefix}_set_{s}", "");
            if (string.IsNullOrEmpty(entries)) continue;

            string adv = Preferences.Get($"{prefix}_adv_{s}", "");
            if (string.IsNullOrEmpty(adv)) continue;

            var vals    = entries.Split('|');
            var advRows = adv.Split('|');

            for (int r = 0; r < 10 && r < advRows.Length; r++)
            {
                int idx = r * stride;
                if (!RowFilled(vals, idx, stride)) continue;

                string row = advRows[r];
                if (string.IsNullOrEmpty(row) || row == "~~~" || row == "~") continue;

                var pair = row.Split('~');
                if (pair.Length < 2 || string.IsNullOrEmpty(pair[0]) || string.IsNullOrEmpty(pair[1])) continue;
                if (!DateTime.TryParseExact(pair[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var sd)) continue;
                if (!DateTime.TryParseExact(pair[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var ed)) continue;

                if (ed.Date < DateTime.Today) continue;   // already expired — not currently active
                if (sd.Date >= DateTime.Today) continue;   // starts today or later — that's the normal counted case

                rangeLabel = $"{sd:M/d}–{ed:M/d}";
                return true;
            }
        }
        return false;
    }

    // D3 Both filter = 2 draws per day; Midday or Evening = 1.
    static int DrawsPerDayD3(int r, string[] dfParts)
    {
        string df = r < dfParts.Length ? (dfParts[r] ?? "B") : "B";
        return df == "B" ? 2 : 1;
    }

    static int CountRows(string raw, int stride)
    {
        if (string.IsNullOrEmpty(raw)) return 0;
        var parts = raw.Split('|');
        int count = 0;
        for (int r = 0; r < 10; r++)
        {
            int idx = r * stride;
            if (idx >= parts.Length) break;
            if (!string.IsNullOrWhiteSpace(parts[idx])) count++;
        }
        return count;
    }

    // Returns D3 today-counts split by draw filter: Midday / Evening / Both
    public static (int Midday, int Evening, int Both) CountD3TodayByFilter()
    {
        int    activeSlot  = Preferences.Get("d3_active_slot", -1);
        string live        = Preferences.Get("d3_entries", "");
        string advDates    = activeSlot >= 0 ? Preferences.Get($"d3_adv_{activeSlot}", "") : "";
        string drawFilters = Preferences.Get("d3_drawfilters", "");
        return CountD3RowsToday(live, advDates, drawFilters);
    }

    static (int Midday, int Evening, int Both) CountD3RowsToday(string entries, string advDates, string dfRaw, bool isActiveSlot = true)
    {
        const int stride = 3;
        if (string.IsNullOrEmpty(entries)) return (0, 0, 0);
        var vals    = entries.Split('|');
        var advRows = string.IsNullOrEmpty(advDates) ? Array.Empty<string>() : advDates.Split('|');
        var dfParts = string.IsNullOrEmpty(dfRaw)    ? Array.Empty<string>() : dfRaw.Split('|');
        int m = 0, e = 0, b = 0;
        for (int r = 0; r < 10; r++)
        {
            int idx = r * stride;
            if (!RowFilled(vals, idx, stride)) continue;

            if (r < advRows.Length && !string.IsNullOrEmpty(advRows[r])
                && advRows[r] != "~~~" && advRows[r] != "~")
            {
                var pair = advRows[r].Split('~');

                // If end date is past, ticket is expired — skip
                if (pair.Length >= 2 && !string.IsNullOrEmpty(pair[1])
                    && DateTime.TryParseExact(pair[1], "yyyyMMdd", null,
                        System.Globalization.DateTimeStyles.None, out var ed)
                    && ed.Date < DateTime.Today)
                    continue;

                bool startIsToday = pair.Length >= 1
                    && !string.IsNullOrEmpty(pair[0])
                    && DateTime.TryParseExact(pair[0], "yyyyMMdd", null,
                        System.Globalization.DateTimeStyles.None, out var sd)
                    && sd.Date == DateTime.Today;

                // Only count draws purchased TODAY — skip old advance tickets still in play
                if (!startIsToday) continue;

                // Advance play starting today — count all draws and split M/E
                int totalDraws = PlaysForRow(r, advRows, dfParts, "D3");
                string df2 = r < dfParts.Length ? (dfParts[r] ?? "B") : "B";
                if (df2 == "M")      m += totalDraws;
                else if (df2 == "E") e += totalDraws;
                else
                {
                    // B filter: split by draw# parity (even=Midday, odd=Evening)
                    // matches TicketLogService draw# parity logic
                    int drawStart = 0;
                    if (pair.Length >= 4 &&
                        int.TryParse(pair[2], out int ds2) && int.TryParse(pair[3], out int de2) &&
                        ds2 > 0 && de2 >= ds2)
                        drawStart = ds2;
                    int mDraws, eDraws;
                    if (drawStart > 0)
                    {
                        bool firstIsEven = drawStart % 2 == 0;
                        mDraws = firstIsEven ? (totalDraws + 1) / 2 : totalDraws / 2;
                        eDraws = totalDraws - mDraws;
                    }
                    else
                    {
                        mDraws = totalDraws / 2;
                        eDraws = totalDraws - mDraws;
                    }
                    m += mDraws;
                    e += eDraws;
                }
                continue;
            }

            // Non-advance ticket — only count for the active slot (tickets in other slots
            // were purchased on a different date and must not be counted as today).
            if (!isActiveSlot) continue;
            string df = r < dfParts.Length ? (dfParts[r] ?? "B") : "B";
            if (df == "M") m++;
            else if (df == "E") e++;
            else { m++; e++; }  // "Both" = 1 Midday + 1 Evening draw
        }
        return (m, e, b);
    }

    // Returns a summary of all games that have active tickets: gameKey -> count
    public static Dictionary<string, int> CountAllActiveTickets()
    {
        var result = new Dictionary<string, int>();
        foreach (var key in TicketPrices.Keys)
        {
            int n = CountActiveTickets(key);
            if (n > 0) result[key] = n;
        }
        return result;
    }

    // Returns true if the given game draws on the specified date.
    public static bool GameDrawsOnDate(string gameKey, DateTime date)
    {
        var dow = date.DayOfWeek;
        return gameKey switch
        {
            "PB" => dow == DayOfWeek.Monday || dow == DayOfWeek.Wednesday || dow == DayOfWeek.Saturday,
            "MM" => dow == DayOfWeek.Tuesday || dow == DayOfWeek.Friday,
            "SL" => dow == DayOfWeek.Wednesday || dow == DayOfWeek.Saturday,
            _    => true, // F5, D3, D4, DD draw daily
        };
    }

    // Returns true if the given game draws today.
    static bool GameDrawsToday(string gameKey) => GameDrawsOnDate(gameKey, DateTime.Today);

    // Scan ALL slots (not just active slot) for today's tickets — used by Log Today pre-fill.
    public static int CountAllSlotsTicketsToday(string gameKey)
    {
        // Don't count tickets for games that don't draw today
        if (!GameDrawsToday(gameKey)) return 0;

        int stride = gameKey switch
        {
            "F5" => 5, "SL" => 6, "PB" => 6, "MM" => 6,
            "D3" => 3, "D4" => 4, "DD" => 4, _ => 0,
        };
        if (stride == 0) return 0;
        string prefix     = gameKey.ToLower();
        int    activeSlot = Preferences.Get($"{prefix}_active_slot", -1);
        int    total      = 0;
        for (int s = 0; s < 10; s++)
        {
            // Live entries for active slot; saved entries for all others
            string entries = (s == activeSlot)
                ? Preferences.Get($"{prefix}_entries", "")
                : Preferences.Get($"{prefix}_set_{s}", "");
            if (string.IsNullOrEmpty(entries)) continue;
            string adv   = Preferences.Get($"{prefix}_adv_{s}", "");
            string dfRaw = gameKey == "D3"
                ? ((s == activeSlot)
                    ? Preferences.Get("d3_drawfilters", "")
                    : Preferences.Get($"d3_drawfilters_{s}", ""))
                : "";
            total += CountRowsToday(entries, adv, dfRaw, stride, gameKey);
        }
        return total;
    }

    // Human-readable breakdown of CountAllSlotsTicketsToday's total, e.g. "9 tickets + 1 advance
    // (5 draws) = 14" — shown on the Log Today screen so a multi-draw advance ticket's contribution
    // is visible at a glance instead of the total looking unexplained. Empty when every row today
    // is a plain 1-draw ticket (nothing extra to explain).
    public static string BuildTodayCountBreakdown(string gameKey)
    {
        if (!GameDrawsToday(gameKey)) return "";

        int stride = gameKey switch
        {
            "F5" => 5, "SL" => 6, "PB" => 6, "MM" => 6,
            "D3" => 3, "D4" => 4, "DD" => 4, _ => 0,
        };
        if (stride == 0) return "";

        string prefix     = gameKey.ToLower();
        int    activeSlot = Preferences.Get($"{prefix}_active_slot", -1);
        int    plainCount = 0;
        var    advanceDraws = new List<int>();

        for (int s = 0; s < 10; s++)
        {
            string entries = (s == activeSlot)
                ? Preferences.Get($"{prefix}_entries", "")
                : Preferences.Get($"{prefix}_set_{s}", "");
            if (string.IsNullOrEmpty(entries)) continue;
            string adv   = Preferences.Get($"{prefix}_adv_{s}", "");
            string dfRaw = gameKey == "D3"
                ? ((s == activeSlot)
                    ? Preferences.Get("d3_drawfilters", "")
                    : Preferences.Get($"d3_drawfilters_{s}", ""))
                : "";

            var vals    = entries.Split('|');
            var advRows = string.IsNullOrEmpty(adv)   ? Array.Empty<string>() : adv.Split('|');
            var dfParts = string.IsNullOrEmpty(dfRaw) ? Array.Empty<string>() : dfRaw.Split('|');

            for (int r = 0; r < 10; r++)
            {
                int idx = r * stride;
                if (!RowFilled(vals, idx, stride)) continue;
                int n = PlaysForRowToday(r, advRows, dfParts, gameKey);
                if (n <= 0) continue;
                if (n == 1) plainCount++;
                else advanceDraws.Add(n);
            }
        }

        if (advanceDraws.Count == 0) return "";

        var parts = new List<string>();
        if (plainCount > 0) parts.Add($"{plainCount} ticket{(plainCount == 1 ? "" : "s")}");
        foreach (var n in advanceDraws) parts.Add($"1 advance ({n} draws)");
        int total = plainCount + advanceDraws.Sum();
        decimal dollars = total * TicketCost(gameKey);
        return $"{string.Join(" + ", parts)} = {total} tickets (${dollars:N2})";
    }

    // Returns D3 today-counts split by draw filter across ALL slots: Midday / Evening / Both
    public static (int Midday, int Evening, int Both) CountD3AllSlotsTodayByFilter()
    {
        int m = 0, e = 0, b = 0;
        int activeSlot = Preferences.Get("d3_active_slot", -1);
        for (int s = 0; s < 10; s++)
        {
            // Active slot stores live data in "d3_entries" / "d3_drawfilters", not in the set_ keys
            string entries = (s == activeSlot)
                ? Preferences.Get("d3_entries", "")
                : Preferences.Get($"d3_set_{s}", "");
            if (string.IsNullOrEmpty(entries)) continue;
            string adv   = Preferences.Get($"d3_adv_{s}", "");
            string dfRaw = (s == activeSlot)
                ? Preferences.Get("d3_drawfilters", "")
                : Preferences.Get($"d3_drawfilters_{s}", "");
            var (sm, se, sb) = CountD3RowsToday(entries, adv, dfRaw, s == activeSlot);
            m += sm; e += se; b += sb;
        }
        return (m, e, b);
    }

    // ── Debug dump ────────────────────────────────────────────────────────────
    public static string BuildPrefsDebug()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"=== Prefs Debug {DateTime.Now:HH:mm:ss} ===");

        foreach (var gameKey in new[] { "F5", "D3", "SL", "PB", "MM", "D4", "DD" })
        {
            string prefix = gameKey.ToLower();
            int stride = gameKey switch { "F5"=>5,"SL"=>6,"PB"=>6,"MM"=>6,"D3"=>3,"D4"=>4,"DD"=>4,_=>1 };
            int activeSlot = Preferences.Get($"{prefix}_active_slot", -1);
            string entries = Preferences.Get($"{prefix}_entries", "");
            int rowCount = CountFilledRows(entries, stride);

            int totalCount = CountActiveTickets(gameKey);
            sb.AppendLine($"\n[{gameKey}] active_slot={activeSlot} entries_rows={rowCount} TOTAL_COUNT={totalCount}");
            if (gameKey == "D3")
                sb.AppendLine($"  drawfilters={Preferences.Get("d3_drawfilters", "(none)")}");

            for (int s = 0; s < 10; s++)
            {
                string adv    = Preferences.Get($"{prefix}_adv_{s}", "");
                string set    = Preferences.Get($"{prefix}_set_{s}", "");
                string df     = gameKey == "D3"
                    ? (s == activeSlot
                        ? Preferences.Get("d3_drawfilters", "")
                        : Preferences.Get($"d3_drawfilters_{s}", ""))
                    : "";
                int setRows   = CountFilledRows(set, stride);
                int slotCount = CountRowsAdvance(set, adv, df, stride, gameKey);
                if (!string.IsNullOrEmpty(adv) || setRows > 0)
                {
                    string dfLabel = gameKey == "D3" && !string.IsNullOrEmpty(df) ? $" df={df}" : "";
                    sb.AppendLine($"  slot{s}: set_rows={setRows} slot_count={slotCount}{dfLabel}");
                    if (!string.IsNullOrEmpty(adv))
                    {
                        // Show each adv row on its own line
                        var advParts = adv.Split('|');
                        for (int r = 0; r < advParts.Length; r++)
                        {
                            if (!string.IsNullOrEmpty(advParts[r]) && advParts[r] != "~~~")
                                sb.AppendLine($"    adv[{r}]={advParts[r]}");
                        }
                    }
                }
            }

            // Also show active slot live entries count
            if (activeSlot >= 0)
            {
                string live = Preferences.Get($"{prefix}_entries", "");
                string saved = Preferences.Get($"{prefix}_set_{activeSlot}", "");
                string advActive = Preferences.Get($"{prefix}_adv_{activeSlot}", "");
                string dfActive = gameKey == "D3" ? Preferences.Get("d3_drawfilters", "") : "";
                int liveCount = CountRowsAdvance(live, advActive, dfActive, stride, gameKey);
                int savedCount = CountRowsAdvance(saved, advActive, dfActive, stride, gameKey);
                sb.AppendLine($"  active slot{activeSlot}: live_count={liveCount} saved_count={savedCount}");
            }
        }

        return sb.ToString();
    }

    public static void DumpPrefsToFile()
    {
        try
        {
            string text = BuildPrefsDebug();
            string path = Path.Combine(FileSystem.AppDataDirectory, "debug_prefs.txt");
            File.WriteAllText(path, text);
#if ANDROID
            string extPath = System.IO.Path.Combine(
                Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
                    ?? FileSystem.AppDataDirectory,
                "debug_prefs.txt");
            File.WriteAllText(extPath, text);
#endif
        }
        catch { }
    }

    static int CountFilledRows(string entries, int stride)
    {
        if (string.IsNullOrEmpty(entries)) return 0;
        var vals = entries.Split('|');
        int count = 0;
        for (int r = 0; r < 10; r++)
        {
            int idx = r * stride;
            if (idx >= vals.Length) break;
            if (!string.IsNullOrWhiteSpace(vals[idx])) count++;
        }
        return count;
    }
}
