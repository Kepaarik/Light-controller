using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LightSpace.Core;
using LightSpace.Core.Config;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Строка списка сценариев группы.
/// </summary>
/// <param name="Title">Название сценария.</param>
/// <param name="IsPlaying">Сценарий сейчас играет в группе.</param>
public sealed record ScenarioRowViewModel(string Title, bool IsPlaying);

/// <summary>
/// Группа на сцене: её устройства, название, цвет обводки, яркость, сценарии и содержимое «По умолчанию».
/// Устройство без группы — группа-одиночка: на сцене это просто карточка устройства.
/// Правки сразу попадают в настройки группы, движок получает их новым снимком.
/// </summary>
public sealed partial class SceneGroupViewModel : ObservableObject
{
    private readonly Action _changed;

    // Группа выбрана на сцене, справа открыта её панель
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// changed вызывается после каждого изменения названия, цвета или яркости группы.
    /// </summary>
    public SceneGroupViewModel(GroupConfig settings, IReadOnlyList<SceneDeviceViewModel> devices,
        IReadOnlyList<ScenarioRowViewModel> scenarios, Action changed)
    {
        Settings = settings;
        Devices = devices;
        Scenarios = scenarios;
        _changed = changed;
    }

    /// <summary>
    /// Сохраняемые настройки группы.
    /// </summary>
    public GroupConfig Settings { get; }

    public string Id => Settings.Id;

    /// <summary>
    /// Группа-одиночка: устройство без группы.
    /// </summary>
    public bool IsSolo => Settings.IsSolo;

    /// <summary>
    /// Устройства группы в порядке списка устройств.
    /// </summary>
    public IReadOnlyList<SceneDeviceViewModel> Devices { get; }

    /// <summary>
    /// Первое устройство группы; у группы-одиночки — её единственное устройство.
    /// </summary>
    public SceneDeviceViewModel Device => Devices[0];

    /// <summary>
    /// Название группы, которое задаёт пользователь.
    /// </summary>
    public string Name
    {
        get => Settings.Name;
        set
        {
            var name = value ?? "";

            if (name == Settings.Name)
                return;

            Settings.Name = name;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            _changed();
        }
    }

    /// <summary>
    /// Как группа подписана на сцене и в панели: у группы-одиночки — названием её устройства.
    /// </summary>
    public string Title => IsSolo ? Device.Name : string.IsNullOrWhiteSpace(Settings.Name) ? "Без названия" : Settings.Name;

    /// <summary>
    /// Цвет обводки группы на сцене.
    /// </summary>
    public Color OutlineColor
    {
        // Испорченный руками цвет в файле настроек показывается нейтральной обводкой
        get => RgbColor.TryParse(Settings.Color, out var color) ? Color.FromRgb(color.R, color.G, color.B) : Colors.Gray;
        set
        {
            var hex = new RgbColor(value.R, value.G, value.B).ToHex();

            if (hex == Settings.Color)
                return;

            Settings.Color = hex;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutlineBrush));
            _changed();
        }
    }

    /// <summary>
    /// Кисть обводки группы на сцене.
    /// </summary>
    public Brush OutlineBrush
    {
        get
        {
            var brush = new SolidColorBrush(OutlineColor);
            brush.Freeze();

            return brush;
        }
    }

    /// <summary>
    /// Яркость подсветки устройств группы, 0–100 %.
    /// </summary>
    public double Brightness
    {
        get => Settings.Brightness;
        set
        {
            var brightness = (int)Math.Round(Math.Clamp(value, 0, 100));

            if (brightness == Settings.Brightness)
                return;

            Settings.Brightness = brightness;
            OnPropertyChanged();
            _changed();
        }
    }

    /// <summary>
    /// Сценарии группы по убыванию важности; последний — всегда «По умолчанию».
    /// </summary>
    public IReadOnlyList<ScenarioRowViewModel> Scenarios { get; }

    /// <summary>
    /// Название сценария, который сейчас играет в группе.
    /// </summary>
    public string ActiveScenarioTitle => Scenarios.FirstOrDefault(s => s.IsPlaying)?.Title ?? "";

    /// <summary>
    /// Содержимое «По умолчанию» группы: эффект и настройки эффектов.
    /// </summary>
    public ContentConfig Content => Settings.DefaultContent;
}
