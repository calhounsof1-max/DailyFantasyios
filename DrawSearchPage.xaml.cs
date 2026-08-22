using System.Text.Json;
using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

public partial class DrawSearchPage : ContentPage
{
    // ── Game config ───────────────────────────────────────────────

    enum InputMode { Digits3, Digits4, Numbers5, Numbers5Plus1, Numbers3Ordered }

    record GameConfig(
        string    Name,
        int       DefaultId,
        string    PrefKey,
        InputMode Mode,
        string    Placeholder,
        Color     BallColor
    );

    static readonly GameConfig[] _games =
    [
        new("Daily 3",       9,  "",                InputMode.Digits3,        "520",              Color.FromArgb("#1565C0")),
        new("Daily 4",       14, "",                InputMode.Digits4,        "1234",             Color.FromArgb("#0D47A1")),
        new("Fantasy 5",     10, "fantasy5_game_id", InputMode.Numbers5,      "3 7 15 23 39",     Color.FromArgb("#1B5E20")),
        new("Super Lotto",   8,  "sl_game_id",      InputMode.Numbers5Plus1,  "3 7 15 23 39 2",   Color.FromArgb("#BF360C")),
        new("Powerball",     12, "",                InputMode.Numbers5Plus1,  "3 7 15 23 39 10",  Color.FromArgb("#B71C1C")),
        new("Mega Millions", 4,  "mm_game_id",      InputMode.Numbers5Plus1,  "3 7 15 23 39 11",  Color.FromArgb("#E65100")),
        new("Daily Derby",   11, "",                InputMode.Numbers3Ordered,"1 5 12",           Color.FromArgb("#4A148C")),
    ];

    int  _selGame        = 0;
    bool _numbersVisible = true;

    public static string PresetGame { get; set; } = "Daily 3";

    // ── ctor ──────────────────────────────────────────────────────

    public DrawSearchPage()
    {
        InitializeComponent();
        foreach (var g in _games)
            gamePicker.Items.Add(g.Name);
        gamePicker.SelectedIndex = 0;
        UpdateGameLogo();
        AddNumberRow(scroll: false);
        AddNumberRow(scroll: false);
        AddNumberRow(scroll: false);
        AddNumberRow(scroll: false);
        AddNumberRow(scroll: false);
        AddNumberRow(scroll: false);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        int idx = Array.FindIndex(_games, g => g.Name == PresetGame);
        if (idx >= 0)
        {
            _selGame = idx;
            gamePicker.SelectedIndex = idx;
        }
        UpdateGameUI();
        UpdateGameLogo();
        ApplyNextDraw();

        // Restore numbers section collapse state
        _numbersVisible = Preferences.Get("ds_numbers_visible", true);
        numbersScrollView.IsVisible = _numbersVisible;
        lblChevron.Text = _numbersVisible ? "▼" : "▶";
        lblNumsHeader.Text = _numbersVisible
            ? "Numbers to Check (tap to hide)"
            : "Numbers to Check (tap to show)";
    }

    // ── Collapse / expand numbers ─────────────────────────────────

    private void NumbersHeader_Tapped(object? sender, TappedEventArgs e)
    {
        _numbersVisible = !_numbersVisible;
        Preferences.Set("ds_numbers_visible", _numbersVisible);
        numbersScrollView.IsVisible = _numbersVisible;
        lblChevron.Text = _numbersVisible ? "▼" : "▶";
        lblNumsHeader.Text = _numbersVisible
            ? "Numbers to Check (tap to hide)"
            : "Numbers to Check (tap to show)";
    }

    // ── Add / clear number rows ───────────────────────────────────

    private void BtnAddRow_Clicked(object? sender, EventArgs e) => AddNumberRow(scroll: true);

    private void BtnClearRows_Clicked(object? sender, EventArgs e)
    {
        int rowCount = Math.Max(numbersContainer.Children.Count, 6);
        numbersContainer.Children.Clear();
        for (int i = 0; i < rowCount; i++)
            AddNumberRow(scroll: false);
        resultsContainer.Children.Clear();
        lblSummary.Text = "";
        lblStatus.Text  = "Enter Draw# range, add your numbers, then tap Search.";
    }

