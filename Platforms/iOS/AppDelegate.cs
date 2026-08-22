using Foundation;

namespace DailyFantasyMAUI;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool FinishedLaunching(UIKit.UIApplication application, NSDictionary launchOptions)
	{
		// Must run before this method returns, per BGTaskScheduler's own requirement — see
		// HotSpotFastCheckScheduler.cs.
		HotSpotFastCheckScheduler.RegisterBackgroundTask();

		var result = base.FinishedLaunching(application, launchOptions);
		// Request permission and restore any notifications wiped by reinstall
		_ = iOSNotificationScheduler.RequestPermissionAsync();
		_ = iOSNotificationScheduler.RescheduleIfEnabledAsync();
		// Send daily SMS once per day when app is opened (iOS has no background SMS API)
		_ = DailyFantasyMAUI.Services.SmtpSmsService.TrySendDailyIfNeededAsync();
		// Arm (or leave disarmed) the Hot Spot background refresh chain based on current
		// ticket state — mirrors Android's own "call once at app startup" convention.
		HotSpotFastCheckScheduler.EnsureScheduled();
		return result;
	}
}
