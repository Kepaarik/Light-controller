using System.Diagnostics;
using System.IO;

namespace LightSpace.App.Services;

/// <summary>
/// Журнал предупреждений приложения в %AppData%\LightSpace\log.txt: всё, что пишется через Trace, попадает в файл.
/// Журнал больше 1 МБ при запуске уходит в log.old.txt, а запись начинается заново.
/// </summary>
public static class FileLog
{
    private const long MaxSize = 1024 * 1024;

    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LightSpace");

    private static readonly string FilePath = Path.Combine(Folder, "log.txt");
    private static readonly string OldFilePath = Path.Combine(Folder, "log.old.txt");

    /// <summary>
    /// Начинает писать журнал. Если файл открыть не удалось, приложение работает без журнала.
    /// </summary>
    public static void Start()
    {
        try
        {
            Directory.CreateDirectory(Folder);

            var file = new FileInfo(FilePath);

            if (file.Exists && file.Length > MaxSize)
                File.Move(FilePath, OldFilePath, overwrite: true);

            // Второй запущенный экземпляр тоже может писать в тот же файл
            var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            var writer = new StreamWriter(stream) { AutoFlush = true };

            Trace.Listeners.Add(new TextWriterTraceListener(writer) { TraceOutputOptions = TraceOptions.DateTime });
            Trace.AutoFlush = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Без журнала приложение работает так же
        }
    }
}
