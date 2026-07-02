using System.Text.Json;
using System.Text.Json.Nodes;

namespace DailyFantasyMAUI;

public partial class MyFavoritePage : ContentPage
{
    const int Rows = 10;

    // Game config: prefKey → (specialCol, specialLabel, accentColor, maxPerMain, maxSpecial)
    static readonly Dictionary<string, (int SpecialCol, string SpecialLabel, string Accent)> GameConfig = new()
    {
        { "f5_set_", (-1, "",      "#FF8F00") },
        { "sl_set_", ( 5, "Mega",  "#E53935") },
        { "pb_set_", ( 5, "PB",    "#C62828") },
        { "mm_set_", ( 5, "MB",    "#F57F17") },
        { "d3_set_", (-1, "",      "#1565C0") },
        { "d4_set_", (-1, "",      "#00695C") },
        { "dd_set_", (-1, "",      "#5D4037") },
    };

    record FavFile(string Name, string Path);
    record FavSet(string Label, string Caption, string PrefKey, int Slot, string Data, string BetTypes);
    record SetView(FavSet Set, Entry[,] Entries, int Stride);

    List<FavFile> _files    = [];
    List<FavSet>  _sets     = [];
    List<SetView> _setViews = [];
    SetView?      _activeSetView;

    bool _suppressPicker = false;
    readonly Dictionary<string, bool> _collapsed = new();
    readonly List<(VerticalStackLayout body, Label chevron)> _setBodyRefs = [];

    public MyFavoritePage() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadFileList();
    }

    // ── File list ─────────────────────────────────────────────────────────────

    static string GetFavDir()
    {
#if ANDROID
        var ext = Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
                  ?? Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
        return Path.Combine(ext, "MyFavorite");
#else
        return Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "data", "MyFavorite");
