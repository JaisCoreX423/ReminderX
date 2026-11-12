package com.jaiscorex.reminderx.data

import java.time.DayOfWeek
import java.time.LocalDateTime
import java.time.LocalTime
import java.util.UUID

enum class RecurrenceUnit { Seconds, Minutes, Hours, Days, Weeks, Months }

enum class ReminderState { Pending, Snoozed, Completed, Dismissed }

enum class HistoryAction { Created, Shown, Completed, Snoozed, Dismissed, Edited, Deleted, Rescheduled }

fun newId(): String = UUID.randomUUID().toString().replace("-", "")

data class Recurrence(
    val unit: RecurrenceUnit = RecurrenceUnit.Days,
    val interval: Int = 1,
    val timeOfDay: LocalTime = LocalTime.of(9, 0),
    val daysOfWeek: Set<DayOfWeek> = emptySet(),
    val endDate: LocalDateTime? = null,
) {
    fun describe(): String {
        val time = Format.time(timeOfDay)
        val n = interval.coerceAtLeast(1)
        return when (unit) {
            RecurrenceUnit.Seconds -> if (n == 1) "Every second" else "Every $n seconds"
            RecurrenceUnit.Minutes -> if (n == 1) "Every minute" else "Every $n minutes"
            RecurrenceUnit.Hours -> if (n == 1) "Every hour" else "Every $n hours"
            RecurrenceUnit.Days -> (if (n == 1) "Every day" else "Every $n days") + " at $time"
            RecurrenceUnit.Weeks -> {
                val days = if (daysOfWeek.isEmpty()) "" else " on " +
                    daysOfWeek.sortedBy { it.value % 7 }.joinToString(", ") { Format.shortDay(it) }
                (if (n == 1) "Every week" else "Every $n weeks") + days + " at $time"
            }
            RecurrenceUnit.Months -> (if (n == 1) "Every month" else "Every $n months") + " at $time"
        }
    }
}

data class Reminder(
    val id: String = newId(),
    val title: String = "",
    val description: String = "",
    val dueAt: LocalDateTime = LocalDateTime.now().plusHours(1),
    val recurrence: Recurrence? = null,
    val state: ReminderState = ReminderState.Pending,
    val createdAt: LocalDateTime = LocalDateTime.now(),
    val nextRenotifyAt: LocalDateTime? = null,
    val renotifyMinutes: Int = 10,
    val isPaused: Boolean = false,
    val sourceTodoId: String? = null,
    /** When this phone last showed it. Device-local: not synced. */
    val lastShownAt: LocalDateTime? = null,
) {
    val isActive: Boolean get() = !isPaused && (state == ReminderState.Pending || state == ReminderState.Snoozed)

    /** Next moment this reminder needs an alert on this phone, or null. */
    val nextAlertAt: LocalDateTime?
        get() = when {
            !isActive -> null
            lastShownAt == null -> dueAt
            else -> nextRenotifyAt
        }
}

data class Todo(
    val id: String = newId(),
    val title: String = "",
    val notes: String = "",
    val dueAt: LocalDateTime? = null,
    val isDone: Boolean = false,
    val createdAt: LocalDateTime = LocalDateTime.now(),
    val completedAt: LocalDateTime? = null,
    val linkedReminderId: String? = null,
)

data class HistoryEntry(
    val at: LocalDateTime = LocalDateTime.now(),
    val reminderId: String,
    val title: String,
    val action: HistoryAction,
    val note: String = "",
)

data class AppData(
    val reminders: List<Reminder> = emptyList(),
    val todos: List<Todo> = emptyList(),
    val history: List<HistoryEntry> = emptyList(),
)
