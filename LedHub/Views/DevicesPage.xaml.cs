using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LedHub.ViewModels;

namespace LedHub.Views;

public partial class DevicesPage : UserControl
{
    public DevicesPage()
    {
        InitializeComponent();
    }

    private void LedCell_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        PaintLed(sender);
    }

    /// <summary>
    /// Закрашивает светодиоды, по которым ведут мышью с зажатой кнопкой.
    /// </summary>
    private void LedCell_MouseEnter(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            PaintLed(sender);
    }

    private void PaintLed(object sender)
    {
        if (sender is FrameworkElement { DataContext: LedCellViewModel cell } && DataContext is MainViewModel viewModel)
            viewModel.PaintLedCommand.Execute(cell);
    }
}
