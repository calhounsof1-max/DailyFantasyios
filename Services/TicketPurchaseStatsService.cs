using System.Text.Json;
using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI.Services;

// ── Models ────────────────────────────────────────────────────────────────────

/// <summary>Ticket count for one game on one day.</summary>
public class DailyGameStat
{
    public string Game     { get; set; } = "";
    public string GameName { get; set; } = "";
    public int    Count    { get; set; }
}

/// <summary>All games played on a single date.</summary>
public class DailyStat
{
    public string              Date         { get; set; } = "";   // "yyyy-MM-dd"
    public int                 TotalTickets { get; set; }
    public decimal             TotalCost    { get; set; }
    public List<DailyGameStat> Games        { get; set; } = new();
}

/// <summary>All-time ticket totals for one game.</summary>
public class GameAllTimeStat
{
    public string  GameKey    { get; set; } = "";
    public string  GameName   { get; set; } = "";
    public string  Color      { get; set; } = "#888888";
    public int     TotalCount { get; set; }
    public decimal TotalCost  { get; set; }
    /// <summary>Average tickets per day the game was played (days with count > 0).</summary>
    public double  AvgPerDay  { get; set; }
    /// <summary>Number of distinct days the game was played.</summary>
    public int     DaysPlayed { get; set; }
}

/// <summary>Full stats result returned from a single GetStatsAsync() call.</summary>
public class TicketPurchaseStatsResult
{
    public string                GeneratedAt       { get; set; } = "";
    public int                   GrandTotalTickets { get; set; }
    /// <summary>Average total tickets per day across all days in the log.</summary>
    public double                AvgTicketsPerDay  { get; set; }
    /// <summary>Total number of distinct days in the log.</summary>
    public int                   TotalDays         { get; set; }
    /// <summary>Sorted descending by TotalCount.</summary>
    public List<GameAllTimeStat> GameTotals        { get; set; } = new();
    /// <summary>Most recent date first.</summary>
    public List<DailyStat>       DailyStats        { get; set; } = new();
}

// ── Service ───────────────────────────────────────────────────────────────────

/// <summary>
/// Computes daily/all-time ticket purchase stats from the ticket log.
/// All logic lives here; call <see cref="GetStatsAsync"/> for everything needed.
/// Saves a human-readable ticket_purchase_stats.json file after each refresh.
/// </summary>
public static class TicketPurchaseStatsService
{
    static readonly JsonSerializerOptions PrettyOpts = new() { WriteIndented = true };

    /// <summary>Path to the saved stats file (viewable/editable outside the app).</summary>
    public static string DataPath =>
        Path.Combine(FileSystem.AppDataDirectory, "ticket_purchase_stats.json");

    static readonly (string Key, string Name, string Color, decimal CostEach)[] Games =
    [
        ("F5", "Fantasy 5",        "#FF8F00", 1m),
        ("SL", "Super Lotto Plus", "#7B1FA2", 1m),
        ("PB", "Powerball",        "#C62828", 2m),
        ("MM", "Mega Millions",    "#F57F17", 5m),
        ("D3", "Daily 3",          "#1565C0", 1m),
        ("D4", "Daily 4",          "#00695C", 1m),
        ("DD", "Daily Derby",      "#5D4037", 2m),
        ("SC", "Scratchers",       "#2E7D32", 1m),
        // Hot Spot's real per-ticket cost (wager × draws × Bulls-eye multiplier) is computed
        // from each entry's Extra/DrawCount below, same as D3 — CostEach here is unused.
        ("HS", "Hot Spot",         "#E65100", 0m),
    ];

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Single async entry point: loads the ticket log, computes stats,
    /// saves ticket_purchase_stats.json, and returns the full result.
    /// </summary>
    public static async Task<TicketPurchaseStatsResult> GetStatsAsync()
    {
        var entries = await TicketLogService.LoadAllAsync();
        var result  = Compute(entries);
        await SaveAsync(result);
        return result;
    }

    /// <summary>
    /// Loads the last saved stats file without recomputing (for instant display
    /// while the full refresh runs in the background).
    /// Returns null if no file exists yet.
    /// </summary>
    public static async Task<TicketPurchaseStatsResult?> LoadSavedAsync()
    {
        try
        {
            if (!File.Exists(DataPath)) return null;
            string json = await File.ReadAllTextAsync(DataPath);
            return JsonSerializer.Deserialize<TicketPurchaseStatsResult>(json);
        }
        catch { return null; }
    }

    // ── Compute ───────────────────────────────────────────────────────────────