    void AddNumberRow(bool scroll = true)
    {
        int num = numbersContainer.Children.Count + 1;
        var game = _games[_selGame];

        bool isDigitGame = game.Mode is InputMode.Digits3 or InputMode.Digits4;
        int mainCount = game.Mode switch
        {
            InputMode.Digits3         => 3,
            InputMode.Digits4         => 4,
            InputMode.Numbers5        => 5,
            InputMode.Numbers5Plus1   => 5,
            InputMode.Numbers3Ordered => 3,
            _                         => 3
        };
        bool hasSpecial = game.Mode == InputMode.Numbers5Plus1;
        int totalBoxes  = mainCount + (hasSpecial ? 1 : 0);
        int maxLen      = isDigitGame ? 1 : 2;

        var numLabel = new Label
        {
            Text              = $"#{num}",
            FontSize          = 12,
            FontAttributes    = FontAttributes.Bold,
            TextColor         = Color.FromArgb("#6B7280"),
            VerticalOptions   = LayoutOptions.Center,
            WidthRequest      = 36,
            LineBreakMode     = LineBreakMode.NoWrap,
            Margin            = new Thickness(0, 0, 4, 0)
        };

        var boxRow  = new HorizontalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        var entries = new List<Entry>();

        // rowGrid declared early so TextChanged closures can reference it for next-row wrap
        Grid? rowGrid = null;

        for (int i = 0; i < totalBoxes; i++)
        {
            bool isSpecial = hasSpecial && i == mainCount;

            // "+" separator before special ball
            if (isSpecial)
                boxRow.Children.Add(new Label
                {
                    Text            = "+",
                    FontSize        = 14,
                    FontAttributes  = FontAttributes.Bold,
                    TextColor       = Color.FromArgb("#9CA3AF"),
                    VerticalOptions = LayoutOptions.Center,
                    Margin          = new Thickness(2, 0)
                });

            var entry = new Entry
            {
                Keyboard               = Keyboard.Numeric,
                WidthRequest           = isDigitGame ? 42 : hasSpecial ? 40 : 48,
                HeightRequest          = 44,
                HorizontalTextAlignment= TextAlignment.Center,
                BackgroundColor        = isSpecial
                                             ? Color.FromArgb("#FFF3E0")
                                             : Color.FromArgb("#F0F6FF"),
                TextColor              = isSpecial
                                             ? Color.FromArgb("#E65100")
                                             : Color.FromArgb("#1565C0"),
                FontSize               = 16,
                FontAttributes         = FontAttributes.Bold,
            };

            int capturedIdx = i; // capture for closure
            entry.TextChanged += (s, e) =>
            {
                var en  = (Entry)s!;
                string txt = e.NewTextValue ?? "";

                // Enforce max length without MaxLength property
                if (txt.Length > maxLen)
                {
                    en.Text = txt[..maxLen];
                    return; // TextChanged fires again with trimmed text
                }

                if (txt.Length == maxLen)
                {
                    if (capturedIdx < entries.Count - 1)
                    {
                        // Advance within this row
                        entries[capturedIdx + 1].Focus();
                    }
                    else if (rowGrid != null)
                    {
                        // Last column — wrap to first entry of next row
                        int rowIdx = numbersContainer.Children.IndexOf(rowGrid);
                        if (rowIdx >= 0 && rowIdx + 1 < numbersContainer.Children.Count &&
                            numbersContainer.Children[rowIdx + 1] is Grid nextGrid)
                        {
                            foreach (var c in nextGrid.Children)
                                if (c is HorizontalStackLayout hsl)
                                {
                                    hsl.Children.OfType<Entry>().FirstOrDefault()?.Focus();
                                    break;
                                }
                        }
                    }
                }
            };

            entries.Add(entry);
            boxRow.Children.Add(entry);
        }

        var removeVisual = new Border
        {
            BackgroundColor   = Color.FromArgb("#E5E7EB"),
            StrokeThickness   = 0,
            StrokeShape       = new RoundRectangle { CornerRadius = 4 },
            WidthRequest      = 30,
            HeightRequest     = 30,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions   = LayoutOptions.Center,
            Padding           = new Thickness(0),
            Content           = new Label
            {
                Text                    = "×",
                FontSize                = 17,
                FontAttributes          = FontAttributes.Bold,
                TextColor               = Color.FromArgb("#6B7280"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment   = TextAlignment.Center,
            }
        };
        var removeBtn = new Grid
        {
            WidthRequest    = 44,
            HeightRequest   = 44,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Colors.Transparent,
        };
        removeBtn.Children.Add(removeVisual);

        rowGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Padding         = new Thickness(4, 3),
            BackgroundColor = Colors.White
        };
        rowGrid.Add(numLabel,  0, 0);
        rowGrid.Add(boxRow,    1, 0);
        rowGrid.Add(removeBtn, 2, 0);

        var removeTap = new TapGestureRecognizer();
        removeTap.Tapped += (s, e) =>
        {
            numbersContainer.Children.Remove(rowGrid);
            RenumberRows();
        };
        removeBtn.GestureRecognizers.Add(removeTap);

        numbersContainer.Children.Add(rowGrid);

        if (scroll)
            MainThread.BeginInvokeOnMainThread(async () =>
                await numbersScrollView.ScrollToAsync(0, numbersScrollView.ContentSize.Height, false));
    }

