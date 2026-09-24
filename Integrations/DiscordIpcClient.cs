using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using JelloClient.Services;

namespace JelloClient.Integrations;

internal enum DiscordOpcode
{
    Handshake = 0,
    Frame = 1,
    Close = 2,
    Ping = 3,
    Pong = 4
}

internal readonly record struct DiscordActivityResult(bool Acknowledged, string? Error);

internal sealed class DiscordIpcClient : IDisposable
{
    private const int MaxPayloadBytes = 64 * 1024;
    private const int AcknowledgementTimeoutMs = 3000;

    private readonly string _clientId;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private NamedPipeClientStream? _pipe;
    private bool _disposed;

    public DiscordIpcClient(string clientId)
    {
        _clientId = clientId;
    }

    public bool IsConnected => _pipe?.IsConnected == true;

    public int? PipeIndex { get; private set; }

    public string? UserName { get; private set; }

    public async Task<bool> ConnectAsync(CancellationToken ct)
    {
        if (IsConnected)
        {
            return true;
        }

        for (int index = 0; index < 10; index++)
        {
            var pipe = new NamedPipeClientStream(".", $"discord-ipc-{index}", PipeDirection.InOut, PipeOptions.Asynchronous);

            try
            {
                await pipe.ConnectAsync(500, ct).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                continue;
            }

            _pipe = pipe;

            try
            {
                await _gate.WaitAsync(ct).ConfigureAwait(false);

                try
                {
                    await WriteFrameAsync(DiscordOpcode.Handshake, JsonSerializer.Serialize(new { v = 1, client_id = _clientId }), ct)
                        .ConfigureAwait(false);

                    var response = await ReadFrameAsync(ct).ConfigureAwait(false)
                        ?? throw new IOException("Discord closed the pipe during the handshake.");

                    if (response.Opcode == DiscordOpcode.Close)
                    {
                        Log.Write("DiscordIpcClient::Connect", $"discord-ipc-{index} rejected the handshake: {DescribeRejection(response.Payload)}");

                        await pipe.DisposeAsync().ConfigureAwait(false);
                        _pipe = null;

                        return false;
                    }

                    PipeIndex = index;
                    UserName = ReadUserName(response.Payload);

                    Log.Write("DiscordIpcClient::Connect", $"Handshake accepted on discord-ipc-{index} as {UserName ?? "an unknown user"}");
                    return true;
                }
                finally
                {
                    _gate.Release();
                }
            }
            catch (Exception ex)
            {
                Log.Write("DiscordIpcClient::Connect", $"Handshake on discord-ipc-{index} failed: {ex.Message}");

                await pipe.DisposeAsync().ConfigureAwait(false);
                _pipe = null;
            }
        }

        Log.Write("DiscordIpcClient::Connect", "No Discord IPC pipe accepted a connection");
        return false;
    }

    public async Task<DiscordActivityResult> SetActivityAsync(object? activity, CancellationToken ct)
    {
        if (!IsConnected)
        {
            return new DiscordActivityResult(false, "Not connected to Discord.");
        }

        string nonce = Guid.NewGuid().ToString();

        var payload = new
        {
            cmd = "SET_ACTIVITY",
            nonce,
            args = new
            {
                pid = Environment.ProcessId,
                activity
            }
        };

        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            await WriteFrameAsync(DiscordOpcode.Frame, JsonSerializer.Serialize(payload), ct).ConfigureAwait(false);

            return await AwaitAcknowledgementAsync(nonce, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Write("DiscordIpcClient::SetActivity", $"Write failed, dropping the connection: {ex.Message}");
            Disconnect();

            return new DiscordActivityResult(false, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DiscordActivityResult> AwaitAcknowledgementAsync(string nonce, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(AcknowledgementTimeoutMs);

        try
        {
            while (true)
            {
                var frame = await ReadFrameAsync(timeout.Token).ConfigureAwait(false);

                if (frame is null)
                {
                    Disconnect();
                    return new DiscordActivityResult(false, "Discord closed the pipe.");
                }

                using var document = JsonDocument.Parse(frame.Value.Payload);
                var root = document.RootElement;

                if (!root.TryGetProperty("nonce", out var replyNonce)
                    || replyNonce.ValueKind != JsonValueKind.String
                    || replyNonce.GetString() != nonce)
                {
                    continue;
                }

                if (root.TryGetProperty("evt", out var evt)
                    && evt.ValueKind == JsonValueKind.String
                    && evt.GetString() == "ERROR")
                {
                    string message = root.TryGetProperty("data", out var data) && data.TryGetProperty("message", out var m)
                        ? m.GetString() ?? "unknown error"
                        : "unknown error";

                    Log.Write("DiscordIpcClient::SetActivity", $"Discord rejected the activity: {message}");
                    return new DiscordActivityResult(false, message);
                }

                return new DiscordActivityResult(true, null);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            Log.Write("DiscordIpcClient::SetActivity", "Discord did not acknowledge the activity within the timeout");
            return new DiscordActivityResult(false, "Discord did not acknowledge the request.");
        }
        catch (Exception ex)
        {
            Log.Write("DiscordIpcClient::SetActivity", $"Could not read the acknowledgement: {ex.Message}");
            return new DiscordActivityResult(false, ex.Message);
        }
    }

    private static string? ReadUserName(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);

            if (document.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("user", out var user)
                && user.TryGetProperty("username", out var name))
            {
                return name.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string DescribeRejection(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);

            string? message = document.RootElement.TryGetProperty("message", out var value) ? value.GetString() : null;
            int code = document.RootElement.TryGetProperty("code", out var codeValue) ? codeValue.GetInt32() : 0;

            return message is null ? payload : $"{message} (code {code})";
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    private async Task WriteFrameAsync(DiscordOpcode opcode, string json, CancellationToken ct)
    {
        var pipe = _pipe ?? throw new InvalidOperationException("The Discord pipe is not connected.");

        byte[] body = Encoding.UTF8.GetBytes(json);
        byte[] frame = new byte[8 + body.Length];

        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(0, 4), (int)opcode);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(4, 4), body.Length);
        body.CopyTo(frame.AsSpan(8));

        await pipe.WriteAsync(frame, ct).ConfigureAwait(false);
        await pipe.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task<(DiscordOpcode Opcode, string Payload)?> ReadFrameAsync(CancellationToken ct)
    {
        var pipe = _pipe;

        if (pipe is null)
        {
            return null;
        }

        byte[] header = new byte[8];

        if (!await ReadExactlyAsync(pipe, header, ct).ConfigureAwait(false))
        {
            return null;
        }

        var opcode = (DiscordOpcode)BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));

        if (length < 0 || length > MaxPayloadBytes)
        {
            throw new IOException($"Discord sent an implausible frame length of {length}.");
        }

        byte[] body = new byte[length];

        if (!await ReadExactlyAsync(pipe, body, ct).ConfigureAwait(false))
        {
            return null;
        }

        return (opcode, Encoding.UTF8.GetString(body));
    }

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;

        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), ct).ConfigureAwait(false);

            if (read == 0)
            {
                return false;
            }

            offset += read;
        }

        return true;
    }

    public void Disconnect()
    {
        try
        {
            _pipe?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Write("DiscordIpcClient::Disconnect", ex.Message);
        }

        _pipe = null;
        PipeIndex = null;
        UserName = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Disconnect();
        _gate.Dispose();
    }
}
