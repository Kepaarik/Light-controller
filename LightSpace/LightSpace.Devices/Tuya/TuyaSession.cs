using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using LightSpace.Core;

namespace LightSpace.Devices.Tuya;

/// <summary>
/// Соединение с устройством Tuya 3.4 или 3.5. После каждого подключения стороны обмениваются случайными числами
/// и получают сессионный ключ, которым шифруются команды; оборванное соединение открывается и согласуется заново.
/// </summary>
public sealed class TuyaSession : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(3);

    private readonly IPEndPoint _endPoint;
    private readonly string _version;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private TcpClient? _client;
    private byte[]? _sessionKey;
    private uint _sequence;

    public TuyaSession(IPEndPoint endPoint, string version)
    {
        _endPoint = endPoint;
        _version = version;
    }

    /// <summary>
    /// Отправляет точки данных командой управления; при обрыве соединения подключается заново один раз.
    /// </summary>
    public async Task SendDpsAsync(byte[] localKey, byte[] dpsJson, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            try
            {
                await SendControlAsync(localKey, dpsJson, ct);
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
            {
                Close();
                await SendControlAsync(localKey, dpsJson, ct);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        Close();
        _lock.Dispose();
    }

    private async Task SendControlAsync(byte[] localKey, byte[] dpsJson, CancellationToken ct)
    {
        if (_client == null || _sessionKey == null)
            await OpenAsync(localKey, ct);

        DrainIncoming();

        // В 3.4 и 3.5 перед данными команды управления идёт заголовок версии, и он шифруется вместе с ними
        var plaintext = TuyaVersionHeader().Concat(dpsJson).ToArray();

        await WriteAsync(TuyaProtocol.CommandControlNew, plaintext, _sessionKey!, ct);
    }

    /// <summary>
    /// Подключается и согласует сессионный ключ.
    /// </summary>
    private async Task OpenAsync(byte[] localKey, CancellationToken ct)
    {
        Close();

        var client = new TcpClient { NoDelay = true };

        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            cts.CancelAfter(ConnectTimeout);

            try
            {
                await client.ConnectAsync(_endPoint, cts.Token);
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

        _client = client;

        var localNonce = RandomNumberGenerator.GetBytes(16);

        await WriteAsync(TuyaProtocol.CommandSessionStart, localNonce, localKey, ct);

        var (command, reply) = await ReadAsync(localKey, ct);

        // Ответ: случайное число устройства и HMAC нашего числа локальным ключом — так проверяется, что ключ верный
        if (command != TuyaProtocol.CommandSessionResponse || reply.Length < 48)
            throw new IOException($"Устройство не начало обмен ключом (команда {command}, {reply.Length} байт)");

        var remoteNonce = reply[..16];

        if (!CryptographicOperations.FixedTimeEquals(reply.AsSpan(16, 32), HMACSHA256.HashData(localKey, localNonce)))
            throw new InvalidOperationException("Устройство отклонило Local Key — проверьте ключ");

        await WriteAsync(TuyaProtocol.CommandSessionFinish, HMACSHA256.HashData(localKey, remoteNonce), localKey, ct);

        _sessionKey = TuyaProtocol.SessionKey(_version, localKey, localNonce, remoteNonce);

        DeviceLog.Write($"Tuya {_endPoint}: сессионный ключ {_version} согласован");
    }

    private async Task WriteAsync(uint command, byte[] plaintext, byte[] key, CancellationToken ct)
    {
        var sequence = ++_sequence;

        var frame = _version == "3.4"
            ? TuyaProtocol.BuildFrame34(sequence, command, TuyaProtocol.Encrypt(key, plaintext), key)
            : TuyaProtocol.BuildFrame35(sequence, command, plaintext, key, RandomNumberGenerator.GetBytes(12));

        await _client!.GetStream().WriteAsync(frame, ct);
    }

    /// <summary>
    /// Читает один кадр от устройства и расшифровывает его данные.
    /// </summary>
    private async Task<(uint Command, byte[] Payload)> ReadAsync(byte[] key, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ReplyTimeout);

        var stream = _client!.GetStream();

        if (_version == "3.4")
        {
            var header = new byte[16];
            await stream.ReadExactlyAsync(header, cts.Token);

            var rest = new byte[BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12))];
            await stream.ReadExactlyAsync(rest, cts.Token);

            var (command, encrypted) = TuyaProtocol.ParseFrame34(header.Concat(rest).ToArray());

            return (command, TuyaProtocol.Decrypt(key, encrypted));
        }
        else
        {
            var header = new byte[TuyaProtocol.Header35Size];
            await stream.ReadExactlyAsync(header, cts.Token);

            var rest = new byte[BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(14)) + 4];
            await stream.ReadExactlyAsync(rest, cts.Token);

            var (command, payload) = TuyaProtocol.ParseFrame35(header.Concat(rest).ToArray(), key);

            // Ответ устройства начинается с 4 байт кода возврата
            return (command, payload.Length % 16 == 4 ? payload[4..] : payload);
        }
    }

    private byte[] TuyaVersionHeader()
    {
        return System.Text.Encoding.ASCII.GetBytes(_version).Concat(new byte[12]).ToArray();
    }

    /// <summary>
    /// Выбрасывает ответы устройства на команды: они не нужны, но иначе копились бы в сокете.
    /// </summary>
    private void DrainIncoming()
    {
        var socket = _client!.Client;
        var buffer = new byte[512];

        while (socket.Available > 0)
            socket.Receive(buffer);

        // Устройство закрыло соединение — сессия начнётся заново
        if (socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0)
            throw new IOException("Устройство закрыло соединение");
    }

    private void Close()
    {
        _client?.Dispose();
        _client = null;
        _sessionKey = null;
    }
}
