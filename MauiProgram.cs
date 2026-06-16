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
		// Per-page: hide the native nav bar and apply ONLY status-bar top padding.
		// UseSafeArea is "false" on all pages so we control Padding entirely from here.
		// This removes the extra ~44 pt gray band caused by nav-bar safe-area.
		PageHandler.Mapper.AppendToMapping("HideIOSNavBar", (handler, view) =>
		{
			if (view is ContentPage page && handler is PageHandler pageHandler)
			{
				Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
				{
					var vc = pageHandler.ViewController;
					if (vc == null) return;
					vc.NavigationController?.SetNavigationBarHidden(true, false);

					// Status bar height only — no nav bar contribution.
					nfloat statusBarTop = 0;
					foreach (var scene in UIKit.UIApplication.SharedApplication.ConnectedScenes)
						if (scene is UIKit.UIWindowScene ws)
							{ statusBarTop = ws.StatusBarManager?.StatusBarFrame.Height ?? 0; break; }

					// Bottom safe area for home indicator.
					var safeBottom = vc.View?.SafeAreaInsets.Bottom ?? 0;
					page.Padding = new Microsoft.Maui.Thickness(0, (double)statusBarTop, 0, (double)safeBottom);
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
