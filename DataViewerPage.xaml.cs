namespace DailyFantasyMAUI;

public partial class DataViewerPage : ContentPage
{
    record FileEntry(string Label, string Path);

    List<FileEntry> _files = [];
    string[] _allLines = [];

    public DataViewerPage() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadFileList();
    }

    void LoadFileList()
    {
        var appDir = FileSystem.AppDataDirectory;
        _files =
        [
            new("Fantasy 5 CSV",    Path.Combine(appDir, "data", "myFantasy5.csv")),
            new("SuperLotto CSV",   Path.Combine(appDir, "data", "mySuperlotto.csv")),
            new("My Combos",        Path.Combine(appDir, "myCombos.txt")),
            new("Archives",         Path.Combine(appDir, "archives.json")),
            new("Fantasy 5 Data",   Path.Combine(appDir, "fantasy5", "fantasy5.txt")),
            new("Error Log",        Path.Combine(appDir, "error_log.txt")),
        ];

        filePicker.Items.Clear();
        foreach (var f in _files)
            filePicker.Items.Add(f.Label + (File.Exists(f.Path) ? "" : " (missing)"));

        _allLines = [];
        lineList.ItemsSource = null;
        lblLineCount.Text = "";
    }

    async void FilePicker_SelectedIndexChanged(object sender, EventArgs e)
    {
        int idx = filePicker.SelectedIndex;
        if (idx < 0) return;

        var entry = _files[idx];
        lblTitle.Text = entry.Label;

        if (!File.Exists(entry.Path))
        {
            _allLines = ["(file not found)"];
            ShowLines(_allLines);
            return;
        }

        loadingPanel.IsVisible = true;
        spinner.IsRunning = true;
        lineList.ItemsSource = null;

        try
        {
            _allLines = await Task.Run(() => File.ReadAllLines(entry.Path));
            ShowLines(_allLines);
        }
        catch (Exception ex)
        {
            _allLines = [$"Error: {ex.Message}"];
            ShowLines(_allLines);
        }
        finally
        {
            loadingPanel.IsVisible = false;
            spinner.IsRunning = false;
        }
    }

    void ShowLines(string[] lines)
    {
        lineList.ItemsSource = lines;
        lblLineCount.Text = $"{lines.Length:N0} lines";
    }

    void BtnFilter_Clicked(object sender, EventArgs e)
    {
        if (_allLines.Length == 0) return;

        var d = datePicker.Date ?? DateTime.Today;
        string monthDay = $"-{d.Month:D2}-{d.Day:D2}";

        // Skip header, match month-day in any year, sort by date ascending
        var header = _allLines.Length > 0 && !_allLines[0].StartsWith("19") && !_allLines[0].StartsWith("20")
            ? _allLines[0] : null;

        var filtered = _allLines
            .Skip(header != null ? 1 : 0)
            .Where(l => l.Length >= 10 && l.Substring(4, 6) == monthDay)
            .OrderBy(l => l)
            .ToArray();

        if (filtered.Length == 0)
        {
            ShowLines([$"No results for {d:MMMM d}"]);
            return;
        }

        var result = header != null
            ? new[] { header }.Concat(filtered).ToArray()
            : filtered;

        lblLineCount.Text = $"{filtered.Length} match(es) for {d:MMMM d}";
        lineList.ItemsSource = result;
    }

    void BtnShowAll_Clicked(object sender, EventArgs e)
    {
        if (_allLines.Length == 0) return;
        ShowLines(_allLines);
    }

    void BtnTop_Clicked(object sender, EventArgs e)
    {
        if (lineList.ItemsSource is string[] lines && lines.Length > 0)
            lineList.ScrollTo(0, animate: false);
    }

    void BtnBottom_Clicked(object sender, EventArgs e)
    {
        if (lineList.ItemsSource is string[] lines && lines.Length > 0)
            lineList.ScrollTo(lines.Length - 1, animate: false);
    }

    async void BtnBack_Clicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..", false);
}
