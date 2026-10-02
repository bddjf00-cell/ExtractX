using System.IO;
using System.Text.Json;

namespace ExtractX.Core;

public sealed class AppStore
{
    private static readonly string BaseDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ExtractX");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public List<HistoryEntry> History { get; set; } = new();
    public List<FavoriteFolder> Favorites { get; set; } = new();
    public List<SavedPassword> Passwords { get; set; } = new();
    public AppSettings Settings { get; set; } = new();

    public static AppStore Load()
    {
        var store = new AppStore();
        try
        {
            Directory.CreateDirectory(BaseDir);
            store.History = Read<List<HistoryEntry>>("history.json") ?? new();
            store.Favorites = Read<List<FavoriteFolder>>("favorites.json") ?? DefaultFavorites();
            store.Passwords = Read<List<SavedPassword>>("passwords.json") ?? new();
            store.Settings = Read<AppSettings>("settings.json") ?? new();
        }
        catch { /* arranque limpio ante fichero corrupto */ }
        return store;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(BaseDir);
            Write("history.json", History);
            Write("favorites.json", Favorites);
            Write("passwords.json", Passwords);
            Write("settings.json", Settings);
        }
        catch { }
    }

    private static List<FavoriteFolder> DefaultFavorites() => new()
    {
        new() { Name = "Descargas", Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") },
        new() { Name = "Documentos", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
        new() { Name = "Escritorio", Path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) },
    };

    private static T? Read<T>(string file)
    {
        var p = Path.Combine(BaseDir, file);
        if (!File.Exists(p)) return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(p), JsonOpts);
    }

    private static void Write<T>(string file, T value)
        => File.WriteAllText(Path.Combine(BaseDir, file), JsonSerializer.Serialize(value, JsonOpts));
}
