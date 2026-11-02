# Testing multiplayer fixes without a human playtest per fix

Every bug in [FIX_SPEC.md](FIX_SPEC.md) is about two players seeing different things, so a fix only counts as tested if
a second player was involved. **Constraint:** this project doesn't rely on human play-tests of individual fixes.
Real playthroughs are the only human testing. So per-fix verification has to be automated and run by agents on
the dev PC, and the playthrough needs safety nets so a bad fix can't cost progress.

## How multiplayer games are usually tested

| Technique | Typical example | What it catches | Cost |
|---|---|---|---|
| Several local clients on one machine | Unreal "Play In Editor" with N players, Unity *Multiplayer Play Mode* / ParrelSync clones | Everything, including engine-side behaviour (physics, AI, animation) | Heavy on RAM/GPU; normally someone drives two windows |
| **Automated end-to-end scenarios** | Scripted test clients that run real game builds and assert state (common in live-service QA) | Real client + server interaction with nobody at the keyboard | Expensive to build, cheap to run |
| Headless bot clients | Load-test bots, scripted "fake players" | Protocol and server logic | Bot has no engine, so no physics or AI |
| Server integration tests with fake sessions | Dedicated-server CI suites | Server state, persistence, broadcast rules. Deterministic | Needs a test host. Doesn't cover client patches |
| Network condition simulation | Latency/packet-loss injection (e.g. `clumsy` on Windows) | Ordering bugs that never show on localhost | Cheap |
| Record & replay / crash reporters | Packet capture, "report bug" buttons that bundle logs and state | Turns a "random" bug seen by a player into a reproducible one | Medium |
| State fixtures | Saved worlds at interesting points | Skips hours of play | Cheap |

Studios with no QA budget per change use **automated scenarios as the gate** and treat **real play sessions as
telemetry**: players just play, and a one-key report captures everything when something looks wrong. That's the model
we copy.

## Facts from the code that make this feasible

- **The game is installed on the dev PC** (`F:\Spiele\SteamLibrary\steamapps\common\Subnautica`), so agents can build,
  start a server and start game clients from the shell.
- The launcher has **Options → Allow multiple game instances** (Steam). It starts Subnautica without the Steam bootstrap
  (`Nitrox.Model/Platforms/Store/Steam.cs`). Nitrox identifies players by the name typed at join, so one Steam account can
  run two clients.
- The transport is plain LiteNetLib. A client connects with key `"nitrox"` and sends length-prefixed `Packet.Serialize()`
  bytes, with CRC32C only in Release (`NitroxClient/Communication/NetworkingLayer/LiteNetLib/LiteNetLibClient.cs`).
  `Nitrox.Model(.Subnautica)` also target `net10.0`, so a plain console app can speak the protocol.
- Simulation ownership is handed out from `CellVisibilityChanged` packets
  (`Nitrox.Server.Subnautica/Models/Packets/Processors/CellVisibilityChangedProcessor.cs`, `.../GameLogic/Entities/EntitySimulation.cs`).
  A bot that never reports cells gets no cell-based simulation, but the first player to join takes a transient lock on
  every free global root (`EntitySimulation.AssignGlobalRootEntitiesAndGetData`, called from `JoiningManager`).
