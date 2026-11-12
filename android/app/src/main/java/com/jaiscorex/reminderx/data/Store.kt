package com.jaiscorex.reminderx.data

import android.content.Context
import android.util.Log
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import org.json.JSONObject
import java.io.File
import java.time.LocalDateTime

/**
 * All reminders, to-dos and history, kept in memory as a StateFlow (the UI observes it) and saved
 * to data.json in the app's private storage after every change. Same operations as the desktop's
 * ReminderService.
 */
object Store {
    private const val MaxHistory = 500
    private lateinit var file: File
    private val lock = Any()
    private val _data = MutableStateFlow(AppData())
    val data: StateFlow<AppData> = _data

    /** Called after every change: alarms reschedule, sync pushes. [fromSync] is true for changes pulled from other devices. */
    val listeners = mutableListOf<(fromSync: Boolean) -> Unit>()

    fun init(context: Context) {
        file = File(context.filesDir, "data.json")
        _data.value = try {
            if (file.exists()) RecordJson.appDataFrom(JSONObject(file.readText())) else AppData()
        } catch (e: Exception) {
            Log.e("ReminderX", "Couldn't read data.json", e)
            file.copyTo(File(context.filesDir, "data.broken.json"), overwrite = true)
            AppData()
        }
    }

    fun update(fromSync: Boolean = false, mutate: (AppData) -> AppData) {
        synchronized(lock) {
            val next = mutate(_data.value)
            if (next == _data.value) return
            _data.value = next.copy(history = next.history.take(MaxHistory))
            val temp = File(file.parentFile, "data.json.tmp")
            temp.writeText(RecordJson.appData(_data.value).toString())
            temp.renameTo(file)
        }
        listeners.forEach { runCatching { it(fromSync) } }
    }

    private fun AppData.log(id: String, title: String, action: HistoryAction, note: String = "") =
        copy(history = listOf(HistoryEntry(reminderId = id, title = title, action = action, note = note)) + history)

    private fun AppData.mapReminder(id: String, f: (Reminder) -> Reminder) =
        copy(reminders = reminders.map { if (it.id == id) f(it) else it })

    fun reminder(id: String): Reminder? = data.value.reminders.firstOrNull { it.id == id }

    // ---------- Reminders ----------

    fun saveReminder(r: Reminder) = update { d ->
        val existing = d.reminders.firstOrNull { it.id == r.id }
        if (existing == null) {
            d.copy(reminders = d.reminders + r.copy(nextRenotifyAt = null)).log(r.id, r.title, HistoryAction.Created)
        } else {
            // A changed time or rule means a fresh alert.
            val rescheduled = existing.dueAt != r.dueAt || existing.recurrence != r.recurrence
            val updated = if (rescheduled) r.copy(lastShownAt = null, nextRenotifyAt = null,
                state = if (r.state == ReminderState.Completed || r.state == ReminderState.Dismissed) ReminderState.Pending else r.state) else r
            d.mapReminder(r.id) { updated }.log(r.id, r.title, HistoryAction.Edited)
        }
    }

    fun deleteReminder(id: String) = update { d ->
        val r = d.reminders.firstOrNull { it.id == id } ?: return@update d
        d.copy(reminders = d.reminders.filter { it.id != id }).log(id, r.title, HistoryAction.Deleted)
    }

    fun complete(id: String) = update { d ->
        val r = d.reminders.firstOrNull { it.id == id } ?: return@update d
        val next = r.recurrence?.let { RecurrenceCalculator.next(it, r.dueAt) }
        val done = if (next != null) r.copy(dueAt = next, state = ReminderState.Pending, lastShownAt = null, nextRenotifyAt = null)
        else r.copy(state = ReminderState.Completed)
        d.mapReminder(id) { done }.log(id, r.title, HistoryAction.Completed)
    }

    fun snooze(id: String, minutes: Long = 10) = update { d ->
        val r = d.reminders.firstOrNull { it.id == id } ?: return@update d
        d.mapReminder(id) {
            it.copy(state = ReminderState.Snoozed, nextRenotifyAt = LocalDateTime.now().plusMinutes(minutes),
                lastShownAt = it.lastShownAt ?: LocalDateTime.now())
        }.log(id, r.title, HistoryAction.Snoozed, "for $minutes min")
    }

