using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

public partial class RundownPage : ContentPage
{
    // ── Game config ───────────────────────────────────────────────

    record GameDef(string Name, int Digits, string AccentHex);

    static readonly GameDef[] _games =
    [
        new("Daily 3", 3, "#1565C0"),
        new("Daily 4", 4, "#0D47A1"),
    ];

    // ── Presets ───────────────────────────────────────────────────

    record Preset(string Label, int[] Step, int Direction);

    static readonly Preset[] _presetsD3 =
    [
        new("+111", [1,1,1], +1),
        new("+222", [2,2,2], +1),
        new("+333", [3,3,3], +1),
        new("+555", [5,5,5], +1),
        new("+123", [1,2,3], +1),
        new("+321", [3,2,1], +1),
        new("+132", [1,3,2], +1),
        new("−111", [1,1,1], -1),
        new("−222", [2,2,2], -1),
        new("−123", [1,2,3], -1),
        new("−321", [3,2,1], -1),
    ];

    static readonly Preset[] _presetsD4 =
    [
        new("+1111", [1,1,1,1], +1),
        new("+2222", [2,2,2,2], +1),
        new("+3333", [3,3,3,3], +1),
        new("+1234", [1,2,3,4], +1),
        new("+4321", [4,3,2,1], +1),
        new("−1111", [1,1,1,1], -1),
        new("−2222", [2,2,2,2], -1),
        new("−1234", [1,2,3,4], -1),
    ];

    // ── State ─────────────────────────────────────────────────────

    int           _selGame    = 0;
    int           _direction  = +1;   // +1 or -1
    List<Entry>   _seedEntries = [];
    List<Entry>   _stepEntries = [];
    int[][]       _results    = [];   // [row][digit]

    public static string PresetGame { get; set; } = "Daily 3";

    // ── Constructor ───────────────────────────────────────────────

    public RundownPage()
    {
        InitializeComponent();

        foreach (var g in _games)
            gamePicker.Items.Add(g.Name);

        gamePicker.SelectedIndex = 0;
        UpdateGameLogo();
    }

    // ── Lifecycle ─────────────────────────────────────────────────

    protected override void OnAppearing()
    {
        base.OnAppearing();
        int idx = Array.FindIndex(_games, g => g.Name == PresetGame);
        if (idx >= 0 && idx != gamePicker.SelectedIndex)
            gamePicker.SelectedIndex = idx;
    }

    // ── Game picker ───────────────────────────────────────────────

    private void GamePicker_Changed(object? sender, EventArgs e)
    {
        int i = gamePicker.SelectedIndex;
        if (i < 0) return;
        _selGame = i;
        BuildSeedRow();
        BuildStepRow();
        BuildPresets();
        resultsCard.IsVisible = false;
        _results = [];
        lblStatus.Text = $"Enter your {_games[i].Name} number and step.";
        UpdateGameLogo();
    }

    // ── Seed row ──────────────────────────────────────────────────

    void BuildSeedRow()
    {
        seedRow.Children.Clear();
        _seedEntries.Clear();
        int digits = _games[_selGame].Digits;

        for (int i = 0; i < digits; i++)
        {
            int ci = i;
            var entry = MakeDigitEntry();
            entry.TextChanged += (s, ev) =>
            {
                UpdateSeedSum();
                if ((ev.NewTextValue?.Length ?? 0) >= 1)
                    Dispatcher.Dispatch(() => AdvanceFocus(_seedEntries, ci));
            };
            _seedEntries.Add(entry);
            seedRow.Children.Add(entry);
        }
    }

    void UpdateSeedSum()
    {
        var nums = ParseEntries(_seedEntries);
        if (nums == null)
        { lblSeedSum.Text = ""; return; }
        lblSeedSum.Text = $"Sum: {nums.Sum()}";
    }

    // ── Step row ──────────────────────────────────────────────────

    void BuildStepRow()
    {
        stepRow.Children.Clear();
        _stepEntries.Clear();
        int digits = _games[_selGame].Digits;

        for (int i = 0; i < digits; i++)
        {
            int ci = i;
            var entry = MakeDigitEntry();
            entry.Text = "1";
            entry.TextChanged += (s, ev) =>
            {
                UpdateStepHint();
                if ((ev.NewTextValue?.Length ?? 0) >= 1)
                    Dispatcher.Dispatch(() => AdvanceFocus(_stepEntries, ci));
            };
            _stepEntries.Add(entry);
            stepRow.Children.Add(entry);
        }

        UpdateStepHint();
    }

