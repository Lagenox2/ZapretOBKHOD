# ZapretOBKHOD for BusyBox routers: settings
# Edit this file, then run: sh zapret.sh restart

# Mode: tpws (transparent proxy, recommended) or nfqws (needs NFQUEUE)
MODE=tpws

# Default preset: combo, youtube, discord, telegram, media, all, soft, custom
PRESET=combo

# Transparent proxy port (tpws only)
PORT=988

# Network interfaces. Leave empty for auto-detection.
LAN_IFACE=
WAN_IFACE=

# Block UDP/443 (QUIC) so browsers fall back to TCP
BLOCK_QUIC=1

# Do not touch banks, government services and Russian sites (lists/exclude.txt)
EXCLUDE_RU=1

# Web interface on LAN. 0 disables it.
WEB_UI=1
WEB_PORT=81
