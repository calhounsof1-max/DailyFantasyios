namespace DailyFantasyMAUI;

/// <summary>
/// Carries the slot + row to highlight when a game page opens after tapping a winner in ResultsPage.
/// </summary>
public static class PendingHighlight
{
    public static string? Game { get; set; }   // "F5","SL","PB","MM","D3","D4","DD"
    public static int     Slot { get; set; }   // 0-based slot index
    public static int     Row  { get; set; }   // 0-based row index
    public static bool HasPending => Game != null;
    public static void Clear() => Game = null;
}
