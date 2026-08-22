namespace DailyFantasyMAUI.Services;

/// <summary>
/// Manages per-page "Enhance Mode" preferences (plain text vs. bubble/ball UI).
/// Default for all pages is Classic (plain text) unless the user turns on Enhanced.
/// Provides a reusable dialog so any page can expose the toggle.
/// </summary>
public static class EnhanceModeService
{
    // ── Preference keys — one per page that supports Enhance Mode ────────────
    public const string ResultsPageKey   = "enhance_results";
    public const string SummaryPageKey   = "enhance_summary";
    public const string TicketLogPageKey = "enhance_ticketlog";
    public const string MainListKey      = "main_show_bubbles";

    // ── Read / write ──────────────────────────────────────────────────────────

    /// <summary>Returns true when the given page is in Enhanced (bubble) mode.</summary>
    public static bool IsEnhanced(string key) => Preferences.Get(key, false);

    /// <summary>
    /// Fired whenever any enhance-mode setting changes so interested pages can
    /// update their UI immediately (e.g. MainPage indicator on the Results button).
    /// </summary>
    public static event Action? SettingChanged;

    // ── Dialog ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the Enhance Mode picker dialog.
    /// Lists every page that supports the feature with its current on/off state.
    /// Returns after the user dismisses the dialog.
    /// </summary>
    /// <summary>
    /// Shows the Enhance Mode dialog. Returns true if the user pressed "Back"
    /// (caller should re-open the previous menu), false if they pressed "Close".
    /// </summary>
    public static async Task<bool> ShowDialogAsync(Page host)
    {
        while (true)
        {
            bool resultsOn   = IsEnhanced(ResultsPageKey);
            bool summaryOn   = IsEnhanced(SummaryPageKey);
            bool ticketLogOn = IsEnhanced(TicketLogPageKey);
            bool mainListOn  = IsEnhanced(MainListKey);

            string resultsOption   = resultsOn   ? "✅  Results Page"    : "○  Results Page";
            string summaryOption   = summaryOn   ? "✅  Wins & Spending" : "○  Wins & Spending";
            string ticketLogOption = ticketLogOn ? "✅  Ticket Log"      : "○  Ticket Log";
            string mainListOption  = mainListOn  ? "✅  Home List Bubbles" : "○  Home List Bubbles";

            string? choice = await host.DisplayActionSheet(
                "✨ Enhance Mode", "◀ Back", "Close",
                resultsOption, summaryOption, ticketLogOption, mainListOption);

            if (choice == null || choice == "Close") return false;  // fully dismiss
            if (choice == "◀ Back")                  return true;   // go back to previous menu

            if (choice.Contains("Results Page"))
            {
                Preferences.Set(ResultsPageKey, !resultsOn);
                ResultsPage.NeedsRefresh = true;
                SettingChanged?.Invoke();
            }
            else if (choice.Contains("Wins & Spending"))
            {
                Preferences.Set(SummaryPageKey, !summaryOn);
                SummaryPage.NeedsRefresh = true;
                SettingChanged?.Invoke();
            }
            else if (choice.Contains("Ticket Log"))
            {
                Preferences.Set(TicketLogPageKey, !ticketLogOn);
                TicketLogPage.NeedsRefresh = true;
                SettingChanged?.Invoke();
            }
            else if (choice.Contains("Home List Bubbles"))
            {
                Preferences.Set(MainListKey, !mainListOn);
                SettingChanged?.Invoke();
            }
            // Loop — redisplay with updated toggle states
        }
    }
}
