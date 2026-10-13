using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;

namespace CodexDreamSkin;

internal sealed class CdpClient : IAsyncDisposable
{
    private readonly ClientWebSocket socket = new();
    private readonly SemaphoreSlim sendGate = new(1, 1);
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly CancellationTokenSource lifetime = new();
    private Task? receiver;
    private int nextId;
    internal bool Connected => socket.State == WebSocketState.Open;
    internal event Action<JsonElement>? BindingCalled;

    internal static async Task<CdpClient> ConnectAsync(string address, int expectedPort)
    {
        var uri = new Uri(address);
        if (uri.Scheme != "ws" || uri.Host != "127.0.0.1" || uri.Port != expectedPort)
            throw new InvalidOperationException("调试地址必须是当前 Codex 的本机端口。");
        var client = new CdpClient();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await client.socket.ConnectAsync(uri, timeout.Token);
            client.receiver = client.ReceiveAsync();
            return client;
        }
        catch { await client.DisposeAsync(); throw; }
    }

    internal async Task<JsonElement> SendAsync(string method, object? parameters = null)
    {
        int id = Interlocked.Increment(ref nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            byte[] message = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters ?? new { } });
            // 发送通过信号量串行化；收到的响应按 CDP id 分发。
            await sendGate.WaitAsync(timeout.Token);
            try { await socket.SendAsync(message.AsMemory(), WebSocketMessageType.Text, true, timeout.Token); }
            finally { sendGate.Release(); }
            return await completion.Task.WaitAsync(timeout.Token);
        }
        finally { pending.TryRemove(id, out _); }
    }

    private async Task ReceiveAsync()
    {
        var buffer = new byte[16384];
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await socket.ReceiveAsync(buffer, lifetime.Token);
                    if (part.MessageType == WebSocketMessageType.Close)
                        throw new IOException("Codex 调试连接已关闭。");
                    message.Write(buffer, 0, part.Count);
                    if (message.Length > 8 * 1024 * 1024) throw new IOException("调试响应超过允许大小。");
                } while (!part.EndOfMessage);
                using var json = JsonDocument.Parse(message.ToArray());
                var root = json.RootElement;
                if (!root.TryGetProperty("id", out var id) || !pending.TryRemove(id.GetInt32(), out var completion))
                {
                    if (root.TryGetProperty("method", out var method) && method.GetString() == "Runtime.bindingCalled")
                        BindingCalled?.Invoke(root.GetProperty("params").Clone());
                    continue;
                }
                if (root.TryGetProperty("error", out var error)) completion.TrySetException(new InvalidOperationException(error.ToString()));
                else completion.TrySetResult(root.GetProperty("result").Clone());
            }
        }
        catch (Exception ex)
        {
            foreach (var entry in pending)
                if (pending.TryRemove(entry.Key, out var completion)) completion.TrySetException(ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel(); socket.Abort();
        if (receiver is not null) try { await receiver; } catch (Exception) { }
        socket.Dispose(); lifetime.Dispose(); sendGate.Dispose();
    }
}
