using CoreGraphics;
using Microsoft.Maui.Handlers;
using UIKit;

namespace DailyFantasyMAUI;

/// <summary>
/// Custom UITextField that overrides the text/editing rects to remove
/// UIKit's default vertical padding, so HeightRequest is actually respected.
/// </summary>
class ZeroPaddingTextField : UITextField
{
    public override CGRect TextRect(CGRect forBounds)
        => forBounds.Inset(8, 0);

    public override CGRect EditingRect(CGRect forBounds)
        => forBounds.Inset(8, 0);

    public override CGRect PlaceholderRect(CGRect forBounds)
        => forBounds.Inset(8, 0);
}

public class CompactEntryHandler : EntryHandler
{
    protected override UITextField CreatePlatformView()
    {
        var tf = new ZeroPaddingTextField();
        tf.BorderStyle = UITextBorderStyle.None;
        tf.VerticalAlignment = UIControlContentVerticalAlignment.Center;
        return tf;
    }
}
