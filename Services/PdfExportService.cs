using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;

namespace DailyFantasyMAUI.Services;

/// Embeds the app's own bundled OpenSans TTFs so PdfSharpCore can render text on
/// Android without relying on system/GDI fonts (which it can't access there).
class OpenSansFontResolver : IFontResolver
{
    public static byte[] RegularBytes = Array.Empty<byte>();
    public static byte[] BoldBytes = Array.Empty<byte>();

    public string DefaultFontName => "OpenSansRegular";

    public byte[] GetFont(string faceName) => faceName == "OpenSansBold" ? BoldBytes : RegularBytes;

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new FontResolverInfo(isBold ? "OpenSansBold" : "OpenSansRegular");
}

/// Renders real, table-formatted, bookmarked PDF reports via PdfSharpCore — styled
/// to match the app's own established dark-navy/gold design language. Every
/// exportable report in the app should go through this, not a plain CSV/text dump.
public static class PdfExportService
{
    const double PageWidth  = 612; // US Letter, 72dpi points
    const double PageHeight = 792;
    const double MarginX    = 48;
    const double ContentRight = PageWidth - MarginX;

    static readonly XColor Navy       = Hex("#1E2733");
    static readonly XColor Gold       = Hex("#D4A94A");
    static readonly XColor HeaderRow  = Hex("#F1EDE2");
    static readonly XColor RowAlt     = Hex("#FAF9F5");
    static readonly XColor White      = XColors.White;
    static readonly XColor TextDark   = Hex("#1C2430");
    static readonly XColor TextGray   = Hex("#6B7280");
    static readonly XColor BorderGray = Hex("#E2DDD0");
    static readonly XColor Green      = Hex("#2E7D32");
    static readonly XColor Red        = Hex("#C62828");
    static readonly XColor NetGreen   = Hex("#81C784");
    static readonly XColor NetRed     = Hex("#EF9A9A");

    static XColor Hex(string h)
    {
        h = h.TrimStart('#');
        return XColor.FromArgb(Convert.ToInt32(h[..2], 16), Convert.ToInt32(h[2..4], 16), Convert.ToInt32(h[4..6], 16));
    }

    static bool _fontsLoaded = false;
    static async Task EnsureFontsLoadedAsync()
    {
        if (_fontsLoaded) return;
        using (var s = await FileSystem.OpenAppPackageFileAsync("fonts/OpenSans-Regular.ttf"))
        using (var ms = new MemoryStream()) { await s.CopyToAsync(ms); OpenSansFontResolver.RegularBytes = ms.ToArray(); }
        using (var s = await FileSystem.OpenAppPackageFileAsync("fonts/OpenSans-Semibold.ttf"))
        using (var ms = new MemoryStream()) { await s.CopyToAsync(ms); OpenSansFontResolver.BoldBytes = ms.ToArray(); }
        GlobalFontSettings.FontResolver = new OpenSansFontResolver();
        _fontsLoaded = true;
    }

    static XFont Font(double size, bool bold) => new("OpenSans", size, bold ? XFontStyle.Bold : XFontStyle.Regular);

    // ── Report 1: single-month breakdown — one file per month, so opening "July" can ──
    // ── never scroll into "August" (a PDF viewer will always let you scroll past a  ──
    // ── bookmark within one file — the only way to truly confine a month is its own  ──
    // ── standalone file). Call once per month; the caller (TicketCalendarPage) is    ──
    // ── responsible for looping over months to produce one file each.               ──

