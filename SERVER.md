# `server` branch — the build the game box runs

Fork of [blaxxun-boop/DedicatedServer](https://github.com/blaxxun-boop/DedicatedServer).
The game box runs this branch's `dist/` build **on the server only**. Players keep the stock
release from Thunderstore/Gale, because the mod also has to run on clients (see *Why the
override is server-only*).

| Branch | What | Upstream |
|---|---|---|
| `master` | upstream, untouched | — |
| `fix/raid-spawners` | raids spawn creatures when the server owns spawners | [PR #4](https://github.com/blaxxun-boop/DedicatedServer/pull/4) |
| `fix/ship-sail` | server-owned ships no longer free-fall when the sail moves | not opened yet |
| `server` | `master` + every `fix/*` + the build/test tooling below | never PR'd |

`fix/*` branches each hold one upstream-shaped commit and nothing else. Tooling lives only on
`server`, so a fix can be PR'd as is.

## Build

```powershell
mise trust .\mise.toml                                 # once per clone
.\tools\fetch-gamelibs.ps1                             # game + BepInEx assemblies from the box → .gamelibs/
mise exec -- dotnet build server -c Release            # → dist/Smoothbrain-DedicatedServer/DedicatedServer.dll
```

- `server/DedicatedServer.Server.csproj` compiles upstream's `DedicatedServer/*.cs` with the
  .NET 8 SDK alone. Upstream's own `.csproj` needs Visual Studio's MSBuild for its ILRepack task.
  Private game members are opened at build time by `BepInEx.AssemblyPublicizer.MSBuild`, and
  ServerSync is merged in by `ILRepack.Lib.MSBuild.Task`, the same way upstream ships it.
- **Keep `ModVersion` at the upstream release (`1.0.3`).** ServerSync rejects a client whose
  version is below the server's `MinimumRequiredVersion`, and both sides set that to `ModVersion`.
  Only `dist/.../manifest.json` carries the fork marker (`1.0.3-server.N`), which the game box sync
  script shows and checks.
- `dist/` is committed, so the raspberrySetup sync can push it without an SDK. Rebuild and commit
  it with any change to the source.

## Why the override is server-only

- `ConfigSync { ModRequired = true, MinimumRequiredVersion = ModVersion }`: the server disconnects
  a client without the mod (`Disconnect: The client (…) doesn't have the correct DedicatedServer
  version 1.0.3`).
- `ActivateAreas` has a client branch (`uid == ZNet.instance.GetServerPeer().m_uid`): it is what
  stops a client's `ReleaseNearbyZDOS` from taking over server-owned objects near it. Without the
  mod on clients, the server keeps ownership only until a player walks up.
- Both fixes only matter where the object is owned, which is the server. `SpawnSystem.UpdateSpawning`
  runs for the spawner's owner. `Ship.UpdateSailSize` only lacks a local player on the server.
  A stock client plus this server build was tested in game for the raid fix.

## Test locally (no game client needed for the ship test)

`.local/` (git-ignored) holds a Windows dedicated server with BepInEx (`.local/server`), its world
(`.local/save`), the released 1.0.3 DLL for baselines (`.local/stock`) and run outputs
(`.local/evidence`).

```powershell
mise exec -- dotnet build test\ShipTest -c Release
pwsh test\start-server.ps1 -Build stock -Harness ShipTest   # baseline
pwsh test\start-server.ps1 -Build dist  -Harness ShipTest   # this branch
# → .local/server/BepInEx/shiptest.csv + per-sail-change summaries in BepInEx/LogOutput.log ("SHIPTEST DONE")
```

**ShipTest** loads a deep-ocean area with no players, spawns a server-owned longship with a
stand-in passenger, steps the sail Stop/Half/Full and logs every physics tick. On 1.0.16:

| | stock 1.0.3 | `server` |
|---|---|---|
| `Ship.UpdateSailSize` exceptions | 245 (24–25 per half step, 49–50 per full) | 0 |
| hull height swing per sail change | 2.6–3.8 m half steps, 19–21 m stop ↔ full | 0.5–1.1 m |
| water-impact hits (10 dmg each) | 6 | 0 |

Test-harness shortcuts, so a reader doesn't mistake them for game behaviour:
- **Stand-in passenger.** Vanilla `CustomFixedUpdate` sets `m_speed = Stop` whenever `m_players` is
  empty, and drops Slow/Back without a helmsman. The harness adds a mock `Player` and sets the
  sail to Half/Full directly.
- **`IsWindControllActive` stubbed to false.** It reads the passengers' status effects, which the mock
  doesn't have.
- **Late damage.** Both runs take some odd-amount damage after ~95 s, once the ship has drifted out
  of the deep spot.
- **Visibility hook.** A postfix on `ZDOMan.FindSectorObjects` adds the test area to the list
  DedicatedServer builds from connected players only.

**RaidTrigger** forces a raid 60 s after a player joins and counts raid creatures. It needs a game
client: join `127.0.0.1:2456`, password `raidfix1`.

The `.local/server` install comes from SteamCMD (app 896660) plus BepInExPack_Valheim. A fresh
clone has to recreate it; nothing here downloads it.

## Updating to a new upstream release

1. `git fetch origin`, then rebase each `fix/*` onto `origin/master` and drop any that upstream merged.
2. Rebuild `server`: reset it to `origin/master`, cherry-pick the remaining `fix/*` commits, and bring
   the tooling commit back.
3. Set `ModVersion`'s upstream value in the manifest (`<new>-server.1`), then fetch, build, test and commit `dist/`.
4. When no `fix/*` is left, drop the override from the game box sync and go back to the stock release.
