using System.Text.Json;
using DailyFantasyMAUI;

namespace DailyFantasyMAUI.Services;

public class TicketLogEntry
{
    public string Date      { get; set; } = ""; // yyyy-MM-dd computer date when first logged
    public string Game      { get; set; } = ""; // "F5","SL","PB","MM","D3","D4","DD"
    public int    Slot      { get; set; }
    public int    Row       { get; set; }
    public string Numbers   { get; set; } = ""; // formatted display string
    public string Extra     { get; set; } = ""; // D3: "S|B" (betType|drawFilter), PB: powerball #
    public string PlayFrom  { get; set; } = ""; // advance play from date e.g. "7/7"
    public string PlayTo    { get; set; } = ""; // advance play to date e.g. "7/8"
    public int    DrawCount  { get; set; }        // exact draws this entry covers (0 = unknown, use date range)
    public bool   IsFreePlay { get; set; }        // F5 free play ticket (not counted in spending)
}

public static class TicketLogService
{
    static string DataPath => Path.Combine(FileSystem.AppDataDirectory, "ticket_log.json");

    // Scan runs once per app session so manual deletions via game pages aren't undone
    // every time the user navigates to a summary page.
    static string _lastScanDate = "";

    // ── Advance ticket "actually entered on" tracking ──────────────────────────
    // An advance ticket's Play From/To range is just the calendar span it plays across —
    // it is NOT the day the user bought/entered it (a range can start today, tomorrow, or
    // last week regardless of when it was actually set up). This store records the real
    // day each advance row's date range was saved, keyed by a fingerprint of
    // game|slot|row|fromKey|toKey, so Ticket Log can file a ticket under the day it was
    // truly entered instead of guessing from the range.
    static string EnteredPath => Path.Combine(FileSystem.AppDataDirectory, "ticket_advance_entered.json");
    static Dictionary<string, string>? _enteredCache;

