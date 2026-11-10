namespace LightSpace.Devices.Robobloq;

/// <summary>
/// Параметры ленты Robobloq, которые она сообщает о себе.
/// </summary>
/// <param name="ScreenSize">Диагональ монитора, для которого лента, в дюймах.</param>
/// <param name="LedCount">Число светодиодов; 0 — лента не сообщила, управлять нечем.</param>
/// <param name="Serial">Серийный номер ленты.</param>
/// <param name="Firmware">Версия прошивки, например «1.8.2».</param>
public sealed record RobobloqDeviceInfo(byte ScreenSize, byte LedCount, string Serial, string Firmware);
