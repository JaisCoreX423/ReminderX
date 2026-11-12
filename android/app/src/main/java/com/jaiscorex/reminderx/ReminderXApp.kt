package com.jaiscorex.reminderx

import android.app.Application
import com.jaiscorex.reminderx.alarm.AlarmScheduler
import com.jaiscorex.reminderx.alarm.Notifier
import com.jaiscorex.reminderx.data.Store
import com.jaiscorex.reminderx.sync.SyncEngine
import com.jaiscorex.reminderx.sync.SyncWorker
import com.jaiscorex.reminderx.update.UpdateWorker

class ReminderXApp : Application() {
    override fun onCreate() {
        super.onCreate()
        Store.init(this)
        SyncEngine.init(this)
        Notifier.createChannels(this)

        // Every change (local or synced) can move the next alert.
        Store.listeners += { AlarmScheduler.reschedule(this) }
        AlarmScheduler.reschedule(this)

        SyncWorker.schedule(this)
        UpdateWorker.schedule(this)
    }
}
