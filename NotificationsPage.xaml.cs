namespace DailyFantasyMAUI;

public partial class NotificationsPage : ContentPage
{
    static readonly (string Display, string Key)[] Carriers =
    [
        ("AT&T",        "att"),
        ("T-Mobile",    "tmobile"),
        ("Verizon",     "verizon"),
        ("Xfinity Mobile","xfinity"),
        ("Sprint",      "sprint"),
        ("Boost Mobile","boost"),
        ("Cricket",     "cricket"),
        ("Metro PCS",   "metro"),
        ("US Cellular", "uscellular"),
    ];

    const string PrefEnabled    = "notif_enabled";
    const string PrefDays       = "notif_days_ahead";
    const string PrefSmsEnabled = "notif_sms_enabled";
    const string PrefPhone      = "notif_phone";
    const string PrefCarrier    = "notif_carrier";
    const string PrefTimes      = "notif_times";
    const string PrefGmail      = Services.SmtpSmsService.PrefGmail;
    const string PrefGmailPw    = Services.SmtpSmsService.PrefGmailPw;

    static readonly (int Hour, string Label)[] TimeOptions =
    [
        ( 6,"6 AM"),( 7,"7 AM"),( 8,"8 AM"),( 9,"9 AM"),(10,"10 AM"),(11,"11 AM"),
        (12,"12 PM"),(13,"1 PM"),(14,"2 PM"),(15,"3 PM"),(16,"4 PM"),(17,"5 PM"),
        (18,"6 PM"),(19,"7 PM"),(20,"8 PM"),(21,"9 PM"),
    ];

    HashSet<int> _selectedHours = [];
    bool _loading = true;

    public NotificationsPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _loading = true;

        switchEnabled.IsToggled = Preferences.Get(PrefEnabled, true);
        btnDaysAhead.Text       = Preferences.Get(PrefDays, 14).ToString();
        switchSms.IsToggled     = Preferences.Get(PrefSmsEnabled, false);
        entryPhone.Text         = Preferences.Get(PrefPhone, "");
        UpdateCarrierButton(Preferences.Get(PrefCarrier, "att"));
        entryGmail.Text         = Preferences.Get(PrefGmail, "");
        entryGmailPw.Text       = Preferences.Get(PrefGmailPw, "");
        LoadSelectedHours();
        BuildTimeChips();