    public static async Task<string> GenerateSingleMonthReportAsync(
        string filePath, string monthLabel, decimal spent, decimal won, decimal net,
        List<TicketLogEntry> tickets, Func<string, string> gameName)
    {
        await EnsureFontsLoadedAsync();
        var doc = new Doc();

        DrawHeaderBand(doc, monthLabel, $"Generated {DateTime.Now:MMMM d, yyyy \\a\\t h:mm tt}");
        double y = 132;

        doc.Gfx.DrawRectangle(new XSolidBrush(HeaderRow), MarginX, y, ContentRight - MarginX, 26);
        DrawText(doc.Gfx, monthLabel, MarginX + 6, y, 26, 12, true, TextDark, XStringAlignment.Near);
        DrawText(doc.Gfx, $"Spent ${spent:N2}   Won ${won:N2}   Net {(net >= 0 ? "+" : "")}${net:N2}",
            MarginX, y, 26, 10, true, net >= 0 ? Green : Red, XStringAlignment.Far, ContentRight - MarginX);
        y += 34;

        y = DrawTable(doc, y, TicketCols, TicketRightAlign,
            tickets.Select(t => new[]
            {
                t.Date, gameName(t.Game), t.Numbers ?? "", FormatExtra(t.Game, t.Extra),
                string.IsNullOrEmpty(t.PlayFrom) ? "" : $"{t.PlayFrom}-{t.PlayTo}",
                t.IsFreePlay ? "Yes" : "",
            }).ToList(),
            "No tickets logged this month.");

        double totalsY = EnsureRowSpace(doc, y + 4, 34);
        doc.Gfx.DrawRectangle(new XSolidBrush(Navy), MarginX, totalsY, ContentRight - MarginX, 34);
        DrawText(doc.Gfx, $"{monthLabel.ToUpper()} TOTAL", MarginX + 6, totalsY, 34, 12, true, White, XStringAlignment.Near);
        DrawText(doc.Gfx, $"Spent: ${spent:N2}   Won: ${won:N2}   Net: {(net >= 0 ? "+" : "")}${net:N2}",
            MarginX, totalsY, 34, 12, true, net >= 0 ? NetGreen : NetRed, XStringAlignment.Far, ContentRight - MarginX);

        return doc.Save(filePath);
    }

    // ── Report 1b: combined all-time report — one page per month in a single file, ──
    // ── with a clickable top tab row (all months visible, tap to jump). Scrolling   ──
    // ── past a month's last page still naturally continues into the next month —    ──
    // ── that's an unavoidable property of a single PDF file, not fixable here. Use  ──
    // ── GenerateSingleMonthReportAsync above instead when a month must never show   ──
    // ── anything but itself.                                                       ──

    const double MbColMonthX = MarginX;
    const double MbColSpentR = MarginX + 220 + 96;
    const double MbColWonR   = MarginX + 220 + 96 + 96;
    const double MbColNetR   = ContentRight;

    public static async Task<string> GenerateMonthlyBreakdownReportAsync(
        string filePath,
        List<(string MonthLabel, decimal Spent, decimal Won, decimal Net, List<TicketLogEntry> Tickets)> rows,
        decimal grandSpent, decimal grandWon, decimal grandNet,
        Func<string, string> gameName)
    {
        await EnsureFontsLoadedAsync();
        var doc = new Doc();

        DrawHeaderBand(doc, "All-Time Monthly Breakdown");
        doc.Document.Outlines.Add("ALL-TIME TOTAL", doc.Page, true);
        double y = DrawGrandTotalBar(doc, 132, grandSpent, grandWon, grandNet) + 18;

        var allMonthLabels = rows.Select(r => r.MonthLabel).ToList();
        doc.AllTabLabels = allMonthLabels;
        var monthStartPageIndex = new Dictionary<string, int>();

        bool firstMonth = true;
        foreach (var r in rows)
        {
            // One month per page, each with its own bookmark — so a long report can be
            // jumped to directly instead of scrolled through top to bottom.
            doc.TabLabel = r.MonthLabel;
            if (!firstMonth) { doc.NewPage(); y = 48; }
            else { DrawMonthTabStack(doc, allMonthLabels, r.MonthLabel); }
            firstMonth = false;
            monthStartPageIndex[r.MonthLabel] = doc.PageNum; // 1-based — AddDocumentLink requires page number >= 1, not a 0-based index
            doc.Document.Outlines.Add(r.MonthLabel, doc.Page, true);

            doc.Gfx.DrawRectangle(new XSolidBrush(HeaderRow), MarginX, y, ContentRight - MarginX, 26);
            DrawText(doc.Gfx, r.MonthLabel, MbColMonthX + 6, y, 26, 12, true, TextDark, XStringAlignment.Near);
            DrawText(doc.Gfx, $"Spent ${r.Spent:N2}   Won ${r.Won:N2}   Net {(r.Net >= 0 ? "+" : "")}${r.Net:N2}",
                MarginX, y, 26, 10, true, r.Net >= 0 ? Green : Red, XStringAlignment.Far, ContentRight - MarginX);
            y += 30;

            y = DrawTable(doc, y, TicketCols, TicketRightAlign,
                r.Tickets.Select(t => new[]
                {
                    t.Date, gameName(t.Game), t.Numbers ?? "", FormatExtra(t.Game, t.Extra),
                    string.IsNullOrEmpty(t.PlayFrom) ? "" : $"{t.PlayFrom}-{t.PlayTo}",
                    t.IsFreePlay ? "Yes" : "",
                }).ToList(),
                "No tickets logged this month.");
        }

        double totalsY = EnsureRowSpace(doc, y + 4, 34 + 16);
        DrawGrandTotalBar(doc, totalsY, grandSpent, grandWon, grandNet);

        AddMonthTabLinks(doc, allMonthLabels, monthStartPageIndex);
        return doc.Save(filePath);
    }

