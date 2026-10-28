using System.Windows;
using ReminderX.Models;
using MessageBox = System.Windows.MessageBox;

namespace ReminderX.Views;

public partial class TodoEditorWindow : Window
{
    private static readonly TimeOption[] TimeOptions = TimeOption.BuildQuarterHours();

    public TodoItem? Result { get; private set; }
    public bool CreateReminderRequested { get; private set; }

    public TodoEditorWindow()
    {
        InitializeComponent();
        DueTimeCombo.ItemsSource = TimeOptions;
        DueTimeCombo.ItemTemplate = (DataTemplate)FindResource("DisplayTextItemTemplate");
        DueTimeCombo.SelectedValuePath = "Value";
        DueDatePicker.SelectedDate = DateTime.Today;
        DueTimeCombo.SelectedValue = new TimeSpan(9, 0, 0);

        ContentRendered += (_, _) =>
        {
            if (Services.DiagSnapshot.Dir == null) return;
            HasDueDateCheck.IsChecked = true;
            Dispatcher.BeginInvoke(() =>
            {
                Services.DiagSnapshot.Save((FrameworkElement)Content, "todo-editor", Background);
                Services.Logger.Log("DiagSnapshot: done, exiting test instance");
                Environment.Exit(0);
            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
    }

    private void HasDueDateCheck_Changed(object sender, RoutedEventArgs e)
    {
        DuePanel.Visibility = HasDueDateCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        if (string.IsNullOrEmpty(title))
        {
            MessageBox.Show(this, "Give the to-do a title.", "ReminderX", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DateTime? dueAt = null;
        if (HasDueDateCheck.IsChecked == true)
        {
            var date = DueDatePicker.SelectedDate ?? DateTime.Today;
            var time = (TimeSpan)(DueTimeCombo.SelectedValue ?? TimeSpan.Zero);
            dueAt = date.Date.Add(time);
        }

        Result = new TodoItem
        {
            Title = title,
            Notes = NotesBox.Text.Trim(),
            DueAt = dueAt
        };
        CreateReminderRequested = HasDueDateCheck.IsChecked == true && CreateReminderCheck.IsChecked == true;

        DialogResult = true;
        Close();
    }
}
