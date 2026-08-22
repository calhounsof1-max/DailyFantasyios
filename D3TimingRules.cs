using System;

namespace DailyFantasyMAUI;

/// <summary>
/// Pure, MAUI-free rules for "is this D3 ticket done/expired/still visible." D3 draws twice a
/// day and the Results page keeps a ticket visible until 6am the morning after its last valid
/// day (see billy.md), so every one of these decisions must be driven by the actual draw number
/// or calendar date passed in — never by reading the wall clock directly — or it silently
/// inverts once a day boundary is crossed. Kept dependency-free (no Preferences, no MAUI) so it
/// can be unit-tested without a device.
/// </summary>
public static class D3TimingRules
{
    /// <summary>
    /// True during the one deliberate exception: a ticket whose play window ended yesterday
    /// stays visible until 6am today, giving the last evening draw time to post.
    /// </summary>
    public static bool IsGraceWindow(DateTime? endDate, DateTime today, TimeSpan nowTimeOfDay) =>
        endDate.HasValue
        && endDate.Value.Date == today.AddDays(-1)
        && nowTimeOfDay < TimeSpan.FromHours(6);

    /// <summary>
    /// The draw stream a row actually plays. An M-only row must only ever be compared against
    /// the midday stream, or it looks "expired" the instant evening posts on its own valid day.
    /// </summary>
    public static int RelevantLatestDraw(string drawFilter, int middayDrawNum, int eveningDrawNum) =>
        drawFilter switch
        {
            "M" => middayDrawNum,
            "E" => eveningDrawNum,
            _   => Math.Max(middayDrawNum, eveningDrawNum),
        };

    /// <summary>
    /// True once a ticket's play window is fully over — by date or by draw#, unless the 6am
    /// grace window is still open.
    /// </summary>
    public static bool ShouldRetire(
        DateTime? endDate, DateTime? startDate, int drawEnd,
        string drawFilter, int middayDrawNum, int eveningDrawNum,
        DateTime today, TimeSpan nowTimeOfDay)
    {
        bool grace = IsGraceWindow(endDate, today, nowTimeOfDay);
        int relevantLatestDraw = RelevantLatestDraw(drawFilter, middayDrawNum, eveningDrawNum);

        bool dateExpired = endDate.HasValue && endDate.Value.Date < today;
        bool drawExpired = drawEnd > 0 && relevantLatestDraw > drawEnd;
        if ((dateExpired || drawExpired) && !grace) return true;

        if (!endDate.HasValue && startDate.HasValue && startDate.Value.Date < today && !grace) return true;

        return false;
    }

    /// <summary>
    /// True once a midday-only ticket's own midday draw has posted a result — done regardless
    /// of wall-clock time, so it stays correct across the 6am carry-over window (the bug fixed
    /// 2026-07-31: this used to check "is it currently past 2pm," which reset every midnight).
    /// </summary>
    public static bool IsMiddayDone(int knownMiddayDrawNum, int ticketDrawEnd) =>
        knownMiddayDrawNum > 0 && ticketDrawEnd > 0 && knownMiddayDrawNum >= ticketDrawEnd;

    /// <summary>
    /// Same rule as <see cref="IsMiddayDone"/>, mirrored for an evening-only ticket: true once
    /// the evening draw matching this ticket's own draw# has posted, regardless of wall-clock
    /// time. Draw-number driven for the same reason as the midday check — a wall-clock "is it
    /// past 7pm" test would silently flip back to false once the ticket carries over past
    /// midnight via the 6am grace window.
    /// </summary>
    public static bool IsEveningDone(int knownEveningDrawNum, int ticketDrawEnd) =>
        knownEveningDrawNum > 0 && ticketDrawEnd > 0 && knownEveningDrawNum >= ticketDrawEnd;

    /// <summary>
    /// True while a D3 row is still "current" on the Results page — current until the lower of
    /// the two draw streams passes the ticket's own end draw#.
    ///
    /// The draw-number check alone is not enough: if today's own draws haven't posted yet (e.g.
    /// 6am, before the 1pm midday draw), a win from yesterday is never "superseded" by a newer
    /// draw and this would say "still visible" indefinitely — well past the 6am cutoff the app
    /// otherwise enforces everywhere else (D3RetireReceiver fires daily at 6am specifically for
    /// this). The optional ticketDrawDate/today/nowTimeOfDay params add that same hard 6am
    /// cutoff here too, mirroring <see cref="ShouldRetire"/>'s grace-window rule. Left optional
    /// (defaulting to skip the date check) so this stays a pure draw-number function for
    /// existing callers/tests that don't have date context.
    /// </summary>
    public static bool IsStillVisible(int middayDrawNum, int eveningDrawNum, int ticketDrawEnd,
        DateTime? ticketDrawDate = null, DateTime? today = null, TimeSpan? nowTimeOfDay = null)
    {
        if (ticketDrawDate.HasValue && today.HasValue && nowTimeOfDay.HasValue)
        {
            bool grace = IsGraceWindow(ticketDrawDate, today.Value, nowTimeOfDay.Value);
            if (ticketDrawDate.Value.Date < today.Value.Date && !grace) return false;
        }

        int minD3Draw = (middayDrawNum > 0 && eveningDrawNum > 0)
            ? Math.Min(middayDrawNum, eveningDrawNum)
            : Math.Max(middayDrawNum, eveningDrawNum);
        return minD3Draw <= 0 || ticketDrawEnd <= 0 || ticketDrawEnd >= minD3Draw;
    }
}
