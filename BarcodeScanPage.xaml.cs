using DailyFantasyMAUI.Services;
using ZXing.Net.Maui;

namespace DailyFantasyMAUI;

public partial class BarcodeScanPage : ContentPage
{
    record GameDef(string Name, int Cols, int MaxMain, int? MaxBonus = null, string? BonusLabel = null);

    static readonly GameDef[] Games =
    [
        new("Fantasy 5",        5, 39),
        new("Super Lotto Plus", 5, 47, 27, "Mega"),
        new("Powerball",        5, 69, 26, "PB"),
        new("Daily 3",          3,  9),
        new("Daily 4",          4,  9),
        new("Daily Derby",      3, 12),
    ];

    int _gameIdx = 0;
    List<Entry[]> _playEntries = [];
    bool _isPanning = false;
    double _panLeft, _panRight;

    public BarcodeScanPage()
    {
        InitializeComponent();
        foreach (var g in Games) pickerGame.Items.Add(g.Name);
        pickerGame.SelectedIndex = 0;
        ShowEntryRows([]);
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
        StartScanning();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        barcodeView.IsDetecting = false;
    }

    void StartScanning()
    {
        scannerPanel.IsVisible = true;
        resultsPanel.IsVisible = false;
        resultCard.IsVisible   = false;
        btnRescan.IsVisible    = false;
        lblStatus.Text = "Align barcode within the frame";
        barcodeView.IsDetecting = true;
    }

    // ── Entry row management ──────────────────────────────────────────────────

