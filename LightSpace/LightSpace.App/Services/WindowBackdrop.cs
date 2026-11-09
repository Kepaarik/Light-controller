using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace LightSpace.App.Services;

/// <summary>
/// Системное стекло Windows 11 под всем окном: прозрачное (Acrylic) или матовое (Mica).
/// В Windows без системного стекла окно просто заливается тёмным цветом.
/// </summary>
public static class WindowBackdrop
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmBorderColor = 34;
    private const int DwmCaptionColor = 35;
    private const int DwmSystemBackdropType = 38;

    // DWMWA_COLOR_NONE: рамка окна не рисуется вовсе
    private const int ColorNone = unchecked((int)0xFFFFFFFE);
    private const int BackdropMica = 2;
    private const int BackdropAcrylic = 3;

    // Первая сборка Windows 11, где DWMWA_SYSTEMBACKDROP_TYPE поддерживается официально (22H2)
    private const int MinimumBuild = 22621;

    /// <summary>
    /// Системное стекло есть в этой версии Windows.
    /// </summary>
    public static bool IsSupported => Environment.OSVersion.Version.Build >= MinimumBuild;

    /// <summary>
    /// Включает тёмный заголовок и системное стекло под всем окном; вызывать после SourceInitialized и при смене вида стекла.
    /// </summary>
    public static void Apply(Window window, bool useAcrylic)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var source = HwndSource.FromHwnd(handle);
        var dark = 1;

        if (handle == IntPtr.Zero)
            return;

        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));

        if (!IsSupported || source is null)
        {
            window.SetResourceReference(Window.BackgroundProperty, "WindowBrush");
            return;
        }

        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        var backdrop = useAcrylic ? BackdropAcrylic : BackdropMica;
        var borderColor = ColorNone;

        // Окно должно быть прозрачным насквозь, иначе стекло закроет заливка WPF
        window.Background = Brushes.Transparent;
        source.CompositionTarget.BackgroundColor = Colors.Transparent;

        DwmExtendFrameIntoClientArea(handle, ref margins);
        DwmSetWindowAttribute(handle, DwmSystemBackdropType, ref backdrop, sizeof(int));

        // У активного окна Windows 11 рисует рамку цветом акцента — сверху она видна полоской над стеклом
        DwmSetWindowAttribute(handle, DwmBorderColor, ref borderColor, sizeof(int));

        // При включённом «Цвет акцента в заголовках» Windows заливает им весь заголовок активного окна поверх стекла
        DwmSetWindowAttribute(handle, DwmCaptionColor, ref borderColor, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }
}