    void RenumberRows()
    {
        int i = 1;
        foreach (var child in numbersContainer.Children)
            if (child is Grid g && g.Children.FirstOrDefault() is Label lbl)
                lbl.Text = $"#{i++}";
    }

    // ── Events ────────────────────────────────────────────────────

    private void GamePicker_Changed(object? sender, EventArgs e)
    {
        int i = gamePicker.SelectedIndex;
        if (i < 0) return;
        _selGame = i;
        UpdateGameUI();
        ApplyNextDraw();
        resultsContainer.Children.Clear();
        lblSummary.Text = "";
        UpdateGameLogo();
    }

    void ApplyNextDraw()
    {
        int n = Services.DrawNumberService.GetNextDraw(_games[_selGame].Name);
        if (n <= 0) return;
        string s = n.ToString();
        entryDrawFrom.Text = s;
        entryDrawTo.Text   = s;
    }

    private async void BtnGoHome_Clicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..", false);

    private async void BtnSearch_Clicked(object? sender, EventArgs e)
    {
        if (!int.TryParse(entryDrawFrom.Text?.Trim(), out int fromDraw))
        {
            lblStatus.Text = "Enter a Draw# in the From field.";
            return;
        }
        int toDraw = string.IsNullOrWhiteSpace(entryDrawTo.Text)
            ? fromDraw
            : int.TryParse(entryDrawTo.Text.Trim(), out int td) ? td : fromDraw;
        if (fromDraw > toDraw) (fromDraw, toDraw) = (toDraw, fromDraw);

        var game = _games[_selGame];

        // Collect all non-empty number rows from individual boxes
        var searchList = new List<(int[] Main, int Special, string Raw)>();
        foreach (var child in numbersContainer.Children)
        {
            if (child is not Grid g) continue;

            // Column 1 is the HorizontalStackLayout of boxes
            HorizontalStackLayout? boxRow = null;
            foreach (var c in g.Children)
                if (c is HorizontalStackLayout hsl) { boxRow = hsl; break; }
            if (boxRow == null) continue;

            var vals = boxRow.Children.OfType<Entry>()
                             .Select(en => en.Text?.Trim() ?? "")
                             .ToList();

            if (vals.All(string.IsNullOrWhiteSpace)) continue;

            // Build combined string that ParseInput expects
            string combined = game.Mode is InputMode.Digits3 or InputMode.Digits4
                ? string.Join("", vals)
                : string.Join(" ", vals);

            var (main, special, err) = ParseInput(game, combined);
            if (err != null) { lblStatus.Text = $"Row #{searchList.Count + 1}: {err}"; return; }
            searchList.Add((main, special, combined));
        }

        if (searchList.Count == 0)
        {
            lblStatus.Text = "Enter at least one number to search.";
            return;
        }

        // Keep the raw entry values so we can re-parse for other games if needed
        var rawEntries = new List<string>();
        foreach (var child in numbersContainer.Children)
        {
            if (child is not Grid g) continue;
            HorizontalStackLayout? boxRow = null;
            foreach (var c in g.Children)
                if (c is HorizontalStackLayout hsl) { boxRow = hsl; break; }
            if (boxRow == null) continue;
            var vals = boxRow.Children.OfType<Entry>().Select(en => en.Text?.Trim() ?? "").ToList();
            if (vals.All(string.IsNullOrWhiteSpace)) continue;
            rawEntries.Add(game.Mode is InputMode.Digits3 or InputMode.Digits4
                ? string.Join("", vals)
                : string.Join(" ", vals));
        }

        btnSearch.IsEnabled = false;
        resultsContainer.Children.Clear();
        lblSummary.Text = "";
        loadingOverlay.IsVisible = true;
        SetLoadingMsg($"Fetching draws #{fromDraw}–#{toDraw}...");

        try
        {
            int gameId = string.IsNullOrEmpty(game.PrefKey)
                ? game.DefaultId
                : Preferences.Get(game.PrefKey, game.DefaultId);

            var draws = await FetchDrawsInRange(gameId, game, fromDraw, toDraw);

            // If no draws found, try all other games automatically
            if (draws.Count == 0)
            {
                for (int gi = 0; gi < _games.Length; gi++)
                {
                    if (gi == _selGame) continue;
                    var g2 = _games[gi];
                    SetLoadingMsg($"Trying {g2.Name}...");
                    int gid2 = string.IsNullOrEmpty(g2.PrefKey)
                        ? g2.DefaultId
                        : Preferences.Get(g2.PrefKey, g2.DefaultId);
                    try
                    {
                        var d2 = await FetchDrawsInRange(gid2, g2, fromDraw, toDraw);
                        if (d2.Count == 0) continue;

                        // Try to re-parse entered numbers for this game's format
                        var sl2 = new List<(int[] Main, int Special, string Raw)>();
                        foreach (var raw in rawEntries)
                        {
                            // Reformat: split digits/spaces to match new game's box count
                            string adapted = raw;
                            if (g2.Mode is InputMode.Digits3 or InputMode.Digits4)
                                adapted = raw.Replace(" ", "");
                            else
                                adapted = string.Join(" ", raw.Replace(" ", "").ToCharArray().Select(c => c.ToString()))
                                         .Trim();
                            // Use original raw if it already has spaces (number game input)
                            if (raw.Contains(' ')) adapted = raw;

                            var (m2, s2, err2) = ParseInput(g2, adapted);
                            if (err2 == null) sl2.Add((m2, s2, adapted));
                        }

                        if (sl2.Count > 0)
                        {
                            BuildResults(d2, g2, sl2, fromDraw, toDraw);
                            return;
                        }
                        else
                        {
                            // Show draws even if numbers don't match this game's format
                            BuildResults(d2, g2, searchList, fromDraw, toDraw);
                            return;
                        }
                    }
                    catch { /* skip */ }
                }
                // Nothing found in any game
                BuildResults(draws, game, searchList, fromDraw, toDraw);
            }
            else
            {
                BuildResults(draws, game, searchList, fromDraw, toDraw);
            }
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            btnSearch.IsEnabled      = true;
            loadingOverlay.IsVisible = false;
        }
    }

