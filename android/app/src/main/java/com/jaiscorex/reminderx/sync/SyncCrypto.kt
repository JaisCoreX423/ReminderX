package com.jaiscorex.reminderx.sync

import android.util.Base64
import java.security.MessageDigest
import java.security.SecureRandom
import javax.crypto.Cipher
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * AES-256-GCM, identical to the desktop's Services/Sync/SyncCrypto.cs:
 * base64url(nonce(12) | ciphertext | tag(16)), with "kind:id" as associated data.
 */
object SyncCrypto {
    private const val NonceSize = 12
    private const val TagBits = 128
    private val random = SecureRandom()

    fun encrypt(key: ByteArray, kind: String, id: String, plaintext: String): String {
        val nonce = ByteArray(NonceSize).also(random::nextBytes)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(TagBits, nonce))
        cipher.updateAAD("$kind:$id".toByteArray(Charsets.UTF_8))
        return b64(nonce + cipher.doFinal(plaintext.toByteArray(Charsets.UTF_8)))
    }

    /** Throws when the key is wrong or the data was altered. */
    fun decrypt(key: ByteArray, kind: String, id: String, data: String): String {
        val raw = unb64(data)
        val cipher = Cipher.getInstance("AES/GCM/NoPadding")
        cipher.init(Cipher.DECRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(TagBits, raw, 0, NonceSize))
        cipher.updateAAD("$kind:$id".toByteArray(Charsets.UTF_8))
        return String(cipher.doFinal(raw, NonceSize, raw.size - NonceSize), Charsets.UTF_8)
    }

    fun b64(bytes: ByteArray): String = Base64.encodeToString(bytes, Base64.URL_SAFE or Base64.NO_PADDING or Base64.NO_WRAP)

    fun unb64(text: String): ByteArray = Base64.decode(text, Base64.URL_SAFE or Base64.NO_PADDING or Base64.NO_WRAP)

    fun hash(text: String): String =
        MessageDigest.getInstance("SHA-256").digest(text.toByteArray(Charsets.UTF_8)).joinToString("") { "%02x".format(it) }.take(32)
}
