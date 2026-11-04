using System.Diagnostics;
using System.Text;

namespace LightSpace.Core;

/// <summary>
/// Подробный журнал работы с устройствами для разбора неполадок: что нашлось при поиске, какие команды ушли
/// на устройство и чем они закончились. Пишет, только пока включён; последние строки держит в памяти для окна,
/// и все строки попадают в общий журнал приложения.
/// </summary>
public static class DeviceLog
{
    private const int MaxLines = 3000;

    private static readonly object Sync = new();
    private static readonly LinkedList<string> Lines = new();

    private static volatile bool _isEnabled;

    /// <summary>
    /// В журнал добавилась строка или он очищен. Вызывается из любого потока.
    /// </summary>
    public static event EventHandler? Changed;

    public static bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            Write(value ? "Журнал устройств включён" : "Журнал устройств выключен", force: true);
        }
    }

    /// <summary>
    /// Добавляет строку, если журнал включён.
    /// </summary>
    public static void Write(string message) => Write(message, force: false);

    /// <summary>
    /// Байты команды в шестнадцатеричном виде: «7E 00 05 …».
    /// </summary>
    public static string Hex(IReadOnlyList<byte> data)
    {
        var builder = new StringBuilder(data.Count * 3);

        foreach (var b in data)
        {
            if (builder.Length > 0)
                builder.Append(' ');

            builder.Append(b.ToString("X2"));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Весь журнал из памяти одним текстом.
    /// </summary>
    public static string Text
    {
        get
        {
            lock (Sync)
                return string.Join(Environment.NewLine, Lines);
        }
    }

    public static void Clear()
    {
        lock (Sync)
            Lines.Clear();

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void Write(string message, bool force)
    {
        if (!_isEnabled && !force)
            return;

        var line = $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId,3}] {message}";

        lock (Sync)
        {
            Lines.AddLast(line);

            if (Lines.Count > MaxLines)
                Lines.RemoveFirst();
        }

        Trace.WriteLine($"Light Space: устройства: {message}");
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
