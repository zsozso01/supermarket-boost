#!/bin/sh
# Dev only: send harness commands to a running game and print the [TEST] lines they produce.
# Usage: tools/sbtest.sh "state" "npcs" ...   (needs BepInEx/plugins/SupermarketBoost/devharness)
G="${SB_GAME:-/mnt/Games_SSD_Bazzite/SteamLibrary/steamapps/common/Supermarket Together}"
L="$G/BepInEx/LogOutput.log"
TIMEOUT="${SB_TIMEOUT:-120}"
for c in "$@"; do
  start=$(wc -l < "$L")
  printf '%s\n' "$c" > "$G/BepInEx/sb_cmd.txt"
  name=$(echo "$c" | cut -d' ' -f1)
  i=0
  while [ $i -lt $((TIMEOUT*4)) ]; do
    tail -n +$((start+1)) "$L" | grep -q "\[TEST\] INFO < $name done" && break
    sleep 0.25; i=$((i+1))
  done
  tail -n +$((start+1)) "$L" | grep -E "\[TEST\]|Exception|rror" | grep -v "INFO > \|INFO < " | sed 's/^\[Info   :Supermarket Boost\] \[TEST\] //'
  [ $i -ge $((TIMEOUT*4)) ] && echo "TIMEOUT waiting for $c"
done
