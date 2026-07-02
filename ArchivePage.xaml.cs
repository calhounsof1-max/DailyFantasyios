using DailyFantasyMAUI.Services;
using Microsoft.Maui.Controls.Shapes;

namespace DailyFantasyMAUI;

public partial class ArchivePage : ContentPage
{
    List<ArchiveEntry> _archives = [];

    public ArchivePage() => InitializeComponent();

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Refresh();
    }

    void Refresh()
    {
        _archives = ArchiveService.Load();
        archiveList.Children.Clear();

        if (_archives.Count == 0)
        {
            lblEmpty.IsVisible = true;
            return;
        }

        lblEmpty.IsVisible = false;

        foreach (var entry in _archives)
            archiveList.Children.Add(BuildCard(entry));
    }

    View BuildCard(ArchiveEntry entry)
    {
        // Count non-empty slots per game for summary
        int f5 = ArchiveService.CountNonEmpty(entry, "f5");
        int sl = ArchiveService.CountNonEmpty(entry, "sl");
        int pb = ArchiveService.CountNonEmpty(entry, "pb");
        int mm = ArchiveService.CountNonEmpty(entry, "mm");
        int d3 = ArchiveService.CountNonEmpty(entry, "d3");
        int d4 = ArchiveService.CountNonEmpty(entry, "d4");
        int total = f5 + sl + pb + mm + d3 + d4;

        var dateLabel = new Label
        {
            Text = entry.Date.ToString("MMM d, yyyy  h:mm tt"),
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#1E2733")
        };

        var summaryLabel = new Label
        {
            Text = $"F5: {f5}  SL: {sl}  PB: {pb}  MM: {mm}  D3: {d3}  D4: {d4}   ({total} total sets)",
            FontSize = 12,
            TextColor = Color.FromArgb("#546E7A"),
            Margin = new Thickness(0, 3, 0, 10)
        };

        var btnRestore = new Button
        {
            Text = "Restore",
            FontSize = 13,
            BackgroundColor = Color.FromArgb("#1565C0"),
            TextColor = Colors.White,
            CornerRadius = 18,
            HeightRequest = 36,
            Padding = new Thickness(0),
            HorizontalOptions = LayoutOptions.Fill
        };
        btnRestore.Clicked += async (s, e) => await OnRestore(entry);

        var btnDelete = new Button
        {
            Text = "Delete",
            FontSize = 13,
            BackgroundColor = Color.FromArgb("#B71C1C"),
            TextColor = Colors.White,
            CornerRadius = 18,
            HeightRequest = 36,
            Padding = new Thickness(0),
            HorizontalOptions = LayoutOptions.Fill
        };
        btnDelete.Clicked += async (s, e) => await OnDelete(entry);

        var btnGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(new GridLength(10)),
                new ColumnDefinition(GridLength.Star))
        };
        btnGrid.Add(btnRestore, 0, 0);
        btnGrid.Add(btnDelete, 2, 0);

        var inner = new VerticalStackLayout { Spacing = 0 };
        inner.Add(dateLabel);
        inner.Add(summaryLabel);
        inner.Add(btnGrid);

        var card = new Border
        {
            BackgroundColor = Colors.White,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            StrokeThickness = 0,
            Padding = new Thickness(16, 14),
            Margin = new Thickness(0, 0, 0, 10),
            Content = inner
        };

        return card;
    }

    async Task OnRestore(ArchiveEntry entry)
    {
        var action = await DisplayActionSheet(
            $"Restore from {entry.Date:MMM d, yyyy}",
            "Cancel", null,
            "All Games", "Fantasy 5", "Super Lotto", "Powerball", "Mega Millions", "Daily 3", "Daily 4");

        if (action == null || action == "Cancel") return;

        // Build preview of the numbers in this archive
        string preview = BuildRestorePreview(entry, action);
        bool confirm = await DisplayAlert($"Preview — {action}", preview, "Restore", "Cancel");
        if (!confirm) return;

        switch (action)
        {
            case "All Games":      ArchiveService.RestoreAll(entry); break;
            case "Fantasy 5":      ArchiveService.RestoreGame(entry, "f5"); break;
            case "Super Lotto":    ArchiveService.RestoreGame(entry, "sl"); break;
            case "Powerball":      ArchiveService.RestoreGame(entry, "pb"); break;
            case "Mega Millions":  ArchiveService.RestoreGame(entry, "mm"); break;
            case "Daily 3":        ArchiveService.RestoreGame(entry, "d3"); break;
            case "Daily 4":        ArchiveService.RestoreGame(entry, "d4"); break;
        }

        await DisplayAlert("Restored", $"{action} sets have been restored.", "OK");
    }

    static string BuildRestorePreview(ArchiveEntry entry, string action)
    {
        // map action → (prefix, cols)
        var games = action == "All Games"
            ? new[] { ("f5","Fantasy 5",5), ("sl","Super Lotto",6), ("pb","Powerball",6),
                      ("mm","Mega Millions",6), ("d3","Daily 3",3), ("d4","Daily 4",4) }
            : action switch
            {
                "Fantasy 5"     => new[] { ("f5","Fantasy 5",5) },
                "Super Lotto"   => new[] { ("sl","Super Lotto",6) },
                "Powerball"     => new[] { ("pb","Powerball",6) },
                "Mega Millions" => new[] { ("mm","Mega Millions",6) },
                "Daily 3"       => new[] { ("d3","Daily 3",3) },
                "Daily 4"       => new[] { ("d4","Daily 4",4) },
                _               => Array.Empty<(string,string,int)>()
            };

        var sb = new System.Text.StringBuilder();
        foreach (var (prefix, label, cols) in games)
        {
            if (!entry.Games.TryGetValue(prefix, out var d)) continue;
            var rows = new List<string>();
            for (int s = 0; s < 10; s++)
            {
                if (!d.TryGetValue($"set_{s}", out var raw) || string.IsNullOrWhiteSpace(raw.Replace("|",""))) continue;
                var vals = raw.Split('|');
                var nums = new List<string>();
                for (int c = 0; c < cols && c < vals.Length; c++)
                    if (!string.IsNullOrWhiteSpace(vals[c])) nums.Add(vals[c]);
                if (nums.Count > 0) rows.Add($"Set {s+1}: {string.Join(" ", nums)}");
            }
            if (rows.Count == 0) continue;
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"── {label} ──");
            foreach (var r in rows) sb.AppendLine(r);
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no data found)";
    }

    async Task OnDelete(ArchiveEntry entry)
    {
        bool confirm = await DisplayAlert("Delete Archive",
            $"Delete archive from {entry.Date:MMM d, yyyy h:mm tt}?\nThis cannot be undone.",
            "Delete", "Cancel");
        if (!confirm) return;

        ArchiveService.Delete(entry);
        Refresh();
    }

    async void BtnBack_Clicked(object sender, EventArgs e) =>
        await Shell.Current.GoToAsync("..", false);
}
