using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LedHub.Services.Games;

/// <summary>
/// Кладёт в папку игры файл Game State Integration, после которого игра сама шлёт LedHub события матча.
/// Игры ищутся во всех библиотеках Steam.
/// </summary>
public static partial class GsiInstaller
{
    private const string FileName = "gamestate_integration_ledhub.cfg";

    public static bool IsInstalled(GameKind kind)
    {
        return ConfigPath(kind) is { } path && File.Exists(path);
    }

    /// <summary>
    /// Записывает файл настроек и возвращает путь к нему.
    /// </summary>
    /// <exception cref="InvalidOperationException">Steam или игра не найдены.</exception>
    public static string Install(GameKind kind, string address)
    {
        var path = ConfigPath(kind) ?? throw new InvalidOperationException(
            kind == GameKind.Dota2 ? "Dota 2 не найдена в библиотеках Steam" : "Counter-Strike 2 не найдена в библиотеках Steam");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, kind == GameKind.Dota2 ? DotaConfig(address) : CsConfig(address));

        return path;
    }

    private static string? ConfigPath(GameKind kind)
    {
        var (folder, cfg) = kind switch
        {
            GameKind.Dota2 => ("dota 2 beta", Path.Combine("game", "dota", "cfg", "gamestate_integration")),
            GameKind.CounterStrike2 => ("Counter-Strike Global Offensive", Path.Combine("game", "csgo", "cfg")),
            _ => (null, null)
        };

        if (folder == null || cfg == null)
            return null;

        foreach (var library in SteamLibraries())
        {
            var game = Path.Combine(library, "steamapps", "common", folder);

            if (Directory.Exists(game))
                return Path.Combine(game, cfg, FileName);
        }

        return null;
    }

    /// <summary>
    /// Папка Steam и все добавленные в него библиотеки.
    /// </summary>
    private static IEnumerable<string> SteamLibraries()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");

        if (key?.GetValue("SteamPath") is not string steam || !Directory.Exists(steam))
            yield break;

        yield return steam;

        var folders = Path.Combine(steam, "steamapps", "libraryfolders.vdf");

        if (!File.Exists(folders))
            yield break;

        foreach (Match match in LibraryPath().Matches(File.ReadAllText(folders)))
        {
            // В файле Steam обратные слэши удвоены
            yield return match.Groups[1].Value.Replace(@"\\", @"\");
        }
    }

    private static string DotaConfig(string address)
    {
        return $$"""
            "LedHub"
            {
                "uri"           "{{address}}"
                "timeout"       "5.0"
                "buffer"        "0.1"
                "throttle"      "0.1"
                "heartbeat"     "30.0"
                "data"
                {
                    "provider"      "1"
                    "map"           "1"
                    "player"        "1"
                    "hero"          "1"
                }
                "auth"
                {
                    "token"         "{{GsiServer.Token}}"
                }
            }
            """;
    }

    private static string CsConfig(string address)
    {
        return $$"""
            "LedHub"
            {
                "uri"           "{{address}}"
                "timeout"       "5.0"
                "buffer"        "0.1"
                "throttle"      "0.1"
                "heartbeat"     "30.0"
                "data"
                {
                    "provider"      "1"
                    "map"           "1"
                    "round"         "1"
                    "player_id"     "1"
                    "player_state"  "1"
                }
                "auth"
                {
                    "token"         "{{GsiServer.Token}}"
                }
            }
            """;
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex LibraryPath();
}
