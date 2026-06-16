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
				handlers.AddHandler<Button, CompactButtonHandler>();
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

		// Per-page fix: set AdditionalSafeAreaInsets on the page VC itself so MAUI's
		// UseSafeArea sees the corrected insets regardless of nav-bar state.
		// We run after a 120 ms delay to ensure MAUI has finished its own safe-area setup.
		PageHandler.Mapper.AppendToMapping("HideIOSNavBar", (handler, view) =>
		{
			if (view is ContentPage && handler is PageHandler pageHandler)
			{
				Task.Delay(120).ContinueWith(_ =>
					Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
					{
						var vc = pageHandler.ViewController;
						if (vc == null) return;

						// Also hide via the nav controller if available.
						vc.NavigationController?.SetNavigationBarHidden(true, false);

						// If the page VC's own safe-area top is > 62 pt the nav bar
						// (44 pt) is still contributing.  Cancel it on the page VC so
						// UseSafeArea picks up the corrected value and fires its observer.
						var safeTop = vc.View?.SafeAreaInsets.Top ?? 0;
						if (safeTop > 62)
							vc.AdditionalSafeAreaInsets = new UIKit.UIEdgeInsets(-44, 0, 0, 0);
					}));
			}
		});
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
