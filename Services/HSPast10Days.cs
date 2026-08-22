using System.Text;
using System.Text.Json;
using DailyFantasyMAUI;

namespace DailyFantasyMAUI.Services;

// ── Self-contained "Past 10 Days" Hot Spot win-checker ───────────────────────
// Deliberately kept in this ONE FILE, separate from HotSpotPage.cs/HotSpotChecker.cs/
// HotSpotDrawService.cs, at the user's explicit request — this whole feature can be
// deleted outright later (this file + its own JSON store + its own Downloads export)
// without touching any of the existing Hot Spot code it borrows from.
//
// What it reuses rather than reimplements: HotSpotDrawService.FetchDrawAsync/Score for
// the actual draw-fetch + payout math (same source of truth as every other win check in
// the app), and TicketLogService's existing "HS" ticket_log rows as its only input data
// (no separate ticket store — those rows already have everything: purchase date, picks,
// spots/wager/bullseye via the Extra field).
//
// Entry point: RunCheckAsync(startDraw, endDraw), called fire-and-forget from
// HotSpotPage's "📆 Past 10 Days" dialog. Only ever touches Preferences/files, never a UI
// control, so it's safe to keep running after the dialog that started it has closed.
public static class HSPast10Days
{
    public class WinRow
    {
        public string  PurchasedDate  { get; set; } = ""; // yyyy-MM-dd, from the ticket_log row
        public int     DrawNumber     { get; set; }
        public string  DrawDate       { get; set; } = "";
        public string  Numbers        { get; set; } = ""; // the ticket's picks, space-separated
        public string  MatchedNumbers { get; set; } = ""; // just the ones that hit
        public int     Matches        { get; set; }
        public int     Spots          { get; set; }
        public decimal Wager          { get; set; }
        public bool    Bullseye       { get; set; }
        public decimal Amount         { get; set; }
        public DateTime FoundAt       { get; set; } // when this scan found it — drives the 10-day rolling prune
    }

    const string FileName      = "HSPast10Days.json"; // app-private store, source of truth
    const string ExportFolder  = "HSPast10Days";
    const string ExportFile    = "HSPast10Days.txt";   // human-readable table, written to Downloads
    const int    KeepDays      = 10;
    const int    MaxDrawsPerRun = 4000; // ~11 days of Hot Spot draws (~4 min apart) — a sane cap against a fat-fingered range never finishing

    // Per-device daily cap — each install throttles itself independently (there's no shared
    // server here to rate-limit across installs), so this bounds how much any one phone can
    // hit calottery.com with this tool, regardless of how many times "Check" gets tapped.
    public const int MaxRunsPerDay = 2;
    const string PrefRunDate  = "hs_p10_run_date";  // yyyy-MM-dd the count below applies to
    const string PrefRunCount = "hs_p10_run_count";

    static string DataPath => Path.Combine(FileSystem.AppDataDirectory, FileName);

    // How many runs this device has already started today (resets automatically once the
    // stored date rolls to a new day — nothing to prune/reset explicitly).
    public static int RunsUsedToday()
    {
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        return Preferences.Get(PrefRunDate, "") == today ? Preferences.Get(PrefRunCount, 0) : 0;
    }

    static void RecordRunStarted()
    {
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        int count = Preferences.Get(PrefRunDate, "") == today ? Preferences.Get(PrefRunCount, 0) : 0;
        Preferences.Set(PrefRunDate, today);
        Preferences.Set(PrefRunCount, count + 1);
    }

    public static async Task<List<WinRow>> LoadAllAsync()
    {
        try
        {
            if (!File.Exists(DataPath)) return new();
            string json = await File.ReadAllTextAsync(DataPath);
            return JsonSerializer.Deserialize<List<WinRow>>(json) ?? new();
        }
        catch { return new(); }
    }

