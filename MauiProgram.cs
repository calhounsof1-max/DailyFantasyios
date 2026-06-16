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
		// Per-page: hide the native nav bar.
		// UseSafeArea="false" + iOS MAUI Shell already positions content at the safe-area
		// top automatically, so NO top padding is needed from us. We only set bottom
		// padding for the home indicator.
		PageHandler.Mapper.AppendToMapping("HideIOSNavBar", (handler, view) =>
		{
			if (view is ContentPage page && handler is PageHandler ph)
			{
				ph.ViewController?.NavigationController?.SetNavigationBarHidden(true, false);

				page.NavigatedTo += (_, _) =>
					Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
					{
						var vc = ph.ViewController;
						if (vc == null) return;
						vc.NavigationController?.SetNavigationBarHidden(true, false);
						// Top=0: iOS Shell already puts content below the Dynamic Island.
						// Bottom=safeAreaInsets.Bottom: protect home indicator.
						var safeBottom = vc.View?.SafeAreaInsets.Bottom ?? 0;
						page.Padding = new Microsoft.Maui.Thickness(0, 0, 0, (double)safeBottom);
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