    // ── Report 2: Ticket Calendar export (Day / Month / All-Time), bookmarked by section ──

    static readonly (string Header, double Width)[] SpendCols =
    {
        ("DATE", 66), ("GAME", 96), ("TIX", 56), ("EACH", 66), ("TOTAL", 76), ("NOTE", 156),
    };
    static readonly int[] SpendRightAlign = { 2, 3, 4 };

    static readonly (string Header, double Width)[] TicketCols =
    {
        ("DATE", 66), ("GAME", 80), ("NUMBERS", 170), ("EXTRA", 90), ("PLAY RANGE", 64), ("FREE", 46),
    };
    static readonly int[] TicketRightAlign = Array.Empty<int>();

    static readonly (string Header, double Width)[] WinCols =
    {
        ("DATE", 60), ("GAME", 80), ("AMOUNT", 70), ("FREE", 50), ("NOTE", 90), ("NUMBERS", 166),
    };
    static readonly int[] WinRightAlign = { 2 };

    public static async Task<string> GenerateTicketExportReportAsync(
        string filePath, string title, string subtitle,
        List<SpendingRecord> spending, List<TicketLogEntry> tickets, List<WinningRecord> wins,
        Func<string, string> gameName)
    {
        await EnsureFontsLoadedAsync();
        var doc = new Doc();
        DrawHeaderBand(doc, title, subtitle);
        double y = 156;

        decimal totalSpent = spending.Sum(r => r.TotalCost);
        decimal totalWon   = wins.Where(w => !w.IsFreeTicket).Sum(w => w.Amount);

        doc.Document.Outlines.Add("Spending Summary", doc.Page, true);
        y = DrawSectionTitle(doc, y, "Spending Summary");
        y = DrawTable(doc, y, SpendCols, SpendRightAlign,
            spending.OrderBy(r => r.Date).ThenBy(r => r.Game).Select(r => new[]
            {
                r.Date, gameName(r.Game), r.TicketCount.ToString(), $"${r.CostEach:N2}", $"${r.TotalCost:N2}", r.Note ?? "",
            }).ToList(),
            "No spending logged.");

        doc.Document.Outlines.Add("Every Ticket Purchased", doc.Page, true);
        y = DrawSectionTitle(doc, y, "Every Ticket Purchased");
        y = DrawTable(doc, y, TicketCols, TicketRightAlign,
            tickets.OrderBy(t => t.Date).ThenBy(t => t.Game).ThenBy(t => t.Slot).ThenBy(t => t.Row).Select(t => new[]
            {
                t.Date, gameName(t.Game), t.Numbers ?? "", FormatExtra(t.Game, t.Extra),
                string.IsNullOrEmpty(t.PlayFrom) ? "" : $"{t.PlayFrom}-{t.PlayTo}",
                t.IsFreePlay ? "Yes" : "",
            }).ToList(),
            "No tickets logged.");

        doc.Document.Outlines.Add("Wins", doc.Page, true);
        y = DrawSectionTitle(doc, y, "Wins");
        y = DrawTable(doc, y, WinCols, WinRightAlign,
            wins.OrderBy(w => w.Date).Select(w => new[]
            {
                w.Date, gameName(w.Game), w.IsFreeTicket ? "—" : $"${w.Amount:N2}", w.IsFreeTicket ? "Yes" : "",
                w.Note ?? "", w.Numbers ?? "",
            }).ToList(),
            "No wins recorded.");

        double totalsY = EnsureRowSpace(doc, y + 14, 34);
        decimal net = totalWon - totalSpent;
        doc.Gfx.DrawRectangle(new XSolidBrush(Navy), MarginX, totalsY, ContentRight - MarginX, 34);
        DrawText(doc.Gfx, "TOTAL", MarginX + 6, totalsY, 34, 12, true, White, XStringAlignment.Near);
        DrawText(doc.Gfx, $"Spent: ${totalSpent:N2}   Won: ${totalWon:N2}   Net: {(net >= 0 ? "+" : "")}${net:N2}",
            MarginX, totalsY, 34, 12, true, net >= 0 ? NetGreen : NetRed, XStringAlignment.Far, ContentRight - MarginX);

        return doc.Save(filePath);
    }

