#!/bin/sh
# ZapretOBKHOD dlya BusyBox-routerov (ASUS RT-N16 i analogov).
# Sovmestim s ash/dash. Ne ispolzuet bash, su, python i vneshnih serverov.

SELF="$0"
BASE=$(cd "$(dirname "$SELF")" && pwd)
[ -f "$BASE/config.sh" ] && . "$BASE/config.sh"
[ -f "$BASE/state" ] && . "$BASE/state"
[ -f "$BASE/presets.sh" ] && . "$BASE/presets.sh"

PIDFILE=/tmp/zobkhod.pid
HTTPID=/tmp/zobkhod-httpd.pid
WORK=/tmp/zobkhod
LANSAVE=/tmp/zobkhod-lan
WANSAVE=/tmp/zobkhod-wan

find_cmd() {
    c=$(command -v "$1" 2>/dev/null)
    [ -n "$c" ] && { echo "$c"; return 0; }
    for d in /sbin /usr/sbin /bin /usr/bin; do
        [ -x "$d/$1" ] && { echo "$d/$1"; return 0; }
    done
    return 1
}

IPT=$(find_cmd iptables)
LOGGER=$(find_cmd logger)
HTTPD=$(find_cmd httpd)
NV=$(find_cmd nvram)

log() {
    msg="$*"
    [ -n "$LOGGER" ] && "$LOGGER" -t zapret-obkhod "$msg" 2>/dev/null
    echo "[ZapretOBKHOD] $msg"
}

find_binary() {
    name="$1"
    for arch in mipsel mips; do
        f="$BASE/bin/$name-$arch"
        [ -x "$f" ] || continue
        "$f" --version >/dev/null 2>&1 && { echo "$f"; return 0; }
    done
    return 1
}

iface_lan() {
    if [ -n "$LAN_IFACE" ]; then
        echo "$LAN_IFACE"
        return 0
    fi
    if [ -f "$LANSAVE" ]; then
        saved=$(cat "$LANSAVE" 2>/dev/null)
        [ -n "$saved" ] && { echo "$saved"; return 0; }
    fi
    if [ -n "$NV" ]; then
        saved=$("$NV" get lan_ifname 2>/dev/null)
        [ -n "$saved" ] && { echo "$saved"; return 0; }
    fi
    for i in br0 br-lan; do
        [ -d "/sys/class/net/$i" ] && { echo "$i"; return 0; }
    done
    echo br0
}

iface_wan() {
    if [ -n "$WAN_IFACE" ]; then
        echo "$WAN_IFACE"
        return 0
    fi
    if [ -f "$WANSAVE" ]; then
        saved=$(cat "$WANSAVE" 2>/dev/null)
        [ -n "$saved" ] && { echo "$saved"; return 0; }
    fi
    if [ -n "$NV" ]; then
        saved=$("$NV" get wan_ifname 2>/dev/null)
        [ -n "$saved" ] && { echo "$saved"; return 0; }
    fi
    saved=$(route -n 2>/dev/null | awk '$1 == "0.0.0.0" { print $NF; exit }')
    [ -n "$saved" ] && { echo "$saved"; return 0; }
    echo ""
}

lan_ip() {
    lan=$(iface_lan)
    ip=$(ifconfig "$lan" 2>/dev/null | sed -n \
        -e 's/.*inet addr:\([0-9.]*\).*/\1/p' \
        -e 's/.*inet \([0-9.]*\)\/.*/\1/p' | head -n 1)
    if [ -z "$ip" ] && command -v ip >/dev/null 2>&1; then
        ip=$(ip -4 addr show dev "$lan" 2>/dev/null | awk '/inet / { sub(/\/.*/, "", $2); print $2; exit }')
    fi
    echo "$ip"
}

is_running() {
    [ -f "$PIDFILE" ] || return 1
    pid=$(cat "$PIDFILE" 2>/dev/null)
    [ -n "$pid" ] && [ -d "/proc/$pid" ] && return 0
    return 1
}

