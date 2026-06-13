using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

#if ANDROID
using Android.Graphics;
using Xamarin.Google.MLKit.Vision.Common;
using MlText = Xamarin.Google.MLKit.Vision.Text;
using Xamarin.Google.MLKit.Vision.Text.Latin;
#endif

namespace DailyFantasyMAUI;

public partial class ScanTicketPage : ContentPage
{
    static readonly (string Label, int Cols, string Prefix, int Min, int Max)[] Games =
    [
        ("Fantasy 5  (5 nums, 1-39)",        5, "f5_set_", 1, 39),
        ("Super Lotto  (5+Mega, 1-47/1-27)", 6, "sl_set_", 1, 47),
        ("Daily 3  (3 nums, 0-9)",            3, "d3_set_", 0,  9),
        ("Daily 4  (4 nums, 0-9)",            4, "d4_set_", 0,  9),
        ("Daily Derby  (3 horses, 1-12)",     3, "dd_set_", 1, 12),
    ];

    int _gameIdx = 2;
    List<Entry[]> _playEntries = [];
    string _debugInfo = "";
    bool _initialized;
    bool _cropOpen;

    public ScanTicketPage()
    {
        InitializeComponent();
        foreach (var g in Games) pickerGame.Items.Add(g.Label);
        pickerGame.SelectedIndex = 2;
        for (int i = 1; i <= 10; i++) { pickerSet.Items.Add($"Set {i}"); pickerRow.Items.Add($"Row {i}"); }
        pickerSet.SelectedIndex = 0;
        pickerRow.SelectedIndex = 0;
    }

    void Game_Changed(object sender, EventArgs e)
    {
        _gameIdx = Math.Max(0, pickerGame.SelectedIndex);
        ShowEditableRows([]);
        UpdateInsertButton();
    }

