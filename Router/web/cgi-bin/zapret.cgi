#!/bin/sh
# Веб-управление ZapretOBKHOD через BusyBox httpd.
# Все действия из whitelist: никаких произвольных команд из URL.

BASE=$(cd "$(dirname "$0")/.." && pwd)
[ -f "$BASE/presets.sh" ] && . "$BASE/presets.sh"

Q=${QUERY_STRING:-}
get_q() {
    printf '%s\n' "$Q" | tr '&' '\n' | sed -n "s/^$1=//p" | head -n 1
}

action=$(get_q action)
id=$(get_q id)

case "$action" in
    start)   "$BASE/zapret.sh" start   >/dev/null 2>&1 ;;
    stop)    "$BASE/zapret.sh" stop    >/dev/null 2>&1 ;;
    restart) "$BASE/zapret.sh" restart >/dev/null 2>&1 ;;
    preset)
        if preset_valid "$id"; then
            "$BASE/zapret.sh" preset "$id" >/dev/null 2>&1
        fi
        ;;
esac

status=$("$BASE/zapret.sh" status 2>/dev/null | sed \
    -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g')

printf 'Content-Type: text/html; charset=utf-8\r\n\r\n'
cat <<EOF
<!DOCTYPE html>
<html lang="ru">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>ZapretOBKHOD — роутер</title>
  <style>
    body { font-family: sans-serif; margin: 24px; color: #222; }
    h1 { font-size: 22px; }
    a.btn { display: inline-block; padding: 8px 14px; margin: 3px;
            background: #eef2f7; border: 1px solid #b8c4d4; border-radius: 5px;
            text-decoration: none; color: #17365d; }
    a.btn.on { background: #d9f0d9; border-color: #7fae7f; }
    pre { background: #f5f6f8; padding: 12px; border-radius: 5px; }
    small { color: #666; }
  </style>
</head>
<body>
  <h1>ZapretOBKHOD на роутере</h1>
  <pre>$status</pre>
  <p>
    <a class="btn on" href="zapret.cgi?action=start">Включить</a>
    <a class="btn" href="zapret.cgi?action=stop">Выключить</a>
    <a class="btn" href="zapret.cgi?action=restart">Перезапустить</a>
    <a class="btn" href="zapret.cgi">Обновить</a>
  </p>
  <h2>Профиль</h2>
  <p>
    <a class="btn" href="zapret.cgi?action=preset&amp;id=combo">Комбо</a>
    <a class="btn" href="zapret.cgi?action=preset&amp;id=youtube">YouTube</a>
    <a class="btn" href="zapret.cgi?action=preset&amp;id=discord">Discord</a>
    <a class="btn" href="zapret.cgi?action=preset&amp;id=telegram">Telegram + HTTPS</a>
    <a class="btn" href="zapret.cgi?action=preset&amp;id=media">Медиа</a>
    <a class="btn" href="zapret.cgi?action=preset&amp;id=all">Все сайты</a>
    <a class="btn" href="zapret.cgi?action=preset&amp;id=soft">Мягкий</a>
    <a class="btn" href="zapret.cgi?action=preset&amp;id=custom">Свой список</a>
  </p>
  <p><small>Трафик идёт напрямую через провайдера. Никаких внешних серверов и VPN нет.</small></p>
</body>
</html>
EOF
