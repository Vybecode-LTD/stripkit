using System.Text.Json;
using StripKit.Models;

namespace StripKit.Services;

/// <inheritdoc />
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;

    /// <param name="path">Override the settings file location (tests point this at a temp file);
    /// defaults to <c>%APPDATA%/StripKit/settings.json</c>.</param>
    public SettingsService(string? path = null)
    {
        _path = path ?? DefaultPath();
        Settings = Load(_path);
    }

    public AppSettings Settings { get; }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Settings, Json));
        }
        catch
        {
            // Settings are best-effort — never crash the app over a failed preference write.
        }
    }

    private static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt/unreadable → fall back to defaults.
        }
        return new AppSettings();
    }

    private static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StripKit", "settings.json");
}
