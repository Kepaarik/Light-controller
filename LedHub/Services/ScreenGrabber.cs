using System.Runtime.InteropServices;

namespace LedHub.Services;

/// <summary>
/// Снимает монитор сразу уменьшенным до маленького кадра: так захват дешёвый даже 20 раз в секунду.
/// </summary>
public sealed class ScreenGrabber : IDisposable
{
    private const int Halftone = 4;
    private const int SrcCopy = 0x00CC0020;

    // Размер BITMAPINFOHEADER без палитры, который ждёт CreateDIBSection
    private const int BitmapInfoHeaderSize = 40;

    private readonly IntPtr _memoryDc;
    private readonly IntPtr _bitmap;
    private readonly IntPtr _previousBitmap;
    private readonly IntPtr _bits;
    private readonly byte[] _pixels;

    public ScreenGrabber(int width, int height)
    {
        Width = width;
        Height = height;
        _pixels = new byte[width * height * 4];

        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            _memoryDc = CreateCompatibleDC(screenDc);

            // Отрицательная высота — строки кадра идут сверху вниз
            var info = new BitmapInfo
            {
                Size = BitmapInfoHeaderSize,
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32
            };

            _bitmap = CreateDIBSection(screenDc, ref info, 0, out _bits, IntPtr.Zero, 0);
            _previousBitmap = SelectObject(_memoryDc, _bitmap);

            // Режим HALFTONE усредняет пиксели при уменьшении, а не выбрасывает их
            SetStretchBltMode(_memoryDc, Halftone);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>
    /// Снимает монитор; пиксели в формате BGRA, строки сверху вниз.
    /// Контекст рабочего стола охватывает все мониторы, поэтому достаточно координат нужного.
    /// </summary>
    public byte[] Capture(MonitorInfo monitor)
    {
        var screenDc = GetDC(IntPtr.Zero);
        try
        {
            StretchBlt(_memoryDc, 0, 0, Width, Height, screenDc, monitor.Left, monitor.Top,
                monitor.Width, monitor.Height, SrcCopy);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }

        Marshal.Copy(_bits, _pixels, 0, _pixels.Length);
        return _pixels;
    }

    public void Dispose()
    {
        SelectObject(_memoryDc, _previousBitmap);
        DeleteObject(_bitmap);
        DeleteDC(_memoryDc);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
        public int Colors;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr destination, int x, int y, int width, int height,
        IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight, int operation);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
}
