namespace LightSpace.Engine.Output;

/// <summary>
/// Снимок диагностики вывода на одно устройство.
/// </summary>
/// <param name="SentFrames">Сколько кадров ушло на устройство.</param>
/// <param name="DroppedFrames">Сколько кадров заменено более свежими, не дождавшись отправки.</param>
/// <param name="LastSendDuration">Сколько заняла последняя успешная отправка.</param>
/// <param name="LastSentAt">Когда ушла последняя успешная команда; null — ещё ни одной.</param>
/// <param name="LastError">Текст последней ошибки связи; null — ошибок не было.</param>
/// <param name="LastErrorAt">Когда случилась последняя ошибка.</param>
/// <param name="IsFailing">Последняя команда не дошла до устройства.</param>
/// <param name="FailingSince">С какого момента команды подряд не доходят; null — последняя дошла.</param>
public sealed record OutputDiagnostics(
    long SentFrames,
    long DroppedFrames,
    TimeSpan LastSendDuration,
    DateTime? LastSentAt,
    string? LastError,
    DateTime? LastErrorAt,
    bool IsFailing,
    DateTime? FailingSince);
