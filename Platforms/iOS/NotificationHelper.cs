using Foundation;
using UserNotifications;

namespace DailyFantasyMAUI;

// iOS counterpart to the Android app's Platforms/Android/NotificationHelper.cs — same public
// shape (synchronous, fire-and-forget Show/ShowWin) so HotSpotPage.cs and friends call it
// identically on both platforms. Android gets a dedicated high-priority "alarm" channel that
// bypasses Do Not Disturb; iOS has no equivalent notification-channel/DND-bypass concept for
// third-party apps, so this just fires an immediate local notification through the same
// UNUserNotificationCenter iOSNotificationScheduler already uses for daily reminders.
public static class NotificationHelper
{
    public static void Show(string title, string body) => _ = ShowAsync("lottery_alert", title, body);
    public static void ShowWin(string title, string body) => _ = ShowAsync("lottery_win", title, body);

    static async Task ShowAsync(string idPrefix, string title, string body)
    {
        try
        {
            var content = new UNMutableNotificationContent
            {
                Title = title,
                Body  = body,
                Sound = UNNotificationSound.Default,
            };
            // Fire almost immediately — this is an already-happened event (a win, a status
            // update), not a scheduled future reminder.
            var trigger = UNTimeIntervalNotificationTrigger.CreateTrigger(1, repeats: false);
            var request = UNNotificationRequest.FromIdentifier($"{idPrefix}_{DateTime.Now.Ticks}", content, trigger);
            await UNUserNotificationCenter.Current.AddNotificationRequestAsync(request);
        }
        catch { }
    }
}
