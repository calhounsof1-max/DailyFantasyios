using System.Text.Json;

namespace DailyFantasyMAUI.Services
{
    public record DrawPrizeTier(int Tier, decimal Amount, int Count = 0);

    public static class GetDataEntry
    {
        const string PrefGameId   = "fantasy5_game_id";
        const int    DefaultId    = 10;
        const string PrefSLGameId = "sl_game_id";
        const int    DefaultSLId  = 8;
        const int    PBGameId     = 12;
        const int    D4GameId     = 14;
        const int    DDGameId     = 11;

        // ── Cache helpers (data folder txt files) ────────────────────────────

        static string DataDir => Path.Combine(FileSystem.AppDataDirectory, "data");

        static string CacheFile(string name)
        {
            Directory.CreateDirectory(DataDir);
            return Path.Combine(DataDir, name);
        }

        static void SaveF5Cache(IEnumerable<(string DrawDate, int[] Numbers)> draws)
        {
            var lines = draws.Take(10).Select(d => $"{d.DrawDate}\t{string.Join(",", d.Numbers)}");
            File.WriteAllText(CacheFile("Fantasy_5.txt"), string.Join("\n", lines));
        }

        static List<(string DrawDate, int[] Numbers, DrawPrizeTier[] Prizes)> LoadF5Cache()
        {
            var path = CacheFile("Fantasy_5.txt");
            var results = new List<(string, int[], DrawPrizeTier[])>();
            if (!File.Exists(path)) return results;
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('\t');
                if (parts.Length != 2) continue;
                var nums = parts[1].Split(',').Select(s => int.TryParse(s, out int n) ? n : -1).Where(n => n >= 0).ToArray();
                if (nums.Length == 5) results.Add((parts[0], nums, Array.Empty<DrawPrizeTier>()));
            }
            return results;
        }

        static void SavePBCache(IEnumerable<(string DrawDate, int[] MainNumbers, int PBNumber)> draws)
        {
            var lines = draws.Take(10).Select(d => $"{d.DrawDate}\t{string.Join(",", d.MainNumbers)}\t{d.PBNumber}");
            File.WriteAllText(CacheFile("powerball.txt"), string.Join("\n", lines));
        }