    static TicketPurchaseStatsResult Compute(List<TicketLogEntry> entries)
    {
        var gameMeta = Games.ToDictionary(g => g.Key, g => g);

        // All-time totals per game
        var gameTotals = Games.ToDictionary(g => g.Key, _ => 0);

        // Daily: date → game key → count
        var dailyDict = new SortedDictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        // D3: each unique combination (Slot, Row) is 1 ticket — but every draw entry costs $1.
        // Track cost separately so count=distinct combos but cost=total draws spent.
        var seenD3        = new HashSet<(string Date, int Slot, int Row)>();
        decimal d3TotalCost = 0m;
        var d3CostByDate  = new Dictionary<string, decimal>();

        // Hot Spot: cost is wager × draws × Bulls-eye multiplier per ticket, not a fixed
        // CostEach like the other games, so it's accumulated the same way D3's cost is.
        decimal hsTotalCost = 0m;
        var hsCostByDate  = new Dictionary<string, decimal>();

        foreach (var e in entries)
        {
            if (string.IsNullOrEmpty(e.Date) || string.IsNullOrEmpty(e.Game)) continue;

            if (e.Game == "HS")
            {
                decimal wager = 1m; bool bullseye = false;
                foreach (var part in e.Extra.Split('|'))
                {
                    var kv = part.Split(':');
                    if (kv.Length != 2) continue;
                    if (kv[0] == "W") decimal.TryParse(kv[1], out wager);
                    else if (kv[0] == "BE") bullseye = kv[1] == "1";
                }
                decimal hsCost = wager * Math.Max(1, e.DrawCount) * (bullseye ? 2 : 1);
                hsTotalCost += hsCost;
                hsCostByDate.TryGetValue(e.Date, out decimal hc);
                hsCostByDate[e.Date] = hc + hsCost;
            }

            if (e.Game == "D3")
            {
                // Accumulate cost for every draw entry ($1 each)
                d3TotalCost += 1m;
                d3CostByDate.TryGetValue(e.Date, out decimal dc);
                d3CostByDate[e.Date] = dc + 1m;

                // Count only first occurrence of each combination per date
                if (!seenD3.Add((e.Date, e.Slot, e.Row))) continue;
                gameTotals["D3"] += 1;
                if (!dailyDict.TryGetValue(e.Date, out var d3Day))
                    dailyDict[e.Date] = d3Day = new Dictionary<string, int>();
                d3Day.TryGetValue("D3", out int d3Existing);
                d3Day["D3"] = d3Existing + 1;
                continue;
            }

            int count = e.Game == "SC" ? Math.Max(1, e.DrawCount) : 1;

            if (gameTotals.ContainsKey(e.Game))
                gameTotals[e.Game] += count;

            if (!dailyDict.TryGetValue(e.Date, out var dayGames))
                dailyDict[e.Date] = dayGames = new Dictionary<string, int>();

            dayGames.TryGetValue(e.Game, out int existing);
            dayGames[e.Game] = existing + count;
        }

        // Days played per game (distinct dates where game count > 0)
        var daysPerGame = new Dictionary<string, int>();
        foreach (var (key, _, _, _) in Games) daysPerGame[key] = 0;
        foreach (var kvp in dailyDict)
            foreach (var gameKvp in kvp.Value)
                if (gameKvp.Value > 0 && daysPerGame.ContainsKey(gameKvp.Key))
                    daysPerGame[gameKvp.Key]++;

        // GameAllTimeStat list — sorted descending by count, zeros last
        var gameStats = gameTotals
            .Select(kvp =>
            {
                var meta       = gameMeta[kvp.Key];
                int daysPlayed = daysPerGame.TryGetValue(kvp.Key, out int d) ? d : 0;
                // D3: cost = sum of all draw entries ($1 each); count = distinct combinations
                // HS: cost = sum of each ticket's wager × draws × Bulls-eye multiplier
                decimal totalCost = kvp.Key == "D3" ? d3TotalCost
                                   : kvp.Key == "HS" ? hsTotalCost
                                   : kvp.Value * meta.CostEach;
                return new GameAllTimeStat
                {
                    GameKey    = kvp.Key,
                    GameName   = meta.Name,
                    Color      = meta.Color,
                    TotalCount = kvp.Value,
                    TotalCost  = totalCost,
                    DaysPlayed = daysPlayed,
                    AvgPerDay  = daysPlayed > 0 ? Math.Round((double)kvp.Value / daysPlayed, 1) : 0,
                };
            })
            .OrderByDescending(g => g.TotalCount)
            .ThenBy(g => g.GameKey)
            .ToList();

        // DailyStat list — newest first; games inside each day sorted by count desc
        var dailyStats = dailyDict
            .Select(kvp =>
            {
                var games = kvp.Value
                    .OrderByDescending(g => g.Value)
                    .Select(g => new DailyGameStat
                    {
                        Game     = g.Key,
                        GameName = gameMeta.TryGetValue(g.Key, out var m) ? m.Name : g.Key,
                        Count    = g.Value,
                    })
                    .ToList();

                decimal dayCost = kvp.Value.Sum(g =>
                {
                    if (g.Key == "D3")
                        return d3CostByDate.TryGetValue(kvp.Key, out var dc) ? dc : 0m;
                    if (g.Key == "HS")
                        return hsCostByDate.TryGetValue(kvp.Key, out var hc) ? hc : 0m;
                    return gameMeta.TryGetValue(g.Key, out var gm) ? g.Value * gm.CostEach : 0m;
                });

                return new DailyStat
                {
                    Date         = kvp.Key,
                    TotalTickets = kvp.Value.Values.Sum(),
                    TotalCost    = dayCost,
                    Games        = games,
                };
            })
            .OrderByDescending(d => d.Date)
            .ToList();

        int totalDays    = dailyDict.Count;
        int grandTotal   = gameTotals.Values.Sum();

        return new TicketPurchaseStatsResult
        {
            GeneratedAt       = DateTime.Now.ToString("M/d/yy h:mm tt"),
            GrandTotalTickets = grandTotal,
            TotalDays         = totalDays,
            AvgTicketsPerDay  = totalDays > 0 ? Math.Round((double)grandTotal / totalDays, 1) : 0,
            GameTotals        = gameStats,
            DailyStats        = dailyStats,
        };
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    static async Task SaveAsync(TicketPurchaseStatsResult result)
    {
        try
        {
            string json = JsonSerializer.Serialize(result, PrettyOpts);
            await File.WriteAllTextAsync(DataPath, json);
        }
        catch { }
    }
}
