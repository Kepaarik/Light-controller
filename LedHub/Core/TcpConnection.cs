using System.Net;
using System.Net.Sockets;

namespace LedHub.Core;

/// <summary>
/// Постоянное TCP-соединение с устройством, которое само переподключается после обрыва.
/// </summary>
public sealed class TcpConnection : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    private readonly IPEndPoint _endPoint;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private TcpClient? _client;

    public TcpConnection(IPEndPoint endPoint)
    {
        _endPoint = endPoint;
    }

    public async Task SendAsync(byte[] data, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            try
            {
                await WriteAsync(data, ct);
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
            {
                // Устройство могло закрыть простаивающее соединение — переподключаемся один раз
                CloseClient();
                await WriteAsync(data, ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        CloseClient();
        _lock.Dispose();
    }

    private async Task WriteAsync(byte[] data, CancellationToken ct)
    {
        DrainIncoming();

        if (_client == null)
            _client = await ConnectAsync(ct);

        await _client.GetStream().WriteAsync(data, ct);
    }

    private async Task<TcpClient> ConnectAsync(CancellationToken ct)
    {
        var client = new TcpClient { NoDelay = true };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ConnectTimeout);

        try
        {
            await client.ConnectAsync(_endPoint, cts.Token);
            return client;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException($"Нет ответа от {_endPoint}");
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Выбрасывает ответы устройства (они не нужны) и закрывает соединение, если устройство его разорвало.
    /// </summary>
    private void DrainIncoming()
    {
        if (_client == null)
            return;

        var socket = _client.Client;
        var buffer = new byte[256];

        while (socket.Available > 0)
            socket.Receive(buffer);

        // Poll == true при пустом буфере означает, что удалённая сторона закрыла соединение
        if (socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0)
            CloseClient();
    }

    private void CloseClient()
    {
        _client?.Dispose();
        _client = null;
    }
}
