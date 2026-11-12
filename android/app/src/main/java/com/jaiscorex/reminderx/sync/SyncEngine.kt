package com.jaiscorex.reminderx.sync

import android.content.Context
import android.content.SharedPreferences
import android.net.Uri
import android.os.Build
import android.util.Log
import com.jaiscorex.reminderx.data.Store
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.time.LocalDateTime

data class SyncStatus(
    val linked: Boolean = false,
    val syncing: Boolean = false,
    val lastSyncAt: LocalDateTime? = null,
    val lastError: String? = null,
)

/**
 * Same algorithm as the desktop's SyncService: local edits are found by fingerprint, pushed with a
 * version (epoch ms); other devices' changes are pulled from a cursor and applied when newer.
 * Runs when the app opens, 3 s after a local change, and every 15 min from SyncWorker.
 */
object SyncEngine {
    private const val PushBatch = 200
    private const val Tag = "ReminderX.Sync"

    private lateinit var prefs: SharedPreferences
    private lateinit var knownFile: File
    private val known = mutableMapOf<String, Known>()
    private val mutex = Mutex()
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)
    private var debounce: Job? = null

    private val _status = MutableStateFlow(SyncStatus())
    val status: StateFlow<SyncStatus> = _status

    private data class Known(val kind: String, val hash: String, val updatedAt: Long, val deleted: Boolean)

    fun init(context: Context) {
        prefs = context.getSharedPreferences("sync", Context.MODE_PRIVATE)
        knownFile = File(context.filesDir, "sync_known.json")
        loadKnown()
        _status.value = SyncStatus(linked = isLinked)
        Store.listeners += { fromSync -> if (!fromSync) syncSoon() }
    }

    val isLinked: Boolean get() = prefs.getString("token", null) != null

    private val server get() = prefs.getString("server", null)!!
    private val token get() = prefs.getString("token", null)!!
    private val deviceId get() = prefs.getString("deviceId", null)
    private val key get() = SyncCrypto.unb64(prefs.getString("key", null)!!)
    private var cursor: Long
        get() = prefs.getLong("cursor", 0)
        set(value) = prefs.edit().putLong("cursor", value).apply()

    // ---------- Pairing ----------

    /** Redeems the desktop's QR: reminderx://pair?u=<server>&v=<vault>&c=<code>&k=<key>. Blocking. */
    fun pair(qr: String) {
        val uri = Uri.parse(qr.trim())
        require(uri.scheme == "reminderx" && uri.host == "pair") { "That isn't a ReminderX code." }
        val server = uri.getQueryParameter("u") ?: error("The code has no server.")
        val vaultId = uri.getQueryParameter("v") ?: error("The code has no vault.")
        val code = uri.getQueryParameter("c") ?: error("The code is incomplete.")
        val key = uri.getQueryParameter("k") ?: error("The code has no key.")
        require(SyncCrypto.unb64(key).size == 32) { "The code's key is invalid." }

        val name = listOf(Build.MANUFACTURER.replaceFirstChar { it.uppercase() }, Build.MODEL)
            .distinct().joinToString(" ").take(60)
        val issued = SyncApi(server).completePairing(vaultId, code, name)

        prefs.edit()
            .putString("server", server)
            .putString("vaultId", issued.getString("vaultId"))
            .putString("deviceId", issued.getString("deviceId"))
            .putString("token", issued.getString("token"))
            .putString("key", key)
            .putLong("cursor", 0)
            .commit()
        known.clear()
        saveKnown()
        _status.value = SyncStatus(linked = true)
        Log.i(Tag, "Paired with vault ${issued.getString("vaultId")}")
    }

    /** Unlinks this phone on the server and forgets the link. Local reminders stay. */
    suspend fun unlink() = mutex.withLock {
        withContext(Dispatchers.IO) { runCatching { SyncApi(server, token).unlink(deviceId!!) } }
        forget()
    }

    private fun forget(error: String? = null) {
        prefs.edit().clear().commit()
        known.clear()
        saveKnown()
        _status.value = SyncStatus(linked = false, lastError = error)
    }

    // ---------- Sync ----------

    fun syncSoon(delayMs: Long = 3_000) {
        if (!isLinked) return
        debounce?.cancel()
        debounce = scope.launch {
            delay(delayMs)
            syncNow()
        }
    }

    suspend fun syncNow() {
        if (!isLinked || !mutex.tryLock()) return
        _status.value = _status.value.copy(syncing = true)
        try {
            val (sent, received) = withContext(Dispatchers.IO) { runCycle() }
            if (sent > 0 || received > 0) Log.i(Tag, "Sent $sent, received $received")
            _status.value = SyncStatus(linked = true, lastSyncAt = LocalDateTime.now())
        } catch (e: SyncHttpException) {
            if (e.status == 401) {
                Log.w(Tag, "This phone was unlinked")
                forget("This phone was unlinked from the PC.")
            } else {
                _status.value = _status.value.copy(syncing = false, lastError = e.message)
            }
        } catch (e: Exception) {
            Log.w(Tag, "Sync failed", e)
            _status.value = _status.value.copy(syncing = false, lastError = e.message ?: e.javaClass.simpleName)
        } finally {
            mutex.unlock()
        }
    }

    private fun runCycle(): Pair<Int, Int> {
        val api = SyncApi(server, token)
        val key = key
        val pendingHashes = mutableMapOf<String, String>()
        val outgoing = collectLocalChanges(key, pendingHashes)

        var received = 0
        val batches = outgoing.chunked(PushBatch).ifEmpty { listOf(emptyList()) }
        for (batch in batches) {
            var response = api.sync(cursor, batch)
            for (change in batch) {
                val id = change.getString("id")
                val deleted = change.optBoolean("deleted", false)
                known[id] = Known(change.getString("kind"), if (deleted) "" else pendingHashes.getValue(id), change.getLong("updatedAt"), deleted)
            }
            received += apply(response, key)
            cursor = response.cursor
            while (response.more) {
                response = api.sync(cursor, emptyList())
                received += apply(response, key)
                cursor = response.cursor
            }
        }
        saveKnown()
        return outgoing.size to received
    }

    private fun collectLocalChanges(key: ByteArray, pendingHashes: MutableMap<String, String>): List<JSONObject> {
        val (reminders, todos) = Store.syncSnapshot()
        val now = System.currentTimeMillis()
        val present = mutableSetOf<String>()
        val changes = mutableListOf<JSONObject>()

        fun collect(kind: String, id: String, json: String) {
            present += id
            val hash = SyncCrypto.hash(json)
            val k = known[id]
            if (k != null && !k.deleted && k.hash == hash) return
            pendingHashes[id] = hash
            changes += JSONObject().put("id", id).put("kind", kind)
                .put("updatedAt", maxOf(now, (k?.updatedAt ?: 0) + 1))
                .put("data", SyncCrypto.encrypt(key, kind, id, json))
        }
        reminders.forEach { (id, json) -> collect("reminder", id, json) }
        todos.forEach { (id, json) -> collect("todo", id, json) }

        for ((id, k) in known) {
            if (k.deleted || id in present) continue
            changes += JSONObject().put("id", id).put("kind", k.kind)
                .put("updatedAt", maxOf(now, k.updatedAt + 1)).put("deleted", true)
        }
        return changes
    }

    private fun apply(response: SyncResult, key: ByteArray): Int {
        val reminders = mutableMapOf<String, JSONObject?>()
        val todos = mutableMapOf<String, JSONObject?>()
        val versions = mutableMapOf<String, Pair<String, Long>>()

        for (change in response.changes) {
            if (change.from == deviceId) continue
            val k = known[change.id]
            if (k != null && k.updatedAt >= change.updatedAt) continue
            val record = if (change.deleted) null else try {
                JSONObject(SyncCrypto.decrypt(key, change.kind, change.id, change.data!!))
            } catch (e: Exception) {
                Log.w(Tag, "Couldn't decrypt ${change.kind} ${change.id}; skipped")
                continue
            }
            when (change.kind) {
                "reminder" -> reminders[change.id] = record
                "todo" -> todos[change.id] = record
                else -> continue
            }
            versions[change.id] = change.kind to change.updatedAt
        }
        if (versions.isEmpty()) return 0

        val local = Store.applySynced(reminders, todos)
        for ((id, version) in versions) {
            val json = local[id]
            known[id] = Known(version.first, json?.let(SyncCrypto::hash) ?: "", version.second, json == null)
        }
        return versions.size
    }

    private fun loadKnown() {
        known.clear()
        runCatching {
            if (!knownFile.exists()) return
            val o = JSONObject(knownFile.readText())
            for (id in o.keys()) {
                val k = o.getJSONObject(id)
                known[id] = Known(k.getString("kind"), k.getString("hash"), k.getLong("updatedAt"), k.getBoolean("deleted"))
            }
        }
    }

    private fun saveKnown() {
        val o = JSONObject()
        known.forEach { (id, k) ->
            o.put(id, JSONObject().put("kind", k.kind).put("hash", k.hash).put("updatedAt", k.updatedAt).put("deleted", k.deleted))
        }
        knownFile.writeText(o.toString())
    }
}