prepare_lists() {
    mkdir -p "$WORK" 2>/dev/null
    HOSTS="$WORK/hosts.txt"
    EXCL="$WORK/exclude.txt"
    : > "$HOSTS"
    : > "$EXCL"

    case "$PRESET" in
        combo)
            cat "$BASE/lists/youtube.txt" "$BASE/lists/discord.txt" "$BASE/lists/telegram.txt" >> "$HOSTS" 2>/dev/null
            ;;
        youtube)  cat "$BASE/lists/youtube.txt"  >> "$HOSTS" 2>/dev/null ;;
        discord)  cat "$BASE/lists/discord.txt"  >> "$HOSTS" 2>/dev/null ;;
        telegram) cat "$BASE/lists/telegram.txt" >> "$HOSTS" 2>/dev/null ;;
        media)    cat "$BASE/lists/media.txt"    >> "$HOSTS" 2>/dev/null ;;
        custom)   cat "$BASE/lists/custom.txt"   >> "$HOSTS" 2>/dev/null ;;
    esac

    if [ "$PRESET" = custom ] && [ ! -s "$HOSTS" ]; then
        log "lists/custom.txt пуст — добавьте туда домены"
        return 1
    fi

    [ "$EXCLUDE_RU" = 1 ] && cat "$BASE/lists/exclude.txt" >> "$EXCL" 2>/dev/null
    return 0
}

# $1 = A (добавить) или D (удалить). Используем одинаковые аргументы в обоих случаях,
# чтобы удаление гарантированно снимало именно наши правила.
fw_rules() {
    op="$1"
    [ -n "$IPT" ] || return 0

    lan=$(iface_lan)
    wan=$(iface_wan)
    echo "$lan" > "$LANSAVE" 2>/dev/null
    [ -n "$wan" ] && echo "$wan" > "$WANSAVE" 2>/dev/null

    if [ "$MODE" = tpws ]; then
        "$IPT" -t nat -"$op" PREROUTING -i "$lan" -p tcp --dport 80  -j REDIRECT --to-ports "$PORT" >/dev/null 2>&1
        "$IPT" -t nat -"$op" PREROUTING -i "$lan" -p tcp --dport 443 -j REDIRECT --to-ports "$PORT" >/dev/null 2>&1
        [ "$BLOCK_QUIC" = 1 ] && \
            "$IPT" -"$op" FORWARD -i "$lan" -p udp --dport 443 -j DROP >/dev/null 2>&1
    else
        "$IPT" -t mangle -"$op" FORWARD -i "$lan" -p tcp --dport 80  -j NFQUEUE --queue-num 200 --queue-bypass >/dev/null 2>&1
        "$IPT" -t mangle -"$op" FORWARD -i "$lan" -p tcp --dport 443 -j NFQUEUE --queue-num 200 --queue-bypass >/dev/null 2>&1
        "$IPT" -t mangle -"$op" FORWARD -i "$lan" -p udp --dport 443 -j NFQUEUE --queue-num 200 --queue-bypass >/dev/null 2>&1
        if [ -n "$wan" ]; then
            "$IPT" -t mangle -"$op" FORWARD -i "$wan" -p tcp --sport 80  -j NFQUEUE --queue-num 200 --queue-bypass >/dev/null 2>&1
            "$IPT" -t mangle -"$op" FORWARD -i "$wan" -p tcp --sport 443 -j NFQUEUE --queue-num 200 --queue-bypass >/dev/null 2>&1
        fi
    fi
}

web_start() {
    [ "$WEB_UI" = 1 ] || return 0
    [ -n "$HTTPD" ] || return 0
    if [ -f "$HTTPID" ]; then
        p=$(cat "$HTTPID" 2>/dev/null)
        [ -n "$p" ] && kill -0 "$p" 2>/dev/null && return 0
    fi
    "$HTTPD" -f -h "$BASE/web" -p "$WEB_PORT" >/dev/null 2>&1 &
    echo $! > "$HTTPID"
}

web_stop() {
    if [ -f "$HTTPID" ]; then
        p=$(cat "$HTTPID" 2>/dev/null)
        [ -n "$p" ] && kill "$p" 2>/dev/null
        rm -f "$HTTPID"
    fi
}