    static async Task SaveAllAsync(List<WinRow> rows)
    {
        var cutoff = DateTime.Today.AddDays(-KeepDays);
        var pruned = rows.Where(r => r.FoundAt.Date >= cutoff).ToList();
        string json = JsonSerializer.Serialize(pruned);
        await File.WriteAllTextAsync(DataPath, json);
    }

    // True from the moment a run starts until it fully finishes (success, error, or nothing
    // to check) — HotSpotPage reads this to keep the Check button disabled and won't start a
    // second overlapping run while one is already going.
    public static bool IsRunning { get; private set; }

    public static async Task RunCheckAsync(int startDraw, int endDraw)
    {
        if (IsRunning) return; // extra safety net even though the UI already guards against this
        string phone = Preferences.Get("notif_phone", "");
        if (RunsUsedToday() >= MaxRunsPerDay)
        {
            SmsHelper.SendSms(phone, $"Hot Spot Past 10 Days: already used {MaxRunsPerDay} checks today — try again tomorrow.");
            return;
        }
        IsRunning = true;
        RecordRunStarted();
        try
        {
            if (endDraw < startDraw) (startDraw, endDraw) = (endDraw, startDraw);
            bool capped = (endDraw - startDraw + 1) > MaxDrawsPerRun;
            if (capped) endDraw = startDraw + MaxDrawsPerRun - 1;

            var cutoffDate = DateTime.Today.AddDays(-KeepDays);
            var tickets = (await TicketLogService.LoadAllAsync())
                .Where(t => t.Game == "HS")
                .Where(t => DateTime.TryParse(t.Date, out var d) && d.Date >= cutoffDate)
                .ToList();

            if (tickets.Count == 0)
            {
                await WriteExportAsync(await LoadAllAsync(), startDraw, endDraw);
                SmsHelper.SendSms(phone, "Hot Spot Past 10 Days: no tickets found in the last 10 days — nothing to check.");
                return;
            }

            // Pre-parse each ticket's picks/spots/wager/bullseye once (Extra format: "SP:x|W:y|BE:z")
            // rather than per-draw, since the draw loop below is the outer loop.
            var parsed = new List<(string Date, int[] Picks, int Spots, decimal Wager, bool Bullseye)>();
            foreach (var t in tickets)
            {
                int[] picks;
                try { picks = t.Numbers.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray(); }
                catch { continue; }
                if (picks.Length == 0) continue;

                int spots = picks.Length;
                decimal wager = 1m;
                bool bullseye = false;
                foreach (var part in t.Extra.Split('|', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split(':', 2);
                    if (kv.Length != 2) continue;
                    if (kv[0] == "SP" && int.TryParse(kv[1], out int sp)) spots = sp;
                    else if (kv[0] == "W" && decimal.TryParse(kv[1], out decimal w)) wager = w;
                    else if (kv[0] == "BE") bullseye = kv[1] == "1";
                }
                parsed.Add((t.Date, picks, spots, wager, bullseye));
            }

            var cache = new Dictionary<int, HotSpotDrawService.DrawResult>();
            var newWins = new List<WinRow>();
            var runTime = DateTime.Now;

            for (int dn = startDraw; dn <= endDraw; dn++)
            {
                // HSRecentDraws holds the last ~100 draws this device has already fetched
                // (foreground auto-refresh + the background HSRecentDrawsReceiver alarm) —
                // check it first so a range that overlaps recent draws doesn't re-hit
                // calottery.com for numbers this device already has.
                var draw = await HSRecentDraws.TryGetAsync(dn);
                if (draw is { } cached)
                {
                    if (!cached.Ok) continue;
                    ScoreDrawAgainstTickets(cached, parsed, newWins, runTime);
                    continue;
                }

                var fetched = await HotSpotDrawService.FetchDrawAsync(dn, cache);
                // A small, deliberate pause between real fetches (not on a cache hit — cache
                // hits above skip this entirely) so a large range doesn't hammer
                // calottery.com back-to-back with no delay at all.
                await Task.Delay(150);
                if (!fetched.Ok) continue;
                _ = HSRecentDraws.AddAsync(fetched); // fire-and-forget: feed the cache for next time
                ScoreDrawAgainstTickets(fetched, parsed, newWins, runTime);
            }

            var existing = await LoadAllAsync();
            var existingKeys = existing.Select(r => $"{r.PurchasedDate}_{r.DrawNumber}_{r.Numbers}").ToHashSet();
            var reallyNew = newWins.Where(w => !existingKeys.Contains($"{w.PurchasedDate}_{w.DrawNumber}_{w.Numbers}")).ToList();
            existing.AddRange(reallyNew);
            await SaveAllAsync(existing);

            var finalRows = await LoadAllAsync(); // re-read post-prune, so the export matches exactly what's kept
            await WriteExportAsync(finalRows, startDraw, endDraw);

            decimal newTotal = reallyNew.Sum(w => w.Amount);
            string capNote = capped ? $" (range was capped at {MaxDrawsPerRun} draws, up to #{endDraw})" : "";
            string msg = reallyNew.Count > 0
                ? $"Hot Spot Past 10 Days: {reallyNew.Count} win(s) found, ${newTotal:N2} total. Draws #{startDraw}-#{endDraw}{capNote}."
                : $"Hot Spot Past 10 Days: finished, no wins in draws #{startDraw}-#{endDraw}{capNote}.";
            SmsHelper.SendSms(phone, msg);
        }
        catch (Exception ex)
        {
            _ = Logger.LogAsync($"HSPast10Days: run failed — {ex.GetType().Name}: {ex.Message}");
            SmsHelper.SendSms(phone, "Hot Spot Past 10 Days check hit an error and didn't finish — open the app to retry.");
        }
        finally
        {
            IsRunning = false;
        }
    }

    // Shared scoring body for one draw against every parsed ticket, used from both the
    // cache-hit and live-fetch branches of RunCheckAsync's loop so neither path duplicates
    // the win-row-building logic.
    static void ScoreDrawAgainstTickets(HotSpotDrawService.DrawResult draw,
        List<(string Date, int[] Picks, int Spots, decimal Wager, bool Bullseye)> parsed,
        List<WinRow> newWins, DateTime runTime)
    {
        var drawnSet = draw.Numbers.ToHashSet();
        if (draw.BullseyeNumber > 0) drawnSet.Add(draw.BullseyeNumber);
        string drawDate = draw.DrawTime > DateTime.MinValue
            ? draw.DrawTime.ToString("yyyy-MM-dd h:mm tt")
            : draw.DrawLabel;

        foreach (var (date, picks, spots, wager, bullseye) in parsed)
        {
            var (matches, bullseyeHit, winAmount) = HotSpotDrawService.Score(picks, bullseye, spots, wager, draw);
            if (winAmount <= 0) continue;

            newWins.Add(new WinRow
            {
                PurchasedDate  = date,
                DrawNumber     = draw.DrawNumber,
                DrawDate       = drawDate,
                Numbers        = string.Join(" ", picks.OrderBy(n => n)),
                MatchedNumbers = string.Join(" ", picks.Where(n => drawnSet.Contains(n)).OrderBy(n => n)),
                Matches        = matches,
                Spots          = spots,
                Wager          = wager,
                Bullseye       = bullseyeHit,
                Amount         = winAmount,
                FoundAt        = runTime,
            });
        }
    }

    // Plain fixed-width text table — opens in any file viewer, no spreadsheet app needed.
    // Overwritten every run (Downloads/HSPast10Days/HSPast10Days.txt) so it always reflects
    // exactly the current rolling 10-day window kept in HSPast10Days.json.
    static async Task WriteExportAsync(List<WinRow> rows, int lastStartDraw, int lastEndDraw)
    {
        var ordered = rows.OrderByDescending(r => r.PurchasedDate).ThenBy(r => r.DrawNumber).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("HOT SPOT — PAST 10 DAYS — WINS ONLY");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd h:mm tt}");
        sb.AppendLine($"Last range checked: draws #{lastStartDraw}-#{lastEndDraw}");
        sb.AppendLine("Rolling window — entries older than 10 days drop off automatically on the next check.");
        sb.AppendLine();

        if (ordered.Count == 0)
        {
            sb.AppendLine("No wins found in the last 10 days.");
        }
        else
        {
            string[] headers = { "Purchased", "Draw #", "Draw Date/Time", "Picked", "Matched", "Spots", "Wager", "BullsEye", "Won" };
            var rowsOut = ordered.Select(r => new[]
            {
                r.PurchasedDate, r.DrawNumber.ToString(), r.DrawDate, r.Numbers, r.MatchedNumbers,
                r.Spots.ToString(), $"${r.Wager:0.##}", r.Bullseye ? "Yes" : "No", $"${r.Amount:N2}",
            }).ToList();

            var widths = headers.Select((h, i) => Math.Max(h.Length, rowsOut.Count == 0 ? 0 : rowsOut.Max(r => r[i].Length))).ToArray();

            void WriteRow(string[] cells)
            {
                for (int i = 0; i < cells.Length; i++)
                    sb.Append(cells[i].PadRight(widths[i] + 2));
                sb.AppendLine();
            }

            WriteRow(headers);
            sb.AppendLine(string.Join("", widths.Select(w => new string('-', w + 2))));
            foreach (var r in rowsOut) WriteRow(r);

            sb.AppendLine();
            sb.AppendLine($"Total shown: {ordered.Count} win(s), ${ordered.Sum(r => r.Amount):N2}");
        }

        string text = sb.ToString();
        try
        {
#if ANDROID
            if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Q)
                await WriteExportViaMediaStoreAsync(text);
            else
                await WriteExportDirectAsync(text);
#else
            // No public Downloads folder on iOS — mirrors SpendingTracker.DumpPrefsToFile's
            // fallback: land it in the app's own sandboxed Documents dir. Visible via Files app
            // > On My iPhone > [app] as long as Info.plist has file-sharing enabled; otherwise
            // still pullable for debugging, same tradeoff as the rest of this silent background
            // writer already accepts on Android's own SdkInt<29 branch.
            string dir = Path.Combine(FileSystem.AppDataDirectory, ExportFolder);
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, ExportFile), text);
#endif
        }
        catch (Exception ex) { _ = Logger.LogAsync($"HSPast10Days: export write failed — {ex.GetType().Name}: {ex.Message}"); }
    }

