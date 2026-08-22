using System.Text.Json;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls.Shapes;
using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

// ── Self-contained Hot Spot feature ──────────────────────────────────────────
// Everything for Hot Spot lives in THIS ONE FILE, deliberately — the user asked
// for this game to be its own class so it can be pulled out cleanly if it ever
// causes real problems, without touching any of the other 7 games' code.
//
// The only touches OUTSIDE this file (each a small, additive block, never an
// edit to existing per-game logic):
//   - AppShell.xaml.cs      : singleton instance field + one route registration
//   - MainPage.xaml.cs      : one "Hot Spot" entry in the nav dropdown
//   - Services/TicketLogService.cs : one new "HS" block in ScanAndLogTodayAsync,
//                             using the existing generic LogRowsWithDrawCountAsync
//   - SpendingTracker.cs    : one new SumHotSpotCostToday() function, since Hot
//                             Spot's variable wager breaks the "fixed price per
//                             game" assumption every other game relies on
//   - SpendingLogPage.xaml.cs : one HS row in the Log Today overlay using the
//                             function above instead of the generic TicketCost() path
//
// NOTE on live draw checking: this app's other 7 games all fetch results from
// calottery.com/api/DrawGameApi/DrawGamePastDrawResults/{id}/... with a small
// game ID (8-15). Hot Spot draws ~250x/day with 7-digit draw numbers and has no
// ID in that same JSON API. Its real data source is calottery.com's own
// server-rendered "Past Winning Numbers" page (no query param = latest draw),
// scraped via regex in FetchLatestDrawAsync() below — see that method for the
// exact markup shape relied on.
public class HotSpotPage : ContentPage
{
    // ── Preferences keys (10-slot system, added 2026-08-09 — was single-ticket
    // before) ─────────────────────────────────────────────────────────────────
    // Each base name below gets suffixed "_{slot}" (slot 0-9) via SlotKey(). Kept as
    // separate flat suffixed keys rather than one serialized blob per slot — matches
    // the pattern every other game in this app already uses for its 10 slots, and
    // means TicketLogService/SpendingTracker can read individual fields directly
    // without needing to know about a ticket class at all.
    // internal (not private): HotSpotChecker.cs (background win-check, run from
    // WinCheckReceiver) reads the same saved-ticket slots and must use the exact same key
    // names — kept as the single source of truth here rather than duplicated.
    internal const string KeySpots         = "hs_spots";
    internal const string KeyBullseye      = "hs_bullseye";
    internal const string KeyWager         = "hs_wager";
    internal const string KeyDraws         = "hs_draws";
    internal const string KeyNumbers       = "hs_numbers";
    internal const string KeyPurchasedDate = "hs_purchased_date";
    internal const string KeyStartDraw     = "hs_start_draw";
    // Per-slot: exact clock time this ticket was actually Saved (real Save tap, whether a
    // plain fresh entry or a Replay reviewed and then committed) — user's explicit ask
    // 2026-08-21: not shown anywhere in the UI, kept purely so a later feature can read it back.
    internal const string KeySavedTime     = "hs_saved_time";
    const string KeyActiveSlot    = "hs_active_slot";
    internal const string KeyReviewed      = "hs_reviewed"; // per-slot: already auto-checked once since finishing, don't nag again
    // Per-slot: pipe-separated subset of this ticket's picks that were both picked AND drawn on
    // the most recently live-checked draw within its range — only ever set when that same draw
    // actually paid cash (winAmount > 0). Read by HotSpotMyNumbersPanel to color just those balls
    // green in "My Tickets". Set/cleared every time CheckAllSlotsLiveWinAsync checks a genuinely
    // new draw for this slot (win or not), so a previous draw's green balls never linger once a
    // later draw comes and goes without paying — user's explicit ask 2026-08-22.
    internal const string KeyWinNumbers    = "hs_win_numbers";
    internal const string KeyWinDrawNumber = "hs_win_draw";
    // Per-slot: the highest draw# AutoCheckAndRecordAllSlotsAsync has already fetched+scored for
    // this ticket — lets repeat passes (every auto-refresh tick) only ever fetch NEW draws
    // instead of re-walking a whole in-progress ticket's elapsed range every 8 seconds.
    const string KeyLastAutoChecked = "hs_last_autochecked_draw";
    const string KeyCoverDraw     = "hs_cover_draw"; // per-slot: whatever the user typed into "Covers Draws #" — persisted like Start#, not a shared scratch value
    // Renamed from hs_auto_refresh_minutes when the value switched int -> double (added the
    // 30 sec option) — reusing the old key would crash Preferences.Get<double> against a
    // value previously stored as an int. Not per-slot — one shared setting for the page, 0 = off.
    const string KeyAutoRefreshMinutes = "hs_auto_refresh_minutes_v2";
    const string KeyCalCountdownEnabled = "hs_cal_countdown_enabled";
    const string KeyShowRefreshingLabel = "hs_show_refreshing_label";
    // Off by default — user's explicit ask: "put an Option... to turn if off or no, to see this
    // feature" — an opt-in toggle so the auto-jump can be tried and turned back off, not a
    // silent always-on behavior change. See CheckAllSlotsLiveWinAsync for the jump itself.
    const string KeyMatchedAllNumbersEnabled = "hs_matched_all_numbers_enabled";
    // On by default (matches the reveal's existing always-on behavior) — user's ask 2026-08-20:
    // replace the testing-only "Preview Fly In" menu row with a persistent toggle for the ghost
    // drop-in reveal itself. Off skips straight to the instant-fill path ShowDrawResultOnGridAsync
    // already has for repeat/non-live checks — see playFlyIn's own comment.
    const string KeyAnimationModeEnabled = "hs_animation_mode_enabled";

    // Single source of truth for "how many ticket slots does Hot Spot have" — was scattered as
    // a bare `10` literal across ~14 sites in this file plus HotSpotChecker.cs (background/
    // closed-app win checker), HotSpotFastCheckScheduler.cs (fast-check alarm gate),
    // SpendingTracker.cs (today's spend total), and TicketLogService.cs (Ticket Log rows) —
    // raised to 12 by user's explicit ask 2026-08-18. internal so those other files reference
    // this one constant instead of carrying their own copy of the number.
    internal const int SlotCount = 12;

    internal static string SlotKey(string baseKey, int slot) => $"{baseKey}_{slot}";

    static readonly int[]     SpotOptions  = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    static readonly decimal[] WagerOptions = { 1, 2, 3, 4, 5, 10, 20 };
    static readonly int[]     DrawOptions  = { 1, 2, 3, 4, 5, 10, 20, 50, 100 };

    // Full Screen mode (2026-08-14, user's explicit ask) — hides the intake panel/legend/
    // status text/button row, leaving just the header and the ball grid, with a "Regular Mode"
    // bar pinned at the bottom to exit. Balls also switch from the regular 10-per-row layout to
    // 8-per-row so each one renders noticeably bigger (screen width ÷ 8 instead of ÷ 10) —
    // user's explicit choice after comparing both against a mockup, picked for readability. The
    // 80-ball grid itself isn't duplicated: RelayoutBallGrid re-parents the SAME Border/Label
    // views already in _ballViews onto a different row/column layout, so there's only ever one
    // set of ball views and one source of truth for their state.
    const int RegularBallsPerRow    = 10;
    const int FullScreenBallsPerRow = 8;

    // Draw-fetch + prize-table logic moved to Services/HotSpotDrawService.cs (2026-08-11) so
    // the background win-checker (HotSpotChecker.cs, run from WinCheckReceiver) can reuse it
    // without building this page's UI. This page delegates to it below rather than
    // duplicating the tables/regexes/HTTP logic.

    int             _activeSlot = 0; // 0-9, displayed to the user as Ticket 1-10
    // True once the user has explicitly confirmed they want to change an already-saved
    // ticket's picks this viewing — reset to false every time LoadSlot runs (switching away
    // and back always re-locks). Added after a real accidental edit to an already-saved
    // ticket's numbers — the ball grid used to be freely tappable on a saved ticket with no
    // warning at all.
    bool            _editUnlocked = false;
    // Public, read-only "am I editing an already-saved ticket right now" flag — distinct from
    // _editUnlocked (which also arms Save on a brand-new, never-saved slot, see the Draws/
    // Spots/Bullseye/Wager handlers below). Edit only ever means there's a saved ticket
    // underneath the unlock, so it's what actually drives the green header/status text, and is
    // exposed publicly so the rest of the app can tell a slot edit is in progress. Kept in sync
    // by UpdateEditMode(), called from every place _editUnlocked changes.
    public bool     Edit { get; private set; }
    int             _spots     = 4;
    bool            _bullseye  = false;
    decimal         _wager     = 1m;
    int             _draws     = 1;
    int             _startDraw = 0; // the ticket's first covered draw #, from the real receipt — 0 = not set
    readonly HashSet<int> _selected = new();

    Picker _drawsPicker   = null!;
    Picker _spotsPicker   = null!;
    // Ticket # is a custom popup (_ticketSlotOverlay), not a native Picker — see BuildTicketSlotOverlay
    // — so finished tickets can show real red strikethrough text (impossible on a native Picker,
    // which only supports plain uncolored strings). _slotDisplayLabel is the always-visible
    // "closed" display; RefreshSlotDisplayLabel() keeps it in sync.
    Label _slotDisplayLabel = null!;
    Grid  _ticketSlotOverlay = null!;
    VerticalStackLayout _ticketSlotRowsContainer = null!;
    // True only for a brief window right after LoadSlot programmatically sets these Pickers'
    // SelectedIndex — the actual trigger for the dotnet/maui#15394 auto-reopen bug the Focused
    // handlers below unfocus against. Confirmed live 2026-08-16: unfocusing on EVERY Focused
    // event (the original blanket fix) was dismissing the native dialog a real tap had just
    // opened, before the user could pick anything — it just read as the field "flashing" and
    // never actually opening. Scoping the unfocus to only this window fixes that without
    // reopening the original bug.
    // A deadline, not a timer-driven bool: LoadSlot can in principle run twice in quick
    // succession, and two independent 600ms timers on a shared bool race — the EARLIER call's timer
    // can clear the flag while the LATER call's protection window is still supposed to be live,
    // letting a spurious native re-fire through unguarded. Comparing against a pushed-forward
    // deadline instead has no such race: a second LoadSlot just extends the window instead of
    // starting a competing timer.
    DateTime _suppressPickerFocusReopenUntil = DateTime.MinValue;
    bool _suppressPickerFocusReopen => DateTime.UtcNow < _suppressPickerFocusReopenUntil;
    Switch _bullseyeSwitch = null!;
    Picker _wagerPicker   = null!;
    Entry  _startDrawEntry = null!;
    Entry  _searchDrawEntry = null!;
    Label  _currentDrawLabel = null!;
    Label  _todaySpentLabel = null!;
    // Guards AppendTodayHotSpotWinsAsync against out-of-order completion — see that method's
    // own comment for the real-money bug this fixes.
    int _todaySpentLabelRequestSeq;
    // Last successfully-computed win total, reused as a placeholder while a fresh
    // AppendTodayHotSpotWinsAsync is still loading — see RefreshTodaySpentLabel's own comment
    // for the flicker this prevents.
    decimal _lastKnownHotSpotWinsToday;
    // Mirrors _lastKnownHotSpotWinsToday for the other two numbers that make up the label, so a
    // tap can redraw it in Net form without recomputing anything — see RenderTodaySpentLabel.
    int     _lastKnownHotSpotCountToday;
    decimal _lastKnownHotSpotSpendToday;
    // User's ask 2026-08-19: tapping "Today: $..." toggles it to a single "Net: $..." figure
    // (wins minus spend, today only) and back — see _todaySpentLabel's TapGestureRecognizer.
    bool    _showNetToday;
    // Last total computed by UpdateTotalDrawsLeftLabel — RenderTodaySpentLabel forces Net display
    // once this hits 0 (nothing left in progress today), user's explicit ask 2026-08-20.
    int     _lastKnownTotalDrawsLeft;
    Label  _selectedCountLabel = null!;
    // ⚙️ Options button — its Background color doubles as the Animation Mode indicator (green =
    // on, red = off), replacing an earlier standalone dot the user tried first and found too
    // small to notice on the real device. See RefreshAnimationModeIndicator().
    Button _btnOptions = null!;
    // "N of M matched" routine result — sits between _selectedCountLabel and _prizeLabel
    // (not in _statusLabel) so it never overwrites the cross-slot win banner CheckAllSlotsLiveWinAsync
    // puts in _statusLabel; see that method's own comment for the bug this fixes (a win on
    // another ticket was only visible for a few seconds before the next Auto Refresh tick's
    // routine match-count update wiped it out).
    Label  _matchLabel = null!;
    Label  _slotStatusLabel = null!;
    Label  _statusLabel   = null!;
    Label  _prizeLabel    = null!;
    Label  _totalCostLabel = null!;
    Grid _ballGrid  = null!;
    readonly Dictionary<int, Border> _ballViews = new();
    // Tracks each ball's current semantic state so a ball-color theme change (see
    // HotSpotBallColors) can repaint only the balls actually showing the plain/unpicked look,
    // without disturbing a live Check result's Drawn/Match/Bullseye highlighting.
    readonly Dictionary<int, BallState> _ballCurrentState = new();
    // The last draw # the HotSpotFlyIn reveal has already played for — gates it to fire once
    // per genuinely new draw (see ShowDrawResultOnGridAsync), never on a repeat check against
    // the same still-current draw or a ticket switch that just redisplays it.
    int _flyInPlayedForDrawNumber;
    // Gates CheckAllSlotsLiveWinAsync's popup to once per draw # — that check runs on every
    // page load and every Auto Refresh tick, so without this the same still-current draw's win
    // would re-alert repeatedly until the draw actually changes.
    int _lastLiveWinPopupDrawNumber;
    // True whenever _statusLabel is currently showing a ticket-win summary (CheckFinishedTicketsAsync's
    // finished-ticket line or CheckAllSlotsLiveWinAsync's cross-slot "Draw #X hit" banner) — user's
    // explicit ask 2026-08-18: once the draw those wins were reported on is over and a genuinely new
    // draw starts, that stale win text should clear itself instead of lingering on screen indefinitely.
    // CheckAutoRefreshDrawChangeAsync clears _statusLabel on a new-draw detection ONLY when this is
    // true, so it never stomps some other unrelated message (Edit Mode notice, "Checking...", etc.)
    // that happens to be showing at that same moment.
    bool _statusLabelShowsTicketWins;
    // Set true for the duration of any background pass that walks MULTIPLE slots by calling
    // LoadSlot(otherSlot) + CheckRangeAsync() in a loop (CheckFinishedTicketsAsync, and the
    // "Matched all Numbers" jumps in CheckAllSlotsLiveWinAsync/AutoCheckAndRecordAllSlotsAsync).
    // Those temporarily repoint every _activeSlot-scoped global (_selected, _startDraw, _draws,
    // _pendingWins, etc.) at a DIFFERENT ticket than whatever's on screen. CheckAutoRefreshDrawChangeAsync
    // bails out early while this is true — without that, an Auto Refresh tick landing mid-loop
    // (confirmed live 2026-08-18: reported wins attributed to the wrong Ticket #, 3 times) would
    // read/write those same globals via ApplyDrawResult on whatever slot the loop happened to have
    // loaded at that instant, then label/record the result under the WRONG ticket once the loop's
    // own await resumed and moved on. The tick is simply skipped, not queued — the next tick a few
    // seconds later re-checks the real current draw once nothing else is switching slots underneath it.
    bool _slotScanBusy;
    // User's ask 2026-08-17: when every one of the active slot's picks matches (a full hit),
    // spin those balls once the reveal has fully landed — gated per slot, keyed to the draw #
    // it already played for, same "once per genuinely new draw" rule the fly-in reveal itself
    // uses (see _flyInPlayedForDrawNumber) so it never replays on a ticket switch or a repeat
    // check of the same still-current draw.
    readonly Dictionary<int, int> _winSpinPlayedForDrawNumber = new();
    // Set true right before SwitchToSlotAsync's own RefreshCurrentDrawOnGridAsync call — the
    // ONE place a real user-driven ticket slot switch happens (see SwitchToSlotAsync) — and
    // consumed (read + reset to false) the instant ShowDrawResultOnGridAsync's instant-fill
    // path uses it, so it only ever arms the SpringInBallAsync bump for that one exact
    // downstream call, never for a repeat draw check, paging, search, or Auto Refresh tick
    // that happens to land afterward. User's explicit ask 2026-08-15: the bump is ONLY for
    // switching slots, nothing else.
    bool _springOnNextInstantFill;
    // True while HotSpotFlyIn.PlayAsync is actually running (real reveal OR Preview) — a real
    // reveal takes ~22 sec, and Hot Spot draws roughly every 4-5 min, so a genuinely new draw #
    // CAN legitimately arrive while a previous reveal is still mid-flight. Confirmed live
    // 2026-08-16: with no guard, the second reveal's own "clear first" step immediately wiped
    // out balls the first reveal had already landed, and both animations fought over the same
    // ball views — a visible glitch, not the clean board the user was expecting. When busy, a
    // newly-eligible draw falls back to the instant-fill path for now (see
    // ShowDrawResultOnGridAsync) — the NEXT Auto Refresh tick, a few seconds later once this
    // one has finished, finds the same draw # still never recorded in
    // _flyInPlayedForDrawNumber and plays it cleanly then. Shared between the real path and
    // Preview so the two can never collide with each other either.
    bool _flyInBusy;
    Grid _loadingOverlay  = null!;
    Grid _payoutOverlay   = null!;
    Grid _ballColorsOverlay = null!;
    // See Replay_Numbers.cs — fully self-contained "Replay Numbers" overlay, this page only
    // builds it, attaches it to root, offers the Options-menu row, and handles the actual slot
    // switch when a row's Replay button is tapped (HandleReplayNumbersTapped).
    Grid _replayNumbersOverlay = null!;
    Action _showReplayNumbers = null!;
    // See HotSpotMyNumbersPanel.cs — fully self-contained floating "My Numbers" panel, this
    // page only builds it, attaches it to root, and offers the Options-menu on/off row.
    Grid _myNumbersPanel = null!;
    ScrollView _mainScrollView = null!;
    // Custom-built ⚙️ Options menu — replaces a native DisplayActionSheet (see
    // BuildOptionsOverlay/RefreshOptionsMenuRows). A DisplayActionSheet has no per-item
    // text/tint color API at all, which is why the "What if -? (test)" row's ❓ could never
    // actually be made purple no matter what was tried on it — this popup uses real Labels
    // instead, so that row's "?" glyph can genuinely take a purple TextColor.
    Grid _optionsOverlay = null!;
    VerticalStackLayout _optionsRowsContainer = null!;
    // ⭐ Favorites overlay — see HotSpotFavorites.cs (fully isolated feature file). `show` action
    // comes back from HotSpotFavorites.Build's out parameter, same pattern Replay_Numbers.Build
    // uses for _replayNumbersOverlay's own show action below.
    Action _showFavoritesOverlay = null!;
    // Remaining target slots (in the order the favorites were checked) from a multi-favorite
    // "Play Selected" batch that haven't been walked through yet — user's explicit ask
    // 2026-08-22: land on the first one, then auto-advance to the next after each Save instead
    // of dumping all of them silently and leaving the user to go hunt down each ticket slot
    // manually. Null whenever no such batch is in progress. Only ever consulted/advanced from
    // SaveTicketAsync — a save on any OTHER slot (the user browsing away mid-batch) leaves it
    // untouched, so the walkthrough always resumes exactly where it left off.
    List<int>? _favoritesPlayQueue;
    // "Past 10 Days" draw-range check dialog — see BuildPast10DaysOverlay(). Everything this
    // feature touches (the actual scan/save/export/text) lives in the fully separate
    // Services/HSPast10Days.cs, same "own file, easy to pull out" philosophy as this page's own
    // header comment — this overlay is just the two-textbox input UI for it.
    Grid _past10DaysOverlay = null!;
    Entry _p10StartEntry = null!;
    Entry _p10EndEntry   = null!;
    Button _p10CheckButton = null!;
    Label _p10RunningLabel = null!;
    // "Last 200 Draws" live viewer — see BuildLast200DrawsOverlay()/OpenLast200DrawsAsync().
    // Plain raw draw list (no ticket/win logic), your current picks + Bulls-eye highlighted
    // per row, meant to be opened and re-Refreshed while actively watching the game. Its own
    // data half lives in Services/HSLast200Draws.cs, same split HSPast10Days.cs uses.
    Grid _last200Overlay = null!;
    Grid _ticketAnalysisOverlay = null!;
    Label _ticketAnalysisTitle = null!;
    Label _ticketAnalysisSubtitle = null!;
    // Red-text legend line — only populated/shown by ShowDrawsLeftByTicketAsync (its Bulls-eye
    // sub-rows are the only case that needs explaining what red means); empty + hidden for the
    // overlay's other two callers (AnalyzeActiveLast200TicketAsync, AnalyzeAllTicketsAsync).
    Label _ticketAnalysisLegend = null!;
    VerticalStackLayout _ticketAnalysisRows = null!;
    Label _ticketAnalysisTotal = null!;
    CollectionView _last200List = null!;
    Label _last200TicketLabel = null!;
    Label _last200StatusLabel = null!;
    Button _last200RefreshButton = null!;
    Button _last200AnalyzeButton = null!;
    // Which mode _last200AnalyzeAllButton is currently armed to run — flipped directly via
    // _last200WhatIfSwitch (a real, always-visible Switch, only interactable while _viewingOnly
    // is on), not by tapping the analyze button itself. See RefreshAnalyzeModeToggleUi.
    bool _last200WhatIfSelected;
    Switch _last200WhatIfSwitch = null!;
    Label _last200WhatIfLabel = null!;
    // Explains what the mode actually does — user's ask 2026-08-17: a status line so it's clear
    // this dialog is view-only against the current draws until What-if is turned on, at which
    // point Analyze All Tickets scores every draw currently in the listbox instead. Text is set
    // in RefreshAnalyzeModeToggleUi, the same place everything else about this toggle updates.
    Label _last200WhatIfHintLabel = null!;
    // Title strip of the Last Draws + Analyze Tickets dialog — background tinted purple
    // (HeaderWhatIfColor) while What-if is armed, cleared back to transparent otherwise.
    Label _last200TitleLabel = null!;
    // User's explicit ask 2026-08-16: a checkbox next to "What-if?" that, when checked, forces
    // the listbox to only ever show draws matching EVERY one of the ticket's spots (e.g. a
    // 4-spot ticket with picks 1,2,3,4 only shows draws where all 4 landed) — reuses the
    // existing "Match ≥ N" filter picker's own "Match = N" top option (PopulateLast200FilterOptions
    // already labels the last item "= N" instead of "≥ N" since matches can never exceed spots),
    // just pins it there automatically instead of requiring a manual picker tap every time the
    // ticket/spot count changes or new draws are loaded.
    CheckBox _last200ExactMatchCheckBox = null!;
    Label _last200ExactMatchLabel = null!; // text refreshed alongside _last200TicketLabel — "Exact Match (T1)"
    bool _last200ExactOnly;
    // Guards RefreshAnalyzeModeToggleUi's own programmatic IsToggled writes from re-entering the
    // Switch's Toggled handler — same pattern _last200SuppressFilterEvent uses for the Match
    // filter picker.
    bool _last200SuppressWhatIfToggleEvent;
    // Toggles between "Analyze All Tickets" (real saved slots) and "Analyze What-if?" (whatever
    // draws are currently in the on-screen list) — a single button that both runs the current
    // mode and flips to the other one afterward.
    Button _last200AnalyzeAllButton = null!;
    // True from the moment AnalyzeAllTicketsAsync/AnalyzeWhatIfListboxAsync starts until its
    // results popup closes, or it bails early — mirrors _last200AnalyzeFlowActive's purpose but
    // kept separate since the two flows can't run into each other's button state (each button
    // disables only itself).
    bool _last200AnalyzeAllBusy;
    // "Search" — user's explicit ask 2026-08-14: a dedicated popup (its own Start Draw #/Covers
    // Draw # boxes, styled to match the ticket's own, but genuinely separate Entry controls) to
    // pull an arbitrary range of draws into this SAME list/highlighting, without going through
    // "My Ticket" mode's _last200RangeStartEntry/_last200RangeEndEntry — those two write straight
    // through to the real ticket's Start#/Search# boxes (see their own comment above), which
    // would be exactly the wrong thing for a one-off lookup unrelated to what's actually saved.
    // Second box blank = a single draw#; both filled = a range (start/end auto-swapped if typed
    // backwards, same as My Ticket mode).
    Button _last200SearchButton = null!;
    Grid _rangeSearchOverlay = null!;
    Entry _rangeSearchStartEntry = null!;
    Entry _rangeSearchEndEntry = null!;
    // "Single #" / "Range of #" mode toggle — user's explicit ask 2026-08-14, so the popup
    // states plainly which one you're doing instead of it being an implicit "leave this blank"
    // convention. Single mode hides the Covers Draw # field entirely (nothing to fill in).
    bool _rangeSearchModeSingle = true;
    Button _rangeSearchSingleBtn = null!;
    Button _rangeSearchRangeBtn = null!;
    View _rangeSearchEndBlock = null!; // the "Covers Draw #" label + entry, hidden together in Single mode
    // "Use this Data" — user's explicit ask 2026-08-17: an optional custom pick set typed right
    // into this dialog, scored against the searched draw(s) INSTEAD of the active ticket's real
    // saved numbers (which is what RunRangeSearchAsync uses otherwise — see LoadSavedSlotForScoring
    // there). Unrelated to Analyze Ticket, which must always stay tied to what was actually
    // purchased (see AnalyzeActiveLast200TicketAsync's own comment) — this only affects this
    // dialog's own Search.
    CheckBox _rangeSearchUseCustomDataCheck = null!;
    Picker _rangeSearchSpotsPicker = null!;
    View _rangeSearchSpotsBlock = null!; // "How many Spots" picker + the entries row, shown only while the checkbox is on
    HorizontalStackLayout _rangeSearchSpotEntriesRow = null!;
    readonly List<Entry> _rangeSearchSpotEntries = new();
    // "My Ticket" mode — shows just Start Draw # through Covers Draws #, oldest draw first,
    // instead of the full 200. These two Entrys write straight through to _startDrawEntry/
    // _searchDrawEntry (the SAME fields the main ticket screen saves from) so there's never a
    // second copy of the number to drift out of sync — typing a Start Draw # here is exactly
    // the same as typing it on the ticket itself, still needs the normal Save tap to persist.
    bool _last200TicketMode;
    Button _last200TabAllBtn = null!;
    Button _last200TabTicketBtn = null!;
    VerticalStackLayout _last200RangeFieldsRow = null!;
    Entry _last200RangeStartEntry = null!;
    Entry _last200RangeEndEntry = null!;
    // "Match ≥ N" filter — sits to the right of the Last 200/My Ticket tabs, applies in
    // either mode. Options run 0 (show everything) through the active ticket's spot count,
    // rebuilt from _spots every load. Filtering re-uses HotSpotDrawService.Score's own
    // Matches count (see BuildLast200RowVms) so "Match ≥ 3" always lines up with what the app
    // would actually consider a win at that spot count — purely a local re-filter of the
    // already-loaded _last200AllRows, never triggers a re-fetch.
    Picker _last200FilterPicker = null!;
    // Lets the user switch which of the 10 tickets they're looking at without leaving the
    // dialog — previously the only way was Close, then the main page's own "Ticket #" field.
    // Drives the exact same SwitchToSlotAsync the main picker uses, so both stay in lockstep.
    // Custom popup (_last200TicketOverlay), not a native Picker — same reasoning as the main
    // page's own Ticket # popup (BuildTicketSlotOverlay): real red strikethrough for a finished
    // ticket instead of native Picker's plain-string-only limitation. _last200TicketDisplay is
    // just the always-visible compact "closed" trigger in the narrow tab-row column.
    Label _last200TicketDisplay = null!;
    Grid  _last200TicketOverlay = null!;
    VerticalStackLayout _last200TicketRowsContainer = null!;
    List<Last200RowVm> _last200AllRows = new();
    // Raw draws parallel to _last200AllRows — Last200RowVm only keeps formatted display text,
    // not the actual numbers, so AnalyzeWhatIfListboxAsync needs this to re-score against the
    // active SLOT's real saved picks (Last200RowVm.WinAmount was computed against whatever picks
    // were on screen at load time, which in What-if/test mode can be different test numbers, not
    // the real saved ticket — see AnalyzeWhatIfListboxAsync's own comment).
    List<HotSpotDrawService.DrawResult> _last200AllDraws = new();
    // Non-null/non-empty exactly when the CURRENT contents of _last200AllDraws/_last200AllRows
    // came from a "Use this Data" custom search (see RunRangeSearchAsync) rather than the active
    // ticket — user's explicit ask 2026-08-17: "Analyze What-if?" must then score against these
    // typed numbers too, with no requirement that any slot be saved, instead of always demanding
    // a saved ticket (see AnalyzeWhatIfListboxAsync). Cleared by LoadLast200DrawsAsync (Last 200/
    // My Ticket/Refresh all use the real ticket) and by RunRangeSearchAsync itself on a normal
    // (non-custom) search, so it only ever reflects whatever produced the list currently on screen.
    HashSet<int>? _last200CustomPicks;
    int _last200CustomSpots;
    int _last200FilterMinMatches; // == the picker's SelectedIndex by construction (item i is "Match ≥ i")
    bool _last200SuppressFilterEvent; // true while PopulateLast200FilterOptions is setting SelectedIndex programmatically
    // Tap a row to select it (CollectionView paints the highlight), then 💰 shows that one
    // draw's actual payout for the active ticket — reuses Matches/WinAmount already computed
    // onto the row's Last200RowVm by BuildLast200RowVms, no extra calc needed.
    Last200RowVm? _last200SelectedRow;
    Button _last200PayoutButton = null!;
    // Guards against a slow load/render getting re-triggered by extra taps while it's still
    // working — confirmed live 2026-08-14: without this, a tap that lands while the previous
    // one is still rendering just queues a second full rebuild on top of the first, and the
    // UI reads as fully frozen (InputDispatcher logged 5.6s+ per MotionEvent) until they all
    // drain. The row list itself is now a virtualized CollectionView (only ~15 on-screen rows
    // ever get built, not all 200 at once) so a single load should never be slow enough to
    // need this in practice, but it stays as a hard stop either way.
    bool _last200Busy;
    // True from the moment AnalyzeActiveLast200TicketAsync starts until it either shows the
    // results popup or bails out with its own re-enable — tells LoadLast200DrawsAsync's finally
    // to leave the Analyze button disabled rather than re-enabling it, since that method itself
    // owns the button's final state once it returns (stay disabled behind the popup, or
    // re-enable on a genuine "no draws" bail). See AnalyzeActiveLast200TicketAsync.
    bool _last200AnalyzeFlowActive;
    ActivityIndicator _spinner = null!;
    Label _loadingLabel = null!;
    Button _btnRecordWin  = null!;
    Button _btnDismissRecordWin = null!;
    Button _btnCheckRange = null!;
    Button _btnSave       = null!;

    // Viewing only (test) mode — ⚙️ Options menu toggle for trying out numbers ("what if I
    // played these") without ever writing a real win or a real saved ticket. Deliberately
    // NOT persisted via Preferences — always starts OFF on page load, so it can never be left
    // on by accident across app restarts (user's explicit call). While on: PersistCurrentSlotRaw
    // and SaveTicketAsync both no-op (nothing touches the 10 real slot keys HotSpotChecker's
    // background auto win-checker reads), so a test entry can never later get silently
    // auto-recorded as a real win. CheckRangeAsync/_prizeLabel still show win results exactly as
    // normal — only the actual recording step (RecordPendingWinAsync) is blocked.
    bool _viewingOnly = false;
    Grid _headerGrid = null!;
    static readonly Color HeaderNormalColor  = Color.FromArgb("#1E2733");
    // Same purple as the What-if switch's OnColor / Analyze What-if? button — used both to tint
    // the Last Draws + Analyze Tickets dialog's own title strip when What-if is armed, and (via
    // RefreshMainHeaderColor) the main page header whenever Viewing-Only/"What if -? (test)" is
    // on, so the whole app reads consistently as "in test mode" with one color, not two.
    static readonly Color HeaderWhatIfColor = Color.FromArgb("#6A1B9A");
    // Edit Mode — shown (via RefreshMainHeaderColor) whenever Edit is true, i.e. an already-
    // saved ticket is actively unlocked for editing. Takes priority over HeaderWhatIfColor.
    static readonly Color HeaderEditColor = Color.FromArgb("#2E7D32");

    // Full Screen mode state — see the field-group comment above RegularBallsPerRow.
    bool _fullScreen = false;
    int _ballsPerRow = RegularBallsPerRow;
    Border _topHalfBorder = null!;
    Border _bottomHalfBorder = null!;
    // Background fill behind 1-40 / 41-80 (2026-08-19) — see BuildHalfBackground.
    Border _topHalfBackground = null!;
    Border _bottomHalfBackground = null!;
    View _normalControlsTop = null!;    // intake panel — hidden while full screen
    Label _fullScreenBullseyeLabel = null!; // read-only "🎯 Bulls-eye: On/Off" — only shown in Full Screen, filling the empty gap left above the ball grid once the intake panel (which has the real toggle) is hidden. User's explicit ask 2026-08-14.
    View _normalControlsBottom = null!; // legend + status text + button row — hidden while full screen
    ContentView _fullScreenExitBar = null!; // bottom bar showing the big draw#/countdown while Full Screen is on — no longer has its own button (see _fullScreenEnterBtn)
    Label _fullScreenDrawLabel = null!; // bigger mirror of the header's small "Current draw: #..." text — user's explicit ask, since the header copy is too small to read while eyes are down on the bigger balls
    Label _fullScreenCountdownLabel = null!; // mirrors whichever "next draw" countdown Regular Mode itself shows — user's explicit ask, so Full Screen visibly ticks instead of looking frozen
    Border _fullScreenEnterBtn = null!; // header icon that TOGGLES Full Screen both directions (was enter-only, with a separate bottom "Regular Mode" button for the way back — user's explicit ask 2026-08-14 to collapse both into this one control)
    FullScreenToggleIcon _fullScreenToggleDrawable = null!; // the arrow-cluster drawn inside _fullScreenEnterBtn — see FullScreenToggleIcon.cs
    GraphicsView _fullScreenToggleIconView = null!; // shown only in Full Screen (collapse arrows)
    Label _fullScreenExpandGlyph = null!; // shown only in Regular Mode — original "⛶" glyph, user's explicit ask to keep this one for the "enter" direction
    VerticalStackLayout _scrollContent = null!; // holds _normalControlsTop/_ballGrid/_normalControlsBottom — vertically centered while Full Screen so the (now much shorter) grid isn't stuck at the top with dead space below it

    // Results waiting for an explicit "Record Win" tap — Check (single or range) never
    // writes to the winnings log by itself, per the user's explicit ask.
    readonly List<(int DrawNumber, int Matches, decimal Amount, int[] Numbers, DateTime DrawTime, bool BullseyeHit)> _pendingWins = new();

    // Mirrors _pendingWins' count/total per slot, but — unlike _pendingWins itself — survives
    // switching tickets (LoadSlot/ClearSelection wipes _pendingWins every time). Lets the
    // Record Win button report a running total across every ticket that has an unrecorded win
    // right now, not just whichever one is currently on screen (user's explicit ask 2026-08-12,
    // after adding the auto-jump-to-winning-ticket behavior below made multiple tickets having
    // pending wins at once a real scenario). Recording only ever acts on the currently active
    // slot's own _pendingWins — this dictionary is purely for the button's display total.
    // DrawNumbers (added 2026-08-15, user's explicit ask) is purely for the button's own label —
    // showing which draw(s) a pending win is for so the user can tell whether the banner
    // reappearing is a genuinely new draw or the same one they've already seen.
    readonly Dictionary<int, (int Count, decimal Total, List<int> DrawNumbers)> _slotPendingWins = new();

    // Recomputes the Record Win button from _slotPendingWins (all slots) rather than just the
    // active slot's _pendingWins — call this instead of setting _btnRecordWin directly anywhere
    // a slot's pending-win state changes, so the button's total always reflects every ticket.
    void RefreshRecordWinButton()
    {
        int totalCount = _slotPendingWins.Values.Sum(v => v.Count);
        decimal totalAmount = _slotPendingWins.Values.Sum(v => v.Total);
        // User's explicit ask 2026-08-19: this banner (manual fallback for the rare case a win
        // hasn't aged into the auto-record window yet — see AutoCheckAndRecordAllSlotsAsync)
        // kept popping up and was unwanted — wins already record themselves automatically within
        // a few minutes with no tap needed, so the banner is permanently hidden now. Text/total
        // below is still computed (cheap, and other code reads _slotPendingWins) but never shown.
        _btnRecordWin.IsVisible = false;
        _btnDismissRecordWin.IsVisible = false;
        // User's explicit ask 2026-08-15: append the actual draw #(s) so the button doubles as
        // proof of what it's about to record — if this banner keeps reappearing, seeing the SAME
        // draw # every time means something's wrong (it should have been recorded already);
        // seeing a NEW draw # each time means it's legitimately a different win.
        string drawSuffix = string.Join(", ", _slotPendingWins.Values.SelectMany(v => v.DrawNumbers).OrderBy(n => n).Select(n => $"#{n}"));
        _btnRecordWin.Text = _viewingOnly
            ? (totalCount > 1 ? $"{totalCount} Wins (test) (${totalAmount:N2}) {drawSuffix}" : $"Win (test) (${totalAmount:N2}) {drawSuffix}")
            : (totalCount > 1 ? $"Record {totalCount} Wins (${totalAmount:N2}) {drawSuffix}" : $"Record Win (${totalAmount:N2}) {drawSuffix}");

        // User's explicit ask 2026-08-16: the header's "Today: N tickets · $spend - $wins" total
        // must reflect a win the instant it's found live (auto-refresh detecting a match on an
        // in-progress ticket), not only once the user manually taps Record Win — this is the one
        // function every place _slotPendingWins changes already calls, so it's the single spot
        // that's guaranteed to fire right when a newly-staged win appears.
        RefreshTodaySpentLabel();
    }

    // Auto-refresh: Hot Spot draws every ~4 minutes. Once a draw's own time is known, the
    // page counts down to (that time + 4 minutes) and re-checks automatically when it
    // hits zero — same idea as calottery.com's own countdown to the next draw.
    int _lastSeenDrawNumber;
    DateTime _nextDrawAt = DateTime.MinValue;
    // Ticket # picker strikethrough (see IsSlotFullyFinished/RefreshSlotPickerItems): tracks
    // which slots were finished as of the last poll so OnPollTick can detect a slot crossing
    // into "finished" WHILE the page is sitting open (not just at app-open or the next
    // Save/Delete) and re-strike it immediately, per user's explicit ask. Throttled via
    // _lastFinishedSlotsCheckAt rather than every single 1-sec tick — 10 slots' worth of
    // Preferences reads every second, forever, for a value that only ever changes roughly once
    // per ~4min draw, is needless churn.
    HashSet<int> _lastKnownFinishedSlots = new();
    DateTime _lastFinishedSlotsCheckAt = DateTime.MinValue;
    static readonly TimeSpan FinishedSlotsCheckInterval = TimeSpan.FromSeconds(5);
    Label _countdownLabel = null!;
    IDispatcherTimer? _pollTimer;
    // Guards ReseedNextDrawAtAsync (see OnPollTick) so a countdown sitting expired doesn't fire
    // a fresh fetch on every single 1-sec tick while waiting for that fetch to return.
    bool _reseedingNextDrawAt;
    // Belt-and-suspenders alongside _reseedingNextDrawAt — confirmed live 2026-08-14 that the
    // busy-flag alone still let repeated reseed attempts slip through (each one hits calottery.com
    // for the live landing page, not just the cached ?query= lookup) and hammered the site once
    // per second for extended stretches while the countdown sat expired. A real elapsed-time
    // cooldown is a hard ceiling on request rate no matter what causes the flag-only guard to
    // under-block — self-healing only needs to succeed eventually, not immediately.
    DateTime _lastReseedAttemptAt = DateTime.MinValue;
    static readonly TimeSpan ReseedCooldown = TimeSpan.FromSeconds(30);

    // ♻️ Auto Refresh (Options menu) — user-selectable interval (minutes) for automatically
    // re-fetching just the header's "Current draw #" label in the background. 0 = off.
    // Persisted via KeyAutoRefreshMinutes so it survives a full app close/reopen, not just
    // page navigation within one running session — OnAppearing already resumed it across page
    // nav using the in-memory field alone; loading the saved value here covers a fresh process.
    // Hard-coded on, 8 sec — the ♻️ Auto Refresh submenu (interval picker, "Next Draw", and
    // "Refreshing" toggles) was removed from the Options menu per the user's 2026-08-20 ask;
    // these three now always run rather than being user-selectable. Preferences lookups
    // removed too so a stale "Off" saved from before this change can't reintroduce it.
    double _autoRefreshMinutes = 8.0 / 60.0;
    IDispatcherTimer? _autoRefreshTimer;
    DateTime _nextAutoRefreshAt = DateTime.MinValue;
    Label _autoRefreshCountdownLabel = null!;

    // Chevron countdown — replaces the "Refreshing in ##:##" digits with 8 small ">" chevrons
    // (one per second of the 8-sec Auto Refresh interval, see AutoRefreshMinuteOptions), filling
    // left to right in sync with the SAME _nextAutoRefreshAt countdown the digits used to read
    // off (see UpdateRefreshChevrons), then clearing and looping again next lap. User's explicit
    // ask 2026-08-19, reference image: a row of right-pointing chevron arrowheads lighting up
    // like a progress bar. Only shown for the actively-counting-down case; the "closed until
    // 6am" / "Refreshing…" edge cases still use _autoRefreshCountdownLabel's text.
    Border _refreshChevronsBorder = null!;
    Polygon[] _refreshChevrons = null!;
    // Full Screen's own copy — same countdown, drawn a bit bigger to match the rest of Full
    // Screen's bigger text, and left-aligned (not centered like _fullScreenDrawLabel/
    // _fullScreenCountdownLabel above it) — user's explicit ask 2026-08-19.
    Border _fullScreenChevronsBorder = null!;
    Polygon[] _fullScreenChevrons = null!;
    // User's explicit ask 2026-08-19: filled chevrons gradient green -> yellow left to right,
    // rather than one flat fill color — see LerpChevronColor.
    static readonly Color ChevronFilledColorStart = Color.FromArgb("#4CAF7D"); // green
    static readonly Color ChevronFilledColorEnd = Color.FromArgb("#FFD54F"); // yellow
    static readonly Color ChevronEmptyColor = Color.FromArgb("#3A4A5E");

    static Color LerpChevronColor(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgba(
            ChevronFilledColorStart.Red + (ChevronFilledColorEnd.Red - ChevronFilledColorStart.Red) * t,
            ChevronFilledColorStart.Green + (ChevronFilledColorEnd.Green - ChevronFilledColorStart.Green) * t,
            ChevronFilledColorStart.Blue + (ChevronFilledColorEnd.Blue - ChevronFilledColorStart.Blue) * t,
            1.0);
    }

    // Solid arrowhead polygon (not a text glyph) — user's explicit ask for a "thicker" chevron
    // than the thin ">" character originally used. notch=0.35*w matches the interlocking
    // chevron-bar look from the reference image: tip point on the right, concave notch on the
    // left so consecutive chevrons appear to nest into each other.
    static PointCollection ChevronPoints(double w, double h)
    {
        double notch = w * 0.35;
        return new PointCollection
        {
            new Point(0, 0),
            new Point(w - notch, 0),
            new Point(w, h / 2),
            new Point(w - notch, h),
            new Point(0, h),
            new Point(notch, h / 2),
        };
    }

    // Builds one 8-chevron track (rounded-corner Border + the Polygon chevrons inside it) at the
    // given chevron size — shared by the header row's small track and Full Screen's bigger one,
    // so the two stay visually identical apart from size. IsVisible starts false; callers wire up
    // their own show/hide gating.
    static (Border Border, Polygon[] Chevrons) BuildChevronsTrack(double chevronW, double chevronH)
    {
        var chevrons = new Polygon[8];
        var inner = new HorizontalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
        for (int i = 0; i < chevrons.Length; i++)
        {
            chevrons[i] = new Polygon { Points = ChevronPoints(chevronW, chevronH), Fill = ChevronEmptyColor, WidthRequest = chevronW, HeightRequest = chevronH };
            inner.Add(chevrons[i]);
        }
        var border = new Border
        {
            StrokeThickness = 1,
            Stroke = ChevronEmptyColor,
            StrokeShape = new RoundRectangle { CornerRadius = 5 },
            Background = Color.FromArgb("#141C26"),
            Padding = new Thickness(3, 2),
            Content = inner,
            HorizontalOptions = LayoutOptions.Start,
            IsVisible = false,
        };
        return (border, chevrons);
    }

    // Sum of remaining draws across every saved slot that's currently mid-ticket — i.e.
    // excludes a slot whose start draw hasn't come up yet AND one that's already finished all
    // its covered draws (user's explicit ask 2026-08-19: only count slots actually counting
    // down right now). Shares the same "not counting the draw in progress" rule as the
    // single-ticket gamesLeft in UpdateTicketTimeRemainingLabel, just totalled across slots.
    Label _totalDrawsLeftLabel = null!;

    // 📊 calCountDown — experimental, separate from the "Next draw" countdown above (that one
    // predicts from the draw's own posted timestamp; this one predicts from when THIS device
    // actually observed the draw# change). Piggybacks on whatever Auto Refresh is already
    // doing — it has no polling of its own — so its accuracy is capped by the Auto Refresh
    // interval currently selected (can't detect a change any faster than it checks). The 4:00
    // countdown resets from CheckAutoRefreshDrawChangeAsync's onFlyInStarting callback, fired the
    // instant the ball-drop animation for a genuinely new draw actually starts (not the moment
    // the draw# is merely detected — see that callback's comment for why the two differ by
    // 15+ sec); this is a rolling best-effort prediction that resyncs on every catch, not a
    // fixed clock synced to calottery.com's own timing. User's explicit ask, framed as an
    // experiment to see how close a device-side guess can get.
    // Hard-coded on — see _autoRefreshMinutes' comment, same 2026-08-20 change.
    bool _calCountdownEnabled = true;
    // In-memory only, NOT persisted — tried persisting this across app close/reopen
    // (2026-08-19) so the countdown would resume instead of restarting on "waiting for next
    // draw change…". Reverted same day: user reported the restored value could read up to 2
    // real draws stale by the time Auto Refresh caught back up, which isn't acceptable for a
    // "current draw" reference the user relies on live — always starting fresh on a new page
    // instance means it re-anchors on the very next genuinely observed draw change instead of
    // trusting however old the last-saved target was.
    DateTime _calNextChangeAt = DateTime.MinValue;
    Label _calCountdownLabel = null!;
    // Plain-text mirror of whatever _calCountdownLabel is currently showing — kept in sync by
    // hand alongside every SetCalCountdownText call, since the label itself is driven via
    // FormattedText (see that method) and Label.Text does NOT auto-update from FormattedText.
    // Full Screen's countdown label reads this instead of _calCountdownLabel.Text.
    string _calCountdownDisplayText = "";
    // Toggled once per OnPollTick second while calCountDown is clamped at 00:00 (the draw is
    // running late past the predicted 4:00) — drives just the "00:00" span's color between
    // normal and transparent in SetCalCountdownText, so only that part blinks (user's explicit
    // ask 2026-08-21 — an earlier version flashed the whole "(approx.) Next Draw: 00:00" line
    // via Label.Opacity and was rejected live: "that didn't look good everything flashing").
    // Turns off on its own the instant onFlyInStarting resets _calNextChangeAt (see that
    // callback's comment) and the countdown branch below stops taking the <= Zero path.
    bool _calCountdownFlashOn;

    // Hard-coded on — see _autoRefreshMinutes' comment, same 2026-08-20 change. Previously
    // hidden-by-default/togglable; now always shown.
    bool _showRefreshingLabel = true;

    // Fresher (but numbers-free) draw number from the main Hot Spot page — used as an upper
    // bound so ▶ can't page into a draw that hasn't happened yet (see PageDrawAsync).
    int _approxCurrentDrawNumber;
    Button _btnPrevDraw = null!;
    Button _btnNextDraw = null!;

    // Draw currently shown on the grid via the ◀/▶ paging buttons — 0 means paging hasn't
    // been used yet this session, so the first tap falls back to whatever's already in the
    // Search box / Start box / last-seen draw (see PageDrawAsync).
    int _pageDrawNumber;

    // Deliberately different HUES, not just different shades of the same color — two
    // similar blues (originally used for Selected vs Drawn) were confirmed too hard to
    // tell apart at a glance, which made "did my pick actually match?" unreadable.
    static readonly Color BallDefault  = Color.FromArgb("#2D3E55");
    static readonly Color BallSelected = Color.FromArgb("#1565C0"); // your pick, not (yet) checked — blue
    static readonly Color BallDrawn    = Color.FromArgb("#8D6E63"); // drawn, but not one of your picks — tan/brown
    static readonly Color BallMatch    = Color.FromArgb("#2E7D32"); // your pick AND it was drawn — green
    static readonly Color BallBullseye    = Color.FromArgb("#C62828"); // the drawn Bulls-eye number — red
    static readonly Color BallBullseyeHit = Color.FromArgb("#FFB300"); // the Bulls-eye number AND one of your picks — gold
    static readonly Color Last200PayoutButtonIdleColor      = Color.FromArgb("#243447");
    static readonly Color Last200PayoutButtonArmedColor     = Color.FromArgb("#FFB300"); // matches BallBullseyeHit gold — "tap me next"

    public HotSpotPage()
    {
        Shell.SetNavBarIsVisible(this, false);
        BackgroundColor = Color.FromArgb("#0F1923");
        BuildLayout();
    }

    // In Hot Spot Only mode this page was launched straight from the splash screen with
    // no Home screen behind it for the user to see — so "back" closes the app instead of
    // popping to a Home the user never chose to visit. Regular mode is unchanged: Hot Spot
    // was reached from Home like any other game, so back returns there as always.
    async Task GoBackOrExitAsync()
    {
        if (Preferences.Get(MainPage.HotSpotOnlyModeKey, false))
        {
            bool confirmed = await DisplayAlert("Close App", "This will close the app. Do you want to close the app?", "Yes", "Cancel");
            if (!confirmed) return;
#if ANDROID
            // FinishAffinity() alone ends the visible task but can leave the .NET process
            // resident in the background — reopening the app while that stale process is
            // still alive skips MAUI's normal startup and lands on a blank white screen.
            // Killing the process outright guarantees the next launch is a true cold start.
            Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.FinishAffinity();
            Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
#else
            Microsoft.Maui.Controls.Application.Current?.Quit();
#endif
            return;
        }
        await Shell.Current.GoToAsync("..", false);
    }

    protected override bool OnBackButtonPressed()
    {
        _ = GoBackOrExitAsync();
        return true;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        MigrateLegacyTicketIfNeeded();
        // Always opens fresh on Ticket 1 rather than restoring whatever slot was last
        // active — landing on an already-saved ticket (e.g. still showing last night's
        // real numbers) read as a mystery/bug when the user expected a blank page.
        LoadSlot(0); // also refreshes _slotDisplayLabel for this fresh app open — see LoadSlot

        _ = RunStartupChecksAsync(); // seeds the current draw#, then auto-checks any finished tickets — see method
        _pollTimer ??= Dispatcher.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(1);
        _pollTimer.Tick -= OnPollTick; // avoid double-subscribing if OnAppearing fires again
        _pollTimer.Tick += OnPollTick;
        _pollTimer.Start();

        if (_autoRefreshMinutes > 0) StartAutoRefreshTimer(); // resume whatever interval was picked before the page was last left

        // calCountDown's on/off preference persists across restarts same as Auto Refresh's
        // interval, but it has no timer of its own to restart — just needs its label made
        // visible again so OnPollTick starts drawing it; a fresh page instance already has
        // _calNextChangeAt=MinValue by default (see its field comment — persisting it was
        // tried and reverted), so it correctly starts back on "waiting for next draw
        // change…" rather than reusing a stale prediction from before the restart.
        if (_calCountdownEnabled) _calCountdownLabel.IsVisible = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _pollTimer?.Stop(); // no fetching while the page isn't visible
        StopAutoRefreshTimer(); // re-started in OnAppearing if _autoRefreshMinutes is still > 0

        // Leaving the page must never be how a ticket gets "saved" — only an explicit Save
        // tap does that. An unsaved slot's in-progress picks are simply dropped on exit
        // (deliberate, confirmed live 2026-08-10). This call used to also re-persist edits
        // made to an already-saved ticket after leaving it unsaved — that's now exactly the
        // behavior the user doesn't want (an unlocked-but-unsaved edit must vanish, not
        // silently stick), so PersistCurrentSlotRaw itself no-ops for an already-saved slot;
        // the guard below is kept only so this call stays a no-op for a brand-new slot too.
        bool alreadySaved = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));
        if (alreadySaved) PersistCurrentSlotRaw();
    }

    // Fetches just enough to start the countdown (draw number + time) without touching the
    // ball grid or status/prize labels — called once on page load so the timer isn't blank,
    // but never paints "already there" results before the user has made any picks.
    async Task SeedCountdownAsync()
    {
        int previouslySeen = _lastSeenDrawNumber;
        var (ok, _, _, drawNumber, _, drawTime) = await FindLatestDrawAsync();
        if (!ok) return;
        _lastSeenDrawNumber = drawNumber;
        // Deliberately NOT using the site's own posted drawTime for this anymore (was
        // drawTime.AddMinutes(4).AddSeconds(15)) — confirmed live 2026-08-14 this was the real
        // reason the countdown looked permanently broken, not just an occasional parse miss.
        // TryFastDetectLatestDrawAsync only trusts a draw once it's already 2+ minutes old (its
        // own "too fresh, waiting" guard), and detection can lag further behind that — so by the
        // time this code ever LEARNS a draw's posted time, adding the 4:15 buffer to that
        // already-old timestamp routinely lands in the PAST the instant it's computed. Same fix
        // calCountDown already proved out: anchor purely to the moment THIS device observed the
        // change. Only for a genuinely NEW draw — never reset just because the same still-current
        // draw was re-fetched.
        if (drawNumber != previouslySeen) _nextDrawAt = DateTime.Now.AddMinutes(4).AddSeconds(15);
    }

    // See the OnPollTick self-heal comment — reruns the same fetch SeedCountdownAsync does
    // whenever the countdown is caught sitting expired, so a single missed detection can't
    // leave it frozen for the rest of the session. Flag-guarded since OnPollTick fires every
    // second and this fetch can take longer than that.
    async Task ReseedNextDrawAtAsync()
    {
        _reseedingNextDrawAt = true;
        try { await SeedCountdownAsync(); }
        finally { _reseedingNextDrawAt = false; }
    }

    async Task RunStartupChecksAsync()
    {
        await SeedCountdownAsync(); // start the countdown without painting the grid before the user has picked anything
        await ShowApproxCurrentDrawAsync(); // quick informational number, independent of the checking mechanism
        // Only run the plain "paint the latest draw" refresh below when CheckFinishedTicketsAsync
        // didn't already leave the grid showing a real checked result (its own alert/jump-to-winner
        // flow already covers that case) — otherwise this would immediately clobber it with an
        // unrelated later draw.
        bool ranFinishedCheck = await CheckFinishedTicketsAsync();
        if (!ranFinishedCheck) await RefreshCurrentDrawOnGridAsync();

        // Also check every saved-but-still-in-progress ticket against the live current draw —
        // CheckFinishedTicketsAsync above only ever looks at a ticket once its whole range is
        // done, so a still-running ticket that just won on today's latest draw was otherwise
        // invisible unless the user happened to switch to it themselves.
        var latest = await FindLatestDrawAsync();
        if (latest.ok)
        {
            _ = CheckAllSlotsLiveWinAsync(latest.numbers, latest.bullseyeNumber, latest.drawNumber);
            // Catches up any backlog immediately on page open, rather than waiting for the
            // first Auto Refresh tick — see AutoCheckAndRecordAllSlotsAsync's own comment.
            await AutoCheckAndRecordAllSlotsAsync(latest.drawNumber);
        }
    }

    // User's explicit ask: the ball grid should show the latest draw's winning numbers painted
    // on automatically — on page load (here) and whenever the Ticket # stepper switches to a
    // different ticket (see SwitchToSlotAsync) — instead of requiring an explicit
    // Check/Go tap first. Reuses the same queryDrawNumber path the Search box's "Go" button
    // already uses (informational only, stageAsWin=false — see ApplyDrawResult) rather than the
    // "check latest" path, since this runs unconditionally for whatever ticket happens to be on
    // screen and must never risk staging a win against a draw outside that ticket's own covered
    // range.
    async Task RefreshCurrentDrawOnGridAsync()
    {
        // Always ask calottery.com fresh here rather than trusting whatever
        // _lastSeenDrawNumber/_approxCurrentDrawNumber already holds — those only get updated
        // by the initial page-load seed or an Auto Refresh timer tick, so reusing them made a
        // ticket switch redisplay a stale draw instead of the real current one until the timer
        // next fired. User's explicit ask 2026-08-15. FindLatestDrawAsync already does its own
        // accurate-source-first, anti-stale-regression fetch (same one page load uses), so this
        // is safe/correct whether the game is open or in its 2am-6am closed window.
        var latest = await FindLatestDrawAsync();
        if (!latest.ok) return; // couldn't reach calottery.com — leave the grid as LoadSlot left it

        // A saved ticket whose own covered range is already fully finished has nothing to do
        // with "today's latest draw" as far as CHECKING it goes — CheckFinishedTicketsAsync
        // already handles that (checking its own real range) — but user's explicit ask
        // 2026-08-15: the grid should still always show the last/current draw's numbers on page
        // load and every ticket switch, saved-and-finished or not, so there's always something
        // to look at instead of a blank grid. queryDrawNumber below already keeps this purely
        // informational for WIN STAGING (stageAsWin=false — see ApplyDrawResult), same as the
        // Search box/paging, so this can never stage a bogus win against a draw the ticket
        // didn't actually cover.

        // eligibleForFlyIn: true — user's explicit ask 2026-08-16: this IS the real current
        // draw (just fetched fresh above), so its first-ever display this session — whether
        // that's page load or a ticket switch — must go through the same clear-then-reveal
        // animation as any other live draw sighting, never an instant "spoiler" showing every
        // brown ball already in place. The _flyInPlayedForDrawNumber gate (keyed purely on the
        // draw #, not on how it was triggered) is what stops a SECOND ticket switch onto this
        // same already-revealed draw from replaying it — this call being reveal-eligible just
        // means it's allowed to fire, not that it always will.
        //
        // silent:true — no full-screen "Please wait..." overlay. Confirmed live 2026-08-13:
        // that overlay covers the whole grid (including the picks LoadSlot just repainted) for
        // the ~1-3s of the fetch, which read as the page clearing/restarting on every single
        // ticket switch. This should feel like a quiet in-place repaint, not a page reload.
        await CheckAgainstLatestDrawAsync(silent: true, queryDrawNumber: latest.drawNumber, eligibleForFlyIn: true);
    }

    // Automatically checks every saved ticket whose full covered draw range has already
    // completed, the moment the page opens — no Search/Range tap needed. Historically this
    // only staged wins (Record Win stayed an explicit tap) — as of 2026-08-16, user's explicit
    // ask, wins record themselves; see AutoCheckAndRecordAllSlotsAsync, called separately from
    // RunStartupChecksAsync/CheckAutoRefreshDrawChangeAsync, which is what actually writes them
    // now. This method's own staging into _pendingWins/_slotPendingWins below is effectively
    // superseded by that pass for any slot it also covers, but is left as-is since it's still
    // what drives this method's own jump-to-winner/status-message behavior. This is
    // NOT the same thing as the auto-refresh-every-90s feature removed earlier the same day
    // (see OnPollTick's comment): that repeatedly re-checked the CURRENT/still-in-progress
    // draw and visibly jumped between different cached answers for it. This checks each
    // ALREADY-FINISHED ticket's fixed, settled, past draw range exactly ONCE (tracked via the
    // per-slot "reviewed" flag, cleared whenever that slot is re-Saved or Deleted) — a
    // finished draw's result never changes, so there's no equivalent jumping-result risk.
    // Returns true if it actually checked any finished ticket(s) — used by RunStartupChecksAsync
    // to decide whether the page-load grid still needs its own separate "paint the latest draw"
    // refresh (see RefreshCurrentDrawOnGridAsync), since this method already leaves the grid
    // showing a real checked result whenever it finds something to check.
    async Task<bool> CheckFinishedTicketsAsync()
    {
        int currentDraw = Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber);
        if (currentDraw <= 0) return false; // don't know the current draw yet — can't tell what's finished

        // The Ticket # picker's initial build (OnAppearing's RefreshSlotPickerItems() call)
        // always runs before the current draw# is known, so every slot looks "not finished yet"
        // at that point — and an already-finished ticket that got marked Reviewed in an earlier
        // session never re-enters finishedSlots below, so it would never get a second chance to
        // pick up its strikethrough. Unconditionally refreshing here (now that currentDraw is
        // actually known) covers that case for free, independent of anything finishing today.
        RefreshSlotDisplayLabel();

        var finishedSlots = new List<int>();
        for (int s = 0; s < SlotCount; s++)
        {
            // Must actually be a SAVED/purchased ticket — every other consequential check in
            // this file (LoadSlot's spot-count re-derive, ConfirmEditIfLockedAsync,
            // OnDisappearing's persist guard, FillNewTicketDrawNumbersIfEmpty) already gates on
            // KeyPurchasedDate; this loop was the one place that didn't. Without this guard, a
            // still-unsaved slot ("picked, not saved yet") whose Start# was only ever
            // auto-filled with today's draw# (see FillNewTicketDrawNumbersIfEmpty) and whose
            // Draws is still the default of 1 looks trivially "already finished" the instant a
            // pick is made — confirmed live 2026-08-13: an unsaved Ticket 6 popped its own
            // "Finished Hot Spot Tickets Checked" dialog and hijacked the grid away from
            // whatever ticket the user was actually looking at.
            if (string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, s), ""))) continue;
            string numbers = Preferences.Get(SlotKey(KeyNumbers, s), "");
            if (string.IsNullOrWhiteSpace(numbers)) continue;
            if (Preferences.Get(SlotKey(KeyReviewed, s), false)) continue;
            int startDraw = Preferences.Get(SlotKey(KeyStartDraw, s), 0);
            if (startDraw <= 0) continue; // no receipt draw# entered — can't tell if finished
            int draws = Preferences.Get(SlotKey(KeyDraws, s), 1);
            if (startDraw + draws - 1 > currentDraw) continue; // still in progress
            finishedSlots.Add(s);
        }
        if (finishedSlots.Count == 0) return false;

        _statusLabel.TextColor = Color.FromArgb("#90CAF9");
        _statusLabel.Text = $"Checking {finishedSlots.Count} finished ticket{(finishedSlots.Count == 1 ? "" : "s")}...";
        _statusLabel.IsVisible = true;

        var winningSlots = new List<int>();
        var winningAmounts = new List<decimal>();
        var losingSlots = new List<int>();

        // See _slotScanBusy's own comment — this loop repeatedly repoints _activeSlot and every
        // global that hangs off it at a different ticket than whatever's on screen; blocks the
        // Auto Refresh timer's own ApplyDrawResult from landing on one of those tickets mid-loop
        // and mislabeling/recording its result under the wrong Ticket #.
        _slotScanBusy = true;
        try
        {
            for (int i = 0; i < finishedSlots.Count; i++)
            {
                int slot = finishedSlots[i];
                // CheckRangeAsync shows/hides _loadingOverlay itself around its own fetch loop —
                // this label just rides along on the same overlay so it's never blank while visible.
                _loadingLabel.Text = $"Checking Ticket {slot + 1} — finished draws ({i + 1} of {finishedSlots.Count})...";
                if (slot != _activeSlot) { PersistCurrentSlotRaw(); LoadSlot(slot); }
                await CheckRangeAsync(silent: true);
                decimal slotWon = _pendingWins.Sum(w => w.Amount);
                if (slotWon > 0) { winningSlots.Add(slot); winningAmounts.Add(slotWon); }
                else losingSlots.Add(slot);
                // Only mark reviewed for a LOSING slot — marking a winning one reviewed here (this
                // pass only stages the win, it never records it) made the win permanently invisible
                // to every future check: this same foreground pass skips already-reviewed slots on
                // the next page open, AND HotSpotChecker's background pass (which DOES auto-record
                // unconditionally) also skips already-reviewed slots — so a detected-but-never-tapped
                // win could sit staged forever with no path left to actually record or notify it.
                // Confirmed live 2026-08-12: a real $55 win got jumped-to and shown, but the app was
                // closed before tapping Record Win, and the next several background alarm runs
                // silently skipped it because this line had already marked it reviewed. Leaving a
                // winning slot unreviewed means it keeps getting re-offered (foreground re-check, or
                // background auto-record) until it's actually recorded — the same way HotSpotChecker
                // only reaches its own `Reviewed = true` after successfully calling AddWinAsync.
                if (slotWon == 0)
                    Preferences.Set(SlotKey(KeyReviewed, slot), true);
            }
            _loadingLabel.Text = "";

            // Jump straight to the first winning ticket (if any) so it's already on screen — Record
            // Win button live and pending wins staged — instead of leaving them to manually "switch
            // to it" themselves. Only re-runs CheckRangeAsync a second time when that winning slot
            // isn't already the one the loop happened to finish on (its pending-win state was
            // already built above).
            if (winningSlots.Count > 0)
            {
                int jumpTo = winningSlots[0];
                if (jumpTo != _activeSlot)
                {
                    PersistCurrentSlotRaw();
                    LoadSlot(jumpTo);
                    await CheckRangeAsync(silent: true);
                }
            }
        }
        finally { _slotScanBusy = false; }

        // Status-bar message (not a popup) so a finished ticket's outcome is impossible to miss
        // on page open but doesn't need dismissing — reverses the earlier 2026-08-13 "no popup,
        // silent jump only" call (user reported 2026-08-16 having to manually click through
        // saved tickets to discover a win this pass had already found and jumped to), then
        // switched from an initial DisplayAlert version to this status-line version per the same
        // session's explicit ask ("put the message in the Status bar... just once only" — this
        // line only ever gets set once, right here, the moment this pass finishes, not on some
        // repeating timer). Covers both a win and a plain "finished, no win" — every ticket this
        // pass finished checking, win or lose.
        var lines = winningSlots.Select((s, idx) => $"Ticket {s + 1}: ${winningAmounts[idx]:N2} won")
            .Concat(losingSlots.Select(s => $"Ticket {s + 1} finished — no win"));
        _statusLabel.Text = string.Join("  •  ", lines);
        _statusLabel.TextColor = winningSlots.Count > 0 ? Color.FromArgb("#4CAF7D") : Color.FromArgb("#8B9DC3");
        _statusLabel.IsVisible = true;
        _statusLabelShowsTicketWins = true; // see field's own comment — cleared by CheckAutoRefreshDrawChangeAsync once a genuinely new draw starts

        return true;
    }

    // Checks a single already-fetched draw against EVERY saved, still-in-progress slot (not
    // just whichever ticket happens to be on screen) and shows a status-bar summary if any of
    // them just won on it — separate from CheckFinishedTicketsAsync, which only ever looks at a
    // ticket once its ENTIRE covered range has finished. User's explicit ask 2026-08-16: had to
    // manually switch to Ticket 2 to discover it had won on the current live draw while Ticket 1
    // (the one on screen) hadn't — confirmed via "Yes — that's it" against a screenshot showing
    // Ticket 2's "This draw would pay $1.00" that only appeared after switching to it. This is
    // purely informational (never records/marks Reviewed — the ticket isn't finished yet, so
    // CheckFinishedTicketsAsync/HotSpotChecker's background pass still own actually recording it
    // once its range completes) and gated to fire once per draw # via _lastLiveWinPopupDrawNumber
    // so it doesn't re-alert every Auto Refresh tick for the same still-current draw. Status-bar
    // only, not a DisplayAlert popup — same "no messagebox, status bar only" call as
    // CheckFinishedTicketsAsync's finished-ticket message.
    async Task CheckAllSlotsLiveWinAsync(int[] numbers, int bullseyeNumber, int drawNumber)
    {
        if (drawNumber <= 0 || drawNumber == _lastLiveWinPopupDrawNumber) return;
        var draw = new HotSpotDrawService.DrawResult(true, numbers, bullseyeNumber, drawNumber, "", DateTime.Now);

        var hits = new List<(int slot, decimal amount)>();
        // Slots where EVERY number the user picked on that ticket was drawn — "matches == spots"
        // rather than winAmount>0, per the user's own wording ("match all number select in that
        // slot"), so this stays correct even for a hypothetical payout table with a $0 top prize.
        var fullMatches = new List<(int slot, decimal amount)>();
        for (int s = 0; s < SlotCount; s++)
        {
            if (string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, s), ""))) continue;
            if (Preferences.Get(SlotKey(KeyReviewed, s), false)) continue;
            string numbersRaw = Preferences.Get(SlotKey(KeyNumbers, s), "");
            if (string.IsNullOrWhiteSpace(numbersRaw)) continue;
            int startDraw = Preferences.Get(SlotKey(KeyStartDraw, s), 0);
            if (startDraw <= 0) continue;
            int draws = Preferences.Get(SlotKey(KeyDraws, s), 1);
            int lastDraw = startDraw + draws - 1;
            if (drawNumber < startDraw || drawNumber > lastDraw) continue; // not part of this ticket's covered range

            int[] picks;
            try { picks = numbersRaw.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray(); }
            catch { continue; }
            int spots = Preferences.Get(SlotKey(KeySpots, s), picks.Length);
            decimal wager = (decimal)Preferences.Get(SlotKey(KeyWager, s), 1.0);
            bool bullseye = Preferences.Get(SlotKey(KeyBullseye, s), false);

            var (matches, _, winAmount) = HotSpotDrawService.Score(picks, bullseye, spots, wager, draw);
            if (winAmount > 0) hits.Add((s, winAmount));
            if (matches == spots) fullMatches.Add((s, winAmount));

            // My Tickets green-ball highlight (see KeyWinNumbers' own comment) — runs for every
            // in-range slot checked here, not just ones in `hits` above, so a losing draw right
            // after a winning one clears the previous draw's green balls instead of leaving them.
            if (winAmount > 0)
            {
                Preferences.Set(SlotKey(KeyWinNumbers, s), string.Join("|", picks.Where(p => draw.Numbers.Contains(p))));
                Preferences.Set(SlotKey(KeyWinDrawNumber, s), drawNumber);
            }
            else
            {
                Preferences.Set(SlotKey(KeyWinNumbers, s), "");
                Preferences.Set(SlotKey(KeyWinDrawNumber, s), 0);
            }
        }
        // Picks up the green-ball highlight set/cleared just above the instant it changes,
        // regardless of whether this draw also has a status-banner-worthy hit below.
        HotSpotMyNumbersPanel.Refresh(_myNumbersPanel);

        // Opt-in (Options → 🏆 Matched all Numbers) — user's explicit ask: auto-jump to whichever
        // ticket just matched every number picked, so it's the reveal on screen without hunting
        // for it manually. With 2+ full matches on the same draw, jump to the one that actually
        // won the most money ("got the slot that won the money"), not just the lowest slot #.
        if (fullMatches.Count > 0 && Preferences.Get(KeyMatchedAllNumbersEnabled, false))
        {
            int jumpTo = fullMatches.OrderByDescending(m => m.amount).First().slot;
            if (jumpTo != _activeSlot)
            {
                // See _slotScanBusy's own comment — same guard as CheckFinishedTicketsAsync's
                // loop, just around a single slot switch here instead of several.
                _slotScanBusy = true;
                try
                {
                    PersistCurrentSlotRaw();
                    LoadSlot(jumpTo);
                    await CheckRangeAsync(silent: true);
                }
                finally { _slotScanBusy = false; }
            }
        }

        if (hits.Count == 0) return;
        _lastLiveWinPopupDrawNumber = drawNumber;
        string body = string.Join("  •  ", hits.Select(h => $"Ticket {h.slot + 1}: ${h.amount:N2}"));
        // ✅, not 🎯 — user's explicit ask 2026-08-17: the dart-target emoji read as claiming
        // an actual Bulls-eye hit even for a ticket with Bulls-eye off, though the underlying
        // win data was always correct (this is just a generic "you won" banner).
        _statusLabel.Text = $"✅ Draw #{drawNumber} hit — {body}";
        _statusLabel.TextColor = Color.FromArgb("#4CAF7D");
        _statusLabel.IsVisible = true;
        _statusLabelShowsTicketWins = true; // see field's own comment — cleared by CheckAutoRefreshDrawChangeAsync once a genuinely new draw starts

        // User's explicit ask 2026-08-16: the header's "Today: N tickets · $spend - $wins" total
        // must include a win found here too, not just ones on whichever ticket is on screen —
        // confirmed live: header stuck at $20 when the true total (winnings_log.json + a hit this
        // method just found on a non-active slot) was $21. This method used to be purely
        // informational (status-bar message only) — now it ALSO merges each hit into
        // _slotPendingWins, the same aggregate RefreshRecordWinButton already reads from and
        // (per its own fix earlier this session) refreshes the header off of.
        //
        // Bug fixed 2026-08-19: this loop used to skip the active slot outright, assuming
        // ApplyDrawResult always stages it instead — true when this runs from a live
        // stageAsWin:true tick, but NOT true when this is called from app-open/ticket-switch
        // (RunStartupChecksAsync -> RefreshCurrentDrawOnGridAsync), which paints the active
        // ticket's grid with stageAsWin:false (informational-only) by design. Confirmed live:
        // a real $1 win on the active, just-opened ticket announced itself in the status banner
        // ("✅ Draw #X hit — Ticket Y: $Z") but never reached the header total, because NEITHER
        // path staged it — ApplyDrawResult skipped it (stageAsWin:false) and this loop skipped
        // it too (assumed ApplyDrawResult already had). Treating every slot the same here, with
        // the same per-drawNumber DrawNumbers.Contains dedup below, fixes that gap without
        // double-staging the normal live case: when ApplyDrawResult DID already stage the active
        // slot (a real stageAsWin:true tick, right before this runs — see CheckAutoRefreshDrawChangeAsync),
        // _slotPendingWins[_activeSlot] already contains this drawNumber by the time this loop
        // runs, so the dedup check below skips it there too — no double count either way.
        foreach (var (slot, amount) in hits)
        {
            if (_slotPendingWins.TryGetValue(slot, out var cur))
            {
                if (cur.DrawNumbers.Contains(drawNumber)) continue;
                cur.DrawNumbers.Add(drawNumber);
                _slotPendingWins[slot] = (cur.Count + 1, cur.Total + amount, cur.DrawNumbers);
            }
            else
            {
                _slotPendingWins[slot] = (1, amount, new List<int> { drawNumber });
            }
        }
        RefreshRecordWinButton();
    }

    // Guards AutoCheckAndRecordAllSlotsAsync against overlapping runs — a real backlog (e.g.
    // right after this feature first shipped, or the app was closed a while) can mean fetching
    // several draws in a row, which could still be running when the next 8-sec Auto Refresh
    // tick fires.
    bool _autoRecordBusy;

    // User's explicit ask 2026-08-16: "make it automatically so, i don't have to press [Record
    // Win] again... it gets in the way" — wins must record themselves, no confirmation tap.
    // This also fixes a real gap the header total exposed the same session: CheckAllSlotsLiveWinAsync
    // (above) only ever evaluates the SINGLE current live draw each tick, so a win on an
    // already-PAST draw within a still-in-progress (not yet fully finished) OTHER ticket's range
    // was never checked at all — only a fully-finished ticket ever got a full-range walk
    // (CheckFinishedTicketsAsync here, or HotSpotChecker's own 6/7/8/9 PM background pass).
    // Confirmed live: header stuck at $20 vs the real $21 because of exactly this — a win on an
    // elapsed-but-not-final draw on a non-active ticket that no live tick ever happened to catch
    // as "the current draw" in real time.
    //
    // Walks every saved, not-yet-fully-Reviewed slot's already-elapsed but not-yet-auto-checked
    // draws (KeyLastAutoChecked is a per-slot cursor, so repeat calls — every auto-refresh tick,
    // plus page load — only ever fetch genuinely NEW draws, cheap enough to run constantly) and
    // records any win it finds directly via SummaryPage.AddWinAsync, no staging/confirmation
    // step. Marks Reviewed once a ticket's full covered range has actually finished, same rule
    // every other recording path here already used. Clears _slotPendingWins for every slot this
    // pass examines — anything it found is already recorded, not staged, so the Record Win
    // button (and the header, via RefreshRecordWinButton) should have nothing left to show once
    // this settles.
    async Task AutoCheckAndRecordAllSlotsAsync(int currentDraw)
    {
        if (currentDraw <= 0 || _autoRecordBusy) return;
        _autoRecordBusy = true;
        try
        {
            var cache = new Dictionary<int, HotSpotDrawService.DrawResult>();
            // Matched all Numbers (see CheckAllSlotsLiveWinAsync for the option itself) needs a
            // jump check here TOO, not just there — confirmed live 2026-08-18: a real full match
            // (Ticket 10, single spot) got recorded through THIS backlog-catch-up path instead of
            // the live single-draw check, because the live tick never happened to catch that
            // draw# as "the current draw" in real time (the exact same pre-existing gap this
            // method's own doc comment above already describes for win-recording in general — it
            // turns out to affect the jump feature too, since it was only wired into the live
            // path). Collected across every slot this pass touches, same as `hits` there.
            var fullMatches = new List<(int slot, decimal amount)>();

            for (int s = 0; s < SlotCount; s++)
            {
                if (string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, s), ""))) continue;
                if (Preferences.Get(SlotKey(KeyReviewed, s), false)) continue;
                string numbersRaw = Preferences.Get(SlotKey(KeyNumbers, s), "");
                if (string.IsNullOrWhiteSpace(numbersRaw)) continue;
                int startDraw = Preferences.Get(SlotKey(KeyStartDraw, s), 0);
                if (startDraw <= 0) continue;
                int draws = Preferences.Get(SlotKey(KeyDraws, s), 1);
                int coverDraw = startDraw + draws - 1;
                // Up to and including currentDraw now (was currentDraw - 1, forcing a full extra
                // draw cycle of delay before ANY win recorded — confirmed live 2026-08-22, a real
                // win sat unrecorded for minutes after it was already announced live). The actual
                // safety concern this used to work around (below) is only a ~2-minute window, not
                // a whole draw cycle — currentDraw itself gets its own per-draw safety check right
                // after it's fetched, inside the loop.
                int lastCheckable = Math.Min(coverDraw, currentDraw);

                if (lastCheckable >= startDraw)
                {
                    int lastChecked = Preferences.Get(SlotKey(KeyLastAutoChecked, s), startDraw - 1);
                    int from = Math.Max(startDraw, lastChecked + 1);
                    if (from <= lastCheckable)
                    {
                        int[] picks;
                        try { picks = numbersRaw.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).OrderBy(n => n).ToArray(); }
                        catch { continue; }
                        int spots = Preferences.Get(SlotKey(KeySpots, s), picks.Length);
                        decimal wager = (decimal)Preferences.Get(SlotKey(KeyWager, s), 1.0);
                        bool bullseye = Preferences.Get(SlotKey(KeyBullseye, s), false);

                        for (int dn = from; dn <= lastCheckable; dn++)
                        {
                            var draw = await HotSpotDrawService.FetchDrawAsync(dn, cache);
                            // A miss just means this draw isn't indexed yet — stop walking forward
                            // for THIS slot this pass (dn+1.. likely isn't posted either); the
                            // cursor stays at whatever was last successfully checked, so the next
                            // pass retries from the same `dn` instead of skipping it.
                            if (!draw.Ok) break;
                            // dn == currentDraw is the just-posted draw the caller hasn't advanced
                            // past yet — the real ~2-min-after-posting unsafe window (2026-08-16
                            // phantom-win bug) still applies to IT specifically. Bail without
                            // advancing the cursor (retries next 8-sec tick) until draw.DrawTime
                            // itself is old enough — this is what actually gets a win recorded
                            // within a couple minutes instead of making every draw wait a full
                            // extra ~4-min cycle regardless of how much time has really passed.
                            if (dn == currentDraw && draw.DrawTime > DateTime.MinValue
                                && DateTime.Now - draw.DrawTime < TimeSpan.FromMinutes(2.5)) break;
                            Preferences.Set(SlotKey(KeyLastAutoChecked, s), dn);

                            var (matches, bullseyeHit, winAmount) = HotSpotDrawService.Score(picks, bullseye, spots, wager, draw);
                            if (matches == spots) fullMatches.Add((s, winAmount));
                            if (winAmount <= 0) continue;

                            // draw.DrawNumber, not the loop's `dn` — matches HotSpotChecker.cs's
                            // own convention. FetchDrawAsync can echo back a DIFFERENT draw#
                            // than requested (documented "falls back to an arbitrary historical
                            // draw" quirk) without that alone making Ok false; labeling with the
                            // number actually scored keeps the SourceKey internally consistent
                            // even if that ever happens, instead of attributing a win to a draw#
                            // whose numbers were never actually checked.
                            string sourceKey = $"HS_{draw.DrawNumber}_{string.Join("-", picks)}";
                            string winDate = draw.DrawTime > DateTime.MinValue
                                ? draw.DrawTime.ToString("yyyy-MM-dd")
                                : DateTime.Today.ToString("yyyy-MM-dd");
                            bool added = await SummaryPage.AddWinAsync(new WinningRecord
                            {
                                Game      = "HS",
                                Date      = winDate,
                                Numbers   = string.Join(" ", picks),
                                Amount    = winAmount,
                                Note      = $"{matches}/{spots} (draw #{draw.DrawNumber})" + (bullseyeHit ? " (Bulls-eye)" : ""),
                                SourceKey = sourceKey,
                            });
                            if (added)
                            {
                                SummaryPage.NeedsRefresh = true;
                                decimal minAmount = Preferences.Get("win_min_amount", 100);
                                if (Preferences.Get("win_alert_enabled", true) && winAmount >= minAmount)
                                    NotificationHelper.ShowWin($"You Won ${winAmount:N0} on Hot Spot!", $"${winAmount:N2} ({matches}/{spots}, draw #{draw.DrawNumber})");
                            }
                        }
                    }
                }

                if (lastCheckable == coverDraw) Preferences.Set(SlotKey(KeyReviewed, s), true);
                // Bug fixed 2026-08-19: this used to run unconditionally, wiping ANY win this
                // slot had staged (from CheckAllSlotsLiveWinAsync or ApplyDrawResult) regardless
                // of whether this pass actually verified it. lastCheckable is always currentDraw
                // - 1 (see its own comment — the live/just-posted draw is deliberately excluded
                // as unsafe to query yet), so a pending win staged on the live CURRENT draw can
                // never be "already recorded above" — that draw is never in this pass's from..
                // lastCheckable walk. Confirmed live: a real $1 win on the just-opened active
                // ticket's live draw was announced in the status banner, correctly staged, then
                // silently erased right here before ever reaching the header total or Record Win
                // button — even though this slot's own backlog walk above never ran this pass
                // (lastCheckable < startDraw, nothing to catch up on yet). Only clear now when
                // every draw # this slot has staged actually falls inside the range just walked.
                if (_slotPendingWins.TryGetValue(s, out var stillPending) && stillPending.DrawNumbers.All(dn => dn <= lastCheckable))
                    _slotPendingWins.Remove(s);
            }

            // User's explicit ask 2026-08-18: status bar should only ever announce a win on the
            // CURRENT draw, never a backlog one. This pass checks currentDraw - 1 on purpose (see
            // the loop's own comment above — checking the true current draw too early returned an
            // incomplete result and recorded a real phantom win once), so by definition every win
            // it finds here is for a draw that's already one or more steps behind whatever's on
            // screen right now. Confirmed live 2026-08-18 (twice — Ticket 10 $1, Ticket 1 $16):
            // both were real, correctly-calculated wins on an already-past draw, but announcing
            // them here read as "wrong" since the on-screen grid had already moved on to a later
            // draw by the time the user checked. Recording (AddWinAsync above), the header total,
            // and the win notification (NotificationHelper.ShowWin) all still happen exactly as
            // before — this only silences the _statusLabel announcement for a non-current draw.
            // A win on the genuinely CURRENT draw is still announced instantly, just via a
            // different path: CheckAllSlotsLiveWinAsync's cross-slot banner (other tickets) and
            // ApplyDrawResult's own activeTicketWin callout (whichever ticket is on screen).
            RefreshRecordWinButton(); // also refreshes the header's today's-wins total either way

            // See fullMatches' own comment above — same jump-to-highest-payout behavior as
            // CheckAllSlotsLiveWinAsync, just reachable from this path too.
            if (fullMatches.Count > 0 && Preferences.Get(KeyMatchedAllNumbersEnabled, false))
            {
                int jumpTo = fullMatches.OrderByDescending(m => m.amount).First().slot;
                if (jumpTo != _activeSlot)
                {
                    // See _slotScanBusy's own comment.
                    _slotScanBusy = true;
                    try
                    {
                        PersistCurrentSlotRaw();
                        LoadSlot(jumpTo);
                        await CheckRangeAsync(silent: true);
                    }
                    finally { _slotScanBusy = false; }
                }
            }
        }
        finally { _autoRecordBusy = false; }
    }

    void OnPollTick(object? sender, EventArgs e)
    {
        // Keeps the big Full Screen draw-number label (above Regular Mode) in sync with the
        // header's own small copy — piggybacks on this timer (already ticking every second
        // while the page is visible) instead of hunting down every place _currentDrawLabel.Text
        // gets set. No-op, and skipped entirely, while not in Full Screen.
        if (_fullScreen) _fullScreenDrawLabel.Text = BigDrawLabelText(_currentDrawLabel.Text);

        // Automatic background re-checking REMOVED (2026-08-09) — it silently repainted the
        // whole ball grid every ~90s while the page was open, and confirmed live that
        // calottery.com's own caching can serve a different-than-expected draw depending on
        // the exact moment, so the grid didn't just refresh, it visibly jumped between
        // different draws' results with no tap from the user. Explicitly not wanted. Checking
        // is now only ever explicit: the Search box, Check Range, or the ◀/▶ draw-paging
        // buttons (see PageDrawAsync). This countdown label is still hidden
        // (IsVisible=false) — left in place doing only cheap local arithmetic, no network, in
        // case reliable auto-checking is revisited later.
        bool unseeded = _nextDrawAt == DateTime.MinValue;
        var remaining = unseeded ? TimeSpan.Zero : _nextDrawAt - DateTime.Now;
        _countdownLabel.Text = remaining <= TimeSpan.Zero
            ? (unseeded ? "Connecting…" : "Next draw…")
            : $"Next draw in {remaining:m\\:ss}";

        bool closedWindow = InHotSpotClosedWindow();

        // Self-heal: this countdown only ever gets a fresh target when a genuinely new draw#
        // is observed (see SeedCountdownAsync/CheckAgainstLatestDrawAsync/
        // CheckAutoRefreshDrawChangeAsync) — if that detection ever misses once (a parse miss,
        // a dropped connection, a draw whose posted time didn't come through), nothing else was
        // re-triggering it, so it sat on "Next draw…" forever until the page was reloaded.
        // Confirmed live 2026-08-14. Kicking a lightweight re-fetch here whenever it's found
        // sitting expired means the worst case is a brief stuck read, not a permanent one.
        // Cooldown-gated (not just the busy-flag) — also confirmed live 2026-08-14 that relying
        // on the busy-flag alone let this fire roughly once a second for extended stretches,
        // hammering calottery.com's landing page well beyond what a "next draw" countdown
        // justifies. 30s is still fast enough to recover well within one ~4min draw cycle.
        if (!unseeded && remaining <= TimeSpan.Zero && !_reseedingNextDrawAt && !closedWindow
            && DateTime.Now - _lastReseedAttemptAt >= ReseedCooldown)
        {
            _lastReseedAttemptAt = DateTime.Now;
            _ = ReseedNextDrawAtAsync();
        }

        if (_autoRefreshMinutes > 0 && _nextAutoRefreshAt != DateTime.MinValue)
        {
            // No text in any state anymore, chevrons only — user's explicit ask 2026-08-19
            // ("make sure it never says refreshing anywhere", clarified to mean the chevron
            // track stays on screen through every state, closed window included, instead of
            // falling back to the old "Refreshing: closed until 6am" / "Refreshing…" text.
            // _autoRefreshCountdownLabel is kept around (still wired into BuildRefreshRow's grid
            // cell) but never made visible or given text anymore.
            _autoRefreshCountdownLabel.IsVisible = false;
            _refreshChevronsBorder.IsVisible = _showRefreshingLabel;
            _fullScreenChevronsBorder.IsVisible = _showRefreshingLabel;

            if (closedWindow)
            {
                FillChevrons(_refreshChevrons, 0); // idle/empty — nothing is counting down right now
                FillChevrons(_fullScreenChevrons, 0);
            }
            else
            {
                var untilRefresh = _nextAutoRefreshAt - DateTime.Now;
                if (untilRefresh <= TimeSpan.Zero)
                {
                    FillChevrons(_refreshChevrons, _refreshChevrons.Length); // brief full flash right at the reset instant
                    FillChevrons(_fullScreenChevrons, _fullScreenChevrons.Length);
                }
                else
                {
                    UpdateRefreshChevrons(untilRefresh);
                }
            }
        }

        if (_calCountdownEnabled)
        {
            // Hot Spot draws stop 2:00 AM–6:00 AM (device-local/Pacific) — user's explicit call,
            // overriding the "no reliable closed window" note in ShowApproxCurrentDrawAsync
            // (that was based on one 8/11 observation at 3:03 AM). Display-only here; the
            // underlying state (_calNextChangeAt) is frozen because
            // StartAutoRefreshTimer's Tick handler skips its fetches during this same window,
            // so nothing updates it until a real draw# change is observed again after 6am.
            if (closedWindow)
            {
                SetCalCountdownText("closed until 6am");
            }
            else if (_calNextChangeAt == DateTime.MinValue)
            {
                // Hasn't caught a real draw# change yet since it was (re)started — no baseline
                // guess, see ShowApproxCurrentDrawAsync's comment. Stays on this text until the
                // first genuine change is observed.
                SetCalCountdownText("waiting for next draw change…");
            }
            else
            {
                // Clamped to 00:00 instead of a "due now…" phrase once it hits zero — the
                // phrase read as a stuck/broken state during testing; just holding at 00:00
                // until the next real observed change resets it reads more like a live clock.
                var untilChange = _calNextChangeAt - DateTime.Now;
                if (untilChange <= TimeSpan.Zero)
                {
                    // Draw is running late past the 4:00 prediction — flash just the "00:00"
                    // part once per tick instead of sitting static, so a long gap still reads
                    // as "waiting", not "stuck". Stops flashing on its own the instant
                    // onFlyInStarting resets _calNextChangeAt above and this branch stops being
                    // taken.
                    _calCountdownFlashOn = !_calCountdownFlashOn;
                    SetCalCountdownText("00:00", flashSuffix: true);
                }
                else
                {
                    SetCalCountdownText($"{untilChange:mm\\:ss}");
                }
            }
        }

        // Live per-second tick for the active ticket's time-remaining label (see
        // UpdateTicketTimeRemainingLabel) — no-ops instantly for an empty/unpurchased slot, so
        // this is cheap to call unconditionally every tick rather than tracking a separate flag.
        UpdateTicketTimeRemainingLabel();

        // Same idea across every slot at once — a fixed 12-slot Preferences loop, cheap enough
        // to run unconditionally every tick like the line above.
        UpdateTotalDrawsLeftLabel();

        CheckForNewlyFinishedSlots();

        // Full Screen mirrors whichever countdown Regular Mode actually shows the user, so the
        // two never disagree — confirmed live 2026-08-14 they were showing genuinely different
        // times, because this used to always mirror _countdownLabel (a separate "Next draw in
        // m:ss" prediction that's permanently hidden in Regular Mode, IsVisible=false) while
        // Regular Mode itself only ever visibly shows calCountDown's "(approx.) Time next Draw"
        // line — two independent predictions of the same real-world event, computed differently
        // (this one anchors +4:15 off the site's own posted draw time when available; calCountDown
        // anchors +4:00 off the moment THIS device observed the change), so they drift apart
        // rather than reading as one shared countdown. calCountDown is what's actually on screen
        // in Regular Mode, so Full Screen now shows exactly that — falls back to this label's own
        // text only if the user has calCountDown turned off.
        if (_fullScreen)
        {
            // Full Screen's countdown is a single (non-split) label — deliberately never
            // flashes, even while Regular Mode's "00:00" is blinking, so it always shows the
            // plain text (_calCountdownDisplayText, kept in sync by SetCalCountdownText).
            _fullScreenCountdownLabel.Text = _calCountdownEnabled ? _calCountdownDisplayText : _countdownLabel.Text;
        }
    }

    // Builds _calCountdownLabel's FormattedText as two spans — a static "(approx.) Next Draw: "
    // prefix and the variable suffix — so flashSuffix can blink just the suffix (e.g. "00:00")
    // via the span's own TextColor alpha, without touching the prefix at all. User's explicit
    // ask 2026-08-21 after an Opacity-on-the-whole-Label version flashed the entire line and
    // was rejected live ("that didn't look good everything flashing"). Also updates
    // _calCountdownDisplayText, the plain-text mirror Full Screen's countdown label reads from
    // (Label.Text does not auto-derive from FormattedText, so it has to be tracked separately).
    void SetCalCountdownText(string suffix, bool flashSuffix = false)
    {
        var color = Color.FromArgb("#CE93D8");
        bool suffixHidden = flashSuffix && !_calCountdownFlashOn;
        _calCountdownLabel.FormattedText = new FormattedString
        {
            Spans =
            {
                new Span { Text = "(approx.) Next Draw: ", TextColor = color },
                new Span { Text = suffix, TextColor = suffixHidden ? Colors.Transparent : color },
            }
        };
        _calCountdownDisplayText = "(approx.) Next Draw: " + suffix;
    }

    // Fills _refreshChevrons left to right, one per elapsed second of the 8-sec Auto Refresh lap
    // — reads off the exact same _nextAutoRefreshAt countdown the old "Refreshing in ##:##" text
    // used, so it's the same clock, just drawn differently. untilRefresh counts DOWN from 8:
    // elapsed=0 sec -> 0 lit, elapsed=7 sec -> 7 lit; the 8th only flashes lit for the instant
    // right before the lap rolls over and OnPollTick swaps to the "Refreshing…" text branch.
    // Math.Ceiling (not floor/truncate) — floor topped out at 7/8 lit because the 8th chevron's
    // window was the last fraction-of-a-second sliver before untilRefresh hit exactly zero and
    // OnPollTick's branch flipped to the "Refreshing…" text, so the last chevron never had a
    // whole elapsed-second of its own to floor into. Ceiling lights chevron N as soon as N
    // seconds have started elapsing (not finished), so all 8 light in turn, the last one for the
    // final fractional second right before the lap resets. Confirmed live: user reported it
    // "never hits the last chevron" with the floor version.
    void UpdateRefreshChevrons(TimeSpan untilRefresh)
    {
        double elapsed = 8.0 - untilRefresh.TotalSeconds;
        int filled = Math.Clamp((int)Math.Ceiling(elapsed), 0, _refreshChevrons.Length);
        FillChevrons(_refreshChevrons, filled);
        FillChevrons(_fullScreenChevrons, filled);
    }

    static void FillChevrons(Polygon[] chevrons, int filled)
    {
        for (int i = 0; i < chevrons.Length; i++)
            chevrons[i].Fill = i < filled
                ? LerpChevronColor((double)i / (chevrons.Length - 1))
                : ChevronEmptyColor;
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    void BuildLayout()
    {
        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto), // header (always visible, never scrolls)
                new RowDefinition(GridLength.Star),  // everything else, in one scroll region
                new RowDefinition(GridLength.Auto), // Full Screen's "Regular Mode" exit bar — 0-height (Auto, empty) outside Full Screen
            }
        };

        AtRow(BuildHeader(), 0, root);

        // Ball grid — regular mode lays out 1-80 as 10-per-row (matches calottery.com's own Hot
        // Spot board); Full Screen mode re-lays the SAME ball views out as 8-per-row for bigger
        // balls (user's explicit choice, see the RegularBallsPerRow/FullScreenBallsPerRow field
        // comment). RelayoutBallGrid does the actual row/column assignment for both cases —
        // this just creates the grid, the 80 ball views, and the half-board divider once.
        _ballGrid = new Grid
        {
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(6, 4),
            RowSpacing = 0,
            ColumnSpacing = 0,
        };
        // Background fill behind each half, added BEFORE the balls so Grid z-order (later
        // child = on top) keeps the balls in front of the fill, not covered by it.
        _topHalfBackground = BuildHalfBackground();
        _bottomHalfBackground = BuildHalfBackground();
        _ballGrid.Children.Add(_topHalfBackground);
        _ballGrid.Children.Add(_bottomHalfBackground);
        for (int n = 1; n <= 80; n++) _ballGrid.Children.Add(BuildBall(n));
        // Square outline around each half (1-40 / 41-80) instead of the old single line
        // through the middle — matches how most real Keno boards present the two blocks.
        // Color mirrors whatever's currently selected in ⚙️ Options → Hot Spot Ball Colors and
        // is kept in sync by RepaintDefaultBalls whenever that selection changes.
        _topHalfBorder = BuildHalfBorder();
        _bottomHalfBorder = BuildHalfBorder();
        _ballGrid.Children.Add(_topHalfBorder);
        _ballGrid.Children.Add(_bottomHalfBorder);
        RelayoutBallGrid(RegularBallsPerRow);

        var legend = BuildBallLegend();

        var statusPanel = new VerticalStackLayout
        {
            Spacing = 2, Padding = new Thickness(14, 4),
            Children =
            {
                // Hidden for now — the countdown/"Checking for the next draw" text isn't
                // tracking the real draw timing reliably. Left in place, not deleted, in
                // case it's revisited later.
                (_countdownLabel = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#90CAF9"), HorizontalOptions = LayoutOptions.Center, IsVisible = false }),
                // Total Draws Left (left) / Refreshing (right) share one row — user's explicit
                // ask 2026-08-19. Total Draws Left is filled in by UpdateTotalDrawsLeftLabel,
                // ticking every second alongside everything else in OnPollTick. Refreshing
                // keeps its own existing visibility rule (_showRefreshingLabel toggle), just
                // right-justified instead of centered now that it shares the row.
                BuildRefreshRow(),
                // Experimental — see the field comment on _calCountdownEnabled. Only visible
                // when the ⚙️ Options → ♻️ Auto Refresh menu's "📊 calCountDown" toggle is on
                // AND an interval is selected (it has no polling of its own to draw from).
                (_calCountdownLabel = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#CE93D8"), HorizontalOptions = LayoutOptions.Center, IsVisible = false }),
                // IsVisible starts false so the blank pre-check state doesn't leave a dead
                // empty line pushing "Top prize" away from "N of M spots selected".
                (_statusLabel = new Label { FontSize = 12, TextColor = Color.FromArgb("#8B9DC3"), HorizontalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center, IsVisible = false }),
            }
        };

        // Split into a "top" (intake panel, above the grid) and "bottom" (legend/status/buttons,
        // below the grid) container so Full Screen mode can hide both at once via SetFullScreen
        // while leaving the ball grid itself in between, untouched.
        _normalControlsTop = BuildIntakePanel();
        _normalControlsBottom = new VerticalStackLayout
        {
            Spacing = 0,
            Children = { legend, statusPanel, BuildButtonRow() }
        };

        // Everything below the header scrolls together as one unit — with the intake
        // panel, search box, status text, and buttons all taking variable space, a
        // separately-scrolled ball grid could get squeezed too short to reach every
        // row. One shared ScrollView guarantees all 80 balls and both buttons are
        // always reachable regardless of how much status text is showing.
        // Full Screen-only — sits between the (hidden, in Full Screen) intake panel and the ball
        // grid, so it fills the empty gap left there instead of the grid butting straight up
        // against the header. Larger font + generous margin per user's explicit ask, since a
        // small label read as lost/swallowed by all the empty space around it.
        _fullScreenBullseyeLabel = new Label
        {
            FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#C7D3EA"),
            HorizontalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 18, 0, 18),
            IsVisible = false,
        };

        _scrollContent = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                _normalControlsTop,
                _fullScreenBullseyeLabel,
                _ballGrid,
                _normalControlsBottom,
            }
        };
        _mainScrollView = new ScrollView { Content = _scrollContent };
        AtRow(_mainScrollView, 1, root);

        // Added here — BEFORE every modal overlay below (loading/payout/options/etc.) — so
        // those always stack visually on top of this floating panel in z-order. Added after
        // any of them, this panel would render OVER an open modal card and slice through it
        // (confirmed live 2026-08-20: opening Options while this panel was showing cut the
        // Options card in half). Row 1 only (not RowSpan 3) — this floating panel's own
        // untranslated position starts flush below the header, which HotSpotMyNumbersPanel
        // relies on for its drag clamp. See HotSpotMyNumbersPanel.cs.
        _myNumbersPanel = HotSpotMyNumbersPanel.Build(_ballGrid, _mainScrollView, _headerGrid,
            onTicketTapped: slot => { _ = SwitchToSlotAsync(slot); });
        root.Children.Add(_myNumbersPanel);
        Grid.SetRow(_myNumbersPanel, 1);
        if (HotSpotMyNumbersPanel.Enabled)
        {
            Dispatcher.Dispatch(() => HotSpotMyNumbersPanel.Show(_myNumbersPanel, _ballGrid, _mainScrollView, _headerGrid));
        }

        // Full Screen's bottom bar — just the big draw#/countdown now (no button of its own, see
        // _fullScreenEnterBtn's header-icon toggle comment). Only visible while Full Screen is
        // on. Its own row is Auto and this ContentView starts IsVisible=false, so it takes 0
        // height in regular mode instead of leaving a dead gap under the button row.
        _fullScreenDrawLabel = new Label
        {
            FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#C7D3EA"),
            HorizontalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap, // user's explicit ask — keep this on one line
        };
        _fullScreenCountdownLabel = new Label
        {
            // 15 -> 17 per user's explicit ask ("a little bigger, as long as it stays on the
            // page") — checked against the longest string this label actually shows
            // ("(approx.) Time next Draw: waiting for next draw change…") still fitting on one
            // line at this size; go no higher without re-checking that string specifically.
            FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#4CAF7D"),
            HorizontalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap, // user's explicit ask — keep this on one line
        };
        // A bit bigger than the header row's track (4x5.5) to match Full Screen's bigger text —
        // user's explicit ask 2026-08-19. Left-aligned (HorizontalOptions.Start, set inside
        // BuildChevronsTrack) rather than centered like the two labels above it, also per the
        // user's explicit ask.
        var (fullScreenChevronsBorder, fullScreenChevrons) = BuildChevronsTrack(6, 8.5);
        _fullScreenChevronsBorder = fullScreenChevronsBorder;
        _fullScreenChevrons = fullScreenChevrons;

        _fullScreenExitBar = new ContentView
        {
            IsVisible = false,
            BackgroundColor = Color.FromArgb("#0F1923"),
            Padding = new Thickness(14, 6, 14, 10),
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children = { _fullScreenDrawLabel, _fullScreenCountdownLabel, _fullScreenChevronsBorder },
            },
        };
        AtRow(_fullScreenExitBar, 2, root);

        var overlay = BuildLoadingOverlay();
        root.Children.Add(overlay);
        Grid.SetRowSpan(overlay, 3);

        var payoutOverlay = BuildPayoutOverlay();
        root.Children.Add(payoutOverlay);
        Grid.SetRowSpan(payoutOverlay, 3);

        _ballColorsOverlay = HotSpotBallColors.BuildPickerOverlay(BallDefault, RepaintDefaultBalls,
            HotSpotMyNumbersPanel.NumbersAsBalls, v => { HotSpotMyNumbersPanel.NumbersAsBalls = v; HotSpotMyNumbersPanel.Refresh(_myNumbersPanel); });
        root.Children.Add(_ballColorsOverlay);
        Grid.SetRowSpan(_ballColorsOverlay, 3);

        var past10DaysOverlay = BuildPast10DaysOverlay();
        root.Children.Add(past10DaysOverlay);
        Grid.SetRowSpan(past10DaysOverlay, 3);

        var last200Overlay = BuildLast200DrawsOverlay();
        root.Children.Add(last200Overlay);
        Grid.SetRowSpan(last200Overlay, 3);

        var ticketAnalysisOverlay = BuildTicketAnalysisOverlay();
        root.Children.Add(ticketAnalysisOverlay);
        Grid.SetRowSpan(ticketAnalysisOverlay, 3);

        var rangeSearchOverlay = BuildRangeSearchOverlay();
        root.Children.Add(rangeSearchOverlay);
        Grid.SetRowSpan(rangeSearchOverlay, 3);

        var optionsOverlay = BuildOptionsOverlay();
        root.Children.Add(optionsOverlay);
        Grid.SetRowSpan(optionsOverlay, 3);

        var ticketSlotOverlay = BuildTicketSlotOverlay();
        root.Children.Add(ticketSlotOverlay);
        Grid.SetRowSpan(ticketSlotOverlay, 3);

        var last200TicketOverlay = BuildLast200TicketOverlay();
        root.Children.Add(last200TicketOverlay);
        Grid.SetRowSpan(last200TicketOverlay, 3);

        _replayNumbersOverlay = Replay_Numbers.Build(
            () => Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber),
            HandleReplayNumbersTapped, out _showReplayNumbers);
        root.Children.Add(_replayNumbersOverlay);
        Grid.SetRowSpan(_replayNumbersOverlay, 3);

        var favoritesOverlay = HotSpotFavorites.Build(HandlePlayFavoritesTapped, out _showFavoritesOverlay);
        root.Children.Add(favoritesOverlay);
        Grid.SetRowSpan(favoritesOverlay, 3);

        var deleteTicketOverlay = BuildDeleteTicketOverlay();
        root.Children.Add(deleteTicketOverlay);
        Grid.SetRowSpan(deleteTicketOverlay, 3);

        Content = root;
    }

    // Re-parents the SAME 80 ball views (and the half-board divider) already sitting in
    // _ballGrid.Children onto a new row/column layout — never rebuilds or duplicates them, so
    // _ballViews/_ballCurrentState stay the single source of truth for every ball's state no
    // matter which mode is active. perRow must evenly divide 80 (10 and 8 both do).
    void RelayoutBallGrid(int perRow)
    {
        _ballsPerRow = perRow;
        _ballGrid.ColumnDefinitions.Clear();
        _ballGrid.RowDefinitions.Clear();
        for (int c = 0; c < perRow; c++) _ballGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        int ballRows = 80 / perRow;
        for (int r = 0; r < ballRows; r++) _ballGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        // Gap row between the two half-boards (after number 40) regardless of how many
        // columns — 40 is always an exact multiple of perRow for both 10 and 8, so it lands
        // cleanly at the start of a row either way. Fixed height (not Auto) since this row
        // has no content of its own to size it now that the divider is two square borders,
        // each spanning its own half's rows, rather than a line living in this row. Sized to
        // comfortably fit both borders' -6 negative-margin bleed (see BuildHalfBorder) without
        // the top and bottom outlines touching in the middle.
        int dividerRow = 40 / perRow;
        _ballGrid.RowDefinitions.Insert(dividerRow, new RowDefinition(new GridLength(20)));

        for (int n = 1; n <= 80; n++)
        {
            int idx = n - 1;
            int row = idx / perRow;
            if (row >= dividerRow) row++;
            var ball = _ballViews[n];
            Grid.SetRow(ball, row);
            Grid.SetColumn(ball, idx % perRow);
        }

        Grid.SetRow(_topHalfBorder, 0);
        Grid.SetRowSpan(_topHalfBorder, dividerRow);
        Grid.SetColumn(_topHalfBorder, 0);
        Grid.SetColumnSpan(_topHalfBorder, perRow);

        Grid.SetRow(_bottomHalfBorder, dividerRow + 1);
        Grid.SetRowSpan(_bottomHalfBorder, ballRows - dividerRow);
        Grid.SetColumn(_bottomHalfBorder, 0);
        Grid.SetColumnSpan(_bottomHalfBorder, perRow);

        // Same span as the two outlines above, so the fill exactly covers each half's cells.
        Grid.SetRow(_topHalfBackground, 0);
        Grid.SetRowSpan(_topHalfBackground, dividerRow);
        Grid.SetColumn(_topHalfBackground, 0);
        Grid.SetColumnSpan(_topHalfBackground, perRow);

        Grid.SetRow(_bottomHalfBackground, dividerRow + 1);
        Grid.SetRowSpan(_bottomHalfBackground, ballRows - dividerRow);
        Grid.SetColumn(_bottomHalfBackground, 0);
        Grid.SetColumnSpan(_bottomHalfBackground, perRow);

        // Regular mode keeps the original fixed 36px ball, confirmed working at 10/row —
        // untouched here. Full Screen mode instead sizes each ball from the ACTUAL device width
        // (DeviceDisplay, same pattern GeneratePage.xaml.cs already uses elsewhere in this app)
        // rather than a guessed fixed pixel value — a hardcoded guess (first tried: 50px/8 =
        // 448px + padding) overflowed off both edges on the real device, since 8 columns at a
        // generous fixed size doesn't account for how wide the phone actually is. Computing the
        // cell size from the real screen width guarantees all 8 columns fit on whatever device
        // this runs on, with only a small safety margin subtracted so nothing sits flush against
        // the very edge.
        bool fullScreen = perRow != RegularBallsPerRow;
        double size, margin, fontSize;
        if (fullScreen)
        {
            double screenWidthDp = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
            double horizontalPadding = 12 + 8; // _ballGrid's own Padding(6,4) left+right (12) + a small safety margin (8) so balls never sit flush against the screen edge
            double cell = Math.Max(24, (screenWidthDp - horizontalPadding) / perRow);
            margin = 2;
            size = Math.Max(24, cell - margin * 2);
            fontSize = Math.Max(14, Math.Round(size * 0.4));
        }
        else
        {
            size = 36;
            margin = 2;
            fontSize = 13;
        }
        foreach (var ball in _ballViews.Values)
        {
            ball.WidthRequest = size;
            ball.HeightRequest = size;
            ball.Margin = new Thickness(margin);
            if (ball.Content is Label lbl) lbl.FontSize = fontSize;
        }
    }

    // Single entry/exit point for Full Screen mode — flips which controls are visible and
    // re-lays the ball grid out at the matching size. See the field-group comment above
    // RegularBallsPerRow for the full rationale.
    void SetFullScreen(bool on)
    {
        if (_fullScreen == on) return;
        _fullScreen = on;
        _normalControlsTop.IsVisible = !on;
        _normalControlsBottom.IsVisible = !on;
        _fullScreenExitBar.IsVisible = on;
        _fullScreenBullseyeLabel.IsVisible = on;
        if (on) _fullScreenBullseyeLabel.Text = _bullseye ? "🎯 Bulls-eye: On" : "🎯 Bulls-eye: Off";
        // Stays visible in both modes now — this same icon toggles both directions, see its
        // own comment. Glyph flips to show which tap it's offering next.
        _fullScreenExpandGlyph.IsVisible = !on;
        _fullScreenToggleIconView.IsVisible = on;
        // Full Screen only ever shows the ball grid inside the ScrollView (top/bottom controls
        // collapsed above), so centering it top-to-bottom in the leftover space beats leaving it
        // stuck at the top with a dead gap above the Regular Mode bar. Regular mode has enough
        // content to fill/exceed the screen anyway, so Fill vs. Center makes no visible
        // difference there — reset to Fill purely so this doesn't leave a stale Center behind.
        _scrollContent.VerticalOptions = on ? LayoutOptions.Center : LayoutOptions.Fill;
        RelayoutBallGrid(on ? FullScreenBallsPerRow : RegularBallsPerRow);
        if (on)
        {
            // OnPollTick keeps both of these live from here on; this just avoids a blank label
            // for the ~1s until the next tick.
            _fullScreenDrawLabel.Text = BigDrawLabelText(_currentDrawLabel.Text);
            _fullScreenCountdownLabel.Text = _calCountdownEnabled ? _calCountdownDisplayText : _countdownLabel.Text;
        }
    }

    // The header's own "Current draw: #N (as of H:MM a.m.)" text is too long to fit one line at
    // the Full Screen label's bigger font — user's explicit ask to keep it on a single line.
    // Dropping the "(as of ...)" tail (redundant with the header, which is still visible above)
    // is enough to make "Current draw: #N" fit comfortably; LineBreakMode.NoWrap on the label
    // itself is the backstop in case a future longer draw number still doesn't fit.
    static string BigDrawLabelText(string headerText)
    {
        int idx = headerText.IndexOf(" (", StringComparison.Ordinal);
        return idx >= 0 ? headerText[..idx] : headerText;
    }

    // User's explicit ask 2026-08-16: the header's "Curr. draw: #N (as of 2:08 p.m.)" wrapped
    // to a second line on the phone's own width — condenses the site's scraped "2:08 p.m." (or
    // "2:08 PM") down to "2:08p", dropping the space and every period. Combined with "Current"
    // -> "Curr." and "as of" -> "of" at the call site, this is what makes the whole thing fit on
    // one line without changing what info is shown.
    static string AbbreviateDrawTime(string raw) =>
        System.Text.RegularExpressions.Regex.Replace(raw.Trim(), @"\s*([AaPp])\.?[Mm]\.?\s*$", "$1");

    static T AtRow<T>(T v, int row, Grid g) where T : View { Grid.SetRow(v, row); g.Children.Add(v); return v; }

    // Refreshing (Start-justified) + Total Draws Left (End-justified) on one row — user's
    // explicit ask 2026-08-19. See the field comments on _autoRefreshCountdownLabel and
    // _totalDrawsLeftLabel for what each shows and when. IsVisible starts false on Refreshing
    // (matches its prior standalone default); Total Draws Left starts visible since it has no
    // comparable "still testing" gate.
    Grid BuildRefreshRow()
    {
        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
        };
        row.Add(_autoRefreshCountdownLabel = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#4CAF7D"), HorizontalOptions = LayoutOptions.Start, IsVisible = false }, 0, 0);

        var (chevronsBorder, chevrons) = BuildChevronsTrack(4, 5.5);
        _refreshChevrons = chevrons;
        _refreshChevronsBorder = chevronsBorder;
        row.Add(chevronsBorder, 0, 0);

        row.Add(_totalDrawsLeftLabel = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#90CAF9"), HorizontalOptions = LayoutOptions.End }, 1, 0);
        // User's explicit ask 2026-08-19: tapping the total brings up a per-ticket breakdown of
        // what it's adding together, same tap-to-detail idea as _todaySpentLabel elsewhere.
        var totalDrawsLeftTap = new TapGestureRecognizer();
        totalDrawsLeftTap.Tapped += (_, _) => _ = ShowDrawsLeftByTicketAsync();
        _totalDrawsLeftLabel.GestureRecognizers.Add(totalDrawsLeftTap);
        return row;
    }

    View BuildHeader()
    {
        var grid = _headerGrid = new Grid
        {
            BackgroundColor = HeaderNormalColor,
            Padding = new Thickness(4, 8),
            RowSpacing = 2,
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            }
        };

        // "7 LOTTERY" gold ball watermark — every other game page's header has this
        // (declared in XAML), but HotSpotPage is built entirely in code and never got it.
        // User's explicit ask 2026-08-20: moved from centered-with-a-TranslationX-offset to
        // sitting directly beside the title text in a left-aligned HorizontalStackLayout (see
        // titleRow below) — frees up room on the right of the header for the new 🎫 ticket
        // button without the title/logo pair crowding into it.
        var watermark = new GraphicsView
        {
            Drawable = new BallWatermark(),
            Opacity = 0.70, HeightRequest = 38, WidthRequest = 38,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
        };

        var title = new Label
        {
            Text = "🎯 Hot Spot", FontSize = 15, FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White, VerticalOptions = LayoutOptions.Center,
        };
        // User's ask 2026-08-20: tapping the title opens calottery.com's own Hot Spot page —
        // same Launcher.OpenAsync pattern MainPage's calottery link and StatsAnalyzePage's
        // AI Studio link already use.
        title.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await Launcher.OpenAsync("https://www.calottery.com/en/draw-games/hot-spot"))
        });

        // Logo + title as one left-aligned unit — user's explicit ask 2026-08-20 to move both
        // to the left (previously centered in the Star column) to make room for the new 🎫
        // button on the right without crowding.
        var titleRow = new HorizontalStackLayout
        {
            Spacing = 2, HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center,
            Children = { watermark, title },
        };

        var homeIcon = new Label
        {
            Text = "⌂", FontSize = 21, TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
        };
        // Glossy raised circle, same Make3DBallBrush technique used for the number balls below,
        // so this reads as a tappable 3D button instead of a flat glyph.
        // 30->38 (all 4 header icons) — user's ask 2026-08-22, bigger tap targets. Safe to grow
        // freely here: the header Grid's row is GridLength.Auto (see BuildLayout/grid's
        // RowDefinitions), so it just grows taller and pushes the "Curr. draw:"/"Net:" row below
        // down with it — there's no fixed-height row these could clip into or overlap.
        var homeLbl = new Border
        {
            WidthRequest = 38, HeightRequest = 38,
            // A dark stroke on an already-dark navy ball is nearly invisible regardless of
            // thickness — confirmed live 2026-08-14 this read as "missing a border" on the
            // Full Screen icon specifically, since its thin glyph leaves mostly empty circle
            // with nothing else to anchor the eye to a button shape (Home's solid house glyph
            // hid the same problem). A light stroke actually shows up against the dark fill.
            StrokeThickness = 1.2,
            Stroke = Colors.White.WithAlpha(0.35f),
            StrokeShape = new Ellipse(),
            Background = Make3DBallBrush(Color.FromArgb("#2D3D50")),
            Margin = new Thickness(8, 0),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = homeIcon,
        };
        homeLbl.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await Shell.Current.GoToAsync("//MainPage", false))
        });

        // ⛶ Full Screen — same glossy-circle treatment as ⌂ Home. User's explicit ask 2026-08-14:
        // this ONE icon now toggles both directions (⛶ to enter, ▭ to return) instead of only
        // entering with a separate "Regular Mode" button pinned to the bottom — frees that bottom
        // space up and reads as one button with two states rather than two different controls
        // doing related things in two different places.
        // User's explicit ask 2026-08-14: keep the original "⛶" glyph for entering Full Screen,
        // only use the new hand-drawn arrow cluster (see FullScreenToggleIcon.cs) for the
        // collapse/return-to-Regular state — a hybrid of both icon styles, not the custom
        // drawable for both directions. Both live in the same spot; SetFullScreen toggles which
        // one is visible.
        _fullScreenExpandGlyph = new Label
        {
            Text = "⛶", FontSize = 20, TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
        };
        _fullScreenToggleDrawable = new FullScreenToggleIcon { Collapse = true };
        _fullScreenToggleIconView = new GraphicsView
        {
            Drawable = _fullScreenToggleDrawable,
            WidthRequest = 22, HeightRequest = 22,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            InputTransparent = true, // taps go through to _fullScreenEnterBtn's own gesture recognizer below
            IsVisible = false,
        };
        _fullScreenEnterBtn = new Border
        {
            WidthRequest = 38, HeightRequest = 38,
            // A dark stroke on an already-dark navy ball is nearly invisible regardless of
            // thickness — confirmed live 2026-08-14 this read as "missing a border" on the
            // Full Screen icon specifically, since its thin glyph leaves mostly empty circle
            // with nothing else to anchor the eye to a button shape (Home's solid house glyph
            // hid the same problem). A light stroke actually shows up against the dark fill.
            StrokeThickness = 1.2,
            Stroke = Colors.White.WithAlpha(0.35f),
            StrokeShape = new Ellipse(),
            Background = Make3DBallBrush(Color.FromArgb("#2D3D50")),
            Margin = new Thickness(4, 0, 0, 0),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new Grid { Children = { _fullScreenExpandGlyph, _fullScreenToggleIconView } },
        };
        _fullScreenEnterBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => SetFullScreen(!_fullScreen))
        });

        // 🎫 My Tickets toggle — same glossy-circle treatment as ⛶/⌂, placed right next to Full
        // Screen. User's explicit ask 2026-08-20: move this out of the ⚙️ Options submenu (see
        // ShowOptionsMenuAsync — that row was removed) and into the header as its own one-tap
        // button, now that the panel itself is scrollable/capped instead of growing unbounded.
        var ticketGlyph = new Label
        {
            Text = "🎫", FontSize = 19,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
        };
        var ticketBtn = new Border
        {
            WidthRequest = 38, HeightRequest = 38,
            StrokeThickness = 1.2,
            Stroke = Colors.White.WithAlpha(0.35f),
            StrokeShape = new Ellipse(),
            Background = Make3DBallBrush(Color.FromArgb("#2D3D50")),
            Margin = new Thickness(4, 0, 0, 0),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = ticketGlyph,
        };
        ticketBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                if (HotSpotMyNumbersPanel.Enabled)
                    HotSpotMyNumbersPanel.Hide(_myNumbersPanel);
                else
                    HotSpotMyNumbersPanel.Show(_myNumbersPanel, _ballGrid, _mainScrollView, _headerGrid);
            })
        });

        // 🔁 Replay Numbers — same glossy-circle treatment as 🎫/⛶/⌂. User's explicit ask
        // 2026-08-21: move this out of the ⚙️ Options submenu into its own one-tap header
        // button (Options row removed below, in RefreshOptionsMenuRows).
        var replayGlyph = new Label
        {
            Text = "🔁", FontSize = 19,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
        };
        // Building the overlay (scans all 12 slots, then builds the first ended ticket's full
        // editable card — pickers + 80-ball grid) takes a few real seconds — user's explicit
        // ask: show a spinner immediately on tap so the button doesn't look dead while it loads.
        var replaySpinner = new ActivityIndicator
        {
            Color = Colors.White, WidthRequest = 20, HeightRequest = 20,
            IsRunning = false, IsVisible = false,
        };
        var replayBtn = new Border
        {
            WidthRequest = 38, HeightRequest = 38,
            StrokeThickness = 1.2,
            Stroke = Colors.White.WithAlpha(0.35f),
            StrokeShape = new Ellipse(),
            Background = Make3DBallBrush(Color.FromArgb("#2D3D50")),
            Margin = new Thickness(4, 0, 0, 0),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new Grid { Children = { replayGlyph, replaySpinner } },
        };
        replayBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                replayGlyph.IsVisible = false;
                replaySpinner.IsVisible = true;
                replaySpinner.IsRunning = true;
                await Task.Yield(); // let the spinner actually paint before the overlay's synchronous build blocks the UI thread

                _showReplayNumbers();

                replaySpinner.IsRunning = false;
                replaySpinner.IsVisible = false;
                replayGlyph.IsVisible = true;
            })
        });

        var topRightIcons = new HorizontalStackLayout { Children = { _fullScreenEnterBtn, ticketBtn, replayBtn, homeLbl } };

        grid.Add(titleRow, 1, 0);
        grid.Add(topRightIcons, 2, 0);

        // Shown immediately on page load from a faster (though numbers-free) page than the
        // one used for actual match-checking — purely informational, so you don't have to
        // wait for the slower/more-cached checking mechanism just to see roughly where the
        // draw count stands right now. The header's old 🔄 refresh icon and 💰 Payout icon
        // (2026-08-11: moved into the single ⚙️ Options menu in the button row, along with
        // Color and Delete, to free up header space — see ShowOptionsMenuAsync) are gone from
        // here now; this label is the only thing left in this row.
        _currentDrawLabel = new Label
        {
            Text = "Curr. draw: —", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3"),
            VerticalOptions = LayoutOptions.Center,
        };

        // How much this app already has saved/purchased for Hot Spot TODAY across all 10
        // slots at once — previously the only way to see that was leaving this page for the
        // separate Spending Log. Reuses SpendingTracker's existing SumHotSpotCostToday()/
        // CountHotSpotTicketsToday() (already built for the Spending Log's own HS row), so
        // this can't drift from what that page reports.
        _todaySpentLabel = new Label
        {
            Text = "Today: $0.00", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3"),
            VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.End,
        };
        // Tap toggles between the spend/wins breakdown and a single Net (wins - spend) figure,
        // and back again — user's explicit ask, "vice versa."
        var todaySpentTap = new TapGestureRecognizer();
        todaySpentTap.Tapped += (_, _) => { _showNetToday = !_showNetToday; RenderTodaySpentLabel(); };
        _todaySpentLabel.GestureRecognizers.Add(todaySpentTap);

        var currentDrawRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        currentDrawRow.Add(_currentDrawLabel, 0, 0);
        currentDrawRow.Add(_todaySpentLabel, 1, 0);
        grid.Add(currentDrawRow, 0, 1);
        Grid.SetColumnSpan(currentDrawRow, 3);

        return grid;
    }

    View BuildIntakePanel()
    {
        // 3 equal columns (was 2) — added to fit the "How many ticket" slot picker next to
        // "How many spots?" per the user's explicit request. Row 1 fills out the same 3
        // columns with Bulls-eye / Wager / Total, the last of which is new too (there was
        // previously no total-cost display anywhere on this page).
        var grid = new Grid
        {
            BackgroundColor = Color.FromArgb("#1A2230"),
            Padding = new Thickness(12, 6),
            RowSpacing = 3,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto),
            }
        };

        // Q0: which of the 10 stored tickets is being viewed/edited — plain 1-10 dropdown,
        // not styled like the other games' fancier per-slot Set UI (explicit user request).
        // Switching slots persists the outgoing slot's raw fields first (PersistCurrentSlotRaw)
        // then loads the new one (LoadSlot) — see those methods for why _activeSlot is checked
        // before doing anything, to avoid a self-triggered reload loop.
        // Custom popup instead of a native Picker (see BuildTicketSlotOverlay) — tapping opens
        // _ticketSlotOverlay; this Label is just the always-visible "closed" display, refreshed
        // by RefreshSlotDisplayLabel() (called from LoadSlot and everywhere a slot's saved state
        // changes). Sidesteps the whole class of native-Picker Android reopen bugs (dotnet/maui
        // #15394) that _drawsPicker/_spotsPicker/_wagerPicker below still need the
        // _suppressPickerFocusReopen workaround for — a custom overlay has no native Picker
        // semantics to trigger that bug in the first place.
        _slotDisplayLabel = new Label { Text = "○ 1", TextColor = Colors.White, FontSize = 13 };
        _slotDisplayLabel.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(ShowTicketSlotOverlay)
        });
        // Shows whether the ticket currently on screen is actually saved, still just picks-
        // in-progress, or empty — added after a real mix-up where a user cleared an already-
        // saved ticket, switched away, and had no way to tell from the picker alone that the
        // slot had gone blank while still silently counting toward that day's spending.
        // Right-justified with a small inset (user's explicit ask 2026-08-16) — the countdown
        // text reads better hugging the right side of this column than sitting flush against it.
        _slotStatusLabel = new Label
        {
            FontSize = 10, TextColor = Color.FromArgb("#6B7A94"),
            HorizontalOptions = LayoutOptions.End, HorizontalTextAlignment = TextAlignment.End,
            Margin = new Thickness(0, 0, 6, 0),
        };

        // Q1: how many games (consecutive draws)
        _drawsPicker = new Picker { Title = "Draws", TextColor = Colors.White, FontSize = 13 };
        _drawsPicker.Focused += (s, _) => { if (_suppressPickerFocusReopen) ((Picker)s!).Unfocus(); }; // see dotnet/maui#15394 workaround comment near _suppressPickerFocusReopenUntil's declaration
        foreach (var d in DrawOptions) _drawsPicker.Items.Add(d.ToString());
        _drawsPicker.SelectedIndex = 0;
        _drawsPicker.SelectedIndexChanged += (_, _) =>
        {
            // No confirm dialog and no auto-save here — user's explicit ask 2026-08-17: changing
            // Draws/Spots/Bullseye/Wager should behave exactly like Start#/Search# and ball taps
            // already do, arm the Save button and wait for an explicit tap, nothing more. This
            // also sidesteps the whole class of confirm-dialog-misfire bugs from tonight, since
            // there's no dialog left to misfire.
            if (_suppressPickerFocusReopen) return; // LoadSlot's own SelectedIndex assignment, not a real edit — see its declaration
            if (_drawsPicker.SelectedIndex < 0) return;
            int newDraws = DrawOptions[_drawsPicker.SelectedIndex];
            if (newDraws == _draws) return;
            _draws = newDraws;
            _editUnlocked = true; UpdateEditMode(); // arms Save on an already-saved ticket, same flag UpdateSaveButtonState already checks
            UpdatePrizeDisplay();
            UpdateSlotStatusLabel(); // refreshes the "~Xh Ym total" preview against the new games count
            RecalcCoverDrawFromGames(); // user's ask 2026-08-19: Covers# = Start# (or current draw) + games - 1, empty/unsaved slots only
            UpdateSaveButtonState();
        };

        // Q2: how many spots
        _spotsPicker = new Picker { Title = "Spots", TextColor = Colors.White, FontSize = 13 };
        _spotsPicker.Focused += (s, _) => { if (_suppressPickerFocusReopen) ((Picker)s!).Unfocus(); }; // see dotnet/maui#15394 workaround comment near _suppressPickerFocusReopenUntil's declaration
        foreach (var s in SpotOptions) _spotsPicker.Items.Add(s.ToString());
        _spotsPicker.SelectedIndex = _spots - 1;
        _spotsPicker.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressPickerFocusReopen) return; // see draws picker's matching guard above
            if (_spotsPicker.SelectedIndex < 0) return;
            int newSpots = SpotOptions[_spotsPicker.SelectedIndex];
            if (newSpots == _spots) return;
            _spots = newSpots;
            _editUnlocked = true; UpdateEditMode(); // arms Save on an already-saved ticket — see draws picker's matching comment above
            // Spot count changed — trim any selections beyond the new limit.
            while (_selected.Count > _spots)
            {
                int last = _selected.Last();
                _selected.Remove(last);
                SetBallState(last, BallState.Default);
            }
            UpdateSelectedCountLabel(); // also calls UpdateSaveButtonState()
            UpdatePrizeDisplay();
        };

        // Q3: Bulls-eye yes/no
        var bullseyeLabel = new Label { Text = "Bulls-eye", FontSize = 13, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
        _bullseyeSwitch = new Switch { IsToggled = false, OnColor = Color.FromArgb("#C62828") };
        _bullseyeSwitch.Toggled += (_, e) =>
        {
            if (_suppressPickerFocusReopen) return; // see draws picker's matching guard above
            if (e.Value == _bullseye) return;
            _bullseye = e.Value;
            _editUnlocked = true; UpdateEditMode(); // arms Save on an already-saved ticket — see draws picker's matching comment above
            UpdatePrizeDisplay();
            UpdateSaveButtonState();
        };
        var bullseyeRow = new HorizontalStackLayout { Spacing = 8, Children = { bullseyeLabel, _bullseyeSwitch } };

        // Q4: wager per draw
        _wagerPicker = new Picker { Title = "Wager", TextColor = Colors.White, FontSize = 13 };
        _wagerPicker.Focused += (s, _) => { if (_suppressPickerFocusReopen) ((Picker)s!).Unfocus(); }; // see dotnet/maui#15394 workaround comment near _suppressPickerFocusReopenUntil's declaration
        foreach (var w in WagerOptions) _wagerPicker.Items.Add($"${w:0}");
        _wagerPicker.SelectedIndex = 0;
        _wagerPicker.SelectedIndexChanged += (_, _) =>
        {
            if (_suppressPickerFocusReopen) return; // see draws picker's matching guard above
            if (_wagerPicker.SelectedIndex < 0) return;
            decimal newWager = WagerOptions[_wagerPicker.SelectedIndex];
            if (newWager == _wager) return;
            _wager = newWager;
            _editUnlocked = true; UpdateEditMode(); // arms Save on an already-saved ticket — see draws picker's matching comment above
            UpdatePrizeDisplay();
            UpdateSaveButtonState();
        };

        // Total cost — wasn't shown anywhere before. Bulls-eye's wager must equal the Hot
        // Spot one (calottery.com's own rule), so toggling it on doubles the total rather
        // than adding a flat amount — see UpdatePrizeDisplay(), which keeps this in sync.
        _totalCostLabel = new Label { Text = "$1.00", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#FFD54F") };

        // Was "How many ticket" — a leftover from copying the "How many games?"/"How many
        // spots?" wording pattern, but this control isn't a quantity at all, it's a slot
        // selector (which of the 10 stored tickets you're viewing/editing). That mismatch is
        // exactly what caused real confusion: picking "2" here does not mean "I'm buying 2
        // tickets," it means "show me ticket slot 2" (empty until something is picked and
        // Saved into it). Renamed + a status line added right under it so the currently
        // selected slot's saved/empty/in-progress state is never a guess.
        var slotBlock = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = "Ticket #", FontSize = 10, TextColor = Color.FromArgb("#8B9DC3") },
                _slotDisplayLabel,
                _slotStatusLabel,
            }
        };

        // "Games" (consecutive draws this ticket covers) vs. "Spots" (numbers picked on it,
        // the only one of the two that actually changes the ball grid below) have been mixed
        // up before — see the "Ticket #" rename/status-label comment above for the same class
        // of confusion. This subtext makes the distinction visible without already knowing it.
        var drawsBlock = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Label { Text = "How many games?", FontSize = 10, TextColor = Color.FromArgb("#8B9DC3") },
                _drawsPicker,
                new Label { Text = "draws this ticket covers", FontSize = 9, TextColor = Color.FromArgb("#5A6B85") },
            }
        };
        grid.Add(drawsBlock, 0, 0);
        grid.Add(Labeled("How many spots?", _spotsPicker), 1, 0);
        grid.Add(slotBlock, 2, 0);
        grid.Add(Labeled("Bulls-eye?", bullseyeRow), 0, 1);
        grid.Add(Labeled("Wager per draw", _wagerPicker), 1, 1);
        grid.Add(Labeled("Total", _totalCostLabel), 2, 1);

        // Optional: the real starting draw # printed on the physical receipt for a
        // multi-draw ticket — lets "Check Range" walk every draw the ticket actually
        // covers instead of only ever checking "the latest" draw. Shares a row with the
        // "search a draw #" box (calottery.com's own Search by Draw Number box) so both
        // fit without stacking three separate rows.
        _startDrawEntry = new Entry
        {
            Placeholder = "Start #", PlaceholderColor = Color.FromArgb("#6B8FAF"),
            TextColor = Colors.White, FontSize = 12, Keyboard = Keyboard.Numeric,
            BackgroundColor = Colors.Transparent,
        };
        // Start#/Search# are receipt metadata, not picks — they bypass ConfirmEditIfLockedAsync
        // entirely (no "Change Ticket?" dialog) even on an already-saved ticket, and just arm
        // Save directly as you type. User's explicit ask: these two boxes are always editable.
        // User's ask 2026-08-19: typing a Start# by hand on an empty/unsaved slot should also
        // recompute Covers# (Start# + games - 1) live as they type. Originally this only ran on
        // Unfocused, but that event is unreliable here (same class of Android quirk as the
        // Picker Focused/Unfocused timing issues elsewhere in this file) — confirmed live it
        // never fired reliably from a plain digit edit, so Covers# silently never updated.
        _startDrawEntry.TextChanged += (_, _) => { UpdateSaveButtonState(); RecalcCoverDrawFromGames(); };
        // Entry has no CornerRadius of its own (unlike Button) — wrapped in a Border with
        // the same RoundRectangle/CornerRadius=8 look as the page's buttons so the box reads
        // rounded instead of the platform-default square-edged text field.
        var startDrawEntryBorder = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Background = Make3DButtonBrush(Color.FromArgb("#2D3E55")),
            Padding = new Thickness(6, 0),
            Content = _startDrawEntry,
            // Box only ever holds a 7-digit draw number — was stretching to fill the whole row
            // width for no reason, per direct feedback ("sticks out like a sore thumb"). Capped
            // to content width; Covers# below must match this exactly or the two boxes look
            // mismatched (confirmed live 2026-08-12 — only this one had a WidthRequest at first).
            // 90, not 100 — needed the extra room now both fields sit on one row (see startDrawRow).
            WidthRequest = 90,
        };

        _searchDrawEntry = new Entry
        {
            Placeholder = "Search # (7 digits)", PlaceholderColor = Color.FromArgb("#6B8FAF"),
            TextColor = Colors.White, FontSize = 12, Keyboard = Keyboard.Numeric,
            BackgroundColor = Colors.Transparent,
        };
        _searchDrawEntry.TextChanged += (_, _) => UpdateSaveButtonState();
        var searchDrawEntryBorder = new Border
        {
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Background = Make3DButtonBrush(Color.FromArgb("#2D3E55")),
            Padding = new Thickness(6, 0),
            Content = _searchDrawEntry,
            WidthRequest = 90, // matches Start# above — same size, same look
        };
        var btnGo = new Button
        {
            Text = "Go", Background = Make3DButtonBrush(Color.FromArgb("#4B5563")), TextColor = Colors.White,
            CornerRadius = 8, FontSize = 11, WidthRequest = 34, Padding = new Thickness(0),
            VerticalOptions = LayoutOptions.Fill, // matches ◀/▶ exactly — same width/padding/fill so all three read as one set
        };
        async void DoSearch()
        {
            if (int.TryParse(_searchDrawEntry.Text?.Trim(), out int dn) && dn > 0)
                await CheckAgainstLatestDrawAsync(silent: false, queryDrawNumber: dn);
        }
        btnGo.Clicked += (_, _) => DoSearch();
        _searchDrawEntry.Completed += (_, _) => DoSearch();

        // ◀/▶ paging — same idea as calottery.com's own Previous/Next buttons on the Hot
        // Spot past-winning-numbers page (they just step the currently-shown draw # by 1 and
        // reload). Left arrow sits before "Start draw#", right arrow sits after "Covers
        // Draws #", per the user's explicit request to mirror that layout.
        _btnPrevDraw = new Button
        {
            Text = "◀", Background = Make3DButtonBrush(Color.FromArgb("#4B5563")), TextColor = Colors.White,
            CornerRadius = 8, FontSize = 13, FontAttributes = FontAttributes.Bold,
            WidthRequest = 34, Padding = new Thickness(0), VerticalOptions = LayoutOptions.Fill,
        };
        _btnNextDraw = new Button
        {
            Text = "▶", Background = Make3DButtonBrush(Color.FromArgb("#4B5563")), TextColor = Colors.White,
            CornerRadius = 8, FontSize = 13, FontAttributes = FontAttributes.Bold,
            WidthRequest = 34, Padding = new Thickness(0), VerticalOptions = LayoutOptions.Fill,
        };
        _btnPrevDraw.Clicked += async (_, _) => await PageDrawAsync(-1);
        _btnNextDraw.Clicked += async (_, _) => await PageDrawAsync(+1);

        var lblStartDraw = new Label { Text = "Start#", FontSize = 10, TextColor = Color.FromArgb("#8B9DC3"), HorizontalTextAlignment = TextAlignment.Center };
        var lblCoverDraw = new Label { Text = "Covers#", FontSize = 10, TextColor = Color.FromArgb("#8B9DC3"), HorizontalTextAlignment = TextAlignment.Center };

        // Single row (2026-08-12), then user asked for the label to sit above its box instead of
        // inline to the left — but centered above just that one box, not stacked full-width like
        // the earlier 2-row attempt that doubled the block's height and got reverted. This is
        // still only 2 rows total (one label row shared by both fields, one box row shared by
        // both fields), same overall height as the inline version — ◀/Go/▶ span both rows so
        // they stay vertically centered against the label+box pair next to them.
        var startDrawRow = new Grid
        {
            ColumnSpacing = 4,
            RowSpacing = 1,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),  // ◀
                new ColumnDefinition(GridLength.Auto),  // Start# label / entry
                new ColumnDefinition(GridLength.Auto),  // Covers# label / entry
                new ColumnDefinition(GridLength.Auto),  // Go
                new ColumnDefinition(GridLength.Auto),  // ▶
            },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            HorizontalOptions = LayoutOptions.Center,
            Children = { _btnPrevDraw, lblStartDraw, startDrawEntryBorder, lblCoverDraw, searchDrawEntryBorder, btnGo, _btnNextDraw },
        }.Also(g =>
        {
            Grid.SetColumn(lblStartDraw, 1); Grid.SetRow(lblStartDraw, 0);
            Grid.SetColumn(startDrawEntryBorder, 1); Grid.SetRow(startDrawEntryBorder, 1);

            Grid.SetColumn(lblCoverDraw, 2); Grid.SetRow(lblCoverDraw, 0);
            Grid.SetColumn(searchDrawEntryBorder, 2); Grid.SetRow(searchDrawEntryBorder, 1);

            // Row 1 only (the box row), not spanning both rows — spanning made these three
            // taller than the boxes with no label above them, so they visually floated off-center
            // against the label+box pairs next to them. Sitting in row 1 lines their top/bottom
            // up exactly with the boxes instead.
            Grid.SetColumn(btnGo, 3); Grid.SetRow(btnGo, 1);

            Grid.SetColumn(_btnPrevDraw, 0); Grid.SetRow(_btnPrevDraw, 1);

            Grid.SetColumn(_btnNextDraw, 4); Grid.SetRow(_btnNextDraw, 1);
            _btnNextDraw.VerticalOptions = LayoutOptions.Center;
        });

        // "N of M spots selected" (left), the routine "N of M matched" result (middle), and the
        // prize/result line — "Top prize: $X" before a check, "No win this draw" / "You won $X!"
        // after — (right) share one row per the user's explicit request, instead of _prizeLabel
        // sitting stacked further down the page. The middle match slot moved here from
        // _statusLabel per the user's 2026-08-18 ask — it used to overwrite _statusLabel's
        // cross-slot win banner every Auto Refresh tick (as little as 8 sec later), making a
        // real win on another ticket vanish almost as soon as it appeared. Living here instead
        // means it never touches _statusLabel at all, so that banner stays on screen as long as
        // whatever set it intends.
        var selectedAndPrizeRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            Children =
            {
                // Shrunk from 14 to 11 (matches _prizeLabel) so "10 of 10 spots selected" fits
                // on one line in its half of the row instead of wrapping to two.
                (_selectedCountLabel = new Label { FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#8B9DC3"), HorizontalOptions = LayoutOptions.Start, HorizontalTextAlignment = TextAlignment.Start, LineBreakMode = LineBreakMode.NoWrap }),
                (_matchLabel = new Label { FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#8B9DC3"), HorizontalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.NoWrap }),
                // FontSize kept small enough that the longest case — "Top prize: $30,000.00
                // (Typical Prize Pool — may vary)" on 9/10-spot tickets — still fits on one line.
                (_prizeLabel = new Label { FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#FFD54F"), HorizontalOptions = LayoutOptions.End, HorizontalTextAlignment = TextAlignment.End, LineBreakMode = LineBreakMode.NoWrap }),
            },
        }.Also(g =>
        {
            Grid.SetColumn(_selectedCountLabel, 0);
            Grid.SetColumn(_matchLabel, 1);
            Grid.SetColumn(_prizeLabel, 2);
        });

        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var startDrawBlock = new VerticalStackLayout
        {
            Spacing = 2,
            Children = { startDrawRow, selectedAndPrizeRow },
        };
        grid.Add(startDrawBlock, 0, 2);
        Grid.SetColumnSpan(startDrawBlock, 3); // outer grid is now 3 columns, was 2

        return grid;
    }

    static View Labeled(string caption, View control) => new VerticalStackLayout
    {
        Spacing = 2,
        Children =
        {
            new Label { Text = caption, FontSize = 10, TextColor = Color.FromArgb("#8B9DC3") },
            control,
        }
    };

    enum BallState { Default, Selected, Drawn, Match, Bullseye, BullseyeHit }

    // Glossy sphere look (like calottery.com's own ball graphics) instead of a flat circle —
    // a radial gradient with the highlight offset toward the upper-left (simulated light
    // source) and a darker rim at the edge. Cheap (just a brush fill, no per-ball Shadow/
    // blur), so it's safe across all 80 balls without the per-row-effect perf trap seen
    // elsewhere in this app (see [[feedback_dailyfantasy_border_shape_perf]]).
    // Modeled after a real photographed lottery ball (see the reference "Fly In" image in
    // billy.md): a tight near-white specular spot that falls off fast (mimicking a glass
    // highlight), a broad midtone belly, then a darker rim from ambient occlusion at the very
    // edge. Tried this as a separate small highlight Ellipse layered on top instead (closer to
    // the real photo) — confirmed live 2026-08-13 that adding that extra Ellipse+Grid+
    // RadialGradientBrush to all 80 balls made the page hang on "Loading Hot Spot..." for
    // 20-30+ seconds. This bakes the same punchy-highlight idea into the ball's own single
    // gradient instead (one extra stop, no extra views) — much cheaper, still a clear step up
    // from the old even three-stop fade.
    static Brush Make3DBallBrush(Color baseColor) => new RadialGradientBrush
    {
        Center = new Point(0.30, 0.28),
        Radius = 0.9,
        GradientStops =
        {
            new GradientStop { Color = baseColor.AddLuminosity(0.55f), Offset = 0.0f },
            new GradientStop { Color = baseColor.AddLuminosity(0.40f), Offset = 0.12f },
            new GradientStop { Color = baseColor.AddLuminosity(0.10f), Offset = 0.32f },
            new GradientStop { Color = baseColor,                      Offset = 0.62f },
            new GradientStop { Color = baseColor.AddLuminosity(-0.32f), Offset = 1.0f },
        }
    };

    // Same glossy, raised look as the balls, adapted for rectangular buttons — a top-to-bottom
    // linear gradient (lighter top edge, darker bottom edge) instead of the balls' radial
    // highlight, which reads as "glossy sphere" on a circle but "glossy raised button" here.
    static Brush Make3DButtonBrush(Color baseColor) => new LinearGradientBrush
    {
        StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
        GradientStops =
        {
            new GradientStop { Color = baseColor.AddLuminosity(0.22f), Offset = 0.0f },
            new GradientStop { Color = baseColor,                      Offset = 0.5f },
            new GradientStop { Color = baseColor.AddLuminosity(-0.18f), Offset = 1.0f },
        }
    };

    Border BuildBall(int number)
    {
        var label = new Label
        {
            Text = number.ToString(), FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = HotSpotBallColors.CurrentDefaultTextColor(Colors.White), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center,
        };
        var ball = new Border
        {
            // Bumped up from 30x30/1px margin (340px total row width) — confirmed live there was
            // plenty of spare width on the actual device, and bigger targets are easier to tap
            // accurately. 36+2*2=40px/ball * 10 = 400px total, still fits comfortably on typical
            // modern phone widths (360dp+).
            WidthRequest = 36, HeightRequest = 36,
            // A hairline dark rim gives the edge some ambient-occlusion definition against
            // same-hued neighboring balls — real balls always show a subtle dark edge, a flat
            // 0-stroke circle reads noticeably flatter side-by-side. Just a Border property
            // (no extra view), unlike the glint Ellipse that caused the load hang — trying this
            // one on its own to see if it's cheap enough to keep.
            StrokeThickness = 0.6,
            Stroke = Colors.Black.WithAlpha(0.20f),
            StrokeShape = new Ellipse(),
            Background = HotSpotBallColors.CurrentDefaultBallBrush() ?? Make3DBallBrush(BallDefault),
            Margin = new Thickness(2),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = label,
        };
        ball.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => OnBallTapped(number)) });
        _ballViews[number] = ball;
        _ballCurrentState[number] = BallState.Default;
        return ball;
    }

    // A disposable, fully-styled clone for HotSpotFlyIn's reveal — mirrors BuildBall/
    // SetBallState's exact look for the given target state so the ghost is visually
    // indistinguishable from a real ball, but is never registered in _ballViews/
    // _ballCurrentState and carries no tap gesture (purely decorative, removed once it lands —
    // see HotSpotFlyIn.PlayAsync). Keep this in sync by hand with BuildBall/SetBallState if
    // either changes, same as HotSpotBallColors' own mirrored copy.
    // Size/margin/font are read live off the REAL ball rather than hardcoded — RelayoutBallGrid
    // resizes every ball dynamically for Full Screen mode (bigger balls, 8/row, computed from
    // the actual device width — see its own comment), so a fixed 36px ghost would land the
    // wrong size there. Copying the real ball's current values keeps the ghost correctly sized
    // in either mode without this method needing to know anything about Full Screen at all.
    Border BuildFlyInGhost(int number, BallState state)
    {
        var realBall = _ballViews[number];
        var realLabel = (Label)realBall.Content;
        var label = new Label
        {
            Text = number.ToString(), FontSize = realLabel.FontSize, FontAttributes = FontAttributes.Bold,
            TextColor = state == BallState.Default ? HotSpotBallColors.CurrentDefaultTextColor(Colors.White) : Colors.White,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center,
        };
        Brush background = state == BallState.Default
            ? (HotSpotBallColors.CurrentDefaultBallBrush() ?? Make3DBallBrush(BallDefault))
            : Make3DBallBrush(state switch
            {
                BallState.Selected => BallSelected,
                BallState.Drawn    => BallDrawn,
                BallState.Match    => BallMatch,
                BallState.Bullseye => BallBullseye,
                _                  => BallBullseyeHit,
            });
        return new Border
        {
            WidthRequest = realBall.WidthRequest, HeightRequest = realBall.HeightRequest,
            StrokeThickness = 0.6, Stroke = Colors.Black.WithAlpha(0.20f), StrokeShape = new Ellipse(),
            Background = background,
            Margin = realBall.Margin,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            Content = label,
        };
    }

    static View BuildBallLegend()
    {
        View Item(Color c, string label) => new HorizontalStackLayout
        {
            Spacing = 3, VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Border
                {
                    WidthRequest = 9, HeightRequest = 9, StrokeThickness = 0,
                    StrokeShape = new Ellipse(), BackgroundColor = c,
                },
                new Label { Text = label, FontSize = 8, TextColor = Color.FromArgb("#8B9DC3"), VerticalOptions = LayoutOptions.Center },
            }
        };
        return new FlexLayout
        {
            Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
            // Wrap (was NoWrap) — a 5th item (Bulls-eye hit) no longer reliably fits one line
            // at every screen width; letting it flow to a second row beats clipping it.
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Center,
            Padding = new Thickness(4, 2, 4, 6),
            Children =
            {
                new HorizontalStackLayout { Margin = new Thickness(0,0,6,0), Children = { Item(BallSelected, "your pick") } },
                new HorizontalStackLayout { Margin = new Thickness(0,0,6,0), Children = { Item(BallMatch, "match (win)") } },
                new HorizontalStackLayout { Margin = new Thickness(0,0,6,0), Children = { Item(BallDrawn, "drawn, not yours") } },
                new HorizontalStackLayout { Margin = new Thickness(0,0,6,0), Children = { Item(BallBullseye, "Bulls-eye") } },
                new HorizontalStackLayout { Children = { Item(BallBullseyeHit, "Bulls-eye hit!") } },
            }
        };
    }

    // Gates any action that would change an already-saved ticket's picks (ball taps, Quick
    // Pick, Clear) behind an explicit confirmation — a no-op (returns true immediately) for a
    // slot with nothing saved yet, or one already unlocked this viewing. Once confirmed, stays
    // unlocked until the slot changes (LoadSlot resets it), so the rest of this editing pass
    // doesn't re-prompt on every single tap.
    async Task<bool> ConfirmEditIfLockedAsync(bool autoSaves = false, string? changeDescription = null)
    {
        if (_editUnlocked) return true;
        bool hasSavedTicket = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));
        if (!hasSavedTicket) return true;

        // Draws/Spots/Bullseye/Wager are single-shot, complete edits (unlike ball taps, which
        // build up a selection over several taps), so those callers pass autoSaves: true and
        // save immediately once confirmed instead of relying on a separate Save tap — Save
        // arming itself (UpdateSaveButtonState) was never wired into those four handlers, so
        // the old "remember to tap Save" wording was leaving the edit stranded with no visibly
        // enabled way to commit it.
        //
        // changeDescription spells out exactly which field and what new value is about to be
        // saved (e.g. "Draws: 20 → 3") — confirmed live 2026-08-17 that a rare native Android
        // touch-misattribution bug can fire a DIFFERENT picker's change event than the one the
        // user actually tapped (e.g. tapping "Ticket 3" in the Ticket# picker landing on the
        // Draws picker instead, right after the slot picker's items list rebuilds). A generic
        // "changing this now" message gives no way to notice that before it's too late; spelling
        // out the actual field/value lets the user catch a misfire and tap Cancel instead of
        // silently overwriting a real saved ticket.
        string message = autoSaves
            ? $"Ticket {_activeSlot + 1} is already saved. {changeDescription} will be saved immediately — make sure that's really what you meant to change."
            : $"Ticket {_activeSlot + 1} is already saved. Changing the numbers now — remember to tap Save when you're done, or the change won't stick.";

        bool confirmed = await DisplayAlert(
            $"Change Ticket {_activeSlot + 1}?", message, "Yes, Change It", "Cancel");
        if (confirmed) { _editUnlocked = true; UpdateEditMode(); }
        return confirmed;
    }

    async void OnBallTapped(int number)
    {
        // On an already-saved, still-locked ticket, only tapping one of YOUR OWN picks may
        // bring up the "Change Ticket?" dialog — tapping any other ball is a silent no-op.
        // Before this, any tap at all (even on a ball that was never part of the ticket)
        // triggered the confirmation, which read as the whole grid being locked rather than
        // just the saved picks themselves.
        if (!_editUnlocked && !_selected.Contains(number))
        {
            bool hasSavedTicket = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));
            if (hasSavedTicket) return;
        }
        if (!await ConfirmEditIfLockedAsync()) return;

        if (_selected.Contains(number))
        {
            _selected.Remove(number);
            SetBallState(number, BallState.Default);
        }
        else
        {
            if (_selected.Count >= _spots) return; // at the chosen Spot limit — no-op
            _selected.Add(number);
            SetBallState(number, BallState.Selected);
        }
        UpdateSelectedCountLabel();
        UpdatePrizeDisplay();
    }

    void SetBallState(int number, BallState state)
    {
        if (!_ballViews.TryGetValue(number, out var ball)) return;
        _ballCurrentState[number] = state;
        if (state == BallState.Default)
        {
            ball.Background = HotSpotBallColors.CurrentDefaultBallBrush() ?? Make3DBallBrush(BallDefault);
            ((Label)ball.Content).TextColor = HotSpotBallColors.CurrentDefaultTextColor(Colors.White);
        }
        else
        {
            ball.Background = Make3DBallBrush(state switch
            {
                BallState.Selected    => BallSelected,
                BallState.Drawn       => BallDrawn,
                BallState.Match       => BallMatch,
                BallState.Bullseye    => BallBullseye,
                _                     => BallBullseyeHit,
            });
            // Every non-Default background is dark enough for white numbers — only Default
            // (the theme-able one) ever needs the SoftWhite-dark-text override above.
            ((Label)ball.Content).TextColor = Colors.White;
        }
    }

    // Rebuilds the Hot Spot Ball Colors popup fresh every time it's opened (not just once at
    // startup) so its ✓ marks reflect whatever was last saved — needed now that picks inside
    // the popup no longer auto-close it (user's explicit ask: pick a ball color, a line color,
    // and a thickness in one visit), so a stale, never-rebuilt overlay would otherwise keep
    // showing the marks from whenever the page first loaded. Same "rebuild every open" pattern
    // RefreshOptionsMenuRows already uses for its own live-state rows.
    void ShowBallColorsOverlay()
    {
        var parent = (Grid)_ballColorsOverlay.Parent!;
        int rowSpan = Grid.GetRowSpan(_ballColorsOverlay);
        parent.Children.Remove(_ballColorsOverlay);
        _ballColorsOverlay = HotSpotBallColors.BuildPickerOverlay(BallDefault, RepaintDefaultBalls,
            HotSpotMyNumbersPanel.NumbersAsBalls, v => { HotSpotMyNumbersPanel.NumbersAsBalls = v; HotSpotMyNumbersPanel.Refresh(_myNumbersPanel); });
        parent.Children.Add(_ballColorsOverlay);
        Grid.SetRowSpan(_ballColorsOverlay, rowSpan);
        _ballColorsOverlay.IsVisible = true;
    }

    // Repaints only the balls currently showing the plain/unpicked look — called after the
    // user picks a new ball color theme so it applies immediately without disturbing a live
    // Check result's Drawn/Match/Bullseye highlighting.
    void RepaintDefaultBalls()
    {
        foreach (var n in _ballCurrentState.Keys.ToList())
            if (_ballCurrentState[n] == BallState.Default)
                SetBallState(n, BallState.Default);

        var borderColor = new SolidColorBrush(HotSpotBallColors.CurrentBorderColor(BallDefault));
        _topHalfBorder.Stroke = borderColor;
        _bottomHalfBorder.Stroke = borderColor;
        _topHalfBorder.StrokeThickness = HotSpotBallColors.CurrentBorderThickness;
        _bottomHalfBorder.StrokeThickness = HotSpotBallColors.CurrentBorderThickness;

        // Keeps the background fill live with whatever's picked in Hot Spot Ball Colors' "BG" column.
        var bgColor = new SolidColorBrush(HotSpotBallColors.CurrentBackgroundColor());
        _topHalfBackground.Background = bgColor;
        _bottomHalfBackground.Background = bgColor;
    }

    // Plain square outline, no fill — sits on top of its half's balls (added to _ballGrid
    // after them) but stays fully transparent inside so nothing but the outline itself is
    // visible. Color and thickness start matching whatever's currently selected in Hot Spot
    // Ball Colors (color can now be set independently of the ball color) and are kept live by
    // RepaintDefaultBalls whenever either selection changes.
    //
    // Negative margin deliberately pushes the outline OUTWARD past the exact ball-cell
    // boundary it spans — at 0 margin the line sat right against the balls' own 2px margin
    // (BuildBall), which read as "almost touching." Vertical-only: _ballGrid's own Padding is
    // (6,4) — a uniform -6 on all four sides (tried first) exactly canceled the 6px left/right
    // padding but still pushed the right edge off the physical screen (confirmed live), so
    // horizontal bleed is 0 here and only top/bottom (4px padding, more room to spare) bleed
    // outward. RoundRectangle (small radius) per user's ask for rounded corners — sharp
    // Rectangle was tried first and explicitly reverted.
    Border BuildHalfBorder() => new()
    {
        StrokeShape = new RoundRectangle { CornerRadius = 8 },
        StrokeThickness = HotSpotBallColors.CurrentBorderThickness,
        Stroke = new SolidColorBrush(HotSpotBallColors.CurrentBorderColor(BallDefault)),
        Background = Colors.Transparent,
        Margin = new Thickness(0, -6, 0, -6),
        InputTransparent = true,
    };

    // A fill behind each half's balls (2026-08-19), added to _ballGrid BEFORE the balls (see
    // BuildLayout) so the balls stay on top of it, not covered by it. Same footprint/rounding
    // as BuildHalfBorder so the fill lines up exactly with the outline. Starts fully
    // transparent (HotSpotBallColors.CurrentBackgroundColor's Default) — no visual change at
    // all until a color is actually picked in Hot Spot Ball Colors' "BG" column.
    Border BuildHalfBackground() => new()
    {
        StrokeShape = new RoundRectangle { CornerRadius = 8 },
        StrokeThickness = 0,
        Background = new SolidColorBrush(HotSpotBallColors.CurrentBackgroundColor()),
        Margin = new Thickness(0, -6, 0, -6),
        InputTransparent = true,
    };

    // Redraws the whole grid for a checked draw: every one of the 20 drawn numbers gets
    // highlighted (blue, or green if it's also one of the player's picks); the Bulls-eye
    // number is red normally, or gold specifically when it's ALSO one of the player's picks
    // (a real Bulls-eye hit) — the plain-red case doesn't otherwise look any different from
    // "just the Bulls-eye ball, not yours," which was confusing at a glance.
    //
    // SetBallState (the ground truth) always runs synchronously for all 80 balls first,
    // regardless of eligibleForFlyIn — every ball's real color is correct the instant this is
    // called, animation or not. eligibleForFlyIn + drawNumber only control whether the
    // HotSpotFlyIn "draw reveal" plays on top of that: user's explicit ask (2026-08-16) is
    // once per genuinely NEW draw #, never on a repeat check against the same still-current
    // draw and never on a ticket switch that's just redisplaying it ("fill it regular... just
    // like before") — see _flyInPlayedForDrawNumber. Callers pass eligibleForFlyIn = whether
    // this is a real "checking the live draw" moment (ApplyDrawResult's own stageAsWin is
    // already exactly that signal) as opposed to an exploratory paging/search lookup or
    // CheckRangeAsync's own bulk walk, neither of which should ever trigger the reveal.
    // The lightweight per-ball flourish this screen had before HotSpotFlyIn.cs's big arc/fall
    // reveal existed. User's corrections 2026-08-15, after watching each attempt live: the
    // ball never flew in from off-screen or faded from invisible — it was already sitting in
    // its slot the whole time. First correction ruled out the fade/drop-from-above. Second
    // correction: it's an up/down vertical hop, not a size pulse — the ball hops up a little
    // and settles back down, not grows and shrinks in place. A single overshoot, not a
    // multi-bounce (user: "they would bounce once") — Easing.SpringOut gives exactly that: one
    // overshoot past the target before settling, unlike Easing.BounceOut's several shrinking
    // bounces. If Android's system animations are off (accessibility/battery saver), MAUI's
    // *To methods jump straight to their end value, so the ball still ends up 100% correct
    // even with zero animation.
    static async Task SpringInBallAsync(Border ball, uint delayMs)
    {
        if (delayMs > 0) await Task.Delay((int)delayMs);
        ball.TranslationY = -14;
        await ball.TranslateTo(0, 0, 220, Easing.SpringOut);
    }

    // User's ask 2026-08-17: when every one of the ticket's picks hits, spin those balls once
    // the reveal has fully landed — two full turns over 2 seconds at constant speed (Linear,
    // not eased) so it reads as an actual spin rather than a wobble, then stops back at its
    // normal orientation. Fire-and-forget from ApplyDrawResult; runs all picked balls together.
    async Task SpinWinningBallsAsync(IEnumerable<int> numbers)
    {
        var balls = numbers.Where(n => _ballViews.ContainsKey(n)).Select(n => _ballViews[n]).ToList();
        await Task.WhenAll(balls.Select(b => b.RotateTo(720, 2000, Easing.Linear)));
        foreach (var b in balls) b.Rotation = 0;
    }

    // onFlyInStarting: fired synchronously the instant a real animated reveal is committed to
    // play (right before the drop animation itself begins) — never on a repeat draw, ticket
    // switch, stale/busy bailout, or plain instant-fill. CheckAutoRefreshDrawChangeAsync uses
    // this to restart calCountDown's 4:00 "Time next Draw" prediction at the exact moment the
    // balls actually start falling, not the instant a new draw# is merely detected (see its own
    // comment for why that used to run 15+ sec ahead of what was on screen).
    // Returns whether this call actually painted the grid — false means it bailed out early
    // (another reveal already owns the grid, or the fetch was stale) and did NOTHING, including
    // not touching _flyInPlayedForDrawNumber. Added 2026-08-16: ApplyDrawResult used to treat
    // ANY return from this method as "the grid is now showing this draw," so a second concurrent
    // Auto Refresh tick landing on the SAME live draw while the first tick's ~22s fly-in reveal
    // was still mid-flight would hit the _flyInBusy bail-out below (correct — it must not start
    // a second animation) but then still fell through to ApplyDrawResult's win-staging/Record Win
    // button, which fired using that second call's own freshly-fetched data even though the
    // balls actually on screen (from the FIRST call's still-playing reveal) hadn't finished
    // landing yet. User: "it happen when the draw changes and the balls fly in." The first call's
    // own await eventually completes once its reveal really finishes and correctly stages the
    // win then — this bool just tells every OTHER concurrent caller to do nothing and let it.
    async Task<bool> ShowDrawResultOnGridAsync(int[] drawnNumbers, int bullseyeNumber, int drawNumber, bool eligibleForFlyIn, Action? onFlyInStarting = null)
    {
        // A reveal already in flight OWNS the grid exclusively until it finishes — completely
        // hands-off here, not even the plain instant-fill loop below. Confirmed live 2026-08-16:
        // a second concurrent check (correctly blocked from starting its OWN ghost animation by
        // the busy-guard) was still falling through to "instant fill all 80 balls," which
        // stomped every ball — including the ones the FIRST reveal's ghosts hadn't landed on
        // yet — straight to their final color while that first reveal was still visibly
        // mid-flight. This draw # simply isn't marked played (see below), so it stays eligible
        // and the next tick, once the current reveal finishes, handles it cleanly instead.
        if (_flyInBusy) return false;

        // A stale/regressed fetch claiming to be "the current live draw" (eligibleForFlyIn)
        // but numerically OLDER than a draw already shown this session must never touch the
        // grid AT ALL — not even the plain instant-fill below. Confirmed live 2026-08-16: the
        // previous fix (requiring drawNumber > _flyInPlayedForDrawNumber) correctly stopped a
        // stale draw from replaying a full ANIMATED reveal, but that same stale draw still fell
        // through to instant-fill (which had no staleness check of its own) and silently
        // overwrote the whole grid with old data — log proved it: "instant-fill draw#3292387"
        // fired right after #3292390 had already played, painting 3292387's numbers over the
        // real current state. A handful of those numbers happened to overlap with the NEXT
        // genuine draw (#3292391) and were then wrongly skipped by ITS clear step too, since
        // they already looked "being revealed." A deliberate historical lookup (Search/paging/
        // CheckRangeAsync, all eligibleForFlyIn=false) is NOT stale — it's supposed to show an
        // older draw — so this only rejects the "pretends to be live but isn't" case.
        if (eligibleForFlyIn && drawNumber > 0 && drawNumber < _flyInPlayedForDrawNumber) return false;

        var drawnPlusBullseye = drawnNumbers.ToHashSet();
        if (bullseyeNumber > 0) drawnPlusBullseye.Add(bullseyeNumber);
        // Strictly GREATER than, not just different — confirmed live via the diagnostic log
        // 2026-08-16: RefreshCurrentDrawOnGridAsync's FindLatestDrawAsync() fetch (this file's
        // own well-documented CDN-caching/fallback gotchas — see HotSpotDrawService's header)
        // returned a STALE, already-old draw # a few minutes after a NEWER one had already been
        // revealed. Real Hot Spot draw #s only ever increase, so a "different" # that's actually
        // LOWER than the last one played is never a genuinely new draw — it's a bad fetch — and
        // must never restart a full reveal. This single check protects every caller uniformly.
        // User's ask 2026-08-20: Animation Mode (Options menu toggle) — off skips the ghost
        // drop-in reveal entirely and falls through to the plain instant-fill loop below, which
        // still paints every ball's real final color, just with no animation. Deliberately
        // ANDed in here rather than folded into eligibleForFlyIn itself — the staleness check
        // just above (comparing against _flyInPlayedForDrawNumber) must keep working the same
        // regardless of this toggle. While Animation Mode is off, _flyInPlayedForDrawNumber
        // simply stops advancing (it's only ever bumped further down, inside the real-reveal
        // branch this condition gates) — harmless: turning the toggle back on just means the
        // very next live draw plays normally, same as a fresh page load would.
        // isLiveDrawChange is Animation Mode's toggle stripped back out — a genuinely new live
        // draw must still fire onFlyInStarting (resetting the calCountDown "(approx.) Next
        // Draw" prediction) even while Animation Mode is off, since that reset is the only
        // thing that ever advances it. Confirmed live 2026-08-20: with Animation Mode off, the
        // whole animated-reveal branch below (the only place onFlyInStarting used to fire) never
        // ran, so _calNextChangeAt stayed DateTime.MinValue forever and the countdown sat stuck
        // on "waiting for next draw change…" no matter how many real draws passed. playFlyIn
        // still separately gates just the ball-drop ANIMATION rendering.
        bool isLiveDrawChange = eligibleForFlyIn && drawNumber > _flyInPlayedForDrawNumber;
        bool playFlyIn = isLiveDrawChange && Preferences.Get(KeyAnimationModeEnabled, true);

        // Not playing the big reveal (repeat draw, ticket switch, exploratory lookup) — every
        // ball's real color is still set immediately regardless. ONLY when this call is the
        // direct result of a real ticket slot switch (_springOnNextInstantFill, armed by
        // SwitchToSlotAsync and consumed here) does a ball newly becoming Drawn/Match/
        // Bullseye/BullseyeHit get a quick staggered SpringInBallAsync bump-in-place — the
        // lightweight flourish this screen had before the big HotSpotFlyIn reveal existed.
        // User's explicit ask 2026-08-15: ONLY on slot switch, not on a repeat draw check,
        // paging, or search. Deliberately separate from HotSpotFlyIn.cs — user asked that
        // file left untouched.
        bool playSpring = _springOnNextInstantFill;
        _springOnNextInstantFill = false;
        if (!playFlyIn)
        {
            // Still a genuinely new live draw even though Animation Mode (or some other reason
            // playFlyIn is false) skips the animated reveal — advance the dedup marker and fire
            // the countdown-reset hook here too, same as the animated branch below does, so
            // calCountDown keeps resetting every real draw change regardless of the toggle.
            if (isLiveDrawChange)
            {
                _flyInPlayedForDrawNumber = drawNumber;
                onFlyInStarting?.Invoke();
            }
            const uint SpringStaggerMs = 45;
            int springIndex = 0;
            // Collected and awaited below (instead of each being fired-and-forgotten) so the
            // caller — ApplyDrawResult, which shows the win text/Record Win button right after
            // this method returns — never sees that button appear while a ball is still
            // visibly mid-bounce. User's explicit ask 2026-08-16: "know before my save number
            // ever hit" — the button was rendering before the matching ball had even landed.
            List<Task>? springTasks = playSpring ? new List<Task>() : null;
            for (int n = 1; n <= 80; n++)
            {
                var newState = n == bullseyeNumber
                    ? (_selected.Contains(n) ? BallState.BullseyeHit : BallState.Bullseye)
                    : drawnPlusBullseye.Contains(n)
                        ? (_selected.Contains(n) ? BallState.Match : BallState.Drawn)
                        : (_selected.Contains(n) ? BallState.Selected : BallState.Default);

                bool isNewlyDrawn = playSpring
                                     && newState is BallState.Drawn or BallState.Match or BallState.Bullseye or BallState.BullseyeHit
                                     && _ballCurrentState.GetValueOrDefault(n) != newState;

                SetBallState(n, newState);

                if (isNewlyDrawn && _ballViews.TryGetValue(n, out var ball))
                    springTasks?.Add(SpringInBallAsync(ball, (uint)(springIndex++ * SpringStaggerMs)));
            }
            if (springTasks is { Count: > 0 })
                await Task.WhenAll(springTasks);
            return true;
        }

        // Set BEFORE awaiting — closes the re-entrancy window where another check (e.g. the
        // very next 8-sec Auto Refresh tick) lands on this SAME still-current draw while the
        // first reveal is still mid-flight and would otherwise re-trigger it.
        _flyInPlayedForDrawNumber = drawNumber;

        // This IS the moment the drop animation is committed to start — fire before the awaits
        // below so a caller's onFlyInStarting hook sees it at the same instant the balls do.
        onFlyInStarting?.Invoke();

        // User's explicit ask 2026-08-16: the FIRST thing that happens is clearing the grid
        // back down to just the player's own picks — EVERY ball, unconditionally, including
        // the ones about to be revealed again this round. Confirmed live via log cross-check:
        // this used to SKIP clearing a ball that's part of THIS round's drawn set (reasoning:
        // its own ghost will paint it at landing anyway) — but if that same number ALSO
        // happened to be drawn in the immediately PREVIOUS reveal (a real, common case — ~20 of
        // 80 numbers repeat draw to draw fairly often), it was already sitting at that exact
        // same color from before, so skipping it meant it never visually blanked at all, and
        // looked "already there" the instant the new reveal started even though its own ghost
        // hadn't run yet. Every ball now blanks first, no exceptions — HotSpotFlyIn repaints
        // the drawn ones at their own landing moment, same as always.
        var targetState = new Dictionary<int, BallState>();
        for (int n = 1; n <= 80; n++)
        {
            bool isBeingRevealed = n == bullseyeNumber || drawnPlusBullseye.Contains(n);
            if (isBeingRevealed)
            {
                targetState[n] = n == bullseyeNumber
                    ? (_selected.Contains(n) ? BallState.BullseyeHit : BallState.Bullseye)
                    : (_selected.Contains(n) ? BallState.Match : BallState.Drawn);
            }
        }

        // Bulls-eye is drawn 20th/last in the real game — landing it last here too (not sorted
        // in with the other 19) makes the reveal read like an actual draw happening, not a
        // scoreboard filling in ascending order.
        var landingOrder = new List<int>(drawnNumbers);
        if (bullseyeNumber > 0) landingOrder.Add(bullseyeNumber);
        _flyInBusy = true; // covers the drop-out clear below too, same as the reveal itself — the grid is off-limits to everything else until both are done
        try
        {
            // User's explicit ask 2026-08-16: the board visibly clears itself first — every
            // PREVIOUSLY-DRAWN ball's color falls away in two waves (41-80, then a half-second
            // later 1-40) — instead of snapping straight to blank. Ghost-based, same as
            // PlayAsync's reveal: the real numbered ball is repainted to blank the instant its
            // ghost (showing the OLD color) is placed, so the number itself never disappears
            // from the grid. Only balls that currently show a real drawn color are included —
            // confirmed live on the very first real draw this played for: passing the FULL
            // 80-ball dict animated every plain gray/unmatched-blue ball too, and since the
            // stagger follows plain number order, several meaningless gray balls fell BEFORE
            // reaching whichever colored ones happened to sit later in that wave ("saw some
            // gray balls drop first, a bad effect"). Filtering to just the colored balls first
            // also means the whole thing is skipped automatically when there's nothing colored
            // at all (e.g. a fresh page load) — no separate gate needed anymore.
            var previouslyDrawn = _ballCurrentState
                .Where(kv => kv.Value is BallState.Drawn or BallState.Match or BallState.Bullseye or BallState.BullseyeHit)
                .ToDictionary(kv => kv.Key, kv => _ballViews[kv.Key]);
            if (previouslyDrawn.Count > 0)
            {
                await HotSpotFlyIn.DropOutAsync(_ballGrid, previouslyDrawn,
                    n => BuildFlyInGhost(n, _ballCurrentState[n]),
                    n => SetBallState(n, _selected.Contains(n) ? BallState.Selected : BallState.Default));
            }

            await HotSpotFlyIn.PlayAsync(landingOrder, _ballGrid, _ballViews,
                n => BuildFlyInGhost(n, targetState[n]), n => SetBallState(n, targetState[n]));
        }
        finally { _flyInBusy = false; }
        return true;
    }

    void UpdateSelectedCountLabel()
    {
        _selectedCountLabel.Text = $"{_selected.Count} of {_spots} spots selected";
        UpdateSaveButtonState();
    }

    // Green = Animation Mode on, red = off — see _btnOptions' own field comment. Green matches
    // _btnCheckRange's own color (#00695C) exactly — user's ask 2026-08-20, the brighter
    // #4CAF7D this started with "looked funny" (clashed) next to Range's teal-green right there
    // in the same row. Red uses its own explicit gradient stops instead of Make3DButtonBrush —
    // confirmed live that helper's AddLuminosity-based top/bottom shading read as flat/solid on
    // #C62828 specifically (every other color this helper's ever been used with here is
    // blue/green/gray/purple/gold — red was untested), so this spells out a clearly lighter top
    // and darker bottom by hand instead of trusting the same math to look right on red too.
    // Wider swing than a straight AddLuminosity shift would give — human color perception is
    // far less sensitive to luminance changes in saturated reds than in greens/teals (confirmed
    // live: the same relative +22%/-18% shift Make3DButtonBrush applies read as visibly flat
    // here even though it's the identical formula Range's teal-green uses successfully), so the
    // top stop is pushed almost to pale pink and the bottom almost to near-black maroon to force
    // a highlight that actually reads on a 32px-tall icon button.
    static readonly Brush AnimationModeOffBrush = new LinearGradientBrush
    {
        StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
        GradientStops =
        {
            new GradientStop { Color = Color.FromArgb("#FFCDD2"), Offset = 0.0f },
            new GradientStop { Color = Color.FromArgb("#C62828"), Offset = 0.4f },
            new GradientStop { Color = Color.FromArgb("#1A0000"), Offset = 1.0f },
        }
    };

    void RefreshAnimationModeIndicator()
    {
        _btnOptions.Background = Preferences.Get(KeyAnimationModeEnabled, true)
            ? Make3DButtonBrush(Color.FromArgb("#00695C"))
            : AnimationModeOffBrush;
    }

    // Save is only ever "live" when there's a real, complete, unsaved change sitting on
    // screen — never enabled on a partial pick, never left enabled after a tap. This is
    // what stops a second accidental tap of Save from doing anything: by the time the first
    // tap's handler returns, the button is already disabled again, so there's nothing left
    // to double-fire. User's explicit ask, after real double-Save mistakes.
    void UpdateSaveButtonState()
    {
        if (_btnSave == null) return; // called once from LoadSlot during BuildLayout, before BuildButtonRow has run

        bool enabled;
        bool complete = _selected.Count == _spots;
        bool alreadyPurchased = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));

        // Start#/Search# are receipt metadata, not picks — always compared against the saved
        // values regardless of _editUnlocked, since typing in either box never goes through
        // ConfirmEditIfLockedAsync (see their TextChanged wiring). A real change here is enough
        // to arm Save on its own, with no need to touch a ball first.
        int savedStartDraw = Preferences.Get(SlotKey(KeyStartDraw, _activeSlot), 0);
        int savedCoverDraw = Preferences.Get(SlotKey(KeyCoverDraw, _activeSlot), 0);
        int currentStartDraw = int.TryParse(_startDrawEntry.Text?.Trim(), out int sd) && sd > 0 ? sd : 0;
        int currentCoverDraw = int.TryParse(_searchDrawEntry.Text?.Trim(), out int cd) && cd > 0 ? cd : 0;
        bool startOrSearchChanged = currentStartDraw != savedStartDraw || currentCoverDraw != savedCoverDraw;

        if (_viewingOnly)
            // What-if (test) mode never arms Save, even on an already-saved slot that gets
            // unlocked/edited while testing — SaveTicketAsync/PersistCurrentSlotRaw already
            // no-op while _viewingOnly is true (see ToggleViewingOnly's comment), so an armed
            // button here would just be a lie about what tapping it could do. User's explicit
            // ask: while testing "what if" values on a saved ticket, Save must stay off so
            // there's no chance of it looking tappable/live before What-if mode is turned off
            // (which already discards the test values and restores the real saved ticket).
            enabled = false;
        else if (!complete)
            enabled = false;
        else if (!alreadyPurchased)
            enabled = true; // brand-new ticket, nothing saved yet to compare against
        else
            // Already-saved ticket: Save arms the moment it's unlocked (tapping any of your
            // own picks, or changing Draws/Spots/Wager/Bullseye, pops "Change Ticket?" and sets
            // _editUnlocked) or Start#/Search# changed (those bypass the lock entirely). It does
            // NOT require landing on a numbers/spots/wager/draws/bullseye combo that actually
            // differs from what's saved — re-tapping the same old ball twice (off, then back on)
            // is enough. User's explicit ask: you shouldn't have to pick a genuinely different
            // ball just to re-arm Save.
            enabled = _editUnlocked || startOrSearchChanged;

        _btnSave.IsEnabled = enabled;
        // IsEnabled alone doesn't visibly change this button — it uses an explicit Background
        // brush (Make3DButtonBrush) instead of a plain BackgroundColor, which bypasses the
        // platform's automatic disabled-state dimming. Confirmed live: the button was already
        // correctly un-tappable while "disabled" but still looked 100% identical to enabled,
        // which read as the feature not working at all. Opacity is brush-agnostic on every
        // platform, so it's what actually makes the state visible.
        _btnSave.Opacity = enabled ? 1.0 : 0.4;
    }

    View BuildButtonRow()
    {
        // Horizontal padding trimmed to 0 (was 2) on these three only — makes room in the row
        // for a 5th button (Delete Ticket) without anything wrapping/truncating.
        var btnQuickPick = new Button { Text = "Quick", Background = Make3DButtonBrush(Color.FromArgb("#4B5563")), TextColor = Colors.White, CornerRadius = 8, FontSize = 11, Padding = new Thickness(0, 1), HeightRequest = 32, MinimumHeightRequest = 0 };
        btnQuickPick.Clicked += (_, _) => QuickPick();

        var btnClear = new Button { Text = "Clear", Background = Make3DButtonBrush(Color.FromArgb("#4B5563")), TextColor = Colors.White, CornerRadius = 8, FontSize = 11, Padding = new Thickness(0, 1), HeightRequest = 32, MinimumHeightRequest = 0 };
        btnClear.Clicked += async (_, _) => { if (await ConfirmEditIfLockedAsync()) { _slotPendingWins.Remove(_activeSlot); ClearSelection(); } };

        _btnSave = new Button { Text = "Save", Background = Make3DButtonBrush(Color.FromArgb("#2563EB")), TextColor = Colors.White, CornerRadius = 8, FontSize = 11, FontAttributes = FontAttributes.Bold, Padding = new Thickness(0, 1), HeightRequest = 32, MinimumHeightRequest = 0, IsEnabled = false, Opacity = 0.4 };
        _btnSave.Clicked += async (_, _) => await SaveTicketAsync();

        // "Check" (single-draw check) is hidden for now — not deleted, may come back later.
        var btnCheck = new Button { Text = "Check", BackgroundColor = Color.FromArgb("#2E7D32"), TextColor = Colors.White, CornerRadius = 8, FontSize = 11, FontAttributes = FontAttributes.Bold, IsVisible = false };
        btnCheck.Clicked += async (_, _) => await CheckAgainstLatestDrawAsync();

        // Shortened from "Check Range" to one word — at 5 columns the two-word label was
        // wrapping onto a second line that the button's fixed height then clipped away
        // entirely (rendered as if the button just said "Check"), not a padding/font issue.
        _btnCheckRange = new Button { Text = "Range", Background = Make3DButtonBrush(Color.FromArgb("#00695C")), TextColor = Colors.White, CornerRadius = 8, FontSize = 11, FontAttributes = FontAttributes.Bold, Padding = new Thickness(0, 1), HeightRequest = 32, MinimumHeightRequest = 0 };
        _btnCheckRange.Clicked += async (_, _) => await CheckRangeAsync();

        _btnRecordWin = new Button
        {
            Text = "Record Win", BackgroundColor = Color.FromArgb("#D4A94A"), TextColor = Color.FromArgb("#12181F"),
            CornerRadius = 10, FontSize = 13, FontAttributes = FontAttributes.Bold, IsVisible = false,
        };
        _btnRecordWin.Clicked += async (_, _) => await RecordPendingWinAsync();

        // User's explicit ask 2026-08-14, after a re-staged-but-already-recorded win prompt came
        // back with no way to dismiss it short of tapping Record again (which would've correctly
        // no-op'd, but with zero indication that's all it would do). Never touches
        // winnings_log.json or the Reviewed flag, so a win that's genuinely still unrecorded can
        // still surface again later (e.g. via the background checker) rather than being silently
        // suppressed forever by a stray tap.
        _btnDismissRecordWin = new Button
        {
            Text = "✕", BackgroundColor = Color.FromArgb("#4B5563"), TextColor = Colors.White,
            CornerRadius = 10, FontSize = 13, FontAttributes = FontAttributes.Bold, IsVisible = false,
            WidthRequest = 44, MinimumWidthRequest = 44, Padding = new Thickness(0),
        };
        _btnDismissRecordWin.Clicked += (_, _) =>
        {
            // Used to only clear the ACTIVE slot's entry — but the banner's own count/total is
            // an ALL-slots aggregate (see _slotPendingWins), so if the pending win on screen
            // actually belonged to a different ticket, the active slot's entry was already empty
            // and clearing it changed nothing. That read as the X doing nothing at all. Confirmed
            // live 2026-08-15. Clears every slot's staged win now, matching what the banner shows.
            _pendingWins.Clear();
            _slotPendingWins.Clear();
            RefreshRecordWinButton();
        };

        // Single ⚙️ Options button (2026-08-11) replaces the standalone 🎨 Color and 🗑 Delete
        // buttons that used to live here, plus the 💰 Payout icon that used to live in the
        // header — consolidating all four (+ Auto Refresh) into one menu frees up real space on
        // the main page. Icon-only, same narrow treatment the old Color button had.
        _btnOptions = new Button { Text = "⚙️", TextColor = Colors.White, CornerRadius = 8, FontSize = 15, Padding = new Thickness(0, 1), HeightRequest = 32, MinimumHeightRequest = 0, WidthRequest = 40 };
        _btnOptions.Clicked += (_, _) => ShowOptionsMenu();
        RefreshAnimationModeIndicator();

        var row1 = new Grid
        {
            Padding = new Thickness(10, 3, 10, 0),
            ColumnSpacing = 5,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Children = { btnQuickPick, btnClear, _btnSave, _btnCheckRange, _btnOptions },
        }.Also(g =>
        {
            Grid.SetColumn(btnQuickPick, 0); Grid.SetColumn(btnClear, 1);
            Grid.SetColumn(_btnSave, 2); Grid.SetColumn(_btnCheckRange, 3);
            Grid.SetColumn(_btnOptions, 4);
        });

        var recordRow = new Grid
        {
            Padding = new Thickness(10, 6),
            ColumnSpacing = 6,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { _btnRecordWin, _btnDismissRecordWin },
        }.Also(g =>
        {
            Grid.SetColumn(_btnRecordWin, 0);
            Grid.SetColumn(_btnDismissRecordWin, 1);
        });

        return new VerticalStackLayout
        {
            Children = { row1, recordRow }
        };
    }

    // Purple used for the "What if -? (test)" row's "?" glyph — a real Label/TextColor now
    // (see BuildOptionsOverlay), not an emoji, so it actually renders purple instead of
    // whatever color a fixed emoji glyph happens to be.
    static readonly Color OptionsWhatIfIconColor = Color.FromArgb("#BA68C8");

    // Single entry point for the four controls that used to be separate on-page buttons/icons
    // (🎨 Color, ♻️ Auto Refresh, 🗑 Delete, 💰 Payout) — each row below just calls the exact
    // same handler its old standalone control used to call directly. Opens the custom overlay
    // built in BuildOptionsOverlay (a native DisplayActionSheet can't color individual rows at
    // all, which is why the "What if" row's icon could never actually be made purple there).
    void ShowOptionsMenu()
    {
        RefreshOptionsMenuRows();
        _optionsOverlay.IsVisible = true;
    }

    // Rebuilt every time the menu opens (not just once) so rows whose label/icon depends on
    // live state — "What if -? (test): On/Off", "Hot Spot Only: On/Off" — always reflect
    // whatever is currently true, the same reason RebuildRangeSearchSpotEntries re-runs on
    // every open instead of building once at startup.
    void RefreshOptionsMenuRows()
    {
        _optionsRowsContainer.Children.Clear();

        _optionsRowsContainer.Children.Add(BuildOptionsRow("🎨", Colors.White, "Color",
            () => { ShowBallColorsOverlay(); return Task.CompletedTask; }));
        // "♻️ Auto Refresh" row removed from the menu per the user's 2026-08-20 ask — the
        // interval (8 sec), "Next Draw", and "Refreshing" label it used to control are now
        // hard-coded always-on (see _autoRefreshMinutes/_calCountdownEnabled/_showRefreshingLabel
        // field comments). ShowAutoRefreshMenuAsync itself is left intact, just unreachable,
        // in case a way to change these is wanted again later.
        _optionsRowsContainer.Children.Add(BuildOptionsRow("🗑️", Colors.White, "Delete",
            DeleteTicketAsync));
        _optionsRowsContainer.Children.Add(BuildOptionsRow("💰", Colors.White, "Payout",
            () => { _payoutOverlay.IsVisible = true; return Task.CompletedTask; }));
        _optionsRowsContainer.Children.Add(BuildOptionsRow("?", OptionsWhatIfIconColor,
            // Shortened wording — user's ask 2026-08-18: the original "What if -? (test): On —
            // tap to turn off" wrapped to two lines in this popup's row; this fits on one.
            _viewingOnly ? "What-if (test): On — tap turns off" : "What-if (test): Off — tap turns on",
            () => { ToggleViewingOnly(); return Task.CompletedTask; }));
        bool matchedAllEnabled = Preferences.Get(KeyMatchedAllNumbersEnabled, false);
        _optionsRowsContainer.Children.Add(BuildOptionsRow("🏆", Colors.White,
            matchedAllEnabled ? "Matched all Numbers: On — tap to turn off" : "Matched all Numbers: Off — tap to turn on",
            () =>
            {
                Preferences.Set(KeyMatchedAllNumbersEnabled, !matchedAllEnabled);
                RefreshOptionsMenuRows(); // repaint this row's own On/Off text immediately
                return Task.CompletedTask;
            }));
        _optionsRowsContainer.Children.Add(BuildOptionsRow("📆", Colors.White, "Past 10 Days…",
            () =>
            {
                RefreshPast10DaysDialogState();
                PrefillPast10DaysRange();
                _past10DaysOverlay.IsVisible = true;
                return Task.CompletedTask;
            }));
        _optionsRowsContainer.Children.Add(BuildOptionsRow("🔢", Colors.White, "Last Draws + Analyze Tickets…",
            OpenLast200DrawsAsync));
        _optionsRowsContainer.Children.Add(BuildOptionsRow("⭐", Colors.White, "Favorites…",
            () => { _showFavoritesOverlay(); return Task.CompletedTask; }));
        // "Preview Fly In" (testing-only row added 2026-08-16) hidden from the menu per user's
        // ask 2026-08-20 — PreviewFlyInAsync itself is left intact in case it's wanted again,
        // just no longer reachable from here.
        bool animationModeEnabled = Preferences.Get(KeyAnimationModeEnabled, true);
        _optionsRowsContainer.Children.Add(BuildOptionsRow("🎞️", Colors.White,
            animationModeEnabled ? "Animation Mode: On — tap to turn off" : "Animation Mode: Off — tap to turn on",
            () =>
            {
                Preferences.Set(KeyAnimationModeEnabled, !animationModeEnabled);
                RefreshOptionsMenuRows(); // repaint this row's own On/Off text immediately
                RefreshAnimationModeIndicator();
                return Task.CompletedTask;
            }));
        // Same toggle as MainPage's Options menu (see MainPage.xaml.cs BtnOptions_Clicked) —
        // repeated here so a user already living on this page in Hot Spot Only mode can turn
        // it back off without first hunting for the ⌂ Home icon.
        bool hotSpotOnly = Preferences.Get(MainPage.HotSpotOnlyModeKey, false);
        _optionsRowsContainer.Children.Add(BuildOptionsRow("🎯", Colors.White,
            hotSpotOnly ? "Hot Spot Only: On — tap to turn off" : "Hot Spot Only: Off — tap to turn on",
            async () =>
            {
                bool newVal = !Preferences.Get(MainPage.HotSpotOnlyModeKey, false);
                Preferences.Set(MainPage.HotSpotOnlyModeKey, newVal);
                await DisplayAlert("Hot Spot Only",
                    newVal
                        ? "Hot Spot Only is now ON.\nNext app launch will jump straight to the Hot Spot page — the arrow back button there will close the app instead of returning Home."
                        : "Hot Spot Only is now OFF.\nNext app launch will show the regular Home screen again.",
                    "OK");
            }));
    }

    // One tappable row: an icon/glyph cell + a text cell. `iconColor` is only meaningful for
    // plain-text glyphs like "?" — color emoji (🎨♻️🗑️💰📆🔢▶️🎯) ignore TextColor entirely and
    // always render in their own fixed colors, same as they did in the old action sheet.
    View BuildOptionsRow(string icon, Color iconColor, string text, Func<Task> onTap)
    {
        var iconLabel = new Label
        {
            Text = icon, FontSize = 18, FontAttributes = FontAttributes.Bold,
            TextColor = iconColor, WidthRequest = 30,
            HorizontalTextAlignment = TextAlignment.Center, VerticalOptions = LayoutOptions.Center,
        };
        var textLabel = new Label
        {
            Text = text, FontSize = 14, TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.WordWrap,
        };
        var row = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10,
            Padding = new Thickness(4, 13),
        };
        row.Children.Add(iconLabel);
        Grid.SetColumn(textLabel, 1);
        row.Children.Add(textLabel);
        row.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                _optionsOverlay.IsVisible = false;
                await onTap();
            })
        });
        return row;
    }

    // Custom ⚙️ Options popup — same dark-card overlay style as BuildPayoutOverlay/
    // BuildPast10DaysOverlay, chosen specifically because (unlike DisplayActionSheet) it lets
    // each row be a real, individually-colorable Label. Rows themselves are (re)built by
    // RefreshOptionsMenuRows every time the menu opens.
    Grid BuildOptionsOverlay()
    {
        _optionsRowsContainer = new VerticalStackLayout { Spacing = 2 };

        var cancelLabel = new Label
        {
            Text = "Cancel", FontSize = 14, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#FFD54F"),
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(0, 12, 0, 0),
        };
        cancelLabel.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => _optionsOverlay.IsVisible = false)
        });

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(18, 14),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 320,
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label
                    {
                        Text = "Options", FontSize = 16, FontAttributes = FontAttributes.Bold,
                        TextColor = Colors.White,
                    },
                    // No ScrollView here on purpose — user's explicit ask 2026-08-20 ("i dont
                    // want to scroll... can you make the dialog height long so i can see
                    // everything at once"). The container just takes its natural full height
                    // now, so the card grows to fit every row instead of capping/scrolling.
                    _optionsRowsContainer,
                    cancelLabel,
                }
            }
        };

        _optionsOverlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false,
            Children = { card },
        };
        return _optionsOverlay;
    }

    // Replays HotSpotFlyIn against whatever numbers are ALREADY showing as Drawn/Match/
    // Bullseye/BullseyeHit on the board right now (read straight from _ballCurrentState, the
    // same ground truth SetBallState maintains) — doesn't fetch anything, doesn't touch
    // _flyInPlayedForDrawNumber, and never runs the win-text/staging logic in ApplyDrawResult.
    // Purely a "watch it again" preview for tuning direction/speed/scale without waiting for
    // an actual new live draw. Shares _flyInBusy with the real reveal path (see that field's
    // comment) so a Preview tap can never collide with a genuine live reveal firing at the
    // same time, in either direction.
    async Task PreviewFlyInAsync()
    {
        if (_flyInBusy) return;

        var landingOrder = _ballCurrentState
            .Where(kv => kv.Value is BallState.Drawn or BallState.Match)
            .Select(kv => kv.Key)
            .Concat(_ballCurrentState.Where(kv => kv.Value is BallState.Bullseye or BallState.BullseyeHit).Select(kv => kv.Key))
            .ToList();
        if (landingOrder.Count == 0)
        {
            await DisplayAlert("Preview Fly In", "No draw is showing on the board yet — Check a ticket first, then preview.", "OK");
            return;
        }

        _flyInBusy = true;
        try
        {
            // Same "clear first" step the real reveal does (see ShowDrawResultOnGridAsync) —
            // capture each ball's real target color before wiping it, then clear every one of
            // them straight back to blank/Selected so nothing shows its answer before its own
            // flight starts. Confirmed live 2026-08-16: without this, Preview just replayed the
            // flight motion over balls that were ALREADY sitting there fully colored the whole
            // time, same bug as the real reveal originally had.
            var targetState = landingOrder.ToDictionary(n => n, n => _ballCurrentState[n]);
            var previewBallViews = landingOrder.ToDictionary(n => n, n => _ballViews[n]);
            await HotSpotFlyIn.DropOutAsync(_ballGrid, previewBallViews,
                n => BuildFlyInGhost(n, targetState[n]),
                n => SetBallState(n, _selected.Contains(n) ? BallState.Selected : BallState.Default));

            await HotSpotFlyIn.PlayAsync(landingOrder, _ballGrid, _ballViews,
                n => BuildFlyInGhost(n, targetState[n]), n => SetBallState(n, targetState[n]));
        }
        finally { _flyInBusy = false; }
    }

    // Main Hot Spot page header color — green while Edit Mode is on (an already-saved ticket is
    // actively unlocked for editing, see UpdateEditMode), else purple whenever either Viewing-
    // Only/"What if -? (test)" mode or the Last Draws dialog's What-if is on, normal otherwise.
    // Previously Viewing-Only alone showed a separate red, which read as a different/lesser
    // state than What-if's purple even though both are the same "test mode" concept — collapsed
    // to one color. Called from ToggleViewingOnly (viewing-only flips), RefreshAnalyzeModeToggleUi
    // (What-if flips inside the Last Draws dialog), and UpdateEditMode (Edit Mode flips), since
    // any one of those alone can change what color this should be.
    void RefreshMainHeaderColor()
    {
        _headerGrid.BackgroundColor = Edit
            ? HeaderEditColor
            : (_viewingOnly || _last200WhatIfSelected)
                ? HeaderWhatIfColor
                : HeaderNormalColor;
    }

    // Recomputes Edit Mode for the active slot from _editUnlocked — true only when there's
    // actually an already-saved ticket underneath the unlock. A first-time pick on a brand-new,
    // never-saved slot also sets _editUnlocked (see the Draws/Spots/Bullseye/Wager handlers and
    // QuickPick's replace-all-slots path), but that's not "editing a saved ticket," so it must
    // never show Edit Mode. Greens the header and posts a status line the moment editing starts;
    // both revert the instant it's saved (SaveTicketAsync) or the slot changes (LoadSlot).
    void UpdateEditMode()
    {
        bool hasSavedTicket = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));
        bool editing = _editUnlocked && hasSavedTicket;
        if (editing == Edit) return;
        Edit = editing;
        RefreshMainHeaderColor();
        if (Edit)
        {
            _statusLabel.Text = $"Ticket {_activeSlot + 1} is in Edit Mode — you must tap Save to make changes.";
            _statusLabel.TextColor = HeaderEditColor;
            _statusLabel.IsVisible = true;
        }
    }

    void ToggleViewingOnly()
    {
        _viewingOnly = !_viewingOnly;
        // Turning What if -? (test) mode off must also drop the "What-if" pill back to "All
        // Tickets" — it can't stay selected on an option that's about to go locked-out/dimmed.
        if (!_viewingOnly) _last200WhatIfSelected = false;
        RefreshAnalyzeModeToggleUi();
        RefreshMainHeaderColor();

        // Turning viewing mode back OFF: whatever picks/start-draw are sitting in memory right
        // now came from viewing mode (PersistCurrentSlotRaw/SaveTicketAsync both no-op while
        // _viewingOnly was true, so none of it ever touched this slot's real Preferences).
        // Re-loading the active slot from Preferences discards those in-memory test picks and
        // restores exactly whatever is really saved for this slot — so a test entry can never
        // ride along into a real Save or a real slot-switch persist after this point.
        if (!_viewingOnly) LoadSlot(_activeSlot);
        else UpdateSaveButtonState(); // force Save off immediately if it was armed from a real edit right before entering What-if mode

        _statusLabel.TextColor = Color.FromArgb("#4CAF7D");
        _statusLabel.Text = _viewingOnly
            ? "Viewing only (test) is ON — checks still show wins, but nothing is saved or recorded."
            : "Viewing only (test) is OFF — test picks cleared, back to your real ticket.";
        _statusLabel.IsVisible = true;
        _pendingWins.Clear();
        _slotPendingWins.Remove(_activeSlot);
        RefreshRecordWinButton();
    }

    // Only 8 sec kept — every slower option (10 sec through 30 min) was too slow to be useful
    // and got removed at the user's request. 8 sec (7.5 req/min) is well under the documented
    // anti-bot risk threshold flagged in an earlier session (~100+ rapid requests/session, or
    // the 3 sec/20-req/min pace that was explicitly declined).
    static readonly double[] AutoRefreshMinuteOptions = { 8.0 / 60.0 };

    // 0.5 min reads as "30 sec" in the picker; everything else stays "N min".
    static string FormatAutoRefreshInterval(double minutes) =>
        minutes < 1 ? $"Every {(int)Math.Round(minutes * 60)} sec" : $"Every {minutes:0.#} min";

    // Lets the user pick how often the header's "Current draw #" re-fetches itself in the
    // background, instead of only ever updating via an explicit tap. Deliberately drives
    // ShowApproxCurrentDrawAsync only — NOT the full ball-grid/results checking that was
    // removed from OnPollTick earlier (see that method's comment) — so this can't reproduce
    // the "results visibly jump between draws" problem that got the old automatic checker
    // turned off; this only ever touches the small informational draw-number label.
    async Task ShowAutoRefreshMenuAsync()
    {
        string offOpt = _autoRefreshMinutes == 0 ? "Off (current)" : "Off";
        var minuteOpts = AutoRefreshMinuteOptions
            .Select(m => m == _autoRefreshMinutes ? $"{FormatAutoRefreshInterval(m)} (current)" : FormatAutoRefreshInterval(m))
            .ToArray();
        string calOpt = _calCountdownEnabled ? "📊 (approx.) Next Draw: On — tap to turn off" : "📊 (approx.) Next Draw: Off — tap to turn on";
        // Testing-only toggle for the "Refreshing: ..." label — see _showRefreshingLabel comment.
        // Remove this menu item (and the field/preference) once testing is done and it's no
        // longer needed, per the user's own "not yet" — don't remove it before then.
        string showRefreshOpt = _showRefreshingLabel ? "🔧 \"Refreshing:\" label: On — tap to hide" : "🔧 \"Refreshing:\" label: Off — tap to show";

        string action = await DisplayActionSheet("Auto Refresh — Current Draw #", "Cancel", null,
            new[] { offOpt }.Concat(minuteOpts).Append(calOpt).Append(showRefreshOpt).ToArray());

        if (string.IsNullOrEmpty(action) || action == "Cancel") return;

        if (action == showRefreshOpt)
        {
            _showRefreshingLabel = !_showRefreshingLabel;
            Preferences.Set(KeyShowRefreshingLabel, _showRefreshingLabel);
            _autoRefreshCountdownLabel.IsVisible = _showRefreshingLabel && _autoRefreshMinutes > 0;
            _refreshChevronsBorder.IsVisible = _showRefreshingLabel && _autoRefreshMinutes > 0;
            _fullScreenChevronsBorder.IsVisible = _showRefreshingLabel && _autoRefreshMinutes > 0;
            _statusLabel.TextColor = Color.FromArgb("#4CAF7D");
            _statusLabel.Text = _showRefreshingLabel ? "\"Refreshing:\" label shown" : "\"Refreshing:\" label hidden";
            _statusLabel.IsVisible = true;
            return;
        }

        if (action == calOpt)
        {
            _calCountdownEnabled = !_calCountdownEnabled;
            Preferences.Set(KeyCalCountdownEnabled, _calCountdownEnabled);
            _calNextChangeAt = DateTime.MinValue;
            _calCountdownLabel.IsVisible = _calCountdownEnabled;
            _calCountdownLabel.FormattedText = null;
            _calCountdownDisplayText = "";

            // calCountDown has no polling of its own — it needs Auto Refresh actually running
            // to ever observe a draw# change. Turning it on with no interval selected would
            // just sit on "waiting…" forever, so auto-start the fastest interval (8 sec, the
            // tightest accuracy this app offers) rather than making the user pick one
            // separately. Turning calCountDown back off leaves Auto Refresh exactly as it was —
            // this only ever turns refresh ON, never off, on the user's behalf.
            bool autoStartedRefresh = false;
            if (_calCountdownEnabled && _autoRefreshMinutes <= 0)
            {
                _autoRefreshMinutes = AutoRefreshMinuteOptions[0]; // 8/60 min = 8 sec
                Preferences.Set(KeyAutoRefreshMinutes, _autoRefreshMinutes);
                StartAutoRefreshTimer();
                autoStartedRefresh = true;
            }

            _statusLabel.TextColor = Color.FromArgb("#4CAF7D");
            _statusLabel.Text = !_calCountdownEnabled
                ? "(approx.) Next Draw turned off"
                : "(approx.) Next Draw turned on" + (autoStartedRefresh ? $" (Auto Refresh set to {FormatAutoRefreshInterval(_autoRefreshMinutes).ToLowerInvariant()})" : "");
            _statusLabel.IsVisible = true;
            return;
        }

        if (action.StartsWith("Off"))
        {
            _autoRefreshMinutes = 0;
            Preferences.Set(KeyAutoRefreshMinutes, 0d);
            StopAutoRefreshTimer();
            _statusLabel.TextColor = Color.FromArgb("#8B9DC3");
            _statusLabel.Text = "Auto refresh turned off";
            _statusLabel.IsVisible = true;
            return;
        }

        double minutes = AutoRefreshMinuteOptions.First(m => action.StartsWith(FormatAutoRefreshInterval(m)));
        _autoRefreshMinutes = minutes;
        Preferences.Set(KeyAutoRefreshMinutes, minutes);
        StartAutoRefreshTimer();
        _statusLabel.TextColor = Color.FromArgb("#4CAF7D");
        _statusLabel.Text = $"Auto refresh set to {FormatAutoRefreshInterval(minutes).ToLowerInvariant()}";
        _statusLabel.IsVisible = true;
    }

    // Hot Spot draws stop 2:00 AM–6:00 AM (device-local/Pacific) — user's explicit call. Auto
    // Refresh and calCountDown both go idle for this window instead of hitting calottery.com
    // every 8 sec for a closed game; both resume on their own the moment DateTime.Now clears 6am.
    static bool InHotSpotClosedWindow()
    {
        var t = DateTime.Now.TimeOfDay;
        return t >= TimeSpan.FromHours(2) && t < TimeSpan.FromHours(6);
    }

    void StartAutoRefreshTimer()
    {
        StopAutoRefreshTimer();
        if (_autoRefreshMinutes <= 0) return;
        _autoRefreshTimer = Dispatcher.CreateTimer();
        _autoRefreshTimer.Interval = TimeSpan.FromMinutes(_autoRefreshMinutes);
        _autoRefreshTimer.Tick += async (_, _) =>
        {
            _nextAutoRefreshAt = DateTime.Now.AddMinutes(_autoRefreshMinutes); // reset for the next lap
            if (InHotSpotClosedWindow()) return; // no fetches while the game is closed
            await ShowApproxCurrentDrawAsync(forceOverwrite: true);
            await CheckAutoRefreshDrawChangeAsync();
        };
        _autoRefreshTimer.Start();
        _nextAutoRefreshAt = DateTime.Now.AddMinutes(_autoRefreshMinutes);

        _autoRefreshCountdownLabel.IsVisible = _showRefreshingLabel;
        _refreshChevronsBorder.IsVisible = _showRefreshingLabel;
        _fullScreenChevronsBorder.IsVisible = _showRefreshingLabel;
    }

    // Deliberately does NOT touch _calNextChangeAt/_calCountdownLabel —
    // calCountDown's own on/off state (toggled from the Auto Refresh menu, see calOpt) is
    // independent of whether Auto Refresh itself is currently running. Stopping Auto Refresh
    // just means calCountDown stops getting fresh observations to resync on; an already-running
    // 4:00 prediction keeps counting down and stays on screen exactly where it was, it doesn't
    // reset or vanish just because refresh was turned off. Confirmed this is what's wanted —
    // an earlier version reset/hid it here and that was flagged as wrong live.
    void StopAutoRefreshTimer()
    {
        _autoRefreshTimer?.Stop();
        _autoRefreshTimer = null;
        _nextAutoRefreshAt = DateTime.MinValue;
        _autoRefreshCountdownLabel.IsVisible = false;
        _autoRefreshCountdownLabel.Text = "";
        _refreshChevronsBorder.IsVisible = false;
        _fullScreenChevronsBorder.IsVisible = false;
    }

    // Removes the CURRENT slot's ticket entirely and resets it back to a fresh, nothing-saved
    // state — the other 9 slots are untouched. Confirmed destructive, so it asks first.
    async Task DeleteTicketAsync()
    {
        // What-if (test) mode blocks every real write already (Save, range-analysis overwrite)
        // — Delete is the one destructive action that wasn't gated the same way. User's explicit
        // ask 2026-08-14: block it outright rather than letting a real ticket get deleted while
        // mid-test, even though Delete already has its own separate confirmation dialog.
        if (_viewingOnly)
        {
            await DisplayAlert("What if -? (test) is on", "Can't delete a ticket while What-if mode is on — turn it off in ⚙️ Options first.", "OK");
            return;
        }

        // Every saved slot can be deleted directly, without switching to it first — user's
        // explicit ask 2026-08-21: with several tickets to clear out, picking each one beats
        // making it "the current one" first. Originally listed every "Delete Ticket N" as its
        // own DisplayActionSheet row (native, but with 12 possible tickets it filled the whole
        // screen — user's follow-up ask same day) — now just a 2-line action sheet, with the
        // per-ticket choice handed off to a compact Picker (a real native dropdown, one line on
        // screen until tapped) in its own small popup — see BuildDeleteTicketOverlay/
        // ShowDeleteTicketPicker.
        var savedSlots = Enumerable.Range(0, SlotCount).Where(s => Preferences.ContainsKey(SlotKey(KeyNumbers, s))).ToList();

        // Nothing saved anywhere at all — avoid showing a scary "this can't be undone" prompt
        // for a no-op.
        if (savedSlots.Count == 0)
        {
            _statusLabel.TextColor = Color.FromArgb("#8B9DC3");
            _statusLabel.Text = "Nothing to delete — no Hot Spot tickets saved.";
            _statusLabel.IsVisible = true;
            return;
        }

        const string deleteOne = "Delete one ticket…";
        const string allOfThem = "Delete ALL Hot Spot tickets";
        string action = await DisplayActionSheet("Delete Hot Spot Ticket(s)", "Cancel", null, deleteOne, allOfThem);

        if (action == allOfThem)
        {
            bool confirmedAll = await DisplayAlert(
                "Delete ALL Hot Spot Tickets?",
                $"This removes every saved Hot Spot ticket in all {SlotCount} slots. This can't be undone.",
                "Delete All", "Cancel");
            if (confirmedAll) await DeleteAllHotSpotTicketsAsync();
            return;
        }
        if (action != deleteOne) return; // Cancel or dismissed

        ShowDeleteTicketPicker(savedSlots);
    }

    // Compact "which ticket?" popup — a Picker (native dropdown, collapsed to one line until
    // tapped) instead of listing every "Delete Ticket N" as its own row, which filled the whole
    // screen once several tickets were saved. `_deleteTicketPickerSlots` maps the Picker's
    // SelectedIndex back to the real slot #, since saved slots are rarely contiguous from 0.
    List<int> _deleteTicketPickerSlots = new();
    void ShowDeleteTicketPicker(List<int> savedSlots)
    {
        _deleteTicketPickerSlots = savedSlots;
        _deleteTicketPicker.Items.Clear();
        foreach (var s in savedSlots) _deleteTicketPicker.Items.Add($"Ticket {s + 1}");
        _deleteTicketPicker.SelectedIndex = -1;
        _deleteTicketOverlay.IsVisible = true;
    }

    Grid _deleteTicketOverlay = null!;
    Picker _deleteTicketPicker = null!;
    Grid BuildDeleteTicketOverlay()
    {
        var title = new Label
        {
            Text = "🗑 Delete Ticket", FontSize = 15, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#FFD54F"), HorizontalOptions = LayoutOptions.Center,
        };

        _deleteTicketPicker = new Picker
        {
            Title = "Select Ticket", TextColor = Colors.White, FontSize = 13,
            BackgroundColor = Color.FromArgb("#101923"),
        };

        var btnDelete = new Button
        {
            Text = "Delete", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#B71C1C"), CornerRadius = 10, Padding = new Thickness(6, 10),
        };
        btnDelete.Clicked += async (_, _) =>
        {
            if (_deleteTicketPicker.SelectedIndex < 0) return;
            int slot = _deleteTicketPickerSlots[_deleteTicketPicker.SelectedIndex];
            _deleteTicketOverlay.IsVisible = false;
            await DeleteOneTicketAsync(slot);
        };

        var btnCancel = new Button
        {
            Text = "Cancel", FontSize = 13, TextColor = Color.FromArgb("#FFD54F"),
            BackgroundColor = Colors.Transparent, HorizontalOptions = LayoutOptions.End,
            Padding = new Thickness(4, 4),
        };
        btnCancel.Clicked += (_, _) => _deleteTicketOverlay.IsVisible = false;

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(18, 14),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 300,
            Content = new VerticalStackLayout
            {
                Spacing = 10,
                Children = { title, _deleteTicketPicker, btnDelete, btnCancel },
            }
        };

        _deleteTicketOverlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false,
            Children = { card },
        };
        return _deleteTicketOverlay;
    }

    // Deletes exactly one slot's saved ticket, wherever it lives — split out from
    // DeleteTicketAsync so a non-active slot can be deleted too (see that method's own comment).
    // Only resets the on-screen entry fields (pickers/ball grid/Start#) when the deleted slot IS
    // the one currently being viewed; a different slot's own on-screen ticket must never change
    // just because some OTHER slot got deleted.
    async Task DeleteOneTicketAsync(int slot)
    {
        // Captured before the prefs below are wiped — used to find this exact ticket's row
        // in the persisted Ticket Log, which otherwise has no idea a slot was just deleted
        // (it only ever ADDS rows during a rescan, see TicketLogService.LogRowsWithDrawCountAsync).
        string purchasedRaw = Preferences.Get(SlotKey(KeyPurchasedDate, slot), "");

        Preferences.Remove(SlotKey(KeySpots, slot));
        Preferences.Remove(SlotKey(KeyBullseye, slot));
        Preferences.Remove(SlotKey(KeyWager, slot));
        Preferences.Remove(SlotKey(KeyDraws, slot));
        Preferences.Remove(SlotKey(KeyNumbers, slot));
        Preferences.Remove(SlotKey(KeyPurchasedDate, slot));
        Preferences.Remove(SlotKey(KeyStartDraw, slot));
        Preferences.Remove(SlotKey(KeyCoverDraw, slot));
        Preferences.Remove(SlotKey(KeyReviewed, slot));
        Preferences.Remove(SlotKey(KeyWinNumbers, slot));
        Preferences.Remove(SlotKey(KeyWinDrawNumber, slot));

        // One Ticket Log row per Hot Spot slot per purchase day (Row is always 0 for "HS" —
        // see TicketLogService), so Game+Slot+Date is enough to find and remove it here too,
        // instead of leaving a stale entry the user would otherwise have to go delete by hand
        // on the Ticket Log page.
        if (DateTime.TryParseExact(purchasedRaw, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var purchasedDate))
        {
            var log = await TicketLogService.LoadAllAsync();
            string logDate = purchasedDate.ToString("yyyy-MM-dd");
            if (log.RemoveAll(e => e.Game == "HS" && e.Slot == slot && e.Date == logDate) > 0)
                await TicketLogService.SavePublicAsync(log);
        }

        _slotPendingWins.Remove(slot);

        if (slot == _activeSlot)
        {
            _spots = 4; _bullseye = false; _wager = 1m; _draws = 1; _startDraw = 0;
            _spotsPicker.SelectedIndex = Array.IndexOf(SpotOptions, _spots);
            _bullseyeSwitch.IsToggled = false;
            _wagerPicker.SelectedIndex = 0;
            _drawsPicker.SelectedIndex = 0;
            _startDrawEntry.Text = "";
            _pageDrawNumber = 0;

            ClearSelection(); // resets balls, _selected, _pendingWins, Record Win button
            UpdatePrizeDisplay();
            UpdateSlotStatusLabel();
            RefreshSlotDisplayLabel(); // this slot's saved state just changed
        }

        _statusLabel.TextColor = Color.FromArgb("#4CAF7D");
        _statusLabel.Text = $"🗑 Ticket {slot + 1} deleted";
        _statusLabel.IsVisible = true;
        HotSpotMyNumbersPanel.Refresh(_myNumbersPanel);
    }

    // Wipes every one of the 10 Hot Spot slots and every matching Ticket Log row, regardless
    // of date — a genuine "start completely over" reset, not scoped to just today. Confirmed
    // via its own DisplayAlert by the caller before this runs.
    async Task DeleteAllHotSpotTicketsAsync()
    {
        for (int s = 0; s < SlotCount; s++)
        {
            Preferences.Remove(SlotKey(KeySpots, s));
            Preferences.Remove(SlotKey(KeyBullseye, s));
            Preferences.Remove(SlotKey(KeyWager, s));
            Preferences.Remove(SlotKey(KeyDraws, s));
            Preferences.Remove(SlotKey(KeyNumbers, s));
            Preferences.Remove(SlotKey(KeyPurchasedDate, s));
            Preferences.Remove(SlotKey(KeyStartDraw, s));
            Preferences.Remove(SlotKey(KeyCoverDraw, s));
            Preferences.Remove(SlotKey(KeyReviewed, s));
            Preferences.Remove(SlotKey(KeyWinNumbers, s));
            Preferences.Remove(SlotKey(KeyWinDrawNumber, s));
        }

        var log = await TicketLogService.LoadAllAsync();
        if (log.RemoveAll(e => e.Game == "HS") > 0)
            await TicketLogService.SavePublicAsync(log);

        _spots = 4; _bullseye = false; _wager = 1m; _draws = 1; _startDraw = 0;
        _spotsPicker.SelectedIndex = Array.IndexOf(SpotOptions, _spots);
        _bullseyeSwitch.IsToggled = false;
        _wagerPicker.SelectedIndex = 0;
        _drawsPicker.SelectedIndex = 0;
        _startDrawEntry.Text = "";
        _pageDrawNumber = 0;
        _activeSlot = 0;
        RefreshSlotDisplayLabel();
        Preferences.Set(KeyActiveSlot, 0);

        _slotPendingWins.Clear();
        ClearSelection();
        UpdatePrizeDisplay();
        UpdateSlotStatusLabel();
        HotSpotMyNumbersPanel.Refresh(_myNumbersPanel);

        await SpendingTracker.AutoSyncTodayAsync(); // clears today's $ total immediately, not just on next page visit

        _statusLabel.TextColor = Color.FromArgb("#4CAF7D");
        _statusLabel.Text = "🗑 All Hot Spot tickets deleted";
        _statusLabel.IsVisible = true;
    }

    Grid BuildLoadingOverlay()
    {
        _spinner = new ActivityIndicator { IsRunning = false, Color = Color.FromArgb("#90CAF9"), WidthRequest = 40, HeightRequest = 40 };
        // Was spinner-only, no text — confirmed live that the new automatic finished-ticket
        // check (CheckFinishedTicketsAsync) can sit here for a while with nothing explaining
        // why, especially checking multiple tickets' full draw ranges back to back on page
        // open. Empty by default (a plain Check/Range tap still just shows the bare spinner).
        _loadingLabel = new Label
        {
            Text = "", FontSize = 12, TextColor = Color.FromArgb("#90CAF9"),
            HorizontalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };
        _loadingOverlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false,
            Children = { new VerticalStackLayout { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children = { _spinner, _loadingLabel } } }
        };
        return _loadingOverlay;
    }

    // Payout reference popup — read-only, built once from the existing BasePrizes/
    // BullseyePrizes tables (same data CheckRangeAsync scores against), so there's no
    // second source of truth to drift out of sync. Sections default collapsed, tap the
    // "N spot" header to expand, matching the existing chevron collapse pattern used on
    // TicketLogPage's date groups.
    Grid BuildPayoutOverlay()
    {
        var body = new VerticalStackLayout { Spacing = 6 };
        for (int spots = 10; spots >= 1; spots--)
            body.Children.Add(BuildPayoutSpotSection(spots));

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(18, 14),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 340,
        };

        var btnClose = new Button
        {
            Text = "Close", FontSize = 13, TextColor = Color.FromArgb("#FFD54F"),
            BackgroundColor = Colors.Transparent, HorizontalOptions = LayoutOptions.End,
            Padding = new Thickness(4, 4),
        };
        btnClose.Clicked += (_, _) => _payoutOverlay.IsVisible = false;

        card.Content = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new Label
                {
                    Text = "Hot Spot & Bulls-eye Payouts",
                    FontSize = 15, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#FFD54F"),
                    HorizontalOptions = LayoutOptions.Center,
                    HorizontalTextAlignment = TextAlignment.Center,
                },
                new Label
                {
                    Text = "What each match count pays per $1 wager, plain vs. with Bulls-eye. Your actual win scales with your ticket's real wager.",
                    FontSize = 11, TextColor = Color.FromArgb("#8B9DC3"),
                    HorizontalTextAlignment = TextAlignment.Center,
                },
                new ScrollView { MaximumHeightRequest = 480, Content = body },
                btnClose,
            }
        };

        _payoutOverlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false,
            Children = { card },
        };
        return _payoutOverlay;
    }

    // Draw-range input for HSPast10Days — plain OK/Cancel dialogs in MAUI only support one
    // text field (DisplayPromptAsync), so this needs its own small popup, same overlay
    // pattern as the payout/color panels above. Tapping Check kicks off the actual scan as a
    // fire-and-forget background task (see RunPast10DaysCheckAsync) and closes immediately —
    // the whole point is that it never blocks the ticket you're currently working on.
    Grid BuildPast10DaysOverlay()
    {
        _p10StartEntry = new Entry
        {
            Placeholder = "e.g. 3291700", Keyboard = Keyboard.Numeric,
            BackgroundColor = Color.FromArgb("#243447"), TextColor = Colors.White,
            FontSize = 14,
        };
        _p10EndEntry = new Entry
        {
            Placeholder = "e.g. 3291820", Keyboard = Keyboard.Numeric,
            BackgroundColor = Color.FromArgb("#243447"), TextColor = Colors.White,
            FontSize = 14,
        };

        Label FieldLabel(string text) => new()
        {
            Text = text, FontSize = 11, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#D4A94A"), Margin = new Thickness(0, 0, 0, 3),
        };

        var btnCancel = new Button
        {
            Text = "Cancel", BackgroundColor = Color.FromArgb("#243447"), TextColor = Color.FromArgb("#8B9DC3"),
            CornerRadius = 8, FontSize = 13, FontAttributes = FontAttributes.Bold,
        };
        btnCancel.Clicked += (_, _) => _past10DaysOverlay.IsVisible = false;

        _p10CheckButton = new Button
        {
            Text = "Check", Background = Make3DButtonBrush(Color.FromArgb("#2E7D32")), TextColor = Colors.White,
            CornerRadius = 8, FontSize = 13, FontAttributes = FontAttributes.Bold,
        };
        _p10CheckButton.Clicked += async (_, _) => await RunPast10DaysCheckAsync();
        var btnCheck = _p10CheckButton;

        _p10RunningLabel = new Label
        {
            Text = "A check is already running — please wait for it to finish before starting another.",
            FontSize = 10, TextColor = Color.FromArgb("#E0965A"), Margin = new Thickness(0, 6, 0, 0),
            IsVisible = false,
        };

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1A2230"),
            Stroke = new SolidColorBrush(Color.FromArgb("#2D3E55")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(18, 16),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 300,
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label { Text = "Check Past 10 Days", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                    new Label
                    {
                        Text = "Scans every Hot Spot ticket bought in the last 10 days against the draws below, and lists only the ones that won. Runs in the background — keep using the app.",
                        FontSize = 11, TextColor = Color.FromArgb("#8B9DC3"), Margin = new Thickness(0, 2, 0, 12),
                    },
                    FieldLabel("Start Draw #"),
                    _p10StartEntry,
                    new BoxView { HeightRequest = 10, Color = Colors.Transparent },
                    FieldLabel("End Draw #"),
                    _p10EndEntry,
                    new Label
                    {
                        Text = "You'll get a text when it's done.", FontSize = 10, TextColor = Color.FromArgb("#6B7A93"),
                        Margin = new Thickness(0, 4, 0, 0),
                    },
                    _p10RunningLabel,
                    new Grid
                    {
                        Margin = new Thickness(0, 14, 0, 0), ColumnSpacing = 10,
                        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
                        Children = { btnCancel, btnCheck },
                    },
                }
            }
        };
        Grid.SetColumn(btnCancel, 0);
        Grid.SetColumn(btnCheck, 1);

        _past10DaysOverlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false,
            Children = { card },
        };
        return _past10DaysOverlay;
    }

    // "Search" popup — user's explicit ask 2026-08-14: a Start Draw #/Covers Draw # pair, styled
    // like the ticket's own, but living in its own dialog on top of "Last Draws + Analyze
    // Tickets" rather than reusing that dialog's "My Ticket" mode fields (see the field comment
    // on _last200SearchButton for why those weren't safe to reuse here). Second box blank means
    // "just this one draw"; both filled means a range. Results replace whatever's currently in
    // the SAME list the Last 200/My Ticket tabs already populate.
    Grid BuildRangeSearchOverlay()
    {
        _rangeSearchStartEntry = new Entry
        {
            Placeholder = "e.g. 3292071", Keyboard = Keyboard.Numeric,
            BackgroundColor = Color.FromArgb("#243447"), TextColor = Colors.White,
            FontSize = 14,
        };
        _rangeSearchEndEntry = new Entry
        {
            Placeholder = "e.g. 3292090", Keyboard = Keyboard.Numeric,
            BackgroundColor = Color.FromArgb("#243447"), TextColor = Colors.White,
            FontSize = 14,
        };

        Label FieldLabel(string text) => new()
        {
            Text = text, FontSize = 11, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#D4A94A"), Margin = new Thickness(0, 0, 0, 3),
        };

        _rangeSearchEndBlock = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                new BoxView { HeightRequest = 10, Color = Colors.Transparent },
                FieldLabel("Covers Draw #"),
                _rangeSearchEndEntry,
            }
        };

        // "Use this Data" + "How many Spots" — see the field comments on
        // _rangeSearchUseCustomDataCheck for what this does vs. the default (active ticket) search.
        _rangeSearchUseCustomDataCheck = new CheckBox { Color = Color.FromArgb("#6D28D9") };
        var useCustomDataLabel = new Label
        {
            Text = "Use this Data", FontSize = 13, TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
        };
        var useCustomDataRow = new HorizontalStackLayout
        {
            Spacing = 4, Margin = new Thickness(0, 12, 0, 0),
            Children = { _rangeSearchUseCustomDataCheck, useCustomDataLabel },
        };

        _rangeSearchSpotsPicker = new Picker
        {
            Title = "How many Spots", TitleColor = Color.FromArgb("#8B9DC3"),
            TextColor = Colors.White, BackgroundColor = Color.FromArgb("#243447"), FontSize = 13,
            ItemsSource = Enumerable.Range(1, 10).Select(n => n.ToString()).ToList(),
        };

        _rangeSearchSpotEntriesRow = new HorizontalStackLayout { Spacing = 4 };
        _rangeSearchSpotsPicker.SelectedIndexChanged += (_, _) => RebuildRangeSearchSpotEntries();

        _rangeSearchSpotsBlock = new VerticalStackLayout
        {
            IsVisible = false, Spacing = 4, Margin = new Thickness(0, 8, 0, 0),
            Children =
            {
                FieldLabel("How many Spots"),
                _rangeSearchSpotsPicker,
                new ScrollView
                {
                    Orientation = ScrollOrientation.Horizontal, Margin = new Thickness(0, 8, 0, 0),
                    Content = _rangeSearchSpotEntriesRow,
                },
            }
        };
        _rangeSearchUseCustomDataCheck.CheckedChanged += (_, e) => _rangeSearchSpotsBlock.IsVisible = e.Value;

        // Single #/Range of # toggle — same visual pattern as the Last 200/My Ticket tabs.
        _rangeSearchSingleBtn = new Button { Text = "Single #", FontSize = 11, CornerRadius = 6, Padding = new Thickness(0, 6), TextColor = Colors.White };
        _rangeSearchRangeBtn = new Button { Text = "Range of #", FontSize = 11, CornerRadius = 6, Padding = new Thickness(0, 6), TextColor = Colors.White };
        void SetRangeSearchMode(bool single)
        {
            _rangeSearchModeSingle = single;
            _rangeSearchSingleBtn.BackgroundColor = single ? Color.FromArgb("#1565C0") : Color.FromArgb("#243447");
            _rangeSearchRangeBtn.BackgroundColor = single ? Color.FromArgb("#243447") : Color.FromArgb("#1565C0");
            _rangeSearchEndBlock.IsVisible = !single;
        }
        _rangeSearchSingleBtn.Clicked += (_, _) => SetRangeSearchMode(true);
        _rangeSearchRangeBtn.Clicked += (_, _) => SetRangeSearchMode(false);
        var rangeSearchModeRow = new Grid
        {
            ColumnSpacing = 6, Margin = new Thickness(0, 0, 0, 10),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            Children = { _rangeSearchSingleBtn, _rangeSearchRangeBtn },
        };
        Grid.SetColumn(_rangeSearchSingleBtn, 0);
        Grid.SetColumn(_rangeSearchRangeBtn, 1);

        var btnCancel = new Button
        {
            Text = "Cancel", BackgroundColor = Color.FromArgb("#243447"), TextColor = Color.FromArgb("#8B9DC3"),
            CornerRadius = 8, FontSize = 13, FontAttributes = FontAttributes.Bold,
        };
        btnCancel.Clicked += (_, _) => _rangeSearchOverlay.IsVisible = false;

        var btnGo = new Button
        {
            Text = "Search", Background = Make3DButtonBrush(Color.FromArgb("#6D28D9")), TextColor = Colors.White,
            CornerRadius = 8, FontSize = 13, FontAttributes = FontAttributes.Bold,
        };
        btnGo.Clicked += async (_, _) =>
        {
            if (!int.TryParse(_rangeSearchStartEntry.Text?.Trim(), out int startDraw) || startDraw <= 0)
            {
                await DisplayAlert("Search", "Enter a valid Start Draw # first.", "OK");
                return;
            }
            int endDraw = startDraw; // Single # mode — Covers Draw # is hidden/ignored
            if (!_rangeSearchModeSingle)
            {
                if (!int.TryParse(_rangeSearchEndEntry.Text?.Trim(), out endDraw) || endDraw <= 0)
                {
                    await DisplayAlert("Search", "Enter a valid Covers Draw # for the range.", "OK");
                    return;
                }
            }

            HashSet<int>? customPicks = null;
            if (_rangeSearchUseCustomDataCheck.IsChecked)
            {
                if (_rangeSearchSpotsPicker.SelectedIndex < 0)
                {
                    await DisplayAlert("Search", "Choose \"How many Spots\" first.", "OK");
                    return;
                }
                customPicks = new HashSet<int>();
                foreach (var entry in _rangeSearchSpotEntries)
                {
                    if (!int.TryParse(entry.Text?.Trim(), out int n) || n is < 1 or > 80)
                    {
                        await DisplayAlert("Search", $"Enter a valid number (1-80) in all {_rangeSearchSpotEntries.Count} spot boxes.", "OK");
                        return;
                    }
                    customPicks.Add(n);
                }
            }

            _rangeSearchOverlay.IsVisible = false;
            await RunRangeSearchAsync(startDraw, endDraw, customPicks);
        };

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1A2230"),
            Stroke = new SolidColorBrush(Color.FromArgb("#2D3E55")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(18, 16),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 340, // wider than the 300 other popups use — needs to fit up to 10 spot entry boxes in a row
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new Label { Text = "Search Draws", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                    new Label
                    {
                        Text = "Looks up any draw(s) by number and shows them in the list below — independent of whichever ticket is active.",
                        FontSize = 11, TextColor = Color.FromArgb("#8B9DC3"), Margin = new Thickness(0, 2, 0, 12),
                    },
                    rangeSearchModeRow,
                    FieldLabel("Start Draw #"),
                    _rangeSearchStartEntry,
                    _rangeSearchEndBlock,
                    useCustomDataRow,
                    _rangeSearchSpotsBlock,
                    new Grid
                    {
                        Margin = new Thickness(0, 14, 0, 0), ColumnSpacing = 10,
                        ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
                        Children = { btnCancel, btnGo },
                    },
                }
            }
        };
        Grid.SetColumn(btnCancel, 0);
        Grid.SetColumn(btnGo, 1);
        SetRangeSearchMode(true); // default to Single # every time the dialog is (re)built

        _rangeSearchOverlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false,
            Children = { card },
        };
        return _rangeSearchOverlay;
    }

    // Rebuilds the row of small numeric Entry boxes under "How many Spots" to match whatever
    // count is currently selected (1-10) — called on every Picker selection change, and on
    // dialog reopen (via the reset in _last200SearchButton's Clicked) to clear out stale boxes/
    // values from a previous search.
    void RebuildRangeSearchSpotEntries()
    {
        _rangeSearchSpotEntriesRow.Children.Clear();
        _rangeSearchSpotEntries.Clear();
        if (_rangeSearchSpotsPicker.SelectedIndex < 0) return;

        int count = _rangeSearchSpotsPicker.SelectedIndex + 1;
        for (int i = 0; i < count; i++)
        {
            var entry = new Entry
            {
                Placeholder = "#", Keyboard = Keyboard.Numeric, FontSize = 12,
                WidthRequest = 28, HeightRequest = 36, Margin = 0,
                BackgroundColor = Color.FromArgb("#243447"), TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
                MaxLength = 2, // spot numbers run 1-80
            };
            _rangeSearchSpotEntries.Add(entry);
            _rangeSearchSpotEntriesRow.Children.Add(entry);
        }

        // Auto-advance to the next box once a 2-digit spot number is typed, and jump back to
        // the previous box on backspace (detected as a non-empty-to-empty text change, since
        // MAUI's Entry has no cross-platform raw key-down event) — so the user can punch in
        // all N spots, or correct one, without tapping between boxes.
        for (int i = 0; i < _rangeSearchSpotEntries.Count; i++)
        {
            int prevIndex = i - 1;
            int nextIndex = i + 1;
            _rangeSearchSpotEntries[i].TextChanged += (_, e) =>
            {
                if (e.NewTextValue?.Length >= 2 && nextIndex < _rangeSearchSpotEntries.Count)
                    _rangeSearchSpotEntries[nextIndex].Focus();
                else if (string.IsNullOrEmpty(e.NewTextValue) && !string.IsNullOrEmpty(e.OldTextValue) && prevIndex >= 0)
                    _rangeSearchSpotEntries[prevIndex].Focus();
            };
        }
    }

    // Re-synced every time the dialog is about to be shown (not just once) so reopening it
    // while an earlier check is still going always reflects the real current state, and
    // reopening it after that check finishes always re-enables the button — no separate
    // completion callback needed from HSPast10Days back into this page.
    void RefreshPast10DaysDialogState()
    {
        bool running = HSPast10Days.IsRunning;
        int usedToday = HSPast10Days.RunsUsedToday();
        bool dailyCapped = usedToday >= HSPast10Days.MaxRunsPerDay;
        bool disabled = running || dailyCapped;

        _p10CheckButton.IsEnabled = !disabled;
        _p10CheckButton.Opacity = disabled ? 0.4 : 1.0;
        _p10RunningLabel.IsVisible = disabled;
        _p10RunningLabel.Text = running
            ? "A check is already running — please wait for it to finish before starting another."
            : $"You've used {usedToday}/{HSPast10Days.MaxRunsPerDay} checks today — try again tomorrow.";
    }

    // Pre-fills Start/End Draw # with the current draw and ~10 days back, so the user only
    // needs to tap Check instead of typing both numbers. Hot Spot draws every ~4 min and is
    // closed 2am-6am Pacific (see InHotSpotClosedWindow), so ~20 running hours/day x 15
    // draws/hour = ~300 draws/day; using 300 is deliberately a little generous so the start
    // number errs toward covering slightly more than 10 days rather than less. User can still
    // edit either field before tapping Check.
    const int ApproxHotSpotDrawsPerDay = 300;
    const int Past10DaysPrefillDays = 10; // mirrors HSPast10Days.KeepDays

    void PrefillPast10DaysRange()
    {
        int currentDraw = Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber);
        if (currentDraw <= 0) return; // current draw# not known yet this launch — leave blank

        _p10EndEntry.Text = currentDraw.ToString();
        _p10StartEntry.Text = Math.Max(1, currentDraw - ApproxHotSpotDrawsPerDay * Past10DaysPrefillDays).ToString();
    }

    async Task RunPast10DaysCheckAsync()
    {
        // Belt-and-suspenders — the button is already disabled while a check is running (see
        // RefreshPast10DaysDialogState), but guard the actual start too in case this ever gets
        // reached some other way (e.g. a double-tap landing before the disabled state repaints).
        if (HSPast10Days.IsRunning)
        {
            RefreshPast10DaysDialogState();
            await DisplayAlert("Still Running", "A Past 10 Days check is already running — wait for it to finish (you'll get a text) before starting another.", "OK");
            return;
        }
        if (HSPast10Days.RunsUsedToday() >= HSPast10Days.MaxRunsPerDay)
        {
            RefreshPast10DaysDialogState();
            await DisplayAlert("Daily Limit Reached", $"You've already used {HSPast10Days.MaxRunsPerDay} Past 10 Days checks today — try again tomorrow.", "OK");
            return;
        }

        if (!int.TryParse(_p10StartEntry.Text?.Trim(), out int startDraw) || startDraw <= 0 ||
            !int.TryParse(_p10EndEntry.Text?.Trim(), out int endDraw) || endDraw <= 0)
        {
            await DisplayAlert("Enter Both Draw #s", "Type a Start Draw # and an End Draw # (numbers only) before checking.", "OK");
            return;
        }

        _past10DaysOverlay.IsVisible = false;
        _p10StartEntry.Text = "";
        _p10EndEntry.Text = "";

        _statusLabel.TextColor = Color.FromArgb("#8B9DC3");
        _statusLabel.Text = $"Checking Past 10 Days in the background (draws #{Math.Min(startDraw, endDraw)}–#{Math.Max(startDraw, endDraw)}) — you'll get a text when it's done.";
        _statusLabel.IsVisible = true;

        // Fire-and-forget — HSPast10Days.RunCheckAsync only ever touches Preferences/files (no
        // UI control), so it's safe to keep running after this method returns and the user has
        // moved on to whatever else they're doing on this ticket.
        _ = HSPast10Days.RunCheckAsync(startDraw, endDraw);
    }

    // "Last 200 Draws" — plain raw draw list (no ticket/win math), current picks + Bulls-eye
    // highlighted per row, meant for watching live while a ticket is active. Data comes from
    // Services/HSLast200Draws.cs; everything here is just the overlay shell + row rendering.
    Grid BuildLast200DrawsOverlay()
    {
        _last200StatusLabel = new Label
        {
            Text = "", FontSize = 11, TextColor = Color.FromArgb("#8B9DC3"),
            Margin = new Thickness(0, 0, 0, 8),
        };

        // CollectionView, not a plain ScrollView+VerticalStackLayout — Android backs this with
        // a RecyclerView that only ever builds the ~15 rows actually on screen, reusing them as
        // you scroll. Building all 200 rows up front (the original approach) was the actual
        // cause of the multi-second UI freeze reported live 2026-08-14.
        _last200List = new CollectionView
        {
            ItemTemplate = new DataTemplate(() => new Last200RowView()),
            SelectionMode = SelectionMode.Single,
        };
        // Just tracks which row is highlighted for the 💰 button below — CollectionView paints
        // the highlight itself via the Selected/Normal VisualStates set on Last200RowView.
        // The 💰 button itself lights up gold as a "tap me next" cue — it never auto-fires,
        // tapping a row only arms it, the user still has to tap 💰 themselves to see the payout.
        _last200List.SelectionChanged += (_, e) =>
        {
            _last200SelectedRow = e.CurrentSelection.FirstOrDefault() as Last200RowVm;
            UpdateLast200PayoutButtonHighlight();
        };

        var btnClose = new Button
        {
            Text = "Close", BackgroundColor = Color.FromArgb("#243447"), TextColor = Color.FromArgb("#8B9DC3"),
            CornerRadius = 8, FontSize = 11, FontAttributes = FontAttributes.Bold, Padding = new Thickness(10, 6),
        };
        btnClose.Clicked += (_, _) => _last200Overlay.IsVisible = false;

        _last200SearchButton = new Button
        {
            Text = "Search", Background = Make3DButtonBrush(Color.FromArgb("#6D28D9")), TextColor = Colors.White,
            CornerRadius = 8, FontSize = 11, FontAttributes = FontAttributes.Bold, Padding = new Thickness(0, 6),
        };
        _last200SearchButton.Clicked += (_, _) =>
        {
            // User's explicit ask 2026-08-14: prefill Start Draw # with the current draw so
            // Search is usually just "tap Search" for the common case, and always reset back to
            // Single # mode (not whatever was left selected last time the popup was used).
            // User's explicit ask 2026-08-15: prefill Covers Draw # with the current draw too —
            // a search is almost always backward from "now" (Covers Draw # is the OLDER end of
            // the range), so starting both fields equal means switching to Range mode just needs
            // that one field dialed back to however far, instead of starting from a blank/example.
            int currentDraw = Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber);
            _rangeSearchStartEntry.Text = currentDraw > 0 ? currentDraw.ToString() : "";
            _rangeSearchEndEntry.Text = currentDraw > 0 ? currentDraw.ToString() : "";
            _rangeSearchModeSingle = true;
            _rangeSearchSingleBtn.BackgroundColor = Color.FromArgb("#1565C0");
            _rangeSearchRangeBtn.BackgroundColor = Color.FromArgb("#243447");
            _rangeSearchEndBlock.IsVisible = false;
            // Reset "Use this Data" back off every time the dialog is (re)opened, same reasoning
            // as always resetting back to Single # mode above — otherwise a custom pick set typed
            // in during an earlier search would silently still be armed for this one.
            _rangeSearchUseCustomDataCheck.IsChecked = false;
            _rangeSearchSpotsBlock.IsVisible = false;
            _rangeSearchSpotsPicker.SelectedIndex = -1;
            RebuildRangeSearchSpotEntries();
            _rangeSearchOverlay.IsVisible = true;
        };

        // Walks the ticket's full covered range (same data "My Ticket" mode loads) and totals
        // every win in it — see AnalyzeActiveLast200TicketAsync. Result shown in a DisplayAlert,
        // same closeable-popup pattern the 💰 payout button already uses.
        _last200AnalyzeButton = new Button
        {
            Text = "Analyze Ticket", Background = Make3DButtonBrush(Color.FromArgb("#1565C0")), TextColor = Colors.White,
            CornerRadius = 8, FontSize = 11, FontAttributes = FontAttributes.Bold, Padding = new Thickness(0, 6),
        };
        // User's explicit ask 2026-08-14: disable the instant it's tapped (was previously
        // possible to tap it again while the first tap's data was still loading — the loading
        // call silently swallowed the second tap via _last200Busy with zero visual feedback,
        // which read as "have to click twice for it to come up"), re-enabled only when the
        // results popup is closed (see btnAnalysisClose below) — covers the loading AND the
        // whole time the popup is on screen, not just the load itself.
        _last200AnalyzeButton.Clicked += async (_, _) =>
        {
            _ = Logger.LogAsync("HS ANALYZE: tapped");
            _last200AnalyzeButton.IsEnabled = false;
            _last200AnalyzeButton.Opacity = 0.5;
            // User's explicit ask 2026-08-14: the button graying out alone wasn't a strong
            // enough "it's working" signal for a 3-4 sec wait — swapping its own text to a
            // spinner glyph + "Analyzing…" is guaranteed visible since that's exactly where the
            // user is already looking right after tapping it, no extra layout needed.
            _last200AnalyzeButton.Text = "⏳ Analyzing…";
            try { await AnalyzeActiveLast200TicketAsync(); }
            catch (Exception ex)
            {
                // Safety net — an exception here would otherwise leave _last200AnalyzeFlowActive
                // stuck true forever, permanently blocking every future load from re-enabling
                // this button, not just this one.
                _last200AnalyzeFlowActive = false;
                _last200AnalyzeButton.IsEnabled = true;
                _last200AnalyzeButton.Opacity = 1.0;
                _last200AnalyzeButton.Text = "Analyze Ticket";
                _ = Logger.LogAsync($"HS ANALYZE: threw — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        };

        _last200RefreshButton = new Button
        {
            Text = "Refresh", Background = Make3DButtonBrush(Color.FromArgb("#2E7D32")), TextColor = Colors.White,
            CornerRadius = 8, FontSize = 11, FontAttributes = FontAttributes.Bold, Padding = new Thickness(0, 6),
        };
        _last200RefreshButton.Clicked += async (_, _) => await LoadLast200DrawsAsync();

        // User's explicit ask 2026-08-15: "Analyze Ticket" only ever covers whichever ticket is
        // currently active — this walks every saved ("✓") slot in one tap instead of having to
        // switch to each one individually. Reads each slot's saved fields straight from
        // Preferences rather than switching _activeSlot through them, so it never disturbs
        // whatever's actually on screen (unlike Analyze Ticket, which forces My Ticket mode on
        // the active slot) — see AnalyzeAllTicketsAsync.
        //
        // User's explicit ask 2026-08-15 (final version): a real, always-visible Switch control
        // (_last200WhatIfSwitch, built below) sits right above this button — flip IT to arm
        // "What-if?", the button just runs whatever's currently armed. The earlier "flip itself
        // after each run" version was invisible until you'd already tapped it once, which is
        // exactly why an actual Switch replaced it. The switch is only interactable while the
        // gear menu's "What if -? (test)" mode (_viewingOnly) is on — see RefreshAnalyzeModeToggleUi.
        _last200AnalyzeAllButton = new Button
        {
            TextColor = Colors.White, CornerRadius = 8, FontSize = 12, FontAttributes = FontAttributes.Bold, Padding = new Thickness(0, 6),
        };
        _last200AnalyzeAllButton.Clicked += async (_, _) =>
        {
            if (_last200AnalyzeAllBusy) return;
            _last200AnalyzeAllBusy = true;
            // "Use this Data" — user's explicit ask 2026-08-17: once a custom search is active
            // (see _last200CustomPicks's own comment), this button must run the What-if-style
            // "score whatever's currently shown in the list" path regardless of the separate
            // What-if switch/viewing-only mode, since AnalyzeWhatIfListboxAsync is what actually
            // knows how to score custom picks without requiring any ticket to be saved.
            bool useCustom = _last200CustomPicks is { Count: > 0 };
            bool whatIf = useCustom || (_last200WhatIfSelected && _viewingOnly);
            string idleText = useCustom ? "Analyze Custom Search" : whatIf ? "Analyze What-if?" : "Analyze All Tickets";
            _last200AnalyzeAllButton.IsEnabled = false;
            _last200AnalyzeAllButton.Opacity = 0.5;
            _last200AnalyzeAllButton.Text = whatIf ? "Analyze What-if (searching)…" : "⏳ Analyzing…";
            try { await (whatIf ? AnalyzeWhatIfListboxAsync() : AnalyzeAllTicketsAsync()); }
            catch (Exception ex)
            {
                _ = Logger.LogAsync($"HS ANALYZE {(whatIf ? "WHATIF" : "ALL")}: threw — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                await DisplayAlert(idleText, "Something went wrong checking — try again.", "OK");
            }
            finally
            {
                _last200AnalyzeAllBusy = false;
                _last200AnalyzeAllButton.IsEnabled = true;
                _last200AnalyzeAllButton.Opacity = 1.0;
                RefreshAnalyzeModeToggleUi();
            }
        };

        _last200WhatIfLabel = new Label { Text = "What-if?", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
        _last200WhatIfSwitch = new Switch { VerticalOptions = LayoutOptions.Center, OnColor = Color.FromArgb("#6A1B9A"), ThumbColor = Colors.White };
        _last200WhatIfHintLabel = new Label { FontSize = 10, TextColor = Color.FromArgb("#6B7A93"), HorizontalOptions = LayoutOptions.Center, HorizontalTextAlignment = TextAlignment.Center };
        _last200WhatIfSwitch.Toggled += (_, e) =>
        {
            if (_last200SuppressWhatIfToggleEvent) return;
            _last200WhatIfSelected = e.Value;
            RefreshAnalyzeModeToggleUi();
        };
        // Not calling RefreshAnalyzeModeToggleUi() here — at this point in the constructor
        // _last200TitleLabel (built further down, right before headerBlock) doesn't exist yet,
        // so it would NullReferenceException. OpenLast200DrawsAsync already calls it every time
        // this dialog opens, which is the only time any of this UI is actually visible.

        _last200ExactMatchLabel = new Label { Text = $"Exact Match (T{_activeSlot + 1})", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
        _last200ExactMatchCheckBox = new CheckBox { Color = Color.FromArgb("#6A1B9A"), VerticalOptions = LayoutOptions.Center };
        _last200ExactMatchCheckBox.CheckedChanged += (_, e) =>
        {
            _last200ExactOnly = e.Value;
            // Unchecking must actually go back to showing every draw, not just leave the
            // picker sitting wherever the checked state had pinned it (Math.Clamp below would
            // otherwise just clamp back to the same "= spots" value it was already at).
            if (!_last200ExactOnly) _last200FilterMinMatches = 0;
            // Re-derives the picker's SelectedIndex (forced to spots when checked, per
            // PopulateLast200FilterOptions' own _last200ExactOnly check below) then re-filters
            // the already-loaded rows immediately — no re-fetch needed, same as the picker's
            // own SelectedIndexChanged handler. Picker disabled while checked so there's no
            // way to end up with the checkbox on but the picker showing a different threshold.
            _last200FilterPicker.IsEnabled = !_last200ExactOnly;
            PopulateLast200FilterOptions(_spots);
            ApplyLast200Filter();
        };

        _last200TicketLabel = new Label { FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#4CAF7D") };

        // Small everywhere in this row — four controls have to share one card-width line.
        _last200TabAllBtn = new Button { Text = "Last 200", FontSize = 10, CornerRadius = 6, Padding = new Thickness(0, 4) };
        _last200TabTicketBtn = new Button { Text = "My Ticket", FontSize = 10, CornerRadius = 6, Padding = new Thickness(0, 4) };
        _last200TabAllBtn.Clicked += async (_, _) => { SetLast200Mode(false); await LoadLast200DrawsAsync(); };
        _last200TabTicketBtn.Clicked += async (_, _) => { SetLast200Mode(true); await LoadLast200DrawsAsync(); };

        // Compact "closed" display — text refreshed by RefreshLast200TicketDisplay (called
        // wherever RefreshLast200SlotPickerItems used to be); tap opens _last200TicketOverlay,
        // whose rows are rebuilt fresh each time it opens (RefreshLast200TicketOverlayRows).
        _last200TicketDisplay = new Label
        {
            FontSize = 10, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#243447"),
            LineBreakMode = LineBreakMode.TailTruncation, VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Center,
        };
        _last200TicketDisplay.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(ShowLast200TicketOverlay)
        });

        _last200FilterPicker = new Picker
        {
            FontSize = 10, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#243447"),
            Title = "Match ≥",
        };
        _last200FilterPicker.SelectedIndexChanged += (_, _) => { if (!_last200SuppressFilterEvent) ApplyLast200Filter(); };

        _last200PayoutButton = new Button
        {
            Text = "💰", FontSize = 14, CornerRadius = 6, Padding = new Thickness(0, 4),
            BackgroundColor = Last200PayoutButtonIdleColor,
        };
        _last200PayoutButton.Clicked += (_, _) => ShowLast200SelectedPayout();

        var tabRow = new Grid
        {
            Margin = new Thickness(0, 6, 0, 0), ColumnSpacing = 4,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(46)),
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(38)),
            },
            Children = { _last200TabAllBtn, _last200TabTicketBtn, _last200TicketDisplay, _last200FilterPicker, _last200PayoutButton },
        };
        Grid.SetColumn(_last200TabAllBtn, 0);
        Grid.SetColumn(_last200TabTicketBtn, 1);
        Grid.SetColumn(_last200TicketDisplay, 2);
        Grid.SetColumn(_last200FilterPicker, 3);
        Grid.SetColumn(_last200PayoutButton, 4);

        Label RangeFieldLabel(string text) => new()
        {
            Text = text, FontSize = 10, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#D4A94A"), Margin = new Thickness(0, 0, 0, 2),
        };
        _last200RangeStartEntry = new Entry
        {
            Placeholder = "Start Draw #", Keyboard = Keyboard.Numeric, FontSize = 13,
            BackgroundColor = Color.FromArgb("#243447"), TextColor = Colors.White,
        };
        _last200RangeEndEntry = new Entry
        {
            Placeholder = "Covers Draws #", Keyboard = Keyboard.Numeric, FontSize = 13,
            BackgroundColor = Color.FromArgb("#243447"), TextColor = Colors.White,
        };
        // Write straight through to the real ticket fields — see the field comment above.
        _last200RangeStartEntry.TextChanged += (_, e) => _startDrawEntry.Text = e.NewTextValue;
        _last200RangeEndEntry.TextChanged   += (_, e) => _searchDrawEntry.Text = e.NewTextValue;

        var rangeStartBlock = new VerticalStackLayout { Children = { RangeFieldLabel("Start Draw #"), _last200RangeStartEntry } };
        var rangeEndBlock   = new VerticalStackLayout { Children = { RangeFieldLabel("Covers Draws #"), _last200RangeEndEntry } };
        var rangeFieldsGrid = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            Children = { rangeStartBlock, rangeEndBlock },
        };
        Grid.SetColumn(rangeStartBlock, 0);
        Grid.SetColumn(rangeEndBlock, 1);

        _last200RangeFieldsRow = new VerticalStackLayout
        {
            Spacing = 4, IsVisible = false, Margin = new Thickness(0, 8, 0, 0),
            Children = { rangeFieldsGrid },
        };

        _last200TitleLabel = new Label
        {
            Text = "Last Draws + Analyze Tickets", FontSize = 16, FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White, Padding = new Thickness(6, 4), BackgroundColor = Colors.Transparent,
        };
        var headerBlock = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                _last200TitleLabel,
                _last200TicketLabel,
                new Label
                {
                    Text = "Most recent first. Your current picks are highlighted green, Bulls-eye red (gold if it's one of your picks).",
                    FontSize = 10, TextColor = Color.FromArgb("#6B7A93"),
                },
                tabRow,
                _last200RangeFieldsRow,
                _last200StatusLabel,
            }
        };
        var buttonRow = new Grid
        {
            // Close sized to just its own text (Auto) instead of an equal Star share — user's
            // explicit ask 2026-08-14: with 4 equal columns "Analyze Ticket" wrapped onto two
            // lines. Close never needed as much room as the other three anyway.
            Margin = new Thickness(0, 10, 0, 0), ColumnSpacing = 6,
            // Close moved to the far right of Refresh (user's explicit ask 2026-08-17 — sitting
            // first/leftmost read as unconventional) — still Auto-sized to just its own text,
            // same reasoning as the 2026-08-14 comment below, just at the other end now.
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { btnClose, _last200SearchButton, _last200AnalyzeButton, _last200RefreshButton },
        };
        Grid.SetColumn(_last200SearchButton, 0);
        Grid.SetColumn(_last200AnalyzeButton, 1);
        Grid.SetColumn(_last200RefreshButton, 2);
        Grid.SetColumn(btnClose, 3);

        // Real, always-visible Switch + label — sits right above the analyze button, own row so
        // it doesn't compete for space with buttonRow's four columns.
        var whatIfSwitchRow = new HorizontalStackLayout
        {
            Spacing = 8, HorizontalOptions = LayoutOptions.Center,
            Children = { _last200WhatIfLabel, _last200WhatIfSwitch, _last200ExactMatchLabel, _last200ExactMatchCheckBox },
        };

        // Full-width analyze button below the switch — doesn't compete for space with
        // per-ticket actions the way a 5th column squeezed into buttonRow would have (that
        // row's already tight enough that Close had to drop to Auto-width, see above).
        var analyzeAllRow = new VerticalStackLayout
        {
            Spacing = 6, Margin = new Thickness(0, 6, 0, 0),
            Children = { whatIfSwitchRow, _last200WhatIfHintLabel, _last200AnalyzeAllButton },
        };

        var cardGrid = new Grid
        {
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            RowSpacing = 8,
            Children = { headerBlock, _last200List, buttonRow, analyzeAllRow },
        };
        Grid.SetRow(headerBlock, 0);
        Grid.SetRow(_last200List, 1);
        Grid.SetRow(buttonRow, 2);
        Grid.SetRow(analyzeAllRow, 3);

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1A2230"),
            Stroke = new SolidColorBrush(Color.FromArgb("#2D3E55")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(18, 16),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 360,
            HeightRequest = 564,
            Content = cardGrid,
        };

        _last200Overlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false,
            Children = { card },
        };
        return _last200Overlay;
    }

    async Task OpenLast200DrawsAsync()
    {
        // Opening always lands on "Last 200" (unchanged default) — the range fields are
        // pre-filled either way so switching to "My Ticket" immediately has sensible numbers.
        SetLast200Mode(false);
        // What if -? (test) mode may have been toggled off via the gear menu while this dialog
        // was closed — don't leave the "What-if" pill selected on a now-locked-out option.
        if (!_viewingOnly) _last200WhatIfSelected = false;
        RefreshAnalyzeModeToggleUi();
        _last200Overlay.IsVisible = true;
        await LoadLast200DrawsAsync();
    }

    // Keeps the toggle button's idle label in sync with _viewingOnly — never touched while a
    // run is in flight (the Clicked handler owns the text then, ending with its own call to this
    // once it's done) so this never stomps on "⏳ Analyzing…" mid-run.
    // Repaints the mode pills (selected highlight + "What-if" locked-out/dimmed unless
    // _viewingOnly is on) and the big Analyze button's idle label to match. Called from the pill
    // Clicked handlers, ToggleViewingOnly, and OpenLast200DrawsAsync.
    void RefreshAnalyzeModeToggleUi()
    {
        // "What-if?" can only ever be armed while What if -? (test) mode is actually on — force
        // back to "All Tickets" if it isn't, so a mode change elsewhere (gear menu) or the
        // dialog reopening can never leave this stuck showing "What-if?" with no way to reach it.
        if (!_viewingOnly) _last200WhatIfSelected = false;

        _last200SuppressWhatIfToggleEvent = true;
        _last200WhatIfSwitch.IsToggled = _last200WhatIfSelected;
        _last200SuppressWhatIfToggleEvent = false;
        _last200WhatIfSwitch.IsEnabled = _viewingOnly;
        _last200WhatIfLabel.Opacity = _last200WhatIfSwitch.Opacity = _viewingOnly ? 1.0 : 0.4;

        // "Use this Data" — a custom search (see _last200CustomPicks) always wins over the
        // What-if switch/viewing-only state for what this button/hint say and do; it needs no
        // saved ticket at all, unlike either the "All Tickets" or ordinary "What-if?" paths.
        bool useCustom = _last200CustomPicks is { Count: > 0 };
        _last200WhatIfHintLabel.Text = useCustom
            ? $"Using the {_last200CustomSpots} number(s) typed into \"Use this Data\" — no saved ticket needed."
            : _last200WhatIfSelected
                ? "What-if is on — Analyze All Tickets will score every draw currently shown in the list below."
                : _viewingOnly
                    ? "Viewing current draws only. Turn What-if on to analyze all draws in the list instead."
                    : "Viewing current draws only. Turn on What if -? (test) mode (⚙️ Options) to unlock What-if.";
        RefreshMainHeaderColor();

        if (_last200AnalyzeAllBusy) return;
        _last200AnalyzeAllButton.Text = useCustom ? "Analyze Custom Search" : _last200WhatIfSelected ? "Analyze What-if?" : "Analyze All Tickets";
        _last200AnalyzeAllButton.Background = Make3DButtonBrush(Color.FromArgb(useCustom || _last200WhatIfSelected ? "#6A1B9A" : "#B8860B"));
    }

    void SetLast200Mode(bool ticketMode)
    {
        _last200TicketMode = ticketMode;
        _last200RangeFieldsRow.IsVisible = ticketMode;
        _last200TabAllBtn.BackgroundColor = ticketMode ? Color.FromArgb("#243447") : Color.FromArgb("#1565C0");
        _last200TabTicketBtn.BackgroundColor = ticketMode ? Color.FromArgb("#1565C0") : Color.FromArgb("#243447");
        _last200TabAllBtn.TextColor = Colors.White;
        _last200TabTicketBtn.TextColor = Colors.White;

        // Prefill from the real ticket fields every time "My Ticket" is switched to, so it
        // always starts from whatever's actually on the ticket right now rather than a stale
        // value left over from the last time the dialog was open.
        //
        // End was `_searchDrawEntry.Text` (the main page's "Search#/Covers#" box) — but that
        // box holds KeyCoverDraw, a separate value the user can freely type over and which is
        // deliberately NEVER recomputed from Draws (see SaveTicketAsync's own comment on that
        // field). Real-money bug confirmed live 2026-08-17: a ticket whose Draws got corrected
        // to 20 still had a stale/mistyped KeyCoverDraw sitting one draw off from the true
        // Start#+19 end, so opening this dialog visibly snapped the Covers# from one number to
        // another the instant it loaded — looked exactly like the ticket's own data "changing"
        // on its own. AnalyzeActiveLast200TicketAsync already computes the real end as
        // Start#+Draws-1 and treats THAT as ground truth for what a ticket actually covers;
        // matching that same math here means the dialog shows the correct range from the moment
        // it opens, with nothing left to visibly snap once Analyze Ticket is tapped afterward.
        if (ticketMode)
        {
            _last200RangeStartEntry.Text = _startDrawEntry.Text;
            int realStart = Preferences.Get(SlotKey(KeyStartDraw, _activeSlot), 0);
            int realDraws = Preferences.Get(SlotKey(KeyDraws, _activeSlot), 1);
            _last200RangeEndEntry.Text = realStart > 0 ? (realStart + realDraws - 1).ToString() : _searchDrawEntry.Text;
        }
    }

    // Guarded by _last200Busy so a tap landing while a load/render is already in flight is a
    // silent no-op instead of queuing a second one on top of it (see the field's own comment
    // for what that used to do to the UI thread).
    async Task LoadLast200DrawsAsync()
    {
        if (_last200Busy) return;
        _last200Busy = true;
        // Always the real ticket's own data — clear any custom pick set left over from a "Use
        // this Data" search so Analyze What-if? doesn't keep scoring against stale custom numbers
        // once the list has moved on to a normal ticket load (see _last200CustomPicks's comment).
        _last200CustomPicks = null;
        RefreshAnalyzeModeToggleUi(); // repaints the big Analyze button/hint back off "Analyze Custom Search" immediately
        // Disabled the INSTANT busy flips true, synchronously, before this method's first
        // await — no matter which caller triggered this load (opening the dialog, Refresh, a
        // tab/slot switch, or Analyze Ticket itself), the button is now genuinely non-tappable
        // at the platform level for the whole duration, not just "ignored by app logic" while
        // still looking clickable. That was the real bug: a tap landing during some OTHER
        // caller's load (e.g. the dialog's own opening load) reached this method's `if
        // (_last200Busy) return;` bail specifically because nothing had disabled the button
        // for THAT load yet — the tap was genuinely registered, not blocked.
        _last200AnalyzeButton.IsEnabled = false;
        _last200AnalyzeButton.Opacity = 0.5;
        _last200TicketLabel.Text = $"Ticket {_activeSlot + 1} is selected — {_spots} spot{(_spots == 1 ? "" : "s")}{(_bullseye ? ", Bulls-eye" : "")}";
        // User's explicit ask 2026-08-16: show which slot the "Exact Match" checkbox applies
        // to, same T{n} convention _last200SlotPicker's own items already use — kept in sync
        // here since this method (LoadLast200DrawsAsync) already runs on every slot switch.
        _last200ExactMatchLabel.Text = $"Exact Match (T{_activeSlot + 1})";
        // Keep the dialog's own ticket display current — both which slot is active (may have
        // changed via the main page's picker while this dialog was already open) and this
        // slot's saved/empty/finished state (may have changed via Save/Delete elsewhere).
        RefreshLast200TicketDisplay();

        // Nothing to show/analyze for an empty ("○ T#") slot — user's explicit ask: don't fetch
        // anything and lock the action buttons until they pick a saved ("✓") ticket instead.
        bool ticketHasData = IsSlotSaved(_activeSlot);
        SetLast200ControlsEnabled(ticketHasData);
        if (!ticketHasData)
        {
            _last200AllRows.Clear();
            _last200List.ItemsSource = null;
            _last200SelectedRow = null;
            UpdateLast200PayoutButtonHighlight();
            _last200StatusLabel.Text = $"Ticket {_activeSlot + 1} has no saved data yet — pick a ✓ ticket above to view its draws.";
            _last200Busy = false;
            return;
        }

        _last200RefreshButton.IsEnabled = false;
        _last200RefreshButton.Opacity = 0.5;
        // Analyze Ticket's own enable/disable is handled entirely at its Clicked handler / the
        // results popup's Close (see there) — deliberately NOT toggled here, since re-enabling
        // it the moment loading finishes (this method's own finally, below) would flash it back
        // on right as the results popup opens, before the user has even seen the results.
        //
        // Analyze What-if? IS toggled here (same reasoning RunRangeSearchAsync now uses) — this
        // method rebuilds _last200AllDraws/_last200List, and AnalyzeWhatIfListboxAsync reads
        // those directly with no fetch of its own, so a tap landing mid-load would silently
        // analyze whatever was there BEFORE this load started.
        if (!_last200AnalyzeAllBusy)
        {
            _last200AnalyzeAllButton.IsEnabled = false;
            _last200AnalyzeAllButton.Opacity = 0.5;
        }
        _last200StatusLabel.Text = "Loading…";
        PopulateLast200FilterOptions(_spots);
        try
        {
            // The REAL saved ticket's own numbers, not _selected/_bullseye/_spots/_wager — see
            // LoadSavedSlotForScoring's own comment. Also snapshotted (via that method's own
            // fresh Preferences read, not any shared mutable field) before the off-UI-thread
            // build below, so a slot switch mid-build can't have it read half-changed state.
            var (picksArr, bullseyeOn, wagerSnapshot, spotsSnapshot) = LoadSavedSlotForScoring(_activeSlot);
            var picks = new HashSet<int>(picksArr);

            List<HotSpotDrawService.DrawResult> draws = _last200TicketMode
                ? await LoadTicketRangeDrawsAsync()
                : await HSLast200Draws.LoadAsync(Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber), progress =>
                    MainThread.BeginInvokeOnMainThread(() => _last200StatusLabel.Text = progress));

            if (draws == null) return; // LoadTicketRangeDrawsAsync already set an explanatory status and bailed

            _last200AllDraws = draws;
            _last200AllRows = await Task.Run(() => BuildLast200RowVms(draws, picks, bullseyeOn, spotsSnapshot, wagerSnapshot));
            ApplyLast200Filter();
        }
        finally
        {
            _last200RefreshButton.IsEnabled = true;
            _last200RefreshButton.Opacity = 1.0;
            // Leave Analyze disabled if AnalyzeActiveLast200TicketAsync is still mid-flow — it
            // owns the button's final state once this call returns to it (stays disabled behind
            // the results popup, or re-enables itself on a genuine "no draws" bail). Any OTHER
            // caller (dialog open, Refresh, tab/slot switch) re-enables it normally right here.
            if (!_last200AnalyzeFlowActive)
            {
                bool hasData = IsSlotSaved(_activeSlot);
                _last200AnalyzeButton.IsEnabled = hasData;
                _last200AnalyzeButton.Opacity = hasData ? 1.0 : 0.4;
            }
            // Same "don't undo my own click handler's disable" guard as Analyze Ticket just
            // above, mirrored for Analyze What-if?'s own busy flag.
            if (!_last200AnalyzeAllBusy)
            {
                _last200AnalyzeAllButton.IsEnabled = true;
                _last200AnalyzeAllButton.Opacity = 1.0;
            }
            _last200Busy = false;
        }
    }

    // Locks/unlocks the dialog's action controls based on whether the active ticket actually
    // has saved data — user's explicit ask: an empty ("○ T#") slot has nothing to fetch,
    // analyze, or filter, so Last 200/My Ticket/Match/Analyze Ticket/Refresh/💰 all get
    // disabled+dimmed until the user picks a saved ("✓") ticket instead. Deliberately leaves
    // Close and the ticket picker itself untouched — the T# picker is exactly how the user
    // escapes an empty ticket in the first place.
    void SetLast200ControlsEnabled(bool enabled)
    {
        double opacity = enabled ? 1.0 : 0.4;
        _last200TabAllBtn.IsEnabled = enabled;
        _last200TabAllBtn.Opacity = opacity;
        _last200TabTicketBtn.IsEnabled = enabled;
        _last200TabTicketBtn.Opacity = opacity;
        // Analyze Ticket is deliberately NOT touched here when enabled==true — confirmed live
        // 2026-08-14: this runs from inside LoadLast200DrawsAsync, which the Analyze button's own
        // Clicked handler calls right after disabling itself, so unconditionally re-enabling it
        // here undid that disable before the user ever saw it gray out. Its enabled state is now
        // owned entirely by its own Clicked handler and the results popup's Close (see there) —
        // still allowed to force it OFF here for a genuinely empty/unsaved slot, since that case
        // must always win regardless of what the click flow is doing.
        if (!enabled)
        {
            _last200AnalyzeButton.IsEnabled = false;
            _last200AnalyzeButton.Opacity = 0.4;
        }
        _last200FilterPicker.IsEnabled = enabled;
        _last200FilterPicker.Opacity = opacity;
        _last200RefreshButton.IsEnabled = enabled;
        _last200RefreshButton.Opacity = opacity;
        _last200PayoutButton.IsEnabled = enabled;
        _last200PayoutButton.Opacity = opacity;
    }

    // Rebuilds the "Match ≥ N" options for the active ticket's spot count (item i's label is
    // always "Match ≥ i", so the picker's SelectedIndex doubles as the threshold value — no
    // separate lookup table needed). The top item reads "Match = N" instead of "≥" since
    // matches can never exceed the ticket's own spot count, so "≥" would be misleading there.
    // Preserves the previous threshold across a spot-count change when it still fits (e.g.
    // switching tickets), clamping down otherwise. Guarded by _last200SuppressFilterEvent so
    // setting SelectedIndex here doesn't fire a premature re-filter against the previous load's
    // rows.
    void PopulateLast200FilterOptions(int spots)
    {
        // User's explicit ask 2026-08-16: while the "Exact" checkbox is on, the filter is
        // always pinned to "Match = spots" (every pick landed) regardless of whatever
        // _last200FilterMinMatches was left at before — follows the active ticket's spot
        // count automatically across a ticket switch, same as the un-pinned clamp below does.
        int desired = _last200ExactOnly ? spots : Math.Clamp(_last200FilterMinMatches, 0, spots);
        _last200SuppressFilterEvent = true;
        _last200FilterPicker.ItemsSource = Enumerable.Range(0, spots + 1).Select(n => n == spots ? $"Match = {n}" : $"Match ≥ {n}").ToList();
        _last200FilterPicker.SelectedIndex = desired;
        _last200SuppressFilterEvent = false;
        _last200FilterMinMatches = desired;
    }

    // Lights the 💰 button gold once a row is selected — purely visual, never fires the payout
    // itself, just points at the button the user taps next.
    void UpdateLast200PayoutButtonHighlight() =>
        _last200PayoutButton.BackgroundColor = _last200SelectedRow != null ? Last200PayoutButtonArmedColor : Last200PayoutButtonIdleColor;

    // Purely a local re-filter of the already-loaded _last200AllRows — never re-fetches, so
    // changing the dropdown mid-session is instant.
    void ApplyLast200Filter()
    {
        _last200FilterMinMatches = Math.Max(0, _last200FilterPicker.SelectedIndex);
        var filtered = _last200AllRows.Where(r => r.Matches >= _last200FilterMinMatches).ToList();
        _last200List.ItemsSource = filtered;
        _last200List.SelectedItem = null; // the old selection may not even be in the new filtered list anymore
        _last200SelectedRow = null;
        UpdateLast200PayoutButtonHighlight();

        if (_last200AllRows.Count == 0)
        {
            _last200StatusLabel.Text = "No draws available yet.";
            return;
        }

        int minDn = _last200AllRows.Min(r => r.DrawNumber);
        int maxDn = _last200AllRows.Max(r => r.DrawNumber);
        string order = _last200TicketMode ? "for this ticket, oldest first" : "most recent first";
        string filterOp = _last200FilterMinMatches == _spots ? "=" : "≥";
        string filterNote = _last200FilterMinMatches > 0 ? $" matching Match {filterOp} {_last200FilterMinMatches}" : "";
        _last200StatusLabel.Text = $"Showing {filtered.Count} of {_last200AllRows.Count} draw{(_last200AllRows.Count == 1 ? "" : "s")}{filterNote}, {order} — draws #{minDn}–#{maxDn}.";
    }

    // The 💰 button's handler — shows what the currently-selected (tapped) row would actually
    // pay for the active ticket, reusing Matches/WinAmount already computed onto that row's
    // Last200RowVm by BuildLast200RowVms (HotSpotDrawService.Score under the hood), so this
    // always agrees with the real payout tables rather than recomputing anything itself.
    async void ShowLast200SelectedPayout()
    {
        if (_last200SelectedRow is not { } row)
        {
            await DisplayAlert("No Draw Selected", "Tap a draw in the list first, then tap 💰 to see its payout.", "OK");
            return;
        }
        // "(Bulls-eye)" not "+ Bulls-eye" — the Bulls-eye hit is one of the Matches already
        // counted above, not a separate event on top of them, so "+" reads misleadingly.
        string body = row.WinAmount > 0
            ? $"{row.Matches} match{(row.Matches == 1 ? "" : "es")}{(row.BullseyeHit ? " (Bulls-eye)" : "")}\n\nWin: ${row.WinAmount:N2}"
            : $"{row.Matches} match{(row.Matches == 1 ? "" : "es")} — no win at this ticket's spot count/wager.";
        await DisplayAlert($"Draw {row.DrawText} Payout", body, "OK");
    }

    // "Analyze Ticket" button's handler — forces My Ticket mode (so the range fields hold the
    // active ticket's real Start Draw #/Covers Draws #, not whatever "Last 200" happened to be
    // showing) then loads its full covered range and totals every win in it, same math the 💰
    // button uses per-row via BuildLast200RowVms/HotSpotDrawService.Score. Shown in
    // _ticketAnalysisOverlay (a real aligned table, see BuildTicketAnalysisOverlay) rather than
    // a DisplayAlert — plain-text dashes didn't line up once match text varied in length.
    async Task AnalyzeActiveLast200TicketAsync()
    {
        // Both early-exit paths below never show the results popup, so nothing would otherwise
        // re-enable the button (only the popup's own Close does that) — re-enable here first so
        // it doesn't get stuck disabled.
        if (_last200Busy)
        {
            _ = Logger.LogAsync("HS ANALYZE: bailed — _last200Busy was already true");
            _last200AnalyzeButton.IsEnabled = true;
            _last200AnalyzeButton.Opacity = 1.0;
            _last200AnalyzeButton.Text = "Analyze Ticket";
            return;
        }
        // Tells LoadLast200DrawsAsync's own finally (below, via await) to leave the Analyze
        // button disabled instead of re-enabling it when that method finishes — this method
        // decides the button's real final state itself, right after that await returns.
        _last200AnalyzeFlowActive = true;
        SetLast200Mode(true);
        // SetLast200Mode just copied whatever's CURRENTLY sitting in _startDrawEntry/
        // _searchDrawEntry into the range boxes — not necessarily the real saved ticket. Those
        // two live entries can be left showing an unrelated draw# after paging (◀/▶ intentionally
        // repaints _startDrawEntry with the paged-to draw for browsing, see PageDrawCoreAsync) or
        // after typing directly into these same range boxes to explore a different span. Confirmed
        // live 2026-08-14: this produced an "analysis" spanning draws the ticket never actually
        // covered. Analyze Ticket must always reflect what was actually PURCHASED, so overwrite
        // the range boxes here with the real persisted Start#/Covers# right before loading —
        // this only affects this button; "My Ticket" mode itself still lets you freely browse/edit.
        //
        // EXCEPT in What-if (test) mode — user's explicit ask 2026-08-14: the whole point of
        // typing a different Covers Draws # while _viewingOnly is on is to see what a wider/
        // narrower range WOULD have paid, so it must never get silently snapped back to what
        // was actually purchased. Real-ticket accuracy only matters when this analysis could be
        // mistaken for the real ticket's real result — What-if mode already labels/treats
        // everything as exploratory (see ApplyDrawResult's _viewingOnly branch), so it's safe
        // to trust whatever the user typed here instead.
        if (!_viewingOnly)
        {
            int realStart = Preferences.Get(SlotKey(KeyStartDraw, _activeSlot), 0);
            int realDraws = Preferences.Get(SlotKey(KeyDraws, _activeSlot), 1);
            if (realStart > 0)
            {
                _last200RangeStartEntry.Text = realStart.ToString();
                _last200RangeEndEntry.Text = (realStart + realDraws - 1).ToString();
            }
        }
        _ = Logger.LogAsync($"HS ANALYZE: range set to #{_last200RangeStartEntry.Text}-#{_last200RangeEndEntry.Text}, loading…");
        await LoadLast200DrawsAsync();
        _last200AnalyzeFlowActive = false; // back in our hands — decide the button's real state below
        _ = Logger.LogAsync($"HS ANALYZE: load returned {_last200AllRows.Count} rows");
        if (_last200AllRows.Count == 0)
        {
            _last200AnalyzeButton.IsEnabled = true;
            _last200AnalyzeButton.Opacity = 1.0;
            _last200AnalyzeButton.Text = "Analyze Ticket";
            await DisplayAlert("Analyze Ticket", "No draws loaded for this ticket yet — check the Start Draw #/Covers Draws # above.", "OK");
            return;
        }
        _ = Logger.LogAsync("HS ANALYZE: showing popup");

        var wins = _last200AllRows.Where(r => r.WinAmount > 0).OrderBy(r => r.DrawNumber).ToList();
        decimal total = wins.Sum(r => r.WinAmount);

        _ticketAnalysisTitle.Text = $"Ticket {_activeSlot + 1} Analysis";
        _ticketAnalysisSubtitle.Text = wins.Count == 0
            ? $"No wins across all {_last200AllRows.Count} covered draws."
            : $"{wins.Count} of {_last200AllRows.Count} draws paid.";

        _ticketAnalysisRows.Children.Clear();
        AddGroupedWinRows(_ticketAnalysisRows, wins.Select(r => (r.DrawNumber, r.Matches, r.BullseyeHit, r.WinAmount)));
        _ticketAnalysisLegend.IsVisible = false; // only "Draws Left by Ticket" uses this
        _ticketAnalysisTotal.Text = $"${total:N2}";
        _ticketAnalysisOverlay.IsVisible = true;
    }

    // "Analyze All" button's handler — user's explicit ask 2026-08-15: walk every SAVED slot
    // (not just whichever one is active) and total up every win across all of them in one tap.
    // Deliberately reads each slot's fields straight from Preferences and fetches via
    // FetchDrawRangeAsync directly, rather than routing through LoadSlot/SwitchToSlotAsync for
    // each one — that would repeatedly overwrite _activeSlot, _selected, and every on-screen
    // control for a ticket the user isn't even looking at, and would fight with whatever they
    // actually have on screen right now. Same results popup as AnalyzeActiveLast200TicketAsync
    // (_ticketAnalysisOverlay), just with a group header + subtotal per ticket ahead of that
    // ticket's own win rows, same win-row builder underneath.
    async Task AnalyzeAllTicketsAsync()
    {
        var savedSlots = Enumerable.Range(0, SlotCount).Where(IsSlotSaved).ToList();
        if (savedSlots.Count == 0)
        {
            await DisplayAlert("Analyze All Tickets", "No saved tickets yet — pick and Save at least one ticket first.", "OK");
            return;
        }

        _ticketAnalysisRows.Children.Clear();
        decimal grandTotal = 0m;
        int grandWins = 0, ticketsAnalyzed = 0, ticketsSkipped = 0;

        for (int i = 0; i < savedSlots.Count; i++)
        {
            int slot = savedSlots[i];
            int startDraw = Preferences.Get(SlotKey(KeyStartDraw, slot), 0);
            int drawsCount = Preferences.Get(SlotKey(KeyDraws, slot), 1);
            // Same guard CheckFinishedTicketsAsync uses — a slot can be "saved" (picks + Save
            // tapped) without a receipt Starting Draw # ever having been entered, and there's no
            // range to walk without one.
            if (startDraw <= 0) { ticketsSkipped++; continue; }

            string numbersRaw = Preferences.Get(SlotKey(KeyNumbers, slot), "");
            var picks = numbersRaw.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => int.TryParse(t, out int n) ? n : -1).Where(n => n is >= 1 and <= 80).ToArray();
            if (picks.Length == 0) { ticketsSkipped++; continue; }

            bool bullseye = Preferences.Get(SlotKey(KeyBullseye, slot), false);
            decimal wager = (decimal)Preferences.Get(SlotKey(KeyWager, slot), 1.0);
            // Same reasoning LoadSlot uses for an already-purchased ticket: the real spot count
            // is whatever numbers were actually saved with it, not the separate Spots field
            // (which can go stale — see LoadSlot's own comment). Every slot reaching here is
            // guaranteed purchased (IsSlotSaved requires it), so this is always safe.
            int spotsForSlot = picks.Length;
            int endDraw = startDraw + drawsCount - 1;

            _last200StatusLabel.Text = $"Analyzing Ticket {slot + 1} ({i + 1} of {savedSlots.Count})…";
            var draws = await FetchDrawRangeAsync(startDraw, endDraw);
            ticketsAnalyzed++;

            decimal subtotal = 0m;
            var winRows = new List<(int DrawNumber, int Matches, bool BullseyeHit, decimal WinAmount)>();
            foreach (var draw in draws)
            {
                var (matches, bullseyeHit, winAmount) = HotSpotDrawService.Score(picks, bullseye, spotsForSlot, wager, draw);
                if (winAmount <= 0) continue;
                winRows.Add((draw.DrawNumber, matches, bullseyeHit, winAmount));
                subtotal += winAmount;
            }
            grandTotal += subtotal;
            grandWins += winRows.Count;

            _ticketAnalysisRows.Children.Add(BuildTicketAnalysisGroupHeader(slot, draws.Count, winRows.Count, subtotal));
            AddGroupedWinRows(_ticketAnalysisRows, winRows);
        }
        _last200StatusLabel.Text = "";

        if (ticketsAnalyzed == 0)
        {
            await DisplayAlert("Analyze All Tickets", "None of your saved tickets have a Starting Draw # entered yet — nothing to check.", "OK");
            return;
        }

        _ticketAnalysisTitle.Text = "All Tickets Analysis";
        _ticketAnalysisSubtitle.Text = ticketsSkipped > 0
            ? $"{ticketsAnalyzed} ticket{(ticketsAnalyzed == 1 ? "" : "s")} checked, {grandWins} win{(grandWins == 1 ? "" : "s")} — {ticketsSkipped} skipped (no Starting Draw #)."
            : $"{ticketsAnalyzed} ticket{(ticketsAnalyzed == 1 ? "" : "s")} checked, {grandWins} win{(grandWins == 1 ? "" : "s")} total.";
        _ticketAnalysisLegend.IsVisible = false; // only "Draws Left by Ticket" uses this
        _ticketAnalysisTotal.Text = $"${grandTotal:N2}";
        _ticketAnalysisOverlay.IsVisible = true;
    }

    // Reads slot `slot`'s REAL saved ticket data straight from Preferences — picks, Bulls-eye,
    // wager, and spot count (derived from picks.Length, same as AnalyzeAllTicketsAsync/
    // AnalyzeWhatIfListboxAsync already did before this was factored out). Deliberately never
    // touches _selected/_bullseye/_spots/_wager, which mirror whatever's on screen right now —
    // in What-if/test mode those are routinely different exploratory test values, not the real
    // ticket. Confirmed live 2026-08-16: LoadLast200DrawsAsync and RunRangeSearchAsync were both
    // still using _selected directly (pre-dating this helper), which is exactly what made a
    // Search/"My Ticket" load score against whatever test picks happened to be on screen instead
    // of the ticket actually being browsed — same bug class this method already existed to fix
    // for the What-if analyze path, just not yet applied everywhere it needed to be.
    (int[] Picks, bool Bullseye, decimal Wager, int Spots) LoadSavedSlotForScoring(int slot)
    {
        string numbersRaw = Preferences.Get(SlotKey(KeyNumbers, slot), "");
        var picks = numbersRaw.Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => int.TryParse(t, out int n) ? n : -1).Where(n => n is >= 1 and <= 80).ToArray();
        bool bullseye = Preferences.Get(SlotKey(KeyBullseye, slot), false);
        decimal wager = (decimal)Preferences.Get(SlotKey(KeyWager, slot), 1.0);
        return (picks, bullseye, wager, picks.Length);
    }

    // "What-if" side of the toggle (only reachable while _viewingOnly/"What if -? (test)" mode
    // is on) — user's explicit ask 2026-08-15: check the SELECTED SLOT's real saved numbers
    // against every draw CURRENTLY sitting in the on-screen list (whichever tab/Match filter/
    // Search result is active) and total up every win. Deliberately re-scores from _last200AllDraws
    // (the raw draws) against the slot's real Preferences-saved picks rather than trusting each
    // row's precomputed WinAmount — that was computed against whatever picks were on screen at
    // load time (_selected), which in What-if/test mode is often different test numbers, not the
    // real saved ticket. Same per-slot field reads AnalyzeAllTicketsAsync uses, just for one slot
    // instead of every saved one, and never touches Preferences/the ticket fields on screen.
    async Task AnalyzeWhatIfListboxAsync()
    {
        int slot = _activeSlot;
        int[] picks;
        bool bullseye;
        decimal wager;
        int spotsForSlot;
        // "Use this Data" — user's explicit ask 2026-08-17: when the list currently on screen
        // came from a custom search (see _last200CustomPicks's own comment), What-if scores
        // against those typed numbers and never needs any slot saved at all. Only falls back to
        // requiring the active ticket's real saved numbers when no custom search is in play.
        if (_last200CustomPicks is { Count: > 0 })
        {
            picks = _last200CustomPicks.ToArray();
            bullseye = false;
            wager = 1.0m;
            spotsForSlot = _last200CustomSpots;
        }
        else
        {
            if (!IsSlotSaved(slot))
            {
                await DisplayAlert("Analyze What-if?", $"Ticket {slot + 1} has no saved numbers yet — pick and Save a ticket first, then What-if checks those numbers against whatever's in the list.", "OK");
                return;
            }
            (picks, bullseye, wager, spotsForSlot) = LoadSavedSlotForScoring(slot);
            if (picks.Length == 0)
            {
                await DisplayAlert("Analyze What-if?", $"Ticket {slot + 1} has no saved numbers yet — pick and Save a ticket first.", "OK");
                return;
            }
        }

        // Only the draws actually shown right now (respects whatever Match filter is dialed in),
        // matched back to the raw draws that carry the real numbers Score() needs.
        var shownDrawNumbers = (_last200List.ItemsSource as IEnumerable<Last200RowVm>)?.Select(r => r.DrawNumber).ToHashSet() ?? new HashSet<int>();
        var draws = _last200AllDraws.Where(d => shownDrawNumbers.Contains(d.DrawNumber)).ToList();
        if (draws.Count == 0)
        {
            await DisplayAlert("Analyze What-if?", "No draws currently showing in the list — load some draws first.", "OK");
            return;
        }

        var winRows = new List<(int DrawNumber, int Matches, bool BullseyeHit, decimal WinAmount)>();
        decimal total = 0m;
        foreach (var draw in draws)
        {
            var (matches, bullseyeHit, winAmount) = HotSpotDrawService.Score(picks, bullseye, spotsForSlot, wager, draw);
            if (winAmount <= 0) continue;
            winRows.Add((draw.DrawNumber, matches, bullseyeHit, winAmount));
            total += winAmount;
        }

        _ticketAnalysisTitle.Text = _last200CustomPicks is { Count: > 0 }
            ? $"What-if Analysis — Custom Search ({spotsForSlot} spots)"
            : $"What-if Analysis — Ticket {slot + 1}";
        _ticketAnalysisSubtitle.Text = winRows.Count == 0
            ? $"No wins across all {draws.Count} draws shown in the list."
            : $"{winRows.Count} of {draws.Count} draws shown paid.";

        _ticketAnalysisRows.Children.Clear();
        AddGroupedWinRows(_ticketAnalysisRows, winRows);
        _ticketAnalysisLegend.IsVisible = false; // only "Draws Left by Ticket" uses this
        _ticketAnalysisTotal.Text = $"${total:N2}";
        _ticketAnalysisOverlay.IsVisible = true;
    }

    // Group header ahead of one ticket's win rows in the "Analyze All Tickets" popup — same
    // three-column alignment idea as BuildTicketAnalysisRow (label left, $ amount right), plus a
    // thin divider so consecutive tickets' rows don't visually run together.
    static View BuildTicketAnalysisGroupHeader(int slot, int drawsChecked, int winsCount, decimal subtotal)
    {
        var g = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Margin = new Thickness(0, 10, 0, 4),
        };
        var label = new Label
        {
            Text = winsCount > 0
                ? $"Ticket {slot + 1} — {winsCount} win{(winsCount == 1 ? "" : "s")} of {drawsChecked}"
                : $"Ticket {slot + 1} — no wins ({drawsChecked} draws)",
            FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#D4A94A"), VerticalOptions = LayoutOptions.Center,
        };
        var amount = new Label
        {
            Text = $"${subtotal:N2}", FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = subtotal > 0 ? Color.FromArgb("#4CAF7D") : Color.FromArgb("#6B7A93"),
            HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center,
        };
        g.Add(label, 0, 0);
        g.Add(amount, 1, 0);
        var divider = new BoxView { HeightRequest = 1, Color = Color.FromArgb("#2D3E55"), Margin = new Thickness(0, 2, 0, 0) };
        return new VerticalStackLayout { Spacing = 0, Children = { g, divider } };
    }

    // One line of the analysis table — three fixed columns (draw #, match description, amount)
    // so every row's $ figure lands in the same spot regardless of how long the match text is,
    // instead of the plain-text "—"-separated line that didn't align once "1 match (Bulls-eye)"
    // sat above "3 matches".
    // `spotsText` is only passed by the "Draws Left by Ticket" list (user's explicit ask
    // 2026-08-20: a column next to "Ticket #" showing how many spots that ticket picked) — the
    // other caller (individual draw-win rows inside a match-count group) leaves it null and
    // gets the original 3-column layout unchanged.
    static View BuildTicketAnalysisRow(string drawText, string matchText, decimal amount, string? spotsText = null)
    {
        var g = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 3) };
        var drawLabel = new Label { Text = drawText, FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#D4A94A"), VerticalOptions = LayoutOptions.Center };
        var matchLabel = new Label { Text = matchText, FontSize = 12, TextColor = Color.FromArgb("#C7D0DC"), VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
        var amountLabel = new Label { Text = $"${amount:N2}", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#4CAF7D"), HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center };

        if (spotsText == null)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(64)));
            g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            g.Add(drawLabel, 0, 0);
            g.Add(matchLabel, 1, 0);
            g.Add(amountLabel, 2, 0);
        }
        else
        {
            g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(64)));
            g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(58)));
            g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            var spotsLabel = new Label { Text = spotsText, FontSize = 12, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
            g.Add(drawLabel, 0, 0);
            g.Add(spotsLabel, 1, 0);
            g.Add(matchLabel, 2, 0);
            g.Add(amountLabel, 3, 0);
        }
        return g;
    }

    // Dedicated row for a Bulls-eye hit nested under its ticket's row in ShowDrawsLeftByTicketAsync
    // — user's explicit ask 2026-08-19: the first attempt reused BuildTicketAnalysisRow's 3-column
    // layout, but that made the hit row look like its own peer "Ticket" entry (same gold color,
    // same column position as the "Ticket N" label above it) — "hard to make out is that for
    // ticket 1 or not". This one is deliberately different in every way that matters: indented
    // (Padding left), a single line (no separate match-count column to wrap awkwardly), and
    // entirely in Bulls-eye red (both the label AND the amount, not just the amount) so it reads
    // unmistakably as a note ABOUT the row above it rather than a sibling row.
    static View BuildBullseyeHitRow(int ticketNumber, int drawNumber, decimal amount)
    {
        var g = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8,
            Padding = new Thickness(20, 1, 0, 2),
        };
        // Spells out "Ticket N" explicitly rather than relying on indentation/position alone to
        // imply which ticket this hit belongs to — user's explicit ask 2026-08-19 ("so i know the
        // bulls-eye reflect ticket 1"), after indentation alone still read as ambiguous.
        var label = new Label { Text = $"🎯 Ticket {ticketNumber} Bulls-eye — draw #{drawNumber}", FontSize = 11, TextColor = BallBullseye, VerticalOptions = LayoutOptions.Center, LineBreakMode = LineBreakMode.TailTruncation };
        var amountLabel = new Label { Text = $"${amount:N2}", FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = BallBullseye, HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center };
        g.Add(label, 0, 0);
        g.Add(amountLabel, 1, 0);
        return g;
    }

    // User's explicit ask 2026-08-15: a flat list of wins reads as an unreadable wall once a
    // wide search turns up dozens of same-match-count rows (e.g. thirty "2 matches" rows in a
    // row) — group them by match count instead, each group collapsed behind a ▶/▼ chevron
    // header (same tap-to-collapse convention already used elsewhere in this app), so the popup
    // opens showing just "2 matches (30) — $30.00" / "3 matches (1) — $4.00" and you expand only
    // the group you want the individual draw #s for. Highest match count first (the rarer,
    // more interesting hits) so those aren't buried under a long run of small ones.
    static void AddGroupedWinRows(VerticalStackLayout container, IEnumerable<(int DrawNumber, int Matches, bool BullseyeHit, decimal WinAmount)> wins)
    {
        foreach (var group in wins.GroupBy(w => w.Matches).OrderByDescending(g => g.Key))
        {
            var rows = group.OrderBy(w => w.DrawNumber).ToList();
            decimal subtotal = rows.Sum(w => w.WinAmount);
            bool anyBullseye = rows.Any(w => w.BullseyeHit);

            var body = new VerticalStackLayout { Spacing = 0, IsVisible = false };
            foreach (var r in rows)
            {
                string matchText = $"{r.Matches} match{(r.Matches == 1 ? "" : "es")}{(r.BullseyeHit ? " (Bulls-eye)" : "")}";
                body.Children.Add(BuildTicketAnalysisRow($"#{r.DrawNumber}", matchText, r.WinAmount));
            }

            var chevron = new Label { Text = "▶", FontSize = 12, TextColor = Color.FromArgb("#D4A94A"), VerticalOptions = LayoutOptions.Center, WidthRequest = 16 };
            var headerLabel = new Label
            {
                Text = $"{group.Key} match{(group.Key == 1 ? "" : "es")} ({rows.Count}){(anyBullseye ? " ★" : "")}",
                FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#D4A94A"), VerticalOptions = LayoutOptions.Center,
            };
            var subtotalLabel = new Label
            {
                Text = $"${subtotal:N2}", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#4CAF7D"),
                HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center,
            };
            var headerGrid = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                Padding = new Thickness(0, 6),
                Children = { chevron, headerLabel, subtotalLabel },
            };
            Grid.SetColumn(chevron, 0);
            Grid.SetColumn(headerLabel, 1);
            Grid.SetColumn(subtotalLabel, 2);
            headerGrid.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    body.IsVisible = !body.IsVisible;
                    chevron.Text = body.IsVisible ? "▼" : "▶";
                }),
            });

            var divider = new BoxView { HeightRequest = 1, Color = Color.FromArgb("#2D3E55"), Margin = new Thickness(0, 2, 0, 0) };
            container.Children.Add(new VerticalStackLayout { Spacing = 0, Children = { headerGrid, body, divider } });
        }
    }

    // Table popup for "Analyze Ticket" — same overlay pattern as BuildPayoutOverlay/
    // BuildLast200DrawsOverlay (a dark Border card centered over a dimmed backdrop), populated
    // fresh each time by AnalyzeActiveLast200TicketAsync rather than rebuilt per open.
    Grid BuildTicketAnalysisOverlay()
    {
        _ticketAnalysisTitle = new Label
        {
            FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.White,
        };
        _ticketAnalysisSubtitle = new Label
        {
            FontSize = 12, TextColor = Color.FromArgb("#8B9DC3"), Margin = new Thickness(0, 2, 0, 0),
        };
        _ticketAnalysisLegend = new Label
        {
            // Same red as the drawn Bulls-eye ball itself (BallBullseye) — one color, one meaning,
            // everywhere in the page.
            FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = BallBullseye,
            Margin = new Thickness(0, 2, 0, 0), IsVisible = false,
        };
        _ticketAnalysisRows = new VerticalStackLayout { Spacing = 0, Margin = new Thickness(0, 8, 0, 0) };

        var totalRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Margin = new Thickness(0, 4, 0, 0),
        };
        var totalCaption = new Label { Text = "Total", FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center };
        _ticketAnalysisTotal = new Label { FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#FFD54F"), HorizontalTextAlignment = TextAlignment.End, VerticalOptions = LayoutOptions.Center };
        totalRow.Add(totalCaption, 0, 0);
        totalRow.Add(_ticketAnalysisTotal, 1, 0);

        var btnClose = new Button
        {
            Text = "Close", FontSize = 13, TextColor = Color.FromArgb("#FFD54F"),
            BackgroundColor = Colors.Transparent, HorizontalOptions = LayoutOptions.End,
            Padding = new Thickness(4, 4),
        };
        btnClose.Clicked += (_, _) =>
        {
            _ticketAnalysisOverlay.IsVisible = false;
            _last200AnalyzeButton.IsEnabled = true;
            _last200AnalyzeButton.Opacity = 1.0;
            _last200AnalyzeButton.Text = "Analyze Ticket";
        };

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1A2230"),
            Stroke = new SolidColorBrush(Color.FromArgb("#2D3E55")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(18, 16),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            // 340 -> 380 — user's explicit ask 2026-08-19: the indented "Ticket N Bulls-eye —
            // draw #N" sub-rows (ShowDrawsLeftByTicketAsync) need the extra room so that text
            // doesn't get cramped/truncated at the narrower width the other two callers were fine with.
            WidthRequest = 380,
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    _ticketAnalysisTitle,
                    _ticketAnalysisSubtitle,
                    _ticketAnalysisLegend,
                    new ScrollView { MaximumHeightRequest = 420, Content = _ticketAnalysisRows },
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#2D3E55"), Margin = new Thickness(0, 8, 0, 0) },
                    totalRow,
                    btnClose,
                }
            }
        };

        _ticketAnalysisOverlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false,
            Children = { card },
        };
        return _ticketAnalysisOverlay;
    }

    // "My Ticket" mode's data half — walks Start Draw # through Covers Draws # ascending
    // (oldest first, matching the order the draws actually land in), same fetch path
    // CheckRangeAsync uses (this page's own FetchDrawAsync, which feeds HSRecentDraws as it
    // goes), except purely read-only: no pending-win tracking, nothing gets recorded. Checks
    // HSRecentDraws' cache first per draw so a range that overlaps recent draws (or a range
    // already viewed once this session) doesn't re-hit calottery.com for numbers already on
    // hand. Returns null (having already set an explanatory status) if the two fields aren't
    // both valid numbers yet.
    async Task<List<HotSpotDrawService.DrawResult>?> LoadTicketRangeDrawsAsync()
    {
        if (!int.TryParse(_last200RangeStartEntry.Text?.Trim(), out int startDraw) || startDraw <= 0 ||
            !int.TryParse(_last200RangeEndEntry.Text?.Trim(), out int endDraw) || endDraw <= 0)
        {
            _last200StatusLabel.Text = "Enter a Start Draw # and Covers Draws # above first.";
            _last200List.ItemsSource = null;
            return null;
        }
        return await FetchDrawRangeAsync(startDraw, endDraw);
    }

    // Shared by "My Ticket" mode (LoadTicketRangeDrawsAsync, above) and the standalone Search
    // popup (RunRangeSearchAsync, below) — walks every draw # from startDraw to endDraw
    // (inclusive, auto-swapped if given backwards) and returns whichever ones actually fetched.
    async Task<List<HotSpotDrawService.DrawResult>> FetchDrawRangeAsync(int startDraw, int endDraw)
    {
        if (endDraw < startDraw) (startDraw, endDraw) = (endDraw, startDraw);

        // "How many games?" tops out at 100 (see DrawOptions) — 150 leaves headroom for a
        // fat-fingered Covers# without letting a typo turn into an open-ended fetch.
        const int MaxTicketRangeDraws = 150;
        if (endDraw - startDraw + 1 > MaxTicketRangeDraws) endDraw = startDraw + MaxTicketRangeDraws - 1;

        var draws = new List<HotSpotDrawService.DrawResult>();
        int total = endDraw - startDraw + 1;
        int done = 0;
        for (int dn = startDraw; dn <= endDraw; dn++)
        {
            done++;
            var cached = await HSRecentDraws.TryGetAsync(dn);
            if (cached is { Ok: true } c) { draws.Add(c); continue; }

            _last200StatusLabel.Text = $"Loading draw #{dn}… ({done} of {total})";
            var (ok, numbers, bullseyeNumber, drawNumber, _, drawTime) = await FetchDrawAsync(dn);
            await Task.Delay(150); // same deliberate pacing HSPast10Days/HSLast200Draws use
            // Must echo back the SAME draw # requested — same well-documented gotcha every other
            // careful call site in this file already guards against (PageDrawCoreAsync, the
            // ?query= caching in HotSpotDrawService.FetchDrawAsync): querying an unindexed/edge
            // draw# can silently fall back to an unrelated historical draw instead of failing.
            // Missing this check here meant a mismatched response got added under its OWN
            // (different) draw number — confirmed live 2026-08-14: draw #3292077 got fetched
            // correctly on its own iteration AND a second time as a wrong fallback from another
            // dn in the same range, so Ticket 1 Analysis double-counted it ($8.00 shown vs. the
            // real $5.00 on calottery.com's own site).
            if (ok && drawNumber == dn) draws.Add(new HotSpotDrawService.DrawResult(true, numbers, bullseyeNumber, drawNumber, "", drawTime));
        }
        return draws;
    }

    // The "Search" popup's handler — same busy-guard/button-disable shape LoadLast200DrawsAsync
    // uses, but completely independent of "My Ticket"/"Last 200" mode: fetches exactly the
    // draw(s) the user typed and replaces the SAME list/highlighting those modes populate.
    // Doesn't touch _last200TicketMode, the ticket's own Start#/Search# boxes, or KeyStartDraw/
    // KeyDraws — purely a lookup, same spirit as the ◀/▶ paging on the main ticket screen.
    async Task RunRangeSearchAsync(int startDraw, int endDraw, HashSet<int>? customPicks = null)
    {
        if (_last200Busy) return;
        _last200Busy = true;
        _last200AnalyzeButton.IsEnabled = false;
        _last200AnalyzeButton.Opacity = 0.5;
        _last200RefreshButton.IsEnabled = false;
        _last200RefreshButton.Opacity = 0.5;
        _last200SearchButton.IsEnabled = false;
        _last200SearchButton.Opacity = 0.5;
        _last200AnalyzeAllButton.IsEnabled = false;
        _last200AnalyzeAllButton.Opacity = 0.5;
        _last200StatusLabel.Text = "Loading…";
        try
        {
            HashSet<int> picks;
            bool bullseyeOn;
            decimal wagerSnapshot;
            int spotsSnapshot;
            if (customPicks is { Count: > 0 })
            {
                // "Use this Data" — user's explicit ask 2026-08-17: score against the numbers
                // typed into the dialog instead of the active ticket. No bullseye (this dialog
                // has no bullseye field of its own) and a nominal $1 wager, since this isn't tied
                // to any real purchased ticket — only the matches/spot count are meaningful here.
                picks = customPicks;
                bullseyeOn = false;
                wagerSnapshot = 1.0m;
                spotsSnapshot = customPicks.Count;
                _last200CustomPicks = customPicks;
                _last200CustomSpots = spotsSnapshot;
            }
            else
            {
                // The REAL saved ticket's own numbers, not _selected — see LoadSavedSlotForScoring's
                // own comment for why _selected/_bullseye/_spots/_wager can't be trusted here.
                var (picksArr, bullseye, wager, spots) = LoadSavedSlotForScoring(_activeSlot);
                picks = new HashSet<int>(picksArr);
                bullseyeOn = bullseye;
                wagerSnapshot = wager;
                spotsSnapshot = spots;
                _last200CustomPicks = null; // a normal (non-custom) Search — clear any earlier custom search's picks
            }

            var draws = await FetchDrawRangeAsync(startDraw, endDraw);
            _last200AllDraws = draws;
            _last200AllRows = await Task.Run(() => BuildLast200RowVms(draws, picks, bullseyeOn, spotsSnapshot, wagerSnapshot));
            PopulateLast200FilterOptions(spotsSnapshot);
            ApplyLast200Filter();
            if (draws.Count == 0)
                _last200StatusLabel.Text = endDraw == startDraw
                    ? $"Couldn't find draw #{startDraw}."
                    : $"No draws found in #{Math.Min(startDraw, endDraw)}–#{Math.Max(startDraw, endDraw)}.";
        }
        finally
        {
            _last200AnalyzeButton.IsEnabled = true;
            _last200AnalyzeButton.Opacity = 1.0;
            _last200RefreshButton.IsEnabled = true;
            _last200RefreshButton.Opacity = 1.0;
            _last200SearchButton.IsEnabled = true;
            _last200SearchButton.Opacity = 1.0;
            // Was missing — confirmed live 2026-08-16: this method fetches a draw range one
            // draw at a time (a 63-draw search takes well over 10 seconds), and "Analyze
            // What-if?" was never disabled during that wait. Tapping it mid-fetch ran
            // AnalyzeWhatIfListboxAsync against whatever _last200AllDraws/_last200List still
            // held from BEFORE this search (usually the ticket's own real range) — looked like
            // a wrong total, but it was actually analyzing stale data, not this search's real
            // results, which hadn't been written yet. Only re-enable here if its OWN Clicked
            // handler isn't the thing currently running (mirrors _last200AnalyzeFlowActive's
            // guard just above for the other Analyze button).
            if (!_last200AnalyzeAllBusy)
            {
                _last200AnalyzeAllButton.IsEnabled = true;
                _last200AnalyzeAllButton.Opacity = 1.0;
                RefreshAnalyzeModeToggleUi(); // reflects the just-set/cleared _last200CustomPicks in the button/hint right away
            }
            _last200Busy = false;
        }
    }

    // Pure data — no MAUI views touched here, so this is safe to run on a background thread
    // via Task.Run. Only the ~15 rows CollectionView actually realizes on screen ever get
    // turned into real views (see Last200RowView), so this list existing at 200 entries costs
    // almost nothing next to the old eager-render approach. Uses HotSpotDrawService.Score —
    // the exact same matches/win calc CheckRangeAsync and HSPast10Days use — so vm.Matches
    // and the "Match ≥ N" filter always agree with what the app would actually pay out.
    static List<Last200RowVm> BuildLast200RowVms(List<HotSpotDrawService.DrawResult> draws, HashSet<int> picks, bool bullseyeOn, int spots, decimal wager)
    {
        var picksArray = picks.ToArray();
        var result = new List<Last200RowVm>(draws.Count);
        foreach (var draw in draws)
        {
            var (matches, bullseyeHit, winAmount) = HotSpotDrawService.Score(picksArray, bullseyeOn, spots, wager, draw);
            var vm = new Last200RowVm
            {
                DrawText = $"#{draw.DrawNumber}",
                WhenText = draw.DrawTime > DateTime.MinValue ? draw.DrawTime.ToString("MMM d, h:mm tt") : "",
                BullseyeText = $"BE {draw.BullseyeNumber:00}",
                BullseyeColor = bullseyeHit ? BallBullseyeHit : BallBullseye,
                BullseyeHit = bullseyeHit,
                DrawNumber = draw.DrawNumber,
                Matches = matches,
                WinAmount = winAmount,
            };
            foreach (var n in draw.Numbers.OrderBy(x => x))
            {
                bool isMatch = picks.Contains(n);
                vm.Numbers.Add((n.ToString("00"), isMatch ? BallMatch : Color.FromArgb("#B8C4D9"), isMatch));
            }
            result.Add(vm);
        }
        return result;
    }

    // Plain data holder bound to each CollectionView item — Last200RowView reads it in
    // OnBindingContextChanged to paint that row.
    public class Last200RowVm
    {
        public string DrawText = "";
        public string WhenText = "";
        public string BullseyeText = "";
        public Color BullseyeColor = Colors.White;
        public List<(string Text, Color Color, bool Bold)> Numbers = new();
        public int DrawNumber;
        public int Matches;
        public decimal WinAmount;
        public bool BullseyeHit;
    }

    // Recycled by CollectionView's RecyclerView-backed virtualization — built once per
    // on-screen slot, then repainted (not rebuilt) via OnBindingContextChanged as new rows
    // scroll into view. This is what keeps 200 draws cheap: only ever a screenful of these
    // actually exist at once.
    class Last200RowView : ContentView
    {
        readonly Label _header;
        readonly Label _when;
        readonly Label _bullseye;
        readonly Label _numbers;
        readonly BoxView _winStripe;

        public Last200RowView()
        {
            _header   = new Label { FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#D4A94A") };
            _when     = new Label { FontSize = 11, TextColor = Color.FromArgb("#6B7A93") };
            _bullseye = new Label { FontSize = 11, FontAttributes = FontAttributes.Bold };
            _numbers  = new Label { FontSize = 12, LineBreakMode = LineBreakMode.WordWrap };
            // Left-edge stripe, gold when this draw actually paid out — lets a win jump out
            // while scrolling instead of having to count green numbers on every row.
            _winStripe = new BoxView { Color = Colors.Transparent, WidthRequest = 4 };

            Padding = new Thickness(6, 4);
            var mainStack = new VerticalStackLayout
            {
                Spacing = 2,
                Padding = new Thickness(0, 0, 0, 4),
                Children =
                {
                    new HorizontalStackLayout { Spacing = 8, Children = { _header, _when, _bullseye } },
                    _numbers,
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#2D3E55"), Margin = new Thickness(0, 6, 0, 0) },
                }
            };
            var rowGrid = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(new GridLength(4)), new ColumnDefinition(GridLength.Star) },
                Children = { _winStripe, mainStack },
            };
            Grid.SetColumn(_winStripe, 0);
            Grid.SetColumn(mainStack, 1);
            Content = rowGrid;

            // CollectionView (SelectionMode != None) sets "Selected"/"Normal" on the
            // ItemTemplate's own root element — this ContentView IS that root, so the states
            // go directly on `this` rather than some inner child. Tapping a row now visibly
            // highlights it; the 💰 button reads which one via _last200List.SelectionChanged.
            var normal = new VisualState { Name = "Normal" };
            normal.Setters.Add(new Setter { Property = BackgroundColorProperty, Value = Colors.Transparent });
            // Blue, not gold/amber — a gold selection tint would wash out the gold win stripe
            // on the same row, which is exactly why it disappeared on a selected winning draw.
            var selected = new VisualState { Name = "Selected" };
            selected.Setters.Add(new Setter { Property = BackgroundColorProperty, Value = Color.FromArgb("#331565C0") });
            var commonStates = new VisualStateGroup { Name = "CommonStates" };
            commonStates.States.Add(normal);
            commonStates.States.Add(selected);
            VisualStateManager.SetVisualStateGroups(this, new VisualStateGroupList { commonStates });
        }

        protected override void OnBindingContextChanged()
        {
            base.OnBindingContextChanged();
            if (BindingContext is not Last200RowVm vm) return;

            _header.Text = vm.DrawText;
            _when.Text = vm.WhenText;
            _bullseye.Text = vm.BullseyeText;
            _bullseye.TextColor = vm.BullseyeColor;
            _winStripe.Color = vm.WinAmount > 0 ? BallBullseyeHit : Colors.Transparent;

            var fs = new FormattedString();
            foreach (var (text, color, bold) in vm.Numbers)
                fs.Spans.Add(new Span { Text = text + "  ", TextColor = color, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None });
            _numbers.FormattedText = fs;
        }
    }

    View BuildPayoutSpotSection(int spots)
    {
        bool isCollapsed = true; // matches calottery.com's own collapsed-by-default list

        var header = new Grid
        {
            BackgroundColor = Color.FromArgb("#243447"),
            Padding = new Thickness(10, 8),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
        };
        var chevron = new Label
        {
            Text = "▶", FontSize = 12, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#90CAF9"), VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        header.Add(chevron, 0, 0);
        header.Add(new Label
        {
            Text = $"{spots} spot", FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White, VerticalOptions = LayoutOptions.Center,
        }, 1, 0);
        if (HotSpotDrawService.IsTypicalPoolSpotCount(spots))
            header.Add(new Label
            {
                Text = "typical pool", FontSize = 9, TextColor = Color.FromArgb("#8B9DC3"),
                VerticalOptions = LayoutOptions.Center,
            }, 2, 0);

        var body = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(10, 8, 4, 12), IsVisible = false };
        var colHeader = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(64)), new ColumnDefinition(new GridLength(88)) },
        };
        colHeader.Add(new Label { Text = "Match", FontSize = 10, TextColor = Color.FromArgb("#607D8B") }, 0, 0);
        colHeader.Add(new Label { Text = "Plain", FontSize = 10, TextColor = Color.FromArgb("#607D8B"), HorizontalTextAlignment = TextAlignment.End }, 1, 0);
        colHeader.Add(new Label { Text = "w/ Bulls-eye", FontSize = 10, TextColor = Color.FromArgb("#607D8B"), HorizontalTextAlignment = TextAlignment.End }, 2, 0);
        body.Children.Add(colHeader);

        var baseTiers = HotSpotDrawService.BasePrizes[spots];
        var beTiers   = HotSpotDrawService.BullseyePrizes[spots];
        foreach (int m in baseTiers.Keys.Union(beTiers.Keys).OrderByDescending(x => x))
        {
            baseTiers.TryGetValue(m, out decimal baseAmt);
            beTiers.TryGetValue(m, out decimal beAmt);
            bool isTopPool = m == spots && HotSpotDrawService.IsTypicalPoolSpotCount(spots);
            string label = $"{m} of {spots} matched" + (isTopPool ? " *" : "");

            var row = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(64)), new ColumnDefinition(new GridLength(88)) },
            };
            row.Add(new Label { Text = label, FontSize = 12, TextColor = Colors.White }, 0, 0);
            row.Add(new Label
            {
                Text = baseAmt > 0 ? $"${baseAmt:0.##}" : "—", FontSize = 12,
                TextColor = baseAmt > 0 ? Color.FromArgb("#4CAF7D") : Color.FromArgb("#546E7A"),
                HorizontalTextAlignment = TextAlignment.End,
            }, 1, 0);
            row.Add(new Label
            {
                Text = beAmt > 0 ? $"${beAmt:0.##}" : "—", FontSize = 12,
                TextColor = beAmt > 0 ? Color.FromArgb("#FFB300") : Color.FromArgb("#546E7A"),
                HorizontalTextAlignment = TextAlignment.End,
            }, 2, 0);
            body.Children.Add(row);
        }
        if (HotSpotDrawService.IsTypicalPoolSpotCount(spots))
            body.Children.Add(new Label
            {
                Text = "* typical prize pool amount — can vary/be shared, not a fixed guaranteed prize",
                FontSize = 9, TextColor = Color.FromArgb("#607D8B"), Margin = new Thickness(0, 4, 0, 0),
            });

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            isCollapsed = !isCollapsed;
            chevron.Text = isCollapsed ? "▶" : "▼";
            body.IsVisible = !isCollapsed;
        };
        header.GestureRecognizers.Add(tap);

        return new VerticalStackLayout { Children = { header, body } };
    }

    // ── Selection helpers ────────────────────────────────────────────────────

    // Quick Pick never overwrites an already-saved ticket in place — instead it hops to the
    // next empty slot and picks there, so a careless tap can't blow away real saved numbers
    // the way a plain overwrite-confirm could. Only asks to replace when every one of the 10
    // slots is already full and there's nowhere else to go.
    async void QuickPick()
    {
        // Asks up front every time, regardless of whether the current slot is locked —
        // otherwise choosing a spot count meant going through the "How many spots?" picker
        // first, which is now confirm-gated on an already-saved ticket (see ConfirmEditIfLockedAsync)
        // and got in the way of just doing a quick pick. This sidesteps that entirely.
        string[] spotChoices = SpotOptions.Select(s => s.ToString()).ToArray();
        string chosenStr = await DisplayActionSheet("How many spots?", "Cancel", null, spotChoices);
        if (string.IsNullOrEmpty(chosenStr) || chosenStr == "Cancel" || !int.TryParse(chosenStr, out int chosenSpots))
            return;

        bool hasSavedTicket = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));

        if (hasSavedTicket)
        {
            int fromSlot = _activeSlot;
            int nextEmpty = -1;
            for (int i = 1; i <= SlotCount; i++)
            {
                int candidate = (_activeSlot + i) % SlotCount;
                if (string.IsNullOrWhiteSpace(Preferences.Get(SlotKey(KeyNumbers, candidate), "")))
                { nextEmpty = candidate; break; }
            }

            if (nextEmpty >= 0)
            {
                PersistCurrentSlotRaw();
                LoadSlot(nextEmpty);
                Preferences.Set(KeyActiveSlot, _activeSlot);
                FillNewTicketDrawNumbersIfEmpty();
                _statusLabel.Text = $"Ticket {fromSlot + 1} is already saved — Quick Pick moved to empty Ticket {nextEmpty + 1}.";
                _statusLabel.TextColor = Color.FromArgb("#4CAF7D");
                _statusLabel.IsVisible = true;
            }
            else
            {
                bool replace = await DisplayAlert(
                    $"All {SlotCount} tickets are full",
                    $"Every ticket slot already has numbers saved. Replace Ticket {_activeSlot + 1}'s numbers with a new Quick Pick?",
                    "Yes, Replace It", "Cancel");
                if (!replace) return;
                _editUnlocked = true; UpdateEditMode();
            }
        }

        _spots = chosenSpots;
        _spotsPicker.SelectedIndex = Array.IndexOf(SpotOptions, _spots);

        _slotPendingWins.Remove(_activeSlot);
        ClearSelection();
        var rng = new Random();
        var pool = Enumerable.Range(1, 80).OrderBy(_ => rng.Next()).Take(_spots);
        foreach (var n in pool) { _selected.Add(n); SetBallState(n, BallState.Selected); }
        UpdateSelectedCountLabel();
        UpdatePrizeDisplay();
    }

    void ClearSelection()
    {
        // Resets every ball, not just the ones in _selected — otherwise a previous
        // Check's Drawn/Match/Bullseye colors (which aren't tracked in _selected)
        // would survive a tap of Clear.
        for (int n = 1; n <= 80; n++) SetBallState(n, BallState.Default);
        _selected.Clear();
        _pendingWins.Clear();
        // Otherwise a still-showing "N of M matched" from whatever slot was on screen before
        // this Clear/LoadSlot would sit there stale — nothing else ever resets this label, and
        // ApplyDrawResult itself no longer touches an empty, never-saved slot (see its 2026-08-20
        // early-return comment), so this is the only place left to clear it.
        _matchLabel.Text = "";
        _matchLabel.FormattedText = null; // _matchLabel is now driven via FormattedText (see the match-count-green span below) — Text alone can't override a stale FormattedText
        // Deliberately does NOT touch _slotPendingWins here — this only clears the in-memory
        // pending list for whatever slot happens to be active right now (including mid-LoadSlot,
        // where _activeSlot has already flipped to the slot being switched TO). Callers that mean
        // to actually invalidate a slot's pending win (Delete, Delete All, Quick Pick replacing a
        // ticket's numbers) remove it from _slotPendingWins themselves, right around their own
        // ClearSelection() call.
        RefreshRecordWinButton();
        UpdateSelectedCountLabel();
        UpdatePrizeDisplay();
    }

    // ── Persistence (single active ticket, no 10-slot system) ───────────────

    // One-time upgrade path from the original single-ticket design (flat "hs_numbers" etc.,
    // no slot suffix) to the 10-slot system — runs at most once per install. If a legacy
    // ticket is found, it becomes Ticket 1 (slot 0); the old flat keys are removed after.
    // Never touches anything if there's no legacy data (the common case for every session
    // after the first one following this update).
    void MigrateLegacyTicketIfNeeded()
    {
        if (!Preferences.ContainsKey(KeyNumbers)) return; // no legacy flat key at all
        if (!Preferences.ContainsKey(SlotKey(KeyNumbers, 0)))
        {
            Preferences.Set(SlotKey(KeySpots, 0), Preferences.Get(KeySpots, 4));
            Preferences.Set(SlotKey(KeyBullseye, 0), Preferences.Get(KeyBullseye, false));
            Preferences.Set(SlotKey(KeyWager, 0), Preferences.Get(KeyWager, 1.0));
            Preferences.Set(SlotKey(KeyDraws, 0), Preferences.Get(KeyDraws, 1));
            Preferences.Set(SlotKey(KeyNumbers, 0), Preferences.Get(KeyNumbers, ""));
            Preferences.Set(SlotKey(KeyPurchasedDate, 0), Preferences.Get(KeyPurchasedDate, ""));
            Preferences.Set(SlotKey(KeyStartDraw, 0), Preferences.Get(KeyStartDraw, 0));
        }
        Preferences.Remove(KeySpots);
        Preferences.Remove(KeyBullseye);
        Preferences.Remove(KeyWager);
        Preferences.Remove(KeyDraws);
        Preferences.Remove(KeyNumbers);
        Preferences.Remove(KeyPurchasedDate);
        Preferences.Remove(KeyStartDraw);
    }

    // Shared by both the main page's Ticket # stepper and the Last 200 dialog's own compact
    // ticket picker (_last200SlotPicker) — factored out 2026-08-14 so "switch ticket" behaves
    // identically no matter which of the two dropdowns triggered it, instead of the dialog's
    // copy silently drifting out of sync with whatever this one does over time.
    async Task SwitchToSlotAsync(int newSlot)
    {
        if (newSlot < 0 || newSlot >= SlotCount) return;
        // Re-picking the SAME already-active set (e.g. after ◀/▶ paging changed what's showing
        // in Start#/Search#) must still snap every field back to what's actually saved for it —
        // skip PersistCurrentSlotRaw only in this case, since persisting here would bake in the
        // paged-through values instead of discarding them.
        bool sameSlot = newSlot == _activeSlot;
        if (!sameSlot) PersistCurrentSlotRaw();
        LoadSlot(newSlot);
        Preferences.Set(KeyActiveSlot, _activeSlot);
        // The page-load auto-fill (ShowApproxCurrentDrawAsync) only ever runs once, on
        // whichever slot happened to be active at that moment — switching TO a different
        // new/empty ticket afterward never got the same convenience fill. This covers that
        // case too, using the already-fetched current draw# (no new network call needed).
        FillNewTicketDrawNumbersIfEmpty();
        // User's explicit ask: switching tickets repaints the grid with the latest draw's
        // numbers automatically, same as page load — see RefreshCurrentDrawOnGridAsync.
        _ = Logger.LogAsync($"HS DIAG: SelectedIndexChanged firing, newSlot={newSlot}");
        _springOnNextInstantFill = true; // arms the bump for exactly this one grid repaint — see field comment
        await RefreshCurrentDrawOnGridAsync();
    }

    // Loads slot N's saved fields into memory and every on-screen control, same shape as the
    // old single-ticket LoadSavedTicket() had. Sets _activeSlot FIRST — the slot Picker's own
    // SelectedIndexChanged checks against _activeSlot to no-op when it's this method (not the
    // user) moving the Picker, avoiding a redundant persist+reload loop.
    void LoadSlot(int slot)
    {
        // See _suppressPickerFocusReopenUntil's declaration — only actually needed while this
        // method is setting SelectedIndex below (the dotnet/maui#15394 trigger); the deadline
        // lapses on its own a moment after so it never suppresses a real tap made once the page
        // has settled, and a second overlapping LoadSlot just pushes the deadline out further
        // instead of racing a competing timer.
        _suppressPickerFocusReopenUntil = DateTime.UtcNow.AddMilliseconds(600);

        _activeSlot = slot;
        RefreshSlotDisplayLabel();
        _editUnlocked = false; // always re-locks on any slot load — an unlock only ever applies to the one viewing that earned it
        UpdateEditMode(); // drops Edit Mode/green header back to normal when switching away from an unlocked slot

        _spots     = Math.Clamp(Preferences.Get(SlotKey(KeySpots, slot), 4), 1, 10);
        _bullseye  = Preferences.Get(SlotKey(KeyBullseye, slot), false);
        _wager     = (decimal)Preferences.Get(SlotKey(KeyWager, slot), 1.0);
        _draws     = Preferences.Get(SlotKey(KeyDraws, slot), 1);
        _startDraw = Preferences.Get(SlotKey(KeyStartDraw, slot), 0);

        // A saved ticket's true spot count is whatever numbers were actually saved with it —
        // the separate Spots field can't be trusted alone for an already-purchased ticket.
        // Confirmed live 2026-08-10: a locked, saved 3-number ticket displayed "9 spots"
        // because Spots got changed after the Save (nothing guarded it back then) without the
        // Numbers ever changing to match. Once a ticket is purchased, always re-derive the
        // displayed spot count from its real saved numbers so a stale Spots value can never
        // show again.
        bool purchasedForSpots = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, slot), ""));
        if (purchasedForSpots)
        {
            string numbersForSpots = Preferences.Get(SlotKey(KeyNumbers, slot), "");
            int savedCount = string.IsNullOrEmpty(numbersForSpots) ? 0 : numbersForSpots.Split('|').Length;
            if (savedCount is >= 1 and <= 10) _spots = savedCount;
        }

        _spotsPicker.SelectedIndex = Array.IndexOf(SpotOptions, _spots);
        _bullseyeSwitch.IsToggled  = _bullseye;
        int wIdx = Array.IndexOf(WagerOptions, _wager);
        _wagerPicker.SelectedIndex = wIdx >= 0 ? wIdx : 0;
        int dIdx = Array.IndexOf(DrawOptions, _draws);
        _drawsPicker.SelectedIndex = dIdx >= 0 ? dIdx : 0;
        _startDrawEntry.Text = _startDraw > 0 ? _startDraw.ToString() : "";
        // "Covers Draws #" is now a real per-slot saved field (see KeyCoverDraw), same as
        // Start# — restored here, not left as a shared scratch value that bled the same number
        // across every ticket you switched to. What each ticket actually has saved (or nothing,
        // if never entered for that ticket) is what shows now, every time.
        int coverDraw = Preferences.Get(SlotKey(KeyCoverDraw, slot), 0);
        _searchDrawEntry.Text = coverDraw > 0 ? coverDraw.ToString() : "";
        _pageDrawNumber = 0; // ◀/▶ paging state doesn't carry across slots

        ClearSelection();
        string numbers = Preferences.Get(SlotKey(KeyNumbers, slot), "");
        if (!string.IsNullOrEmpty(numbers))
        {
            foreach (var tok in numbers.Split('|'))
                if (int.TryParse(tok, out int n) && n is >= 1 and <= 80 && _selected.Count < _spots)
                {
                    _selected.Add(n);
                    SetBallState(n, BallState.Selected);
                }
        }
        UpdateSelectedCountLabel();
        UpdatePrizeDisplay(); // also refreshes the Total cost line
        UpdateSlotStatusLabel();

        // Last 200 Draws highlights whichever slot's picks were active when it last loaded —
        // confirmed live 2026-08-14: switching tickets while it was open left the OLD slot's
        // picks highlighted since nothing told it to reload. _last200Overlay is null until
        // BuildLast200DrawsOverlay runs (this fires once before that, from the constructor),
        // hence the null-conditional.
        if (_last200Overlay?.IsVisible == true) _ = LoadLast200DrawsAsync();
    }

    // Tells the user, in plain words, whether the ticket they're currently looking at is
    // real (saved) or not — added after a real mix-up where a saved ticket's numbers got
    // silently blanked (see PersistCurrentSlotRaw) and the only sign anything was wrong was
    // an unexplained dollar amount elsewhere in the app. Reads the SAVED-to-disk state, not
    // the live/unsaved ball selection, since "does this slot actually have a ticket" is a
    // persistence question, not a screen-state one.
    void UpdateSlotStatusLabel()
    {
        string numbers = Preferences.Get(SlotKey(KeyNumbers, _activeSlot), "");
        bool purchased = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));
        // No-commit preview of the same "games × 4min" total UpdateTicketTimeRemainingLabel
        // shows live once actually saved — user's explicit ask 2026-08-16, so the countdown
        // math can be seen/tested against the "How many games?" picker alone, without ever
        // tapping Save on a real ticket. Flat only (no live anchor to tick from pre-Save).
        string previewSuffix = _draws > 0
            ? $" · ~{FormatHotSpotDuration(TimeSpan.FromMinutes(_draws * HotSpotDrawInterval.TotalMinutes))} total"
            : "";
        if (string.IsNullOrWhiteSpace(numbers))
        {
            // User's explicit ask 2026-08-16: drop the "— empty" wording here, the preview
            // total alone is what's useful to see.
            _slotStatusLabel.Text = previewSuffix.TrimStart(' ', '·', ' ');
            _slotStatusLabel.TextColor = Color.FromArgb("#6B7A94");
        }
        else if (!purchased)
        {
            _slotStatusLabel.Text = "— picked, not saved yet" + previewSuffix;
            _slotStatusLabel.TextColor = Color.FromArgb("#E0965A");
        }
        else
        {
            // Filled in by UpdateTicketTimeRemainingLabel just below — a saved ticket now
            // shows its own live time-remaining here instead of sitting blank (the Ticket #
            // dropdown already covers the plain "which slot is saved" info separately).
            UpdateTicketTimeRemainingLabel();
        }

        RefreshTodaySpentLabel();
    }

    // Draws land roughly every 4 minutes — the same cadence _calNextChangeAt is anchored to
    // (see the draw-change handler that sets it to Now+4:00). Kept as its own constant since
    // this needs to estimate ticket time even when calCountDown hasn't produced a live anchor.
    static readonly TimeSpan HotSpotDrawInterval = TimeSpan.FromMinutes(4);

    // User's explicit ask 2026-08-16: show how much time is left on the ACTIVE ticket —
    // "games × 4min", counting down live and re-basing itself the instant a new draw is
    // observed — in the spot UpdateSlotStatusLabel otherwise leaves blank for a saved ticket.
    // Called both from there (slot switch/Save) and every OnPollTick (the live per-second
    // tick), so it can't go stale while this ticket is on screen.
    void UpdateTicketTimeRemainingLabel()
    {
        bool purchased = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));
        if (!purchased || _startDraw <= 0 || _draws <= 0) return; // not this method's case — leave whatever UpdateSlotStatusLabel already set

        int coverDraw = _startDraw + _draws - 1;
        // Same "best known current draw#" idiom used elsewhere (e.g. CheckRangeAsync's upper
        // bound) — works whether or not calCountDown/Auto Refresh is actually running.
        int currentDraw = Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber);
        // Only _calNextChangeAt gives a real live anchor (set from an actually-observed draw
        // change); everything else here is a flat, non-ticking estimate off the 4:00 cadence.
        bool haveLiveAnchor = _calNextChangeAt != DateTime.MinValue && !InHotSpotClosedWindow();

        if (currentDraw <= 0)
        {
            // No draw# fetched at all yet this launch — flat total ticket duration, nothing to anchor a countdown to.
            _slotStatusLabel.Text = $"~{FormatHotSpotDuration(TimeSpan.FromMinutes(_draws * HotSpotDrawInterval.TotalMinutes))} total";
            _slotStatusLabel.TextColor = Color.FromArgb("#8B9DC3");
            return;
        }

        if (currentDraw < _startDraw)
        {
            int drawsUntilStart = _startDraw - currentDraw;
            TimeSpan untilStart = haveLiveAnchor
                ? (_calNextChangeAt - DateTime.Now) + TimeSpan.FromMinutes((drawsUntilStart - 1) * HotSpotDrawInterval.TotalMinutes)
                : TimeSpan.FromMinutes(drawsUntilStart * HotSpotDrawInterval.TotalMinutes);
            if (untilStart < TimeSpan.Zero) untilStart = TimeSpan.Zero;
            _slotStatusLabel.Text = $"Starts in {FormatHotSpotDuration(untilStart)}";
            _slotStatusLabel.TextColor = Color.FromArgb("#8B9DC3");
            return;
        }

        if (currentDraw > coverDraw)
        {
            _slotStatusLabel.Text = "Finished";
            _slotStatusLabel.TextColor = Color.FromArgb("#6B7A94");
            return;
        }

        int drawsLeftAfterCurrent = coverDraw - currentDraw; // not counting the draw in progress right now
        TimeSpan remaining = haveLiveAnchor
            ? (_calNextChangeAt - DateTime.Now) + TimeSpan.FromMinutes(drawsLeftAfterCurrent * HotSpotDrawInterval.TotalMinutes)
            : TimeSpan.FromMinutes((drawsLeftAfterCurrent + 1) * HotSpotDrawInterval.TotalMinutes);
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

        // User's explicit ask 2026-08-16: current draw #3292693, ticket covers up to #3292694 —
        // user counts that as "1 draw left" (694, the one still ahead), not 2. The draw
        // currently in progress is "now," not "left" — matches drawsLeftAfterCurrent as-is,
        // no +1 for the in-progress draw (an earlier version of this added one, which is what
        // produced the wrong "(2)" here).
        int gamesLeft = drawsLeftAfterCurrent;
        _slotStatusLabel.Text = (haveLiveAnchor ? "" : "~") + $"{FormatHotSpotDuration(remaining)} left ({gamesLeft})";
        _slotStatusLabel.TextColor = Color.FromArgb("#4CAF7D");
    }

    static string FormatHotSpotDuration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    // Sums drawsLeftAfterCurrent (see UpdateTicketTimeRemainingLabel) across every slot that's
    // currently mid-ticket. User's explicit ask 2026-08-19: skip a slot whose start draw hasn't
    // come up yet (nothing counting down on it yet) AND skip one that's already finished all its
    // covered draws — only slots actually in progress right now contribute. Naturally ticks down
    // by however many slots were covering the old draw# each time a new draw is observed, since
    // currentDraw only ever moves forward.
    void UpdateTotalDrawsLeftLabel()
    {
        int currentDraw = Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber);
        int total = 0;
        if (currentDraw > 0)
        {
            for (int s = 0; s < SlotCount; s++)
            {
                if (string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, s), ""))) continue;
                int startDraw = Preferences.Get(SlotKey(KeyStartDraw, s), 0);
                if (startDraw <= 0) continue;
                int draws = Preferences.Get(SlotKey(KeyDraws, s), 1);
                if (draws <= 0) continue;
                int coverDraw = startDraw + draws - 1;
                if (currentDraw < startDraw || currentDraw > coverDraw) continue; // not started yet, or already finished
                total += coverDraw - currentDraw; // not counting the draw in progress right now — same rule as gamesLeft above
            }
        }
        _totalDrawsLeftLabel.Text = $"Total Draws Left: {total}";
        _lastKnownTotalDrawsLeft = total;
        RenderTodaySpentLabel();
    }

    // Tap-to-detail for _totalDrawsLeftLabel — user's explicit ask 2026-08-19: show the same
    // per-slot numbers UpdateTotalDrawsLeftLabel is adding together, one line per saved ticket,
    // so the total isn't a black box. Lists every purchased slot with a Start Draw #, not just
    // the in-progress ones the total itself counts — a not-started-yet or already-finished
    // ticket still gets a line, explaining why it isn't contributing to the total above it.
    //
    // Reuses _ticketAnalysisOverlay/BuildTicketAnalysisRow (same dark-card table popup as
    // "Analyze Ticket"/"Analyze All Tickets") instead of a plain DisplayAlert — user's explicit
    // ask 2026-08-19 for a real third "how much has this ticket won" column, which a system
    // alert's flat string can't render as actual aligned columns.
    //
    // The "won" column reads winnings_log.json (via SummaryPage.LoadAllAsync — local file, no
    // network) rather than re-fetching/rescoring each ticket's draw range live the way "Analyze
    // All Tickets" does — this popup has always been an instant local summary, and doing a live
    // fetch per saved slot here would make a quick tap-to-detail noticeably slow. A win's
    // SourceKey ("HS_{drawNumber}_{sorted numbers joined by '-'}", see RecordPendingWinAsync) is
    // matched back to a slot by draw # falling inside that slot's covered range AND the same
    // sorted picks — same identity a win was recorded under in the first place.
    async Task ShowDrawsLeftByTicketAsync()
    {
        int currentDraw = Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber);
        var hsWins = (await SummaryPage.LoadAllAsync()).Where(r => r.Game == "HS").ToList();

        _ticketAnalysisRows.Children.Clear();
        decimal grandWon = 0m;
        int ticketCount = 0;
        int grandBullseyeHits = 0;

        for (int s = 0; s < SlotCount; s++)
        {
            if (string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, s), ""))) continue;
            int startDraw = Preferences.Get(SlotKey(KeyStartDraw, s), 0);
            if (startDraw <= 0) continue;
            int draws = Preferences.Get(SlotKey(KeyDraws, s), 1);
            if (draws <= 0) continue;
            int coverDraw = startDraw + draws - 1;

            string statusText;
            if (currentDraw <= 0) statusText = "current draw # unknown yet";
            else if (currentDraw < startDraw) statusText = "not started yet";
            else if (currentDraw > coverDraw) statusText = "finished";
            else statusText = $"{coverDraw - currentDraw} draws left";

            string numbersRaw = Preferences.Get(SlotKey(KeyNumbers, s), "");
            var picks = numbersRaw.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => int.TryParse(t, out int n) ? n : -1).Where(n => n is >= 1 and <= 80).ToArray();
            string picksKey = string.Join("-", picks.OrderBy(n => n));

            // Parse each matching record's draw # once, up front — needed for both the total
            // and (below) the individual Bulls-eye sub-rows, so this isn't recomputed twice.
            var slotWins = hsWins
                .Select(w =>
                {
                    var parts = w.SourceKey.Split('_', 3);
                    return (Record: w, Parts: parts, Ok: parts.Length == 3 && int.TryParse(parts[1], out int wDraw) && wDraw >= startDraw && wDraw <= coverDraw && parts[2] == picksKey, Draw: parts.Length == 3 && int.TryParse(parts[1], out int d) ? d : 0);
                })
                .Where(x => x.Ok)
                .OrderBy(x => x.Draw)
                .ToList();

            decimal won = slotWins.Sum(x => x.Record.Amount);
            grandWon += won;
            ticketCount++;

            _ticketAnalysisRows.Children.Add(BuildTicketAnalysisRow($"Ticket {s + 1}", statusText, won, spotsText: $"{picks.Length}-Spot"));

            // Bulls-eye hits within this ticket — user's explicit ask 2026-08-19: call out which
            // specific draws hit the Bulls-eye (in red, matching BallBullseye's color everywhere
            // else on the page), indented under the ticket's own row, one line per hit so
            // multiple hits are simply multiple visible lines — no separate count needed, seeing
            // 2 or 3 red lines under one ticket already says "this happened more than once."
            // Note.Contains("(Bulls-eye)") only exists on records written after this same-day
            // change (RecordPendingWinAsync) — older already-recorded wins won't retroactively
            // show up here even if they were real Bulls-eye hits.
            foreach (var hit in slotWins.Where(x => x.Record.Note.Contains("(Bulls-eye)")))
            {
                grandBullseyeHits++;
                _ticketAnalysisRows.Children.Add(BuildBullseyeHitRow(s + 1, hit.Draw, hit.Record.Amount));
            }
        }

        if (ticketCount == 0)
        {
            await DisplayAlert("Draws Left by Ticket", "No saved tickets with a Start Draw # yet.", "OK");
            return;
        }

        _ticketAnalysisTitle.Text = "Draws Left by Ticket";
        _ticketAnalysisSubtitle.Text = $"{ticketCount} ticket{(ticketCount == 1 ? "" : "s")}"
            + (grandBullseyeHits > 0 ? $" — {grandBullseyeHits} Bulls-eye hit{(grandBullseyeHits == 1 ? "" : "s")}" : "")
            + " — Won column reads already-recorded wins only.";
        // Shortened to fit one line — user's explicit ask 2026-08-19 — the "only wins recorded
        // from today onward are tracked" caveat still applies (see the field comment/summary
        // above) but no longer needs to be spelled out on screen every time.
        _ticketAnalysisLegend.Text = "🔴 Red = Bulls-eye hit";
        _ticketAnalysisLegend.IsVisible = true;
        _ticketAnalysisTotal.Text = $"${grandWon:N2}";
        _ticketAnalysisOverlay.IsVisible = true;
    }

    // Mirrors the Spending Log's own HS row so "how much have I spent today" is visible on
    // this page directly, without navigating away — see SpendingTracker.SumHotSpotCostToday
    // for why this can't just reuse the generic per-game TicketCost() total. Called from the
    // same places RefreshSlotPickerItems is, since both change whenever a slot's saved state
    // does (Save, Delete, Delete All, slot switch/load).
    // User's explicit ask 2026-08-16: also show today's total Hot Spot WINS (across every
    // slot, not just whichever ticket is on screen) right next to the spend, e.g.
    // "Today: 2 tickets · $60.00 - $14.00" — fire-and-forget async since the win total comes
    // from winnings_log.json (SummaryPage.LoadAllAsync, file I/O) but every caller of this
    // method is itself synchronous (LoadSlot/Save/Delete/slot switch); the label just updates
    // a beat later once the load completes, same pattern as this file's other _ = ...Async()
    // fire-and-forget UI refreshes.
    void RefreshTodaySpentLabel()
    {
        int count = SpendingTracker.CountHotSpotTicketsToday();
        decimal total = SpendingTracker.SumHotSpotCostToday();
        _lastKnownHotSpotCountToday = count;
        _lastKnownHotSpotSpendToday = total;
        // Renders with the last known win total immediately (not blank) — confirmed live
        // 2026-08-18: this method is called from many places in quick succession within one
        // auto-refresh tick (ApplyDrawResult, AutoCheckAndRecordAllSlotsAsync, LoadSlot, etc.),
        // and used to synchronously reset the text to the plain "$X.XX" (no win subtraction)
        // every single call, only re-appending "- $wins" a moment later once
        // AppendTodayHotSpotWinsAsync's file read finished. With several calls firing per tick,
        // the label visibly flickered blank/full/blank/full on every one of them. Reusing
        // _lastKnownHotSpotWinsToday here means a call that lands before the fresh async result
        // is back still shows the right total, not a temporary wrong one.
        RenderTodaySpentLabel();
        // Those overlapping reads of winnings_log.json/_slotPendingWins can finish in ANY
        // order, so an older call (started before a win was actually recorded) could resolve
        // AFTER a newer one and stomp the correct "$20.00 - $1.00" text back down to plain
        // "$20.00" — confirmed live 2026-08-17, real win safely recorded in winnings_log.json
        // the whole time, just never showing here. Tagging each call with a ticket and only
        // letting the LATEST one actually win the write makes this immune to completion order.
        // Always re-fetch, even at count==0 (e.g. right after Clear All) — wins recorded today
        // are day-based, not tied to whichever tickets are currently on screen, so a stale
        // _lastKnownHotSpotWinsToday from before a Clear must still get refreshed to the real
        // current total rather than just carried over as-is — user's explicit ask 2026-08-20.
        int mySeq = ++_todaySpentLabelRequestSeq;
        _ = AppendTodayHotSpotWinsAsync(count, total, mySeq);
    }

    // Draws _todaySpentLabel from whatever was last computed (_lastKnownHotSpotCountToday/
    // SpendToday/WinsToday) — never recomputes anything itself, so both RefreshTodaySpentLabel
    // and the tap handler can call this freely. _showNetToday swaps the usual "N tickets ·
    // $spend - $wins" breakdown for a single "Net: $X.XX" (wins minus spend), colored green
    // for break-even-or-better and red for a net loss, same red/green already used elsewhere on
    // this page (BallBullseye / #4CAF7D). Also forces the Net form once _lastKnownTotalDrawsLeft
    // hits 0 — nothing left in progress today, so Net is the more useful number regardless of
    // the manual tap toggle — user's explicit ask 2026-08-20.
    void RenderTodaySpentLabel()
    {
        int count = _lastKnownHotSpotCountToday;
        decimal total = _lastKnownHotSpotSpendToday;
        decimal wins = _lastKnownHotSpotWinsToday;
        if (_showNetToday || _lastKnownTotalDrawsLeft == 0)
        {
            decimal net = wins - total;
            _todaySpentLabel.Text = $"Net: {(net >= 0 ? "$" : "-$")}{Math.Abs(net):0.00}";
            _todaySpentLabel.TextColor = net >= 0 ? Color.FromArgb("#4CAF7D") : Color.FromArgb("#C62828");
            return;
        }
        _todaySpentLabel.TextColor = Color.FromArgb("#8B9DC3");
        // Wins already recorded today must keep showing even at 0 tickets (e.g. right after
        // Clear All) — user's explicit ask 2026-08-20: previously this branch collapsed straight
        // to "Today: $0.00" whenever count==0, hiding a real recorded win total until the next
        // ticket was added back, which looked like Clear had wiped the day's wins too.
        _todaySpentLabel.Text = count > 0
            ? (wins > 0
                ? $"Today: {count} ticket{(count == 1 ? "" : "s")} · ${total:0.00} - ${wins:0.00}"
                : $"Today: {count} ticket{(count == 1 ? "" : "s")} · ${total:0.00}")
            : (wins > 0 ? $"Today: $0.00 - ${wins:0.00}" : "Today: $0.00");
    }

    async Task AppendTodayHotSpotWinsAsync(int count, decimal total, int requestSeq)
    {
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        var records = await SummaryPage.LoadAllAsync();
        decimal recordedWins = records.Where(r => r.Game == "HS" && r.Date == today).Sum(r => r.Amount);
        // Also count wins already found live but not yet recorded (RefreshRecordWinButton's own
        // _slotPendingWins, across every slot) — user's explicit ask 2026-08-16: this total must
        // be right the instant a win is found, without requiring a Record Win tap first. Safe to
        // add straight on top of recordedWins: a slot's entry here is removed the moment it's
        // actually recorded (RecordPendingWinAsync) or found to already be recorded (CheckRangeAsync's
        // IsNew filter / ApplyDrawResult's alreadyRecorded check), so nothing here can double-count
        // something winnings_log.json already has.
        decimal pendingWins = _slotPendingWins.Values.Sum(v => v.Total);
        decimal wins = recordedWins + pendingWins;
        // A newer request already landed while this one was awaiting file I/O — that one reflects
        // more current state, so let it stand rather than potentially overwriting it with ours.
        if (requestSeq != _todaySpentLabelRequestSeq) return;
        _lastKnownHotSpotWinsToday = wins; // keeps RefreshTodaySpentLabel's placeholder current either way, including a real drop back to 0
        if (wins <= 0) return;
        RenderTodaySpentLabel();
    }

    // Refreshes just the always-visible "closed" display for the CURRENTLY active slot —
    // cheap, safe to call from anywhere a slot's saved state (or _activeSlot itself) might have
    // changed. The full 12-row popup (_ticketSlotOverlay) is rebuilt separately, lazily, only
    // when it's actually opened — see RefreshTicketSlotOverlayRows/ShowTicketSlotOverlay.
    void RefreshSlotDisplayLabel()
    {
        int currentDraw = Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber);
        string numbers = Preferences.Get(SlotKey(KeyNumbers, _activeSlot), "");
        bool purchased = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));
        _slotDisplayLabel.Text = purchased && !string.IsNullOrWhiteSpace(numbers)
            ? $"✓ {_activeSlot + 1} - ({numbers.Split('|').Length} spots)"
            : $"○ {_activeSlot + 1}";
        bool finished = IsSlotFullyFinished(_activeSlot, currentDraw);
        _slotDisplayLabel.TextColor = finished ? Colors.Red : Colors.White;
        _slotDisplayLabel.TextDecorations = finished ? TextDecorations.Strikethrough : TextDecorations.None;
    }

    void ShowTicketSlotOverlay()
    {
        RefreshTicketSlotOverlayRows();
        _ticketSlotOverlay.IsVisible = true;
    }

    // Rebuilt every time the popup opens (not just once), same reasoning as
    // RefreshOptionsMenuRows — so a slot finishing or being saved/deleted elsewhere is always
    // reflected the next time this list is actually looked at, without needing to eagerly keep
    // 12 invisible rows in sync in the background.
    void RefreshTicketSlotOverlayRows()
    {
        _ticketSlotRowsContainer.Children.Clear();
        int currentDraw = Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber);
        for (int s = 0; s < SlotCount; s++)
        {
            string numbers = Preferences.Get(SlotKey(KeyNumbers, s), "");
            bool purchased = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, s), ""));
            string label = purchased && !string.IsNullOrWhiteSpace(numbers)
                ? $"✓ {s + 1} - ({numbers.Split('|').Length} spots)"
                : $"○ {s + 1}";
            bool finished = IsSlotFullyFinished(s, currentDraw);
            _ticketSlotRowsContainer.Children.Add(BuildTicketSlotRow(s, label, finished, selected: s == _activeSlot));
        }
    }

    // Finished tickets show real red strikethrough text — the whole reason this became a custom
    // popup instead of staying a native Picker (which can only ever show plain, uncolored
    // strings). The currently active slot is highlighted blue/bold so it still reads like a
    // selection, same purpose the native Picker's filled radio button served.
    View BuildTicketSlotRow(int slot, string label, bool finished, bool selected)
    {
        var textLabel = new Label
        {
            Text = label, FontSize = 15,
            TextColor = finished ? Colors.Red : (selected ? Color.FromArgb("#4FC3F7") : Colors.White),
            TextDecorations = finished ? TextDecorations.Strikethrough : TextDecorations.None,
            FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None,
            VerticalOptions = LayoutOptions.Center,
        };
        var row = new Grid { Padding = new Thickness(4, 12) };
        row.Children.Add(textLabel);
        row.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await CloseTicketSlotOverlayAsync(slot))
        });
        return row;
    }

    // Mirrors the old native Picker's Unfocused workaround (see the git history around
    // 2026-08-10 for that comment) — fires on EVERY close, whether a different slot was tapped,
    // the same already-active slot was re-tapped, or Cancel was tapped, so any ◀/▶-paged field
    // values that were never persisted always snap back to the now-active slot's real saved
    // state. Harmless/redundant when the slot actually changed (SwitchToSlotAsync already
    // reloaded it) — just reloads the same now-current slot a second time.
    async Task CloseTicketSlotOverlayAsync(int? newSlot)
    {
        _ticketSlotOverlay.IsVisible = false;
        if (newSlot.HasValue && newSlot.Value != _activeSlot)
        {
            // A real slot switch — SwitchToSlotAsync already does everything the block below
            // does (persist, load, refresh grid). Running both back-to-back is what caused the
            // ball grid to visibly fill/clear/fill again on every tap (confirmed live
            // 2026-08-20) — the redundant "harmless" second pass wasn't actually harmless once
            // it ran synchronously right after the first, instead of after a native dropdown's
            // own close animation like the old Unfocused handler had.
            await SwitchToSlotAsync(newSlot.Value);
            return;
        }
        // Re-tapped the already-active slot, or Cancel — snap any ◀/▶-paged-but-unsaved field
        // values back to the real saved state (mirrors the old native Picker's Unfocused fix).
        PersistCurrentSlotRaw();
        LoadSlot(_activeSlot);
        FillNewTicketDrawNumbersIfEmpty();
        await RefreshCurrentDrawOnGridAsync();
    }

    Grid BuildTicketSlotOverlay()
    {
        _ticketSlotRowsContainer = new VerticalStackLayout { Spacing = 2 };

        var cancelLabel = new Label
        {
            Text = "Cancel", FontSize = 14, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#FFD54F"),
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(0, 12, 0, 0),
        };
        cancelLabel.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await CloseTicketSlotOverlayAsync(null))
        });

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(18, 14),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 320,
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label { Text = "Ticket", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                    _ticketSlotRowsContainer,
                    cancelLabel,
                }
            }
        };

        _ticketSlotOverlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false,
            Children = { card },
        };
        return _ticketSlotOverlay;
    }

    // Same ✓/○ + "- (N spots)" phrasing as the main "Ticket #" popup's rows — user's explicit
    // ask to match that dialog exactly, just with a "T" prefix so it still reads as this
    // dialog's own ticket picker rather than the main page's. `strike: false` skips the
    // Unicode-combining-character workaround for callers that apply real color/TextDecorations
    // instead (see BuildLast200TicketRow) — StrikeThrough is only for contexts stuck rendering
    // plain, uncolored strings.
    static string CompactSlotLabel(int slot, int currentDraw, bool strike = true)
    {
        string numbers = Preferences.Get(SlotKey(KeyNumbers, slot), "");
        string label = IsSlotSaved(slot)
            ? $"✓ T{slot + 1} - ({numbers.Split('|').Length} spots)"
            : $"○ T{slot + 1}";
        return strike && IsSlotFullyFinished(slot, currentDraw) ? StrikeThrough(label) : label;
    }

    static bool IsSlotSaved(int slot)
    {
        string numbers = Preferences.Get(SlotKey(KeyNumbers, slot), "");
        bool purchased = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, slot), ""));
        return purchased && !string.IsNullOrWhiteSpace(numbers);
    }

    // True once a saved slot's LAST covered draw has actually completed — same "is this ticket
    // done" formula CheckFinishedTicketsAsync uses (startDraw + draws - 1 <= currentDraw), just
    // without that method's KeyReviewed gate, since this is a pure display check and must still
    // read "finished" for a ticket that's already been reviewed/recorded. User's explicit ask:
    // strike through a Ticket # picker row once its last draw is in, un-strike the moment it's
    // edited/restarted/new (both handled for free — RefreshSlotPickerItems only rebuilds after
    // Save/Delete, and Delete clears KeyPurchasedDate so IsSlotSaved goes false first).
    // Polled from OnPollTick (throttled to FinishedSlotsCheckInterval) so a ticket gets struck
    // through the moment its last draw completes, even while the user is just sitting on the
    // page watching it — user's explicit ask, not willing to wait for the next Save/Delete or
    // app reopen. Only rebuilds the picker when the actual set of finished slots changed since
    // the last check, so a normal tick where nothing finished costs 10 cheap Preferences reads
    // and nothing else.
    void CheckForNewlyFinishedSlots()
    {
        if (DateTime.Now - _lastFinishedSlotsCheckAt < FinishedSlotsCheckInterval) return;
        _lastFinishedSlotsCheckAt = DateTime.Now;

        int currentDraw = Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber);
        if (currentDraw <= 0) return; // don't know the current draw yet

        var nowFinished = new HashSet<int>();
        for (int s = 0; s < SlotCount; s++)
            if (IsSlotFullyFinished(s, currentDraw)) nowFinished.Add(s);

        if (!nowFinished.SetEquals(_lastKnownFinishedSlots))
        {
            _lastKnownFinishedSlots = nowFinished;
            RefreshSlotDisplayLabel();
        }
    }

    static bool IsSlotFullyFinished(int slot, int currentDraw)
    {
        if (currentDraw <= 0) return false; // don't know the current draw yet
        if (!IsSlotSaved(slot)) return false;
        int startDraw = Preferences.Get(SlotKey(KeyStartDraw, slot), 0);
        if (startDraw <= 0) return false;
        int draws = Preferences.Get(SlotKey(KeyDraws, slot), 1);
        return startDraw + draws - 1 <= currentDraw;
    }

    // Overlays a combining "long stroke" mark (U+0336) after every character so the text reads
    // as struck-through even inside a native platform Picker dialog, which only ever renders
    // plain strings (no per-item Spannable/rich-text hook — same limitation noted for
    // DisplayActionSheet elsewhere in this file). Leaves the visible characters themselves
    // untouched, per user's explicit ask — same label, just struck.
    static string StrikeThrough(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length * 2);
        foreach (char c in text) { sb.Append(c); sb.Append('̶'); }
        return sb.ToString();
    }

    // Refreshes just the always-visible compact "closed" trigger for the CURRENTLY active
    // slot — cheap, called wherever a slot's saved state or _activeSlot itself might have
    // changed. The full popup (_last200TicketOverlay) is rebuilt separately, lazily, only when
    // it's actually opened — see RefreshLast200TicketOverlayRows/ShowLast200TicketOverlay.
    void RefreshLast200TicketDisplay()
    {
        int currentDraw = Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber);
        _last200TicketDisplay.Text = $"T{_activeSlot + 1}";
        bool finished = IsSlotFullyFinished(_activeSlot, currentDraw);
        _last200TicketDisplay.TextColor = finished ? Colors.Red : Colors.White;
        _last200TicketDisplay.TextDecorations = finished ? TextDecorations.Strikethrough : TextDecorations.None;
    }

    void ShowLast200TicketOverlay()
    {
        RefreshLast200TicketOverlayRows();
        _last200TicketOverlay.IsVisible = true;
    }

    // Saved tickets listed first, empty ones after — user's explicit ask (2026-08-14), kept from
    // the native-Picker version. Recomputed fresh every open rather than kept as a stored field,
    // since nothing else needs the ordering outside this one rebuild.
    void RefreshLast200TicketOverlayRows()
    {
        _last200TicketRowsContainer.Children.Clear();
        int currentDraw = Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber);
        var order = Enumerable.Range(0, SlotCount).OrderByDescending(IsSlotSaved);
        foreach (int slot in order)
        {
            string label = CompactSlotLabel(slot, currentDraw, strike: false); // color+decoration does the striking now
            bool finished = IsSlotFullyFinished(slot, currentDraw);
            _last200TicketRowsContainer.Children.Add(BuildLast200TicketRow(slot, label, finished, selected: slot == _activeSlot));
        }
    }

    View BuildLast200TicketRow(int slot, string label, bool finished, bool selected)
    {
        var textLabel = new Label
        {
            Text = label, FontSize = 15,
            TextColor = finished ? Colors.Red : (selected ? Color.FromArgb("#4FC3F7") : Colors.White),
            TextDecorations = finished ? TextDecorations.Strikethrough : TextDecorations.None,
            FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None,
            VerticalOptions = LayoutOptions.Center,
        };
        var row = new Grid { Padding = new Thickness(4, 12) };
        row.Children.Add(textLabel);
        row.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await CloseLast200TicketOverlayAsync(slot))
        });
        return row;
    }

    async Task CloseLast200TicketOverlayAsync(int? newSlot)
    {
        _last200TicketOverlay.IsVisible = false;
        if (newSlot.HasValue && newSlot.Value != _activeSlot)
        {
            await SwitchToSlotAsync(newSlot.Value);
            // Re-run (not just LoadLast200DrawsAsync) so "My Ticket" mode's Start Draw #/Covers
            // Draws # fields re-prefill from the newly-active ticket instead of showing the
            // previous ticket's range — SetLast200Mode is what actually does that prefill.
            SetLast200Mode(_last200TicketMode);
            await LoadLast200DrawsAsync();
        }
    }

    Grid BuildLast200TicketOverlay()
    {
        _last200TicketRowsContainer = new VerticalStackLayout { Spacing = 2 };

        var cancelLabel = new Label
        {
            Text = "Cancel", FontSize = 14, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#FFD54F"),
            HorizontalOptions = LayoutOptions.Center,
            Padding = new Thickness(0, 12, 0, 0),
        };
        cancelLabel.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await CloseLast200TicketOverlayAsync(null))
        });

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(18, 14),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 320,
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label { Text = "Ticket", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                    _last200TicketRowsContainer,
                    cancelLabel,
                }
            }
        };

        _last200TicketOverlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false,
            Children = { card },
        };
        return _last200TicketOverlay;
    }

    // Silently persists the current slot's raw fields (spots/bullseye/wager/draws/numbers/
    // start#) WITHOUT touching PurchasedDate or triggering TicketLogService/SpendingTracker —
    // called on slot switch and page exit so an in-progress NEW ticket's picks survive
    // navigating away. For an already-saved ticket this is a deliberate no-op: every one of
    // its real fields (numbers, spots, wager, draws, bullseye, start#) only ever changes via
    // an explicit Save tap (SaveTicketAsync) — confirmed live that unlocking a saved ticket
    // ("Yes, Change It"), tweaking it, then switching away/leaving the page WITHOUT tapping
    // Save must leave the saved ticket exactly as it was, not silently bake in the edit.
    void PersistCurrentSlotRaw()
    {
        // Viewing only (test): never write picks/start-draw into a real slot — that's exactly
        // what would let HotSpotChecker's background auto win-checker (which only looks at slot
        // storage, not whether Save was ever tapped) silently record a test entry as a real win
        // hours later. _startDraw itself still updates in-memory so CheckRangeAsync keeps working.
        if (_viewingOnly)
        {
            _startDraw = int.TryParse(_startDrawEntry.Text?.Trim(), out int svd) && svd > 0 ? svd : 0;
            return;
        }

        _startDraw = int.TryParse(_startDrawEntry.Text?.Trim(), out int sd) && sd > 0 ? sd : 0;

        bool alreadyPurchased = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));
        if (alreadyPurchased) return; // real ticket — only SaveTicketAsync may change its stored fields

        Preferences.Set(SlotKey(KeySpots, _activeSlot), _spots);
        Preferences.Set(SlotKey(KeyBullseye, _activeSlot), _bullseye);
        Preferences.Set(SlotKey(KeyWager, _activeSlot), (double)_wager);
        Preferences.Set(SlotKey(KeyDraws, _activeSlot), _draws);
        Preferences.Set(SlotKey(KeyNumbers, _activeSlot), string.Join("|", _selected.OrderBy(n => n)));
        Preferences.Set(SlotKey(KeyStartDraw, _activeSlot), _startDraw);

        // Literally whatever is in the Search# box right now — not recomputed from Start# +
        // Draws. User's explicit ask: what they type in that box is what comes back when they
        // switch back to this slot, not a value the app silently recalculates over it.
        int coverDraw = int.TryParse(_searchDrawEntry.Text?.Trim(), out int pcd) && pcd > 0 ? pcd : 0;
        Preferences.Set(SlotKey(KeyCoverDraw, _activeSlot), coverDraw);
    }

    // Replay Numbers row tapped — see Replay_Numbers.cs. Always lands back in the SAME Ticket #
    // slot it was replayed from (user's explicit ask: all 12 slots can be full at once, so an
    // ended ticket's own slot — free by definition, its game is over — is the only target
    // that's always available). `edited`/`startDraw` reflect whatever the popup's own editable
    // controls were left at (Games/Spots/Bulls-eye/Wager/numbers/Start# all editable right there
    // — user's explicit ask: leaving the popup to fix something on the main page "defeats the
    // purpose" of a fast pre-draw tool). Already validated (start > current draw, full spot
    // count) by the popup itself before this ever fires. This method's only job is the same
    // slot-switch sequence QuickPick already uses (PersistCurrentSlotRaw the slot being left,
    // then LoadSlot the target) so the freshly-written data actually renders on screen.
    void HandleReplayNumbersTapped(Replay_Numbers.TicketEntry edited, int startDraw)
    {
        if (_activeSlot != edited.Slot) PersistCurrentSlotRaw();
        Replay_Numbers.ReplayInto(edited, startDraw);
        _replayNumbersOverlay.IsVisible = false;

        // Same slot-identity reset DeleteTicketAsync/QuickPick already do before reusing a slot —
        // the old (ended) ticket's pending-win bookkeeping must not carry over onto the new one.
        _slotPendingWins.Remove(edited.Slot);

        LoadSlot(edited.Slot); // also refreshes the slot label, selected-count, prize, and status displays
        Preferences.Set(KeyActiveSlot, _activeSlot);
        HotSpotMyNumbersPanel.Refresh(_myNumbersPanel);

        _statusLabel.TextColor = Color.FromArgb("#4CAF7D");
        _statusLabel.Text = $"Ticket {edited.Slot + 1} reloaded for the next draw — Start# {startDraw}. ⚠️ Remember to tap Save!";
        _statusLabel.IsVisible = true;
    }

    // Finds the next empty ticket slot (starting from the active slot, wrapping around,
    // skipping anything already in `excludeSlots` — used so several favorites played in one
    // batch never pick the same slot twice). When nothing's empty, asks the user directly
    // instead of just failing — user's explicit ask 2026-08-22 ("if all 12 slot are filled or
    // save, ask to overwrite a slot or delete one"). Returns null if the user cancels out of
    // that prompt entirely. "Overwrite" hands back the chosen slot as-is (caller is responsible
    // for actually confirming/clearing it — see HandlePlayFavoritesTapped); "Delete" actually
    // deletes the chosen ticket via DeleteOneTicketAsync (same real cleanup Options → Delete
    // uses) and hands back that now-empty slot.
    async Task<int?> ResolveTargetSlotForFavoriteAsync(HashSet<int> excludeSlots)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            int candidate = (_activeSlot + i) % SlotCount;
            if (excludeSlots.Contains(candidate)) continue;
            if (string.IsNullOrWhiteSpace(Preferences.Get(SlotKey(KeyNumbers, candidate), "")))
                return candidate;
        }

        const string overwriteOpt = "Overwrite a Ticket";
        const string deleteOpt = "Delete a Ticket First";
        string action = await DisplayActionSheet("All 12 Ticket Slots Are Full", "Cancel", null, overwriteOpt, deleteOpt);
        if (action != overwriteOpt && action != deleteOpt) return null; // Cancel or dismissed

        string[] choices = Enumerable.Range(0, SlotCount)
            .Where(s => !excludeSlots.Contains(s))
            .Select(s => $"Ticket {s + 1}").ToArray();
        string chosen = await DisplayActionSheet(
            action == overwriteOpt ? "Overwrite Which Ticket?" : "Delete Which Ticket?",
            "Cancel", null, choices);
        if (string.IsNullOrEmpty(chosen) || chosen == "Cancel") return null;
        int slot = int.Parse(chosen.Replace("Ticket ", "")) - 1;

        if (action == deleteOpt) await DeleteOneTicketAsync(slot); // frees it up; caller writes the favorite's numbers into it next
        return slot;
    }

    // "Play Selected" tapped in the ⭐ Favorites overlay — see HotSpotFavorites.cs. Every
    // favorite (whether just one or several at once) goes through the exact same "find the next
    // empty ticket slot" search — user's explicit ask 2026-08-22, replacing the old rule where a
    // single favorite loaded straight onto whatever ticket happened to be on screen. Writes
    // straight to that slot's Preferences (same as QuickPick/Replay do) rather than the on-screen
    // grid, then reloads the last slot touched so it's visible. Still just picks the numbers —
    // Save must be tapped separately per ticket, same as every other selection path here.
    async Task HandlePlayFavoritesTapped(List<HotSpotFavorites.FavoriteEntry> favorites)
    {
        if (favorites.Count == 0) return;

        PersistCurrentSlotRaw(); // save whatever's in-flight on the current slot before hopping away from it
        var usedSlots = new HashSet<int>();
        var placedSlots = new List<int>();
        foreach (var fav in favorites)
        {
            int? target = await ResolveTargetSlotForFavoriteAsync(usedSlots);
            if (target == null) break; // user cancelled the "all full" prompt — stop, report whatever's placed so far
            int slot = target.Value;

            bool alreadyPurchased = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, slot), ""));
            if (alreadyPurchased)
            {
                bool confirmed = await DisplayAlert("Overwrite Saved Ticket?",
                    $"Ticket {slot + 1} already has a saved ticket on it. Overwrite it with \"{fav.Name}\"?",
                    "Yes, Overwrite", "Cancel");
                if (!confirmed) continue; // this favorite skipped, move on to the next one
                await DeleteOneTicketAsync(slot); // real cleanup (Ticket Log row, etc.) before writing over it below
            }

            usedSlots.Add(slot);
            _slotPendingWins.Remove(slot);
            Preferences.Set(SlotKey(KeySpots, slot), Math.Clamp(fav.Spots, 1, 10));
            Preferences.Set(SlotKey(KeyNumbers, slot), string.Join("|", fav.Numbers.OrderBy(n => n)));
            placedSlots.Add(slot);
        }

        if (placedSlots.Count == 0) return; // nothing placed — no reload/alert needed

        // Walk-through queue for 2+ favorites — see _favoritesPlayQueue's own comment.  A single
        // favorite doesn't need chaining (there's nothing to advance to), so it's left null and
        // behaves exactly as before.
        _favoritesPlayQueue = placedSlots.Count > 1 ? new List<int>(placedSlots) : null;

        LoadSlot(placedSlots[0]); // land on the FIRST one, not the last — that's what the queue above walks forward from
        Preferences.Set(KeyActiveSlot, _activeSlot);
        HotSpotMyNumbersPanel.Refresh(_myNumbersPanel);

        bool allPlaced = placedSlots.Count == favorites.Count;
        string slotList = string.Join(", ", placedSlots.Select(s => $"Ticket {s + 1}"));
        _statusLabel.TextColor = allPlaced ? Color.FromArgb("#4CAF7D") : Color.FromArgb("#E0965A");
        _statusLabel.Text = allPlaced
            ? $"Placed {placedSlots.Count} favorite ticket(s) — review and tap Save on each."
            : $"Placed {placedSlots.Count} of {favorites.Count} favorite(s) — the rest were skipped or cancelled.";
        _statusLabel.IsVisible = true;

        // Modal, not just the status line — this is picking numbers, not yet a real ticket (same
        // as any other pick on this page), and a missed status label is how a favorite gets
        // "played" but never actually purchased. User's explicit ask 2026-08-22.
        string alertBody = _favoritesPlayQueue != null
            ? $"{placedSlots.Count} favorite(s) are sitting on {slotList} now — NONE of them are saved yet. You're viewing Ticket {placedSlots[0] + 1} first; tap Save to buy it and it'll automatically move you to the next one, and so on until all {placedSlots.Count} are saved."
            : $"{placedSlots.Count} favorite(s) are sitting on {slotList} now — NONE of them are saved yet. Go to the main Hot Spot page and tap Save on each one to actually buy them.";
        await DisplayAlert("Numbers Loaded", alertBody, "Got it");
    }

    async Task SaveTicketAsync()
    {
        if (_viewingOnly)
        {
            _statusLabel.TextColor = Color.FromArgb("#E0965A");
            _statusLabel.Text = "Viewing only (test) is on — turn it off in ⚙️ Options to save a real ticket.";
            _statusLabel.IsVisible = true;
            return;
        }
        if (_selected.Count == 0)
        {
            _statusLabel.TextColor = Color.FromArgb("#8B9DC3");
            _statusLabel.Text = "Nothing to save — pick at least one ball first.";
            _statusLabel.IsVisible = true;
            return;
        }
        // User's explicit ask (Replay Numbers feature): a ticket can never be saved to cover a
        // draw that's already happened. Gated on currentDraw actually being known yet, same as
        // RecalcCoverDrawFromGames/FillNewTicketDrawNumbersIfEmpty — fails open (doesn't block
        // Save) if this launch hasn't fetched a current draw # yet.
        int currentDrawForSaveGuard = Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber);
        if (currentDrawForSaveGuard > 0
            && int.TryParse(_searchDrawEntry.Text?.Trim(), out int saveCoverDraw) && saveCoverDraw > 0
            && saveCoverDraw <= currentDrawForSaveGuard)
        {
            _statusLabel.TextColor = Color.FromArgb("#E0965A");
            _statusLabel.Text = $"Covers# ({saveCoverDraw}) must be after the current draw (#{currentDrawForSaveGuard}) — can't save a ticket for a draw that already happened.";
            _statusLabel.IsVisible = true;
            return;
        }
        _startDraw = int.TryParse(_startDrawEntry.Text?.Trim(), out int sd) && sd > 0 ? sd : 0;

        Preferences.Set(SlotKey(KeySpots, _activeSlot), _spots);
        Preferences.Set(SlotKey(KeyBullseye, _activeSlot), _bullseye);
        Preferences.Set(SlotKey(KeyWager, _activeSlot), (double)_wager);
        Preferences.Set(SlotKey(KeyDraws, _activeSlot), _draws);
        Preferences.Set(SlotKey(KeyNumbers, _activeSlot), string.Join("|", _selected.OrderBy(n => n)));
        Preferences.Set(SlotKey(KeyPurchasedDate, _activeSlot), DateTime.Today.ToString("yyyyMMdd"));
        Preferences.Set(SlotKey(KeySavedTime, _activeSlot), DateTime.Now.ToString("o"));
        Preferences.Set(SlotKey(KeyStartDraw, _activeSlot), _startDraw);
        // Literally whatever is in the Search# box right now — see the matching comment in
        // PersistCurrentSlotRaw. Not recomputed/overwritten, so what the user typed is exactly
        // what shows again next time they switch back to this slot.
        int coverDraw = int.TryParse(_searchDrawEntry.Text?.Trim(), out int scd) && scd > 0 ? scd : 0;
        Preferences.Set(SlotKey(KeyCoverDraw, _activeSlot), coverDraw);
        Preferences.Remove(SlotKey(KeyReviewed, _activeSlot)); // fresh/edited ticket — eligible for auto-check again once it finishes
        Preferences.Remove(SlotKey(KeyWinNumbers, _activeSlot)); // fresh/edited picks — any old green-ball highlight no longer applies
        Preferences.Remove(SlotKey(KeyWinDrawNumber, _activeSlot));
        HotSpotFastCheckScheduler.EnsureScheduled(); // arm the ~2-min background check for this ticket

        // Re-lock immediately: what's on screen now matches what's on disk, so there is
        // nothing left to save — this is what makes a second accidental tap of Save a no-op
        // (the button is already disabled by the time any second tap could land).
        _editUnlocked = false;
        UpdateEditMode(); // Save commits the edit — Edit Mode/green header revert to normal immediately
        UpdateSaveButtonState();

        string savedExtra = $"SP:{_spots}|W:{_wager:0}|BE:{(_bullseye ? 1 : 0)}";
        string savedNumbersSpaced = string.Join(" ", _selected.OrderBy(n => n));
        int savedDraws = _draws;

        // Stays on the same slot after Save — user's explicit ask, previously this auto-
        // advanced to the next empty slot, which meant a locked ticket's Start#/Search# edit
        // (or any other field-only edit) landed you looking at a different, empty ticket right
        // after tapping Save.
        int savedSlot = _activeSlot;
        _statusLabel.Text = $"✓ Ticket {savedSlot + 1} saved.";
        _statusLabel.TextColor = Color.FromArgb("#4CAF7D");
        _statusLabel.IsVisible = true;
        UpdateSlotStatusLabel();
        RefreshSlotDisplayLabel(); // this slot's saved state just changed
        HotSpotMyNumbersPanel.Refresh(_myNumbersPanel);

        // Was missing — every other game's Save path clears its own slot's existing same-day
        // Ticket Log row(s) before re-logging directly (see WinnerPage/SuperLottoPage/
        // PowerballPage/MegaMillionsPage/Daily3Page/Daily4Page/DailyDerbyPage, all call
        // ClearTodayGameSlotAsync + a direct LogRows call for just their own row). Without the
        // clear, re-picking and re-Saving the same slot later the same day just piled up an
        // extra stale row per edit instead of replacing the old one.
        //
        // Also — this used to call TicketLogService.ScanAndLogTodayAsync() instead of logging
        // this one row directly, which is the real reason NOTHING was showing up in Ticket Log
        // or Log Today after saving fresh tickets (confirmed live 2026-08-09): that method only
        // ever scans ONCE PER APP SESSION PER DAY (a static `_lastScanDate` guard shared by the
        // whole app, meant to stop repeated page navigations from undoing a manual deletion) —
        // so the moment anything else in the app triggered that scan once today (e.g. opening
        // Ticket Log), every later Hot Spot Save silently no-op'd here for the rest of the
        // session. Logging this one row directly, the same way every other game already does,
        // sidesteps that gate entirely instead of depending on session timing.
        await TicketLogService.ClearTodayGameSlotAsync("HS", savedSlot);
        await TicketLogService.LogRowsWithDrawCountAsync("HS", new List<(int Slot, int Row, string Numbers, string Extra, string PlayFrom, string PlayTo, int DrawCount)>
        {
            (savedSlot, 0, savedNumbersSpaced, savedExtra, DateTime.Today.ToString("M/d"), DateTime.Today.ToString("M/d"), savedDraws),
        });
        await SpendingTracker.AutoSyncTodayAsync();

        // "Play Selected" walk-through — see _favoritesPlayQueue's own comment. Only advances
        // when the ticket just saved is actually the queue's current head; a save on some other
        // slot (browsing away mid-batch) leaves the queue untouched.
        if (_favoritesPlayQueue != null && _favoritesPlayQueue.Count > 0 && _favoritesPlayQueue[0] == savedSlot)
        {
            _favoritesPlayQueue.RemoveAt(0);
            if (_favoritesPlayQueue.Count > 0)
            {
                int nextSlot = _favoritesPlayQueue[0];
                LoadSlot(nextSlot);
                Preferences.Set(KeyActiveSlot, _activeSlot);
                HotSpotMyNumbersPanel.Refresh(_myNumbersPanel);
                _statusLabel.Text = $"✓ Ticket {savedSlot + 1} saved — showing next favorite, Ticket {nextSlot + 1} ({_favoritesPlayQueue.Count} left). Tap Save to buy it.";
                _statusLabel.TextColor = Color.FromArgb("#4CAF7D");
                _statusLabel.IsVisible = true;
            }
            else
            {
                _favoritesPlayQueue = null;
                _statusLabel.Text = $"✓ Ticket {savedSlot + 1} saved — all favorite tickets from that batch are saved.";
                _statusLabel.TextColor = Color.FromArgb("#4CAF7D");
                _statusLabel.IsVisible = true;
            }
        }
    }

    // ── Prize lookup ─────────────────────────────────────────────────────────
    // Tables/lookup now live in HotSpotDrawService — kept as thin aliases here so the many
    // existing call sites below didn't all need renaming.

    static decimal? BasePrizeFor(int spots, int matches) => HotSpotDrawService.BasePrizeFor(spots, matches);
    static decimal? BullseyePrizeFor(int spots, int matches) => HotSpotDrawService.BullseyePrizeFor(spots, matches);
    static decimal? PrizeFor(int spots, int matches, bool bullseyeHit) => HotSpotDrawService.PrizeFor(spots, matches, bullseyeHit);

    void UpdatePrizeDisplay()
    {
        // Shows the ticket's max possible prize (all spots hit) at the current wager —
        // actual win amount is computed against a real draw in CheckAgainstLatestDrawAsync.
        // Reflects the higher Bulls-eye combo amount when that add-on is toggled on, since
        // hitting every spot on a ticket that included the Bulls-eye ball is the realistic
        // "everything went right" outcome to preview.
        var top = _bullseye ? BullseyePrizeFor(_spots, _spots) : BasePrizeFor(_spots, _spots);
        if (top == null) { _prizeLabel.Text = ""; }
        else
        {
            decimal scaled = top.Value * _wager;
            string suffix = HotSpotDrawService.IsTypicalPool(_spots, _spots) ? " (Typical Prize Pool — may vary)" : "";
            _prizeLabel.Text = $"Top prize: ${scaled:N2}{suffix}";
        }

        // Bulls-eye's wager must equal the Hot Spot one (calottery.com's own rule) — so
        // turning it on doesn't add a flat amount, it doubles the total: a $2 ticket becomes
        // $4, a $5 ticket becomes $10, etc. Same formula SpendingTracker uses for the actual
        // logged cost — this is just the live on-screen preview of it.
        decimal total = _wager * _draws * (_bullseye ? 2 : 1);
        _totalCostLabel.Text = $"${total:N2}";
    }

    // ── Live draw checking ───────────────────────────────────────────────────
    // Hot Spot has no small-integer game ID in calottery.com's DrawGameApi (used by the
    // other 7 games) — its draw numbers are 7 digits (e.g. 3290273) and it's not reachable
    // there. Its real, working public data source is calottery.com's own server-rendered
    // "Past Winning Numbers" page: fetching it with no query param shows the CURRENT/latest
    // draw, and it embeds a clean accessibility-only list (class="sr-only") with each drawn
    // number and the Bulls-eye number, plus the draw number and date — scraped via regex
    // in HotSpotDrawService (DrawNumberRx/DrawTimeRx are public there since
    // TryFastDetectLatestDrawAsync below reuses them against the main Hot Spot page, which
    // shares the same "current-drawNumber"/"Draw Time" markup fields).

    // A specific past draw # (via ?query=) is a fixed historical fact once posted — it can
    // never change, so caching it for the rest of the app session is always safe. The bare
    // "latest draw" fetch (queryDrawNumber == null) is deliberately NEVER cached here since
    // that answer changes every few minutes.
    readonly Dictionary<int, HotSpotDrawService.DrawResult> _drawResultCache = new();

    // drawNumber=null fetches the latest/current draw; passing a specific 7-digit draw
    // number (via calottery.com's own "?query=" param, same as its Search-by-Draw-Number
    // box) fetches that exact past draw instead. Delegates to HotSpotDrawService, passing
    // this page's own session-scoped cache (see _drawResultCache comment above) — the
    // background checker (HotSpotChecker.cs) calls the same shared method with its own
    // per-run cache instead.
    async Task<(bool ok, int[] numbers, int bullseyeNumber, int drawNumber, string drawLabel, DateTime drawTime)> FetchDrawAsync(int? queryDrawNumber = null)
    {
        var r = await HotSpotDrawService.FetchDrawAsync(queryDrawNumber, _drawResultCache);
        if (r.Ok) _ = HSRecentDraws.AddAsync(r); // feed the rolling cache so Past 10 Days can reuse it later
        return (r.Ok, r.Numbers, r.BullseyeNumber, r.DrawNumber, r.DrawLabel, r.DrawTime);
    }

    // Getting "the current draw" turned out to have several traps, ruled out one at a time
    // by live testing against the real site:
    //   1. The bare landing page (no query string) gets cached by calottery.com's CDN for
    //      several minutes — it can lag behind the site's own displayed number for a while.
    //   2. Adding ANY extra query param to bust that cache (even a harmless "&_=timestamp")
    //      makes the endpoint treat the request as an unrecognized query and fall back to
    //      what looks like an arbitrary historical draw (one test landed on July 29).
    //   3. Querying a specific real draw number via "?query=" is normally accurate (it's
    //      what the site's own Previous/Next buttons and this page's own draw-number Search
    //      box use) — EXCEPT for a draw number that just happened moments ago, which isn't
    //      indexed for search yet and ALSO falls back to an arbitrary unrelated draw (one
    //      test: querying the number the bootstrap itself just returned came back with a
    //      result from two days earlier).
    // Net result: there is no reliable way to bypass the landing page's cache for "right
    // now" data. The bare landing page is the only fetch that has been correct in every
    // single test — so that's the only thing this uses, accepting that it can be a few
    // minutes behind rather than risking wildly wrong (different day) results.
    //
    // 2026-08-09: attempting the previously-parked idea — use the main Hot Spot page (the
    // same one ShowApproxCurrentDrawAsync already uses, confirmed several minutes fresher
    // than the past-winning-numbers page) as a LOWER BOUND signal only. If it reports a draw
    // number higher than what we've already seen, wait a safety margin past that draw's own
    // reported time (gotcha #3: a draw number queried moments after it posts can still return
    // wrong-day data), then validate the ?query= result actually echoes back the SAME draw
    // number before trusting it. Any failure at any step (no time to parse, too fresh, ok=false,
    // or a mismatched drawNumber meaning the site fell back to an unrelated draw) discards the
    // attempt and falls through to the unchanged bare-page fetch below — this can only make
    // detection faster, never less safe, since a rejected fast-detect changes nothing.
    async Task<(bool ok, int[] numbers, int bullseyeNumber, int drawNumber, string drawLabel, DateTime drawTime)> TryFastDetectLatestDrawAsync()
    {
        (bool, int[], int, int, string, DateTime) NotOk() => (false, Array.Empty<int>(), 0, 0, "", DateTime.MinValue);
        try
        {
            string html = await HotSpotDrawService.SharedHttpClient.GetStringAsync("https://www.calottery.com/en/draw-games/hot-spot");

            var drawNumMatch = HotSpotDrawService.DrawNumberRx.Match(html);
            if (!drawNumMatch.Success) return NotOk();
            int freshNum = int.Parse(drawNumMatch.Groups[1].Value);
            if (freshNum <= _lastSeenDrawNumber) return NotOk(); // no newer draw signaled yet

            string time = HotSpotDrawService.DrawTimeRx.Match(html) is { Success: true } tm ? tm.Groups[1].Value.Trim() : "";
            string normalizedTime = System.Text.RegularExpressions.Regex.Replace(
                time, @"([ap])\.m\.", m => m.Groups[1].Value.ToUpperInvariant() + "M",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            // The main page's markup has no separate "Draw Date" field (unlike the
            // past-winning-numbers page) — assume today. Only wrong in the few-minute window
            // around midnight, and worst case just skips the fast path for one cycle.
            if (!DateTime.TryParse($"{DateTime.Today:MM/dd/yyyy} {normalizedTime}", out var freshTime))
            {
                _ = Logger.LogAsync($"HS fast-detect: couldn't parse draw time '{time}' for draw #{freshNum}, skipping this round");
                return NotOk();
            }

            // Untested guess at how long the site needs before ?query= indexes a just-posted
            // draw — logged either way so a future session can tune this from real data instead
            // of guessing again.
            var age = DateTime.Now - freshTime;
            if (age < TimeSpan.FromMinutes(2))
            {
                _ = Logger.LogAsync($"HS fast-detect: draw #{freshNum} too fresh ({age:mm\\:ss} old), waiting");
                return NotOk();
            }

            var result = await FetchDrawAsync(freshNum);
            if (result.ok && result.drawNumber == freshNum)
            {
                _ = Logger.LogAsync($"HS fast-detect: SUCCESS — draw #{freshNum} validated via ?query= ({age:mm\\:ss} after posting)");
                return result;
            }
            _ = Logger.LogAsync($"HS fast-detect: ?query={freshNum} returned ok={result.ok}, drawNumber={result.drawNumber} (mismatch) — discarding");
            return NotOk();
        }
        catch (Exception ex)
        {
            _ = Logger.LogAsync($"HS fast-detect failed — {ex.GetType().Name}: {ex.Message}");
            return NotOk();
        }
    }

    async Task<(bool ok, int[] numbers, int bullseyeNumber, int drawNumber, string drawLabel, DateTime drawTime)> FindLatestDrawAsync()
    {
        // Confirmed live 2026-08-15 (04:52 local, well inside the 2am-6am closed window): the
        // live "hot-spot" landing page TryFastDetectLatestDrawAsync scrapes still renders a
        // current-drawNumber during the closed window and is the MORE accurate source (log:
        // "SUCCESS — draw #3292256"), while the bare FetchDrawAsync() fallback below returned a
        // stale #3292255 — its own "possibly a few minutes stale from CDN caching" caveat turned
        // out to last for hours once the game closes and nothing refreshes that cache entry. So
        // always try the fast/live path first, closed window or not — never skip straight to the
        // known-stale bare fetch.
        var fast = await TryFastDetectLatestDrawAsync();
        if (fast.ok) { _lastSeenDrawNumber = fast.drawNumber; return fast; }

        var result = await FetchDrawAsync();
        // The bare (no ?query=) endpoint's CDN cache can lag behind a draw# this session
        // already confirmed via the accurate fast-detect path above — confirmed live
        // 2026-08-15: during the closed window that lag lasted for HOURS (stuck one draw
        // behind), not just the "few minutes" the endpoint's own comment describes. Never let
        // a stale bare read regress _lastSeenDrawNumber backward — re-query the already-known
        // number directly instead (safe/accurate via ?query=, see FetchDrawAsync's comment).
        if (result.ok && result.drawNumber < _lastSeenDrawNumber)
            return await FetchDrawAsync(_lastSeenDrawNumber);

        if (result.ok) _lastSeenDrawNumber = result.drawNumber;
        return result;
    }

    // Purely informational — confirmed live (curl, same moment) that calottery.com's main
    // Hot Spot page (not the past-winning-numbers one used for actual checking) runs several
    // minutes fresher, but it has no winning-numbers list at all, so it's only ever used to
    // show "roughly where things stand" the instant the page opens. Deliberately separate
    // from FindLatestDrawAsync/FetchDrawAsync — never used to drive match-checking, since a
    // draw number this fresh isn't necessarily safe to query yet (see the note above).
    // forceOverwrite=true is used by the auto-refresh tick (the old dedicated 🔄 refresh icon
    // this comment used to reference was removed 2026-08-11, folded into ⚙️ Options) — an
    // automatic refresh is an intentional "give me the latest number" action, so it should
    // overwrite both fields even if they already hold something. The passive page-load call
    // leaves it false so it never clobbers a value already typed or loaded from a saved ticket.
    // A 2026-08-11 comment here previously claimed there was no reliable daily closed window
    // (based on one 3:03 AM observation) — superseded 2026-08-12 by the user's explicit,
    // direct report of the real 2am-6am closed window; see InHotSpotClosedWindow. Any other
    // failure here — the page loading but having no draw-number element, or the fetch throwing
    // outright — updates the label to say so instead of silently leaving it on a stale/blank
    // placeholder forever; the next successful auto-refresh tick or page open overwrites it
    // back to the normal "Current draw: #______" text. No "tap to retry" wording anywhere here
    // since there's no dedicated retry button anymore.
    async Task ShowApproxCurrentDrawAsync(bool forceOverwrite = false)
    {
        // Hot Spot draws stop 2:00 AM–6:00 AM — see InHotSpotClosedWindow's comment. The plain
        // unauthenticated scrape below (current-drawNumber element on the live "hot-spot"
        // landing page) is the SAME source FindLatestDrawAsync's fast-detect path already
        // reads — no need for a separate fetch here. Route through FindLatestDrawAsync so the
        // header always agrees with whatever the rest of the page (grid painting, countdown)
        // is using, and gets the same anti-stale-regression protection — confirmed live
        // 2026-08-15: calling the bare past-winning-numbers endpoint directly here returned a
        // draw# stuck one behind the real latest for hours once the game closed. This also
        // still seeds _approxCurrentDrawNumber so FillNewTicketDrawNumbersIfEmpty can auto-fill
        // Start#/Search# for a new/unsaved ticket even while the game is closed, instead of
        // leaving those boxes blank all night.
        if (InHotSpotClosedWindow())
        {
            var last = await FindLatestDrawAsync();
            if (last.ok)
            {
                _approxCurrentDrawNumber = last.drawNumber;
                _currentDrawLabel.Text = $"Curr. draw: #{last.drawNumber} (closed until 6am)";
                if (forceOverwrite)
                {
                    bool alreadySaved = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));
                    if (!alreadySaved)
                    {
                        // Never stomp a Start# the user already typed — same "only fill if empty"
                        // rule the Covers# guard right below already followed; this box was
                        // missing it, so the Auto Refresh tick kept overwriting a hand-typed
                        // Start# with the live draw number every ~8 sec. Fixed 2026-08-19.
                        if (string.IsNullOrWhiteSpace(_startDrawEntry.Text))
                            _startDrawEntry.Text = last.drawNumber.ToString();
                        if (string.IsNullOrWhiteSpace(_searchDrawEntry.Text))
                            _searchDrawEntry.Text = last.drawNumber.ToString();
                    }
                }
                else
                {
                    FillNewTicketDrawNumbersIfEmpty();
                }
            }
            else
            {
                _currentDrawLabel.Text = "Curr. draw: closed until 6am";
            }
            return;
        }

        try
        {
            string html = await HotSpotDrawService.SharedHttpClient.GetStringAsync("https://www.calottery.com/en/draw-games/hot-spot");
            var drawNumMatch = HotSpotDrawService.DrawNumberRx.Match(html);
            if (!drawNumMatch.Success)
            {
                _currentDrawLabel.Text = "Curr. draw: unavailable";
                _ = Logger.LogAsync("HS: current-draw page loaded but no draw number found");
                return;
            }
            int drawNumber = int.Parse(drawNumMatch.Groups[1].Value);

            // 📊 calCountDown — see field comment. This landing-page scrape is deliberately NOT
            // what resets _calNextChangeAt anymore (moved to CheckAutoRefreshDrawChangeAsync's
            // onFlyInStarting callback, fired when the ball-drop animation actually starts) —
            // this fetch runs and returns well before that animation begins, so resetting here
            // made the countdown restart 15+ sec ahead of what was visibly still happening on
            // the grid. This call still anchors DateTime.Now, same design as before — see the
            // field comment, user's call to keep observation-time anchoring, not the site's own
            // posted drawTime.
            _approxCurrentDrawNumber = drawNumber;
            string time = HotSpotDrawService.DrawTimeRx.Match(html) is { Success: true } tm ? tm.Groups[1].Value.Trim() : "";
            _currentDrawLabel.Text = $"Curr. draw: #{drawNumber}" + (time != "" ? $" (of {AbbreviateDrawTime(time)})" : "");

            if (forceOverwrite)
            {
                // Established 2026-08-10 (Set 1's Start#/Cover# got corrupted by paging): once a
                // slot is saved, Start draw#/Covers Draws # may ONLY change via an explicit Save
                // tap — never from live draw-number tracking. forceOverwrite (the manual 🔄 tap,
                // and every Auto Refresh timer tick) was only ever meant to bypass the "only fill
                // if empty" restriction for a ticket still being built, not to override that rule
                // — it was missing the same already-saved check FillNewTicketDrawNumbersIfEmpty
                // already has, so Auto Refresh kept silently overwriting the displayed boxes for
                // an already-saved ticket while its own tab was open.
                bool alreadySaved = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));
                if (!alreadySaved)
                {
                    // Never stomp a Start# the user already typed — same "only fill if empty"
                    // rule the Covers# guard right below already followed; this box was missing
                    // it, so the Auto Refresh tick kept overwriting a hand-typed Start# with the
                    // live draw number every ~8 sec. Fixed 2026-08-19.
                    if (string.IsNullOrWhiteSpace(_startDrawEntry.Text))
                        _startDrawEntry.Text = drawNumber.ToString();
                    // Covers# is different from Start# — for a multi-draw ticket the user often
                    // types a value that deliberately differs from "whatever draw is live right
                    // now" (e.g. start + draws - 1). Confirmed live 2026-08-14: with Auto Refresh
                    // at its 8-sec interval, force-overwriting this every tick stomped a real typed
                    // Covers# back to the live current draw before Save was even tapped, so the
                    // WRONG value got saved. Only auto-fill it here while it's still empty — same
                    // restriction FillNewTicketDrawNumbersIfEmpty already applies to both boxes —
                    // never overwrite something the user already typed.
                    if (string.IsNullOrWhiteSpace(_searchDrawEntry.Text))
                        _searchDrawEntry.Text = drawNumber.ToString();
                }
            }
            else
            {
                FillNewTicketDrawNumbersIfEmpty();
            }
        }
        catch (Exception ex)
        {
            _currentDrawLabel.Text = "Curr. draw: connection error";
            _ = Logger.LogAsync($"HS: current-draw fetch failed — {ex.GetType().Name}: {ex.Message}");
        }
    }

    // Auto-refresh-only draw-change repaint — on every tick, does exactly what tapping "Go"
    // with the current draw# already typed into the search box would do: query that specific
    // draw directly (same FetchDrawAsync(queryDrawNumber) the Go button itself uses) and
    // repaint the grid with whatever comes back, every single time — no waiting, no
    // multi-tick confirmation. User's explicit choice after being told the tradeoff: this can
    // occasionally query a draw# so fresh it isn't indexed yet and get back an unrelated old
    // draw's numbers (the same documented gotcha `FindLatestDrawAsync`'s comment describes) —
    // accepted in exchange for the grid never lagging behind a real new draw by several minutes.
    async Task CheckAutoRefreshDrawChangeAsync()
    {
        if (_approxCurrentDrawNumber <= 0) return; // current draw# not fetched yet this launch
        // See _slotScanBusy's own comment — skip this tick entirely (not queue it) while a
        // background pass is mid-way through switching _activeSlot across several tickets;
        // the next Auto Refresh tick picks the live draw back up once that settles.
        if (_slotScanBusy) return;

        var (ok, numbers, bullseyeNumber, drawNumber, drawLabel, drawTime) = await FetchDrawAsync(_approxCurrentDrawNumber);
        if (!ok)
        {
            _ = Logger.LogAsync($"HS auto-refresh: query for draw #{_approxCurrentDrawNumber} failed, skipping this tick");
            return;
        }

        // Anchored to observation time, not the site's own posted drawTime — see
        // SeedCountdownAsync's comment for why. Guarded on an actual change (wasn't before) —
        // this runs every 8 sec via Auto Refresh, so re-anchoring on every tick even when the
        // draw hasn't changed would let the countdown drift later and later on every single tick.
        if (drawNumber != _lastSeenDrawNumber)
        {
            _nextDrawAt = DateTime.Now.AddMinutes(4).AddSeconds(15);
            // User's explicit ask 2026-08-18: a ticket-win summary sitting in _statusLabel is
            // about the draw that just ended — once a genuinely new draw starts, clear it rather
            // than leaving it up to look like it still applies. Only clears when it's actually
            // showing a win summary (see _statusLabelShowsTicketWins's own comment) so an
            // unrelated message on screen at this same moment (Edit Mode notice, "Checking...",
            // etc.) is left alone.
            if (_statusLabelShowsTicketWins)
            {
                _statusLabel.Text = "";
                _statusLabel.IsVisible = false;
                _statusLabelShowsTicketWins = false;
            }
        }
        _lastSeenDrawNumber = drawNumber;

        // Once this ticket's own covered draws are all done, the live "current draw" keeps
        // moving on to the next game — without this check, every auto-refresh tick kept
        // checking (and offering to record) matches against whatever draw calottery.com is
        // now processing, with zero connection to what was actually purchased. Same rule
        // paging/search already use (see ApplyDrawResult's stageAsWin comment) — only stage a
        // win when the checked draw is actually inside [Start draw#, Covers Draws #].
        int coverDraw = _startDraw > 0 ? _startDraw + _draws - 1 : 0;
        bool inTicketRange = _startDraw > 0 && drawNumber >= _startDraw && drawNumber <= coverDraw;

        // Must also actually be a SAVED ticket — same guard CheckFinishedTicketsAsync already
        // has (see its comment at the KeyPurchasedDate check). An empty/unsaved slot's Start#
        // gets auto-filled to today's current draw the moment a pick is made, so it trivially
        // satisfies inTicketRange on the very first Auto Refresh tick and staged a bogus
        // "Record Win" for a ticket that was never bought — confirmed live 2026-08-14.
        bool isSaved = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));

        // eligibleForFlyIn is always true here (independent of inTicketRange/isSaved) — this IS
        // the real, live "the draw just changed" moment, and the user wants the reveal to play
        // for every one of those regardless of whether the ticket currently on screen still
        // covers it. Win staging (stageAsWin) stays exactly as strict as before.
        //
        // calCountDown's 4:00 "Time next Draw" prediction resets via onFlyInStarting — fired
        // from inside ShowDrawResultOnGridAsync at the exact instant the drop animation is
        // committed to play, NOT the moment this tick merely detects a new draw#. User's explicit
        // ask 2026-08-16: the old spot (ShowApproxCurrentDrawAsync's landing-page scrape, which
        // runs BEFORE this fetch+animation even starts) reset the countdown well before the balls
        // actually finished falling, so by the time the animation caught up the countdown had
        // already ticked down 15+ sec — looked broken/off. Held at "00:00" the whole gap (see the
        // OnPollTick display clamp) and now only restarts once the balls are actually dropping.
        await ApplyDrawResult(numbers, bullseyeNumber, drawNumber, drawLabel, drawTime,
            stageAsWin: inTicketRange && isSaved, eligibleForFlyIn: true,
            onFlyInStarting: () =>
            {
                if (!_calCountdownEnabled) return;
                _calNextChangeAt = DateTime.Now.AddMinutes(4);
                _ = Logger.LogAsync($"HS calCountDown: ball-drop animation started for draw #{drawNumber} at {DateTime.Now:HH:mm:ss}, resetting 4:00 prediction");
            });

        // Skip these two backlog passes on a tick that lands while a fly-in reveal is already
        // mid-flight — 20 concurrent ghost-ball animations already push the runtime hard, and
        // stacking these fire-and-forget network/JSON passes on top of that same GC-heavy window
        // is what a live crash (native SIGSEGV inside Mono, mid-reveal, 2026-08-18) traced back
        // to. Both are backlog-safe by design (see AutoCheckAndRecordAllSlotsAsync's own comment)
        // — the very next tick, a few seconds after this reveal finishes, catches up cleanly.
        if (_flyInBusy) return;

        // Reuses this same fetch (no extra network call) to also check every OTHER saved,
        // still-in-progress ticket against this draw — not just whichever one is on screen.
        _ = CheckAllSlotsLiveWinAsync(numbers, bullseyeNumber, drawNumber);

        // Backlog-safe auto-record pass — catches any already-elapsed draw within a saved
        // ticket's range that CheckAllSlotsLiveWinAsync's single-draw check never happened to
        // see live (see AutoCheckAndRecordAllSlotsAsync's own comment for why that gap exists).
        // Cheap on a normal tick (KeyLastAutoChecked cursor means nothing to fetch beyond
        // `drawNumber` itself for tickets already caught up).
        _ = AutoCheckAndRecordAllSlotsAsync(drawNumber);
    }

    // Keeps Start draw#/Covers Draws # synced to the live current draw for any ticket that
    // hasn't been Saved yet — not just a one-time fill the moment a slot is first empty.
    // Confirmed live 2026-08-10: gating this on "no balls picked yet" froze the numbers the
    // instant a Quick Pick landed, so an unsaved ticket left sitting around (or revisited later)
    // kept showing whatever draw# was current back then instead of the real current draw. A
    // Save tap is what actually locks the real numbers in (see SaveTicketAsync, which reads
    // straight from these boxes) — once a slot has a PurchasedDate, this never touches it again.
    // Uses the already-fetched _approxCurrentDrawNumber — no new network call needed just from
    // switching tickets.
    void FillNewTicketDrawNumbersIfEmpty()
    {
        if (_approxCurrentDrawNumber <= 0) return; // current draw# not known yet
        bool alreadySaved = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));
        if (alreadySaved) return; // a saved ticket's recorded draw# is locked in, never auto-touched
        _startDrawEntry.Text = _approxCurrentDrawNumber.ToString();
        _searchDrawEntry.Text = _approxCurrentDrawNumber.ToString();
    }

    // User's ask 2026-08-19: on an empty/unsaved slot, Covers# should track the "How many
    // games?" picker instead of just mirroring Start#/current draw 1-for-1. Covers# = start +
    // games - 1 — the "-1" counts the start draw itself as the first of the N games, same
    // formula this file already uses everywhere else for a ticket's real coverage window (see
    // IsSlotFullyFinished, CheckAgainstLatestDrawAsync's coverDraw, CheckRangeAsync). Called from
    // the games picker's SelectedIndexChanged and from Start#'s TextChanged (live as you type —
    // Unfocused proved unreliable, see the field-setup comment) — never once a slot is saved
    // (matches FillNewTicketDrawNumbersIfEmpty's gate).
    void RecalcCoverDrawFromGames()
    {
        bool alreadySaved = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, _activeSlot), ""));
        if (alreadySaved) return;
        if (_drawsPicker.SelectedIndex < 0) return;
        int games = DrawOptions[_drawsPicker.SelectedIndex];
        int start = int.TryParse(_startDrawEntry.Text?.Trim(), out int sd) && sd > 0 ? sd : _approxCurrentDrawNumber;
        if (start <= 0 || games <= 0) return;
        _searchDrawEntry.Text = (start + games - 1).ToString();
    }

    // silent=true is used by the auto-refresh timer — no full-screen spinner, and it never
    // overwrites the status/prize labels unless the draw actually changed since last check.
    // queryDrawNumber checks one specific past draw instead of the latest (used by the
    // "Search by Draw Number" box) — the auto-refresh countdown is only ever driven by
    // the latest draw, so a searched past draw never touches _lastSeenDrawNumber/_nextDrawAt.
    // eligibleForFlyIn defaults to "is this the real current/latest draw" (queryDrawNumber ==
    // null) but callers that already KNOW they're repainting the genuine live draw under a
    // specific number (RefreshCurrentDrawOnGridAsync — see its own comment) pass true
    // explicitly. User's explicit ask 2026-08-16: no instant "spoiler" display of the current
    // draw's numbers, ever, even the very first time the page/a ticket shows them — only a
    // genuinely exploratory lookup (DoSearch's "Go" box, an arbitrary user-typed draw #) stays
    // instant. The once-per-draw _flyInPlayedForDrawNumber gate (see ShowDrawResultOnGridAsync)
    // is what still keeps a SECOND view of the same already-revealed draw (switching tickets,
    // re-polling) from replaying it — this flag only controls whether a given call is ALLOWED
    // to trigger the reveal at all, not whether it necessarily will this time.
    async Task CheckAgainstLatestDrawAsync(bool silent = false, int? queryDrawNumber = null, bool? eligibleForFlyIn = null)
    {
        if (!silent)
        {
            _loadingOverlay.IsVisible = true;
            _spinner.IsRunning = true;
            _loadingLabel.Text = queryDrawNumber.HasValue
                ? $"Please wait — looking up draw #{queryDrawNumber}..."
                : "Please wait — checking the latest draw...";
        }
        try
        {
            // Captured BEFORE the fetch — FindLatestDrawAsync (and TryFastDetectLatestDrawAsync
            // inside it) update the _lastSeenDrawNumber field themselves as soon as they get an
            // ok result, so by the time execution reaches the "is this new?" check below, the
            // field would already equal the freshly-fetched value and the comparison would be
            // unconditionally true. That was a real bug: it meant the silent auto-poll's "draw
            // changed, reset the countdown and repaint" branch could never fire even the very
            // first time a genuinely new draw was found — comparing against this pre-fetch
            // snapshot instead fixes it.
            int previousSeen = _lastSeenDrawNumber;
            var (ok, numbers, bullseyeNumber, drawNumber, drawLabel, drawTime) = queryDrawNumber.HasValue
                ? await FetchDrawAsync(queryDrawNumber)
                : await FindLatestDrawAsync();
            if (!ok)
            {
                if (!silent)
                {
                    _statusLabel.Text = queryDrawNumber.HasValue
                        ? $"Couldn't find draw #{queryDrawNumber} — check the number and try again."
                        : "Couldn't fetch live results — check your connection and try again.";
                    _statusLabel.TextColor = Color.FromArgb("#E0965A");
                    _statusLabel.IsVisible = true;
                }
                return;
            }
            if (queryDrawNumber == null)
            {
                if (silent && drawNumber == previousSeen) return; // nothing new yet
                _lastSeenDrawNumber = drawNumber;
                // Anchored to observation time, not the site's own posted drawTime — see
                // SeedCountdownAsync's comment for why (the site's timestamp is routinely already
                // older than the 4:15 buffer by the time it's ever observed here).
                if (drawNumber != previousSeen) _nextDrawAt = DateTime.Now.AddMinutes(4).AddSeconds(15);
            }

            // Only the real "check against today's latest draw" path (queryDrawNumber == null)
            // stages a win — a manual Search-by-Draw-Number lookup (queryDrawNumber has a value)
            // is exploratory, same as paging, and must never offer to record a chance match
            // against an arbitrary draw the user just typed in out of curiosity.
            await ApplyDrawResult(numbers, bullseyeNumber, drawNumber, drawLabel, drawTime,
                stageAsWin: queryDrawNumber == null, eligibleForFlyIn: eligibleForFlyIn ?? (queryDrawNumber == null));
        }
        finally
        {
            if (!silent) { _spinner.IsRunning = false; _loadingOverlay.IsVisible = false; _loadingLabel.Text = ""; }
        }
    }

    // Shared by CheckAgainstLatestDrawAsync and PageDrawAsync (◀/▶) — computes matches against
    // the player's picks, paints the grid, updates status/prize text, and stages any win the
    // same way regardless of which path found the draw.
    // stageAsWin=false is for exploratory lookups — the "Go" search box (any draw # you type)
    // and ◀/▶ paging (browsing arbitrary past draws) — where a chance match against a random
    // historical draw unrelated to the ticket's real covered range must NEVER be offered up as
    // a recordable win. Confirmed live 2026-08-10: paging through several unrelated past draws
    // silently staged multiple bogus "wins" and the button read "Record 4 Wins" for draws that
    // had nothing to do with when this ticket was actually active. Only the automatic
    // check-against-the-latest-draw path (the real "did today's draw hit?" flow every other
    // game already uses) and CheckRangeAsync's own bounded range still stage real wins.
    // eligibleForFlyIn is deliberately a SEPARATE flag from stageAsWin — user's explicit ask
    // 2026-08-16: "I want it to happen ... every single game [draw] when the draw number
    // changes," not gated on whether the active ticket happens to still be in-progress/saved.
    // Win STAGING stays exactly as strict as before (still only ever true for a genuine
    // in-range, saved-ticket live check) — only the visual reveal itself is now decoupled from
    // that, so it plays for every real live-draw-change moment regardless of ticket state.
    async Task ApplyDrawResult(int[] numbers, int bullseyeNumber, int drawNumber, string drawLabel, DateTime drawTime = default, bool stageAsWin = true, bool? eligibleForFlyIn = null, Action? onFlyInStarting = null)
    {
        // User's explicit ask 2026-08-20 (revised same day — the grid itself must always paint,
        // only the text was ever the problem): a slot with zero spots picked AND never saved has
        // no real ticket to check a win against, so the "N of M matched" / "No win this draw" /
        // "You won $X" text is misleading for it — but the drawn-numbers reveal itself should
        // still play/paint normally, same as any other slot. See the two `if (!emptyUnsavedSlot)`
        // guards below, around the _matchLabel/_prizeLabel assignments, for where this actually
        // takes effect.
        bool emptyUnsavedSlot = _selected.Count == 0 && !IsSlotSaved(_activeSlot);

        eligibleForFlyIn ??= stageAsWin; // callers that don't pass it explicitly keep the old tied-together behavior
        // All 20 drawn numbers count toward the match total — the Bulls-eye ball is
        // still one of the 20 draws, it's just also separately eligible for the
        // Bulls-eye add-on bonus if the player opted in.
        var drawnSet = numbers.ToHashSet();
        if (bullseyeNumber > 0) drawnSet.Add(bullseyeNumber);
        int matches = _selected.Count(n => drawnSet.Contains(n));

        // Bulls-eye pays a higher combo prize when the single drawn Bulls-eye ball is one of
        // the player's own picks (you don't pick a separate Bulls-eye number — see BullseyePrizes
        // comment). Zero matches makes this impossible by construction.
        bool bullseyeHit = _bullseye && bullseyeNumber > 0 && _selected.Contains(bullseyeNumber);

        // Light up all 20 drawn numbers on the grid (not just the player's matches), same look
        // as calottery.com's own Draw Results page. Always awaited: when this is a genuinely
        // new draw AND a real "checking the live draw" moment (stageAsWin), the reveal
        // animation plays and this doesn't return until every ball has landed — the win text/
        // staging below must never appear before that, per the user's explicit ask. Every
        // other case (repeat draw, ticket switch, exploratory lookup) returns effectively
        // immediately, so there's no perceptible delay added there.
        //
        // painted=false means this call bailed out early (ShowDrawResultOnGridAsync's own
        // _flyInBusy/staleness guards) instead of doing anything — a second Auto Refresh tick
        // landing on the SAME live draw while an earlier tick's reveal is still mid-flight. Bail
        // out here too, without staging a win or touching status/prize text: the ORIGINAL call
        // whose reveal is actually playing on screen will finish its own await and stage the win
        // itself once the balls really land. User: "it happen when the draw changes and the
        // balls fly in" — this call used to keep going and show Record Win using its own fresh
        // data despite the visible balls not having landed yet.
        bool painted = await ShowDrawResultOnGridAsync(numbers, bullseyeNumber, drawNumber, eligibleForFlyIn: eligibleForFlyIn.Value, onFlyInStarting: onFlyInStarting);
        if (!painted) return;

        // Full match (every one of this slot's spots hit) — spin those balls once, right after
        // the reveal has actually finished landing. Same "genuinely new draw" gate as the reveal
        // itself (eligibleForFlyIn) plus a per-slot record of the last draw # already spun, so
        // this never replays on a ticket switch or a repeat check of the same still-current draw.
        if (matches == _spots && _spots > 0 && eligibleForFlyIn == true &&
            (!_winSpinPlayedForDrawNumber.TryGetValue(_activeSlot, out int lastSpun) || drawNumber > lastSpun))
        {
            _winSpinPlayedForDrawNumber[_activeSlot] = drawNumber;
            _ = SpinWinningBallsAsync(_selected);
        }

        var prizeBase = PrizeFor(_spots, matches, bullseyeHit);
        decimal winAmount = (prizeBase ?? 0m) * _wager;
        // User's explicit ask 2026-08-18: this routine "N of M matched" result now lives in
        // _matchLabel (between _selectedCountLabel and _prizeLabel), not _statusLabel — it used
        // to overwrite CheckAllSlotsLiveWinAsync's "🎯 Draw #X hit — Ticket Y: $Z" cross-slot win
        // banner on the VERY NEXT auto-refresh tick (as little as 8 sec later, Auto Refresh's
        // fastest setting), so a win on another slot was only ever visible for one tick before
        // this unconditional overwrite erased it. Living in its own label instead of a shared
        // hold-timer workaround means _statusLabel is free to stay on screen as long as whatever
        // set it intends, and a real win on the ACTIVE ticket is still called out clearly here too.
        bool activeTicketWin = stageAsWin && winAmount > 0;
        if (!emptyUnsavedSlot)
        {
            // The match COUNT itself is always green (user's explicit ask 2026-08-22 — "make the
            // number 2 green... or any number that shows up"), so it stands out from the rest of
            // the line even when the rest is muted gray (no win). FormattedText with two spans
            // instead of a single colored string — see _matchLabel.FormattedText = null in
            // ResetActiveSlotUI's comment for why the clear site there had to change too.
            var restColor = activeTicketWin ? Color.FromArgb("#4CAF7D") : Color.FromArgb("#8B9DC3");
            string suffix = activeTicketWin
                ? $" of {_spots} matched!"
                : $" of {_spots} matched" + (bullseyeHit ? " 🎯 Bulls-eye!" : "");
            _matchLabel.FormattedText = new FormattedString
            {
                Spans =
                {
                    new Span { Text = activeTicketWin ? "✅ " : "", TextColor = restColor },
                    new Span { Text = matches.ToString(), TextColor = Color.FromArgb("#4CAF7D") },
                    new Span { Text = suffix, TextColor = restColor },
                }
            };
        }
        if (!stageAsWin)
        {
            // Informational only — show what this specific draw would have paid, but never
            // stage it, and never touch whatever real pending win(s) already exist from an
            // earlier legitimate check.
            if (!emptyUnsavedSlot)
                _prizeLabel.Text = winAmount > 0 ? $"This draw would pay ${winAmount:N2} (view only — not staged)" : "No win this draw";
            return;
        }

        // A ticket still in progress (its covered range not yet finished) never gets its
        // Reviewed flag set by a manual Record Win tap (see RecordPendingWinAsync's own comment
        // on why not) — so as long as calottery.com's "current draw" keeps landing on the SAME
        // already-recorded draw # (routine: this runs every 8 sec via Auto Refresh, and a Hot
        // Spot draw stays "current" for several minutes), _pendingWins being empty again right
        // after a Record tap looked exactly like a fresh new win to the check below and re-staged
        // the SAME draw over and over. Confirmed live 2026-08-15 — user reported "Record Win
        // keeps coming up" and had no way to tell whether it was the same win resurfacing.
        // Guard against that here the same way CheckRangeAsync's own existingKeys/IsNew already
        // does for its range walk — check winnings_log.json before staging, not just _pendingWins.
        bool alreadyRecorded = false;
        if (winAmount > 0)
        {
            string sourceKey = $"HS_{drawNumber}_{string.Join("-", _selected.OrderBy(n => n))}";
            var existing = await SummaryPage.LoadAllAsync();
            alreadyRecorded = existing.Any(r => r.Game == "HS" && r.SourceKey == sourceKey);
        }

        // User's explicit ask 2026-08-18: wins auto-record themselves now (see
        // AutoCheckAndRecordAllSlotsAsync/CheckAllSlotsLiveWinAsync) — no manual Record Win tap
        // needed anymore, so this text shouldn't tell the user to do one.
        if (!emptyUnsavedSlot)
            _prizeLabel.Text = winAmount > 0
                ? (_viewingOnly ? $"You won ${winAmount:N2}! (Viewing only — not recorded.)" : $"You won ${winAmount:N2}!")
                : "No win this draw";

        // Checking never writes anything by itself — only staged here. The user taps
        // "Record Win" explicitly to add it to Wins & Spending, same as how the
        // Results page requires an explicit tap-to-collect for every other game.
        if (winAmount > 0 && !alreadyRecorded && !_pendingWins.Any(w => w.DrawNumber == drawNumber))
            _pendingWins.Add((drawNumber, matches, winAmount, _selected.OrderBy(n => n).ToArray(), drawTime, bullseyeHit));
        if (_pendingWins.Count > 0)
            _slotPendingWins[_activeSlot] = (_pendingWins.Count, _pendingWins.Sum(w => w.Amount), _pendingWins.Select(w => w.DrawNumber).ToList());
        else
            _slotPendingWins.Remove(_activeSlot);
        RefreshRecordWinButton();
    }

    // ◀/▶ paging — mirrors calottery.com's own Previous/Next buttons on the Hot Spot
    // past-winning-numbers page, which just step the shown draw # by 1 and reload. First tap
    // (when _pageDrawNumber is still 0) seeds from whatever's already visible: the Search box,
    // then the Start box, then the last-seen draw from page load — same fallback chain, so
    // paging works even if the user hasn't typed or searched anything yet.
    static readonly Color ArrowActive = Color.FromArgb("#2E7D32");  // green — the arrow you just tapped
    static readonly Color ArrowIdle   = Color.FromArgb("#4B5563");  // gray — the other one

    bool _pagingInFlight;

    async Task PageDrawAsync(int delta)
    {
        // Ignores a tap while a previous one is still fetching, rather than letting them race —
        // confirmed live 2026-08-10: rapid taps could fire overlapping fetches whose responses
        // came back out of order, stepping _pageDrawNumber/_startDrawEntry to the wrong place
        // and occasionally surfacing a spurious "Couldn't find draw #X" from a stale response
        // landing after a newer one already moved past it.
        if (_pagingInFlight) return;
        _pagingInFlight = true;
        // Visibly disables both arrows while a fetch is in flight — on a slow connection there
        // was previously no obvious sign anything was happening, so a slow first tap read as
        // "did nothing" and the follow-up second tap (silently dropped by the guard above) got
        // credited with the result once it finally landed. A disabled button is an unmistakable
        // "still working" signal that a plain status-text line further down the screen isn't.
        _btnPrevDraw.IsEnabled = false;
        _btnNextDraw.IsEnabled = false;
        try { await PageDrawCoreAsync(delta); }
        finally
        {
            _pagingInFlight = false;
            _btnPrevDraw.IsEnabled = true;
            _btnNextDraw.IsEnabled = true;
        }
    }

    async Task PageDrawCoreAsync(int delta)
    {
        // Tapped arrow turns green, the other reverts to gray — pure visual feedback, doesn't
        // gate anything below.
        (delta < 0 ? _btnPrevDraw : _btnNextDraw).BackgroundColor = ArrowActive;
        (delta < 0 ? _btnNextDraw : _btnPrevDraw).BackgroundColor = ArrowIdle;

        int baseNum = _pageDrawNumber > 0 ? _pageDrawNumber
            : int.TryParse(_startDrawEntry.Text?.Trim(), out int st) && st > 0 ? st
            : int.TryParse(_searchDrawEntry.Text?.Trim(), out int sn) && sn > 0 ? sn
            : _lastSeenDrawNumber;
        int target = baseNum + delta;
        if (target <= 0) return;

        // Refuse to page forward past the real current draw — calottery.com confirmed live to
        // silently fall back to an arbitrary unrelated past draw for any draw # that hasn't
        // posted yet (never fails outright), so without this check ▶ would look like it keeps
        // "working" while actually just showing wrong data for a draw that hasn't happened.
        // _approxCurrentDrawNumber comes from the fresher main page (ShowApproxCurrentDrawAsync);
        // fall back to _lastSeenDrawNumber if that hasn't loaded yet.
        int upperBound = Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber);
        if (delta > 0 && upperBound > 0 && target > upperBound)
        {
            _statusLabel.TextColor = Color.FromArgb("#E0965A");
            _statusLabel.Text = "That draw hasn't happened yet.";
            _statusLabel.IsVisible = true;
            return;
        }

        // No full-screen loading overlay here (unlike Search/Check Range) — paging is meant to
        // feel like the real site's instant Previous/Next, and the overlay popping in and out
        // for a sub-second fetch read as the whole screen flashing on every tap. Still shows a
        // lightweight status line so a slower-than-usual fetch doesn't look stuck with zero
        // feedback — ApplyDrawResult (success) or the error branch below always overwrites it.
        _statusLabel.TextColor = Color.FromArgb("#8B9DC3");
        _statusLabel.Text = $"Please wait — loading draw #{target}...";
        _statusLabel.IsVisible = true;

        // Confirmed live 2026-08-10: on a weak connection a single fetch attempt often just
        // fails outright (not merely slow) — the user was having to tap the SAME arrow again
        // and again to get it to actually move, because a failed attempt silently left the
        // draw # unchanged with only a small status-text error as the only sign anything went
        // wrong. Retries automatically now instead of making the user manually re-tap.
        bool ok = false; int[] numbers = Array.Empty<int>(); int bullseyeNumber = 0, drawNumber = 0; string drawLabel = "";
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (attempt > 1)
            {
                _statusLabel.Text = $"Please wait — loading draw #{target}... (retry {attempt - 1} of {maxAttempts - 1})";
                _statusLabel.IsVisible = true;
            }
            (ok, numbers, bullseyeNumber, drawNumber, drawLabel, _) = await FetchDrawAsync(target);
            // Require the response to actually echo back the draw # we asked for — same
            // validation TryFastDetectLatestDrawAsync uses, guarding against the confirmed
            // gotcha where an unrecognized/too-fresh query silently falls back to an
            // unrelated historical draw instead of failing outright.
            if (ok && drawNumber == target) break;
        }
        if (!ok || drawNumber != target)
        {
            _statusLabel.TextColor = Color.FromArgb("#E0965A");
            _statusLabel.Text = $"Couldn't load draw #{target} after {maxAttempts} tries — check your connection and try again.";
            _statusLabel.IsVisible = true;
            return;
        }
        _pageDrawNumber = target;
        // Safe to always show the paged-to draw # here now — PersistCurrentSlotRaw no longer
        // writes Start#/Cover# back to storage for an already-purchased slot (see there), so
        // this is purely a live display while browsing. Leaving the page and coming back always
        // restores the ticket's real saved Start#/Cover# via LoadSlot, regardless of what was
        // shown here mid-browse.
        _startDrawEntry.Text = target.ToString();
        // Never stages a win — see the stageAsWin comment on ApplyDrawResult. Paging is for
        // browsing/curiosity, not for verifying real tickets (that's CheckRangeAsync's job,
        // bounded to the ticket's actual covered draws).
        await ApplyDrawResult(numbers, bullseyeNumber, drawNumber, drawLabel, stageAsWin: false);

        // Quietly fetch the next draw one step further in the same direction the user is
        // already paging, so a follow-up tap in that direction is served from FetchDrawAsync's
        // cache instead of paying another full network round-trip — confirmed live 2026-08-10
        // that repeated paging felt slow (~2-3s/tap) even after reusing one HttpClient, because
        // each tap was still a brand-new fetch. Fire-and-forget, only ever one draw ahead (not
        // a bulk pre-download) so this doesn't hammer the site.
        int lookAheadTarget = target + delta;
        if (lookAheadTarget > 0 && !(delta > 0 && upperBound > 0 && lookAheadTarget > upperBound))
            _ = FetchDrawAsync(lookAheadTarget);
    }

    // Walks every draw from the ticket's recorded starting draw # through (start + draws -
    // 1), same range shown in the "Covers draws #X to #Y" label, checking each one and
    // totaling up any wins found. Results are only staged in _pendingWins until the user
    // taps "Record Win(s)" — this never writes to the winnings log by itself.
    // silent: true skips the full-screen "Please wait..." overlay entirely, no matter how long
    // the walk takes — used by every call site except the literal Range button tap itself:
    // CheckFinishedTicketsAsync/CheckAllSlotsLiveWinAsync/AutoCheckAndRecordAllSlotsAsync's
    // jump-to-winner calls, and RecordPendingWinAsync's own re-check loop (wins record themselves
    // automatically — see that method's own comment — so even a Record Win tap isn't "waiting on
    // a check" the way an actual Range tap is). Per user's explicit ask 2026-08-18: the overlay
    // popping up with no tap of their own read as the app doing something unprompted/broken —
    // this used to fall back to showing it anyway after a short delay on a long walk, but a
    // background/automatic pass covering up to 100 draws routinely blew past that delay and
    // ended up dimming/blocking the whole screen mid-session regardless, which is exactly what
    // the user was trying to avoid. Now a background pass never shows it, full stop — only
    // _statusLabel's own "Checked N of M draws" summary reports back once it's actually done.
    async Task CheckRangeAsync(bool silent = false)
    {
        // Read the textbox directly rather than the _startDraw field — that field only
        // updates when Save is tapped, so typing (or auto-filling) a number without saving
        // first was incorrectly reported as "no starting draw entered" even with a value visible.
        if (!int.TryParse(_startDrawEntry.Text?.Trim(), out int startDraw) || startDraw <= 0)
        {
            await DisplayAlert("No Starting Draw #", "Enter the starting draw # from your receipt first.", "OK");
            return;
        }
        _startDraw = startDraw;
        if (!silent)
        {
            _loadingOverlay.IsVisible = true;
            _spinner.IsRunning = true;
        }
        if (silent) { _statusLabel.TextColor = Color.FromArgb("#8B9DC3"); _statusLabel.IsVisible = true; }
        DateTime lastStatusUpdate = DateTime.MinValue;
        try
        {
            int found = 0, checkedCount = 0;
            decimal totalWon = 0m;
            int lastDraw = _startDraw + _draws - 1;
            for (int dn = _startDraw; dn <= lastDraw; dn++)
            {
                // User report 2026-08-18: this loop's own per-draw work (fetch + JSON parse +
                // Preferences reads, on top of the status-bar text update below) piled GC/
                // allocation pressure directly on top of the ball fly-in reveal's already
                // GC-heavy 20-animation window — visibly slowed the balls down, then crashed
                // with the exact same native Mono SIGSEGV as the earlier "live crash mid fly-in"
                // fix this same day (see that entry — strcasecmp/mono_assembly_request_byname/
                // mono_class_from_typeref_checked, not a catchable C# exception). Pausing the
                // WHOLE iteration here — not just the status text below — means nothing from
                // this walk (silent or not) can add pressure while a reveal is actually playing;
                // it simply resumes the instant _flyInBusy clears.
                while (_flyInBusy) await Task.Delay(250);

                // A ticket covering many draws (up to 100) could otherwise spin for the better
                // part of a minute with zero sign it wasn't stuck. A manual Range tap gets the
                // full-screen overlay's own progress text; a silent/background pass gets the
                // same progress in _statusLabel instead — visible, but never blocking the grid
                // or dimming the screen (see CheckRangeAsync's own comment on why the overlay
                // itself never shows for these). Throttled to at most ~2/sec so a fast local
                // walk can't spam layout passes either.
                if (silent && DateTime.Now - lastStatusUpdate >= TimeSpan.FromMilliseconds(500))
                {
                    _statusLabel.Text = $"Checking draw #{dn} ({dn - _startDraw + 1} of {_draws})...";
                    lastStatusUpdate = DateTime.Now;
                }
                else if (!silent)
                {
                    _loadingLabel.Text = $"Please wait — checking draw #{dn} ({dn - _startDraw + 1} of {_draws})...";
                }
                var (ok, numbers, bullseyeNumber, drawNumber, drawLabel, drawTime) = await FetchDrawAsync(dn);
                if (!ok) continue; // draw hasn't happened/posted yet, or a transient miss — skip, don't abort the whole range
                checkedCount++;

                var drawnSet = numbers.ToHashSet();
                if (bullseyeNumber > 0) drawnSet.Add(bullseyeNumber);
                int matches = _selected.Count(n => drawnSet.Contains(n));
                bool bullseyeHit = _bullseye && bullseyeNumber > 0 && _selected.Contains(bullseyeNumber);
                decimal winAmount = (PrizeFor(_spots, matches, bullseyeHit) ?? 0m) * _wager;

                // Never eligible for the fly-in reveal — this is a bulk historical walk across
                // the ticket's whole range, not the "a live draw just happened" moment, so it
                // always takes the instant-fill path regardless of drawNumber.
                if (dn == lastDraw) await ShowDrawResultOnGridAsync(numbers, bullseyeNumber, drawNumber, eligibleForFlyIn: false);

                if (winAmount > 0 && !_pendingWins.Any(w => w.DrawNumber == drawNumber))
                {
                    _pendingWins.Add((drawNumber, matches, winAmount, _selected.OrderBy(n => n).ToArray(), drawTime, bullseyeHit));
                    totalWon += winAmount;
                    found++;
                }
            }

            // A win already sitting in winnings_log.json (same SourceKey RecordPendingWinAsync
            // would use) isn't something to invite recording again — confirmed live 2026-08-09:
            // re-checking an already-recorded ticket still showed "Tap Record Win(s) to save"
            // and a live "Record N Wins" button even though tapping it would do nothing (already
            // silently deduped there). Filters the SUMMARY/BUTTON down to genuinely new wins;
            // _pendingWins itself is left untouched — RecordPendingWinAsync's own dedup still
            // applies as the real safety net either way.
            var existingKeys = (await SummaryPage.LoadAllAsync())
                .Where(r => r.Game == "HS").Select(r => r.SourceKey).ToHashSet();
            bool IsNew((int DrawNumber, int Matches, decimal Amount, int[] Numbers, DateTime DrawTime, bool BullseyeHit) w) =>
                !existingKeys.Contains($"HS_{w.DrawNumber}_{string.Join("-", w.Numbers)}");
            int newCount = _pendingWins.Count(IsNew);
            decimal newTotal = _pendingWins.Where(IsNew).Sum(w => w.Amount);

            _statusLabel.TextColor = Color.FromArgb("#8B9DC3");
            _statusLabel.Text = $"Checked {checkedCount} of {_draws} draws (#{_startDraw}–#{lastDraw})";
            _statusLabel.IsVisible = true;
            // User's explicit ask 2026-08-18: wins auto-record themselves now — no manual Record
            // Win tap needed, so this text shouldn't tell the user to do one (see the same change
            // on ApplyDrawResult's _prizeLabel text above).
            _prizeLabel.Text = newCount > 0
                ? _viewingOnly
                    ? $"{newCount} winning draw{(newCount == 1 ? "" : "s")} found — ${newTotal:N2} total. (Viewing only — not recorded.)"
                    : $"{newCount} winning draw{(newCount == 1 ? "" : "s")} found — ${newTotal:N2} total!"
                : found > 0
                    ? $"{found} winning draw{(found == 1 ? "" : "s")} found — already recorded — ${totalWon:N2} total."
                    : "No wins found in this range";
            if (newCount > 0)
                _slotPendingWins[_activeSlot] = (newCount, newTotal, _pendingWins.Where(IsNew).Select(w => w.DrawNumber).ToList());
            else
                _slotPendingWins.Remove(_activeSlot);
            RefreshRecordWinButton();
        }
        finally
        {
            _spinner.IsRunning = false;
            _loadingOverlay.IsVisible = false;
            _loadingLabel.Text = "";
        }
    }

    async Task RecordPendingWinAsync()
    {
        if (_pendingWins.Count == 0)
        {
            // The button's count/total reflect every ticket's pending wins combined (see
            // _slotPendingWins), so it can be showing a nonzero total while the ticket actually
            // on screen has nothing to record. Used to just tell the user which other ticket
            // owned it and stop there — but switching to that ticket alone doesn't repopulate
            // _pendingWins (LoadSlot/ClearSelection always wipes it), so the user still had to
            // know to separately re-tap Range before Record Win would do anything. Confirmed
            // live 2026-08-15 that this read as the button and the X both being stuck in a
            // circle. Now it just does the switch-and-recheck itself and records whatever
            // that turns up, so a single repeated tap always makes real progress instead of
            // requiring the user to know the manual workaround.
            if (_slotPendingWins.Count > 0)
            {
                int otherSlot = _slotPendingWins.Keys.First();
                _statusLabel.TextColor = Color.FromArgb("#90CAF9");
                _statusLabel.Text = $"Checking Ticket {otherSlot + 1}...";
                _statusLabel.IsVisible = true;
                PersistCurrentSlotRaw();
                LoadSlot(otherSlot);
                // silent: true — wins record themselves automatically now (see this method's own
                // "no manual Record Win tap needed" comment below), so this re-check is really
                // just catching up the on-screen ticket, not something the user is waiting on the
                // way they are for an actual Range button tap. User's explicit ask 2026-08-18.
                await CheckRangeAsync(silent: true);
                // CheckRangeAsync removes otherSlot from _slotPendingWins whenever it finds
                // nothing new to stage (e.g. it was already recorded by the background
                // checker), so this always shrinks toward the base case rather than looping.
                await RecordPendingWinAsync();
            }
            return;
        }
        if (_viewingOnly)
        {
            _statusLabel.TextColor = Color.FromArgb("#E0965A");
            _statusLabel.Text = "Viewing only (test) is on — this win was shown but not recorded.";
            _statusLabel.IsVisible = true;
            return;
        }
        decimal newlyAddedTotal = 0;
        var newlyAddedDetails = new List<string>();
        foreach (var win in _pendingWins)
        {
            string sourceKey = $"HS_{win.DrawNumber}_{string.Join("-", win.Numbers)}";
            // The draw's own date, not "today" — a win found today for a draw from
            // yesterday's ticket must not be logged as if it happened today. Falls back to
            // today only if DrawTime never parsed (default(DateTime)).
            string winDate = win.DrawTime > DateTime.MinValue
                ? win.DrawTime.ToString("yyyy-MM-dd")
                : DateTime.Today.ToString("yyyy-MM-dd");
            bool added = await SummaryPage.AddWinAsync(new WinningRecord
            {
                Game      = "HS",
                Date      = winDate,
                Numbers   = string.Join(" ", win.Numbers),
                Amount    = win.Amount,
                Note      = $"{win.Matches}/{_spots} (draw #{win.DrawNumber})" + (win.BullseyeHit ? " (Bulls-eye)" : ""),
                SourceKey = sourceKey,
            });
            // AddWinAsync dedupes on SourceKey — only count/announce wins that were genuinely
            // new this tap, so re-checking an already-recorded draw can't fire a duplicate
            // "you won" notification below.
            if (added)
            {
                newlyAddedTotal += win.Amount;
                newlyAddedDetails.Add($"${win.Amount:N0} ({win.Matches}/{_spots}, draw #{win.DrawNumber})");
            }
        }
        SummaryPage.NeedsRefresh = true;
        _pendingWins.Clear();
        _slotPendingWins.Remove(_activeSlot);
        // Was missing — confirmed live 2026-08-14: a WINNING slot only ever gets marked Reviewed
        // by the background HotSpotChecker (runs on its own 6/7/8/9 PM schedule), never by this
        // manual tap. Until that background pass happened to run, CheckFinishedTicketsAsync kept
        // re-walking this ticket's already-finished range on every single page open and re-staged
        // the SAME already-recorded wins right back into "Record N Wins" — safe (AddWinAsync's
        // dedup blocks an actual duplicate write either way), but confusing and looked broken.
        // Only safe to mark here once the ticket's full covered range has actually finished (same
        // "is this ticket done" check CheckFinishedTicketsAsync itself uses) — a still-in-progress
        // ticket must stay unreviewed so later draws in its range still get checked.
        int reviewStartDraw = Preferences.Get(SlotKey(KeyStartDraw, _activeSlot), 0);
        int reviewDraws = Preferences.Get(SlotKey(KeyDraws, _activeSlot), 1);
        int reviewCurrentDraw = Math.Max(_approxCurrentDrawNumber, _lastSeenDrawNumber);
        if (reviewStartDraw > 0 && reviewCurrentDraw > 0 && reviewStartDraw + reviewDraws - 1 <= reviewCurrentDraw)
            Preferences.Set(SlotKey(KeyReviewed, _activeSlot), true);
        // Also refreshes the header's "Today: N tickets · $spend - $wins" line (RefreshRecordWinButton
        // calls RefreshTodaySpentLabel internally) — was missing entirely before, confirmed live
        // 2026-08-16: it lagged one win behind ($13 shown, $14 actually recorded) since nothing
        // here ever re-pulled the win total after a Record Win tap actually changed it.
        RefreshRecordWinButton(); // shows any other tickets' still-unrecorded wins, or hides itself if none left
        // AddWinAsync already blocks a true duplicate write (same SourceKey), but the status
        // message used to always say "recorded" regardless — misleading if every pending win
        // this tap was actually already recorded earlier (e.g. re-checking the same range
        // twice). Now says so plainly instead of implying something new just happened.
        if (newlyAddedDetails.Count > 0)
        {
            _statusLabel.TextColor = Color.FromArgb("#4CAF7D");
            _statusLabel.Text = "✓ Win recorded — see Wins & Spending";
        }
        else
        {
            _statusLabel.TextColor = Color.FromArgb("#8B9DC3");
            _statusLabel.Text = "Already recorded — nothing new to save.";
        }
        _statusLabel.IsVisible = true;

        // Hot Spot wins are discovered by the user's own Search/Check Range + Record Win tap,
        // never by the background win-check pipeline the other 7 games use (WinCheckReceiver.cs
        // / NotificationsPage.xaml.cs's ProcessDateAsync call — neither ever sees Hot Spot data,
        // since it deliberately isn't part of that CSV-based auto-checking system). This is the
        // one point a Hot Spot win can trigger the same push alert those games get. Respects the
        // same master on/off switch and minimum-dollar-amount threshold as every other game's
        // alert, so a user who tuned those isn't surprised by an always-on separate channel.
        if (newlyAddedTotal > 0 && Preferences.Get("win_alert_enabled", true))
        {
            decimal minAmount = Preferences.Get("win_min_amount", 100);
            if (newlyAddedTotal >= minAmount)
                NotificationHelper.ShowWin($"You Won ${newlyAddedTotal:N0} on Hot Spot!", string.Join("\n", newlyAddedDetails));
        }
    }
}

static class HotSpotExtensions
{
    public static T Also<T>(this T obj, Action<T> block) { block(obj); return obj; }
}
