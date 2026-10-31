using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LightSpace.App.Services;

/// <summary>
/// Значки и названия приложений по пути к их exe-файлу. Один раз прочитанное запоминается до выхода из приложения.
/// </summary>
public static class AppIcons
{
    private static readonly Dictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Titles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Значок приложения; null — файла нет или значок не прочитался.
    /// </summary>
    public static ImageSource? IconOf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
            return null;

        if (Icons.TryGetValue(path, out var cached))
            return cached;

        ImageSource? image = null;

        try
        {
            if (File.Exists(path))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);

                if (icon != null)
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();
                    image = bitmap;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            // Без значка приложение показывается общим значком
        }

        Icons[path] = image;

        return image;
    }

    /// <summary>
    /// Название приложения из описания его exe-файла; если описания нет — имя файла без расширения.
    /// </summary>
    public static string TitleOf(string pathOrFileName)
    {
        if (Titles.TryGetValue(pathOrFileName, out var cached))
            return cached;

        var title = Path.GetFileNameWithoutExtension(pathOrFileName);

        try
        {
            if (Path.IsPathRooted(pathOrFileName) && File.Exists(pathOrFileName))
            {
                var description = System.Diagnostics.FileVersionInfo.GetVersionInfo(pathOrFileName).FileDescription;

                if (!string.IsNullOrWhiteSpace(description))
                    title = description.Trim();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Без описания остаётся имя файла
        }

        Titles[pathOrFileName] = title;

        return title;
    }
}
