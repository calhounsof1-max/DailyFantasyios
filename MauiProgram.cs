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
		// Use PageHandler.Mapper to reach the concrete PageHandler (which has ViewController)
		// and from there get the UINavigationController to force-hide the nav bar.
		// This fires every time a ContentPage handler is created or updated.
		Microsoft.Maui.Handlers.PageHandler.Mapper.AppendToMapping("HideIOSNavBar", (handler, view) =>
		{
			if (view is ContentPage && handler is Microsoft.Maui.Handlers.PageHandler pageHandler)
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