    // ── Shared drawing primitives ───────────────────────────────────────────

    class Doc
    {
        public PdfDocument Document = new();
        public PdfPage Page;
        public XGraphics Gfx;
        public int PageNum = 1;
        public string? TabLabel;
        public List<string>? AllTabLabels;

        public Doc()
        {
            Page = Document.AddPage();
            Page.Width  = XUnit.FromPoint(PageWidth);
            Page.Height = XUnit.FromPoint(PageHeight);
            Gfx = XGraphics.FromPdfPage(Page);
        }

        public void NewPage()
        {
            DrawFooter(this);
            PageNum++;
            Page = Document.AddPage();
            Page.Width  = XUnit.FromPoint(PageWidth);
            Page.Height = XUnit.FromPoint(PageHeight);
            Gfx = XGraphics.FromPdfPage(Page);
            if (TabLabel != null && AllTabLabels != null) DrawMonthTabStack(this, AllTabLabels, TabLabel);
        }

        public string Save(string path)
        {
            DrawFooter(this);
            Document.Save(path);
            return path;
        }
    }

    static void DrawHeaderBand(Doc doc, string title, string? subtitle = null)
    {
        doc.Gfx.DrawRectangle(new XSolidBrush(Navy), 0, 0, PageWidth, 118);
        doc.Gfx.DrawRectangle(new XSolidBrush(Gold), 0, 118, PageWidth, 3);

        DrawText(doc.Gfx, "CA 7 Tracker", MarginX, 26, 24, 13, true, Gold, XStringAlignment.Near);
        DrawText(doc.Gfx, title, MarginX, 58, 26, 20, true, White, XStringAlignment.Near);
        DrawText(doc.Gfx, subtitle ?? $"Generated {DateTime.Now:MMMM d, yyyy \\a\\t h:mm tt}",
            MarginX, 88, 18, 10, false, Hex("#B0B8C0"), XStringAlignment.Near);
    }

    /// A row of index-divider-style tabs across the very top of the page — one per
    /// month, all visible together (not just the current page's month), with the
    /// current month's tab highlighted gold/bold and the rest muted. Present on every
    /// page of a month's section (including table overflow pages) via Doc.NewPage(), so
    /// the whole month list is visible while flipping, even in viewers whose bookmarks
    /// panel is hard to find.
    static readonly XColor TabMuted   = Hex("#3A4552");
    static readonly XColor TabMutedFg = Hex("#C7CDD4");
    static readonly XColor TabBorder  = Hex("#5A6472");

    /// Computes the tab row's rectangles (top-down XGraphics space) once, shared by
    /// both the visual draw and the clickable-link pass below, so the tappable area
    /// always matches exactly what's drawn.
    static List<(double X, double Y, double W, double H)> MonthTabRects(List<string> allLabels)
    {
        const double tabH = 22;
        double tabW = PageWidth / Math.Max(1, allLabels.Count);

        var list = new List<(double, double, double, double)>();
        for (int i = 0; i < allLabels.Count; i++)
            list.Add((i * tabW, 0, tabW, tabH));
        return list;
    }

    /// Narrow tabs (many months) can't fit a full "August 2026" — shorten to "Aug 26".
    static string AbbreviateMonth(string label, double tabW)
    {
        if (tabW >= 70) return label;
        var parts = label.Split(' ');
        return parts.Length == 2 && parts[0].Length >= 3 && parts[1].Length >= 2
            ? $"{parts[0][..3]} {parts[1][^2..]}" : label;
    }

