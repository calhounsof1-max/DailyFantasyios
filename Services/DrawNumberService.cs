using System.Text.Json;

namespace DailyFantasyMAUI.Services;

/// <summary>
/// Fetches the latest completed draw number for each CA Lottery game on app launch,
/// adds +1, and caches it. Daily 3 is re-fetched after 1 pm because it draws twice daily.
/// _next is ONLY populated by live API calls — never by preferences or backup restores.
/// </summary>
public static class DrawNumberService
{
    record GameDef(string Name, int DefaultId, string PrefKey);

    static readonly GameDef[] _games =
    [
        new("Fantasy 5",    10, "fantasy5_game_id"),
        new("Super Lotto",  8,  "sl_game_id"),
        new("Daily 3",      9,  ""),
        new("Daily 4",      14, ""),
        new("Powerball",    12, ""),
        new("Mega Millions",4,  "mm_game_id"),
        new("Daily Derby",  11, ""),
    ];

    // in-memory cache: game name → next draw number (live-fetched only)
    static readonly Dictionary<string, int> _next = new();

    // games that have been successfully fetched from the API this session
    static readonly HashSet<string> _fetched = new();

    // when did we last fetch Daily 3?
    static DateTime _d3FetchedAt = DateTime.MinValue;

    // task for the in-progress FetchAllAsync, so callers can await it
    static Task? _fetchAllTask;
    // date of the last completed full fetch (yyyyMMdd)
    static string _lastFetchDate = "";

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Call on app startup and on app resume. Fetches fresh draw numbers from
    /// the CA Lottery API. Safe to call multiple times — only re-fetches on a
    /// new calendar day. Never loads from preferences or backup.
    /// </summary>
    public static async Task InitAsync()
    {
        // If a fetch is already in progress, just wait for it
        if (_fetchAllTask != null) { await _fetchAllTask; return; }

        string today = DateTime.Today.ToString("yyyyMMdd");
        // Already fetched successfully today — nothing to do
        if (_lastFetchDate == today) return;

        // New day: clear previous session's cached values
        _next.Clear();
        _fetched.Clear();

        _fetchAllTask = FetchAllAsync();
        await _fetchAllTask;
        _fetchAllTask  = null;
        _lastFetchDate = today;
    }

    /// <summary>
    /// Returns the next (upcoming) draw number for the given game name,
    /// or 0 if not yet fetched this session.
    /// </summary>
    public static int GetNextDraw(string gameName) =>
        _fetched.Contains(gameName) && _next.TryGetValue(gameName, out int n) ? n : 0;

    /// <summary>
    /// Returns the next draw number from a live API fetch, waiting up to 8 seconds.
    /// If the batch fetch fails for this game, retries once with a direct call.
    /// Falls back to last stored preference only as absolute last resort.
    /// </summary>
    public static async Task<int> EnsureNextDrawAsync(string gameName)
    {
        // Wait for any in-progress batch fetch to complete
        var t = _fetchAllTask;
        if (t != null)
        {
            try { await t.WaitAsync(TimeSpan.FromSeconds(8)); }
            catch { /* timeout or cancelled */ }
        }

        // If the live fetch succeeded for this game, return it
        if (_fetched.Contains(gameName) && _next.TryGetValue(gameName, out int n) && n > 0)
            return n;

        // Batch fetch failed or timed out for this game — try a direct fetch,
        // with one extra retry after a short delay in case of a transient
        // weak-signal blip (a single attempt was found to fail intermittently).
        var game = _games.FirstOrDefault(g => g.Name == gameName);
        if (game != null)
        {
            await FetchGameAsync(game);
            if (!_fetched.Contains(gameName))
            {
                await Task.Delay(1500);
                await FetchGameAsync(game);
            }
        }

        // Return live value if a retry succeeded, else stale pref as last resort
        return _fetched.Contains(gameName) && _next.TryGetValue(gameName, out n) && n > 0
            ? n
            : Preferences.Get($"nd_{gameName}", 0);
    }

    /// <summary>
    /// Re-fetches Daily 3 if it is after 1 pm and we haven't fetched it
    /// since 1 pm today (midday draw has already happened → new draw # is next).
    /// Call this from Daily3Page.OnAppearing.
    /// </summary>
    public static async Task RefreshDaily3IfNeededAsync()
    {
        var now = DateTime.Now;
        bool afterNoon = now.Hour >= 13;
        bool fetchedBeforeNoon = _d3FetchedAt.Date < now.Date ||
                                 (_d3FetchedAt.Date == now.Date && _d3FetchedAt.Hour < 13);
        if (afterNoon && fetchedBeforeNoon)
            await FetchGameAsync(_games.First(g => g.Name == "Daily 3"));
    }

    // ── Implementation ────────────────────────────────────────────────────────

    static Task FetchAllAsync() =>
        Task.WhenAll(_games.Select(g => FetchGameAsync(g)));

    static async Task FetchGameAsync(GameDef game)
    {
        try
        {
            int gameId = string.IsNullOrEmpty(game.PrefKey)
                ? game.DefaultId
                : Preferences.Get(game.PrefKey, game.DefaultId);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/124.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "application/json, */*");
            client.DefaultRequestHeaders.Add("Referer", "https://www.calottery.com/");

            string url  = "https://www.calottery.com/api/DrawGameApi/" +
                          $"DrawGamePastDrawResults/{gameId}/1/1";
            string json = await client.GetStringAsync(url).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("PreviousDraws", out var draws) ||
                draws.GetArrayLength() == 0)
                return;

            var first = draws.EnumerateArray().First();
            if (!first.TryGetProperty("DrawNumber", out var dn)) return;

            int latest = dn.ValueKind == JsonValueKind.Number
                ? dn.GetInt32()
                : int.TryParse(dn.GetRawText().Trim('"'), out int parsed) ? parsed : 0;

            if (latest <= 0) return;

            int next = latest + 1;
            _next[game.Name]   = next;
            _fetched.Add(game.Name);                                   // mark as live-fetched
            Preferences.Set($"nd_{game.Name}", next);
            Preferences.Set($"nd_date_{game.Name}", DateTime.Today.ToString("yyyyMMdd"));

            if (game.Name == "Daily 3")
                _d3FetchedAt = DateTime.Now;
        }
        catch (Exception ex)
        {
            // _next not updated, _fetched not updated — logged so a future
            // "draw# didn't fill in" report can be diagnosed from logcat
            System.Diagnostics.Debug.WriteLine($"[DrawNumberService] fetch failed for {game.Name}: {ex.Message}");
        }
    }
}
