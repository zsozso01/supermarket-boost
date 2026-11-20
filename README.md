# Supermarket Boost

A BepInEx mod for **Supermarket Together** that makes co-op days busier and sillier.

## Features

- **More customers**: about 2× as many shoppers, arriving about 2× as often. Prices and what customers pay are unchanged.
- **Fewer breakdowns**: machines break about 3× less often (or never, if you want).
- **Game speed**: a button in the office (or F6) runs the whole store at ×2 or ×4 while players keep walking at normal speed.
- **Cosmetics room**: a button in the office (or F8) takes you to the skins & hats room and back.
- **Pistol**: a real held item (G), shoot with the action button, R to reload, 15-round magazine with spare ammo every morning. Per-limb ragdolls for shot and broomed people, 3 shots keep a shopper or thief down, shoot your friends to ragdoll them, blood. Synced for everyone.
- **Continue button**: the main menu loads your latest store as a friends-only Steam lobby. Friends join with Steam's *Join Game* or an invite (F7), with no codes needed.
- **Save backups**: your save folder is copied before installing, at every game start and before every save.
- **Auto-updates**: new versions download by themselves and apply on the next start.

## Install (Windows)

1. Download `SupermarketBoost.zip` from the [latest release](https://github.com/zsozso01/supermarket-boost/releases/latest).
2. Close the game, unzip, double-click `Install.bat`.
3. Start the game from Steam.

**Everyone in the lobby needs the mod** for the synced pistol. If you join a host without it, set `MultiplayerSync = false` in the config first.

### Linux / Steam Deck (Proton)

Install as above (or copy BepInEx 5 + the DLLs by hand), then set the Steam launch option:

```
WINEDLLOVERRIDES="winhttp=n,b" %command%
```

## Settings

`<game folder>/BepInEx/config/codescrambler.supermarkettogether.boost.cfg` (created on first launch).

## Building

Requires the .NET SDK and the game installed. Set `GameManaged` in `MoreCustomers.csproj` to your game's `Supermarket Together_Data/Managed` folder, then run `./package.sh`.
To release, bump the version in `Plugin.cs` and the `.csproj`, then run `./release.sh "notes"`.
