using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DailyFantasyMAUI.Services;

// ── Data passed from the Stats page ──────────────────────────────────────────

public class AnalysisInput
{
    public int                   GrandTotal  { get; init; }
    public double                AvgPerDay   { get; init; }
    public decimal               TotalSpent  { get; init; }
    public decimal               TotalWon    { get; init; }
    public string                GeneratedAt { get; init; } = "";
    public List<GameAllTimeStat> Games       { get; init; } = [];
    public List<DailyPoint>      Daily       { get; init; } = [];
}

public class DailyPoint
{
    public string  Date  { get; init; } = "";
    public decimal Spent { get; init; }
    public decimal Won   { get; init; }
}

// ── Service ───────────────────────────────────────────────────────────────────

public static class AnalyzeService
{
    // Google Gemini 2.0 Flash — FREE tier: no credit card, just a Google account
    // 15 requests/min · 1,500 requests/day · 1M tokens/day
    const string Model = "gemini-2.0-flash";

    static string GeminiUrl(string key) =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={key}";

    /// <summary>Shared between Stats page and Analyze page to avoid nav-param overhead.</summary>
    public static AnalysisInput? PendingInput { get; set; }

    public static string ApiKey
    {
        get => Preferences.Get("gemini_api_key", "");
        set => Preferences.Set("gemini_api_key", value);
    }

    // ── Built-in local report (no API key required) ───────────────────────────

    public static string BuildLocalReport(AnalysisInput d)
    {
        decimal net     = d.TotalWon - d.TotalSpent;
        string  roi     = d.TotalSpent > 0 ? $"{d.TotalWon / d.TotalSpent * 100m:F1}%" : "N/A";
        decimal retBack = d.TotalSpent > 0 ? d.TotalWon / d.TotalSpent : 0m;
        var ordered     = d.Daily.OrderByDescending(p => p.Date).ToList();
        var games       = d.Games.Where(g => g.TotalCount > 0).OrderByDescending(g => g.TotalCount).ToList();

        var sb = new StringBuilder();

        // ── 1. Overview ───────────────────────────────────────────────────────
        sb.AppendLine("## 1. Overview");
        if (ordered.Count > 0)
        {
            string first = ordered.Last().Date;
            string last  = ordered.First().Date;
            sb.AppendLine($"You purchased **{d.GrandTotal}** lottery tickets between **{first}** and **{last}** across **{ordered.Count}** active days, averaging **{d.AvgPerDay:F1}** tickets per day.");
        }
        else
        {
            sb.AppendLine($"You purchased **{d.GrandTotal}** lottery tickets, averaging **{d.AvgPerDay:F1}** per day.");
        }
        sb.AppendLine($"**Total Spent:** ${d.TotalSpent:F2}   **Total Won:** ${d.TotalWon:F2}   **Net:** {(net >= 0 ? "+" : "")}{net:F2}   **ROI:** {roi}");
        sb.AppendLine();

        // ── 2. Game Breakdown ─────────────────────────────────────────────────
        if (games.Count > 0)
        {
            sb.AppendLine("## 2. Game-by-Game Breakdown");
            foreach (var g in games)
            {
                string pct = d.GrandTotal > 0 ? $"{(double)g.TotalCount / d.GrandTotal * 100:F0}%" : "";
                sb.AppendLine($"- **{g.GameName}**: {g.TotalCount} tickets ({pct}) · **${g.TotalCost:F2}** spent · {g.DaysPlayed} day(s) · avg {g.AvgPerDay:F1}/day");
            }
            sb.AppendLine();
        }

        // ── 3. Financial Performance ──────────────────────────────────────────
        sb.AppendLine("## 3. Financial Performance");
        if (net >= 0)
            sb.AppendLine($"You are **net positive** by **${net:F2}** — you have won more than you spent! Your ROI of **{roi}** means for every dollar spent you got back **${retBack:F2}**.");
        else
            sb.AppendLine($"You are **net negative** by **${Math.Abs(net):F2}**. Your ROI of **{roi}** means for every dollar spent you got back **${retBack:F2}**. Setting a monthly budget can help keep lottery play fun.");

        if (ordered.Count > 0)
        {
            var bestWon   = ordered.OrderByDescending(p => p.Won).First();
            var mostSpent = ordered.OrderByDescending(p => p.Spent).First();
            if (bestWon.Won > 0)
                sb.AppendLine($"Your best winning day was **{bestWon.Date}** with **${bestWon.Won:F2}** won.");
            sb.AppendLine($"Your biggest spending day was **{mostSpent.Date}** with **${mostSpent.Spent:F2}** spent.");
            decimal avgDailySpend = d.TotalSpent / ordered.Count;
            sb.AppendLine($"Your average spend on active days is **${avgDailySpend:F2}**.");
        }
        sb.AppendLine();

        // ── 4. Spending Trends ────────────────────────────────────────────────
        sb.AppendLine("## 4. Spending Trends & Patterns");
        if (ordered.Count >= 4)
        {
            int     half         = ordered.Count / 2;
            decimal recentSpend  = ordered.Take(half).Sum(p => p.Spent);
            decimal earlierSpend = ordered.Skip(half).Sum(p => p.Spent);
            string  trend        = recentSpend > earlierSpend * 1.1m ? "**increasing ▲**"
                                 : recentSpend < earlierSpend * 0.9m ? "**decreasing ▼**"
                                 : "**steady →**";
            sb.AppendLine($"Comparing recent {half} days (${recentSpend:F2} spent) to earlier {half} days (${earlierSpend:F2} spent): your spending is {trend}.");
        }
        if (ordered.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"**Recent activity (last {Math.Min(ordered.Count, 10)} days):**");
            foreach (var p in ordered.Take(10))
            {
                decimal dayNet = p.Won - p.Spent;
                sb.AppendLine($"- {p.Date}  spent **${p.Spent:F2}**  won **${p.Won:F2}**  net {(dayNet >= 0 ? "+" : "")}{dayNet:F2}");
            }
        }
        sb.AppendLine();

