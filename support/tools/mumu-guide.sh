#!/bin/bash
# NOTE: S (work folder) below points at the 2026-09-08 session scratchpad; set it to any writable folder before use.
# Guide-room autopilot on the MuMu emulator (chat stays OPEN so the user can watch it).
# Join detection: Unity logcat lines "<CreatePlayer>" (one per player object spawned). On a new join the guide
# line is pasted into the chat (JA via the Windows clipboard + Ctrl+V) - only when the chat is open with an EMPTY
# field (the send button looks the same as in the reference); if the field has text (the user is typing) or the
# chat is closed, the chat is opened / the round is skipped.
# Lobby expiry (vanilla closes an idle lobby after ~10 min -> main menu + "サーバによりルームが閉じられました"):
# the screen is checked every 30 s and after every lobby event; at the main menu the lobby is recreated
# automatically (popup OK -> オンライン -> ゲーム作成 -> ゲーム作成 -> 確認 -> public -> chat open).
# Stop: create guide.stop. Role-room code: guide.code. New guide-room code crop: guide-code.png.
ADB="/c/Program Files/Netease/MuMuPlayer/nx_main/adb.exe"; DEV=127.0.0.1:16384
S="/c/Users/RIOTGA~1/AppData/Local/Temp/claude/C--Users-riotgames-Desktop/1eaccab8-734b-44e9-bdf5-5a65a349468d/scratchpad"
FF="/c/Users/riotgames/AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.1-full_build/bin"; export PATH="$FF:$PATH"
LOG="$S/guide.log"; STOP="$S/guide.stop"; CODEFILE="$S/guide.code"
CHAT_OPEN_MD5="6d5b16eb05a20fe1331504cb12085298"   # send button region while the chat is open with an empty field
MIN_GAP=20
a() { timeout 25 "$ADB" -s $DEV "$@"; }   # timeout: a stalled adb call once hung the loop for 13 min
tap() { a shell input tap "$1" "$2" >/dev/null 2>&1; }
log() { echo "$(date +%H:%M:%S) $*" >> "$LOG"; }
shot() { a exec-out screencap -p > "$S/guide.raw.png" 2>/dev/null; }
md5region() { ffmpeg -v error -y -i "$S/guide.raw.png" -vf "crop=$3:$4:$1:$2" -f md5 - 2>/dev/null | tail -c 33 | tr -d '\n'; }
avgregion() { ffmpeg -v error -i "$S/guide.raw.png" -vf "crop=$3:$4:$1:$2,scale=1:1" -f rawvideo -pix_fmt rgb24 - 2>/dev/null | od -An -tu1 | tr -s " "; }
chat_open_empty() { [ "$(md5region 1300 820 180 50)" = "$CHAT_OPEN_MD5" ]; }
joins() { a logcat -d 2>/dev/null | grep -c '<CreatePlayer>'; }
near() { local d1=$(( $1 - $4 )) d2=$(( $2 - $5 )) d3=$(( $3 - $6 )); [ ${d1#-} -le 22 ] && [ ${d2#-} -le 22 ] && [ ${d3#-} -le 22 ]; }
# privacy button colour (chat must be closed): public ~ (123,186,60), private ~ (155,43,43)
privacy_rgb() { avgregion 1712 613 120 30; }
is_lobby_rgb() { { [ "$1" -gt 130 ] && [ "$2" -lt 90 ]; } || { [ "$2" -gt 150 ] && [ "$1" -lt 150 ] && [ "$3" -lt 110 ]; }; }
# Screen classification (takes a fresh screenshot):
#   lobby-chat   chat open with an empty field (normal state)
#   lobby        lobby with the chat closed (privacy button visible)
#   menu         clean main menu            menu-popup   main menu + "room closed" popup (OK at 1110,711)
#   menu-friend  main menu + friend list (X at 1150,48)     menu-overlay  main menu + something else
#   other        anything else (create screen, loading, chat with text ...)
screen_state() {
  shot
  chat_open_empty && { echo lobby-chat; return; }
  set -- $(avgregion 60 120 300 60); lr=${1:-0}; lg=${2:-0}; lb=${3:-0}          # "AMONG US" logo
  set -- $(avgregion 100 355 400 80); pg=${2:-0}                                # プレイ button
  set -- $(avgregion 1350 290 300 40); fg=${2:-0}                               # friend-list title area
  if near $lr $lg $lb 97 142 131; then
    if [ "$pg" -gt 170 ]; then echo menu; elif [ "$pg" -lt 110 ]; then echo menu-popup; else echo menu-overlay; fi; return
  fi
  if near $lr $lg $lb 74 108 100; then
    if [ "$fg" -gt 190 ]; then echo menu-friend; else echo menu-overlay; fi; return
  fi
  set -- $(privacy_rgb); if is_lobby_rgb ${1:-0} ${2:-0} ${3:-0}; then echo lobby; return; fi
  echo other
}
ensure_public() {
  shot; if chat_open_empty; then tap 1580 80; sleep 1.5; shot; fi
  set -- $(privacy_rgb); r=${1:-0}; g=${2:-0}
  if [ "$r" -gt 130 ] && [ "$g" -lt 90 ]; then tap 1772 628; sleep 1.5; shot; set -- $(privacy_rgb); log "  lobby was private -> toggled (now rgb $1 $2 $3)"; else log "  lobby public check ok (rgb $r $g $3)"; fi
  ffmpeg -v error -y -i "$S/guide.raw.png" -vf "crop=340:90:1500:220,scale=iw*1.2:-1" "$S/guide-code.png" 2>/dev/null
  tap 1580 80; sleep 1.5   # reopen the chat for the user
}
ensure_chat_ready() {
  shot; chat_open_empty && return 0
  # closed, or open with text in the field: try one toggle and re-check; if still not clean, undo and skip
  tap 1580 80; sleep 1.8; shot; chat_open_empty && return 0
  tap 1580 80; sleep 1
  log "  chat not ready (someone typing or another panel) - skipped"
  return 1
}
paste_ja() { powershell -STA -NoProfile -Command "Set-Clipboard -Value '$1'" >/dev/null 2>&1; sleep 2; tap 820 846; sleep 0.6; a shell input keycombination 113 50 >/dev/null 2>&1; sleep 1.5; tap 1390 846; sleep 2; }
send_guide() {
  code=$(cat "$CODEFILE" 2>/dev/null | tr -d '\r\n'); [ -z "$code" ] && { log "no code file"; return; }
  ensure_chat_ready || return
  paste_ja "ここは案内用で試合は始まりません。役職ありの部屋は別コード $code です。今そちらに入ってください。何も入れなくてOK"
  log "guide sent (code $code)"
}
# Keepalive against "DC because LobbyInactivity": nudge the character (the joystick only works with the chat
# closed - a touch outside the chat panel closes it without moving), then reopen the chat.
keepalive() {
  st=$(screen_state)
  case "$st" in lobby-chat) tap 1580 80; sleep 1.2 ;; lobby) ;; *) return ;; esac
  a shell input swipe 165 900 165 760 700 >/dev/null 2>&1; sleep 0.8
  a shell input swipe 165 760 165 900 700 >/dev/null 2>&1; sleep 0.8
  tap 1580 80; sleep 1.5
  st=$(screen_state); [ "$st" = lobby ] && { tap 1580 80; sleep 1.2; }
  log "keepalive nudge (state now $(screen_state))"
}
lastrecreate=0
recreate() {
  now=$(date +%s); [ $((now - lastrecreate)) -lt 90 ] && return 1; lastrecreate=$now
  log "recreate: lobby gone (state $1) -> creating a new guide lobby"
  st=$1
  for t in 1 2 3 4 5; do
    case "$st" in
      menu) break ;;
      menu-popup) tap 1110 711; sleep 2 ;;
      menu-friend) tap 1150 48; sleep 2 ;;
      *) a shell input keyevent 4 >/dev/null 2>&1; sleep 2 ;;
    esac
    st=$(screen_state)
  done
  if [ "$st" != menu ]; then cp "$S/guide.raw.png" "$S/guide-fail.png" 2>/dev/null; log "recreate: could not reach the main menu (state $st) - will retry (snapshot guide-fail.png)"; return 1; fi
  tap 450 390; sleep 3       # プレイ (opens the ローカル/オンライン panel)
  tap 1570 820; sleep 4      # オンライン
  tap 1076 560; sleep 4      # ゲーム作成 (online menu)
  tap 1174 976; sleep 4      # ゲーム作成 (create screen)
  tap 960 914;  sleep 14     # 確認
  st=$(screen_state)
  if [ "$st" = lobby ] || [ "$st" = lobby-chat ]; then
    ensure_public
    last=$(joins); lastjoin=0
    log "recreate: new guide lobby ready (code crop guide-code.png)"
    return 0
  fi
  cp "$S/guide.raw.png" "$S/guide-fail.png" 2>/dev/null; log "recreate: no lobby after the create sequence (state $st) - will retry (snapshot guide-fail.png)"; return 1
}
handle_state() {   # $1 = state
  case "$1" in
    menu|menu-popup|menu-friend|menu-overlay) recreate "$1" ;;
    lobby) ensure_public ;;
  esac
}
EVPAT="LobbyTimeExpiring|ExtendLobby|DisconnectPopup|Lobby.*Expir|Menu: (MainMenu|DisconnectPopUp)"
log "autopilot start (code $(cat "$CODEFILE" 2>/dev/null)), join detection via logcat, auto-recreate on"
last=$(joins); lastpost=0; lastjoin=0; n=0; lastev=$(a logcat -d -v time 2>/dev/null | grep -c -i -E "$EVPAT")
while [ ! -f "$STOP" ]; do
  sleep 6; n=$((n+1))
  cur=$(joins); [ "$cur" -lt "$last" ] && last=$cur   # logcat buffer wrapped
  ev=$(a logcat -d -v time 2>/dev/null | grep -c -i -E "$EVPAT")
  if [ "$ev" != "$lastev" ]; then
    if [ "$ev" -gt "$lastev" ]; then shot; cp "$S/guide.raw.png" "$S/guide-event-$ev.png" 2>/dev/null; log "lobby event lines: $lastev -> $ev (snapshot guide-event-$ev.png)"; lastev=$ev; sleep 15; handle_state "$(screen_state)"; else lastev=$ev; fi
  fi
  if [ "$cur" -gt "$last" ]; then
    now=$(date +%s)
    log "join detected (+$((cur - last)))"; lastjoin=$now
    if [ $((now - lastpost)) -ge $MIN_GAP ]; then send_guide; lastpost=$(date +%s); else log "  (rate-limited)"; fi
  fi
  last="$cur"
  # periodic reminder while people may be in the room (a join within the last 10 min), at most every 4 min
  if [ $(( $(date +%s) - lastjoin )) -le 600 ] && [ $(( $(date +%s) - lastpost )) -ge 240 ]; then log "periodic reminder"; send_guide; lastpost=$(date +%s); fi
  # screen check every 30 s: main menu -> recreate; lobby with the chat closed -> leave it (the user may be reading the panel)
  if [ $((n % 5)) -eq 0 ]; then st=$(screen_state); case "$st" in menu*) handle_state "$st" ;; esac; fi
  # keepalive nudge every ~2 min (experiment: does host movement count as lobby activity?)
  # (keepalive disabled: movement does not reset the server timer - lobby closed at exactly 10 min anyway)
  if [ $((n % 50)) -eq 0 ]; then cp "$S/guide.raw.png" "$S/guide-last.png" 2>/dev/null; log "alive (joins so far: $cur, state $st)"; fi
done
log "autopilot stopped"
