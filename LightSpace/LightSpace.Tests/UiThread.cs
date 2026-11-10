using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;

namespace LightSpace.Tests;

/// <summary>
/// Поток окна для тестов: один STA-поток с загруженными ресурсами приложения — токенами, стилями и палитрой.
/// Цикл сообщений в нём не крутится, поэтому таймеры не срабатывают и отложенное сохранение не пишет настоящий файл настроек.
/// </summary>
public static class UiThread
{
    private static readonly BlockingCollection<Action> Work = new();
    private static readonly BindingErrorListener BindingErrors = new();

    // Ресурсы приложения не загрузились — каждый тест в потоке окна падает с этой ошибкой
    private static ExceptionDispatchInfo? _startupFailure;

    static UiThread()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Тесты: поток окна" };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>
    /// Выполняет действие в потоке окна и ждёт его; исключение из действия поднимается в тесте.
    /// </summary>
    public static void Invoke(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        using var done = new ManualResetEventSlim();

        Work.Add(() =>
        {
            try
            {
                _startupFailure?.Throw();
                action();
            }
            catch (Exception e)
            {
                failure = ExceptionDispatchInfo.Capture(e);
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait();
        failure?.Throw();
    }

    /// <summary>
    /// Выполняет действие в потоке окна и возвращает ошибки путей привязок, которые случились за это время,
    /// например привязку к свойству, которого у модели нет.
    /// </summary>
    public static IReadOnlyList<string> InvokeCollectingBindingErrors(Action action)
    {
        var errors = new List<string>();

        Invoke(() =>
        {
            BindingErrors.Clear();
            action();
            errors.AddRange(BindingErrors.Messages);
        });

        return errors;
    }

    /// <summary>
    /// Все элементы визуального дерева под элементом.
    /// </summary>
    public static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
                yield return match;

            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    /// <summary>
    /// Окно за краем экрана: показывается, чтобы применились шаблоны, но не мешает и не забирает фокус.
    /// </summary>
    public static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;

        window.Show();
        window.UpdateLayout();
    }

    private static void Run()
    {
        try
        {
            // Без отладчика WPF не пишет ошибки привязок, пока трассировку не включить явно;
            // слушатель добавляется после этого: Refresh перечитывает источники заново
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(BindingErrors);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;

            var app = new global::LightSpace.App.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
        }
        catch (Exception e)
        {
            _startupFailure = ExceptionDispatchInfo.Capture(e);
        }

        foreach (var action in Work.GetConsumingEnumerable())
            action();
    }

    /// <summary>
    /// Собирает ошибки путей привязок. Ошибки «не найден источник» не считаются: они бывают, пока элемент
    /// ещё не попал в дерево окна, и проходят сами.
    /// </summary>
    private sealed class BindingErrorListener : TraceListener
    {
        private readonly List<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages;

        public void Clear()
        {
            _messages.Clear();
        }

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message != null && message.Contains("BindingExpression path error", StringComparison.Ordinal))
                _messages.Add(message);
        }
    }
}
