using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.Core;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Скрытый раздел настроек «Журнал устройств»: включение подробного журнала, его текст, копирование и папка с файлом.
/// Открывается семью щелчками по заголовку «Настройки».
/// </summary>
public sealed partial class DeviceLogViewModel : ObservableObject
{
    // Сколько щелчков по заголовку открывают раздел
    private const int RevealClicks = 7;

    // Строки приходят из любых потоков и иногда очень часто — текст в окне обновляется не чаще этого
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

    private readonly DispatcherTimer _refreshTimer;

    private int _titleClicks;
    private volatile bool _isDirty;

    [ObservableProperty]
    private bool _isShown;

    [ObservableProperty]
    private string _text = "";

    public DeviceLogViewModel()
    {
        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += (_, _) => Refresh();

        DeviceLog.Changed += (_, _) => _isDirty = true;
    }

    /// <summary>
    /// Пишется ли подробный журнал устройств.
    /// </summary>
    public bool IsEnabled
    {
        get => DeviceLog.IsEnabled;
        set
        {
            if (value == DeviceLog.IsEnabled)
                return;

            DeviceLog.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Щелчок по заголовку страницы: после нескольких подряд раздел журнала появляется.
    /// </summary>
    public void OnTitleClicked()
    {
        if (IsShown || ++_titleClicks < RevealClicks)
            return;

        IsShown = true;
        _isDirty = true;
        _refreshTimer.Start();
    }

    [RelayCommand]
    private void Copy()
    {
        try
        {
            Clipboard.SetText(DeviceLog.Text);
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.ExternalException)
        {
            // Буфер обмена занят другим приложением — можно нажать ещё раз
        }
    }

    [RelayCommand]
    private void Clear()
    {
        DeviceLog.Clear();
    }

    /// <summary>
    /// Открывает папку с файлом журнала приложения log.txt.
    /// </summary>
    [RelayCommand]
    private void OpenFolder()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LightSpace");

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    private void Refresh()
    {
        if (!_isDirty)
            return;

        _isDirty = false;
        Text = DeviceLog.Text;
    }
}
