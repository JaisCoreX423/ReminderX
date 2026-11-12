package com.jaiscorex.reminderx.alarm

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.media.AudioAttributes
import android.media.RingtoneManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import com.jaiscorex.reminderx.R
import com.jaiscorex.reminderx.data.Format
import com.jaiscorex.reminderx.data.Reminder
import com.jaiscorex.reminderx.ui.AlertActivity
import com.jaiscorex.reminderx.ui.MainActivity

object Notifier {
    const val ExtraReminderId = "reminderId"
    const val ExtraOpenUpdate = "openUpdate"
    private const val RemindersChannel = "reminders"
    private const val UpdatesChannel = "updates"
    private const val UpdateNotificationId = 42

    fun createChannels(context: Context) {
        val manager = context.getSystemService(NotificationManager::class.java)
        manager.createNotificationChannel(
            NotificationChannel(RemindersChannel, "Reminders", NotificationManager.IMPORTANCE_HIGH).apply {
                description = "Due reminders"
                enableVibration(true)
                setSound(
                    RingtoneManager.getDefaultUri(RingtoneManager.TYPE_NOTIFICATION),
                    AudioAttributes.Builder().setUsage(AudioAttributes.USAGE_NOTIFICATION_EVENT)
                        .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build(),
                )
            },
        )
        manager.createNotificationChannel(
            NotificationChannel(UpdatesChannel, "Updates", NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = "New ReminderX versions"
            },
        )
    }

    private fun notificationId(reminderId: String) = reminderId.hashCode()

    private fun action(context: Context, action: String, id: String): PendingIntent =
        PendingIntent.getBroadcast(
            context, (action + id).hashCode(),
            Intent(context, ActionReceiver::class.java).setAction(action).putExtra(ExtraReminderId, id),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

    /** Heads-up notification with actions, plus the full-screen alert when the phone allows it. */
    fun showReminder(context: Context, reminder: Reminder) {
        if (!NotificationManagerCompat.from(context).areNotificationsEnabled()) return

        val open = PendingIntent.getActivity(
            context, notificationId(reminder.id),
            Intent(context, MainActivity::class.java).putExtra(ExtraReminderId, reminder.id)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        val alert = PendingIntent.getActivity(
            context, notificationId(reminder.id) + 1,
            Intent(context, AlertActivity::class.java).putExtra(ExtraReminderId, reminder.id)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )

        val text = reminder.description.ifBlank { Format.friendly(reminder.dueAt) }
        val builder = NotificationCompat.Builder(context, RemindersChannel)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle(reminder.title)
            .setContentText(text)
            .setStyle(NotificationCompat.BigTextStyle().bigText(text))
            .setCategory(NotificationCompat.CATEGORY_REMINDER)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setContentIntent(open)
            .setAutoCancel(true)
            .addAction(0, "Complete", action(context, ActionReceiver.Complete, reminder.id))
            .addAction(0, "Snooze 10 min", action(context, ActionReceiver.Snooze, reminder.id))
            .addAction(0, "Dismiss", action(context, ActionReceiver.Dismiss, reminder.id))

        if (AppSettings.fullScreenAlerts(context) && canUseFullScreen(context)) builder.setFullScreenIntent(alert, true)

        try {
            NotificationManagerCompat.from(context).notify(notificationId(reminder.id), builder.build())
        } catch (e: SecurityException) {
            // Notification permission revoked between the check and the call.
        }
    }

    fun cancel(context: Context, reminderId: String) =
        NotificationManagerCompat.from(context).cancel(notificationId(reminderId))

    fun canUseFullScreen(context: Context): Boolean =
        Build.VERSION.SDK_INT < 34 || context.getSystemService(NotificationManager::class.java).canUseFullScreenIntent()

    fun showUpdate(context: Context, version: String) {
        if (!NotificationManagerCompat.from(context).areNotificationsEnabled()) return
        val open = PendingIntent.getActivity(
            context, UpdateNotificationId,
            Intent(context, MainActivity::class.java).putExtra(ExtraOpenUpdate, true)
                .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_SINGLE_TOP),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE,
        )
        val notification = NotificationCompat.Builder(context, UpdatesChannel)
            .setSmallIcon(R.drawable.ic_notification)
            .setContentTitle("ReminderX $version is available")
            .setContentText("Tap to update.")
            .setContentIntent(open)
            .setAutoCancel(true)
            .build()
        try {
            NotificationManagerCompat.from(context).notify(UpdateNotificationId, notification)
        } catch (e: SecurityException) {
        }
    }
}

/** Small app preferences (theme, full-screen alerts, automatic update checks). */
object AppSettings {
    private fun prefs(context: Context) = context.getSharedPreferences("settings", Context.MODE_PRIVATE)

    fun theme(context: Context): String = prefs(context).getString("theme", "System") ?: "System"
    fun setTheme(context: Context, value: String) = prefs(context).edit().putString("theme", value).apply()

    fun fullScreenAlerts(context: Context): Boolean = prefs(context).getBoolean("fullScreen", true)
    fun setFullScreenAlerts(context: Context, value: Boolean) = prefs(context).edit().putBoolean("fullScreen", value).apply()

    fun autoUpdate(context: Context): Boolean = prefs(context).getBoolean("autoUpdate", true)
    fun setAutoUpdate(context: Context, value: Boolean) = prefs(context).edit().putBoolean("autoUpdate", value).apply()
}
