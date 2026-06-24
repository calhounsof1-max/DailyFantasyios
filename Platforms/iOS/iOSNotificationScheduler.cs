using Foundation;
using UserNotifications;
using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

// Shows notifications even when the app is in the foreground
class LotteryNotificationDelegate : UNUserNotificationCenterDelegate
{
    public override void WillPresentNotification(UNUserNotificationCenter center,
        UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler)
        => completionHandler(UNNotificationPresentationOptions.Banner
                           | UNNotificationPresentationOptions.Sound
                           | UNNotificationPresentationOptions.Badge);
}

public static class iOSNotificationScheduler
{
    const string IdPrefix = "lottery_daily_";
    const string PrefEnabled = "notif_enabled";
    const string PrefTimes   = "notif_times";

    static readonly LotteryNotificationDelegate _delegate = new();

    public static async Task RequestPermissionAsync()
    {
        UNUserNotificationCenter.Current.Delegate = _delegate;
        var (_, _) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge);
    }

    /// Call on every app launch to restore scheduled notifications (wiped on reinstall).
    public static async Task RescheduleIfEnabledAsync()
    {
        UNUserNotificationCenter.Current.Delegate = _delegate;
        if (!Preferences.Get(PrefEnabled, true)) return;

        bool enabled = await AreNotificationsEnabledAsync();
        if (!enabled) return;

        string raw = Preferences.Get(PrefTimes, "8");
        var hours = raw.Split(',')
            .Select(s => int.TryParse(s.Trim(), out int h) ? h : -1)
            .Where(h => h >= 0)
            .ToList();
        if (hours.Count == 0) hours.Add(8);

        // Only reschedule if no lottery notifications are already pending
        var pending = await UNUserNotificationCenter.Current.GetPendingNotificationRequestsAsync();
        bool alreadyScheduled = pending.Any(r => r.Identifier.StartsWith(IdPrefix));
        if (!alreadyScheduled)
            await ScheduleNotificationsAsync(hours);
    }

    public static async Task<bool> AreNotificationsEnabledAsync()
    {
        var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();
        return settings.AuthorizationStatus == UNAuthorizationStatus.Authorized
            || settings.AuthorizationStatus == UNAuthorizationStatus.Provisional;
    }

    public static void CancelAll()
    {
        UNUserNotificationCenter.Current.RemoveAllPendingNotificationRequests();
    }

    /// Cancel all existing lottery alarms then schedule one daily repeating notification per hour.
    public static async Task ScheduleNotificationsAsync(IEnumerable<int> hours)
    {
        var center = UNUserNotificationCenter.Current;

        // Cancel all existing lottery notifications
        var pending = await center.GetPendingNotificationRequestsAsync();
        var toRemove = pending
            .Where(r => r.Identifier.StartsWith(IdPrefix))
            .Select(r => r.Identifier)
            .ToArray();
        if (toRemove.Length > 0)
            center.RemovePendingNotificationRequests(toRemove);

        foreach (int hour in hours)
        {
            var content = new UNMutableNotificationContent
            {
                Title = "Lottery — Daily Update",
                Body = AdvancePlayNotificationService.BuildSummary(),
                Sound = UNNotificationSound.Default,
            };

            var dateComponents = new NSDateComponents
            {
                Hour   = hour,
                Minute = 0,
                Second = 0,
            };
            var trigger = UNCalendarNotificationTrigger.CreateTrigger(dateComponents, repeats: true);
            var request = UNNotificationRequest.FromIdentifier($"{IdPrefix}{hour}", content, trigger);

            await center.AddNotificationRequestAsync(request);
        }
    }

    public static async Task ShowTestNotificationAsync()
    {
        var center = UNUserNotificationCenter.Current;

        var content = new UNMutableNotificationContent
        {
            Title = "Lottery — Test Notification",
            Body  = "✓ Notifications are working!\nYou'll be alerted when advance play dates expire.",
            Sound = UNNotificationSound.Default,
        };

        // Fire in 2 seconds so user can see it arrive
        var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(2, repeats: false);
        var request = UNNotificationRequest.FromIdentifier("lottery_test", content, trigger);
        await center.AddNotificationRequestAsync(request);
    }
}