    private async void BtnLookup_Clicked(object? sender, EventArgs e)
    {
        if (!int.TryParse(entryDrawFrom.Text?.Trim(), out int fromDraw))
        {
            lblStatus.Text = "Enter a Draw# in the From field.";
            return;
        }
        int toDraw = string.IsNullOrWhiteSpace(entryDrawTo.Text)
            ? fromDraw
            : int.TryParse(entryDrawTo.Text.Trim(), out int td) ? td : fromDraw;
        if (fromDraw > toDraw) (fromDraw, toDraw) = (toDraw, fromDraw);

        var game = _games[_selGame];

        btnLookup.IsEnabled = false;
        resultsContainer.Children.Clear();
        lblSummary.Text = "";
        loadingOverlay.IsVisible = true;
        SetLoadingMsg($"Fetching draws #{fromDraw}–#{toDraw}...");

        try
        {
            // Try selected game first
            int gameId = string.IsNullOrEmpty(game.PrefKey)
                ? game.DefaultId
                : Preferences.Get(game.PrefKey, game.DefaultId);

            var draws = await FetchDrawsInRange(gameId, game, fromDraw, toDraw);

            // If nothing found, try all other games automatically
            if (draws.Count == 0)
            {
                var allResults = new List<(List<DrawRow> Draws, GameConfig Game)>();
                for (int gi = 0; gi < _games.Length; gi++)
                {
                    if (gi == _selGame) continue;
                    var g2 = _games[gi];
                    SetLoadingMsg($"Trying {g2.Name}...");
                    int gid2 = string.IsNullOrEmpty(g2.PrefKey)
                        ? g2.DefaultId
                        : Preferences.Get(g2.PrefKey, g2.DefaultId);
                    try
                    {
                        var d2 = await FetchDrawsInRange(gid2, g2, fromDraw, toDraw);
                        if (d2.Count > 0) allResults.Add((d2, g2));
                    }
                    catch { /* skip games that error */ }
                }

                if (allResults.Count > 0)
                    BuildLookupResults(allResults, fromDraw, toDraw);
                else
                    BuildLookupResults(new List<(List<DrawRow>, GameConfig)> { (draws, game) }, fromDraw, toDraw);
            }
            else
            {
                BuildLookupResults(new List<(List<DrawRow>, GameConfig)> { (draws, game) }, fromDraw, toDraw);
            }
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            btnLookup.IsEnabled      = true;
            loadingOverlay.IsVisible = false;
        }
    }

