using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace CodexDreamSkin;

internal sealed record ThemeSettings
{
    public bool Enabled { get; set; }
    public bool AutoStart { get; set; } = true;
    public string PresetId { get; set; } = "center-shrine";
    public string? CustomMediaPath { get; set; }
    public double Panel { get; set; } = 35;
    public double Motion { get; set; } = 100;
}

internal sealed class ThemeSettingsStore
{
    private readonly string path;
    private readonly string registryPath;
    private readonly string sharedPath;
    internal ThemeSettingsStore(string? profileOverride)
    {
        string profile = WorkerIdentity.ProfileDirectory(profileOverride);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.ToUpperInvariant())))[..16];
        registryPath = @"Software\CodexDreamSkin\Profiles\" + key;
        sharedPath = ProfileStorage.PathFor(key, "settings");
        path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CodexDreamSkin", "settings-" + key + ".json");
    }

    internal ThemeSettings Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(registryPath);
            string? json = key?.GetValue("Settings") as string;
            if (File.Exists(sharedPath))
            {
                using var shared = new FileStream(sharedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(shared);
                json = reader.ReadToEnd();
            }
            ThemeSettings settings;
            if (json is not null) settings = JsonSerializer.Deserialize<ThemeSettings>(json) ?? new ThemeSettings();
            else
            {
                if (!File.Exists(path)) return new ThemeSettings();
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                settings = JsonSerializer.Deserialize<ThemeSettings>(stream) ?? new ThemeSettings();
            }
            settings.Panel = double.IsFinite(settings.Panel) ? Math.Clamp(settings.Panel, 10, 85) : 35;
            settings.Motion = double.IsFinite(settings.Motion) ? Math.Clamp(settings.Motion, 0, 100) : 70;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException) { return new ThemeSettings(); }
    }

    internal void Save(ThemeSettings settings, bool durable = false)
    {
        string json = JsonSerializer.Serialize(settings);
        // 共享主配置位于已验证可原子替换的 ProgramData 私有目录；注册表保留兼容镜像。
        ProfileStorage.Write(sharedPath, json, durable);
        using var key = Registry.CurrentUser.CreateSubKey(registryPath);
        key.SetValue("Settings", json, RegistryValueKind.String);
        if (durable) key.Flush();
    }
}
