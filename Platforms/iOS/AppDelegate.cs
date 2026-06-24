using Foundation;

namespace DailyFantasyMAUI;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override bool FinishedLaunching(UIKit.UIApplication application, NSDictionary launchOptions)
	{
		var result = base.FinishedLaunching(application, launchOptions);
		// Request permission and restore any notifications wiped by reinstall
		_ = iOSNotificationScheduler.RequestPermissionAsync();
		_ = iOSNotificationScheduler.RescheduleIfEnabledAsync();
		return result;
	}
}
