using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public partial class NotificationsPage : ContentPage
{
    // Carrier display names → gateway domain keys (matches notify.ps1 list)
    static readonly (string Display, string Key)[] Carriers =
    [
        ("AT&T",        "att"),
        ("T-Mobile",    "tmobile"),
        ("Verizon",     "verizon"),
        ("Sprint",      "sprint"),
        ("Boost Mobile","boost"),
        ("Cricket",     "cricket"),
        ("Metro PCS",   "metro"),
        ("US Cellular", "uscellular"),
    ];

    // Pref keys
    const string PrefEnabled      = "notif_enabled";
    const string PrefDays         = "notif_days_ahead";
    const string PrefSmsEnabled   = "notif_sms_enabled";
    const string PrefPhone        = "notif_phone";
    const string PrefCarrier      = "notif_carrier";
    const string PrefTimes        = "notif_times";       // comma-separated 24h hours e.g. "8,18"
    const string PrefWinEnabled         = "win_alert_enabled";
    const string PrefWinTimes           = "win_check_times";      // default "18,19,20,21"
    const string PrefWinMinAmount       = "win_min_amount";       // default 100
    const string PrefWinIntervalEnabled = "win_interval_enabled"; // default false
    const string PrefWinIntervalStart   = "win_interval_start";   // hour, default 19 (7 PM)
    const string PrefWinIntervalMinutes = "win_interval_minutes"; // default 30
    const string PrefWinIntervalEnd     = "win_interval_end";     // hour, default 21 (9 PM)

    static readonly (int Minutes, string Label)[] IntervalOptions =
    [
        (15, "15 min"), (20, "20 min"), (30, "30 min"), (45, "45 min"), (60, "1 hr")
    ];

    static readonly (int Hour, string Label)[] IntervalHours =
    [
        (12,"12 PM"),(13,"1 PM"),(14,"2 PM"),(15,"3 PM"),(16,"4 PM"),(17,"5 PM"),
        (18,"6 PM"),(19,"7 PM"),(20,"8 PM"),(21,"9 PM"),(22,"10 PM"),(23,"11 PM"),
    ];

    static readonly (int Hour, string Label)[] TimeOptions =
    [
        ( 6,"6 AM"),( 7,"7 AM"),( 8,"8 AM"),( 9,"9 AM"),(10,"10 AM"),(11,"11 AM"),
        (12,"12 PM"),(13,"1 PM"),(14,"2 PM"),(15,"3 PM"),(16,"4 PM"),(17,"5 PM"),
        (18,"6 PM"),(19,"7 PM"),(20,"8 PM"),(21,"9 PM"),
    ];

    HashSet<int> _selectedHours    = [];
    HashSet<int> _winSelectedHours = [];
    bool _loading = true;

    public NotificationsPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _loading = true;

        switchEnabled.IsToggled   = Preferences.Get(PrefEnabled, true);
        btnDaysAhead.Text         = Preferences.Get(PrefDays, 14).ToString();
        switchSms.IsToggled       = Preferences.Get(PrefSmsEnabled, false);
        entryPhone.Text           = Preferences.Get(PrefPhone, "");
        UpdateCarrierButton(Preferences.Get(PrefCarrier, "att"));
        LoadSelectedHours();
        UpdateNotifTimesSummary();

        switchWinAlert.IsToggled    = Preferences.Get(PrefWinEnabled, true);
        btnWinMinAmount.Text        = "$" + Preferences.Get(PrefWinMinAmount, 100).ToString();
        LoadWinSelectedHours();
        UpdateWinTimesSummary();

        bool intervalOn             = Preferences.Get(PrefWinIntervalEnabled, false);
        switchWinInterval.IsToggled = intervalOn;
        intervalSettings.IsVisible  = intervalOn;
        UpdateIntervalButtons();

        _loading = false;
        UpdateStatus();
        CheckPermissionBanner();
    }

    void CheckPermissionBanner()
    {
#if ANDROID
        var nm = AndroidX.Core.App.NotificationManagerCompat.From(
            Android.App.Application.Context);
        permBanner.IsVisible = !nm.AreNotificationsEnabled();
#endif
    }

    async void PermBanner_Tapped(object sender, TappedEventArgs e)
    {
#if ANDROID
        var intent = new Android.Content.Intent(
            Android.Provider.Settings.ActionAppNotificationSettings);
        intent.PutExtra(Android.Provider.Settings.ExtraAppPackage,
            Android.App.Application.Context.PackageName);
        intent.AddFlags(Android.Content.ActivityFlags.NewTask);
        Android.App.Application.Context.StartActivity(intent);
#endif
        await Task.CompletedTask;
    }

    // ── Notification toggle ──────────────────────────────────────────────────

    void SwitchEnabled_Toggled(object sender, ToggledEventArgs e)
    {
        if (_loading) return;
        Preferences.Set(PrefEnabled, e.Value);
        UpdateStatus();
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

#if ANDROID
        // Check if system-level notification permission is granted
        var nmCompat = AndroidX.Core.App.NotificationManagerCompat.From(
            Android.App.Application.Context);
        if (!nmCompat.AreNotificationsEnabled())
        {
            bool goSettings = await DisplayAlert(
                "Permission Required",
                "Notifications are blocked by Android.\n\nGo to Settings → Apps → Lottery → Notifications and turn them ON.",
                "Open Settings", "Cancel");
            if (goSettings)
            {
                var intent = new Android.Content.Intent(
                    Android.Provider.Settings.ActionAppNotificationSettings);
                intent.PutExtra(Android.Provider.Settings.ExtraAppPackage,
                    Android.App.Application.Context.PackageName);
                intent.AddFlags(Android.Content.ActivityFlags.NewTask);
                Android.App.Application.Context.StartActivity(intent);
            }
            return;
        }
#endif

        const string testMsg = "✓ Notifications are working!\nYou'll be alerted when advance play dates expire.";

#if ANDROID
        NotificationHelper.Show("Lottery — Test Notification", testMsg);
#endif

        // Always show an in-app confirmation so user knows the code ran
        await DisplayAlert("Notification Sent",
            "A test notification was just posted.\n\nIf you don't see it:\n• Pull down from the top of your screen\n• Check Lottery is ON in Android Settings → Apps → Lottery → Notifications",
            "OK");

        // Also send a test SMS if enabled
        bool smsOn = Preferences.Get(PrefSmsEnabled, false);
        string phone = Preferences.Get(PrefPhone, "");
        if (smsOn && phone.Length >= 10)
        {
#if ANDROID
            bool sent = SmsHelper.SendSms(phone, $"Lottery Test:\n{testMsg}");
            lblStatus.Text      = sent ? $"Notification + SMS sent to {phone}!"
                                       : "Notification sent. SMS failed — check Send SMS permission in Android settings.";
#endif
        }
        else
        {
            lblStatus.Text = "Test notification sent — check your notification bar.";
        }
        lblStatus.TextColor = Color.FromArgb("#059669");
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

    void UpdateNotifTimesSummary()
    {
        lblNotifTimesSummary.Text = string.Join(", ", _selectedHours.OrderBy(h => h)
            .Select(h => TimeOptions.FirstOrDefault(t => t.Hour == h).Label ?? $"{h}:00"));
    }

    void NotifTimesHeader_Tapped(object sender, TappedEventArgs e)
    {
        bool expanding = !timesChips.IsVisible;
        timesChips.IsVisible = expanding;
        lblNotifTimesChevron.Text = expanding ? "▼" : "▶";
        if (expanding) BuildTimeChips();
    }

    void BuildTimeChips()
    {
        timesChips.Children.Clear();
        foreach (var (hour, label) in TimeOptions)
        {
            bool active = _selectedHours.Contains(hour);
            var btn = new Button
            {
                Text             = label,
                FontSize         = 12,
                CornerRadius     = 16,
                HeightRequest    = 32,
                Padding          = new Thickness(10, 0),
                Margin           = new Thickness(3, 3),
                BackgroundColor  = active ? Color.FromArgb("#1E3A8A") : Color.FromArgb("#E5E7EB"),
                TextColor        = active ? Colors.White : Color.FromArgb("#374151"),
                CommandParameter = hour,
            };
            btn.Clicked += TimeChip_Clicked;
            timesChips.Children.Add(btn);
        }
    }

    void TimeChip_Clicked(object? sender, EventArgs e)
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
        UpdateNotifTimesSummary();
#if ANDROID
        AlarmScheduler.ScheduleAlarms(_selectedHours);
#endif
        UpdateStatus();
    }

    // ── Win alert settings ───────────────────────────────────────────────────

    void SwitchWinAlert_Toggled(object sender, ToggledEventArgs e)
    {
        if (_loading) return;
        Preferences.Set(PrefWinEnabled, e.Value);
        UpdateStatus();
    }

    async void BtnWinMinAmount_Clicked(object sender, EventArgs e)
    {
        int current = Preferences.Get(PrefWinMinAmount, 100);
        string? input = await DisplayPromptAsync(
            "Alert When I Win At Least",
            "Enter any dollar amount:",
            initialValue: current.ToString(),
            keyboard: Keyboard.Numeric,
            maxLength: 7);
        if (input == null) return;
        string digits = new string(input.Where(char.IsDigit).ToArray());
        if (int.TryParse(digits, out int amount) && amount >= 1)
        {
            Preferences.Set(PrefWinMinAmount, amount);
            btnWinMinAmount.Text = "$" + amount.ToString();
            UpdateStatus();
        }
    }

    void LoadWinSelectedHours()
    {
        string raw = Preferences.Get(PrefWinTimes, "18,19,20,21");
        _winSelectedHours = [.. raw.Split(',')
            .Select(s => int.TryParse(s.Trim(), out int h) ? h : -1)
            .Where(h => h >= 0)];
        if (_winSelectedHours.Count == 0)
            _winSelectedHours = [18, 19, 20, 21];
    }

    void SaveWinSelectedHours()
    {
        Preferences.Set(PrefWinTimes, string.Join(",", _winSelectedHours.OrderBy(h => h)));
#if ANDROID
        WinCheckScheduler.ScheduleAlarms();
#endif
    }

    void UpdateWinTimesSummary()
    {
        lblWinTimesSummary.Text = string.Join(", ", _winSelectedHours.OrderBy(h => h)
            .Select(h => TimeOptions.FirstOrDefault(t => t.Hour == h).Label ?? $"{h}:00"));
    }

    void WinTimesHeader_Tapped(object sender, TappedEventArgs e)
    {
        bool expanding = !winTimesChips.IsVisible;
        winTimesChips.IsVisible = expanding;
        lblWinTimesChevron.Text = expanding ? "▼" : "▶";
        if (expanding) BuildWinTimeChips();
    }

    void BuildWinTimeChips()
    {
        winTimesChips.Children.Clear();
        foreach (var (hour, label) in TimeOptions)
        {
            bool active = _winSelectedHours.Contains(hour);
            var btn = new Button
            {
                Text             = label,
                FontSize         = 12,
                CornerRadius     = 16,
                HeightRequest    = 32,
                Padding          = new Thickness(10, 0),
                Margin           = new Thickness(3, 3),
                BackgroundColor  = active ? Color.FromArgb("#059669") : Color.FromArgb("#E5E7EB"),
                TextColor        = active ? Colors.White : Color.FromArgb("#374151"),
                CommandParameter = hour,
            };
            btn.Clicked += WinTimeChip_Clicked;
            winTimesChips.Children.Add(btn);
        }
    }

    void WinTimeChip_Clicked(object? sender, EventArgs e)
    {
        if (sender is not Button btn) return;
        int hour = (int)btn.CommandParameter!;

        if (_winSelectedHours.Contains(hour))
        {
            if (_winSelectedHours.Count <= 1) return;
            _winSelectedHours.Remove(hour);
            btn.BackgroundColor = Color.FromArgb("#E5E7EB");
            btn.TextColor       = Color.FromArgb("#374151");
        }
        else
        {
            _winSelectedHours.Add(hour);
            btn.BackgroundColor = Color.FromArgb("#059669");
            btn.TextColor       = Colors.White;
        }

        SaveWinSelectedHours();
        UpdateWinTimesSummary();
        UpdateStatus();
    }

    async void BtnTestWinAlert_Clicked(object sender, EventArgs e)
    {
        int minAmt = Preferences.Get(PrefWinMinAmount, 100);
        string title = "You Won $250 Today! 🎉";
        string body  = $"• F5 S1R3: $250\n(Test — fires when you win ${minAmt}+)";

#if ANDROID
        NotificationHelper.ShowWin(title, body);

        bool smsOn = Preferences.Get(PrefSmsEnabled, false);
        string phone = Preferences.Get(PrefPhone, "");
        if (smsOn && phone.Length >= 10)
        {
            bool sent = SmsHelper.SendSms(phone, $"Lottery Win Alert!\n{title}\n{body}");
            await DisplayAlert("Test Sent",
                sent ? $"Notification + SMS sent to {phone}!"
                     : "Notification sent. SMS failed — check Send SMS permission in Android settings.",
                "OK");
            return;
        }
#endif
        await DisplayAlert("Test Sent",
            "Notification posted.\n\nTo also get SMS: enable 'Send SMS Texts' and enter your phone number in the SMS section.",
            "OK");
    }

    // ── Interval check ───────────────────────────────────────────────────────

    void UpdateIntervalButtons()
    {
        int start = Preferences.Get(PrefWinIntervalStart, 19);
        int every = Preferences.Get(PrefWinIntervalMinutes, 30);
        int end   = Preferences.Get(PrefWinIntervalEnd, 21);

        btnIntervalStart.Text = IntervalHours.FirstOrDefault(h => h.Hour == start).Label ?? $"{start}:00";
        btnIntervalEvery.Text = IntervalOptions.FirstOrDefault(o => o.Minutes == every).Label ?? $"{every} min";
        btnIntervalEnd.Text   = IntervalHours.FirstOrDefault(h => h.Hour == end).Label ?? $"{end}:00";
    }

    void SwitchWinInterval_Toggled(object sender, ToggledEventArgs e)
    {
        if (_loading) return;
        Preferences.Set(PrefWinIntervalEnabled, e.Value);
        intervalSettings.IsVisible = e.Value;
#if ANDROID
        WinCheckScheduler.ScheduleAlarms();
#endif
        UpdateStatus();
    }

    async void BtnIntervalStart_Clicked(object sender, EventArgs e)
    {
        string? pick = await DisplayActionSheet("Check from...", "Cancel", null,
            IntervalHours.Select(h => h.Label).ToArray());
        if (pick == null || pick == "Cancel") return;
        var match = IntervalHours.FirstOrDefault(h => h.Label == pick);
        if (match == default) return;
        Preferences.Set(PrefWinIntervalStart, match.Hour);
        btnIntervalStart.Text = match.Label;
#if ANDROID
        WinCheckScheduler.ScheduleAlarms();
#endif
    }

    async void BtnIntervalEvery_Clicked(object sender, EventArgs e)
    {
        string? pick = await DisplayActionSheet("Check every...", "Cancel", null,
            IntervalOptions.Select(o => o.Label).ToArray());
        if (pick == null || pick == "Cancel") return;
        var match = IntervalOptions.FirstOrDefault(o => o.Label == pick);
        if (match == default) return;
        Preferences.Set(PrefWinIntervalMinutes, match.Minutes);
        btnIntervalEvery.Text = match.Label;
#if ANDROID
        WinCheckScheduler.ScheduleAlarms();
#endif
    }

    async void BtnIntervalEnd_Clicked(object sender, EventArgs e)
    {
        string? pick = await DisplayActionSheet("Check until...", "Cancel", null,
            IntervalHours.Select(h => h.Label).ToArray());
        if (pick == null || pick == "Cancel") return;
        var match = IntervalHours.FirstOrDefault(h => h.Label == pick);
        if (match == default) return;
        Preferences.Set(PrefWinIntervalEnd, match.Hour);
        btnIntervalEnd.Text = match.Label;
#if ANDROID
        WinCheckScheduler.ScheduleAlarms();
#endif
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