    void BuildLookupResults(List<(List<DrawRow> Draws, GameConfig Game)> allResults, int fromDraw, int toDraw)
    {
        resultsContainer.Children.Clear();
        string rangeStr = fromDraw != toDraw ? $"#{fromDraw}–#{toDraw}" : $"#{fromDraw}";

        // No results found anywhere
        if (allResults.Count == 0 || allResults.All(r => r.Draws.Count == 0))
        {
            resultsContainer.Children.Add(new Label
            {
                Text = $"No draws found for {rangeStr}.\nThis range may be too old for the CA Lottery API.",
                TextColor = Color.FromArgb("#6B7280"), FontSize = 14,
                HorizontalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(20)
            });
            lblSummary.Text = "No draws found.";
            lblStatus.Text  = $"Draw {rangeStr} not found.";
            return;
        }

        int total = allResults.Sum(r => r.Draws.Count);
        string gameNames = string.Join(", ", allResults.Where(r => r.Draws.Count > 0).Select(r => r.Game.Name));
        lblSummary.Text = $"{total} draw(s) found  •  {gameNames}";
        lblStatus.Text  = $"{rangeStr}  |  {gameNames}";

        foreach (var (draws, game) in allResults)
        {
            if (draws.Count == 0) continue;

            // Game label if multiple games found
            if (allResults.Count(r => r.Draws.Count > 0) > 1)
                resultsContainer.Children.Add(new Label
                {
                    Text = game.Name,
                    FontSize = 12, FontAttributes = FontAttributes.Bold,
                    TextColor = game.BallColor,
                    Margin = new Thickness(4, 6, 4, 2)
                });

            foreach (var d in draws)
            {
                string dateStr = d.DateRaw;
                if (DateTime.TryParse(d.DateRaw, out var dt)) dateStr = dt.ToString("MMM d, yyyy");
                string timeStr = string.IsNullOrEmpty(d.DrawTime) ? "" : $"  {d.DrawTime}";

                var ballRow = new HorizontalStackLayout { Spacing = 6 };
                foreach (int n in d.Main)
                    ballRow.Children.Add(Ball(n.ToString(), game.BallColor, 36));
                if (game.Mode == InputMode.Numbers5Plus1 && d.Special > 0)
                {
                    ballRow.Children.Add(new Label
                    {
                        Text = "+", FontSize = 14, TextColor = Color.FromArgb("#9E9E9E"),
                        VerticalOptions = LayoutOptions.Center, Margin = new Thickness(2, 0)
                    });
                    ballRow.Children.Add(Ball(d.Special.ToString(), Color.FromArgb("#880E4F"), 36));
                }

                var content = new VerticalStackLayout { Spacing = 6, Padding = new Thickness(14, 12) };
                content.Children.Add(new Label
                {
                    Text = $"#{d.DrawNumber}  {dateStr}{timeStr}  [{game.Name}]",
                    FontSize = 13, FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#374151")
                });
                content.Children.Add(ballRow);

                resultsContainer.Children.Add(new Border
                {
                    BackgroundColor = Colors.White,
                    StrokeThickness = 0,
                    StrokeShape     = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                    Content         = content
                });
            }
        }
    }

    // ── UI helpers ────────────────────────────────────────────────

