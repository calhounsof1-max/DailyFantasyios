using Microsoft.Maui.Handlers;
using UIKit;
using CoreGraphics;

namespace DailyFantasyMAUI;

/// <summary>
/// Removes the extra internal padding UITextField adds on iOS so that
/// HeightRequest values match Android's rendering (no inflated heights).
/// </summary>
public class CompactEntryHandler : EntryHandler
{
    protected override void ConnectHandler(UITextField platformView)
    {
        base.ConnectHandler(platformView);
        // Remove the default rounded-rect border and its extra vertical padding.
        platformView.BorderStyle = UITextBorderStyle.None;
        // Zero out the internal content insets so the text is centred within
        // whatever height MAUI requests, not within UITextField's default minimum.
        platformView.VerticalAlignment = UIControlContentVerticalAlignment.Center;
    }
}