    void ShowEditableRows(List<List<int>> plays)
    {
        int cols     = Games[_gameIdx].Cols;
        int numPlays = plays.Count > 0 ? plays.Count : 1;

        stkMultiEntries.Children.Clear();
        _playEntries.Clear();

        for (int p = 0; p < numPlays; p++)
        {
            var play       = plays.Count > p ? plays[p] : new List<int>();
            var rowEntries = new Entry[cols];
            var hstack     = new HorizontalStackLayout { Spacing = 6, HorizontalOptions = LayoutOptions.Center };

            if (numPlays > 1)
                hstack.Children.Add(new Label
                {
                    Text = $"{(char)('A' + p)}:",
                    FontSize = 14, FontAttributes = FontAttributes.Bold,
                    TextColor = Microsoft.Maui.Graphics.Color.FromArgb("#9CA3AF"),
                    VerticalOptions = LayoutOptions.Center, WidthRequest = 22
                });

            for (int c = 0; c < cols; c++)
            {
                bool isMega = (_gameIdx == 1 && c == 5);
                int  pc = p, cc = c;
                var entry = new Entry
                {
                    WidthRequest = 50, HeightRequest = 50, MaxLength = 2,
                    Keyboard = Keyboard.Numeric, TextColor = Colors.White,
                    BackgroundColor = isMega
                        ? Microsoft.Maui.Graphics.Color.FromArgb("#4A2060")
                        : Microsoft.Maui.Graphics.Color.FromArgb("#374151"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    FontSize = 20, FontAttributes = FontAttributes.Bold,
                    Text = (play.Count > c) ? play[c].ToString() : ""
                };
                entry.TextChanged += (s, ev) =>
                {
                    UpdateInsertButton();
                    if ((entry.Text?.Length ?? 0) >= 2)
                    {
                        if (cc + 1 < cols) _playEntries[pc][cc + 1].Focus();
                        else if (pc + 1 < _playEntries.Count) _playEntries[pc + 1][0].Focus();
                    }
                };
                rowEntries[c] = entry;
                hstack.Children.Add(entry);
            }

            _playEntries.Add(rowEntries);
            stkMultiEntries.Children.Add(hstack);
        }

        stkMultiEntries.IsVisible = true;
        lblEditHint.IsVisible = plays.Count > 0;
        lblEditHint.Text = plays.Count > 0
            ? "Check numbers — tap any box to correct."
            : "Enter your numbers in the boxes above.";
        UpdateInsertButton();
    }

    async void BtnCapture_Clicked(object sender, EventArgs e)
    {
        btnCapture.IsEnabled = false;
        loadingOverlay.IsVisible = true;
        lblLoading.Text = "Opening camera…";
        try
        {
            var photo = await MediaPicker.CapturePhotoAsync(
                new MediaPickerOptions { Title = "Get CLOSE — fill frame with A/B/C number rows only" });
            if (photo == null) { loadingOverlay.IsVisible = false; btnCapture.IsEnabled = true; return; }

            loadingOverlay.IsVisible = false;
            CropPage.Result = new TaskCompletionSource<string?>();
            _cropOpen = true;
            await Navigation.PushModalAsync(new CropPage(photo.FullPath), false);
            _cropOpen = false;
            string? croppedPath = await CropPage.Result.Task;
            if (croppedPath == null) { btnCapture.IsEnabled = true; return; }

            loadingOverlay.IsVisible = true;
            lblLoading.Text = "Reading numbers…";
            var plays = await ParsePlaysFromPhoto(croppedPath);
            loadingOverlay.IsVisible = false;

            btnToggleOcr.IsVisible = true;
            btnToggleOcr.Text = "Show Scanned Image";
            frmOcrPreview.IsVisible = false;
            imgOcrPreview.Source = null;
            string debugPath = System.IO.Path.Combine(FileSystem.CacheDirectory, "ocr_debug.jpg");
            if (System.IO.File.Exists(debugPath))
                imgOcrPreview.Source = ImageSource.FromFile(debugPath);

            string cropDbg = CropPage.LastCropDebug;
            if (plays.Count > 0)
            {
                lblStatus.Text = $"Found {plays.Count} play(s). {_debugInfo} | {cropDbg}";
                ShowEditableRows(plays);
            }
            else
            {
                lblStatus.Text = (_debugInfo.Length > 0 ? _debugInfo : "Could not read numbers.") + $" | {cropDbg}";
                ShowEditableRows([]);
            }
        }
        catch (Exception ex)
        {
            loadingOverlay.IsVisible = false;
            lblStatus.Text = $"Error: {ex.Message}";
        }
        btnCapture.IsEnabled = true;
    }

    void BtnToggleOcr_Clicked(object sender, EventArgs e)
    {
        frmOcrPreview.IsVisible = !frmOcrPreview.IsVisible;
        btnToggleOcr.Text = frmOcrPreview.IsVisible ? "Hide Scanned Image" : "Show Scanned Image";
    }

    async Task<List<List<int>>> ParsePlaysFromPhoto(string imagePath)
    {
        _debugInfo = "";
#if ANDROID
        try
        {
            // Pass 1: Natural Direct
            var lines = await GetOcrLinesMlKit(imagePath, -1);
            var plays = ProcessOcrLines(lines ?? []);
            if (plays.Count > 0) return plays;

            // Pass 2: Normalized
            lines = await GetOcrLinesMlKit(imagePath, 0);
            plays = ProcessOcrLines(lines ?? []);
            if (plays.Count > 0) { _debugInfo = "P2:" + _debugInfo; return plays; }

            // Pass 3: Surgical
            lines = await GetOcrLinesMlKit(imagePath, 1);
            plays = ProcessOcrLines(lines ?? []);
            if (plays.Count > 0) { _debugInfo = "P3:" + _debugInfo; return plays; }

            // Pass 4: Color filter + downscale + binarize
            lines = await GetOcrLinesMlKit(imagePath, 2);
            plays = ProcessOcrLines(lines ?? []);
            if (plays.Count > 0) { _debugInfo = "P4:" + _debugInfo; return plays; }

            // Pass 5: OCR each row strip independently, one play per strip
            plays = await ParsePlaysFromStrips(imagePath);
            if (plays.Count > 0) _debugInfo = "P5:" + _debugInfo;

            return plays;
        }
        catch (Exception ex) { _debugInfo = $"OCR Error: {ex.Message}"; return []; }
#else
        return [];
#endif
    }

#if ANDROID
    static Bitmap? PreprocessBitmap(string imagePath, int mode)
    {
        var bmp = BitmapFactory.DecodeFile(imagePath);
        if (bmp == null) return null;

        int w = bmp.Width, h = bmp.Height;

        // Mode 2: color-aware pass — remove orange/colored elements (starburst, paper)
        // by turning saturated pixels white, keeping only near-black ink (numbers + lines),
        // then downscale so thin wavy lines vanish as sub-pixel noise.
        if (mode >= 2)
        {
            var pixels = new int[w * h];
            bmp.GetPixels(pixels, 0, w, 0, 0, w, h);
            bmp.Recycle();

            for (int i = 0; i < pixels.Length; i++)
            {
                int px = pixels[i];
                int r = (px >> 16) & 0xFF;
                int g = (px >> 8)  & 0xFF;
                int b =  px        & 0xFF;
                int maxC = Math.Max(r, Math.Max(g, b));
                int minC = Math.Min(r, Math.Min(g, b));
                int gray = (maxC - minC) > 30
                    ? 255                                                   // colored → white
                    : (int)(0.299 * r + 0.587 * g + 0.114 * b);            // near-gray → keep
                pixels[i] = unchecked((int)0xFF000000) | (gray << 16) | (gray << 8) | gray;
            }

            var proc2 = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!);
            proc2.SetPixels(pixels, 0, w, 0, 0, w, h);

            if (w > 400)
            {
                float s = 400f / w;
                int sw = 400, sh = Math.Max(1, (int)(h * s));
                var small2 = Bitmap.CreateScaledBitmap(proc2, sw, sh, true)!;
                proc2.Recycle();
                proc2 = small2;
            }

            // Binarize after downscale: pure black numbers, pure white everything else
            {
                int pw = proc2.Width, ph = proc2.Height;
                var th = new int[pw * ph];
                proc2.GetPixels(th, 0, pw, 0, 0, pw, ph);
                for (int i = 0; i < th.Length; i++)
                {
                    int g = (th[i] >> 16) & 0xFF;
                    th[i] = g < 160 ? unchecked((int)0xFF000000) : unchecked((int)0xFFFFFFFF);
                }
                proc2.SetPixels(th, 0, pw, 0, 0, pw, ph);
            }

            try
            {
                string dbg2 = System.IO.Path.Combine(FileSystem.CacheDirectory, "ocr_debug.jpg");
                using var fs2 = System.IO.File.Create(dbg2);
                proc2.Compress(Bitmap.CompressFormat.Jpeg, 90, fs2);
            }
            catch { }
            return proc2;
        }

        float targetH = 400f;
        float targetW = 2000f;
        float scale = (h < (w * 0.4)) ? (targetH / h) : (targetW / Math.Max(w, h));

        if (scale < 0.6f || scale > 1.4f)
        {
            var scaled = Bitmap.CreateScaledBitmap(bmp, (int)(w * scale), (int)(h * scale), true)!;
            bmp.Recycle();
            bmp = scaled;
            w = bmp.Width; h = bmp.Height;
        }

        var proc = Bitmap.CreateBitmap(w, h, Bitmap.Config.Argb8888!);
        using (var canvas = new Android.Graphics.Canvas(proc))
        {
            using var paint = new Android.Graphics.Paint();
            var cm = new Android.Graphics.ColorMatrix();
            cm.SetSaturation(0);

            if (mode == 1)
            {
                float contrast = 2.2f, brightness = 1.2f;
                float translate = 128f * (1f - contrast) + (brightness - 1f) * 255f;
                var matrix = new Android.Graphics.ColorMatrix(new float[]
                    { contrast,0,0,0,translate, 0,contrast,0,0,translate, 0,0,contrast,0,translate, 0,0,0,1,0 });
                cm.PostConcat(matrix);
            }

            paint.SetColorFilter(new Android.Graphics.ColorMatrixColorFilter(cm));
            canvas.DrawBitmap(bmp, 0, 0, paint);
        }
        bmp.Recycle();

        try
        {
            string dbg = System.IO.Path.Combine(FileSystem.CacheDirectory, "ocr_debug.jpg");
            using var fs = System.IO.File.Create(dbg);
            proc.Compress(Bitmap.CompressFormat.Jpeg, 90, fs);
        }
        catch { }
        return proc;
    }

