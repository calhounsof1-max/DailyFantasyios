namespace DailyFantasyMAUI.Services;

/// Wraps Preferences.Get so a value corrupted to the wrong native type
/// (e.g. a string-only key that got written as an int by a bad backup restore)
/// can't crash the app. On a type mismatch it clears the bad key and returns
/// the fallback, so the app self-heals on next read instead of crashing forever.
public static class SafePreferences
{
    public static string GetString(string key, string fallback)
    {
        try { return Preferences.Get(key, fallback); }
        catch { Preferences.Remove(key); return fallback; }
    }

    public static int GetInt(string key, int fallback)
    {
        try { return Preferences.Get(key, fallback); }
        catch { Preferences.Remove(key); return fallback; }
    }

    public static bool GetBool(string key, bool fallback)
    {
        try { return Preferences.Get(key, fallback); }
        catch { Preferences.Remove(key); return fallback; }
    }
}
