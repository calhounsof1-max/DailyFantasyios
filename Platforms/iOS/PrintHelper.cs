using UIKit;
using WebKit;

namespace DailyFantasyMAUI;

public static class PrintHelper
{
    public static void PrintHtml(string html, string jobName)
    {
        var renderer = new UIPrintPageRenderer();
        var formatter = new UIMarkupTextPrintFormatter(html);
        renderer.AddPrintFormatter(formatter, 0);

        var info = UIPrintInfo.PrintInfo;
        info.JobName = jobName;
        info.OutputType = UIPrintInfoOutputType.General;

        var controller = UIPrintInteractionController.SharedPrintController;
        controller.PrintInfo = info;
        controller.PrintPageRenderer = renderer;
        controller.Present(true, null);
    }
}
