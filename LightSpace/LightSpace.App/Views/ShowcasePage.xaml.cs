using System.Windows.Controls;
using LightSpace.App.ViewModels;

namespace LightSpace.App.Views;

/// <summary>
/// Витрина компонентов дизайн-системы: есть только в отладочной сборке, открывается по Ctrl+Shift+D.
/// </summary>
public partial class ShowcasePage : UserControl
{
    public ShowcasePage()
    {
        InitializeComponent();

        DataContext = new ShowcaseViewModel();
    }
}
