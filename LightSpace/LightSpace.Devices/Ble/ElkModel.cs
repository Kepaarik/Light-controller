namespace LightSpace.Devices.Ble;

/// <summary>
/// Особенности конкретной модели контроллера ELK/MELK: собственная команда яркости и встроенные эффекты.
/// Команды и номера эффектов взяты из интеграции elkbledom для Home Assistant (models.json, definitions.json).
/// </summary>
public sealed class ElkModel
{
    private static readonly IReadOnlyList<BuiltinEffect> StripXEffects = BuildEffects(new (string, int[])[]
    {
        ("Радуга вверх", new[] { 1 }),
        ("Радуга вниз", new[] { 2 }),
        ("Бегущие огни", new[] { 3, 4, 5, 6, 7, 8, 9, 10 }),
        ("Бегущие импульсы", new[] { 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22 }),
        ("Поток", new[] { 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52 }),
        ("Пульсирующие вспышки", new[] { 53, 54 }),
        ("Занавес", new[] { 55, 56, 57, 58, 59, 60 }),
        ("Огонь", new[] { 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73 }),
        ("Переливы", new[] { 74, 75, 76, 77, 78, 79, 80, 81, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 99, 100, 101, 102, 103, 104, 105, 106, 107, 108, 109, 110, 111, 112, 113, 114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 212, 213, 214, 215, 216, 217, 218, 219 }),
        ("Погоня", new[] { 144, 145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167 }),
        ("Лифт", new[] { 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181 }),
        ("Мигающие полосы", new[] { 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193 }),
        ("Вспышки RGB", new[] { 194, 195, 196, 197, 198, 199 }),
        ("Вечеринка", new[] { 200 }),
        ("Затухание снизу", new[] { 201 }),
        ("Пульс", new[] { 202, 203, 204, 205, 206, 207, 208, 209, 210, 211 }),
        ("Переливающаяся полоса", new[] { 220 })
    });

    private static readonly IReadOnlyList<BuiltinEffect> MelkOxEffects = new BuiltinEffect[]
    {
        new(0, "Автосмена"),
        new(1, "Magic Back"),
        new(2, "Эффект 2"),
        new(3, "Эффект 3"),
        new(4, "Эффект 4"),
        new(5, "Эффект 5"),
        new(16, "Радуга"),
        new(32, "Цветная волна"),
        new(48, "Дыхание"),
        new(64, "Стробоскоп"),
        new(128, "Скачки RGB"),
        new(144, "Переливы RGB"),
        new(207, "Синяя прокрутка")
    };

    // Включены только модели, у которых команды включения, выключения и цвета совпадают с общими для ELK-BLEDOM
    private static readonly ElkModel[] Models =
    {
        new("MELK-OA21", StripXEffects),
        new("MELK-OA10", MelkOxEffects),
        new("MELK-OC10", MelkOxEffects),
        new("MELK-OF10", MelkOxEffects)
    };

    private ElkModel(string name, IReadOnlyList<BuiltinEffect> effects)
    {
        Name = name;
        Effects = effects;
    }

    public string Name { get; }

    /// <summary>
    /// Встроенные эффекты контроллера этой модели.
    /// </summary>
    public IReadOnlyList<BuiltinEffect> Effects { get; }

    /// <summary>
    /// Находит модель по имени Bluetooth-устройства; null, если модель неизвестна.
    /// </summary>
    public static ElkModel? Find(string deviceName)
    {
        var key = Normalize(deviceName);

        return Models
            .Where(m => key.StartsWith(Normalize(m.Name), StringComparison.Ordinal))
            .OrderByDescending(m => m.Name.Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// Собственная яркость контроллера, 0–100 %.
    /// </summary>
    public static byte[] BrightnessCommand(int brightness)
    {
        return new byte[] { 0x7E, 0x04, 0x01, (byte)Math.Clamp(brightness, 0, 100), 0x01, 0xFF, 0xFF, 0x00, 0xEF };
    }

    /// <summary>
    /// Запуск встроенного эффекта по его номеру.
    /// </summary>
    public static byte[] EffectCommand(int effectId)
    {
        return new byte[] { 0x7E, 0x05, 0x03, (byte)effectId, 0x06, 0xFF, 0xFF, 0x00, 0xEF };
    }

    /// <summary>
    /// Скорость встроенного эффекта; интеграция elkbledom передаёт её в диапазоне 0–255.
    /// </summary>
    public static byte[] EffectSpeedCommand(int speed)
    {
        var value = (byte)Math.Round(Math.Clamp(speed, 0, 100) * 255 / 100.0);

        return new byte[] { 0x7E, 0x04, 0x02, value, 0xFF, 0xFF, 0xFF, 0x00, 0xEF };
    }

    // Имена в рекламе бывают с пробелами и в разном регистре: "MELK-OA21 16", "melk-oa2116"
    private static string Normalize(string name)
    {
        return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    // У многих эффектов есть варианты под соседними номерами — они получают общее название с номером варианта
    private static IReadOnlyList<BuiltinEffect> BuildEffects((string Title, int[] Ids)[] groups)
    {
        var effects = new List<BuiltinEffect>();

        foreach (var (title, ids) in groups)
        {
            for (var i = 0; i < ids.Length; i++)
                effects.Add(new BuiltinEffect(ids[i], ids.Length > 1 ? $"{title} {i + 1}" : title));
        }

        return effects;
    }
}
