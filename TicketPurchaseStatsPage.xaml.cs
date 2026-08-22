using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

// ── Bar chart drawable ────────────────────────────────────────────────────────

/// <summary>
/// Horizontal bar chart — one bar per game, sorted by count descending.
/// Only games with TotalCount > 0 are shown.
/// </summary>
class BarChartDrawable(List<GameAllTimeStat> stats) : IDrawable
{
    readonly List<GameAllTimeStat> _bars = stats.Where(s => s.TotalCount > 0).ToList();

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (_bars.Count == 0) return;

        const float padX      = 16f;
        const float padTop    = 12f;
        const float labelW    = 88f;
        const float countW    = 42f;
        const float barH      = 26f;
        const float spacing   = 10f;

        float barAreaW = rect.Width - padX * 2 - labelW - countW;
        int   maxCount = _bars.Max(b => b.TotalCount);

        float y = padTop;
        foreach (var g in _bars)
        {
            // Game name
            canvas.FontColor = Colors.White;
            canvas.FontSize  = 11f;
            canvas.DrawString(g.GameName, padX, y, labelW - 4, barH,
                              HorizontalAlignment.Left, VerticalAlignment.Center);

            // Filled bar
            float barW  = maxCount > 0 ? (float)g.TotalCount / maxCount * barAreaW : 0f;
            float barX  = padX + labelW;
            var   color = Color.FromArgb(g.Color);
            canvas.FillColor = color.WithAlpha(0.85f);
            canvas.FillRoundedRectangle(barX, y + 2, Math.Max(barW, 4), barH - 4, 4);

            // Count label
            canvas.FontColor = Colors.White;
            canvas.FontSize  = 12f;
            canvas.DrawString(g.TotalCount.ToString(),
                              barX + barW + 6, y, countW, barH,
                              HorizontalAlignment.Left, VerticalAlignment.Center);

            y += barH + spacing;
        }
    }
}

// ── Bar chart: daily spent vs won (vertical, grouped) ────────────────────────