do_start() {
    if is_running; then
        echo "ZapretOBKHOD уже включён."
        return 0
    fi

    prepare_lists || return 1

    BIN=$(find_binary "$MODE")
    if [ -z "$BIN" ]; then
        log "не найден рабочий бинарник для режима $MODE"
        return 1
    fi

    build_args "$MODE" "$PRESET" "$HOSTS" "$EXCL" "$BASE/fake"
    if [ -z "$ARGS" ]; then
        log "не удалось собрать параметры профиля"
        return 1
    fi

    fw_rules D
    fw_rules A

    if [ "$MODE" = tpws ]; then
        "$BIN" --bind-addr=0.0.0.0 --port="$PORT" $ARGS \
            --pidfile="$PIDFILE" --daemon >/dev/null 2>&1
    else
        "$BIN" --qnum=200 $ARGS \
            --pidfile="$PIDFILE" --daemon >/dev/null 2>&1
    fi

    sleep 1
    if ! is_running; then
        fw_rules D
        log "движок не запустился ($BIN). Проверьте режим и поддержку NFQUEUE."
        return 1
    fi

    web_start
    log "включён: профиль $(preset_name "$PRESET"), режим $MODE, LAN $(iface_lan)"
    echo "ZapretOBKHOD включён: $(preset_name "$PRESET")"
}

do_stop() {
    if is_running; then
        pid=$(cat "$PIDFILE" 2>/dev/null)
        kill "$pid" 2>/dev/null
        i=0
        while [ "$i" -lt 5 ]; do
            is_running || break
            sleep 1
            i=$((i + 1))
        done
        is_running && kill -9 "$pid" 2>/dev/null
    fi
    rm -f "$PIDFILE"
    fw_rules D
    web_stop
    log "выключен"
    echo "ZapretOBKHOD выключен."
}

do_status() {
    if is_running; then
        echo 'Состояние: включён'
    else
        echo 'Состояние: выключен'
    fi
    echo "Профиль: $(preset_name "$PRESET")"
    echo "Режим: $MODE"
    echo "LAN: $(iface_lan)"
    if [ "$WEB_UI" = 1 ]; then
        ip=$(lan_ip)
        if [ -n "$ip" ]; then
            echo "Web: http://$ip:$WEB_PORT/"
        else
            echo "Web: порт $WEB_PORT (IP LAN не определён)"
        fi
    fi
}

do_preset() {
    id="$1"
    if ! preset_valid "$id"; then
        echo "Неизвестный профиль: $id" >&2
        return 1
    fi
    printf 'PRESET=%s\n' "$id" > "$BASE/state"
    if is_running; then
        do_stop >/dev/null
        do_start
    else
        echo "Профиль сохранён: $(preset_name "$id")"
    fi
}

autostart_off() {
    if [ -f "$BASE/autostart.kind" ]; then
        kind=$(cat "$BASE/autostart.kind" 2>/dev/null)
        old=""
        [ -f "$BASE/autostart.old" ] && old=$(cat "$BASE/autostart.old" 2>/dev/null)
        case "$kind" in
            tomato)
                [ -n "$NV" ] && "$NV" set jffs2_exec="$old" >/dev/null 2>&1 && "$NV" commit >/dev/null 2>&1
                ;;
            ddwrt)
                [ -n "$NV" ] && "$NV" set rc_startup="$old" >/dev/null 2>&1 && "$NV" commit >/dev/null 2>&1
                ;;
        esac
        rm -f "$BASE/autostart.kind" "$BASE/autostart.old" "$BASE/autostart.sh"
    fi
    rm -f /etc/init.d/zapret-obkhod /etc/rc.d/S95zapret-obkhod /opt/etc/init.d/S95zapret-obkhod
    echo "Автозапуск отключён. Каталог $BASE оставлен на месте."
}

usage() {
    cat <<'EOF'
Использование: sh zapret.sh <команда>

  start       включить обход
  stop        выключить обход и снять правила iptables
  restart     перезапустить
  status      состояние и текущий профиль
  preset ID   сменить профиль (combo|youtube|discord|telegram|media|all|soft|custom)
  webstart    запустить веб-интерфейс
  webstop     остановить веб-интерфейс
  autostart-off  убрать автозапуск
  boot        то же, что start (используется при загрузке роутера)
EOF
}

case "$1" in
    start|boot)   do_start ;;
    stop)         do_stop ;;
    restart)      do_stop >/dev/null; do_start ;;
    status)       do_status ;;
    preset)       shift; do_preset "$1" ;;
    webstart)     web_start; echo 'Web-интерфейс запущен.' ;;
    webstop)      web_stop; echo 'Web-интерфейс остановлен.' ;;
    autostart-off) autostart_off ;;
    *)            usage ;;
esac
