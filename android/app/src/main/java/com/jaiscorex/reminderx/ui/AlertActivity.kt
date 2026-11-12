package com.jaiscorex.reminderx.ui

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.jaiscorex.reminderx.R
import com.jaiscorex.reminderx.alarm.ActionReceiver
import com.jaiscorex.reminderx.alarm.AppSettings
import com.jaiscorex.reminderx.alarm.Notifier
import com.jaiscorex.reminderx.data.Format
import com.jaiscorex.reminderx.data.Store

/**
 * The phone's version of the desktop's full-screen alert: a card over a dimmed screen (and over the
 * lock screen) with Complete, Snooze 10 min, Open and Dismiss. Several due at once queue up.
 */
class AlertActivity : ComponentActivity() {
    private val queue = mutableStateListOf<String>()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enqueue(intent)
        setContent {
            ReminderXTheme(AppSettings.theme(this)) {
                val id = queue.firstOrNull()
                val reminder = id?.let(Store::reminder)
                if (id == null || reminder == null) {
                    // Nothing left, or that reminder was deleted meanwhile: move on outside composition.
                    LaunchedEffect(id) { if (id != null) queue.remove(id) else finish() }
                    return@ReminderXTheme
                }

                Box(Modifier.fillMaxSize().padding(24.dp), contentAlignment = Alignment.Center) {
                    Surface(shape = RoundedCornerShape(22.dp), color = MaterialTheme.colorScheme.surface, shadowElevation = 12.dp) {
                        Column(
                            Modifier.padding(24.dp).fillMaxWidth(),
                            horizontalAlignment = Alignment.CenterHorizontally,
                            verticalArrangement = Arrangement.spacedBy(10.dp),
                        ) {
                            Image(painterResource(R.mipmap.ic_launcher), null, Modifier.size(56.dp).clip(RoundedCornerShape(13.dp)))
                            Text(reminder.title, fontSize = 22.sp, fontWeight = FontWeight.Bold, textAlign = TextAlign.Center,
                                color = MaterialTheme.colorScheme.onSurface)
                            Caption(reminder.recurrence?.let { "${Format.friendly(reminder.dueAt)} · ${it.describe()}" } ?: Format.friendly(reminder.dueAt))
                            if (reminder.description.isNotBlank()) {
                                Text(reminder.description, textAlign = TextAlign.Center, color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                            if (queue.size > 1) Caption("${queue.size - 1} more waiting")
                            Spacer(Modifier.height(6.dp))
                            Button(
                                onClick = { act(ActionReceiver.Complete, id) }, modifier = Modifier.fillMaxWidth().height(50.dp),
                                shape = RoundedCornerShape(14.dp), colors = ButtonDefaults.buttonColors(containerColor = Accent),
                            ) { Text("Complete", fontSize = 16.sp) }
                            OutlinedButton(
                                onClick = { act(ActionReceiver.Snooze, id) }, modifier = Modifier.fillMaxWidth().height(50.dp),
                                shape = RoundedCornerShape(14.dp),
                            ) { Text("Snooze 10 min", fontSize = 16.sp) }
                            TextButton(onClick = {
                                startActivity(Intent(this@AlertActivity, MainActivity::class.java)
                                    .putExtra(Notifier.ExtraReminderId, id).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
                                next(id)
                            }) { Text("Open reminder") }
                            TextButton(onClick = { act(ActionReceiver.Dismiss, id) }) {
                                Text("Dismiss", color = MaterialTheme.colorScheme.onSurfaceVariant)
                            }
                        }
                    }
                }
            }
        }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        enqueue(intent)
    }

    private fun enqueue(intent: Intent?) {
        val id = intent?.getStringExtra(Notifier.ExtraReminderId) ?: return
        if (id !in queue) queue.add(id)
    }

    private fun act(action: String, id: String) {
        ActionReceiver.handle(this, action, id)
        next(id)
    }

    private fun next(id: String) {
        Notifier.cancel(this, id)
        queue.remove(id)
        if (queue.isEmpty()) finish()
    }
}
