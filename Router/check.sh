#!/bin/sh
# Диагностика окружения перед запуском ZapretOBKHOD.

BASE=$(cd "$(dirname "$0")" && pwd)

echo '=== Система ==='
uname -a 2>/dev/null
echo "arch: $(uname -m 2>/dev/null)"

echo ''
echo '=== Команды ==='
for c in iptables httpd nvram logger route sed awk; do
    p=$(command -v "$c" 2>/dev/null)
    [ -n "$p" ] && echo "$c: $p" || echo "$c: НЕ НАЙДЕН"
done

echo ''
echo '=== Бинарники ==='
for b in bin/tpws-mipsel bin/nfqws-mipsel bin/tpws-mips bin/nfqws-mips; do
    f="$BASE/$b"
    if [ -x "$f" ]; then
        v=$("$f" --version 2>&1 | head -n 1)
        echo "$b: OK ($v)"
    else
        echo "$b: не запускается или нет прав"
    fi
done

echo ''
echo '=== Сеть ==='
if command -v nvram >/dev/null 2>&1; then
    echo "lan_ifname: $(nvram get lan_ifname 2>/dev/null)"
    echo "wan_ifname: $(nvram get wan_ifname 2>/dev/null)"
fi

echo ''
echo '=== Место ==='
df -h /tmp "$BASE" 2>/dev/null

echo ''
echo '=== Состояние ZapretOBKHOD ==='
sh "$BASE/zapret.sh" status
