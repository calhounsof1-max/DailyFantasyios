using System.Text.RegularExpressions;
using DailyFantasyMAUI.Services;

namespace DailyFantasyMAUI;

/// <summary>
/// Imports ticket rows from a plain text or CSV file into the same Preferences
/// storage the game pages (WinnerPage, SuperLottoPage, PowerballPage, MegaMillionsPage,
/// Daily3Page, Daily4Page, DailyDerbyPage) read from — "{prefix}_set_{slot}" — so imported
/// rows show up exactly like manually-entered ones.
///
/// Line formats accepted:
///   "F5,12,5,22,31,9"      — leading game code (F5/SL/PB/MM/D3/D4/DD), numbers after
///   "F5 12 5 22 31 9"      — code + space-separated numbers
///   "12,5,22,31,9"         — no code: game is inferred from how many numbers the row has
///                            (3 digits 0-9 → Daily 3, 3 nums with a 10-12 → Daily Derby,
///                             4 → Daily 4, 5 → Fantasy 5). 6-number rows are always
///                            ambiguous between Super Lotto/Powerball/Mega Millions and
///                            are skipped — those always need a leading code.
///
/// Numbers may be separated by comma, space, or tab in any mix.
///
/// Usage: call ParseText() first (pure, does not touch Preferences) to show the user a
/// preview, then call InsertAsync() with that result once they confirm.
/// </summary>
public static class ImportDataTicket
{
    public sealed class ParsedGame
    {
        public string Code = "";
        public string Name = "";
        public List<int[]> Rows { get; } = new();
    }

    public sealed class ParseResult
    {
        public int TotalLinesRead;
        public List<ParsedGame> Games { get; } = new();
        public List<string> SkippedLines { get; } = new();
        public int TotalParsed => Games.Sum(g => g.Rows.Count);
    }

    public sealed class ImportResult
    {
        public Dictionary<string, int> InsertedByGame { get; } = new();
        public List<string> SkippedLines { get; } = new();
        public int TotalInserted => InsertedByGame.Values.Sum();
    }

    sealed record GameDef(string Code, string Name, string Prefix, string DrawServiceName,
        int Cols, int BonusCol, int MinMain, int MaxMain, int MinBonus, int MaxBonus, bool IsDerby);

    static readonly GameDef[] Games =
    [
        new("F5", "Fantasy 5",     "f5", "Fantasy 5",     5, -1, 1,  39, 0,  0,  false),
        new("SL", "Super Lotto",   "sl", "Super Lotto",   6,  5, 1,  47, 1,  27, false),
        new("PB", "Powerball",     "pb", "Powerball",     6,  5, 1,  69, 1,  26, false),
        new("MM", "Mega Millions", "mm", "Mega Millions", 6,  5, 1,  70, 1,  25, false),
        new("D3", "Daily 3",       "d3", "Daily 3",       3, -1, 0,  9,  0,  0,  false),
        new("D4", "Daily 4",       "d4", "Daily 4",       4, -1, 0,  9,  0,  0,  false),
        new("DD", "Daily Derby",   "dd", "Daily Derby",   3, -1, 1,  12, 0,  0,  true),
    ];

