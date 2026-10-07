# start-server.ps1 — run the local Windows dedicated server (.local/server, git-ignored) with a chosen
# DedicatedServer build and test harness. Foreground; the caller backgrounds it and stops it by PID.
#   -Build  stock  .local/stock/DedicatedServer.dll (the released 1.0.3, for baselines)
#           dist   dist/Smoothbrain-DedicatedServer/DedicatedServer.dll (this branch's build)
#   -Harness ShipTest | RaidTrigger | LocalPlayerTest | none   test plugin to install (built from test/<name>)
param(
    [ValidateSet('stock', 'dist')][string]$Build = 'dist',
    [ValidateSet('ShipTest', 'RaidTrigger', 'LocalPlayerTest', 'none')][string]$Harness = 'ShipTest'
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$local = "$root\.local"
$server = "$local\server"
$plugins = "$server\BepInEx\plugins"

$dll = if ($Build -eq 'stock') { "$local\stock\DedicatedServer.dll" } else { "$root\dist\Smoothbrain-DedicatedServer\DedicatedServer.dll" }
Copy-Item $dll "$plugins\Smoothbrain-DedicatedServer\DedicatedServer.dll" -Force
Write-Host "DedicatedServer.dll <- $dll" -ForegroundColor Cyan

foreach ($h in 'ShipTest', 'RaidTrigger', 'LocalPlayerTest') { Remove-Item "$plugins\$h" -Recurse -Force -ErrorAction SilentlyContinue }
if ($Harness -ne 'none') {
    New-Item -ItemType Directory -Force "$plugins\$Harness" | Out-Null
    Copy-Item "$root\test\$Harness\bin\Release\net481\$Harness.dll" "$plugins\$Harness\" -Force
    Write-Host "harness: $Harness" -ForegroundColor Cyan
}

# What start_headless_server.bat does; the app id is the game's, not the server tool's.
$env:SteamAppId = '892970'
Set-Location $server
& "$server\valheim_server.exe" -nographics -batchmode `
    -name 'DSTest' -port 2456 -world 'RaidFix' -password 'raidfix1' `
    -public 0 -savedir "$local\save" -setkey defeated_eikthyr
