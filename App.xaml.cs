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
		return new Window(new AppShell());
	}
}