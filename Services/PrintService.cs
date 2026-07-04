using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DailyFantasyMAUI.Services;

public class PrintOptions
{
    public bool    IncludeSets      { get; set; } = true;
    public bool    IncludeFavorites { get; set; } = true;
    public bool    IncludePicks     { get; set; } = true;
    public bool    IncludeSummary   { get; set; } = true;
    public string? DataFilePath     { get; set; }   // null = skip data file section
}

public static class PrintService
{
    // Same game config as ViewSetsPage
    static readonly (string Caption, string PrefKey, int Rows, int Cols, int SpecialCol,
                     string SpecialLabel, string AccentColor, int Stride, bool HasTime)[] Games =
    {
        ("Fantasy 5",        "f5_set_", 10, 5, -1, "",     "#FF8F00", 5, false),
        ("Super Lotto Plus", "sl_set_", 10, 6,  5, "Mega", "#7B1FA2", 6, false),
        ("Daily 3",          "d3_set_", 10, 3, -1, "",     "#1565C0", 3, false),
        ("Daily 4",          "d4_set_", 10, 4, -1, "",     "#00695C", 4, false),
        ("Powerball",        "pb_set_", 10, 6,  5, "PB",   "#C62828", 6, false),
        ("Mega Millions",    "mm_set_", 10, 6,  5, "MB",   "#F57F17", 6, false),
        ("Daily Derby",      "dd_set_", 10, 3, -1, "",     "#5D4037", 4, true),
    };

    // Same as SummaryPage
    static readonly (string Key, string Name, string Color)[] GameDefs =
    [
        ("F5", "Fantasy 5",     "#FF8F00"),
        ("SL", "Super Lotto",   "#7B1FA2"),
        ("PB", "Powerball",     "#C62828"),
        ("MM", "Mega Millions", "#F57F17"),
        ("D3", "Daily 3",       "#1565C0"),
        ("D4", "Daily 4",       "#00695C"),
        ("DD", "Daily Derby",   "#5D4037"),
    ];

    // Same as MyFavoritePage
    static readonly Dictionary<string, (int SpecialCol, string SpecialLabel)> FavConfig = new()
    {
        { "f5_set_", (-1, "")      },
        { "sl_set_", ( 5, "Mega")  },
        { "pb_set_", ( 5, "PB")    },
        { "mm_set_", ( 5, "MB")    },
        { "d3_set_", (-1, "")      },
        { "d4_set_", (-1, "")      },
        { "dd_set_", (-1, "")      },
    };

    // ── Known data files (mirrors DataViewerPage) ─────────────────────────────

    public static List<(string Label, string Path)> GetDataFiles()
    {
        string appDir = FileSystem.AppDataDirectory;
        var files = new List<(string Label, string Path)>
        {
            ("Fantasy 5 CSV",  System.IO.Path.Combine(appDir, "data", "myFantasy5.csv")),
            ("SuperLotto CSV", System.IO.Path.Combine(appDir, "data", "mySuperlotto.csv")),
            ("My Combos",      System.IO.Path.Combine(appDir, "myCombos.txt")),
            ("Archives",       System.IO.Path.Combine(appDir, "archives.json")),
            ("Fantasy 5 Data", System.IO.Path.Combine(appDir, "fantasy5", "fantasy5.txt")),
            ("Error Log",      System.IO.Path.Combine(appDir, "error_log.txt")),
        };

        string favDir = GetFavDir();
        if (Directory.Exists(favDir))
        {
            foreach (var f in Directory.GetFiles(favDir, "*.*")
                         .Where(f => f.EndsWith(".json") || f.EndsWith(".txt"))
                         .OrderByDescending(f => f))
                files.Add(($"★ {System.IO.Path.GetFileName(f)}", f));
        }

        return files;
    }

    // ── Main entry point ──────────────────────────────────────────────────────

