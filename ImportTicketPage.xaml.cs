namespace DailyFantasyMAUI;

public partial class ImportTicketPage : ContentPage
{
    const long MaxFileBytes = 2 * 1024 * 1024; // ticket lists are tiny; anything bigger isn't one
    static readonly TimeSpan ParseTimeout = TimeSpan.FromSeconds(10);

    ImportDataTicket.ParseResult? _parsed;
    string? _fileName;
    CancellationTokenSource? _cts;

    public ImportTicketPage()
    {
        InitializeComponent();
    }

    async void BtnChooseFile_Clicked(object sender, EventArgs e)
    {
        var result = await FilePicker.PickAsync(new PickOptions
        {
            PickerTitle = "Select a ticket .txt or .csv file",
            // A narrow MIME filter here hides whole storage sources (Downloads, other drives,
            // cloud providers) from Android's picker, so "*/*" stays in the list to keep every
            // location visible. Binary files (images, etc.) are still caught below by sniffing
            // the actual file content before it ever reaches the parser.
            FileTypes   = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "text/plain", "text/csv", "text/comma-separated-values", "*/*" } },
                { DevicePlatform.iOS,     new[] { "public.plain-text", "public.comma-separated-values-text" } },
                { DevicePlatform.WinUI,   new[] { ".txt", ".csv" } },
            })
        });
        if (result == null) return;

        loadingOverlay.IsVisible = true;
        btnCancelLoad.IsVisible = true;
        lblLoading.Text = "Reading file…";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            var info = new FileInfo(result.FullPath);
            if (!info.Exists) throw new Exception("Couldn't read that file.");
            if (info.Length > MaxFileBytes)
                throw new Exception("That file is too large to be a ticket list — pick a plain .txt/.csv file.");
            if (!await LooksLikeTextAsync(result.FullPath, token))
                throw new Exception("That doesn't look like a text file — pick a plain .txt/.csv file with your ticket rows, not a photo or other file type.");

            var parseTask = Task.Run(() => ImportDataTicket.ParseText(File.ReadAllText(result.FullPath)), token);
            var winner = await Task.WhenAny(parseTask, Task.Delay(ParseTimeout, token));
            if (token.IsCancellationRequested) return;
            if (winner != parseTask)
                throw new Exception("That file is taking too long to read — it may not be a valid ticket list.");

            _fileName = result.FileName;
            _parsed   = await parseTask;
            RenderPreview();
        }
        catch (OperationCanceledException)
        {
            // user pressed Cancel — just return to the idle state, nothing to show
        }
        catch (Exception ex)
        {
            await DisplayAlert("Import Error", ex.Message, "OK");
        }
        finally
        {
            loadingOverlay.IsVisible = false;
            btnCancelLoad.IsVisible = false;
            _cts = null;
        }
    }

    void BtnCancelLoad_Clicked(object sender, EventArgs e) => _cts?.Cancel();

    // Cheap sniff of the first few KB: a NUL byte or a high ratio of non-printable bytes
    // means this is binary (image, pdf, etc.), not a ticket list — bail before ever handing
    // it to the parser.
    static async Task<bool> LooksLikeTextAsync(string path, CancellationToken token)
    {
        var buf = new byte[8192];
        int read;
        using (var fs = File.OpenRead(path))
            read = await fs.ReadAsync(buf.AsMemory(0, buf.Length), token);
        if (read == 0) return true;

        int suspicious = 0;
        for (int i = 0; i < read; i++)
        {
            byte b = buf[i];
            if (b == 0) return false; // NUL byte — always binary
            bool printable = b is 9 or 10 or 13 || b is >= 32 and < 127 || b >= 128;
            if (!printable) suspicious++;
        }
        return suspicious < read / 10;
    }

    void RenderPreview()
    {
        stkEmptyState.IsVisible = false;
        lblResult.IsVisible = false;
        stkGames.Children.Clear();
        stkSkippedList.Children.Clear();

        var parsed = _parsed!;
        lblFileSummary.IsVisible = true;
        lblFileSummary.Text = $"{_fileName} — {parsed.TotalLinesRead} line(s) read, {parsed.TotalParsed} row(s) recognized";

        foreach (var game in parsed.Games)
            stkGames.Children.Add(BuildGameCard(game));

        stkSkipped.IsVisible = parsed.SkippedLines.Count > 0;
        if (parsed.SkippedLines.Count > 0)
        {
            lblSkippedHeader.Text = $"⚠ Skipped ({parsed.SkippedLines.Count}) — fix and re-import if needed:";
            foreach (var line in parsed.SkippedLines)
                stkSkippedList.Children.Add(new Label
                {
                    Text = line, FontSize = 12, TextColor = Color.FromArgb("#9CA3AF")
                });
        }

        btnInsert.IsVisible = true;
        btnInsert.IsEnabled = parsed.TotalParsed > 0;
        btnInsert.Text = parsed.TotalParsed > 0 ? $"Insert All {parsed.TotalParsed} Row(s)" : "Nothing to Insert";
    }

    Frame BuildGameCard(ImportDataTicket.ParsedGame game)
    {
        var list = new VerticalStackLayout { Spacing = 2 };
        foreach (var row in game.Rows)
            list.Children.Add(new Label
            {
                Text = FormatRow(game.Code, row),
                FontSize = 13, FontFamily = "Monospace",
                TextColor = Colors.White
            });

        return new Frame
        {
            BackgroundColor = Color.FromArgb("#1E2733"),
            BorderColor = Color.FromArgb("#374151"),
            CornerRadius = 10, Padding = new Thickness(12, 10),
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label
                    {
                        Text = $"{game.Name} — {game.Rows.Count} row(s)",
                        FontSize = 14, FontAttributes = FontAttributes.Bold,
                        TextColor = Color.FromArgb("#8B9DC3")
                    },
                    list
                }
            }
        };
    }

    static string FormatRow(string code, int[] nums) => code switch
    {
        "SL" or "PB" or "MM" =>
            string.Join(" ", nums.Take(5).Select(n => n.ToString("00"))) + "  |  " + nums[5].ToString("00"),
        "F5" or "DD" =>
            string.Join(" ", nums.Select(n => n.ToString("00"))),
        _ => string.Join(" ", nums), // D3 / D4 — single digits
    };

    async void BtnInsert_Clicked(object sender, EventArgs e)
    {
        if (_parsed == null || _parsed.TotalParsed == 0) return;

        btnInsert.IsEnabled = false;
        loadingOverlay.IsVisible = true;
        lblLoading.Text = "Inserting…";
        try
        {
            var insertResult = await ImportDataTicket.InsertAsync(_parsed);

            string inserted = insertResult.InsertedByGame.Count == 0
                ? "Nothing inserted."
                : string.Join("\n", insertResult.InsertedByGame.Select(kv => $"{kv.Key}: {kv.Value} row(s)"));
            string text = $"✔ Inserted:\n{inserted}";
            if (insertResult.SkippedLines.Count > 0)
                text += "\n\n" + string.Join("\n", insertResult.SkippedLines);

            lblResult.Text = text;
            lblResult.IsVisible = true;
            btnInsert.Text = "Inserted ✓";

            // Nothing left to import from this file — let the user pick a new one.
            btnChooseFile.Text = "📄  Choose Another File";
        }
        catch (Exception ex)
        {
            await DisplayAlert("Import Error", ex.Message, "OK");
            btnInsert.IsEnabled = true;
        }
        finally { loadingOverlay.IsVisible = false; }
    }

    async void BtnBack_Clicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..");

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Reset so the next visit starts fresh rather than showing a stale preview.
        _parsed = null;
        _fileName = null;
        stkEmptyState.IsVisible = true;
        lblFileSummary.IsVisible = false;
        lblResult.IsVisible = false;
        stkGames.Children.Clear();
        stkSkipped.IsVisible = false;
        stkSkippedList.Children.Clear();
        btnInsert.IsVisible = false;
        btnInsert.IsEnabled = false;
        btnChooseFile.Text = "📄  Choose File";
    }
}
