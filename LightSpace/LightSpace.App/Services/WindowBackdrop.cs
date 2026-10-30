using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LightSpace.App.Services;

/// <summary>
/// Фон окна Mica от Windows 11 под всем окном со своим заголовком.
/// В Windows без Mica окно просто заливается цветом фона темы.
/// </summary>
public static class WindowBackdrop
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmSystemBackdropType = 38;
    private const int BackdropMica = 2;
    private const int CornerRound = 2;

    // Первая сборка Windows 11, где DWMWA_SYSTEMBACKDROP_TYPE поддерживается официально (22H2)
    private const int MicaMinimumBuild = 22621;

    private const int ThemeAttributeNonClient = 1;
    private const uint NoDrawCaption = 0x1;
    private const uint NoDrawIcon = 0x2;
    private const uint NoSystemMenu = 0x4;
    private const uint ValidBits = 0xF;

    /// <summary>
    /// Mica поддерживается этой версией Windows.
    /// </summary>
    public static bool IsMicaSupported => Environment.OSVersion.Version.Build >= MicaMinimumBuild;

    /// <summary>
    /// Включает Mica и тёмный или светлый вариант рамки; вызывать после SourceInitialized и при смене темы.
    /// </summary>
    public static void Apply(Window window, bool isDark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(handle);

        if (handle == IntPtr.Zero || source is null)
            return;

        var dark = isDark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));

        if (!IsMicaSupported)
        {
            window.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
            return;
        }

        var corner = CornerRound;
        var backdrop = BackdropMica;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };

        // Окно должно быть прозрачным насквозь, иначе Mica закроет заливка WPF
        window.Background = Brushes.Transparent;
        source.CompositionTarget.BackgroundColor = Colors.Transparent;

        DwmExtendFrameIntoClientArea(handle, ref margins);
        DwmSetWindowAttribute(handle, DwmWindowCornerPreference, ref corner, sizeof(int));
        DwmSetWindowAttribute(handle, DwmSystemBackdropType, ref backdrop, sizeof(int));

        HideSystemCaption(handle);
    }

    /// <summary>
    /// Убирает системные подпись, значок и кнопки заголовка: на прозрачном окне они просвечивали бы сквозь свой заголовок.
    /// </summary>
    private static void HideSystemCaption(IntPtr handle)
    {
        var options = new ThemeOptions { Flags = NoDrawCaption | NoDrawIcon | NoSystemMenu, Mask = ValidBits };

        SetWindowThemeAttribute(handle, ThemeAttributeNonClient, ref options, (uint)Marshal.SizeOf<ThemeOptions>());
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("uxtheme.dll")]
    private static extern int SetWindowThemeAttribute(IntPtr hwnd, int attribute, ref ThemeOptions options, uint size);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ThemeOptions
    {
        public uint Flags;
        public uint Mask;
    }
}