    public static async Task<string> BuildHtmlAsync(PrintOptions opts)
    {
        var sb = new StringBuilder();
        sb.Append(BuildHeader());

        if (opts.IncludeSets)      sb.Append(BuildSetsHtml());
        if (opts.IncludeFavorites) sb.Append(await BuildFavoritesHtmlAsync());
        if (opts.IncludePicks)     sb.Append(await BuildPicksHtmlAsync());
        if (opts.IncludeSummary)   sb.Append(await BuildSummaryHtmlAsync());
        if (opts.DataFilePath != null) sb.Append(await BuildDataFileHtmlAsync(opts.DataFilePath));

        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    // ── HTML header + CSS (Word/document style) ───────────────────────────────

    // Use $$""" so CSS braces { } are literal; real interpolations use {{expr}}
    static string BuildHeader() => $$"""
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width,initial-scale=1">
        <title>CA Lottery Report</title>
        <style>
          * { box-sizing: border-box; margin: 0; padding: 0; }

          body {
            font-family: 'Calibri', 'Segoe UI', Arial, sans-serif;
            font-size: 11pt;
            line-height: 1.45;
            color: #212121;
            background: #B0BEC5;
            padding: 16px 10px;
          }

          .page {
            background: #ffffff;
            max-width: 720px;
            margin: 0 auto 20px;
            padding: 36px 42px 42px;
            box-shadow: 0 3px 14px rgba(0,0,0,0.30);
          }

          .doc-header {
            border-bottom: 3px solid #1E2733;
            padding-bottom: 10px;
            margin-bottom: 20px;
          }
          .doc-title {
            font-size: 20pt;
            font-weight: 700;
            color: #1E2733;
            letter-spacing: -0.5px;
          }
          .doc-meta {
            font-size: 9.5pt;
            color: #607D8B;
            margin-top: 3px;
          }
          .doc-divider {
            height: 1px;
            background: #CFD8DC;
            margin: 4px 0 0;
          }

          .h1 {
            font-size: 14pt;
            font-weight: 700;
            color: #ffffff;
            padding: 7px 14px 7px 10px;
            margin: 22px 0 10px;
            border-left: 6px solid rgba(0,0,0,0.20);
            border-radius: 2px;
            page-break-after: avoid;
          }

          .h2 {
            font-size: 11pt;
            font-weight: 700;
            margin: 14px 0 5px;
            padding: 4px 0 4px 10px;
            border-left: 4px solid currentColor;
            page-break-after: avoid;
          }

          .h3 {
            font-size: 10pt;
            font-weight: 600;
            color: #546E7A;
            margin: 8px 0 3px;
            text-transform: uppercase;
            letter-spacing: 0.5px;
          }

          .num-row {
            display: flex;
            align-items: center;
            gap: 4px;
            margin: 4px 0;
            flex-wrap: nowrap;
          }
          .row-num {
            font-size: 9pt;
            color: #9E9E9E;
            width: 20px;
            text-align: right;
            flex-shrink: 0;
            font-weight: 600;
          }
          .ball {
            display: inline-flex;
            align-items: center;
            justify-content: center;
            width: 32px;
            height: 32px;
            border-radius: 50%;
            font-size: 12pt;
            font-weight: 700;
            flex-shrink: 0;
            border: 2px solid rgba(0,0,0,0.10);
          }
          .ball-main { background: #ECEFF1; color: #1E2733; }
          .ball-sp   { background: #B71C1C; color: #ffffff; border-color: #7F0000; }
          .ball-sep  {
            width: 2px; height: 28px;
            background: #EF5350;
            border-radius: 1px;
            flex-shrink: 0;
            margin: 0 3px;
          }
          .ball-time {
            font-size: 9pt;
            font-weight: 600;
            color: #4E342E;
            background: #EFEBE9;
            border-radius: 10px;
            padding: 2px 8px;
            margin-left: 6px;
            border: 1px solid #D7CCC8;
          }

          .picks-grid {
            display: flex;
            flex-wrap: wrap;
            gap: 5px;
            margin-top: 5px;
          }
          .chip {
            font-family: 'Consolas', 'Courier New', monospace;
            font-size: 10pt;
            font-weight: 600;
            background: #ECEFF1;
            border: 1px solid #CFD8DC;
            border-radius: 4px;
            padding: 4px 10px;
            color: #1E2733;
          }

          .doc-table {
            width: 100%;
            border-collapse: collapse;
            margin: 6px 0 10px;
            font-size: 10pt;
          }
          .doc-table thead tr { background: #1E2733; color: #ffffff; }
          .doc-table th {
            padding: 6px 10px;
            text-align: left;
            font-weight: 600;
            font-size: 9.5pt;
          }
          .doc-table td {
            padding: 5px 10px;
            border-bottom: 1px solid #ECEFF1;
            vertical-align: top;
          }
          .doc-table tr:nth-child(even) td { background: #F5F7F9; }
          .doc-table tr:last-child td { border-bottom: none; }

          .game-total {
            font-size: 9.5pt;
            color: #607D8B;
            margin-bottom: 6px;
            padding-left: 2px;
          }

          .grand-total {
            font-size: 13pt;
            font-weight: 700;
            color: #1B5E20;
            border-top: 2px solid #A5D6A7;
            padding-top: 8px;
            margin-top: 12px;
          }

          pre {
            font-family: 'Consolas', 'Courier New', monospace;
            font-size: 8pt;
            white-space: pre-wrap;
            word-break: break-all;
            background: #FAFAFA;
            border: 1px solid #E0E0E0;
            border-left: 4px solid #90A4AE;
            padding: 10px 12px;
            line-height: 1.6;
            border-radius: 2px;
          }

          .note { font-style: italic; color: #9E9E9E; font-size: 10pt; }

          @media print {
            body { background: #ffffff; padding: 0; }
            .page { box-shadow: none; padding: 0; max-width: 100%; }
            @page { margin: 0.75in; }
            .h1, .ball-main, .ball-sp, .ball-sep, .doc-table thead tr {
              -webkit-print-color-adjust: exact;
              print-color-adjust: exact;
            }
            .page-break { page-break-before: always; }
          }
        </style>
        </head>
        <body>
        <div class="page">
        <div class="doc-header">
          <div class="doc-title">CA Lottery Report</div>
          <div class="doc-meta">Generated: {{DateTime.Now:dddd, MMMM dd, yyyy}} &mdash; {{DateTime.Now:h:mm tt}}</div>
          <div class="doc-divider"></div>
        </div>
        <div>
        """;

    // ── SETS ──────────────────────────────────────────────────────────────────

    static string BuildSetsHtml()
    {
        var sb = new StringBuilder();
        sb.Append("<div class='h1' style='background:#37474F'>My Saved Sets</div>");

        bool anyGame = false;
        foreach (var g in Games)
        {
            string exclKey = ExclKey(g.PrefKey);
            if (Preferences.Get($"excl_game_{exclKey}", false)) continue;

            var slots = new List<(int Slot, string[] Vals)>();
            for (int s = 0; s < 10; s++)
            {
                string raw = Preferences.Get($"{g.PrefKey}{s}", "");
                if (string.IsNullOrEmpty(raw)) continue;
                if (Preferences.Get($"excl_set_{exclKey}_{s}", false)) continue;
                slots.Add((s, raw.Split('|')));
            }
            if (slots.Count == 0) continue;

            anyGame = true;
            int mainCols = g.SpecialCol >= 0 ? g.SpecialCol : g.Cols;

            sb.Append($"<div class='h2' style='color:{g.AccentColor}'>{g.Caption}</div>");

            foreach (var (slot, vals) in slots)
            {
                sb.Append($"<div class='h3'>Set {slot + 1}</div>");

                for (int r = 0; r < g.Rows; r++)
                {
                    bool rowEmpty = true;
                    for (int c = 0; c < g.Cols; c++)
                    {
                        int i = r * g.Stride + c;
                        if (i < vals.Length && !string.IsNullOrWhiteSpace(vals[i])) { rowEmpty = false; break; }
                    }
                    if (rowEmpty) continue;

                    sb.Append($"<div class='num-row'><span class='row-num'>{r + 1}.</span>");
                    for (int c = 0; c < mainCols; c++)
                    {
                        int i = r * g.Stride + c;
                        string v = i < vals.Length ? Esc(vals[i]) : "—";
                        sb.Append($"<span class='ball ball-main'>{v}</span>");
                    }
                    if (g.SpecialCol >= 0)
                    {
                        int i = r * g.Stride + g.SpecialCol;
                        string v = i < vals.Length ? Esc(vals[i]) : "—";
                        sb.Append("<span class='ball-sep'></span>");
                        sb.Append($"<span class='ball ball-sp'>{v}</span>");
                    }
                    if (g.HasTime)
                    {
                        int i = r * g.Stride + 3;
                        string v = i < vals.Length ? Esc(vals[i]) : "";
                        if (!string.IsNullOrWhiteSpace(v))
                            sb.Append($"<span class='ball-time'>&#9201; {v}</span>");
                    }
                    sb.Append("</div>");
                }
            }
        }

        if (!anyGame)
            sb.Append("<p class='note'>No sets saved.</p>");

        return sb.ToString();
    }

    static string ExclKey(string prefKey) => prefKey switch
    {
        "f5_set_" => "f5",
        "sl_set_" => "sl",
        "d3_set_" => "d3",
        "d4_set_" => "d4",
        "pb_set_" => "pb",
        "mm_set_" => "mm",
        "dd_set_" => "dd",
        _ => prefKey.Replace("_set_", "")
    };

    // ── FAVORITES ─────────────────────────────────────────────────────────────

    static async Task<string> BuildFavoritesHtmlAsync()
    {
        var sb = new StringBuilder();
        sb.Append("<div class='h1' style='background:#6A1B9A'>My Favorites</div>");

        string favDir = GetFavDir();
        if (!Directory.Exists(favDir))
        {
            sb.Append("<p class='note'>No favorites saved.</p>");
            return sb.ToString();
        }

        var files = Directory.GetFiles(favDir, "*.json").OrderByDescending(f => f).ToList();
        if (files.Count == 0)
        {
            sb.Append("<p class='note'>No favorites saved.</p>");
            return sb.ToString();
        }

        foreach (var file in files)
        {
            try
            {
                string json = await File.ReadAllTextAsync(file);
                var root    = JsonNode.Parse(json)?.AsObject();
                var setsArr = root?["sets"]?.AsArray();
                if (setsArr == null) continue;

                sb.Append($"<div class='h3'>{Esc(Path.GetFileNameWithoutExtension(file))}</div>");

                foreach (var setNode in setsArr)
                {
                    var o = setNode?.AsObject();
                    string label   = o?["label"]?.GetValue<string>()   ?? "?";
                    string prefKey = o?["prefKey"]?.GetValue<string>()  ?? "";
                    string data    = o?["data"]?.GetValue<string>()     ?? "";

                    var cfg = FavConfig.TryGetValue(prefKey, out var cv) ? cv : (-1, "");
                    var vals = data.Split('|');
                    int stride   = vals.Length >= 10 ? vals.Length / 10 : (cfg.Item1 >= 0 ? cfg.Item1 + 1 : 3);
                    int mainCols = cfg.Item1 >= 0 ? cfg.Item1 : stride;

                    sb.Append($"<div style='font-size:10pt;font-weight:600;color:#455A64;margin:6px 0 2px'>{Esc(label)}</div>");

                    for (int r = 0; r < 10; r++)
                    {
                        bool empty = true;
                        for (int c = 0; c < stride; c++)
                        {
                            int i = r * stride + c;
                            if (i < vals.Length && !string.IsNullOrWhiteSpace(vals[i])) { empty = false; break; }
                        }
                        if (empty) continue;

                        sb.Append($"<div class='num-row'><span class='row-num'>{r + 1}.</span>");
                        for (int c = 0; c < mainCols; c++)
                        {
                            int i = r * stride + c;
                            string v = i < vals.Length ? Esc(vals[i]) : "—";
                            sb.Append($"<span class='ball ball-main'>{v}</span>");
                        }
                        if (cfg.Item1 >= 0)
                        {
                            int i = r * stride + cfg.Item1;
                            string v = i < vals.Length ? Esc(vals[i]) : "—";
                            sb.Append("<span class='ball-sep'></span>");
                            sb.Append($"<span class='ball ball-sp'>{v}</span>");
                        }
                        sb.Append("</div>");
                    }
                }
            }
            catch { /* skip corrupt */ }
        }

        return sb.ToString();
    }

    // ── PICKS (Main Page listbox autosave) ────────────────────────────────────

    static async Task<string> BuildPicksHtmlAsync()
    {
        var sb = new StringBuilder();
        sb.Append("<div class='h1' style='background:#1565C0'>Main Page Picks</div>");

        var modeFiles = new[]
        {
            ("Fantasy 5",   "_autosave_F5.txt",  "#FF8F00"),
            ("Super Lotto", "_autosave_SL.txt",  "#7B1FA2"),
            ("Daily 3",     "_autosave_D3.txt",  "#1565C0"),
        };

        bool any = false;
        foreach (var (label, fname, color) in modeFiles)
        {
            string path = Path.Combine(FileSystem.AppDataDirectory, fname);
            if (!File.Exists(path)) continue;

            string[] lines;
            try { lines = await File.ReadAllLinesAsync(path); }
            catch { continue; }

            var filled = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            if (filled.Length == 0) continue;

            any = true;
            sb.Append($"<div class='h2' style='color:{color}'>" +
                      $"{label} <span style='font-weight:400;font-size:9.5pt;color:#9E9E9E'>" +
                      $"({filled.Length} pick{(filled.Length == 1 ? "" : "s")})</span></div>");

            sb.Append("<div class='picks-grid'>");
            foreach (var line in filled)
                sb.Append($"<span class='chip'>{Esc(line)}</span>");
            sb.Append("</div>");
        }

        if (!any)
            sb.Append("<p class='note'>No picks saved.</p>");

        return sb.ToString();
    }

    // ── SUMMARY (winnings log) ────────────────────────────────────────────────

    static async Task<string> BuildSummaryHtmlAsync()
    {
        var sb = new StringBuilder();
        sb.Append("<div class='h1' style='background:#2E7D32'>Winnings Summary</div>");

        string path = Path.Combine(FileSystem.AppDataDirectory, "winnings_log.json");
        if (!File.Exists(path))
        {
            sb.Append("<p class='note'>No winnings recorded.</p>");
            return sb.ToString();
        }

        List<WinningRecord> records;
        try
        {
            string json = await File.ReadAllTextAsync(path);
            records = JsonSerializer.Deserialize<List<WinningRecord>>(json) ?? [];
        }
        catch
        {
            sb.Append("<p class='note'>Error reading winnings log.</p>");
            return sb.ToString();
        }

        if (records.Count == 0)
        {
            sb.Append("<p class='note'>No winnings recorded.</p>");
            return sb.ToString();
        }

        decimal grandTotal = 0;
        int totalFree = 0;

        foreach (var (key, name, color) in GameDefs)
        {
            var gameRecs = records
                .Where(r => r.Game == key)
                .OrderByDescending(r => r.Date)
                .ToList();
            if (gameRecs.Count == 0) continue;

            decimal gameTotal = gameRecs.Where(r => !r.IsFreeTicket).Sum(r => r.Amount);
            int gameFree      = gameRecs.Count(r => r.IsFreeTicket);
            grandTotal += gameTotal;
            totalFree  += gameFree;

            sb.Append($"<div class='h2' style='color:{color}'>{name}</div>");
            sb.Append($"<div class='game-total'>${gameTotal:N2} cash &nbsp;+&nbsp; {gameFree} free ticket(s)</div>");

            sb.Append("<table class='doc-table'>");
            sb.Append("<thead><tr><th>Date</th><th>Numbers</th><th>Amount</th><th>Note</th></tr></thead>");
            sb.Append("<tbody>");
            foreach (var r in gameRecs)
            {
                string amt = r.IsFreeTicket ? "Free Ticket" : $"${r.Amount:N2}";
                sb.Append($"<tr><td>{Esc(r.Date)}</td><td>{Esc(r.Numbers)}</td>" +
                           $"<td>{amt}</td><td>{Esc(r.Note)}</td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        sb.Append($"<div class='grand-total'>Grand Total: ${grandTotal:N2} cash " +
                  $"+ {totalFree} free ticket(s)</div>");
        return sb.ToString();
    }

    // ── DATA FILE ─────────────────────────────────────────────────────────────

    static async Task<string> BuildDataFileHtmlAsync(string filePath)
    {
        string fname = Path.GetFileName(filePath);
        string ext   = Path.GetExtension(filePath).ToLowerInvariant();
        var sb = new StringBuilder();
        sb.Append($"<div class='h1' style='background:#455A64'>Data File: {Esc(fname)}</div>");

        if (!File.Exists(filePath))
        {
            sb.Append("<p class='note'>File not found.</p>");
            return sb.ToString();
        }

        try
        {
            string content = await File.ReadAllTextAsync(filePath);
            string trimmed = content.TrimStart();

            // HTML file — strip tags and show plain text
            if (trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("<html",      StringComparison.OrdinalIgnoreCase))
            {
                // Remove script/style blocks first
                string stripped = System.Text.RegularExpressions.Regex.Replace(
                    content, @"<(script|style)[^>]*>[\s\S]*?<\/\1>",
                    "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                // Remove all remaining tags
                stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"<[^>]+>", "");
                // Collapse whitespace
                stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"[ \t]+", " ");
                stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"\n{3,}", "\n\n");
                stripped = stripped.Trim();

                var lines = stripped.Split('\n');
                const int limit = 400;
                sb.Append("<pre>");
                foreach (var line in lines.Take(limit))
                {
                    string l = line.Trim();
                    if (!string.IsNullOrEmpty(l))
                        sb.Append(Esc(l) + "\n");
                }
                if (lines.Length > limit)
                    sb.Append($"\n&#8230; ({lines.Length - limit} more lines not shown)");
                sb.Append("</pre>");
                return sb.ToString();
            }

            // CSV file — render as table
            if (ext == ".csv")
            {
                var rows = content.Split('\n')
                    .Select(l => l.TrimEnd('\r'))
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToArray();
                const int rowLimit = 200;
                sb.Append("<table class='doc-table'><tbody>");
                bool isFirst = true;
                foreach (var row in rows.Take(rowLimit))
                {
                    var cells = row.Split(',');
                    if (isFirst)
                    {
                        sb.Append("<thead><tr>");
                        foreach (var c in cells) sb.Append($"<th>{Esc(c.Trim())}</th>");
                        sb.Append("</tr></thead><tbody>");
                        isFirst = false;
                    }
                    else
                    {
                        sb.Append("<tr>");
                        foreach (var c in cells) sb.Append($"<td>{Esc(c.Trim())}</td>");
                        sb.Append("</tr>");
                    }
                }
                sb.Append("</tbody></table>");
                if (rows.Length > rowLimit)
                    sb.Append($"<p class='note'>&#8230; ({rows.Length - rowLimit} more rows not shown)</p>");
                return sb.ToString();
            }

            // JSON — pretty print
            if (ext == ".json")
            {
                try
                {
                    var doc = System.Text.Json.JsonDocument.Parse(content);
                    string pretty = System.Text.Json.JsonSerializer.Serialize(
                        doc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    const int limit = 300;
                    var lines2 = pretty.Split('\n');
                    sb.Append("<pre>");
                    foreach (var line in lines2.Take(limit))
                        sb.Append(Esc(line) + "\n");
                    if (lines2.Length > limit)
                        sb.Append($"\n&#8230; ({lines2.Length - limit} more lines not shown)");
                    sb.Append("</pre>");
                    return sb.ToString();
                }
                catch { /* fall through to plain text */ }
            }

            // Plain text / .txt
            {
                string[] lines = content.Split('\n');
                const int limit = 500;
                sb.Append("<pre>");
                foreach (var line in lines.Take(limit))
                    sb.Append(Esc(line.TrimEnd('\r')) + "\n");
                if (lines.Length > limit)
                    sb.Append($"\n&#8230; ({lines.Length - limit} more lines not shown)");
                sb.Append("</pre>");
            }
        }
        catch (Exception ex)
        {
            sb.Append($"<p class='note'>Error: {Esc(ex.Message)}</p>");
        }

        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static string GetFavDir()
    {
#if ANDROID
        var ext = Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
                  ?? Microsoft.Maui.Storage.FileSystem.AppDataDirectory;
        return Path.Combine(ext, "MyFavorite");
#else
        return Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "data", "MyFavorite");
#endif
    }

    static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
    }
}
