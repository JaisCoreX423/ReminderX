package com.jaiscorex.reminderx.ui

import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.compose.foundation.Image
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.jaiscorex.reminderx.R
import com.jaiscorex.reminderx.sync.SyncEngine
import com.journeyapps.barcodescanner.ScanContract
import com.journeyapps.barcodescanner.ScanOptions
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

/** First run: link this phone to ReminderX on the PC by scanning its QR (Settings > Phone > Link a phone). */
@Composable
fun PairScreen(lastError: String?) {
    val scope = rememberCoroutineScope()
    var busy by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf(lastError) }

    val scanner = rememberLauncherForActivityResult(ScanContract()) { result ->
        val qr = result.contents ?: return@rememberLauncherForActivityResult
        busy = true
        error = null
        scope.launch {
            try {
                withContext(Dispatchers.IO) { SyncEngine.pair(qr) }
                SyncEngine.syncNow()
            } catch (e: Exception) {
                error = e.message ?: "Couldn't link this phone."
            } finally {
                busy = false
            }
        }
    }

    Column(
        modifier = Modifier.fillMaxSize().background(MaterialTheme.colorScheme.background).systemBarsPadding().padding(28.dp),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Image(
            painter = painterResource(R.mipmap.ic_launcher),
            contentDescription = null,
            modifier = Modifier.size(96.dp).clip(RoundedCornerShape(22.dp)),
        )
        Spacer(Modifier.height(20.dp))
        Text("ReminderX", fontSize = 28.sp, fontWeight = FontWeight.Bold, color = MaterialTheme.colorScheme.onBackground)
        Spacer(Modifier.height(10.dp))
        Text(
            "Link this phone to ReminderX on your PC to keep reminders and to-dos in sync.",
            textAlign = TextAlign.Center, color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Spacer(Modifier.height(28.dp))
        Card {
            Text("On your PC", fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface)
            Spacer(Modifier.height(6.dp))
            Caption("Open ReminderX > Settings > Phone > Link a phone. A QR code appears; scan it with the button below.")
        }
        Spacer(Modifier.height(24.dp))

        if (busy) {
            CircularProgressIndicator()
        } else {
            Button(
                onClick = {
                    scanner.launch(
                        ScanOptions()
                            .setDesiredBarcodeFormats(ScanOptions.QR_CODE)
                            .setPrompt("Scan the code shown by ReminderX on your PC")
                            .setBeepEnabled(false)
                            .setOrientationLocked(true),
                    )
                },
                modifier = Modifier.fillMaxWidth().height(52.dp),
                shape = RoundedCornerShape(14.dp),
                colors = ButtonDefaults.buttonColors(containerColor = Accent),
            ) {
                Text("Scan the QR code", fontSize = 16.sp)
            }
        }

        error?.let {
            Spacer(Modifier.height(16.dp))
            Text(it, color = Danger, textAlign = TextAlign.Center)
        }
        Spacer(Modifier.height(16.dp))
        Caption("Everything travels end-to-end encrypted with a key that only your PC and this phone have.")
    }
}