    // Pass 5: color-filter + binarize each strip inline at full resolution,
    // then downscale width to 600px (strips stay ~50px tall — above MLKit's 32px minimum).
    async Task<List<List<int>>> ParsePlaysFromStrips(string imagePath)
    {
        int cols = Games[_gameIdx].Cols;
        // Use 4 strips instead of 3: the crop often has dead space at top,
        // so equal thirds miss row C. 4 strips gives more overlap coverage;
        // we collect all strips with numbers and keep the best cols-count ones.
        int stripCount = cols > 3 ? 4 : 1;
        var plays = new List<List<int>>();

        var strips = await Task.Run(() =>
        {
            var bmp = BitmapFactory.DecodeFile(imagePath);
            if (bmp == null) return null;
            int bw = bmp.Width, bh = bmp.Height;

            var list = new List<Bitmap>();
            for (int i = 0; i < stripCount; i++)
            {
                int y0 = i * bh / stripCount;
                int sh = (i + 1) * bh / stripCount - y0;
                // Use raw strip — no preprocessing. Let MLKit handle the full-color image.
                // Strips at native resolution avoid scaling artifacts and keep number strokes intact.
                list.Add(Bitmap.CreateBitmap(bmp, 0, y0, bw, sh)!);
            }
            bmp.Recycle();
            return list;
        });

        if (strips == null) return plays;

        int minNeeded = cols > 3 ? 3 : 1;
        var dbg = new List<string>();
        foreach (var strip in strips)
        {
            try
            {
                var img = InputImage.FromBitmap(strip, 0);
                var lines = await RunRecognizer(img);
                var text = string.Join(" ", lines.OrderBy(l => l.left).Select(l => l.text));
                var nums = ExtractNums(text);
                if (nums.Count >= minNeeded)
                {
                    var play = nums.Count > cols ? nums.GetRange(0, cols) : nums;
                    plays.Add(play);
                    dbg.Add($"[{string.Join(",", play)}]");
                }
                else
                    dbg.Add(text.Length > 0 ? $"t:{text[..Math.Min(12, text.Length)]}" : "_");
            }
            finally { strip.Recycle(); }
        }

        // From 4 strips, keep up to 3 plays sorted by number count (most complete first),
        // skipping near-duplicates (same first number = same row captured twice).
        int maxRows = cols > 3 ? 3 : 1;
        var deduped = plays
            .OrderByDescending(p => p.Count)
            .GroupBy(p => p[0])
            .Select(g => g.First())
            .Take(maxRows)
            .OrderBy(p => plays.IndexOf(p))   // restore original strip order
            .ToList();

        _debugInfo = $"fnd:strips rows:{deduped.Count} samples:\"{string.Join("|", dbg)}\"";
        return deduped;
    }

