using System.Text.Json;

namespace DailyFantasyMAUI.Services;

public class ArchiveEntry
{
    public string Name { get; set; } = "";
    public DateTime Date { get; set; }
    // Games: game prefix → (key → pipe-delimited values)
    // Keys: "set_0".."set_9" (named slots) and "entries" (working grid entries)
    public Dictionary<string, Dictionary<string, string>> Games { get; set; } = new();
}

public static class ArchiveService
{
    static readonly string ArchivePath =
        System.IO.Path.Combine(FileSystem.AppDataDirectory, "archives.json");

    // prefix, number of slots, working-entries prefs key
    static readonly (string Prefix, int Slots, string EntriesKey)[] GameDefs =
    [
        ("f5", 10, "f5_entries"),
        ("sl", 10, "sl_entries"),
        ("pb", 10, "pb_entries"),
        ("d3", 10, "d3_entries"),
        ("d4", 10, "d4_entries"),
    ];

    public static List<ArchiveEntry> Load()
    {
        if (!File.Exists(ArchivePath)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<ArchiveEntry>>(
                File.ReadAllText(ArchivePath)) ?? [];
        }
        catch { return []; }
    }

    static void SaveList(List<ArchiveEntry> list) =>
        File.WriteAllText(ArchivePath,
            JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));

    // Archive ALL games (named slots + working entries) and clear them
    public static void Archive(string name)
    {
        var entry = new ArchiveEntry { Name = name, Date = DateTime.Now };
        foreach (var (prefix, slots, entriesKey) in GameDefs)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i < slots; i++)
                d[$"set_{i}"] = Preferences.Get($"{prefix}_set_{i}", "");
            d["entries"] = Preferences.Get(entriesKey, "");
            entry.Games[prefix] = d;
        }
        var list = Load();
        list.Insert(0, entry); // newest first
        SaveList(list);

        // Clear all sets and working entries after archiving
        foreach (var (prefix, slots, entriesKey) in GameDefs)
        {
            for (int i = 0; i < slots; i++)
                Preferences.Remove($"{prefix}_set_{i}");
            Preferences.Remove(entriesKey);
        }
    }

    // Archive a single game's slots + entries and clear them
    public static void ArchiveGame(string prefix, string name)
    {
        var (_, slots, entriesKey) = GameDefs.First(g => g.Prefix == prefix);
        var entry = new ArchiveEntry { Name = name, Date = DateTime.Now };
        var d = new Dictionary<string, string>();
        for (int i = 0; i < slots; i++)
            d[$"set_{i}"] = Preferences.Get($"{prefix}_set_{i}", "");
        d["entries"] = Preferences.Get(entriesKey, "");
        entry.Games[prefix] = d;

        var list = Load();
        list.Insert(0, entry);
        SaveList(list);

        for (int i = 0; i < slots; i++)
            Preferences.Remove($"{prefix}_set_{i}");
        Preferences.Remove(entriesKey);
    }

    public static void RestoreGame(ArchiveEntry entry, string prefix)
    {
        if (!entry.Games.TryGetValue(prefix, out var d)) return;
        var (_, _, entriesKey) = GameDefs.First(g => g.Prefix == prefix);
        foreach (var kv in d)
        {
            if (kv.Key == "entries")
                Preferences.Set(entriesKey, kv.Value);
            else
                Preferences.Set($"{prefix}_{kv.Key}", kv.Value);
        }
    }

    public static void RestoreAll(ArchiveEntry entry)
    {
        foreach (var (prefix, _, entriesKey) in GameDefs)
        {
            if (!entry.Games.TryGetValue(prefix, out var d)) continue;
            foreach (var kv in d)
            {
                if (kv.Key == "entries")
                    Preferences.Set(entriesKey, kv.Value);
                else
                    Preferences.Set($"{prefix}_{kv.Key}", kv.Value);
            }
        }
    }

    public static void Delete(ArchiveEntry entry)
    {
        var list = Load();
        list.RemoveAll(e => e.Name == entry.Name);
        SaveList(list);
    }

    // Only count named slots (set_0..set_9), not the working entries key
    public static int CountNonEmpty(ArchiveEntry entry, string prefix)
    {
        if (!entry.Games.TryGetValue(prefix, out var d)) return 0;
        return d.Where(kv => kv.Key.StartsWith("set_"))
                .Count(kv => !string.IsNullOrWhiteSpace(kv.Value.Replace("|", "")));
    }
}
