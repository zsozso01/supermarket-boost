# Supermarket Boost: handoff (2026-11-21)

BepInEx 5 mod for **Supermarket Together** (Unity 2023.2.22f1, Mono, Mirror networking, Steam/FizzySteamworks, PlayMaker UI).
Repo: https://github.com/zsozso01/supermarket-boost (public). Current release **v1.6.0**, installed on this PC.
The owner and a friend want to **play co-op today without issues**. Nothing since v1.5 has been verified in-game.

## Goal of the next session

1. **Test harness in the real game** (dev builds only, off in releases):
   - Plugin listens on `127.0.0.1:<port>` (or watches a command file) for commands, e.g.
     `state`, `dumpui` (active Canvas/Buttons with paths and texts), `continue`, `opendstore`,
     `spawncustomer`, `shoot <netId> [n]`, `broom <netId>`, `speed <x>`, `cosmetics`, `screenshot <file>`,
     `give pistol`, `ragdollplayer`. Each command logs a clear PASS/FAIL line to `BepInEx/LogOutput.log`.
   - Set `Application.runInBackground = true` in dev mode (the game stalls when its window isn't focused).
   - Scripted test runs (a shell/python driver) that launch the game, go menu → Continue → open store →
     run every feature, assert on log lines, take screenshots, then quit.
2. **Run the game on the `vdesk` virtual desktop** (enabled for this folder) so tests never take over the owner's screen.
   Steam must be running for SteamAPI; the game has `steam_appid.txt` (2709570). Find how to launch the Proton
   build onto the vdesk display (compatdata 2709570, Proton Experimental) instead of `steam://rungameid`.
3. **UnityExplorer** (BepInEx 5 Mono build, from its official GitHub releases; ask the owner before downloading)
   for live inspection of player/NPC rigs, the menu, the cosmetics room colliders.
4. **Better gun model** (owner request). Options: a proper procedural mesh in code (beveled slide with serrations,
   ejection port, barrel, rounded grip with texture, magazine base, sights, trigger), or a real model loaded as an
   AssetBundle. AssetBundles must be built with **Unity 2023.2.x** (installed: 2022.3.29f1, 6000.3.6f1, 6000.6.7f1,
   none match; installing 2023.2.22f1 via Unity Hub would be needed). Hold pose / IK grip points must be re-fit.
5. Fix whatever the tests find, then release (see "Release").

## Must-verify list (all currently unverified)

- Main menu: Continue panel shows (bottom-left), Continue loads latest `StoreFileN.es3` as a friends-only lobby; "JOIN <friend>'s store" button.
- Accepted Steam invite: joins (from menu; from inside a store → "save and quit", auto-join in menu). Needs the friend (second Steam account).
- Pistol: G equips as item 18 (synced `equippedItem`), hand IK grip looks right for others, R reload, ammo HUD, no "Need a broom" message, no "Equipped item error" spam.
- Shots: NPC ragdoll per limb (Bip01 rigs), gets up without floating; 2 shots knock out shoppers/thieves (log line "Knocked out ..."), body removed after 30 s, thieves drop loot; staff / checkout-line customers always get up.
- Player ragdoll when shot by a friend: follow camera, stands up where landed, model not glitched afterwards (RigBuilder rebuilt).
- Broom hits ragdoll gently.
- Blood: ballistic droplets, round cut-out stains on surfaces, pool under knocked-out bodies.
- Game speed x2/x4: players walk and animate at normal speed; resets at store close.
- Cosmetics room: F8 / button teleports in and out; elephant "Herbert" and its invisible barrier gone.
- Customers 1.4x, thieves 40 %, breakdowns 1/3, config migration from old defaults.
- Save backups at start and before each save.

## Where things are

- Game: `/mnt/Games_SSD_Bazzite/SteamLibrary/steamapps/common/Supermarket Together/`
  (plugin `BepInEx/plugins/SupermarketBoost/`, updater `BepInEx/patchers/SupermarketBoost/`, config `BepInEx/config/codescrambler.supermarkettogether.boost.cfg`, mod log `BepInEx/LogOutput.log`).
- Unity log: `.../steamapps/compatdata/2709570/pfx/drive_c/users/steamuser/AppData/LocalLow/DDTNL/Supermarket Together/Player.log`
  (BepInEx can't write Unity logs, so Unity errors are only here).
- Saves: same LocalLow folder; mod backups in `.../DDTNL/Supermarket Together Backups (SupermarketBoost)/`; pre-mod copy in `~/Documents/Supermarket Together save backups/2026-11-20 before mods`.
- Proton loads BepInEx via `"winhttp"="native,builtin"` in the prefix `user.reg` (backup `user.reg.bak-before-supermarketboost`).
- Decompile for reference (not kept): `dotnet tool install --tool-path <scratch>/tools ilspycmd`, then
  `ilspycmd -p -o <scratch>/decomp ".../Managed/Assembly-CSharp.dll"` (also `Mirror.dll`, `Unity.StarterAssets.dll`).

## Game facts the mod relies on

- Customer cap: `GameData.AuxiliarSupermarketOpen` sets `maxCustomersNPCs`; spawning `NPC_Manager.SpawnCustomerNPC` coroutine (thief decided inside; customers under `NPC_Manager.customersnpcParentOBJ`).
- Breakdowns: `GameData.BreakingManager` / `GetNextTimeAdd` (from day 21).
- Hits: `NPC_Info.AuxiliarAnimationPlay(int)` on the host (broom via PlayMaker → `CmdAnimationPlay`, requiresAuthority=false). Customer AI `NPC_Manager.CustomerNPCControl` skips `state == -1`; thieves get `StartWaitState(2f, 11)` (must be cancelled for knockouts).
- Items: `PlayerNetwork.equippedPrefabs[]`, SyncVar `equippedItem`, `ChangeEquipment` coroutine (no default case), hand IK in `PlayerSyncCharacter.LateUpdate` (`IKOBJs/RightHandTarget`, `Rig/RigHandIK` TwoBoneIK, children `RightHandIK`/`LeftHandIK` on the held object). Player/NPC rigs are 3ds Max `Bip01`.
- Menu: the main menu has the store scene loaded behind it (`GameData.Instance` exists); "in menu" = not `NetworkClient.active`/`NetworkServer.active`. Hosting: `SteamLobby.HostLobby(mode,…)` (1 = friends-only) after setting PlayMaker globals `CurrentFilename`, `LoadingFromAutosave`.
- Office switch object: `OpenSupermarketSwitch`. Cosmetics room: `TheCoolRoom` (`skinCanvasOBJ`). Elephant: Animator "Herbert".
- Player movement: `StarterAssets.FirstPersonController.Move/JumpAndGravity` (transpiled to real time when sped up; `CharacterController.velocity` corrected). `inEvent` freezes input.
- Notifications: `GameCanvas.CreateCanvasNotification(hash)`; "Need a broom" = `message14`.
- Mirror kicks peers on unknown messages: custom messages only go to clients that sent `SbHelloMsg` with the same protocol (currently 3).

## Release

Bump `Version` in `Plugin.cs` and `<Version>` in `MoreCustomers.csproj`, `./release.sh "notes"` (builds, zips, uploads `SupermarketBoost.dll`, zip and `version.txt`; installed copies self-update on next start via `releases/latest/download/version.txt`). Stage locally by copying the DLL to `BepInEx/plugins/SupermarketBoost/SupermarketBoost.dll.new`.
No AI wording in public text (README, release notes). Update the row in `~/Documents/Egyetem/Notes/Work/Work.md`.

## Rules for testing on this PC

- Ask before closing the owner's running game; the game only progresses while focused unless `runInBackground` is set.
- `pgrep -f` patterns that appear in your own command line match your own shell: use `ps -eo pid,args | grep … | grep -v grep`.
- Close tabs/processes you start; one browser-pane user at a time.
