package com.jaiscorex.reminderx.data

import java.time.DayOfWeek
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.format.DateTimeFormatter
import java.time.format.TextStyle
import java.util.Locale

/** Same wording as the desktop: "Today, 3:00 PM", "Tomorrow, 9:00 AM", "Nov 3, 9:00 AM". */
object Format {
    private val timeFmt = DateTimeFormatter.ofPattern("h:mm a", Locale.US)
    private val dayFmt = DateTimeFormatter.ofPattern("MMM d", Locale.US)
    private val dayYearFmt = DateTimeFormatter.ofPattern("MMM d, yyyy", Locale.US)
    private val fullFmt = DateTimeFormatter.ofPattern("EEE, MMM d 'at' h:mm a", Locale.US)

    fun time(t: LocalTime): String = t.format(timeFmt)

    fun shortDay(d: DayOfWeek): String = d.getDisplayName(TextStyle.SHORT, Locale.US)

    fun day(d: LocalDate): String {
        val today = LocalDate.now()
        return when (d) {
            today -> "Today"
            today.plusDays(1) -> "Tomorrow"
            today.minusDays(1) -> "Yesterday"
            else -> d.format(if (d.year == today.year) dayFmt else dayYearFmt)
        }
    }

    fun friendly(at: LocalDateTime): String = "${day(at.toLocalDate())}, ${time(at.toLocalTime())}"

    fun full(at: LocalDateTime): String = at.format(fullFmt)
}
