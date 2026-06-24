using Foundation;
using UserNotifications;
using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

public static class iOSNotificationScheduler
{
    const string IdPrefix = "lottery_daily_";

    public static async Task RequestPermissionAsync()
    {
        var (_, _) = await UNUserNotificationCenter.Current.RequestAuthorizationAsync(
            UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound | UNAuthorizationOptions.Badge);
    }

    public static async Task<bool> AreNotificationsEnabledAsync()
    {
        var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();
        return settings.AuthorizationStatus == UNAuthorizationStatus.Authorized
            || settings.AuthorizationStatus == UNAuthorizationStatus.Provisional;
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
