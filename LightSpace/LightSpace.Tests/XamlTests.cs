using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LightSpace.App;
using LightSpace.App.Controls;
using LightSpace.App.ViewModels;
using LightSpace.App.Views;
using LightSpace.Core.Config;

namespace LightSpace.Tests;

/// <summary>
/// Разметка окна и меню трея загружается: все ресурсы находятся, шаблоны применяются, привязки ведут к существующим свойствам.
/// Ошибку в ресурсе сборка не ловит — она видна только при загрузке страницы.
/// </summary>
public sealed class XamlTests
{
    [Fact]
    public void EveryPageOfMainWindowLoads()
    {
        var errors = UiThread.InvokeCollectingBindingErrors(() =>
        {
            var vm = new MainViewModel(ConfigWithProfile());
            var connected = vm.Devices.Items.Single(i => i.Device.Id == "a");

            connected.Device.Connect(new FakeLed("a", "ELK-BLE"), isLightingEnabled: false);

            var window = new MainWindow(vm);

            try
            {
                UiThread.ShowOffscreen(window);

                foreach (var page in new[] { AppPage.Devices, AppPage.Monitors, AppPage.Profiles, AppPage.Settings })
                {
                    vm.SelectedPage = page;
                    vm.Devices.SelectedItem = connected;
                    vm.SelectedProfile = vm.Profiles[0];

                    ShowEverything(window);
                }

                // Страница подключённого устройства и устройства, которого сейчас нет
                foreach (var id in new[] { "a", "b" })
                {
                    vm.SelectedDeviceId = id;

                    Assert.Equal(AppPage.Device, vm.SelectedPage);
                    ShowEverything(window);
                }

                // Меню, свёрнутое до значков
                vm.ToggleMenuCommand.Execute(null);
                ShowEverything(window);
                vm.ToggleMenuCommand.Execute(null);
            }
            finally
            {
                window.Close();
                vm.Shutdown();
            }
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void BrokenBindingIsCaught()
    {
        // Проверка самой проверки: привязка к несуществующему свойству должна попасть в ошибки
        var errors = UiThread.InvokeCollectingBindingErrors(() =>
        {
            var text = new TextBlock { DataContext = new PageOption(AppPage.Devices, "Устройства", "") };
            var window = new Window { Content = text, Width = 200, Height = 200 };

            text.SetBinding(TextBlock.TextProperty, "НетТакогоСвойства");

            try
            {
                UiThread.ShowOffscreen(window);
            }
            finally
            {
                window.Close();
            }
        });

        Assert.Contains(errors, e => e.Contains("НетТакогоСвойства", StringComparison.Ordinal));
    }

    [Fact]
    public void DevicesPageHidesDetailsUntilOpened()
    {
        UiThread.Invoke(() =>
        {
            var vm = new MainViewModel(ConfigWithProfile());
            var window = new MainWindow(vm);

            try
            {
                vm.SelectedPage = AppPage.Devices;
                vm.Devices.SelectedItem = vm.Devices.Items[0];
                UiThread.ShowOffscreen(window);

                var page = UiThread.Descendants<DevicesPage>(window).Single();
                var sections = UiThread.Descendants<Expander>(page).ToList();

                Assert.Equal(new[] { "Возможности", "Диагностика" }, sections.Select(s => s.Header as string));
                Assert.All(sections, s => Assert.False(s.IsExpanded));
                Assert.All(sections, s => Assert.False(((FrameworkElement)s.Content).IsVisible));

                sections[1].IsExpanded = true;
                window.UpdateLayout();

                Assert.True(((FrameworkElement)sections[1].Content).IsVisible);
                Assert.False(((FrameworkElement)sections[0].Content).IsVisible);

                // «Забыть» — внизу страницы, после разделов
                var forget = UiThread.Descendants<Button>(page).Single(b => b.Content as string == "Забыть устройство");

                Assert.True(forget.TranslatePoint(new Point(), page).Y > sections[1].TranslatePoint(new Point(), page).Y);
            }
            finally
            {
                window.Close();
                vm.Shutdown();
            }
        });
    }

    [Fact]
    public void DevicesListShowsIconAndStatusDot()
    {
        UiThread.Invoke(() =>
        {
            var vm = new MainViewModel(ConfigWithProfile());
            var window = new MainWindow(vm);

            try
            {
                vm.SetDeviceIcon("a", DeviceIcons.All[3]);
                vm.SelectedPage = AppPage.Devices;
                UiThread.ShowOffscreen(window);

                var page = UiThread.Descendants<DevicesPage>(window).Single();
                var list = UiThread.Descendants<ListBox>(page).First();
                var texts = UiThread.Descendants<TextBlock>(list).Select(t => t.Text).ToList();

                Assert.Contains(DeviceIcons.All[3], texts);
                Assert.Contains(DeviceIcons.Strip, texts);

                // Точка состояния у каждого устройства; состояние словами — только в подсказке
                Assert.Equal(2, UiThread.Descendants<System.Windows.Shapes.Ellipse>(list).Count());
                Assert.DoesNotContain("Не найдено", texts);
            }
            finally
            {
                window.Close();
                vm.Shutdown();
            }
        });
    }

    [Fact]
    public void SettingsKeepGlassSlidersFolded()
    {
        UiThread.Invoke(() =>
        {
            var vm = new MainViewModel(ConfigWithProfile());
            var window = new MainWindow(vm);

            try
            {
                vm.SelectedPage = AppPage.Settings;
                UiThread.ShowOffscreen(window);

                var page = UiThread.Descendants<SettingsPage>(window).Single();
                var fineTuning = Assert.Single(UiThread.Descendants<Expander>(page));

                Assert.Equal("Тонкая настройка", fineTuning.Header);
                Assert.False(fineTuning.IsExpanded);
                Assert.DoesNotContain(UiThread.Descendants<ValueSlider>(page), s => s.Label == "Прозрачность" && s.IsVisible);

                fineTuning.IsExpanded = true;
                window.UpdateLayout();

                Assert.Contains(UiThread.Descendants<ValueSlider>(page), s => s.Label == "Прозрачность" && s.IsVisible);

                // Без найденного обновления плашки «Скачать» не видно
                Assert.DoesNotContain(UiThread.Descendants<Button>(page), b => b.Content as string == "Скачать" && b.IsVisible);
            }
            finally
            {
                window.Close();
                vm.Shutdown();
            }
        });
    }

    [Fact]
    public void TrayMenuShowsDevicesOnline()
    {
        var errors = UiThread.InvokeCollectingBindingErrors(() =>
        {
            var vm = new MainViewModel(ConfigWithProfile());

            vm.Devices.Items.Single(i => i.Device.Id == "a").Device.Connect(new FakeLed("a", "ELK-BLE"), isLightingEnabled: false);
            vm.ApplyProfileCommand.Execute(vm.Profiles[0]);

            var menu = new TrayMenuWindow(vm, () => { }, () => { });

            try
            {
                UiThread.ShowOffscreen(menu);

                var texts = UiThread.Descendants<TextBlock>(menu).Select(t => t.Text).ToList();

                Assert.Contains("Light Space", texts);
                Assert.Contains("На связи 1 из 2", texts);
                Assert.Contains("Нет связи: 1", texts);
                Assert.Contains(UiThread.Descendants<ValueSlider>(menu), s => s.Label == "Общая яркость");

                // Значок солнца у ползунка яркости
                Assert.Contains("", texts);
            }
            finally
            {
                menu.Close();
                vm.Shutdown();
            }
        });

        Assert.Empty(errors);
    }

    [Fact]
    public void PageFadesInWhenShown()
    {
        UiThread.Invoke(() =>
        {
            var page = new Border { Width = 100, Height = 100, Visibility = Visibility.Collapsed };
            var window = new Window { Content = new Grid { Children = { page } }, Width = 200, Height = 200 };

            PageTransition.SetIsEnabled(page, true);

            try
            {
                UiThread.ShowOffscreen(window);

                Assert.False(page.HasAnimatedProperties);

                page.Visibility = Visibility.Visible;

                Assert.IsType<TranslateTransform>(page.RenderTransform);
                Assert.True(page.HasAnimatedProperties);
                Assert.True(page.RenderTransform.HasAnimatedProperties);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PageWithoutTransitionAppearsAtOnce()
    {
        UiThread.Invoke(() =>
        {
            var page = new Border { Width = 100, Height = 100, Visibility = Visibility.Collapsed };
            var window = new Window { Content = new Grid { Children = { page } }, Width = 200, Height = 200 };

            PageTransition.SetIsEnabled(page, true);
            PageTransition.SetIsEnabled(page, false);

            try
            {
                UiThread.ShowOffscreen(window);
                page.Visibility = Visibility.Visible;

                Assert.False(page.HasAnimatedProperties);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void NewTokensAreDefined()
    {
        UiThread.Invoke(() =>
        {
            var resources = Application.Current.Resources;

            foreach (var key in new[]
                     {
                         "MotionFast", "MotionNormal", "MotionSlow", "RadiusTile", "RadiusFlyout", "GapTop12", "GapTop24", "GapTop64",
                         "GapLeft16", "GapBottom12", "GapVertical4", "GapVertical8", "GapHorizontal12", "FieldLabelMargin", "GapTextPair",
                         "PageSubtitleMargin", "GapWrap", "IconLabelGap", "PaddingBanner", "SliderIconMargin", "PaddingCardBody",
                         "EffectPanelMargin", "HeaderIconMargin", "Disclosure"
                     })
                Assert.NotNull(resources[key]);

            // Слой окна скруглён сильнее карточек на нём, быстрое движение короче обычного, обычное — короче медленного
            Assert.True(((CornerRadius)resources["RadiusLayer"]).TopLeft > ((CornerRadius)resources["RadiusCard"]).TopLeft);
            Assert.True(((Duration)resources["MotionFast"]).TimeSpan < ((Duration)resources["MotionNormal"]).TimeSpan);
            Assert.True(((Duration)resources["MotionNormal"]).TimeSpan < ((Duration)resources["MotionSlow"]).TimeSpan);
        });
    }

    /// <summary>
    /// Применяет шаблоны страницы и раскрывает все свёрнутые разделы, чтобы загрузилась и их разметка.
    /// </summary>
    private static void ShowEverything(Window window)
    {
        window.UpdateLayout();

        // Раскрытый раздел может открыть вложенные — проходим, пока закрытых не останется
        for (var pass = 0; pass < 3; pass++)
        {
            var folded = UiThread.Descendants<Expander>(window).Where(e => !e.IsExpanded).ToList();

            if (folded.Count == 0)
                break;

            foreach (var expander in folded)
                expander.IsExpanded = true;

            window.UpdateLayout();
        }
    }

    private static AppConfig ConfigWithProfile()
    {
        var config = new AppConfig();

        config.Devices.Add(new DeviceConfig { Id = "a", Name = "ELK-BLE" });
        config.Devices.Add(new DeviceConfig { Id = "b", Name = "Лента b" });
        config.Normalize();
        config.SaveProfile("Вечер");

        return config;
    }
}
