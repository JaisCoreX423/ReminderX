using System.Windows;
using System.Windows.Controls;
using ReminderX.Models;
using ReminderX.Services;
using UserControl = System.Windows.Controls.UserControl;
using MessageBox = System.Windows.MessageBox;

namespace ReminderX.Views;

public partial class TodoView : UserControl
{
    public TodoView()
    {
        InitializeComponent();
        AppHost.Reminders.Changed += () => Dispatcher.Invoke(Refresh);
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var items = AppHost.Reminders.GetAllTodos();
        ItemsHost.ItemsSource = items;
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void OpenNewTodoDialog()
    {
        var dialog = new TodoEditorWindow { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            AppHost.Reminders.CreateTodo(dialog.Result, dialog.CreateReminderRequested);
            Refresh();
        }
    }

    private static TodoItem? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as TodoItem;

    private void NewTodo_Click(object sender, RoutedEventArgs e) => OpenNewTodoDialog();

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } t) return;
        AppHost.Reminders.CompleteTodo(t.Id, !t.IsDone);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } t) return;
        var result = MessageBox.Show(Window.GetWindow(this), $"Delete \"{t.Title}\"?", "ReminderX",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            AppHost.Reminders.DeleteTodo(t.Id);
    }
}