class BarSpentDrawable(List<(string Date, decimal Spent, decimal Won)> points) : IDrawable
{
    public void Draw(ICanvas canvas, RectF rect)
    {
        // Show up to last 14 days so bars aren't too thin
        var pts = points.Count > 14 ? points.Skip(points.Count - 14).ToList() : points;
        int n   = pts.Count;
        if (n == 0)
        {
            canvas.FontColor = Color.FromArgb("#445566");
            canvas.FontSize  = 12f;
            canvas.DrawString("No data", rect.X, rect.Y, rect.Width, rect.Height,
                HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        const float padL = 52f, padR = 12f, padT = 20f, padB = 32f;
        float w = rect.Width  - padL - padR;
        float h = rect.Height - padT - padB;

        // Include net (won-spent) in Y range; net can be negative
        var nets = pts.Select(p => (float)(p.Won - p.Spent)).ToList();
        float maxVal = (float)Math.Max(pts.Max(p => Math.Max(p.Spent, p.Won)), 1m);
        float minVal = Math.Min(0f, nets.Min());
        float range  = maxVal - minVal;
        if (range <= 0f) range = 1f;

        // Zero baseline Y
        float baseY = padT + h * maxVal / range;

        // Grid + Y labels
        for (int i = 0; i <= 4; i++)
        {
            float val = minVal + range * i / 4f;
            float gy  = padT + h - (val - minVal) / range * h;
            canvas.StrokeColor = Color.FromArgb("#1A2E45");
            canvas.StrokeSize  = 0.8f;
            canvas.DrawLine(padL, gy, padL + w, gy);
            canvas.FontColor = Color.FromArgb("#607D8B");
            canvas.FontSize  = 9f;
            canvas.DrawString($"${val:F0}", 2, gy - 7, padL - 6, 14,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        // Zero line (highlighted when net is negative)
        if (minVal < 0f)
        {
            canvas.StrokeColor = Color.FromArgb("#334466");
            canvas.StrokeSize  = 1.2f;
            canvas.DrawLine(padL, baseY, padL + w, baseY);
        }

        float groupW = w / n;
        const float gap = 2f;
        float barW = Math.Min((groupW - gap * 4f) / 3f, 14f);
        float clusterW = barW * 3f + gap * 2f;

        for (int i = 0; i < n; i++)
        {
            float cx  = padL + i * groupW + groupW / 2f;
            float x0  = cx - clusterW / 2f;   // spent bar left edge
            float x1  = x0 + barW + gap;       // won bar left edge
            float x2  = x1 + barW + gap;       // net bar left edge
            float net = nets[i];

            // Spent bar (red) — always positive, upward from baseline
            float spH = (float)pts[i].Spent / range * h;
            canvas.FillColor = Color.FromArgb("#EF5350").WithAlpha(0.85f);
            if (spH > 0) canvas.FillRoundedRectangle(x0, baseY - spH, barW, spH, 3);

            // Won bar (green) — always positive, upward from baseline
            float wnH = (float)pts[i].Won / range * h;
            canvas.FillColor = Color.FromArgb("#66BB6A").WithAlpha(0.85f);
            if (wnH > 0) canvas.FillRoundedRectangle(x1, baseY - wnH, barW, wnH, 3);

            // Net bar (yellow) — up if positive, down if negative
            float ntH = MathF.Abs(net) / range * h;
            canvas.FillColor = Color.FromArgb("#FFD54F").WithAlpha(0.85f);
            if (ntH > 0)
            {
                float netTop = net >= 0f ? baseY - ntH : baseY;
                canvas.FillRoundedRectangle(x2, netTop, barW, ntH, 3);
            }

            // Date label
            string lbl = pts[i].Date.Length >= 10 ? pts[i].Date.Substring(5) : pts[i].Date;
            canvas.FontColor = Color.FromArgb("#607D8B");
            canvas.FontSize  = 7.5f;
            canvas.DrawString(lbl, cx - 20f, padT + h + 4f, 40f, 14f,
                HorizontalAlignment.Center, VerticalAlignment.Top);
        }

        // Totals annotation (top-right)
        float ts  = (float)pts.Sum(p => p.Spent);
        float tw  = (float)pts.Sum(p => p.Won);
        float tn  = tw - ts;
        string ns = tn >= 0f ? $"+${tn:F0}" : $"-${MathF.Abs(tn):F0}";
        canvas.FontColor = Color.FromArgb("#90CAF9");
        canvas.FontSize  = 9f;
        canvas.DrawString($"Spent ${ts:F0}  Won ${tw:F0}  Net {ns}",
            padL, padT - 16f, w, 14f, HorizontalAlignment.Right, VerticalAlignment.Center);
    }
}

// ── Pie chart: total spent vs won ─────────────────────────────────────────────

class PieSpentDrawable(List<(string Date, decimal Spent, decimal Won)> points) : IDrawable
{
    public void Draw(ICanvas canvas, RectF rect)
    {
        float totalSpent = (float)points.Sum(p => p.Spent);
        float totalWon   = (float)points.Sum(p => p.Won);
        float totalNet   = MathF.Abs(totalWon - totalSpent);

        if (totalSpent <= 0f && totalWon <= 0f)
        {
            canvas.FontColor = Color.FromArgb("#445566");
            canvas.FontSize  = 12f;
            canvas.DrawString("No data", rect.X, rect.Y, rect.Width, rect.Height,
                HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        // Use Spent + Won + |Net| as total so all 3 get a visible slice
        float total = totalSpent + totalWon + totalNet;
        if (total <= 0f) total = 1f;

        float cx = rect.Width  / 2f;
        float cy = rect.Height / 2f - 22f;
        float r  = Math.Min(rect.Width, rect.Height) / 2f - 40f;

        float spentAngle = totalSpent / total * 360f;
        float wonAngle   = totalWon   / total * 360f;
        float netAngle   = totalNet   / total * 360f;

        float a0 = -90f;
        DrawSlice(canvas, cx, cy, r, a0,                          spentAngle, Color.FromArgb("#EF5350"));
        DrawSlice(canvas, cx, cy, r, a0 + spentAngle,             wonAngle,   Color.FromArgb("#66BB6A"));
        DrawSlice(canvas, cx, cy, r, a0 + spentAngle + wonAngle,  netAngle,   Color.FromArgb("#FFD54F"));

        // % labels inside each slice (only if slice is wide enough to fit text)
        float labelR = r * 0.62f;
        DrawSliceLabel(canvas, cx, cy, labelR, a0,                         spentAngle, $"{spentAngle/360f*100:F0}%");
        DrawSliceLabel(canvas, cx, cy, labelR, a0 + spentAngle,            wonAngle,   $"{wonAngle/360f*100:F0}%");
        DrawSliceLabel(canvas, cx, cy, labelR, a0 + spentAngle + wonAngle, netAngle,   $"{netAngle/360f*100:F0}%");

        // Net sign and % relative to spent
        float  rawNet    = totalWon - totalSpent;
        string netSign   = rawNet >= 0f ? "+" : "-";
        string netColor  = rawNet >= 0f ? "#A5D6A7" : "#EF9A9A";
        float  netPct    = totalSpent > 0f ? MathF.Abs(rawNet) / totalSpent * 100f : 0f;

        // 3-row legend
        float ly1 = rect.Height - 52f;
        float ly2 = rect.Height - 34f;
        float ly3 = rect.Height - 16f;
        float lx  = cx - 120f;

        canvas.FillColor = Color.FromArgb("#EF5350");
        canvas.FillRoundedRectangle(lx, ly1 + 1f, 12f, 10f, 2f);
        canvas.FontColor = Color.FromArgb("#EF9A9A");
        canvas.FontSize  = 10.5f;
        canvas.DrawString($"Spent  ${totalSpent:F0}",
            lx + 16f, ly1 - 2f, 220f, 14f, HorizontalAlignment.Left, VerticalAlignment.Center);

        canvas.FillColor = Color.FromArgb("#66BB6A");
        canvas.FillRoundedRectangle(lx, ly2 + 1f, 12f, 10f, 2f);
        canvas.FontColor = Color.FromArgb("#A5D6A7");
        canvas.FontSize  = 10.5f;
        canvas.DrawString($"Won    ${totalWon:F0}",
            lx + 16f, ly2 - 2f, 220f, 14f, HorizontalAlignment.Left, VerticalAlignment.Center);

        canvas.FillColor = Color.FromArgb("#FFD54F");
        canvas.FillRoundedRectangle(lx, ly3 + 1f, 12f, 10f, 2f);
        canvas.FontColor = Color.FromArgb(netColor);
        canvas.FontSize  = 10.5f;
        canvas.DrawString($"Net    {netSign}${MathF.Abs(rawNet):F0}  ({netSign}{netPct:F0}% of spent)",
            lx + 16f, ly3 - 2f, 220f, 14f, HorizontalAlignment.Left, VerticalAlignment.Center);
    }

    static void DrawSlice(ICanvas canvas, float cx, float cy, float r, float startDeg, float sweepDeg, Color color)
    {
        if (sweepDeg <= 0f) return;
        int steps = Math.Max(3, (int)(sweepDeg / 4));
        var path  = new PathF();
        path.MoveTo(cx, cy);
        for (int i = 0; i <= steps; i++)
        {
            float angle = (startDeg + sweepDeg * i / steps) * MathF.PI / 180f;
            path.LineTo(cx + MathF.Cos(angle) * r, cy + MathF.Sin(angle) * r);
        }
        path.Close();
        canvas.FillColor   = color.WithAlpha(0.85f);
        canvas.FillPath(path);
        canvas.StrokeColor = Color.FromArgb("#0D1B2A");
        canvas.StrokeSize  = 1.5f;
        canvas.DrawPath(path);
    }

    static void DrawSliceLabel(ICanvas canvas, float cx, float cy, float labelR,
                                float startDeg, float sweepDeg, string text)
    {
        if (sweepDeg < 18f) return;   // skip if slice too narrow for text
        float midAngle = (startDeg + sweepDeg / 2f) * MathF.PI / 180f;
        float tx = cx + MathF.Cos(midAngle) * labelR;
        float ty = cy + MathF.Sin(midAngle) * labelR;
        const float hw = 28f, hh = 14f;
        canvas.FontColor = Colors.White;
        canvas.FontSize  = 11f;
        canvas.DrawString(text, tx - hw, ty - hh / 2f, hw * 2f, hh,
            HorizontalAlignment.Center, VerticalAlignment.Center);
    }
}

// ── Line chart drawable ───────────────────────────────────────────────────────

/// <summary>
/// Cumulative spent vs won vs net line chart.
/// </summary>
class LineChartDrawable(List<(string Date, decimal Spent, decimal Won)> points) : IDrawable
{
    public void Draw(ICanvas canvas, RectF rect)
    {
        if (points.Count == 0)
        {
            canvas.FontColor = Color.FromArgb("#445566");
            canvas.FontSize  = 12f;
            canvas.DrawString("No spending/winnings data", rect.X, rect.Y, rect.Width, rect.Height,
                HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        const float padL = 58f, padR = 16f, padT = 24f, padB = 36f;
        float w = rect.Width  - padL - padR;
        float h = rect.Height - padT - padB;

        // Build cumulative running totals including net
        var cum = new List<(string Date, float Spent, float Won, float Net)>();
        float cs = 0f, cw = 0f;
        foreach (var p in points)
        {
            cs += (float)p.Spent;
            cw += (float)p.Won;
            cum.Add((p.Date, cs, cw, cw - cs));
        }

        float maxVal = Math.Max(cum.Max(p => Math.Max(p.Spent, Math.Max(p.Won, p.Net))), 1f);
        float minVal = Math.Min(0f, cum.Min(p => p.Net));
        float range  = maxVal - minVal;
        if (range <= 0f) range = 1f;

        float ToY(float val) => padT + h - (val - minVal) / range * h;

        // Grid lines + Y labels
        int ySteps = 4;
        for (int i = 0; i <= ySteps; i++)
        {
            float val = minVal + range * i / ySteps;
            float y   = ToY(val);
            canvas.StrokeColor = Color.FromArgb("#1A2E45");
            canvas.StrokeSize  = 0.8f;
            canvas.DrawLine(padL, y, padL + w, y);
            canvas.FontColor = Color.FromArgb("#607D8B");
            canvas.FontSize  = 9f;
            canvas.DrawString($"${val:F0}", 2, y - 7, padL - 6, 14,
                HorizontalAlignment.Right, VerticalAlignment.Center);
        }

        // Zero line (highlighted when net goes negative)
        if (minVal < 0f)
        {
            float zy = ToY(0f);
            canvas.StrokeColor = Color.FromArgb("#334466");
            canvas.StrokeSize  = 1.2f;
            canvas.DrawLine(padL, zy, padL + w, zy);
        }

        int   n  = cum.Count;
        float sx = n > 1 ? w / (n - 1) : w;

        // Spent line (red)
        canvas.StrokeColor = Color.FromArgb("#EF5350");
        canvas.StrokeSize  = 2f;
        for (int i = 0; i < n - 1; i++)
            canvas.DrawLine(padL + i * sx, ToY(cum[i].Spent), padL + (i+1) * sx, ToY(cum[i+1].Spent));

        // Won line (green)
        canvas.StrokeColor = Color.FromArgb("#66BB6A");
        canvas.StrokeSize  = 2f;
        for (int i = 0; i < n - 1; i++)
            canvas.DrawLine(padL + i * sx, ToY(cum[i].Won), padL + (i+1) * sx, ToY(cum[i+1].Won));

        // Net line (yellow)
        canvas.StrokeColor = Color.FromArgb("#FFD54F");
        canvas.StrokeSize  = 2f;
        for (int i = 0; i < n - 1; i++)
            canvas.DrawLine(padL + i * sx, ToY(cum[i].Net), padL + (i+1) * sx, ToY(cum[i+1].Net));

        // Dots
        for (int i = 0; i < n; i++)
        {
            float x = padL + i * sx;
            canvas.FillColor = Color.FromArgb("#EF5350");
            canvas.FillCircle(x, ToY(cum[i].Spent), 3f);
            canvas.FillColor = Color.FromArgb("#66BB6A");
            canvas.FillCircle(x, ToY(cum[i].Won), 3f);
            canvas.FillColor = Color.FromArgb("#FFD54F");
            canvas.FillCircle(x, ToY(cum[i].Net), 3f);
        }

        // X-axis date labels
        int every = Math.Max(1, n / 7);
        canvas.FontColor = Color.FromArgb("#607D8B");
        canvas.FontSize  = 8.5f;
        for (int i = 0; i < n; i += every)
        {
            float x    = padL + i * sx;
            string lbl = cum[i].Date.Length >= 10 ? cum[i].Date.Substring(5) : cum[i].Date;
            canvas.DrawString(lbl, x - 20, padT + h + 5, 40, 14,
                HorizontalAlignment.Center, VerticalAlignment.Top);
        }

        // Totals annotation
        float net     = cum[^1].Net;
        string netStr = net >= 0 ? $"+${net:F0}" : $"-${Math.Abs(net):F0}";
        canvas.FontColor = Color.FromArgb("#90CAF9");
        canvas.FontSize  = 9f;
        canvas.DrawString($"Spent ${cum[^1].Spent:F0}  Won ${cum[^1].Won:F0}  Net {netStr}",
            padL, padT - 18, w, 14, HorizontalAlignment.Right, VerticalAlignment.Center);
    }
}

// ── Page ──────────────────────────────────────────────────────────────────────

enum SpentChartType { Line, Bar, Pie }

public partial class TicketPurchaseStatsPage : ContentPage
{
    TicketPurchaseStatsResult? _result;
    bool                       _chartVisible = true;

    // Spent/Won chart state
    SpentChartType _spentChartType = SpentChartType.Line;
    List<(string Date, decimal Spent, decimal Won)> _linePoints = new();

    // Tracks which daily date headers are expanded
    readonly Dictionary<string, VerticalStackLayout> _dailyBodies = new();
    readonly Dictionary<string, Label>               _dailyChevrons = new();

    bool _splitGamesMode = false;

    public TicketPurchaseStatsPage()
    {
        InitializeComponent();
        lblFilePath.Text = TicketPurchaseStatsService.DataPath;

        // Set up chart type picker
        chartTypePicker.Items.Add("Line Chart");
        chartTypePicker.Items.Add("Bar Chart");
        chartTypePicker.Items.Add("Pie Chart");
        chartTypePicker.SelectedIndex = 0;
        chartTypePicker.SelectedIndexChanged += (s, e) =>
        {
            if (chartTypePicker.SelectedIndex < 0) return;
            _spentChartType = (SpentChartType)chartTypePicker.SelectedIndex;
            RenderSpentChart();
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = LoadAsync();
    }

    // ── Load & compute ────────────────────────────────────────────────────────

    async Task LoadAsync()
    {
        // Show last saved result immediately (no flicker), then refresh in background
        var saved = await TicketPurchaseStatsService.LoadSavedAsync();
        if (saved != null) Render(saved);

        // Full async recompute from ticket log
        _result = await TicketPurchaseStatsService.GetStatsAsync();
        Render(_result);
        await LoadLineChartAsync();
    }

    // ── Render ────────────────────────────────────────────────────────────────

    void Render(TicketPurchaseStatsResult result)
    {
        _result = result;

        lblGrandTotal.Text = result.GrandTotalTickets.ToString();
        lblAvgPerDay.Text  = result.AvgTicketsPerDay.ToString("0.#");
        lblGenerated.Text  = $"Updated {result.GeneratedAt}";

        BuildGameRows(result.GameTotals);
        BuildDailyRows(result.DailyStats);
        BuildSplitGamesRows(result.DailyStats);

        if (_chartVisible)
        {
            RefreshChart();
            _ = LoadLineChartAsync();
        }
    }

    // ── Game totals section ───────────────────────────────────────────────────

    void BuildGameRows(List<GameAllTimeStat> games)
    {
        gameRows.Children.Clear();

        // Column layout: color bar(4) | name(*) | tickets(56) | avg/day(56) | spent(68)
        GridLength[] cols =
        [
            new GridLength(4,  GridUnitType.Absolute),
            GridLength.Star,
            new GridLength(56, GridUnitType.Absolute),
            new GridLength(56, GridUnitType.Absolute),
            new GridLength(68, GridUnitType.Absolute),
        ];

        foreach (var g in games)
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection(cols.Select(c => new ColumnDefinition(c)).ToArray()),
                BackgroundColor   = Color.FromArgb("#0D1B2A"),
                Padding           = new Thickness(0, 0, 12, 0),
            };

            row.Add(new BoxView { BackgroundColor = Color.FromArgb(g.Color), VerticalOptions = LayoutOptions.Fill });

            row.Add(new Label
            {
                Text            = g.GameName,
                TextColor       = g.TotalCount > 0 ? Colors.White : Color.FromArgb("#445566"),
                FontSize        = 14,
                VerticalOptions = LayoutOptions.Center,
                Margin          = new Thickness(14, 14, 0, 14),
            }, 1, 0);

            row.Add(new Label
            {
                Text              = g.TotalCount > 0 ? g.TotalCount.ToString() : "—",
                TextColor         = g.TotalCount > 0 ? Color.FromArgb(g.Color) : Color.FromArgb("#333333"),
                FontSize          = 15,
                FontAttributes    = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions   = LayoutOptions.Center,
            }, 2, 0);

            row.Add(new Label
            {
                Text              = g.AvgPerDay > 0 ? g.AvgPerDay.ToString("0.#") : "—",
                TextColor         = g.AvgPerDay > 0 ? Color.FromArgb("#80CBC4") : Color.FromArgb("#333333"),
                FontSize          = 13,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions   = LayoutOptions.Center,
            }, 3, 0);

            row.Add(new Label
            {
                Text              = g.TotalCost > 0 ? $"${g.TotalCost:F2}" : "—",
                TextColor         = g.TotalCost > 0 ? Color.FromArgb("#81C784") : Color.FromArgb("#333333"),
                FontSize          = 12,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions   = LayoutOptions.Center,
            }, 4, 0);

            var wrapper = new VerticalStackLayout { Spacing = 0 };
            wrapper.Add(row);
            wrapper.Add(new BoxView { HeightRequest = 1, BackgroundColor = Color.FromArgb("#1E3A5F"), HorizontalOptions = LayoutOptions.Fill });
            gameRows.Add(wrapper);
        }

        // Totals row
        if (games.Count > 0)
        {
            int     totalTickets = games.Sum(g => g.TotalCount);
            decimal totalSpent   = games.Sum(g => g.TotalCost);
            double  totalAvg     = _result != null ? _result.AvgTicketsPerDay : 0;

            var totalRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection(cols.Select(c => new ColumnDefinition(c)).ToArray()),
                BackgroundColor   = Color.FromArgb("#0E2039"),
                Padding           = new Thickness(0, 0, 12, 0),
            };
            totalRow.Add(new BoxView { BackgroundColor = Color.FromArgb("#607D8B"), VerticalOptions = LayoutOptions.Fill });
            totalRow.Add(new Label
            {
                Text            = "TOTAL",
                TextColor       = Color.FromArgb("#90CAF9"),
                FontSize        = 13,
                FontAttributes  = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
                Margin          = new Thickness(14, 12, 0, 12),
            }, 1, 0);
            totalRow.Add(new Label
            {
                Text              = totalTickets > 0 ? totalTickets.ToString() : "—",
                TextColor         = Color.FromArgb("#90CAF9"),
                FontSize          = 15,
                FontAttributes    = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions   = LayoutOptions.Center,
            }, 2, 0);
            totalRow.Add(new Label
            {
                Text              = totalAvg > 0 ? totalAvg.ToString("0.#") : "—",
                TextColor         = Color.FromArgb("#90CAF9"),
                FontSize          = 13,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions   = LayoutOptions.Center,
            }, 3, 0);
            totalRow.Add(new Label
            {
                Text              = totalSpent > 0 ? $"${totalSpent:F2}" : "—",
                TextColor         = Color.FromArgb("#90CAF9"),
                FontSize          = 12,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions   = LayoutOptions.Center,
            }, 4, 0);
            gameRows.Add(totalRow);
        }

        // Column header above the list
        if (gameRows.Children.Count > 0)
        {
            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection(cols.Select(c => new ColumnDefinition(c)).ToArray()),
                BackgroundColor   = Color.FromArgb("#0E2039"),
                Padding           = new Thickness(0, 8, 12, 8),
            };
            header.Add(new BoxView { Color = Colors.Transparent });
            header.Add(new Label { Text = "Game",     TextColor = Color.FromArgb("#90CAF9"), FontSize = 11, FontAttributes = FontAttributes.Bold, Margin = new Thickness(14,0,0,0) }, 1, 0);
            header.Add(new Label { Text = "Tickets",  TextColor = Color.FromArgb("#90CAF9"), FontSize = 11, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.End }, 2, 0);
            header.Add(new Label { Text = "Avg/Day",  TextColor = Color.FromArgb("#90CAF9"), FontSize = 11, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.End }, 3, 0);
            header.Add(new Label { Text = "Spent",    TextColor = Color.FromArgb("#90CAF9"), FontSize = 11, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.End }, 4, 0);
            gameRows.Insert(0, header);
        }
    }

    // ── Daily breakdown section ───────────────────────────────────────────────

    void BuildDailyRows(List<DailyStat> days)
    {
        dailyRows.Children.Clear();
        _dailyBodies.Clear();
        _dailyChevrons.Clear();

        if (days.Count == 0)
        {
            dailyRows.Add(new Label
            {
                Text            = "No daily data yet.",
                TextColor       = Color.FromArgb("#445566"),
                FontSize        = 13,
                Margin          = new Thickness(20, 16),
            });
            return;
        }

        foreach (var day in days)
        {
            string capturedDate = day.Date;

            // Date header row (tap to expand/collapse)
            var chevron = new Label
            {
                Text            = "▶",
                TextColor       = Color.FromArgb("#64B5F6"),
                FontSize        = 12,
                VerticalOptions = LayoutOptions.Center,
                Margin          = new Thickness(0, 0, 8, 0),
            };

            var headerGrid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(60, GridUnitType.Absolute)),
                    new ColumnDefinition(new GridLength(28, GridUnitType.Absolute)),
                },
                BackgroundColor = Color.FromArgb("#112233"),
                Padding         = new Thickness(16, 11),
            };

            // Parse date for friendly display
            string dateLabel = day.Date;
            if (DateTime.TryParse(day.Date, out var dt))
                dateLabel = dt.ToString("ddd M/d/yyyy");

            headerGrid.Add(new Label
            {
                Text            = dateLabel,
                TextColor       = Colors.White,
                FontSize        = 13,
                FontAttributes  = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
            });
            headerGrid.Add(new Label
            {
                Text              = $"{day.TotalTickets} tickets",
                TextColor         = Color.FromArgb("#64B5F6"),
                FontSize          = 12,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions   = LayoutOptions.Center,
            }, 1, 0);
            headerGrid.Add(chevron, 2, 0);

            // Body (hidden by default)
            var body = new VerticalStackLayout
            {
                Spacing         = 0,
                IsVisible       = false,
                BackgroundColor = Color.FromArgb("#0A1628"),
            };

            foreach (var g in day.Games)
            {
                var gameRow = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(new GridLength(4,  GridUnitType.Absolute)),
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(new GridLength(60, GridUnitType.Absolute)),
                    },
                    Padding = new Thickness(0, 0, 12, 0),
                };

                string color = GameColorFor(g.Game);
                gameRow.Add(new BoxView { BackgroundColor = Color.FromArgb(color), VerticalOptions = LayoutOptions.Fill });
                gameRow.Add(new Label
                {
                    Text            = g.GameName,
                    TextColor       = Colors.White,
                    FontSize        = 13,
                    VerticalOptions = LayoutOptions.Center,
                    Margin          = new Thickness(14, 10, 0, 10),
                }, 1, 0);
                gameRow.Add(new Label
                {
                    Text              = g.Count.ToString(),
                    TextColor         = Color.FromArgb(color),
                    FontSize          = 14,
                    FontAttributes    = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions   = LayoutOptions.Center,
                }, 2, 0);

                body.Add(gameRow);
                body.Add(new BoxView { HeightRequest = 1, BackgroundColor = Color.FromArgb("#1A2E45"), HorizontalOptions = LayoutOptions.Fill });
            }

            _dailyBodies[day.Date]   = body;
            _dailyChevrons[day.Date] = chevron;

            headerGrid.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command          = new Command(() => ToggleDayBody(capturedDate))
            });