    void UpdateStepHint()
    {
        var step = ParseEntries(_stepEntries);
        if (step == null) { lblStepHint.Text = ""; return; }
        string sign = _direction > 0 ? "+" : "−";
        string digits = string.Join("", step);
        lblStepHint.Text = $"{sign}{digits} per row  (wraps 9→0)";
    }

    // ── Preset pills ──────────────────────────────────────────────

    void BuildPresets()
    {
        presetRow.Children.Clear();
        var presets = _selGame == 0 ? _presetsD3 : _presetsD4;
        var accent  = _games[_selGame].AccentHex;

        foreach (var p in presets)
        {
            var captured = p;
            var pill = new Border
            {
                BackgroundColor = Color.FromArgb("#E3F2FD"),
                StrokeThickness = 0,
                StrokeShape     = new RoundRectangle { CornerRadius = 12 },
                Padding         = new Thickness(12, 6),
                Margin          = new Thickness(0, 0, 6, 0),
                Content = new Label
                {
                    Text           = p.Label,
                    FontSize       = 12,
                    FontAttributes = FontAttributes.Bold,
                    TextColor      = Color.FromArgb(accent),
                }
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, ev) => ApplyPreset(captured);
            pill.GestureRecognizers.Add(tap);
            presetRow.Children.Add(pill);
        }
    }

    void ApplyPreset(Preset p)
    {
        // Set direction
        _direction = p.Direction;
        directionLabel.Text            = _direction > 0 ? "+" : "−";
        directionBorder.BackgroundColor = _direction > 0
            ? Color.FromArgb("#1565C0")
            : Color.FromArgb("#C62828");

        // Set step digits
        for (int i = 0; i < _stepEntries.Count && i < p.Step.Length; i++)
            _stepEntries[i].Text = p.Step[i].ToString();

        UpdateStepHint();
    }

    // ── Direction toggle ──────────────────────────────────────────

    private void ToggleDirection_Tapped(object? sender, TappedEventArgs e)
    {
        _direction = -_direction;
        directionLabel.Text            = _direction > 0 ? "+" : "−";
        directionBorder.BackgroundColor = _direction > 0
            ? Color.FromArgb("#1565C0")
            : Color.FromArgb("#C62828");
        UpdateStepHint();
    }

    // ── Calculate ─────────────────────────────────────────────────

    private void BtnCalculate_Clicked(object? sender, EventArgs e)
    {
        var seed = ParseEntries(_seedEntries);
        var step = ParseEntries(_stepEntries);
        var game = _games[_selGame];

        if (seed == null)
        {
            lblStatus.Text = $"Enter all {game.Digits} digits for your starting number.";
            return;
        }
        if (step == null)
        {
            lblStatus.Text = $"Enter all {game.Digits} step digits (0–9).";
            return;
        }
        if (step.Any(d => d > 9 || d < 0))
        {
            lblStatus.Text = "Step digits must be 0–9.";
            return;
        }

        _results = RunDown(seed, step, _direction, rows: 10);
        BuildResults(game);

        string sign   = _direction > 0 ? "+" : "−";
        string stepStr = string.Join("", step);
        lblStatus.Text = $"Seed: {string.Join("", seed)}  ·  Step: {sign}{stepStr}  ·  10 rows";
    }

    // ── Rundown algorithm ─────────────────────────────────────────

    static int[][] RunDown(int[] seed, int[] step, int direction, int rows)
    {
        var result = new int[rows + 1][];
        result[0] = seed.ToArray();
        for (int r = 1; r <= rows; r++)
        {
            result[r] = new int[seed.Length];
            for (int d = 0; d < seed.Length; d++)
                result[r][d] = ((result[r - 1][d] + direction * step[d]) % 10 + 10) % 10;
        }
        return result;
    }

    // ── Build result rows ─────────────────────────────────────────

