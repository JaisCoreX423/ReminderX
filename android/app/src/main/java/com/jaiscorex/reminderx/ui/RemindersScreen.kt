package com.jaiscorex.reminderx.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.outlined.Check
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material.icons.outlined.Pause
import androidx.compose.material.icons.outlined.PlayArrow
import androidx.compose.material.icons.outlined.Snooze
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.jaiscorex.reminderx.data.Format
import com.jaiscorex.reminderx.data.Reminder
import com.jaiscorex.reminderx.data.ReminderState
import com.jaiscorex.reminderx.data.Store

@Composable
fun ScreenTitle(text: String) {
    Text(
        text, fontSize = 30.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onBackground,
        modifier = Modifier.padding(start = 20.dp, top = 16.dp, bottom = 8.dp),
    )
}

@Composable
fun Badge(text: String) {
    Surface(color = MaterialTheme.colorScheme.surfaceVariant, shape = RoundedCornerShape(6.dp)) {
        Text(text, fontSize = 11.sp, color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(horizontal = 6.dp, vertical = 2.dp))
    }
}

@Composable
fun RemindersScreen(openReminderId: String?, onOpened: () -> Unit) {
    val data by Store.data.collectAsState()
    var editing by remember { mutableStateOf<Reminder?>(null) }
    var confirmDelete by remember { mutableStateOf<Reminder?>(null) }

    LaunchedEffect(openReminderId) {
        if (openReminderId != null) {
            editing = Store.reminder(openReminderId)
            onOpened()
        }
    }

    editing?.let { r ->
        ReminderEditor(r, isNew = Store.reminder(r.id) == null, onClose = { editing = null })
        return
    }

    val items = data.reminders
        .filter { it.state == ReminderState.Pending || it.state == ReminderState.Snoozed }
        .sortedBy { it.dueAt }

    Box(Modifier.fillMaxSize()) {
        LazyColumn(
            contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = 96.dp),
            verticalArrangement = Arrangement.spacedBy(10.dp),
        ) {
            item { ScreenTitle("Reminders") }
            if (items.isEmpty()) item {
                Caption("No reminders yet. Tap + to create one.", Modifier.padding(20.dp))
            }
            items(items, key = { it.id }) { r ->
                ReminderCard(r, onOpen = { editing = r }, onDelete = { confirmDelete = r })
            }
        }
        FloatingActionButton(
            onClick = { editing = Reminder() },
            containerColor = Accent,
            modifier = Modifier.align(Alignment.BottomEnd).padding(20.dp),
        ) { Icon(Icons.Filled.Add, contentDescription = "New reminder", tint = MaterialTheme.colorScheme.onPrimary) }
    }

    confirmDelete?.let { r ->
        AlertDialog(
            onDismissRequest = { confirmDelete = null },
            title = { Text("Delete \"${r.title}\"?") },
            text = { Text("It's also deleted on your PC.") },
            confirmButton = { TextButton(onClick = { Store.deleteReminder(r.id); confirmDelete = null }) { Text("Delete", color = Danger) } },
            dismissButton = { TextButton(onClick = { confirmDelete = null }) { Text("Cancel") } },
        )
    }
}

@Composable
private fun ReminderCard(r: Reminder, onOpen: () -> Unit, onDelete: () -> Unit) {
    Card(Modifier.clickable(onClick = onOpen)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(
                r.title, fontWeight = FontWeight.SemiBold, fontSize = 16.sp, color = MaterialTheme.colorScheme.onSurface,
                maxLines = 2, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f, fill = false),
            )
            if (r.recurrence != null) { Spacer(Modifier.width(8.dp)); Badge("repeats") }
            if (r.isPaused) { Spacer(Modifier.width(6.dp)); Badge("paused") }
            if (r.state == ReminderState.Snoozed) { Spacer(Modifier.width(6.dp)); Badge("snoozed") }
        }
        if (r.description.isNotBlank()) {
            Spacer(Modifier.height(2.dp))
            Text(r.description, maxLines = 2, overflow = TextOverflow.Ellipsis, color = MaterialTheme.colorScheme.onSurfaceVariant, fontSize = 14.sp)
        }
        Spacer(Modifier.height(6.dp))
        Caption(r.recurrence?.let { "${Format.friendly(r.dueAt)} · ${it.describe()}" } ?: Format.friendly(r.dueAt))
        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
            IconButton(onClick = { Store.complete(r.id) }) { Icon(Icons.Outlined.Check, "Complete", tint = MaterialTheme.colorScheme.onSurface) }
            IconButton(onClick = { Store.snooze(r.id, 10) }) { Icon(Icons.Outlined.Snooze, "Snooze 10 min", tint = MaterialTheme.colorScheme.onSurface) }
            IconButton(onClick = { Store.togglePause(r.id) }) {
                Icon(if (r.isPaused) Icons.Outlined.PlayArrow else Icons.Outlined.Pause, "Pause or resume", tint = MaterialTheme.colorScheme.onSurface)
            }
            IconButton(onClick = onDelete) { Icon(Icons.Outlined.Delete, "Delete", tint = Danger) }
        }
    }
}
