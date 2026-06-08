#if IOS
using Vision;
using CoreGraphics;
using Foundation;
using UIKit;
#endif
using System.Text.RegularExpressions;

namespace DailyFantasyMAUI;

record GameConfig(
    string Name, string Short, string SetPrefix, string ActiveSlotKey,
    int SetCols,
    int MainMin, int MainMax, int MainPick,
    int GridCols,    // UI display columns
    int DetectCols,  // bubble detection columns (matches real slip layout)
    int SpecialMax, int SpecialPick,
    string AccentColor, string SpecialColor, string SpecialName,
    string NavPage, string ActiveSlotPrefKey,
    bool OrderedPick = false,  // true = preserve tap order (Daily 3/4)
    bool HasTime = false       // true = game has a 3-digit race time pick (Daily Derby)
);

public partial class ScanSlipPage : ContentPage
{
    static readonly GameConfig[] Games =
    [
        //                                                              GridCols DetectCols
        new("Fantasy 5",        "F5", "f5_set_", "f5_active_slot",  5, 1, 39, 5, 7, 4,  0,  0, "#FF8F00", "",        "",          nameof(WinnerPage),     "f5_active_slot"),
        new("Super Lotto Plus", "SL", "sl_set_", "sl_active_slot",  6, 1, 47, 5, 6, 6, 27,  1, "#7B1FA2", "#CE93D8", "Mega Ball", nameof(SuperLottoPage), "sl_active_slot"),
        new("Daily 3",          "D3", "d3_set_", "d3_active_slot",  3, 0,  9, 3, 5, 5,  0,  0, "#1565C0", "",        "",          nameof(Daily3Page),     "d3_active_slot", OrderedPick: true),
        new("Daily 4",          "D4", "d4_set_", "d4_active_slot",  4, 0,  9, 4, 5, 5,  0,  0, "#00695C", "",        "",          nameof(Daily4Page),     "d4_active_slot", OrderedPick: true),
        new("Powerball",        "PB", "pb_set_", "pb_active_slot",  6, 1, 69, 5, 7, 7, 26,  1, "#C62828", "#EF9A9A", "Powerball", nameof(PowerballPage),  "pb_active_slot"),
        new("Daily Derby",      "DD", "dd_set_", "dd_active_slot",  4, 1, 12, 3, 6, 6,  0,  0, "#5D4037", "",        "",          nameof(DailyDerbyPage), "dd_active_slot", OrderedPick: true, HasTime: true),
    ];

    int _gameIdx = 0;
    GameConfig Game => Games[_gameIdx];

    // Per-panel state (5 panels A–E)
    List<int>[] _panelMain    = [];
    int[]       _panelSpecial = [];  // special ball value, -1 = unset
    string[]    _panelTimeStr = [];  // 3-digit race time string (Daily Derby only)

    int  _activePanel = 0;
    bool _cropOpen    = false;

    Button[] _numBtns          = [];
    Button[] _specialBtns      = [];
    Button[] _panelBtns        = [];
    Button[] _gameBtns         = [];
    Entry[]  _timeEntryControls = [];

    public ScanSlipPage()
    {
        InitializeComponent();
        ResetPanels();
        BuildGameButtons();
        BuildPanelButtons();
        BuildNumberGrid();
        RefreshSaved();
        RefreshSetPicker();
    }

    void ResetPanels()
    {
        _panelMain    = Enumerable.Range(0, 5).Select(_ => new List<int>()).ToArray();
        _panelSpecial = Enumerable.Repeat(-1, 5).ToArray();
        _panelTimeStr = Enumerable.Repeat("", 5).ToArray();
    }

    // ── Game selector ──────────────────────────────────────────────────────────

    void BuildGameButtons()
    {
        stkGames.Children.Clear();
        _gameBtns = new Button[Games.Length];
        for (int i = 0; i < Games.Length; i++)
        {
            int idx = i;
            var btn = new Button
            {
                Text = Games[i].Short,
                FontSize = 13, FontAttributes = FontAttributes.Bold,
                BackgroundColor = idx == _gameIdx
                    ? MColor(Games[i].AccentColor) : MColor("#2D3D50"),
                TextColor = Microsoft.Maui.Graphics.Colors.White,
                WidthRequest = 52, HeightRequest = 38, CornerRadius = 19, Padding = Thickness.Zero
            };
            btn.Clicked += (s, e) => SelectGame(idx);
            _gameBtns[i] = btn;
            stkGames.Children.Add(btn);
        }
    }

