using CodexDreamSkin;
using System.Runtime.InteropServices;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);

    private static int Main(string[] args)
    {
        string? profile = args.FirstOrDefault(x => x.StartsWith("--profile-dir="))?[14..];
        if (args.Contains("--launch"))
        {
            try
            {
                LaunchAsync(profile).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex) { MessageBox(IntPtr.Zero, ex.Message, "Codex 动态皮肤", 0x10); return 1; }
        }
        if (args.Contains("--startup") && !new ThemeSettingsStore(profile).Load().Enabled) return 0;
        using var mutex = new Mutex(false, "Local\\" + WorkerIdentity.PipeName(profile) + ".Worker5");
        try { if (!mutex.WaitOne(0)) return 0; } catch (AbandonedMutexException) { }
        try { new SkinWorker(profile).RunAsync().GetAwaiter().GetResult(); return 0; }
        catch (Exception ex) { SkinLog.Write("轻量守护退出：" + ex); return 1; }
        finally { mutex.ReleaseMutex(); }
    }

    private static async Task LaunchAsync(string? profile)
    {
        bool enabled = new ThemeSettingsStore(profile).Load().Enabled;
        if (enabled) await new WorkerClient(profile).ApplyAsync(true);
        await using var controller = new SkinController(profile);
        // 第二次启动由 Electron 的单实例机制激活已有窗口，包含最小化和托盘场景。
        await controller.LaunchCodexAsync(enabled);
    }
}
