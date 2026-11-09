using System.Net;
using System.Net.Sockets;

namespace LightSpace.Devices.OpenRgb;

/// <summary>
/// Соединение с SDK-сервером OpenRGB: запросы с ответом и команды без ответа.
/// </summary>
public sealed class OpenRgbClient : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(3);

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    private OpenRgbClient(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    /// <summary>
    /// Подключается к серверу и договаривается о версии протокола.
    /// </summary>
    public static async Task<OpenRgbClient> ConnectAsync(IPEndPoint endPoint, CancellationToken ct)
    {
        var tcp = new TcpClient { NoDelay = true };

        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(ConnectTimeout);

            try
            {
                await tcp.ConnectAsync(endPoint, cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                tcp.Dispose();
                throw new TimeoutException($"Сервер OpenRGB не отвечает на {endPoint}");
            }
            catch
            {
                tcp.Dispose();
                throw;
            }
        }

        var client = new OpenRgbClient(tcp);

        try
        {
            // Сервер отвечает версией не новее нашей — описания контроллеров приходят в понятном формате
            await client.RequestAsync(0, OpenRgbProtocol.RequestProtocolVersion, OpenRgbProtocol.UInt32(OpenRgbProtocol.ClientVersion), ct);
            await client.SendAsync(0, OpenRgbProtocol.SetClientName, OpenRgbProtocol.ClientName("Light Space"), ct);

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task<IReadOnlyList<OpenRgbController>> ListControllersAsync(CancellationToken ct)
    {
        var count = BitConverter.ToUInt32(await RequestAsync(0, OpenRgbProtocol.RequestControllerCount, ReadOnlyMemory<byte>.Empty, ct));
        var controllers = new List<OpenRgbController>();

        for (var i = 0; i < count; i++)
        {
            var data = await RequestAsync((uint)i, OpenRgbProtocol.RequestControllerData, OpenRgbProtocol.UInt32(OpenRgbProtocol.ClientVersion), ct);

            controllers.Add(OpenRgbProtocol.ParseController(i, data));
        }

        return controllers;
    }

    /// <summary>
    /// Отправляет команду, на которую сервер не отвечает.
    /// </summary>
    public async Task SendAsync(uint deviceIndex, uint packetId, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        await _stream.WriteAsync(OpenRgbProtocol.BuildPacket(deviceIndex, packetId, data.Span), ct);
    }

    public void Dispose()
    {
        _stream.Dispose();
        _client.Dispose();
    }

    /// <summary>
    /// Отправляет запрос и ждёт ответа того же типа; данные ответа без заголовка.
    /// </summary>
    private async Task<byte[]> RequestAsync(uint deviceIndex, uint packetId, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        await SendAsync(deviceIndex, packetId, data, ct);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ReplyTimeout);

        while (true)
        {
            var header = new byte[OpenRgbProtocol.HeaderSize];
            await _stream.ReadExactlyAsync(header, cts.Token);

            var (_, replyId, length) = OpenRgbProtocol.ParseHeader(header);
            var body = new byte[length];
            await _stream.ReadExactlyAsync(body, cts.Token);

            // Между ответами сервер может прислать уведомление об изменении списка устройств — его пропускаем
            if (replyId == packetId)
                return body;
        }
    }
}
