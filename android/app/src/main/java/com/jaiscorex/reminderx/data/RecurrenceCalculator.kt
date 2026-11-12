package com.jaiscorex.reminderx.data

import java.time.LocalDateTime

/** Port of the desktop's Services/RecurrenceCalculator.cs, so both devices schedule the same next occurrence. */
object RecurrenceCalculator {
    /** Next occurrence strictly after [from], or null when past the end date. */
    fun next(rule: Recurrence, from: LocalDateTime): LocalDateTime? {
        val n = rule.interval.coerceAtLeast(1).toLong()
        val next = when (rule.unit) {
            RecurrenceUnit.Seconds -> from.plusSeconds(n)
            RecurrenceUnit.Minutes -> from.plusMinutes(n)
            RecurrenceUnit.Hours -> from.plusHours(n)
            RecurrenceUnit.Days -> from.toLocalDate().plusDays(n).atTime(rule.timeOfDay)
            RecurrenceUnit.Weeks -> nextWeekly(rule, from, n)
            RecurrenceUnit.Months -> {
                val month = from.plusMonths(n)
                val day = minOf(from.dayOfMonth, month.toLocalDate().lengthOfMonth())
                month.toLocalDate().withDayOfMonth(day).atTime(rule.timeOfDay)
            }
        }
        return if (rule.endDate != null && next.isAfter(rule.endDate)) null else next
    }

    private fun nextWeekly(rule: Recurrence, from: LocalDateTime, weeks: Long): LocalDateTime {
        if (rule.daysOfWeek.isEmpty()) return from.toLocalDate().plusWeeks(weeks).atTime(rule.timeOfDay)

        var cursor = from.toLocalDate().plusDays(1)
        repeat((7 * weeks + 7).toInt()) {
            if (cursor.dayOfWeek in rule.daysOfWeek) {
                val candidate = cursor.atTime(rule.timeOfDay)
                if (candidate.isAfter(from)) return candidate
            }
            cursor = cursor.plusDays(1)
        }
        return from.toLocalDate().plusWeeks(weeks).atTime(rule.timeOfDay)
    }
}
