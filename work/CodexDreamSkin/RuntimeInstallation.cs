using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;

namespace CodexDreamSkin;

internal static class RuntimeInstallation
{
    private static string? installedWorker;
    private static readonly SemaphoreSlim gate = new(1, 1);

    internal static string UserDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "CodexDreamSkin", "Users", WindowsIdentity.GetCurrent().User?.Value ?? throw new IOException("无法确定当前用户。"));

    internal static void PrepareUserDirectory()
    {
        string path = UserDirectory;
        Directory.CreateDirectory(path);
        var permissions = new DirectorySecurity();
        permissions.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null) })
            permissions.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(permissions);
    }

    internal static async Task<string> EnsureAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (installedWorker is not null && File.Exists(installedWorker)) return installedWorker;
            string source = Path.Combine(AppContext.BaseDirectory, "CodexDreamSkin.Worker.exe");
            // 只保护本软件当前用户的目录；不改变用户原有目录、证书或加密属性。
            PrepareUserDirectory();
            string root = Path.Combine(UserDirectory, "Runtime");
            if (Path.GetFullPath(AppContext.BaseDirectory).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return installedWorker = source;
            var files = new List<(string Source, string Relative, byte[] Digest)>();
            var names = new[] { "CodexDreamSkin.Worker.exe" }.Concat(ThemeDefinitions.Definitions
                .Where(t => t.Id != "aurora").Select(t => Path.Combine("Themes", t.File))).Distinct();
            using var identity = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string name in names)
            {
                string path = Path.Combine(AppContext.BaseDirectory, name);
                await using var stream = File.OpenRead(path);
                byte[] digest = await SHA256.HashDataAsync(stream);
                files.Add((path, name, digest));
                identity.AppendData(Encoding.UTF8.GetBytes(name)); identity.AppendData(digest);
            }
            // 内容独立目录避免覆盖正在运行的 EXE；安装副本不依赖开发目录或 D 盘挂载时序。
            string destination = Path.Combine(root, Convert.ToHexString(identity.GetHashAndReset())[..20]);
            foreach (var file in files)
            {
                string target = Path.Combine(destination, file.Relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    await using var existing = File.OpenRead(target);
                    if ((await SHA256.HashDataAsync(existing)).SequenceEqual(file.Digest)) continue;
                }
                // 不使用覆盖重命名，兼容本机 EFS 目录；复制完成并校验后才登记启动项。
                await using (var input = File.OpenRead(file.Source))
                await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                    await input.CopyToAsync(output);
                await using var copied = File.OpenRead(target);
                if (!(await SHA256.HashDataAsync(copied)).SequenceEqual(file.Digest))
                    throw new IOException("运行组件校验失败：" + file.Relative);
            }
            installedWorker = Path.Combine(destination, "CodexDreamSkin.Worker.exe");
            SkinLog.Write("启动运行组件已安装并校验：" + destination);
            return installedWorker;
        }
        finally { gate.Release(); }
    }
}
