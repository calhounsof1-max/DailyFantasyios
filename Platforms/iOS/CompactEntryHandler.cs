using CoreGraphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace DailyFantasyMAUI;

/// <summary>
/// Subclass of MauiTextField that overrides text/editing rects to remove
/// UIKit's default vertical padding, so MAUI's HeightRequest is respected.
/// </summary>
class ZeroPaddingTextField : MauiTextField
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
    protected override MauiTextField CreatePlatformView()
    {
        var tf = new ZeroPaddingTextField();
        tf.BorderStyle = UITextBorderStyle.None;
        tf.VerticalAlignment = UIControlContentVerticalAlignment.Center;
        return tf;
    }
}
