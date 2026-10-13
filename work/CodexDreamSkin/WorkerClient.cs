using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace CodexDreamSkin;

internal sealed record WorkerRequest(string Command, bool AllowLaunch = false);
internal sealed record WorkerResponse(bool Success, int Windows, string Message, int Version = 0);

internal static class WorkerIdentity
{
    internal static string ProfileDirectory(string? profileOverride) => Path.TrimEndingDirectorySeparator(
        Path.GetFullPath(profileOverride ?? Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData), "Codex", "web", "Codex")));

    internal static string PipeName(string? profileOverride)
    {
        string profile = ProfileDirectory(profileOverride);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.ToUpperInvariant())))[..16];
        return "codex-dream-skin-" + key;
    }
}

internal sealed class WorkerClient(string? profileOverride) : IAsyncDisposable
{
    private readonly string pipeName = WorkerIdentity.PipeName(profileOverride);
    private bool versionChecked;
    private uint serverPid;
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    private async Task<WorkerResponse> RequestAsync(WorkerRequest request)
    {
        bool launched = false;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            try { await pipe.ConnectAsync(250); }
            catch (Exception ex) when (ex is TimeoutException or IOException)
            {
                if (!launched || attempt % 10 == 0)
                {
                    string executable = Path.Combine(AppContext.BaseDirectory, "CodexDreamSkin.Worker.exe");
                    if (!File.Exists(executable)) throw new FileNotFoundException("缺少轻量守护程序，请使用完整 v5 软件包。", executable);
                    var start = new ProcessStartInfo(executable)
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                    start.ArgumentList.Add("--background");
                    if (profileOverride is not null) start.ArgumentList.Add("--profile-dir=" + profileOverride);
                    using var worker = Process.Start(start);
                    launched = true;
                }
                await Task.Delay(100); continue;
            }
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
            GetNamedPipeServerProcessId(pipe.SafePipeHandle, out serverPid);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(request));
            string? response = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(40));
            return response is null ? throw new IOException("后台守护没有返回状态。") :
                JsonSerializer.Deserialize<WorkerResponse>(response) ?? throw new IOException("后台响应无效。");
        }
        throw new IOException("无法连接皮肤守护，请重新打开工具。");
    }

    internal async Task<int> ApplyAsync(bool allowLaunch)
    {
        if (!versionChecked)
        {
            var version = await RequestAsync(new WorkerRequest("version"));
            if (!version.Success || version.Version < 5)
            {
                // 新版本主动接管旧守护，避免主题库升级后仍被旧资源覆盖。
                var stopped = await RequestAsync(new WorkerRequest("stop"));
                if (!stopped.Success)
                {
                    // v4 的死连接可能导致停止命令无法完成；仅在旧连接端口已失效时回收其守护。
                    await using var probe = new SkinController(profileOverride);
                    if (await probe.IsAvailableAsync() || serverPid == 0) throw new InvalidOperationException(stopped.Message);
                    using var legacy = Process.GetProcessById((int)serverPid);
                    string path = legacy.MainModule?.FileName ?? "";
                    if (Path.GetFileName(path) != "CodexDreamSkin.exe" || FileVersionInfo.GetVersionInfo(path).FileMajorPart > 4)
                        throw new InvalidOperationException("旧守护无法安全接管，请关闭旧版主题工具后重试。");
                    legacy.Kill(); await legacy.WaitForExitAsync();
                }
                await Task.Delay(500);
                version = await RequestAsync(new WorkerRequest("version"));
            }
            if (!version.Success || version.Version != 5)
                throw new InvalidOperationException("皮肤守护版本不匹配，请使用最新版本的设置工具。");
            versionChecked = true;
        }
        var response = await RequestAsync(new WorkerRequest("apply", allowLaunch));
        if (!response.Success) throw new InvalidOperationException(response.Message);
        return response.Windows;
    }

    internal async Task RestoreAsync()
    {
        var response = await RequestAsync(new WorkerRequest("stop"));
        if (!response.Success) throw new InvalidOperationException(response.Message);
    }

    // 设置窗口关闭只结束前台，不通知后台撤销皮肤。
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
