using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.Core.Config;
using LightSpace.Engine;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Сохранённый профиль подсветки в разделе «Профили» и в меню трея: название и что он включает на каждом устройстве.
/// </summary>
public sealed partial class ProfileOption : ObservableObject
{
    private readonly Func<string, ManagedDevice?> _findDevice;
    private readonly Action _changed;

    /// <summary>
    /// findDevice находит известное устройство по идентификатору; changed вызывается после каждой правки профиля.
    /// findMonitor находит монитор, за которым висит лента, — для вида «Монитор» в сцене.
    /// </summary>
    public ProfileOption(ProfileConfig settings, Func<string, ManagedDevice?> findDevice, Action changed,
        Func<string, MonitorConfig?>? findMonitor = null)
    {
        Settings = settings;
        _findDevice = findDevice;
        _changed = changed;

        Content = new DeviceContentViewModel(findDevice, OnContentChanged);
        Scene = new ProfileSceneViewModel(settings, findDevice, OnSceneChanged, findMonitor);

        // «Все настройки устройства» на холсте сцены открывают это устройство в списке
        Scene.DeviceOpenRequested += deviceId => SelectedItem = Devices.FirstOrDefault(d => d.Lighting.DeviceId == deviceId) ?? SelectedItem;

        Reload();
    }

    public ProfileConfig Settings { get; }

    /// <summary>
    /// Эффект устройства, открытого в профиле: галерея и встроенные эффекты устройства. Правит профиль, а не то, что
    /// играет на устройстве.
    /// </summary>
    public DeviceContentViewModel Content { get; }

    /// <summary>
    /// Галерея эффектов для устройства, открытого в профиле.
    /// </summary>
    public EffectEditorViewModel Editor => Content.Editor;

    /// <summary>
    /// Сцена профиля: несколько устройств играют один эффект.
    /// </summary>
    public ProfileSceneViewModel Scene { get; }

    /// <summary>
    /// Список слева: сцена, затем устройства профиля.
    /// </summary>
    public ObservableCollection<object> Items { get; } = new();

    /// <summary>
    /// Выбранное в списке: сцена или устройство профиля; справа видны все его настройки. null — ничего не выбрано.
    /// </summary>
    [ObservableProperty]
    private object? _selectedItem;

    partial void OnSelectedItemChanged(object? value)
    {
        Content.Edit(EditedDevice?.Lighting.Content, EditedDevice?.Lighting.DeviceId);

        OnPropertyChanged(nameof(EditedDevice));
        OnPropertyChanged(nameof(HasEditedDevice));
        OnPropertyChanged(nameof(IsSceneSelected));
        OnPropertyChanged(nameof(IsSceneOpen));
        OnEditedSceneChanged();
    }

    /// <summary>
    /// Выбрана сцена: справа её настройки или предложение собрать её.
    /// </summary>
    public bool IsSceneSelected => SelectedItem == Scene;

    /// <summary>
    /// Выбрана собранная сцена: справа её редактор.
    /// </summary>
    public bool IsSceneOpen => IsSceneSelected && Scene.HasScene;

    /// <summary>
    /// Выбирает сцену; у профиля без сцены сначала собирает её.
    /// </summary>
    [RelayCommand]
    private void OpenScene()
    {
        if (!Scene.HasScene)
            Scene.BuildCommand.Execute(null);

        SelectedItem = Scene;
    }

    /// <summary>
    /// Убирает сцену из профиля; выбранной остаётся сцена — с предложением собрать её заново.
    /// </summary>
    [RelayCommand]
    private void RemoveScene()
    {
        Scene.RemoveCommand.Execute(null);
    }

    /// <summary>
    /// Устройство, выбранное в списке: справа его яркость, эффект и место в сцене; null — выбрана сцена или ничего.
    /// </summary>
    public ProfileDeviceViewModel? EditedDevice => SelectedItem as ProfileDeviceViewModel;

    public bool HasEditedDevice => EditedDevice != null;

    /// <summary>
    /// Выбранное устройство на холсте сцены; null — оно не в сцене или выбрано не устройство.
    /// </summary>
    public SceneDeviceViewModel? EditedSceneDevice =>
        EditedDevice is { } device ? Scene.Devices.FirstOrDefault(d => d.DeviceId == device.Lighting.DeviceId) : null;

    /// <summary>
    /// Переключатель «В сцене» выбранного устройства; null — сцены нет или устройство не подключалось.
    /// </summary>
    public SceneMemberViewModel? EditedMember =>
        EditedDevice is { } device ? Scene.Members.FirstOrDefault(m => m.DeviceId == device.Lighting.DeviceId) : null;

