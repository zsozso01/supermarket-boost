#!/bin/sh
# Build the mod and make SupermarketBoost.zip to send to friends.
set -e
cd "$(dirname "$0")"
dotnet build -c Release | tail -2
dotnet build UpdaterPatcher -c Release | tail -2
rm -rf dist && mkdir -p dist/SupermarketBoost
cp installer/* dist/SupermarketBoost/
cp bin/Release/netstandard2.1/SupermarketBoost.dll dist/SupermarketBoost/
cp UpdaterPatcher/bin/Release/netstandard2.1/SupermarketBoost.Updater.dll dist/SupermarketBoost/
unix2dos -q dist/SupermarketBoost/*.bat dist/SupermarketBoost/*.ps1 dist/SupermarketBoost/*.txt
(cd dist && zip -qr SupermarketBoost.zip SupermarketBoost)
echo "Made dist/SupermarketBoost.zip"
