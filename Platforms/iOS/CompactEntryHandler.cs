using Microsoft.Maui.Handlers;
using UIKit;

namespace DailyFantasyMAUI;

/// <summary>
/// Removes UITextField's default border and internal padding so that
/// HeightRequest values are actually respected on iOS (matching Android sizes).
/// AppendToMapping runs AFTER all MAUI's own property mappers, ensuring our
/// changes are last and not overwritten.
/// </summary>
public class CompactEntryHandler : EntryHandler
{
    public static void Register()
    {
        Mapper.AppendToMapping("CompactEntry", (handler, view) =>
        {
            if (handler.PlatformView is UITextField tf)
            {
                tf.BorderStyle = UITextBorderStyle.None;
                tf.VerticalAlignment = UIControlContentVerticalAlignment.Center;
                // Clear any content insets that inflate the height
                tf.LayoutMargins = UIEdgeInsets.Zero;
            }
        });
    }
}
