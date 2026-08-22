using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

// ── Fully self-contained, optional cosmetic add-on for the Hot Spot ball grid ──────────
// Reskins ONLY the DEFAULT (unpicked/unchecked) ball look. Never touches Selected/Drawn/
// Match/Bullseye/BullseyeHit — those colors carry real meaning per the on-screen legend
// (see HotSpotPage.BuildBallLegend), not decoration.
//
// Deliberately kept independent of HotSpotPage's internals — own Preferences key, own
// gradient-brush builder (not a call into HotSpotPage's private Make3DBallBrush), own popup
// UI — so this whole feature can be deleted outright later: remove this file, then in
// HotSpotPage.cs remove the one menu tap handler and the one line in SetBallState that reads
// CurrentDefaultBallBrush(). Nothing else in the app references this class.
public static class HotSpotBallColors
{
    // White — 2026-08-19, added specifically for the background fill below: a literal pure
    // white, distinct from the existing muted SoftWhite.
    public enum Theme { Default, SoftWhite, SlateBlue, SageGreen, DustyRose, WarmTaupe, SlateGray, MutedTeal, White }

    const string PrefKey = "hs_ball_color_theme";
    const string BorderColorPrefKey = "hs_border_color_theme";
    const string ThicknessPrefKey = "hs_ball_border_thickness";
    // Background fill behind 1-40 / 41-80 (2026-08-19) — independent of both the ball color
    // and line color above. Default here means "no fill at all" (fully transparent, the
    // board's original look) — unlike the line's Default (which tracks the ball color), a
    // background needs an explicit off-state so it stays invisible until a color is picked.
    const string BackgroundColorPrefKey = "hs_background_color_theme";

    public static Theme Current
    {
        get => Enum.TryParse<Theme>(Preferences.Get(PrefKey, nameof(Theme.Default)), out var t) ? t : Theme.Default;
        private set => Preferences.Set(PrefKey, value.ToString());
    }

    // Independent of the ball color theme above — Default here means "track the ball color"
    // (the original behavior), but any other choice locks the half-board borders to that
    // color regardless of what the balls themselves are doing. User's explicit ask: the line
    // should be able to be a different color than the balls.
    public static Theme CurrentBorderTheme
    {
        get => Enum.TryParse<Theme>(Preferences.Get(BorderColorPrefKey, nameof(Theme.Default)), out var t) ? t : Theme.Default;
        private set => Preferences.Set(BorderColorPrefKey, value.ToString());
    }

    // Stroke thickness for the two half-board square borders (see HotSpotPage.BuildHalfBorder).
    // 2 matches the original fixed thickness the borders shipped with.
    public static double CurrentBorderThickness
    {
        get => Preferences.Get(ThicknessPrefKey, 2.0);
        private set => Preferences.Set(ThicknessPrefKey, value);
    }

    static readonly (double Value, string Name)[] ThicknessChoices =
    {
        (0.0, "None"),
        (1.0, "Thin"),
        (2.0, "Medium"),
        (3.0, "Thick"),
        (5.0, "Extra Thick"),
    };

    static void SetBorderThickness(double t) => CurrentBorderThickness = t;

    // Theme.Default = fully transparent (no fill) — see BackgroundColorPrefKey's own comment
    // for why this differs from the line's Default.
    public static Theme CurrentBackgroundTheme
    {
        get => Enum.TryParse<Theme>(Preferences.Get(BackgroundColorPrefKey, nameof(Theme.Default)), out var t) ? t : Theme.Default;
        private set => Preferences.Set(BackgroundColorPrefKey, value.ToString());
    }

    public static Color CurrentBackgroundColor() =>
        CurrentBackgroundTheme == Theme.Default ? Colors.Transparent : BaseColors[CurrentBackgroundTheme];

    static void SetBackgroundTheme(Theme t) => CurrentBackgroundTheme = t;

