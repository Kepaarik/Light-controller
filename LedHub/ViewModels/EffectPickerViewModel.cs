using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LedHub.Services;
using LedHub.Services.Effects;

namespace LedHub.ViewModels;

/// <summary>
/// Галерея эффектов с настройками выбранного: клик по плитке выбирает и запускает эффект,
/// настройки меняют его прямо во время работы и сохраняются между запусками.
/// </summary>
public sealed partial class EffectPickerViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly Func<EffectOption, bool> _start;
    private readonly Action _stop;
    private readonly Action _save;

    // Настройки сохраняются, когда ползунок перестали двигать, а не на каждом шаге
    private CancellationTokenSource? _saveDelay;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private EffectOption? _selectedEffect;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private EffectOption? _runningEffect;

    /// <summary>
    /// start запускает эффект и сообщает, удалось ли; stop останавливает запущенный.
    /// </summary>
    public EffectPickerViewModel(AppConfig config, Func<EffectOption, bool> start, Action stop, Action save)
    {
        _config = config;
        _start = start;
        _stop = stop;
        _save = save;

        Options = EffectLibrary.All.Select(e => new EffectOption(e)).ToList();
        Groups = Options
            .GroupBy(o => o.Definition.Category)
            .Select(g => new EffectGroup(EffectLibrary.CategoryTitle(g.Key), g.ToList()))
            .ToList();

        SelectedEffect = Options[0];
    }

    public IReadOnlyList<EffectOption> Options { get; }

    public IReadOnlyList<EffectGroup> Groups { get; }

    public ObservableCollection<EffectParameterViewModel> Parameters { get; } = new();

    public bool HasSelection => SelectedEffect != null;

    public bool IsRunning => RunningEffect != null;

    public string StatusText => RunningEffect != null ? $"Сейчас: {RunningEffect.Title}" : "Эффект не запущен";

    /// <summary>
    /// Сохранённые настройки эффекта; заводятся пустыми, если эффект ещё не настраивали.
    /// </summary>
    public EffectValues ValuesFor(EffectDefinition effect)
    {
        if (!_config.Effects.TryGetValue(effect.Id, out var values))
        {
            values = new EffectValues();
            _config.Effects[effect.Id] = values;
        }

        return values;
    }

    /// <summary>
    /// Текущая скорость эффекта; у эффектов без настройки скорости — обычная.
    /// </summary>
    public double SpeedOf(EffectDefinition effect)
    {
        if (ValuesFor(effect).Numbers.TryGetValue("speed", out var speed))
            return speed;

        return effect.Find("speed")?.Default ?? 1;
    }

    /// <summary>
    /// Отмечает запущенный эффект; null — ничего не запущено.
    /// </summary>
    public void MarkRunning(EffectOption? option)
    {
        foreach (var item in Options)
            item.IsActive = item == option;

        RunningEffect = option;
    }

    [RelayCommand]
    private void Start(EffectOption option)
    {
        SelectedEffect = option;

        if (_start(option))
            MarkRunning(option);
    }

    [RelayCommand]
    private void Stop()
    {
        _stop();
        MarkRunning(null);
    }

    [RelayCommand]
    private void ResetParameters()
    {
        foreach (var parameter in Parameters)
            parameter.Reset();

        ScheduleSave();
    }

    partial void OnSelectedEffectChanged(EffectOption? value)
    {
        foreach (var item in Options)
            item.IsSelected = item == value;

        Parameters.Clear();

        if (value == null)
            return;

        var values = ValuesFor(value.Definition);

        foreach (var parameter in value.Definition.Parameters)
        {
            EffectParameterViewModel item = parameter.Kind switch
            {
                EffectParameterKind.Color => new ColorParameterViewModel(parameter, values, ScheduleSave),
                EffectParameterKind.Choice => new ChoiceParameterViewModel(parameter, values, ScheduleSave),
                EffectParameterKind.Toggle => new ToggleParameterViewModel(parameter, values, ScheduleSave),
                _ => new NumberParameterViewModel(parameter, values, ScheduleSave)
            };

            Parameters.Add(item);
        }
    }

    private async void ScheduleSave()
    {
        _saveDelay?.Cancel();
        _saveDelay?.Dispose();

        var cts = new CancellationTokenSource();
        _saveDelay = cts;

        try
        {
            await Task.Delay(700, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _save();
    }
}
