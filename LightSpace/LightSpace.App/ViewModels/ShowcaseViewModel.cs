using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.App.Controls;
using LightSpace.App.Services;
using LightSpace.Core;
using LightSpace.Devices.RoyalKludge;
using LightSpace.Engine.Effects;
using LightSpace.Engine.Space;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Вариант темы для переключателя на витрине.
/// </summary>
public sealed record ThemeOption(ThemePreference Preference, string Title)
{
    public override string ToString() => Title;
}

/// <summary>
/// Образец плитки галереи: подпись и кисть-превью.
/// </summary>
public sealed record TileSample(string Title, Brush Preview);

/// <summary>
/// Образец строки перетаскиваемого списка: название, пояснение и отметка «▶ играет».
/// </summary>
public sealed record ReorderSample(string Title, string Caption, bool IsPlaying);

/// <summary>
/// Витрина компонентов: образцы данных для всех компонентов дизайн-системы и переключатель темы.
/// </summary>
public sealed partial class ShowcaseViewModel : ObservableObject
{
    private ThemeOption _selectedTheme;
    private RgbColor _color = RgbColor.Parse("#7C6CFF");

    [ObservableProperty]
    private string _selectedSegment = "Эффект";

    [ObservableProperty]
    private TileSample? _selectedTile;

    [ObservableProperty]
    private string? _selectedApp;

    [ObservableProperty]
    private AppOptionViewModel? _selectedAppOption;

    [ObservableProperty]
    private ContentKindOption _selectedContentKind = ContentKindOption.All[0];

    [ObservableProperty]
    private double _brightness = 70;

    [ObservableProperty]
    private string _fieldText = "Подсветка стола";

    [ObservableProperty]
    private string _status = "";

    public ShowcaseViewModel()
    {
        _selectedTheme = Themes.First(t => t.Preference == ThemeService.Preference);
        _selectedTile = Tiles[1];
        _selectedApp = Apps[2];
        _selectedAppOption = AppOptions[0];
    }

    public IReadOnlyList<ThemeOption> Themes { get; } = new ThemeOption[]
    {
        new(ThemePreference.System, "Как в Windows"),
        new(ThemePreference.Light, "Светлая"),
        new(ThemePreference.Dark, "Тёмная")
    };

    public IReadOnlyList<string> Segments { get; } = new[] { "Эффект", "Сцена", "Экран", "Музыка", "Игра" };

    public IReadOnlyList<TileSample> Tiles { get; } = new TileSample[]
    {
        new("Радуга", Gradient("#FF3B3B", "#FFD23B", "#3BFF6E", "#3BB5FF", "#B03BFF")),
        new("Северное сияние", Gradient("#0B3D3A", "#21D19F", "#6C5CE7")),
        new("Закат", Gradient("#FF7A18", "#AF002D", "#319197")),
        new("Океан", Gradient("#0F2027", "#2C5364", "#5BC0EB")),
        new("Кадр эффекта", RenderEffect("aurora", DeviceGeometry.Grid(24, 10), 24, 10)),
        new("Без превью", Brushes.Transparent)
    };

    /// <summary>
    /// Кадр радуги на ленте из 60 светодиодов — образец полосы живых цветов.
    /// </summary>
    public Brush StripColors { get; } = RenderEffect("rainbow", DeviceGeometry.Strip("showcase", 60), 60, 1);

    /// <summary>
    /// Кадр радуги на клавиатуре RK S98 — образец раскладки с живыми цветами клавиш.
    /// </summary>
    public IReadOnlyList<RgbColor> KeyboardColors { get; } = EffectRenderer.Render(EffectLibrary.FindOrSolid("rainbow"), EffectValues.Empty,
        DeviceGeometry.Keyboard(RkS98Layout.Keys, RkS98Layout.Keys.Max(k => k.Led) + 1), 0);

