package com.jaiscorex.reminderx.ui

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.CheckCircle
import androidx.compose.material.icons.outlined.History
import androidx.compose.material.icons.outlined.Notifications
import androidx.compose.material.icons.outlined.Settings
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.NavigationBarItemDefaults
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.core.content.ContextCompat
import androidx.lifecycle.lifecycleScope
import com.jaiscorex.reminderx.alarm.AppSettings
import com.jaiscorex.reminderx.alarm.Notifier
import com.jaiscorex.reminderx.sync.SyncEngine
import kotlinx.coroutines.launch

enum class Tab(val label: String) { Reminders("Reminders"), Todo("To-Do"), History("History"), Settings("Settings") }

class MainActivity : ComponentActivity() {
    private var tab by mutableStateOf(Tab.Reminders)
    private var openReminderId by mutableStateOf<String?>(null)
    private var theme by mutableStateOf("System")

    private val askNotifications = registerForActivityResult(ActivityResultContracts.RequestPermission()) { }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        theme = AppSettings.theme(this)
        handleIntent(intent)

        if (Build.VERSION.SDK_INT >= 33 &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) askNotifications.launch(Manifest.permission.POST_NOTIFICATIONS)

        setContent {
            ReminderXTheme(theme) {
                val status by SyncEngine.status.collectAsState()
                if (!status.linked) {
                    PairScreen(lastError = status.lastError)
                } else {
                    MainScaffold()
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        lifecycleScope.launch { SyncEngine.syncNow() }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        handleIntent(intent)
    }

    private fun handleIntent(intent: Intent?) {
        intent?.getStringExtra(Notifier.ExtraReminderId)?.let {
            tab = Tab.Reminders
            openReminderId = it
        }
        if (intent?.getBooleanExtra(Notifier.ExtraOpenUpdate, false) == true) tab = Tab.Settings
    }

    @Composable
    private fun MainScaffold() {
        Scaffold(
            containerColor = MaterialTheme.colorScheme.background,
            bottomBar = {
                NavigationBar(containerColor = MaterialTheme.colorScheme.surface) {
                    for (t in Tab.entries) {
                        NavigationBarItem(
                            selected = tab == t,
                            onClick = { tab = t },
                            icon = {
                                Icon(
                                    when (t) {
                                        Tab.Reminders -> Icons.Outlined.Notifications
                                        Tab.Todo -> Icons.Outlined.CheckCircle
                                        Tab.History -> Icons.Outlined.History
                                        Tab.Settings -> Icons.Outlined.Settings
                                    },
                                    contentDescription = t.label,
                                )
                            },
                            label = { Text(t.label) },
                            colors = NavigationBarItemDefaults.colors(indicatorColor = MaterialTheme.colorScheme.surfaceVariant),
                        )
                    }
                }
            },
        ) { padding ->
            Box(Modifier.fillMaxSize().padding(padding)) {
                when (tab) {
                    Tab.Reminders -> RemindersScreen(openReminderId) { openReminderId = null }
                    Tab.Todo -> TodoScreen()
                    Tab.History -> HistoryScreen()
                    Tab.Settings -> SettingsScreen(theme) {
                        theme = it
                        AppSettings.setTheme(this@MainActivity, it)
                    }
                }
            }
        }
    }
}
