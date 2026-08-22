using DailyFantasyMAUI.Services;
using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

/// <summary>
/// Self-contained jackpot ticker panel for MainPage.
/// Shows F5 / SL / PB / MM jackpots with logos and amounts on white cards.
/// Refreshes once daily after 8 PM. +/− button collapses/expands.
/// Feature can be disabled in Notifications settings ("jd_show_panel").
/// </summary>
public class JackpotDisplay : ContentView
{
    const string PrefDateKey  = "jd_cache_date";  // "yyyy-MM-dd"
    const string PrefHourKey  = "jd_cache_hour";  // int (24h) of last fetch
    public const string PrefShowPanel = "jd_show_panel"; // bool, default true

    bool _expanded = true;

    /// <summary>Fires whenever the panel's expanded/collapsed state changes, so MainPage can
    /// hide the ticket lists behind it while expanded instead of both squeezing onto screen
    /// at once (found 2026-08-02: the lists sat in the Grid's remaining "*" row below the panel,
    /// so an expanded panel just pushed them into a thin visible sliver instead of hiding them).</summary>
    public event Action<bool>? ExpandedChanged;
    public bool IsExpanded => _expanded;

    readonly Grid  _cardsGrid;
    readonly Label _lblFooter;
    readonly Label _lblToggle;

    readonly Label _lblF5Amt  = MakeAmountLabel();
    readonly Label _lblSLAmt  = MakeAmountLabel();
    readonly Label _lblPBAmt  = MakeAmountLabel();
    readonly Label _lblMMAmt  = MakeAmountLabel();

    readonly Label _lblF5Date  = MakeDateLabel();
    readonly Label _lblSLDate  = MakeDateLabel();
    readonly Label _lblPBDate  = MakeDateLabel();
    readonly Label _lblMMDate  = MakeDateLabel();