    static readonly Regex CodePrefix =
        new(@"^\s*(F5|SL|PB|MM|D3|D4|DD)[\s,:\t]+(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex Splitter = new(@"[,\s\t]+", RegexOptions.Compiled);

    public static async Task<ParseResult> ParseFileAsync(string filePath)
    {
        string text = await File.ReadAllTextAsync(filePath);
        return ParseText(text);
    }

    // Pass 1: read every row in the file before inserting anything, so a bad
    // line partway through never leaves some games imported and others not.
    public static ParseResult ParseText(string text)
    {
        var result = new ParseResult();
        var byCode = Games.ToDictionary(g => g.Code, g => new ParsedGame { Code = g.Code, Name = g.Name });

        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        foreach (var rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            result.TotalLinesRead++;

            var codeMatch = CodePrefix.Match(line);
            if (codeMatch.Success)
            {
                string code = codeMatch.Groups[1].Value.ToUpperInvariant();
                var game    = Games.First(g => g.Code == code);
                var nums    = ParseNumbers(codeMatch.Groups[2].Value);

                if (nums.Count != game.Cols)
                {
                    result.SkippedLines.Add($"\"{line}\" — {code} needs {game.Cols} numbers, found {nums.Count}");
                    continue;
                }
                if (!InRange(nums, game, out string rangeErr))
                {
                    result.SkippedLines.Add($"\"{line}\" — {rangeErr}");
                    continue;
                }
                byCode[code].Rows.Add(nums.ToArray());
            }
            else
            {
                var nums = ParseNumbers(line);
                string? inferred = nums.Count switch
                {
                    5 => "F5",
                    4 => "D4",
                    3 => nums.Any(n => n >= 10) ? "DD" : "D3",
                    6 => null, // ambiguous: SL vs PB vs MM — always needs an explicit code
                    _ => null,
                };

                if (inferred == null)
                {
                    result.SkippedLines.Add(nums.Count == 6
                        ? $"\"{line}\" — 6 numbers but no game code (SL/PB/MM look alike); add F5/SL/PB/MM/D3/D4/DD in front"
                        : $"\"{line}\" — can't tell which game ({nums.Count} number(s) found)");
                    continue;
                }

                var game = Games.First(g => g.Code == inferred);
                if (!InRange(nums, game, out string rangeErr))
                {
                    result.SkippedLines.Add($"\"{line}\" — {rangeErr}");
                    continue;
                }
                byCode[inferred].Rows.Add(nums.ToArray());
            }
        }

        result.Games.AddRange(byCode.Values.Where(g => g.Rows.Count > 0));
        return result;
    }

    static List<int> ParseNumbers(string text)
    {
        var nums = new List<int>();
        foreach (var tok in Splitter.Split(text))
            if (int.TryParse(tok, out int n))
                nums.Add(n);
        return nums;
    }

    static bool InRange(List<int> nums, GameDef game, out string error)
    {
        int mainCount = game.BonusCol >= 0 ? game.Cols - 1 : game.Cols;
        for (int i = 0; i < mainCount; i++)
        {
            if (nums[i] < game.MinMain || nums[i] > game.MaxMain)
            {
                error = $"{game.Code} number {nums[i]} is outside {game.MinMain}-{game.MaxMain}";
                return false;
            }
        }
        if (game.BonusCol >= 0)
        {
            int bonus = nums[game.BonusCol];
            if (bonus < game.MinBonus || bonus > game.MaxBonus)
            {
                error = $"{game.Code} bonus number {bonus} is outside {game.MinBonus}-{game.MaxBonus}";
                return false;
            }
        }
        error = "";
        return true;
    }

    // Pass 2: writes each game's parsed rows into the first empty rows across its 10 sets.
    public static async Task<ImportResult> InsertAsync(ParseResult parsed)
    {
        var result = new ImportResult();
        foreach (var parsedGame in parsed.Games)
        {
            var game = Games.First(g => g.Code == parsedGame.Code);
            await InsertGameRowsAsync(game, parsedGame.Rows, result);
        }
        return result;
    }

    // Writes rows into the first empty row it finds, scanning slot 0-9 then row 0-9 within
    // each slot — the same "skip to next empty row" rule ScanTicketPage uses, extended to
    // roll over into the next set once the current one is full.
    static async Task InsertGameRowsAsync(GameDef game, List<int[]> rows, ImportResult result)
    {
        int drawNum = await DrawNumberService.EnsureNextDrawAsync(game.DrawServiceName);
        string todayKey = DateTime.Today.ToString("yyyyMMdd");
        string advPart  = drawNum > 0
            ? $"{todayKey}~{todayKey}~{drawNum}~{drawNum}"
            : $"{todayKey}~{todayKey}~~";

        int storedCols = game.IsDerby ? 4 : game.Cols; // Daily Derby keeps a 4th "race time" cell
        int totalCells = 10 * storedCols;

        int rowIdx = 0;
        for (int slot = 0; slot < 10 && rowIdx < rows.Count; slot++)
        {
            string slotKey = $"{game.Prefix}_set_{slot}";
            string advKey  = $"{game.Prefix}_adv_{slot}";

            string existing = Preferences.Get(slotKey, "");
            var vals = string.IsNullOrEmpty(existing) ? new string[totalCells] : existing.Split('|');
            if (vals.Length < totalCells) Array.Resize(ref vals, totalCells);
            for (int i = 0; i < vals.Length; i++) vals[i] ??= "";

            string existingAdv = Preferences.Get(advKey, "");
            var advVals = string.IsNullOrEmpty(existingAdv) ? new string[10] : existingAdv.Split('|');
            if (advVals.Length < 10) Array.Resize(ref advVals, 10);
            for (int i = 0; i < advVals.Length; i++) advVals[i] ??= "";

            bool slotChanged = false;
            for (int r = 0; r < 10 && rowIdx < rows.Count; r++)
            {
                bool empty = true;
                for (int c = 0; c < game.Cols; c++)
                    if (!string.IsNullOrWhiteSpace(vals[r * storedCols + c])) { empty = false; break; }
                if (!empty) continue;

                var numsRow = rows[rowIdx];
                for (int c = 0; c < game.Cols; c++)
                    vals[r * storedCols + c] = numsRow[c].ToString();
                // Daily Derby's 4th cell (race time) is left as-is — imports never touch it.

                advVals[r] = advPart;
                slotChanged = true;
                rowIdx++;
            }

            if (slotChanged)
            {
                Preferences.Set(slotKey, string.Join("|", vals));
                Preferences.Set(advKey, string.Join("|", advVals));
            }
        }

        result.InsertedByGame[game.Code] = rowIdx;
        if (rowIdx < rows.Count)
            result.SkippedLines.Add(
                $"{game.Code}: {rows.Count - rowIdx} row(s) not inserted — all 10 sets are full");
    }
}
