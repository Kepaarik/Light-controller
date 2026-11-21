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
    /// </summary>
    public ProfileOption(ProfileConfig settings, Func<string, ManagedDevice?> findDevice, Action changed)
    {
        Settings = settings;
        _findDevice = findDevice;
        _changed = changed;

        Editor = new EffectEditorViewModel(OnEditorChanged);
        Scene = new ProfileSceneViewModel(settings, findDevice, OnSceneChanged);

        Reload();
    }

    public ProfileConfig Settings { get; }

    /// <summary>
    /// Галерея эффектов для устройства, открытого в профиле; правит профиль, а не то, что играет на устройстве.
    /// </summary>
    public EffectEditorViewModel Editor { get; }

    /// <summary>
    /// Сцена профиля: несколько устройств играют один эффект.
    /// </summary>
    public ProfileSceneViewModel Scene { get; }

    /// <summary>
    /// Редактор сцены открыт.
    /// </summary>
    [ObservableProperty]
    private bool _isSceneOpen;

    /// <summary>
    /// Открывает редактор сцены; у профиля без сцены сначала собирает её. Повторное нажатие закрывает редактор.
    /// </summary>
    [RelayCommand]
    private void OpenScene()
    {
        if (!Scene.HasScene)
        {
            Scene.BuildCommand.Execute(null);
            IsSceneOpen = true;
            return;
        }

        IsSceneOpen = !IsSceneOpen;
    }

    /// <summary>
    /// Убирает сцену из профиля и закрывает её редактор.
    /// </summary>
    [RelayCommand]
    private void RemoveScene()
    {
        Scene.RemoveCommand.Execute(null);
        IsSceneOpen = false;
    }

    /// <summary>
    /// Устройство, эффект которого выбирают в профиле; null — галерея закрыта.
    /// </summary>
    [ObservableProperty]
    private ProfileDeviceViewModel? _editedDevice;

    public bool HasEditedDevice => EditedDevice != null;

    partial void OnEditedDeviceChanged(ProfileDeviceViewModel? value)
    {
        Editor.Edit(value?.Lighting.Content);
        Editor.IsPlaying = value?.Lighting.Content.Kind == ContentKind.Effect;

        OnPropertyChanged(nameof(HasEditedDevice));
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
        // Открытое устройство пересоздаётся вместе с остальными — галерея закрывается
        EditedDevice = null;
        Devices.Clear();

        foreach (var lighting in Settings.DeviceLighting)
            Devices.Add(new ProfileDeviceViewModel(lighting, _findDevice(lighting.DeviceId), OnDeviceChanged, Remove, Edit));

        Scene.Reload();

        OnPropertyChanged(nameof(DevicesText));
        OnPropertyChanged(nameof(SummaryText));
    }

    private void OnDeviceChanged()
    {
        OnPropertyChanged(nameof(SummaryText));
        _changed();
    }

    private void OnSceneChanged()
    {
        OnPropertyChanged(nameof(SummaryText));
        _changed();
    }

    /// <summary>
    /// Открывает галерею для устройства; повторный щелчок по тому же устройству её закрывает.
    /// </summary>
    private void Edit(ProfileDeviceViewModel device)
    {
        EditedDevice = EditedDevice == device ? null : device;
    }

    /// <summary>
    /// В галерее выбрали эффект или поменяли его настройки: устройство в профиле играет эффект из галереи.
    /// </summary>
    private void OnEditorChanged()
    {
        if (EditedDevice == null)
            return;

        EditedDevice.Lighting.Content.Kind = ContentKind.Effect;
        Editor.IsPlaying = true;
        EditedDevice.RefreshContent();

        OnDeviceChanged();
    }

    private void Remove(ProfileDeviceViewModel device)
    {
        if (EditedDevice == device)
            EditedDevice = null;

        Settings.DeviceLighting.Remove(device.Lighting);
        Devices.Remove(device);
        Scene.RemoveDevice(device.Lighting.DeviceId);

        OnPropertyChanged(nameof(DevicesText));
        OnPropertyChanged(nameof(SummaryText));
        _changed();
    }

    public override string ToString() => Name;
}
