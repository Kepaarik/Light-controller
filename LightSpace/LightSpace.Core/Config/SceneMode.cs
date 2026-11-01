namespace LightSpace.Core.Config;

/// <summary>
/// Что играет на устройствах сцены.
/// </summary>
public enum SceneMode
{
    /// <summary>
    /// У каждой группы устройств своё содержимое; устройство без группы — само себе группа.
    /// </summary>
    Groups,

    /// <summary>
    /// На всех устройствах играет одно и то же.
    /// </summary>
    WholeScene,

    /// <summary>
    /// У каждого устройства своё содержимое, группы не действуют.
    /// </summary>
    Devices
}
