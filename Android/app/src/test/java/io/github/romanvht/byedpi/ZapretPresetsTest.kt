package io.github.romanvht.byedpi

import io.github.romanvht.byedpi.utility.shellSplit
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class ZapretPresetsTest {

    private val lists = mapOf(
        "youtube" to listOf("youtube.com", "youtu.be", "i.ytimg.com"),
        "googlevideo" to listOf("googlevideo.com", "googleusercontent.com"),
        "discord" to listOf("discord.com", "discord.gg", "discordapp.com"),
        "telegram" to listOf("telegram.org", "t.me", "web.telegram.org"),
        "social" to listOf("instagram.com", "facebook.com", "x.com"),
        "custom" to listOf("example.com"),
    )

    private fun expand(command: String): String =
        Regex("""\{list:([^}]+)\}""").replace(command) { match ->
            lists[match.groupValues[1]]?.joinToString(" ") ?: ""
        }

    @Test
    fun presetsAreUniqueAndHaveNames() {
        val names = ZapretPresets.all.map { it.name }
        val commands = ZapretPresets.all.map { it.args }
        assertEquals(names.size, names.distinct().size)
        assertEquals(commands.size, commands.distinct().size)
        assertTrue(names.all { it.isNotBlank() })
        assertEquals(ZapretPresets.all.first(), ZapretPresets.default)
    }

    @Test
    fun firstPresetIsComboOfAllThreeServices() {
        val args = ZapretPresets.default.args
        assertTrue(args.contains("{list:youtube}"))
        assertTrue(args.contains("{list:discord}"))
        assertTrue(args.contains("{list:telegram}"))
    }

    @Test
    fun expandedCommandKeepsHostListAsSingleToken() {
        for (preset in ZapretPresets.all) {
            val tokens = shellSplit(expand(preset.args))
            assertTrue("no tokens for ${preset.name}", tokens.isNotEmpty())
            assertTrue("command must start with option: ${preset.name}", tokens.first().startsWith("-"))
            assertTrue("bad option in ${preset.name}: ${tokens.first()}", tokens.first() == "-o1" || tokens.first() == "-s1")
            val hostToken = tokens.firstOrNull { it.startsWith("-H:") }
            if (hostToken != null) {
                assertTrue(
                    "host list must contain expanded domains: ${preset.name} -> $hostToken",
                    hostToken.substringAfter("-H:").contains(".")
                )
            }
        }
    }

    @Test
    fun softPresetOnlySplitsWithoutFakePackets() {
        val soft = ZapretPresets.all.first { it.name.startsWith("Мягкий") }
        val tokens = shellSplit(soft.args)
        assertTrue(tokens.contains("-s1"))
        assertTrue(tokens.contains("-Kt"))
        assertTrue(tokens.none { it.startsWith("-f") || it.startsWith("-r") || it.startsWith("-o") })
    }
}
