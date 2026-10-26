using System.IO;
using System.Text;

namespace CodexDreamSkin;

// 前台、普通启动和计划任务共享同一份文件，避免不同启动环境的配置副本不一致。
internal static class ProfileStorage
{
    internal static string PathFor(string key, string kind) => Path.Combine(RuntimeInstallation.UserDirectory,
        "Profiles", kind + "-" + key + ".json");

    internal static void Write(string path, string json, bool durable = false)
    {
        RuntimeInstallation.PrepareUserDirectory();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] data = Encoding.UTF8.GetBytes(json);
                stream.Write(data);
                stream.Flush(durable);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
