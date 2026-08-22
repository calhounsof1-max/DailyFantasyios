using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

// ── Fully self-contained "Replay Numbers" feature for the Hot Spot page ───────────────────
// User's ask 2026-08-21: a fast way to re-enter a ticket for the next draw without hand-typing
// everything again, since real draws come every ~4 minutes. Lists every ticket slot whose game
// has already ENDED (same "startDraw + draws - 1 <= currentDraw" formula HotSpotPage.
// IsSlotFullyFinished already uses), and lets the user reload it — back into that SAME Ticket #
// slot (user's explicit ask: replay lands where it was replayed FROM, not on some other empty
// slot; all 12 slots can legitimately be full at once, so an ended ticket's own slot is the only
// target that's always available).
//
// User's explicit ask (live, mid-testing): everything (Games/Spots/Bulls-eye/Wager/Start#/the
// actual numbers) must be editable right in this popup before committing — going back out to the
// main page to fix something "defeats the purpose" of a fast pre-draw tool. So every field below
// is a live-editable control seeded from the ended ticket's saved values, defaulting to exactly
// what a plain one-tap replay would use (Start# = current draw + 2, same numbers/spots/etc as
// before) but nothing is written to Preferences until Replay is actually tapped.
//
// Deliberately reads/writes HotSpotPage's per-slot Preferences directly through its own copies
// of the key strings/SlotCount/option lists below (same reasoning as HotSpotMyNumbersPanel)
// rather than referencing HotSpotPage's internals, so this whole feature can be deleted outright
// later: remove this file, then in HotSpotPage.cs remove the one options-menu row, the overlay
// build/wire-in lines in BuildLayout, and the HandleReplayNumbersTapped method. Nothing else in
// the app references this class. The replayed ticket is written exactly like an unsaved,
// hand-filled-in ticket (see PersistCurrentSlotRaw) — KeyPurchasedDate/KeyReviewed are cleared,
// so the user still has to review it and tap the real Save button, same as filling it in by
// hand; the OLD game's win/loss result already lives in TicketLogService's own log from whenever
// HotSpotChecker checked it, so clearing the slot's PurchasedDate here doesn't erase that history.
public static class Replay_Numbers
{
    const string KeySpots         = "hs_spots";
    const string KeyBullseye      = "hs_bullseye";
    const string KeyWager         = "hs_wager";
    const string KeyDraws         = "hs_draws";
    const string KeyNumbers       = "hs_numbers";
    const string KeyPurchasedDate = "hs_purchased_date";
    const string KeyStartDraw     = "hs_start_draw";
    const string KeyCoverDraw     = "hs_cover_draw";
    const string KeyReviewed      = "hs_reviewed";
    const int SlotCount = 12;