    public JackpotDisplay()
    {
        // ── Header bar ────────────────────────────────────────────────────────
        _lblToggle = new Label
        {
            Text                    = "−",
            FontSize                = 20,
            FontAttributes          = FontAttributes.Bold,
            TextColor               = Colors.White,
            VerticalOptions         = LayoutOptions.Center,
            WidthRequest            = 38,
            HorizontalTextAlignment = TextAlignment.Center,
        };

        var headerLbl = new Label
        {
            Text             = "🏆  CURRENT JACKPOTS",
            FontSize         = 11,
            FontAttributes   = FontAttributes.Bold,
            TextColor        = Color.FromArgb("#F59E0B"),
            VerticalOptions  = LayoutOptions.Center,
            CharacterSpacing = 0.4,
        };

        var header = new Grid
        {
            BackgroundColor   = Color.FromArgb("#0D1407"),
            Padding           = new Thickness(12, 3),
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
        };
        header.Add(headerLbl, 0, 0);
        header.Add(_lblToggle, 1, 0);
        header.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(Toggle) });

        // ── 2 × 2 card grid ──────────────────────────────────────────────────
        _cardsGrid = new Grid
        {
            RowDefinitions    = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            RowSpacing        = 0,
            ColumnSpacing     = 0,
            BackgroundColor   = Color.FromArgb("#050A10"),
            Padding           = new Thickness(6, 3, 6, 3),
            IsVisible         = true,
        };

        _cardsGrid.Add(MakeCard("logo_fantasy5.png",    Color.FromArgb("#FF8F00"), "Fantasy 5",        _lblF5Amt, _lblF5Date), 0, 0);
        _cardsGrid.Add(MakeCard("logo_superlotto.png",  Color.FromArgb("#7B1FA2"), "Super Lotto Plus", _lblSLAmt, _lblSLDate), 1, 0);
        _cardsGrid.Add(MakeCard("logo_powerball.png",   Color.FromArgb("#C62828"), "Powerball",        _lblPBAmt, _lblPBDate), 0, 1);
        _cardsGrid.Add(MakeCard("logo_megamillions.png",Color.FromArgb("#1565C0"), "Mega Millions",    _lblMMAmt, _lblMMDate), 1, 1);

        // ── Footer ────────────────────────────────────────────────────────────
        _lblFooter = new Label
        {
            Text                    = "Fetching jackpot amounts…",
            FontSize                = 9,
            TextColor               = Color.FromArgb("#4A7FB5"),
            HorizontalTextAlignment = TextAlignment.Center,
            Padding                 = new Thickness(8, 3, 8, 5),
            BackgroundColor         = Color.FromArgb("#070D14"),
            IsVisible               = true,
        };

        var sep = new BoxView { Color = Color.FromArgb("#1E3A5F"), HeightRequest = 1 };

        var outer = new VerticalStackLayout { Spacing = 0 };
        outer.Add(header);
        outer.Add(_cardsGrid);
        outer.Add(_lblFooter);
        outer.Add(sep);
        Content = outer;
    }

    // ── Card builder ─────────────────────────────────────────────────────────

    static View MakeCard(string logoFile, Color accent, string gameName, Label amtLabel, Label dateLabel)
    {
        var stripe = new BoxView { Color = accent, HeightRequest = 4 };

        amtLabel.Margin = new Thickness(4, 5, 4, 0);

        var img = new Image
        {
            Source            = ImageSource.FromFile(logoFile),
            Aspect            = Aspect.AspectFit,
            HeightRequest     = 48,
            HorizontalOptions = LayoutOptions.Center,
            Margin            = new Thickness(8, 6, 8, 0),
        };

        var gameLbl = new Label
        {
            Text                    = gameName,
            FontSize                = 8,
            TextColor               = Color.FromArgb("#7DB3D8"),
            HorizontalTextAlignment = TextAlignment.Center,
            Margin                  = new Thickness(4, 2, 4, 8),
        };

        var inner = new VerticalStackLayout { Spacing = 0 };
        inner.Add(stripe);
        inner.Add(img);
        inner.Add(dateLabel);
        inner.Add(amtLabel);
        inner.Add(gameLbl);

        return new Border
        {
            Content         = inner,
            BackgroundColor = Color.FromArgb("#0D1B2A"),
            Stroke          = new SolidColorBrush(Color.FromArgb("#1E3A5F")),
            StrokeThickness = 1,
            StrokeShape     = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Margin          = new Thickness(3),
        };
    }

    static Label MakeAmountLabel() => new Label
    {
        Text                    = "…",
        FontSize                = 14,
        FontAttributes          = FontAttributes.Bold,
        TextColor               = Color.FromArgb("#F59E0B"),
        HorizontalTextAlignment = TextAlignment.Center,
    };

    static Label MakeDateLabel() => new Label
    {
        Text                    = "",
        FontSize                = 9,
        TextColor               = Color.FromArgb("#7DB3D8"),
        HorizontalTextAlignment = TextAlignment.Center,
        Margin                  = new Thickness(4, 4, 4, 0),
    };

    // ── Expand / Collapse ─────────────────────────────────────────────────────

    void Toggle()
    {
        _expanded            = !_expanded;
        _cardsGrid.IsVisible = _expanded;
        _lblFooter.IsVisible = _expanded;
        _lblToggle.Text      = _expanded ? "−" : "+";
        ExpandedChanged?.Invoke(_expanded);
    }

    /// <summary>Collapse the panel (called when Combos button is tapped).</summary>
    public void Collapse() { if (_expanded) Toggle(); }

    /// <summary>Expand the panel (called on every app launch).</summary>
    public void Expand()
    {
        // Respect the Notifications setting
        if (!Preferences.Get(PrefShowPanel, true)) return;
        if (!_expanded) Toggle();
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        // Don't fetch if feature is disabled
        if (!Preferences.Get(PrefShowPanel, true)) return;

        string today    = DateTime.Today.ToString("yyyy-MM-dd");
        string lastDate = Preferences.Get(PrefDateKey, "");

        // Always fetch fresh jackpot data on launch
        bool needsFresh = true;

        // Show cached amounts immediately (no network wait)
        string cf5 = Preferences.Get("jd_f5", "");
        string csl = Preferences.Get("jd_sl", "");
        string cpb = Preferences.Get("jd_pb", "");
        string cmm = Preferences.Get("jd_mm", "");

        if (!string.IsNullOrEmpty(cf5)) _lblF5Amt.Text = cf5;
        if (!string.IsNullOrEmpty(csl)) _lblSLAmt.Text = csl;
        if (!string.IsNullOrEmpty(cpb)) _lblPBAmt.Text = cpb;
        if (!string.IsNullOrEmpty(cmm)) _lblMMAmt.Text = cmm;

        string cd5 = Preferences.Get("jd_f5_date", "");
        string cdsl = Preferences.Get("jd_sl_date", "");
        string cdpb = Preferences.Get("jd_pb_date", "");
        string cdmm = Preferences.Get("jd_mm_date", "");

        if (!string.IsNullOrEmpty(cd5))  _lblF5Date.Text = cd5;
        if (!string.IsNullOrEmpty(cdsl)) _lblSLDate.Text = cdsl;
        if (!string.IsNullOrEmpty(cdpb)) _lblPBDate.Text = cdpb;
        if (!string.IsNullOrEmpty(cdmm)) _lblMMDate.Text = cdmm;

        if (!string.IsNullOrEmpty(lastDate))
            _lblFooter.Text = $"Cached {lastDate}  •  tap header to hide";

        if (!needsFresh) return;

        // Fetch fresh jackpot amounts in background
        try
        {
            var jp = await GetDataEntry.GetNextJackpotAmounts();
            string ts = DateTime.Now.ToString("MMM d, yyyy  h:mm tt");

            // Only overwrite a game's label/cache when its own fetch actually returned a value.
            // The 5 jackpot requests (F5/SL/PB/MM/DD) fire concurrently alongside a bunch of other
            // draw-history requests at launch, and calottery.com can silently fail/time out a few
            // of them while others succeed — a per-game null here must NOT blank out a previously
            // known-good cached amount with "—"; just leave that game's label/cache untouched and
            // let the next launch's fetch try again.
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (jp.F5.HasValue) { _lblF5Amt.Text = FormatJackpot(jp.F5.Value); _lblF5Date.Text = jp.F5Date; Preferences.Set("jd_f5", _lblF5Amt.Text); Preferences.Set("jd_f5_date", jp.F5Date); }
                if (jp.SL.HasValue) { _lblSLAmt.Text = FormatJackpot(jp.SL.Value); _lblSLDate.Text = jp.SLDate; Preferences.Set("jd_sl", _lblSLAmt.Text); Preferences.Set("jd_sl_date", jp.SLDate); }
                if (jp.PB.HasValue) { _lblPBAmt.Text = FormatJackpot(jp.PB.Value); _lblPBDate.Text = jp.PBDate; Preferences.Set("jd_pb", _lblPBAmt.Text); Preferences.Set("jd_pb_date", jp.PBDate); }
                if (jp.MM.HasValue) { _lblMMAmt.Text = FormatJackpot(jp.MM.Value); _lblMMDate.Text = jp.MMDate; Preferences.Set("jd_mm", _lblMMAmt.Text); Preferences.Set("jd_mm_date", jp.MMDate); }
                _lblFooter.Text = $"Updated {ts}  •  tap header to hide";
            });

            Preferences.Set(PrefDateKey, today);
            Preferences.Set(PrefHourKey, DateTime.Now.Hour);
        }
        catch
        {
            // Network failure — cached values already showing
        }
    }

    static string FormatJackpot(decimal amount)
    {
        if (amount >= 1_000_000) return $"${amount / 1_000_000:0.##} MILLION";
        if (amount >= 1_000)     return $"${amount / 1_000:0.##}K";
        return $"${amount:N0}";
    }
}