    // Pass 5: OCR each horizontal strip independently.
    // Uses a large fake Y gap between strips so ProcessOcrLines never merges across strips.
    async Task<List<(int top, int cy, int h, int left, string text)>?> GetOcrLinesSplit(string imagePath)
    {
        int cols = Games[_gameIdx].Cols;
        int stripCount = cols > 3 ? 3 : 1;
        var allLines = new List<(int top, int cy, int h, int left, string text)>();

        var strips = await Task.Run(() =>
        {
            var bmp = BitmapFactory.DecodeFile(imagePath);
            if (bmp == null) return null;
            int bw = bmp.Width, bh = bmp.Height;
            var list = new List<Bitmap>();
            for (int i = 0; i < stripCount; i++)
            {
                int y0 = i * bh / stripCount;
                int sh = (i + 1) * bh / stripCount - y0;
                list.Add(Bitmap.CreateBitmap(bmp, 0, y0, bw, sh)!);
            }
            bmp.Recycle();
            return list;
        });

        if (strips == null) return null;

        // Use a large cy gap (10000) per strip so ProcessOcrLines always puts
        // each strip in its own row bucket, never merging across strips.
        const int CY_STRIDE = 10000;

        for (int i = 0; i < strips.Count; i++)
        {
            try
            {
                var img = InputImage.FromBitmap(strips[i], 0);
                var lines = await RunRecognizer(img);
                foreach (var l in lines)
                    allLines.Add((l.top + i * CY_STRIDE, l.cy + i * CY_STRIDE, l.h, l.left, l.text));
            }
            finally { strips[i].Recycle(); }
        }

        return allLines;
    }

