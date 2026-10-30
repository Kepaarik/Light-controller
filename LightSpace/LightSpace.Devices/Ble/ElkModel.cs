namespace LightSpace.Devices.Ble;

/// <summary>
/// Особенности конкретной модели контроллера ELK/MELK: собственная команда яркости.
/// Команды взяты из интеграции elkbledom для Home Assistant (models.json, definitions.json).
/// </summary>
public sealed class ElkModel
{
    // Включены только модели, у которых команды включения, выключения и цвета совпадают с общими для ELK-BLEDOM
    private static readonly ElkModel[] Models =
    {
        new("MELK-OA21"),
        new("MELK-OA10"),
        new("MELK-OC10"),
        new("MELK-OF10")
    };

    private ElkModel(string name)
    {
        Name = name;
    }

    public string Name { get; }

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

    // Имена в рекламе бывают с пробелами и в разном регистре: "MELK-OA21 16", "melk-oa2116"
    private static string Normalize(string name)
    {
        return new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }
}