        // ── 5. Key Observations ───────────────────────────────────────────────
        sb.AppendLine("## 5. Key Observations");
        if (games.Count > 0)
        {
            string topPct = d.GrandTotal > 0 ? $"{(double)games[0].TotalCount / d.GrandTotal * 100:F0}%" : "";
            sb.AppendLine($"- **{games[0].GameName}** is your most played game — {topPct} of all tickets.");
        }
        if (ordered.Count > 0)
            sb.AppendLine($"- You play on **{ordered.Count}** days averaging **${d.TotalSpent / ordered.Count:F2}** per active day.");
        if (net >= 0)
            sb.AppendLine($"- You are ahead by **${net:F2}** — a positive result overall!");
        else
        {
            sb.AppendLine($"- You are down **${Math.Abs(net):F2}** overall. Lottery is entertainment — enjoy it within your budget.");
            if (d.TotalSpent > 0 && ordered.Count > 0)
            {
                decimal monthly = d.TotalSpent / ordered.Count * 30m;
                sb.AppendLine($"- At your current pace, estimated monthly spend is around **${monthly:F2}**.");
            }
        }
        if (games.Count > 1)
            sb.AppendLine($"- You play **{games.Count}** different lottery games, showing nice variety in your play style.");
        sb.AppendLine();
        sb.AppendLine("*Tap **✨ Enhance with AI** below to get personalized AI-written insights powered by Google Gemini — free with a Google account.*");

