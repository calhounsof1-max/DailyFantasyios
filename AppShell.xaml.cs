namespace DailyFantasyMAUI;

class SingletonRouteFactory(Element instance) : RouteFactory
{
    public override Element GetOrCreate() => instance;
    public override Element GetOrCreate(IServiceProvider services) => instance;
}

// HotSpotPage's constructor eagerly builds an ~8,000-line UI tree — unlike every other page
// here (constructed eagerly at AppShell's static-init time), it's built lazily on first
// navigation instead, so a user who never opens Hot Spot never pays that cold-start cost.
// Matches the Android app's own AppShell.xaml.cs pattern for this same page.
class LazySingletonRouteFactory(Func<Element> factory) : RouteFactory
{
    Element? _instance;
    public override Element GetOrCreate() => _instance ??= factory();
    public override Element GetOrCreate(IServiceProvider services) => _instance ??= factory();
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
	internal static readonly MyFavoritePage       MyFavoritePageInstance       = new();
	internal static readonly NotificationsPage    NotificationsPageInstance    = new();
	internal static readonly AdvanceGamesPage     AdvanceGamesPageInstance     = new();
	internal static readonly SummaryPage          SummaryPageInstance          = new();
	internal static readonly DrawSearchPage       DrawSearchPageInstance        = new();
	internal static readonly PrintPreviewPage     PrintPreviewPageInstance      = new();

	static HotSpotPage? _hotSpotPage;
	internal static HotSpotPage HotSpotPageInstance => _hotSpotPage ??= new();

	static DailyFantasyMAUI.LotteryDirectory.StateLotteryPage? _stateLotteryPage;
	internal static DailyFantasyMAUI.LotteryDirectory.StateLotteryPage StateLotteryPageInstance => _stateLotteryPage ??= new();

	// Stage 2 port — same lazy-construct pattern as HotSpotPage above, matching how the
	// Android app's AppShell.xaml.cs lazily constructs every one of its own routes.
	static AboutPage?               _aboutPage;
	static BackTestPage?            _backTestPage;
	static BalanceCheckPage?        _balanceCheckPage;
	static ComboFilterPage?         _comboFilterPage;
	static GapTrackerPage?          _gapTrackerPage;
	static HotColdPage?             _hotColdPage;
	static PairsTripletsPage?       _pairsTripletsPage;
	static PositionalFreqPage?      _positionalFreqPage;
	static RundownPage?             _rundownPage;
	static ScatchersPage?           _scatchersPage;
	static SpendingLogPage?         _spendingLogPage;
	static SumRangePage?            _sumRangePage;
	static TicketScorerPage?        _ticketScorerPage;
	static WheelingPage?            _wheelingPage;
	static TicketPurchaseStatsPage? _ticketPurchaseStatsPage;
	static TicketSummaryPage?       _ticketSummaryPage;
	static ImportTicketPage?        _importTicketPage;
	static CheckMyNumber?           _checkMyNumberPage;
	static TicketLogPage?           _ticketLogPage;
	static TicketCalendarPage?      _ticketCalendarPage;

