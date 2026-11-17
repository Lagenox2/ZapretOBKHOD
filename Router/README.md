# ZapretOBKHOD для BusyBox-роутеров (ASUS RT-N16)

Версия для маршрутизаторов на Linux + BusyBox: **без VPN, без внешних серверов**.
Трафик идёт напрямую через провайдера, роутер лишь корректирует пакеты так, чтобы
оборудование провайдера не распознавало TLS/HTTP.

Рассчитана на **ASUS RT-N16** (MIPS 74K, little-endian) и совместимые устройства
с прошивками на BusyBox: Tomato, DD-WRT, OpenWrt, Entware, стоковая ASUS с JFFS.

> **Простую пошаговую инструкцию** для схемы «один кабель входит, один выходит»
> читайте в [QUICKSTART.md](QUICKSTART.md).

> Проверка на живом RT-N16 в этой среде не выполнялась: нет доступа к устройству.
> Скрипты и бинарники проверены статически; перед установкой прочитайте раздел
> «Если не работает».

---

## Что внутри

```
zapret-obkhod/
  install.sh          установка и настройка автозапуска
  zapret.sh           управление: start/stop/status/preset
  config.sh           настройки (режим, порт, LAN, QUIC, web)
  presets.sh          профили для tpws и nfqws
  bin/
    tpws-mipsel       прозрачный прокси, little-endian (основной)
    nfqws-mipsel      движок через NFQUEUE, little-endian
    tpws-mips         то же для big-endian (резерв)
    nfqws-mips        то же для big-endian (резерв)
  lists/              списки доменов (youtube, discord, telegram, media,
                      exclude, custom)
  fake/               фейковые пакеты для nfqws (QUIC, Discord voice)
  web/                веб-интерфейс для BusyBox httpd
  CHECKSUMS.sha256    контрольные суммы бинарников
```

