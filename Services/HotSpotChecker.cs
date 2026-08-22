namespace DailyFantasyMAUI.Services;

// Background, no-UI counterpart to HotSpotPage's CheckFinishedTicketsAsync — run from
// WinCheckReceiver.cs on the same 6/7/8/9 PM schedule as the other 7 games, so Hot Spot
// wins get noticed and notified without the app ever being opened. Added 2026-08-11
// after the user reported a real $38 Hot Spot win that never notified them: Hot Spot
// wins previously only surfaced when the user opened the page (auto-check on load) and
// then manually tapped "Record Win" — there was no automatic path at all.
//
// Mirrors CheckFinishedTicketsAsync's own gating exactly (same "hs_reviewed" flag, same
// "full covered draw range must have already completed" rule) so this is exactly as safe
// against calottery.com's endpoint quirks as the already-proven page-load path — see
// HotSpotDrawService.FetchDrawAsync's comments for the specific gotchas (no stray query
// params, latest-draw fetch is CDN-cached several minutes, a just-posted draw # isn't
// safely queryable for ~2 minutes). The one behavioral difference from the page-load path:
// there's no one present to tap "Record Win", so a win found here is recorded directly.
public static class HotSpotChecker
{
    // Checks every saved Hot Spot ticket (10 slots) whose full covered draw range has
    // already completed and hasn't been reviewed yet, across its entire range (never just
    // the latest draw — a ticket can win on any draw within its covered range), and
    // records any win directly via SummaryPage.AddWinAsync (same SourceKey dedup the
    // in-app manual "Record Win" flow already relies on, so this can never double-record
    // a win the user already saved themselves). Returns the wins genuinely newly recorded
    // this run, for building a notification.
    public static async Task<List<WinningRecord>> CheckFinishedTicketsAsync()
    {
        var newlyRecorded = new List<WinningRecord>();

        // Bare fetch only (no "fast detect") — this runs at most 4x/day, so the few-minutes
        // CDN staleness the bare fetch accepts doesn't matter, and it's the one fetch mode
        // FetchDrawAsync's own comments call "correct in every single test."
        var latest = await HotSpotDrawService.FetchDrawAsync();
        if (!latest.Ok) return newlyRecorded; // can't tell what's finished without a current draw#
        int currentDraw = latest.DrawNumber;

        // Past (immutable) draws fetched while walking one ticket's range are safe to reuse
        // if a later slot's range overlaps the same draw numbers this same run.
        var cache = new Dictionary<int, HotSpotDrawService.DrawResult>();

        for (int slot = 0; slot < HotSpotPage.SlotCount; slot++)
        {
            string numbersRaw = Preferences.Get(HotSpotPage.SlotKey(HotSpotPage.KeyNumbers, slot), "");
            if (string.IsNullOrWhiteSpace(numbersRaw)) continue;
            if (Preferences.Get(HotSpotPage.SlotKey(HotSpotPage.KeyReviewed, slot), false)) continue;
            int startDraw = Preferences.Get(HotSpotPage.SlotKey(HotSpotPage.KeyStartDraw, slot), 0);
            if (startDraw <= 0) continue; // no receipt draw# entered — can't tell if finished
            int draws = Preferences.Get(HotSpotPage.SlotKey(HotSpotPage.KeyDraws, slot), 1);
            int lastDraw = startDraw + draws - 1;
            if (lastDraw > currentDraw) continue; // still in progress — leave unreviewed, check again next run

            int[] picks;
            try { picks = numbersRaw.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).OrderBy(n => n).ToArray(); }
            catch { continue; } // malformed stored numbers — skip rather than throw the whole run

            int spots = Preferences.Get(HotSpotPage.SlotKey(HotSpotPage.KeySpots, slot), picks.Length);
            decimal wager = (decimal)Preferences.Get(HotSpotPage.SlotKey(HotSpotPage.KeyWager, slot), 1.0);
            bool bullseye = Preferences.Get(HotSpotPage.SlotKey(HotSpotPage.KeyBullseye, slot), false);

            // Same as CheckRangeAsync: skip draws that fail to fetch rather than aborting the
            // whole ticket, and still mark the slot Reviewed once done — a transient miss on
            // one draw shouldn't make this ticket get re-walked forever.
            try
            {
                for (int dn = startDraw; dn <= lastDraw; dn++)
                {
                    var draw = await HotSpotDrawService.FetchDrawAsync(dn, cache);
                    if (!draw.Ok) continue;

                    var (matches, _, winAmount) = HotSpotDrawService.Score(picks, bullseye, spots, wager, draw);
                    if (winAmount <= 0) continue;

                    // The draw's own date, not "today" — this runs on a schedule, so a ticket
                    // whose draws finished yesterday but only got reviewed just now (e.g. after
                    // sitting unreviewed for a while) must be logged under when it actually
                    // drew, not when this background pass happened to catch it. Falls back to
                    // today only if DrawTime never parsed (default(DateTime)).
                    string winDate = draw.DrawTime > DateTime.MinValue
                        ? draw.DrawTime.ToString("yyyy-MM-dd")
                        : DateTime.Today.ToString("yyyy-MM-dd");
                    var record = new WinningRecord
                    {
                        Game      = "HS",
                        Date      = winDate,
                        Numbers   = string.Join(" ", picks),
                        Amount    = winAmount,
                        Note      = $"{matches}/{spots} (draw #{draw.DrawNumber})",
                        SourceKey = $"HS_{draw.DrawNumber}_{string.Join("-", picks)}",
                    };
                    bool added = await SummaryPage.AddWinAsync(record);
                    if (added) newlyRecorded.Add(record);
                }
            }
            finally
            {
                Preferences.Set(HotSpotPage.SlotKey(HotSpotPage.KeyReviewed, slot), true);
                SummaryPage.NeedsRefresh = true;
            }
        }

        return newlyRecorded;
    }
}
