namespace DailyFantasyMAUI.Services;

public static class AdvancePlayNotificationService
{
    record GameDef(string Prefix, string Name);

    static readonly GameDef[] Games =
    [
        new("f5", "Fantasy 5"),
        new("sl", "Super Lotto"),
        new("pb", "Powerball"),
        new("mm", "Mega Millions"),
        new("d3", "Daily 3"),
        new("d4", "Daily 4"),
        new("dd", "Daily Derby"),
    ];

    const int MaxSlots = 10;
    const int MaxRows  = 10;

    public record AdvEntry(string Game, int Slot, int Row, DateTime End, int DaysLeft);

    /// Reads all advance-play end dates from Preferences, sorted soonest-expiring first.
    public static List<AdvEntry> ScanAll()
    {
        var today = DateTime.Today;
        var list  = new List<AdvEntry>();

        foreach (var g in Games)
        {
            for (int slot = 0; slot < MaxSlots; slot++)
            {
                string raw = Preferences.Get($"{g.Prefix}_adv_{slot}", "");
                if (string.IsNullOrEmpty(raw)) continue;

                string[] rows = raw.Split('|');
                for (int r = 0; r < MaxRows && r < rows.Length; r++)
                {
                    string[] pair = rows[r].Split('~');
                    if (pair.Length < 2) continue;
                    if (!DateTime.TryParseExact(pair[1], "yyyyMMdd", null,
                            System.Globalization.DateTimeStyles.None, out var end)) continue;
                    list.Add(new AdvEntry(g.Name, slot, r, end, (int)(end - today).TotalDays));
                }
            }
        }

        return [.. list.OrderBy(e => e.DaysLeft)];
    }

    /// Returns a formatted multi-line summary of all active advance-play dates.
    public static string BuildSummary()
    {
        var active = ScanAll().Where(e => e.DaysLeft >= 0).ToList();
        if (active.Count == 0) return "No active advance play dates.";

        var sb = new System.Text.StringBuilder();
        foreach (var grp in active.GroupBy(e => e.Game))
        {
            var soonest = grp.OrderBy(e => e.DaysLeft).First();
            string days = soonest.DaysLeft == 0 ? "expires TODAY"
                        : soonest.DaysLeft == 1 ? "1 day left"
                        : $"{soonest.DaysLeft} days left";
            sb.AppendLine($"{grp.Key}: {days}  (thru {soonest.End:M/d/yy})");
        }
        return sb.ToString().TrimEnd();
    }

    /// Set by CheckAndNotify() when games are expiring soon.
    /// MainPage reads and clears this to show an in-app alert on launch.
    public static string? PendingLaunchTitle  { get; private set; }
    public static string? PendingLaunchBody   { get; private set; }

    /// On app launch: stores expiring games (≤1 day) for in-app display only.
    public static void CheckAndNotify()
    {
        var active = ScanAll()
            .Where(e => e.DaysLeft >= 0 && e.DaysLeft <= 1)
            .GroupBy(e => e.Game)
            .Select(g =>
            {
                var first = g.OrderBy(e => e.DaysLeft).First();
                string days = first.DaysLeft == 0 ? "expires TODAY"
                            : first.DaysLeft == 1 ? "1 day left"
                            : $"{first.DaysLeft} days left";
                return $"• {g.Key}: {days}  (thru {first.End:M/d/yy})";
            })
            .ToList();

        if (active.Count == 0) return;

        PendingLaunchTitle = active.Count == 1
            ? "Advance Play Expiring Soon"
            : $"Advance Play — {active.Count} Games Expiring Soon";
        PendingLaunchBody = string.Join("\n", active);
    }

    /// Called by MainPage after showing the launch alert to clear the pending message.
    public static void ClearLaunchAlert()
    {
        PendingLaunchTitle = null;
        PendingLaunchBody  = null;
    }
}
