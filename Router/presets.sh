#!/bin/sh
# Profily ZapretOBKHOD dlya dvizhkov tpws i nfqws.
# Ne vyzyvayte etot fail napryamuyu - ego podklyuchaet zapret.sh.

preset_valid() {
    case "$1" in
        combo|youtube|discord|telegram|media|all|soft|custom) return 0 ;;
        *) return 1 ;;
    esac
}

preset_name() {
    case "$1" in
        combo)    echo 'Комбо: YouTube + Discord + Telegram' ;;
        youtube)  echo 'Только YouTube' ;;
        discord)  echo 'Только Discord' ;;
        telegram) echo 'Telegram + весь HTTPS' ;;
        media)    echo 'Соцсети и медиа' ;;
        all)      echo 'Все сайты (агрессивно)' ;;
        soft)     echo 'Мягкий режим (только разделение)' ;;
        custom)   echo 'Свой список сайтов' ;;
        *)        echo 'Неизвестный профиль' ;;
    esac
}

# $1 - dvizhok (tpws|nfqws), $2 - profil, $3 - fayl domenov,
# $4 - fayl isklyucheniy, $5 - papka s faykami.
# Rezultat - globalnaya peremennaya ARGS.
build_args() {
    mode="$1"
    p="$2"
    hosts="$3"
    excl="$4"
    fake="$5"

    hl=""
    case "$p" in
        combo|youtube|discord|media|custom) hl="--hostlist=$hosts" ;;
    esac

    xl=""
    if [ "$EXCLUDE_RU" = 1 ] && [ -s "$excl" ]; then
        xl="--hostlist-exclude=$excl"
    fi

    filters="$hl $xl"
    ARGS=""

    if [ "$mode" = nfqws ]; then
        s80="--filter-tcp=80 --dpi-desync=fake,multisplit --dpi-desync-split-pos=method+2 --dpi-desync-fooling=md5sig --dpi-desync-repeats=6"
        s443="--filter-tcp=443 --dpi-desync=fake,multidisorder --dpi-desync-split-pos=1,midsld --dpi-desync-fooling=badseq,md5sig --dpi-desync-repeats=6"
        squic="--filter-udp=443 --filter-l7=quic --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=@$fake/quic_initial_www_google_com.bin"
        ssoft="--filter-tcp=443 --dpi-desync=multisplit --dpi-desync-split-pos=1"

        case "$p" in
            soft)
                ARGS="$ssoft $filters"
                ;;
            *)
                ARGS="$s80 $filters --new $s443 $filters --new $squic $filters"
                case "$p" in
                    combo|discord)
                        ARGS="$ARGS --new --filter-udp=50000-50100 --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-discord=@$fake/discord-ip-discovery-with-port.bin"
                        ;;
                esac
                ;;
        esac
    else
        # tpws ne podderzhivaet UDP i faykovye pakety.
        # QUIC blockiruyetsya pravilom iptables (BLOCK_QUIC=1).
        s80="--filter-tcp=80 --methodspace"
        s443="--filter-tcp=443 --split-pos=1,midsld --disorder"
        ssoft="--filter-tcp=443 --split-pos=1"

        case "$p" in
            soft) ARGS="$ssoft $filters" ;;
            *)    ARGS="$s80 $filters --new $s443 $filters" ;;
        esac
    fi
}