    void FocusFirstBox()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(300);
            if (numbersContainer.Children.FirstOrDefault() is Grid g)
                foreach (var c in g.Children)
                    if (c is HorizontalStackLayout hsl)
                    {
                        hsl.Children.OfType<Entry>().FirstOrDefault()?.Focus();
                        break;
                    }
        });
    }

    void UpdateGameUI()
    {
        // Rebuild rows since box count differs per game
        int rowCount = Math.Max(numbersContainer.Children.Count, 6);
        numbersContainer.Children.Clear();
        for (int i = 0; i < rowCount; i++)
            AddNumberRow(scroll: false);

        FocusFirstBox();

        var g = _games[_selGame];
        string hint = g.Mode switch
        {
            InputMode.Digits3         => "Daily 3 — enter each digit in its own box.",
            InputMode.Digits4         => "Daily 4 — enter each digit in its own box.",
            InputMode.Numbers5        => "Fantasy 5 — enter each number in its own box.",
            InputMode.Numbers5Plus1   => "Enter 5 main numbers + 1 special ball.",
            InputMode.Numbers3Ordered => "Daily Derby — enter 3 horse numbers in order.",
            _                         => ""
        };
        lblStatus.Text  = hint;
        resultsContainer.Children.Clear();
        lblSummary.Text = "";
    }

    void SetLoadingMsg(string msg) =>
        MainThread.BeginInvokeOnMainThread(() => lblLoadingMsg.Text = msg);

    // ── Input parsing ─────────────────────────────────────────────

    static (int[] Main, int Special, string? Error) ParseInput(GameConfig g, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ([], 0, "Enter a number.");

        if (g.Mode is InputMode.Digits3 or InputMode.Digits4)
        {
            int want = g.Mode == InputMode.Digits3 ? 3 : 4;
            string s = text.Replace(" ", "");
            if (s.Length != want || !s.All(char.IsDigit))
                return ([], 0, $"Enter exactly {want} digits.");
            return (s.Select(c => c - '0').ToArray(), 0, null);
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nums  = new List<int>();
        foreach (var p in parts)
        {
            if (!int.TryParse(p, out int n))
                return ([], 0, $"'{p}' is not a valid number.");
            nums.Add(n);
        }

        switch (g.Mode)
        {
            case InputMode.Numbers5:
                if (nums.Count != 5) return ([], 0, "Fill all 5 number boxes.");
                return (nums.ToArray(), 0, null);
            case InputMode.Numbers5Plus1:
                if (nums.Count != 6) return ([], 0, "Fill all 5 boxes + special ball.");
                return ([.. nums.Take(5)], nums[5], null);
            case InputMode.Numbers3Ordered:
                if (nums.Count != 3) return ([], 0, "Fill all 3 horse number boxes.");
                return (nums.ToArray(), 0, null);
            default:
                return ([], 0, "Unknown mode.");
        }
    }

    // ── API fetch ─────────────────────────────────────────────────

    record DrawRow(int DrawNumber, string DateRaw, string DrawTime, int[] Main, int Special);

    async Task<List<DrawRow>> FetchDrawsInRange(int gameId, GameConfig game, int fromDraw, int toDraw)
    {
        int mainCount = game.Mode switch
        {
            InputMode.Digits3         => 3,
            InputMode.Digits4         => 4,
            InputMode.Numbers5        => 5,
            InputMode.Numbers5Plus1   => 5,
            InputMode.Numbers3Ordered => 3,
            _                         => 3
        };
        bool hasSpecial = game.Mode == InputMode.Numbers5Plus1;

        var collected = new List<DrawRow>();
        int page      = 1;
        const int pageSize = 50;

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Add("Accept", "application/json, */*");
        client.DefaultRequestHeaders.Add("Referer", "https://www.calottery.com/");

        while (true)
        {
            SetLoadingMsg($"Fetching page {page}...");
            string url  = "https://www.calottery.com/api/DrawGameApi/" +
                          $"DrawGamePastDrawResults/{gameId}/{page}/{pageSize}";
            string json = await client.GetStringAsync(url).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("PreviousDraws", out var drawsEl) ||
                drawsEl.GetArrayLength() == 0)
                break;

            bool pastRange = false;
            foreach (var d in drawsEl.EnumerateArray())
            {
                int drawNum = ReadInt(d, "DrawNumber");
                if (drawNum < fromDraw) { pastRange = true; break; }
                if (drawNum > toDraw)   continue;

                string dateRaw = d.TryGetProperty("DrawDate", out var de)
                    ? de.GetString() ?? "" : "";

                var (main, special) = ReadWinningNumbers(d, mainCount, hasSpecial);
                if (main.Length == mainCount)
                    collected.Add(new DrawRow(drawNum, dateRaw, "", main, special));
            }

            if (pastRange || collected.Count >= toDraw - fromDraw + 1) break;
            page++;
            await Task.Delay(400).ConfigureAwait(false);
        }

        var byDate = collected
            .GroupBy(r => DateKey(r.DateRaw))
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.DrawNumber).ToList());

        return [.. collected
            .OrderBy(r => r.DrawNumber)
            .Select(r =>
            {
                string dk   = DateKey(r.DateRaw);
                string time = "";
                if (byDate.TryGetValue(dk, out var dayList) && dayList.Count >= 2)
                    time = dayList[0].DrawNumber == r.DrawNumber ? "Midday" : "Evening";
                return r with { DrawTime = time };
            })];
    }

    static string DateKey(string raw) => raw.Length >= 10 ? raw[..10] : raw;

    static (int[] Main, int Special) ReadWinningNumbers(JsonElement draw, int mainCount, bool hasSpecial)
    {
        if (!draw.TryGetProperty("WinningNumbers", out var wn) ||
            wn.ValueKind != JsonValueKind.Object)
            return ([], 0);

        bool zeroBased = wn.TryGetProperty("0", out _);
        int  start     = zeroBased ? 0 : 1;
        int  total     = hasSpecial ? mainCount + 1 : mainCount;

        var main    = new List<int>();
        int special = 0;

        for (int i = start; i < start + total; i++)
        {
            if (!wn.TryGetProperty(i.ToString(), out var el)) break;
            int  n         = 0;
            bool isSpecial = false;
            if (el.ValueKind == JsonValueKind.Object)
            {
                if (!el.TryGetProperty("Number", out var np)) continue;
                if (!int.TryParse(np.GetRawText().Trim('"'), out n)) continue;
                if (el.TryGetProperty("IsSpecial", out var sp)) isSpecial = sp.GetBoolean();
            }
            else if (el.ValueKind == JsonValueKind.Number)  n = el.GetInt32();
            else if (el.ValueKind == JsonValueKind.String)
            { if (!int.TryParse(el.GetString(), out n)) continue; }
            else continue;

            if (isSpecial) special = n;
            else           main.Add(n);
        }

        if (hasSpecial && special == 0 && main.Count == mainCount + 1)
        {
            special = main[mainCount];
            main.RemoveAt(mainCount);
        }

        return (main.ToArray(), special);
    }

    static int ReadInt(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number ? v.GetInt32()
             : int.TryParse(v.GetRawText().Trim('"'), out int n) ? n : 0;
    }

    // ── Results UI ────────────────────────────────────────────────

    void BuildResults(List<DrawRow> draws, GameConfig game,
                      List<(int[] Main, int Special, string Raw)> searchList,
                      int fromDraw, int toDraw)
    {
        resultsContainer.Children.Clear();

        if (draws.Count == 0)
        {
            resultsContainer.Children.Add(new Label
            {
                Text = $"No draws found in range #{fromDraw}–#{toDraw}.\n" +
                        "This range may be too old for the CA Lottery API.",
                TextColor = Color.FromArgb("#6B7280"), FontSize = 14,
                HorizontalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(20)
            });
            lblSummary.Text = "No draws found.";
            lblStatus.Text  = $"Range #{fromDraw}–#{toDraw} not found.";
            return;
        }

        int totalWins = 0, totalPartials = 0;
        foreach (var d in draws)
            foreach (var (sMain, sSp, _) in searchList)
            {
                var (lvl, _) = Classify(game.Mode, d.Main, d.Special, sMain, sSp);
                if (lvl == 2) totalWins++;
                else if (lvl == 1) totalPartials++;
            }

        lblSummary.Text = $"{draws.Count} draws  •  {searchList.Count} numbers  •  ★ {totalWins} win(s)  •  ◆ {totalPartials} partial(s)";
        lblStatus.Text  = $"#{fromDraw}–#{toDraw}  |  {game.Name}  |  {searchList.Count} numbers";

        foreach (var d in draws)
            resultsContainer.Children.Add(BuildDrawCard(d, game, searchList));
    }

    View BuildDrawCard(DrawRow d, GameConfig game,
                       List<(int[] Main, int Special, string Raw)> searchList)
    {
        bool anyWin     = searchList.Any(s => Classify(game.Mode, d.Main, d.Special, s.Main, s.Special).Level == 2);
        bool anyPartial = !anyWin && searchList.Any(s => Classify(game.Mode, d.Main, d.Special, s.Main, s.Special).Level == 1);

        Color cardBg = anyWin     ? Color.FromArgb("#E8F5E9")
                     : anyPartial ? Color.FromArgb("#FFF8E1")
                                  : Colors.White;

        string dateStr = d.DateRaw;
        if (DateTime.TryParse(d.DateRaw, out var dt)) dateStr = dt.ToString("MMM d, yyyy");
        string timeStr = string.IsNullOrEmpty(d.DrawTime) ? "" : $"  {d.DrawTime}";

        var ballRow = new HorizontalStackLayout { Spacing = 4 };
        foreach (int n in d.Main)
            ballRow.Children.Add(Ball(n.ToString(), game.BallColor, 30));
        if (game.Mode == InputMode.Numbers5Plus1 && d.Special > 0)
        {
            ballRow.Children.Add(new Label
            {
                Text = "•", FontSize = 12, TextColor = Color.FromArgb("#9E9E9E"),
                VerticalOptions = LayoutOptions.Center, Margin = new Thickness(2, 0)
            });
            ballRow.Children.Add(Ball(d.Special.ToString(), Color.FromArgb("#880E4F"), 30));
        }

        var header = new VerticalStackLayout { Spacing = 4, Padding = new Thickness(12, 8, 12, 6) };
        header.Children.Add(new Label
        {
            Text = $"#{d.DrawNumber}  {dateStr}{timeStr}",
            FontSize = 12, FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#374151")
        });
        header.Children.Add(ballRow);

        var sep = new BoxView { BackgroundColor = Color.FromArgb("#E5E7EB"), HeightRequest = 1 };

        var matchRows = new VerticalStackLayout { Spacing = 0 };
        for (int i = 0; i < searchList.Count; i++)
        {
            var (sMain, sSp, rawTxt) = searchList[i];
            var (level, label) = Classify(game.Mode, d.Main, d.Special, sMain, sSp);

            Color tagColor = level == 2 ? Color.FromArgb("#2E7D32")
                           : level == 1 ? Color.FromArgb("#F57F17")
                                        : Color.FromArgb("#BDBDBD");
            Color rowBg    = level == 2 ? Color.FromArgb("#F1F8F2")
                           : level == 1 ? Color.FromArgb("#FFFDF0")
                                        : Colors.Transparent;

            var matchRow = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(28) },
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto }
                },
                Padding     = new Thickness(12, 5),
                BackgroundColor = rowBg
            };

            matchRow.Add(new Label
            {
                Text = $"#{i + 1}", FontSize = 11, TextColor = Color.FromArgb("#9CA3AF"),
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);

            matchRow.Add(new Label
            {
                Text = rawTxt, FontSize = 15, FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#1565C0"), VerticalOptions = LayoutOptions.Center
            }, 1, 0);

            matchRow.Add(new Label
            {
                Text = label, FontSize = 12, FontAttributes = FontAttributes.Bold,
                TextColor = tagColor, HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center
            }, 2, 0);

            matchRows.Children.Add(matchRow);

            if (i < searchList.Count - 1)
                matchRows.Children.Add(new BoxView
                {
                    BackgroundColor = Color.FromArgb("#F3F4F6"),
                    HeightRequest   = 1,
                    Margin          = new Thickness(12, 0)
                });
        }

        var stack = new VerticalStackLayout { Spacing = 0 };
        stack.Children.Add(header);
        stack.Children.Add(sep);
        stack.Children.Add(matchRows);

        return new Border
        {
            BackgroundColor = cardBg,
            StrokeThickness = 0,
            StrokeShape     = new RoundRectangle { CornerRadius = 10 },
            Content         = stack
        };
    }

    // ── Match logic ───────────────────────────────────────────────

    static (int Level, string Label) Classify(
        InputMode mode, int[] winMain, int winSp, int[] srchMain, int srchSp)
    {
        switch (mode)
        {
            case InputMode.Digits3:
            case InputMode.Digits4:
                if (winMain.SequenceEqual(srchMain))
                    return (2, "STRAIGHT ★");
                if (winMain.OrderBy(x => x).SequenceEqual(srchMain.OrderBy(x => x)))
                    return (1, "BOX ◆");
                return (0, "—");

            case InputMode.Numbers5:
            {
                int m = srchMain.Count(n => winMain.Contains(n));
                if (m == 5) return (2, "5/5 ★");
                if (m >= 2) return (1, $"{m}/5 ◆");
                return (0, "—");
            }

            case InputMode.Numbers5Plus1:
            {
                int  m     = srchMain.Count(n => winMain.Contains(n));
                bool spHit = winSp == srchSp;
                if (m == 5 && spHit) return (2, "5+1 ★");
                if (m == 5)          return (2, "5/5 ★");
                if (m >= 3 && spHit) return (1, $"{m}+1 ◆");
                if (m >= 3)          return (1, $"{m}/5 ◆");
                if (spHit)           return (1, "+1 ◆");
                return (0, "—");
            }

            case InputMode.Numbers3Ordered:
            {
                if (winMain.SequenceEqual(srchMain)) return (2, "EXACT ★");
                int p = srchMain.Where((n, i) => i < winMain.Length && winMain[i] == n).Count();
                if (p > 0) return (1, $"{p}/3 ◆");
                return (0, "—");
            }

            default:
                return (0, "—");
        }
    }

    static Border Ball(string text, Color color, int size) => new Border
    {
        BackgroundColor = color,
        StrokeThickness = 0,
        StrokeShape     = new RoundRectangle { CornerRadius = size / 2 },
        WidthRequest    = size, HeightRequest = size,
        Content = new Label
        {
            Text              = text,
            FontSize          = size / 2,
            FontAttributes    = FontAttributes.Bold,
            TextColor         = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions   = LayoutOptions.Center
        }
    };

    void UpdateGameLogo()
    {
        if (gamePicker.SelectedIndex < 0) return;
        string game = gamePicker.Items[gamePicker.SelectedIndex];
        imgGameLogo.Source = game switch
        {
            "Fantasy 5"    => "logo_fantasy5.png",
            "Super Lotto"  => "logo_superlotto.png",
            "Powerball"    => "logo_powerball.png",
            "Mega Millions"=> "logo_megamillions.png",
            "Daily 3"      => "logo_daily3.png",
            "Daily 4"      => "logo_daily4.png",
            "Daily Derby"  => "logo_dailyderby.png",
            _              => "logo_fantasy5.png"
        };
    }
}
