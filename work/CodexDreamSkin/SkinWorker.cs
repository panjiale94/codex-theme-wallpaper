using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace CodexDreamSkin;

internal sealed class SkinWorker(string? profileOverride)
{
    private readonly SkinController controller = new(profileOverride);
    private readonly ThemeSettingsStore store = new(profileOverride);
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly MediaStore mediaStore = new();
    private readonly NativeLaunchMonitor launchMonitor = new(profileOverride);
    private readonly SemaphoreSlim wake = new(0, 1);
    private FileSystemWatcher? watcher;
    private bool connected;
    private string? lastPollError;
    private string? cachedMediaId;
    private string cachedSource = "";
    private string cachedKind = "art";
    private string cachedEffect = "fire";

    internal async Task RunAsync()
    {
        SkinLog.Write("皮肤守护启动：" + WorkerIdentity.PipeName(profileOverride));
        controller.MediaSource = mediaStore;
        Directory.CreateDirectory(WorkerIdentity.ProfileDirectory(profileOverride));
        watcher = new FileSystemWatcher(WorkerIdentity.ProfileDirectory(profileOverride), "DevToolsActivePort")
        { NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, EnableRaisingEvents = true };
        watcher.Created += (_, _) => Wake(); watcher.Changed += (_, _) => Wake(); watcher.Renamed += (_, _) => Wake();
        Wake();
        Task poller = PollAsync();
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                using var pipe = new NamedPipeServerStream(WorkerIdentity.PipeName(profileOverride), PipeDirection.InOut,
                    1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(lifetime.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                string? line;
                try { line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (Exception ex) when (ex is IOException or TimeoutException) { continue; }
                if (line is null || line.Length > 4096) continue;
                var request = JsonSerializer.Deserialize<WorkerRequest>(line);
                if (request is null) continue;
                WorkerResponse response;
                try
                {
                    if (request.Command == "stop")
                    {
                        await operationGate.WaitAsync();
                        try
                        {
                            await controller.RestoreAsync();
                            var settings = store.Load(); settings.Enabled = false; store.Save(settings, true);
                            await StartupIntegration.UpdateAsync(settings, profileOverride);
                            response = new WorkerResponse(true, 0, "效果已关闭。");
                        }
                        finally { operationGate.Release(); }
                    }
                    else if (request.Command == "version")
                        response = new WorkerResponse(true, 0, "普通启动自动恢复 v6", 6);
                    else if (request.Command == "apply")
                        response = new WorkerResponse(true, await ApplyAsync(request.AllowLaunch, true), "皮肤已应用。");
                    else response = new WorkerResponse(false, 0, "不支持的后台命令。");
                }
                catch (Exception ex) { SkinLog.Write(request.Command + " 失败：" + ex.Message); response = new WorkerResponse(false, 0, ex.Message); }
                try
                {
                    byte[] reply = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(response) + "\n");
                    await pipe.WriteAsync(reply); await pipe.FlushAsync();
                }
                catch (IOException) { /* 前台可能已关闭，守护仍保留页面连接。 */ }
                if (request.Command == "stop" && response.Success) lifetime.Cancel();
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            lifetime.Cancel();
            watcher?.Dispose();
            try { await poller; } catch (OperationCanceledException) { }
            await controller.DisposeAsync(); mediaStore.Clear();
            operationGate.Dispose(); wake.Dispose(); lifetime.Dispose();
            SkinLog.Write("皮肤守护已停止。");
        }
    }

    private async Task PollAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            await wake.WaitAsync(TimeSpan.FromSeconds(connected ? 3 : 1), lifetime.Token);
            if (!store.Load().Enabled) continue;
            try { await ApplyAsync(false, false); lastPollError = null; }
            catch (Exception ex)
            {
                if (lastPollError != ex.Message) { lastPollError = ex.Message; SkinLog.Write("自动恢复等待：" + ex.Message); }
            }
        }
    }

    private async Task<int> ApplyAsync(bool allowLaunch, bool enable)
    {
        await operationGate.WaitAsync();
        try
        {
            var settings = store.Load();
            if (!enable && !settings.Enabled) return 0;
            if (enable)
            {
                settings.Enabled = true; store.Save(settings, true);
                await StartupIntegration.UpdateAsync(settings, profileOverride);
            }
            if (!allowLaunch && !await controller.IsAvailableAsync())
            {
                if (connected)
                {
                    await controller.DisconnectAsync(); connected = false; cachedMediaId = null; mediaStore.Clear();
                    // 仅在客户端退出时释放上一轮调试连接的临时分配，不周期性裁剪工作集。
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, false, false);
                }
                await launchMonitor.RecoverAsync();
                return 0;
            }
            if (allowLaunch && !await controller.IsAvailableAsync())
            {
                if (await launchMonitor.RecoverAsync())
                    for (int attempt = 0; attempt < 40 && !await controller.IsAvailableAsync(); attempt++)
                        await Task.Delay(350);
            }
            LoadMedia(settings);
            var config = new SkinConfig(cachedKind, cachedSource, cachedMediaId!, settings.Panel / 100d,
                .04, cachedEffect, settings.Motion / 100d);
            int windows = await controller.ApplyAsync(config, allowLaunch);
            connected = true;
            return windows;
        }
        finally { operationGate.Release(); }
    }

    private void LoadMedia(ThemeSettings settings)
    {
        if (settings.PresetId == "custom")
        {
            var file = new FileInfo(settings.CustomMediaPath ?? "");
            if (!file.Exists || file.Length > 100 * 1024 * 1024) throw new IOException("自定义素材不存在或超过 100 MB。");
            string id = "custom:" + file.FullName + ":" + file.LastWriteTimeUtc.Ticks;
            if (cachedMediaId == id) return;
            string mime = file.Extension.ToLowerInvariant() switch
            {
                ".mp4" => "video/mp4", ".webm" => "video/webm", ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif", ".webp" => "image/webp", ".bmp" => "image/bmp", _ => "image/png"
            };
            cachedSource = mediaStore.SetMedia(file.FullName, mime);
            cachedKind = mime.StartsWith("video/") ? "video" : "image"; cachedEffect = "none"; cachedMediaId = id;
            return;
        }
        var preset = ThemeDefinitions.Definitions.FirstOrDefault(x => x.Id == settings.PresetId) ?? ThemeDefinitions.Definitions[0];
        if (cachedMediaId == preset.Id) return;
        cachedKind = preset.Id == "aurora" ? "aurora" : "art"; cachedEffect = preset.Effect;
        if (cachedKind == "aurora") cachedSource = "";
        else
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Themes", preset.File);
            if (!File.Exists(path)) throw new FileNotFoundException("缺少主题素材，请使用完整 v6 软件包。", path);
            cachedSource = mediaStore.SetMedia(path, "image/png");
        }
        cachedMediaId = preset.Id;
    }

    private void Wake()
    {
        try { if (wake.CurrentCount == 0) wake.Release(); } catch (Exception ex) when (ex is SemaphoreFullException or ObjectDisposedException) { }
    }
}