#if ANDROID
    static async Task WriteExportViaMediaStoreAsync(string text)
    {
        var resolver = Android.App.Application.Context.ContentResolver;
        if (resolver == null) return;
        var downloadsUri = Android.Provider.MediaStore.Downloads.ExternalContentUri;

        // Delete any existing copy first so this always overwrites rather than accumulating
        // "HSPast10Days (1).txt", "HSPast10Days (2).txt", ... — same pattern BackupService
        // uses for its own daily-overwrite file.
        string sel = $"{Android.Provider.MediaStore.IMediaColumns.DisplayName} = ?";
        resolver.Delete(downloadsUri, sel, new[] { ExportFile });

        var values = new Android.Content.ContentValues();
        values.Put(Android.Provider.MediaStore.IMediaColumns.DisplayName, ExportFile);
        values.Put(Android.Provider.MediaStore.IMediaColumns.MimeType, "text/plain");
        values.Put(Android.Provider.MediaStore.IMediaColumns.RelativePath, $"Download/{ExportFolder}");
        var uri = resolver.Insert(downloadsUri, values);
        if (uri == null) return;
        using var stream = resolver.OpenOutputStream(uri);
        if (stream == null) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(bytes);
    }

    static async Task WriteExportDirectAsync(string text)
    {
        var downloadsRoot = Android.OS.Environment
            .GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryDownloads)
            ?.AbsolutePath;
        if (downloadsRoot == null) return;
        string dir = Path.Combine(downloadsRoot, ExportFolder);
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, ExportFile), text);
    }
#endif
}
