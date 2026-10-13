using System.Diagnostics;
using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace CodexDreamSkin;

internal sealed record SkinConfig(string Kind, string Source, string MediaId, double Panel, double Shade,
    string Effect = "none", double Motion = .7);

internal sealed class SkinController : IAsyncDisposable
{
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, Session> sessions = new();
    private readonly string profileDirectory;
    private readonly string runtime;
    private readonly bool isAlternateProfile;
    private readonly string journalPath;
    private readonly string journalRegistryPath;
    private bool recovered;
    private int port;
    internal MediaStore? MediaSource { get; set; }
    private sealed record Target(string id, string type, string url, string webSocketDebuggerUrl);
    private sealed record JournalEntry(string TargetId, string Address, int Port, string ScriptId);
    private sealed class Session(CdpClient client, string address, int port)
    {
        internal CdpClient Client { get; } = client;
        internal string Address { get; } = address;
        internal int Port { get; } = port;
        internal string? ScriptId { get; set; }
        internal string? Revision { get; set; }
        internal bool BindingReady { get; set; }
        internal readonly SemaphoreSlim TransferGate = new(1, 1);
    }

    internal SkinController(string? profileOverride = null)
    {
        isAlternateProfile = profileOverride is not null;
        profileDirectory = WorkerIdentity.ProfileDirectory(profileOverride);
        string profileKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileDirectory.ToUpperInvariant())))[..16];
        journalRegistryPath = @"Software\CodexDreamSkin\Profiles\" + profileKey;
        journalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CodexDreamSkin", $"injection-{profileKey}.json");
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexDreamSkin.SkinRuntime.js")!;
        using var reader = new StreamReader(stream);
        using var effectStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexDreamSkin.EffectRuntime.js")!;
        using var effectReader = new StreamReader(effectStream);
        runtime = effectReader.ReadToEnd() + "\n" + reader.ReadToEnd();
    }

    internal async Task<int> ApplyAsync(SkinConfig config, bool allowLaunch)
    {
        await gate.WaitAsync();
        try
        {
            var targets = await ReadTargetsAsync();
            if (targets.Count == 0 && allowLaunch)
            {
                if (!isAlternateProfile && IsCodexRunning())
                    throw new InvalidOperationException("当前 Codex 尚未开启主题连接。请从 Codex“文件 → 退出”彻底退出，再通过桌面“Codex（动态皮肤）”入口打开。聊天会保留。");
                await LaunchCodexAsync();
                for (int attempt = 0; attempt < 40 && targets.Count == 0; attempt++)
                {
                    await Task.Delay(350);
                    targets = await ReadTargetsAsync();
                }
            }
            if (targets.Count == 0) throw new InvalidOperationException("尚未连接到 Codex。请确认 Codex 已通过本工具启动。");
            if (!recovered)
            {
                await AdoptExistingAsync(targets);
                recovered = true;
            }
            var liveIds = targets.Select(t => t.id).ToHashSet();
            foreach (string stale in sessions.Keys.Where(id => !liveIds.Contains(id)).ToArray())
            {
                await sessions[stale].Client.DisposeAsync(); sessions.Remove(stale);
            }
            string revision = $"{config.MediaId}:{config.Panel:F3}:{config.Shade:F3}:{config.Effect}:{config.Motion:F3}";
            string? source = null;
            foreach (Target target in targets)
            {
                if (!sessions.TryGetValue(target.id, out var session) || !session.Client.Connected)
                {
                    if (session is not null) await session.Client.DisposeAsync();
                    session = new Session(await CdpClient.ConnectAsync(target.webSocketDebuggerUrl, port), target.webSocketDebuggerUrl, port);
                    sessions[target.id] = session;
                }
                if (session.Revision == revision)
                {
                    var check = await session.Client.SendAsync("Runtime.evaluate", new
                    {
                        expression = "({present:Boolean(window.__codexDreamSkin && document.getElementById('codex-dream-background')),error:document.getElementById('codex-dream-background')?.dataset.error})",
                        returnByValue = true
                    });
                    if (check.TryGetProperty("result", out var result) && result.TryGetProperty("value", out var state))
                    {
                        if (state.TryGetProperty("error", out var error))
                            throw new InvalidOperationException("素材无法播放，请更换 H.264 MP4 或 WebM 视频。错误：" + error.GetString());
                        if (state.TryGetProperty("present", out var present) && present.ValueKind == JsonValueKind.True) continue;
                    }
                }
                if (!session.BindingReady)
                {
                    Session active = session;
                    session.Client.BindingCalled += value => _ = TransferMediaAsync(active, value);
                    await session.Client.SendAsync("Runtime.enable");
                    await session.Client.SendAsync("Runtime.addBinding", new { name = "__codexDreamRead" });
                    session.BindingReady = true;
                }
                if (session.ScriptId is not null)
                {
                    try { await session.Client.SendAsync("Page.removeScriptToEvaluateOnNewDocument", new { identifier = session.ScriptId }); }
                    catch (InvalidOperationException) { /* 旧调试连接退出后，注册可能已失效。 */ }
                }
                // 稳定运行时只检查页面状态，不反复序列化大图片或视频。
                source ??= runtime + "\nwindow.__codexDreamSkin?.apply(" +
                    JsonSerializer.Serialize(config, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) + ");";
                var registration = await session.Client.SendAsync("Page.addScriptToEvaluateOnNewDocument", new { source });
                session.ScriptId = registration.GetProperty("identifier").GetString();
                await SaveJournalAsync();
                var applied = await session.Client.SendAsync("Runtime.evaluate", new { expression = source, returnByValue = true });
                if (applied.TryGetProperty("exceptionDetails", out var exception))
                    throw new InvalidOperationException("页面皮肤加载失败：" + exception.ToString());
                session.Revision = revision;
            }
            return targets.Count;
        }
        finally { gate.Release(); }
    }

    private async Task<List<Target>> ReadTargetsAsync()
    {
        try
        {
            string file = Path.Combine(profileDirectory, "DevToolsActivePort");
            if (!File.Exists(file)) return [];
            string firstLine = (await File.ReadAllLinesAsync(file)).FirstOrDefault() ?? "";
            if (!int.TryParse(firstLine, out int foundPort) || foundPort is < 1024 or > 65535) return [];
            string data = await http.GetStringAsync($"http://127.0.0.1:{foundPort}/json/list");
            var targets = JsonSerializer.Deserialize<List<Target>>(data) ?? [];
            port = foundPort;
            // 只访问 Codex 自己的主界面，排除网页、插件、悬浮宠物。
            return targets.Where(t => t.type == "page" &&
                (t.url == "app://-/index.html" || t.url.StartsWith("app://-/detached-window.html", StringComparison.Ordinal)) &&
                !string.IsNullOrEmpty(t.webSocketDebuggerUrl)).ToList();
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or TaskCanceledException or JsonException) { return []; }
    }

    internal async Task<bool> IsAvailableAsync() => (await ReadTargetsAsync()).Count > 0;

    internal async Task DisconnectAsync()
    {
        await gate.WaitAsync();
        try
        {
            foreach (var session in sessions.Values) await session.Client.DisposeAsync();
            sessions.Clear(); recovered = false;
        }
        finally { gate.Release(); }
    }

    private async Task TransferMediaAsync(Session session, JsonElement call)
    {
        string? id = null; int context = 0;
        byte[]? buffer = null; bool acquired = false;
        try
        {
            if (call.GetProperty("name").GetString() != "__codexDreamRead") return;
            string? payload = call.GetProperty("payload").GetString();
            if (payload is null || payload.Length > 512) return;
            using var request = JsonDocument.Parse(payload);
            string source = request.RootElement.GetProperty("source").GetString() ?? "";
            id = request.RootElement.GetProperty("id").GetString();
            if (id is null || id.Length > 80) return;
            context = call.GetProperty("executionContextId").GetInt32();
            var entry = MediaSource?.Find(source);
            if (entry is null) return;
            await session.TransferGate.WaitAsync(); acquired = true;
            // 16 KB 原始块使 Base64 字符串与 JSON 缓冲均小于大对象堆阈值。
            buffer = ArrayPool<byte>.Shared.Rent(16384);
            await using var file = new FileStream(entry.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                16384, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (file.Length > 100L * 1024 * 1024) throw new IOException("素材超过 100 MB。");
            while (session.Client.Connected)
            {
                int count = await file.ReadAsync(buffer.AsMemory(0, 16384));
                string chunk = count == 0 ? "" : Convert.ToBase64String(buffer, 0, count);
                string expression = "window.__codexDreamTransfer?.accept(" + JsonSerializer.Serialize(id) + "," +
                    JsonSerializer.Serialize(chunk) + "," + JsonSerializer.Serialize(entry.Mime) + "," + (count == 0 ? "true" : "false") + ")";
                var result = await session.Client.SendAsync("Runtime.evaluate", new { expression, contextId = context, returnByValue = true });
                if (count == 0 || result.TryGetProperty("exceptionDetails", out _) ||
                    !result.GetProperty("result").TryGetProperty("value", out var accepted) || accepted.ValueKind != JsonValueKind.True) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or JsonException or KeyNotFoundException)
        {
            if (id is not null && session.Client.Connected)
                try { await session.Client.SendAsync("Runtime.evaluate", new {contextId = context,
                    expression = "window.__codexDreamTransfer?.fail(" + JsonSerializer.Serialize(id) + ",\"素材传输中断，请重新应用主题\")"}); } catch (Exception) { }
        }
        finally { if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer); if (acquired) session.TransferGate.Release(); }
    }

    private static bool IsCodexRunning()
    {
        foreach (Process process in Process.GetProcessesByName("ChatGPT"))
        {
            using (process)
            {
                try
                {
                    if (process.MainModule?.FileName?.Contains(
                        "OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) == true) return true;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        return false;
    }

    internal async Task LaunchCodexAsync(bool debugging = true)
    {
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodexDreamSkin", "codex-install.txt");
        string executable = File.Exists(cache) ? (await File.ReadAllTextAsync(cache)).Trim() : "";
        if (!File.Exists(executable)) executable = await LocateCodexAsync();
        Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
        if (!File.Exists(cache) || (await File.ReadAllTextAsync(cache)).Trim() != executable) await File.WriteAllTextAsync(cache, executable);
        var launch = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (debugging)
        {
            launch.ArgumentList.Add("--remote-debugging-port=0");
            launch.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
        }
        launch.ArgumentList.Add("--user-data-dir=" + profileDirectory);
        using var launched = Process.Start(launch);
    }

    private static async Task<string> LocateCodexAsync()
    {
        var locate = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        locate.ArgumentList.Add("-NoProfile"); locate.ArgumentList.Add("-Command");
        locate.ArgumentList.Add("(Get-AppxPackage OpenAI.Codex | Select-Object -First 1).InstallLocation");
        using var process = Process.Start(locate) ?? throw new InvalidOperationException("无法检测 Codex 安装目录。");
        string installation = (await process.StandardOutput.ReadToEndAsync()).Trim();
        await process.WaitForExitAsync();
        string executable = Path.Combine(installation, "app", "ChatGPT.exe");
        if (!File.Exists(executable)) throw new InvalidOperationException("没有找到 Microsoft Store 版 Codex。请先安装 Codex。");
        return executable;
    }

    internal async Task RestoreAsync()
    {
        await gate.WaitAsync();
        try
        {
            // 工具可能已关闭再重新打开；从日志接管以前的页面注册后再关闭。
            var targets = await ReadTargetsAsync();
            if (!recovered) { await AdoptExistingAsync(targets); recovered = true; }
            foreach (var target in targets.Where(t => !sessions.ContainsKey(t.id)))
                sessions[target.id] = new Session(await CdpClient.ConnectAsync(target.webSocketDebuggerUrl, port), target.webSocketDebuggerUrl, port);
            var failed = new List<JournalEntry>();
            foreach (var pair in sessions)
            {
                var session = pair.Value;
                if (!session.Client.Connected) { await session.Client.DisposeAsync(); continue; }
                try
                {
                    if (session.ScriptId is not null)
                    {
                        try { await session.Client.SendAsync("Page.removeScriptToEvaluateOnNewDocument", new { identifier = session.ScriptId }); }
                        catch (InvalidOperationException) { /* 已移除的注册不妨碍清理当前页面。 */ }
                    }
                    await session.Client.SendAsync("Runtime.evaluate", new { expression = "window.__codexDreamSkin?.restore()", returnByValue = true });
                }
                catch (Exception)
                {
                    if (session.ScriptId is not null)
                        failed.Add(new JournalEntry(pair.Key, session.Address, session.Port, session.ScriptId));
                }
                await session.Client.DisposeAsync();
            }
            sessions.Clear();
            if (failed.Count == 0) await WriteJournalAsync([]);
            else
            {
                await WriteJournalAsync(failed);
                throw new IOException("部分 Codex 窗口暂时无法连接。请再次点击关闭效果，或重新启动 Codex。");
            }
        }
        finally { gate.Release(); }
    }

    private Task SaveJournalAsync() => WriteJournalAsync(sessions.Where(x => x.Value.ScriptId is not null)
        .Select(x => new JournalEntry(x.Key, x.Value.Address, x.Value.Port, x.Value.ScriptId!)).ToList());

    private Task WriteJournalAsync(List<JournalEntry> entries)
    {
        using var key = Registry.CurrentUser.CreateSubKey(journalRegistryPath);
        key.SetValue("Journal", JsonSerializer.Serialize(entries), RegistryValueKind.String);
        return Task.CompletedTask;
    }

    private async Task AdoptExistingAsync(List<Target> targets)
    {
        using var key = Registry.CurrentUser.OpenSubKey(journalRegistryPath);
        string? saved = key?.GetValue("Journal") as string;
        if (saved is null && !File.Exists(journalPath)) return;
        List<JournalEntry> entries;
        try { entries = JsonSerializer.Deserialize<List<JournalEntry>>(saved ?? await File.ReadAllTextAsync(journalPath)) ?? []; }
        catch (JsonException) { await WriteJournalAsync([]); return; }
        foreach (var entry in entries)
        {
            if (!targets.Any(t => t.id == entry.TargetId && t.webSocketDebuggerUrl == entry.Address) || entry.Port != port) continue;
            if (!sessions.ContainsKey(entry.TargetId))
                sessions[entry.TargetId] = new Session(await CdpClient.ConnectAsync(entry.Address, entry.Port), entry.Address, entry.Port)
                { ScriptId = entry.ScriptId };
        }
    }

    public async ValueTask DisposeAsync()
    {
        // 退出管理器只断开连接；页面背景和刷新注册继续由 Codex 执行。
        await gate.WaitAsync();
        try
        {
            foreach (var session in sessions.Values) await session.Client.DisposeAsync();
            sessions.Clear();
        }
        finally { gate.Release(); }
        http.Dispose(); gate.Dispose();
    }
}
