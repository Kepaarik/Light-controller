using System.Diagnostics;

namespace LightSpace.Engine.Sources;

/// <summary>
/// Захват одного монитора в своём потоке: около 30 раз в секунду снимает монитор уменьшенной картинкой и публикует
/// последний снимок. Рендер только читает снимок; пока снять не удаётся, снимок — пустая чёрная картинка.
/// </summary>
public sealed class ScreenCapture : IDisposable
{
    // Длинная сторона уменьшенной картинки; короткая — по пропорциям монитора
    private const int LongSide = 64;

    private static readonly TimeSpan CaptureInterval = TimeSpan.FromSeconds(1.0 / 30);

    // Монитор переспрашивается раз в пару секунд: его могли отключить или сменить у него разрешение
    private static readonly TimeSpan MonitorCheckInterval = TimeSpan.FromSeconds(2);

    // После ошибки захват пробует снова не сразу, чтобы не крутиться впустую
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    private readonly ManualResetEventSlim _stop = new();
    private readonly string _monitorKey;

    // Снимок заменяется целиком атомарной записью ссылки
    private ScreenPicture _picture = ScreenPicture.Empty;

    /// <summary>
    /// monitorKey — системное имя монитора; пусто — основной монитор Windows.
    /// </summary>
    public ScreenCapture(string monitorKey)
    {
        _monitorKey = monitorKey;

        var thread = new Thread(Run) { IsBackground = true, Name = "Light Space: захват экрана" };
        thread.Start();
    }

    /// <summary>
    /// Последняя картинка монитора; пустая — монитор ещё не сняли или снять не удалось.
    /// </summary>
    public ScreenPicture Picture => Volatile.Read(ref _picture);

    /// <summary>
    /// Останавливает захват; поток сам освобождает всё, что занял, и не задерживает вызывающего.
    /// </summary>
    public void Dispose()
    {
        _stop.Set();
    }

    private void Run()
    {
        ScreenGrabber? grabber = null;
        MonitorInfo? monitor = null;
        var lastCheck = Stopwatch.StartNew();
        var isFailing = false;

        try
        {
            while (!_stop.IsSet)
            {
                var wait = CaptureInterval;

                try
                {
                    if (grabber == null || lastCheck.Elapsed >= MonitorCheckInterval)
                    {
                        lastCheck.Restart();

                        var resolved = Monitors.Resolve(_monitorKey);

                        if (resolved != monitor)
                        {
                            grabber?.Dispose();
                            grabber = null;
                            monitor = resolved;

                            if (monitor != null)
                                grabber = CreateGrabber(monitor);
                        }
                    }

                    if (grabber == null || monitor == null)
                    {
                        Volatile.Write(ref _picture, ScreenPicture.Empty);
                        wait = RetryDelay;
                    }
                    else
                    {
                        var pixels = grabber.Capture(monitor);

                        Volatile.Write(ref _picture, ScreenPicture.FromBgra(pixels, grabber.Width, grabber.Height));
                        isFailing = false;
                    }
                }
                catch (Exception e)
                {
                    // Ошибку пишем один раз за полосу неудач: на экране блокировки снимок не получается каждую секунду
                    if (!isFailing)
                        Trace.TraceWarning($"Light Space: не удалось снять монитор «{monitor?.Key ?? _monitorKey}»: {e.Message}");

                    isFailing = true;
                    Volatile.Write(ref _picture, ScreenPicture.Empty);

                    // Кадр пересоздаётся при следующей попытке
                    grabber?.Dispose();
                    grabber = null;
                    monitor = null;
                    wait = RetryDelay;
                }

                _stop.Wait(wait);
            }
        }
        finally
        {
            grabber?.Dispose();
            _stop.Dispose();
        }
    }

    /// <summary>
    /// Уменьшенный кадр в пропорциях монитора.
    /// </summary>
    private static ScreenGrabber CreateGrabber(MonitorInfo monitor)
    {
        var aspect = (double)monitor.Width / Math.Max(1, monitor.Height);
        var columns = aspect >= 1 ? LongSide : Math.Max(8, (int)Math.Round(LongSide * aspect));
        var rows = aspect >= 1 ? Math.Max(8, (int)Math.Round(LongSide / aspect)) : LongSide;

        return new ScreenGrabber(columns, rows);
    }
}
