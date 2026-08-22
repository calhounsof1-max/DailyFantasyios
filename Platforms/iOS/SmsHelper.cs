namespace DailyFantasyMAUI;

// iOS counterpart to the Android app's Platforms/Android/SmsHelper.cs. Android sends a real,
// silent SMS via SmsManager; iOS has no equivalent — Apple only allows composing a text via
// MFMessageComposeViewController, which requires presenting UI and the user manually tapping
// Send, wrong for what these call sites actually want (a quick unattended status ping from a
// background-ish scan). Same public shape (SendSms returns bool, RequestPermissionIfNeeded is a
// no-op) so callers like HSPast10Days.cs don't need platform-specific branches — the message
// just arrives as a local notification instead of a text.
public static class SmsHelper
{
    public static bool SendSms(string toNumber, string message)
    {
        NotificationHelper.Show("Hot Spot", message);
        return true;
    }

    public static void RequestPermissionIfNeeded(object? activity = null) { }
}
