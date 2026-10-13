using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace CodexDreamSkin;

internal sealed class SkinWorker(string? profileOverride)
{
    private readonly SkinController controller = new(profileOverride);
    private readonly ThemeSettingsStore store = new(profileOverride);
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private string? cachedMediaId;
    private string cachedSource = "";
    private string cachedKind = "art";
    private string cachedEffect = "fire";

    internal async Task RunAsync()
    {
        SkinLog.Write("皮肤守护启动：" + WorkerIdentity.PipeName(profileOverride));
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
                            var settings = store.Load(); settings.Enabled = false; store.Save(settings);
                            response = new WorkerResponse(true, 0, "效果已关闭。");
                        }
                        finally { operationGate.Release(); }
                    }
                    else if (request.Command == "version")
                        response = new WorkerResponse(true, 0, "中央人物强动效 v4", 4);
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
            try { await poller; } catch (OperationCanceledException) { }
            await controller.DisposeAsync(); operationGate.Dispose(); lifetime.Dispose();
            SkinLog.Write("皮肤守护已停止。");
        }
    }

    private async Task PollAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), lifetime.Token);
            if (!store.Load().Enabled) continue;
            try { await ApplyAsync(false, false); }
            catch (Exception) { /* 客户端未运行或暂时断线时保持安静，下一轮继续连接。 */ }
        }
    }

    private async Task<int> ApplyAsync(bool allowLaunch, bool enable)
    {
        await operationGate.WaitAsync();
        try
        {
            var settings = store.Load();
            if (!enable && !settings.Enabled) return 0;
            await LoadMediaAsync(settings);
            var config = new SkinConfig(cachedKind, cachedSource, cachedMediaId!, settings.Panel / 100d,
                .04, cachedEffect, settings.Motion / 100d);
            int windows = await controller.ApplyAsync(config, allowLaunch);
            if (enable && !settings.Enabled) { settings.Enabled = true; store.Save(settings); }
            return windows;
        }
        finally { operationGate.Release(); }
    }

    private async Task LoadMediaAsync(ThemeSettings settings)
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
            cachedSource = $"data:{mime};base64," + Convert.ToBase64String(await File.ReadAllBytesAsync(file.FullName));
            cachedKind = mime.StartsWith("video/") ? "video" : "image"; cachedEffect = "none"; cachedMediaId = id;
            return;
        }
        var preset = ThemeCatalog.Definitions.FirstOrDefault(x => x.Id == settings.PresetId) ?? ThemeCatalog.Definitions[0];
        if (cachedMediaId == preset.Id) return;
        cachedKind = preset.Id == "aurora" ? "aurora" : "art"; cachedEffect = preset.Effect;
        if (cachedKind == "aurora") cachedSource = "";
        else
        {
            using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/" + preset.File))!.Stream;
            using var memory = new MemoryStream(); await stream.CopyToAsync(memory);
            cachedSource = "data:image/png;base64," + Convert.ToBase64String(memory.ToArray());
        }
        cachedMediaId = preset.Id;
    }
}
