namespace DailyFantasyMAUI;

/// <summary>Tracks which direction the next page should slide in from.</summary>
internal static class NavDir
{
    /// <summary>True = slide in from right (going back); False = slide in from left (going forward).</summary>
    public static bool FromRight { get; set; }
}
