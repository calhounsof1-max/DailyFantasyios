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
    // NoIntrinsicMetric lets MAUI/Grid fully control the height via HeightRequest
    // and fixed row sizes.  The old value of 34 caused the UITextField to leak
    // outside its 24 pt grid cell, making boxes appear huge.
    public override CGSize IntrinsicContentSize
        => new CGSize(UIView.NoIntrinsicMetric, UIView.NoIntrinsicMetric);

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
        // Clip so the text field never visually bleeds outside its grid cell.
        tf.ClipsToBounds = true;
        return tf;
    }

    protected override void ConnectHandler(MauiTextField platformView)
    {
        base.ConnectHandler(platformView); // MAUI's property mappers run here
        // Set AFTER base so MAUI's keyboard/content-type mapper can't override us.
        // OneTimeCode prevents iOS from painting the yellow autofill tint.
        platformView.TextContentType = UITextContentType.OneTimeCode;
    }
}
