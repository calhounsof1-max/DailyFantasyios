using DailyFantasyMAUI.Services;
using Microsoft.Extensions.DependencyInjection;
using Application = Microsoft.Maui.Controls.Application;

namespace DailyFantasyMAUI;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		// Force light mode so Android dark theme never overrides text/background colors
		UserAppTheme = AppTheme.Light;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
		// Fire advance-play check after the window is ready
		window.Created += (_, _) =>
			Task.Run(() => AdvancePlayNotificationService.CheckAndNotify());
		return window;
	}
}