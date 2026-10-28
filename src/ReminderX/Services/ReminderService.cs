using ReminderX.Models;

namespace ReminderX.Services;

/// <summary>All reminder/todo/history business logic lives here. UI and the scheduler both go through this.</summary>
public class ReminderService
{
    private readonly DataStore _store;

    public ReminderService(DataStore store)
    {
        _store = store;
    }

    public event Action? Changed;

    private void RaiseChanged() => Changed?.Invoke();

    // ---------- Reminders ----------

    public List<Reminder> GetAllReminders() =>
        _store.Read(d => d.Reminders.OrderBy(r => r.DueAt).ToList());

    public Reminder? GetReminder(string id) =>
        _store.Read(d => d.Reminders.FirstOrDefault(r => r.Id == id));

    public Reminder CreateReminder(Reminder reminder)
    {
        reminder.NextRenotifyAt = null;
        _store.Mutate(d => d.Reminders.Add(reminder));
        AddHistory(reminder.Id, reminder.Title, HistoryAction.Created, "");
        RaiseChanged();
        return reminder;
    }

    public void UpdateReminder(Reminder updated)
    {
        _store.Mutate(d =>
        {
            var idx = d.Reminders.FindIndex(r => r.Id == updated.Id);
            if (idx >= 0) d.Reminders[idx] = updated;
        });
        AddHistory(updated.Id, updated.Title, HistoryAction.Edited, "");
        RaiseChanged();
    }

    public void DeleteReminder(string id)
    {
        var title = GetReminder(id)?.Title ?? "";
        _store.Mutate(d => d.Reminders.RemoveAll(r => r.Id == id));
        _store.DeleteAttachmentFolder(id);
        AddHistory(id, title, HistoryAction.Deleted, "");
        RaiseChanged();
    }

    /// <summary>Marks the current occurrence done. For recurring reminders, schedules the next one.</summary>
    public void CompleteReminder(string id)
    {
        _store.Mutate(d =>
        {
            var r = d.Reminders.FirstOrDefault(x => x.Id == id);
            if (r == null) return;

            if (r.Recurrence != null)
            {
                var next = RecurrenceCalculator.GetNext(r.Recurrence, r.DueAt);
                if (next.HasValue)
                {
                    r.DueAt = next.Value;
                    r.State = ReminderState.Pending;
                    r.LastShownAt = null;
                    r.NextRenotifyAt = null;
                }
                else
                {
                    r.State = ReminderState.Completed;
                }
            }
            else
            {
                r.State = ReminderState.Completed;
            }
        });

        var reminder = GetReminder(id);
        AddHistory(id, reminder?.Title ?? "", HistoryAction.Completed, "");
        RaiseChanged();
    }

    public void SnoozeReminder(string id, TimeSpan duration)
    {
        _store.Mutate(d =>
        {
            var r = d.Reminders.FirstOrDefault(x => x.Id == id);
            if (r == null) return;
            r.State = ReminderState.Snoozed;
            r.NextRenotifyAt = DateTime.Now.Add(duration);
        });

        var reminder = GetReminder(id);
        AddHistory(id, reminder?.Title ?? "", HistoryAction.Snoozed, $"for {duration.TotalMinutes:0} min");
        RaiseChanged();
    }

    public void DismissReminder(string id)
    {
        _store.Mutate(d =>
        {
            var r = d.Reminders.FirstOrDefault(x => x.Id == id);
            if (r == null) return;

            if (r.Recurrence != null)
            {
                var next = RecurrenceCalculator.GetNext(r.Recurrence, r.DueAt);
                r.DueAt = next ?? r.DueAt;
                r.State = ReminderState.Pending;
            }
            else
            {
                r.State = ReminderState.Dismissed;
            }
            r.NextRenotifyAt = null;
        });

        var reminder = GetReminder(id);
        AddHistory(id, reminder?.Title ?? "", HistoryAction.Dismissed, "");
        RaiseChanged();
    }

    public void TogglePause(string id, bool paused)
    {
        _store.Mutate(d =>
        {
            var r = d.Reminders.FirstOrDefault(x => x.Id == id);
            if (r != null) r.IsPaused = paused;
        });
        RaiseChanged();
    }

