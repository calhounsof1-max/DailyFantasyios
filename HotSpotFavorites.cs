using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

// ── Fully self-contained "Favorites" feature for the Hot Spot page ────────────────────────
// User's ask 2026-08-22: save a spot-count + number combo once, then play it back anytime
// without re-picking every ball by hand. Deliberately isolated the same way HotSpotMyNumbersPanel/
// Replay_Numbers are — its own Preferences key, its own copy of the couple of slot-key constants
// it needs, zero references into HotSpotPage's internals. Whole feature can be deleted later by
// removing this file, the one options-menu row, the overlay build/wire-in lines in BuildLayout,
// and the HandlePlayFavoritesTapped method. Nothing else in the app references this class.
//
// Saved numbers can't be edited directly on their row (no in-place text box there) — tapping
// ✏️ Edit instead loads a favorite's name/numbers back into the Save boxes up top so they can be
// changed and re-saved, without deleting the favorite first and losing it if you change your
// mind partway through. User's follow-up ask 2026-08-22 after finding Delete-then-retype
// tedious ("do want to go back and type those number again").
public static class HotSpotFavorites
{
    const string KeyFavoritesJson = "hs_favorites_json";

    // Mirrors HotSpotPage's own SlotKey/KeySpots/KeyNumbers/SlotCount — isolated copy per this
    // file's own reasoning above, only needed by HandlePlayFavoritesTapped's caller (HotSpotPage
    // itself uses its real ones; this class never touches slot Preferences directly).
    public sealed record FavoriteEntry(string Id, string Name, int Spots, int[] Numbers);

    static List<FavoriteEntry> LoadAll()
    {
        try
        {
            return JsonSerializer.Deserialize<List<FavoriteEntry>>(Preferences.Get(KeyFavoritesJson, "[]")) ?? new();
        }
        catch
        {
            return new(); // corrupt/old data shouldn't crash the page — just start fresh
        }
    }

    static void SaveAll(List<FavoriteEntry> list) =>
        Preferences.Set(KeyFavoritesJson, JsonSerializer.Serialize(list));

    public static void Add(string name, int spots, int[] numbers)
    {
        var list = LoadAll();
        list.Add(new FavoriteEntry(Guid.NewGuid().ToString("N"), name, spots, numbers.OrderBy(n => n).ToArray()));
        SaveAll(list);
    }

    public static void Delete(string id)
    {
        var list = LoadAll();
        list.RemoveAll(f => f.Id == id);
        SaveAll(list);
    }

    // Overwrites an existing favorite in place, keeping its Id (and so its position in the
    // list) — used by the ✏️ Edit flow below. Falls back to a fresh Add if the id can't be
    // found (e.g. it was deleted from elsewhere between opening Edit and tapping Save).
    public static void Update(string id, string name, int spots, int[] numbers)
    {
        var list = LoadAll();
        int idx = list.FindIndex(f => f.Id == id);
        if (idx < 0) { Add(name, spots, numbers); return; }
        list[idx] = new FavoriteEntry(id, name, spots, numbers.OrderBy(n => n).ToArray());
        SaveAll(list);
    }

