using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace LightSpace.App.Controls;

/// <summary>
/// Выпадающая панель-карточка с поиском для длинных списков: приложения, exe-файлы.
/// Варианты показываются и ищутся по их тексту (ToString или DisplayMemberPath).
/// </summary>
public partial class SearchPicker : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(SearchPicker), new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(
        nameof(SelectedItem), typeof(object), typeof(SearchPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(SearchPicker), new PropertyMetadata("Выберите", OnSelectedItemChanged));

    public static readonly DependencyProperty DisplayMemberPathProperty = DependencyProperty.Register(
        nameof(DisplayMemberPath), typeof(string), typeof(SearchPicker), new PropertyMetadata(null, OnDisplayMemberPathChanged));

    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(SearchPicker), new PropertyMetadata(null, OnItemTemplateChanged));

    public static readonly DependencyProperty OpenCommandProperty = DependencyProperty.Register(
        nameof(OpenCommand), typeof(ICommand), typeof(SearchPicker), new PropertyMetadata(null));

    private ICollectionView? _view;

    // Пока список заполняется или выделение ставится из кода, выбор не считается действием пользователя
    private bool _isSyncing;

    public SearchPicker()
    {
        InitializeComponent();

        UpdateSelectionText();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Текст на кнопке, пока ничего не выбрано.
    /// </summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>
    /// Свойство варианта, которое показывается и по которому идёт поиск; если не задано — ToString().
    /// </summary>
    public string? DisplayMemberPath
    {
        get => (string?)GetValue(DisplayMemberPathProperty);
        set => SetValue(DisplayMemberPathProperty, value);
    }

    /// <summary>
    /// Как выглядит вариант в списке и выбранный вариант на кнопке; если не задан — текстом варианта.
    /// Поиск всё равно идёт по тексту варианта.
    /// </summary>
    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>
    /// Команда, которая выполняется при каждом открытии панели, например чтобы обновить список вариантов.
    /// </summary>
    public ICommand? OpenCommand
    {
        get => (ICommand?)GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (SearchPicker)d;

        // Своё представление списка: фильтр поиска не должен задевать другие списки на тех же данных
        picker._view = e.NewValue is IEnumerable items ? new CollectionViewSource { Source = items }.View : null;

        if (picker._view is not null)
            picker._view.Filter = picker.Matches;

        picker._isSyncing = true;
        picker.Items.ItemsSource = picker._view;
        picker.Items.SelectedItem = picker.SelectedItem;
        picker._isSyncing = false;
    }

    private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (SearchPicker)d;

        picker._isSyncing = true;
        picker.Items.SelectedItem = picker.SelectedItem;
        picker._isSyncing = false;

        picker.UpdateSelectionText();
    }

    private static void OnDisplayMemberPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (SearchPicker)d;

        // Список не может одновременно показывать варианты шаблоном и текстом свойства
        if (picker.ItemTemplate is null)
            picker.Items.DisplayMemberPath = e.NewValue as string;

        picker.UpdateSelectionText();
    }

    private static void OnItemTemplateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (SearchPicker)d;
        var template = e.NewValue as DataTemplate;

        // Сначала убираем текст свойства, потом ставим шаблон: вместе их список не принимает
        picker.Items.DisplayMemberPath = template is null ? picker.DisplayMemberPath : null;
        picker.Items.ItemTemplate = template;
        picker.SelectionContent.ContentTemplate = template;

        picker.UpdateSelectionText();
    }

    private void OnFlyoutOpened(object? sender, EventArgs e)
    {
        if (OpenCommand?.CanExecute(null) == true)
            OpenCommand.Execute(null);

        Search.Text = "";
        Search.Focus();
        Keyboard.Focus(Search);

        if (Items.SelectedItem is not null)
            Items.ScrollIntoView(Items.SelectedItem);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();

        NothingFound.Visibility = _view is not null && _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Enter выбирает первый найденный вариант, Esc закрывает панель, стрелка вниз переводит фокус в список.
    /// </summary>
    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                var first = _view?.Cast<object>().FirstOrDefault();
                if (first is not null)
                    Choose(first);

                e.Handled = true;
                break;

            case Key.Escape:
                Opener.IsChecked = false;
                e.Handled = true;
                break;

            case Key.Down when Items.Items.Count > 0:
                Items.SelectedIndex = Math.Max(Items.SelectedIndex, 0);
                (Items.ItemContainerGenerator.ContainerFromIndex(Items.SelectedIndex) as UIElement)?.Focus();
                e.Handled = true;
                break;
        }
    }

    private void OnItemSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncing || Items.SelectedItem is null)
            return;

        // Перемещение стрелками по списку не закрывает панель, клик мышью — закрывает
        if (Keyboard.FocusedElement is ListBoxItem && Mouse.LeftButton != MouseButtonState.Pressed)
            return;

        Choose(Items.SelectedItem);
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Items.SelectedItem is not null)
        {
            Choose(Items.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Opener.IsChecked = false;
            e.Handled = true;
        }
    }

    private void Choose(object item)
    {
        SetCurrentValue(SelectedItemProperty, item);
        Opener.IsChecked = false;
    }

    private bool Matches(object item)
    {
        var query = Search.Text.Trim();

        return query.Length == 0 || TextOf(item).Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void UpdateSelectionText()
    {
        // До InitializeComponent элементов разметки ещё нет
        if (SelectionText is null)
            return;

        var hasSelection = SelectedItem is not null;
        var isTemplated = hasSelection && ItemTemplate is not null;

        SelectionText.Text = hasSelection ? TextOf(SelectedItem!) : Placeholder;
        SelectionText.SetResourceReference(TextBlock.ForegroundProperty, hasSelection ? "TextBrush" : "TextTertiaryBrush");

        SelectionContent.Content = isTemplated ? SelectedItem : null;
        SelectionContent.Visibility = isTemplated ? Visibility.Visible : Visibility.Collapsed;
        SelectionText.Visibility = isTemplated ? Visibility.Collapsed : Visibility.Visible;
    }

    private string TextOf(object item)
    {
        if (string.IsNullOrEmpty(DisplayMemberPath))
            return item.ToString() ?? "";

        var property = TypeDescriptor.GetProperties(item).Find(DisplayMemberPath, ignoreCase: false);

        return property?.GetValue(item)?.ToString() ?? "";
    }
}
