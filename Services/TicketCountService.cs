namespace DailyFantasyMAUI.Services;

/// <summary>
/// Counts all-time tickets purchased per game from the ticket log.
/// </summary>
public static class TicketCountService
{
    public static readonly (string Key, string Name, string Color)[] Games =
    [
        ("F5", "Fantasy 5",      "#FF8F00"),
        ("SL", "Super Lotto Plus","#7B1FA2"),
        ("PB", "Powerball",      "#C62828"),
        ("MM", "Mega Millions",  "#F57F17"),
        ("D3", "Daily 3",        "#1565C0"),
        ("D4", "Daily 4",        "#00695C"),
        ("DD", "Daily Derby",    "#5D4037"),
        ("SC", "Scratchers",     "#2E7D32"),
        ("HS", "Hot Spot",       "#E65100"),
    ];

    /// <summary>
    /// Returns total ticket count across all games from the log.
    /// D3 M+E pairs for the same slot/row/numbers/date count as 1 ticket.
    /// </summary>
    public static async Task<int> GetTotalAsync()
    {
        var entries = await TicketLogService.LoadAllAsync();
        var d3Seen = new HashSet<string>();
        int total = 0;
        foreach (var e in entries)
        {
            if (e.Game == "D3")
            {
                string key = $"{e.Date}_{e.Slot}_{e.Row}_{e.Numbers}";
                if (!d3Seen.Add(key)) continue;
            }
            total += e.Game == "SC" ? Math.Max(1, e.DrawCount) : 1;
        }
        return total;
    }

    /// <summary>
    /// Returns ticket count per game key — uses manual override if set, otherwise log count.
    /// D3 M+E pairs for the same slot/row/numbers/date count as 1 ticket.
    /// </summary>
    public static async Task<Dictionary<string, int>> GetCountByGameAsync()
    {
        var entries = await TicketLogService.LoadAllAsync();
        var logCounts = Games.ToDictionary(g => g.Key, _ => 0);
        var d3Seen = new HashSet<string>();
        foreach (var e in entries)
        {
            if (!logCounts.ContainsKey(e.Game)) continue;
            if (e.Game == "D3")
            {
                string key = $"{e.Date}_{e.Slot}_{e.Row}_{e.Numbers}";
                if (!d3Seen.Add(key)) continue;
            }
            logCounts[e.Game] += e.Game == "SC" ? Math.Max(1, e.DrawCount) : 1;
        }

        var result = new Dictionary<string, int>();
        foreach (var (key, _, _) in Games)
        {
            int ovr = GetOverride(key);
            result[key] = ovr >= 0 ? ovr : logCounts[key];
        }
        return result;
    }

    /// <summary>
    /// Returns today's ticket count per game key from the log (no override — always live).
    /// D3 M+E pairs for the same slot/row/numbers/date count as 1 ticket.
    /// </summary>
    public static async Task<Dictionary<string, int>> GetTodayCountByGameAsync()
    {
        string todayStr = DateTime.Today.ToString("yyyy-MM-dd");
        var entries = await TicketLogService.LoadAllAsync();
        var counts = Games.ToDictionary(g => g.Key, _ => 0);
        var d3Seen = new HashSet<string>();
        foreach (var e in entries)
        {
            if (e.Date != todayStr || !counts.ContainsKey(e.Game)) continue;
            if (e.Game == "D3")
            {
                string key = $"{e.Slot}_{e.Row}_{e.Numbers}";
                if (!d3Seen.Add(key)) continue;
            }
            counts[e.Game] += e.Game == "SC" ? Math.Max(1, e.DrawCount) : 1;
        }
        return counts;
    }

    static string OverrideKey(string gameKey) => $"ticket_count_override_{gameKey}";

    /// <summary>Returns the manual override for a game, or -1 if not set.</summary>
    public static int GetOverride(string gameKey) =>
        Preferences.Get(OverrideKey(gameKey), -1);

    /// <summary>Saves a manual override count for a game.</summary>
    public static void SetOverride(string gameKey, int count) =>
        Preferences.Set(OverrideKey(gameKey), count);

    /// <summary>Clears the manual override, reverting to log count.</summary>
    public static void ClearOverride(string gameKey) =>
        Preferences.Remove(OverrideKey(gameKey));
}