    // Mirrors HotSpotPage's SpotOptions/DrawOptions/WagerOptions exactly — same choices as the
    // main page's own pickers, kept as an isolated copy per this file's own reasoning above.
    static readonly int[] SpotOptions = { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
    static readonly int[] DrawOptions = { 1, 2, 3, 4, 5, 10, 20, 50, 100 };
    static readonly decimal[] WagerOptions = { 1, 2, 3, 4, 5, 10, 20 };

    // Android's native EditText (both Picker and Entry render onto one under MAUI's handler)
    // enforces its own minimum height and internal top/bottom padding no matter what
    // HeightRequest says — confirmed live 2026-08-21 that HeightRequest alone had zero visible
    // effect. Reaching into the native view once its handler attaches and zeroing both out is
    // the only thing that actually shrinks it; FontSize/text size is untouched either way.
    static void ShrinkAndroidTextBox(View control)
    {
        control.HandlerChanged += (_, _) =>
        {
#if ANDROID
            if (control.Handler?.PlatformView is Android.Widget.EditText et)
            {
                int padPx = (int)(2 * DeviceDisplay.MainDisplayInfo.Density);
                et.SetPadding(et.PaddingLeft, padPx, et.PaddingRight, padPx);
                et.SetMinHeight(0);
                et.SetMinimumHeight(0);
            }
#endif
        };
    }

    static string SlotKey(string baseKey, int slot) => $"{baseKey}_{slot}";

    static bool IsSlotSaved(int slot)
    {
        string numbers = Preferences.Get(SlotKey(KeyNumbers, slot), "");
        bool purchased = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, slot), ""));
        return purchased && !string.IsNullOrWhiteSpace(numbers);
    }

    // Mirrors HotSpotPage.IsSlotFullyFinished exactly — kept as its own copy so this file has
    // zero references into HotSpotPage's internals.
    static bool IsSlotEnded(int slot, int currentDraw)
    {
        if (currentDraw <= 0) return false;
        if (!IsSlotSaved(slot)) return false;
        int startDraw = Preferences.Get(SlotKey(KeyStartDraw, slot), 0);
        if (startDraw <= 0) return false;
        int draws = Preferences.Get(SlotKey(KeyDraws, slot), 1);
        return startDraw + draws - 1 <= currentDraw;
    }

    // Named TicketEntry (not "Entry") to avoid colliding with Microsoft.Maui.Controls.Entry —
    // the popup below builds real Entry text boxes for Start# right in this same class.
    public sealed record TicketEntry(
        int Slot, int StartDraw, int CoverDraw, int Draws, int Spots,
        bool Bullseye, decimal Wager, int[] Numbers);

    // Ended tickets only, in slot order — in-progress and empty slots never show up here.
    public static List<TicketEntry> GetEndedTickets(int currentDraw)
    {
        var list = new List<TicketEntry>();
        for (int s = 0; s < SlotCount; s++)
        {
            if (!IsSlotEnded(s, currentDraw)) continue;

            string numbersRaw = Preferences.Get(SlotKey(KeyNumbers, s), "");
            int[] numbers = numbersRaw
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(tok => int.TryParse(tok, out int n) ? n : 0)
                .Where(n => n is >= 1 and <= 80)
                .OrderBy(n => n)
                .ToArray();
            if (numbers.Length == 0) continue; // shouldn't happen for a saved slot, but never replay a blank ticket

            list.Add(new TicketEntry(
                Slot: s,
                StartDraw: Preferences.Get(SlotKey(KeyStartDraw, s), 0),
                CoverDraw: Preferences.Get(SlotKey(KeyCoverDraw, s), 0),
                Draws: Preferences.Get(SlotKey(KeyDraws, s), 1),
                Spots: Math.Clamp(Preferences.Get(SlotKey(KeySpots, s), numbers.Length), 1, 10),
                Bullseye: Preferences.Get(SlotKey(KeyBullseye, s), false),
                Wager: (decimal)Preferences.Get(SlotKey(KeyWager, s), 1.0),
                Numbers: numbers));
        }
        return list;
    }

    // Reloads (possibly edited) ticket data back into its OWN slot with the given Start# —
    // Covers# is always start + games - 1, the same formula HotSpotPage.RecalcCoverDrawFromGames
    // uses on the main page. Numbers/spots/bullseye/wager/games come from `edited`, which may
    // differ from what was originally saved if the user changed anything in the popup.
    // KeyPurchasedDate/KeyReviewed are cleared so the slot goes back to "unsaved, hand-filled-in"
    // state — the same state PersistCurrentSlotRaw leaves an in-progress ticket in — requiring an
    // explicit Save tap to become a real ticket again, same as filling it in by hand.
    public static void ReplayInto(TicketEntry edited, int startDraw)
    {
        int slot = edited.Slot;
        int cover = startDraw + Math.Max(edited.Draws, 1) - 1;

        Preferences.Set(SlotKey(KeySpots, slot), edited.Spots);
        Preferences.Set(SlotKey(KeyBullseye, slot), edited.Bullseye);
        Preferences.Set(SlotKey(KeyWager, slot), (double)edited.Wager);
        Preferences.Set(SlotKey(KeyDraws, slot), edited.Draws);
        Preferences.Set(SlotKey(KeyNumbers, slot), string.Join("|", edited.Numbers.OrderBy(n => n)));
        Preferences.Set(SlotKey(KeyStartDraw, slot), startDraw);
        Preferences.Set(SlotKey(KeyCoverDraw, slot), cover);
        Preferences.Remove(SlotKey(KeyPurchasedDate, slot));
        Preferences.Remove(SlotKey(KeyReviewed, slot));
    }

    // ── UI: a full-screen overlay Grid, same CC000000-backdrop + centered Border "card" pattern
    // every other HotSpotPage overlay uses (see BuildPayoutOverlay/BuildLast200TicketOverlay).
    // `onReplay(edited, startDraw)` fires when Replay is tapped on a row, carrying whatever the
    // user left the row's controls set to — the caller (HotSpotPage) is responsible for the
    // actual slot switch; this class only ever computes/writes data, never touches HotSpotPage's
    // live UI fields. `show` lets the caller open it, freshly populated (called fresh every open,
    // same as RefreshOptionsMenuRows).
    //
    // `getCurrentDraw` is a LIVE accessor, not a one-time value — real bug found live 2026-08-21:
    // a fixed int captured once when the popup opened went stale if the user left the popup open
    // across an actual draw or two (reviewing numbers, switching the ticket picker, etc.), so
    // Replay could silently start a ticket 2 draws too early because the "Start# must be after
    // current draw" check was validating against the moment the popup opened, not the moment
    // Replay was actually tapped. Every place that needs "the current draw" below calls
    // getCurrentDraw() fresh at that exact moment instead of closing over a stale int.
    public static Grid Build(Func<int> getCurrentDraw, Action<TicketEntry, int> onReplay, out Action show)
    {
        var rowsContainer = new VerticalStackLayout { Spacing = 10 };

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(14, 10),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 360,
        };

        var title = new Label
        {
            Text = "🔁 Replay Numbers",
            FontSize = 15, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#FFD54F"),
            HorizontalOptions = LayoutOptions.Center,
        };
        var subtitle = new Label
        {
            Text = "Ended tickets only - Tap Replay when ready.",
            FontSize = 9, TextColor = Color.FromArgb("#8B9DC3"),
            HorizontalTextAlignment = TextAlignment.Center,
        };

        Grid overlay = null!;
        var btnClose = new Button
        {
            Text = "Close", FontSize = 13, TextColor = Color.FromArgb("#FFD54F"),
            BackgroundColor = Colors.Transparent, HorizontalOptions = LayoutOptions.End,
            Padding = new Thickness(4, 4),
        };
        btnClose.Clicked += (_, _) => overlay.IsVisible = false;

        // Footer slot for the currently-selected ticket's Replay button — lives OUTSIDE the
        // ScrollView, next to Close, so it's always on screen with no scrolling. User's explicit
        // ask 2026-08-22: the button used to sit at the bottom of the (scrollable) ball grid,
        // requiring a scroll to reach it. ShowSelected below swaps this slot's Content each time
        // the ticket picker selection changes, since BuildRow rebuilds the button fresh per ticket.
        var replaySlot = new ContentView { HorizontalOptions = LayoutOptions.Fill };
        var footerRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8,
        };
        footerRow.Add(replaySlot, 0, 0);
        footerRow.Add(btnClose, 1, 0);

        card.Content = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                title, subtitle,
                new ScrollView { MaximumHeightRequest = 560, Content = rowsContainer },
                footerRow,
            }
        };

        overlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false,
            Children = { card },
        };

        show = () =>
        {
            int currentDraw = getCurrentDraw(); // fresh at open time — used only to decide which tickets are ended and seed the default Start#
            var ended = GetEndedTickets(currentDraw);
            rowsContainer.Children.Clear();
            replaySlot.Content = null; // cleared here so a stale button from a prior open can't linger with nothing to select

            if (ended.Count == 0)
            {
                rowsContainer.Children.Add(new Label
                {
                    Text = "No ended tickets yet — nothing to replay.",
                    FontSize = 12, TextColor = Color.FromArgb("#8B9DC3"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 4),
                });
            }
            else
            {
                // Picker to choose WHICH ended ticket to work on, rather than building every
                // ended ticket's full editable card (80-ball grid + 4 pickers each) all at once —
                // user's explicit ask 2026-08-21, to save resources when several tickets have
                // ended. Only the selected one's card gets built/shown at a time.
                var picker = new Picker
                {
                    Title = "Select Ticket", TextColor = Colors.White, FontSize = 12,
                    BackgroundColor = Color.FromArgb("#101923"), HeightRequest = 34,
                };
                foreach (var e in ended)
                {
                    int endedAt = e.CoverDraw > 0 ? e.CoverDraw : e.StartDraw + e.Draws - 1;
                    // Spot count added — user's ask 2026-08-22: draw# alone doesn't say which
                    // game (# spots) a ticket is, so picking blind among several ended tickets
                    // wasn't possible without opening each one first.
                    picker.Items.Add($"Ticket #{e.Slot + 1}  ·  {e.Spots} Spot  ·  ended draw {endedAt}");
                }

                var detailContainer = new ContentView();
                void ShowSelected(int idx)
                {
                    if (idx >= 0 && idx < ended.Count)
                    {
                        var built = BuildRow(ended[idx], currentDraw, getCurrentDraw, onReplay);
                        detailContainer.Content = built.Card;
                        replaySlot.Content = built.ReplayHost;
                    }
                    else
                    {
                        detailContainer.Content = null;
                        replaySlot.Content = null;
                    }
                }
                picker.SelectedIndexChanged += (_, _) => ShowSelected(picker.SelectedIndex);

                rowsContainer.Children.Add(picker);
                rowsContainer.Children.Add(detailContainer);

                picker.SelectedIndex = 0;
                ShowSelected(0); // explicit — don't rely on SelectedIndexChanged firing for a 0->0 "change"
            }

            overlay.IsVisible = true;
        };

        return overlay;
    }

    static (View Card, View ReplayHost) BuildRow(TicketEntry entry, int currentDrawAtOpen, Func<int> getCurrentDraw, Action<TicketEntry, int> onReplay)
    {
        int endedAt = entry.CoverDraw > 0 ? entry.CoverDraw : entry.StartDraw + entry.Draws - 1;
        int defaultStart = currentDrawAtOpen + 2; // just the initial suggestion shown in the box — live taps below always re-check against getCurrentDraw()

        // Live-editable state, seeded from the ended ticket — nothing here touches Preferences
        // until Replay is actually tapped.
        var selected = new List<int>(entry.Numbers);
        int spots = entry.Spots;
        int games = entry.Draws;
        bool bullseye = entry.Bullseye;
        decimal wager = entry.Wager;

        var ticketLabel = new Label { Text = $"Ticket #{entry.Slot + 1}", FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#C7D3EA") };
        var endedLabel = new Label
        {
            Text = $"ENDED · draw {endedAt}", FontSize = 9, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#8B9DC3"), HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
        };
        var headerGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { ticketLabel, endedLabel },
        };
        Grid.SetColumn(endedLabel, 1);

        Label FieldLabel(string t) => new Label { Text = t, FontSize = 9, TextColor = Color.FromArgb("#8B9DC3") };
        View Field(string label, View control) => new VerticalStackLayout { Spacing = 1, Children = { FieldLabel(label), control } };

        // Plain HeightRequest alone doesn't shrink these on Android — user confirmed live:
        // "the textboxes height look the same no change at all." Android's native EditText
        // (Picker and Entry both render as one under MAUI's handler) enforces its own minimum
        // height/padding regardless of HeightRequest, so the actual fix has to reach into the
        // native view once its handler attaches and strip that padding/minimum directly — see
        // ShrinkAndroidTextBox below. FontSize left alone on every one of these controls per the
        // user's explicit ask (box height down, text size unchanged).
        var gamesPicker = new Picker { Title = "Games", TextColor = Colors.White, FontSize = 11, BackgroundColor = Color.FromArgb("#101923"), HeightRequest = 22, VerticalOptions = LayoutOptions.Center };
        ShrinkAndroidTextBox(gamesPicker);
        foreach (var d in DrawOptions) gamesPicker.Items.Add(d.ToString());
        gamesPicker.SelectedIndex = Math.Max(0, Array.IndexOf(DrawOptions, games));

        var spotsPicker = new Picker { Title = "Spots", TextColor = Colors.White, FontSize = 11, BackgroundColor = Color.FromArgb("#101923"), HeightRequest = 22, VerticalOptions = LayoutOptions.Center };
        ShrinkAndroidTextBox(spotsPicker);
        foreach (var s in SpotOptions) spotsPicker.Items.Add(s.ToString());
        spotsPicker.SelectedIndex = Math.Max(0, Array.IndexOf(SpotOptions, spots));

        var bullseyeSwitch = new Switch { IsToggled = bullseye, OnColor = Color.FromArgb("#C62828"), HeightRequest = 22, Scale = 0.8 };
        var bullseyeRow = new HorizontalStackLayout
        {
            Spacing = 6,
            Children = { new Label { Text = bullseye ? "On" : "Off", FontSize = 11, TextColor = Colors.White, VerticalOptions = LayoutOptions.Center }, bullseyeSwitch },
        };
        var bullseyeStateLabel = (Label)bullseyeRow.Children[0];

        var wagerPicker = new Picker { Title = "Wager", TextColor = Colors.White, FontSize = 11, BackgroundColor = Color.FromArgb("#101923"), HeightRequest = 22, VerticalOptions = LayoutOptions.Center };
        ShrinkAndroidTextBox(wagerPicker);
        foreach (var w in WagerOptions) wagerPicker.Items.Add($"${w:0}");
        wagerPicker.SelectedIndex = Math.Max(0, Array.IndexOf(WagerOptions, wager));

        var startEntry = new Entry
        {
            Text = defaultStart.ToString(), Keyboard = Keyboard.Numeric, FontSize = 12,
            TextColor = Colors.White, BackgroundColor = Color.FromArgb("#101923"),
            HorizontalTextAlignment = TextAlignment.Center, HeightRequest = 22, VerticalOptions = LayoutOptions.Center,
        };
        ShrinkAndroidTextBox(startEntry);

        var coverPreview = new Label
        {
            FontSize = 10, FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 1, 0, 2),
        };

        var selectedCountLabel = new Label { FontSize = 9, TextColor = Color.FromArgb("#8B9DC3"), HorizontalTextAlignment = TextAlignment.Center };

        // Shrunk + shortened text (was "▶  Replay this ticket" at FontSize 12/HeightRequest 38,
        // full-width) — user's ask 2026-08-22 to move this next to Close, which only leaves it
        // the footer row's Star column (Close takes the Auto column), so it has to be small
        // enough for that text to actually fit instead of wrapping/clipping.
        // MinimumHeightRequest = 0 is required here — Resources/Styles/Styles.xaml's global
        // implicit Button style forces MinimumHeightRequest="44", which silently overrides any
        // smaller HeightRequest (this exact override bit the Quick/Clear/Save/Range/Delete
        // buttons before too, see billy.md). HorizontalOptions = Start keeps the button hugging
        // its own text instead of stretching to fill the footer row's Star column — the earlier
        // version filled that whole column and looked "way too wide" per user's live screenshot.
        var replayBtn = new Button
        {
            Text = "▶ Replay Ticket", FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#1A4FAA"), CornerRadius = 8, Padding = new Thickness(10, 4),
            HeightRequest = 32, MinimumHeightRequest = 0, HorizontalOptions = LayoutOptions.Start,
        };
        // Reloading the target slot repaints the main page's 80-ball grid (LoadSlot), which takes
        // a few real seconds — user's explicit ask: show a spinner immediately on tap so the
        // button doesn't look dead during that wait, even though the repaint itself (shared,
        // used by every slot switch in the app) isn't something this isolated file should touch.
        var replaySpinner = new ActivityIndicator
        {
            Color = Colors.White, WidthRequest = 20, HeightRequest = 20,
            IsRunning = false, IsVisible = false,
        };
        var replayBtnHost = new Grid { HorizontalOptions = LayoutOptions.Start, Children = { replayBtn, replaySpinner } };

        void RefreshCoverPreview()
        {
            int liveCurrentDraw = getCurrentDraw(); // live, not the stale open-time value — see Build's comment
            bool validStart = int.TryParse(startEntry.Text?.Trim(), out int start) && start > liveCurrentDraw;
            bool validCount = selected.Count == spots;
            if (!validStart)
            {
                coverPreview.Text = $"Start# must be after current draw (#{liveCurrentDraw})";
                coverPreview.TextColor = Color.FromArgb("#E0522C");
            }
            else
            {
                int cover = start + Math.Max(games, 1) - 1;
                coverPreview.Text = $"Will set Start# {start}  →  Covers# {cover}";
                coverPreview.TextColor = Color.FromArgb("#90CAF9");
            }
            replayBtn.IsEnabled = validStart && validCount;
            replayBtn.Opacity = replayBtn.IsEnabled ? 1.0 : 0.5;
        }

        Dictionary<int, Border> ballLookup = new();
        void RepaintBall(int n)
        {
            if (ballLookup.TryGetValue(n, out var b))
                b.BackgroundColor = selected.Contains(n) ? Color.FromArgb("#1565C0") : Color.FromArgb("#2D3E55");
        }
        void RefreshSelectedCount()
        {
            selectedCountLabel.Text = $"{selected.Count} of {spots} spots selected";
            RefreshCoverPreview();
        }

        var ballsFlex = new FlexLayout
        {
            Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            Margin = new Thickness(0, 2, 0, 6),
        };
        for (int n = 1; n <= 80; n++)
        {
            int captured = n;
            var ball = new Border
            {
                WidthRequest = 26, HeightRequest = 26, Margin = new Thickness(2),
                StrokeShape = new Ellipse(), StrokeThickness = 0,
                BackgroundColor = selected.Contains(captured) ? Color.FromArgb("#1565C0") : Color.FromArgb("#2D3E55"),
                Content = new Label
                {
                    Text = captured.ToString(), FontSize = 11, FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                },
            };
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                if (selected.Contains(captured))
                    selected.Remove(captured);
                else
                {
                    if (selected.Count >= spots) return; // at the chosen Spot limit — no-op, same as the main page
                    selected.Add(captured);
                }
                RepaintBall(captured);
                RefreshSelectedCount();
            };
            ball.GestureRecognizers.Add(tap);
            ballLookup[captured] = ball;
            ballsFlex.Children.Add(ball);
        }

        gamesPicker.SelectedIndexChanged += (_, _) =>
        {
            if (gamesPicker.SelectedIndex < 0) return;
            games = DrawOptions[gamesPicker.SelectedIndex];
            RefreshCoverPreview();
        };
        spotsPicker.SelectedIndexChanged += (_, _) =>
        {
            if (spotsPicker.SelectedIndex < 0) return;
            spots = SpotOptions[spotsPicker.SelectedIndex];
            while (selected.Count > spots)
            {
                int last = selected[^1];
                selected.RemoveAt(selected.Count - 1);
                RepaintBall(last);
            }
            RefreshSelectedCount();
        };
        bullseyeSwitch.Toggled += (_, e) => { bullseye = e.Value; bullseyeStateLabel.Text = bullseye ? "On" : "Off"; };
        wagerPicker.SelectedIndexChanged += (_, _) => { if (wagerPicker.SelectedIndex >= 0) wager = WagerOptions[wagerPicker.SelectedIndex]; };
        startEntry.TextChanged += (_, _) => RefreshCoverPreview();

        replayBtn.Clicked += async (_, _) =>
        {
            // Re-check against the LIVE current draw at the exact moment of tap, not whatever
            // was true when the popup opened — real bug found live 2026-08-21: leaving the
            // popup open across an actual draw or two let a stale check through, starting the
            // ticket 2 draws too early.
            if (!int.TryParse(startEntry.Text?.Trim(), out int start) || start <= getCurrentDraw()) return;
            if (selected.Count != spots) return;

            replayBtn.IsEnabled = false;
            replayBtn.Opacity = 0;
            replaySpinner.IsVisible = true;
            replaySpinner.IsRunning = true;
            await Task.Yield(); // let the spinner actually paint before LoadSlot's synchronous repaint blocks the UI thread

            var edited = entry with { Draws = games, Spots = spots, Bullseye = bullseye, Wager = wager, Numbers = selected.OrderBy(x => x).ToArray() };
            onReplay(edited, start);
        };

        RefreshSelectedCount(); // also runs RefreshCoverPreview and sets the initial button state

        var controlsGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8, RowSpacing = 2,
            Margin = new Thickness(0, 2, 0, 1),
        };
        controlsGrid.Add(Field("Games", gamesPicker), 0, 0);
        controlsGrid.Add(Field("Spots", spotsPicker), 1, 0);
        controlsGrid.Add(Field("Bulls-eye", bullseyeRow), 0, 1);
        controlsGrid.Add(Field("Wager", wagerPicker), 1, 1);

        var rowCard = new Border
        {
            BackgroundColor = Color.FromArgb("#182432"),
            Stroke = new SolidColorBrush(Color.FromArgb("#243447")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(10, 8),
            Content = new VerticalStackLayout
            {
                Spacing = 1,
                Children =
                {
                    headerGrid, controlsGrid,
                    Field("Start#", startEntry),
                    coverPreview,
                    selectedCountLabel, ballsFlex,
                },
            }
        };

        return (rowCard, replayBtnHost);
    }
}
