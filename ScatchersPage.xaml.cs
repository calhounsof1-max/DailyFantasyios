using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

/// <summary>Combines a ticket purchase with any win logged against it.</summary>
public class ScratcherRow
{
    public SpendingRecord Record     { get; set; } = new();
    public WinningRecord? Win        { get; set; }

    public string  Date        => Record.Date;
    public int     TicketCount => Record.TicketCount;
    public decimal CostEach    => Record.CostEach;
    public decimal TotalCost   => Record.TotalCost;

    public bool    HasWin      => Win != null;
    public decimal WinAmount   => Win?.Amount ?? 0;
    public string  WinLabel    => Win == null ? "" : (Win.Amount > 0 ? $"Won: {WinAmount:C}" : "No win");

    // Memo: stored in SpendingRecord.Note; "Scratcher" is the old default — treat as blank
    public string  Memo        => (Record.Note == "Scratcher" || string.IsNullOrWhiteSpace(Record.Note))
                                      ? "" : Record.Note;
    public bool    HasMemo     => !string.IsNullOrEmpty(Memo);
}

public partial class ScatchersPage : ContentPage
{
    List<ScratcherRow> _rows = new();
    List<string> _memoSuggestions = new();

    public ScatchersPage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        datePicker.Date = DateTime.Today;
        await LoadAsync();
    }

    async Task LoadAsync()
    {
        var spending = await SpendingTracker.LoadAllAsync();

        // Past memo values (most-recently-used first) for the autocomplete list below the Memo field.
        _memoSuggestions = spending
            .Where(r => r.Game == "SC" && !string.IsNullOrWhiteSpace(r.Note) && r.Note != "Scratcher")
            .OrderByDescending(r => r.Date)
            .Select(r => r.Note.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var scRecords = spending.Where(r => r.Game == "SC").OrderByDescending(r => r.Date).ToList();

        var wins = await SummaryPage.LoadAllAsync();
        var scWins = wins.Where(w => w.Game == "SC" && !w.IsFreeTicket).ToList();

        // Match each spending record to a win by date + note
        var usedWins = new HashSet<WinningRecord>();
        _rows = scRecords.Select(rec =>
        {
            string expectedNote = $"{rec.TicketCount} × ${rec.CostEach:F2} ticket";
            var win = scWins.FirstOrDefault(w =>
                w.Date == rec.Date && w.Note == expectedNote && !usedWins.Contains(w));
            if (win != null) usedWins.Add(win);
            return new ScratcherRow { Record = rec, Win = win };
        }).ToList();

        lstLog.ItemsSource = null;
        lstLog.ItemsSource = _rows;

        decimal spent = _rows.Sum(r => r.TotalCost);
        decimal won   = scWins.Sum(w => w.Amount);
        decimal net   = won - spent;
        string  sign  = net >= 0 ? "+" : "";

        lblSpent.Text    = $"Spent: {spent:C}";
        lblWon.Text      = $"Won: {won:C}";
        lblNet.Text      = $"Net: {sign}{net:C}";
        lblNet.TextColor = net >= 0 ? Color.FromArgb("#66BB6A") : Color.FromArgb("#EF5350");
    }

    private async void BtnWin_Clicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not ScratcherRow row) return;

        string? amtStr = await DisplayPromptAsync("Log Scratcher Win",
            $"How much did you win?\n({row.Date}  {row.TicketCount} × ${row.CostEach:F2})",
            placeholder: "0.00", keyboard: Keyboard.Numeric, maxLength: 8);
        if (amtStr == null) return;

        if (!decimal.TryParse(amtStr.Trim(), out decimal amount) || amount < 0)
        {
            await DisplayAlert("Invalid", "Enter a valid win amount.", "OK");
            return;
        }

        string note = $"{row.TicketCount} × ${row.CostEach:F2} ticket";

        // If this row already has a win, update it; otherwise add new
        if (row.Win != null)
        {
            var allWins = await SummaryPage.LoadAllAsync();
            var match = allWins.FirstOrDefault(w =>
                w.Game == "SC" && w.Date == row.Date && w.Note == note);
            if (match != null) match.Amount = amount;
            await SummaryPage.SaveAllAsync(allWins);
        }
        else
        {
            await SummaryPage.AddWinAsync(new WinningRecord
            {
                Game   = "SC",
                Date   = row.Date,
                Amount = amount,
                Note   = note
            });
        }

        await LoadAsync();
    }

    private void PriceBtn_Clicked(object sender, EventArgs e)
    {
        if (sender is Button btn)
            entryPrice.Text = btn.CommandParameter?.ToString() ?? "";
    }

    // ── Memo autocomplete ────────────────────────────────────────────────────

    private void EntryMemo_Focused(object sender, FocusEventArgs e) => UpdateMemoSuggestions();

    private void EntryMemo_TextChanged(object sender, TextChangedEventArgs e) => UpdateMemoSuggestions();

    private async void EntryMemo_Unfocused(object sender, FocusEventArgs e)
    {
        // Delay hiding so a tap on a suggestion still registers before the list disappears.
        await Task.Delay(150);
        lstMemoSuggestions.IsVisible = false;
    }

    private void UpdateMemoSuggestions()
    {
        string text = entryMemo.Text?.Trim() ?? "";
        var matches = (string.IsNullOrEmpty(text)
                ? _memoSuggestions
                : _memoSuggestions.Where(m => m.Contains(text, StringComparison.OrdinalIgnoreCase)))
            .Take(6)
            .ToList();

        lstMemoSuggestions.ItemsSource = matches;
        lstMemoSuggestions.IsVisible = matches.Count > 0 && entryMemo.IsFocused;
    }

    private void MemoSuggestions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is string memo)
        {
            entryMemo.Text = memo;
            entryMemo.CursorPosition = memo.Length;
        }
        lstMemoSuggestions.SelectedItem = null;
        lstMemoSuggestions.IsVisible = false;
    }

    private async void BtnAdd_Clicked(object sender, EventArgs e)
    {
        if (!int.TryParse(entryQty.Text?.Trim(), out int qty) || qty < 1)
        {
            await DisplayAlert("Invalid", "Enter a valid ticket count.", "OK");
            return;
        }
        if (!decimal.TryParse(entryPrice.Text?.Trim(), out decimal price) || price <= 0)
        {
            await DisplayAlert("Invalid", "Enter a valid ticket price.", "OK");
            return;
        }

        string date = $"{datePicker.Date:yyyy-MM-dd}";

        string memo = entryMemo.Text?.Trim() ?? "";

        var record = new SpendingRecord
        {
            Game        = "SC",
            Date        = date,
            TicketCount = qty,
            CostEach    = price,
            Note        = memo
        };
        var allSpending = await SpendingTracker.LoadAllAsync();
        allSpending.Add(record);
        await SpendingTracker.SaveAllAsync(allSpending);

        decimal total = qty * price;
        await TicketLogService.AddSingleAsync(new TicketLogEntry
        {
            Game      = "SC",
            Date      = date,
            Slot      = 0,
            Row       = 0,
            Numbers   = $"{qty} × ${price:F2}",
            Extra     = $"${total:F2}",
            DrawCount = qty
        });

        entryQty.Text = "1";
        entryPrice.Text = "";
        entryMemo.Text = "";
        await LoadAsync();
    }

    private async void BtnEdit_Clicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not ScratcherRow row) return;

        string? qtyStr = await DisplayPromptAsync("Edit Tickets",
            "Number of tickets:", initialValue: row.TicketCount.ToString(),
            keyboard: Keyboard.Numeric, maxLength: 4);
        if (qtyStr == null) return;
        if (!int.TryParse(qtyStr.Trim(), out int newQty) || newQty < 1)
        {
            await DisplayAlert("Invalid", "Enter a valid ticket count.", "OK");
            return;
        }

        string? priceStr = await DisplayPromptAsync("Edit Price",
            "Price per ticket ($):", initialValue: row.CostEach.ToString("F2"),
            keyboard: Keyboard.Numeric, maxLength: 6);
        if (priceStr == null) return;
        if (!decimal.TryParse(priceStr.Trim(), out decimal newPrice) || newPrice <= 0)
        {
            await DisplayAlert("Invalid", "Enter a valid price.", "OK");
            return;
        }

        string? memoStr = await DisplayPromptAsync("Memo",
            "Notes about this ticket (optional):",
            initialValue: row.Memo,
            placeholder: "e.g. Gold Rush 200x, Gas station",
            maxLength: 80);
        if (memoStr == null) return;

        var all = await SpendingTracker.LoadAllAsync();
        var match = all.FirstOrDefault(r =>
            r.Game == "SC" && r.Date == row.Date &&
            r.TicketCount == row.TicketCount && r.CostEach == row.CostEach);
        if (match != null)
        {
            match.TicketCount = newQty;
            match.CostEach    = newPrice;
            match.Note        = memoStr.Trim();
            await SpendingTracker.SaveAllAsync(all);
        }
        await LoadAsync();
    }

    private async void BtnDelete_Clicked(object sender, EventArgs e)
    {
        if (sender is not Button btn || btn.CommandParameter is not ScratcherRow row) return;
        bool confirm = await DisplayAlert("Delete",
            $"Remove {row.Date} — {row.TicketCount} x ${row.CostEach:F2}?", "Delete", "Cancel");
        if (!confirm) return;

        // Remove from spending log
        var all = await SpendingTracker.LoadAllAsync();
        var match = all.FirstOrDefault(r =>
            r.Game == "SC" && r.Date == row.Date &&
            r.TicketCount == row.TicketCount && r.CostEach == row.CostEach);
        if (match != null) all.Remove(match);
        await SpendingTracker.SaveAllAsync(all);

        // Remove from ticket log
        string numbers = $"{row.TicketCount} × ${row.CostEach:F2}";
        var logAll = await TicketLogService.LoadAllAsync();
        var logMatch = logAll.FirstOrDefault(e =>
            e.Game == "SC" && e.Date == row.Date && e.Numbers == numbers);
        if (logMatch != null)
        {
            logAll.Remove(logMatch);
            await TicketLogService.SavePublicAsync(logAll);
        }

        // Remove the linked win if one was logged for this ticket
        if (row.HasWin)
        {
            string winNote = $"{row.TicketCount} × ${row.CostEach:F2} ticket";
            var allWins = await SummaryPage.LoadAllAsync();
            var winMatch = allWins.FirstOrDefault(w =>
                w.Game == "SC" && w.Date == row.Date && w.Note == winNote);
            if (winMatch != null)
            {
                allWins.Remove(winMatch);
                await SummaryPage.SaveAllAsync(allWins);
            }
        }

        await LoadAsync();
    }

    private async void BtnBack_Clicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..", false);

    private async void BtnHome_Clicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("//MainPage", false);
}