- The game-side code runs through Harmony patches inside the real game. A test runner injected the same way can call real
  game methods (e.g. a terminal's `OnHandClick`), and those calls go through the same Nitrox patches a player's input would.
- **A multi-client auto-launch exists (Debug builds only):**
  `Nitrox.Launcher.exe instantlaunch --save <SaveName> --players <Player1> <Player2> …` creates or starts the server for
  that save (embedded), then starts one game per player name with `--instantlaunch <name>`. Each game auto-joins
  `127.0.0.1:11000` from the main menu. The positional form `instantlaunch <save> <players>` is silently ignored
  (ConsoleAppFramework only binds the named options), and the launcher just opens normally.
  (`Nitrox.Launcher/App.axaml.cs`, `Nitrox.Launcher/ViewModels/LaunchGameViewModel.cs` `HandleInstantLaunchForDevelopment`,
  `NitroxPatcher/Patches/Persistent/uGUI_MainMenu_Start_Patch.cs`.) Two fork fixes (S0.1): upstream refused to start the
  second game unless *Allow multiple game instances* was on, and all games shared one `Player.log`. Now each game logs to
  `%APPDATA%\Nitrox\logs\Player-<name>.log` (Unity `-logFile`).
- **`tools/dual-client.ps1`** (S0.1) wraps it: Debug build, optional fixture save (`-Fixture <folder|zip>`, `-Force` moves an
  existing save aside), launch, log locations, `-WaitForJoin` (PASS/FAIL from the server log line
  `Player <name> joined successfully`, read only from what the log gained after launch) and `-Stop` (closes only what the
  last run started). Needs Steam running. **Run on the dev PC 2026-11-01:** both players join about 20 s after launch, and
  a run uses about 3 GB RAM per game.
- **Driving the game windows from an agent** (learned 2026-11-01, computer-use tools):
  - The app name for `request_access` is `Subnautica.exe`; "Subnautica" isn't found.
  - The dev PC has a German **QWERTZ** layout, and the Input System binds by physical key, so Nitrox's chat key `y` is
    sent as **`z`**. Escape, Enter, F-keys and typing through the clipboard work as usual.
  - **Mouse-look can't be automated:** neither absolute cursor moves nor relative `SendInput` turn the camera. So aiming
    and driving (e.g. the #2761 vehicle check) go through the dev bridge (below), not UI automation.
  - Both games run fullscreen on one monitor. `WScript.Shell.AppActivate(<pid>)` switches between them, and `PrintWindow`
    (with `PW_RENDERFULLCONTENT`) captures the background game's frame. So one instance can act while the other is
    observed.
  - Both test players are admins (`LocalhostIsAdmin=True`), so dev-console commands (`warp`, `spawn`) work and are synced.
- **Save → load round trip** (S0.5): `Nitrox.Test/Helper/SaveLoadRoundTrip.cs`. `AssertSurvivesSaveAndLoad(roots…)` runs
  entity trees through the server's real `WorldService.Save`/`LoadPersistedWorld` (JSON) and deep-compares every entity.
  Example: `Nitrox.Test/Server/Serialization/World/WorldServiceSaveLoadTest.cs`.
- **Bug report** (S0.6): `/report <text>` in chat or **F4** writes `%APPDATA%\Nitrox\reports\<time>_<player>.zip` (Nitrox
  logs from the last 24 h, the Unity log, position) on the player's PC, and the server's `report` command writes
  `<time>_server_<player>.zip` (fresh save snapshot, server logs, `server.cfg` without passwords) on the server's PC.
  Verified in game 2026-11-01 (`/report` from both players, F4).
- Server scenario commands already exist: `warp`, `teleport`, `sunbeam`, `aurora`, `time`, `save`, `restart`, `query`
  (`Nitrox.Server.Subnautica/Models/Commands`).
- **Dev bridge** (2026-11-02, replaces the S0.4 in-game runner): every Debug game and the Debug server can be driven
  and inspected from the shell. See [the next section](#the-dev-bridge-driving-games-from-the-shell).
- Missing today: any way to pass scenario arguments through `instantlaunch` (not needed with the bridge), a packet ring
  buffer in the bug report.

## The dev bridge: driving games from the shell

**Debug builds only** (`#if DEBUG`; a Release build contains none of it). Each game opens a localhost-only TCP endpoint
when it starts, named after its `--instantlaunch` player name (`pid<id>` otherwise); the server opens one named
`server`. Ports are stable per name (21000 + hash) and written to `%APPDATA%\Nitrox\bridge\<name>.json`; the Nitrox
logs show `Dev bridge for 'Alice' listening on 127.0.0.1:<port>`. Game commands run on the Unity main thread.

```powershell
tools\dual-client.ps1 -WaitForJoin                    # games + server (opens windows)
tools\bridge.ps1 list                                  # running bridges
tools\bridge.ps1 Alice help                            # all game commands
tools\bridge.ps1 Alice state                           # position, sub, underwater, oxygen, health, inventory (ids, batteries)
tools\bridge.ps1 Alice teleport 372.9 -72.7 1078.4     # like the warp console command, instant
tools\bridge.ps1 Alice look 90 -10                     # yaw, pitch
tools\bridge.ps1 Alice console item flashlight         # any Subnautica dev console command
tools\bridge.ps1 Alice chat "/save"                    # chat; a /command runs as this player
tools\bridge.ps1 Alice find PrecursorDisableGunTerminal          # loaded objects of a type, nearest first
tools\bridge.ps1 Alice entity <nitroxId>                          # GameObject, components, simple fields, child entities
tools\bridge.ps1 Bob get StoryGoalCustomEventHandler.main.gunDisabled
tools\bridge.ps1 Bob set player Survival.food 50
tools\bridge.ps1 Bob call <nitroxId> LiveMixin.Kill
tools\bridge.ps1 Alice call Inventory.main.InternalDropItem <nitroxId> true   # ids convert to the parameter's component
tools\bridge.ps1 Bob use <nitroxId>                    # OnHandClick as the local player (pick up, open, press)
tools\bridge.ps1 Alice screenshot $env:TEMP\a.png
tools\bridge.ps1 server query <nitroxId>               # any server console command, output returned (save, restart, warp, ...)
tools\bridge.ps1 diff                                  # desync detector, see below
```

- **Targets:** `player`, a NitroxId, or `find:<ComponentType>` (the nearest loaded object with that component, for
  scene objects without an id, e.g. `use find:PrecursorDisableGunTerminal PrecursorDisableGunTerminal`). Static paths
  start with a type name (`Story.StoryGoalManager.main.completedGoals`); `member[3]` indexes a list. Private members
  work (reflection with non-public flags). Values: numbers, `true`/`false`, `null`, enum names, `x,y,z`, ids.
- **`diff`** fetches every game's loaded entities (id, tech type, position, parent or container owner, live-extracted
  metadata) and the server's records, and prints mismatches: `GHOST` (game has an id the server doesn't), `MISSING`
  (server has a child of a loaded entity, the game doesn't), `TECHTYPE`, `PARENT` (container contents included),
  `POSITION` (beyond `-Tolerance`, default 1 m; `-MoverTolerance` 15 m for vehicles and loose physics objects;
  creatures only game vs game), `METADATA` (numbers within 0.5 count as equal). Representation quirks are on a short,
  commented `$KnownNoise` list in `bridge.ps1` (`-All` shows them). Takes about 3 s. Run it after every scenario step.
- **Scenarios** are PowerShell scripts in `tools/scenarios/` over `ScenarioHelpers.ps1`: `Start-ScenarioGames` (fresh
  world `bridge-scenario`, both games joined, no-oxygen and no-damage cheats on), `Step` (runs a block, then `diff`),
  `Assert-*`, `Wait-Until`, `Assert-NoDiff -Ids` (no desync for the scenario's entities), `Complete-Scenario` (PASS/FAIL,
  exit code, stops the games). `-NoLaunch` reuses running games, `-Keep` leaves them running. A run takes about 2 min.
  ```powershell
  tools\scenarios\S3-GunDisableSyncs.ps1
  tools\scenarios\S8-EmptyToolNoDupe.ps1
  ```
- **Negative control:** for a new scenario, run it once with the fix removed (move the patch file out or check out the
  pre-fix files, `dotnet build Nitrox.Launcher`, run, restore). It must FAIL there, or it doesn't test the fix.
- **Gotchas:** teleporting does fire trigger volumes (the gun terminal opened). Things only load near a player:
  teleport first, then `Wait-Until { find ... }`. Prefer the game's own entry points (`OnHandClick`, `EnergyMixin.Select`,
  `Inventory.InternalDropItem`) over setting fields, so the Nitrox patches run as they would for a player. Read the
  decompiled method first (AGENTS.md §5.3) to find them. Values that live in assets (story goal keys, teleport
  locations) aren't in the decompiled code: read them through the bridge
  (`get find:PrecursorDisableGunTerminal PrecursorDisableGunTerminal.gunDeactivate.key`,
  `get GotoConsoleCommand.main.data.locations`).

## The plan

### Gate for every fix (no human involved)

| Layer | What runs | Who runs it | Catches |
|---|---|---|---|
| **L1 Unit/persistence tests** | `dotnet test Nitrox.Test/Nitrox.Test.csproj` + new tests per fix. Includes a **save → load round-trip helper** (S0.5) | Agent, every change | Serialization registration, server processors, "gone after restart" bugs |
| **L2 Headless server tests** | Real server hosted inside `Nitrox.Test` (`ServerHarness`), with `FakePlayer`s that speak the real protocol. No game window. Part of `dotnet test`; `--filter TestCategory=L2` runs only them (about 6 s), `TestCategory!=L2` skips them. See [below](#writing-a-headless-l2-test) | Agent, every change, in parallel | Server persistence across restart, broadcasts to the partner, late-join sync, dupes and ghost entries |
| **L3 Automated dual-client scenarios** | Two real Subnautica instances + server on the dev PC, each running a scripted role, asserting on in-game state (S0.1–S0.2, S0.4) | Agent, unattended (PC must be free; about 3–6 min per scenario) | Real client↔client behaviour: AI, physics, Cyclops, story objects |

A fix goes into `coop` only when L1 passes and its spec item's L3 scenario passes (or L2, if that item has no L3 scenario).

### Which layer to use: headless first, the game only where the game decides
**The rule:** if the question is "what does the *server* store, reload, or send to whom?", test it **headless (L1/L2)**.
If the question is "what does *Subnautica itself* do?", meaning AI, physics, player state, a Harmony patch firing, or
a client applying a change to a GameObject, it **needs real game clients (L3)**. Headless tests can't see game logic,
and a fake client only does what the test tells it, so a passing L2 test says nothing about the game side.

| Use **headless (L1/L2)** — fast, parallel, no rig | Use **real game clients (L3)** — the old approach, scarce |
|---|---|
| Entity/metadata survives save → server restart → load | Creature AI, combat, deaths and corpses (S4) |
| Server bookkeeping: registry, reparenting, cell lists, ghost entries (S7 step 1, #2691) | Physics, movement, vehicles, docking |
| Who receives a broadcast (partner, late joiner, only players who can see the cell) | Player state: `currentSub`, swimming, oxygen, precursor flag (S6) |
| Late-join payload: what the server sends a rejoining player | Game objects reacting to state: story objects (S3), Cyclops damage effects and fires, machines producing or burning items (S7 step 2) |
| Duplicates and loss at server level (two adds, missing remove, `EntityDestroyed` deleting a record) | That a Harmony patch fires at all, and when (timing, echo loops) |
| Packet validation, server processors, commands, save upgrades | A client spawning or applying metadata to the right GameObject (e.g. `RestoreMapRoom`) |
| Story goals and PDA state stored per player (server side of #2508) | Anything visual, audio or animation |

**Per spec item:** most fixes are *hybrid*. Prove the server half headless, then use **one** in-game scenario for the game
half.

| Item | Headless covers | Still needs the game |
|---|---|---|
| S1 Cyclops | Damage-point and fire entities persist and reload | The game creating and repairing damage points, fires, the explosion check |
| S2 Equipment | Server never deletes slot items on resync/echo packets; map-room children kept | Resync not sending the delete; map-room children respawned; camera count |
| S3 Gun | — | Everything (game-side flag) |
| S4 Creatures | Death and corpse packets reach the partner | AI, kill path, corpse |
| S5 Leaks/flood | Leak and flood metadata persist and are sent to rejoiners | Water level and leak objects in game |
| S6 Swimming | `InPrecursor` / sub id stored and sent on teleport | Player state itself |
| S7 Containers | Ghost cell entries, remote add/remove bookkeeping, server-side capacity | Machine output, bioreactor burning |
| S8 Battery dupe | Server child count under a dropped tool | The game's pickup conversion |
| S9 Escape pod | Pod registered and sent in the join payload | Player placed inside the pod |
| S10 Scanner room | Scan target/camera metadata persist and reach the partner | Scanner UI, blips, `ResourceTracker` |

### Writing a headless (L2) test
`Nitrox.Test/Helper/Headless/`: `ServerHarness` starts the real `Nitrox.Server.Subnautica` host inside the test process
(all services and processors; left out: console input, the launcher connection, the dev bridge and the start-up status
log). It uses a free UDP port and a fresh save in `%TEMP%\nitrox-l2\<name>-<guid>` (deleted afterwards; set
`NITROX_L2_KEEP=1` to keep it, the server log is in its `logs` folder). `FakePlayer` speaks the real protocol (key
`"nitrox"`, length-prefixed `Packet.Serialize()`, CRC32C in Release) using only `Nitrox.Model(.Subnautica)`, does the real
join handshake and records every packet it gets. Needs Subnautica installed, not running.

```csharp
[TestClass]
[TestCategory("L2")]
public class MyFeatureTest
{
    [TestMethod]
    public async Task PartnerSeesIt_AndItSurvivesRestart()
    {
        await using ServerHarness server = await ServerHarness.StartAsync("L2-MyFeature"); // about 0.6 s
        FakePlayer alice = await server.JoinAsync("Alice");  // policy, reservation, join queue, InitialPlayerSync, PlayerSyncFinished
        FakePlayer bob = await server.JoinAsync("Bob");

        alice.Send(new EntityReparented(itemId, lockerStorageId));             // whatever the game would send
        await bob.WaitForAsync<EntityReparented>(p => p.Id == itemId);         // broadcast reached the partner
        server.EntityRegistry.TryGetEntityById(itemId, out Entity item).Should().BeTrue(); // server state, via its DI container

        await server.RestartAsync();                                            // save, stop, start on the same save
        FakePlayer bobAgain = await server.JoinAsync("Bob");                    // bobAgain.InitialSync = join payload
        bobAgain.Send(new CellVisibilityChanged(bobAgain.SessionId, [cell], []));
        SpawnEntities reply = await bobAgain.WaitForAsync<SpawnEntities>(p => p.SpawnedCells.Contains(cell));
    }
}
```

- **Server state:** `server.Get<T>()` resolves any server service (`EntityRegistry`, `WorldEntityManager`, `PlayerManager`,
  ...). `server.SaveAsync()` writes the save; the files are in `server.SavePath`.
- **Waiting:** most packets get no reply. Wait for the broadcast the partner receives (`WaitForAsync`), or poll server
  state with `ServerHarness.WaitUntilAsync(() => ..., "reason")`. `AssertNotReceivedAsync` can only show that nothing came
  *yet*; prefer a reply that always comes (the `SpawnEntities` answering `CellVisibilityChanged` is sent even when empty).
- **Sharing:** a server start costs about 0.6 s (first one in a run about 1.2 s), so one per class (`[ClassInitialize]`)
  or per test are both fine. With a shared server, call `server.DisconnectPlayersAsync()` in `[TestCleanup]` so the next
  test can use the same player names.
- **Negative control:** revert the fix, the test must fail (same rule as L3).
- **Can't be faked:** anything the game decides. A `FakePlayer` sends exactly what the test tells it, so the packets
  must match what the client really sends (read the client code: `NitroxClient/GameLogic/Items.cs` and friends). It
  doesn't spawn what it receives, reports no cells unless told to and never answers server requests the game would
  answer. It still gets simulation locks: on join, `EntitySimulation.AssignGlobalRootEntitiesAndGetData` gives the
  joining player a transient lock on every free global root (escape pod, vehicles), and it simulates none of them.
  Two servers in one process share static state (logging dedup, the packet serializer); pooled processor contexts
  were made per server for this.
- Examples: `Nitrox.Test/Server/Headless/` (S7 ghost cell entries, S9 join payload, S8 tool batteries).

### Game-time budget (the rig is the bottleneck)
There is one game rig: one PC, port 11000, `tools\game-lock.ps1`. A launch plus join takes minutes, so game time is the
scarcest resource. Rules:
- **Prove as much as possible in L1.** Server processors, metadata logic and `SaveLoadRoundTrip` run in seconds and in
  parallel. An L1 test that fails without the fix is the preferred negative control.
- **Per fix, run each scenario in game until it passes once.** No repeat confirmation runs. Run an in-game negative
  control only when the root cause is a hypothesis.
- **Batch:** launch once and run several scenarios with `-NoLaunch`. Relaunch only for restart or rejoin steps. Build
  setup-heavy worlds once as fixture saves.
- **Regression:** the full scenario suite runs once per integration into `coop`, not once per fix branch.

### L3 in detail: the scenario runner (the key investment)
**Built differently (2026-11-02):** the dev bridge above plus `tools/scenarios/*.ps1`. Scenarios run outside the game
and act through the bridge, so they need no per-role arguments and no in-game result files. The original design below
is kept for reference.

1. **Auto-join:** reuse `instantlaunch` (above). Extend it to forward extra per-player arguments
   (`--nitrox-scenario`, `--nitrox-role`) into `StartMultiplayerAsync([...])`, and add a `--save-dir`/fixture option
   so the orchestrator can choose the world.
2. **Scenario runner** (client, active only with `--nitrox-scenario <Name> --nitrox-role A|B`): a MonoBehaviour that starts
   after initial sync and runs C# steps such as:
   ```csharp
   [Scenario("ChargerBatteriesSurviveRestart")]
   class ChargerScenario : IScenario {
       public async Task RoleA(Ctx c) { var ch = await c.Spawn(TechType.BatteryCharger, c.Fixture("base")); await c.Insert(ch, TechType.Battery); await c.Barrier("inserted"); }
       public async Task RoleB(Ctx c) { await c.Barrier("inserted"); await c.Reconnect(); c.Assert(c.Find<Charger>().HasItem(TechType.Battery)); }
   }
   ```
   The two roles coordinate through a barrier: a tiny test-only packet, or chat messages, relayed by the server. The server
   side of a scenario (restart, time skip) uses existing server commands.
3. **Results:** each client writes `scenario-result-<role>.json` (pass/fail, assertion messages, timings) and quits. The
   script collects both results plus server and `Player.log` logs.
4. **Orchestrator:** `tools/run-scenario.ps1 -Name ChargerBatteriesSurviveRestart [-Fixture base-with-charger]` builds,
   copies the fixture world, starts the server and two windowed low-res instances, waits, and prints PASS/FAIL. Agents
   run this. `-All` runs every scenario (overnight batch).
5. Steps call real game APIs (spawn, `OnHandClick`, `Inventory`/`ItemsContainer` operations, teleport), so they go through
   the same Harmony patches as player input. **Not** covered: things that need real mouse/keyboard feel or a human eye.

Fallback when a scenario can't assert something (visual-only bugs like the laser-cutter beam): the agent can take
screenshots of both windows during the run and compare them. Use this rarely; screenshot checks are flaky.

### Human layer: the playthrough *is* the acceptance test
- **No per-fix sessions.** Fixes reach players in **batches** as new `coop` builds, each one gated by L1–L3.
- **One-key bug report** (S0.6, built): **F4** or `/report <text>` in chat. When something looks wrong, a player presses the
  key and keeps playing. Collect `%APPDATA%\Nitrox\reports\` from **both** PCs: each player's PC has
  `<time>_<player>.zip` (Nitrox logs, `Player.log`, position), and the server's PC also has `<time>_server_<player>.zip`
  (save snapshot taken at that moment, server logs, `server.cfg`). An agent turns the bundles into a reproduction (L1
  round-trip test with the snapshot, L2 replay or a new L3 scenario) and a fix. Still missing: the last N minutes of
  packets (ring buffer).
- **Safety nets, so skipping manual tests is acceptable:**
  - Back up the world before every build update. Nitrox keeps world backups (the launcher can find them); the update
    script also copies the save folder.
  - Keep the previous `coop` build next to the new one (tag each playable build `play-YYYYMMDD`), so rolling back takes
    one click.
  - Change the save format only through migrations, and always test them against a copy of a real long-running save
    (L1 + L3 with that save as the fixture).
  - Release builds go to players, Debug builds to tests.

### Optional: network chaos
Run L3 scenarios through `clumsy` (100–200 ms latency, 1–2 % loss) in the overnight batch. Ordering bugs (dupes, items
coming back) often show up only then.

## Build order (spec item S0)

| Step | What | Effort | Unlocks |
|---|---|---|---|
| S0.1 | `tools/dual-client.ps1`: Debug build + copy fixture save + `Nitrox.Launcher.exe instantlaunch --save <save> --players A B` (**done**) | hours (`instantlaunch` exists) | One-command dual-client runs |
| S0.2 | Forward `--nitrox-scenario/--nitrox-role` through `instantlaunch`; clients quit on scenario end | small | Unattended runs |
| S0.3 | Headless players: built as `ServerHarness` + `FakePlayer` in `Nitrox.Test/Helper/Headless` (**done**, see [L2](#writing-a-headless-l2-test)) | 1–2 days | L2 |
| S0.4 | Scenario runner: built as the dev bridge (`tools/bridge.ps1`, `diff`) + `tools/scenarios/` with S3 and S8 (**done**) | 1 day | L3, the per-fix gate |
| S0.5 | Save → load round-trip test helper (**done**) | small | L1 for persistence bugs |
| S0.6 | `/report` bundle hotkey (**done**) + packet ring buffer (open) | 1 day | Playthrough-as-testing |
| S0.7 | Save fixtures for S1–S10 | grows with use | Fast scenario setup |

S0.1, S0.2, S0.5 and S0.6 are worth doing before any bug fix. S0.4 should be done before the "Build" items in the spec. The
"Adopt" items (upstream PRs) can be merged earlier, gated by L1 plus a dual-client smoke run.

Upstream has wanted this for years (#1533 "Nitrox.Test.EndToEnd.Subnautica"). The auto-join args, the bot and the
scenario runner would all make good upstream PRs.
