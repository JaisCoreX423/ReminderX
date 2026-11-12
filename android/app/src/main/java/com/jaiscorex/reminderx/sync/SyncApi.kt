package com.jaiscorex.reminderx.sync

import org.json.JSONArray
import org.json.JSONObject
import java.net.HttpURLConnection
import java.net.URL

class SyncHttpException(val status: Int, message: String) : Exception("$message (HTTP $status)")

data class RemoteChange(
    val id: String,
    val kind: String,
    val updatedAt: Long,
    val deleted: Boolean,
    val data: String?,
    val from: String?,
)

data class SyncResult(val cursor: Long, val more: Boolean, val accepted: Int, val changes: List<RemoteChange>)

/** Blocking HTTP client for the Netlify sync service (sync/lib/api.mjs). Call from a background thread. */
class SyncApi(private val baseUrl: String, private val token: String? = null) {

    fun completePairing(vaultId: String, code: String, name: String): JSONObject =
        send("POST", "/api/pairings/complete", JSONObject().put("vaultId", vaultId).put("code", code)
            .put("name", name).put("platform", "android"))

    fun devices(): JSONArray = send("GET", "/api/devices", null).getJSONArray("devices")

    fun unlink(deviceId: String) {
        send("DELETE", "/api/devices/$deviceId", null)
    }

    fun sync(since: Long, changes: List<JSONObject>): SyncResult {
        val response = send("POST", "/api/sync", JSONObject().put("since", since).put("changes", JSONArray(changes)))
        val list = response.getJSONArray("changes")
        return SyncResult(
            cursor = response.getLong("cursor"),
            more = response.optBoolean("more", false),
            accepted = response.optInt("accepted", 0),
            changes = (0 until list.length()).map { i ->
                val c = list.getJSONObject(i)
                RemoteChange(
                    id = c.getString("id"),
                    kind = c.getString("kind"),
                    updatedAt = c.getLong("updatedAt"),
                    deleted = c.optBoolean("deleted", false),
                    data = if (c.isNull("data")) null else c.getString("data"),
                    from = if (c.isNull("from")) null else c.getString("from"),
                )
            },
        )
    }

    private fun send(method: String, path: String, body: JSONObject?): JSONObject {
        val conn = URL(baseUrl.trimEnd('/') + path).openConnection() as HttpURLConnection
        try {
            conn.requestMethod = method
            conn.connectTimeout = 15_000
            conn.readTimeout = 30_000
            conn.setRequestProperty("Accept", "application/json")
            token?.let { conn.setRequestProperty("Authorization", "Bearer $it") }
            if (body != null) {
                conn.doOutput = true
                conn.setRequestProperty("Content-Type", "application/json")
                conn.outputStream.use { it.write(body.toString().toByteArray(Charsets.UTF_8)) }
            }

            val status = conn.responseCode
            val text = (if (status in 200..299) conn.inputStream else conn.errorStream)
                ?.bufferedReader()?.use { it.readText() } ?: ""
            if (status !in 200..299) {
                val message = runCatching { JSONObject(text).optString("error") }.getOrNull().orEmpty()
                throw SyncHttpException(status, message.ifEmpty { "Request failed" })
            }
            return if (text.isBlank()) JSONObject() else JSONObject(text)
        } finally {
            conn.disconnect()
        }
    }
}
