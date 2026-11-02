# AGENTS.md: working on this Nitrox fork

Read this before changing anything. It covers what this fork is for, how to build and test it, how the code fits
together, and the workflows we use. Claude Code loads it through `CLAUDE.md`. Other agents should read it directly.

## 1. Purpose of this fork

- **Repo:** [Nitrox Tandem](https://github.com/Denis-v-ops/Nitrox-Tandem), a fork of
  [SubnauticaNitrox/Nitrox](https://github.com/SubnauticaNitrox/Nitrox), the Subnautica multiplayer mod (C#, Harmony, Unity/Mono).
- **Goal:** two players can play the **whole Subnautica story in co-op** without
  desyncs that lose progress. Judge every change by that goal. Linux/macOS/VR/Docker/public-server features are out of
  scope unless they block it.
- **What to work on:** [docs/fork/FIX_SPEC.md](docs/fork/FIX_SPEC.md) (the top 10, in order), backed by
  [docs/fork/ISSUE_TRIAGE.md](docs/fork/ISSUE_TRIAGE.md) (every open upstream issue, tiered) and
  [docs/fork/TESTING.md](docs/fork/TESTING.md) (how to test with two players). New co-op features (shared story
  gates, rescue window, partner signal, cozy home, and more) are specified in
  [docs/fork/COOP_FEATURES_SPEC.md](docs/fork/COOP_FEATURES_SPEC.md); its §2 design principles apply to every
  player-facing change.

## 2. Remotes and branches

| Remote | URL | Use |
|---|---|---|
| `origin` | `https://github.com/Denis-v-ops/Nitrox-Tandem.git` | Our fork. Push here |
| `upstream` | `https://github.com/SubnauticaNitrox/Nitrox.git` | The original. Fetch only |

| Branch | Purpose |
|---|---|
| `master` | Mirror of `upstream/master`. Never commit fork-only work here; fast-forward it from upstream. |
| `coop` | **Integration branch** we build and play from: fork docs + adopted upstream PRs + our fixes. |
| `fix/<issue>-<slug>` | One branch per spec item or fix, cut from `coop`, merged back into `coop`. |
| `upstream/<slug>` | A fix we want to offer upstream: cut from `master`, holds only that fix (no fork docs). |

Sync with upstream regularly (do it in a clean tree):

```bash
git fetch upstream
git switch master && git merge --ff-only upstream/master && git push origin master
git switch coop && git merge master        # resolve conflicts, build, run tests, then push
```

The fork's name is **Nitrox Tandem**. Its identity lives in `Nitrox.Model/Core/NitroxFork.cs`: name, repo URLs, and no
official-update prompts. The launcher title, the in-game version text, crash reports and the updates page use it.
`README.md` belongs to the fork: on merge conflicts with upstream, keep ours and fold in anything relevant from theirs.

Adopting an open upstream PR (many of our top-10 items already have one, see the spec):

```bash
git fetch upstream pull/<N>/head:pr-<N>
git switch -c fix/<issue>-<slug> coop
git merge --no-ff pr-<N>                   # or cherry-pick specific commits
```

Then build, run tests, and do the two-player verification steps from the spec item before merging into `coop`. Read the
PR's open review comments first (`gh pr view <N> -R SubnauticaNitrox/Nitrox --comments` and
`gh api repos/SubnauticaNitrox/Nitrox/pulls/<N>/comments`). Unresolved maintainer objections are often real bugs.

**Contributing back:** upstream's `CONTRIBUTING.md` requires PRs to **declare AI assistance** in the PR template, to stay
small (one issue per PR), and to be tested in game. Never push to `upstream`. Never open, comment on or close upstream
issues or PRs unless the user asks.

## 3. Build and test (verified on this machine)

Prerequisites: .NET SDK **10** (`global.json` is absent, so the newest installed SDK is used) and **Subnautica installed**.
Visual Studio or Rider are optional. Solution file: `Nitrox.slnx`.

```bash
dotnet build Nitrox.slnx                                    # Debug, about 40 s cold / 12 s warm
dotnet test Nitrox.Test/Nitrox.Test.csproj                  # MSTest; 273 pass, 13 Linux-only skipped (about 6 s)
dotnet build Nitrox.Launcher -c Release -r win-x64          # what CI ships for Windows
```

- **Game discovery:** `Directory.Build.targets` → target `FindGameAndIncludeReferences` uses the
  `Nitrox.Discovery.MSBuild` package to find the install (Steam/Epic/…). The fallback is env var
  `SUBNAUTICA_INSTALLATION_PATH`. On this machine it finds
  `F:\Spiele\SteamLibrary\steamapps\common\Subnautica\` (game version 83031; minimum is `GameMinimumVersion` 82304 in
  `Directory.Build.props`). **Nothing that references game code builds without the game**, including `Nitrox.Test`.
- **Publicizer:** game assemblies (`Assembly-CSharp*.dll`) are referenced with `Publicize="true"` (BepInEx
  AssemblyPublicizer), so you can access private game members directly from `NitroxClient`/`NitroxPatcher`.
- **Known flake:** a *cold* parallel build can fail with `MSB4018 PublicizeTask ... being used by another process`
  (two target frameworks publicize at once). Just run the build again. If it keeps failing, use `-m:1`.
- **Warnings are errors** (`TreatWarningsAsErrors=true`). Exceptions: NuGet vulnerability warnings NU1901–NU1904,
  `DIMA001` (direct DI container use; 8 known occurrences) and nullable warnings. Fix new warnings rather than suppressing them.
- **Target frameworks:** `NitroxClient`, `NitroxPatcher`, `Nitrox.Model`, `Nitrox.Model.Subnautica` are multi-targeted to
  `net472` (what runs inside the game's Mono) and `net10.0` (for tests/tools). `Nitrox.Server.Subnautica`,
  `Nitrox.Launcher` and `Nitrox.Test` are `net10.0`. `LangVersion` 14 everywhere, through PolySharp polyfills on net472.
- **Output:** `Nitrox.Launcher/bin/<Config>/net10.0/` contains the launcher; the game-side DLLs are copied into its
  `lib/net472/`; the server is bundled alongside. **Run `Nitrox.Launcher.exe` from there** to start a server and the game
  with your build.
- **CI does not run on this fork.** The build and test workflows (`buildingMaster.yml`, `buildingPullRequest.yml`,
  `testingPullRequest.yml`) gate their first job on `github.repository_owner == 'SubnauticaNitrox'` and run on
  self-hosted Linux runners. `testResults.yml` only runs after them. Build and test locally before pushing.

## 4. Running, debugging and logs

- **Play/test a build:** run `Nitrox.Launcher/bin/Debug/net10.0/Nitrox.Launcher.exe`, create or start a server, launch the
  game, then use **Multiplayer** on the main menu and join `127.0.0.1` (default port `11000`, from
  `Nitrox.Model/Constants/SubnauticaServerConstants.cs`).
- **Headless first.** Anything about what the *server* stores, reloads or sends to whom is tested headless (unit tests
  and the in-process server harness): seconds per test, many in parallel, no game. Only what *Subnautica itself* does
  needs real game clients: AI, physics, player state, patches firing, a client applying state to GameObjects. The table
  is in [TESTING.md](docs/fork/TESTING.md#which-layer-to-use-headless-first-the-game-only-where-the-game-decides).
- **One game rig, take turns:** only one dual-client run can exist on this PC at a time. It uses fixed port 11000, and
  each worktree's launch copies its own `NitroxPatcher.dll` into the shared game install. Hold the lock from before
  launching until after `-Stop`: `tools\game-lock.ps1 -Acquire -Owner <you>` … `tools\game-lock.ps1 -Release -Owner <you>`.
  Check who has it with `-Status`. Never restart, shut down or log off the PC, and only stop processes you started.
- **Two clients on one PC, no clicking (Debug builds):** `tools\dual-client.ps1 -WaitForJoin` builds, runs
  `Nitrox.Launcher/bin/Debug/net10.0/Nitrox.Launcher.exe instantlaunch --save <SaveName> --players Alice Bob` (server for
  that save plus one game per name, each auto-joining localhost; the positional form is silently ignored), prints PASS
  once both have joined (about 20 s on this PC), and `tools\dual-client.ps1 -Stop` closes it all. `-Fixture <save
  folder|zip>` starts from a prepared world. This opens game windows, so tell the user before running it. Driving the
  windows with computer use has quirks (QWERTZ keys, no mouse-look); see TESTING.md. By hand: launcher **Options → Allow
  multiple game instances** (Steam). See [docs/fork/TESTING.md](docs/fork/TESTING.md) for the automated test plan.
  Upstream has no CI for gameplay and this project doesn't rely on per-fix human playtests, so **automated verification is your job**.
- **Controlling the games from the shell (Debug builds):** the dev bridge. `tools\bridge.ps1 Alice state`,
  `... teleport x y z`, `... console item flashlight`, `... get/set/call <id|Type.main> ...`, `... use <id>`,
  `tools\bridge.ps1 server <server command>` (output returned), and `tools\bridge.ps1 diff` (desync detector: each game's
  loaded entities vs the server's records and vs each other). Scenario scripts over it live in `tools/scenarios/`
  (`S3-GunDisableSyncs.ps1`, `S8-EmptyToolNoDupe.ps1`, about 2 min each). Localhost only, absent from Release builds.
  Details, diff kinds and gotchas: [TESTING.md](docs/fork/TESTING.md#the-dev-bridge-driving-games-from-the-shell).
- **Headless server tests (L2):** `Nitrox.Test/Helper/Headless/` runs the real server in-process with fake players over the real
  protocol (join, broadcasts, restart). Use it before the game rig; see TESTING.md, "Writing a headless (L2) test".
- **Persistence tests:** `Nitrox.Test/Helper/SaveLoadRoundTrip.cs` (`AssertSurvivesSaveAndLoad(entityTrees…)`) runs
  entities through the server's real save and load code. Use it for every "gone after restart" fix.
- **Logs:** Nitrox (launcher, server, client) → `%APPDATA%\Nitrox\logs`. Game →
  `%UserProfile%\AppData\LocalLow\Unknown Worlds\Subnautica\Player.log` (instant-launched games:
  `%APPDATA%\Nitrox\logs\Player-<name>.log`). Always ask for or collect both sides when
  debugging a desync. **Bug reports** from players (F4 or `/report <text>`) are zips in `%APPDATA%\Nitrox\reports`:
  collect them from both PCs (the server's PC also has a save snapshot taken at that moment).
- **Attaching a debugger to the game:** dnSpyEx + Unity debug Mono DLL (`MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll`
  swap), then *Debug → Start Debugging → Unity (Connect)* on `127.0.0.1:5555`. Debug builds raise the network timeout to
  5 min so breakpoints don't disconnect you. Full steps:
  <https://subnauticanitrox.github.io/Documentation/docs/development/tutorials/debugging>.
- **Server console commands** (`Nitrox.Server.Subnautica/Models/Commands`) help set up scenarios: `warp`, `teleport`, `time`,
  `sunbeam`, `aurora`, `save`, `restart`, `query` (inspect an entity by id), plus `Debugging/` commands. Run them from the
  shell with `tools\bridge.ps1 server <command>` (Debug server; the output comes back).

## 5. Architecture map

```
Nitrox.Launcher (Avalonia, net10.0) ── starts ──► Nitrox.Server.Subnautica (net10.0, Generic Host + DI)
      │  injects NitroxPatcher into the game                    ▲
      ▼                                                         │ LiteNetLib UDP, BinaryPack packets
Subnautica.exe (Unity/Mono) ── NitroxPatcher (Harmony) ── NitroxClient (game logic, Autofac)
                 shared by all:  Nitrox.Model (generic) + Nitrox.Model.Subnautica (packets, entities)
```

| Project | Role |
|---|---|
| `Nitrox.Model` | Generic core: `Packet` base, networking helpers, logging, config, platform/store detection, paths (`NitroxDirectory`, `NitroxUser`). |
| `Nitrox.Model.Subnautica` | Subnautica packets (`Packets/`, about 130), entity and metadata data model (`DataStructures/GameLogic/`). |
| `NitroxPatcher` | Entry point inside the game + all Harmony patches (`Patches/Dynamic`, `Patches/Persistent`). |
| `NitroxClient` | Client game logic: managers, packet processors, spawners, metadata extractors/processors, MonoBehaviours, debuggers. |
| `Nitrox.Server.Subnautica` | Dedicated server: packet processors, world/entity managers, story, persistence, commands. |
| `Nitrox.Launcher` | UI to manage servers and launch the game. Bundles everything into its output folder. |
| `Nitrox.Test` | MSTest suite (serialization, processors, patches, server logic). |
| `Nitrox.Assets.Subnautica` | Shared project: language files, asset bundles, `protobuf-net.dll`, `discord_game_sdk.dll`. |

### 5.1 Startup
- **Launcher → game:** `Nitrox.Launcher/ViewModels/LaunchGameViewModel.cs` copies `lib/net472/NitroxPatcher.dll` into
  `Subnautica_Data/Managed/`. `Nitrox.Launcher/Models/Utils/NitroxEntryPatch.cs` (dnlib) then inserts a call to
  `NitroxPatcher.Main.Execute()` at the start of `PlatformUtils.Awake` in `Assembly-CSharp.dll`. Launching singleplayer
  removes it again. The launcher directory reaches the game via env `NITROX_LAUNCHER_PATH` / `--nitrox <dir>`.
- **In game:** `NitroxPatcher/Main.cs` installs an assembly resolver for `<launcher>/lib/net472` and sets up logging.
  `NitroxPatcher/Patcher.cs` builds the Autofac container (`NitroxClient/ClientAutoFacRegistrar.cs`), applies
  persistent patches, and applies or removes **dynamic** patches when multiplayer starts or ends (Harmony id
  `com.nitroxmod.harmony`).
- **Joining:** `NitroxClient/MonoBehaviours/Gui/MainMenu/ServerJoin/JoinServerBackend.cs` → `IMultiplayerSession.ConnectAsync`.
  The connection-state machine lives in `NitroxClient/Communication/MultiplayerSession/ConnectionState/`. After the game
  loads, `InitialPlayerSyncProcessor` runs the `NitroxClient/GameLogic/InitialSync/*` steps.
- **Server:** started by the launcher (`Nitrox.Launcher/Models/Design/ServerEntry.cs`) with
  `--save <name> --game-path <path> [--embedded]` (options in `Nitrox.Model/Configuration/ServerStartOptions.cs`).
  `Nitrox.Server.Subnautica/Program.cs` sets up a Generic Host. Services are registered in
  `Extensions/ServiceCollectionExtensions.cs` (`ServiceScan.SourceGenerator`). The server parses the game's batch/cell
  files with AssetsTools.NET, so **it needs the game install too**.

### 5.2 Packets and processors
- **Define:** `[Serializable] public sealed class Foo : Packet` in `Nitrox.Model.Subnautica/Packets/`, with get-only
  properties and a constructor (example: `ModifyConstructedAmount.cs`). Serialization uses Nitrox.BinaryPack and is
  registered automatically by reflection (`Packet.InitSerializer()`). Set `DeliveryMethod`/`UdpChannel` in the constructor
  if needed.
- **Server:** `Nitrox.Server.Subnautica/Models/Packets/Processors/FooProcessor.cs` implementing
  `IAuthPacketProcessor<Foo>` (joined players; the context has `ReplyAsync`/`SendToOthersAsync`/`SendToAllAsync`) or
  `IAnonPacketProcessor<Foo>` (pre-join). Exactly **one constructor**. Auto-registered. A packet with no server processor
  is **rebroadcast to everyone else** by `DefaultServerPacketProcessor` and **not persisted**, a common root cause of
  "fine until restart" bugs. `TransmitIfCanSeePacketProcessor` relays only to players who can see the entity.
- **Client:** `NitroxClient/Communication/Packets/Processors/FooProcessor.cs` implementing `IClientPacketProcessor<Foo>`.
  It runs on the Unity main thread from `Multiplayer.Update`. A missing client processor throws.
- **Echo prevention:** when a processor calls game code that is itself patched to send packets, wrap it:
  `using (PacketSuppressor<Foo>.Suppress()) { ... }` (`NitroxClient/Communication/PacketSuppressor.cs`).
- **Tests enforce it:** `Nitrox.Test/Model/Packets/PacketsSerializableTest.cs` round-trips every packet. New leaf field
  types need an entry in `Nitrox.Test/Helper/Faker/NitroxFaker.cs`. `.../Processors/PacketProcessorTest.cs` checks every
  packet has a processor with one constructor.

### 5.3 Harmony patches
- `NitroxPatcher/Patches/Dynamic/` (multiplayer-only, about 350) and `Patches/Persistent/` (always on, about 20). Base class
  `NitroxPatch` (`Patches/NitroxPatch.cs`). Patches are discovered automatically (`Modules/NitroxPatchesModule.cs`).
- **Convention (required by a source generator in `Nitrox.Analyzers`):**
  ```csharp
  // NitroxPatcher/Patches/Dynamic/EscapePod_Awake_Patch.cs (real file)
  public sealed partial class EscapePod_Awake_Patch : NitroxPatch, IDynamicPatch
  {
      public static readonly MethodInfo TARGET_METHOD = Reflect.Method((EscapePod t) => t.Awake());

      public static bool Prefix(EscapePod __instance)
      {
          return !EscapePodEntitySpawner.SuppressEscapePodAwakeMethod;
      }
  }
  ```
  The class must be `partial`, and the static methods must be named exactly `Prefix`/`Postfix`/`Transpiler`/`Finalizer`.
  The generator writes `Patch()`. Get services with `Resolve<T>()`.
- **Transpilers:** use `NitroxPatcher/PatternMatching` (`InstructionsPattern`, `.Transform`, `.InsertAfterMarker`). Every
  transpiler must be registered with its IL instruction delta in
  `Nitrox.Test/Patcher/Patches/PatchesTranspilerTest.cs`, or the test fails.
- **Game source: read the decompiled C#, it's already on this PC.** It lives *outside* the repo because it's the game's
  code, so **never commit or paste large parts of it**:
  - `F:\nitrox\game-src\83031\` holds `Assembly-CSharp` (1,979 files, e.g. `Player.cs`, `StoryGoalCustomEventHandler.cs`).
  - `F:\nitrox\game-src\83031-firstpass\` holds `Assembly-CSharp-firstpass` (UWE utilities, 1,269 files).
  - The folder name is the game version from `plastic_status.ignore`.
  - Search it like normal code: `rg -n "void OnHandClick" F:\nitrox\game-src\83031`.
  - After a Subnautica update, run `tools\decompile-game.ps1`. It needs `ilspycmd`, which is installed
    (`dotnet tool install --global ilspycmd`); call it from PowerShell, because Git Bash doesn't have `~/.dotnet/tools`
    on PATH. Then update the paths here.
  - One-off lookups: `ilspycmd -t <FullTypeName> <...>\Subnautica_Data\Managed\Assembly-CSharp.dll`. GUI: ILSpy or
    dnSpyEx (also the debugger, see §4).
  - Decompiled code can be subtly off (e.g. compiler-generated state machines, field initialisers). For anything
    timing-critical, confirm in the IL or in game.
  **Never guess game behaviour.** Upstream's CONTRIBUTING warns that AI-written Nitrox code goes wrong
  mostly because of wrong assumptions about game internals. Mark unverified assumptions as such.

### 5.4 Entities, metadata and simulation
- **Entities** (`Nitrox.Model.Subnautica/DataStructures/GameLogic/`): `Entity` (`NitroxId Id`, `TechType`, `Metadata`,
  `ParentId`, `ChildEntities`). Subtypes include `WorldEntity` (transform, `ClassId`) and its children (`GlobalRootEntity`,
  `CellRootEntity`, bases in `Entities/Bases/`), plus child/inventory types (`PrefabChildEntity`,
  `PathBasedChildEntity`, `InventoryEntity`, `InventoryItemEntity`, `InstalledModuleEntity`, `InstalledBatteryEntity`, …).
- **Serialization attributes:** `[DataContract]`, `[DataMember(Order = n)]`, a protected `[IgnoreConstructor]` parameterless
  constructor, and `[ProtoInclude(tag, typeof(Sub))]` on the **parent** type. Pick an unused tag; in
  `Entities/Metadata/EntityMetadata.cs` the highest is 91. Don't copy the existing duplicate use of
  `PlantableMetadata` (tags 65 and 82).
- **Client spawners** are registered **by hand** in the `NitroxClient/GameLogic/Entities.cs` constructor
  (`entitySpawnersByType[...] = ...`). A new entity type needs a line there.
- **Metadata** (persistent per-entity state such as health, on/off flags or names): extractor
  `NitroxClient/GameLogic/Spawning/Metadata/Extractor/*` (`EntityMetadataExtractor<TComponent,TMetadata>`) and processor
  `.../Metadata/Processor/*` (`EntityMetadataProcessor<TMetadata>`). Both are auto-registered and must derive **directly**
  from the generic base. Send changes with `Entities.EntityMetadataChanged(...)`. The server's
  `EntityMetadataUpdateProcessor` stores the new metadata, so it is persisted and sent to late joiners.
  **Prefer metadata over ad-hoc packets for any state that must survive restart or rejoin.**
- **Simulation ownership:** exactly one client simulates each creature or vehicle and broadcasts its state. The lock
  types are in `Nitrox.Model/DataStructures/SimulationLock.cs`. Client side: `NitroxClient/GameLogic/SimulationOwnership.cs`
  and `GameLogic/Simulation/`. Server side: `Models/GameLogic/SimulationOwnership.cs`, `Entities/EntitySimulation.cs`,
  `SimulationWhitelist.cs`. Ownership follows the cells a player reports visible (`CellVisibilityChanged` →
  `CellVisibilityChangedProcessor`). Many desyncs are "only the simulating player sees it": always ask *who simulates
  this, and what does the other client do?*
- **World spawning:** `Models/GameLogic/Entities/Spawning/BatchEntitySpawner.cs` generates entities from the game's
  batch files the first time a cell is seen. `WorldEntityManager` tracks them.

### 5.5 Server organisation and persistence
- `Nitrox.Server.Subnautica/Services/` holds hosted services: networking, packet registry, save/autosave, time, commands,
  console, port forwarding, LAN broadcast.
- `Models/GameLogic/`: `PlayerManager`, `StoryManager`, `StoryScheduler`, `PdaManager`, `EscapePodManager`,
  `Bases/BuildingManager`, `Entities/*`.
- Commands: `Models/Commands/*Command.cs` (`ICommandHandler<...>`, `[RequiresPermission]`, `[Description]`), auto-registered.
- **Saves:** `%APPDATA%\Nitrox\saves\<SaveName>\`, holding `Version.json`, `PlayerData.json`, `WorldData.json`,
  `GlobalRootData.json`, `EntityData.json` (Newtonsoft, `TypeNameHandling.Auto`), plus `server.cfg` (options:
  `Nitrox.Model/Configuration/SubnauticaServerOptions.cs`) and `backups/`. Save-format changes need an upgrade in
  `Models/Serialization/SaveDataUpgrades/`. **Never break players' existing saves**: test upgrades on a copy of it.

### 5.6 Adding a synced feature end to end (checklist)
1. Packet in `Nitrox.Model.Subnautica/Packets/`, *or* a `*Metadata` class for persistent state (+ `ProtoInclude`).
2. Patch in `NitroxPatcher/Patches/Dynamic/` that detects the local action and sends the packet or metadata change.
3. Server processor: validate, **update the entity/world state so it persists**, relay to others.
4. Client processor (or metadata processor) that applies the change, wrapped in `PacketSuppressor` where needed.
5. Initial sync: make sure a player who joins later gets the state (metadata does this for you).
6. Tests: serialization (automatic), faker entry for new field types, transpiler registration, plus a unit test for the
   server logic. Then the scenario run from `docs/fork/TESTING.md`.

## 6. Code style and gotchas

- Analyzer diagnostics (`Nitrox.Analyzers`) fail the build: **NUSL001–003**: don't use `?.`/`??` on `UnityEngine.Object`,
  use the generated `AliveOrNull()`. **NEU001**: IEnumerators must be iterated. **NXLZ001**: localization keys must exist
  in `Nitrox.Assets.Subnautica/LanguageFiles`.
- `.editorconfig` style that reviewers enforce (not checked by `dotnet build`): explicit types (no `var`), always use
  braces, file-scoped namespaces, usings outside the namespace, camelCase private fields with no underscore,
  `ALL_UPPER` constants, LF line endings.
- `InternalsVisibleTo Nitrox.Test` is set on all Nitrox libraries, so tests can reach `internal` members.
- Logs: `Log.Info/Warn/Error` (client, `Nitrox.Model/Logger/Log.cs`) and ZLogger `ILogger<T>` (server).
- Known oddity: `NitroxPatcher/Patches/Dynamic/Drillable_ManagedUpdate_Patch.cs` isn't `partial` and inherits a custom
  `Patch()`, so its `Transpiler` may never be applied at runtime. Unverified.

## 7. Working agreements for agents

- One spec item or fix per branch. Keep diffs small and in the style of the surrounding code.
- Before writing code that depends on game behaviour, read the decompiled game method. Say what you verified.
- Done means: `dotnet build Nitrox.slnx` passes, `dotnet test` passes, the item's scenario in `tools/scenarios/` passes
  (write one if it's missing, and check it FAILS without the fix), and there's a short note in the PR or commit saying
  what was verified and what wasn't.
- Don't change the save format without an upgrade path, and don't edit a player's save in place.
- Keep `docs/fork/FIX_SPEC.md` current: tick off items, record findings and decisions.
