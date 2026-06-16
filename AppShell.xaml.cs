namespace DailyFantasyMAUI;

class SingletonRouteFactory(Element instance) : RouteFactory
{
    public override Element GetOrCreate() => instance;
    public override Element GetOrCreate(IServiceProvider services) => instance;
}

public partial class AppShell : Shell
{
	internal static readonly WinnerPage     WinnerPageInstance     = new();
	internal static readonly SuperLottoPage SuperLottoPageInstance = new();
	internal static readonly Daily3Page     Daily3PageInstance     = new();
	internal static readonly Daily4Page     Daily4PageInstance     = new();
	internal static readonly ScanTicketPage ScanTicketPageInstance = new();
	internal static readonly ScanSlipPage   ScanSlipPageInstance   = new();
	internal static readonly ResultsPage    ResultsPageInstance    = new();
	internal static readonly PowerballPage      PowerballPageInstance      = new();
	internal static readonly MegaMillionsPage   MegaMillionsPageInstance   = new();
	internal static readonly ArchivePage    ArchivePageInstance    = new();
	internal static readonly DailyDerbyPage DailyDerbyPageInstance = new();

	internal static readonly ViewSetsPage   ViewSetsPageInstance   = new();
	internal static readonly GeneratePage   GeneratePageInstance   = new();
	internal static readonly DataViewerPage DataViewerPageInstance = new();
	internal static readonly JackpotPage       JackpotPageInstance       = new();
	internal static readonly CheckTicketPage   CheckTicketPageInstance   = new();
	internal static readonly MyFavoritePage   MyFavoritePageInstance    = new();

#if IOS
	protected override void OnNavigated(ShellNavigatedEventArgs args)
	{
		base.OnNavigated(args);
		Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(HideNavBars);
	}

	static void HideNavBars()
	{
		foreach (var scene in UIKit.UIApplication.SharedApplication.ConnectedScenes)
			if (scene is UIKit.UIWindowScene ws)
				foreach (var w in ws.Windows)
					HideNavBar(w.RootViewController);
	}

	static void HideNavBar(UIKit.UIViewController? vc, int depth = 0)
	{
		if (vc == null || depth > 10) return;
		if (vc is UIKit.UINavigationController nav)
			nav.NavigationBarHidden = true;
		HideNavBar(vc.PresentedViewController, depth + 1);
		foreach (var child in vc.ChildViewControllers)
			HideNavBar(child, depth + 1);
	}
#endif

	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(WinnerPage),     new SingletonRouteFactory(WinnerPageInstance));
		Routing.RegisterRoute(nameof(SuperLottoPage), new SingletonRouteFactory(SuperLottoPageInstance));
		Routing.RegisterRoute(nameof(Daily3Page),     new SingletonRouteFactory(Daily3PageInstance));
		Routing.RegisterRoute(nameof(Daily4Page),     new SingletonRouteFactory(Daily4PageInstance));
		Routing.RegisterRoute(nameof(ScanTicketPage), new SingletonRouteFactory(ScanTicketPageInstance));
		Routing.RegisterRoute(nameof(ScanSlipPage),   new SingletonRouteFactory(ScanSlipPageInstance));
		Routing.RegisterRoute(nameof(ResultsPage),    new SingletonRouteFactory(ResultsPageInstance));
		Routing.RegisterRoute(nameof(PowerballPage),      new SingletonRouteFactory(PowerballPageInstance));
		Routing.RegisterRoute(nameof(MegaMillionsPage),   new SingletonRouteFactory(MegaMillionsPageInstance));
		Routing.RegisterRoute(nameof(ViewSetsPage),   new SingletonRouteFactory(ViewSetsPageInstance));
		Routing.RegisterRoute(nameof(ArchivePage),    new SingletonRouteFactory(ArchivePageInstance));
		Routing.RegisterRoute(nameof(DailyDerbyPage), new SingletonRouteFactory(DailyDerbyPageInstance));
		Routing.RegisterRoute(nameof(GeneratePage),    new SingletonRouteFactory(GeneratePageInstance));
		Routing.RegisterRoute(nameof(DataViewerPage),  new SingletonRouteFactory(DataViewerPageInstance));
		Routing.RegisterRoute(nameof(JackpotPage),       new SingletonRouteFactory(JackpotPageInstance));
		Routing.RegisterRoute(nameof(CheckTicketPage),   new SingletonRouteFactory(CheckTicketPageInstance));
		Routing.RegisterRoute(nameof(MyFavoritePage),    new SingletonRouteFactory(MyFavoritePageInstance));
	}
}
