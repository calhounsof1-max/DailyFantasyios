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
		// Apply iOS safe area padding to all ContentPages globally so content
		// stays below the notch/Dynamic Island on every page.
		Microsoft.Maui.Handlers.PageHandler.Mapper.AppendToMapping("iOSSafeArea", (handler, view) =>
		{
			if (view is ContentPage page)
				Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetUseSafeArea(page, true);
		});
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