    static void DrawMonthTabStack(Doc doc, List<string> allLabels, string currentLabel)
    {
        var rects = MonthTabRects(allLabels);
        for (int i = 0; i < allLabels.Count; i++)
        {
            string label = allLabels[i];
            bool current = label == currentLabel;
            var (x, y, w, h) = rects[i];

            doc.Gfx.DrawRectangle(new XSolidBrush(current ? Gold : TabMuted), x, y, w, h);
            doc.Gfx.DrawRectangle(new XPen(current ? Navy : TabBorder, 0.75), x, y, w, h);
            DrawText(doc.Gfx, AbbreviateMonth(label, w), x, y, h, 8.5, current,
                current ? Navy : TabMutedFg, XStringAlignment.Center, w);
        }
    }

    /// Wires up real tap-to-jump navigation on every tab, on every page — a second pass
    /// run only after all pages exist, since a tab must be able to link to any month's
    /// page including ones generated later in the document. PDF link-annotation rects
    /// are in the PDF's native bottom-up page space, unlike XGraphics drawing calls
    /// (top-down) — must flip Y (PageHeight - y - h) or the tappable area lands
    /// mirrored/offset from what's actually drawn.
    static void AddMonthTabLinks(Doc doc, List<string> allLabels, Dictionary<string, int> pageIndexByLabel)
    {
        var rects = MonthTabRects(allLabels);
        foreach (var page in doc.Document.Pages)
        {
            for (int i = 0; i < allLabels.Count; i++)
            {
                if (!pageIndexByLabel.TryGetValue(allLabels[i], out int destPage)) continue;
                var (x, y, w, h) = rects[i];
                var pdfRect = new PdfRectangle(new XRect(x, PageHeight - y - h, w, h));
                page.AddDocumentLink(pdfRect, destPage);
            }
        }
    }

    static double DrawSectionTitle(Doc doc, double y, string text)
    {
        y = EnsureRowSpace(doc, y, 30);
        DrawText(doc.Gfx, text, MarginX, y, 24, 13, true, TextDark, XStringAlignment.Near);
        return y + 24;
    }

    static double DrawGrandTotalBar(Doc doc, double y, decimal grandSpent, decimal grandWon, decimal grandNet)
    {
        const double barHeight = 48;
        doc.Gfx.DrawRectangle(new XSolidBrush(Navy), MarginX, y, ContentRight - MarginX, barHeight);

        DrawText(doc.Gfx, "ALL-TIME TOTAL", MbColMonthX + 6, y, barHeight, 13, true, White, XStringAlignment.Near);

        DrawColumnStat(doc, "SPENT", $"${grandSpent:N2}", MbColSpentR - 130, MbColSpentR, y, White);
        DrawColumnStat(doc, "WON", $"${grandWon:N2}", MbColWonR - 130, MbColWonR, y, White);
        DrawColumnStat(doc, "NET", $"{(grandNet >= 0 ? "+" : "")}${grandNet:N2}", MbColNetR - 130, MbColNetR, y,
            grandNet >= 0 ? NetGreen : NetRed);

        return y + barHeight;
    }

    static void DrawColumnStat(Doc doc, string label, string value, double left, double right, double barY, XColor valueColor)
    {
        var rectLabel = new XRect(left, barY + 14, right - left, 14);
        doc.Gfx.DrawString(label, Font(8, false), new XSolidBrush(Gold), rectLabel, XStringFormats.TopRight);
        var rectValue = new XRect(left, barY + 26, right - left, 18);
        doc.Gfx.DrawString(value, Font(13, true), new XSolidBrush(valueColor), rectValue, XStringFormats.TopRight);
    }

