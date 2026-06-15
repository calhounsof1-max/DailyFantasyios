using Microsoft.Maui.Controls.Handlers.Compatibility;
using UIKit;

namespace DailyFantasyMAUI;

/// <summary>
/// Custom Shell renderer that hides the iOS UINavigationBar so it doesn't
/// steal ~44pt of layout height. MAUI's Shell.NavBarIsVisible="False" hides
/// the bar content but the native UINavigationBar still renders and takes
/// space; this tracker is called by MAUI's own Shell setup and gets last word.
/// </summary>
public class CustomShellRenderer : ShellRenderer
{
    protected override IShellNavBarAppearanceTracker CreateNavBarAppearanceTracker()
        => new HideNavBarTracker();
}

class HideNavBarTracker : IShellNavBarAppearanceTracker
{
    public void SetAppearance(UINavigationController controller, ShellAppearance appearance)
        => controller.SetNavigationBarHidden(true, false);

    public void ResetAppearance(UINavigationController controller)
        => controller.SetNavigationBarHidden(true, false);

    public void UpdateLayout(UINavigationController controller)
        => controller.SetNavigationBarHidden(true, false);

    public void Dispose() { }
}