        static List<(string DrawDate, int[] MainNumbers, int PBNumber, DrawPrizeTier[] Prizes)> LoadPBCache()
        {
            var path = CacheFile("powerball.txt");
            var results = new List<(string, int[], int, DrawPrizeTier[])>();
            if (!File.Exists(path)) return results;
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('\t');
                if (parts.Length != 3) continue;
                var main = parts[1].Split(',').Select(s => int.TryParse(s, out int n) ? n : -1).Where(n => n >= 0).ToArray();
                if (main.Length != 5 || !int.TryParse(parts[2], out int pb)) continue;
                results.Add((parts[0], main, pb, Array.Empty<DrawPrizeTier>()));
            }
            return results;
        }

        static void SaveD3Cache(IEnumerable<(string DrawDate, int DrawNumber, int[] Numbers)> draws)
        {
            var lines = draws.Take(10).Select(d => $"{d.DrawDate}\t{d.DrawNumber}\t{string.Join(",", d.Numbers)}");
            File.WriteAllText(CacheFile("daily3.txt"), string.Join("\n", lines));
        }

        static List<(string DrawDate, int DrawNumber, int[] Numbers)> LoadD3Cache()
        {
            var path = CacheFile("daily3.txt");
            var results = new List<(string, int, int[])>();
            if (!File.Exists(path)) return results;
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('\t');
                if (parts.Length != 3) continue;
                if (!int.TryParse(parts[1], out int dn)) continue;
                var nums = parts[2].Split(',').Select(s => int.TryParse(s, out int n) ? n : -1).Where(n => n >= 0).ToArray();
                if (nums.Length == 3) results.Add((parts[0], dn, nums));
            }
            return results;
        }

        static void SaveDDCache(IEnumerable<(string DrawDate, int[] Horses, string RaceTime)> draws)
        {
            var lines = draws.Take(10).Select(d => $"{d.DrawDate}\t{string.Join(",", d.Horses)}\t{d.RaceTime}");
            File.WriteAllText(CacheFile("dailyderby.txt"), string.Join("\n", lines));
        }

        static List<(string DrawDate, int[] Horses, string RaceTime)> LoadDDCache()
        {
            var path = CacheFile("dailyderby.txt");
            var results = new List<(string, int[], string)>();
            if (!File.Exists(path)) return results;
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('\t');
                if (parts.Length != 3) continue;
                var horses = parts[1].Split(',').Select(s => int.TryParse(s, out int n) ? n : -1).Where(n => n >= 0).ToArray();
                if (horses.Length == 3) results.Add((parts[0], horses, parts[2]));
            }
            return results;
        }

        static void SaveD4Cache(IEnumerable<(string DrawDate, int DrawNumber, int[] Numbers)> draws)
        {
            var lines = draws.Take(10).Select(d => $"{d.DrawDate}\t{d.DrawNumber}\t{string.Join(",", d.Numbers)}");
            File.WriteAllText(CacheFile("daily4.txt"), string.Join("\n", lines));
        }

        static List<(string DrawDate, int DrawNumber, int[] Numbers)> LoadD4Cache()
        {
            var path = CacheFile("daily4.txt");
            var results = new List<(string, int, int[])>();
            if (!File.Exists(path)) return results;
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('\t');
                if (parts.Length != 3) continue;
                if (!int.TryParse(parts[1], out int dn)) continue;
                var nums = parts[2].Split(',').Select(s => int.TryParse(s, out int n) ? n : -1).Where(n => n >= 0).ToArray();
                if (nums.Length == 4) results.Add((parts[0], dn, nums));
            }
            return results;
        }

        static void SaveSLCache(IEnumerable<(string DrawDate, int[] MainNumbers, int MegaNumber)> draws)
        {
            var lines = draws.Take(10).Select(d => $"{d.DrawDate}\t{string.Join(",", d.MainNumbers)}\t{d.MegaNumber}");
            File.WriteAllText(CacheFile("superlotto.txt"), string.Join("\n", lines));
        }

        static List<(string DrawDate, int[] MainNumbers, int MegaNumber, DrawPrizeTier[] Prizes)> LoadSLCache()
        {
            var path = CacheFile("superlotto.txt");
            var results = new List<(string, int[], int, DrawPrizeTier[])>();
            if (!File.Exists(path)) return results;
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('\t');
                if (parts.Length != 3) continue;
                var main = parts[1].Split(',').Select(s => int.TryParse(s, out int n) ? n : -1).Where(n => n >= 0).ToArray();
                if (main.Length != 5 || !int.TryParse(parts[2], out int mega)) continue;
                results.Add((parts[0], main, mega, Array.Empty<DrawPrizeTier>()));
            }
            return results;
        }

        static HttpClient MakeClient(int timeoutSec = 20)
        {
            var handler = new SocketsHttpHandler { UseCookies = true, AllowAutoRedirect = true };
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(timeoutSec) };
            c.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            c.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            c.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            return c;
        }

        // ── Fetch past draws (self-healing game ID) ───────────────────────────

        public static async Task<List<(string DrawDate, int[] Numbers, DrawPrizeTier[] Prizes)>> GetPastDraws(
            int count = 30, Func<string, Task<string?>>? fetcher = null)
        {
            int gameId = Preferences.Get(PrefGameId, DefaultId);
            var results = await FetchDraws(gameId, count, "Fantasy 5", fetcher);
            if (results.Count > 0)
                SaveF5Cache(results.Select(r => (r.DrawDate, r.Numbers)));
            else
            {
                var cached = LoadF5Cache();
                if (cached.Count > 0) { SetError("Offline — showing last cached draws"); return cached; }
            }
            return results;
        }

        public static async Task<List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)>> GetDaily3Draws(
            int count = 30, Func<string, Task<string?>>? fetcher = null)
        {
            var results = new List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)>();
            try
            {
                var url = $"https://www.calottery.com/api/DrawGameApi/DrawGamePastDrawResults/9/1/{count}";

                string json;
                if (fetcher != null)
                {
                    json = await fetcher(url) ?? "";
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        SetError("Daily3: WebView returned no data");
                        return results;
                    }
                }
                else
                {
                    using var client = MakeClient(30);
                    json = await client.GetStringAsync(url).ConfigureAwait(false);
                }

                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    SetError($"Daily3: root={doc.RootElement.ValueKind} [{json[..Math.Min(80, json.Length)]}]");
                    return results;
                }

                if (!doc.RootElement.TryGetProperty("Name", out var nameEl) ||
                    !nameEl.GetRawText().Trim('"').Contains("Daily 3", StringComparison.OrdinalIgnoreCase))
                {
                    SetError($"Daily3: unexpected response");
                    return results;
                }

                if (!doc.RootElement.TryGetProperty("PreviousDraws", out var draws) ||
                    draws.ValueKind != JsonValueKind.Array)
                {
                    SetError("Daily3: no PreviousDraws array");
                    return results;
                }

                foreach (var draw in draws.EnumerateArray())
                {
                    try
                    {
                        var rawDate = draw.GetProperty("DrawDate").GetRawText().Trim('"');
                        string drawDate = rawDate;
                        if (DateTime.TryParse(rawDate, out var dt))
                            drawDate = dt.ToString("ddd MMM d, yyyy");

                        var drawNumEl = draw.GetProperty("DrawNumber");
                        int drawNum = drawNumEl.ValueKind == JsonValueKind.Number
                            ? drawNumEl.GetInt32()
                            : int.TryParse(drawNumEl.GetRawText().Trim('"'), out int dn) ? dn : 0;

                        if (!draw.TryGetProperty("WinningNumbers", out var winNums) ||
                            winNums.ValueKind != JsonValueKind.Object)
                            continue;

                        var numList = new List<int>();
                        for (int i = 1; i <= 3; i++)
                        {
                            if (!winNums.TryGetProperty(i.ToString(), out var numEl)) break;
                            int n;
                            if (numEl.ValueKind == JsonValueKind.Number)
                                n = numEl.GetInt32();
                            else if (numEl.ValueKind == JsonValueKind.String)
                            { if (!int.TryParse(numEl.GetString(), out n)) continue; }
                            else if (numEl.ValueKind == JsonValueKind.Object &&
                                     numEl.TryGetProperty("Number", out var np))
                            { if (!int.TryParse(np.GetRawText().Trim('"'), out n)) continue; }
                            else continue;
                            numList.Add(n);
                        }
                        if (numList.Count == 3)
                        {
                            var prizes = ParsePrizeTiers(draw);
                            results.Add((drawDate, drawNum, numList.ToArray(), prizes));
                        }
                    }
                    catch { }
                }
                LastError = "";
            }
            catch (Exception ex)
            {
                SetError($"Daily3: {ex.GetType().Name}: {ex.Message}");
            }
            if (results.Count > 0)
                SaveD3Cache(results.Select(r => (r.DrawDate, r.DrawNumber, r.Numbers)).ToList());
            else
            {
                var cached = LoadD3Cache();
                if (cached.Count > 0) { SetError("Offline — showing last cached draws"); return cached.Select(r => (r.DrawDate, r.DrawNumber, r.Numbers, Array.Empty<DrawPrizeTier>())).ToList(); }
            }
            return results;
        }

        // Daily 4: one draw per day (evening ~6:30 PM), game ID 14
        public static async Task<List<(string DrawDate, int[] Numbers, DrawPrizeTier[] Prizes)>> GetDaily4Draws(
            int count = 30, Func<string, Task<string?>>? fetcher = null)
        {
            var results = new List<(string, int[], DrawPrizeTier[])>();
            try
            {
                var url = $"https://www.calottery.com/api/DrawGameApi/DrawGamePastDrawResults/{D4GameId}/1/{count}";

                string json;
                if (fetcher != null)
                {
                    json = await fetcher(url) ?? "";
                    if (string.IsNullOrWhiteSpace(json)) { SetError("Daily4: WebView returned no data"); return results; }
                }
                else
                {
                    using var client = MakeClient(30);
                    json = await client.GetStringAsync(url).ConfigureAwait(false);
                }

                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    SetError($"Daily4: unexpected root [{json[..Math.Min(80, json.Length)]}]");
                    return results;
                }

                if (!doc.RootElement.TryGetProperty("PreviousDraws", out var draws) ||
                    draws.ValueKind != JsonValueKind.Array)
                {
                    SetError("Daily4: no PreviousDraws array");
                    return results;
                }

                foreach (var draw in draws.EnumerateArray())
                {
                    try
                    {
                        var rawDate = draw.GetProperty("DrawDate").GetRawText().Trim('"');
                        string drawDate = DateTime.TryParse(rawDate, out var dt)
                            ? dt.ToString("ddd MMM d, yyyy") : rawDate;

                        if (!draw.TryGetProperty("WinningNumbers", out var winNums) ||
                            winNums.ValueKind != JsonValueKind.Object) continue;

                        var numList = new List<int>();
                        for (int i = 1; i <= 4; i++)
                        {
                            if (!winNums.TryGetProperty(i.ToString(), out var numEl)) break;
                            int n;
                            if (numEl.ValueKind == JsonValueKind.Number)
                                n = numEl.GetInt32();
                            else if (numEl.ValueKind == JsonValueKind.String)
                            { if (!int.TryParse(numEl.GetString(), out n)) continue; }
                            else if (numEl.ValueKind == JsonValueKind.Object &&
                                     numEl.TryGetProperty("Number", out var np))
                            { if (!int.TryParse(np.GetRawText().Trim('"'), out n)) continue; }
                            else continue;
                            numList.Add(n);
                        }
                        if (numList.Count == 4)
                        {
                            var prizes = ParsePrizeTiers(draw);
                            results.Add((drawDate, numList.ToArray(), prizes));
                        }
                    }
                    catch { }
                }
                LastError = "";
            }
            catch (Exception ex) { SetError($"Daily4: {ex.GetType().Name}: {ex.Message}"); }

            if (results.Count > 0)
                SaveD4Cache(results.Select(r => (r.Item1, 0, r.Item2)));
            else
            {
                var cached = LoadD4Cache();
                if (cached.Count > 0) { SetError("Offline — showing last cached draws"); return cached.Select(r => (r.DrawDate, r.Numbers, Array.Empty<DrawPrizeTier>())).ToList(); }
            }
            return results;
        }

        public static async Task<List<(string DrawDate, int[] Horses, string RaceTime, DrawPrizeTier[] Prizes)>> GetDailyDerbyDraws(
            int count = 30, Func<string, Task<string?>>? fetcher = null)
        {
            var results = new List<(string, int[], string, DrawPrizeTier[])>();
            try
            {
                var url = $"https://www.calottery.com/api/DrawGameApi/DrawGamePastDrawResults/{DDGameId}/1/{count}";

                string json;
                if (fetcher != null)
                {
                    json = await fetcher(url) ?? "";
                    if (string.IsNullOrWhiteSpace(json)) { SetError("DD: WebView returned no data"); return results; }
                }
                else
                {
                    using var client = MakeClient(30);
                    json = await client.GetStringAsync(url).ConfigureAwait(false);
                }

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) { SetError("DD: bad root"); return results; }

                if (!doc.RootElement.TryGetProperty("PreviousDraws", out var draws) ||
                    draws.ValueKind != JsonValueKind.Array)
                { SetError("DD: no PreviousDraws"); return results; }

                foreach (var draw in draws.EnumerateArray())
                {
                    try
                    {
                        var rawDate = draw.GetProperty("DrawDate").GetRawText().Trim('"');
                        string drawDate = DateTime.TryParse(rawDate, out var dt) ? dt.ToString("ddd MMM d, yyyy") : rawDate;

                        string raceTime = "";
                        if (draw.TryGetProperty("RaceTime", out var rtEl))
                            raceTime = rtEl.GetString() ?? "";

                        if (!draw.TryGetProperty("WinningNumbers", out var winNums) ||
                            winNums.ValueKind != JsonValueKind.Object) continue;

                        var horses = new List<int>();
                        for (int i = 1; i <= 3; i++)
                        {
                            if (!winNums.TryGetProperty(i.ToString(), out var numEl)) break;
                            int n;
                            if (numEl.ValueKind == JsonValueKind.Object && numEl.TryGetProperty("Number", out var np))
                            { if (!int.TryParse(np.GetRawText().Trim('"'), out n)) continue; }
                            else if (numEl.ValueKind == JsonValueKind.Number) n = numEl.GetInt32();
                            else if (numEl.ValueKind == JsonValueKind.String) { if (!int.TryParse(numEl.GetString(), out n)) continue; }
                            else continue;
                            horses.Add(n);
                        }

                        if (horses.Count == 3)
                        {
                            var prizes = ParsePrizeTiers(draw);
                            results.Add((drawDate, horses.ToArray(), raceTime, prizes));
                        }
                    }
                    catch { }
                }
                LastError = "";
            }
            catch (Exception ex) { SetError($"DD: {ex.GetType().Name}: {ex.Message}"); }

            if (results.Count > 0)
                SaveDDCache(results.Select(r => (r.Item1, r.Item2, r.Item3)).ToList());
            else
            {
                var cached = LoadDDCache();
                if (cached.Count > 0) { SetError("Offline — showing last cached draws"); return cached.Select(r => (r.DrawDate, r.Horses, r.RaceTime, Array.Empty<DrawPrizeTier>())).ToList(); }
            }
            return results;
        }

        public static async Task<int> GetSuperLottoGameId()
        {
            int cachedId = Preferences.Get(PrefSLGameId, DefaultSLId);
            try
            {
                var url = $"https://www.calottery.com/api/DrawGameApi/DrawGamePastDrawResults/{cachedId}/1/1";
                using var client = MakeClient(8);
                var json = await client.GetStringAsync(url).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("Name", out var ne) &&
                    ne.GetRawText().Trim('"').Contains("SuperLotto", StringComparison.OrdinalIgnoreCase))
                    return cachedId;
            }
            catch { }
            return await ScanForSuperLottoId();
        }

        private static async Task<int> ScanForSuperLottoId()
        {
            int cachedId = Preferences.Get(PrefSLGameId, DefaultSLId);
            for (int id = 1; id <= 30; id++)
            {
                if (id == cachedId) continue;
                try
                {
                    var url = $"https://www.calottery.com/api/DrawGameApi/DrawGamePastDrawResults/{id}/1/1";
                    using var client = MakeClient(6);
                    var json = await client.GetStringAsync(url).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("Name", out var ne) &&
                        ne.GetRawText().Trim('"').Contains("SuperLotto", StringComparison.OrdinalIgnoreCase))
                    {
                        Preferences.Set(PrefSLGameId, id);
                        _ = Logger.LogAsync($"SL: found game ID {id}");
                        return id;
                    }
                }
                catch { }
            }
            _ = Logger.LogAsync($"SL: could not find SuperLotto game ID, using {cachedId}");
            return cachedId;
        }

        public static async Task<List<(string DrawDate, int[] MainNumbers, int MegaNumber, DrawPrizeTier[] Prizes)>> GetSuperLottoDraws(
            int count = 30, Func<string, Task<string?>>? fetcher = null)
        {
            var results = new List<(string, int[], int, DrawPrizeTier[])>();
            try
            {
                int gameId = await GetSuperLottoGameId();
                var url = $"https://www.calottery.com/api/DrawGameApi/DrawGamePastDrawResults/{gameId}/1/{count}";
                string json;
                if (fetcher != null)
                {
                    json = await fetcher(url) ?? "";
                    if (string.IsNullOrWhiteSpace(json)) { SetError("SL: WebView returned no data"); return results; }
                }
                else
                {
                    using var client = MakeClient(30);
                    json = await client.GetStringAsync(url).ConfigureAwait(false);
                }

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) { SetError("SL: bad root"); return results; }

                if (!doc.RootElement.TryGetProperty("PreviousDraws", out var draws) || draws.ValueKind != JsonValueKind.Array)
                { SetError("SL: no PreviousDraws"); return results; }

                foreach (var draw in draws.EnumerateArray())
                {
                    try
                    {
                        var rawDate = draw.GetProperty("DrawDate").GetRawText().Trim('"');
                        string drawDate = DateTime.TryParse(rawDate, out var dt) ? dt.ToString("ddd MMM d, yyyy") : rawDate;

                        if (!draw.TryGetProperty("WinningNumbers", out var winNums) || winNums.ValueKind != JsonValueKind.Object) continue;

                        var mainList = new List<int>();
                        int mega = 0;

                        // Zero-based if key "0" exists (keys 0-5), otherwise 1-based (keys 1-6)
                        bool zeroBased = winNums.TryGetProperty("0", out _);
                        int start = zeroBased ? 0 : 1;
                        int end = start + 5; // 6 numbers total

                        for (int i = start; i <= end; i++)
                        {
                            if (!winNums.TryGetProperty(i.ToString(), out var numEl)) break;
                            int n;
                            bool isSpecial = false;
                            if (numEl.ValueKind == JsonValueKind.Object)
                            {
                                if (!numEl.TryGetProperty("Number", out var np)) continue;
                                if (!int.TryParse(np.GetRawText().Trim('"'), out n)) continue;
                                if (numEl.TryGetProperty("IsSpecial", out var sp)) isSpecial = sp.GetBoolean();
                            }
                            else if (numEl.ValueKind == JsonValueKind.Number) n = numEl.GetInt32();
                            else if (numEl.ValueKind == JsonValueKind.String) { if (!int.TryParse(numEl.GetString(), out n)) continue; }
                            else continue;

                            if (isSpecial) mega = n;
                            else mainList.Add(n);
                        }

                        // Fallback: if no IsSpecial found, last number is mega
                        if (mega == 0 && mainList.Count == 6) { mega = mainList[5]; mainList.RemoveAt(5); }

                        if (mainList.Count == 5 && mega > 0)
                            results.Add((drawDate, mainList.ToArray(), mega, ParsePrizeTiers(draw)));
                    }
                    catch { }
                }
                if (results.Count == 0)
                    SetError($"SL: parsed 0 draws from game ID {gameId} — check API structure");
                else
                    LastError = "";
            }
            catch (Exception ex) { SetError($"SL: {ex.GetType().Name}: {ex.Message}"); }
            if (results.Count > 0)
                SaveSLCache(results.Select(r => (r.Item1, r.Item2, r.Item3)));
            else
            {
                var cached = LoadSLCache();
                if (cached.Count > 0) { SetError("Offline — showing last cached draws"); return cached; }
            }
            return results;
        }

        public static async Task<List<(string DrawDate, int[] MainNumbers, int PBNumber, DrawPrizeTier[] Prizes)>> GetPowerballDraws(
            int count = 30, Func<string, Task<string?>>? fetcher = null)
        {
            var results = new List<(string, int[], int, DrawPrizeTier[])>();
            try
            {
                var url = $"https://www.calottery.com/api/DrawGameApi/DrawGamePastDrawResults/{PBGameId}/1/{count}";
                string json;
                if (fetcher != null)
                {
                    json = await fetcher(url) ?? "";
                    if (string.IsNullOrWhiteSpace(json)) { SetError("PB: WebView returned no data"); return results; }
                }
                else
                {
                    using var client = MakeClient(30);
                    json = await client.GetStringAsync(url).ConfigureAwait(false);
                }

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) { SetError("PB: bad root"); return results; }

                if (!doc.RootElement.TryGetProperty("PreviousDraws", out var draws) || draws.ValueKind != JsonValueKind.Array)
                { SetError("PB: no PreviousDraws"); return results; }

                foreach (var draw in draws.EnumerateArray())
                {
                    try
                    {
                        var rawDate = draw.GetProperty("DrawDate").GetRawText().Trim('"');
                        string drawDate = DateTime.TryParse(rawDate, out var dt) ? dt.ToString("ddd MMM d, yyyy") : rawDate;

                        if (!draw.TryGetProperty("WinningNumbers", out var winNums) || winNums.ValueKind != JsonValueKind.Object) continue;

                        var mainList = new List<int>();
                        int pb = 0;

                        bool zeroBased = winNums.TryGetProperty("0", out _);
                        int start = zeroBased ? 0 : 1;
                        int end   = start + 5;

                        for (int i = start; i <= end; i++)
                        {
                            if (!winNums.TryGetProperty(i.ToString(), out var numEl)) break;
                            int n;
                            bool isSpecial = false;
                            if (numEl.ValueKind == JsonValueKind.Object)
                            {
                                if (!numEl.TryGetProperty("Number", out var np)) continue;
                                if (!int.TryParse(np.GetRawText().Trim('"'), out n)) continue;
                                if (numEl.TryGetProperty("IsSpecial", out var sp)) isSpecial = sp.GetBoolean();
                            }
                            else if (numEl.ValueKind == JsonValueKind.Number) n = numEl.GetInt32();
                            else if (numEl.ValueKind == JsonValueKind.String) { if (!int.TryParse(numEl.GetString(), out n)) continue; }
                            else continue;

                            if (isSpecial) pb = n;
                            else mainList.Add(n);
                        }

                        if (pb == 0 && mainList.Count == 6) { pb = mainList[5]; mainList.RemoveAt(5); }

                        if (mainList.Count == 5 && pb > 0)
                            results.Add((drawDate, mainList.ToArray(), pb, ParsePrizeTiers(draw)));
                    }
                    catch { }
                }
                if (results.Count == 0)
                    SetError($"PB: parsed 0 draws — check API");
                else
                    LastError = "";
            }
            catch (Exception ex) { SetError($"PB: {ex.GetType().Name}: {ex.Message}"); }

            if (results.Count > 0)
                SavePBCache(results.Select(r => (r.Item1, r.Item2, r.Item3)));
            else
            {
                var cached = LoadPBCache();
                if (cached.Count > 0) { SetError("Offline — showing last cached draws"); return cached; }
            }
            return results;
        }

        public static string LastError { get; private set; } = "";

        static void SetError(string msg)
        {
            LastError = msg;
            _ = Logger.LogAsync(msg);
        }

        // ── Parse prize tiers from a draw JSON element ───────────────────────────
        // CA Lottery format: "Prizes": { "1": { "Amount": 83915, "Count": 2, ... }, "2": {...} }
        // Keys are tier numbers (strings), Amount = per-winner prize, Count = number of winners.

        static DrawPrizeTier[] ParsePrizeTiers(JsonElement draw)
        {
            var list = new List<DrawPrizeTier>();

            if (!draw.TryGetProperty("Prizes", out var prizesEl))
                return Array.Empty<DrawPrizeTier>();

            // Object style: { "1": { "Amount": 83915 }, "2": { "Amount": 517 }, ... }
            if (prizesEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var entry in prizesEl.EnumerateObject())
                {
                    if (!int.TryParse(entry.Name, out int tier)) continue;
                    try
                    {
                        decimal amount = 0;
                        int count = 0;
                        if (entry.Value.TryGetProperty("Amount", out var aEl) &&
                            aEl.ValueKind == JsonValueKind.Number)
                            amount = (decimal)aEl.GetDouble();
                        if (entry.Value.TryGetProperty("Count", out var cEl) &&
                            cEl.ValueKind == JsonValueKind.Number)
                            count = cEl.GetInt32();
                        list.Add(new DrawPrizeTier(tier, amount, count));
                    }
                    catch { }
                }
                return list.ToArray();
            }

            // Array fallback (other games)
            if (prizesEl.ValueKind == JsonValueKind.Array)
            {
                int autoTier = 0;
                foreach (var pv in prizesEl.EnumerateArray())
                {
                    autoTier++;
                    try
                    {
                        int tier = autoTier;
                        if (pv.TryGetProperty("Tier", out var tEl) && tEl.ValueKind == JsonValueKind.Number)
                            tier = tEl.GetInt32();

                        decimal amount = 0;
                        foreach (var af in new[] { "Amount", "PrizeAmount", "Prize" })
                        {
                            if (pv.TryGetProperty(af, out var aEl) && aEl.ValueKind == JsonValueKind.Number)
                            { amount = (decimal)aEl.GetDouble(); break; }
                        }
                        list.Add(new DrawPrizeTier(tier, amount));
                    }
                    catch { }
                }
            }

            return list.ToArray();
        }

        private static async Task<List<(string DrawDate, int[] Numbers, DrawPrizeTier[] Prizes)>> FetchDraws(
            int gameId, int count, string? expectedName = null, Func<string, Task<string?>>? fetcher = null)
        {
            var results = new List<(string, int[], DrawPrizeTier[])>();
            try
            {
                var url = $"https://www.calottery.com/api/DrawGameApi/DrawGamePastDrawResults/{gameId}/1/{count}";

                string json;
                if (fetcher != null)
                {
                    json = await fetcher(url) ?? "";
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        SetError($"ID {gameId}: WebView returned no data");
                        return results;
                    }
                }
                else
                {
                    using var client = MakeClient(30);
                    json = await client.GetStringAsync(url).ConfigureAwait(false);
                }

                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    SetError($"ID {gameId}: root={doc.RootElement.ValueKind}");
                    return results;
                }

                var name = doc.RootElement.TryGetProperty("Name", out var ne) ? ne.GetRawText().Trim('"') : "";
                if (expectedName != null && !name.Contains(expectedName, StringComparison.OrdinalIgnoreCase))
                {
                    SetError($"Game ID {gameId} returned '{name}', expected '{expectedName}'");
                    return results;
                }

                if (!doc.RootElement.TryGetProperty("PreviousDraws", out var draws) ||
                    draws.ValueKind != JsonValueKind.Array)
                {
                    SetError($"ID {gameId}: no PreviousDraws array");
                    return results;
                }

                foreach (var draw in draws.EnumerateArray())
                {
                    try
                    {
                        var rawDate = draw.GetProperty("DrawDate").GetRawText().Trim('"');
                        string drawDate = rawDate;
                        if (DateTime.TryParse(rawDate, out var dt))
                            drawDate = dt.ToString("ddd MMM d, yyyy");

                        if (!draw.TryGetProperty("WinningNumbers", out var winNums) ||
                            winNums.ValueKind != JsonValueKind.Object) continue;

                        bool zeroBased = winNums.TryGetProperty("0", out _);
                        var numList = new List<int>();
                        for (int i = zeroBased ? 0 : 1; i <= (zeroBased ? 4 : 10); i++)
                        {
                            if (!winNums.TryGetProperty(i.ToString(), out var numEl)) break;
                            int n;
                            if (numEl.ValueKind == JsonValueKind.Object &&
                                numEl.TryGetProperty("Number", out var np))
                            { if (!int.TryParse(np.GetRawText().Trim('"'), out n)) continue; }
                            else if (numEl.ValueKind == JsonValueKind.Number)
                                n = numEl.GetInt32();
                            else if (numEl.ValueKind == JsonValueKind.String)
                            { if (!int.TryParse(numEl.GetString(), out n)) continue; }
                            else continue;
                            numList.Add(n);
                        }
                        if (numList.Count > 0) results.Add((drawDate, numList.ToArray(), ParsePrizeTiers(draw)));
                    }
                    catch { }
                }
                LastError = "";
            }
            catch (Exception ex)
            {
                SetError($"ID {gameId}: {ex.GetType().Name}: {ex.Message}");
            }
            return results;
        }

        // ── F5 CSV paths ─────────────────────────────────────────────────────
        static string F5LocalCsvPath =>
            Path.Combine(FileSystem.AppDataDirectory, "data", "myFantasy5.csv");

        // ── Daily update: copy bundled CSV on first run, then append new draws ─
        public static async Task UpdateF5CsvAsync()
        {
            try
            {
                var localPath = F5LocalCsvPath;
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                // First launch: copy bundled asset to writable storage
                if (!File.Exists(localPath))
                {
                    using var asset = await FileSystem.OpenAppPackageFileAsync("data/myFantasy5.csv");
                    using var fs = File.Create(localPath);
                    await asset.CopyToAsync(fs);
                    _ = Logger.LogAsync("F5 CSV: initialized from bundled asset");
                }

                // Find last draw date in the local file
                DateTime lastDate = DateTime.MinValue;
                {
                    var lines = await File.ReadAllLinesAsync(localPath);
                    foreach (var l in lines.Reverse())
                    {
                        if (string.IsNullOrWhiteSpace(l) || l.StartsWith("D")) continue;
                        var p = l.Split(',');
                        if (DateTime.TryParse(p[0], out lastDate)) break;
                    }
                }
                if (lastDate == DateTime.MinValue) return;
                if (lastDate.Date >= DateTime.Today) return; // already up to date

                // Fetch recent draws from API and append anything newer than lastDate
                var recent = await GetPastDraws(30);
                var toAppend = recent
                    .Where(d => DateTime.TryParse(d.DrawDate, out var dt) && dt.Date > lastDate.Date)
                    .Select(d => { DateTime.TryParse(d.DrawDate, out var dt); return (dt, d.Numbers); })
                    .OrderBy(x => x.dt)
                    .ToList();

                if (toAppend.Count == 0) return;

                await using var sw = new StreamWriter(localPath, append: true, System.Text.Encoding.UTF8);
                foreach (var (dt, nums) in toAppend)
                    await sw.WriteLineAsync(dt.ToString("yyyy-MM-dd") + ",0," + string.Join(",", nums));

                _ = Logger.LogAsync($"F5 CSV: appended {toAppend.Count} new draw(s) through {toAppend[^1].dt:yyyy-MM-dd}");
            }
            catch (Exception ex) { _ = Logger.LogAsync($"F5 CSV update error: {ex.Message}"); }
        }

        // ── Load F5 draw history (local writable copy, falls back to bundled) ─
        // Returns draws newest-first.
        public static async Task<List<(string DrawDate, int DrawNumber, int[] Numbers, DrawPrizeTier[] Prizes)>> LoadF5CsvDraws()
        {
            var results = new List<(string, int, int[], DrawPrizeTier[])>();
            try
            {
                Stream stream;
                var localPath = F5LocalCsvPath;
                stream = File.Exists(localPath)
                    ? File.OpenRead(localPath)
                    : await FileSystem.OpenAppPackageFileAsync("data/myFantasy5.csv");

                using var reader = new StreamReader(stream);
                bool header = true;
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (header) { header = false; continue; }
                    var p = line.Split(',');
                    if (p.Length < 7) continue;
                    if (!DateTime.TryParse(p[0], out var dt)) continue;
                    if (!int.TryParse(p[1], out int drawNum)) drawNum = 0;
                    var nums = new int[5];
                    bool ok = true;
                    for (int i = 0; i < 5; i++)
                        if (!int.TryParse(p[i + 2], out nums[i])) { ok = false; break; }
                    if (ok)
                        results.Add((dt.ToString("ddd MMM d, yyyy"), drawNum, nums, Array.Empty<DrawPrizeTier>()));
                }
                results.Reverse(); // newest first
            }
            catch (Exception ex) { SetError($"CSV: {ex.Message}"); }
            return results;
        }

        // ── SL CSV paths ─────────────────────────────────────────────────────
        static string SLLocalCsvPath =>
            Path.Combine(FileSystem.AppDataDirectory, "data", "mySuperlotto.csv");

        // ── Daily update: copy bundled SL CSV on first run, append new draws ─
        public static async Task UpdateSLCsvAsync()
        {
            try
            {
                var localPath = SLLocalCsvPath;
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

                if (!File.Exists(localPath))
                {
                    using var asset = await FileSystem.OpenAppPackageFileAsync("data/mySuperlotto.csv");
                    using var fs = File.Create(localPath);
                    await asset.CopyToAsync(fs);
                    _ = Logger.LogAsync("SL CSV: initialized from bundled asset");
                }

                DateTime lastDate = DateTime.MinValue;
                {
                    var lines = await File.ReadAllLinesAsync(localPath);
                    foreach (var l in lines.Reverse())
                    {
                        if (string.IsNullOrWhiteSpace(l) || l.StartsWith("D")) continue;
                        var p = l.Split(',');
                        if (DateTime.TryParse(p[0], out lastDate)) break;
                    }
                }
                if (lastDate == DateTime.MinValue) return;
                if (lastDate.Date >= DateTime.Today) return;

                var recent = await GetSuperLottoDraws(30);
                var toAppend = recent
                    .Where(d => DateTime.TryParse(d.DrawDate, out var dt) && dt.Date > lastDate.Date)
                    .Select(d => { DateTime.TryParse(d.DrawDate, out var dt); return (dt, d.MainNumbers, d.MegaNumber); })
                    .OrderBy(x => x.dt)
                    .ToList();

                if (toAppend.Count == 0) return;

                await using var sw = new StreamWriter(localPath, append: true, System.Text.Encoding.UTF8);
                foreach (var (dt, nums, mega) in toAppend)
                    await sw.WriteLineAsync(dt.ToString("yyyy-MM-dd") + ",0," + string.Join(",", nums) + "," + mega);

                _ = Logger.LogAsync($"SL CSV: appended {toAppend.Count} new draw(s) through {toAppend[^1].dt:yyyy-MM-dd}");
            }
            catch (Exception ex) { _ = Logger.LogAsync($"SL CSV update error: {ex.Message}"); }
        }

        // ── Load SL draw history (local writable copy, falls back to bundled) ─
        // Returns draws newest-first. Format: DrawDate,DrawNumber,N1,N2,N3,N4,N5,Mega
        public static async Task<List<(string DrawDate, int DrawNumber, int[] MainNumbers, int Mega, DrawPrizeTier[] Prizes)>> LoadSLCsvDraws()
        {
            var results = new List<(string, int, int[], int, DrawPrizeTier[])>();
            try
            {
                var localPath = SLLocalCsvPath;
                Stream stream = File.Exists(localPath)
                    ? File.OpenRead(localPath)
                    : await FileSystem.OpenAppPackageFileAsync("data/mySuperlotto.csv");

                using var reader = new StreamReader(stream);
                bool header = true;
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (header) { header = false; continue; }
                    var p = line.Split(',');
                    if (p.Length < 8) continue;
                    if (!DateTime.TryParse(p[0], out var dt)) continue;
                    if (!int.TryParse(p[1], out int drawNum)) drawNum = 0;
                    var nums = new int[5];
                    bool ok = true;
                    for (int i = 0; i < 5; i++)
                        if (!int.TryParse(p[i + 2], out nums[i])) { ok = false; break; }
                    if (!ok) continue;
                    if (!int.TryParse(p[7], out int mega)) continue;
                    results.Add((dt.ToString("ddd MMM d, yyyy"), drawNum, nums, mega, Array.Empty<DrawPrizeTier>()));
                }
                results.Reverse(); // newest first
            }
            catch (Exception ex) { SetError($"SL CSV: {ex.Message}"); }
            return results;
        }

        // ── Auto-discover Fantasy 5 game ID ──────────────────────────────────

        public static async Task<int> GetFantasy5GameId()
        {
            int cachedId = Preferences.Get(PrefGameId, DefaultId);
            try
            {
                var url = $"https://www.calottery.com/api/DrawGameApi/DrawGamePastDrawResults/{cachedId}/1/1";
                using var client = MakeClient(8);
                var json = await client.GetStringAsync(url).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("Name", out var ne) &&
                    ne.GetRawText().Trim('"').Contains("Fantasy 5", StringComparison.OrdinalIgnoreCase))
                    return cachedId;
            }
            catch { }
            return await ScanForFantasy5Id();
        }

        private static async Task<int> ScanForFantasy5Id()
        {
            int cachedId = Preferences.Get(PrefGameId, DefaultId);
            for (int id = 1; id <= 25; id++)
            {
                if (id == cachedId) continue;
                try
                {
                    var url = $"https://www.calottery.com/api/DrawGameApi/DrawGamePastDrawResults/{id}/1/1";
                    using var client = MakeClient(6);
                    var json = await client.GetStringAsync(url).ConfigureAwait(false);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                        doc.RootElement.TryGetProperty("Name", out var ne) &&
                        ne.GetRawText().Trim('"').Contains("Fantasy 5", StringComparison.OrdinalIgnoreCase))
                    {
                        Preferences.Set(PrefGameId, id);
                        return id;
                    }
                }
                catch { }
            }
            return cachedId;
        }

        // ── Fetch latest single draw ──────────────────────────────────────────

        public static async Task<(bool Success, string DrawDate, int[] Numbers)> GetLatestDraw()
        {
            var draws = await GetPastDraws(1);
            if (draws.Count == 0) return (false, string.Empty, Array.Empty<int>());
            var (date, numbers, _) = draws[0];
            return (true, date, numbers);
        }

        // ── Legacy: download full history file ───────────────────────────────

        public static async Task<(bool Success, string LastDraw)> GetDataSource()
        {
            try
            {
                const string url = "https://www.calottery.com/sitecore/content/Miscellaneous/download-numbers?GameName=Fantasy-5";
                using var client = MakeClient();
                var data = await client.GetStringAsync(url).ConfigureAwait(false);

                var dir = Path.Combine(FileSystem.AppDataDirectory, "fantasy5");
                Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(Path.Combine(dir, "fantasy5.txt"), data).ConfigureAwait(false);

                string lastDraw = string.Empty;
                var lines = data.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var d in lines.Skip(3))
                {
                    var res = d.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (res.Length > 9 && double.TryParse(res[0], out _))
                    {
                        lastDraw = $"[{res[2]} {res[3]} {res[4]}] {res[5]}, {res[6]}, {res[7]}, {res[8]}, {res[9]}";
                        break;
                    }
                }
                return (true, lastDraw);
            }
            catch
            {
                return (false, string.Empty);
            }
        }
    }
}
