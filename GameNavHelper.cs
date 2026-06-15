namespace DailyFantasyMAUI;

/// <summary>
/// Shared Games dropdown navigation used by every sub-page header.
/// </summary>
static class GameNavHelper
{
    public static async Task ShowGamesDropdown(Page caller)
    {
        string? result = await caller.DisplayActionSheet(null, "Cancel", null,
            "Fantasy 5", "Super Lotto", "Daily 3", "Daily 4",
            "Powerball", "Mega Millions", "Daily Derby", "Jackpot Winners");
        if (result == null || result == "Cancel") return;

        switch (result)
        {
            case "Fantasy 5":
                AppShell.WinnerPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(WinnerPage), false);
                break;
            case "Super Lotto":
                SuperLottoPage.ComingFrom = "main";
                AppShell.SuperLottoPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(SuperLottoPage), false);
                break;
            case "Daily 3":
                Daily3Page.ComingFrom = "main";
                AppShell.Daily3PageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(Daily3Page), false);
                break;
            case "Daily 4":
                Daily4Page.ComingFrom = "main";
                AppShell.Daily4PageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(Daily4Page), false);
                break;
            case "Powerball":
                PowerballPage.ComingFrom = "main";
                AppShell.PowerballPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(PowerballPage), false);
                break;
            case "Mega Millions":
                MegaMillionsPage.ComingFrom = "main";
                AppShell.MegaMillionsPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(MegaMillionsPage), false);
                break;
            case "Daily Derby":
                DailyDerbyPage.ComingFrom = "main";
                AppShell.DailyDerbyPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(DailyDerbyPage), false);
                break;
            case "Jackpot Winners":
                AppShell.JackpotPageInstance.PrePosition(true);
                await Shell.Current.GoToAsync(nameof(JackpotPage), false);
                break;
        }
    }
}
