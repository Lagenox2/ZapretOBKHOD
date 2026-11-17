#!/bin/sh
# Установка ZapretOBKHOD на маршрутизатор с BusyBox.
# Запускайте: sh install.sh   (из папки с распакованным архивом)

SRC=$(cd "$(dirname "$0")" && pwd)
NV=$(command -v nvram 2>/dev/null)
[ -z "$NV" ] && [ -x /sbin/nvram ] && NV=/sbin/nvram

TARGET=""
for d in /jffs /opt /etc /tmp; do
    if [ -d "$d" ] && [ -w "$d" ]; then
        TARGET="$d/zapret-obkhod"
        break
    fi
done

if [ -z "$TARGET" ]; then
    echo "Не найден каталог для записи (/jffs, /opt, /etc, /tmp)."
    exit 1
fi

echo "Установка в: $TARGET"
mkdir -p "$TARGET" || exit 1
if [ "$SRC" != "$TARGET" ]; then
    cp -R "$SRC/." "$TARGET/" || exit 1
fi
chmod +x "$TARGET"/*.sh "$TARGET"/web/cgi-bin/*.cgi "$TARGET"/bin/* 2>/dev/null

# --- автозапуск -----------------------------------------------------------
kind="none"
if [ -f /etc/openwrt_release ] && [ -f /etc/config/network ]; then
    kind="openwrt"
elif [ -d /opt/etc/init.d ] && [ -x /opt/etc/init.d/rc.unslung ]; then
    kind="entware"
elif [ -n "$NV" ] && [ -n "$("$NV" get tomato_ver 2>/dev/null)" ]; then
    kind="tomato"
elif [ -f /etc/banner ] && grep -qi 'dd-wrt' /etc/banner 2>/dev/null; then
    kind="ddwrt"
elif [ -n "$NV" ] && [ -n "$("$NV" get jffs2_exec 2>/dev/null)" ]; then
    kind="tomato"
fi

case "$kind" in
    tomato)
        old=$("$NV" get jffs2_exec 2>/dev/null)
        printf '%s\n' "$old" > "$TARGET/autostart.old"
        printf '#!/bin/sh\n"%s/zapret.sh" boot\n' "$TARGET" > "$TARGET/autostart.sh"
        chmod +x "$TARGET/autostart.sh"
        "$NV" set jffs2_exec="$TARGET/autostart.sh"
        "$NV" commit
        echo "$kind" > "$TARGET/autostart.kind"
        echo "Автозапуск: Tomato/ASUS (jffs2_exec)."
        ;;
    ddwrt)
        old=$("$NV" get rc_startup 2>/dev/null)
        printf '%s\n' "$old" > "$TARGET/autostart.old"
        "$NV" set rc_startup="$TARGET/zapret.sh boot"
        "$NV" commit
        echo "$kind" > "$TARGET/autostart.kind"
        echo "Автозапуск: DD-WRT (rc_startup)."
        ;;
    openwrt)
        mkdir -p /etc/init.d /etc/rc.d
        ln -sf "$TARGET/zapret.sh" /etc/init.d/zapret-obkhod
        ln -sf ../init.d/zapret-obkhod /etc/rc.d/S95zapret-obkhod
        echo "$kind" > "$TARGET/autostart.kind"
        echo "Автозапуск: OpenWrt (/etc/rc.d/S95zapret-obkhod)."
        ;;
    entware)
        mkdir -p /opt/etc/init.d
        cat > /opt/etc/init.d/S95zapret-obkhod <<EOF
#!/bin/sh
"$TARGET/zapret.sh" "\$1"
EOF
        chmod +x /opt/etc/init.d/S95zapret-obkhod
        echo "$kind" > "$TARGET/autostart.kind"
        echo "Автозапуск: Entware (/opt/etc/init.d/S95zapret-obkhod)."
        ;;
    *)
        echo "Автозапуск не распознан. Включите его вручную по инструкции из README.md."
        ;;
esac

echo ""
echo "Готово. Запуск: sh $TARGET/zapret.sh start"
echo "Проверка:     sh $TARGET/zapret.sh status"
echo "Web:          http://IP-РОУТЕРА:81/"
echo "После установки полезно перезагрузить маршрутизатор."
