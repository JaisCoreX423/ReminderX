package com.jaiscorex.reminderx.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.Add
import androidx.compose.material.icons.outlined.Delete
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CheckboxDefaults
import androidx.compose.material3.FloatingActionButton
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.unit.dp
import com.jaiscorex.reminderx.data.Format
import com.jaiscorex.reminderx.data.Store
import com.jaiscorex.reminderx.data.Todo
import java.time.LocalDate
import java.time.LocalTime

@Composable
fun TodoScreen() {
    val data by Store.data.collectAsState()
    var adding by remember { mutableStateOf(false) }
    val todos = data.todos.sortedWith(compareBy<Todo> { it.isDone }.thenBy { it.dueAt ?: java.time.LocalDateTime.MAX }.thenBy { it.createdAt })

    Box(Modifier.fillMaxSize()) {
        LazyColumn(contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = 96.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            item { ScreenTitle("To-Do") }
            if (todos.isEmpty()) item { Caption("Nothing to do. Tap + to add something.", Modifier.padding(20.dp)) }
            items(todos, key = { it.id }) { t ->
                Card {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(t.isDone, { Store.setTodoDone(t.id, it) }, colors = CheckboxDefaults.colors(checkedColor = Accent))
                        Column(Modifier.weight(1f)) {
                            Text(
                                t.title, color = MaterialTheme.colorScheme.onSurface,
                                textDecoration = if (t.isDone) TextDecoration.LineThrough else null,
                            )
                            if (t.notes.isNotBlank()) Caption(t.notes)
                            t.dueAt?.let { Caption("Due ${Format.friendly(it)}") }
                        }
                        IconButton(onClick = { Store.deleteTodo(t.id) }) { Icon(Icons.Outlined.Delete, "Delete", tint = Danger) }
                    }
                }
            }
        }
        FloatingActionButton(onClick = { adding = true }, containerColor = Accent, modifier = Modifier.align(Alignment.BottomEnd).padding(20.dp)) {
            Icon(Icons.Filled.Add, contentDescription = "New to-do", tint = MaterialTheme.colorScheme.onPrimary)
        }
    }

    if (adding) NewTodoDialog { adding = false }
}

@Composable
private fun NewTodoDialog(onClose: () -> Unit) {
    var title by remember { mutableStateOf("") }
    var notes by remember { mutableStateOf("") }
    var hasDue by remember { mutableStateOf(false) }
    var date by remember { mutableStateOf(LocalDate.now()) }
    var time by remember { mutableStateOf(LocalTime.of(9, 0)) }
    var createReminder by remember { mutableStateOf(true) }

    AlertDialog(
        onDismissRequest = onClose,
        title = { Text("New To-Do") },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedTextField(title, { title = it }, label = { Text("Title") }, singleLine = true, modifier = Modifier.fillMaxWidth())
                OutlinedTextField(notes, { notes = it }, label = { Text("Notes") }, modifier = Modifier.fillMaxWidth())
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Text("Set a due date", Modifier.weight(1f))
                    Switch(hasDue, { hasDue = it })
                }
                if (hasDue) {
                    Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                        DateField(date, { date = it }, Modifier.weight(1f))
                        TimeField(time, { time = it }, Modifier.weight(1f))
                    }
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text("Also create a reminder", Modifier.weight(1f))
                        Switch(createReminder, { createReminder = it })
                    }
                }
            }
        },
        confirmButton = {
            TextButton(enabled = title.isNotBlank(), onClick = {
                Store.saveTodo(
                    Todo(title = title.trim(), notes = notes.trim(), dueAt = if (hasDue) date.atTime(time) else null),
                    createReminder = hasDue && createReminder,
                )
                onClose()
            }) { Text("Add") }
        },
        dismissButton = { TextButton(onClick = onClose) { Text("Cancel") } },
    )
}