    async Task<List<(int top, int cy, int h, int left, string text)>?> GetOcrLinesMlKit(string imagePath, int mode)
    {
        if (mode == -1) 
        {
            try {
                var file = new Java.IO.File(imagePath);
                var inputImage = InputImage.FromFilePath(Android.App.Application.Context, Android.Net.Uri.FromFile(file)!);
                return await RunRecognizer(inputImage);
            } catch { return null; }
        }

        Bitmap? bitmap = await Task.Run(() => PreprocessBitmap(imagePath, mode));
        if (bitmap == null) return null;
        try {
            var inputImage = InputImage.FromBitmap(bitmap, 0);
            return await RunRecognizer(inputImage);
        } finally { bitmap.Recycle(); }
    }

    async Task<List<(int top, int cy, int h, int left, string text)>> RunRecognizer(InputImage image)
    {
        var recognizer = MlText.TextRecognition.GetClient(TextRecognizerOptions.DefaultOptions);
        var tcs        = new TaskCompletionSource<MlText.Text>();
        var mlTask     = recognizer.Process(image);
        mlTask.AddOnSuccessListener(new MlSuccess<MlText.Text>(tcs));
        mlTask.AddOnFailureListener(new MlFailure<MlText.Text>(tcs));
        var result = await tcs.Task;

        var lines = new List<(int top, int cy, int h, int left, string text)>();
        if (result?.TextBlocks == null) return lines;
        foreach (var block in result.TextBlocks)
            foreach (var line in block.Lines)
            {
                var box = line.BoundingBox;
                if (box == null) continue;
                lines.Add((box.Top, (box.Top + box.Bottom) / 2, box.Bottom - box.Top, box.Left, line.Text ?? ""));
            }
        return lines;
    }

    List<List<int>> ProcessOcrLines(List<(int top, int cy, int h, int left, string text)> ocrLines)
    {
        if (ocrLines.Count == 0) return [];
        ocrLines.Sort((a, b) => a.cy.CompareTo(b.cy));

        var rows = new List<List<(int top, int cy, int h, int left, string text)>>();
        foreach (var line in ocrLines)
        {
            float thresh = Math.Max(line.h * 1.5f, 60f);
            var existingRow = rows.FirstOrDefault(r => Math.Abs(r[0].cy - line.cy) < thresh);
            if (existingRow != null) existingRow.Add(line);
            else rows.Add([line]);
        }

        var plays = new List<List<int>>();
        var dbgTxt = new List<string>();
        int minNeeded = Games[_gameIdx].Cols > 3 ? 3 : 1; 

        foreach (var row in rows.OrderBy(r => r[0].cy))
        {
            var text = string.Join(" ", row.OrderBy(l => l.left).Select(l => l.text));
            var nums = ExtractNums(text);
            if (nums.Count >= minNeeded)
            {
                plays.Add(nums);
                if (dbgTxt.Count < 2) dbgTxt.Add("OK:" + (text.Length > 20 ? text[..20] : text));
            }
            else if (!string.IsNullOrWhiteSpace(text) && dbgTxt.Count < 2)
            {
                dbgTxt.Add("RAW:" + (text.Length > 20 ? text[..20] : text));
            }
        }

        _debugInfo = $"fnd:{ocrLines.Count} rows:{plays.Count} samples:\"{string.Join("|", dbgTxt)}\"";
        return plays;
    }

