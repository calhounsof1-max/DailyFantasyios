using System.Text.Json;

namespace DailyFantasyMAUI.Services;

// Rolling local cache of the last 200 Hot Spot draws, so repeat lookups (mainly
// HSPast10Days's draw-range walk and HSLast200Draws's live viewer) don't have to hit
// calottery.com for a draw this device already fetched recently. Fed from two places:
// HotSpotPage's foreground auto-refresh (every ~4 min while the page is open) and
// HSRecentDrawsReceiver's background alarm (every ~5 min even with the app closed, skipping
// the 2am-6am closed window) — see that receiver for why a dedicated always-on alarm was
// used instead of piggybacking on HotSpotFastCheckScheduler (that one only arms itself
// while a ticket is pending, which wouldn't give continuous coverage here). Raised from 100
// to 200 to back HSLast200Draws — 200 draws is ~13 hours, comfortably past 100 (~6.7 hours).
public static class HSRecentDraws
{
    public class Entry
    {
        public int      DrawNumber     { get; set; }
        public string   Numbers        { get; set; } = ""; // space-separated, the 20 drawn numbers
        public int      BullseyeNumber { get; set; }
        public string   DrawTime       { get; set; } = ""; // round-tripped via "o" format, "" if never parsed
    }

    const string FileName = "HSRecentDraws.json";
    const int    MaxDraws = 200;

    static string DataPath => Path.Combine(FileSystem.AppDataDirectory, FileName);

    // Guards the read-modify-write in AddAsync — the foreground auto-refresh and the
    // background alarm can both call this around the same time.
    static readonly SemaphoreSlim _lock = new(1, 1);

    public static async Task<List<Entry>> LoadAllAsync()
    {
        try
        {
            if (!File.Exists(DataPath)) return new();
            string json = await File.ReadAllTextAsync(DataPath);
            return JsonSerializer.Deserialize<List<Entry>>(json) ?? new();
        }
        catch { return new(); }
    }

    // Looks up one draw# in the cache without loading/parsing the whole file more than
    // once per call — fine for HSPast10Days's per-draw loop since each draw# is only
    // ever looked up a handful of times per run.
    public static async Task<HotSpotDrawService.DrawResult?> TryGetAsync(int drawNumber)
    {
        var all = await LoadAllAsync();
        var hit = all.FirstOrDefault(e => e.DrawNumber == drawNumber);
        if (hit == null) return null;

        int[] numbers;
        try { numbers = hit.Numbers.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray(); }
        catch { return null; }

        DateTime.TryParse(hit.DrawTime, out var drawTime);
        return new HotSpotDrawService.DrawResult(true, numbers, hit.BullseyeNumber, hit.DrawNumber, "", drawTime);
    }

    public static async Task AddAsync(HotSpotDrawService.DrawResult draw)
    {
        if (!draw.Ok || draw.DrawNumber <= 0) return;

        await _lock.WaitAsync();
        try
        {
            var all = await LoadAllAsync();
            if (all.Any(e => e.DrawNumber == draw.DrawNumber)) return; // already cached, nothing to do

            all.Add(new Entry
            {
                DrawNumber     = draw.DrawNumber,
                Numbers        = string.Join(" ", draw.Numbers),
                BullseyeNumber = draw.BullseyeNumber,
                DrawTime       = draw.DrawTime > DateTime.MinValue ? draw.DrawTime.ToString("o") : "",
            });

            var trimmed = all.OrderByDescending(e => e.DrawNumber).Take(MaxDraws).OrderBy(e => e.DrawNumber).ToList();
            string json = JsonSerializer.Serialize(trimmed);
            await File.WriteAllTextAsync(DataPath, json);
        }
        catch { /* best-effort cache — a write failure here should never break the caller */ }
        finally { _lock.Release(); }
    }
}
