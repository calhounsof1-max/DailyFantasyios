using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

// ── Fully self-contained, optional "My Numbers" floating panel for the Hot Spot page ──────
// Shows every saved ticket's spot-count, ticket #, last covered draw #, and chosen numbers at
// a glance (up to all 12 slots), so the user doesn't have to open each Ticket # slot one at a
// time to see what they picked. Reads
// HotSpotPage's per-slot Preferences data directly through its own copies of the key strings
// below (deliberately not referencing HotSpotPage's internals, same reasoning as
// HotSpotBallColors) so this whole feature can be deleted outright later: remove this file,
// then in HotSpotPage.cs remove the one options-menu row and the handful of lines in
// BuildLayout that build/attach/restore the panel. Nothing else in the app references this
// class.
public static class HotSpotMyNumbersPanel
{
    const string EnabledPrefKey = "hs_my_numbers_panel_on";
    // Mirrors HotSpotPage.cs KeyNumbers / KeyPurchasedDate / KeyStartDraw / KeyCoverDraw / SlotKey / SlotCount.
    const string KeyNumbers = "hs_numbers";
    const string KeyPurchasedDate = "hs_purchased_date";
    // Whatever's in the Start#/Covers# boxes (_startDrawEntry/_searchDrawEntry on HotSpotPage) —
    // shown here as "where this ticket started → its last covered draw". User's explicit ask
    // 2026-08-20 (Cover# only), extended 2026-08-21 to also show Start# ("so I know were it
    // started") — Cover# alone didn't say where the ticket's range actually began.
    const string KeyStartDraw = "hs_start_draw";
    const string KeyCoverDraw = "hs_cover_draw";
    // Mirrors HotSpotPage.cs KeyWinNumbers — pipe-separated subset of this ticket's picks that
    // both matched AND paid cash on the most recently live-checked draw within its range (set/
    // cleared by CheckAllSlotsLiveWinAsync every time a new draw comes in, win or not, so this
    // is never stale by more than one draw). User's explicit ask 2026-08-22: color just those
    // balls green here, and nowhere else — the main ball grid's own green already means
    // something different ("drawn", not "this specific ticket won cash off it").
    const string KeyWinNumbers = "hs_win_numbers";
    const int SlotCount = 12; // user's explicit ask 2026-08-20: show all 12 slots
    // On by default (user's explicit ask 2026-08-22 was to make this the immediate/current look,
    // not an opt-in) — the toggle to switch back to plain numbers lives in HotSpotBallColors'
    // own "Hot Spot Ball Colors" popup (same ask), not here; this class only owns the pref and
    // the rendering, HotSpotPage.cs wires the toggle switch itself since it's the one place that
    // already knows about both HotSpotBallColors and this panel.
    const string NumbersAsBallsPrefKey = "hs_my_tickets_numbers_as_balls";

    public static bool Enabled
    {
        get => Preferences.Get(EnabledPrefKey, false);
        private set => Preferences.Set(EnabledPrefKey, value);
    }

    public static bool NumbersAsBalls
    {
        get => Preferences.Get(NumbersAsBallsPrefKey, true);
        set => Preferences.Set(NumbersAsBallsPrefKey, value);
    }

    static string SlotKey(string baseKey, int slot) => $"{baseKey}_{slot}";

    static bool IsSlotSaved(int slot)
    {
        string numbers = Preferences.Get(SlotKey(KeyNumbers, slot), "");
        bool purchased = !string.IsNullOrEmpty(Preferences.Get(SlotKey(KeyPurchasedDate, slot), ""));
        return purchased && !string.IsNullOrWhiteSpace(numbers);
    }

    // Builds the (initially hidden) floating shell and wires its close button + drag handle.
    // Caller adds the returned Grid to the root layout and sets Grid.SetRow(shell, 1) — the
    // same row the main ScrollView occupies, so the shell's own untranslated position already
    // starts flush below the header with zero coordinate math, and TranslationY 0 doubles as
    // the "can't cover the header" drag floor (see ClampTranslationY).
    // Default (collapsed) and expanded caps on the scrollable ticket-list area — user's explicit
    // ask 2026-08-20: with up to 12 slots, the panel used to have no scrollview at all and just
    // grew taller with every saved ticket, eventually covering most of the screen (and, per the
    // user, whatever was left uncovered behind/around it was still reachable — a tap meant for
    // the panel could land on the Hot Spot page underneath).
    // Collapsed height fits exactly ~2 rows — confirmed live 2026-08-20: the first value tried
    // (220) comfortably fit all 6 of the user's tickets with zero scrolling needed, nowhere near
    // small enough; user then said 2-3 was the target, then narrowed that to "just 2 items."
    // Expanded is computed live off the actual viewport in the expand button handler below, so it
    // grows only as far as the user explicitly asks for via that button, never automatically.
    const double CollapsedTicketsHeight = 70;

