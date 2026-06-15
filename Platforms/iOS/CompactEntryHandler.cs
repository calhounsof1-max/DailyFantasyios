using CoreGraphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace DailyFantasyMAUI;

/// <summary>
/// Subclass of MauiTextField that removes UIKit's internal vertical padding and
/// reports a compact intrinsic height so iOS layout doesn't inflate row heights.
/// </summary>
class ZeroPaddingTextField : MauiTextField
{
    // Tell iOS layout system this control is 34pt tall (standard touch target).
    // Without this, UITextField reports ~44pt+ which inflates Auto grid rows.
    public override CGSize IntrinsicContentSize
        => new CGSize(UIView.NoIntrinsicMetric, 34);

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
