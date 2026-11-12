package com.jaiscorex.reminderx.data

import org.json.JSONArray
import org.json.JSONObject
import java.time.DayOfWeek
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter

/**
 * Sync record format, schema v1 — the same contract as the desktop's Services/Sync/SyncRecords.cs:
 * camelCase, ISO 8601 dates with offset, timeOfDay "HH:mm:ss", daysOfWeek 0 = Sunday … 6 = Saturday,
 * enum names as strings. lastShownAt and attachments are device-local and never sync.
 */
object RecordJson {
    private val iso: DateTimeFormatter = DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm:ss.SSSXXX")

    fun toIso(value: LocalDateTime): String = value.atZone(ZoneId.systemDefault()).format(iso)

    fun parseIso(value: String): LocalDateTime =
        OffsetDateTime.parse(value).atZoneSameInstant(ZoneId.systemDefault()).toLocalDateTime()

    private fun JSONObject.str(name: String): String? = if (!has(name) || isNull(name)) null else getString(name)
    private fun JSONObject.date(name: String): LocalDateTime? = str(name)?.let(::parseIso)
    private fun nullable(value: Any?): Any = value ?: JSONObject.NULL

    fun reminder(r: Reminder): JSONObject = JSONObject()
        .put("v", 1)
        .put("title", r.title)
        .put("description", r.description)
        .put("dueAt", toIso(r.dueAt))
        .put("recurrence", nullable(r.recurrence?.let(::recurrence)))
        .put("state", r.state.name)
        .put("createdAt", toIso(r.createdAt))
        .put("nextRenotifyAt", nullable(r.nextRenotifyAt?.let(::toIso)))
        .put("renotifyMinutes", r.renotifyMinutes)
        .put("isPaused", r.isPaused)
        .put("sourceTodoId", nullable(r.sourceTodoId))

    /** Applies a synced record, keeping this phone's own alert state when the reminder already exists. */
    fun reminderFrom(id: String, o: JSONObject, existing: Reminder?): Reminder = Reminder(
        id = id,
        title = o.str("title") ?: "",
        description = o.str("description") ?: "",
        dueAt = o.date("dueAt") ?: LocalDateTime.now(),
        recurrence = if (o.isNull("recurrence") || !o.has("recurrence")) null else recurrenceFrom(o.getJSONObject("recurrence")),
        state = enumOr(o.str("state"), ReminderState.Pending),
        createdAt = o.date("createdAt") ?: LocalDateTime.now(),
        nextRenotifyAt = o.date("nextRenotifyAt"),
        renotifyMinutes = o.optInt("renotifyMinutes", 10),
        isPaused = o.optBoolean("isPaused", false),
        sourceTodoId = o.str("sourceTodoId"),
        // A new due time must alert again here; otherwise keep what this phone already showed.
        lastShownAt = if (existing != null && existing.dueAt == (o.date("dueAt") ?: existing.dueAt)) existing.lastShownAt else null,
    )

    fun recurrence(r: Recurrence): JSONObject = JSONObject()
        .put("unit", r.unit.name)
        .put("interval", r.interval)
        .put("timeOfDay", r.timeOfDay.toString().let { if (it.length == 5) "$it:00" else it.take(8) })
        .put("daysOfWeek", JSONArray(r.daysOfWeek.map { it.value % 7 }.sorted()))
        .put("endDate", nullable(r.endDate?.let(::toIso)))

    fun recurrenceFrom(o: JSONObject): Recurrence {
        val days = o.optJSONArray("daysOfWeek")
        return Recurrence(
            unit = enumOr(o.str("unit"), RecurrenceUnit.Days),
            interval = o.optInt("interval", 1).coerceAtLeast(1),
            timeOfDay = runCatching { LocalTime.parse(o.str("timeOfDay") ?: "09:00:00") }.getOrDefault(LocalTime.of(9, 0)),
            daysOfWeek = if (days == null) emptySet() else (0 until days.length())
                .map { days.getInt(it) }
                .filter { it in 0..6 }
                .map { if (it == 0) DayOfWeek.SUNDAY else DayOfWeek.of(it) }
                .toSet(),
            endDate = o.date("endDate"),
        )
    }

    fun todo(t: Todo): JSONObject = JSONObject()
        .put("v", 1)
        .put("title", t.title)
        .put("notes", t.notes)
        .put("dueAt", nullable(t.dueAt?.let(::toIso)))
        .put("isDone", t.isDone)
        .put("createdAt", toIso(t.createdAt))
        .put("completedAt", nullable(t.completedAt?.let(::toIso)))
        .put("linkedReminderId", nullable(t.linkedReminderId))

    fun todoFrom(id: String, o: JSONObject): Todo = Todo(
        id = id,
        title = o.str("title") ?: "",
        notes = o.str("notes") ?: "",
        dueAt = o.date("dueAt"),
        isDone = o.optBoolean("isDone", false),
        createdAt = o.date("createdAt") ?: LocalDateTime.now(),
        completedAt = o.date("completedAt"),
        linkedReminderId = o.str("linkedReminderId"),
    )

    // ---------- Local storage (data.json in the app's private files) ----------

    fun appData(d: AppData): JSONObject = JSONObject()
        .put("reminders", JSONArray(d.reminders.map { r ->
            reminder(r).put("id", r.id).put("lastShownAt", nullable(r.lastShownAt?.let(::toIso)))
        }))
        .put("todos", JSONArray(d.todos.map { todo(it).put("id", it.id) }))
        .put("history", JSONArray(d.history.map {
            JSONObject().put("at", toIso(it.at)).put("reminderId", it.reminderId).put("title", it.title)
                .put("action", it.action.name).put("note", it.note)
        }))

    fun appDataFrom(o: JSONObject): AppData {
        fun objects(name: String): List<JSONObject> =
            o.optJSONArray(name)?.let { a -> (0 until a.length()).map { a.getJSONObject(it) } } ?: emptyList()

        return AppData(
            reminders = objects("reminders").map { r ->
                reminderFrom(r.getString("id"), r, null).copy(lastShownAt = r.date("lastShownAt"))
            },
            todos = objects("todos").map { todoFrom(it.getString("id"), it) },
            history = objects("history").map {
                HistoryEntry(
                    at = it.date("at") ?: LocalDateTime.now(),
                    reminderId = it.str("reminderId") ?: "",
                    title = it.str("title") ?: "",
                    action = enumOr(it.str("action"), HistoryAction.Edited),
                    note = it.str("note") ?: "",
                )
            },
        )
    }

    private inline fun <reified T : Enum<T>> enumOr(name: String?, fallback: T): T =
        enumValues<T>().firstOrNull { it.name == name } ?: fallback
}
