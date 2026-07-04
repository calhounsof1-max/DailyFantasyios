using System.Text.Json;

namespace DailyFantasyMAUI.Services;

/// <summary>
/// Fetches the latest completed draw number for each CA Lottery game on app launch,
/// adds +1, and caches it. Daily 3 is re-fetched after 1 pm because it draws twice daily.
/// New game sales start at 6 am, so we treat the cache as fresh for the current calendar day.
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

    // in-memory cache: game name → next draw number
    static readonly Dictionary<string, int> _next = new();

    // when did we last fetch Daily 3?
    static DateTime _d3FetchedAt = DateTime.MinValue;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Call once on app startup. Loads today's cached values immediately,
    /// then fetches fresh numbers from the CA Lottery API in the background.
    /// </summary>
    public static async Task InitAsync()
    {
        LoadFromPrefs();                   // instant — show cached values right away
        await FetchAllAsync();             // background refresh
    }

    /// <summary>
    /// Returns the next (upcoming) draw number for the given game name,
    /// or 0 if not yet fetched.
    /// </summary>
    public static int GetNextDraw(string gameName) =>
        _next.TryGetValue(gameName, out int n) ? n : 0;

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

    static void LoadFromPrefs()
    {
        string today = DateTime.Today.ToString("yyyyMMdd");
        foreach (var g in _games)
        {
            if (Preferences.Get($"nd_date_{g.Name}", "") == today)
            {
                int n = Preferences.Get($"nd_{g.Name}", 0);
                if (n > 0) _next[g.Name] = n;
            }
        }
    }

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
            _next[game.Name] = next;
            Preferences.Set($"nd_{game.Name}", next);
            Preferences.Set($"nd_date_{game.Name}", DateTime.Today.ToString("yyyyMMdd"));

            if (game.Name == "Daily 3")
                _d3FetchedAt = DateTime.Now;
        }
        catch
        {
            // silently fail — cached value remains
        }
    }
}
