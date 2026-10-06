# fetch-gamelibs.ps1 — copy the assemblies the server build and the test harnesses compile against
# from the game box into .gamelibs/ (git-ignored: Iron Gate's and BepInEx's binaries).
#
# Pulled from the box, not a local game install, so the build targets exactly what the server
# runs. Re-run after a Valheim or BepInExPack update. Private members are opened up at build
# time by BepInEx.AssemblyPublicizer.MSBuild (Publicize="true" on the reference), not here.
param([string]$Box = "gamebox")   # ssh config alias

$ErrorActionPreference = "Stop"
$dest = Join-Path $PSScriptRoot "..\.gamelibs"
$root = "valheim/data/bepinex"    # the server's working directory; data/server and data/dl/server are not what runs
$files = @(
    "valheim_server_Data/Managed/assembly_valheim.dll",
    "valheim_server_Data/Managed/assembly_utils.dll",
    "valheim_server_Data/Managed/assembly_guiutils.dll",
    "valheim_server_Data/Managed/Splatform.dll",
    "valheim_server_Data/Managed/UnityEngine.dll",
    "valheim_server_Data/Managed/UnityEngine.CoreModule.dll",
    "valheim_server_Data/Managed/UnityEngine.PhysicsModule.dll",
    "BepInEx/core/BepInEx.dll",
    "BepInEx/core/0Harmony.dll"
)

New-Item -ItemType Directory -Force -Path $dest | Out-Null
foreach ($f in $files) {
    scp -q "${Box}:$root/$f" $dest
    Write-Host "  $f"
}
$v = ssh $Box "grep -m1 -o 'Valheim Version: [^ ]*' $root/BepInEx/LogOutput.log"
Write-Host "Fetched into $((Resolve-Path $dest).Path) ($v)" -ForegroundColor Green