    // onTicketTapped: called with the slot index (0-based) when a ticket row is tapped — caller
    // (HotSpotPage) wires this to its own SwitchToSlotAsync so tapping a row in this reference
    // panel loads that ticket into the main Ticket # slot/ball grid, same as picking it from the
    // Ticket # stepper. User's explicit ask 2026-08-22. Deliberately does NOT also close/hide
    // this panel — nothing the user asked for, and forcing that would take away the panel's own
    // explicit close (✕)/collapse controls.
    public static Grid Build(Grid ballGrid, ScrollView scrollView, Grid headerGrid, Action<int> onTicketTapped)
    {
        var rowsContainer = new VerticalStackLayout { Spacing = 6 };

        var title = new Label
        {
            Text = "My Tickets", FontSize = 14, FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White, VerticalOptions = LayoutOptions.Center,
        };

        // Toggles the ticket list between the fixed CollapsedTicketsHeight and a computed
        // near-full-viewport height — user's explicit ask: expandable, but only grows the screen
        // "unless I let it," i.e. never automatically, only via this button.
        var btnExpand = new Button
        {
            Text = "⤢", FontSize = 15, FontAttributes = FontAttributes.Bold,
            WidthRequest = 32, HeightRequest = 32, MinimumWidthRequest = 32, MinimumHeightRequest = 32,
            Padding = 0, CornerRadius = 10,
            BackgroundColor = Color.FromArgb("#4B5563"), TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
        };

        var btnClose = new Button
        {
            Text = "✕", FontSize = 13, FontAttributes = FontAttributes.Bold,
            WidthRequest = 32, HeightRequest = 32, MinimumWidthRequest = 32, MinimumHeightRequest = 32,
            Padding = 0, CornerRadius = 10,
            BackgroundColor = Color.FromArgb("#4B5563"), TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
        };

        var titleBar = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 6,
            Padding = new Thickness(14, 8),
            // Light blue — user's explicit ask 2026-08-20: the title bar was blending into the
            // dark page background behind the panel.
            BackgroundColor = Color.FromArgb("#1E88E5"),
        };
        titleBar.Children.Add(title);
        Grid.SetColumn(btnExpand, 1);
        titleBar.Children.Add(btnExpand);
        Grid.SetColumn(btnClose, 2);
        titleBar.Children.Add(btnClose);

        // Scrollable + height-capped instead of the old plain VerticalStackLayout, which had
        // nothing stopping it from growing to fit all 12 possible tickets at once.
        var ticketsScroll = new ScrollView
        {
            MaximumHeightRequest = CollapsedTicketsHeight,
            Content = rowsContainer,
        };

        var ticketsBody = new VerticalStackLayout { Padding = new Thickness(10, 10), Children = { ticketsScroll } };

