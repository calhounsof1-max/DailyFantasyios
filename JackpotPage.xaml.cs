using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class JackpotPage : ContentPage
{
    bool _isPanning = false;
    double _panLeft;
    double _panRight;

    // Stored draw data for overlay detail view
    List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)> _f5Draws = new();
    List<(string DrawDate, int DrawNumber, int[] MainNumbers, int MegaNumber, DrawPrizeTier[] Prizes)> _slDraws = new();
    List<(string DrawDate, int DrawNumber, int[] MainNumbers, int PBNumber, DrawPrizeTier[] Prizes)> _pbDraws = new();
    List<(string DrawDate, int DrawNumber, int[] MainNumbers, int MegaNumber, DrawPrizeTier[] Prizes)> _mmDraws = new();
    List<(string DrawDate, int DrawNumber, int[] Horses, string RaceTime, DrawPrizeTier[] Prizes)> _ddDraws = new();
    List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)> _d3Draws = new();
    List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)> _d3EveDraws = new();
    List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)> _d3MidDraws = new();
    List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)> _d4Draws = new();

    // ── Game order ───────────────────────────────────────────────────────────
    const string PrefGameOrder = "jackpot_game_order";
    static readonly string[] DefaultGameOrder = { "F5", "SL", "PB", "MM", "DD", "D4", "D3E", "D3M" };

    static readonly Dictionary<string, string> GameDisplayNames = new()
    {
        ["F5"]  = "Fantasy 5",
        ["SL"]  = "Super Lotto Plus",
        ["PB"]  = "Powerball",
        ["MM"]  = "Mega Millions",
        ["DD"]  = "Daily Derby",
        ["D4"]  = "Daily 4",
        ["D3E"] = "Daily 3 – Evening",
        ["D3M"] = "Daily 3 – Midday",
    };

    static readonly Dictionary<string, Color> GameColors = new()
    {
        ["F5"]  = Color.FromArgb("#FF8F00"),
        ["SL"]  = Color.FromArgb("#7B1FA2"),
        ["PB"]  = Color.FromArgb("#C62828"),
        ["MM"]  = Color.FromArgb("#1565C0"),
        ["DD"]  = Color.FromArgb("#5D4037"),
        ["D4"]  = Color.FromArgb("#00695C"),
        ["D3E"] = Color.FromArgb("#1976D2"),
        ["D3M"] = Color.FromArgb("#1976D2"),
    };

    List<string> _gameOrder = new();

    public JackpotPage()
    {
        InitializeComponent();
    }

    public void PrePosition(bool fromRight)
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        TranslationX = fromRight ? w : -w;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _gameOrder = LoadGameOrder();
        ApplyGameOrder();
        await this.TranslateTo(0, 0, 220, Easing.CubicOut);
        await LoadAsync();
    }

    async Task LoadAsync()
    {
        // Try to show cached results immediately (populated by daily background update)
        if (GetDataEntry.TryLoadPrizeCache(
                out string cachedDate,
                out var cf5, out var csl, out var cpb, out var cmm, out var cdd, out var cd3, out var cd4,
                out var cjp))
        {
            _f5Draws = new() { (cf5.DrawDate, 0, cf5.Numbers, cf5.Prizes) };
            _slDraws = new() { (csl.DrawDate, 0, csl.Main, csl.Special, csl.Prizes) };
            _pbDraws = new() { (cpb.DrawDate, 0, cpb.Main, cpb.Special, cpb.Prizes) };
            _mmDraws = new() { (cmm.DrawDate, 0, cmm.Main, cmm.Special, cmm.Prizes) };
            _ddDraws = new() { (cdd.DrawDate, 0, cdd.Horses, cdd.RaceTime, cdd.Prizes) };
            _d3EveDraws = new() { (cd3.DrawDate, cd3.DrawNumber, cd3.Numbers, cd3.Prizes) };
            _d3MidDraws = new();
            _d4Draws = new() { (cd4.DrawDate, 0, cd4.Numbers, cd4.Prizes) };

            UpdateF5Card(_f5Draws, cjp.F5);
            UpdateSLCard(_slDraws, cjp.SL);
            UpdatePBCard(_pbDraws, cjp.PB);
            UpdateMMCard(_mmDraws, cjp.MM);
            UpdateDDCard(_ddDraws, cjp.DD);
            UpdateD3EveCard(_d3EveDraws);
            UpdateD3MidCard(_d3MidDraws);
            UpdateD4Card(_d4Draws);

            lblLastChecked.Text = $"Cached results from {cachedDate}";
            lblStatus.Text = "Refreshing...";
        }
        else
        {
            lblStatus.Text = "Checking jackpot results...";
            SetAllLoading();
        }

        progressBar.Progress = 0;
        progressBar.IsVisible = true;

        var f5Task  = GetDataEntry.GetPastDraws(3);
        var slTask  = GetDataEntry.GetSuperLottoDraws(3);
        var pbTask  = GetDataEntry.GetPowerballDraws(3);
        var mmTask  = GetDataEntry.GetMegaMillionsDraws(3);
        var ddTask  = GetDataEntry.GetDailyDerbyDraws(3);
        var d3Task  = GetDataEntry.GetDaily3Draws(3);
        var d4Task  = GetDataEntry.GetDaily4Draws(3);
        var jpTask  = GetDataEntry.GetNextJackpotAmounts();

        int done = 0;
        void OnOne() { done++; progressBar.Progress = done / 8.0; }

        await Task.WhenAll(
            f5Task.ContinueWith(_  => OnOne()),
            slTask.ContinueWith(_  => OnOne()),
            pbTask.ContinueWith(_  => OnOne()),
            mmTask.ContinueWith(_  => OnOne()),
            ddTask.ContinueWith(_  => OnOne()),
            d3Task.ContinueWith(_  => OnOne()),
            d4Task.ContinueWith(_  => OnOne()),
            jpTask.ContinueWith(_  => OnOne())
        );

        _f5Draws = f5Task.Result;
        _slDraws = slTask.Result;
        _pbDraws = pbTask.Result;
        _mmDraws = mmTask.Result;
        _ddDraws = ddTask.Result;
        _d3Draws = d3Task.Result;
        _d4Draws = d4Task.Result;

        SplitD3Draws(_d3Draws);

        var jp = jpTask.Result;
        UpdateF5Card(_f5Draws,  jp.F5);
        UpdateSLCard(_slDraws,  jp.SL);
        UpdatePBCard(_pbDraws,  jp.PB);
        UpdateMMCard(_mmDraws,  jp.MM);
        UpdateDDCard(_ddDraws, jp.DD);
        UpdateD3EveCard(_d3EveDraws);
        UpdateD3MidCard(_d3MidDraws);
        UpdateD4Card(_d4Draws);

        string checkedAt = DateTime.Now.ToString("h:mm tt");
        lblLastChecked.Text = $"Checked {DateTime.Today:MMM d, yyyy} at {checkedAt}";
        lblStatus.Text = "Done";
        progressBar.IsVisible = false;
    }

    void SetAllLoading()
    {
        foreach (var lbl in new[] { lblF5Badge, lblSLBadge, lblPBBadge, lblMMBadge, lblDDBadge, lblD3EveBadge, lblD3MidBadge, lblD4Badge })
        {
            lbl.Text = "...";
            lbl.BackgroundColor = Color.FromArgb("#9CA3AF");
        }
        foreach (var lbl in new[] { lblF5Date, lblSLDate, lblPBDate, lblMMDate, lblDDDate, lblD3EveDate, lblD3MidDate, lblD4Date })
            lbl.Text = "";
        foreach (var lbl in new[] { lblF5Numbers, lblSLNumbers, lblPBNumbers, lblMMNumbers, lblDDNumbers, lblD3EveNumbers, lblD3MidNumbers, lblD4Numbers })
            lbl.Text = "";
        foreach (var lbl in new[] { lblF5Result, lblSLResult, lblPBResult, lblMMResult, lblDDResult, lblD3EveResult, lblD3MidResult, lblD4Result })
            lbl.Text = "";
        foreach (var lbl in new[] { lblF5Jackpot, lblSLJackpot, lblPBJackpot, lblMMJackpot, lblDDJackpot })
            lbl.Text = "";
        foreach (var card in new[] { cardF5, cardSL, cardPB, cardMM, cardDD, cardD3Eve, cardD3Mid, cardD4 })
            card.BackgroundColor = Colors.White;
    }

    void UpdateF5Card(List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)> draws, decimal? nextJackpot)
    {
        if (draws.Count == 0) { SetCardError(cardF5, lblF5Badge, lblF5Date, lblF5Numbers, lblF5Result); return; }
        var d = draws[0];
        lblF5Date.Text    = d.DrawNumber > 0 ? $"{d.DrawDate}  •  Draw #{d.DrawNumber}" : d.DrawDate;
        lblF5Numbers.Text = string.Join("  ", d.Numbers.Select(n => n.ToString("D2")));
        var tier1 = d.Prizes.FirstOrDefault(p => p.Tier == 1);
        SetWinnerResult(cardF5, lblF5Badge, lblF5Result, tier1, "Match 5/5");
        lblF5Jackpot.Text = nextJackpot.HasValue ? $"Next jackpot: ${nextJackpot.Value:N0}" : "";
    }

    void UpdateSLCard(List<(string DrawDate, int DrawNumber, int[] MainNumbers, int MegaNumber, DrawPrizeTier[] Prizes)> draws, decimal? nextJackpot)
    {
        if (draws.Count == 0) { SetCardError(cardSL, lblSLBadge, lblSLDate, lblSLNumbers, lblSLResult); return; }
        var d = draws[0];
        lblSLDate.Text    = d.DrawNumber > 0 ? $"{d.DrawDate}  •  Draw #{d.DrawNumber}" : d.DrawDate;
        lblSLNumbers.Text = string.Join("  ", d.MainNumbers.Select(n => n.ToString("D2"))) +
                            "  +" + d.MegaNumber.ToString("D2");
        var tier1 = d.Prizes.FirstOrDefault(p => p.Tier == 1);
        SetWinnerResult(cardSL, lblSLBadge, lblSLResult, tier1, "Match 5+Mega");
        lblSLJackpot.Text = nextJackpot.HasValue ? $"Next jackpot: ${nextJackpot.Value:N0}" : "";
    }

    void UpdatePBCard(List<(string DrawDate, int DrawNumber, int[] MainNumbers, int PBNumber, DrawPrizeTier[] Prizes)> draws, decimal? nextJackpot)
    {
        if (draws.Count == 0) { SetCardError(cardPB, lblPBBadge, lblPBDate, lblPBNumbers, lblPBResult); return; }
        var d = draws[0];
        lblPBDate.Text    = d.DrawNumber > 0 ? $"{d.DrawDate}  •  Draw #{d.DrawNumber}" : d.DrawDate;
        lblPBNumbers.Text = string.Join("  ", d.MainNumbers.Select(n => n.ToString("D2"))) +
                            "  +" + d.PBNumber.ToString("D2");
        var tier1 = d.Prizes.FirstOrDefault(p => p.Tier == 1);
        SetWinnerResult(cardPB, lblPBBadge, lblPBResult, tier1, "Match 5+PB");
        lblPBJackpot.Text = nextJackpot.HasValue ? $"Next jackpot: ${nextJackpot.Value:N0}" : "";
    }

    void UpdateMMCard(List<(string DrawDate, int DrawNumber, int[] MainNumbers, int MegaNumber, DrawPrizeTier[] Prizes)> draws, decimal? nextJackpot)
    {
        if (draws.Count == 0) { SetCardError(cardMM, lblMMBadge, lblMMDate, lblMMNumbers, lblMMResult); return; }
        var d = draws[0];
        lblMMDate.Text    = d.DrawNumber > 0 ? $"{d.DrawDate}  •  Draw #{d.DrawNumber}" : d.DrawDate;
        lblMMNumbers.Text = string.Join("  ", d.MainNumbers.Select(n => n.ToString("D2"))) +
                            "  +" + d.MegaNumber.ToString("D2");
        var tier1 = d.Prizes.FirstOrDefault(p => p.Tier == 1);
        SetWinnerResult(cardMM, lblMMBadge, lblMMResult, tier1, "Match 5+Mega");
        lblMMJackpot.Text = nextJackpot.HasValue ? $"Next jackpot: ${nextJackpot.Value:N0}" : "";
    }

    void UpdateDDCard(List<(string DrawDate, int DrawNumber, int[] Horses, string RaceTime, DrawPrizeTier[] Prizes)> draws, decimal? nextJackpot)
    {
        if (draws.Count == 0) { SetCardError(cardDD, lblDDBadge, lblDDDate, lblDDNumbers, lblDDResult); return; }
        var d = draws[0];
        lblDDDate.Text = d.DrawNumber > 0 ? $"{d.DrawDate}  •  Draw #{d.DrawNumber}" : d.DrawDate;

        string horses = d.Horses.Length >= 3
            ? $"1st: {d.Horses[0]}  2nd: {d.Horses[1]}  3rd: {d.Horses[2]}"
            : string.Join("  ", d.Horses);
        lblDDNumbers.Text = string.IsNullOrEmpty(d.RaceTime)
            ? horses
            : horses + "   ⏱ " + d.RaceTime;

        var tier1 = d.Prizes.FirstOrDefault(p => p.Tier == 1);
        SetWinnerResult(cardDD, lblDDBadge, lblDDResult, tier1, "GRAND! (1st/2nd/3rd in order)");

        if (nextJackpot.HasValue)
            lblDDJackpot.Text = $"Next GRAND!: ~${nextJackpot.Value:N0}";
        else if (tier1 != null && tier1.Amount > 0)
            lblDDJackpot.Text = $"Last GRAND! prize: ${tier1.Amount:N0}";
        else
            lblDDJackpot.Text = "";
    }

    void SplitD3Draws(List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)> all)
    {
        _d3EveDraws.Clear();
        _d3MidDraws.Clear();
        // Within each date group: higher DrawNumber = Evening, lower = Midday
        foreach (var g in all.GroupBy(d => d.DrawDate))
        {
            var sorted = g.OrderByDescending(d => d.DrawNumber).ToList();
            _d3EveDraws.Add(sorted[0]);
            if (sorted.Count >= 2) _d3MidDraws.Add(sorted[1]);
        }
    }

    void UpdateD3EveCard(List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)> draws)
    {
        if (draws.Count == 0) { SetCardError(cardD3Eve, lblD3EveBadge, lblD3EveDate, lblD3EveNumbers, lblD3EveResult); return; }
        var d = draws[0];
        string drawLabel = d.DrawNumber > 0 ? $"  •  Draw #{d.DrawNumber} Evening" : "";
        lblD3EveDate.Text    = d.DrawDate + drawLabel;
        lblD3EveNumbers.Text = string.Join("  ", d.Numbers);
        var tier1 = d.Prizes.FirstOrDefault(p => p.Tier == 1);
        if (tier1 != null && tier1.Amount > 0)
        {
            lblD3EveBadge.Text            = $"${tier1.Amount:N0} Straight";
            lblD3EveBadge.BackgroundColor = Color.FromArgb("#1565C0");
            lblD3EveResult.Text           = $"Straight: ${tier1.Amount:N0}  •  {tier1.Count} winner{(tier1.Count != 1 ? "s" : "")}";
            lblD3EveResult.TextColor      = Color.FromArgb("#1565C0");
            cardD3Eve.BackgroundColor     = Color.FromArgb("#E3F2FD");
        }
        else
        {
            lblD3EveBadge.Text            = "N/A";
            lblD3EveBadge.BackgroundColor = Color.FromArgb("#9CA3AF");
            lblD3EveResult.Text           = "Prize data unavailable";
            lblD3EveResult.TextColor      = Color.FromArgb("#6B7280");
            cardD3Eve.BackgroundColor     = Colors.White;
        }
    }

    void UpdateD3MidCard(List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)> draws)
    {
        if (draws.Count == 0) { SetCardError(cardD3Mid, lblD3MidBadge, lblD3MidDate, lblD3MidNumbers, lblD3MidResult); return; }
        var d = draws[0];
        string drawLabel = d.DrawNumber > 0 ? $"  •  Draw #{d.DrawNumber} Midday" : "";
        lblD3MidDate.Text    = d.DrawDate + drawLabel;
        lblD3MidNumbers.Text = string.Join("  ", d.Numbers);
        var tier1 = d.Prizes.FirstOrDefault(p => p.Tier == 1);
        if (tier1 != null && tier1.Amount > 0)
        {
            lblD3MidBadge.Text            = $"${tier1.Amount:N0} Straight";
            lblD3MidBadge.BackgroundColor = Color.FromArgb("#1565C0");
            lblD3MidResult.Text           = $"Straight: ${tier1.Amount:N0}  •  {tier1.Count} winner{(tier1.Count != 1 ? "s" : "")}";
            lblD3MidResult.TextColor      = Color.FromArgb("#1565C0");
            cardD3Mid.BackgroundColor     = Color.FromArgb("#E3F2FD");
        }
        else
        {
            lblD3MidBadge.Text            = "N/A";
            lblD3MidBadge.BackgroundColor = Color.FromArgb("#9CA3AF");
            lblD3MidResult.Text           = "Prize data unavailable";
            lblD3MidResult.TextColor      = Color.FromArgb("#6B7280");
            cardD3Mid.BackgroundColor     = Colors.White;
        }
    }

    void UpdateD4Card(List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)> draws)
    {
        if (draws.Count == 0) { SetCardError(cardD4, lblD4Badge, lblD4Date, lblD4Numbers, lblD4Result); return; }
        var d = draws[0];
        lblD4Date.Text    = d.DrawNumber > 0 ? $"{d.DrawDate}  •  Draw #{d.DrawNumber}" : d.DrawDate;
        lblD4Numbers.Text = string.Join("  ", d.Numbers);
        var tier1 = d.Prizes.FirstOrDefault(p => p.Tier == 1);
        if (tier1 != null && tier1.Amount > 0)
        {
            lblD4Badge.Text            = $"${tier1.Amount:N0} Straight";
            lblD4Badge.BackgroundColor = Color.FromArgb("#00695C");
            lblD4Result.Text           = $"Straight: ${tier1.Amount:N0}  •  {tier1.Count} winner{(tier1.Count != 1 ? "s" : "")}";
            lblD4Result.TextColor      = Color.FromArgb("#00695C");
            cardD4.BackgroundColor     = Color.FromArgb("#E0F2F1");
        }
        else
        {
            lblD4Badge.Text            = "N/A";
            lblD4Badge.BackgroundColor = Color.FromArgb("#9CA3AF");
            lblD4Result.Text           = "Prize data unavailable";
            lblD4Result.TextColor      = Color.FromArgb("#6B7280");
            cardD4.BackgroundColor     = Colors.White;
        }
    }

    void SetWinnerResult(Frame card, Label badge, Label resultLbl, DrawPrizeTier? tier1, string matchLabel)
    {
        if (tier1 == null || tier1.Count == 0)
        {
            badge.Text            = "NO WINNER";
            badge.BackgroundColor = Color.FromArgb("#78909C");
            resultLbl.Text        = $"No {matchLabel} jackpot winner this draw";
            resultLbl.TextColor   = Color.FromArgb("#6B7280");
            card.BackgroundColor  = Colors.White;
        }
        else
        {
            int cnt = tier1.Count;
            badge.Text            = $"{cnt} WINNER{(cnt != 1 ? "S" : "")}";
            badge.BackgroundColor = Color.FromArgb("#1B5E20");
            resultLbl.Text        = $"{matchLabel}  •  {cnt} winner{(cnt != 1 ? "s" : "")}  •  ${tier1.Amount:N0} each";
            resultLbl.TextColor   = Color.FromArgb("#1B5E20");
            card.BackgroundColor  = Color.FromArgb("#F0FDF4");
        }
    }

    void SetCardError(Frame card, Label badge, Label dateLbl, Label numbersLbl, Label resultLbl)
    {
        badge.Text            = "N/A";
        badge.BackgroundColor = Color.FromArgb("#DC2626");
        dateLbl.Text          = "";
        numbersLbl.Text       = "";
        resultLbl.Text        = "Could not load data";
        resultLbl.TextColor   = Color.FromArgb("#9CA3AF");
        card.BackgroundColor  = Colors.White;
    }

    // ── Draw Results Overlay ─────────────────────────────────────────────────

    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        string gameCode = e.Parameter as string ?? "";
        ShowDrawDetail(gameCode);
    }

    void ShowDrawDetail(string gameCode)
    {
        overlayNumbersPanel.Children.Clear();
        overlayPrizesPanel.Children.Clear();
        overlayRaceTime.IsVisible = false;

        string gameName;
        string drawDate;
        Color gameColor;
        DrawPrizeTier[] prizes;

        switch (gameCode)
        {
            case "F5":
                if (_f5Draws.Count == 0) return;
                var f5 = _f5Draws[0];
                gameName  = "Fantasy 5";
                drawDate  = f5.DrawNumber > 0 ? $"{f5.DrawDate}  •  Draw #{f5.DrawNumber}" : f5.DrawDate;
                gameColor = Color.FromArgb("#FF8F00");
                prizes    = f5.Prizes;
                foreach (var n in f5.Numbers)
                    overlayNumbersPanel.Children.Add(MakeNumberBall(n, gameColor, false));
                break;

            case "SL":
                if (_slDraws.Count == 0) return;
                var sl = _slDraws[0];
                gameName  = "SuperLotto Plus";
                drawDate  = sl.DrawNumber > 0 ? $"{sl.DrawDate}  •  Draw #{sl.DrawNumber}" : sl.DrawDate;
                gameColor = Color.FromArgb("#7B1FA2");
                prizes    = sl.Prizes;
                foreach (var n in sl.MainNumbers)
                    overlayNumbersPanel.Children.Add(MakeNumberBall(n, gameColor, false));
                overlayNumbersPanel.Children.Add(MakePlusSeparator());
                overlayNumbersPanel.Children.Add(MakeNumberBall(sl.MegaNumber, gameColor, true));
                break;

            case "PB":
                if (_pbDraws.Count == 0) return;
                var pb = _pbDraws[0];
                gameName  = "Powerball";
                drawDate  = pb.DrawNumber > 0 ? $"{pb.DrawDate}  •  Draw #{pb.DrawNumber}" : pb.DrawDate;
                gameColor = Color.FromArgb("#C62828");
                prizes    = pb.Prizes;
                foreach (var n in pb.MainNumbers)
                    overlayNumbersPanel.Children.Add(MakeNumberBall(n, gameColor, false));
                overlayNumbersPanel.Children.Add(MakePlusSeparator());
                overlayNumbersPanel.Children.Add(MakeNumberBall(pb.PBNumber, gameColor, true));
                break;

            case "MM":
                if (_mmDraws.Count == 0) return;
                var mm = _mmDraws[0];
                gameName  = "Mega Millions";
                drawDate  = mm.DrawNumber > 0 ? $"{mm.DrawDate}  •  Draw #{mm.DrawNumber}" : mm.DrawDate;
                gameColor = Color.FromArgb("#1565C0");
                prizes    = mm.Prizes;
                foreach (var n in mm.MainNumbers)
                    overlayNumbersPanel.Children.Add(MakeNumberBall(n, gameColor, false));
                overlayNumbersPanel.Children.Add(MakePlusSeparator());
                overlayNumbersPanel.Children.Add(MakeNumberBall(mm.MegaNumber, gameColor, true));
                break;

            case "DD":
                if (_ddDraws.Count == 0) return;
                var dd = _ddDraws[0];
                gameName  = "Daily Derby";
                drawDate  = dd.DrawNumber > 0 ? $"{dd.DrawDate}  •  Draw #{dd.DrawNumber}" : dd.DrawDate;
                gameColor = Color.FromArgb("#5D4037");
                prizes    = dd.Prizes;
                string[] positions = { "1st", "2nd", "3rd" };
                for (int i = 0; i < dd.Horses.Length && i < 3; i++)
                    overlayNumbersPanel.Children.Add(MakeHorseBall(dd.Horses[i], positions[i], gameColor));
                if (!string.IsNullOrEmpty(dd.RaceTime))
                {
                    overlayRaceTime.Text      = "⏱  Race Time: " + dd.RaceTime;
                    overlayRaceTime.IsVisible = true;
                }
                break;

            case "D3E":
                if (_d3EveDraws.Count == 0) return;
                var d3e = _d3EveDraws[0];
                gameName  = "Daily 3 – Evening";
                drawDate  = d3e.DrawNumber > 0 ? $"{d3e.DrawDate}  •  Draw #{d3e.DrawNumber} Evening" : d3e.DrawDate;
                gameColor = Color.FromArgb("#1976D2");
                prizes    = d3e.Prizes;
                foreach (var n in d3e.Numbers)
                    overlayNumbersPanel.Children.Add(MakeNumberBall(n, gameColor, false));
                break;

            case "D3M":
                if (_d3MidDraws.Count == 0) return;
                var d3m = _d3MidDraws[0];
                gameName  = "Daily 3 – Midday";
                drawDate  = d3m.DrawNumber > 0 ? $"{d3m.DrawDate}  •  Draw #{d3m.DrawNumber} Midday" : d3m.DrawDate;
                gameColor = Color.FromArgb("#1976D2");
                prizes    = d3m.Prizes;
                foreach (var n in d3m.Numbers)
                    overlayNumbersPanel.Children.Add(MakeNumberBall(n, gameColor, false));
                break;

            case "D4":
                if (_d4Draws.Count == 0) return;
                var d4 = _d4Draws[0];
                gameName  = "Daily 4";
                drawDate  = d4.DrawNumber > 0 ? $"{d4.DrawDate}  •  Draw #{d4.DrawNumber}" : d4.DrawDate;
                gameColor = Color.FromArgb("#00695C");
                prizes    = d4.Prizes;
                foreach (var n in d4.Numbers)
                    overlayNumbersPanel.Children.Add(MakeNumberBall(n, gameColor, false));
                break;

            default:
                return;
        }

        overlayHeader.BackgroundColor = gameColor;
        overlayGameName.Text          = gameName;
        overlayGameName.TextColor     = gameColor;
        overlayDrawDate.Text          = drawDate;

        if (prizes != null && prizes.Length > 0)
        {
            foreach (var tier in prizes.OrderBy(p => p.Tier))
                overlayPrizesPanel.Children.Add(MakePrizeRow(gameCode, tier, gameColor));
        }
        else
        {
            overlayPrizesPanel.Children.Add(new Label
            {
                Text      = "Prize data not available for this draw",
                FontSize  = 13,
                TextColor = Color.FromArgb("#9CA3AF"),
                Padding   = new Thickness(0, 16),
                HorizontalOptions = LayoutOptions.Center
            });
        }

        overlayGrid.IsVisible = true;
    }

    View MakeNumberBall(int number, Color gameColor, bool isSpecial)
    {
        return new Frame
        {
            WidthRequest      = 46,
            HeightRequest     = 46,
            CornerRadius      = 23,
            BackgroundColor   = isSpecial ? gameColor : Colors.White,
            BorderColor       = isSpecial ? gameColor : Color.FromArgb("#DDDDDD"),
            HasShadow         = false,
            Padding           = new Thickness(0),
            Margin            = new Thickness(4, 4),
            Content           = new Label
            {
                Text                  = number.ToString("D2"),
                TextColor             = isSpecial ? Colors.White : gameColor,
                FontAttributes        = FontAttributes.Bold,
                FontSize              = 14,
                HorizontalOptions     = LayoutOptions.Fill,
                VerticalOptions       = LayoutOptions.Fill,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment   = TextAlignment.Center
            }
        };
    }

    View MakeHorseBall(int horseNumber, string position, Color gameColor)
    {
        var outer = new VerticalStackLayout
        {
            Spacing = 2,
            Margin  = new Thickness(6, 4),
            HorizontalOptions = LayoutOptions.Center
        };
        outer.Children.Add(new Label
        {
            Text              = position,
            FontSize          = 10,
            FontAttributes    = FontAttributes.Bold,
            TextColor         = gameColor,
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center
        });
        outer.Children.Add(new Frame
        {
            WidthRequest    = 46,
            HeightRequest   = 46,
            CornerRadius    = 23,
            BackgroundColor = Colors.White,
            BorderColor     = gameColor,
            HasShadow       = false,
            Padding         = new Thickness(0),
            Content         = new Label
            {
                Text                    = horseNumber.ToString(),
                TextColor               = gameColor,
                FontAttributes          = FontAttributes.Bold,
                FontSize                = 16,
                HorizontalOptions       = LayoutOptions.Fill,
                VerticalOptions         = LayoutOptions.Fill,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment   = TextAlignment.Center
            }
        });
        return outer;
    }

    View MakePlusSeparator()
    {
        return new Label
        {
            Text              = "+",
            FontSize          = 20,
            FontAttributes    = FontAttributes.Bold,
            TextColor         = Color.FromArgb("#9CA3AF"),
            VerticalOptions   = LayoutOptions.Center,
            Margin            = new Thickness(2, 0)
        };
    }

    View MakePrizeRow(string gameCode, DrawPrizeTier tier, Color gameColor)
    {
        string label    = GetTierLabel(gameCode, tier.Tier);
        string amount   = tier.Amount > 0 ? $"${tier.Amount:N0}" : "Free Play";
        bool isFreePlay = tier.Amount == 0;
        string winners  = tier.Count == 0
            ? "0 Winning Tickets"
            : tier.Count == 1 ? "1 Winner"
            : $"{tier.Count:N0} Winners";

        var matchLbl = new Label
        {
            Text           = label,
            FontAttributes = FontAttributes.Bold,
            FontSize       = 13,
            TextColor      = Color.FromArgb("#1E2733"),
            VerticalOptions = LayoutOptions.Start
        };

        var amountLbl = new Label
        {
            Text                    = amount,
            FontAttributes          = FontAttributes.Bold,
            FontSize                = 13,
            TextColor               = isFreePlay ? gameColor : Color.FromArgb("#1565C0"),
            HorizontalOptions       = LayoutOptions.End,
            HorizontalTextAlignment = TextAlignment.End
        };

        var winnersLbl = new Label
        {
            Text                    = winners,
            FontSize                = 12,
            TextColor               = Color.FromArgb("#6B7280"),
            HorizontalOptions       = LayoutOptions.End,
            HorizontalTextAlignment = TextAlignment.End
        };

        var rightStack = new VerticalStackLayout { Spacing = 1 };
        rightStack.Children.Add(amountLbl);
        rightStack.Children.Add(winnersLbl);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            Padding = new Thickness(0, 12, 0, 0)
        };
        row.Children.Add(matchLbl);
        Grid.SetColumn(matchLbl, 0);
        row.Children.Add(rightStack);
        Grid.SetColumn(rightStack, 1);

        var container = new VerticalStackLayout { Spacing = 0 };
        container.Children.Add(row);
        container.Children.Add(new BoxView
        {
            HeightRequest = 1,
            Color         = Color.FromArgb("#E5E7EB"),
            Margin        = new Thickness(0, 10, 0, 0)
        });
        return container;
    }

    static string GetTierLabel(string gameCode, int tier)
    {
        return gameCode switch
        {
            "F5" => tier switch
            {
                1 => "Matched 5 of 5 Numbers",
                2 => "Matched 4 of 5 Numbers",
                3 => "Matched 3 of 5 Numbers",
                4 => "Matched 2 of 5 Numbers",
                _ => $"Prize Tier {tier}"
            },
            "SL" => tier switch
            {
                1 => "Match 5 of 5 + Mega",
                2 => "Match 5 of 5",
                3 => "Match 4 of 5 + Mega",
                4 => "Match 4 of 5",
                5 => "Match 3 of 5 + Mega",
                6 => "Match 3 of 5",
                7 => "Match 2 of 5 + Mega",
                _ => $"Prize Tier {tier}"
            },
            "PB" => tier switch
            {
                1 => "Match 5 + Powerball",
                2 => "Match 5 of 5",
                3 => "Match 4 + Powerball",
                4 => "Match 4 of 5",
                5 => "Match 3 + Powerball",
                6 => "Match 3 of 5",
                7 => "Match 2 + Powerball",
                8 => "Match 1 + Powerball",
                9 => "Powerball Only",
                _ => $"Prize Tier {tier}"
            },
            "MM" => tier switch
            {
                1 => "Match 5 + Mega Ball",
                2 => "Match 5 of 5",
                3 => "Match 4 + Mega Ball",
                4 => "Match 4 of 5",
                5 => "Match 3 + Mega Ball",
                6 => "Match 3 of 5",
                7 => "Match 2 + Mega Ball",
                8 => "Match 1 + Mega Ball",
                9 => "Mega Ball Only",
                _ => $"Prize Tier {tier}"
            },
            "DD" => tier switch
            {
                1 => "GRAND! (Exact Order 1st-2nd-3rd)",
                2 => "1st & 2nd Place (Exact Order)",
                3 => "1st Place",
                4 => "Consolation",
                _ => $"Prize Tier {tier}"
            },
            "D3E" or "D3M" => tier switch
            {
                1 => "Straight",
                2 => "Box",
                3 => "Straight and Box",
                4 => "Box Only",
                _ => $"Prize Tier {tier}"
            },
            "D4" => tier switch
            {
                1 => "Straight",
                2 => "Box",
                3 => "Straight and Box",
                4 => "Box Only",
                _ => $"Prize Tier {tier}"
            },
            _ => $"Prize Tier {tier}"
        };
    }

    private void BtnCloseOverlay_Clicked(object sender, EventArgs e)
    {
        overlayGrid.IsVisible = false;
    }

    // ── Game order ───────────────────────────────────────────────────────────

    List<string> LoadGameOrder()
    {
        string saved = Preferences.Get(PrefGameOrder, "");
        if (!string.IsNullOrWhiteSpace(saved))
        {
            // Migrate old "D3" code to "D3E"+"D3M"
            var parts = saved.Split(',').ToList();
            int d3Idx = parts.IndexOf("D3");
            if (d3Idx >= 0)
            {
                parts[d3Idx] = "D3E";
                parts.Insert(d3Idx + 1, "D3M");
            }

            var order = parts.Where(c => DefaultGameOrder.Contains(c)).ToList();
            foreach (var code in DefaultGameOrder)
                if (!order.Contains(code)) order.Add(code);
            return order;
        }
        return DefaultGameOrder.ToList();
    }

    void SaveGameOrder() =>
        Preferences.Set(PrefGameOrder, string.Join(",", _gameOrder));

    Dictionary<string, View> GetCardMap() => new()
    {
        ["F5"]  = cardF5,
        ["SL"]  = cardSL,
        ["PB"]  = cardPB,
        ["MM"]  = cardMM,
        ["DD"]  = cardDD,
        ["D4"]  = cardD4,
        ["D3E"] = cardD3Eve,
        ["D3M"] = cardD3Mid,
    };

    void ApplyGameOrder()
    {
        var cardMap = GetCardMap();
        for (int i = cardsStack.Children.Count - 1; i >= 1; i--)
            cardsStack.Children.RemoveAt(i);
        foreach (var code in _gameOrder)
            if (cardMap.TryGetValue(code, out var card))
                cardsStack.Children.Add(card);
    }

    void BuildReorderRows()
    {
        reorderStack.Children.Clear();
        for (int i = 0; i < _gameOrder.Count; i++)
        {
            string code = _gameOrder[i];
            int idx = i;

            var nameLabel = new Label
            {
                Text = GameDisplayNames.TryGetValue(code, out var n) ? n : code,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = GameColors.TryGetValue(code, out var gc) ? gc : Colors.Black,
                VerticalOptions = LayoutOptions.Center
            };

            var btnUp = new Button
            {
                Text = "▲",
                FontSize = 13,
                BackgroundColor = i > 0 ? Color.FromArgb("#2D3D50") : Color.FromArgb("#9CA3AF"),
                TextColor = Colors.White,
                WidthRequest = 40,
                HeightRequest = 40,
                CornerRadius = 20,
                Padding = new Thickness(0),
                IsEnabled = i > 0
            };
            btnUp.Clicked += (s, e) => MoveGame(idx, -1);

            var btnDown = new Button
            {
                Text = "▼",
                FontSize = 13,
                BackgroundColor = i < _gameOrder.Count - 1 ? Color.FromArgb("#2D3D50") : Color.FromArgb("#9CA3AF"),
                TextColor = Colors.White,
                WidthRequest = 40,
                HeightRequest = 40,
                CornerRadius = 20,
                Padding = new Thickness(0),
                Margin = new Thickness(8, 0, 0, 0),
                IsEnabled = i < _gameOrder.Count - 1
            };
            btnDown.Clicked += (s, e) => MoveGame(idx, 1);

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto),
                },
                Padding = new Thickness(14, 12)
            };
            row.Add(nameLabel, 0, 0);
            row.Add(btnUp, 1, 0);
            row.Add(btnDown, 2, 0);

            reorderStack.Children.Add(new Frame
            {
                BackgroundColor = Colors.White,
                CornerRadius = 10,
                BorderColor = Color.FromArgb("#E5E7EB"),
                HasShadow = false,
                Padding = new Thickness(0),
                Content = row
            });
        }
    }

    void MoveGame(int fromIdx, int direction)
    {
        int toIdx = fromIdx + direction;
        if (toIdx < 0 || toIdx >= _gameOrder.Count) return;
        (_gameOrder[fromIdx], _gameOrder[toIdx]) = (_gameOrder[toIdx], _gameOrder[fromIdx]);
        BuildReorderRows();
        ApplyGameOrder();
    }

    private void BtnSort_Clicked(object sender, EventArgs e)
    {
        BuildReorderRows();
        reorderOverlay.IsVisible = true;
    }

    private void BtnReorderDone_Clicked(object sender, EventArgs e)
    {
        SaveGameOrder();
        reorderOverlay.IsVisible = false;
    }

    private void BtnReorderCancel_Clicked(object sender, EventArgs e)
    {
        SaveGameOrder();
        reorderOverlay.IsVisible = false;
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    private async void BtnBack_Clicked(object sender, EventArgs e) => await GoBackWithSlide();

    private async void BtnRefresh_Clicked(object sender, EventArgs e) => await LoadAsync();

    private async Task GoBackWithSlide()
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        Shell.Current.CurrentPage.TranslationX = -w;
        await Shell.Current.GoToAsync("..", false);
    }

    protected override bool OnBackButtonPressed()
    {
        if (reorderOverlay.IsVisible)
        {
            SaveGameOrder();
            reorderOverlay.IsVisible = false;
            return true;
        }
        if (overlayGrid.IsVisible)
        {
            overlayGrid.IsVisible = false;
            return true;
        }
        _ = GoBackWithSlide();
        return true;
    }

    private async void OnPagePan(object? sender, PanUpdatedEventArgs e)
    {
        if (_isPanning) return;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panLeft = _panRight = 0;
                break;
            case GestureStatus.Running:
                if (e.TotalX < _panLeft)  _panLeft  = e.TotalX;
                if (e.TotalX > _panRight) _panRight = e.TotalX;
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_panRight > 40 && !overlayGrid.IsVisible)
                {
                    _isPanning = true;
                    double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
                    Shell.Current.CurrentPage.TranslationX = -w;
                    await Shell.Current.GoToAsync("..", false);
                    _isPanning = false;
                }
                _panLeft = _panRight = 0;
                break;
        }
    }
}
