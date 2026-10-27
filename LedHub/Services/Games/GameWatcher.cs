using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace LedHub.Services.Games;

/// <summary>
/// Узнаёт, какая игра запущена: Dota 2 и Counter-Strike 2 — по процессу, любая другая — по окну,
/// которое занимает весь главный монитор.
/// </summary>
public static class GameWatcher
{
    // Системные окна на весь экран, которые не игры: рабочий стол, Пуск, поиск, экран блокировки
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp",
        "LockApp", "TextInputHost", "ApplicationFrameHost"
    };

    public static DetectedGame? Detect(MonitorInfo? monitor)
    {
        if (FindProcess("dota2") is { } dota)
            return new DetectedGame(GameKind.Dota2, "Dota 2", dota);

        if (FindProcess("cs2") is { } cs)
            return new DetectedGame(GameKind.CounterStrike2, "Counter-Strike 2", cs);

        return monitor == null ? null : FindFullscreen(monitor);
    }

    public static bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static int? FindProcess(string name)
    {
        var processes = Process.GetProcessesByName(name);

        try
        {
            return processes.Length > 0 ? processes[0].Id : null;
        }
        finally
        {
            foreach (var process in processes)
                process.Dispose();
        }
    }

    /// <summary>
    /// Окно на переднем плане, закрывающее весь главный монитор, — скорее всего, игра.
    /// </summary>
    private static DetectedGame? FindFullscreen(MonitorInfo monitor)
    {
        var window = GetForegroundWindow();

        if (window == IntPtr.Zero || window == GetShellWindow() || !GetWindowRect(window, out var rect))
            return null;

        var coversMonitor = rect.Left <= monitor.Left && rect.Top <= monitor.Top
                            && rect.Right >= monitor.Left + monitor.Width && rect.Bottom >= monitor.Top + monitor.Height;

        if (!coversMonitor)
            return null;

        GetWindowThreadProcessId(window, out var processId);

        if (processId == 0 || processId == Environment.ProcessId)
            return null;

        try
        {
            using var process = Process.GetProcessById((int)processId);

            if (NotGames.Contains(process.ProcessName))
                return null;

            var title = WindowTitle(window);
            return new DetectedGame(GameKind.Other, string.IsNullOrWhiteSpace(title) ? process.ProcessName : title, (int)processId);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string WindowTitle(IntPtr window)
    {
        var text = new StringBuilder(256);
        GetWindowText(window, text, text.Capacity);

        return text.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
}
