# Supermarket Boost installer for Windows.
# Finds Supermarket Together in Steam, installs BepInEx 5 (official GitHub release) if missing,
# then copies the mod in. Run via Install.bat.

$ErrorActionPreference = 'Stop'
$BepInExUrl = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.2/BepInEx_win_x64_5.4.23.2.zip'
$GameFolderName = 'Supermarket Together'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Done($msg, $code) {
    Write-Host ''
    Write-Host $msg
    Write-Host ''
    Read-Host 'Press Enter to close'
    exit $code
}

function Find-Game {
    $roots = @()
    foreach ($key in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        try {
            $p = Get-ItemProperty -Path $key -ErrorAction Stop
            if ($p.SteamPath) { $roots += $p.SteamPath }
            if ($p.InstallPath) { $roots += $p.InstallPath }
        } catch {}
    }
    $roots += "${env:ProgramFiles(x86)}\Steam"
    $libs = @()
    foreach ($r in ($roots | Select-Object -Unique)) {
        $libs += $r
        $vdf = Join-Path $r 'steamapps\libraryfolders.vdf'
        if (Test-Path $vdf) {
            foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
                $libs += $m.Groups[1].Value -replace '\\\\', '\'
            }
        }
    }
    foreach ($lib in ($libs | Select-Object -Unique)) {
        $g = Join-Path $lib "steamapps\common\$GameFolderName"
        if (Test-Path (Join-Path $g 'Supermarket Together.exe')) { return $g }
    }
    return $null
}

function Pick-Folder {
    Add-Type -AssemblyName System.Windows.Forms
    $d = New-Object System.Windows.Forms.FolderBrowserDialog
    $d.Description = 'Could not find Supermarket Together automatically. Pick its install folder (the one with "Supermarket Together.exe").'
    if ($d.ShowDialog() -eq 'OK') { return $d.SelectedPath }
    return $null
}

Write-Host '=== Supermarket Boost installer ==='

if (Get-Process -Name 'Supermarket Together' -ErrorAction SilentlyContinue) {
    Done 'Please close Supermarket Together first, then run the installer again.' 1
}

$game = Find-Game
if (-not $game) { $game = Pick-Folder }
if (-not $game -or -not (Test-Path (Join-Path $game 'Supermarket Together.exe'))) {
    Done 'Supermarket Together was not found. Nothing was changed.' 1
}
Write-Host "Game folder: $game"

if (-not (Test-Path (Join-Path $game 'BepInEx\core\BepInEx.dll'))) {
    Write-Host 'Downloading BepInEx 5 (mod loader) from github.com/BepInEx ...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $zip = Join-Path $env:TEMP 'BepInEx_win_x64_5.4.23.2.zip'
    Invoke-WebRequest -Uri $BepInExUrl -OutFile $zip -UseBasicParsing
    Expand-Archive -Path $zip -DestinationPath $game -Force
    Remove-Item $zip -Force
    Write-Host 'BepInEx installed.'
} else {
    Write-Host 'BepInEx already installed.'
}

# Back up saves before touching anything.
$saves = Join-Path $env:USERPROFILE 'AppData\LocalLow\DDTNL\Supermarket Together'
if (Test-Path $saves) {
    $stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm-ss'
    $backup = Join-Path $env:USERPROFILE "AppData\LocalLow\DDTNL\Supermarket Together Backups (SupermarketBoost)\$stamp before-install"
    New-Item -ItemType Directory -Force -Path $backup | Out-Null
    Copy-Item -Path (Join-Path $saves '*') -Destination $backup -Recurse -Force -Exclude 'Player*.log'
    Write-Host "Saves backed up to: $backup"
}

$pluginDir = Join-Path $game 'BepInEx\plugins\SupermarketBoost'
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item -Path (Join-Path $Here 'SupermarketBoost.dll') -Destination $pluginDir -Force
$patcherDir = Join-Path $game 'BepInEx\patchers\SupermarketBoost'
New-Item -ItemType Directory -Force -Path $patcherDir | Out-Null
Copy-Item -Path (Join-Path $Here 'SupermarketBoost.Updater.dll') -Destination $patcherDir -Force
Write-Host 'Supermarket Boost installed.'

Done "All done! Start the game from Steam as usual. The mod keeps itself up to date.`nSettings appear after the first launch in:`n$game\BepInEx\config\codescrambler.supermarkettogether.boost.cfg" 0
