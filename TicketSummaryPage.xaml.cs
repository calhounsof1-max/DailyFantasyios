using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class TicketSummaryPage : ContentPage
{
    static readonly Dictionary<string, string> GameRoutes = new()
    {
        { "F5", nameof(WinnerPage) },
        { "SL", nameof(SuperLottoPage) },
        { "PB", nameof(PowerballPage) },
        { "MM", nameof(MegaMillionsPage) },
        { "D3", nameof(Daily3Page) },
        { "D4", nameof(Daily4Page) },
        { "DD", nameof(DailyDerbyPage) },
        { "SC", nameof(ScatchersPage) },
        { "HS", nameof(HotSpotPage) },
    };

    public TicketSummaryPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = ScanThenLoadAsync();
    }

    async Task ScanThenLoadAsync()
    {
        // Wait for any in-progress game page log write to finish before reading counts.
        if (TicketLogService.PendingWriteTask != null)
        {
            await TicketLogService.PendingWriteTask;
            TicketLogService.PendingWriteTask = null;
        }
        await TicketLogService.ScanAndLogTodayAsync();
        await LoadAsync();
    }

    async Task LoadAsync()
    {
        var totalCounts = await TicketCountService.GetCountByGameAsync();
        var todayCounts = await TicketCountService.GetTodayCountByGameAsync();
        int grandTotal = 0;
        int todayTotal = 0;

        gameRows.Children.Clear();

        foreach (var (key, name, color) in TicketCountService.Games)
        {
            int total = totalCounts.TryGetValue(key, out int tc) ? tc : 0;
            int today = todayCounts.TryGetValue(key, out int dc) ? dc : 0;
            grandTotal += total;
            todayTotal += today;

            bool hasOverride = TicketCountService.GetOverride(key) >= 0;
            string capturedKey = key;

            // Columns: 4(bar) | *(name) | 70(today) | 70(total) | 36(pencil) | 26(arrow)
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(4, GridUnitType.Absolute)),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(70, GridUnitType.Absolute)),
                    new ColumnDefinition(new GridLength(70, GridUnitType.Absolute)),
                    new ColumnDefinition(new GridLength(36, GridUnitType.Absolute)),
                    new ColumnDefinition(new GridLength(26, GridUnitType.Absolute)),
                },
                BackgroundColor = Color.FromArgb("#0D1B2A"),
                Padding = new Thickness(0, 0, 6, 0),
            };

            row.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await NavigateToGame(capturedKey))
            });

            // Color bar
            row.Add(new BoxView { BackgroundColor = Color.FromArgb(color), VerticalOptions = LayoutOptions.Fill });

            // Game name
            row.Add(new Label
            {
                Text = hasOverride ? $"{name} *" : name,
                TextColor = hasOverride ? Color.FromArgb("#FFD54F") : Colors.White,
                FontSize = 15,
                VerticalOptions = LayoutOptions.Center,
                Margin = new Thickness(14, 14, 0, 14),
            }, 1, 0);

            // Today count
            row.Add(new Label
            {
                Text = today > 0 ? today.ToString() : "—",
                TextColor = today > 0 ? Color.FromArgb("#64B5F6") : Color.FromArgb("#333333"),
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
            }, 2, 0);

            // Total count
            row.Add(new Label
            {
                Text = total > 0 ? total.ToString() : "—",
                TextColor = total > 0 ? Color.FromArgb(color) : Color.FromArgb("#555555"),
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
            }, 3, 0);

            // Pencil
            var pencil = new Label
            {
                Text = "✏",
                FontSize = 16,
                TextColor = Color.FromArgb("#4488AA"),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };
            pencil.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await EditCount(capturedKey))
            });
            row.Add(pencil, 4, 0);

            // Arrow
            row.Add(new Label
            {
                Text = "›",
                TextColor = Color.FromArgb("#445566"),
                FontSize = 20,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
            }, 5, 0);

            var wrapper = new VerticalStackLayout { Spacing = 0 };
            wrapper.Add(row);
            wrapper.Add(new BoxView { HeightRequest = 1, BackgroundColor = Color.FromArgb("#1E3A5F"), HorizontalOptions = LayoutOptions.Fill });
            gameRows.Add(wrapper);
        }

        lblTotal.Text = grandTotal.ToString();
        lblTodayTotal.Text = todayTotal > 0 ? todayTotal.ToString() : "0";
    }

    async Task EditCount(string key)
    {
        int current = TicketCountService.GetOverride(key);
        string currentStr = current >= 0 ? current.ToString() : "";

        string? result = await DisplayPromptAsync(
            "Edit Ticket Count",
            $"Enter total count for {key} (leave blank to use log count):",
            initialValue: currentStr,
            keyboard: Keyboard.Numeric,
            maxLength: 6);

        if (result == null) return;

        if (string.IsNullOrWhiteSpace(result))
            TicketCountService.ClearOverride(key);
        else if (int.TryParse(result.Trim(), out int val) && val >= 0)
            TicketCountService.SetOverride(key, val);

        await LoadAsync();
    }

    async Task NavigateToGame(string key)
    {
        if (GameRoutes.TryGetValue(key, out string? route))
            await Shell.Current.GoToAsync(route, false);
    }

    async void Back_Tapped(object sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("..", false);

    async void Stats_Tapped(object sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(nameof(TicketPurchaseStatsPage), false);
}