    void SelectGame(int idx)
    {
        _gameIdx = idx;
        ResetPanels();
        for (int i = 0; i < _gameBtns.Length; i++)
            _gameBtns[i].BackgroundColor = i == idx ? MColor(Games[i].AccentColor) : MColor("#2D3D50");
        BuildNumberGrid();  // rebuilds panel buttons too
        RefreshSaved();
        RefreshSetPicker();
        lblStatus.Text = $"{Game.Name} selected — pick {Game.MainPick}{(Game.SpecialPick > 0 ? $" + {Game.SpecialName}" : "")} per panel";
    }

    // ── Panel A–E ──────────────────────────────────────────────────────────────

    void BuildPanelButtons()
    {
        stkPanels.Children.Clear();
        _panelBtns = new Button[5];
        for (int i = 0; i < 5; i++)
        {
            int idx = i;
            var btn = new Button
            {
                Text = ((char)('A' + i)).ToString(),
                FontSize = 15, FontAttributes = FontAttributes.Bold,
                BackgroundColor = idx == 0 ? MColor(Game.AccentColor) : MColor("#2D3D50"),
                TextColor = Microsoft.Maui.Graphics.Colors.White,
                WidthRequest = 46, HeightRequest = 46, CornerRadius = 23, Padding = Thickness.Zero
            };
            btn.Clicked += (s, e) => SelectPanel(idx);
            _panelBtns[i] = btn;
            stkPanels.Children.Add(btn);
        }
    }

    void SelectPanel(int idx)
    {
        _activePanel = idx;
        for (int i = 0; i < 5; i++)
            _panelBtns[i].BackgroundColor = i == idx ? MColor(Game.AccentColor) : MColor("#2D3D50");
        RefreshGrid();
        RefreshTimeEntries();
    }

    // ── Number grids ───────────────────────────────────────────────────────────

