using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using LightSpace.App.ViewModels;

namespace LightSpace.App.Services;

/// <summary>
/// Приложения с окнами, запущенные сейчас: из них выбирают условие сценария.
/// </summary>
public static class RunningApps
{
    private const uint QueryLimitedInformation = 0x1000;

    /// <summary>
    /// Запущенные приложения с окнами, кроме самого Light Space, по одному на exe-файл, по алфавиту.
    /// </summary>
    public static IReadOnlyList<AppOptionViewModel> List()
    {
        var apps = new Dictionary<string, AppOptionViewModel>(StringComparer.OrdinalIgnoreCase);
        var self = Environment.ProcessId;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == self || process.MainWindowHandle == IntPtr.Zero)
                        continue;

                    // Путь к exe не всегда доступен (например, у приложений администратора) — тогда хватает имени процесса
                    var path = PathOf(process.Id) ?? process.ProcessName + ".exe";
                    var app = AppOptionViewModel.From(path);

                    apps.TryAdd(app.ExeName, app);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Процесс завершился, пока его читали
                }
            }
        }

        return apps.Values.OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>
    /// Полный путь к exe-файлу процесса; null — Windows его не выдала.
    /// </summary>
    private static string? PathOf(int processId)
    {
        var handle = OpenProcess(QueryLimitedInformation, false, (uint)processId);

        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;

            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
