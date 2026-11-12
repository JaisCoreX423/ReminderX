package com.jaiscorex.reminderx.alarm

import android.app.AlarmManager
import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.os.Build
import com.jaiscorex.reminderx.data.Store
import java.time.LocalDateTime
import java.time.ZoneId

/**
 * One exact alarm for the earliest pending alert (first time due, or a re-notify of an ignored one).
 * When it fires, every due reminder is shown and the next alarm is set. Recomputed after every data
 * change, at boot and when the clock or time zone changes.
 */
object AlarmScheduler {
    private const val RequestCode = 1

    fun reschedule(context: Context) {
        val alarms = context.getSystemService(AlarmManager::class.java)
        val intent = PendingIntent.getBroadcast(
            context, RequestCode, Intent(context, AlarmReceiver::class.java),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

        val next = Store.data.value.reminders.mapNotNull { it.nextAlertAt }.minOrNull()
        if (next == null) {
            alarms.cancel(intent)
            return
        }

        val at = maxOf(
            next.atZone(ZoneId.systemDefault()).toInstant().toEpochMilli(),
            System.currentTimeMillis() + 1_000,
        )
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.S || alarms.canScheduleExactAlarms()) {
            alarms.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, intent)
        } else {
            alarms.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, intent)
        }
    }
}

class AlarmReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val now = LocalDateTime.now()
        for (reminder in Store.dueReminders(now)) {
            Notifier.showReminder(context, reminder)
            Store.markShown(reminder.id, now)
        }
        AlarmScheduler.reschedule(context)
    }
}

/** Complete / Snooze / Dismiss from the notification or the alert screen. */
class ActionReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        val id = intent.getStringExtra(Notifier.ExtraReminderId) ?: return
        handle(context, intent.action, id)
    }

    companion object {
        const val Complete = "com.jaiscorex.reminderx.COMPLETE"
        const val Snooze = "com.jaiscorex.reminderx.SNOOZE"
        const val Dismiss = "com.jaiscorex.reminderx.DISMISS"

        fun handle(context: Context, action: String?, id: String) {
            when (action) {
                Complete -> Store.complete(id)
                Snooze -> Store.snooze(id, 10)
                Dismiss -> Store.dismiss(id)
                else -> return
            }
            Notifier.cancel(context, id)
        }
    }
}

class BootReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        AlarmScheduler.reschedule(context)
    }
}
