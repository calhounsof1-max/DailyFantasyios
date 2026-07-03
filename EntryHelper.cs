/// <summary>
/// Helpers for Entry backspace-navigation across number-input boxes.
/// - Backspace on empty box → navigate back, highlight previous box's content.
/// - Backspace when all text is selected → navigate back again (without deleting), highlight.
/// - Typing replaces the highlighted text (standard Android selection behavior).
/// </summary>
public static class EntryHelper
{
    /// <summary>
    /// Attaches backspace-navigation to an entry. <paramref name="onBackspace"/> should call
    /// RetreatFocus (which focuses the previous entry and calls SelectAll on it).
    /// </summary>
    public static void AttachBackspace(Entry entry, Action onBackspace)
    {
        entry.HandlerChanged += (_, _) =>
        {
#if ANDROID
            if (entry.Handler?.PlatformView is Android.Widget.EditText et)
                et.SetOnKeyListener(new BackspaceListener(entry, onBackspace));
#endif
        };
    }

    /// <summary>
    /// Focuses an entry and selects all its text so the user can replace or keep navigating back.
    /// </summary>
    public static void SelectAll(Entry entry)
    {
        entry.Focus();
        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
        {
#if ANDROID
            if (entry.Handler?.PlatformView is Android.Widget.EditText et)
                et.SelectAll();
#endif
        });
    }

#if ANDROID
    class BackspaceListener : Java.Lang.Object, Android.Views.View.IOnKeyListener
    {
        readonly Entry _entry;
        readonly Action _cb;
        public BackspaceListener(Entry entry, Action cb) { _entry = entry; _cb = cb; }

        public bool OnKey(Android.Views.View? v, Android.Views.Keycode keyCode, Android.Views.KeyEvent? e)
        {
            if (keyCode == Android.Views.Keycode.Del && e?.Action == Android.Views.KeyEventActions.Down)
            {
                bool isEmpty     = string.IsNullOrEmpty(_entry.Text);
                bool allSelected = !isEmpty
                    && v is Android.Widget.EditText et
                    && et.SelectionStart == 0
                    && et.SelectionEnd == (et.Text?.Length ?? 0);

                if (isEmpty || allSelected)
                {
                    _cb();
                    return allSelected; // consume event when all-selected to prevent deletion
                }
            }
            return false;
        }
    }
#endif
}