    // Muted/desaturated on purpose — the user asked for something easier on the eyes than
    // the app's normal saturated status colors, so these are deliberately soft, not vivid.
    static readonly Dictionary<Theme, Color> BaseColors = new()
    {
        [Theme.SoftWhite] = Color.FromArgb("#E8E4DC"), // warm off-white, not stark pure white
        [Theme.SlateBlue] = Color.FromArgb("#5C7893"),
        [Theme.SageGreen] = Color.FromArgb("#6B8F73"),
        [Theme.DustyRose] = Color.FromArgb("#A9727C"),
        [Theme.WarmTaupe] = Color.FromArgb("#A89579"),
        [Theme.SlateGray] = Color.FromArgb("#6B7280"),
        [Theme.MutedTeal] = Color.FromArgb("#5C8F8B"),
        [Theme.White]     = Colors.White, // see the enum's own comment
    };

    static readonly (Theme Theme, string Name)[] Choices =
    {
        (Theme.Default,   "Default (current)"),
        (Theme.SoftWhite, "Soft White"),
        (Theme.SlateBlue, "Slate Blue"),
        (Theme.SageGreen, "Sage Green"),
        (Theme.DustyRose, "Dusty Rose"),
        (Theme.WarmTaupe, "Warm Taupe"),
        (Theme.SlateGray, "Slate Gray"),
        (Theme.MutedTeal, "Muted Teal"),
        (Theme.White,     "White"),
    };

    // Same glossy-sphere treatment (radial gradient, highlight upper-left, darker rim) as
    // HotSpotPage's own balls — built independently here rather than calling into
    // HotSpotPage, so this class has zero dependency on it in either direction. Kept in sync
    // by hand with HotSpotPage.Make3DBallBrush (2026-08-13: punchier 5-stop version) — if that
    // one changes again, mirror it here too, or a custom color theme will look flatter than
    // the default balls instead of matching.
    static Brush Make3DBrush(Color baseColor) => new RadialGradientBrush
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

    // Returns null for Theme.Default so the caller just keeps using its own existing default
    // brush/color ("the color we're using right now") — this class never needs to know what
    // that is.
    public static Brush? CurrentDefaultBallBrush() =>
        Current == Theme.Default ? null : Make3DBrush(BaseColors[Current]);

    // Flat (non-gradient) version of the same color, for callers that need a plain Color
    // rather than a Brush — e.g. the half-board square borders around 1-40 / 41-80, which
    // should always read as "whatever the current ball color is," Default theme included.
    public static Color CurrentDefaultColor(Color fallbackColor) =>
        Current == Theme.Default ? fallbackColor : BaseColors[Current];

    // Color for the two half-board square borders. Theme.Default tracks whatever the ball
    // color currently resolves to (via CurrentDefaultColor); any other theme locks the
    // borders to that color independent of the balls.
    public static Color CurrentBorderColor(Color fallbackColor) =>
        CurrentBorderTheme == Theme.Default ? CurrentDefaultColor(fallbackColor) : BaseColors[CurrentBorderTheme];

    // White numbers on SoftWhite's near-white balls were confirmed unreadable live — every
    // other theme's base color is dark enough that white numbers stay fine. Returns the
    // caller's own fallback color for Default and every other theme; only SoftWhite overrides
    // it to a dark number color.
    public static Color CurrentDefaultTextColor(Color fallbackTextColor) =>
        Current == Theme.SoftWhite ? Color.FromArgb("#2D3E55") : fallbackTextColor;

    static void SetTheme(Theme t) => Current = t;
    static void SetBorderTheme(Theme t) => CurrentBorderTheme = t;

