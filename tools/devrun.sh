#!/bin/sh
# Dev only: quit the running test game, install the fresh build with the harness marker, relaunch it
# through the Steam client (which must already be running on the display you want the game on).
G="${SB_GAME:-/mnt/Games_SSD_Bazzite/SteamLibrary/steamapps/common/Supermarket Together}"
P="$G/BepInEx/plugins/SupermarketBoost"
L="$G/BepInEx/LogOutput.log"
cd "$(dirname "$0")/.."
if ps -eo args | grep -q "[S]upermarket Together.exe"; then
  printf 'quit\n' > "$G/BepInEx/sb_cmd.txt"
  i=0; while ps -eo args | grep -q "[S]upermarket Together.exe" && [ $i -lt 60 ]; do sleep 1; i=$((i+1)); done
  ps -eo args | grep -q "[S]upermarket Together.exe" && { echo "game did not quit"; exit 1; }
fi
cp bin/Release/netstandard2.1/SupermarketBoost.dll "$P/" && touch "$P/devharness"
rm -f "$G/BepInEx/sb_cmd.txt"
before=$(stat -c %Y "$L" 2>/dev/null || echo 0)
steam -applaunch 2709570 >/dev/null 2>&1 &
i=0; while [ $i -lt 180 ]; do
  [ "$(stat -c %Y "$L" 2>/dev/null || echo 0)" -gt "$before" ] && grep -q "harness on" "$L" && grep -q "Chainloader startup complete" "$L" && break
  sleep 1; i=$((i+1))
done
grep -E "harness on|loaded|rror" "$L" | tail -4
