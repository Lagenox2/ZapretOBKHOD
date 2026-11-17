package io.github.romanvht.byedpi

import android.app.Application
import android.content.SharedPreferences
import android.util.Log
import androidx.core.content.edit
import io.github.romanvht.byedpi.data.Command
import io.github.romanvht.byedpi.utility.DomainListUtils
import io.github.romanvht.byedpi.utility.HistoryUtils
import io.github.romanvht.byedpi.utility.getCmdEnable
import io.github.romanvht.byedpi.utility.getPreferences

/**
 * Точка входа приложения. При первом запуске:
 *  - синхронизирует встроенные списки доменов;
 *  - добавляет готовые профили ZapretOBKHOD в закреплённые стратегии;
 *  - делает профиль «Комбо» стратегией по умолчанию.
 */
class ZapretApp : Application() {

    override fun onCreate() {
        super.onCreate()

        val preferences = getPreferences()
        if (preferences.getInt(ZapretPresets.SEEDED_KEY, 0) >= ZapretPresets.SEED_VERSION) {
            return
        }

        try {
            DomainListUtils.syncLists(this)
            seedPresets(preferences)
            preferences.edit {
                putInt(ZapretPresets.SEEDED_KEY, ZapretPresets.SEED_VERSION)
            }
        } catch (error: Throwable) {
            // Не блокируем запуск приложения из-за проблем с первичной настройкой.
            Log.e(TAG, "Failed to seed ZapretOBKHOD presets", error)
        }
    }

    private fun seedPresets(preferences: SharedPreferences) {
        val historyUtils = HistoryUtils(this)
        val history = historyUtils.getHistory().toMutableList()

        for (preset in ZapretPresets.all) {
            val existing = history.find { it.text == preset.args }
            if (existing == null) {
                history.add(Command(preset.args, pinned = true, name = preset.name))
            } else {
                existing.pinned = true
                if (existing.name.isNullOrBlank()) {
                    existing.name = preset.name
                }
            }
        }

        historyUtils.saveHistory(history)

        if (!preferences.contains(CMD_ARGS_KEY)) {
            preferences.edit {
                putString(CMD_ARGS_KEY, ZapretPresets.default.args)
                putBoolean(CMD_ENABLE_KEY, true)
            }
        } else if (!preferences.getCmdEnable()) {
            preferences.edit { putBoolean(CMD_ENABLE_KEY, true) }
        }
    }

    private companion object {
        private const val TAG = "ZapretApp"
        private const val CMD_ARGS_KEY = "byedpi_cmd_args"
        private const val CMD_ENABLE_KEY = "byedpi_enable_cmd_settings"
    }
}
