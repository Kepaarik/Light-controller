using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LedHub.Services.Games;

/// <summary>
/// Принимает события Game State Integration от Dota 2 и Counter-Strike 2: игры сами шлют состояние матча
/// на локальный адрес, указанный в их файле настроек.
/// </summary>
public sealed class GsiServer : IDisposable
{
    public const int Port = 4790;
    public const string Token = "ledhub";

    private const int DotaAppId = 570;
    private const int CsAppId = 730;

    // Состояние матча — несколько килобайт; всё, что намного больше, не от игры
    private const int MaxRequestSize = 1024 * 1024;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly byte[] OkResponse = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    // Игры шлют данные из своего потока, а подсветка читает их на своём — подменяем запись целиком
    private volatile DotaState? _dota;
    private volatile CsState? _cs;

    public string Address => $"http://127.0.0.1:{Port}/";

    public DotaState? Dota => _dota;

    public CsState? Cs => _cs;

    /// <summary>
    /// Начинает слушать порт; возвращает текст ошибки, если порт занят.
    /// </summary>
    public string? Start()
    {
        if (_listener != null)
            return null;

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, Port);
            _listener.Start();
        }
        catch (SocketException e)
        {
            _listener = null;
            return $"Порт {Port} занят: {e.Message}";
        }

        _cts = new CancellationTokenSource();
        _ = AcceptLoopAsync(_listener, _cts.Token);

        return null;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _listener?.Stop();
        _listener = null;
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;

            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            _ = HandleAsync(client, ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            var stream = client.GetStream();
            var body = await ReadBodyAsync(stream, timeout.Token);

            if (body != null)
                Accept(body);

            await stream.WriteAsync(OkResponse, timeout.Token);
        }
        catch (Exception)
        {
            // Оборванный или кривой запрос просто пропускаем — игра пришлёт следующий
        }
    }

    /// <summary>
    /// Читает HTTP-запрос и возвращает его тело; null, если запрос неполный или слишком большой.
    /// </summary>
    private static async Task<byte[]?> ReadBodyAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var data = new MemoryStream();
        var headerEnd = -1;

        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(buffer, ct);

            if (read == 0 || data.Length + read > MaxRequestSize)
                return null;

            data.Write(buffer, 0, read);
            headerEnd = data.GetBuffer().AsSpan(0, (int)data.Length).IndexOf("\r\n\r\n"u8);
        }

        var headers = Encoding.ASCII.GetString(data.GetBuffer(), 0, headerEnd);
        var length = ContentLength(headers);
        var bodyStart = headerEnd + 4;

        if (length is not { } bodyLength || bodyLength < 0 || bodyLength > MaxRequestSize)
            return null;

        while (data.Length - bodyStart < bodyLength)
        {
            var read = await stream.ReadAsync(buffer, ct);

            if (read == 0)
                return null;

            data.Write(buffer, 0, read);
        }

        return data.GetBuffer().AsSpan(bodyStart, bodyLength).ToArray();
    }

    private static int? ContentLength(string headers)
    {
        foreach (var line in headers.Split("\r\n"))
        {
            var colon = line.IndexOf(':');

            if (colon > 0 && line[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                          && int.TryParse(line[(colon + 1)..].Trim(), out var length))
                return length;
        }

        return null;
    }

    private void Accept(byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        // Чужие программы тоже могут стучаться на порт — принимаем только с нашим ключом
        if (Text(root, "auth", "token") != Token)
            return;

        switch (Number(root, "provider", "appid"))
        {
            case DotaAppId:
                _dota = ParseDota(root);
                break;

            case CsAppId:
                _cs = ParseCs(root);
                break;
        }
    }

    private static DotaState ParseDota(JsonElement root)
    {
        var hasHero = Find(root, "hero", "health_percent") != null;

        return new DotaState(
            Text(root, "map", "game_state") ?? "",
            Flag(root, "map", "daytime") ?? true,
            Text(root, "map", "win_team") ?? "none",
            Text(root, "player", "team_name") ?? "",
            hasHero,
            Flag(root, "hero", "alive") ?? true,
            Number(root, "hero", "health_percent") ?? 100,
            Number(root, "hero", "mana_percent") ?? 100,
            Number(root, "hero", "respawn_seconds") ?? 0,
            Flag(root, "hero", "stunned") ?? false,
            Flag(root, "hero", "silenced") ?? false,
            Flag(root, "hero", "hexed") ?? false,
            Flag(root, "hero", "smoked") ?? false,
            DateTime.UtcNow);
    }

    private static CsState ParseCs(JsonElement root)
    {
        // Когда игрок погиб и смотрит за другими, игра присылает состояние того, за кем он смотрит
        var ownId = Text(root, "provider", "steamid");
        var playerId = Text(root, "player", "steamid");

        return new CsState(
            ownId != null && ownId == playerId,
            Text(root, "player", "team") ?? "",
            Number(root, "player", "state", "health") ?? 0,
            Number(root, "player", "state", "armor") ?? 0,
            Number(root, "player", "state", "flashed") ?? 0,
            Number(root, "player", "state", "burning") ?? 0,
            Text(root, "map", "phase") ?? "",
            Text(root, "round", "phase") ?? "",
            Text(root, "round", "bomb") ?? "",
            Text(root, "round", "win_team") ?? "",
            DateTime.UtcNow);
    }

    private static JsonElement? Find(JsonElement root, params string[] path)
    {
        var current = root;

        foreach (var name in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
                return null;
        }

        return current;
    }

    private static string? Text(JsonElement root, params string[] path)
    {
        return Find(root, path) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
    }

    private static int? Number(JsonElement root, params string[] path)
    {
        return Find(root, path) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : null;
    }

    private static bool? Flag(JsonElement root, params string[] path)
    {
        return Find(root, path) switch
        {
            { ValueKind: JsonValueKind.True } => true,
            { ValueKind: JsonValueKind.False } => false,
            _ => null
        };
    }
}
