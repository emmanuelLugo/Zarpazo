using System.IO;
using System.Text.Json;

namespace Zarpazo.Settings;

/// <summary>Keeps the settings in a JSON file under the user's AppData.</summary>
internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _path;

    public static string DefaultPath { get; } = Update.TestOverrides.Data(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zarpazo", "settings.json"), "settings.json");

    public SettingsStore(string path)
    {
        _path = path;
        Current = Load();
    }

    public string FilePath => _path;

    public AppSettings Current { get; }

    /// <exception cref="IOException">The file could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The folder is not writable.</exception>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(Current, Options));
    }

    // A missing or damaged file is not an error: the app starts with the defaults and the next save replaces it.
    private AppSettings Load()
    {
        try
        {
            AppSettings settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }
}
