#!/bin/sh
# Publish a new version: bump Version in Plugin.cs and MoreCustomers.csproj first, then run ./release.sh
# Everyone with the mod installed picks it up on their next game start.
set -e
cd "$(dirname "$0")"
V=$(grep -oP 'Version = "\K[0-9.]+' Plugin.cs)
grep -q "<Version>$V</Version>" MoreCustomers.csproj || { echo "Version mismatch between Plugin.cs and csproj"; exit 1; }
./package.sh
echo "$V" > dist/version.txt
gh release create "v$V" --repo zsozso01/supermarket-boost --title "Supermarket Boost v$V" --notes "${1:-Update}" \
  dist/SupermarketBoost/SupermarketBoost.dll dist/SupermarketBoost.zip dist/version.txt
