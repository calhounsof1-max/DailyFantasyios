namespace DailyFantasyMAUI;

// Hot Spot's "draw reveal" animation, split into its own class per the user's own suggestion —
// modeled after the real retailer terminal display (user's reference): all 20 drawn numbers fly
// onto the board together, each ball starting large and off past the board's own top/bottom
// edge, arcing/falling into place while shrinking down to true size as it lands.
//
// User's explicit ask 2026-08-16, after watching the first version live: the real numbered ball
// must NEVER disappear from its grid slot — no blank holes anywhere on the board, ever, the
// number must already be sitting there. That rules out animating the real ball itself (moving
// it via Translation vacates its slot, since nothing else is drawn in that Grid cell while it's
// elsewhere). Instead, PlayAsync adds a temporary "ghost" clone — built fresh by the caller,
// already showing the ball's real target color/number — into the SAME Grid cell as the real
// ball, animates the ghost in from off-board, and only once it lands does it (a) tell the
// caller to paint the real ball's real color and (b) remove the ghost. Since both look
// identical at that exact instant, the swap is seamless — the real ball was there the whole
// time (still showing its number, just not yet colored), the ghost is what actually flies.
public static class HotSpotFlyIn
{
    // 1.5 sec per ball's own flight (user's explicit ask). A fully sequential 20 * 1.5s would
    // be 30s — over the user's own 25s ceiling — so balls are staggered instead of awaited one
    // at a time: 19 gaps * 1100ms + one final 1500ms flight = ~22.4s total, ~2.6s of margin
    // under the cap while each ball's own landing is still 1.1s apart, close enough to read as
    // "one at a time" rather than a single simultaneous flash.
    public const uint BallDurationMs = 1500;
    public const uint StaggerMs = 1100;

    // How far outside the grid's own top/bottom edge a ball starts — large enough that it's
    // genuinely arriving from off the board, not just drifting up/down a row or two.
    const double StartOffsetY = 420;

    // Sideways arc component — the ball drifts in from an angle and straightens out as it
    // approaches, instead of traveling a dead-straight vertical line. User's explicit ask:
    // "the ball should arc in as if they are falling."
    const double StartOffsetX = 70;

    // Big -> small landing size (the user's explicit ask, the opposite of a normal "grow in")
    // — reads as the ball approaching from a distance and settling into its real size, not
    // popping/inflating in place.
    const double StartScale = 2.4;

    // orderedNumbers: the numbers in the order they should land — real draw order (Bulls-eye
    // last, since it's drawn 20th/last in the actual game — see HotSpotPage's caller), NOT
    // sorted ascending, so the reveal reads like a real draw happening rather than a
    // scoreboard filling in numeric order.
    // ballGrid: the real grid every ball lives in — ghosts are added here (in the same Row/
    // Column as their real ball, read off the real ball's own attached Grid properties) and
    // removed once landed.
    // ballViews: number -> its real Border, already sitting in ballGrid at its true position —
    // used only to read that position (Grid.GetRow/GetColumn), never moved or hidden.
    // buildGhost(number): builds a fresh, fully-styled temporary clone already showing this
    // ball's real target color/number — caller's responsibility (HotSpotPage owns the actual
    // ball styling/color rules).
    // onLanded(number): called the instant a ball's ghost finishes landing — the caller's
    // chance to paint the REAL ball's real color in lockstep, right before the ghost that was
    // standing in for it gets removed.
    public static async Task PlayAsync(
        IReadOnlyList<int> orderedNumbers,
        Grid ballGrid,
        IReadOnlyDictionary<int, Border> ballViews,
        Func<int, Border> buildGhost,
        Action<int> onLanded)
    {
        var flights = new List<Task>(orderedNumbers.Count);
        for (int i = 0; i < orderedNumbers.Count; i++)
        {
            if (!ballViews.TryGetValue(orderedNumbers[i], out var realBall)) continue;
            flights.Add(FlyOneAsync(orderedNumbers[i], realBall, ballGrid, buildGhost, onLanded, (uint)(i * StaggerMs)));
        }
        await Task.WhenAll(flights);
    }

    // Numbers 1-40 live in the grid's top half — those balls cross in FROM BELOW the board,
    // arcing upward past the halfway line to land. Numbers 41-80 live in the bottom half —
    // those cross in FROM ABOVE, arcing downward. User's explicit ask: "top balls going to
    // the bottom half numbers and bottom ball going to top half number" — the two streams
    // visibly cross paths instead of every ball dropping from the same direction.
    static async Task FlyOneAsync(int number, Border realBall, Grid ballGrid, Func<int, Border> buildGhost, Action<int> onLanded, uint delayMs)
    {
        if (delayMs > 0) await Task.Delay((int)delayMs);

        var ghost = buildGhost(number);
        ghost.InputTransparent = true; // decorative only — never steals the real ball's tap
        ghost.ZIndex = 1; // render above every real ball, regardless of add order
        Grid.SetRow(ghost, Grid.GetRow(realBall));
        Grid.SetColumn(ghost, Grid.GetColumn(realBall));
        ballGrid.Children.Add(ghost);

        bool topHalf = number <= 40;
        bool arcRight = number % 2 == 0; // alternates so consecutive balls don't all curve the same way
        double startY = topHalf ? StartOffsetY : -StartOffsetY;
        double startX = arcRight ? StartOffsetX : -StartOffsetX;
        ghost.Scale = StartScale;
        ghost.TranslationY = startY;
        ghost.TranslationX = startX;

        await ArcInAsync(ghost, startX, startY);

        // try/finally: a ghost left behind on any exception out of onLanded (the caller's own
        // color/logging callback) would otherwise become a permanent stuck leftover — nothing
        // else ever removes it. User caught exactly this live 2026-08-16: an oversized ball
        // still visible on screen long after its own landing should have finished.
        try { onLanded(number); } // paint the real ball's real color now — ghost and real ball
                                   // are visually identical at this exact instant
        finally { ballGrid.Children.Remove(ghost); }
    }