    void BuildNumberGrid()
    {
        // Main grid
        int total = Game.MainMax - Game.MainMin + 1;
        int cols  = Game.GridCols;
        int rows  = (int)Math.Ceiling(total / (float)cols);

        grdNumbers.ColumnDefinitions.Clear();
        grdNumbers.RowDefinitions.Clear();
        grdNumbers.Children.Clear();
        for (int c = 0; c < cols; c++) grdNumbers.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (int r = 0; r < rows; r++) grdNumbers.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        _numBtns = new Button[total];
        for (int i = 0; i < total; i++)
        {
            int num = Game.MainMin + i;
            var btn = MakeNumBtn(num.ToString(), () => ToggleMain(num));
            _numBtns[i] = btn;
            grdNumbers.Add(btn, i % cols, i / cols);
        }

        // Special ball grid (SL Mega / PB Powerball)
        bool hasSpecial = Game.SpecialMax > 0;
        lblSpecial.IsVisible = hasSpecial;
        grdSpecial.IsVisible = hasSpecial;

        grdSpecial.ColumnDefinitions.Clear();
        grdSpecial.RowDefinitions.Clear();
        grdSpecial.Children.Clear();

        if (hasSpecial)
        {
            lblSpecial.Text = Game.SpecialName.ToUpper();
            int sCols = 6;
            int sRows = (int)Math.Ceiling(Game.SpecialMax / (float)sCols);
            for (int c = 0; c < sCols; c++) grdSpecial.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            for (int r = 0; r < sRows; r++) grdSpecial.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            _specialBtns = new Button[Game.SpecialMax];
            for (int i = 0; i < Game.SpecialMax; i++)
            {
                int num = i + 1;
                var btn = MakeNumBtn(num.ToString(), () => ToggleSpecial(num), special: true);
                _specialBtns[i] = btn;
                grdSpecial.Add(btn, i % sCols, i / sCols);
            }
        }

        // Time entry for Daily Derby
        bool hasTime = Game.HasTime;
        lblTime.IsVisible = hasTime;
        stkTimeEntries.IsVisible = hasTime;
        stkTimeEntries.Children.Clear();
        _timeEntryControls = new Entry[3];
        if (hasTime)
        {
            for (int d = 0; d < 3; d++)
            {
                int di = d;
                var entry = new Entry
                {
                    Keyboard = Keyboard.Numeric,
                    FontSize = 22, FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    BackgroundColor = Microsoft.Maui.Graphics.Color.FromArgb("#3E2723"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    WidthRequest = 52, HeightRequest = 48, MaxLength = 1
                };
                entry.TextChanged += (_, _) =>
                {
                    _panelTimeStr[_activePanel] = string.Concat(_timeEntryControls.Select(e => e.Text ?? ""));
                    if ((entry.Text?.Length ?? 0) == 1 && di + 1 < 3)
                        Dispatcher.Dispatch(() => _timeEntryControls[di + 1].Focus());
                    UpdateSaveButton();
                    RefreshSaved();
                };
                _timeEntryControls[d] = entry;
                stkTimeEntries.Children.Add(entry);
            }
        }

        BuildPanelButtons();
        RefreshGrid();
        RefreshTimeEntries();
    }

    void RefreshTimeEntries()
    {
        if (!Game.HasTime || _timeEntryControls.Length == 0) return;
        string t = _panelTimeStr[_activePanel];
        for (int d = 0; d < 3; d++)
            _timeEntryControls[d].Text = d < t.Length ? t[d].ToString() : "";
    }

    Button MakeNumBtn(string text, Action onClick, bool special = false)
    {
        var btn = new Button
        {
            Text = text, FontSize = 11, FontAttributes = FontAttributes.Bold,
            BackgroundColor = MColor("#2D3D50"),
            TextColor = MColor("#9CA3AF"),
            HeightRequest = 30, CornerRadius = 15,
            Margin = new Thickness(1), Padding = Thickness.Zero
        };
        btn.Clicked += (s, e) => onClick();
        return btn;
    }

    void ToggleMain(int n)
    {
        var main = _panelMain[_activePanel];
        int btnIdx = n - Game.MainMin;
        if (Game.OrderedPick)
        {
            // D3/D4: duplicates allowed — always add, never toggle off
            if (main.Count < Game.MainPick)
            {
                main.Add(n);
                StyleBtn(_numBtns[btnIdx], true);
            }
        }
        else
        {
            if (main.Contains(n))
            {
                main.Remove(n);
                StyleBtn(_numBtns[btnIdx], false);
            }
            else if (main.Count < Game.MainPick)
            {
                main.Add(n);
                StyleBtn(_numBtns[btnIdx], true);
            }
        }
        UpdateSaveButton();
        RefreshSaved();
    }

    void ToggleSpecial(int n)
    {
        int prev = _panelSpecial[_activePanel];
        if (prev == n)
        {
            _panelSpecial[_activePanel] = -1;
            StyleBtn(_specialBtns[n - 1], false, special: true);
        }
        else
        {
            if (prev > 0) StyleBtn(_specialBtns[prev - 1], false, special: true);
            _panelSpecial[_activePanel] = n;
            StyleBtn(_specialBtns[n - 1], true, special: true);
        }
        UpdateSaveButton();
        RefreshSaved();
    }

    void StyleBtn(Button btn, bool on, bool special = false)
    {
        btn.BackgroundColor = on ? MColor(special ? Game.SpecialColor : Game.AccentColor) : MColor("#2D3D50");
        btn.TextColor       = on ? Microsoft.Maui.Graphics.Colors.White : MColor("#9CA3AF");
    }

    void RefreshGrid()
    {
        var main = _panelMain[_activePanel];
        for (int i = 0; i < _numBtns.Length; i++)
            StyleBtn(_numBtns[i], main.Contains(Game.MainMin + i));
        if (Game.SpecialMax > 0)
        {
            int sp = _panelSpecial[_activePanel];
            for (int i = 0; i < _specialBtns.Length; i++)
                StyleBtn(_specialBtns[i], i + 1 == sp, special: true);
        }
        UpdateSaveButton();
    }

    void UpdateSaveButton()
    {
        char lbl      = (char)('A' + _activePanel);
        int  mainCnt  = _panelMain[_activePanel].Count;
        bool spOk     = Game.SpecialPick == 0 || _panelSpecial[_activePanel] > 0;
        bool timeOk   = !Game.HasTime || _panelTimeStr[_activePanel].Length == 3;
        bool ready    = mainCnt == Game.MainPick && spOk && timeOk;
        int  have     = mainCnt + (Game.SpecialPick > 0 && _panelSpecial[_activePanel] > 0 ? 1 : 0);
        int  need     = Game.MainPick + Game.SpecialPick;

        if (Game.HasTime)
            lblCount.Text = $"{mainCnt}/{Game.MainPick} horses  +  time: {(_panelTimeStr[_activePanel].Length > 0 ? _panelTimeStr[_activePanel] : "---")}";
        else if (Game.SpecialPick > 0)
            lblCount.Text = $"{mainCnt}/{Game.MainPick} numbers + {(spOk ? "1" : "0")}/{Game.SpecialPick} {Game.SpecialName}";
        else
            lblCount.Text = $"{mainCnt} / {Game.MainPick} selected";

        btnSavePanel.Text       = $"Save Panel {lbl}  ({have}/{need})";
        btnSavePanel.IsEnabled  = ready;
        btnSavePanel.BackgroundColor = ready ? MColor("#2E7D32") : MColor(Game.AccentColor);
    }

    bool PanelComplete(int i) =>
        _panelMain[i].Count == Game.MainPick &&
        (Game.SpecialPick == 0 || _panelSpecial[i] > 0) &&
        (!Game.HasTime || _panelTimeStr[i].Length == 3);

    // ── Scan button ────────────────────────────────────────────────────────────

    async void BtnScan_Clicked(object sender, EventArgs e)
    {
        string? mode = await DisplayActionSheet(
            $"Scanning Panel {(char)('A' + _activePanel)} — what are you photographing?",
            "Cancel", null,
            "📄 Printed ticket  (reads numbers via OCR)",
            "🖊 Filled slip  (detects darkened bubbles)");
        if (mode == null || mode == "Cancel") return;
        bool isTicket = mode.StartsWith("📄");

        btnScan.IsEnabled = false;
        loadingOverlay.IsVisible = true;
        lblLoading.Text = "Opening camera…";
        try
        {
            string title = isTicket
                ? $"Fill frame with the {Game.Short} numbers on your ticket"
                : $"Panel {(char)('A' + _activePanel)}: fill frame with the number circles only";

            var photo = await MediaPicker.CapturePhotoAsync(new MediaPickerOptions { Title = title });
            if (photo == null) { loadingOverlay.IsVisible = false; btnScan.IsEnabled = true; return; }

            loadingOverlay.IsVisible = false;
            CropPage.Result = new TaskCompletionSource<string?>();
            _cropOpen = true;
            await Navigation.PushModalAsync(new CropPage(photo.FullPath), false);
            _cropOpen = false;
            string? croppedPath = await CropPage.Result.Task;
            if (croppedPath == null) { btnScan.IsEnabled = true; return; }

            loadingOverlay.IsVisible = true;
            HashSet<int> detected;
            if (isTicket)
            {
                lblLoading.Text = "Reading ticket numbers…";
#if IOS
                detected = await OcrTicketNumbers(croppedPath);
#else
                detected = [];
#endif
            }
            else
            {
                lblLoading.Text = "Detecting filled bubbles…";
#if IOS
                detected = await DetectFilledBubbles(croppedPath);
#else
                detected = [];
#endif
            }
            loadingOverlay.IsVisible = false;

            string dbgPath = System.IO.Path.Combine(FileSystem.CacheDirectory, "slip_debug.jpg");
            if (System.IO.File.Exists(dbgPath))
            {
                imgPreview.Source = ImageSource.FromFile(dbgPath);
                btnTogglePreview.IsVisible = true;
                frmPreview.IsVisible = false;
                btnTogglePreview.Text = "Show Scanned Image";
            }

            _panelMain[_activePanel].Clear();
            _panelSpecial[_activePanel] = -1;
            foreach (int n in detected.Where(n => n >= Game.MainMin && n <= Game.MainMax).Take(Game.MainPick))
                _panelMain[_activePanel].Add(n);
            RefreshGrid();

            char panelLbl = (char)('A' + _activePanel);
            string method = isTicket ? "read from ticket" : "detected from slip";
            lblStatus.Text = _panelMain[_activePanel].Count > 0
                ? $"Panel {panelLbl}: {_panelMain[_activePanel].Count} number(s) {method} — correct then Save"
                : "Could not read numbers — tap your numbers in the grid below";
        }
        catch (Exception ex) { loadingOverlay.IsVisible = false; lblStatus.Text = $"Error: {ex.Message}"; }
        btnScan.IsEnabled = true;
    }

    void BtnTogglePreview_Clicked(object sender, EventArgs e)
    {
        frmPreview.IsVisible = !frmPreview.IsVisible;
        btnTogglePreview.Text = frmPreview.IsVisible ? "Hide Scanned Image" : "Show Scanned Image";
    }

    // ── Ticket OCR ─────────────────────────────────────────────────────────────

#if IOS
    async Task<HashSet<int>> OcrTicketNumbers(string imagePath)
    {
        try
        {
            var image = UIImage.FromFile(imagePath);
            if (image == null) return [];

            var tcs = new TaskCompletionSource<List<string>>();
            var textList = new List<string>();

            var request = new VNRecognizeTextRequest((req, err) =>
            {
                if (err != null) { tcs.TrySetResult(textList); return; }
                var observations = req.GetResults<VNRecognizedTextObservation>();
                if (observations == null) { tcs.TrySetResult(textList); return; }
                foreach (var obs in observations)
                {
                    var candidate = obs.TopCandidates(1).FirstOrDefault();
                    if (candidate != null) textList.Add(candidate.String ?? "");
                }
                tcs.TrySetResult(textList);
            });
            request.RecognitionLevel = VNRequestTextRecognitionLevel.Accurate;
            request.RecognitionLanguages = new[] { "en-US" };
            request.UsesLanguageCorrection = false;

            await Task.Run(() =>
            {
                using var cgImage = image.CGImage;
                if (cgImage == null) { tcs.TrySetResult(textList); return; }
                var handler = new VNImageRequestHandler(cgImage, new NSDictionary());
                handler.Perform(new VNRequest[] { request }, out _);
            });

            var lines = await tcs.Task;
            var allText = string.Join(" ", lines);

            var nums = new HashSet<int>();
            foreach (Match m in Regex.Matches(allText, @"\d+"))
            {
                if (int.TryParse(m.Value, out int n) && n >= Game.MainMin && n <= Game.MainMax)
                    nums.Add(n);
                if (nums.Count == Game.MainPick) break;
            }
            return nums;
        }
        catch { return []; }
    }
#endif

    // ── Bubble detection ───────────────────────────────────────────────────────

#if IOS
    async Task<HashSet<int>> DetectFilledBubbles(string imagePath)
    {
        int detectCols = Game.DetectCols;
        int total      = Game.MainMax - Game.MainMin + 1;
        int detectRows = (int)Math.Ceiling(total / (float)detectCols);
        int pick       = Game.MainPick;

        return await Task.Run(() =>
        {
            var uiImage = UIImage.FromFile(imagePath);
            if (uiImage?.CGImage == null) return new HashSet<int>();

            int w = (int)uiImage.Size.Width;
            int h = (int)uiImage.Size.Height;

            // Render to ARGB byte array via CGBitmapContext
            var colorSpace = CGColorSpace.CreateDeviceRGB();
            var bitmapData = new byte[w * h * 4];
            using var context = new CGBitmapContext(bitmapData, w, h, 8, w * 4, colorSpace, CGBitmapFlags.PremultipliedLast);
            context.DrawImage(new CGRect(0, 0, w, h), uiImage.CGImage);

            var gray = new float[w * h];
            for (int i = 0; i < w * h; i++)
            {
                float r = bitmapData[i * 4];
                float g2 = bitmapData[i * 4 + 1];
                float b2 = bitmapData[i * 4 + 2];
                gray[i] = 0.21f * r + 0.72f * g2 + 0.07f * b2;
            }

            // Save debug image
            try
            {
                UIGraphics.BeginImageContextWithOptions(new CGSize(w, h), false, 1.0f);
                var dbgCtx = UIGraphics.GetCurrentContext();
                if (dbgCtx != null)
                {
                    var dbgData = new byte[w * h * 4];
                    for (int i = 0; i < w * h; i++)
                    {
                        byte v = (byte)gray[i];
                        dbgData[i * 4] = v; dbgData[i * 4 + 1] = v;
                        dbgData[i * 4 + 2] = v; dbgData[i * 4 + 3] = 255;
                    }
                    var dbgPath = System.IO.Path.Combine(FileSystem.CacheDirectory, "slip_debug.jpg");
                    // skip debug image save on iOS for brevity
                }
                UIGraphics.EndImageContext();
            }
            catch { }

            int mX = (int)(w * 0.04f), mY = (int)(h * 0.05f);
            float cellW = (w - 2f * mX) / detectCols;
            float cellH = (h - 2f * mY) / detectRows;

            var cells = new List<(int num, float mean)>();
            for (int r = 0; r < detectRows; r++)
            {
                for (int c = 0; c < detectCols; c++)
                {
                    int num = Game.MainMin + r * detectCols + c;
                    if (num > Game.MainMax) continue;

                    float cx = mX + c * cellW + cellW * 0.5f;
                    float cy = mY + r * cellH + cellH * 0.5f;
                    float rad = Math.Min(cellW, cellH) * 0.30f;

                    int x0 = Math.Max(0, (int)(cx - rad)), x1 = Math.Min(w-1, (int)(cx + rad));
                    int y0 = Math.Max(0, (int)(cy - rad)), y1 = Math.Min(h-1, (int)(cy + rad));

                    float sum = 0; int cnt = 0; float r2 = rad * rad;
                    for (int py = y0; py <= y1; py++)
                        for (int px2 = x0; px2 <= x1; px2++)
                        {
                            float dx = px2 - cx, dy = py - cy;
                            if (dx*dx + dy*dy <= r2) { sum += gray[py * w + px2]; cnt++; }
                        }
                    cells.Add((num, cnt > 0 ? sum / cnt : 255f));
                }
            }

            float globalMean = cells.Average(c => c.mean);
            float threshold  = globalMean * 0.80f;
            var filled = new HashSet<int>();
            foreach (var cell in cells.OrderBy(c => c.mean).Take(pick))
                if (cell.mean < threshold) filled.Add(cell.num);
            return filled;
        });
    }
#endif

    // ── Save panel ─────────────────────────────────────────────────────────────

    void BtnSavePanel_Clicked(object sender, EventArgs e)
    {
        char lbl   = (char)('A' + _activePanel);
        var  nums  = Game.OrderedPick ? (IEnumerable<int>)_panelMain[_activePanel] : _panelMain[_activePanel].OrderBy(x => x);
        int  sp    = _panelSpecial[_activePanel];
        string time    = _panelTimeStr[_activePanel];
        string spTxt   = sp > 0 ? $" | {Game.SpecialName} {sp}" : "";
        string timeTxt = Game.HasTime && time.Length == 3 ? $" | Time [{time}]" : "";
        lblStatus.Text = $"Panel {lbl}: {string.Join(", ", nums)}{spTxt}{timeTxt}";
        RefreshSaved();

        // Auto-advance to next incomplete panel
        for (int i = 1; i <= 5; i++)
        {
            int next = (_activePanel + i) % 5;
            if (!PanelComplete(next)) { SelectPanel(next); return; }
        }
    }

    void RefreshSaved()
    {
        stkSaved.Children.Clear();
        bool any = false;

        for (int i = 0; i < 5; i++)
        {
            if (_panelMain[i].Count == 0) continue;
            any = true;
            char lbl      = (char)('A' + i);
            bool complete = PanelComplete(i);
            var  nums     = Game.OrderedPick ? _panelMain[i].ToList() : _panelMain[i].OrderBy(x => x).ToList();
            int  sp       = _panelSpecial[i];

            string numTxt  = string.Join("  ", nums.Select(n => n.ToString("D2")));
            string spTxt   = sp > 0 ? $"  +{sp:D2}" : "";
            string timeTxt = Game.HasTime && _panelTimeStr[i].Length == 3 ? $"  [{_panelTimeStr[i]}]" : "";

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(26)),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(38))
                },
                Margin = new Thickness(0, 2)
            };

            row.Add(new Label
            {
                Text = lbl.ToString(), FontSize = 15, FontAttributes = FontAttributes.Bold,
                TextColor = complete ? MColor("#2E7D32") : MColor(Game.AccentColor),
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);

            row.Add(new Label
            {
                Text = numTxt + spTxt + timeTxt, FontFamily = "Monospace", FontSize = 15,
                TextColor = complete ? Microsoft.Maui.Graphics.Colors.White : MColor("#FFA726"),
                VerticalOptions = LayoutOptions.Center
            }, 1, 0);

            int pi = i;
            var del = new Button
            {
                Text = "✕", FontSize = 13,
                BackgroundColor = MColor("#C62828"), TextColor = Microsoft.Maui.Graphics.Colors.White,
                HeightRequest = 32, WidthRequest = 32, CornerRadius = 16, Padding = Thickness.Zero
            };
            del.Clicked += (s, e2) =>
            {
                _panelMain[pi].Clear(); _panelSpecial[pi] = -1;
                RefreshSaved(); RefreshGrid();
            };
            row.Add(del, 2, 0);
            stkSaved.Children.Add(row);
        }

        if (!any)
            stkSaved.Children.Add(new Label
            {
                Text = "No panels saved yet.", TextColor = MColor("#6B7280"),
                FontSize = 12, HorizontalOptions = LayoutOptions.Center
            });

        grdInsertBar.IsVisible = any;
        if (any) RefreshSetPicker();
    }

    // ── Set picker ─────────────────────────────────────────────────────────────

    void RefreshSetPicker()
    {
        int prev = pickerSet.SelectedIndex;
        pickerSet.Items.Clear();
        int firstEmpty = -1;
        int setCols = Game.SetCols;

        for (int i = 0; i < 10; i++)
        {
            string val    = Preferences.Get($"{Game.SetPrefix}{i}", "");
            bool isEmpty  = string.IsNullOrEmpty(val) || val.Split('|').All(v => string.IsNullOrWhiteSpace(v));
            int usedRows  = 0;
            if (!isEmpty)
            {
                var seg = val.Split('|');
                for (int r = 0; r < 10; r++)
                    if (Enumerable.Range(0, setCols).Any(c => { int idx = r * setCols + c; return idx < seg.Length && !string.IsNullOrWhiteSpace(seg[idx]); }))
                        usedRows++;
            }
            string label = isEmpty ? $"Set {i+1}  ✓ empty" : $"Set {i+1}  ({usedRows}/10 rows used)";
            pickerSet.Items.Add(label);
            if (isEmpty && firstEmpty < 0) firstEmpty = i;
        }

        pickerSet.SelectedIndex = firstEmpty >= 0 ? firstEmpty : Math.Max(0, prev);
    }

    // ── Clear ──────────────────────────────────────────────────────────────────

    void BtnClearPanel_Clicked(object sender, EventArgs e)
    {
        _panelMain[_activePanel].Clear();
        _panelSpecial[_activePanel] = -1;
        RefreshGrid();
    }

    async void BtnClearAll_Clicked(object sender, EventArgs e)
    {
        bool ok = await DisplayAlert("Clear All?", "Remove all 5 panels?", "Clear", "Cancel");
        if (!ok) return;
        ResetPanels();
        RefreshSaved();
        RefreshGrid();
        lblStatus.Text = "";
    }

    // ── Insert ─────────────────────────────────────────────────────────────────

    async void BtnInsert_Clicked(object sender, EventArgs e)
    {
        // Snapshot game config and panel data NOW before any awaits
        var game = Game;
        var allReady = Enumerable.Range(0, 5)
            .Where(i => _panelMain[i].Count == game.MainPick &&
                        (game.SpecialPick == 0 || _panelSpecial[i] > 0) &&
                        (!game.HasTime || _panelTimeStr[i].Length == 3))
            .Select(i =>
            {
                var nums = game.OrderedPick ? _panelMain[i].ToList() : _panelMain[i].OrderBy(x => x).ToList();
                int sp   = _panelSpecial[i];
                string time = _panelTimeStr[i];
                string display = string.Join(", ", nums) + (sp > 0 ? $" +{sp}" : "") + (time.Length == 3 ? $" [{time}]" : "");
                return (idx: i, label: (char)('A' + i), nums, sp, time, display);
            })
            .ToList();

        if (allReady.Count == 0)
        {
            string timeNote = game.HasTime ? " + 3-digit race time" : "";
            await DisplayAlert("No Complete Panels", $"Save at least one complete panel ({game.MainPick} numbers{(game.SpecialPick > 0 ? $" + {game.SpecialName}" : "")}{timeNote}) first.", "OK");
            return;
        }
        if (pickerSet.SelectedIndex < 0)
        {
            await DisplayAlert("Choose a Set", "Select a destination set first.", "OK");
            return;
        }

        var options = allReady.Select(p => $"Panel {p.label}  ({p.display})").ToList();
        if (allReady.Count > 1)
            options.Add($"All Panels  ({string.Join(", ", allReady.Select(p => p.label))})");

        string? choice = await DisplayActionSheet("Which panel(s) to insert?", "Cancel", null, options.ToArray());
        if (choice == null || choice == "Cancel") return;

        var toInsert = choice.StartsWith("All")
            ? allReady
            : allReady.Where(p => choice.Length > 6 && p.label == choice[6]).ToList();

        int slot    = pickerSet.SelectedIndex;
        int setCols = game.SetCols;
        string key  = $"{game.SetPrefix}{slot}";
        string existing = Preferences.Get(key, "");
        string[] vals;
        if (string.IsNullOrEmpty(existing))
        { vals = new string[10 * setCols]; Array.Fill(vals, ""); }
        else
        {
            vals = existing.Split('|');
            if (vals.Length < 10 * setCols) Array.Resize(ref vals, 10 * setCols);
        }

        // Find first empty row
        int startRow = 0;
        for (int r = 0; r < 10; r++)
        {
            bool rowEmpty = Enumerable.Range(0, setCols).All(c => string.IsNullOrWhiteSpace(vals[r * setCols + c]));
            if (rowEmpty) { startRow = r; break; }
            if (r == 9) startRow = 0;
        }

        string preview = string.Join("\n", toInsert.Select(p =>
            $"  Panel {p.label}: {string.Join("  ", p.nums.Select(n => n.ToString("D2")))}{(p.sp > 0 ? $" +{p.sp:D2}" : "")}{(p.time.Length == 3 ? $" [{p.time}]" : "")}"));

        bool confirm = await DisplayAlert($"Insert into {game.Name}?",
            $"{preview}\n\n→ Set {slot + 1}, Row {startRow + 1}",
            "Insert", "Cancel");
        if (!confirm) return;

        int inserted = 0;
        int mainCols = game.MainPick;
        foreach (var panel in toInsert)
        {
            int r = startRow + inserted;
            if (r >= 10) break;
            for (int c = 0; c < mainCols; c++) vals[r * setCols + c] = panel.nums[c].ToString();
            if (game.SpecialPick > 0 && panel.sp > 0) vals[r * setCols + mainCols] = panel.sp.ToString();
            if (game.HasTime) vals[r * setCols + 3] = panel.time;
            inserted++;
        }

        Preferences.Set(key, string.Join("|", vals));
        Preferences.Set(game.ActiveSlotPrefKey, slot);

        bool goNow = await DisplayAlert("Done!",
            $"Inserted {inserted} panel(s) into {game.Name} Set {slot + 1}. Go there now?",
            "Go", "Stay");

        if (!goNow) return;

        switch (game.Short)
        {
            case "F5": AppShell.WinnerPageInstance.PrePosition(true);    await Shell.Current.GoToAsync(nameof(WinnerPage),     false); break;
            case "SL": SuperLottoPage.ComingFrom = "main"; AppShell.SuperLottoPageInstance.PrePosition(true); await Shell.Current.GoToAsync(nameof(SuperLottoPage), false); break;
            case "D3": Daily3Page.ComingFrom = "main"; AppShell.Daily3PageInstance.PrePosition(true);                   await Shell.Current.GoToAsync(nameof(Daily3Page),     false); break;
            case "D4": Daily4Page.ComingFrom = "main"; AppShell.Daily4PageInstance.PrePosition(true);                   await Shell.Current.GoToAsync(nameof(Daily4Page),     false); break;
            case "PB": PowerballPage.ComingFrom = "main"; AppShell.PowerballPageInstance.PrePosition(true);              await Shell.Current.GoToAsync(nameof(PowerballPage),  false); break;
            case "DD": DailyDerbyPage.ComingFrom = "main"; AppShell.DailyDerbyPageInstance.PrePosition(true);           await Shell.Current.GoToAsync(nameof(DailyDerbyPage), false); break;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    static Microsoft.Maui.Graphics.Color MColor(string hex) =>
        Microsoft.Maui.Graphics.Color.FromArgb(hex);

    async void BtnBack_Clicked(object sender, EventArgs e) => await Shell.Current.GoToAsync("..");

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_cropOpen) { /* keep state across visits */ }
    }
}
