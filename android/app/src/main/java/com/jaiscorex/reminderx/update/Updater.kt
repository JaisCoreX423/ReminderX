package com.jaiscorex.reminderx.update

import android.content.Context
import android.content.Intent
import android.net.Uri
import android.provider.Settings
import androidx.core.content.FileProvider
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import com.jaiscorex.reminderx.BuildConfig
import com.jaiscorex.reminderx.alarm.AppSettings
import com.jaiscorex.reminderx.alarm.Notifier
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.withContext
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest
import java.util.concurrent.TimeUnit

data class Release(val version: String, val url: String, val size: Long, val sha256: String?)

sealed interface UpdateState {
    data object Idle : UpdateState
    data object Checking : UpdateState
    data object UpToDate : UpdateState
    data class Available(val release: Release) : UpdateState
    data class Downloading(val release: Release, val percent: Int) : UpdateState
    data class Failed(val message: String) : UpdateState
}

/**
 * Same update source as the desktop: the latest GitHub release of JaisCoreX423/ReminderX. Downloads
 * the ReminderX.apk asset, checks its size and the SHA-256 GitHub publishes for it, then hands it to
 * Android's installer (which always asks the user to confirm).
 */
object Updater {
    private const val LatestRelease = "https://api.github.com/repos/JaisCoreX423/ReminderX/releases/latest"
    private const val AssetName = "ReminderX.apk"

    private val _state = MutableStateFlow<UpdateState>(UpdateState.Idle)
    val state: StateFlow<UpdateState> = _state

    val currentVersion: String get() = BuildConfig.VERSION_NAME

    suspend fun check(): Release? = withContext(Dispatchers.IO) {
        _state.value = UpdateState.Checking
        try {
            val conn = URL(LatestRelease).openConnection() as HttpURLConnection
            conn.setRequestProperty("Accept", "application/vnd.github+json")
            conn.setRequestProperty("User-Agent", "ReminderX-Android/$currentVersion")
            conn.connectTimeout = 15_000
            conn.readTimeout = 30_000
            if (conn.responseCode == 404) {
                _state.value = UpdateState.UpToDate
                return@withContext null
            }
            if (conn.responseCode != 200) error("GitHub answered ${conn.responseCode}")
            val json = JSONObject(conn.inputStream.bufferedReader().use { it.readText() })
            conn.disconnect()

            val version = json.getString("tag_name").removePrefix("v")
            val assets = json.getJSONArray("assets")
            val asset = (0 until assets.length()).map { assets.getJSONObject(it) }.firstOrNull { it.getString("name") == AssetName }
            if (asset == null || !isNewer(version, currentVersion)) {
                _state.value = UpdateState.UpToDate
                return@withContext null
            }

            val release = Release(
                version = version,
                url = asset.getString("browser_download_url"),
                size = asset.getLong("size"),
                sha256 = asset.optString("digest").takeIf { it.startsWith("sha256:") }?.removePrefix("sha256:"),
            )
            _state.value = UpdateState.Available(release)
            release
        } catch (e: Exception) {
            _state.value = UpdateState.Failed(e.message ?: "Couldn't check for updates")
            null
        }
    }

    /** Downloads, verifies and opens the installer. */
    suspend fun install(context: Context, release: Release) {
        if (!context.packageManager.canRequestPackageInstalls()) {
            // Android asks once per app: "Allow from this source".
            context.startActivity(
                Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:${context.packageName}"))
                    .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
            )
            _state.value = UpdateState.Available(release)
            return
        }

        val file = try {
            withContext(Dispatchers.IO) { download(context, release) }
        } catch (e: Exception) {
            _state.value = UpdateState.Failed(e.message ?: "Download failed")
            return
        }

        _state.value = UpdateState.Available(release)
        val uri = FileProvider.getUriForFile(context, "${context.packageName}.files", file)
        context.startActivity(
            Intent(Intent.ACTION_VIEW).setDataAndType(uri, "application/vnd.android.package-archive")
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK),
        )
    }

    private fun download(context: Context, release: Release): File {
        val dir = File(context.cacheDir, "updates").apply { mkdirs() }
        val file = File(dir, AssetName)
        val digest = MessageDigest.getInstance("SHA-256")

        val conn = URL(release.url).openConnection() as HttpURLConnection
        conn.setRequestProperty("User-Agent", "ReminderX-Android/$currentVersion")
        conn.connectTimeout = 15_000
        conn.readTimeout = 60_000
        try {
            conn.inputStream.use { input ->
                file.outputStream().use { output ->
                    val buffer = ByteArray(64 * 1024)
                    var total = 0L
                    var lastPercent = -1
                    while (true) {
                        val read = input.read(buffer)
                        if (read < 0) break
                        output.write(buffer, 0, read)
                        digest.update(buffer, 0, read)
                        total += read
                        val percent = if (release.size > 0) (total * 100 / release.size).toInt() else 0
                        if (percent != lastPercent) {
                            lastPercent = percent
                            _state.value = UpdateState.Downloading(release, percent)
                        }
                    }
                    if (release.size > 0 && total != release.size) error("Download incomplete ($total of ${release.size} bytes)")
                }
            }
        } finally {
            conn.disconnect()
        }

        val actual = digest.digest().joinToString("") { "%02x".format(it) }
        if (release.sha256 != null && !actual.equals(release.sha256, ignoreCase = true)) {
            file.delete()
            error("The download didn't match GitHub's checksum, so it wasn't installed")
        }
        return file
    }

    fun isNewer(candidate: String, current: String): Boolean {
        fun parts(v: String) = v.substringBefore('-').split('.').map { it.toIntOrNull() ?: 0 } + listOf(0, 0, 0)
        val a = parts(candidate)
        val b = parts(current)
        for (i in 0 until 3) if (a[i] != b[i]) return a[i] > b[i]
        return false
    }
}

/** Checks GitHub twice a day and posts a notification when there's a newer version. */
class UpdateWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {
    override suspend fun doWork(): Result {
        if (AppSettings.autoUpdate(applicationContext)) {
            Updater.check()?.let { Notifier.showUpdate(applicationContext, it.version) }
        }
        return Result.success()
    }

    companion object {
        fun schedule(context: Context) {
            val request = PeriodicWorkRequestBuilder<UpdateWorker>(12, TimeUnit.HOURS)
                .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
                .build()
            WorkManager.getInstance(context)
                .enqueueUniquePeriodicWork("updates", ExistingPeriodicWorkPolicy.KEEP, request)
        }
    }
}
