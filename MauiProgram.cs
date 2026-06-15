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
			});

#if IOS
		// Force-hide the iOS UINavigationBar at the native level.
		// MAUI Shell.NavBarIsVisible=False hides the bar content but the bar
		// itself still renders as a gray band and steals ~44pt of layout height.
		// Calling SetNavigationBarHidden(true) directly reclaims that space.
		Microsoft.Maui.Handlers.PageHandler.Mapper.AppendToMapping("HideIOSNavBar", (handler, view) =>
		{
			if (view is ContentPage)
				handler.ViewController?.NavigationController?.SetNavigationBarHidden(true, false);
		});
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