    List<int> ExtractNums(string text)
    {
        var nums = new List<int>();
        int min = Games[_gameIdx].Min, max = Games[_gameIdx].Max;

        string clean = text.ToUpper()
            .Replace("(", "").Replace(")", "").Replace("[", "").Replace("]", "").Replace("{", "").Replace("}", "")
            .Replace("$", "").Replace("|", "").Replace("!", "")
            .Replace("O", "0").Replace("I", "1").Replace("L", "1").Replace("Z", "2").Replace("S", "5");

        if (max > 9)
        {
            string digitOnly = Regex.Replace(clean, @"\s+", "");
            if (digitOnly.Length >= 2) clean = digitOnly;
        }

        foreach (Match m in Regex.Matches(clean, @"\d+"))
        {
            string raw = m.Value;
            if (raw.Length > 20) continue;

            if (max <= 9)
            {
                foreach (char c in raw)
                    if (int.TryParse(c.ToString(), out int n) && n >= min && n <= max)
                        nums.Add(n);
            }
            else
            {
                int i = 0;
                while (i < raw.Length)
                {
                    if (i + 1 < raw.Length)
                    {
                        if (int.TryParse(raw.Substring(i, 2), out int n) && n >= min && n <= max)
                        {
                            nums.Add(n); i += 2; continue;
                        }
                    }
                    if (int.TryParse(raw.Substring(i, 1), out int n1) && n1 >= min && n1 <= max)
                        nums.Add(n1);
                    i++;
                }
            }
        }
        return nums;
    }
#endif

    void UpdateInsertButton()
    {
        bool ok = _playEntries.Count > 0 &&
                  _playEntries.All(row => row.All(e => e == null || !string.IsNullOrWhiteSpace(e.Text)));
        btnInsert.IsEnabled = ok;
        btnInsert.Text = _playEntries.Count > 1
            ? $"Insert {_playEntries.Count} Plays into Set"
            : "Insert Numbers into Set";
    }

    async void BtnInsert_Clicked(object sender, EventArgs e)
    {
        var game  = Games[_gameIdx];
        int slot  = Math.Max(0, pickerSet.SelectedIndex);
        string slotKey  = $"{game.Prefix}{slot}";
        string existing = Preferences.Get(slotKey, "");

        var rowsToInsert = _playEntries
            .Select(row => row.Select(en => en.Text?.Trim() ?? "").ToArray())
            .ToList();

        string preview = rowsToInsert.Count == 1
            ? string.Join("  ", rowsToInsert[0])
            : string.Join("\n", rowsToInsert.Select((r, i) => $"{(char)('A'+i)}: {string.Join(" ", r)}"));

        // Daily Derby: 4-per-row format (h1, h2, h3, time) — preserve existing race times
        if (_gameIdx == 4)
        {
            string[] vals;
            if (string.IsNullOrEmpty(existing))
            { vals = new string[40]; Array.Fill(vals, ""); }
            else
            {
                vals = existing.Split('|');
                if (vals.Length < 40) Array.Resize(ref vals, 40);
            }

            int startRow = Math.Max(0, pickerRow.SelectedIndex);
            for (int r = startRow; r < 10; r++)
            {
                if (string.IsNullOrWhiteSpace(vals[r * 4]) &&
                    string.IsNullOrWhiteSpace(vals[r * 4 + 1]) &&
                    string.IsNullOrWhiteSpace(vals[r * 4 + 2]))
                { startRow = r; break; }
            }

            bool confirm = await DisplayAlert("Insert Numbers?",
                $"{preview}\n\nInsert into Set {slot + 1}, Row {startRow + 1}?",
                "Insert", "Cancel");
            if (!confirm) return;

            int inserted = 0;
            foreach (var row in rowsToInsert)
            {
                int r = startRow + inserted;
                if (r >= 10) break;
                vals[r * 4 + 0] = row[0];
                vals[r * 4 + 1] = row[1];
                vals[r * 4 + 2] = row[2];
                // vals[r*4+3] = time — leave unchanged
                inserted++;
            }

            Preferences.Set(slotKey, string.Join("|", vals));
            Preferences.Set("dd_active_slot", slot);

            bool goNow = await DisplayAlert("Done!",
                $"Inserted {inserted} play(s) into Daily Derby.\nGo there now?", "Go", "Stay");
            if (!goNow) { ShowEditableRows([]); lblStatus.Text = ""; return; }

            DailyDerbyPage.ComingFrom = "main";
            AppShell.DailyDerbyPageInstance.PrePosition(true);
            await Shell.Current.GoToAsync(nameof(DailyDerbyPage), false);
            return;
        }

        int cols      = game.Cols;
        int totalCells = 10 * cols;
        string[] valsStd;

        if (string.IsNullOrEmpty(existing))
        { valsStd = new string[totalCells]; Array.Fill(valsStd, ""); }
        else
        {
            valsStd = existing.Split('|');
            if (valsStd.Length < totalCells) Array.Resize(ref valsStd, totalCells);
        }

        int startRowStd = Math.Max(0, pickerRow.SelectedIndex);
        for (int r = startRowStd; r < 10; r++)
        {
            bool empty = true;
            for (int c = 0; c < cols; c++)
                if (!string.IsNullOrWhiteSpace(valsStd[r * cols + c])) { empty = false; break; }
            if (empty) { startRowStd = r; break; }
        }

        bool confirmStd = await DisplayAlert("Insert Numbers?",
            $"{preview}\n\nInsert into Set {slot + 1}, Row {startRowStd + 1}?",
            "Insert", "Cancel");
        if (!confirmStd) return;

        int insertedStd = 0;
        foreach (var row in rowsToInsert)
        {
            int r = startRowStd + insertedStd;
            if (r >= 10) break;
            for (int c = 0; c < cols; c++) valsStd[r * cols + c] = row[c];
            insertedStd++;
        }

        Preferences.Set(slotKey, string.Join("|", valsStd));
        if (_gameIdx == 0) Preferences.Set("f5_active_slot", slot);

        string gameName = _gameIdx switch { 0 => "F5 Winner", 1 => "Super Lotto", 3 => "Daily 4", _ => "Daily 3" };
        bool goNowStd = await DisplayAlert("Done!",
            $"Inserted {insertedStd} play(s) into {gameName}.\nGo there now?", "Go", "Stay");

        if (!goNowStd) { ShowEditableRows([]); lblStatus.Text = ""; return; }

        if (_gameIdx == 0)
        {
            AppShell.WinnerPageInstance.PrePosition(true);
            await Shell.Current.GoToAsync(nameof(WinnerPage), false);
        }
        else if (_gameIdx == 1)
        {
            SuperLottoPage.ComingFrom = "main";
            AppShell.SuperLottoPageInstance.PrePosition(true);
            await Shell.Current.GoToAsync(nameof(SuperLottoPage), false);
        }
        else if (_gameIdx == 3)
        {
            Daily4Page.ComingFrom = "main";
            AppShell.Daily4PageInstance.PrePosition(true);
            await Shell.Current.GoToAsync(nameof(Daily4Page), false);
        }
        else
        {
            Daily3Page.ComingFrom = "main";
            AppShell.Daily3PageInstance.PrePosition(true);
            await Shell.Current.GoToAsync(nameof(Daily3Page), false);
        }
    }

