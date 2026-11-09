using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace LightSpace.App.Services;

/// <summary>
/// Новый релиз приложения на GitHub: его версия и страница, где его скачать.
/// </summary>
public sealed record AvailableUpdate(Version Version, string PageUrl);

/// <summary>
/// Версия этой сборки и проверка, не вышла ли новее на странице релизов GitHub.
/// </summary>
public static class UpdateCheck
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Kepaarik/Light-controller/releases/latest";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>
    /// Версия этой сборки: три числа, без хвоста коммита.
    /// </summary>
    public static Version Current { get; } = ReadCurrent();

    /// <summary>
    /// Последний релиз, если он новее этой сборки; null — обновлений нет или проверить не удалось (нет сети, нет релизов).
    /// </summary>
    public static async Task<AvailableUpdate?> FindAsync(CancellationToken ct = default)
    {
        try
        {
            await using var stream = await Http.GetStreamAsync(LatestReleaseUrl, ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var tag = json.RootElement.GetProperty("tag_name").GetString() ?? "";
            var page = json.RootElement.GetProperty("html_url").GetString() ?? "";

            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
                return null;

            return Normalize(latest) > Current ? new AvailableUpdate(Normalize(latest), page) : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Trace.WriteLine($"Light Space: проверка обновлений не удалась: {e.Message}");
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // GitHub не отвечает на запросы без User-Agent
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("LightSpace", ReadCurrent().ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        return client;
    }

    private static Version ReadCurrent()
    {
        var text = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

        // К версии сборка дописывает «+хеш коммита»
        var plain = text.Split('+')[0].Split('-')[0];

        return Version.TryParse(plain, out var version) ? Normalize(version) : new Version(0, 0, 0);
    }

    /// <summary>
    /// 1.2 и 1.2.0 — одна версия: сравниваются три числа.
    /// </summary>
    private static Version Normalize(Version version)
    {
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }
}