            var wrapper = new VerticalStackLayout { Spacing = 0 };
            wrapper.Add(headerGrid);
            wrapper.Add(body);
            wrapper.Add(new BoxView { HeightRequest = 1, BackgroundColor = Color.FromArgb("#0D1B2A"), HorizontalOptions = LayoutOptions.Fill });
            dailyRows.Add(wrapper);
        }
    }

    void ToggleDayBody(string date)
    {
        if (!_dailyBodies.TryGetValue(date, out var body)) return;
        body.IsVisible = !body.IsVisible;
        if (_dailyChevrons.TryGetValue(date, out var chev))
            chev.Text = body.IsVisible ? "▼" : "▶";
    }

    // ── Split Games section ───────────────────────────────────────────────────

    void BuildSplitGamesRows(List<DailyStat> days)
    {
        splitGamesRows.Children.Clear();

        // Collect per-game: key → (name, color, list of (date, count)) newest-first
        var byGame = new Dictionary<string, (string Name, string Color, List<(string Date, int Count)> Days)>();

        foreach (var day in days) // already newest-first
        {
            foreach (var g in day.Games)
            {
                if (!byGame.ContainsKey(g.Game))
                    byGame[g.Game] = (g.GameName, GameColorFor(g.Game), new List<(string, int)>());
                byGame[g.Game].Days.Add((day.Date, g.Count));
            }
        }

        if (byGame.Count == 0) return;

        // Sort games descending by total tickets (same order as BY GAME section)
        var sorted = byGame
            .OrderByDescending(kv => kv.Value.Days.Sum(d => d.Count))
            .ToList();

        foreach (var kv in sorted)
        {
            var (gameName, color, dateCounts) = kv.Value;

            var chevron = new Label
            {
                Text            = "▶",
                TextColor       = Color.FromArgb("#64B5F6"),
                FontSize        = 12,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
            };

            var gameHeader = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(new GridLength(4,  GridUnitType.Absolute)),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(new GridLength(48, GridUnitType.Absolute)),
                    new ColumnDefinition(new GridLength(28, GridUnitType.Absolute)),
                },
                BackgroundColor = Color.FromArgb("#0E2039"),
                Padding         = new Thickness(0, 0, 12, 0),
            };
            gameHeader.Add(new BoxView { BackgroundColor = Color.FromArgb(color), VerticalOptions = LayoutOptions.Fill });
            gameHeader.Add(new Label
            {
                Text            = gameName,
                TextColor       = Colors.White,
                FontSize        = 14,
                FontAttributes  = FontAttributes.Bold,
                VerticalOptions = LayoutOptions.Center,
                Margin          = new Thickness(14, 12, 0, 12),
            }, 1, 0);
            int totalForGame = dateCounts.Sum(d => d.Count);
            gameHeader.Add(new Label
            {
                Text              = $"[{totalForGame}]",
                TextColor         = Color.FromArgb(color),
                FontSize          = 14,
                FontAttributes    = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions   = LayoutOptions.Center,
            }, 2, 0);
            gameHeader.Add(chevron, 3, 0);

            var body = new VerticalStackLayout
            {
                Spacing         = 0,
                IsVisible       = false,
                BackgroundColor = Color.FromArgb("#0A1628"),
            };

            foreach (var (date, count) in dateCounts)
            {
                string dateLabel = date;
                if (DateTime.TryParse(date, out var dt))
                    dateLabel = dt.ToString("ddd M/d/yyyy");

                var dateRow = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(new GridLength(4,  GridUnitType.Absolute)),
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(new GridLength(90, GridUnitType.Absolute)),
                    },
                    Padding         = new Thickness(0, 0, 12, 0),
                    BackgroundColor = Color.FromArgb("#0A1628"),
                };
                dateRow.Add(new BoxView { BackgroundColor = Color.FromArgb(color).WithAlpha(0.35f), VerticalOptions = LayoutOptions.Fill });
                dateRow.Add(new Label
                {
                    Text            = dateLabel,
                    TextColor       = Color.FromArgb("#90CAF9"),
                    FontSize        = 13,
                    VerticalOptions = LayoutOptions.Center,
                    Margin          = new Thickness(20, 10, 0, 10),
                }, 1, 0);
                dateRow.Add(new Label
                {
                    Text              = $"{count} tickets",
                    TextColor         = Color.FromArgb(color),
                    FontSize          = 13,
                    FontAttributes    = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions   = LayoutOptions.Center,
                }, 2, 0);

                body.Add(dateRow);
                body.Add(new BoxView { HeightRequest = 1, BackgroundColor = Color.FromArgb("#1A2E45"), HorizontalOptions = LayoutOptions.Fill });
            }

            var localChev = chevron;
            var localBody = body;
            gameHeader.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    localBody.IsVisible = !localBody.IsVisible;
                    localChev.Text      = localBody.IsVisible ? "▼" : "▶";
                })
            });

            var wrapper = new VerticalStackLayout { Spacing = 0 };
            wrapper.Add(gameHeader);
            wrapper.Add(body);
            wrapper.Add(new BoxView { HeightRequest = 1, BackgroundColor = Color.FromArgb("#0D1B2A"), HorizontalOptions = LayoutOptions.Fill });
            splitGamesRows.Add(wrapper);
        }
    }

    void SplitGames_Tapped(object sender, TappedEventArgs e)
    {
        _splitGamesMode             = !_splitGamesMode;
        dailyRows.IsVisible         = !_splitGamesMode;
        splitGamesRows.IsVisible    = _splitGamesMode;
        lblSplitGamesBtn.Text       = _splitGamesMode ? "By Date" : "Split Games";
        lblSplitGamesBtn.TextColor  = _splitGamesMode
            ? Color.FromArgb("#F59E0B")
            : Colors.White;
    }

    // ── Chart ─────────────────────────────────────────────────────────────────

    void RefreshChart()
    {
        if (_result == null) return;
        int visibleBars = _result.GameTotals.Count(g => g.TotalCount > 0);
        chartView.HeightRequest = Math.Max(200, visibleBars * 36 + 24);
        chartView.Drawable      = new BarChartDrawable(_result.GameTotals);
        chartView.Invalidate();
    }

    void ChartBtn_Tapped(object sender, TappedEventArgs e)
    {
        _chartVisible                = !_chartVisible;
        chartView.IsVisible          = _chartVisible;
        lineChartSection.IsVisible   = _chartVisible;
        lblChartBtn.Text             = _chartVisible ? "✕ Chart" : "📊 Chart";
        lblChartBtn.TextColor        = _chartVisible
            ? Color.FromArgb("#EF9A9A")
            : Color.FromArgb("#4FC3F7");

        if (_chartVisible) RefreshChart();
    }

    // ── Line chart ────────────────────────────────────────────────────────────

    async Task LoadLineChartAsync()
    {
        var spentByDate = (_result?.DailyStats ?? new())
            .ToDictionary(d => d.Date, d => d.TotalCost);

        var winnings = await LoadWinningsAsync();
        var wonByDate = winnings
            .Where(w => !w.IsFreeTicket)
            .GroupBy(w => w.Date)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));

        var allDates = spentByDate.Keys.Union(wonByDate.Keys)
            .OrderBy(d => d)
            .ToList();

        _linePoints = allDates
            .Select(d => (
                Date:  d,
                Spent: spentByDate.TryGetValue(d, out var s) ? s : 0m,
                Won:   wonByDate  .TryGetValue(d, out var w) ? w : 0m
            ))
            .ToList();

        RenderSpentChart();
    }

    void RenderSpentChart()
    {
        lineChartView.Drawable = _spentChartType switch
        {
            SpentChartType.Bar => new BarSpentDrawable(_linePoints),
            SpentChartType.Pie => new PieSpentDrawable(_linePoints),
            _                  => new LineChartDrawable(_linePoints),
        };
        lineChartView.Invalidate();
    }

    void LineChart_Tapped(object sender, TappedEventArgs e)
    {
        // Cycle to next chart type; picker change triggers RenderSpentChart
        chartTypePicker.SelectedIndex = ((int)_spentChartType + 1) % 3;
    }

    static async Task<List<WinningRecord>> LoadWinningsAsync()
    {
        try
        {
            string path = Path.Combine(FileSystem.AppDataDirectory, "winnings_log.json");
            if (!File.Exists(path)) return new();
            string json = await File.ReadAllTextAsync(path);
            return System.Text.Json.JsonSerializer.Deserialize<List<WinningRecord>>(json) ?? new();
        }
        catch { return new(); }
    }

    // ── Analyze ───────────────────────────────────────────────────────────────

    async void Analyze_Tapped(object sender, TappedEventArgs e)
    {
        if (_result == null)
        {
            await DisplayAlert("Not Ready", "Stats are still loading. Please wait a moment.", "OK");
            return;
        }

        AnalyzeService.PendingInput = new AnalysisInput
        {
            GrandTotal  = _result.GrandTotalTickets,
            AvgPerDay   = _result.AvgTicketsPerDay,
            TotalSpent  = _linePoints.Sum(p => p.Spent),
            TotalWon    = _linePoints.Sum(p => p.Won),
            GeneratedAt = _result.GeneratedAt,
            Games       = _result.GameTotals,
            Daily       = _linePoints
                .Select(p => new DailyPoint { Date = p.Date, Spent = p.Spent, Won = p.Won })
                .ToList(),
        };

        await Navigation.PushAsync(new StatsAnalyzePage());
    }

    // ── Navigation ────────────────────────────────────────────────────────────

    async void Back_Tapped(object sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync("..", false);

    // ── Helpers ───────────────────────────────────────────────────────────────

    static string GameColorFor(string key) => key switch
    {
        "F5" => "#FF8F00",
        "SL" => "#7B1FA2",
        "PB" => "#C62828",
        "MM" => "#F57F17",
        "D3" => "#1565C0",
        "D4" => "#00695C",
        "DD" => "#5D4037",
        "SC" => "#2E7D32",
        _    => "#888888",
    };
}