    async void BtnBack_Clicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..");

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized) return;
        _initialized = true;

        var status = await Permissions.RequestAsync<Permissions.Camera>();      
        if (status != PermissionStatus.Granted) { lblStatus.Text = "Camera permission required."; return; }

        string game = await DisplayActionSheet("Which ticket are you scanning?",
            "Cancel", null, "Daily 3", "Daily 4", "Daily Derby", "Fantasy 5", "Super Lotto");

        if      (game == "Daily 3")     { _gameIdx = 2; pickerGame.SelectedIndex = 2; }
        else if (game == "Daily 4")     { _gameIdx = 3; pickerGame.SelectedIndex = 3; }
        else if (game == "Daily Derby") { _gameIdx = 4; pickerGame.SelectedIndex = 4; }
        else if (game == "Fantasy 5")   { _gameIdx = 0; pickerGame.SelectedIndex = 0; }
        else if (game == "Super Lotto") { _gameIdx = 1; pickerGame.SelectedIndex = 1; }

        lblStatus.Text = "";
        ShowEditableRows([]);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (!_cropOpen) _initialized = false;
    }

}

#if ANDROID
sealed class MlSuccess<T>(TaskCompletionSource<T> tcs)
    : Java.Lang.Object, Android.Gms.Tasks.IOnSuccessListener
    where T : Java.Lang.Object
{
    public void OnSuccess(Java.Lang.Object? result) => tcs.TrySetResult((T)result!);
}

sealed class MlFailure<T>(TaskCompletionSource<T> tcs)
    : Java.Lang.Object, Android.Gms.Tasks.IOnFailureListener
    where T : Java.Lang.Object
{
    public void OnFailure(Java.Lang.Exception? e) =>
        tcs.TrySetException(new Exception(e?.Message ?? "ML Kit error"));       
}
#endif