    // ── UI: same CC000000-backdrop + centered Border "card" overlay pattern every other
    // HotSpotPage overlay uses (see BuildPayoutOverlay/Replay_Numbers.Build). `onPlaySelected`
    // fires with every checked favorite, in the order they're listed, when "Play Selected" is
    // tapped — the caller (HotSpotPage.HandlePlayFavoritesTapped) owns deciding which ticket
    // slot(s) each one lands in, since only it can touch _selected/_spots/the real slot
    // Preferences. `show` lets the caller open it, freshly rebuilt from disk every time (same
    // "rebuild every open" pattern RefreshOptionsMenuRows/Replay_Numbers.show already use).
    public static Grid Build(Func<List<FavoriteEntry>, Task> onPlaySelected, out Action show)
    {
        var rowsContainer = new VerticalStackLayout { Spacing = 8 };
        // Row checkbox state lives here, keyed by favorite Id, so Play Selected can read it
        // without walking the visual tree — rebuilt fresh every RebuildRows call.
        var checkedIds = new HashSet<string>();
        // Non-null while ✏️ Edit is loaded into the Save boxes above — Save then updates this
        // favorite in place instead of adding a new one. Cleared by a successful Save, by
        // Cancel, or by RebuildRows (e.g. the edited favorite got deleted out from under it).
        string? editingId = null;

        var title = new Label
        {
            Text = "⭐ Favorites", FontSize = 15, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#FFD54F"), HorizontalOptions = LayoutOptions.Center,
        };
        var subtitle = new Label
        {
            Text = "Type numbers below to save them as a favorite. Tap ✏️ on a favorite to edit it.",
            FontSize = 9, TextColor = Color.FromArgb("#8B9DC3"), HorizontalTextAlignment = TextAlignment.Center,
        };

        // ── Save typed-in numbers as a favorite ────────────────────────────────────────────
        var nameEntry = new Entry
        {
            Placeholder = "Favorite name (optional)", PlaceholderColor = Color.FromArgb("#5B6B85"),
            TextColor = Colors.White, BackgroundColor = Color.FromArgb("#101923"),
            FontSize = 12, HeightRequest = 34,
        };
        // Keyboard.Default (not Numeric) so a plain space works as the separator (Android's
        // numeric keyboard has no space key) — commas still work too, ParseTypedNumbers below
        // accepts either, but the placeholder only shows spaces per the user's explicit ask
        // 2026-08-22 ("only thing i dont like is put commas by each number"). Save/Update is
        // disabled whenever this is blank (see RefreshSaveButtonState below) — the old "leave it
        // blank to grab whatever's on the grid" fallback was removed after it caused a real
        // duplicate-favorite bug: an empty-box tap silently grabbed the main grid's current
        // selection instead of doing nothing.
        var numbersEntry = new Entry
        {
            Placeholder = "Numbers, e.g. 7 17 27 37", PlaceholderColor = Color.FromArgb("#5B6B85"),
            TextColor = Colors.White, BackgroundColor = Color.FromArgb("#101923"),
            FontSize = 12, HeightRequest = 34, Keyboard = Keyboard.Default,
        };
        // Re-enabled below (nameEntry/numbersEntry TextChanged, Edit, Cancel) whenever there's
        // something genuinely new to save — HotSpotPage's own _btnSave uses the exact same
        // "dead until there's something new, dead again the instant it's used" rule (see billy.md
        // 2026-08-13) after a real double-tap on THAT button created a duplicate ticket save.
        // This button hit the identical bug 2026-08-22 (double-tap on Update duplicated a
        // favorite instead of overwriting it in place) — same fix applies here.
        var saveStatusLabel = new Label { FontSize = 10, TextColor = Color.FromArgb("#4CAF7D"), IsVisible = false };
        var btnSaveCurrent = new Button
        {
            Text = "💾 Save as Favorite", FontSize = 12, FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White, BackgroundColor = Color.FromArgb("#1A4FAA"), CornerRadius = 8,
            HeightRequest = 36, MinimumHeightRequest = 0,
        };
        // Shown only while editingId is set — names which favorite is being edited and gives a
        // way out that doesn't require saving over it.
        var editBannerLabel = new Label
        {
            Text = "", FontSize = 10, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#FFD54F"), VerticalOptions = LayoutOptions.Center,
        };
        var btnCancelEdit = new Button
        {
            Text = "Cancel", FontSize = 10, TextColor = Color.FromArgb("#8B9DC3"),
            BackgroundColor = Colors.Transparent, Padding = new Thickness(4, 0),
            HeightRequest = 20, MinimumHeightRequest = 0, HorizontalOptions = LayoutOptions.End,
        };
        var editBannerRow = new Grid
        {
            IsVisible = false,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            Children = { editBannerLabel, btnCancelEdit },
        };
        Grid.SetColumn(btnCancelEdit, 1);
        var saveBlock = new VerticalStackLayout
        {
            Spacing = 4, Children = { nameEntry, numbersEntry, editBannerRow, btnSaveCurrent, saveStatusLabel },
        };

        // Save/Update is only ever enabled while the Numbers box actually has something typed in
        // it — user's explicit ask 2026-08-22, after an empty-box tap fell back to whatever was
        // still selected on the main grid and created an unwanted duplicate favorite. nameEntry
        // doesn't gate this (a name is optional either way), only numbersEntry does. This also
        // naturally re-covers the double-tap case below: the Clicked handler clears numbersEntry
        // on success, which disables the button via this same check before a queued second tap
        // could reach it.
        void RefreshSaveButtonState() => btnSaveCurrent.IsEnabled = !string.IsNullOrWhiteSpace(numbersEntry.Text);
        numbersEntry.TextChanged += (_, _) => RefreshSaveButtonState();
        RefreshSaveButtonState();

        void EnterEditMode(FavoriteEntry fav)
        {
            editingId = fav.Id;
            nameEntry.Text = fav.Name;
            numbersEntry.Text = string.Join(" ", fav.Numbers);
            btnSaveCurrent.Text = "💾 Update Favorite";
            editBannerLabel.Text = $"Editing \"{fav.Name}\"";
            editBannerRow.IsVisible = true;
            saveStatusLabel.IsVisible = false;
        }

        void ExitEditMode()
        {
            editingId = null;
            nameEntry.Text = "";
            numbersEntry.Text = "";
            btnSaveCurrent.Text = "💾 Save as Favorite";
            editBannerRow.IsVisible = false;
        }

        btnCancelEdit.Clicked += (_, _) => ExitEditMode();

        // Parses "7, 17 27,37" style free text into 1-80, deduped, no more than 10 numbers —
        // same 10-spot ceiling SpotOptions uses on the main page. Returns null (and sets an
        // error on saveStatusLabel itself) on anything invalid, so the caller can just bail.
        int[]? ParseTypedNumbers(string text)
        {
            var tokens = text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var numbers = new List<int>();
            foreach (var tok in tokens)
            {
                if (!int.TryParse(tok.Trim(), out int n) || n < 1 || n > 80)
                {
                    saveStatusLabel.TextColor = Color.FromArgb("#E0522C");
                    saveStatusLabel.Text = $"\"{tok.Trim()}\" isn't a valid number 1-80.";
                    saveStatusLabel.IsVisible = true;
                    return null;
                }
                numbers.Add(n);
            }
            numbers = numbers.Distinct().OrderBy(n => n).ToList();
            if (numbers.Count > 10)
            {
                saveStatusLabel.TextColor = Color.FromArgb("#E0522C");
                saveStatusLabel.Text = "Up to 10 numbers only.";
                saveStatusLabel.IsVisible = true;
                return null;
            }
            return numbers.ToArray();
        }

        // ── List + footer (built up front, populated by RebuildRows below) ────────────────
        var listScroll = new ScrollView { MaximumHeightRequest = 380, Content = rowsContainer };

        var btnPlaySelected = new Button
        {
            Text = "▶ Play Selected", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#2E7D32"), CornerRadius = 8, Padding = new Thickness(10, 4),
            HeightRequest = 34, MinimumHeightRequest = 0, HorizontalOptions = LayoutOptions.Start,
            IsEnabled = false, Opacity = 0.5,
        };
        var playSpinner = new ActivityIndicator
        {
            Color = Colors.White, WidthRequest = 18, HeightRequest = 18, IsRunning = false, IsVisible = false,
        };
        var playBtnHost = new Grid { HorizontalOptions = LayoutOptions.Start, Children = { btnPlaySelected, playSpinner } };

        Grid overlay = null!;
        var btnClose = new Button
        {
            Text = "Close", FontSize = 13, TextColor = Color.FromArgb("#FFD54F"),
            BackgroundColor = Colors.Transparent, HorizontalOptions = LayoutOptions.End,
            Padding = new Thickness(4, 4),
        };
        btnClose.Clicked += (_, _) => overlay.IsVisible = false;

        var footerRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 8,
        };
        footerRow.Add(playBtnHost, 0, 0);
        footerRow.Add(btnClose, 1, 0);

