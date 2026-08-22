namespace DailyFantasyMAUI.Services;

// ── Self-contained "Last 200 Draws" data source ──────────────────────────────────────────
// Not to be confused with HSPast10Days — that one only saves WINS over a rolling 10-day
// window. This is a plain, read-only feed of the most recent raw Hot Spot draws (numbers +
// Bulls-eye, no ticket/win logic at all) meant to be watched live while a ticket is active.
// UI lives entirely in HotSpotPage (BuildLast200DrawsOverlay/OpenLast200DrawsAsync) — this
// file only knows how to get 200 draws' worth of data, same "own file, easy to delete
// later" split HSPast10Days.cs uses for its own data half.
//
// Source of truth is HSRecentDraws' rolling cache (sized to 200 — see that file). If the
// cache doesn't have 200 yet (fresh install, or the cap was just raised from 100), this
// backfills the gap by walking backward from the oldest cached draw # and fetching each one
// directly via HotSpotDrawService — same fetch path, same 150ms pacing between live
// requests, HSPast10Days already uses for its own range walk.
public static class HSLast200Draws
{
    public const int DrawCount = 200;

    // Stop walking backward after this many straight misses — means backfill has run out of
    // real history to find (e.g. walked past this device's very first-ever cached draw).
    const int MaxBackfillMisses = 5;

    public static bool IsRunning { get; private set; }

    // currentDrawHint anchors the very first backfill when the cache is completely empty
    // (fresh install) — pass the caller's best-known current/last-seen draw #. onProgress is
    // called with a short status string while backfilling so the caller can show it isn't
    // stuck (a full 100-draw backfill takes ~15+ seconds at 150ms/fetch).
    public static async Task<List<HotSpotDrawService.DrawResult>> LoadAsync(int currentDrawHint, Action<string>? onProgress = null)
    {
        if (IsRunning) return await ReadCacheAsync(); // don't overlap a run already in flight — just return what's cached so far
        IsRunning = true;
        try
        {
            var cached = await HSRecentDraws.LoadAllAsync();
            int need = DrawCount - cached.Count;
            int oldest = cached.Count > 0 ? cached.Min(e => e.DrawNumber) : currentDrawHint;

            if (need > 0 && oldest > 0)
            {
                var fetchCache = new Dictionary<int, HotSpotDrawService.DrawResult>();
                int misses = 0;
                int fetched = 0;
                int dn = cached.Count > 0 ? oldest - 1 : oldest;

                while (fetched < need && misses < MaxBackfillMisses && dn > 0)
                {
                    onProgress?.Invoke($"Loading draw #{dn}… ({fetched + 1} of {need})");
                    var draw = await HotSpotDrawService.FetchDrawAsync(dn, fetchCache);
                    await Task.Delay(150); // same deliberate pacing HSPast10Days uses so a big backfill doesn't hammer calottery.com
                    if (draw.Ok)
                    {
                        await HSRecentDraws.AddAsync(draw);
                        misses = 0;
                        fetched++;
                    }
                    else
                    {
                        misses++;
                    }
                    dn--;
                }
            }

            return await ReadCacheAsync();
        }
        finally { IsRunning = false; }
    }

    static async Task<List<HotSpotDrawService.DrawResult>> ReadCacheAsync()
    {
        var all = await HSRecentDraws.LoadAllAsync();
        var results = new List<HotSpotDrawService.DrawResult>();
        foreach (var e in all)
        {
            int[] numbers;
            try { numbers = e.Numbers.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray(); }
            catch { continue; }
            DateTime.TryParse(e.DrawTime, out var drawTime);
            results.Add(new HotSpotDrawService.DrawResult(true, numbers, e.BullseyeNumber, e.DrawNumber, "", drawTime));
        }
        return results.OrderByDescending(r => r.DrawNumber).Take(DrawCount).ToList();
    }
}
