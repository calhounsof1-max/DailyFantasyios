namespace DailyFantasyMAUI;

public partial class CheckTicketPage : ContentPage
{
    const string TicketUrl = "https://www.calottery.com/";
    bool _initialized = false;
    bool _isPanning = false;
    double _panLeft;
    double _panRight;

    public CheckTicketPage()
    {
        InitializeComponent();
        webView.HandlerChanged += (_, _) =>
        {
            SetUserAgent();
            webView.Source = new UrlWebViewSource { Url = TicketUrl };
            _initialized = true;
        };
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
        if (_initialized)
        {
            SetUserAgent();
            if (webView.Source == null)
                webView.Source = new UrlWebViewSource { Url = TicketUrl };
        }
    }

    void SetUserAgent()
    {
#if ANDROID
        if (webView.Handler?.PlatformView is Android.Webkit.WebView av)
            av.Settings.UserAgentString =
                "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Mobile Safari/537.36";
#endif
    }

    private void WebView_Navigating(object sender, WebNavigatingEventArgs e)
    {
        progressBar.Progress = 0;
        _ = AnimateProgressAsync();
    }

    private void WebView_Navigated(object sender, WebNavigatedEventArgs e)
    {
        progressBar.Progress = 1;
        progressBar.IsVisible = false;
    }

    async Task AnimateProgressAsync()
    {
        progressBar.IsVisible = true;
        // Animate to 0.85 over ~3s to show activity while page loads
        await progressBar.ProgressTo(0.85, 3000, Easing.CubicOut);
    }

    private void BtnBack_Clicked(object sender, EventArgs e) => _ = GoBackWithSlide();
    private void BtnReload_Clicked(object sender, EventArgs e)
    {
        progressBar.IsVisible = true;
        webView.Reload();
    }

    private async Task GoBackWithSlide()
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        Shell.Current.CurrentPage.TranslationX = -w;
        await Shell.Current.GoToAsync("..", false);
    }

    protected override bool OnBackButtonPressed()
    {
        if (webView.CanGoBack)
        {
            webView.GoBack();
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
