namespace DailyFantasyMAUI.Services;

// Non-UI Hot Spot draw-fetch + prize-lookup logic, shared between HotSpotPage.cs (the
// interactive page) and HotSpotChecker.cs (the background auto-check run from
// WinCheckReceiver). Extracted 2026-08-11 so a background job can check a saved ticket
// without constructing the page's full on-screen UI tree — the scraping/prize logic
// itself never touched any control, only instance fields the page used purely as
// session-scoped caches, which callers now pass in explicitly (or omit).
//
// See HotSpotPage.cs's own header comment for why Hot Spot's live data has to be
// scraped from calottery.com's server-rendered "Past Winning Numbers" page instead of
// the small-integer-game-ID JSON API the other 7 games use, and the hard-won gotchas
// (no stray query params, latest-draw fetch is CDN-cached several minutes, a
// just-posted draw # isn't safely queryable for ~2 minutes) baked into FetchDrawAsync
// below — those constraints are identical for a background caller.
public static class HotSpotDrawService
{
    // Shared across every fetch — a fresh HttpClient per call was confirmed live to add a
    // full DNS+TCP+TLS handshake to every single request.
    public static readonly HttpClient SharedHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    static HotSpotDrawService() => SharedHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

    // Base $1-wager prize table, confirmed against calottery.com: spot count -> match
    // count -> prize. Absent match counts pay nothing (sparse by design).
    public static readonly Dictionary<int, Dictionary<int, decimal>> BasePrizes = new()
    {
        [1]  = new() { [1] = 2m },
        [2]  = new() { [2] = 10m },
        [3]  = new() { [2] = 2m, [3] = 25m },
        [4]  = new() { [2] = 1m, [3] = 4m, [4] = 78m },
        [5]  = new() { [3] = 2m, [4] = 15m, [5] = 435m },
        [6]  = new() { [3] = 1m, [4] = 6m, [5] = 67m, [6] = 900m },
        [7]  = new() { [3] = 1m, [4] = 3m, [5] = 12m, [6] = 190m, [7] = 2000m },
        [8]  = new() { [0] = 1m, [5] = 12m, [6] = 80m, [7] = 550m, [8] = 10000m },
        [9]  = new() { [0] = 1m, [5] = 6m, [6] = 30m, [7] = 135m, [8] = 2750m, [9] = 30000m },
        [10] = new() { [0] = 2m, [5] = 3m, [6] = 17m, [7] = 42m, [8] = 575m, [9] = 5000m, [10] = 100000m },
    };

    // "Hot Spot & Bulls-Eye" combo prize table (see HotSpotPage.cs history for provenance).
    public static readonly Dictionary<int, Dictionary<int, decimal>> BullseyePrizes = new()
    {
        [1]  = new() { [1] = 54m },
        [2]  = new() { [1] = 15m, [2] = 70m },
        [3]  = new() { [1] = 6m, [2] = 20m, [3] = 155m },
        [4]  = new() { [1] = 4m, [2] = 11m, [3] = 37m, [4] = 300m },
        [5]  = new() { [1] = 3m, [2] = 6m, [3] = 16m, [4] = 80m, [5] = 1000m },
        [6]  = new() { [1] = 5m, [2] = 2m, [3] = 7m, [4] = 40m, [5] = 250m, [6] = 2000m },
        [7]  = new() { [1] = 5m, [2] = 2m, [3] = 3m, [4] = 20m, [5] = 68m, [6] = 575m, [7] = 10000m },
        [8]  = new() { [0] = 1m, [1] = 5m, [2] = 2m, [3] = 2m, [4] = 7m, [5] = 50m, [6] = 200m, [7] = 1300m, [8] = 30000m },
        [9]  = new() { [0] = 1m, [1] = 5m, [2] = 2m, [3] = 2m, [4] = 5m, [5] = 20m, [6] = 90m, [7] = 475m, [8] = 6000m, [9] = 65000m },
        [10] = new() { [0] = 2m, [1] = 5m, [2] = 2m, [3] = 2m, [4] = 2m, [5] = 9m, [6] = 60m, [7] = 215m, [8] = 1400m, [9] = 15000m, [10] = 300000m },
    };
    static readonly HashSet<int> TypicalPoolSpotCounts = new() { 8, 9, 10 };

    public static bool IsTypicalPool(int spots, int matches) => TypicalPoolSpotCounts.Contains(spots) && matches == spots;
    public static bool IsTypicalPoolSpotCount(int spots) => TypicalPoolSpotCounts.Contains(spots);

    public static decimal? BasePrizeFor(int spots, int matches)
        => BasePrizes.TryGetValue(spots, out var tiers) && tiers.TryGetValue(matches, out var amt) ? amt : null;

    public static decimal? BullseyePrizeFor(int spots, int matches)
        => BullseyePrizes.TryGetValue(spots, out var tiers) && tiers.TryGetValue(matches, out var amt) ? amt : null;

    public static decimal? PrizeFor(int spots, int matches, bool bullseyeHit)
        => bullseyeHit ? (BullseyePrizeFor(spots, matches) ?? BasePrizeFor(spots, matches)) : BasePrizeFor(spots, matches);

