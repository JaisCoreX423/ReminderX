using System.Windows;

namespace ReminderX;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        NavReminders.IsChecked = true;
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => Hide();

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // The window never really "closes" from the user's perspective — the app keeps
        // running in the tray until Exit is chosen from the tray menu.
        e.Cancel = true;
        Hide();
    }

    private void HideAllPanels()
    {
        if (RemindersPanel == null) return; // guards against events firing mid-InitializeComponent
        RemindersPanel.Visibility = Visibility.Collapsed;
        TodoPanel.Visibility = Visibility.Collapsed;
        HistoryPanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
    }

    private void NavReminders_Checked(object sender, RoutedEventArgs e)
    {
        HideAllPanels();
        RemindersPanel.Visibility = Visibility.Visible;
        RemindersPanel.Refresh();
    }

    private void NavTodo_Checked(object sender, RoutedEventArgs e)
    {
        HideAllPanels();
        TodoPanel.Visibility = Visibility.Visible;
        TodoPanel.Refresh();
    }

    private void NavHistory_Checked(object sender, RoutedEventArgs e)
    {
        HideAllPanels();
        HistoryPanel.Visibility = Visibility.Visible;
        HistoryPanel.Refresh();
    }

    private void NavSettings_Checked(object sender, RoutedEventArgs e)
    {
        HideAllPanels();
        SettingsPanel.Visibility = Visibility.Visible;
        SettingsPanel.Refresh();
    }

    public void ShowSettings() => NavSettings.IsChecked = true;

    public void RequestNewReminder()
    {
        NavReminders.IsChecked = true;
        RemindersPanel.OpenNewReminderDialog();
    }

    public void RequestNewTodo()
    {
        NavTodo.IsChecked = true;
        TodoPanel.OpenNewTodoDialog();
    }

    public void FocusReminder(string reminderId)
    {
        NavReminders.IsChecked = true;
        RemindersPanel.Refresh();
        RemindersPanel.OpenEditDialogFor(reminderId);
    }
}
