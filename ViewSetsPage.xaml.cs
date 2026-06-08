namespace DailyFantasyMAUI;

public partial class ViewSetsPage : ContentPage
{
    bool _isPanning = false;

    // Edit row state
    string  _editPrefKey = "";
    int     _editSlot;
    int     _editRowIdx;
    int     _editStride;
    int     _editCols;
    int     _editSpecialCol;
    bool    _editHasTime;
    List<Entry> _editEntries = [];
    Entry?      _editTimeEntry;

    // Stride = values per row in storage (= Cols for most games, = 4 for Daily Derby which stores h1,h2,h3,time)
    static readonly (string Caption, string PrefKey, int Rows, int Cols, int SpecialCol, string SpecialLabel, string AccentColor, int Stride, bool HasTime)[] Games =
    {
        ("Fantasy 5",        "f5_set_", 10, 5, -1, "",     "#FF8F00", 5, false),
        ("Super Lotto Plus", "sl_set_", 10, 6,  5, "Mega", "#7B1FA2", 6, false),
        ("Daily 3",          "d3_set_", 10, 3, -1, "",     "#1565C0", 3, false),
        ("Daily 4",          "d4_set_", 10, 4, -1, "",     "#00695C", 4, false),
        ("Powerball",        "pb_set_", 10, 6,  5, "PB",   "#C62828", 6, false),
        ("Daily Derby",      "dd_set_", 10, 3, -1, "",     "#5D4037", 4, true),
    };

    public ViewSetsPage()
    {
        InitializeComponent();
    }

    internal void PrePosition(bool fromRight)
    {
        double w = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        TranslationX = fromRight ? w : -w;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = this.TranslateTo(0, 0, 220, Easing.CubicOut);
        BuildSets();
    }

    protected override bool OnBackButtonPressed()
    {
        _ = NavigateBack();
        return true;
    }

    private async void BtnBack_Clicked(object sender, EventArgs e) => await NavigateBack();

    // ── Edit row overlay ─────────────────────────────────────────────────────

    private void ShowEditRow(string prefKey, int slot, int rowIdx, int stride, int cols, int specialCol, bool hasTime)
    {
        _editPrefKey   = prefKey;
        _editSlot      = slot;
        _editRowIdx    = rowIdx;
        _editStride    = stride;
        _editCols      = cols;
        _editSpecialCol = specialCol;
        _editHasTime   = hasTime;

        // Load current values
        string raw  = Preferences.Get($"{prefKey}{slot}", "");
        string[] vals = string.IsNullOrEmpty(raw)
            ? new string[10 * stride]
            : raw.Split('|');
        if (vals.Length < 10 * stride) Array.Resize(ref vals, 10 * stride);

        editFields.Children.Clear();
        _editEntries.Clear();
        _editTimeEntry = null;

        int mainCols = specialCol >= 0 ? specialCol : cols;

        // Main numbers
        var mainRow = new HorizontalStackLayout { Spacing = 6, HorizontalOptions = LayoutOptions.Center };
        for (int c = 0; c < mainCols; c++)
        {
            int idx = rowIdx * stride + c;
            string cur = idx < vals.Length ? vals[idx] : "";
            var entry = new Entry
            {
                Text = cur,
                Keyboard = Keyboard.Numeric,
                MaxLength = 2,
                WidthRequest = 52,
                HeightRequest = 44,
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                BackgroundColor = Color.FromArgb("#F0F4F8"),
                TextColor = Color.FromArgb("#1E2733"),
            };
            entry.Focused += SelectAllOnFocus;
            _editEntries.Add(entry);
            mainRow.Children.Add(entry);
        }

        // Special ball
        if (specialCol >= 0)
        {
            mainRow.Children.Add(new BoxView
            {
                BackgroundColor = Color.FromArgb("#E53935"),
                WidthRequest = 2, VerticalOptions = LayoutOptions.Fill,
                Margin = new Thickness(4, 6)
            });
            int idx = rowIdx * stride + specialCol;
            string cur = idx < vals.Length ? vals[idx] : "";
            var spEntry = new Entry
            {
                Text = cur,
                Keyboard = Keyboard.Numeric,
                MaxLength = 2,
                WidthRequest = 52,
                HeightRequest = 44,
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                BackgroundColor = Color.FromArgb("#FFEBEE"),
                TextColor = Color.FromArgb("#C62828"),
            };
            spEntry.Focused += SelectAllOnFocus;
            _editEntries.Add(spEntry);
            mainRow.Children.Add(spEntry);
        }

        editFields.Children.Add(mainRow);

        // Time entry for Daily Derby
        if (hasTime)
        {
            int tIdx = rowIdx * stride + 3;
            string curTime = tIdx < vals.Length ? vals[tIdx] : "";
            _editTimeEntry = new Entry
            {
                Text = curTime,
                Keyboard = Keyboard.Numeric,
                MaxLength = 3,
                WidthRequest = 80,
                HeightRequest = 44,
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                BackgroundColor = Color.FromArgb("#FBE9E7"),
                TextColor = Color.FromArgb("#5D4037"),
                Placeholder = "000",
            };
            _editTimeEntry.Focused += SelectAllOnFocus;
            var timeRow = new HorizontalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center };
            timeRow.Children.Add(new Label
            {
                Text = "⏱ Time (last 3):",
                FontSize = 13,
                TextColor = Color.FromArgb("#5D4037"),
                VerticalOptions = LayoutOptions.Center
            });
            timeRow.Children.Add(_editTimeEntry);
            editFields.Children.Add(timeRow);
        }