    // Public — TryFastDetectLatestDrawAsync (still in HotSpotPage.cs, since it depends on
    // page-session state) reuses DrawNumberRx/DrawTimeRx against the main Hot Spot page's
    // markup, which shares the same "current-drawNumber"/"Draw Time" fields.
    public static readonly System.Text.RegularExpressions.Regex DrawNumberRx =
        new(@"current-drawNumber"">(\d+)");
    public static readonly System.Text.RegularExpressions.Regex DrawTimeRx =
        new(@"Draw Time:\s*<strong>([^<]+)</strong>");
    static readonly System.Text.RegularExpressions.Regex DrawDateRx =
        new(@"Draw Date:\s*<strong class=""caps-texts"">([^<]+)</strong>");
    static readonly System.Text.RegularExpressions.Regex BlueNumRx =
        new(@"sr-only blue-num"">(\d+)</li>");
    static readonly System.Text.RegularExpressions.Regex BullseyeRx =
        new(@"sr-only orange-num"">[^<]*?(\d+)\s*</li>");

    public readonly record struct DrawResult(bool Ok, int[] Numbers, int BullseyeNumber, int DrawNumber, string DrawLabel, DateTime DrawTime);

    // drawNumber=null fetches the latest/current draw; passing a specific 7-digit draw
    // number (via calottery.com's own "?query=" param) fetches that exact past draw.
    // `cache` is an optional session/run-scoped cache for specific past draw numbers
    // (never for the latest/null lookup, which changes every few minutes) — pass the
    // same dictionary across a batch of calls (e.g. checking a multi-draw range) to
    // avoid re-fetching a draw already seen this run; omit it for a one-off call.
    public static async Task<DrawResult> FetchDrawAsync(int? queryDrawNumber = null, Dictionary<int, DrawResult>? cache = null)
    {
        if (queryDrawNumber.HasValue && cache != null && cache.TryGetValue(queryDrawNumber.Value, out var cached))
            return cached;

        try
        {
            // IMPORTANT: any extra/unrecognized query param on this endpoint (even a harmless
            // cache-busting one) makes the site fall back to an arbitrary historical draw —
            // confirmed live. A real "?query=<realDrawNumber>" is safe and accurate, and a
            // completely bare URL (no query string) is safe too (just possibly a few minutes
            // stale from CDN caching) — anything else is not.
            string url = "https://www.calottery.com/en/draw-games/hot-spot/past-winning-numbers"
                + (queryDrawNumber.HasValue ? $"?query={queryDrawNumber.Value}" : "");
            string html = await SharedHttpClient.GetStringAsync(url);

            var numbers = BlueNumRx.Matches(html).Select(m => int.Parse(m.Groups[1].Value)).ToArray();
            var bullseyeMatch = BullseyeRx.Match(html);
            int bullseye = bullseyeMatch.Success ? int.Parse(bullseyeMatch.Groups[1].Value) : 0;
            var drawNumMatch = DrawNumberRx.Match(html);
            int drawNumber = drawNumMatch.Success ? int.Parse(drawNumMatch.Groups[1].Value) : 0;
            string date = DrawDateRx.Match(html) is { Success: true } dm ? dm.Groups[1].Value.Trim() : "";
            string time = DrawTimeRx.Match(html) is { Success: true } tm ? tm.Groups[1].Value.Trim() : "";
            string label = drawNumber > 0 ? $"{date} {time}".Trim() : "";
            string normalizedTime = System.Text.RegularExpressions.Regex.Replace(
                time, @"([ap])\.m\.", m => m.Groups[1].Value.ToUpperInvariant() + "M",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            DateTime.TryParse($"{date} {normalizedTime}", out var drawTime);

            if (numbers.Length == 19 && bullseye > 0 && drawNumber > 0)
            {
                var result = new DrawResult(true, numbers, bullseye, drawNumber, label, drawTime);
                // Only cache when the response actually echoed back the draw # asked for — a
                // mismatched fallback must never get cached under the wrong key.
                if (queryDrawNumber.HasValue && cache != null && drawNumber == queryDrawNumber.Value)
                    cache[queryDrawNumber.Value] = result;
                return result;
            }

            _ = Logger.LogAsync($"HS: parsed {numbers.Length} numbers, drawNumber={drawNumber}, bullseye={bullseye}, htmlLen={html.Length}");
        }
        catch (Exception ex) { _ = Logger.LogAsync($"HS: fetch failed — {ex.GetType().Name}: {ex.Message}"); }
        return new DrawResult(false, Array.Empty<int>(), 0, 0, "", DateTime.MinValue);
    }

    // Matches/prize for one saved ticket's picks against one fetched draw. Shared by the
    // interactive page's CheckRangeAsync and the background checker's equivalent loop.
    public static (int matches, bool bullseyeHit, decimal winAmount) Score(
        int[] picks, bool bullseyeEnabled, int spots, decimal wager, DrawResult draw)
    {
        var drawnSet = draw.Numbers.ToHashSet();
        if (draw.BullseyeNumber > 0) drawnSet.Add(draw.BullseyeNumber);
        int matches = picks.Count(n => drawnSet.Contains(n));
        bool bullseyeHit = bullseyeEnabled && draw.BullseyeNumber > 0 && picks.Contains(draw.BullseyeNumber);
        decimal winAmount = (PrizeFor(spots, matches, bullseyeHit) ?? 0m) * wager;
        return (matches, bullseyeHit, winAmount);
    }
}
