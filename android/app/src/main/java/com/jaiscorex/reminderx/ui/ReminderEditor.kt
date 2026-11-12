package com.jaiscorex.reminderx.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DatePicker
import androidx.compose.material3.DatePickerDialog
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TimePicker
import androidx.compose.material3.rememberDatePickerState
import androidx.compose.material3.rememberTimePickerState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.jaiscorex.reminderx.data.Format
import com.jaiscorex.reminderx.data.Recurrence
import com.jaiscorex.reminderx.data.RecurrenceUnit
import com.jaiscorex.reminderx.data.Reminder
import com.jaiscorex.reminderx.data.Store
import java.time.DayOfWeek
import java.time.Instant
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.LocalTime
import java.time.ZoneOffset

private enum class Kind { OneTime, Recurring, Timer }

/** Same three kinds as the desktop editor: one-time, recurring and timer. */
@Composable
fun ReminderEditor(original: Reminder, isNew: Boolean, onClose: () -> Unit) {
    var title by remember { mutableStateOf(original.title) }
    var description by remember { mutableStateOf(original.description) }
    var kind by remember { mutableStateOf(if (original.recurrence != null) Kind.Recurring else Kind.OneTime) }
    var date by remember { mutableStateOf(original.dueAt.toLocalDate()) }
    var time by remember { mutableStateOf(original.dueAt.toLocalTime().withSecond(0).withNano(0)) }
    var unit by remember { mutableStateOf(original.recurrence?.unit ?: RecurrenceUnit.Days) }
    var interval by remember { mutableStateOf((original.recurrence?.interval ?: 1).toString()) }
    var days by remember { mutableStateOf(original.recurrence?.daysOfWeek ?: emptySet()) }
    var timer by remember { mutableStateOf(listOf("0", "0", "0", "30", "0")) }
    var renotify by remember { mutableStateOf(original.renotifyMinutes.toString()) }
    var error by remember { mutableStateOf<String?>(null) }

    fun timerDuration(): Long {
        val n = timer.map { it.toLongOrNull() ?: 0 }
        return n[0] * 604_800 + n[1] * 86_400 + n[2] * 3_600 + n[3] * 60 + n[4]
    }

    fun save() {
        if (title.isBlank()) { error = "Give the reminder a title."; return }
        val dueAt: LocalDateTime
        var recurrence: Recurrence? = null
        when (kind) {
            Kind.OneTime -> dueAt = date.atTime(time)
            Kind.Recurring -> {
                val n = interval.toIntOrNull()?.takeIf { it > 0 } ?: run { error = "Enter how often it repeats."; return }
                recurrence = Recurrence(unit = unit, interval = n, timeOfDay = time, daysOfWeek = if (unit == RecurrenceUnit.Weeks) days else emptySet())
                dueAt = if (unit in listOf(RecurrenceUnit.Seconds, RecurrenceUnit.Minutes, RecurrenceUnit.Hours))
                    LocalDateTime.now().plusSeconds(1).withNano(0) else date.atTime(time)
            }
            Kind.Timer -> {
                val seconds = timerDuration()
                if (seconds <= 0) { error = "Set how long the timer runs."; return }
                dueAt = LocalDateTime.now().plusSeconds(seconds).withNano(0)
            }
        }
        Store.saveReminder(
            original.copy(
                title = title.trim(), description = description.trim(), dueAt = dueAt, recurrence = recurrence,
                renotifyMinutes = renotify.toIntOrNull()?.coerceAtLeast(0) ?: 10,
            ),
        )
        onClose()
    }

    Column(
        Modifier.fillMaxSize().background(MaterialTheme.colorScheme.background).imePadding()
            .verticalScroll(rememberScrollState()).padding(20.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Text(if (isNew) "New Reminder" else "Edit Reminder", fontSize = 26.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onBackground)

        OutlinedTextField(title, { title = it }, label = { Text("Title") }, singleLine = true, modifier = Modifier.fillMaxWidth())
        OutlinedTextField(description, { description = it }, label = { Text("Description") }, minLines = 2, modifier = Modifier.fillMaxWidth())

        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            FilterChip(kind == Kind.OneTime, { kind = Kind.OneTime }, { Text("One-time") })
            FilterChip(kind == Kind.Recurring, { kind = Kind.Recurring }, { Text("Recurring") })
            FilterChip(kind == Kind.Timer, { kind = Kind.Timer }, { Text("Timer") })
        }

        when (kind) {
            Kind.OneTime -> Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                DateField(date, { date = it }, Modifier.weight(1f))
                TimeField(time, { time = it }, Modifier.weight(1f))
            }
            Kind.Recurring -> {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("Every", color = MaterialTheme.colorScheme.onBackground)
                    Spacer(Modifier.width(10.dp))
                    OutlinedTextField(interval, { interval = it.filter(Char::isDigit).take(4) }, singleLine = true,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number), modifier = Modifier.width(90.dp))
                }
                Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    for (u in RecurrenceUnit.entries) FilterChip(unit == u, { unit = u }, { Text(u.name.lowercase()) })
                }
                if (unit == RecurrenceUnit.Weeks) {
                    Row(Modifier.horizontalScroll(rememberScrollState()), horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                        for (d in listOf(DayOfWeek.SUNDAY) + DayOfWeek.entries.filter { it != DayOfWeek.SUNDAY }) {
                            FilterChip(d in days, { days = if (d in days) days - d else days + d }, { Text(Format.shortDay(d).take(2)) })
                        }
                    }
                }
                if (unit in listOf(RecurrenceUnit.Days, RecurrenceUnit.Weeks, RecurrenceUnit.Months)) {
                    Caption("Starts")
                    Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                        DateField(date, { date = it }, Modifier.weight(1f))
                        TimeField(time, { time = it }, Modifier.weight(1f))
                    }
                }
            }
            Kind.Timer -> {
                Caption("Goes off in")
                Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                    listOf("weeks", "days", "hours", "min", "sec").forEachIndexed { i, label ->
                        OutlinedTextField(
                            timer[i], { v -> timer = timer.toMutableList().also { it[i] = v.filter(Char::isDigit).take(4) } },
                            label = { Text(label, fontSize = 11.sp) }, singleLine = true,
                            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number), modifier = Modifier.weight(1f),
                        )
                    }
                }
                Caption("Goes off ${Format.full(LocalDateTime.now().plusSeconds(timerDuration()))}")
            }
        }

        OutlinedTextField(
            renotify, { renotify = it.filter(Char::isDigit).take(4) }, singleLine = true,
            label = { Text("Re-notify if ignored (minutes, 0 = off)") },
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number), modifier = Modifier.fillMaxWidth(),
        )

        error?.let { Text(it, color = Danger) }

        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            OutlinedButton(onClick = onClose, modifier = Modifier.weight(1f).height(48.dp), shape = RoundedCornerShape(12.dp)) { Text("Cancel") }
            Button(
                onClick = { save() }, modifier = Modifier.weight(1f).height(48.dp), shape = RoundedCornerShape(12.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Accent),
            ) { Text("Save") }
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun DateField(value: LocalDate, onChange: (LocalDate) -> Unit, modifier: Modifier = Modifier) {
    var open by remember { mutableStateOf(false) }
    OutlinedButton(onClick = { open = true }, modifier = modifier.height(52.dp), shape = RoundedCornerShape(12.dp)) {
        Text(Format.day(value), color = MaterialTheme.colorScheme.onSurface)
    }
    if (open) {
        val state = rememberDatePickerState(initialSelectedDateMillis = value.atStartOfDay().toInstant(ZoneOffset.UTC).toEpochMilli())
        DatePickerDialog(
            onDismissRequest = { open = false },
            confirmButton = {
                TextButton(onClick = {
                    state.selectedDateMillis?.let { onChange(Instant.ofEpochMilli(it).atZone(ZoneOffset.UTC).toLocalDate()) }
                    open = false
                }) { Text("OK") }
            },
            dismissButton = { TextButton(onClick = { open = false }) { Text("Cancel") } },
        ) { DatePicker(state) }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun TimeField(value: LocalTime, onChange: (LocalTime) -> Unit, modifier: Modifier = Modifier) {
    var open by remember { mutableStateOf(false) }
    OutlinedButton(onClick = { open = true }, modifier = modifier.height(52.dp), shape = RoundedCornerShape(12.dp)) {
        Text(Format.time(value), color = MaterialTheme.colorScheme.onSurface)
    }
    if (open) {
        // Any minute, typed or dialed (the desktop's 15-minute presets aren't needed with a clock face).
        val state = rememberTimePickerState(initialHour = value.hour, initialMinute = value.minute, is24Hour = false)
        AlertDialog(
            onDismissRequest = { open = false },
            confirmButton = { TextButton(onClick = { onChange(LocalTime.of(state.hour, state.minute)); open = false }) { Text("OK") } },
            dismissButton = { TextButton(onClick = { open = false }) { Text("Cancel") } },
            text = { TimePicker(state) },
        )
    }
}
