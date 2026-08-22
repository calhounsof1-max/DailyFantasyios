using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class StatsAnalyzePage : ContentPage
{
    enum ViewState { Loading, NeedKey, Error, Result }

    AnalysisInput? _input;
    string         _analysisText = "";
    string         _analysisHtml = "";
    string         _localHtml    = "";   // always the basic non-AI version
    bool           _isAiReport   = false;

    public StatsAnalyzePage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _input = AnalyzeService.PendingInput;
        if (_input == null) { ShowError("No stats data found. Go back and reload the Stats page."); return; }
        GenerateLocalReport();
    }

    // ── Built-in report (instant, no key required) ────────────────────────────

    void GenerateLocalReport()
    {
        _isAiReport   = false;
        _analysisText = AnalyzeService.BuildLocalReport(_input!);
        _localHtml    = AnalyzeService.ToHtml(_analysisText, _input!);
        _analysisHtml = _localHtml;

        lblModel.Text      = "Built-in Report";
        lblModel.TextColor = Color.FromArgb("#607D8B");

        resultView.Source        = new HtmlWebViewSource { Html = _analysisHtml };
        enhanceAiBtn.IsVisible   = true;
        Show(ViewState.Result);
    }

    // ── AI analysis flow ──────────────────────────────────────────────────────

    async Task RunAiAnalysisAsync()
    {
        if (string.IsNullOrWhiteSpace(AnalyzeService.ApiKey))
        {
            entryApiKey.Text = AnalyzeService.ApiKey;
            Show(ViewState.NeedKey);
            return;
        }

        Show(ViewState.Loading);
        lblStatus.Text = "Connecting to Google AI…";

        try
        {
            await Task.Delay(120);
            lblStatus.Text = "Waiting for AI analysis…";

            _analysisText = await AnalyzeService.RunAsync(_input!, CancellationToken.None);
            _analysisHtml = AnalyzeService.ToHtml(_analysisText, _input!);
            _isAiReport   = true;

            lblModel.Text      = "Gemini 2.0 Flash · AI Enhanced";
            lblModel.TextColor = Color.FromArgb("#2E7D32");

            resultView.Source      = new HtmlWebViewSource { Html = _analysisHtml };
            enhanceAiBtn.IsVisible = false;
            Show(ViewState.Result);
        }
        catch (Exception ex)
        {
            // Restore local report and show error as alert so user doesn't lose the report
            _isAiReport   = false;
            _analysisHtml = _localHtml;
            resultView.Source      = new HtmlWebViewSource { Html = _analysisHtml };
            enhanceAiBtn.IsVisible = true;
            lblModel.Text      = "Built-in Report";
            lblModel.TextColor = Color.FromArgb("#607D8B");
            Show(ViewState.Result);

            string msg = ex.Message switch
            {
                var m when m.Contains("NO_KEY") => "No API key set.",
                var m when m.Contains("401")    => "Invalid API key — tap ✨ Enhance with AI to re-enter your key.",
                var m when m.Contains("429")    => "Google rate limit — your key may be new. Wait 60 seconds and try again.",
                var m when m.Contains("404")    => "AI model not available. Please try again later.",
                var m when m.Length > 300       => ex.Message[..300] + "…",
                _                               => ex.Message,
            };
            await DisplayAlert("AI Analysis Failed", msg, "OK");
        }
    }

    // ── UI state helpers ──────────────────────────────────────────────────────

    void Show(ViewState s)
    {
        loadingView.IsVisible = s == ViewState.Loading;
        keyView    .IsVisible = s == ViewState.NeedKey;
        errorView  .IsVisible = s == ViewState.Error;
        resultView .IsVisible = s == ViewState.Result;
        actionBar  .IsVisible = s == ViewState.Result;
    }

    void ShowError(string msg)
    {
        lblError.Text = msg;
        Show(ViewState.Error);
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    void SaveKey_Clicked(object sender, EventArgs e)
    {
        string key = entryApiKey.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(key)) return;
        AnalyzeService.ApiKey = key;
        _ = RunAiAnalysisAsync();
    }

    void ChangeKey_Clicked(object sender, EventArgs e)
    {
        entryApiKey.Text = AnalyzeService.ApiKey;
        Show(ViewState.NeedKey);
    }

    void CancelKey_Clicked(object sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_analysisHtml))
            Show(ViewState.Result);
        else
            _ = Navigation.PopAsync();
    }

    async void EnhanceAi_Clicked(object sender, EventArgs e) => await RunAiAnalysisAsync();

    async void GetKey_Tapped(object sender, TappedEventArgs e)
        => await Launcher.OpenAsync("https://aistudio.google.com/apikey");

    async void Share_Clicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_analysisText)) return;
        await Share.RequestAsync(new ShareTextRequest
        {
            Title = "My Lottery Stats — Analysis",
            Text  = _analysisText,
        });
    }

    async void PrintPdf_Clicked(object sender, EventArgs e)
    {
        if (string.IsNullOrEmpty(_analysisHtml)) return;
        try
        {
#if ANDROID
            PrintHelper.PrintHtml(_analysisHtml, "Lottery Stats Analysis");
#endif
        }
        catch (Exception ex)
        {
            await DisplayAlert("Print Error", ex.Message, "OK");
        }
    }

    async void ExportCsv_Clicked(object sender, EventArgs e)
    {
        if (_input == null) return;
        try
        {
            string csv  = AnalyzeService.ToCsv(_input);
            string path = Path.Combine(FileSystem.CacheDirectory, "lottery_stats_export.csv");
            await File.WriteAllTextAsync(path, csv);

            await Share.RequestAsync(new ShareFileRequest
            {
                Title = "Lottery Stats Export",
                File  = new ShareFile(path, "text/csv"),
            });
        }
        catch (Exception ex)
        {
            await DisplayAlert("Export Error", ex.Message, "OK");
        }
    }

    async void BackError_Clicked(object sender, EventArgs e)
        => await Navigation.PopAsync();

    async void Back_Tapped(object sender, TappedEventArgs e)
        => await Navigation.PopAsync();
}
