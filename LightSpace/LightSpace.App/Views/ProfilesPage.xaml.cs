using System.Windows.Controls;
using System.Windows.Input;

namespace LightSpace.App.Views;

/// <summary>
/// Раздел «Профили»: сохранённые профили подсветки и что каждый включает на устройствах.
/// </summary>
public partial class ProfilesPage : UserControl
{
    public ProfilesPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Enter в поле названия сохраняет его сразу, не дожидаясь ухода фокуса.
    /// </summary>
    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        ((TextBox)sender).GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }
}
