namespace LightSpace.Core.Config;

/// <summary>
/// Что играет в содержимом.
/// </summary>
public enum ContentKind
{
    /// <summary>Эффект из галереи: каждое устройство показывает его целиком в своём пространстве.</summary>
    Effect,

    /// <summary>Экран: лента повторяет цвета краёв монитора, за которым висит.</summary>
    Screen,

    /// <summary>Музыка: свет следует за звуком компьютера или микрофона.</summary>
    Music,

    /// <summary>Игра: полоски здоровья и маны или брони и события матча Dota 2 и CS2; другие игры — картинка монитора.</summary>
    Game,

    /// <summary>Встроенный эффект: устройства сами играют эффект из своего набора, без кадров от приложения.</summary>
    Builtin
}

/// <summary>
/// Содержимое, которое играет на устройствах: эффект из галереи и сохранённые настройки эффектов.
/// </summary>
public sealed class ContentConfig
{
    /// <summary>
    /// Что играет: эффект из галереи, экран, музыка, игра или встроенный эффект устройств.
    /// </summary>
    public ContentKind Kind { get; set; } = ContentKind.Effect;

    /// <summary>
    /// Идентификатор эффекта, который сейчас играет.
    /// </summary>
    public string EffectId { get; set; } = "solid";

    /// <summary>
    /// Настройки эффектов по их идентификаторам; эффект, которого здесь нет, играет с настройками по умолчанию.
    /// Настройки остаются, когда выбирают другой эффект, и возвращаются вместе с ним.
    /// </summary>
    public Dictionary<string, EffectValuesConfig> Effects { get; set; } = new();

    /// <summary>
    /// Настройки экрана. Играют, только когда содержимое — экран; остаются, когда выбирают другой тип.
    /// </summary>
    public ScreenConfig Screen { get; set; } = new();

    /// <summary>
    /// Настройки музыки. Играют, только когда содержимое — музыка; остаются, когда выбирают другой тип.
    /// </summary>
    public MusicConfig Music { get; set; } = new();

    /// <summary>
    /// Настройки игры. Играют, только когда содержимое — игра; остаются, когда выбирают другой тип.
    /// </summary>
    public GameConfig Game { get; set; } = new();

    /// <summary>
    /// Настройки встроенного эффекта. Играют, только когда содержимое — встроенный эффект; остаются, когда выбирают
    /// другой тип.
    /// </summary>
    public BuiltinConfig Builtin { get; set; } = new();

    /// <summary>
    /// Независимая копия содержимого: правки копии не меняют оригинал.
    /// </summary>
    public ContentConfig Clone()
    {
        return new ContentConfig
        {
            Kind = Kind,
            EffectId = EffectId,
            Effects = Effects.Where(e => e.Value != null).ToDictionary(e => e.Key, e => new EffectValuesConfig
            {
                Numbers = new Dictionary<string, double>(e.Value.Numbers),
                Colors = new Dictionary<string, string>(e.Value.Colors)
            }),
            Screen = Screen.Clone(),
            Music = Music.Clone(),
            Game = Game.Clone(),
            Builtin = Builtin.Clone()
        };
    }

    /// <summary>
    /// Приводит содержимое к допустимому: пустые разделы в файле дали бы null вместо эффекта и его настроек.
    /// </summary>
    public void Normalize()
    {
        EffectId ??= "solid";
        Effects ??= new Dictionary<string, EffectValuesConfig>();

        // Тип, которого нет в этой сборке, мог попасть в файл из другой версии или из правки руками
        if (!Enum.IsDefined(Kind))
            Kind = ContentKind.Effect;

        // В файлах до экрана и музыки этих разделов нет — тогда они получают настройки по умолчанию
        Screen ??= new ScreenConfig();
        Screen.Normalize();

        Music ??= new MusicConfig();
        Music.Normalize();

        // В файлах до игр этого раздела нет — тогда он получает настройки по умолчанию
        Game ??= new GameConfig();
        Game.Normalize();

        // В файлах до встроенных эффектов этого раздела нет — тогда он получает настройки по умолчанию
        Builtin ??= new BuiltinConfig();
        Builtin.Normalize();
    }
}

/// <summary>
/// Значения настроек одного эффекта, которые выставил пользователь; чего здесь нет — берётся по умолчанию.
/// </summary>
public sealed class EffectValuesConfig
{
    /// <summary>
    /// Числа, переключатели (0 или 1) и выбор из вариантов по ключам настроек.
    /// </summary>
    public Dictionary<string, double> Numbers { get; set; } = new();

    /// <summary>
    /// Цвета в формате #RRGGBB по ключам настроек.
    /// </summary>
    public Dictionary<string, string> Colors { get; set; } = new();
}