        void RefreshPlayButtonState()
        {
            btnPlaySelected.Text = checkedIds.Count > 0 ? $"▶ Play Selected ({checkedIds.Count})" : "▶ Play Selected";
            btnPlaySelected.IsEnabled = checkedIds.Count > 0;
            btnPlaySelected.Opacity = btnPlaySelected.IsEnabled ? 1.0 : 0.5;
        }

        void RebuildRows()
        {
            rowsContainer.Children.Clear();
            checkedIds.Clear();
            RefreshPlayButtonState();

            var favorites = LoadAll();
            // The favorite being edited can vanish out from under this list (deleted from
            // elsewhere, or this very rebuild follows its own Delete) — drop out of edit mode
            // automatically rather than leaving Update pointed at an id that no longer exists.
            if (editingId != null && !favorites.Any(f => f.Id == editingId)) ExitEditMode();

            if (favorites.Count == 0)
            {
                rowsContainer.Children.Add(new Label
                {
                    Text = "No favorites saved yet.", FontSize = 12, TextColor = Color.FromArgb("#8B9DC3"),
                    HorizontalTextAlignment = TextAlignment.Center, Margin = new Thickness(0, 10, 0, 4),
                });
                return;
            }

            foreach (var fav in favorites)
            {
                var checkBox = new CheckBox { Color = Color.FromArgb("#4CAF7D") };
                checkBox.CheckedChanged += (_, e) =>
                {
                    if (e.Value) checkedIds.Add(fav.Id); else checkedIds.Remove(fav.Id);
                    RefreshPlayButtonState();
                };

                var nameLabel = new Label
                {
                    Text = fav.Name, FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Colors.White,
                };
                var spotsLabel = new Label
                {
                    Text = $"{fav.Spots} Spot", FontSize = 10, TextColor = Color.FromArgb("#8B9DC3"),
                };
                var numbersLabel = new Label
                {
                    // Read-only — no gesture recognizer, no Entry right on the row itself. Tap
                    // ✏️ Edit to load these into the Save boxes above and change them there.
                    Text = string.Join(", ", fav.Numbers), FontSize = 12, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#FFD54F"), LineBreakMode = LineBreakMode.WordWrap,
                };
                var details = new VerticalStackLayout
                {
                    Spacing = 1, Children = { nameLabel, spotsLabel, numbersLabel },
                };

                var btnEdit = new Button
                {
                    Text = "✏️", FontSize = 13, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#3A4A63"),
                    CornerRadius = 8, WidthRequest = 34, HeightRequest = 34, MinimumHeightRequest = 0, Padding = 0,
                };
                btnEdit.Clicked += (_, _) => EnterEditMode(fav);

                var btnDelete = new Button
                {
                    Text = "🗑", FontSize = 13, TextColor = Colors.White, BackgroundColor = Color.FromArgb("#7A2E2E"),
                    CornerRadius = 8, WidthRequest = 34, HeightRequest = 34, MinimumHeightRequest = 0, Padding = 0,
                };
                btnDelete.Clicked += (_, _) =>
                {
                    Delete(fav.Id);
                    RebuildRows();
                };

                var row = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Auto),
                    },
                    ColumnSpacing = 6,
                };
                row.Add(checkBox, 0, 0);
                row.Add(details, 1, 0);
                row.Add(btnEdit, 2, 0);
                row.Add(btnDelete, 3, 0);

                var rowCard = new Border
                {
                    BackgroundColor = Color.FromArgb("#182432"),
                    Stroke = new SolidColorBrush(Color.FromArgb("#243447")),
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = 10 },
                    Padding = new Thickness(8, 6),
                    Content = row,
                };
                rowsContainer.Children.Add(rowCard);
            }
        }

        btnSaveCurrent.Clicked += (_, _) =>
        {
            // Guard stays even though RefreshSaveButtonState already keeps this button disabled
            // whenever numbersEntry is blank — belt-and-suspenders against Android firing Clicked
            // twice for one physical tap (confirmed live 2026-08-22 on ✏️ Edit → Update: the first
            // call updated the favorite correctly, the second — arriving before this line existed —
            // duplicated it). Numbers is guaranteed non-blank here since the button can't be
            // tapped otherwise, so there's no separate "empty box" branch to handle anymore.
            if (!btnSaveCurrent.IsEnabled) return;
            btnSaveCurrent.IsEnabled = false;

            var typed = ParseTypedNumbers(numbersEntry.Text);
            if (typed == null) { RefreshSaveButtonState(); return; } // ParseTypedNumbers already set the error on saveStatusLabel
            int[] numbers = typed;

            int spots = numbers.Length;
            string name = string.IsNullOrWhiteSpace(nameEntry.Text)
                ? $"Favorite {LoadAll().Count + 1}"
                : nameEntry.Text.Trim();

            string verb;
            if (editingId != null)
            {
                Update(editingId, name, spots, numbers);
                verb = "Updated";
                editingId = null;
                btnSaveCurrent.Text = "💾 Save as Favorite";
                editBannerRow.IsVisible = false;
            }
            else
            {
                Add(name, spots, numbers);
                verb = "Saved";
            }

            // Clearing numbersEntry fires TextChanged -> RefreshSaveButtonState, which disables
            // the button since it's blank again — exactly the state we want it left in.
            nameEntry.Text = "";
            numbersEntry.Text = "";
            saveStatusLabel.TextColor = Color.FromArgb("#4CAF7D");
            saveStatusLabel.Text = $"{verb} \"{name}\" — {spots} Spot.";
            saveStatusLabel.IsVisible = true;
            RebuildRows();
        };

        btnPlaySelected.Clicked += async (_, _) =>
        {
            var favorites = LoadAll();
            var chosen = favorites.Where(f => checkedIds.Contains(f.Id)).ToList();
            if (chosen.Count == 0) return;

            btnPlaySelected.IsEnabled = false;
            btnPlaySelected.Opacity = 0;
            playSpinner.IsVisible = true;
            playSpinner.IsRunning = true;
            await Task.Yield(); // let the spinner paint before the caller's own (possibly synchronous) grid repaint blocks the UI thread

            await onPlaySelected(chosen);

            playSpinner.IsRunning = false;
            playSpinner.IsVisible = false;
            overlay.IsVisible = false;
        };

        var card = new Border
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            Stroke = new SolidColorBrush(Color.FromArgb("#334155")),
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(14, 12),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            WidthRequest = 340,
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children = { title, subtitle, saveBlock, new BoxView { HeightRequest = 1, Color = Color.FromArgb("#334155") }, listScroll, footerRow },
            },
        };

        overlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#CC000000"), IsVisible = false,
            Children = { card },
        };

        show = () =>
        {
            saveStatusLabel.IsVisible = false;
            RebuildRows();
            overlay.IsVisible = true;
        };

        return overlay;
    }
}
