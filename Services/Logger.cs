namespace DailyFantasyMAUI.Services
{
    public static class Logger
    {
        static readonly string LogPath = Path.Combine(FileSystem.AppDataDirectory, "error_log.txt");
        static readonly SemaphoreSlim _lock = new(1, 1);

        public static async Task LogAsync(string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            await _lock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(LogPath, line + Environment.NewLine);
            }
            finally
            {
                _lock.Release();
            }
        }

        public static string GetLogPath() => LogPath;

        public static async Task<string> ReadLogAsync()
        {
            if (!File.Exists(LogPath)) return "(no log yet)";
            return await File.ReadAllTextAsync(LogPath);
        }

        public static void Clear()
        {
            if (File.Exists(LogPath)) File.Delete(LogPath);
        }
    }
}