#endif
    }

    void LoadFileList()
    {
        string favDir = GetFavDir();
        _files = Directory.Exists(favDir)
            ? Directory.GetFiles(favDir, "*.json")
                        .OrderByDescending(f => f)
                        .Select(f => new FavFile(Path.GetFileNameWithoutExtension(f), f))
                        .ToList()
            : [];

        _suppressPicker = true;
        filePicker.Items.Clear();
        foreach (var f in _files)
            filePicker.Items.Add(f.Name);
        _suppressPicker = false;

        bool hasFiles = _files.Count > 0;
        lblNoFiles.IsVisible   = !hasFiles;
        setPickerRow.IsVisible = hasFiles;
        btnRow.IsVisible       = hasFiles;
        btnDelete.IsEnabled    = hasFiles;
        rowsContainer.Children.Clear();

        if (hasFiles)
        {
            filePicker.SelectedIndex = 0;
        }
        else
        {
            lblGameInfo.Text = "";
            _activeSetView = null;
        }
    }

    void FilePicker_Changed(object sender, EventArgs e)
    {
        if (_suppressPicker) return;
        int idx = filePicker.SelectedIndex;
        if (idx < 0 || idx >= _files.Count) return;
        LoadSetsFromFile(_files[idx].Path);
    }

    void LoadSetsFromFile(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            var root = JsonNode.Parse(json)?.AsObject();
            var setsArr = root?["sets"]?.AsArray();
            if (setsArr == null) { lblGameInfo.Text = "Invalid file"; return; }

            _sets = setsArr.Select(s =>
            {
                var o = s?.AsObject();
                return new FavSet(
                    o?["label"]?.GetValue<string>()    ?? "?",
                    o?["caption"]?.GetValue<string>()  ?? "?",
                    o?["prefKey"]?.GetValue<string>()  ?? "",
                    o?["slot"]?.GetValue<int>()        ?? 0,
                    o?["data"]?.GetValue<string>()     ?? "",
                    o?["betTypes"]?.GetValue<string>() ?? "");
            }).ToList();

            setPickerRow.IsVisible = false;
            lblGameInfo.Text = $"{_sets.Count} set{(_sets.Count == 1 ? "" : "s")} in file";

            _setViews.Clear();
            _setBodyRefs.Clear();
            _activeSetView = null;
            rowsContainer.Children.Clear();

            foreach (var set in _sets)
                rowsContainer.Children.Add(BuildSetBlock(set));
        }
        catch (Exception ex)
        {
            lblGameInfo.Text = $"Error: {ex.Message}";
        }
    }

    // ── Build collapsible block for one set ───────────────────────────────────

    View BuildSetBlock(FavSet set)
    {
        var cfg = GameConfig.TryGetValue(set.PrefKey, out var cfgVal)
            ? cfgVal
            : (SpecialCol: -1, SpecialLabel: "", Accent: "#607D8B");

        var vals = set.Data.Split('|');
        int stride = vals.Length >= Rows ? vals.Length / Rows : (cfg.SpecialCol >= 0 ? cfg.SpecialCol + 1 : 3);
        var entries = new Entry[Rows, 8];

        var sv = new SetView(set, entries, stride);
        _setViews.Add(sv);
        _activeSetView ??= sv;

        bool isCollapsed = _collapsed.TryGetValue(set.Label, out bool cv) && cv;

        var chevron = new Label
        {
            Text = isCollapsed ? "▶" : "▼",
            FontSize = 11,
            TextColor = Color.FromArgb(cfg.Accent),
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        var headerLabel = new Label
        {
            Text = set.Label,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb(cfg.Accent),
            VerticalOptions = LayoutOptions.Center,
        };
        var headerGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
            },
            BackgroundColor = Color.FromArgb("#E8EDF3"),
            Padding = new Thickness(10, 7),
        };
        Grid.SetColumn(chevron, 0);
        headerGrid.Children.Add(chevron);
        Grid.SetColumn(headerLabel, 1);
        headerGrid.Children.Add(headerLabel);

        var body = new VerticalStackLayout { Spacing = 0, IsVisible = !isCollapsed };
        _setBodyRefs.Add((body, chevron));

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            body.IsVisible = !body.IsVisible;
            chevron.Text = body.IsVisible ? "▼" : "▶";
            _collapsed[set.Label] = !body.IsVisible;
            _activeSetView = sv;
            UpdateActiveHeader();
        };
        headerGrid.GestureRecognizers.Add(tap);

        for (int r = 0; r < Rows; r++)
        {
            var row = new Grid
            {
                ColumnSpacing = 4,
                BackgroundColor = Colors.White,
                Margin = new Thickness(0, 1),
                Padding = new Thickness(4, 2),
            };
            row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            for (int col = 0; col < stride; col++)
                row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            int rowIdx = r;
            var rowNum = new Label
            {
                Text = $"{r + 1,2}.",
                FontSize = 11,
                TextColor = Color.FromArgb(cfg.Accent),
                VerticalOptions = LayoutOptions.Center,
                WidthRequest = 22,
            };
            rowNum.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => ClearRow(sv, rowIdx))
            });
            Grid.SetColumn(rowNum, 0);
            row.Children.Add(rowNum);

            for (int col = 0; col < stride; col++)
            {
                bool isSpecial = cfg.SpecialCol >= 0 && col == cfg.SpecialCol;
                var entry = new Entry
                {
                    Keyboard = Keyboard.Numeric,
                    FontSize = 18,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = isSpecial ? Colors.White : Colors.Black,
                    BackgroundColor = Color.FromArgb(isSpecial ? cfg.Accent : "#F5F5F5"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    HeightRequest = 44,
                    MaxLength = 2,
                };
                entry.HandlerChanged += ForceBlackText;
                int vi = r * stride + col;
                entry.Text = vi < vals.Length ? vals[vi] : "";
                entries[r, col] = entry;
                Grid.SetColumn(entry, col + 1);
                row.Children.Add(entry);
            }

            body.Children.Add(row);
        }

        var outer = new VerticalStackLayout { Spacing = 0, Margin = new Thickness(0, 4, 0, 0) };
        outer.Children.Add(headerGrid);
        outer.Children.Add(body);
        return outer;
    }

    void SetPicker_Changed(object sender, EventArgs e) { /* replaced by collapsible blocks */ }

    // Track which set block header is "active" (last tapped) via bold label
    void UpdateActiveHeader() { /* visual highlight handled by accent color already */ }

    void ClearRow(SetView sv, int r)
    {
        for (int c = 0; c < sv.Stride; c++)
            if (sv.Entries[r, c] != null) sv.Entries[r, c].Text = "";
    }

    private void ForceBlackText(object? sender, EventArgs e)
    {
#if ANDROID
        if (sender is Entry entry &&
            entry.Handler?.PlatformView is Android.Widget.EditText et)
        {
            et.SetTextColor(Android.Graphics.Color.Black);
            et.SetSelectAllOnFocus(true);
        }
#endif
    }

    // ── Get entries as pipe-delimited string for a SetView ───────────────────

    static string GetSetData(SetView sv)
    {
        var vals = new string[Rows * sv.Stride];
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < sv.Stride; c++)
                vals[r * sv.Stride + c] = sv.Entries[r, c]?.Text ?? "";
        return string.Join("|", vals);
    }

    // ── Save file ─────────────────────────────────────────────────────────────

    private async void BtnSaveFile_Clicked(object sender, EventArgs e)
    {
        if (_setViews.Count == 0 || filePicker.SelectedIndex < 0) return;

        try
        {
            string filePath = _files[filePicker.SelectedIndex].Path;
            string existing = await File.ReadAllTextAsync(filePath);
            var root = JsonNode.Parse(existing)?.AsObject();
            if (root == null) return;

            var setsArr = root["sets"]?.AsArray();
            if (setsArr == null) return;

            // Update all sets in the JSON from their entry grids
            foreach (var sv in _setViews)
            {
                string newData = GetSetData(sv);
                for (int i = 0; i < setsArr.Count; i++)
                {
                    var node = setsArr[i]?.AsObject();
                    if (node?["label"]?.GetValue<string>() == sv.Set.Label)
                    {
                        node["data"] = newData;
                        break;
                    }
                }
            }

            await File.WriteAllTextAsync(filePath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            if (sender is Button btn)
            {
                var orig = btn.Text; var origColor = btn.BackgroundColor;
                btn.Text = "SAVED ✓"; btn.BackgroundColor = Color.FromArgb("#1B5E20");
                await Task.Delay(1200);
                btn.Text = orig; btn.BackgroundColor = origColor;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Save Error", ex.Message, "OK");
        }
    }

    // ── Load to game ──────────────────────────────────────────────────────────

    private async void BtnLoadToGame_Clicked(object sender, EventArgs e)
    {
        if (_setViews.Count == 0) return;

        // If multiple sets, ask which one to load
        SetView? sv = _activeSetView ?? _setViews[0];
        if (_setViews.Count > 1)
        {
            string? choice = await DisplayActionSheet("Load which set?", "Cancel", null,
                _setViews.Select(x => x.Set.Label).ToArray());
            if (choice == null || choice == "Cancel") return;
            sv = _setViews.FirstOrDefault(x => x.Set.Label == choice) ?? sv;
        }

        string newData = GetSetData(sv);
        var slotOptions = Enumerable.Range(0, 10).Select(i => $"Set {i + 1}").ToArray();
        string? destLabel = await DisplayActionSheet(
            $"Load into which slot? ({sv.Set.Caption})", "Cancel", null, slotOptions);
        if (destLabel == null || destLabel == "Cancel") return;

        int destSlot = int.Parse(destLabel.Replace("Set ", "")) - 1;
        Preferences.Set($"{sv.Set.PrefKey}{destSlot}", newData);
        if (!string.IsNullOrEmpty(sv.Set.BetTypes))
        {
            string bkey = sv.Set.PrefKey.Replace("set_", "btypes_");
            Preferences.Set($"{bkey}{destSlot}", sv.Set.BetTypes);
        }

        await DisplayAlert("Loaded",
            $"Loaded into {sv.Set.Caption} — Set {destSlot + 1}.\nOpen that game to see the numbers.", "OK");
    }

    // ── Clear rows ────────────────────────────────────────────────────────────

    private void BtnClearRows_Clicked(object sender, EventArgs e)
    {
        var sv = _activeSetView;
        if (sv == null) return;
        for (int r = 0; r < Rows; r++)
            ClearRow(sv, r);
    }

    // ── Delete file ───────────────────────────────────────────────────────────

    private async void BtnDelete_Clicked(object sender, EventArgs e)
    {
        int idx = filePicker.SelectedIndex;
        if (idx < 0 || idx >= _files.Count) return;

        bool confirm = await DisplayAlert("Delete", $"Delete {_files[idx].Name}?", "Delete", "Cancel");
        if (!confirm) return;

        File.Delete(_files[idx].Path);
        LoadFileList();
    }

    // ── New: import via file picker ───────────────────────────────────────────

    private async void BtnNew_Clicked(object sender, EventArgs e)
    {
        var result = await FilePicker.PickAsync(new PickOptions
        {
            PickerTitle = "Select a MyFavorite .json file",
            FileTypes   = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "application/json", "*/*" } },
                { DevicePlatform.iOS,     new[] { "public.json" } },
                { DevicePlatform.WinUI,   new[] { ".json" } },
            })
        });
        if (result == null) return;

        // Copy into our fav dir
        string favDir = GetFavDir();
        Directory.CreateDirectory(favDir);
        string dest = Path.Combine(favDir, Path.GetFileName(result.FullPath));
        File.Copy(result.FullPath, dest, overwrite: true);
        LoadFileList();
    }

    // ── Collapse / Expand all ─────────────────────────────────────────────────

    private void BtnCollapseAll_Clicked(object sender, EventArgs e)
    {
        bool anyExpanded = _setBodyRefs.Any(x => x.body.IsVisible);
        foreach (var (body, chevron) in _setBodyRefs)
        {
            body.IsVisible = !anyExpanded;
            chevron.Text = body.IsVisible ? "▼" : "▶";
        }
        foreach (var sv in _setViews)
            _collapsed[sv.Set.Label] = anyExpanded;
        if (sender is Button btn)
            btn.Text = anyExpanded ? "⊞" : "⊟";
    }

    // ── Back ──────────────────────────────────────────────────────────────────

    private async void BtnBack_Clicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..", false);

    // ── Static helper: save from any game page ────────────────────────────────

    public static async Task SaveCurrentToMyFavoriteAsync(
        string caption, string prefKey, int slot, string data, string betTypes = "")
    {
        var setNode = new JsonObject
        {
            ["label"]    = $"{caption} — Set {slot + 1}",
            ["caption"]  = caption,
            ["prefKey"]  = prefKey,
            ["slot"]     = slot,
            ["data"]     = data,
            ["betTypes"] = betTypes,
        };
        var root = new JsonObject
        {
            ["saved"] = DateTime.Now.ToString("o"),
            ["sets"]  = new JsonArray(setNode),
        };
        string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        string favDir = GetFavDir();
        Directory.CreateDirectory(favDir);
        string stamp   = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string favName = $"{caption.Replace(" ", "_")}_Set{slot + 1}_{stamp}.json";
        string favPath = Path.Combine(favDir, favName);
        await File.WriteAllTextAsync(favPath, json);

        await Share.RequestAsync(new ShareFileRequest
        {
            Title = $"Save {caption} Set {slot + 1} to MyFavorite",
            File  = new ShareFile(favPath, "application/json")
        });
    }
}
