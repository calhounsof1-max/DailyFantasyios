// =====================================================================
//  StateLotteryPage.xaml.cs  ·  LotteryDirectory/
//  - Data is loaded in OnAppearing (never at app startup).
//  - Loading spinner + status message shown while data builds.
//  - Tapping any card or "Visit Site" opens the lottery website.
// =====================================================================

using System.Collections.ObjectModel;
using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI.LotteryDirectory;

public partial class StateLotteryPage : ContentPage
{
    private readonly ObservableCollection<StateLottery> _displayed = new();
    private string _activeRegion = "All";
    private string _searchText   = "";
    private bool   _loaded       = false;

    // ── Region chip accent colors (match app palette) ──────────────
    private static readonly Dictionary<string, Color> ChipColors = new()
    {
        ["All"]        = Color.FromArgb("#1A3F8A"),
        ["Northeast"]  = Color.FromArgb("#1565C0"),
        ["Southeast"]  = Color.FromArgb("#B71C1C"),
        ["Midwest"]    = Color.FromArgb("#1B5E20"),
        ["Southwest"]  = Color.FromArgb("#BF360C"),
        ["West"]       = Color.FromArgb("#4A148C"),
        ["No Lottery"] = Color.FromArgb("#424242"),
    };
    private readonly Dictionary<string, Border> _chips = new();

    // ── Loading overlay elements (created in code to keep XAML clean) ─
    private Grid    _loadingOverlay = null!;
    private Label   _loadingMsg     = null!;

    public StateLotteryPage()
    {
        InitializeComponent();
        LotteryGrid.ItemsSource = _displayed;
        BuildLoadingOverlay();
        BuildChips();
    }

    // ── Only load data when the page actually appears ─────────────────
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        TranslationX = 0;   // reset slide-out offset from back navigation (singleton page)
        if (_loaded) return;          // already loaded — nothing to do

        ShowLoading("Loading lotteries…");

        // Yield to let the spinner render before the (tiny) build runs
        await Task.Yield();

        // Build list on background thread so UI stays responsive
        var all = await Task.Run(() => StateLotteryDirectory.All);

        _loaded = true;
        HideLoading();

        int active = all.Count(s => s.HasLottery);
        LblActiveCount.Text = active.ToString();
        LblStats.Text       = $"{active} active lotteries  ·  {all.Count - active} states without";

        Refresh();
    }

    // ── Loading overlay ───────────────────────────────────────────────
    private void BuildLoadingOverlay()
    {
        _loadingMsg = new Label
        {
            Text            = "Loading…",
            TextColor       = Color.FromArgb("#60A5FA"),
            FontSize        = 14,
            HorizontalOptions = LayoutOptions.Center,
        };

        _loadingOverlay = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
            },
            BackgroundColor = Color.FromArgb("#0D1B2A"),
            IsVisible       = false,
        };

        var spinner = new ActivityIndicator
        {
            Color          = Color.FromArgb("#60A5FA"),
            IsRunning      = true,
            WidthRequest   = 48,
            HeightRequest  = 48,
            HorizontalOptions = LayoutOptions.Center,
        };

        Grid.SetRow(spinner,      1);
        Grid.SetRow(_loadingMsg,  2);

        _loadingOverlay.Children.Add(spinner);
        _loadingOverlay.Children.Add(_loadingMsg);
        _loadingMsg.Margin = new Thickness(0, 10, 0, 0);

        // Place the overlay on top of everything (row 3 spans full grid)
        var root = (Grid)Content;
        Grid.SetRow(_loadingOverlay, 0);
        Grid.SetRowSpan(_loadingOverlay, 4);
        root.Children.Add(_loadingOverlay);
    }

    private void ShowLoading(string message)
    {
        _loadingMsg.Text      = message;
        _loadingOverlay.IsVisible = true;
    }

    private void HideLoading() => _loadingOverlay.IsVisible = false;

    // ── Region filter chips ───────────────────────────────────────────
    private void BuildChips()
    {
        foreach (var region in StateLotteryDirectory.Regions)
        {
            var label = new Label
            {
                Text              = region,
                TextColor         = Color.FromArgb("#9CA3AF"),
                FontSize          = 12,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
            };

            var chip = new Border
            {
                BackgroundColor = Color.FromArgb("#0E2039"),
                StrokeShape     = new RoundRectangle { CornerRadius = new CornerRadius(20) },
                StrokeThickness = 1.5,
                Stroke          = ChipColors.TryGetValue(region, out var c) ? c : Colors.Gray,
                Padding         = new Thickness(14, 6),
                Content         = label,
            };

            chip.GestureRecognizers.Add(new TapGestureRecognizer
            {
                CommandParameter = region,
                Command          = new Command<string>(SelectRegion),
            });

            ChipBar.Add(chip);
            _chips[region] = chip;
        }

        SelectRegion("All");
    }

    private void SelectRegion(string region)
    {
        _activeRegion = region;

        foreach (var (key, chip) in _chips)
        {
            bool active = key == region;
            var accent  = ChipColors.TryGetValue(key, out var ac) ? ac : Colors.Gray;

            chip.BackgroundColor = active ? accent : Color.FromArgb("#0E2039");
            if (chip.Content is Label lbl)
                lbl.TextColor = active ? Colors.White : Color.FromArgb("#9CA3AF");
        }

        Refresh();
    }

    // ── Search ────────────────────────────────────────────────────────
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchText = e.NewTextValue ?? "";
        Refresh();
    }

    // ── Rebuild the displayed list ────────────────────────────────────
    private void Refresh()
    {
        if (!_loaded) return;

        _displayed.Clear();
        var results = StateLotteryDirectory.Filter(_activeRegion, _searchText).ToList();
        foreach (var item in results) _displayed.Add(item);

        int total = StateLotteryDirectory.All.Count;
        LblResultCount.Text = results.Count < total ? $"{results.Count}" : "";
    }

    // ── Back navigation ──────────────────────────────────────────────
    private async void BtnBack_Clicked(object sender, EventArgs e)
    {
        Shell.Current.CurrentPage.TranslationX =
            -DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        await Shell.Current.GoToAsync("..", false);
    }

    protected override bool OnBackButtonPressed()
    {
        _ = BtnBack_ClickedAsync();
        return true;
    }

    private async Task BtnBack_ClickedAsync()
    {
        Shell.Current.CurrentPage.TranslationX =
            -DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        await Shell.Current.GoToAsync("..", false);
    }

    // ── Open lottery website ──────────────────────────────────────────
    private async void OnVisitClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string url && !string.IsNullOrEmpty(url))
            await Launcher.OpenAsync(new Uri(url));
    }

    private async void OnCardTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is string url && !string.IsNullOrEmpty(url))
            await Launcher.OpenAsync(new Uri(url));
    }
}