    public void MarkShown(string id, DateTime now)
    {
        _store.Mutate(d =>
        {
            var r = d.Reminders.FirstOrDefault(x => x.Id == id);
            if (r == null) return;
            r.LastShownAt = now;
            r.State = ReminderState.Pending;
            r.NextRenotifyAt = r.RenotifyMinutes > 0
                ? now.AddMinutes(r.RenotifyMinutes)
                : null;
        });
        var reminder = GetReminder(id);
        AddHistory(id, reminder?.Title ?? "", HistoryAction.Shown, "");
        RaiseChanged();
    }

    /// <summary>Reminders due to fire right now: first-time due, or nagging because still unacknowledged.</summary>
    public List<Reminder> GetDueReminders(DateTime now) =>
        _store.Read(d => d.Reminders.Where(r =>
        {
            if (!r.IsActive) return false;
            if (r.LastShownAt == null) return r.DueAt <= now;
            return r.NextRenotifyAt.HasValue && r.NextRenotifyAt.Value <= now;
        }).ToList());

    public string AddAttachment(string reminderId, string sourceFilePath)
    {
        var relative = _store.SaveAttachment(reminderId, sourceFilePath);
        return relative;
    }

    public string GetAttachmentFullPath(Attachment a) => _store.GetAttachmentFullPath(a);

    // ---------- Todos ----------

    public List<TodoItem> GetAllTodos() =>
        _store.Read(d => d.Todos.OrderBy(t => t.IsDone).ThenBy(t => t.DueAt).ToList());

    public TodoItem CreateTodo(TodoItem todo, bool createReminder)
    {
        _store.Mutate(d => d.Todos.Add(todo));

        if (createReminder && todo.DueAt.HasValue)
        {
            var reminder = new Reminder
            {
                Title = todo.Title,
                Description = todo.Notes,
                DueAt = todo.DueAt.Value,
                SourceTodoId = todo.Id
            };
            CreateReminder(reminder);
            todo.LinkedReminderId = reminder.Id;
            _store.Mutate(d =>
            {
                var t = d.Todos.FirstOrDefault(x => x.Id == todo.Id);
                if (t != null) t.LinkedReminderId = reminder.Id;
            });
        }

        RaiseChanged();
        return todo;
    }

    public void CompleteTodo(string id, bool done)
    {
        _store.Mutate(d =>
        {
            var t = d.Todos.FirstOrDefault(x => x.Id == id);
            if (t == null) return;
            t.IsDone = done;
            t.CompletedAt = done ? DateTime.Now : null;
        });

        var todo = _store.Read(d => d.Todos.FirstOrDefault(x => x.Id == id));
        if (done && todo?.LinkedReminderId != null)
        {
            CompleteReminder(todo.LinkedReminderId);
        }

        RaiseChanged();
    }

    public void DeleteTodo(string id)
    {
        var todo = _store.Read(d => d.Todos.FirstOrDefault(x => x.Id == id));
        _store.Mutate(d => d.Todos.RemoveAll(t => t.Id == id));
        if (todo?.LinkedReminderId != null)
        {
            DeleteReminder(todo.LinkedReminderId);
        }
        RaiseChanged();
    }

    // ---------- History ----------

    public List<HistoryEntry> GetHistory() =>
        _store.Read(d => d.History.OrderByDescending(h => h.Timestamp).ToList());

    public void ClearHistory() =>
        _store.Mutate(d => d.History.Clear());

    private void AddHistory(string reminderId, string title, HistoryAction action, string note)
    {
        _store.Mutate(d =>
        {
            d.History.Insert(0, new HistoryEntry
            {
                ReminderId = reminderId,
                Title = title,
                Action = action,
                Note = note
            });

            // keep history bounded
            if (d.History.Count > 2000)
                d.History.RemoveRange(2000, d.History.Count - 2000);
        });
    }

    // ---------- Settings ----------

    public AppSettings GetSettings() => _store.Read(d => d.Settings);

    public void UpdateSettings(Action<AppSettings> mutate)
    {
        _store.Mutate(d => mutate(d.Settings));
        RaiseChanged();
    }
}
