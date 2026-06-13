using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class JackpotPage : ContentPage
{
    bool _isPanning = false;
    double _panLeft;
    double _panRight;

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
        await this.TranslateTo(0, 0, 220, Easing.CubicOut);
        await LoadAsync();
    }

    async Task LoadAsync()
    {
        lblStatus.Text = "Checking jackpot results...";
        progressBar.Progress = 0;
        progressBar.IsVisible = true;
        SetAllLoading();

        var f5Task  = GetDataEntry.GetPastDraws(3);
        var slTask  = GetDataEntry.GetSuperLottoDraws(3);
        var pbTask  = GetDataEntry.GetPowerballDraws(3);
        var mmTask  = GetDataEntry.GetMegaMillionsDraws(3);
        var ddTask  = GetDataEntry.GetDailyDerbyDraws(3);
        var jpTask  = GetDataEntry.GetNextJackpotAmounts();

        int done = 0;
        void OnOne() { done++; progressBar.Progress = done / 6.0; }

        await Task.WhenAll(
            f5Task.ContinueWith(_  => OnOne()),
            slTask.ContinueWith(_  => OnOne()),
            pbTask.ContinueWith(_  => OnOne()),
            mmTask.ContinueWith(_  => OnOne()),
            ddTask.ContinueWith(_  => OnOne()),
            jpTask.ContinueWith(_  => OnOne())
        );

        var jp = jpTask.Result;
        UpdateF5Card(f5Task.Result,  jp.F5);
        UpdateSLCard(slTask.Result,  jp.SL);
        UpdatePBCard(pbTask.Result,  jp.PB);
        UpdateMMCard(mmTask.Result,  jp.MM);
        UpdateDDCard(ddTask.Result, jp.DD);

        string checkedAt = DateTime.Now.ToString("h:mm tt");
        lblLastChecked.Text = $"Checked {DateTime.Today:MMM d, yyyy} at {checkedAt}";
        lblStatus.Text = "Done";
        progressBar.IsVisible = false;
    }

    void SetAllLoading()
    {
        foreach (var lbl in new[] { lblF5Badge, lblSLBadge, lblPBBadge, lblMMBadge, lblDDBadge })
        {
            lbl.Text = "...";
            lbl.BackgroundColor = Color.FromArgb("#9CA3AF");
        }
        foreach (var lbl in new[] { lblF5Date, lblSLDate, lblPBDate, lblMMDate, lblDDDate })
            lbl.Text = "";
        foreach (var lbl in new[] { lblF5Numbers, lblSLNumbers, lblPBNumbers, lblMMNumbers, lblDDNumbers })
            lbl.Text = "";
        foreach (var lbl in new[] { lblF5Result, lblSLResult, lblPBResult, lblMMResult, lblDDResult })
            lbl.Text = "";
        foreach (var lbl in new[] { lblF5Jackpot, lblSLJackpot, lblPBJackpot, lblMMJackpot, lblDDJackpot })
            lbl.Text = "";
        foreach (var card in new[] { cardF5, cardSL, cardPB, cardMM, cardDD })
            card.BackgroundColor = Colors.White;
    }

    void UpdateF5Card(List<(string DrawDate, int[] Numbers, DrawPrizeTier[] Prizes)> draws, decimal? nextJackpot)
    {
        if (draws.Count == 0) { SetCardError(cardF5, lblF5Badge, lblF5Date, lblF5Numbers, lblF5Result); return; }
        var d = draws[0];
        lblF5Date.Text    = d.DrawDate;
        lblF5Numbers.Text = string.Join("  ", d.Numbers.Select(n => n.ToString("D2")));
        var tier1 = d.Prizes.FirstOrDefault(p => p.Tier == 1);
        SetWinnerResult(cardF5, lblF5Badge, lblF5Result, tier1, "Match 5/5");
        lblF5Jackpot.Text = nextJackpot.HasValue ? $"Next jackpot: ${nextJackpot.Value:N0}" : "";
    }

    void UpdateSLCard(List<(string DrawDate, int[] MainNumbers, int MegaNumber, DrawPrizeTier[] Prizes)> draws, decimal? nextJackpot)
    {
        if (draws.Count == 0) { SetCardError(cardSL, lblSLBadge, lblSLDate, lblSLNumbers, lblSLResult); return; }
        var d = draws[0];
        lblSLDate.Text    = d.DrawDate;
        lblSLNumbers.Text = string.Join("  ", d.MainNumbers.Select(n => n.ToString("D2"))) +
                            "  +" + d.MegaNumber.ToString("D2");
        var tier1 = d.Prizes.FirstOrDefault(p => p.Tier == 1);
        SetWinnerResult(cardSL, lblSLBadge, lblSLResult, tier1, "Match 5+Mega");
        lblSLJackpot.Text = nextJackpot.HasValue ? $"Next jackpot: ${nextJackpot.Value:N0}" : "";
    }

    void UpdatePBCard(List<(string DrawDate, int[] MainNumbers, int PBNumber, DrawPrizeTier[] Prizes)> draws, decimal? nextJackpot)
    {
        if (draws.Count == 0) { SetCardError(cardPB, lblPBBadge, lblPBDate, lblPBNumbers, lblPBResult); return; }
        var d = draws[0];
        lblPBDate.Text    = d.DrawDate;
        lblPBNumbers.Text = string.Join("  ", d.MainNumbers.Select(n => n.ToString("D2"))) +
                            "  +" + d.PBNumber.ToString("D2");
        var tier1 = d.Prizes.FirstOrDefault(p => p.Tier == 1);
        SetWinnerResult(cardPB, lblPBBadge, lblPBResult, tier1, "Match 5+PB");
        lblPBJackpot.Text = nextJackpot.HasValue ? $"Next jackpot: ${nextJackpot.Value:N0}" : "";
    }

    void UpdateMMCard(List<(string DrawDate, int[] MainNumbers, int MegaNumber, DrawPrizeTier[] Prizes)> draws, decimal? nextJackpot)
    {
        if (draws.Count == 0) { SetCardError(cardMM, lblMMBadge, lblMMDate, lblMMNumbers, lblMMResult); return; }
        var d = draws[0];
        lblMMDate.Text    = d.DrawDate;
        lblMMNumbers.Text = string.Join("  ", d.MainNumbers.Select(n => n.ToString("D2"))) +
                            "  +" + d.MegaNumber.ToString("D2");
        var tier1 = d.Prizes.FirstOrDefault(p => p.Tier == 1);
        SetWinnerResult(cardMM, lblMMBadge, lblMMResult, tier1, "Match 5+Mega");
        lblMMJackpot.Text = nextJackpot.HasValue ? $"Next jackpot: ${nextJackpot.Value:N0}" : "";
    }

    void UpdateDDCard(List<(string DrawDate, int[] Horses, string RaceTime, DrawPrizeTier[] Prizes)> draws, decimal? nextJackpot)
    {
        if (draws.Count == 0) { SetCardError(cardDD, lblDDBadge, lblDDDate, lblDDNumbers, lblDDResult); return; }
        var d = draws[0];
        lblDDDate.Text = d.DrawDate;

        // Show horses with position labels and race time on a separate line
        string horses = d.Horses.Length >= 3
            ? $"1st: {d.Horses[0]}  2nd: {d.Horses[1]}  3rd: {d.Horses[2]}"
            : string.Join("  ", d.Horses);
        lblDDNumbers.Text = string.IsNullOrEmpty(d.RaceTime)
            ? horses
            : horses + "   ⏱ " + d.RaceTime;

        var tier1 = d.Prizes.FirstOrDefault(p => p.Tier == 1);
        SetWinnerResult(cardDD, lblDDBadge, lblDDResult, tier1, "GRAND! (1st/2nd/3rd in order)");

        // Next jackpot: use API amount if available, otherwise show last GRAND! prize as estimate
        if (nextJackpot.HasValue)
            lblDDJackpot.Text = $"Next GRAND!: ~${nextJackpot.Value:N0}";
        else if (tier1 != null && tier1.Amount > 0)
            lblDDJackpot.Text = $"Last GRAND! prize: ${tier1.Amount:N0}";
        else
            lblDDJackpot.Text = "";
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
                if (_panRight > 40)
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
