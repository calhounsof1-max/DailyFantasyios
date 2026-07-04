using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class PrintPreviewPage : ContentPage
{
    // ── Checkboxes ────────────────────────────────────────────────────────────
    CheckBox _chkSets      = new() { IsChecked = true,  Color = Color.FromArgb("#FF8F00") };
    CheckBox _chkFavorites = new() { IsChecked = true,  Color = Color.FromArgb("#6A1B9A") };
    CheckBox _chkPicks     = new() { IsChecked = true,  Color = Color.FromArgb("#1565C0") };
    CheckBox _chkSummary   = new() { IsChecked = true,  Color = Color.FromArgb("#2E7D32") };
    CheckBox _chkDataFile  = new() { IsChecked = false, Color = Color.FromArgb("#455A64") };

    Picker   _dataFilePicker = new() { IsEnabled = false, TextColor = Color.FromArgb("#1E2733"),
                                       BackgroundColor = Color.FromArgb("#E8EDF3"), HeightRequest = 40 };

    Label    _statusLabel    = new() { FontSize = 13, TextColor = Color.FromArgb("#94B8D8"),
                                       HorizontalOptions = LayoutOptions.Center,
                                       HorizontalTextAlignment = TextAlignment.Center };

    ActivityIndicator _spinner = new() { Color = Color.FromArgb("#FF8F00"),
                                         WidthRequest = 36, HeightRequest = 36,
                                         IsVisible = false };

    string? _lastHtmlPath;   // path to last generated HTML file

    public PrintPreviewPage()
    {
        InitializeComponent();
        Content = BuildUI();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshDataFilePicker();
    }

    // ── Build entire UI in code ───────────────────────────────────────────────

    View BuildUI()
    {
        // ── Header bar ───────────────────────────────────────────────────────
        var btnBack = MakeBtn("◀  Back", "#FF8F00", 90, 38);
        btnBack.Clicked += async (_, _) => await Shell.Current.GoToAsync("..", false);

        var header = new Grid
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Padding = new Thickness(10, 10),
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(90)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(90)),
            }
        };
        header.Children.Add(btnBack);
        Grid.SetColumn(btnBack, 0);

        var title = new Label
        {
            Text = "Print Report",
            FontSize = 16, FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        header.Children.Add(title);
        Grid.SetColumn(title, 1);

        // ── Options panel ─────────────────────────────────────────────────────
        _chkDataFile.CheckedChanged += (_, e) => _dataFilePicker.IsEnabled = e.Value;

        var row1 = CheckRow(_chkSets,      "My Sets");
        var row2 = CheckRow(_chkFavorites, "My Favorites");
        var row3 = CheckRow(_chkPicks,     "Main Page Picks");
        var row4 = CheckRow(_chkSummary,   "Winnings Summary");

        var fileRow = new HorizontalStackLayout
        {
            Spacing = 6, Margin = new Thickness(0, 2),
            Children = { _chkDataFile, LabelFor("Data File:"), _dataFilePicker }
        };

        var optionsPanel = new VerticalStackLayout
        {
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            Padding = new Thickness(14, 12),
            Spacing = 4,
            Children =
            {
                new Label
                {
                    Text = "Select what to include:",
                    FontSize = 13, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#1E2733"),
                    Margin = new Thickness(0, 0, 0, 6),
                },
                row1, row2, row3, row4, fileRow,
            }
        };

        // ── Buttons ───────────────────────────────────────────────────────────
        var btnGenerate = MakeBtn("📄  Share Report (Open in Safari)", "#FF8F00", -1, 50);
        btnGenerate.FontSize = 14;
        btnGenerate.Margin = new Thickness(14, 0);
        btnGenerate.Clicked += BtnGenerate_Clicked;

        var btnPrint = MakeBtn("🖨  Print via AirPrint", "#1565C0", -1, 46);
        btnPrint.FontSize = 13;
        btnPrint.Margin = new Thickness(14, 0);
        btnPrint.Clicked += BtnPrint_Clicked;

        var btnShare = MakeBtn("📤  Share HTML File", "#455A64", -1, 46);
        btnShare.FontSize = 13;
        btnShare.Margin = new Thickness(14, 0);
        btnShare.Clicked += BtnShare_Clicked;

        // ── Status + spinner ──────────────────────────────────────────────────
        var statusRow = new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Center,
            Spacing = 6,
            Margin = new Thickness(0, 8),
            Children = { _spinner, _statusLabel }
        };

        // ── Instruction card ──────────────────────────────────────────────────
        var instruction = new Label
        {
            Text = "Tap \"Share Report\" to open the HTML in Safari.\n" +
                   "Use Safari's share button → Print to print or save PDF.\n" +
                   "Or tap \"Print via AirPrint\" to print directly.",
            FontSize = 12,
            TextColor = Color.FromArgb("#607D8B"),
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(20, 4),
            LineBreakMode = LineBreakMode.WordWrap,
        };

        // ── Full scrollable body ──────────────────────────────────────────────
        var body = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                BackgroundColor = Color.FromArgb("#0D1B2A"),
                Spacing = 0,
                Children =
                {
                    optionsPanel,
                    new BoxView { HeightRequest = 1, Color = Color.FromArgb("#1E3A5F") },
                    new VerticalStackLayout
                    {
                        BackgroundColor = Color.FromArgb("#0D1B2A"),
                        Padding = new Thickness(0, 16),
                        Spacing = 10,
                        Children = { btnGenerate, btnPrint, btnShare, statusRow, instruction }
                    }
                }
            }
        };

        // ── Root layout ───────────────────────────────────────────────────────
        var root = new Grid { BackgroundColor = Color.FromArgb("#0D1B2A") };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));  // header
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));  // body

        root.Children.Add(header);
        Grid.SetRow(header, 0);

        root.Children.Add(body);
        Grid.SetRow(body, 1);

        return root;
    }

    static Button MakeBtn(string text, string color, double width, double height)
    {
        var b = new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb(color),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            CornerRadius = 10,
            HeightRequest = height,
        };
        if (width > 0) b.WidthRequest = width;
        else b.HorizontalOptions = LayoutOptions.Fill;
        return b;
    }

    static HorizontalStackLayout CheckRow(CheckBox chk, string label) =>
        new() { Spacing = 6, Margin = new Thickness(0, 2),
                Children = { chk, LabelFor(label) } };

    static Label LabelFor(string text) =>
        new() { Text = text, FontSize = 13, TextColor = Color.FromArgb("#1E2733"),
                VerticalOptions = LayoutOptions.Center };

    // ── Populate data file picker ─────────────────────────────────────────────

    void RefreshDataFilePicker()
    {
        var files = PrintService.GetDataFiles();
        _dataFilePicker.Items.Clear();
        foreach (var (label, _) in files)
            _dataFilePicker.Items.Add(label);
        if (_dataFilePicker.Items.Count > 0 && _dataFilePicker.SelectedIndex < 0)
            _dataFilePicker.SelectedIndex = 0;
    }

    // ── Build options from current UI state ───────────────────────────────────

    PrintOptions GetOptions()
    {
        string? dataPath = null;
        if (_chkDataFile.IsChecked && _dataFilePicker.SelectedIndex >= 0)
        {
            var files = PrintService.GetDataFiles();
            int idx = _dataFilePicker.SelectedIndex;
            if (idx < files.Count) dataPath = files[idx].Path;
        }
        return new PrintOptions
        {
            IncludeSets      = _chkSets.IsChecked,
            IncludeFavorites = _chkFavorites.IsChecked,
            IncludePicks     = _chkPicks.IsChecked,
            IncludeSummary   = _chkSummary.IsChecked,
            DataFilePath     = dataPath,
        };
    }

    // ── Generate HTML and share (iOS: opens via share sheet → Safari) ─────────

    private async void BtnGenerate_Clicked(object sender, EventArgs e)
    {
        SetStatus("Building report…", true);
        try
        {
            string html = await PrintService.BuildHtmlAsync(GetOptions());
            string path = Path.Combine(FileSystem.CacheDirectory, "LotteryReport.html");
            await File.WriteAllTextAsync(path, html);
            _lastHtmlPath = path;

            SetStatus("Sharing report…", false);
            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "Lottery Report",
                File  = new ShareFile(path, "text/html"),
            });
            SetStatus("Open in Safari → use share button → Print to print or save PDF.", false);
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message, false);
        }
    }

    // ── Print directly via iOS AirPrint ──────────────────────────────────────

    private async void BtnPrint_Clicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_lastHtmlPath))
        {
            await DisplayAlert("Generate First", "Tap 'Share Report' first to build the report.", "OK");
            return;
        }
        try
        {
            SetStatus("Opening print dialog…", false);
            string html = await File.ReadAllTextAsync(_lastHtmlPath);
            PrintHelper.PrintHtml(html, "CA Lottery Report");
            SetStatus("Print dialog opened.", false);
        }
        catch (Exception ex)
        {
            SetStatus("Print error: " + ex.Message, false);
        }
    }

    // ── Share HTML file ───────────────────────────────────────────────────────

    private async void BtnShare_Clicked(object sender, EventArgs e)
    {
        SetStatus("Building report…", true);
        try
        {
            string html = await PrintService.BuildHtmlAsync(GetOptions());
            string path = Path.Combine(FileSystem.CacheDirectory, "LotteryReport.html");
            await File.WriteAllTextAsync(path, html);
            _lastHtmlPath = path;
            SetStatus("", false);
            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "Lottery Report",
                File  = new ShareFile(path, "text/html"),
            });
        }
        catch (Exception ex)
        {
            SetStatus("Error: " + ex.Message, false);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void SetStatus(string msg, bool spinning)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _statusLabel.Text  = msg;
            _spinner.IsVisible = spinning;
            _spinner.IsRunning = spinning;
        });
    }

    protected override bool OnBackButtonPressed()
    {
        _ = Shell.Current.GoToAsync("..", false);
        return true;
    }
}