	internal static AboutPage               AboutPageInstance               => _aboutPage               ??= new();
	internal static BackTestPage            BackTestPageInstance            => _backTestPage            ??= new();
	internal static BalanceCheckPage        BalanceCheckPageInstance        => _balanceCheckPage        ??= new();
	internal static ComboFilterPage         ComboFilterPageInstance         => _comboFilterPage         ??= new();
	internal static GapTrackerPage          GapTrackerPageInstance          => _gapTrackerPage          ??= new();
	internal static HotColdPage             HotColdPageInstance             => _hotColdPage             ??= new();
	internal static PairsTripletsPage       PairsTripletsPageInstance       => _pairsTripletsPage       ??= new();
	internal static PositionalFreqPage      PositionalFreqPageInstance      => _positionalFreqPage      ??= new();
	internal static RundownPage             RundownPageInstance             => _rundownPage             ??= new();
	internal static ScatchersPage           ScatchersPageInstance           => _scatchersPage           ??= new();
	internal static SpendingLogPage         SpendingLogPageInstance         => _spendingLogPage         ??= new();
	internal static SumRangePage            SumRangePageInstance            => _sumRangePage            ??= new();
	internal static TicketScorerPage        TicketScorerPageInstance        => _ticketScorerPage        ??= new();
	internal static WheelingPage            WheelingPageInstance            => _wheelingPage            ??= new();
	internal static TicketPurchaseStatsPage TicketPurchaseStatsPageInstance => _ticketPurchaseStatsPage ??= new();
	internal static TicketSummaryPage       TicketSummaryPageInstance       => _ticketSummaryPage       ??= new();
	internal static ImportTicketPage        ImportTicketPageInstance        => _importTicketPage        ??= new();
	internal static CheckMyNumber           CheckMyNumberPageInstance       => _checkMyNumberPage       ??= new();
	internal static TicketLogPage           TicketLogPageInstance           => _ticketLogPage           ??= new();
	internal static TicketCalendarPage      TicketCalendarPageInstance      => _ticketCalendarPage      ??= new();

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
		Routing.RegisterRoute(nameof(MyFavoritePage),       new SingletonRouteFactory(MyFavoritePageInstance));
		Routing.RegisterRoute(nameof(NotificationsPage),    new SingletonRouteFactory(NotificationsPageInstance));
		Routing.RegisterRoute(nameof(AdvanceGamesPage),     new SingletonRouteFactory(AdvanceGamesPageInstance));
		Routing.RegisterRoute(nameof(SummaryPage),           new SingletonRouteFactory(SummaryPageInstance));
		Routing.RegisterRoute(nameof(DrawSearchPage),        new SingletonRouteFactory(DrawSearchPageInstance));
		Routing.RegisterRoute(nameof(PrintPreviewPage),      new SingletonRouteFactory(PrintPreviewPageInstance));
		Routing.RegisterRoute(nameof(HotSpotPage),           new LazySingletonRouteFactory(() => HotSpotPageInstance));
		Routing.RegisterRoute(nameof(DailyFantasyMAUI.LotteryDirectory.StateLotteryPage), new LazySingletonRouteFactory(() => StateLotteryPageInstance));

		Routing.RegisterRoute(nameof(AboutPage),               new LazySingletonRouteFactory(() => AboutPageInstance));
		Routing.RegisterRoute(nameof(BackTestPage),            new LazySingletonRouteFactory(() => BackTestPageInstance));
		Routing.RegisterRoute(nameof(BalanceCheckPage),        new LazySingletonRouteFactory(() => BalanceCheckPageInstance));
		Routing.RegisterRoute(nameof(ComboFilterPage),         new LazySingletonRouteFactory(() => ComboFilterPageInstance));
		Routing.RegisterRoute(nameof(GapTrackerPage),          new LazySingletonRouteFactory(() => GapTrackerPageInstance));
		Routing.RegisterRoute(nameof(HotColdPage),             new LazySingletonRouteFactory(() => HotColdPageInstance));
		Routing.RegisterRoute(nameof(PairsTripletsPage),       new LazySingletonRouteFactory(() => PairsTripletsPageInstance));
		Routing.RegisterRoute(nameof(PositionalFreqPage),      new LazySingletonRouteFactory(() => PositionalFreqPageInstance));
		Routing.RegisterRoute(nameof(RundownPage),             new LazySingletonRouteFactory(() => RundownPageInstance));
		Routing.RegisterRoute(nameof(ScatchersPage),           new LazySingletonRouteFactory(() => ScatchersPageInstance));
		Routing.RegisterRoute(nameof(SpendingLogPage),         new LazySingletonRouteFactory(() => SpendingLogPageInstance));
		Routing.RegisterRoute(nameof(SumRangePage),            new LazySingletonRouteFactory(() => SumRangePageInstance));
		Routing.RegisterRoute(nameof(TicketScorerPage),        new LazySingletonRouteFactory(() => TicketScorerPageInstance));
		Routing.RegisterRoute(nameof(WheelingPage),            new LazySingletonRouteFactory(() => WheelingPageInstance));
		Routing.RegisterRoute(nameof(TicketPurchaseStatsPage), new LazySingletonRouteFactory(() => TicketPurchaseStatsPageInstance));
		Routing.RegisterRoute(nameof(TicketSummaryPage),       new LazySingletonRouteFactory(() => TicketSummaryPageInstance));
		Routing.RegisterRoute(nameof(ImportTicketPage),        new LazySingletonRouteFactory(() => ImportTicketPageInstance));
		Routing.RegisterRoute(nameof(CheckMyNumber),           new LazySingletonRouteFactory(() => CheckMyNumberPageInstance));
		Routing.RegisterRoute(nameof(TicketLogPage),           new LazySingletonRouteFactory(() => TicketLogPageInstance));
		Routing.RegisterRoute(nameof(TicketCalendarPage),      new LazySingletonRouteFactory(() => TicketCalendarPageInstance));
	}
}