    public IReadOnlyList<string> Apps { get; } = new[]
    {
        "Dota 2 — dota2.exe", "Counter-Strike 2 — cs2.exe", "Visual Studio — devenv.exe", "Spotify — Spotify.exe",
        "Discord — Discord.exe", "Google Chrome — chrome.exe", "Telegram — Telegram.exe", "OBS Studio — obs64.exe",
        "Steam — steam.exe", "Блокнот — notepad.exe"
    };

    /// <summary>
    /// Приложения со значками для панели выбора приложения; у последнего пути нет — он показывается общим значком.
    /// </summary>
    public IReadOnlyList<AppOptionViewModel> AppOptions { get; } = new[]
    {
        AppOptionViewModel.From(Environment.ProcessPath ?? "LightSpace.exe"),
        AppOptionViewModel.From(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")),
        AppOptionViewModel.From(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe")),
        AppOptionViewModel.From("dota2.exe")
    };

    /// <summary>
    /// Типы содержимого сценария: доступные и выключенные сегменты.
    /// </summary>
    public IReadOnlyList<ContentKindOption> ContentKinds => ContentKindOption.All;

    /// <summary>
    /// Строки перетаскиваемого списка: их можно переставлять мышью.
    /// </summary>
    public ObservableCollection<ReorderSample> ReorderSamples { get; } = new()
    {
        new("Игры", "dota2.exe · в фокусе", true),
        new("Музыка", "Spotify.exe · запущен", false),
        new("Работа", "devenv.exe · в фокусе", false)
    };

    public ThemeOption SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (SetProperty(ref _selectedTheme, value))
                ThemeService.SetPreference(value.Preference);
        }
    }

    /// <summary>
    /// Цвет на круге выбора цвета.
    /// </summary>
    public RgbColor Color
    {
        get => _color;
        set
        {
            if (!SetProperty(ref _color, value))
                return;

            OnPropertyChanged(nameof(ColorHex));
            OnPropertyChanged(nameof(ColorBrush));
        }
    }

    public string ColorHex => _color.ToHex();

    public Brush ColorBrush => new SolidColorBrush(System.Windows.Media.Color.FromRgb(_color.R, _color.G, _color.B));

    [RelayCommand]
    private async Task ShowDialog()
    {
        var confirmed = await Dialogs.ConfirmAsync("Сохранить сценарий?",
            "Сценарий «Игры» включится, когда Dota 2 будет в фокусе.", "Сохранить");

        Status = confirmed ? "Карточка: подтверждено" : "Карточка: отменено";
    }

    [RelayCommand]
    private async Task ShowDangerDialog()
    {
        var confirmed = await Dialogs.ConfirmAsync("Удалить группу «Стол»?",
            "Устройства останутся, но каждое станет отдельной группой.", "Удалить", isDanger: true);

        Status = confirmed ? "Удаление: подтверждено" : "Удаление: отменено";
    }

    [RelayCommand]
    private void MoveSample(ReorderMove move)
    {
        ReorderSamples.Move(move.From, move.To);
        Status = $"Перестановка: {move.From + 1} → {move.To + 1}";
    }

    [RelayCommand]
    private async Task ShowMessage()
    {
        await Dialogs.ShowAsync("Устройство отключилось", "Лента «Полка» не отвечает. Light Space переподключится, как только она появится в сети.");
    }

    /// <summary>
    /// Один кадр эффекта с настройками по умолчанию, нарисованный тем же рендером, что и кадры устройств.
    /// </summary>
    private static Brush RenderEffect(string effectId, DeviceGeometry space, int columns, int rows)
    {
        var bitmap = new ColorBitmap(columns, rows);
        bitmap.Write(EffectRenderer.Render(EffectLibrary.FindOrSolid(effectId), EffectValues.Empty, space, 3.3));

        return bitmap.Brush;
    }

    private static Brush Gradient(params string[] colors)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };

        for (var i = 0; i < colors.Length; i++)
            brush.GradientStops.Add(new GradientStop((System.Windows.Media.Color)ColorConverter.ConvertFromString(colors[i]), i / (double)(colors.Length - 1)));

        brush.Freeze();

        return brush;
    }
}
