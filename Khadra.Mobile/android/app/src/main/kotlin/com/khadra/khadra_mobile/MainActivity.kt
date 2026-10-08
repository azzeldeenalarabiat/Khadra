package com.khadra.khadra_mobile

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.util.Log
import io.flutter.embedding.android.FlutterActivity
import org.json.JSONObject
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class MainActivity : FlutterActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        PushIntentTrace.record(this, "native-create", intent)
        super.onCreate(savedInstanceState)
    }

    override fun onNewIntent(intent: Intent) {
        PushIntentTrace.record(this, "native-new-intent", intent)
        super.onNewIntent(intent)
    }
}

/**
 * TEMPORARY - STAGING BUILDS ONLY (the W4-9 device investigation, 2026-10-08). Remove with the
 * Dart side's PushTrace once a push tap is verified on a phone (pre-launch checklist item 27).
 *
 * Records what Android hands this activity when it is created or brought back by an intent, BEFORE
 * FlutterFire or the local-notification plugin sees it: whether a tap on a notification reaches the
 * app at all, and in which shape. Written where the Dart trace screen reads it (the plugin
 * shared_preferences' own file and "flutter." prefix) and to logcat under "KhadraPushTrace".
 *
 * NEVER a value but the push's `kind`: extras are listed by their key names, the FCM message id
 * only as present or absent, and a local notification's payload only for its `kind`. A production
 * build (no ".staging" in its application id) records nothing.
 */
object PushIntentTrace {
    private const val TAG = "KhadraPushTrace"
    private const val PREFERENCES = "FlutterSharedPreferences"
    private const val KEY = "flutter.khadra.push_trace.native"
    private const val MAX_LINES = 60
    private val KIND = Regex("^[A-Za-z]{1,64}$")

    fun record(context: Context, event: String, intent: Intent?) {
        if (!context.packageName.endsWith(".staging")) return
        try {
            val line = describe(event, intent)
            Log.i(TAG, line)
            val preferences = context.getSharedPreferences(PREFERENCES, Context.MODE_PRIVATE)
            val lines = (preferences.getString(KEY, "") ?: "").split("\n").filter { it.isNotEmpty() } + line
            preferences.edit().putString(KEY, lines.takeLast(MAX_LINES).joinToString("\n")).apply()
        } catch (error: Exception) {
            Log.w(TAG, "$event not recorded: ${error.javaClass.simpleName}")
        }
    }

    private fun describe(event: String, intent: Intent?): String {
        val time = SimpleDateFormat("HH:mm:ss.SSS", Locale.US).format(Date())
        if (intent == null) return "$time $event intent=none"
        val extras = intent.extras
        val keys = extras?.keySet()?.sorted()?.joinToString(",") ?: ""
        val fcm = extras?.containsKey("google.message_id") == true || extras?.containsKey("message_id") == true
        var kind = extras?.getString("kind")
        val local = intent.action == "SELECT_NOTIFICATION" || intent.action == "SELECT_FOREGROUND_NOTIFICATION"
        if (kind == null && local) {
            kind = try {
                JSONObject(extras?.getString("payload") ?: "{}").optString("kind").ifEmpty { null }
            } catch (error: Exception) {
                "(payload unreadable)"
            }
        }
        val shownKind = when {
            kind == null -> "(none)"
            KIND.matches(kind) -> kind
            else -> "(not a kind)"
        }
        val fromHistory = (intent.flags and Intent.FLAG_ACTIVITY_LAUNCHED_FROM_HISTORY) != 0
        return "$time $event action=${intent.action ?: "(none)"} fcm=$fcm local=$local " +
            "history=$fromHistory keys=[$keys] kind=$shownKind"
    }
}
