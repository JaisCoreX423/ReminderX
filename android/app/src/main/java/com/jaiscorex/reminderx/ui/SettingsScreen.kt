package com.jaiscorex.reminderx.ui

import android.content.Intent
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.compose.foundation.Image
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.FilterChip
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.core.app.NotificationManagerCompat
import com.jaiscorex.reminderx.R
import com.jaiscorex.reminderx.alarm.AppSettings
import com.jaiscorex.reminderx.alarm.Notifier
import com.jaiscorex.reminderx.sync.SyncEngine
import com.jaiscorex.reminderx.update.UpdateState
import com.jaiscorex.reminderx.update.Updater
import kotlinx.coroutines.launch

@Composable
fun SettingsScreen(theme: String, onTheme: (String) -> Unit) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    val sync by SyncEngine.status.collectAsState()
    val update by Updater.state.collectAsState()
    var fullScreen by remember { mutableStateOf(AppSettings.fullScreenAlerts(context)) }
    var autoUpdate by remember { mutableStateOf(AppSettings.autoUpdate(context)) }
    var confirmUnlink by remember { mutableStateOf(false) }

    fun open(intent: Intent) = context.startActivity(intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))

    Column(
        Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(horizontal = 16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        ScreenTitle("Settings")

        Card {
            SectionTitle("Appearance")
            Spacer(Modifier.height(8.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                for (t in listOf("System", "Light", "Dark")) FilterChip(theme == t, { onTheme(t) }, { Text(t) })
            }
        }

        Card {
            SectionTitle("Alerts")
            Spacer(Modifier.height(8.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text("Full-screen alerts", color = MaterialTheme.colorScheme.onSurface)
                    Caption("Shows the reminder over the lock screen, like on the PC.")
                }
                Switch(fullScreen, {
                    fullScreen = it
                    AppSettings.setFullScreenAlerts(context, it)
                }, colors = SwitchDefaults.colors(checkedTrackColor = Accent))
            }
            if (!NotificationManagerCompat.from(context).areNotificationsEnabled()) {
                Spacer(Modifier.height(10.dp))
                Caption("Notifications are off for ReminderX, so reminders can't alert you.")
                TextButton(onClick = {
                    open(Intent(Settings.ACTION_APP_NOTIFICATION_SETTINGS).putExtra(Settings.EXTRA_APP_PACKAGE, context.packageName))
                }) { Text("Turn on notifications") }
            }
            if (fullScreen && !Notifier.canUseFullScreen(context) && Build.VERSION.SDK_INT >= 34) {
                Spacer(Modifier.height(10.dp))
                Caption("Android needs your permission for full-screen alerts.")
                TextButton(onClick = {
                    open(Intent(Settings.ACTION_MANAGE_APP_USE_FULL_SCREEN_INTENT, Uri.parse("package:${context.packageName}")))
                }) { Text("Allow full-screen alerts") }
            }
        }

        Card {
            SectionTitle("Sync with your PC")
            Spacer(Modifier.height(6.dp))
            Caption(
                when {
                    sync.syncing -> "Syncing…"
                    sync.lastError != null -> "Last attempt failed: ${sync.lastError}"
                    sync.lastSyncAt != null -> "Last synced ${com.jaiscorex.reminderx.data.Format.friendly(sync.lastSyncAt!!)}"
                    else -> "Linked"
                },
            )
            Spacer(Modifier.height(10.dp))
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                OutlinedButton(onClick = { scope.launch { SyncEngine.syncNow() } }, shape = RoundedCornerShape(12.dp)) { Text("Sync now") }
                TextButton(onClick = { confirmUnlink = true }) { Text("Unlink this phone", color = Danger) }
            }
        }

        Card {
            SectionTitle("Updates")
            Spacer(Modifier.height(6.dp))
            Row(verticalAlignment = Alignment.CenterVertically) {
                Column(Modifier.weight(1f)) {
                    Text("Check for updates automatically", color = MaterialTheme.colorScheme.onSurface)
                    Caption("Looks at GitHub twice a day and lets you know.")
                }
                Switch(autoUpdate, {
                    autoUpdate = it
                    AppSettings.setAutoUpdate(context, it)
                }, colors = SwitchDefaults.colors(checkedTrackColor = Accent))
            }
            Spacer(Modifier.height(10.dp))
            when (val s = update) {
                is UpdateState.Available -> {
                    Caption("Version ${s.release.version} is available.")
                    Spacer(Modifier.height(8.dp))
                    Button(
                        onClick = { scope.launch { Updater.install(context, s.release) } },
                        colors = ButtonDefaults.buttonColors(containerColor = Accent), shape = RoundedCornerShape(12.dp),
                    ) { Text("Download and install") }
                }
                is UpdateState.Downloading -> {
                    Caption("Downloading ${s.release.version}… ${s.percent}%")
                    Spacer(Modifier.height(8.dp))
                    LinearProgressIndicator(progress = { s.percent / 100f }, color = Accent)
                }
                else -> {
                    Caption(
                        when (s) {
                            UpdateState.Checking -> "Checking…"
                            UpdateState.UpToDate -> "You're on the latest version (${Updater.currentVersion})."
                            is UpdateState.Failed -> s.message
                            else -> "You're on version ${Updater.currentVersion}."
                        },
                    )
                    Spacer(Modifier.height(8.dp))
                    OutlinedButton(onClick = { scope.launch { Updater.check() } }, shape = RoundedCornerShape(12.dp)) { Text("Check now") }
                }
            }
        }

        Card {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Image(painterResource(R.mipmap.ic_launcher), null, Modifier.size(56.dp).clip(RoundedCornerShape(13.dp)))
                Spacer(Modifier.width(14.dp))
                Column {
                    Text("ReminderX ${Updater.currentVersion}", fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface)
                    Caption("Developed by JaisCoreX & Claude")
                    TextButton(onClick = { open(Intent(Intent.ACTION_VIEW, Uri.parse("https://github.com/JaisCoreX423/ReminderX"))) }) {
                        Text("github.com/JaisCoreX423/ReminderX")
                    }
                }
            }
        }
        Spacer(Modifier.height(12.dp))
    }

    if (confirmUnlink) {
        AlertDialog(
            onDismissRequest = { confirmUnlink = false },
            title = { Text("Unlink this phone?") },
            text = { Text("It stops syncing with your PC. Reminders already on this phone stay here.") },
            confirmButton = {
                TextButton(onClick = {
                    confirmUnlink = false
                    scope.launch { SyncEngine.unlink() }
                }) { Text("Unlink", color = Danger) }
            },
            dismissButton = { TextButton(onClick = { confirmUnlink = false }) { Text("Cancel") } },
        )
    }
}