    public bool HasEditedMember => EditedMember != null;

    /// <summary>
    /// Выбранное устройство играет сцену: его эффект в профиле включится, только когда его уберут из сцены.
    /// </summary>
    public bool IsEditedInScene => EditedSceneDevice != null;

    /// <summary>
    /// Показывает выбранное устройство на холсте сцены: там меняют его место, размер и поворот.
    /// </summary>
    [RelayCommand]
    private void ShowOnCanvas()
    {
        if (EditedSceneDevice is not { } device)
            return;

        SelectedItem = Scene;
        Scene.SelectedDevice = device;
    }

    private void OnEditedSceneChanged()
    {
        OnPropertyChanged(nameof(EditedSceneDevice));
        OnPropertyChanged(nameof(EditedMember));
        OnPropertyChanged(nameof(HasEditedMember));
        OnPropertyChanged(nameof(IsEditedInScene));
    }

    /// <summary>
    /// Сейчас играет этот профиль: его применили или записали в него текущую подсветку, и с тех пор её не меняли.
    /// </summary>
    [ObservableProperty]
    private bool _isActive;

    /// <summary>
    /// Название профиля; пустое не сохраняется.
    /// </summary>
    public string Name
    {
        get => Settings.Name;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim() == Settings.Name)
            {
                // Поле с пустым названием возвращается к прежнему
                OnPropertyChanged();
                return;
            }

            Settings.Name = value.Trim();
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>
    /// Устройства профиля с тем, что он на них включает.
    /// </summary>
    public ObservableCollection<ProfileDeviceViewModel> Devices { get; } = new();

    /// <summary>
    /// Сколько устройств помнит профиль.
    /// </summary>
    public string DevicesText => $"Устройств: {Settings.DeviceLighting.Count}";

    /// <summary>
    /// Что профиль включает на каждом устройстве, по строке на устройство — для подсказки в меню трея.
    /// </summary>
    public string SummaryText => string.Join(Environment.NewLine, DeviceLines().Append(Scene.SummaryText).OfType<string>());

    private IEnumerable<string> DeviceLines()
    {
        return Devices.Count > 0
            ? Devices.Select(d => $"{d.Name}: {d.ContentText}, {d.Brightness:0} %")
            : new[] { "В профиле нет устройств" };
    }

    /// <summary>
    /// Перечитывает устройства из настроек профиля, например после записи в него текущей подсветки.
    /// </summary>
    public void Reload()
    {
        // Открытое устройство пересоздаётся вместе с остальными — выбранной становится сцена
        SelectedItem = null;
        Devices.Clear();
        Items.Clear();

        foreach (var lighting in Settings.DeviceLighting)
            Devices.Add(new ProfileDeviceViewModel(lighting, _findDevice(lighting.DeviceId), OnDeviceChanged, Remove, Edit));

        Scene.Reload();

        Items.Add(Scene);

        foreach (var device in Devices)
            Items.Add(device);

        SelectedItem = Scene;

        OnPropertyChanged(nameof(DevicesText));
        OnPropertyChanged(nameof(SummaryText));
    }

    private void OnDeviceChanged()
    {
        OnPropertyChanged(nameof(SummaryText));
        _changed();
    }

    /// <summary>
    /// Сцену правили: её состав мог поменяться — у выбранного устройства пересчитывается его место в сцене.
    /// </summary>
    private void OnSceneChanged()
    {
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(IsSceneOpen));
        OnEditedSceneChanged();
        _changed();
    }

    /// <summary>
    /// Выбирает устройство в списке; повторный выбор того же устройства снимает выбор.
    /// </summary>
    private void Edit(ProfileDeviceViewModel device)
    {
        SelectedItem = SelectedItem == device ? null : device;
    }

    /// <summary>
    /// В галерее или во встроенных эффектах что-то выбрали или поменяли: устройство в профиле играет то, что правили.
    /// </summary>
    private void OnContentChanged()
    {
        if (EditedDevice == null)
            return;

        EditedDevice.RefreshContent();

        OnDeviceChanged();
    }

    private void Remove(ProfileDeviceViewModel device)
    {
        // Убранное устройство больше не показать — выбранной становится сцена
        if (SelectedItem == device)
            SelectedItem = Scene;

        Settings.DeviceLighting.Remove(device.Lighting);
        Devices.Remove(device);
        Items.Remove(device);
        Scene.RemoveDevice(device.Lighting.DeviceId);
        OnEditedSceneChanged();

        OnPropertyChanged(nameof(DevicesText));
        OnPropertyChanged(nameof(SummaryText));
        _changed();
    }

    public override string ToString() => Name;
}
