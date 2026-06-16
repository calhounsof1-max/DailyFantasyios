using Microsoft.Maui.Handlers;
using UIKit;

namespace DailyFantasyMAUI;

/// <summary>
/// Forces MAUI Button to honour HeightRequest on iOS.
/// iOS 15+ UIButtonConfiguration adds internal content insets that make the button
/// report a large desired size; overriding GetDesiredSize caps it at HeightRequest
/// so the MAUI layout engine allocates the correct frame, and ClipsToBounds ensures
/// the UIKit rendering stays within that frame.
/// </summary>
public class CompactButtonHandler : ButtonHandler
{
    protected override UIButton CreatePlatformView()
    {
        var btn = base.CreatePlatformView();
        btn.ClipsToBounds = true;
        return btn;
    }

    public override Size GetDesiredSize(double widthConstraint, double heightConstraint)
    {
        var result = base.GetDesiredSize(widthConstraint, heightConstraint);
        if (VirtualView?.HeightRequest > 0)
            return new Size(result.Width, VirtualView.HeightRequest);
        return result;
    }
}
