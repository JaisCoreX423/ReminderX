package com.jaiscorex.reminderx.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.jaiscorex.reminderx.data.Format
import com.jaiscorex.reminderx.data.Store

@Composable
fun HistoryScreen() {
    val data by Store.data.collectAsState()
    LazyColumn(contentPadding = PaddingValues(start = 16.dp, end = 16.dp, bottom = 24.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
        item { ScreenTitle("History") }
        if (data.history.isEmpty()) item { Caption("Nothing yet.", Modifier.padding(20.dp)) }
        items(data.history.take(300)) { h ->
            Card {
                Text(h.title, fontWeight = FontWeight.SemiBold, color = MaterialTheme.colorScheme.onSurface)
                Caption(listOf(h.action.name, h.note).filter { it.isNotBlank() }.joinToString(" · ") + "  ·  " + Format.friendly(h.at))
            }
        }
    }
}
