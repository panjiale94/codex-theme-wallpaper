using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
                    var start = new ProcessStartInfo(Environment.ProcessPath!)
                    { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
                    start.ArgumentList.Add("--background");
                    if (profileOverride is not null) start.ArgumentList.Add("--profile-dir=" + profileOverride);
                    using var worker = Process.Start(start);
                    launched = true;
                }
                await Task.Delay(100); continue;
            }
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
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
            if (!version.Success || version.Version < 4)
            {
                // 新版本主动接管旧守护，避免主题库升级后仍被旧资源覆盖。
                var stopped = await RequestAsync(new WorkerRequest("stop"));
                if (!stopped.Success) throw new InvalidOperationException(stopped.Message);
                await Task.Delay(500);
                version = await RequestAsync(new WorkerRequest("version"));
            }
            if (!version.Success || version.Version != 4)
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
