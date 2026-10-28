using System.Windows;
using System.Windows.Controls;
using ReminderX.Services;
using UserControl = System.Windows.Controls.UserControl;
using MessageBox = System.Windows.MessageBox;

namespace ReminderX.Views;

public partial class HistoryView : UserControl
{
    public HistoryView()
    {
        InitializeComponent();
        AppHost.Reminders.Changed += () => Dispatcher.Invoke(Refresh);
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var items = AppHost.Reminders.GetHistory();
        ItemsHost.ItemsSource = items;
        EmptyState.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(Window.GetWindow(this), "Clear all history?", "ReminderX",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            AppHost.Reminders.ClearHistory();
            Refresh();
        }
    }
}
