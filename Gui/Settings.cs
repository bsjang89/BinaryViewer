using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Styling;
using BinaryViewer.Core;

namespace BinaryViewer.Gui;

/// <summary>
/// Remembers the language and theme the user picked, so the choice survives a restart even when
/// it differs from the system defaults.
/// </summary>
public sealed class Settings
{
    public string? Language { get; set; }
    public string? Theme { get; set; }

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BinaryViewer", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* unreadable or corrupt: fall back to defaults */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* read only profile: preferences just do not persist */ }
    }

    /// <summary>Applies the stored language unless the command line already set one.</summary>
    public void ApplyLanguage(bool commandLineWins)
    {
        if (commandLineWins) return;
        if (S.TryParse(Language, out var language)) S.Current = language;
    }

    [JsonIgnore]
    public ThemeVariant? StoredTheme => Theme?.ToLowerInvariant() switch
    {
        "dark" => ThemeVariant.Dark,
        "light" => ThemeVariant.Light,
        _ => null
    };

    public void Remember(Language language, ThemeVariant theme)
    {
        Language = language == Core.Language.Korean ? "ko" : "en";
        Theme = theme == ThemeVariant.Dark ? "dark" : "light";
        Save();
    }
}