    void ShowEntryRows(List<List<int>> plays)
    {
        var game = Games[_gameIdx];
        int totalCols = game.Cols + (game.MaxBonus.HasValue ? 1 : 0);
        int numPlays = Math.Max(1, plays.Count);

        stkNumberEntries.Children.Clear();
        _playEntries.Clear();

        for (int p = 0; p < numPlays; p++)
        {
            var play       = plays.Count > p ? plays[p] : [];
            var rowEntries = new Entry[totalCols];
            var hstack     = new HorizontalStackLayout { Spacing = 5, HorizontalOptions = LayoutOptions.Center };

            hstack.Children.Add(new Label
            {
                Text            = $"{(char)('A' + p)}:",
                FontSize        = 14, FontAttributes = FontAttributes.Bold,
                TextColor       = Color.FromArgb("#9CA3AF"),
                VerticalOptions = LayoutOptions.Center, WidthRequest = 22
            });

            for (int c = 0; c < totalCols; c++)
            {
                bool isBonus = game.MaxBonus.HasValue && c == game.Cols;
                int  pc = p, cc = c;
                var entry = new Entry
                {
                    WidthRequest            = 46, HeightRequest = 46, MaxLength = 2,
                    Keyboard                = Keyboard.Numeric,
                    TextColor               = Colors.White,
                    BackgroundColor         = isBonus
                        ? Color.FromArgb("#4A2060")
                        : Color.FromArgb("#374151"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    FontSize                = 18, FontAttributes = FontAttributes.Bold,
                    Text                    = play.Count > c ? play[c].ToString() : ""
                };
                entry.TextChanged += (_, _) =>
                {
                    if ((entry.Text?.Length ?? 0) >= 2)
                    {
                        if (cc + 1 < totalCols)              _playEntries[pc][cc + 1].Focus();
                        else if (pc + 1 < _playEntries.Count) _playEntries[pc + 1][0].Focus();
                    }
                };
                rowEntries[c] = entry;
                hstack.Children.Add(entry);
            }

            // Remove button (all rows except the first)
            if (p > 0)
            {
                int ri = p;
                var rmBtn = new Button
                {
                    Text = "✕", FontSize = 11,
                    BackgroundColor = Color.FromArgb("#4B5563"), TextColor = Colors.White,
                    WidthRequest = 32, HeightRequest = 46, CornerRadius = 6,
                    Padding = new Thickness(0)
                };
                rmBtn.Clicked += (_, _) =>
                {
                    var cur = ReadCurrentValues();
                    cur.RemoveAt(ri);
                    ShowEntryRows(cur);
                };
                hstack.Children.Add(rmBtn);
            }

            _playEntries.Add(rowEntries);
            stkNumberEntries.Children.Add(hstack);
        }

        lblEditNote.Text = $"{numPlays} play set(s) — tap any box to edit";
    }

    List<List<int>> ReadCurrentValues() =>
        _playEntries.Select(row =>
            row.Select(e => int.TryParse(e.Text, out int n) ? n : 0).ToList()
        ).ToList();

    void BtnAddRow_Clicked(object sender, EventArgs e)
    {
        var cur = ReadCurrentValues();
        cur.Add([]);
        ShowEntryRows(cur);
    }

    List<int[]> GetAllNumberSets()
    {
        var game = Games[_gameIdx];
        var sets = new List<int[]>();
        foreach (var row in _playEntries)
        {
            var nums = row.Take(game.Cols)
                .Select(e => int.TryParse(e.Text?.Trim(), out int n) ? n : -1)
                .Where(n => n >= (game.MaxMain <= 9 ? 0 : 1) && n <= game.MaxMain)
                .ToArray();
            if (nums.Length >= (game.MaxMain <= 9 ? 1 : 2)) sets.Add(nums);
        }
        return sets;
    }

    // ── Barcode detection ─────────────────────────────────────────────────────

    private void BarcodeView_BarcodesDetected(object sender, BarcodeDetectionEventArgs e)
    {
        var result = e.Results?.FirstOrDefault();
        if (result == null) return;

        barcodeView.IsDetecting = false;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            string raw = result.Value ?? "";
            lblRawValue.Text      = raw;
            lblBarcodeFormat.Text = $"Format: {result.Format}";

            // CA Lottery barcodes don't encode play numbers in plain text.
            // Show the right number of empty rows for the selected game.
            ShowEntryRows(DefaultEmptyRows());

            scannerPanel.IsVisible = false;
            resultsPanel.IsVisible = true;
            btnRescan.IsVisible    = true;
            resultCard.IsVisible   = false;

            lblStatus.Text = "Enter numbers from each row on your ticket, then CHECK TICKET";
        });
    }

    // Returns the typical number of play sets per ticket for each game.
    // Fantasy 5 / Super Lotto / Powerball tickets have 5 sets (A–E).
    // Daily 3/4/Derby tickets have 1 set by default.
    List<List<int>> DefaultEmptyRows()
    {
        int rows = _gameIdx switch { 0 or 1 or 2 => 5, _ => 1 };
        return Enumerable.Range(0, rows).Select(_ => new List<int>()).ToList();
    }

    private void GamePicker_Changed(object sender, EventArgs e)
    {
        _gameIdx = Math.Max(0, pickerGame.SelectedIndex);
        // Reset to default empty rows for new game — column count may differ
        ShowEntryRows(DefaultEmptyRows());
        resultCard.IsVisible = false;
    }

    // ── Ticket check ──────────────────────────────────────────────────────────

    private async void BtnCheck_Clicked(object sender, EventArgs e)
    {
        var sets = GetAllNumberSets();
        if (sets.Count == 0)
        {
            lblStatus.Text = "Enter numbers in the boxes above first";
            return;
        }
        lblStatus.Text    = "Checking all draws…";
        btnCheck.IsEnabled = false;
        try { await CheckTicketAsync(sets); }
        finally { btnCheck.IsEnabled = true; }
    }

    async Task CheckTicketAsync(List<int[]> sets)
    {
        switch (_gameIdx)
        {
            case 0: // Fantasy 5
            {
                var draws = await GetDataEntry.GetPastDraws(90);
                if (draws.Count == 0) { lblStatus.Text = "Could not load draw data"; return; }
                var results = draws
                    .SelectMany(d => sets.Select((s, si) =>
                        (d.DrawDate, Winning: d.Numbers, SetIdx: si, TicketSet: s,
                         Matched: s.Intersect(d.Numbers).Count())))
                    .ToList();
                ShowAllDrawResults("Fantasy 5", results, 5);
                break;
            }
            case 1: // Super Lotto Plus
            {
                var draws = await GetDataEntry.GetSuperLottoDraws(90);
                if (draws.Count == 0) { lblStatus.Text = "Could not load draw data"; return; }
                var results = draws
                    .SelectMany(d => sets.Select((s, si) =>
                    {
                        int matched = s.Take(5).Intersect(d.MainNumbers).Count();
                        // bonus match counts as extra match for ranking
                        if (s.Length >= 6 && s[5] == d.MegaNumber) matched++;
                        return (d.DrawDate, Winning: d.MainNumbers, SetIdx: si, TicketSet: s, Matched: matched);
                    }))
                    .ToList();
                ShowAllDrawResults("Super Lotto Plus", results, 5);
                break;
            }
            case 2: // Powerball
            {
                var draws = await GetDataEntry.GetPowerballDraws(90);
                if (draws.Count == 0) { lblStatus.Text = "Could not load draw data"; return; }
                var results = draws
                    .SelectMany(d => sets.Select((s, si) =>
                    {
                        int matched = s.Take(5).Intersect(d.MainNumbers).Count();
                        if (s.Length >= 6 && s[5] == d.PBNumber) matched++;
                        return (d.DrawDate, Winning: d.MainNumbers, SetIdx: si, TicketSet: s, Matched: matched);
                    }))
                    .ToList();
                ShowAllDrawResults("Powerball", results, 5);
                break;
            }
            case 3: // Daily 3
            {
                var draws = await GetDataEntry.GetDaily3Draws(90);
                if (draws.Count == 0) { lblStatus.Text = "Could not load draw data"; return; }
                var results = draws
                    .SelectMany(d => sets.Select((s, si) =>
                    {
                        // Exact order match = 3, any match = count
                        bool exact = s.Length >= 3 && s[0] == d.Numbers[0] && s[1] == d.Numbers[1] && s[2] == d.Numbers[2];
                        int matched = exact ? 3 : s.Take(3).Intersect(d.Numbers).Count();
                        return (d.DrawDate, Winning: d.Numbers, SetIdx: si, TicketSet: s, Matched: matched);
                    }))
                    .ToList();
                ShowAllDrawResults("Daily 3", results, 3);
                break;
            }
            case 4: // Daily 4
            {
                var draws = await GetDataEntry.GetDaily4Draws(90);
                if (draws.Count == 0) { lblStatus.Text = "Could not load draw data"; return; }
                var results = draws
                    .SelectMany(d => sets.Select((s, si) =>
                        (d.DrawDate, Winning: d.Numbers, SetIdx: si, TicketSet: s,
                         Matched: s.Take(4).Intersect(d.Numbers).Count())))
                    .ToList();
                ShowAllDrawResults("Daily 4", results, 4);
                break;
            }
            case 5: // Daily Derby
            {
                var draws = await GetDataEntry.GetDailyDerbyDraws(90);
                if (draws.Count == 0) { lblStatus.Text = "Could not load draw data"; return; }
                var results = draws
                    .SelectMany(d => sets.Select((s, si) =>
                        (d.DrawDate, Winning: d.Horses, SetIdx: si, TicketSet: s,
                         Matched: s.Take(3).Intersect(d.Horses).Count())))
                    .ToList();
                ShowAllDrawResults("Daily Derby", results, 3);
                break;
            }
        }
    }

    void ShowAllDrawResults(string gameName,
        List<(string Date, int[] Winning, int SetIdx, int[] TicketSet, int Matched)> results,
        int needed)
    {
        int minWin = needed <= 3 ? needed : 2;   // Daily 3/Derby need exact; others need 2+
        var wins = results
            .Where(r => r.Matched >= minWin)
            .OrderByDescending(r => r.Matched)
            .ThenByDescending(r => r.Date)
            .ToList();

        int drawsChecked = results.Select(r => r.Date).Distinct().Count();
        int setsChecked  = _playEntries.Count;

        lblResultGame.Text = gameName;
        lblResultDraw.Text = $"Checked {drawsChecked} draw(s) × {setsChecked} set(s)";
        stkDrawResults.Children.Clear();

        if (wins.Count > 0)
        {
            lblResultOutcome.Text      = $"WINNER!  {wins.Count} matching draw(s) found";
            lblResultOutcome.TextColor = Color.FromArgb("#1B5E20");
            resultCard.BackgroundColor = Color.FromArgb("#F0FDF4");
            lblResultPrize.Text        = "";

            foreach (var w in wins.Take(10))
            {
                char   setLetter = (char)('A' + w.SetIdx);
                string winNums   = string.Join(" ", w.Winning.Select(n => n.ToString("D2")));
                stkDrawResults.Children.Add(new Label
                {
                    Text            = $"  Set {setLetter}  {w.Date}  {w.Matched}/{needed} matched  [{winNums}]",
                    FontSize        = 11,
                    TextColor       = Color.FromArgb("#1565C0"),
                    FontFamily      = "Monospace",
                    BackgroundColor = Color.FromArgb("#EFF6FF"),
                    Padding         = new Thickness(4, 2)
                });
            }
            if (wins.Count > 10)
                stkDrawResults.Children.Add(new Label
                {
                    Text = $"  … and {wins.Count - 10} more winning draws",
                    FontSize = 11, TextColor = Color.FromArgb("#6B7280")
                });

            lblStatus.Text = $"WINNER! {wins.Count} win(s) across {drawsChecked} draws";
        }
        else
        {
            var best = results.OrderByDescending(r => r.Matched).First();
            lblResultOutcome.Text      = $"No win across {drawsChecked} draws";
            lblResultOutcome.TextColor = Color.FromArgb("#6B7280");
            resultCard.BackgroundColor = Colors.White;
            char bestSet = (char)('A' + best.SetIdx);
            lblResultPrize.Text = $"Best: Set {bestSet} matched {best.Matched}/{needed} on {best.Date}";
            lblStatus.Text      = $"Not a winner — checked {drawsChecked} draws";
        }

        resultCard.IsVisible = true;
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    private void BtnRescan_Clicked(object sender, EventArgs e)
    {
        resultCard.IsVisible = false;
        StartScanning();
    }

    private void BtnBack_Clicked(object sender, EventArgs e) => _ = GoBackWithSlide();

    private async Task GoBackWithSlide()
    {
        barcodeView.IsDetecting = false;
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
                break;
        }
    }
}
