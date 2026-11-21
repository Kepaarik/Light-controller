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

        // Щелчок по устройству на холсте открывает его настройки справа, щелчок мимо — эффект сцены
        Scene.DevicePicked += deviceId => EditedDevice = Devices.FirstOrDefault(d => d.Lighting.DeviceId == deviceId);

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
    /// У профиля есть сцена: слева её холст и настройки.
    /// </summary>
    public bool IsSceneOpen => Scene.HasScene;

    /// <summary>
    /// Собирает сцену, если её ещё нет.
    /// </summary>
    [RelayCommand]
    private void OpenScene()
    {
        if (!Scene.HasScene)
            Scene.BuildCommand.Execute(null);
    }

    /// <summary>
    /// Убирает сцену из профиля; слева снова предложение собрать её.
    /// </summary>
    [RelayCommand]
    private void RemoveScene()
    {
        Scene.RemoveCommand.Execute(null);
    }

    /// <summary>
    /// Устройство, открытое справа: его яркость, место в сцене и эффект; null — справа эффект сцены.
    /// </summary>
    [ObservableProperty]
    private ProfileDeviceViewModel? _editedDevice;

    public bool HasEditedDevice => EditedDevice != null;

    partial void OnEditedDeviceChanged(ProfileDeviceViewModel? value)
    {
        Content.Edit(value?.Lighting.Content, value?.Lighting.DeviceId);

        OnPropertyChanged(nameof(HasEditedDevice));
        OnEditedSceneChanged();
    }

    /// <summary>
    /// Закрывает настройки устройства: справа снова эффект сцены.
    /// </summary>
    [RelayCommand]
    private void CloseDevice()
    {
        EditedDevice = null;
        Scene.SelectedDevice = null;
    }

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
    /// Открытое устройство или состав сцены поменялись: на холсте выделено открытое устройство, если оно в сцене.
    /// </summary>
    private void OnEditedSceneChanged()
    {
        // Без открытого устройства выделение на холсте не трогается: там могут тянуть забытое устройство
        if (EditedDevice != null)
            Scene.SelectedDevice = EditedSceneDevice;

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
        // Открытое устройство пересоздаётся вместе с остальными — справа снова эффект сцены
        EditedDevice = null;
        Devices.Clear();

        foreach (var lighting in Settings.DeviceLighting)
            Devices.Add(new ProfileDeviceViewModel(lighting, _findDevice(lighting.DeviceId), OnDeviceChanged, Remove, Edit));

        Scene.Reload();

        OnPropertyChanged(nameof(IsSceneOpen));
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
    /// Открывает настройки устройства справа; повторно — закрывает их.
    /// </summary>
    private void Edit(ProfileDeviceViewModel device)
    {
        if (EditedDevice == device)
            CloseDevice();
        else
            EditedDevice = device;
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
        // Убранное устройство больше не показать — справа снова эффект сцены
        if (EditedDevice == device)
            EditedDevice = null;

        Settings.DeviceLighting.Remove(device.Lighting);
        Devices.Remove(device);
        Scene.RemoveDevice(device.Lighting.DeviceId);
        OnEditedSceneChanged();

        OnPropertyChanged(nameof(DevicesText));
        OnPropertyChanged(nameof(SummaryText));
        _changed();
    }

    public override string ToString() => Name;
}