        // No-op tap absorber — user's explicit ask 2026-08-20: taps anywhere on the panel were
        // reaching through to the Hot Spot page underneath (a ball toggling, etc.) as the panel
        // grew taller and covered more of the page. The row Labels/Grids inside have no gesture
        // recognizers of their own, so Android had nothing forcing it to treat that space as
        // "claimed" by this view. Deliberately scoped to just the ticket-rows body, NOT the whole
        // card — an earlier version put this on the card itself, which sits above the title bar's
        // own Expand/Close buttons and broke both of them (2026-08-20, caught live: user reported
        // the Expand button "not working" and being unable to reopen the panel at all).
        ticketsBody.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { }) });

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            // Blue outline (matches the title bar) instead of the original dark gray — user's
            // explicit ask 2026-08-20: a visible edge all the way around so the whole panel
            // reads as floating above the page, not just the title bar standing out.
            Stroke = new SolidColorBrush(Color.FromArgb("#1E88E5")),
            StrokeThickness = 2,
            // Drop shadow — the actual "floating above the page" cue; the blue outline alone
            // reads as a colored box, a shadow is what makes it look lifted/elevated. Needs an
            // explicit StrokeShape to actually render on Android (confirmed live 2026-08-20 —
            // invisible without one) — a plain Rectangle, not RoundRectangle, so it stays a flat
            // (non-curved) clip and doesn't reintroduce the drag-blur RoundRectangle caused.
            Shadow = new Shadow { Brush = Colors.Black, Offset = new Point(0, 6), Radius = 14, Opacity = 0.5f },
            StrokeShape = new Rectangle(),
            HorizontalOptions = LayoutOptions.Fill,
            Content = new VerticalStackLayout
            {
                Children =
                {
                    titleBar,
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#334155") },
                    ticketsBody,
                },
            },
        };

        var shell = new Grid
        {
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            Children = { card },
        };
        shell.SetValue(RowsContainerProperty, rowsContainer);
        shell.SetValue(OnTicketTappedProperty, onTicketTapped);

        bool expanded = false;
        btnExpand.Clicked += (_, _) =>
        {
            expanded = !expanded;
            double rootHeight = (shell.Parent as VisualElement)?.Height ?? 0;
            double viewportHeight = Math.Max(CollapsedTicketsHeight, rootHeight - headerGrid.Height);
            // Leaves headroom for the title bar/close row/padding above and below the scrollable
            // list, rather than the raw viewport figure, so "expanded" still stops short of
            // physically covering the header — matches the user's "unless I let it" framing:
            // even fully expanded, it's still bounded, just to a much bigger cap than default.
            double expandedHeight = Math.Max(CollapsedTicketsHeight, viewportHeight - 110);
            ticketsScroll.MaximumHeightRequest = expanded ? expandedHeight : CollapsedTicketsHeight;
            btnExpand.Text = expanded ? "⤡" : "⤢";
            // Height only actually changes after the next layout pass — re-clamp on the
            // following tick so an already-dragged-low panel can't get pushed off-screen by
            // suddenly growing taller.
            shell.Dispatcher.Dispatch(() => shell.TranslationY = ClampTranslationY(shell, headerGrid, shell.TranslationY));
        };

        btnClose.Clicked += (_, _) => Hide(shell);

        double panStartTranslationY = 0;
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    panStartTranslationY = shell.TranslationY;
                    SetDragLayer(shell, hardware: true);
                    break;
                case GestureStatus.Running:
                    shell.TranslationY = ClampTranslationY(shell, headerGrid, panStartTranslationY + e.TotalY);
                    break;
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    SetDragLayer(shell, hardware: false);
                    break;
            }
        };
        titleBar.GestureRecognizers.Add(pan);

        return shell;
    }

    // Bindable-property trick to stash the rows container on the shell itself, so Show() below
    // only needs the shell (matching the shape of the other Build.../Show... overlay pairs in
    // HotSpotPage.cs) instead of a second static field.
    static readonly BindableProperty RowsContainerProperty =
        BindableProperty.CreateAttached("RowsContainer", typeof(VerticalStackLayout), typeof(HotSpotMyNumbersPanel), null);

    static readonly BindableProperty OnTicketTappedProperty =
        BindableProperty.CreateAttached("OnTicketTapped", typeof(Action<int>), typeof(HotSpotMyNumbersPanel), null);

    // Repopulates the rows from whatever is currently saved, docks the panel just above the
    // 1-40 half of the ball grid at its current scroll position, and reveals it. Called both
    // from the Options-menu toggle and (once, deferred) on page load if the panel was left on.
    public static void Show(Grid shell, Grid ballGrid, ScrollView scrollView, Grid headerGrid)
    {
        Enabled = true;
        RebuildRows(shell);
        shell.IsVisible = true;

        void Dock() => shell.TranslationY = ClampTranslationY(shell, headerGrid,
            Math.Max(0, ballGrid.Bounds.Y - scrollView.ScrollY - shell.Height));

        if (shell.Height > 0)
        {
            Dock();
        }
        else
        {
            EventHandler? handler = null;
            handler = (_, _) =>
            {
                if (shell.Height <= 0) return;
                shell.SizeChanged -= handler;
                Dock();
            };
            shell.SizeChanged += handler;
        }
    }

    public static void Hide(Grid shell)
    {
        Enabled = false;
        shell.IsVisible = false;
    }

    // Cheap, event-driven content-only refresh — call this whenever a slot's saved data
    // actually changes (ticket saved/deleted), NOT on any timer/tick. No-ops instantly if the
    // panel isn't currently showing, and never touches its position, so it doesn't jump around
    // if the user has dragged it. Synchronous and non-blocking (a handful of Preferences reads
    // over at most 10 rows), so no async/dispatch is needed here.
    public static void Refresh(Grid shell)
    {
        if (!shell.IsVisible) return;
        RebuildRows(shell);
    }

    // Android normally re-rasterizes this panel's whole text-heavy subtree from scratch on
    // every single frame while TranslationY is changing, which is what actually read as
    // "blurry" while dragging (confirmed live 2026-08-20 — not display motion blur, since it
    // went sharp the instant the user's finger lifted, i.e. the very next frame rendered
    // cleanly). Caching the view into a hardware layer for the duration of the drag makes
    // Android move one already-rendered GPU texture instead of re-rendering text every frame.
    // Reverted back to LayerType.None the moment the drag ends so the panel isn't left holding
    // an unnecessary GPU-cached layer while just sitting there.
