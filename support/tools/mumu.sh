#!/bin/bash
# NOTE: S (work folder) below points at the 2026-09-08 session scratchpad; set it to any writable folder before use.
# MuMu (Android emulator) helper over ADB — an input channel independent of the PC mouse/keyboard.
ADB="/c/Program Files/Netease/MuMuPlayer/nx_main/adb.exe"
DEV="${MUMU_DEV:-127.0.0.1:16384}"
S="/c/Users/RIOTGA~1/AppData/Local/Temp/claude/C--Users-riotgames-Desktop/1eaccab8-734b-44e9-bdf5-5a65a349468d/scratchpad"
FF="/c/Users/riotgames/AppData/Local/Microsoft/WinGet/Packages/Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe/ffmpeg-9.0.1-full_build/bin"
export PATH="$FF:$PATH"
a() { "$ADB" -s "$DEV" "$@"; }
case "$1" in
  shot)   # shot <name> [scale]  -> scratchpad/<name>.png (scaled)
    a exec-out screencap -p > "$S/$2.raw.png" 2>/dev/null; sc="${3:-0.5}"; ffmpeg -v error -y -i "$S/$2.raw.png" -vf "scale=iw*$sc:-1" "$S/$2.png" && echo "shot $S/$2.png" ;;
  tap)    a shell input tap "$2" "$3" ;;
  swipe)  a shell input swipe "$2" "$3" "$4" "$5" "${6:-300}" ;;
  text)   a shell input text "$2" ;;          # ASCII only (spaces as %s)
  key)    a shell input keyevent "$2" ;;      # e.g. KEYCODE_ENTER, KEYCODE_BACK
  focus)  a shell "dumpsys window 2>/dev/null | grep -E 'mCurrentFocus' | head -1" | tr -d '\r' ;;
  pkgs)   a shell "pm list packages 2>/dev/null | grep -i -E 'innersloth|adbkeyboard'" | tr -d '\r' ;;
  launch) a shell monkey -p com.innersloth.spacemafia -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1; echo launched ;;
  md5)    # md5 of a screen region: md5 <x> <y> <w> <h>
    a exec-out screencap -p > "$S/poll.raw.png" 2>/dev/null; ffmpeg -v error -y -i "$S/poll.raw.png" -vf "crop=$4:$5:$2:$3" -f md5 - 2>/dev/null | tail -c 33 ;;
  *) echo "usage: mumu.sh shot|tap|swipe|text|key|focus|pkgs|launch|md5" ;;
esac
