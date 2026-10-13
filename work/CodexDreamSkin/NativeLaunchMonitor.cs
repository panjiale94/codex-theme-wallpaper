using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CodexDreamSkin;

// 仅接管刚启动的本资料目录主进程；不重启已运行的客户端或其他 ChatGPT 应用。
internal sealed class NativeLaunchMonitor(string? profileOverride)
{
    private static readonly TimeSpan StartupWindow = TimeSpan.FromSeconds(15);
    private readonly string profile = WorkerIdentity.ProfileDirectory(profileOverride);
    private readonly Dictionary<int, long> examined = new();

    internal async Task<bool> RecoverAsync()
    {
        Process[] processes = Process.GetProcessesByName("ChatGPT");
        try
        {
            var live = processes.Select(p => p.Id).ToHashSet();
            foreach (int stale in examined.Keys.Where(id => !live.Contains(id)).ToArray()) examined.Remove(stale);
            foreach (var process in processes)
            {
                try
                {
                    DateTime started = process.StartTime.ToUniversalTime();
                    if (examined.TryGetValue(process.Id, out long previous) && previous == started.Ticks) continue;
                    if (DateTime.UtcNow - started > StartupWindow)
                    { examined[process.Id] = started.Ticks; continue; }
                    string executable = process.MainModule?.FileName ?? "";
                    if (!executable.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) ||
                        !Path.GetFileName(executable).Equals("ChatGPT.exe", StringComparison.OrdinalIgnoreCase))
                    { examined[process.Id] = started.Ticks; continue; }
                    string[] args = ReadArguments(process);
                    examined[process.Id] = started.Ticks;
                    if (args.Any(a => a.StartsWith("--type=", StringComparison.Ordinal)) ||
                        args.Any(a => a.StartsWith("--remote-debugging-port", StringComparison.Ordinal)) ||
                        !MatchesProfile(args)) continue;
                    // 同一 PID/启动时间只尝试一次；替代进程携带调试参数，不会循环接管。
                    var launch = new ProcessStartInfo(executable) { UseShellExecute = false };
                    foreach (string arg in args.Skip(1)) launch.ArgumentList.Add(arg);
                    launch.ArgumentList.Add("--remote-debugging-port=0");
                    launch.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
                    if (!args.Any(a => a == "--user-data-dir" || a.StartsWith("--user-data-dir=")))
                        launch.ArgumentList.Add("--user-data-dir=" + profile);
                    // 再核对进程身份。当前对话所在的旧进程超过启动窗口，无法进入此分支。
                    if (process.HasExited || process.StartTime.ToUniversalTime() != started ||
                        DateTime.UtcNow - started > StartupWindow) continue;
                    SkinLog.Write($"普通启动自动接管：PID {process.Id}，启动年龄 {(DateTime.UtcNow - started).TotalSeconds:F1} 秒。");
                    bool closing = process.CloseMainWindow();
                    if (closing)
                    {
                        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(700)); }
                        catch (TimeoutException) { }
                    }
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                    using var replacement = Process.Start(launch) ?? throw new IOException("无法启动主题连接客户端。");
                    SkinLog.Write($"普通启动接管完成：新 PID {replacement.Id}，已保留原启动参数。");
                    return true;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or TimeoutException)
                { SkinLog.Write("启动检测：" + ex.Message); }
            }
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private bool MatchesProfile(string[] args)
    {
        string? requested = null;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--user-data-dir=")) requested = args[i][16..];
            else if (args[i] == "--user-data-dir" && i + 1 < args.Length) requested = args[++i];
        }
        if (requested is null) return profile.Equals(WorkerIdentity.ProfileDirectory(null), StringComparison.OrdinalIgnoreCase);
        try { return WorkerIdentity.ProfileDirectory(requested).Equals(profile, StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static string[] ReadArguments(Process process)
    {
        // 从内核读取当前用户进程的命令行，避免每次检测启动 PowerShell/WMI。
        const int CommandLineInformation = 60;
        NtQueryInformationProcess(process.Handle, CommandLineInformation, IntPtr.Zero, 0, out int size);
        if (size <= 0 || size > 256 * 1024) throw new IOException("无法读取启动参数。");
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            int status = NtQueryInformationProcess(process.Handle, CommandLineInformation, buffer, size, out _);
            if (status != 0) throw new IOException("读取启动参数失败：" + status);
            var value = Marshal.PtrToStructure<UnicodeString>(buffer);
            string command = Marshal.PtrToStringUni(value.Buffer, value.Length / 2) ?? "";
            IntPtr argv = CommandLineToArgvW(command, out int count);
            if (argv == IntPtr.Zero) throw new Win32Exception();
            try
            {
                var args = new string[count];
                for (int i = 0; i < count; i++) args[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "";
                return args;
            }
            finally { LocalFree(argv); }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { internal ushort Length; internal ushort MaximumLength; internal IntPtr Buffer; }
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr information,
        int length, out int returnLength);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argumentCount);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}
