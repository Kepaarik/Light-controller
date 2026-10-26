using LedHub.Services;

namespace LedHub.ViewModels;

/// <summary>
/// Пункт выбора ориентации монитора.
/// </summary>
public sealed record RotationOption(MonitorRotation Rotation, string Title);
