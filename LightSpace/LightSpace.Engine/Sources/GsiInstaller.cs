using System.Text.RegularExpressions;
using LightSpace.Core.Config;
using Microsoft.Win32;

namespace LightSpace.Engine.Sources;

/// <summary>
/// Файл Game State Integration в папке игры: после него Dota 2 или Counter-Strike 2 сама шлёт Light Space события
/// матча. Игры ищутся во всех библиотеках Steam.
/// </summary>
public static partial class GsiInstaller
{
    private const string FileName = "gamestate_integration_lightspace.cfg";

    /// <summary>
    /// Файл уже лежит в папке игры.
    /// </summary>
    public static bool IsInstalled(GameKind game)
    {
        return ConfigPath(game) is { } path && File.Exists(path);
    }

    /// <summary>
    /// Игра есть в одной из библиотек Steam.
    /// </summary>
    public static bool IsGameFound(GameKind game)
    {
        return ConfigPath(game) != null;
    }

    /// <summary>
    /// Кладёт файл в папку игры и возвращает путь к нему.
    /// </summary>
    /// <exception cref="InvalidOperationException">Steam или игра не найдены, или у игры нет интеграции.</exception>
    /// <exception cref="IOException">Файл не удалось записать.</exception>
    /// <exception cref="UnauthorizedAccessException">Нет прав на запись в папку игры.</exception>
    public static string Install(GameKind game)
    {
        var path = ConfigPath(game) ?? throw new InvalidOperationException(game switch
        {
            GameKind.Dota2 => "Dota 2 не найдена в библиотеках Steam",
            GameKind.CounterStrike2 => "Counter-Strike 2 не найдена в библиотеках Steam",
            _ => "У этой игры нет своей интеграции"
        });

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, game == GameKind.Dota2 ? DotaConfig() : CsConfig());

        return path;
    }

    private static string? ConfigPath(GameKind game)
    {
        var (folder, cfg) = game switch
        {
            GameKind.Dota2 => ("dota 2 beta", Path.Combine("game", "dota", "cfg", "gamestate_integration")),
            GameKind.CounterStrike2 => ("Counter-Strike Global Offensive", Path.Combine("game", "csgo", "cfg")),
            _ => ((string?)null, (string?)null)
        };

        if (folder == null || cfg == null)
            return null;

        foreach (var library in SteamLibraries())
        {
            var path = Path.Combine(library, "steamapps", "common", folder);

            if (Directory.Exists(path))
                return Path.Combine(path, cfg, FileName);
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

    private static string DotaConfig()
    {
        return $$"""
            "LightSpace"
            {
                "uri"           "{{GsiServer.Address}}"
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

    private static string CsConfig()
    {
        return $$"""
            "LightSpace"
            {
                "uri"           "{{GsiServer.Address}}"
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
