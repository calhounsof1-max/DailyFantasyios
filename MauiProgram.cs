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
		// Per-page: hide the native nav bar and cancel its 44pt safe-area contribution.
		// We hook page.Loaded so the VC is fully in the navigation hierarchy before we act.
		// UseSafeArea="true" watches for safe-area changes — when AdditionalSafeAreaInsets
		// changes on the nav controller iOS fires viewSafeAreaInsetsDidChange, MAUI
		// re-reads the insets and shrinks the top padding to status-bar height only.
		PageHandler.Mapper.AppendToMapping("HideIOSNavBar", (handler, view) =>
		{
			if (view is ContentPage page && handler is PageHandler pageHandler)
			{
				page.Loaded += (_, _) =>
					Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
					{
						var vc = pageHandler.ViewController;
						if (vc == null) return;
						var nav = vc.NavigationController;
						if (nav == null) return;
						nav.SetNavigationBarHidden(true, false);
						// Subtract the nav bar height so UseSafeArea only accounts for the status bar.
						nav.AdditionalSafeAreaInsets = new UIKit.UIEdgeInsets(-44, 0, 0, 0);
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