        editTitle.Text = $"Edit Row {rowIdx + 1}";
        editOverlay.IsVisible = true;
    }

    static void SelectAllOnFocus(object? sender, FocusEventArgs e)
    {
        if (sender is not Entry en) return;
        Task.Delay(150).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            en.CursorPosition = 0;
            en.SelectionLength = en.Text?.Length ?? 0;
        }));
    }

    private void BtnEditCancel_Clicked(object sender, EventArgs e)
    {
        editOverlay.IsVisible = false;
    }

    private void BtnEditSave_Clicked(object sender, EventArgs e)
    {
        string raw  = Preferences.Get($"{_editPrefKey}{_editSlot}", "");
        string[] vals = string.IsNullOrEmpty(raw)
            ? new string[10 * _editStride]
            : raw.Split('|');
        if (vals.Length < 10 * _editStride) Array.Resize(ref vals, 10 * _editStride);

        int mainCols = _editSpecialCol >= 0 ? _editSpecialCol : _editCols;
        for (int c = 0; c < mainCols; c++)
            vals[_editRowIdx * _editStride + c] = _editEntries[c].Text?.Trim() ?? "";

        if (_editSpecialCol >= 0 && _editEntries.Count > mainCols)
            vals[_editRowIdx * _editStride + _editSpecialCol] = _editEntries[mainCols].Text?.Trim() ?? "";

        if (_editHasTime && _editTimeEntry != null)
            vals[_editRowIdx * _editStride + 3] = _editTimeEntry.Text?.Trim() ?? "";

        Preferences.Set($"{_editPrefKey}{_editSlot}", string.Join("|", vals));
        editOverlay.IsVisible = false;
        BuildSets();
    }

    private async void BtnGenerate_Clicked(object sender, EventArgs e)
    {
        AppShell.GeneratePageInstance.PrePosition(true);
        await Shell.Current.GoToAsync(nameof(GeneratePage), false);
    }

    private async Task NavigateBack()
    {
        if (_isPanning) return;
        await Shell.Current.GoToAsync("..", false);
    }

    // ── Build all game sections ──────────────────────────────────────────────

    private void BuildSets()
    {
        setsContainer.Children.Clear();
        int totalSets = 0;

        foreach (var game in Games)
        {
            var sets  = LoadGameSets(game.PrefKey, game.Rows, game.Cols, game.Stride);
            var times = game.HasTime ? LoadGameTimes(game.PrefKey, game.Rows, game.Stride) : null;
            var nonEmpty = sets.Where(s => s != null).ToList();
            totalSets += nonEmpty.Count;

            setsContainer.Children.Add(MakeSectionHeader(game.Caption, game.AccentColor, nonEmpty.Count));

            if (nonEmpty.Count == 0)
            {
                setsContainer.Children.Add(new Label
                {
                    Text = "No sets saved",
                    FontSize = 13,
                    TextColor = Color.FromArgb("#9CA3AF"),
                    Margin = new Thickness(20, 4, 20, 8),
                    FontAttributes = FontAttributes.Italic
                });
                continue;
            }

            for (int slotIdx = 0; slotIdx < sets.Length; slotIdx++)
            {
                var rows = sets[slotIdx];
                if (rows == null) continue;
                var slotTimes = times?[slotIdx];
                string prefKey = game.PrefKey;
                int capturedSlot = slotIdx;
                int stride = game.Stride;
                int cols = game.Cols;

                Action onDelete = () =>
                {
                    Preferences.Remove($"{prefKey}{capturedSlot}");
                    BuildSets();
                };

                Action<int> onDeleteRow = (rowIdx) =>
                {
                    string raw = Preferences.Get($"{prefKey}{capturedSlot}", "");
                    if (string.IsNullOrEmpty(raw)) return;
                    var vals = raw.Split('|');
                    if (vals.Length < 10 * stride) Array.Resize(ref vals, 10 * stride);
                    // Clear the row
                    for (int c = 0; c < stride; c++)
                        vals[rowIdx * stride + c] = "";
                    // Check if any rows remain
                    bool anyLeft = false;
                    for (int r = 0; r < 10; r++)
                        for (int c = 0; c < cols; c++)
                            if (!string.IsNullOrWhiteSpace(vals[r * stride + c])) { anyLeft = true; break; }
                    if (anyLeft)
                        Preferences.Set($"{prefKey}{capturedSlot}", string.Join("|", vals));
                    else
                        Preferences.Remove($"{prefKey}{capturedSlot}");
                    BuildSets();
                };

                int specialCol = game.SpecialCol;
                bool hasTime   = game.HasTime;
                Action<int> onEditRow = (rowIdx) =>
                    ShowEditRow(prefKey, capturedSlot, rowIdx, stride, cols, specialCol, hasTime);

                setsContainer.Children.Add(MakeSetBlock(slotIdx + 1, rows, game.Cols, game.SpecialCol, game.SpecialLabel, game.AccentColor, slotTimes, onDelete, onDeleteRow, onEditRow));
            }
        }

        lblStatus.Text = $"{totalSets} total set{(totalSets == 1 ? "" : "s")} saved";
    }

    // Returns array[slot] of int[row][col], null if slot empty
    private static int?[][]?[] LoadGameSets(string prefKey, int rows, int cols, int stride)
    {
        var result = new int?[][]?[10];
        for (int slot = 0; slot < 10; slot++)
        {
            var raw = Preferences.Get($"{prefKey}{slot}", "");
            if (string.IsNullOrEmpty(raw)) continue;

            var vals = raw.Split('|');
            var grid = new int?[rows][];
            bool hasAny = false;

            for (int r = 0; r < rows; r++)
            {
                grid[r] = new int?[cols];
                bool rowHasData = false;
                for (int c = 0; c < cols; c++)
                {
                    int idx = r * stride + c;
                    if (idx < vals.Length && int.TryParse(vals[idx], out int n))
                    {
                        grid[r][c] = n;
                        rowHasData = true;
                        hasAny = true;
                    }
                }
                if (!rowHasData) grid[r] = null!; // mark empty row
            }

            if (hasAny) result[slot] = grid;
        }
        return result;
    }

    // Returns array[slot][row] of race time strings (for games with HasTime)
    private static string?[][]? LoadGameTimes(string prefKey, int rows, int stride)
    {
        var result = new string?[10][];
        for (int slot = 0; slot < 10; slot++)
        {
            var raw = Preferences.Get($"{prefKey}{slot}", "");
            if (string.IsNullOrEmpty(raw)) { result[slot] = null; continue; }

            var vals = raw.Split('|');
            var times = new string?[rows];
            for (int r = 0; r < rows; r++)
            {
                int idx = r * stride + 3; // time is at offset 3 (after h1,h2,h3)
                times[r] = idx < vals.Length ? vals[idx] : null;
            }
            result[slot] = times;
        }
        return result;
    }

    // ── UI builders ──────────────────────────────────────────────────────────

    private static View MakeSectionHeader(string caption, string accentColor, int count)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(4)),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            BackgroundColor = Color.FromArgb("#E8EDF3"),
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(0, 8),
        };

        var accent = new BoxView { BackgroundColor = Color.FromArgb(accentColor) };
        Grid.SetColumn(accent, 0);
        grid.Children.Add(accent);

        var lbl = new Label
        {
            Text = caption,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#1E2733"),
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(12, 0, 0, 0)
        };
        Grid.SetColumn(lbl, 1);
        grid.Children.Add(lbl);

        var badge = new Label
        {
            Text = $"{count} set{(count == 1 ? "" : "s")}",
            FontSize = 11,
            TextColor = Color.FromArgb(accentColor),
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 14, 0)
        };
        Grid.SetColumn(badge, 2);
        grid.Children.Add(badge);

        return grid;
    }

    private static View MakeSetBlock(int setNumber, int?[][]? rows, int cols, int specialCol, string specialLabel, string accentColor, string?[]? times = null, Action? onDelete = null, Action<int>? onDeleteRow = null, Action<int>? onEditRow = null)
    {
        var outer = new VerticalStackLayout
        {
            BackgroundColor = Colors.White,
            Margin = new Thickness(10, 4),
            Spacing = 0,
        };

        // Set label header with delete button
        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            Padding = new Thickness(10, 4, 6, 2),
        };

        headerGrid.Children.Add(new Label
        {
            Text = $"Set {setNumber}",
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb(accentColor),
            VerticalOptions = LayoutOptions.Center,
        });

        if (onDelete != null)
        {
            var delBtn = new Button
            {
                Text = "✕ Delete",
                FontSize = 11,
                BackgroundColor = Color.FromArgb("#7B1111"),
                TextColor = Colors.White,
                HeightRequest = 28,
                CornerRadius = 14,
                Padding = new Thickness(10, 0),
            };
            delBtn.Clicked += (_, _) => onDelete();
            Grid.SetColumn(delBtn, 1);
            headerGrid.Children.Add(delBtn);
        }

        outer.Children.Add(headerGrid);

        // Divider
        outer.Children.Add(new BoxView { BackgroundColor = Color.FromArgb("#E5E7EB"), HeightRequest = 1 });

        bool anyRow = false;
        for (int r = 0; r < rows!.Length; r++)
        {
            var rowData = rows[r];
            if (rowData == null) continue;

            // Check if row is all null/empty
            bool empty = true;
            for (int c = 0; c < cols; c++)
                if (rowData[c].HasValue && rowData[c] != 0) { empty = false; break; }
            if (empty) continue;

            anyRow = true;
            string? raceTime = times?[r];
            int capturedR = r;
            Action? delRow  = onDeleteRow != null ? () => onDeleteRow(capturedR) : null;
            Action? editRow = onEditRow   != null ? () => onEditRow(capturedR)   : null;
            outer.Children.Add(MakeNumberRow(r + 1, rowData, cols, specialCol, specialLabel, raceTime, delRow, editRow));
        }

        if (!anyRow)
        {
            outer.Children.Add(new Label
            {
                Text = "Empty",
                FontSize = 12,
                TextColor = Color.FromArgb("#9CA3AF"),
                Padding = new Thickness(10, 6),
                FontAttributes = FontAttributes.Italic
            });
        }

        return new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            StrokeThickness = 0,
            BackgroundColor = Colors.White,
            Margin = new Thickness(10, 4),
            Content = outer,
            Shadow = new Shadow { Brush = Brush.Black, Opacity = 0.07f, Radius = 4, Offset = new Point(0, 1) }
        };
    }

    private static View MakeNumberRow(int rowNum, int?[] nums, int cols, int specialCol, string specialLabel, string? raceTime = null, Action? onDeleteRow = null, Action? onEditRow = null)
    {
        int mainCols = specialCol >= 0 ? specialCol : cols;

        var grid = new Grid { Padding = new Thickness(6, 5), ColumnSpacing = 4 };

        // Row number
        grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(22)));
        // Main number bubbles
        for (int c = 0; c < mainCols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        // Separator + special col if needed
        if (specialCol >= 0)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(6)));
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        int gridCol = 0;

        var rowLbl = new Label
        {
            Text = $"{rowNum}.",
            FontSize = 11,
            TextColor = Color.FromArgb("#9CA3AF"),
            VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.End,
        };
        Grid.SetColumn(rowLbl, gridCol++);
        grid.Children.Add(rowLbl);

        for (int c = 0; c < mainCols; c++)
        {
            var bubble = MakeBubble(nums[c], "#1E2733", "#F0F4F8");
            Grid.SetColumn(bubble, gridCol++);
            grid.Children.Add(bubble);
        }

        if (specialCol >= 0)
        {
            var sep = new BoxView
            {
                BackgroundColor = Color.FromArgb("#E53935"),
                WidthRequest = 2,
                VerticalOptions = LayoutOptions.Fill,
                Margin = new Thickness(1, 4)
            };
            Grid.SetColumn(sep, gridCol++);
            grid.Children.Add(sep);

            var special = MakeBubble(nums[specialCol], "#FFFFFF", "#C62828");
            Grid.SetColumn(special, gridCol++);
            grid.Children.Add(special);
        }

        if (raceTime != null)
        {
            // Show race time as formatted string after horse bubbles
            string displayTime = raceTime;
            // Re-format if stored as raw digits e.g. "14990" → "1:49.90"
            if (raceTime.Length >= 5 && raceTime.All(char.IsDigit))
            {
                displayTime = $"{raceTime[0]}:{raceTime[1..3]}.{raceTime[3..5]}";
                if (raceTime.Length > 5) displayTime += raceTime[5..];
            }

            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(8)));  // spacer
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));    // time label

            var spacer = new BoxView { Color = Colors.Transparent };
            Grid.SetColumn(spacer, gridCol++);
            grid.Children.Add(spacer);

            var timeLbl = new Label
            {
                Text = displayTime,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#5D4037"),
                VerticalOptions = LayoutOptions.Center,
            };
            Grid.SetColumn(timeLbl, gridCol++);
            grid.Children.Add(timeLbl);
        }

        // Edit row button
        if (onEditRow != null)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(36)));
            var editBtn = new Button
            {
                Text = "✎",
                FontSize = 14,
                BackgroundColor = Color.FromArgb("#1565C0"),
                TextColor = Colors.White,
                WidthRequest = 30, HeightRequest = 28,
                CornerRadius = 14, Padding = Thickness.Zero,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
            };
            editBtn.Clicked += (_, _) => onEditRow();
            Grid.SetColumn(editBtn, gridCol++);
            grid.Children.Add(editBtn);
        }

        // Delete row button
        if (onDeleteRow != null)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(36)));
            var delBtn = new Button
            {
                Text = "✕",
                FontSize = 12,
                BackgroundColor = Color.FromArgb("#7B1111"),
                TextColor = Colors.White,
                WidthRequest = 30, HeightRequest = 28,
                CornerRadius = 14, Padding = Thickness.Zero,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
            };
            delBtn.Clicked += (_, _) => onDeleteRow();
            Grid.SetColumn(delBtn, gridCol++);
            grid.Children.Add(delBtn);
        }

        return grid;
    }

    private static View MakeBubble(int? value, string textColor, string bgColor)
    {
        string text = value.HasValue ? value.Value.ToString() : "—";
        return new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 4 },
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb(bgColor),
            HeightRequest = 32,
            Content = new Label
            {
                Text = text,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb(textColor),
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            }
        };
    }
}

