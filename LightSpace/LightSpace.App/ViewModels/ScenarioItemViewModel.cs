using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LightSpace.Core.Config;
using LightSpace.Engine.Scenarios;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Режим условия в сегментах «Когда»: «в фокусе» или «запущен».
/// </summary>
public sealed record ConditionModeOption(ConditionMode Mode, string Title)
{
    public static readonly IReadOnlyList<ConditionModeOption> All = new ConditionModeOption[]
    {
        new(ConditionMode.Focused, "В фокусе"),
        new(ConditionMode.Running, "Запущен")
    };

    public override string ToString() => Title;
}

/// <summary>
/// Группа в чипах «Группы» редактора сценария: отмеченный чип — сценарий стоит в этой группе.
/// </summary>
public sealed class ScenarioGroupChipViewModel : ObservableObject
{
    private readonly Action<ScenarioGroupChipViewModel> _toggled;
    private bool _isChecked;

    /// <summary>
    /// toggled вызывается, когда чип отмечают или снимают.
    /// </summary>
    public ScenarioGroupChipViewModel(GroupConfig group, string title, bool isChecked, Action<ScenarioGroupChipViewModel> toggled)
    {
        Group = group;
        Title = title;
        _isChecked = isChecked;
        _toggled = toggled;
    }

    public GroupConfig Group { get; }

    public string Title { get; }

    /// <summary>
    /// Сценарий стоит в этой группе.
    /// </summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value))
                _toggled(this);
        }
    }
}

/// <summary>
/// Строка диагностики: что сейчас играет в группе и почему.
/// </summary>
/// <param name="GroupTitle">Название группы или её устройства.</param>
/// <param name="ScenarioTitle">Играющий сценарий; «По умолчанию», если ни один сценарий не активен.</param>
/// <param name="Reason">Какое условие сработало или почему не сработало ни одно.</param>
public sealed record GroupPlayingViewModel(string GroupTitle, string ScenarioTitle, string Reason);

/// <summary>
/// Сценарий в списке вкладки «Сценарии» и в её редакторе: название, условие, где играет сейчас.
/// Правки сразу попадают в настройки сценария.
/// </summary>
public sealed class ScenarioItemViewModel : ObservableObject
{
    private readonly Action _changed;

    private AppOptionViewModel? _app;
    private string _playingText = "";
    private bool _isPlaying;

    /// <summary>
    /// changed вызывается после каждой правки названия или условия.
    /// </summary>
    public ScenarioItemViewModel(ScenarioConfig settings, Action changed)
    {
        Settings = settings;
        _changed = changed;
        _app = settings.Condition.IsSet ? AppOptionViewModel.From(settings.Condition.ExePath) : null;
    }

    /// <summary>
    /// Сохраняемые настройки сценария.
    /// </summary>
    public ScenarioConfig Settings { get; }

    public string Id => Settings.Id;

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
    /// Как сценарий подписан в списке: названием, а без названия — «Без названия».
    /// </summary>
    public string Title => TitleOf(Settings);

    /// <summary>
    /// Приложение из условия; null — приложение не выбрано.
    /// </summary>
    public AppOptionViewModel? App
    {
        get => _app;
        set
        {
            // Выпадающая панель сбрасывает выбор в null, когда пересобирает список, — условие от этого не меняется
            if (value == null || value.Path == Settings.Condition.ExePath)
                return;

            _app = value;
            Settings.Condition.ExePath = value.Path;

            OnPropertyChanged();
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(ConditionText));
            _changed();
        }
    }

    /// <summary>
    /// Значок приложения из условия; null — приложение не выбрано или значок не прочитался.
    /// </summary>
    public ImageSource? Icon => _app?.Icon;

    /// <summary>
    /// Режим условия: «в фокусе» или «запущен».
    /// </summary>
    public ConditionModeOption Mode
    {
        get => ConditionModeOption.All.First(o => o.Mode == Settings.Condition.Mode);
        set
        {
            if (value == null || value.Mode == Settings.Condition.Mode)
                return;

            Settings.Condition.Mode = value.Mode;

            OnPropertyChanged();
            OnPropertyChanged(nameof(ConditionText));
            _changed();
        }
    }

    /// <summary>
    /// Условие словами: «dota2.exe · в фокусе».
    /// </summary>
    public string ConditionText => ScenarioSelector.Describe(Settings.Condition);

    /// <summary>
    /// Сценарий сейчас играет хотя бы в одной группе.
    /// </summary>
    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetProperty(ref _isPlaying, value);
    }

    /// <summary>
    /// В каких группах сценарий играет сейчас: «Играет: Стол, Полка».
    /// </summary>
    public string PlayingText
    {
        get => _playingText;
        private set => SetProperty(ref _playingText, value);
    }

    /// <summary>
    /// Как сценарий подписан в списках: его название, а без названия — «Без названия».
    /// </summary>
    public static string TitleOf(ScenarioConfig scenario)
    {
        return string.IsNullOrWhiteSpace(scenario.Name) ? "Без названия" : scenario.Name;
    }

    /// <summary>
    /// Запоминает, в каких группах сценарий играет сейчас, и в каких группах он стоит вообще.
    /// </summary>
    public void SetPlaying(IReadOnlyList<string> playingGroups, int groupCount)
    {
        IsPlaying = playingGroups.Count > 0;

        PlayingText = playingGroups.Count > 0 ? $"Играет: {string.Join(", ", playingGroups)}"
            : groupCount == 0 ? "Не стоит ни в одной группе"
            : "Сейчас не играет";
    }
}
