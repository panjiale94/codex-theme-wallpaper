using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexDreamSkin;

internal sealed record ThemeSettings
{
    public bool Enabled { get; set; }
    public string PresetId { get; set; } = "center-shrine";
    public string? CustomMediaPath { get; set; }
    public double Panel { get; set; } = 35;
    public double Motion { get; set; } = 100;
}

internal sealed class ThemeSettingsStore
{
    private readonly string path;
    internal ThemeSettingsStore(string? profileOverride)
    {
        string profile = WorkerIdentity.ProfileDirectory(profileOverride);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.ToUpperInvariant())))[..16];
        path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CodexDreamSkin", "settings-" + key + ".json");
    }

    internal ThemeSettings Load()
    {
        try
        {
            if (!File.Exists(path)) return new ThemeSettings();
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var settings = JsonSerializer.Deserialize<ThemeSettings>(stream) ?? new ThemeSettings();
            settings.Panel = double.IsFinite(settings.Panel) ? Math.Clamp(settings.Panel, 10, 85) : 35;
            settings.Motion = double.IsFinite(settings.Motion) ? Math.Clamp(settings.Motion, 0, 100) : 70;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return new ThemeSettings(); }
    }

    internal void Save(ThemeSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        // 原子替换，避免退出过程中留下不完整的配置。
        File.Move(temporary, path, true);
    }
}
