package io.github.romanvht.byedpi

/**
 * Набор готовых профилей ZapretOBKHOD.
 *
 * Профили хранятся в истории команд ByeDPI как закреплённые записи, поэтому
 * появляются в штатном списке стратегий приложения. Плейсхолдеры {list:...}
 * раскрываются в домены из соответствующего списка (см. DomainListUtils).
 */
object ZapretPresets {

    /** Версия набора. Меняется, если нужно повторно засеять профили при обновлении. */
    const val SEED_VERSION = 1

    const val SEEDED_KEY = "zapretobkhod_presets_seeded"

    data class Preset(val name: String, val args: String)

    private const val BASE = "-o1 -a1 -r-5+se"

    val all: List<Preset> = listOf(
        Preset(
            "Комбо: YouTube + Discord + Telegram",
            "$BASE -H:\"{list:youtube} {list:googlevideo} {list:discord} {list:telegram}\""
        ),
        Preset(
            "Только YouTube",
            "$BASE -H:\"{list:youtube} {list:googlevideo}\""
        ),
        Preset(
            "Только Discord",
            "$BASE -H:\"{list:discord}\""
        ),
        Preset(
            "Telegram + весь HTTPS",
            "$BASE -Kt"
        ),
        Preset(
            "Соцсети и медиа",
            "$BASE -H:\"{list:social} {list:discord}\""
        ),
        Preset(
            "Все сайты (агрессивно)",
            BASE
        ),
        Preset(
            "Мягкий режим (только разделение)",
            "-s1 -Kt"
        ),
        Preset(
            "Свой список сайтов",
            "$BASE -H:\"{list:custom}\""
        )
    )

    val default: Preset get() = all.first()
}
