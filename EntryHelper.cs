/// <summary>
/// Helpers for Entry backspace-navigation across number-input boxes.
/// - Backspace on empty box → navigate back, highlight previous box's content.
/// - Backspace when all text is selected → navigate back again (without deleting), highlight.
/// - Typing replaces the highlighted text (standard selection behavior).
/// </summary>
public static class EntryHelper
{
    public static void AttachBackspace(Entry entry, Action onBackspace)
    {
        entry.HandlerChanged += (_, _) =>
        {
#if ANDROID
            if (entry.Handler?.PlatformView is Android.Widget.EditText et)
                et.SetOnKeyListener(new AndroidBackspaceListener(entry, onBackspace));
#elif IOS
            if (entry.Handler?.PlatformView is UIKit.UITextField tf
                && tf.Delegate is not iOSBackspaceDelegate)
            {
                var prev = tf.Delegate;
                tf.Delegate = new iOSBackspaceDelegate(entry, prev, onBackspace);
            }
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
#elif IOS
            CoreFoundation.DispatchQueue.MainQueue.DispatchAfter(
                new Foundation.NSTimeInterval(0.05),
                () =>
                {
                    if (entry.Handler?.PlatformView is UIKit.UITextField tf)
                        tf.SelectAll(tf);
                });
#endif
        });
    }

#if ANDROID
    class AndroidBackspaceListener : Java.Lang.Object, Android.Views.View.IOnKeyListener
    {
        readonly Entry _entry;
        readonly Action _cb;
        public AndroidBackspaceListener(Entry entry, Action cb) { _entry = entry; _cb = cb; }

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
#elif IOS
    class iOSBackspaceDelegate : UIKit.UITextFieldDelegate
    {
        readonly Entry _entry;
        readonly UIKit.IUITextFieldDelegate? _prev;
        readonly Action _cb;

        public iOSBackspaceDelegate(Entry entry, UIKit.IUITextFieldDelegate? prev, Action cb)
        {
            _entry = entry;
            _prev  = prev;
            _cb    = cb;
        }

        public override bool ShouldChangeCharacters(UIKit.UITextField textField, Foundation.NSRange range, string replacementString)
        {
            if (replacementString.Length == 0) // delete/backspace
            {
                string text   = textField.Text ?? "";
                bool isEmpty  = text.Length == 0;

                // All-selected: the selected range spans the entire text AND there is an actual
                // selection (not just a cursor), so SelectedTextRange is non-empty.
                bool allSelected = !isEmpty
                    && textField.SelectedTextRange != null
                    && !textField.SelectedTextRange.Empty
                    && range.Location == 0
                    && (nint)range.Length == (nint)text.Length;

                if (isEmpty || allSelected)
                {
                    _cb();
                    return false; // prevent deletion; for empty there is nothing to delete anyway
                }
            }
            return _prev?.ShouldChangeCharacters(textField, range, replacementString) ?? true;
        }

        public override bool ShouldReturn(UIKit.UITextField textField)
            => _prev?.ShouldReturn(textField) ?? true;

        public override bool ShouldBeginEditing(UIKit.UITextField textField)
            => _prev?.ShouldBeginEditing(textField) ?? true;

        public override bool ShouldEndEditing(UIKit.UITextField textField)
            => _prev?.ShouldEndEditing(textField) ?? true;

        public override void EditingStarted(UIKit.UITextField textField)
            => _prev?.EditingStarted(textField);

        public override void EditingEnded(UIKit.UITextField textField)
            => _prev?.EditingEnded(textField);
    }
#endif
}
