using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.App.Services;
using LightSpace.Engine;
using LightSpace.Engine.Frames;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Вкладка «Устройства»: поиск, список известных устройств и панель выбранного.
/// </summary>
public sealed partial class DevicesViewModel : ObservableObject
{
    // Как часто обновляются состояние связи и диагностика устройств
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(500);

    private readonly DeviceManager _manager;
    private readonly FrameLoop _frameLoop;
    private readonly Action _settingsChanged;
    private readonly DispatcherTimer _refreshTimer;

    private DeviceItemViewModel? _selectedItem;

    [ObservableProperty]
    private string _searchProblems = "";

    public DevicesViewModel(DeviceManager manager, FrameLoop frameLoop, Action settingsChanged)
    {
        _manager = manager;
        _frameLoop = frameLoop;
        _settingsChanged = settingsChanged;

        _manager.DevicesChanged += (_, _) => Sync();
        Sync();

        _refreshTimer = new DispatcherTimer { Interval = RefreshInterval };
        _refreshTimer.Tick += (_, _) => RefreshItems();
        _refreshTimer.Start();
    }

    public ObservableCollection<DeviceItemViewModel> Items { get; } = new();

    public DeviceItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (SetProperty(ref _selectedItem, value))
                OnPropertyChanged(nameof(HasSelection));
        }
    }

    public bool HasSelection => _selectedItem != null;

    public bool HasDevices => Items.Count > 0;

    public bool IsSearching => _manager.IsSearching;

    /// <summary>
    /// Заголовок пустого состояния: идёт поиск или устройств нет.
    /// </summary>
    public string EmptyTitle => IsSearching ? "Идёт поиск…" : "Устройства не найдены";

    /// <summary>
    /// Время расчёта кадра для всех устройств: последнего и в среднем.
    /// </summary>
    public string FrameTimeText => _frameLoop.Timing is { Frames: > 0 } timing
        ? $"{timing.Last.TotalMilliseconds:0.00} мс, в среднем {timing.Average.TotalMilliseconds:0.00} мс"
        : "Ещё не считался";

    /// <summary>
    /// Ищет устройства всеми способами подключения; найденные сразу начинают показывать текущий эффект.
    /// </summary>
    [RelayCommand]
    private async Task FindAsync()
    {
        if (_manager.IsSearching)
            return;

        SearchProblems = "";

        var errors = await _manager.DiscoverAsync();

        SearchProblems = errors.Count == 0 ? "" : $"Не все способы поиска сработали: {string.Join("; ", errors)}";
        _settingsChanged();

        SelectedItem ??= Items.FirstOrDefault(i => i.IsConnected) ?? Items.FirstOrDefault();
    }

    /// <summary>
    /// Убирает устройство из списка вместе с его ключом.
    /// </summary>
    [RelayCommand]
    private async Task ForgetAsync(DeviceItemViewModel? item)
    {
        if (item == null)
            return;

        var confirmed = await Dialogs.ConfirmAsync($"Забыть «{item.Name}»?",
            "Устройство пропадёт из списка вместе с ключом. Если оно включено, следующий поиск найдёт его снова.", "Забыть",
            isDanger: true);

        if (!confirmed)
            return;

        _manager.Forget(item.Device);
        _settingsChanged();
    }

    /// <summary>
    /// Приводит список к составу устройств менеджера, сохраняя порядок и выбранное устройство.
    /// </summary>
    private void Sync()
    {
        foreach (var item in Items.Where(i => !_manager.Devices.Contains(i.Device)).ToList())
            Items.Remove(item);

        for (var i = 0; i < _manager.Devices.Count; i++)
        {
            var device = _manager.Devices[i];

            if (Items.All(item => item.Device != device))
                Items.Insert(i, new DeviceItemViewModel(device, _manager, _settingsChanged));
        }

        if (_selectedItem != null && !Items.Contains(_selectedItem))
            SelectedItem = Items.FirstOrDefault();

        RefreshItems();

        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(EmptyTitle));
    }

    private void RefreshItems()
    {
        foreach (var item in Items)
            item.Refresh();

        OnPropertyChanged(nameof(FrameTimeText));
    }
}