#if ANDROID
    static void SetDragLayer(Grid shell, bool hardware)
    {
        if (shell.Handler?.PlatformView is Android.Views.View nativeView)
        {
            nativeView.SetLayerType(hardware ? Android.Views.LayerType.Hardware : Android.Views.LayerType.None, null);
        }
    }
#else
    static void SetDragLayer(Grid shell, bool hardware) { }
#endif

    static double ClampTranslationY(VisualElement shell, VisualElement headerGrid, double proposed)
    {
        var root = shell.Parent as VisualElement;
        double rootHeight = root?.Height ?? 0;
        double minY = 0; // shell's own row already starts at the header's bottom edge
        double viewportHeight = Math.Max(0, rootHeight - headerGrid.Height);
        double maxY = Math.Max(minY, viewportHeight - shell.Height);
        return Math.Clamp(proposed, minY, maxY);
    }

    static void RebuildRows(Grid shell)
    {
        var rowsContainer = (VerticalStackLayout)shell.GetValue(RowsContainerProperty);
        var onTicketTapped = (Action<int>)shell.GetValue(OnTicketTappedProperty);
        rowsContainer.Children.Clear();

        var savedSlots = Enumerable.Range(0, SlotCount).Where(IsSlotSaved).ToList();

        if (savedSlots.Count == 0)
        {
            rowsContainer.Children.Add(new Label
            {
                Text = "No saved tickets yet.", FontSize = 13, TextColor = Color.FromArgb("#8B9DC3"),
            });
            return;
        }

        // Column layout — user's explicit ask 2026-08-21: T# and Spots pulled tight together
        // (was two separate Grid columns with a visible gap — "too hard to read"), and
        // Start→Cover moved onto the same line as the ticket numbers instead of its own row
        // under Spots. Both go into one FormattedString Label now (not separate side-by-side
        // Labels) specifically so they wrap TOGETHER as a unit — user's follow-up: fine for the
        // draw range to drop to its own line if it doesn't fit, as long as it isn't crammed in
        // a way that's confusing.
        var header = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(new GridLength(110)), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 8,
        };
        var spotsHeaderLabel = new Label { Text = "Spots", FontSize = 10, TextColor = Color.FromArgb("#5B6B85") };
        Grid.SetColumn(spotsHeaderLabel, 0);
        header.Children.Add(spotsHeaderLabel);
        var numbersHeaderLabel = new Label { Text = "Draw # → Numbers", FontSize = 10, TextColor = Color.FromArgb("#5B6B85") };
        Grid.SetColumn(numbersHeaderLabel, 1);
        header.Children.Add(numbersHeaderLabel);
        rowsContainer.Children.Add(header);

        foreach (int slot in savedSlots)
        {
            string raw = Preferences.Get(SlotKey(KeyNumbers, slot), "");
            var picks = raw.Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(int.Parse).OrderBy(n => n).ToList();
            string numbersText = string.Join(", ", picks); // user's explicit ask 2026-08-20: comma-separated, no leading zero

            int startDraw = Preferences.Get(SlotKey(KeyStartDraw, slot), 0);
            int coverDraw = Preferences.Get(SlotKey(KeyCoverDraw, slot), 0);
            string startDrawText = startDraw > 0 ? startDraw.ToString() : "—";
            string coverDrawText = coverDraw > 0 ? coverDraw.ToString() : "—";

            var row = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(new GridLength(110)), new ColumnDefinition(GridLength.Star) },
                ColumnSpacing = 8,
                // Explicit (even if transparent) — a Grid with no Background at all leaves its
                // empty cell space non-hit-testable on Android, so taps between/around the
                // Labels and ball bubbles below would fall through instead of registering on
                // this row's own TapGestureRecognizer (added below).
                BackgroundColor = Colors.Transparent,
            };

            var ticketLabel = new Label
            {
                Text = $"T{slot + 1}", FontSize = 13, TextColor = Color.FromArgb("#8B9DC3"),
            };
            var spotsLabel = new Label
            {
                // Not bold — user's explicit ask 2026-08-21 (was FontAttributes.Bold before).
                // Space-padded instead of zero-padded so "4 Spot" still lines up under
                // "10 Spot" without a leading zero.
                Text = $"{picks.Count.ToString().PadLeft(2)} Spot", FontSize = 13,
                TextColor = Colors.White,
            };
            // Tight fixed 4px gap, not a full Grid column each — user's explicit ask: T# and
            // Spots were too far apart to read as one unit.
            var ticketAndSpots = new HorizontalStackLayout { Spacing = 4, Children = { ticketLabel, spotsLabel } };
            Grid.SetColumn(ticketAndSpots, 0);
            row.Children.Add(ticketAndSpots);

            var drawRangeLabel = new Label
            {
                Text = $"{startDrawText}→{coverDrawText}", FontSize = 12, TextColor = Color.FromArgb("#8B9DC3"),
            };

            View numbersView;
            if (NumbersAsBalls)
            {
                // White balls, same glossy-sphere treatment as the main grid's own balls — dark
                // text since white numbers wouldn't read on a white ball (same reasoning as
                // HotSpotBallColors' SoftWhite text override). Small (20px) since up to 10 of
                // these can appear on one ticket row in this already-compact panel.
                // A pick also turns up here (green, white text) if it's part of the subset that
                // both matched AND paid cash on the most recently live-checked draw for this
                // slot — see KeyWinNumbers' own comment. Empty for a losing/not-yet-checked draw.
                var winNumbers = Preferences.Get(SlotKey(KeyWinNumbers, slot), "")
                    .Split('|', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();

                var ballsFlex = new Microsoft.Maui.Controls.FlexLayout
                {
                    Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
                    Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
                };
                foreach (var n in picks)
                {
                    bool won = winNumbers.Contains(n);
                    ballsFlex.Children.Add(new Border
                    {
                        WidthRequest = 20, HeightRequest = 20, Margin = new Thickness(1.5),
                        StrokeThickness = 0, StrokeShape = new Ellipse(),
                        // Same green as the main grid's own "match (win)" ball (BallMatch,
                        // #2E7D32) — kept as a literal here rather than a reference, same
                        // deliberate decoupling as the rest of this file.
                        Background = won ? Color.FromArgb("#2E7D32") : Colors.White,
                        Content = new Label
                        {
                            Text = n.ToString(), FontSize = 9, FontAttributes = FontAttributes.Bold,
                            TextColor = won ? Colors.White : Color.FromArgb("#1E2733"),
                            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                        },
                    });
                }
                numbersView = ballsFlex;
            }
            else
            {
                numbersView = new Label
                {
                    Text = numbersText, FontSize = 12, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#FFD54F"), LineBreakMode = LineBreakMode.WordWrap,
                };
            }

            var numbersCell = new VerticalStackLayout { Spacing = 2, Children = { drawRangeLabel, numbersView } };
            Grid.SetColumn(numbersCell, 1);
            row.Children.Add(numbersCell);

            // Tap the row to load this ticket into the main Ticket # slot — see Build's own
            // onTicketTapped comment. Fires on the Grid itself (not a per-cell recognizer) so
            // tapping anywhere across the row, including the gaps between balls, counts.
            row.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => onTicketTapped?.Invoke(slot)) });

            rowsContainer.Children.Add(row);
        }
    }
}
