using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;
using ZXing.Net.Maui.Controls;

namespace DailyFantasyMAUI;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseBarcodeReader()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			})
			.ConfigureMauiHandlers(handlers =>
			{
#if ANDROID
				handlers.AddHandler<Entry, BlackTextEntryHandler>();
#endif
#if IOS
				// ZeroPaddingTextField removes UIKit's internal vertical padding so
				// HeightRequest values are actually respected (matching Android sizes).
				handlers.AddHandler<Entry, CompactEntryHandler>();
#endif
			});

#if IOS
		// Color the nav bar dark so the gray band is invisible.
		var navAppearance = new UIKit.UINavigationBarAppearance();
		navAppearance.ConfigureWithOpaqueBackground();
		navAppearance.BackgroundColor = UIKit.UIColor.FromRGB(0x1E, 0x27, 0x33);
		navAppearance.ShadowColor = UIKit.UIColor.Clear;
		UIKit.UINavigationBar.Appearance.StandardAppearance   = navAppearance;
		UIKit.UINavigationBar.Appearance.ScrollEdgeAppearance = navAppearance;
		UIKit.UINavigationBar.Appearance.CompactAppearance    = navAppearance;

		// Hide the nav bar after MAUI finishes its own setup.
		PageHandler.Mapper.AppendToMapping("HideIOSNavBar", (handler, view) =>
		{
			if (view is ContentPage && handler is PageHandler pageHandler)
			{
				Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
				{
					pageHandler.ViewController?.NavigationController?.SetNavigationBarHidden(true, false);
				});
			}
		});
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
