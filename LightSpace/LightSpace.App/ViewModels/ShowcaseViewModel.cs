using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LightSpace.App.Services;
using LightSpace.Core;

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
        new("Без превью", Brushes.Transparent)
    };

    public IReadOnlyList<string> Apps { get; } = new[]
    {
        "Dota 2 — dota2.exe", "Counter-Strike 2 — cs2.exe", "Visual Studio — devenv.exe", "Spotify — Spotify.exe",
        "Discord — Discord.exe", "Google Chrome — chrome.exe", "Telegram — Telegram.exe", "OBS Studio — obs64.exe",
        "Steam — steam.exe", "Блокнот — notepad.exe"
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
    private async Task ShowMessage()
    {
        await Dialogs.ShowAsync("Устройство отключилось", "Лента «Полка» не отвечает. Light Space переподключится, как только она появится в сети.");
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