    fun dismiss(id: String) = update { d ->
        val r = d.reminders.firstOrNull { it.id == id } ?: return@update d
        val next = r.recurrence?.let { RecurrenceCalculator.next(it, r.dueAt) }
        d.mapReminder(id) {
            if (r.recurrence != null) it.copy(dueAt = next ?: it.dueAt, state = ReminderState.Pending, nextRenotifyAt = null, lastShownAt = null)
            else it.copy(state = ReminderState.Dismissed, nextRenotifyAt = null)
        }.log(id, r.title, HistoryAction.Dismissed)
    }

    fun togglePause(id: String) = update { d -> d.mapReminder(id) { it.copy(isPaused = !it.isPaused) } }

    fun markShown(id: String, now: LocalDateTime) = update { d ->
        val r = d.reminders.firstOrNull { it.id == id } ?: return@update d
        d.mapReminder(id) {
            it.copy(lastShownAt = now, state = ReminderState.Pending,
                nextRenotifyAt = if (it.renotifyMinutes > 0) now.plusMinutes(it.renotifyMinutes.toLong()) else null)
        }.log(id, r.title, HistoryAction.Shown)
    }

    fun dueReminders(now: LocalDateTime): List<Reminder> =
        data.value.reminders.filter { r -> r.nextAlertAt?.let { !it.isAfter(now) } == true }

    // ---------- To-dos ----------

    fun saveTodo(todo: Todo, createReminder: Boolean) = update { d ->
        val existing = d.todos.firstOrNull { it.id == todo.id }
        var result = if (existing == null) d.copy(todos = d.todos + todo) else d.copy(todos = d.todos.map { if (it.id == todo.id) todo else it })
        if (existing == null && createReminder && todo.dueAt != null) {
            val reminder = Reminder(title = todo.title, description = todo.notes, dueAt = todo.dueAt, sourceTodoId = todo.id)
            result = result.copy(
                reminders = result.reminders + reminder,
                todos = result.todos.map { if (it.id == todo.id) it.copy(linkedReminderId = reminder.id) else it },
            ).log(reminder.id, reminder.title, HistoryAction.Created, "from a to-do")
        }
        result
    }

    fun setTodoDone(id: String, done: Boolean) = update { d ->
        val todo = d.todos.firstOrNull { it.id == id } ?: return@update d
        val updated = d.copy(todos = d.todos.map {
            if (it.id == id) it.copy(isDone = done, completedAt = if (done) LocalDateTime.now() else null) else it
        })
        // Finishing a to-do finishes the reminder it created, like on the desktop.
        val linked = todo.linkedReminderId
        if (done && linked != null) updated.mapReminder(linked) { it.copy(state = ReminderState.Completed) } else updated
    }

    fun deleteTodo(id: String) = update { d -> d.copy(todos = d.todos.filter { it.id != id }) }

    fun clearHistory() = update { d -> d.copy(history = emptyList()) }

    // ---------- Sync ----------

    /** Every record as its sync JSON (for change detection). */
    fun syncSnapshot(): Pair<List<Pair<String, String>>, List<Pair<String, String>>> {
        val d = data.value
        return d.reminders.map { it.id to RecordJson.reminder(it).toString() } to
            d.todos.map { it.id to RecordJson.todo(it).toString() }
    }

    /** Applies records pulled from other devices in one change. Returns each record's local JSON (null = deleted). */
    fun applySynced(reminders: Map<String, JSONObject?>, todos: Map<String, JSONObject?>): Map<String, String?> {
        val result = mutableMapOf<String, String?>()
        update(fromSync = true) { start ->
            var d = start
            for ((id, record) in reminders) {
                val existing = d.reminders.firstOrNull { it.id == id }
                if (record == null) {
                    if (existing != null) d = d.copy(reminders = d.reminders.filter { it.id != id })
                        .log(id, existing.title, HistoryAction.Deleted, "on another device")
                    result[id] = null
                } else {
                    val r = RecordJson.reminderFrom(id, record, existing)
                    d = (if (existing == null) d.copy(reminders = d.reminders + r) else d.mapReminder(id) { r })
                        .log(id, r.title, if (existing == null) HistoryAction.Created else HistoryAction.Edited, "on another device")
                    result[id] = RecordJson.reminder(r).toString()
                }
            }
            for ((id, record) in todos) {
                if (record == null) {
                    d = d.copy(todos = d.todos.filter { it.id != id })
                    result[id] = null
                } else {
                    val t = RecordJson.todoFrom(id, record)
                    d = if (d.todos.any { it.id == id }) d.copy(todos = d.todos.map { if (it.id == id) t else it }) else d.copy(todos = d.todos + t)
                    result[id] = RecordJson.todo(t).toString()
                }
            }
            d
        }
        return result
    }
}