    /// Draws a bordered, zebra-striped, paginating table with arbitrary columns.
    static double DrawTable(Doc doc, double y, (string Header, double Width)[] cols, int[] rightAlign, List<string[]> rows, string emptyText)
    {
        const double rowH = 20;
        y = EnsureRowSpace(doc, y, rowH + 4);
        y = DrawTableHeaderRow(doc, y, cols, rightAlign);

        if (rows.Count == 0)
        {
            DrawText(doc.Gfx, emptyText, MarginX + 6, y, rowH, 9, false, TextGray, XStringAlignment.Near);
            RowBorder(doc, y, rowH);
            return y + rowH + 10;
        }

        bool zebra = false;
        foreach (var row in rows)
        {
            double before = y;
            y = EnsureRowSpace(doc, y, rowH);
            if (y != before) y = DrawTableHeaderRow(doc, y, cols, rightAlign);

            if (zebra) doc.Gfx.DrawRectangle(new XSolidBrush(RowAlt), MarginX, y, ContentRight - MarginX, rowH);
            zebra = !zebra;

            double x = MarginX;
            for (int i = 0; i < cols.Length; i++)
            {
                bool right = rightAlign.Contains(i);
                DrawText(doc.Gfx, row[i], x + (right ? 0 : 6), y, rowH, 9, false, TextDark,
                    right ? XStringAlignment.Far : XStringAlignment.Near, cols[i].Width - (right ? 6 : 0));
                x += cols[i].Width;
            }
            RowBorder(doc, y, rowH);
            y += rowH;
        }
        return y + 10;
    }

    static double DrawTableHeaderRow(Doc doc, double top, (string Header, double Width)[] cols, int[] rightAlign)
    {
        const double rowH = 20;
        doc.Gfx.DrawRectangle(new XSolidBrush(HeaderRow), MarginX, top, ContentRight - MarginX, rowH + 2);
        doc.Gfx.DrawLine(new XPen(BorderGray, 1.5), MarginX, top + rowH + 2, ContentRight, top + rowH + 2);

        double x = MarginX;
        foreach (var col in cols)
        {
            bool right = rightAlign.Contains(Array.IndexOf(cols, col));
            DrawText(doc.Gfx, col.Header, x + (right ? 0 : 6), top, rowH, 8, true, TextGray,
                right ? XStringAlignment.Far : XStringAlignment.Near, col.Width - (right ? 6 : 0));
            x += col.Width;
        }
        return top + rowH + 2;
    }

    /// If the next row wouldn't fit above the bottom margin, starts a new page and
    /// returns the fresh top-of-content y (48). Otherwise returns y unchanged.
    static double EnsureRowSpace(Doc doc, double y, double needed)
    {
        if (y + needed <= PageHeight - 56) return y;
        doc.NewPage();
        return 48;
    }

    static void RowBorder(Doc doc, double y, double height) =>
        doc.Gfx.DrawLine(new XPen(BorderGray, 1), MarginX, y + height, ContentRight, y + height);

    static void DrawFooter(Doc doc)
    {
        doc.Gfx.DrawLine(new XPen(BorderGray, 1), MarginX, PageHeight - 40, ContentRight, PageHeight - 40);
        DrawText(doc.Gfx, "CA 7 Tracker · DailyFantasyMAUI", MarginX, PageHeight - 32, 14, 8, false, TextGray, XStringAlignment.Near);
        DrawText(doc.Gfx, $"Page {doc.PageNum}", MarginX, PageHeight - 32, 14, 8, false, TextGray, XStringAlignment.Far, ContentRight - MarginX);
    }

    /// x/y is the top-left of the text's line box; width (if given) bounds a Far/Center-aligned draw.
    static void DrawText(XGraphics gfx, string text, double x, double y, double lineHeight, double size, bool bold,
        XColor color, XStringAlignment align, double? width = null)
    {
        double w = width ?? (ContentRight - x);
        var rect = new XRect(x, y, w, lineHeight);
        var format = new XStringFormat { Alignment = align, LineAlignment = XLineAlignment.Near };
        gfx.DrawString(text ?? "", Font(size, bold), new XSolidBrush(color), rect, format);
    }

    // Turns TicketLogEntry.Extra ("M:07","PB:14","MB:09","M"/"E" for D3) into a readable suffix.
    static string FormatExtra(string game, string extra)
    {
        if (string.IsNullOrEmpty(extra)) return "";
        if (game == "D3")
        {
            string suf = extra.Contains('|') ? extra.Split('|').Last() : extra;
            return suf.Equals("M", StringComparison.OrdinalIgnoreCase) ? "Midday"
                 : suf.Equals("E", StringComparison.OrdinalIgnoreCase) ? "Evening"
                 : suf;
        }
        if (extra.StartsWith("M:"))  return $"Mega {extra[2..]}";
        if (extra.StartsWith("PB:")) return $"Powerball {extra[3..]}";
        if (extra.StartsWith("MB:")) return $"Mega Ball {extra[3..]}";
        return extra;
    }
}