    void BuildResults(GameDef game)
    {
        resultsContainer.Children.Clear();
        var accent = Color.FromArgb(game.AccentHex);

        for (int r = 0; r < _results.Length; r++)
        {
            var digits = _results[r];
            bool isSeed = r == 0;
            int  sum    = digits.Sum();

            var rowBg = isSeed
                ? Color.FromArgb("#E3F2FD")
                : (r % 2 == 0 ? Color.FromArgb("#FAFAFA") : Colors.White);

            var row = new Grid
            {
                BackgroundColor     = rowBg,
                ColumnDefinitions   =
                [
                    new ColumnDefinition { Width = new GridLength(48) },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = new GridLength(36) },
                ],
                Padding = new Thickness(4, 5),
            };

            // Row label
            string rowLabel = isSeed ? "Seed" : r.ToString();
            row.Add(new Label
            {
                Text                    = rowLabel,
                FontSize                = isSeed ? 10 : 12,
                FontAttributes          = isSeed ? FontAttributes.Bold : FontAttributes.None,
                TextColor               = isSeed ? Color.FromArgb("#1565C0") : Color.FromArgb("#6B7280"),
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalOptions         = LayoutOptions.Center,
            }, 0, 0);

            // Digit chips
            var chips = new HorizontalStackLayout
            {
                Spacing           = 6,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions   = LayoutOptions.Center,
            };

            foreach (int d in digits)
            {
                chips.Children.Add(new Border
                {
                    BackgroundColor = accent,
                    StrokeThickness = 0,
                    StrokeShape     = new RoundRectangle { CornerRadius = 8 },
                    WidthRequest    = 38,
                    HeightRequest   = 38,
                    Content = new Label
                    {
                        Text                    = d.ToString(),
                        FontSize                = 16,
                        FontAttributes          = FontAttributes.Bold,
                        TextColor               = Colors.White,
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalTextAlignment   = TextAlignment.Center,
                    }
                });
            }

            row.Add(chips, 1, 0);

            // Sum label
            row.Add(new Label
            {
                Text                    = sum.ToString(),
                FontSize                = 12,
                TextColor               = Color.FromArgb("#6B7280"),
                HorizontalTextAlignment = TextAlignment.End,
                VerticalOptions         = LayoutOptions.Center,
            }, 2, 0);

            resultsContainer.Children.Add(row);
        }

        string sign    = _direction > 0 ? "+" : "−";
        var    step    = ParseEntries(_stepEntries)!;
        lblResultTitle.Text = $"Rundown  {sign}{string.Join("", step)}  ·  {_results.Length} rows";
        resultsCard.IsVisible = true;
    }

    // ── Copy all ──────────────────────────────────────────────────

    private async void BtnCopyAll_Clicked(object? sender, EventArgs e)
    {
        if (_results.Length == 0) return;
        var lines = _results.Select(row => string.Join("", row));
        await Clipboard.SetTextAsync(string.Join("\n", lines));
        lblStatus.Text = $"Copied {_results.Length} numbers to clipboard.";
    }

    // ── Helpers ───────────────────────────────────────────────────

    static Entry MakeDigitEntry() => new()
    {
        Keyboard                = Keyboard.Numeric,
        MaxLength               = 1,
        WidthRequest            = 50,
        HeightRequest           = 52,
        FontSize                = 22,
        FontAttributes          = FontAttributes.Bold,
        HorizontalTextAlignment = TextAlignment.Center,
        BackgroundColor         = Color.FromArgb("#EFF6FF"),
    };

    static void AdvanceFocus(List<Entry> entries, int currentIdx)
    {
        int next = currentIdx + 1;
        if (next < entries.Count)
            entries[next].Focus();
    }

    // Returns parsed int[] if all entries are valid single digits; else null.
    static int[]? ParseEntries(List<Entry> entries)
    {
        if (entries.Count == 0) return null;
        var result = new int[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            string t = entries[i].Text?.Trim() ?? "";
            if (t.Length == 0 || !int.TryParse(t, out int v) || v < 0 || v > 9)
                return null;
            result[i] = v;
        }
        return result;
    }

    // ── Navigation ────────────────────────────────────────────────

    private async void BtnGoHome_Clicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync("//MainPage");

    void UpdateGameLogo()
    {
        if (gamePicker.SelectedIndex < 0) return;
        string game = gamePicker.Items[gamePicker.SelectedIndex];
        imgGameLogo.Source = game switch
        {
            "Daily 3" => "logo_daily3.png",
            "Daily 4" => "logo_daily4.png",
            _         => "logo_daily3.png"
        };
    }
}
