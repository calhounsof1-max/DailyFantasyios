using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

/// <summary>
/// Starts the three heavyweight CSV loads as soon as the app launches (during
/// the splash screen), so by the time MainPage.OnAppearing fires the data is
/// already loaded — or nearly so — and the page feels instant.
/// </summary>
public static class AppPreloader
{
    public static Task<List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)>>
        F5Task { get; private set; } = Task.FromResult(
            new List<(string, int, int[], DrawPrizeTier[])>());

    public static Task<List<(string DrawDate, int DrawNumber, int[] MainNumbers, int Mega, DrawPrizeTier[] Prizes)>>
        SLTask { get; private set; } = Task.FromResult(
            new List<(string, int, int[], int, DrawPrizeTier[])>());

    public static Task<List<(string DrawDate, int DrawNumber, int[] Numbers, string DrawTime)>>
        D3Task { get; private set; } = Task.FromResult(
            new List<(string, int, int[], string)>());

    /// <summary>Call once in App() constructor — before InitializeComponent().</summary>
    public static void Start()
    {
        // These are async tasks — they start immediately and run concurrently
        // while the MAUI shell is being built and the splash screen is showing.
        F5Task = GetDataEntry.LoadF5CsvDraws();
        SLTask = GetDataEntry.LoadSLCsvDraws();
        D3Task = GetDataEntry.LoadD3CsvDraws();
    }
}