        _loading = false;
        UpdateStatus();
        _ = CheckPermissionBannerAsync();
    }

    async Task CheckPermissionBannerAsync()
    {
#if IOS
        bool enabled = await iOSNotificationScheduler.AreNotificationsEnabledAsync();
        permBanner.IsVisible = !enabled;
#endif
    }

    async void PermBanner_Tapped(object sender, TappedEventArgs e)
    {
#if IOS
        await UIKit.UIApplication.SharedApplication.OpenUrlAsync(
            Foundation.NSUrl.FromString(UIKit.UIApplication.OpenSettingsUrlString)!,
            new UIKit.UIApplicationOpenUrlOptions());
#endif
        await Task.CompletedTask;
    }

    // ── Notification toggle ──────────────────────────────────────────────────

    async void SwitchEnabled_Toggled(object sender, ToggledEventArgs e)
    {
        if (_loading) return;
        Preferences.Set(PrefEnabled, e.Value);
        UpdateStatus();
#if IOS
        if (e.Value)
            await iOSNotificationScheduler.ScheduleNotificationsAsync(_selectedHours);
        else
            iOSNotificationScheduler.CancelAll();
#endif
    }

    // ── Days ahead ───────────────────────────────────────────────────────────

    async void BtnDaysAhead_Clicked(object sender, EventArgs e)
    {
        string? pick = await DisplayActionSheet("Alert days before expiry", "Cancel", null,
            "3 days", "5 days", "7 days", "10 days", "14 days", "21 days", "30 days");
        if (pick == null || pick == "Cancel") return;
        if (int.TryParse(pick.Split(' ')[0], out int days))
        {
            Preferences.Set(PrefDays, days);
            btnDaysAhead.Text = days.ToString();
            UpdateStatus();
        }
    }

    // ── Test notification ────────────────────────────────────────────────────

    async void BtnTestNotif_Clicked(object sender, EventArgs e)
    {
        if (!Preferences.Get(PrefEnabled, true))
        {
            await DisplayAlert("Notifications Off", "Turn on notifications first.", "OK");
            return;
        }

#if IOS
        bool enabled = await iOSNotificationScheduler.AreNotificationsEnabledAsync();
        if (!enabled)
        {
            bool goSettings = await DisplayAlert(
                "Permission Required",
                "Notifications are blocked by iOS.\n\nGo to Settings → Lottery → Notifications and turn them ON.",
                "Open Settings", "Cancel");
            if (goSettings)
                await UIKit.UIApplication.SharedApplication.OpenUrlAsync(
                    Foundation.NSUrl.FromString(UIKit.UIApplication.OpenSettingsUrlString)!,
                    new UIKit.UIApplicationOpenUrlOptions());
            return;
        }

        await iOSNotificationScheduler.ShowTestNotificationAsync();
#endif

        await DisplayAlert("Notification Sent",
            "A test notification will appear in ~2 seconds.\n\nIf you don't see it, check Settings → Lottery → Notifications.",
            "OK");

        lblStatus.Text      = "Test notification sent — check your notification bar.";
        lblStatus.TextColor = Color.FromArgb("#059669");
    }

    // ── Notification times ───────────────────────────────────────────────────

    void LoadSelectedHours()
    {
        string raw = Preferences.Get(PrefTimes, "8");
        _selectedHours = [.. raw.Split(',')
            .Select(s => int.TryParse(s.Trim(), out int h) ? h : -1)
            .Where(h => h >= 0)];
        if (_selectedHours.Count == 0) _selectedHours.Add(8);
    }

    void SaveSelectedHours()
    {
        Preferences.Set(PrefTimes, string.Join(",", _selectedHours.OrderBy(h => h)));
    }

    void BuildTimeChips()
    {
        timesChips.Children.Clear();
        foreach (var (hour, label) in TimeOptions)
        {
            bool active = _selectedHours.Contains(hour);
            var btn = new Button
            {
                Text            = label,
                FontSize        = 12,
                CornerRadius    = 16,
                HeightRequest   = 32,
                Padding         = new Thickness(10, 0),
                Margin          = new Thickness(3, 3),
                BackgroundColor = active ? Color.FromArgb("#1E3A8A") : Color.FromArgb("#E5E7EB"),
                TextColor       = active ? Colors.White : Color.FromArgb("#374151"),
                CommandParameter = hour,
            };
            btn.Clicked += TimeChip_Clicked;
            timesChips.Children.Add(btn);
        }
    }

    async void TimeChip_Clicked(object? sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        int hour = (int)btn.CommandParameter!;

        if (_selectedHours.Contains(hour))
        {
            if (_selectedHours.Count <= 1) return;
            _selectedHours.Remove(hour);
            btn.BackgroundColor = Color.FromArgb("#E5E7EB");
            btn.TextColor       = Color.FromArgb("#374151");
        }
        else
        {
            _selectedHours.Add(hour);
            btn.BackgroundColor = Color.FromArgb("#1E3A8A");
            btn.TextColor       = Colors.White;
        }

        SaveSelectedHours();
#if IOS
        if (Preferences.Get(PrefEnabled, true))
            await iOSNotificationScheduler.ScheduleNotificationsAsync(_selectedHours);
#endif
        UpdateStatus();
    }

    // ── SMS toggle ───────────────────────────────────────────────────────────

    void SwitchSms_Toggled(object sender, ToggledEventArgs e)
    {
        if (_loading) return;
        Preferences.Set(PrefSmsEnabled, e.Value);
        UpdateStatus();
    }

    // ── Phone number ─────────────────────────────────────────────────────────

    void EntryPhone_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        string digits = new string(e.NewTextValue.Where(char.IsDigit).ToArray());
        Preferences.Set(PrefPhone, digits);
        UpdateStatus();
    }

    // ── Carrier picker ───────────────────────────────────────────────────────

    async void BtnCarrier_Clicked(object sender, EventArgs e)
    {
        string? pick = await DisplayActionSheet("Select Carrier", "Cancel", null,
            Carriers.Select(c => c.Display).ToArray());
        if (pick == null || pick == "Cancel") return;

        var match = Carriers.FirstOrDefault(c => c.Display == pick);
        if (match == default) return;

        Preferences.Set(PrefCarrier, match.Key);
        UpdateCarrierButton(match.Key);
        UpdateStatus();
    }

    void UpdateCarrierButton(string key)
    {
        var match = Carriers.FirstOrDefault(c => c.Key == key);
        btnCarrier.Text = match != default ? match.Display : key;
    }

    // ── Gmail credentials ────────────────────────────────────────────────────

    void EntryGmail_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        Preferences.Set(PrefGmail, e.NewTextValue.Trim());
        UpdateStatus();
    }

    void EntryGmailPw_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        Preferences.Set(PrefGmailPw, e.NewTextValue);
        UpdateStatus();
    }

    // ── Test SMS ─────────────────────────────────────────────────────────────

    async void BtnTestSms_Clicked(object sender, EventArgs e)
    {
        string phone   = Preferences.Get(PrefPhone, "");
        string carrier = Preferences.Get(PrefCarrier, "att");
        string gmail   = Preferences.Get(PrefGmail, "");
        string pw      = Preferences.Get(PrefGmailPw, "");

        if (phone.Length < 10)
        {
            await DisplayAlert("Missing Info", "Enter a 10-digit phone number first.", "OK");
            return;
        }
        if (string.IsNullOrWhiteSpace(gmail) || string.IsNullOrWhiteSpace(pw))
        {
            await DisplayAlert("Missing Info", "Enter your Gmail address and App Password first.", "OK");
            return;
        }

        lblStatus.Text      = "Sending test SMS…";
        lblStatus.TextColor = Color.FromArgb("#6B7280");

        var (ok, error) = await Services.SmtpSmsService.SendAsync(
            gmail, pw, phone, carrier, "Lottery Test SMS — your texts are working!");

        if (ok)
        {
            lblStatus.Text      = "Test SMS sent! Check your messages in a few minutes.";
            lblStatus.TextColor = Color.FromArgb("#059669");
        }
        else
        {
            lblStatus.Text      = $"SMS failed: {error}";
            lblStatus.TextColor = Color.FromArgb("#EF4444");
            await DisplayAlert("SMS Failed", $"{error}\n\nMake sure you're using a Gmail App Password, not your Gmail login.", "OK");
        }
    }

    // ── Status summary ───────────────────────────────────────────────────────

    void UpdateStatus()
    {
        bool notifOn = Preferences.Get(PrefEnabled, true);
        bool smsOn   = Preferences.Get(PrefSmsEnabled, false);
        int  days    = Preferences.Get(PrefDays, 14);
        string phone = Preferences.Get(PrefPhone, "");

        string times = _selectedHours.Count == 0 ? "8 AM"
            : string.Join(", ", _selectedHours.OrderBy(h => h)
                .Select(h => TimeOptions.FirstOrDefault(t => t.Hour == h).Label ?? $"{h}:00"));

        if (!notifOn)
        {
            lblStatus.Text      = "Notifications are OFF";
            lblStatus.TextColor = Color.FromArgb("#EF4444");
        }
        else if (smsOn && phone.Length < 10)
        {
            lblStatus.Text      = $"SMS on — enter a valid phone number · {times}";
            lblStatus.TextColor = Color.FromArgb("#F59E0B");
        }
        else if (smsOn)
        {
            lblStatus.Text      = $"ON · SMS to {phone} · {days}-day alert · {times}";
            lblStatus.TextColor = Color.FromArgb("#059669");
        }
        else
        {
            lblStatus.Text      = $"ON · {days}-day alert · {times} · No SMS";
            lblStatus.TextColor = Color.FromArgb("#059669");
        }
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    private async void BtnBack_Clicked(object sender, EventArgs e)
    {
        Shell.Current.CurrentPage.TranslationX = -DeviceDisplay.MainDisplayInfo.Width
                                                 / DeviceDisplay.MainDisplayInfo.Density;
        await Shell.Current.GoToAsync("..", false);
    }

    protected override bool OnBackButtonPressed()
    {
        _ = BtnBack_ClickedAsync();
        return true;
    }

    async Task BtnBack_ClickedAsync()
    {
        Shell.Current.CurrentPage.TranslationX = -DeviceDisplay.MainDisplayInfo.Width
                                                 / DeviceDisplay.MainDisplayInfo.Density;
        await Shell.Current.GoToAsync("..", false);
    }
}
