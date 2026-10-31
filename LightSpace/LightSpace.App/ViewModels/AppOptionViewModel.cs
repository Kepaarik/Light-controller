using System.Windows.Media;
using LightSpace.App.Services;

namespace LightSpace.App.ViewModels;

/// <summary>
/// Приложение, которое можно выбрать условием сценария: значок, название и exe-файл.
/// </summary>
/// <param name="Title">Название приложения из описания exe-файла или имя файла.</param>
/// <param name="ExeName">Имя exe-файла, например dota2.exe.</param>
/// <param name="Path">Полный путь к exe-файлу; если путь неизвестен — только имя файла.</param>
/// <param name="Icon">Значок приложения; null — показывается общий значок.</param>
public sealed record AppOptionViewModel(string Title, string ExeName, string Path, ImageSource? Icon)
{
    /// <summary>
    /// Приложение по пути к exe-файлу или по одному имени файла.
    /// </summary>
    public static AppOptionViewModel From(string pathOrFileName)
    {
        return new AppOptionViewModel(AppIcons.TitleOf(pathOrFileName), System.IO.Path.GetFileName(pathOrFileName), pathOrFileName,
            AppIcons.IconOf(pathOrFileName));
    }

    /// <summary>
    /// Текст, по которому приложение находится поиском: название и имя файла.
    /// </summary>
    public override string ToString() => $"{Title} {ExeName}";
}