Бинарники — из официального релиза проекта
[zapret v72.13](https://github.com/bol-van/zapret), лицензия GPL-3.0
(см. `LICENSE-zapret.txt`). Они статические (musl), поэтому не требуют
библиотек прошивки.

---

## Требования

* ASUS RT-N16 или другой роутер с MIPS и Linux/BusyBox.
* Включённый SSH или Telnet.
* Раздел JFFS (или Entware/OpenWrt overlay) — около **2 МБ** свободно.
* `iptables` в прошивке (есть в Tomato, DD-WRT, OpenWrt; в стоковой ASUS —
  обычно есть).
* Для режима `nfqws` — поддержка `NFQUEUE` (`iptables-mod-nfqueue`).
  Если её нет, используйте `tpws` (по умолчанию).

RT-N16 имеет 32 МБ RAM и 8 МБ flash, поэтому всё хранение — в JFFS,
рабочие файлы и PID — в `/tmp` (tmpfs), чтобы не изнашивать flash.

---

## Установка

1. Включите JFFS в веб-интерфейсе прошивки (Tomato: **Administration → JFFS**;
   DD-WRT: **Administration → JFFS2 Support**).
2. Скопируйте папку `Router` на роутер, например в `/jffs/`:
   * WinSCP/SCP: протокол SCP, путь `/jffs/`;
   * или через `scp -r Router admin@192.168.1.1:/jffs/`.
3. Подключитесь по SSH и выполните:

```sh
cd /jffs/Router
sh install.sh
```

Скрипт скопирует файлы в `/jffs/zapret-obkhod` (или `/opt/...`, `/etc/...`,
`/tmp/...` — что доступно), выставит права и попробует настроить автозапуск.

4. Запустите:

```sh
sh /jffs/zapret-obkhod/zapret.sh start
sh /jffs/zapret-obkhod/zapret.sh status
```

5. Перезагрузите роутер и проверьте, что обход включился сам:

```sh
sh /jffs/zapret-obkhod/zapret.sh status
```

---

## Команды

```sh
sh zapret.sh start          # включить
sh zapret.sh stop           # выключить и снять правила iptables
sh zapret.sh restart        # перезапустить
sh zapret.sh status         # состояние, профиль, режим, LAN
sh zapret.sh preset youtube # сменить профиль (с перезапуском)
sh zapret.sh webstart       # поднять веб-интерфейс
sh zapret.sh webstop        # остановить веб-интерфейс
sh zapret.sh autostart-off  # убрать автозапуск
sh check.sh                 # диагностика: arch, iptables, бинарники, место
```

Профили: `combo`, `youtube`, `discord`, `telegram`, `media`, `all`,
`soft`, `custom`.

---

## Веб-интерфейс

После запуска откройте:

```
http://192.168.1.1:81/
```

(подставьте адрес своего роутера). Интерфейс на русском: включение,
выключение, перезапуск и выбор профиля.

По умолчанию BusyBox `httpd` слушает все интерфейсы, но штатный firewall
роутера не пускает запросы с WAN. Если хотите закрыть доступ полностью,
поставьте в `config.sh`:

```sh
WEB_UI=0
```

и выполните `sh zapret.sh restart`. Порт `81` можно изменить (`WEB_PORT`).

---

## Профили

| Профиль | Что обходит |
|---|---|
| `combo` | YouTube + Discord + Telegram (рекомендуется) |
| `youtube` | только YouTube и Google Video |
| `discord` | сайт и клиент Discord |
| `telegram` | весь HTTPS (Telegram не всегда передаёт домен открыто) |
| `media` | Instagram, Facebook, X/Twitter, Rutracker, Spotify |
| `all` | весь HTTP/HTTPS, кроме списка исключений |
| `soft` | только разделение TLS, без перестановки пакетов |
| `custom` | домены из `lists/custom.txt` |

Исключения (банки, госуслуги, российские сервисы) — `lists/exclude.txt`.
Галочка в веб-интерфейсе отсутствует; включается в `config.sh`:

```sh
EXCLUDE_RU=1
```

Свои сайты:

```sh
vi /jffs/zapret-obkhod/lists/custom.txt
```

по одному домену на строку, затем `sh zapret.sh preset custom`.

---

## Режимы

### `tpws` (по умолчанию)

Прозрачный TCP-прокси. Правила iptables перенаправляют исходящий TCP
80/443 из LAN на локальный порт `988`; `tpws` меняет TLS/HTTP-пакеты
и отправляет их дальше напрямую.

Плюсы: не нужен NFQUEUE, работает на старых ядрах 2.6, мало ест RAM.
Минусы: **не умеет UDP** — QUIC блокируется, чтобы браузеры перешли на TCP;
голосовой UDP Discord не обрабатывается.

### `nfqws` (продвинутый)

Те же профили, что в Windows-версии: fake-пакеты, `multisplit`,
`multidisorder`, QUIC и голос Discord. Требует NFQUEUE и модулей ядра.

```sh
# в config.sh
MODE=nfqws
```

и перезапуск. Если NFQUEUE нет, движок не стартует и правила снимаются
автоматически — трафик не останется заблокированным.

Блокировка QUIC (`BLOCK_QUIC=1`) работает в обоих режимах: в `tpws`
она обязательна, в `nfqws` — по желанию.

---

## Автозапуск

`install.sh` определяет прошивку и настраивает:

* **Tomato / ASUS**: `nvram set jffs2_exec=/jffs/zapret-obkhod/autostart.sh`;
* **DD-WRT**: `nvram set rc_startup=/jffs/zapret-obkhod/zapret.sh boot`;
* **OpenWrt**: симлинки `/etc/init.d/zapret-obkhod` и
  `/etc/rc.d/S95zapret-obkhod`;
* **Entware**: `/opt/etc/init.d/S95zapret-obkhod`.

Старое значение `jffs2_exec`/`rc_startup` сохраняется в
`autostart.old`; команда `autostart-off` возвращает его обратно.

Если автозапуск не распознан, добавьте вручную в раздел инициализации
прошивки строку:

```sh
/jffs/zapret-obkhod/zapret.sh boot
```

---

## Как это работает

```
LAN-клиент → iptables (nat/PREROUTING) → tpws:988 → провайдер → сайт
```

* `tpws` видит исходный адрес через `SO_ORIGINAL_DST`, поэтому не нужен
  VPN-туннель и внешний прокси;
* правила создаются только для интерфейса LAN и только на время работы;
* при `stop`, ошибке старта или перезагрузке правила снимаются;
* DNAT на `127.0.0.1` не используется — только `REDIRECT`, чтобы не
  зависеть от `route_localnet` на старых ядрах;
* трафик самого роутера не перенаправляется (нет правила `OUTPUT`).

---

## Безопасность

* Нет `su`, root-шелла, `curl`/`wget` и загрузки кода из сети.
* Нет удаления файлов и форматирования; установка пишет только в свой
  каталог и (по желанию) в hook автозапуска.
* Нет внешних серверов, VPN и телеметрии.
* Правила iptables создаются с `--queue-bypass` (в `nfqws`) — если движок
  упадёт, трафик продолжит идти напрямую, а не «в никуда».
* Веб-интерфейс принимает только действия из whitelist; произвольные
  команды через URL выполнить нельзя.
* Для `nfqws` нужен root (как и для любого NFQUEUE-движка); `tpws` тоже
  запускается от root, потому что меняет iptables и слушает порт.

Ограничения: веб-интерфейс без пароля, доступен всей LAN. Если в сети есть
недоверенные устройства, отключите его (`WEB_UI=0`) и управляйте по SSH.

---

## Если не работает

**`Exec format error` / `not found` при запуске бинарника.**
Проверьте архитектуру:

```sh
uname -m
```

Для RT-N16 должна подойти `tpws-mipsel`. Если не запускается ни один
вариант, ядро/ABI несовместимы — соберите бинарник под свою прошивку
(см. ниже) или используйте `nfqws`.

**`iptables: not found`.**
В прошивке нет iptables — этот способ не подойдёт. В OpenWrt установите
`iptables` (или используйте nftables-сборку, но её здесь нет).

**`NFQUEUE` не работает.**
Оставьте `MODE=tpws`. Для `nfqws` нужны `iptables-mod-nfqueue` и
`kmod-nfnetlink-queue`.

**Ничего не открывается, но интернет есть.**
Проверьте `sh zapret.sh status` и лог:

```sh
logread | grep zapret-obkhod | tail -n 30
```

Попробуйте профиль `all` или `soft`. Разные провайдеры используют разные DPI.

**После перезагрузки обход не включился.**
Проверьте hook автозапуска (`nvram get jffs2_exec`, `nvram get rc_startup`)
и что JFFS смонтирован (`mount | grep jffs`).

**Веб-интерфейс не открывается.**
Убедитесь, что `httpd` есть (`command -v httpd`) и `WEB_UI=1`;
порт мог быть занят веб-интерфейсом прошивки — смените `WEB_PORT`.

---

## Удаление

```sh
sh /jffs/zapret-obkhod/zapret.sh autostart-off
sh /jffs/zapret-obkhod/zapret.sh stop
rm -rf /jffs/zapret-obkhod
```

`rm -rf` удаляет только каталог программы. Системные файлы не затрагиваются.

---

## Сборка бинарников под свою прошивку

Если готовые бинарники не подходят, возьмите официальный исходник zapret
и SDK своей прошивки (например, OpenWrt SDK для `brcm47xx/mips74k`,
gcc 7.5, musl), затем:

```sh
cd zapret-*/tpws
make CC=mipsel-openwrt-linux-musl-gcc
```

Готовый `tpws` положите в `bin/tpws-mipsel` вместо штатного. Аналогично
`nfq/nfqws.c` для `nfqws`.

---

## Готовый архив и пересборка

Архив лежит в `dist\ZapretOBKHOD-router.zip` (внутри — папка
`ZapretOBKHOD-router`). Пересобрать его можно из корня проекта:

```powershell
powershell -ExecutionPolicy Bypass -File .\build-router.ps1
```

С ключом `-Download` скрипт повторно скачает бинарники zapret v72.13
с официального релиза; после этого проверяются SHA-256 из `CHECKSUMS.sha256`.

---

## Лицензии

* zapret — GPL-3.0, `LICENSE-zapret.txt`.
* Скрипты и веб-интерфейс ZapretOBKHOD — та же лицензия проекта.
* Контрольные суммы бинарников — `CHECKSUMS.sha256`.