    // A straight-line TranslateTo can't produce a curved path — both axes would interpolate
    // together under the same easing, however it's tuned. A real arc needs X and Y driven by
    // DIFFERENT easing curves: Y uses CubicIn ("falling" — slow to start, accelerating in),
    // X uses SinOut and finishes early (70% of the duration) so the sideways drift settles out
    // before the vertical motion does, curving the path instead of cutting a diagonal line.
    // Y uses BounceOut (user's explicit ask: "the balls should be in a bouncing motion") — it
    // overshoots past 0 a couple of times with shrinking amplitude before settling, reading as
    // the ball physically landing and bouncing into its slot rather than gliding to a stop.
    // Scale stays on CubicOut (smooth shrink) — bouncing the SIZE at the same time as position
    // would fight the landing moment instead of selling it.
    static Task ArcInAsync(Border ghost, double startX, double startY)
    {
        var tcs = new TaskCompletionSource<bool>();
        var group = new Animation();
        group.Add(0.0, 1.0, new Animation(v => ghost.TranslationY = v, startY, 0, Easing.BounceOut));
        group.Add(0.0, 0.7, new Animation(v => ghost.TranslationX = v, startX, 0, Easing.SinOut));
        group.Add(0.0, 1.0, new Animation(v => ghost.Scale = v, StartScale, 1, Easing.CubicOut));
        group.Commit(ghost, "HotSpotFlyIn", 16, BallDurationMs, Easing.Linear, (v, cancelled) => tcs.TrySetResult(!cancelled));
        return tcs.Task;
    }

    // "Clear the board" pre-reveal animation — user's explicit ask 2026-08-16: right before a
    // new draw's numbers fly in, every ball's CURRENT color should visibly fall away first, in
    // two waves — numbers 41-80 (the grid's bottom half) drop straight down and off the board
    // FIRST, then after a half-second pause 1-40 (the top half) drop the same way — instead of
    // the old instant snap-to-blank. Bottom-first was the user's own follow-up call, made live
    // after seeing top-first play out. Same rule as PlayAsync's reveal, just run in reverse: the
    // real
    // numbered ball must NEVER disappear from its slot (user caught this live — the first
    // version animated the real ball itself, including its number, so the board briefly went
    // fully blank mid-drop). A ghost already showing the ball's CURRENT color is what actually
    // falls; onCleared repaints the real ball underneath to blank the instant the ghost is
    // placed, so the number is visible in its slot the entire time — the ghost falling away
    // just reveals the (already-blank) real ball was there all along, same swap trick PlayAsync
    // uses on the way in.
    public const uint DropDurationMs = 600;
    public const uint DropWaveGapMs = 500;
    // Staggered instead of every ball in a wave dropping at once — user's explicit ask
    // 2026-08-16, "a staggered fall would be even greater." Follows ballViews' own number
    // order (1-40, then 41-80), which is insertion order into the grid — top-to-bottom,
    // left-to-right — so it reads as a wave sweeping down the board rather than a single flash.
    const uint DropStaggerMs = 15;
    const double DropOffsetY = 500;

    // buildGhost(number): a ghost clone showing this ball's CURRENT (about to be cleared) color
    // — caller's job, same contract as PlayAsync's buildGhost, just fed the ball's present state
    // instead of its landing state.
    // onCleared(number): caller's chance to repaint the real ball to blank, called the instant
    // that number's ghost is placed (so nothing is ever visibly uncovered between the two).
    public static async Task DropOutAsync(Grid ballGrid, IReadOnlyDictionary<int, Border> ballViews, Func<int, Border> buildGhost, Action<int> onCleared)
    {
        await DropWaveAsync(ballGrid, ballViews, buildGhost, onCleared, n => n > 40);
        await Task.Delay((int)DropWaveGapMs);
        await DropWaveAsync(ballGrid, ballViews, buildGhost, onCleared, n => n <= 40);
    }

    static Task DropWaveAsync(Grid ballGrid, IReadOnlyDictionary<int, Border> ballViews, Func<int, Border> buildGhost, Action<int> onCleared, Func<int, bool> inWave)
    {
        var falls = new List<Task>();
        uint i = 0;
        foreach (var (number, realBall) in ballViews)
            if (inWave(number))
                falls.Add(DropOneAsync(number, realBall, ballGrid, buildGhost, onCleared, i++ * DropStaggerMs));
        return Task.WhenAll(falls);
    }

    static async Task DropOneAsync(int number, Border realBall, Grid ballGrid, Func<int, Border> buildGhost, Action<int> onCleared, uint delayMs)
    {
        if (delayMs > 0) await Task.Delay((int)delayMs);

        // User's final word 2026-08-16, after trying it behind first: falling balls render IN
        // FRONT of every other real ball, same rule PlayAsync's own reveal ghosts already use.
        var ghost = buildGhost(number);
        ghost.InputTransparent = true;
        ghost.ZIndex = 1;
        Grid.SetRow(ghost, Grid.GetRow(realBall));
        Grid.SetColumn(ghost, Grid.GetColumn(realBall));
        ballGrid.Children.Add(ghost);

        onCleared(number); // real ball goes blank NOW, hidden under the ghost showing its old color

        try { await ghost.TranslateTo(0, DropOffsetY, DropDurationMs, Easing.CubicIn); }
        finally { ballGrid.Children.Remove(ghost); } // same leftover-ghost safety net as PlayAsync's FlyOneAsync
    }
}