    // Builds the whole "Hot Spot Ball Colors" popup as a self-sized overlay Grid — caller
    // adds it once to its own root layout and toggles IsVisible to show/hide it (same
    // overlay pattern HotSpotPage already uses for its Payout popup). onThemeChanged is
    // called after a swatch is tapped so the caller can repaint its ball grid immediately;
    // a fresh overlay is built each call so the checkmark always reflects the live selection.
    // `myTicketsBallsInitial`/`onMyTicketsBallsChanged` are for a toggle that has nothing to do
    // with this file's own ball-grid theming (see the class header comment) — it lives in this
    // popup purely at the user's explicit ask 2026-08-22 ("option in the Color Dialog"). Kept as
    // primitives/a callback rather than a direct reference to HotSpotMyNumbersPanel so this file
    // still has zero compile-time dependency on that one; HotSpotPage.cs (which already knows
    // about both) is what actually wires the getter/setter through.
    public static Grid BuildPickerOverlay(Color fallbackDefaultColor, Action onThemeChanged,
        bool myTicketsBallsInitial, Action<bool> onMyTicketsBallsChanged)
    {
        Grid overlay = null!;

        // One list instead of two — each row is still "tap to pick the ball color," plus a
        // ✓ Line checkbox on the same row to also use that same color for the half-board
        // outline. At most one checkbox is ever checked (unchecking it, or picking a ball
        // color while none is checked, falls back to "line matches whatever the ball color
        // currently is" — CurrentBorderTheme.Default). suppressCheckboxEvents stops the
        // "uncheck every other box" step below from re-entering this handler.
        var swatchList = new VerticalStackLayout { Spacing = 10 };
        var lineChecks = new List<(Theme Theme, CheckBox Box)>();
        // Mirrors lineChecks for the "BG" (background fill) column.
        var bgChecks = new List<(Theme Theme, CheckBox Box)>();
        var ballLabels = new List<(Theme Theme, Label Label, string BaseText)>();
        bool suppressCheckboxEvents = false;

        foreach (var (theme, name) in Choices)
        {
            string label = theme == Theme.Default ? "Default (current)" : name;
            Color previewColor = theme == Theme.Default ? fallbackDefaultColor : BaseColors[theme];
            bool isCurrent = Current == theme;

            var swatch = new Border
            {
                WidthRequest = 34, HeightRequest = 34, StrokeThickness = 0,
                StrokeShape = new Ellipse(),
                Background = Make3DBrush(previewColor),
            };
            var nameLabel = new Label
            {
                Text = isCurrent ? $"{label}  ✓" : label,
                FontSize = 14,
                TextColor = isCurrent ? Color.FromArgb("#FFD54F") : Colors.White,
                VerticalOptions = LayoutOptions.Center,
            };
            ballLabels.Add((theme, nameLabel, label));
            var ballTapArea = new HorizontalStackLayout
            {
                Spacing = 12, Padding = new Thickness(4, 6),
                Children = { swatch, nameLabel },
            };
            var capturedTheme = theme;
            ballTapArea.GestureRecognizers.Add(new TapGestureRecognizer
            {
                // Doesn't close the overlay — user can pick a ball color, a line color, and a
                // thickness in one visit; Close is the only thing that dismisses this popup.
                Command = new Command(() =>
                {
                    SetTheme(capturedTheme);
                    onThemeChanged();
                    // Re-highlight the ✓ mark in place — same "popup no longer closes on pick"
                    // reason as the Line Thickness fix above; without this the check mark stays
                    // stuck on whichever color was current when the popup was opened.
                    foreach (var (t, l, baseText) in ballLabels)
                    {
                        bool isNowCurrent = t == capturedTheme;
                        l.Text = isNowCurrent ? $"{baseText}  ✓" : baseText;
                        l.TextColor = isNowCurrent ? Color.FromArgb("#FFD54F") : Colors.White;
                    }
                })
            });

            var lineCheck = new CheckBox
            {
                IsChecked = CurrentBorderTheme == theme,
                Color = Color.FromArgb("#4CAF7D"),
                VerticalOptions = LayoutOptions.Center,
            };
            lineCheck.CheckedChanged += (_, e) =>
            {
                if (suppressCheckboxEvents) return;
                suppressCheckboxEvents = true;
                if (e.Value)
                {
                    foreach (var (t, box) in lineChecks)
                        if (t != capturedTheme) box.IsChecked = false;
                    SetBorderTheme(capturedTheme);
                }
                else
                {
                    SetBorderTheme(Theme.Default);
                }
                suppressCheckboxEvents = false;
                onThemeChanged();
            };
            lineChecks.Add((theme, lineCheck));

            // "BG" checkbox — same exclusive-pick pattern as Line above, wired to
            // CurrentBackgroundTheme/SetBackgroundTheme instead.
            var bgCheck = new CheckBox
            {
                IsChecked = CurrentBackgroundTheme == theme,
                Color = Color.FromArgb("#4CAF7D"),
                VerticalOptions = LayoutOptions.Center,
            };
            bgCheck.CheckedChanged += (_, e) =>
            {
                if (suppressCheckboxEvents) return;
                suppressCheckboxEvents = true;
                if (e.Value)
                {
                    foreach (var (t, box) in bgChecks)
                        if (t != capturedTheme) box.IsChecked = false;
                    SetBackgroundTheme(capturedTheme);
                }
                else
                {
                    SetBackgroundTheme(Theme.Default);
                }
                suppressCheckboxEvents = false;
                onThemeChanged();
            };
            bgChecks.Add((theme, bgCheck));

            var row = new Grid
            {
                ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) },
                ColumnSpacing = 10,
            };
            row.Children.Add(ballTapArea);
            Grid.SetColumn(ballTapArea, 0);
            row.Children.Add(lineCheck);
            Grid.SetColumn(lineCheck, 1);
            row.Children.Add(bgCheck);
            Grid.SetColumn(bgCheck, 2);
            swatchList.Children.Add(row);
        }

        // Column header so the checkbox's meaning is clear at a glance — aligned with the
        // same Star/Auto/Auto column structure as the rows above it.
        var swatchHeader = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 10,
        };
        var lineHeaderLabel = new Label { Text = "Line", FontSize = 10, TextColor = Color.FromArgb("#8B9DC3"), HorizontalOptions = LayoutOptions.Center };
        swatchHeader.Children.Add(lineHeaderLabel);
        Grid.SetColumn(lineHeaderLabel, 1);
        var bgHeaderLabel = new Label { Text = "BG", FontSize = 10, TextColor = Color.FromArgb("#8B9DC3"), HorizontalOptions = LayoutOptions.Center };
        swatchHeader.Children.Add(bgHeaderLabel);
        Grid.SetColumn(bgHeaderLabel, 2);

        // Line thickness for the two half-board square borders — separate concern from the
        // ball color theme above, but lives in the same popup since both control the same
        // pair of borders (see HotSpotPage.BuildHalfBorder). Each preview square renders at
        // its own candidate thickness so the option is visibly self-explanatory.
        var thicknessRow = new HorizontalStackLayout { Spacing = 14, HorizontalOptions = LayoutOptions.Center };
        var thicknessEntries = new List<(double Value, Border Preview, Label Label)>();
        foreach (var (value, name) in ThicknessChoices)
        {
            bool isCurrentThickness = Math.Abs(CurrentBorderThickness - value) < 0.01;
            // "None" (0) would otherwise render as an invisible square — indistinguishable from
            // empty space, confusing next to the other visibly-outlined choices. User's ask
            // 2026-08-20: keep the same box shape as the others (a real, always-visible 1px
            // outline regardless of the 0 value actually saved) and mark it with a red ✕ instead.
            bool isNoneOption = value <= 0.0;
            var preview = new Border
            {
                WidthRequest = 30, HeightRequest = 30,
                StrokeShape = new Rectangle(),
                StrokeThickness = isNoneOption ? 1.0 : value,
                Stroke = new SolidColorBrush(isCurrentThickness ? Color.FromArgb("#FFD54F") : Colors.White),
                // Colors.Transparent here made the whole square a dead tap target on Android —
                // confirmed live (taps did nothing at all). A near-zero but non-zero alpha
                // keeps it visually invisible while still giving the renderer something to
                // hit-test against.
                Background = Color.FromArgb("#01000000"),
            };
            // Overlaid on top of `preview`, not a separate control — `preview` (the Border) is
            // still what thicknessEntries tracks for the gold/white highlight toggle below, so
            // wrapping it in a Grid here doesn't need any change to that logic.
            View previewVisual = isNoneOption
                ? new Grid
                {
                    WidthRequest = 30, HeightRequest = 30,
                    Children =
                    {
                        preview,
                        new Label
                        {
                            Text = "✕", FontSize = 16, FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#E05555"),
                            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
                            InputTransparent = true,
                        },
                    },
                }
                : preview;
            var thicknessLabel = new Label
            {
                Text = name, FontSize = 9,
                TextColor = isCurrentThickness ? Color.FromArgb("#FFD54F") : Color.FromArgb("#8B9DC3"),
                HorizontalOptions = LayoutOptions.Center,
            };
            var capturedValue = value;
            var tapArea = new VerticalStackLayout
            {
                Spacing = 4, HorizontalOptions = LayoutOptions.Center,
                Children = { previewVisual, thicknessLabel },
            };
            tapArea.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    SetBorderThickness(capturedValue);
                    onThemeChanged();
                    // Re-highlight in place — this popup no longer closes/rebuilds on pick
                    // (user wants to make several selections in one visit), so without this
                    // the chosen square never visibly moves off "Thin," reading as frozen.
                    foreach (var (v, p, l) in thicknessEntries)
                    {
                        bool isNowCurrent = Math.Abs(v - capturedValue) < 0.01;
                        p.Stroke = new SolidColorBrush(isNowCurrent ? Color.FromArgb("#FFD54F") : Colors.White);
                        l.TextColor = isNowCurrent ? Color.FromArgb("#FFD54F") : Color.FromArgb("#8B9DC3");
                    }
                })
            });
            thicknessEntries.Add((value, preview, thicknessLabel));
            thicknessRow.Children.Add(tapArea);
        }

        // Slightly wider than the original 280 to fit the ✓ Line checkbox column without
        // crowding the color names.
        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(18, 14),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 330, // widened from 300 (2026-08-19) to fit the "BG" checkbox column
        };

        var btnClose = new Button
        {
            Text = "Close", FontSize = 13, TextColor = Color.FromArgb("#FFD54F"),
            BackgroundColor = Colors.Transparent, HorizontalOptions = LayoutOptions.End,
            Padding = new Thickness(4, 4),
        };
        btnClose.Clicked += (_, _) => overlay.IsVisible = false;

        var myTicketsBallsSwitch = new Switch { IsToggled = myTicketsBallsInitial, OnColor = Color.FromArgb("#4CAF7D") };
        myTicketsBallsSwitch.Toggled += (_, e) => onMyTicketsBallsChanged(e.Value);
        var myTicketsBallsRow = new HorizontalStackLayout
        {
            Spacing = 10, HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "My Tickets: numbers as balls", FontSize = 13, TextColor = Colors.White,
                    VerticalOptions = LayoutOptions.Center,
                },
                myTicketsBallsSwitch,
            },
        };

        // Only the color list scrolls (height-capped) — everything else (title, Line Thickness,
        // the My Tickets switch, Close) stays outside the ScrollView so it's always visible.
        // The 8-color list alone used to just barely fit the whole card on screen without any
        // scrolling at all; adding the My Tickets switch row below it (2026-08-22) pushed the
        // card past the bottom of the screen, hiding Close entirely — confirmed live via a
        // screenshot. An earlier attempt at wrapping the WHOLE VerticalStackLayout in one
        // ScrollView "mismeasured on Android and pushed Close out of the rendered card
        // entirely" — that's a different shape (unbounded outer scroll) from this one (a single
        // bounded MaximumHeightRequest around just the swatch list), which matches the same
        // scrollable-inner-list pattern already working elsewhere in this app (Replay_Numbers.cs,
        // HotSpotFavorites.cs).
        var swatchScroll = new ScrollView { MaximumHeightRequest = 260, Content = swatchList };

        card.Content = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new Label
                {
                    Text = "Hot Spot Ball Colors",
                    FontSize = 15, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#FFD54F"),
                    HorizontalOptions = LayoutOptions.Center,
                },
                new Label
                {
                    Text = "Tap a color for the balls. Check ✓ Line to outline 1-40 / 41-80, or ✓ BG to fill behind them, in that color.",
                    FontSize = 11, TextColor = Color.FromArgb("#8B9DC3"),
                    HorizontalTextAlignment = TextAlignment.Center,
                },
                swatchHeader,
                swatchScroll,
                new BoxView { HeightRequest = 1, Color = Color.FromArgb("#334155"), Margin = new Thickness(0, 4) },
                new Label
                {
                    Text = "Line Thickness",
                    FontSize = 13, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#FFD54F"),
                    HorizontalOptions = LayoutOptions.Center,
                },
                thicknessRow,
                new BoxView { HeightRequest = 1, Color = Color.FromArgb("#334155"), Margin = new Thickness(0, 4) },
                myTicketsBallsRow,
                btnClose,
            }
        };

        overlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false,
            Children = { card },
        };
        return overlay;
    }
}
