using CommunityToolkit.Mvvm.ComponentModel;
using LedHub.Services.Scene;

namespace LedHub.ViewModels;

/// <summary>
/// Слой эффектов на сцене: какой эффект в нём играет и где проходят его границы.
/// </summary>
public sealed partial class SceneLayerViewModel : ObservableObject
{
    private readonly Action<SceneLayerViewModel, bool> _edited;

    /// <summary>
    /// edited получает слой и то, сменился ли его эффект, — тогда играющий эффект сцены нужно собрать заново.
    /// </summary>
    public SceneLayerViewModel(SceneLayerConfig config, IReadOnlyList<EffectOption> options, Action<SceneLayerViewModel, bool> edited)
    {
        Config = config;
        Options = options;
        _edited = edited;
    }

    public SceneLayerConfig Config { get; }

    /// <summary>
    /// Эффекты, которые можно поставить в слой.
    /// </summary>
    public IReadOnlyList<EffectOption> Options { get; }

    public EffectOption? Effect
    {
        get => Options.FirstOrDefault(o => o.Definition.Id == Config.EffectId);
        set
        {
            if (value == null || value.Definition.Id == Config.EffectId)
                return;

            Config.EffectId = value.Definition.Id;
            _edited(this, true);
            OnPropertyChanged(string.Empty);
        }
    }

    public string Title => Effect?.Title ?? "Эффект не найден";

    public double X
    {
        get => Config.X;
        set => Edit(() => Config.X = Math.Clamp(Math.Round(value), 0, SceneViewModel.WorldWidth - Config.Width));
    }

    public double Y
    {
        get => Config.Y;
        set => Edit(() => Config.Y = Math.Clamp(Math.Round(value), 0, SceneViewModel.WorldHeight - Config.Height));
    }

    public double Width
    {
        get => Config.Width;
        set => Edit(() => Config.Width = Math.Clamp(Math.Round(value), 10, SceneViewModel.WorldWidth - Config.X));
    }

    public double Height
    {
        get => Config.Height;
        set => Edit(() => Config.Height = Math.Clamp(Math.Round(value), 10, SceneViewModel.WorldHeight - Config.Y));
    }

    /// <summary>
    /// Сообщает интерфейсу, что границы слоя поменялись снаружи, например мышью на схеме.
    /// </summary>
    public void RefreshFields()
    {
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
    }

    private void Edit(Action change)
    {
        change();
        _edited(this, false);
        RefreshFields();
    }
}