    static async Task<Dictionary<string, string>> LoadEnteredAsync()
    {
        if (_enteredCache != null) return _enteredCache;
        try
        {
            if (File.Exists(EnteredPath))
            {
                string json = await File.ReadAllTextAsync(EnteredPath);
                _enteredCache = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
            else _enteredCache = new();
        }
        catch { _enteredCache = new(); }
        return _enteredCache;
    }

    static async Task SaveEnteredAsync()
    {
        try
        {
            if (_enteredCache == null) return;
            string json = JsonSerializer.Serialize(_enteredCache);
            await File.WriteAllTextAsync(EnteredPath, json);
        }
        catch { }
    }

    static string EnteredFingerprint(string game, int slot, int row, string fromKey, string toKey) =>
        $"{game}|{slot}|{row}|{fromKey}|{toKey}";

    /// <summary>
    /// Call this the moment a game page saves a new Play From/To range for a row (the Advance
    /// Play overlay's OK button — the one place the app truly knows when the ticket was
    /// entered). Records today's real date against this exact range's fingerprint, once —
    /// re-saving the same unchanged range never moves the stamp forward. Changing the range
    /// (a genuinely new advance ticket) gets its own new fingerprint and its own new stamp.
    /// </summary>
    public static async Task RecordAdvanceEnteredAsync(string game, int slot, int row, DateTime from, DateTime to)
    {
        try
        {
            string fromKey = from.ToString("yyyyMMdd");
            string toKey   = to.ToString("yyyyMMdd");
            var map = await LoadEnteredAsync();
            string fp = EnteredFingerprint(game, slot, row, fromKey, toKey);
            if (map.ContainsKey(fp)) return;
            map[fp] = DateTime.Today.ToString("yyyy-MM-dd");
            await SaveEnteredAsync();
        }
        catch { }
    }

    /// <summary>
    /// Forces today's scan to run again even if it already ran this session.
    /// Call from game pages (e.g. Daily3Page.OnDisappearing) so ticket_log.json
    /// is always on disk and survives an app kill / new install.
    /// </summary>
    public static async Task ForceSyncTodayAsync()
    {
        _lastScanDate = "";
        await ScanAndLogTodayAsync();
    }

    /// <summary>
    /// Set by game pages when they start their OnDisappearing log write.
    /// Summary/log pages await this before reading so they never see a stale count.
    /// </summary>
    public static Task? PendingWriteTask;

    public static async Task<List<TicketLogEntry>> LoadAllAsync()
    {
        try
        {
            if (!File.Exists(DataPath)) return new();
            string json = await File.ReadAllTextAsync(DataPath);
            return JsonSerializer.Deserialize<List<TicketLogEntry>>(json) ?? new();
        }
        catch { return new(); }
    }

    public static async Task SavePublicAsync(List<TicketLogEntry> entries) => await SaveAllAsync(entries);

    static async Task SaveAllAsync(List<TicketLogEntry> entries)
    {
        try
        {
            string json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = false });
            await File.WriteAllTextAsync(DataPath, json);
        }
        catch { }
    }

    // Logs only rows that were actually entered/changed this session (caller compares against snapshot).
    // Date = today's computer date (when the ticket was inputted).
    public static async Task LogRowsAsync(string game, List<(int Slot, int Row, string Numbers, string Extra, string PlayFrom, string PlayTo)> rows, Dictionary<(int Slot, int Row), bool>? fpFlags = null, string? logDate = null)
    {
        if (rows.Count == 0) return;
        try
        {
            string today = logDate ?? DateTime.Today.ToString("yyyy-MM-dd");
            var all = await LoadAllAsync();
            bool changed = false;
            for (int i = 0; i < rows.Count; i++)
            {
                var (slot, row, numbers, extra, playFrom, playTo) = rows[i];
                if (string.IsNullOrWhiteSpace(numbers)) continue;
                bool isFp = fpFlags != null && fpFlags.TryGetValue((slot, row), out bool f) && f;

                // Advance tickets log on their FROM date (purchase date), not today.
                // This keeps the log accurate — a ticket bought Jul 13 for Jul 13-14 appears on Jul 13.
                string rowDate = today;
                if (!string.IsNullOrEmpty(playFrom))
                {
                    if (DateTime.TryParseExact(playFrom, "M/d", null, System.Globalization.DateTimeStyles.None, out var pfDate))
                    {
                        pfDate = new DateTime(DateTime.Today.Year, pfDate.Month, pfDate.Day);
                        if (pfDate > DateTime.Today) pfDate = pfDate.AddYears(-1);
                        if (pfDate < DateTime.Today)
                            rowDate = pfDate.ToString("yyyy-MM-dd");
                    }
                }

                var existing = all.FirstOrDefault(e => e.Date == rowDate && e.Game == game && e.Slot == slot && e.Row == row && e.Numbers == numbers && e.Extra == extra);
                if (existing == null)
                {
                    all.Add(new TicketLogEntry { Date = rowDate, Game = game, Slot = slot, Row = row, Numbers = numbers, Extra = extra, PlayFrom = playFrom, PlayTo = playTo, IsFreePlay = isFp });
                    changed = true;
                }
                else if (existing.IsFreePlay != isFp)
                {
                    existing.IsFreePlay = isFp;
                    changed = true;
                }
            }
            if (changed) await SaveAllAsync(all);
        }
        catch { }
    }

    /// <summary>Updates IsFreePlay on existing entries without adding new ones.</summary>
    public static async Task UpdateFreePlayFlagsAsync(string game, List<(int Slot, int Row, string Numbers, string Extra, string PlayFrom, string PlayTo)> rows, Dictionary<(int Slot, int Row), bool> fpFlags)
    {
        if (rows.Count == 0) return;
        try
        {
            var all = await LoadAllAsync();
            bool changed = false;
            for (int i = 0; i < rows.Count; i++)
            {
                var (slot, row, numbers, extra, _, _) = rows[i];
                bool isFp = fpFlags.TryGetValue((slot, row), out bool f) && f;
                var existing = all.FirstOrDefault(e => e.Game == game && e.Slot == slot && e.Row == row && e.Numbers == numbers && e.Extra == extra);
                if (existing != null && existing.IsFreePlay != isFp)
                {
                    existing.IsFreePlay = isFp;
                    changed = true;
                }
            }
            if (changed) await SaveAllAsync(all);
        }
        catch { }
    }

    /// <summary>
    /// Scans ALL game Preferences slots for tickets whose advance play date range includes today.
    /// Runs automatically when the Ticket Log page opens — no need to visit each game.
    /// </summary>
    public static async Task ScanAndLogTodayAsync()
    {
        // Only scan once per app session per day — prevents repeated page navigations
        // from undoing manual deletions made via game page OnDisappearing.
        string todayStr2 = DateTime.Today.ToString("yyyy-MM-dd");
        if (_lastScanDate == todayStr2) return;
        _lastScanDate = todayStr2;

        try
        {
            string todayStr = todayStr2;
            string todayKey = DateTime.Today.ToString("yyyyMMdd");

            // Respect "Clear All" — don't re-add entries whose FROM date is before the user's last clear.
            string clearedAtStr = Preferences.Get("ticket_log_cleared_at", "");
            string clearedAtKey = ""; // yyyyMMdd — tickets with fromKey < this are skipped
            if (!string.IsNullOrEmpty(clearedAtStr))
            {
                if (DateTime.TryParseExact(clearedAtStr, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var cd))
                    clearedAtKey = cd.ToString("yyyyMMdd");
            }

            // The "actually entered on" store (see RecordAdvanceEnteredAsync) — loaded once per
            // scan. Mutated in place by RowMatchesDate below when it has to fall back to the old
            // range-guessing behavior for a row saved before this tracking existed; saved once at
            // the end of the scan instead of per-row.
            var enteredMap = await LoadEnteredAsync();
            bool enteredMapChanged = false;

            // Returns true if this row's advance-play date range includes targetKey (yyyyMMdd).
            // Sets pf/pt to "M/d" formatted from/to for display.
            // Skips rows whose FROM date is before clearedAtKey (user did a Clear All after that date).
            bool RowMatchesDate(string advRaw, int slot, int rowIdx, out string pf, out string pt, string targetKey, string gameKey = "")
            {
                pf = pt = "";
                if (string.IsNullOrEmpty(advRaw)) return false;
                var parts = advRaw.Split('|');
                if (rowIdx >= parts.Length) return false;
                var pair = parts[rowIdx].Split('~');
                if (pair.Length < 2) return false;
                string fromKey = pair[0];
                string toKey   = pair[1];
                if (string.IsNullOrEmpty(fromKey)) return false;
                // Skip advance entries that existed before the user's last Clear All.
                if (!string.IsNullOrEmpty(clearedAtKey) &&
                    string.Compare(fromKey, clearedAtKey, StringComparison.Ordinal) < 0) return false;

                string effectiveFrom;
                string fp = !string.IsNullOrEmpty(gameKey) ? EnteredFingerprint(gameKey, slot, rowIdx, fromKey, toKey) : "";

                if (!string.IsNullOrEmpty(fp) && enteredMap.TryGetValue(fp, out var stampedDate)
                    && DateTime.TryParseExact(stampedDate, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var stampedDt))
                {
                    // We know the real day this range was saved — use it, full stop. This is the
                    // only case that's actually correct; everything below is a fallback for rows
                    // saved before this tracking existed.
                    effectiveFrom = stampedDt.ToString("yyyyMMdd");
                }
                else
                {
                    // No stamp yet (ticket entered before this tracking was added). Fall back to
                    // the old range-guessing behavior: for games that don't draw every day (PB:
                    // Mon/Wed/Sat, MM: Tue/Fri), anchor to the first day in the range the game
                    // actually draws instead of the raw start date. Then lock this guess in as the
                    // row's permanent stamp so it can never drift again on a future rescan.
                    effectiveFrom = fromKey;
                    if (!string.IsNullOrEmpty(gameKey) &&
                        DateTime.TryParseExact(fromKey, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var fStart) &&
                        DateTime.TryParseExact(string.IsNullOrEmpty(toKey) ? fromKey : toKey, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var fEnd))
                    {
                        for (var d = fStart; d <= fEnd; d = d.AddDays(1))
                        {
                            if (SpendingTracker.GameDrawsOnDate(gameKey, d)) { effectiveFrom = d.ToString("yyyyMMdd"); break; }
                        }
                    }
                    if (!string.IsNullOrEmpty(fp) &&
                        DateTime.TryParseExact(effectiveFrom, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var lockDt))
                    {
                        enteredMap[fp] = lockDt.ToString("yyyy-MM-dd");
                        enteredMapChanged = true;
                    }
                }

                // Match only on the effective (entered-day-anchored) FROM date.
                if (effectiveFrom != targetKey) return false;
                string effectiveTo = string.IsNullOrEmpty(toKey) ? fromKey : toKey;
                if (DateTime.TryParseExact(fromKey, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var fd))
                    pf = fd.ToString("M/d");
                if (DateTime.TryParseExact(effectiveTo, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var td))
                    pt = td.ToString("M/d");
                return true;
            }

            // Scan last 30 days so the ticket log self-heals if the JSON was ever wiped.
            // Today's entries were cleared above, so they get a full rescan.
            // Past-day entries use dedup in LogRowsAsync — missing ones are added back, existing ones are untouched.
            for (int daysBack = 0; daysBack <= 30; daysBack++)
            {
                var    scanDate = DateTime.Today.AddDays(-daysBack);
                string scanKey  = scanDate.ToString("yyyyMMdd");
                string scanStr  = scanDate.ToString("yyyy-MM-dd");
                bool   isToday  = daysBack == 0;

                // Skip past dates that were cleared by the user — only today always rescans.
                if (!isToday && !string.IsNullOrEmpty(clearedAtKey) && string.Compare(scanKey, clearedAtKey, StringComparison.Ordinal) < 0)
                    continue;

                var f5      = new List<(int, int, string, string, string, string)>();
                var f5Flags = new Dictionary<(int Slot, int Row), bool>();
                var sl      = new List<(int, int, string, string, string, string, int)>();
                var pb      = new List<(int, int, string, string, string, string, int)>();
                var mm      = new List<(int, int, string, string, string, string, int)>();
                var d3      = new List<(int, int, string, string, string, string, int)>();
                var d4      = new List<(int, int, string, string, string, string)>();
                var dd      = new List<(int, int, string, string, string, string)>();
                var hs      = new List<(int, int, string, string, string, string, int)>();

                // Hot Spot: 10-slot system (see HotSpotPage.cs — was single-ticket before
                // 2026-08-09). Each slot only ever logs on the exact day it was purchased/
                // saved; row index = slot number so multiple same-day tickets show as
                // distinct rows instead of colliding.
                for (int hsSlot = 0; hsSlot < HotSpotPage.SlotCount; hsSlot++)
                {
                    string hsPurchased = Preferences.Get($"hs_purchased_date_{hsSlot}", "");
                    if (hsPurchased != scanKey) continue;
                    string hsNumbers = Preferences.Get($"hs_numbers_{hsSlot}", "");
                    if (string.IsNullOrWhiteSpace(hsNumbers)) continue;
                    int hsSpots    = Preferences.Get($"hs_spots_{hsSlot}", 0);
                    double hsWager = Preferences.Get($"hs_wager_{hsSlot}", 1.0);
                    bool hsBull    = Preferences.Get($"hs_bullseye_{hsSlot}", false);
                    int hsDraws    = Preferences.Get($"hs_draws_{hsSlot}", 1);
                    string extra   = $"SP:{hsSpots}|W:{hsWager:0}|BE:{(hsBull ? 1 : 0)}";
                    string pfpt    = scanDate.ToString("M/d");
                    hs.Add((hsSlot, 0, hsNumbers.Replace('|', ' '), extra, pfpt, pfpt, hsDraws));
                }

                for (int s = 0; s < 10; s++)
                {
                    // Fantasy 5 — 10 rows × 5 cols
                    {
                        string set   = Preferences.Get($"f5_set_{s}", "");
                        string adv   = Preferences.Get($"f5_adv_{s}", "");
                        string fpRaw = Preferences.Get($"f5_freeplay_{s}", "");
                        var fpParts  = string.IsNullOrEmpty(fpRaw) ? Array.Empty<string>() : fpRaw.Split('|');
                        if (!string.IsNullOrEmpty(set))
                        {
                            var vals     = set.Split('|');
                            var advParts = string.IsNullOrEmpty(adv) ? new string[10] : adv.Split('|');
                            if (advParts.Length < 10) Array.Resize(ref advParts, 10);
                            for (int r = 0; r < 10; r++)
                            {
                                bool hasAdv = !string.IsNullOrEmpty(advParts[r]) && advParts[r] != "~";
                                string pf = "", pt = "";
                                // Include row if: advance date matches scanKey, OR no advance date and scanning today
                                if (hasAdv)
                                {
                                    if (!RowMatchesDate(adv, s, r, out pf, out pt, scanKey, "F5")) continue;
                                }
                                else if (!isToday) continue; // regular tickets only log on today's scan
                                var nums = new List<string>(); bool full = true;
                                for (int c = 0; c < 5; c++)
                                {
                                    string v = r * 5 + c < vals.Length ? vals[r * 5 + c] : "";
                                    if (string.IsNullOrWhiteSpace(v)) { full = false; break; }
                                    nums.Add(v.PadLeft(2, '0'));
                                }
                                if (full)
                                {
                                    bool isFp = r < fpParts.Length && fpParts[r] == "1";
                                    f5.Add((s, r, string.Join(" ", nums), "", pf, pt));
                                    f5Flags[(s, r)] = isFp;
                                }
                            }
                        }
                    }

                    // Super Lotto — 10 rows × 6 cols (5 main + mega at col 5)
                    {
                        string set = Preferences.Get($"sl_set_{s}", "");
                        string adv = Preferences.Get($"sl_adv_{s}", "");
                        if (!string.IsNullOrEmpty(set))
                        {
                            var vals    = set.Split('|');
                            var advRows = string.IsNullOrEmpty(adv) ? Array.Empty<string>() : adv.Split('|');
                            for (int r = 0; r < 10; r++)
                            {
                                if (!RowMatchesDate(adv, s, r, out string pf, out string pt, scanKey, "SL")) continue;
                                var nums = new List<string>(); bool full = true;
                                for (int c = 0; c < 5; c++)
                                {
                                    string v = r * 6 + c < vals.Length ? vals[r * 6 + c] : "";
                                    if (string.IsNullOrWhiteSpace(v)) { full = false; break; }
                                    nums.Add(v.PadLeft(2, '0'));
                                }
                                string mega = r * 6 + 5 < vals.Length ? vals[r * 6 + 5] : "";
                                if (full && !string.IsNullOrWhiteSpace(mega))
                                {
                                    int dc = 0;
                                    if (r < advRows.Length && !string.IsNullOrEmpty(advRows[r]))
                                    {
                                        var p = advRows[r].Split('~');
                                        if (p.Length >= 4 && int.TryParse(p[2], out int ds) && int.TryParse(p[3], out int de) && ds > 0 && de >= ds)
                                            dc = Math.Abs(de - ds) + 1;
                                    }
                                    sl.Add((s, r, string.Join(" ", nums), $"M:{mega.PadLeft(2, '0')}", pf, pt, dc));
                                }
                            }
                        }
                    }

                    // Powerball — 10 rows × 6 cols (5 main + pb at col 5)
                    {
                        string set = Preferences.Get($"pb_set_{s}", "");
                        string adv = Preferences.Get($"pb_adv_{s}", "");
                        if (!string.IsNullOrEmpty(set))
                        {
                            var vals    = set.Split('|');
                            var advRows = string.IsNullOrEmpty(adv) ? Array.Empty<string>() : adv.Split('|');
                            for (int r = 0; r < 10; r++)
                            {
                                if (!RowMatchesDate(adv, s, r, out string pf, out string pt, scanKey, "PB")) continue;
                                var nums = new List<string>(); bool full = true;
                                for (int c = 0; c < 5; c++)
                                {
                                    string v = r * 6 + c < vals.Length ? vals[r * 6 + c] : "";
                                    if (string.IsNullOrWhiteSpace(v)) { full = false; break; }
                                    nums.Add(v.PadLeft(2, '0'));
                                }
                                string pbnum = r * 6 + 5 < vals.Length ? vals[r * 6 + 5] : "";
                                if (full && !string.IsNullOrWhiteSpace(pbnum))
                                {
                                    int dc = 0;
                                    if (r < advRows.Length && !string.IsNullOrEmpty(advRows[r]))
                                    {
                                        var p = advRows[r].Split('~');
                                        if (p.Length >= 4 && int.TryParse(p[2], out int ds) && int.TryParse(p[3], out int de) && ds > 0 && de >= ds)
                                            dc = Math.Abs(de - ds) + 1;
                                    }
                                    pb.Add((s, r, string.Join(" ", nums), $"PB:{pbnum.PadLeft(2, '0')}", pf, pt, dc));
                                }
                            }
                        }
                    }

                    // Mega Millions — 10 rows × 6 cols (5 main + mb at col 5)
                    {
                        string set = Preferences.Get($"mm_set_{s}", "");
                        string adv = Preferences.Get($"mm_adv_{s}", "");
                        if (!string.IsNullOrEmpty(set))
                        {
                            var vals    = set.Split('|');
                            var advRows = string.IsNullOrEmpty(adv) ? Array.Empty<string>() : adv.Split('|');
                            for (int r = 0; r < 10; r++)
                            {
                                if (!RowMatchesDate(adv, s, r, out string pf, out string pt, scanKey, "MM")) continue;
                                var nums = new List<string>(); bool full = true;
                                for (int c = 0; c < 5; c++)
                                {
                                    string v = r * 6 + c < vals.Length ? vals[r * 6 + c] : "";
                                    if (string.IsNullOrWhiteSpace(v)) { full = false; break; }
                                    nums.Add(v.PadLeft(2, '0'));
                                }
                                string mb = r * 6 + 5 < vals.Length ? vals[r * 6 + 5] : "";
                                if (full && !string.IsNullOrWhiteSpace(mb))
                                {
                                    int dc = 0;
                                    if (r < advRows.Length && !string.IsNullOrEmpty(advRows[r]))
                                    {
                                        var p = advRows[r].Split('~');
                                        if (p.Length >= 4 && int.TryParse(p[2], out int ds) && int.TryParse(p[3], out int de) && ds > 0 && de >= ds)
                                            dc = Math.Abs(de - ds) + 1;
                                    }
                                    mm.Add((s, r, string.Join(" ", nums), $"MB:{mb.PadLeft(2, '0')}", pf, pt, dc));
                                }
                            }
                        }
                    }

                    // Daily 3 — 10 rows × 3 cols
                    {
                        int    d3ActiveSlot = Preferences.Get("d3_active_slot", -1);
                        // Active slot live data lives in d3_entries/d3_drawfilters, not d3_set_{s}.
                        // Reading d3_entries for the active slot ensures TL and SL count from the same source.
                        string set   = (s == d3ActiveSlot)
                            ? Preferences.Get("d3_entries", "")
                            : Preferences.Get($"d3_set_{s}", "");
                        string adv   = Preferences.Get($"d3_adv_{s}", "");
                        string dfRaw = (s == d3ActiveSlot)
                            ? Preferences.Get("d3_drawfilters", "")
                            : Preferences.Get($"d3_drawfilters_{s}", "");
                        var dfParts  = string.IsNullOrEmpty(dfRaw) ? Array.Empty<string>() : dfRaw.Split('|');
                        var advRows  = string.IsNullOrEmpty(adv) ? Array.Empty<string>() : adv.Split('|');
                        if (!string.IsNullOrEmpty(set))
                        {
                            var vals = set.Split('|');
                            for (int r = 0; r < 10; r++)
                            {
                                bool d3HasAdv = r < advRows.Length && !string.IsNullOrEmpty(advRows[r])
                                    && advRows[r] != "~" && advRows[r] != "~~~";
                                string pf, pt;
                                if (d3HasAdv)
                                {
                                    if (!RowMatchesDate(adv, s, r, out pf, out pt, scanKey)) continue;
                                }
                                else
                                {
                                    // Non-advance D3 ticket — logs for TODAY from ANY slot, active or not
                                    // (matches F5/SL/PB/MM below, which never required the active slot).
                                    // Previously required s == d3ActiveSlot, which silently dropped a slot's
                                    // real same-day tickets from Ticket Log the moment the user switched to
                                    // a different Set before the next rescan (2026-08-05).
                                    if (!isToday) continue;
                                    pf = pt = "";
                                }
                                var nums = new List<string>(); bool full = true;
                                for (int c = 0; c < 3; c++)
                                {
                                    string v = r * 3 + c < vals.Length ? vals[r * 3 + c] : "";
                                    if (string.IsNullOrWhiteSpace(v)) { full = false; break; }
                                    nums.Add(v);
                                }
                                if (!full) continue;
                                string numStr = string.Join(" ", nums);
                                string df     = r < dfParts.Length && !string.IsNullOrEmpty(dfParts[r]) ? dfParts[r] : "B";
                                string advRow = r < advRows.Length ? advRows[r] : "";
                                var pair      = advRow.Split('~');
                                int totalDraws = 1;
                                int drawStart  = 0;
                                if (d3HasAdv && pair.Length >= 4 &&
                                    int.TryParse(pair[2], out int ds) && int.TryParse(pair[3], out int de) &&
                                    ds > 0 && de >= ds)
                                {
                                    totalDraws = de - ds + 1;
                                    drawStart  = ds;
                                }
                                if (df == "M")
                                {
                                    d3.Add((s, r, numStr, "M", pf, pt, totalDraws));
                                }
                                else if (df == "E")
                                {
                                    d3.Add((s, r, numStr, "E", pf, pt, totalDraws));
                                }
                                else // "B" = Both
                                {
                                    if (!d3HasAdv)
                                    {
                                        // Non-advance B row: 1 Midday + 1 Evening (same as CountD3RowsToday)
                                        d3.Add((s, r, numStr, "M", pf, pt, 1));
                                        d3.Add((s, r, numStr, "E", pf, pt, 1));
                                    }
                                    else
                                    {
                                        // Advance B row — split M/E by draw# parity
                                        // even draw# = Midday, odd draw# = Evening
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
                                        if (mDraws > 0) d3.Add((s, r, numStr, "M", pf, pt, mDraws));
                                        if (eDraws > 0) d3.Add((s, r, numStr, "E", pf, pt, eDraws));
                                    }
                                }
                            }
                        }
                    }

                    // Daily 4 — 10 rows × 4 cols
                    {
                        string set = Preferences.Get($"d4_set_{s}", "");
                        string adv = Preferences.Get($"d4_adv_{s}", "");
                        if (!string.IsNullOrEmpty(set))
                        {
                            var vals = set.Split('|');
                            for (int r = 0; r < 10; r++)
                            {
                                if (!RowMatchesDate(adv, s, r, out string pf, out string pt, scanKey, "D4")) continue;
                                var nums = new List<string>(); bool full = true;
                                for (int c = 0; c < 4; c++)
                                {
                                    string v = r * 4 + c < vals.Length ? vals[r * 4 + c] : "";
                                    if (string.IsNullOrWhiteSpace(v)) { full = false; break; }
                                    nums.Add(v);
                                }
                                if (full) d4.Add((s, r, string.Join(" ", nums), "", pf, pt));
                            }
                        }
                    }

                    // Daily Derby — 10 rows × 4 stored cols (3 horse + 1 time); use cols 0-2
                    {
                        string set = Preferences.Get($"dd_set_{s}", "");
                        string adv = Preferences.Get($"dd_adv_{s}", "");
                        if (!string.IsNullOrEmpty(set))
                        {
                            var vals = set.Split('|');
                            for (int r = 0; r < 10; r++)
                            {
                                if (!RowMatchesDate(adv, s, r, out string pf, out string pt, scanKey, "DD")) continue;
                                var nums = new List<string>(); bool full = true;
                                for (int c = 0; c < 3; c++)
                                {
                                    string v = r * 4 + c < vals.Length ? vals[r * 4 + c] : "";
                                    if (string.IsNullOrWhiteSpace(v)) { full = false; break; }
                                    nums.Add(v);
                                }
                                if (full) dd.Add((s, r, string.Join(" ", nums), "", pf, pt));
                            }
                        }
                    }
                }

                if (f5.Count > 0) await LogRowsAsync("F5", f5, f5Flags, scanStr);
                if (sl.Count > 0) await LogRowsWithDrawCountAsync("SL", sl, scanStr);
                if (pb.Count > 0) await LogRowsWithDrawCountAsync("PB", pb, scanStr);
                if (hs.Count > 0) await LogRowsWithDrawCountAsync("HS", hs, scanStr);
                if (mm.Count > 0) await LogRowsWithDrawCountAsync("MM", mm, scanStr);
                if (d3.Count > 0)
                {
                    await LogD3RowsAsync(d3, scanStr);
                    if (isToday)
                    {
                        // Remove stale D3 TL entries for today only — rows whose Extra changed
                        // (e.g. row was B-filter before but is now E-only, leaving orphan M entries)
                        var d3ValidKeys   = new HashSet<(int, int, string, string)>(d3.Select(x => (x.Item1, x.Item2, x.Item3, x.Item4)));
                        var d3TouchedRows = new HashSet<(int, int, string)>(d3.Select(x => (x.Item1, x.Item2, x.Item3)));
                        var allEntries    = await LoadAllAsync();
                        int removedStale  = allEntries.RemoveAll(e =>
                            e.Game == "D3" && e.Date == todayStr
                            && d3TouchedRows.Contains((e.Slot, e.Row, e.Numbers))
                            && !d3ValidKeys.Contains((e.Slot, e.Row, e.Numbers, e.Extra)));
                        if (removedStale > 0) await SaveAllAsync(allEntries);
                    }
                }
                if (d4.Count > 0) await LogRowsAsync("D4", d4, null, scanStr);
                if (dd.Count > 0) await LogRowsAsync("DD", dd, null, scanStr);
            }

            if (enteredMapChanged) await SaveEnteredAsync();
        }
        catch { }
    }

    // Logs SL/PB/MM rows with DrawCount; updates DrawCount on existing entries.
    public static async Task LogRowsWithDrawCountAsync(string game, List<(int Slot, int Row, string Numbers, string Extra, string PlayFrom, string PlayTo, int DrawCount)> rows, string? logDate = null)
    {
        if (rows.Count == 0) return;
        try
        {
            string today = logDate ?? DateTime.Today.ToString("yyyy-MM-dd");
            var all = await LoadAllAsync();
            bool changed = false;
            foreach (var (slot, row, numbers, extra, playFrom, playTo, drawCount) in rows)
            {
                if (string.IsNullOrWhiteSpace(numbers)) continue;
                string rowDate = today;
                if (!string.IsNullOrEmpty(playFrom))
                {
                    if (DateTime.TryParseExact(playFrom, "M/d", null, System.Globalization.DateTimeStyles.None, out var pfDate))
                    {
                        pfDate = new DateTime(DateTime.Today.Year, pfDate.Month, pfDate.Day);
                        if (pfDate > DateTime.Today) pfDate = pfDate.AddYears(-1);
                        if (pfDate < DateTime.Today)
                            rowDate = pfDate.ToString("yyyy-MM-dd");
                    }
                }
                // Identity is Date+Game+Slot+Row only — NOT Numbers/Extra too. Matching on the
                // full content used to mean an edited ticket (different spot count/wager/
                // bullseye/draws, i.e. a different Extra) looked like a brand-new row instead of
                // an update to the existing one, so re-saving an edited Hot Spot ticket left the
                // stale pre-edit row behind alongside the new one — both counted toward the
                // day's total forever, double-charging Ticket Log vs the live Spending Log
                // (confirmed live 2026-08-11: Slot 3 edited from 2-spot/5-draw to 4-spot/4-draw,
                // both rows survived, Ticket Log showed $30 vs Spending Log's correct $25).
                // Same identity `ClearTodayGameSlotAsync` already uses for its own Slot+Date
                // match — Numbers/Extra/PlayFrom/PlayTo/DrawCount are all just fields on that
                // identity now, updated in place like DrawCount already was.
                var existing = all.FirstOrDefault(e => e.Date == rowDate && e.Game == game && e.Slot == slot && e.Row == row);
                if (existing == null)
                {
                    all.Add(new TicketLogEntry { Date = rowDate, Game = game, Slot = slot, Row = row, Numbers = numbers, Extra = extra, PlayFrom = playFrom, PlayTo = playTo, DrawCount = drawCount });
                    changed = true;
                }
                else
                {
                    if (existing.Numbers != numbers) { existing.Numbers = numbers; changed = true; }
                    if (existing.Extra != extra) { existing.Extra = extra; changed = true; }
                    if (!string.IsNullOrEmpty(playFrom) && existing.PlayFrom != playFrom) { existing.PlayFrom = playFrom; changed = true; }
                    if (!string.IsNullOrEmpty(playTo) && existing.PlayTo != playTo) { existing.PlayTo = playTo; changed = true; }
                    if (drawCount > 0 && existing.DrawCount != drawCount) { existing.DrawCount = drawCount; changed = true; }
                }
            }
            if (changed) await SaveAllAsync(all);
        }
        catch { }
    }

    // Logs D3 rows with DrawCount; updates DrawCount on existing entries.
    static async Task LogD3RowsAsync(List<(int Slot, int Row, string Numbers, string Extra, string PlayFrom, string PlayTo, int DrawCount)> rows, string? logDate = null)
    {
        if (rows.Count == 0) return;
        try
        {
            string today = logDate ?? DateTime.Today.ToString("yyyy-MM-dd");
            var all = await LoadAllAsync();
            bool changed = false;
            foreach (var (slot, row, numbers, extra, playFrom, playTo, drawCount) in rows)
            {
                if (string.IsNullOrWhiteSpace(numbers)) continue;
                // Remove legacy "betType|M" / "betType|E" duplicates for this row
                // (old format from LogCurrentTicketsAsync — now superseded by plain "M"/"E")
                string suffix = extra; // "M" or "E"
                int removed = all.RemoveAll(e => e.Game == "D3" && e.Slot == slot && e.Row == row
                    && e.Numbers == numbers && e.Extra != suffix
                    && e.Extra.EndsWith("|" + suffix, StringComparison.OrdinalIgnoreCase));
                if (removed > 0) changed = true;

                var existing = all.FirstOrDefault(e => e.Date == today && e.Game == "D3" && e.Slot == slot && e.Row == row && e.Numbers == numbers && e.Extra == extra);
                if (existing == null)
                {
                    all.Add(new TicketLogEntry { Date = today, Game = "D3", Slot = slot, Row = row, Numbers = numbers, Extra = extra, PlayFrom = playFrom, PlayTo = playTo, DrawCount = drawCount });
                    changed = true;
                }
                else if (drawCount > 0 && existing.DrawCount != drawCount)
                {
                    existing.DrawCount = drawCount;
                    changed = true;
                }
            }
            if (changed) await SaveAllAsync(all);
        }
        catch { }
    }

    // Returns exact draw count for a row from its draw# range (most accurate),
    // falling back to 1 if not set.
    static int ComputeDrawCount(string advRow)
    {
        if (string.IsNullOrEmpty(advRow)) return 1;
        var pair = advRow.Split('~');
        if (pair.Length >= 4 &&
            int.TryParse(pair[2], out int ds) && int.TryParse(pair[3], out int de) &&
            ds > 0 && de >= ds)
            return Math.Abs(de - ds) + 1;
        // Fall back to date range (each day = 1 draw for M/E filter; caller handles B split)
        if (pair.Length >= 2 &&
            DateTime.TryParseExact(pair[0], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var start) &&
            DateTime.TryParseExact(pair[1], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var end) &&
            end >= start)
            return (end - start).Days + 1;
        return 1;
    }

    /// <summary>
    /// Adds a single entry without dedup — used for manually-entered items like Scratchers.
    /// </summary>
    public static async Task AddSingleAsync(TicketLogEntry entry)
    {
        try
        {
            var all = await LoadAllAsync();
            all.Add(entry);
            await SaveAllAsync(all);
        }
        catch { }
    }

    /// <summary>
    /// Removes today's log entries for a specific game + slot before re-logging.
    /// Ensures cleared/deleted rows don't linger in the log.
    /// </summary>
    public static async Task ClearTodayGameSlotAsync(string game, int slot)
    {
        try
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd");
            var all = await LoadAllAsync();
            int removed = all.RemoveAll(e => e.Date == today && e.Game == game && e.Slot == slot);
            if (removed > 0) await SaveAllAsync(all);
        }
        catch { }
    }

    /// <summary>Clears all entries for a date (used by the Clear Day button).</summary>
    public static async Task ClearDayAsync(string date)
    {
        try { var all = await LoadAllAsync(); all.RemoveAll(e => e.Date == date); await SaveAllAsync(all); }
        catch { }
    }

    /// <summary>Clears only auto-scanned game entries for a date, preserving manually-logged SC entries.</summary>
    public static async Task ClearDayForRescanAsync(string date)
    {
        try { var all = await LoadAllAsync(); all.RemoveAll(e => e.Date == date && e.Game != "SC"); await SaveAllAsync(all); }
        catch { }
    }

    public static async Task ClearAllAsync()
    {
        try
        {
            await SaveAllAsync(new());
            // Record when the user cleared all data — scan won't re-add entries before this date
            Preferences.Set("ticket_log_cleared_at", DateTime.Today.ToString("yyyy-MM-dd"));
        }
        catch { }
    }
}
