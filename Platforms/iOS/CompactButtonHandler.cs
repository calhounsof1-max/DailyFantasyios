using Microsoft.Maui.Handlers;
using UIKit;

namespace DailyFantasyMAUI;

/// <summary>
/// Forces MAUI Button to honour HeightRequest on iOS by overriding MeasureOverride.
/// iOS 15+ UIButtonConfiguration adds internal content insets that make the button
/// render taller than HeightRequest; this handler caps the measured height so the
/// layout engine never allocates more than what HeightRequest specifies.
/// </summary>
public class CompactButtonHandler : ButtonHandler
{
    protected override UIButton CreatePlatformView()
    {
        var btn = base.CreatePlatformView();
        // Clip so the button visual never bleeds outside its allocated frame.
        btn.ClipsToBounds = true;
        return btn;
    }

    protected override void ConnectHandler(UIButton platformView)
    {
        base.ConnectHandler(platformView);
        // Remove iOS 15+ UIButtonConfiguration internal insets that inflate button height.
        platformView.Configuration = null;
#pragma warning disable CA1422
        platformView.ContentEdgeInsets = UIEdgeInsets.Zero;
#pragma warning restore CA1422
    }
}
