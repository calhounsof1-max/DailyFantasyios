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
		// Per-page: set page.Padding = (statusBarHeight, homeIndicator) from NavigatedTo.
		// NavigatedTo fires after the page is fully on-screen so the scene is ready.
		// UseSafeArea="false" means we own Padding entirely — no MAUI override.
		PageHandler.Mapper.AppendToMapping("HideIOSNavBar", (handler, view) =>
		{
			if (view is ContentPage page && handler is PageHandler ph)
			{
				// Hide nav bar immediately when the handler is set up (belt & suspenders).
				ph.ViewController?.NavigationController?.SetNavigationBarHidden(true, false);

				page.NavigatedTo += (_, _) =>
					Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
					{
						var vc = ph.ViewController;
						if (vc == null) return;
						vc.NavigationController?.SetNavigationBarHidden(true, false);

						// Status bar height from the scene (correct on all devices).
						nfloat statusBarH = 0;
						foreach (var scene in UIKit.UIApplication.SharedApplication.ConnectedScenes)
							if (scene is UIKit.UIWindowScene ws)
							{ statusBarH = ws.StatusBarManager?.StatusBarFrame.Height ?? 0; break; }

						// Home indicator height for the bottom.
						var safeBottom = vc.View?.SafeAreaInsets.Bottom ?? 0;

						page.Padding = new Microsoft.Maui.Thickness(0, (double)statusBarH, 0, (double)safeBottom);
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
