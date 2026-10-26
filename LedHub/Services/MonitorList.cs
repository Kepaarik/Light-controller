using System.Runtime.InteropServices;

namespace LedHub.Services;

/// <summary>
/// Монитор компьютера: положение и размер на общем рабочем столе Windows.
/// Key — системное имя (например, \\.\DISPLAY1), по нему сохраняются настройки.
/// </summary>
public sealed record MonitorInfo(string Key, int Number, int Left, int Top, int Width, int Height, bool IsPrimary);

/// <summary>
/// Перечисляет подключённые мониторы.
/// </summary>
public static class MonitorList
{
    private const int PrimaryMonitorFlag = 1;

    public static IReadOnlyList<MonitorInfo> GetAll()
    {
        var monitors = new List<MonitorInfo>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr hdc, ref NativeRect rect, IntPtr data) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };

            if (GetMonitorInfo(handle, ref info))
            {
                var bounds = info.Monitor;
                monitors.Add(new MonitorInfo(info.DeviceName, monitors.Count + 1, bounds.Left, bounds.Top,
                    bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, (info.Flags & PrimaryMonitorFlag) != 0));
            }

            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    private delegate bool MonitorEnumProc(IntPtr handle, IntPtr hdc, ref NativeRect rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public int Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
}