        return sb.ToString();
    }

    // ── API call ──────────────────────────────────────────────────────────────

    public static async Task<string> RunAsync(AnalysisInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("NO_KEY");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };

        var body = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = BuildPrompt(input) } } }
            },
            generationConfig = new { maxOutputTokens = 2048, temperature = 0.7 }
        };

        using var payload  = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(GeminiUrl(ApiKey), payload, ct);
        string raw = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"API error {(int)response.StatusCode}:\n{raw}");

        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement
                  .GetProperty("candidates")[0]
                  .GetProperty("content")
                  .GetProperty("parts")[0]
                  .GetProperty("text")
                  .GetString() ?? "(empty response)";
    }

    // ── Prompt builder ────────────────────────────────────────────────────────

    static string BuildPrompt(AnalysisInput d)
    {
        decimal net = d.TotalWon - d.TotalSpent;
        string  roi = d.TotalSpent > 0
            ? $"{d.TotalWon / d.TotalSpent * 100m:F1}%"
            : "N/A";

        var sb = new StringBuilder();
        sb.AppendLine("You are a friendly data analyst reviewing California Lottery ticket purchase records for a mobile app user.");
        sb.AppendLine("Write a clear, detailed, and engaging report based solely on the real data below.");
        sb.AppendLine("Use specific dollar amounts and counts from the data throughout your report. Be non-judgmental — lottery play is entertainment.");
        sb.AppendLine();

        sb.AppendLine("══════════════════════════════════════════════");
        sb.AppendLine("OVERALL SUMMARY");
        sb.AppendLine("══════════════════════════════════════════════");
        sb.AppendLine($"  Total tickets purchased : {d.GrandTotal}");
        sb.AppendLine($"  Average tickets per day : {d.AvgPerDay:F1}");
        sb.AppendLine($"  Total spent             : ${d.TotalSpent:F2}");
        sb.AppendLine($"  Total won               : ${d.TotalWon:F2}");
        sb.AppendLine($"  Net (won − spent)       : {(net >= 0 ? "+" : "")}{net:F2}");
        sb.AppendLine($"  Return on investment    : {roi}  (won ÷ spent)");
        if (d.Daily.Count > 0)
        {
            sb.AppendLine($"  First activity date     : {d.Daily.Min(p => p.Date)}");
            sb.AppendLine($"  Latest activity date    : {d.Daily.Max(p => p.Date)}");
            sb.AppendLine($"  Active days             : {d.Daily.Count}");
        }
        sb.AppendLine();

        sb.AppendLine("══════════════════════════════════════════════");
        sb.AppendLine("BREAKDOWN BY GAME");
        sb.AppendLine("══════════════════════════════════════════════");
        foreach (var g in d.Games.Where(g => g.TotalCount > 0).OrderByDescending(g => g.TotalCount))
        {
            string pct = d.GrandTotal > 0 ? $"{(double)g.TotalCount / d.GrandTotal * 100:F0}%" : "";
            sb.AppendLine($"  {g.GameName,-22} {g.TotalCount,5} tickets ({pct})  " +
                          $"avg {g.AvgPerDay:F1}/day  ${g.TotalCost:F2} spent  " +
                          $"played {g.DaysPlayed} day(s)");
        }
        sb.AppendLine();

        if (d.Daily.Count > 0)
        {
            var ordered = d.Daily.OrderByDescending(p => p.Date).ToList();
            sb.AppendLine("══════════════════════════════════════════════");
            sb.AppendLine($"DAILY ACTIVITY  (newest first, {Math.Min(ordered.Count, 30)} of {ordered.Count} days)");
            sb.AppendLine("══════════════════════════════════════════════");
            foreach (var p in ordered.Take(30))
            {
                decimal dayNet = p.Won - p.Spent;
                sb.AppendLine($"  {p.Date}   spent ${p.Spent:F2}   won ${p.Won:F2}   " +
                              $"net {(dayNet >= 0 ? "+" : "")}{dayNet:F2}");
            }
            sb.AppendLine();

            // Spending trend (first half vs second half)
            int half = ordered.Count / 2;
            if (half >= 2)
            {
                decimal recentSpend = ordered.Take(half).Sum(p => p.Spent);
                decimal earlierSpend = ordered.Skip(half).Sum(p => p.Spent);
                sb.AppendLine("══════════════════════════════════════════════");
                sb.AppendLine("SPENDING TREND");
                sb.AppendLine("══════════════════════════════════════════════");
                sb.AppendLine($"  Recent half  ({half} days) : ${recentSpend:F2} spent");
                sb.AppendLine($"  Earlier half ({half} days) : ${earlierSpend:F2} spent");
                sb.AppendLine($"  Trend: spending is {(recentSpend > earlierSpend ? "INCREASING ▲" : recentSpend < earlierSpend ? "DECREASING ▼" : "FLAT →")}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("══════════════════════════════════════════════");
        sb.AppendLine("REPORT STRUCTURE REQUESTED");
        sb.AppendLine("══════════════════════════════════════════════");
        sb.AppendLine("Please write a thorough report with these exact section headers:");
        sb.AppendLine();
        sb.AppendLine("## 1. Overview");
        sb.AppendLine("   Summarize the big picture — total activity, time span, overall financial result.");
        sb.AppendLine();
        sb.AppendLine("## 2. Game-by-Game Analysis");
        sb.AppendLine("   Which games are played most? What % of total tickets does each represent?");
        sb.AppendLine("   Which game has the best/worst cost-to-play ratio?");
        sb.AppendLine();
        sb.AppendLine("## 3. Financial Performance");
        sb.AppendLine("   Dive into the spending vs winning numbers. ROI discussion.");
        sb.AppendLine("   Best winning days. Worst spending days.");
        sb.AppendLine();
        sb.AppendLine("## 4. Spending Trends & Patterns");
        sb.AppendLine("   Is activity increasing, decreasing, or steady?");
        sb.AppendLine("   Any noticeable daily patterns or streaks?");
        sb.AppendLine();
        sb.AppendLine("## 5. Key Takeaways");
        sb.AppendLine("   3–5 bullet-point highlights the user should know.");
        sb.AppendLine();
        sb.AppendLine("## 6. Friendly Tips");
        sb.AppendLine("   Practical, encouraging, and non-judgmental suggestions based on the data.");
        sb.AppendLine();
        sb.AppendLine("Format: use **bold** for key numbers and terms. Use ## for section headers.");
        sb.AppendLine("Target length: 600–800 words. Write it so a non-technical user finds it easy and interesting to read.");

        return sb.ToString();
    }

    // ── Markdown → styled HTML ────────────────────────────────────────────────

    public static string ToHtml(string markdown, AnalysisInput input)
    {
        decimal net      = input.TotalWon - input.TotalSpent;
        string  netStr   = (net >= 0 ? "+" : "") + $"${Math.Abs(net):F2}";
        string  netColor = net >= 0 ? "#66BB6A" : "#EF5350";
        string  roi      = input.TotalSpent > 0
            ? $"{input.TotalWon / input.TotalSpent * 100m:F1}%"
            : "N/A";

        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html><html><head>");
        html.AppendLine("<meta charset='utf-8'>");
        html.AppendLine("<meta name='viewport' content='width=device-width,initial-scale=1'>");
        html.AppendLine(@"<style>
body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;background:#0D1B2A;
     color:#D0D8E4;padding:16px 16px 32px;font-size:15px;line-height:1.7;margin:0}
.summary{background:#0E2039;border:1px solid #1A3A5F;border-radius:10px;
         padding:14px;margin-bottom:20px;display:grid;grid-template-columns:1fr 1fr;gap:6px 12px}
.s-row{display:flex;justify-content:space-between;align-items:center;
       border-bottom:1px solid #0A1628;padding:5px 0;grid-column:span 2}
.s-lbl{color:#90CAF9;font-size:13px}
.s-val{font-weight:bold;font-size:15px;color:#F59E0B}
.won{color:#66BB6A}
.spent{color:#EF5350}
h2{color:#64B5F6;font-size:15px;border-bottom:1px solid #1A2E45;
   padding-bottom:6px;margin:22px 0 8px}
strong{color:#90CAF9;font-weight:600}
p{margin:7px 0}
ul{margin:6px 0;padding-left:20px}
li{margin:4px 0}
</style>");
        html.AppendLine("</head><body>");

        // Pinned summary card
        html.AppendLine("<div class='summary'>");
        html.AppendLine($"<div class='s-row'><span class='s-lbl'>Total Tickets</span><span class='s-val'>{input.GrandTotal}</span></div>");
        html.AppendLine($"<div class='s-row'><span class='s-lbl'>Total Spent</span><span class='s-val spent'>${input.TotalSpent:F2}</span></div>");
        html.AppendLine($"<div class='s-row'><span class='s-lbl'>Total Won</span><span class='s-val won'>${input.TotalWon:F2}</span></div>");
        html.AppendLine($"<div class='s-row'><span class='s-lbl'>Net</span><span class='s-val' style='color:{netColor}'>{netStr}</span></div>");
        html.AppendLine($"<div class='s-row'><span class='s-lbl'>ROI</span><span class='s-val'>{roi}</span></div>");
        html.AppendLine("</div>");

        // Render markdown body
        foreach (string rawLine in markdown.Split('\n'))
        {
            string line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) { html.AppendLine("<br>"); continue; }

            if (line.StartsWith("## "))
                html.AppendLine($"<h2>{Escape(line[3..])}</h2>");
            else if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("• "))
                html.AppendLine($"<ul><li>{InlineFormat(line.TrimStart()[2..])}</li></ul>");
            else
                html.AppendLine($"<p>{InlineFormat(line)}</p>");
        }

        html.AppendLine("</body></html>");
        return html.ToString();
    }

    static string InlineFormat(string s)
    {
        s = Escape(s);
        s = Regex.Replace(s, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        return s;
    }

    static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    // ── CSV export ────────────────────────────────────────────────────────────

    public static string ToCsv(AnalysisInput d)
    {
        var sb = new StringBuilder();
        sb.AppendLine("LOTTERY STATS EXPORT");
        sb.AppendLine($"Generated,{DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine("SUMMARY");
        sb.AppendLine($"Total Tickets,{d.GrandTotal}");
        sb.AppendLine($"Avg Per Day,{d.AvgPerDay:F1}");
        sb.AppendLine($"Total Spent,{d.TotalSpent:F2}");
        sb.AppendLine($"Total Won,{d.TotalWon:F2}");
        decimal net = d.TotalWon - d.TotalSpent;
        sb.AppendLine($"Net,{net:F2}");
        if (d.TotalSpent > 0)
            sb.AppendLine($"ROI %,{d.TotalWon / d.TotalSpent * 100m:F1}%");
        sb.AppendLine();
        sb.AppendLine("GAME BREAKDOWN");
        sb.AppendLine("Game,Tickets,Avg/Day,Days Played,Total Spent");
        foreach (var g in d.Games.Where(g => g.TotalCount > 0).OrderByDescending(g => g.TotalCount))
            sb.AppendLine($"{g.GameName},{g.TotalCount},{g.AvgPerDay:F1},{g.DaysPlayed},{g.TotalCost:F2}");
        sb.AppendLine();
        sb.AppendLine("DAILY ACTIVITY");
        sb.AppendLine("Date,Spent,Won,Net");
        foreach (var p in d.Daily.OrderByDescending(p => p.Date))
            sb.AppendLine($"{p.Date},{p.Spent:F2},{p.Won:F2},{p.Won - p.Spent:F2}");
        return sb.ToString();
    }
}
