# Fix spec: top 10 for a two-player story run

This is the work list for the `coop` branch. The ranking method and all other issues are in
[ISSUE_TRIAGE.md](ISSUE_TRIAGE.md). How each fix is verified is in [TESTING.md](TESTING.md). Background for agents is
in [AGENTS.md](../../AGENTS.md).

Snapshot: upstream `master` @ `62ef043a`, 2026-11-01. Line numbers refer to that commit and will drift, so search by
symbol name.

## How to read an item

- **Mode**:
  - **Adopt**: merge an open upstream PR as is.
  - **Adopt + fix**: merge it, then apply the listed corrections.
  - **Finish**: an upstream PR exists but isn't usable yet, so take it over.
  - **Build**: no PR exists; implement it ourselves.
  - **Verify**: reproduce on current master first.
- **Bites in**: when in a playthrough you'd first hit the bug (Start / Early / Mid / Late). Use it to schedule work
  ahead of where a playthrough is in the story.
- **Evidence tags**: *confirmed* = read in Nitrox code or decompiled game IL. *hypothesis* = plausible, must be checked
  before coding. *[asset]* = a value from Unity serialized data that the IL can't show.
- **Done when**: `dotnet build` + `dotnet test` pass, the item's automated check passes (see TESTING.md), and a line
  is added to the **Log** at the bottom.

## Priority and execution order

| Rank | Item | Issues | Bites in | Mode | Size |
|---|---|---|---|---|---|
| 1 | S1 Cyclops damage, leaks and fires persist | #2220 #2230 | Mid | Adopt + fix #2881 | M (merge + test) |
| 2 | S2 Items in charger / scanner-room / upgrade slots vanish | #2259 #2696 #2576 | Early–Mid | Build | M |
| 3 | S3 Precursor gun deactivation not seen by partner | #2640 | Late (story gate) | Build | S |
| 4 | S4 Creatures don't die for the other player | #2510 #1701 | Early → | Adopt #2902 + #2867 with fix | S–M |
| 5 | S5 Base leaks and flooding desync | #2769 #2808 | Mid | Adopt #2798 + Finish #2811 | S + M |
| 6 | S6 Player stuck "swimming" indoors (drowns) | #2712 #2306 | Mid–Late | Build | M |
| 7 | S7 Containers overfill, duplicate, or items respawn | #2495 #2494 #2047 #2691 | Early → | Build | M–L |
| 8 | S8 Battery duplication from empty tools | #2668 | Early | Adopt #2809 | S |
| 9 | S9 Player spawns outside the escape pod | #2898 | Start | Adopt #2899 | XS |
| 10 | S10 Scanner room isn't shared | #2410 #2046 #2151 #1425 | Early–Mid | Build | L |

Rank is severity × likelihood for our run. **Execution order is different, cheapest safe wins first:**

1. **Phase 0, baseline (before anything else):** S0.1 + S0.5 + S0.6 from TESTING.md. Verify the vehicle-movement
   regression #2761 is fixed on master (fix #2864 merged 2026-09-06). Set up world backups.
2. **Phase 1, adopt batch + small builds:** S9 (#2899) → S5a (#2798) → S8 (#2809) → S4 (#2902, then #2867 with fix)
   → S3 (small build) → S7 step 1 (ghost cell entries; pure server logic, L1-testable). A playthrough can start on
   this build.
3. **Phase 2:** S1 (#2881). This is a **one-way save-format change**, so back up the world first.
4. **Phase 3, builds (after S0.4, the scenario runner):** S2 → S7 → S6 → S5b → S10.
   S3 must be in before a playthrough reaches the Disable-Gun terminal. S10 is the biggest and least urgent.

Merge-order facts from a trial merge of all candidate PRs on `62ef043a` (no build):
- Everything merges cleanly except **#2881 × #2899**, which both add a constructor parameter to
  `Nitrox.Server.Subnautica/Models/GameLogic/EscapePodManager.cs`. Keep both.
- **#2867 merges cleanly but breaks #2902 at runtime** (see S4).
- Network type indices come from type names, so **the server and both players must run the same build**.

---

## S0: Test rig (prerequisite, not a bug)

See [TESTING.md](TESTING.md#build-order-spec-item-s0). Minimum before Phase 1: S0.1 (one-command dual client through the
existing Debug-only `Nitrox.Launcher.exe instantlaunch --save <save> --players A B`), S0.5 (save → load round-trip test helper) and
S0.6 (`/report` bundle). Minimum before Phase 3: S0.4 (scenario runner).

**Status (branch `fix/s0-test-rig`, merged into `coop` on 2026-11-01):**
- S0.1 `tools/dual-client.ps1`, **run on the dev PC**: both players join in about 20 s, `-WaitForJoin` prints PASS, and
  `-Stop` cleans up. It needed two launcher fixes. Upstream's `instantlaunch` with two names refused to start the second
  game unless *Allow multiple game instances* was on. And all games shared one `Player.log`, so each now gets
  `Player-<name>.log`. Our docs also had the wrong argument syntax (it's `--save … --players …`).
- S0.5 `Nitrox.Test/Helper/SaveLoadRoundTrip.cs` plus `WorldServiceSaveLoadTest` (base with chargers and a scanner room).
  The charger test passes on current code, as expected: S2's root cause is client-side resync deletion, not the save
  format. So the test guards against future format changes; it doesn't reproduce S2. Finding: `BuildEntity`'s JSON
  constructor forces `SpawnedByServer = true` on load (same pattern as `ModuleEntity`, intended for simulation locks).
- S0.6 `/report` + F4 (client bundle on each player's PC, server bundle with a save snapshot on the server's PC),
  **verified in game** from both players. No packet ring buffer yet.
- S0.4 replaced by the **dev bridge** (branch `fix/dev-bridge`, 2026-11-02): Debug-only localhost control of each game
  and the server from the shell (`tools/bridge.ps1`), a desync detector (`bridge.ps1 diff`), and scenario scripts in
  `tools/scenarios/`. S3 and S8 pass in game, and both fail with their fix removed. See
  [TESTING.md](TESTING.md#the-dev-bridge-driving-games-from-the-shell).

---

## S1: Cyclops damage, leaks and fires persist (#2220, #2230), p/0

**Symptoms:**
- The Cyclops explodes despite high health.
- Health isn't full but there are no leaks to repair.
- Minor damage suddenly spawns 4 leaks and a fire.
- Repairs aren't kept after a restart.

**Root cause** (confirmed, upstream analysis on #2220 + code):
- `CyclopsMetadata.Health` is persisted through the metadata pipeline.
- The `CyclopsDamage` packet, which carries the active damage points, fires and sub-health, is only *relayed* by its
  server processor. Nothing is stored and nothing is sent to late joiners.
- The client rebuilds the whole damage set from each packet, so drift between players gets "caught up" in bursts.

**Plan: Adopt + fix [#2881](https://github.com/SubnauticaNitrox/Nitrox/pull/2881)**
(+972/−781, 50 files, CI green, active review, no formal approval yet). What it does:
- Fires and damage points become real persisted entities: `CyclopsFireEntity`, `CyclopsDamagePointEntity`,
  `CyclopsDamagePointMetadata`.
- The `CyclopsDamage` relay path is removed.
- Extinguishing is synced and throttled.
- The intro escape-pod fire and the extinguisher FX are also synced.

Fixes when merging:
1. Resolve the `EscapePodManager` constructor conflict with #2899 (keep both new parameters).
2. Add the missing `[ProtoInclude(<next free tag>, typeof(CyclopsDamagePointMetadata))]` on `EntityMetadata`. Our saves
   are JSON with type names so it works without it, but keep the model consistent.
3. Check upstream for new commits first (`git fetch upstream pull/2881/head:pr-2881`). The author asked for a group test
   on 2026-10-28.

**Save risk:** a world saved with #2881 refuses to load on a build without it ("restore a backup" error, no silent wipe).
**Back up the world before first use.** Test with a save that already has a damaged Cyclops.

**Automated check:**
- L3 scenario `CyclopsDamagePersists`: A rams the Cyclops (or applies damage through `LiveMixin.TakeDamage` on the
  sub). B asserts the same damage-point indexes and fires.
- A repairs one point. The server restarts. Both assert the persisted set and the health.

**Acceptance (during play):** after a session with Cyclops damage, relog. Leaks, fires and health match for both
players.

**Status (branch `fix/s1-s5b-cyclops-flood`, 2026-11-02):** #2881 merged with fixes 1 and 2 (tag 92). L1 and L3 pass. See the Log.

---

## S2: Items in charger, scanner-room and upgrade-console slots vanish (#2259, #2696, #2576)

**Symptoms:**
- Batteries and power cells in chargers are gone after a rejoin.
- Modules on a Seamoth docked in a moonpool disappear after "Resync base buildings".
- Scanner-room upgrade chips vanish after a server restart.
- Scanner cameras multiply in their slots after restarts.

**How it's modelled:**
- Equipment slots (charger, reactor, upgrade console, vehicle modules) → `InstalledModuleEntity(Slot, ClassId)`, parented
  to `Equipment.owner` (`NitroxClient/GameLogic/EquipmentSlots.cs`, `Helper/EquipmentHelper.cs`), spawned by
  `Spawning/InstalledModuleEntitySpawner.cs`.
- `ItemsContainer` contents (lockers, scanner-room upgrades) → `InventoryItemEntity` (`GameLogic/ItemContainers.cs`).
- The scanner room is a `MapRoomEntity` (id + cell).
- The server deletes entities only on an `EntityDestroyed` packet.

**Root cause A: resync deletes equipment on the server** (confirmed, code + IL):
1. `BuildingResyncProcessor.ClearBaseChildren` calls `Object.Destroy` on base modules and docked vehicles.
2. In vanilla, `Pickupable.OnDestroy` → `SetInventoryItem(null)` → `Equipment.RemoveItem`.
3. `Equipment_RemoveItem_Patch` → `EquipmentSlots.BroadcastUnequip` sends **`EntityDestroyed` for every non-player
   owner**.
4. So the server permanently deletes the batteries and modules. The client still shows them because it respawns from the
   pre-delete snapshot, and the loss only appears after a rejoin or restart.

**Root cause B: echo** (path confirmed, impact *hypothesis*):
- `EntityDestroyedProcessor.DestroyPickupable` calls `pickupable.OnDestroy()` **without** a `PacketSuppressor`, so every
  other client re-sends `EntityDestroyed`.
- That can delete an item the original player has just re-created somewhere else.
- `Entities.DestroyObject` (`CleanupExistingEntities` on force-respawn) has the same pattern.
- This may explain "batteries vanish after a plain logout, without Resync", which code reading alone doesn't explain.
  **Reproduce it.**

**Root cause C: scanner-room children are saved but never spawned** (confirmed):
- `InteriorPieceEntitySpawner.RestoreMapRoom` only sets the id.
- `BuildEntitySpawner` and `BuildingResyncProcessor.OverwriteBase` call it but never spawn the `MapRoomEntity`'s
  `InventoryItemEntity` children.
- So clients never see upgrades the server still holds. Deconstructing the "empty" room then really deletes them.
- Cameras (#2576, confirmed in IL): `MapRoomCameraDocking.Start` spawns a fresh camera unless `deserialized` is set, and
  only `OnProtoDeserialize` sets it. Nitrox never proto-deserializes, so each load adds cameras without ids.

**Plan: Build:**
1. `ClearBaseChildren`: inside `using (PacketSuppressor<EntityDestroyed>.Suppress())`, first detach every equipped
   `Pickupable` under the destroyed children and docked vehicles (`SetInventoryItem(null)` while suppressed), then
   destroy.
   - Alternative: skip in `BroadcastUnequip` when `pickupable.isDestroyed && BuildingHandler.Main.Resyncing`.
   - Avoid a global `isDestroyed` guard: legitimate consumption, such as the Cyclops decoy tube, may use that path.
     *Check it.*
2. Wrap `OnDestroy`/`SetInventoryItem` in `EntityDestroyedProcessor.DestroyPickupable` and in `Entities.DestroyObject`
   in the same suppressor.
3. `RestoreMapRoom`: after setting the id, apply metadata (S10) and
   `yield return entities.SpawnBatchAsync(mapRoomEntity.ChildEntities.OfType<InventoryItemEntity>().ToList<Entity>(), true)`.
   Pass `Entities`/`EntityMetadataManager` in from `BuildEntitySpawner` and `OverwriteBase`.
4. New `MapRoomCameraDocking_Start_Patch` (Prefix): set `deserialized = true` for rooms restored from server data. Give
   freshly built rooms' cameras deterministic ids (`mapRoomId.Increment()`, as `SetupBench`/`SetupWaterPark` do).

**To check in game first:**
- Where the `StorageContainer` sits under `MapRoomFunctionality` (decides how `InventoryContainerHelper.TryGetOwnerId`
  resolves the owner).
- That `Destroy` completes before `WaitForEndOfFrame` resumes.

**Automated check:**
- L1: unit tests for the server side are limited here. Use the save → load round-trip helper on a base with charger
  children and a map room with upgrades.
- L3 `BaseEquipmentSurvivesResync`: a base with a battery charger, power-cell charger, reactor and docked Seamoth with
  modules. A presses Resync inside, then outside the base. The server log shows **no `EntityDestroyed` for item ids**.
  Both rejoin and the server restarts: everything is still there.
- L3 `UnequipEcho`: A removes a battery while B is nearby. A rejoins and still has the battery.
- L3 `MapRoomUpgradesPersist`: 2 range upgrades and exactly 2 cameras after a restart. Take a camera out → after a
  restart, 1 docked + 1 in inventory.

**Overlap:** #2811 (S5b) also edits `BuildingResyncProcessor.cs` and adds a metadata type. Expect conflicts and a race
for the next `ProtoInclude` tag.

---

## S3: Precursor gun deactivation isn't applied for the partner (#2640), p/0, story gate

**Symptoms:**
- After one player disables the gun at the Quarantine Enforcement Platform terminal, the partner's launch button in the
  rocket says the gun is still active. Their terminal keeps opening and prompting.
- If the gun is disabled before the Sunbeam sequence finishes, the partner's client keeps running the shoot-down
  goals.
- The gun's animation looks wrong ("drinking in the water") during the Sunbeam event.

**Root cause** (confirmed in IL + Nitrox code):
- `PrecursorDisableGunTerminal.OnPlayerCinematicModeEnd` calls `StoryGoalCustomEventHandler.main.DisableGun()`, which
  sets `gunDisabled = true` and triggers the `gunDeactivate` goal. The goal reaches other clients through
  `StoryGoalExecutedClientProcessor` → `StoryGoal.Execute` → `NotifyGoalComplete`.
- **`gunDisabled` is only ever written in `Awake` and `DisableGun`**, so on the partner it stays `false` until a relog.
  Late joiners are fine because `StoryGoalInitialSyncProcessor` re-runs `Awake()`.
- `gunDisabled` is read by `LaunchRocket.OnHandClick`
  (`IsRocketReady && !launchStarted && (gunDisabled || forcedRocketReady)`), by the terminal hover/area code, and by
  the Sunbeam branch of `NotifyGoalComplete`.
- The server already persists the goal in `CompletedGoals`.
- Today the Nitrox terminal patch (`PrecursorDisableGunTerminal_OnPlayerCinematicModeEnd_Patch`) syncs only `firstUse`.

**Plan: Build (small, client only):**
1. New `NitroxPatcher/Patches/Dynamic/StoryGoalCustomEventHandler_NotifyGoalComplete_Patch.cs`: a **Prefix** that sets
   `__instance.gunDisabled = true` when `key` equals `__instance.gunDeactivate.key` (ordinal ignore-case).
   - Use the field, not a literal. The literal is probably `"Goal_Disable_Gun"`, but the field value is *[asset]*.
   - A Prefix makes the same call's Sunbeam branch take the cancel path, as in vanilla.
   - It covers remote goals, local schedules, `PlaySunbeamEvent` and the `goal` console command, and it's safe to run
     twice.
2. Optional: if the partner's terminal is open at that moment, close it (`SetOpen(false)`).
3. Gun animation: *hypothesis only*. `PrecursorGunAim.LateUpdate` exits early when `target` is null, and `target` is
   only set in `PrecursorGunTarget.Start` if `PrecursorGunStoryEvents.main.gunAim` exists then. Load order, or Nitrox
   re-calling `PrecursorGunStoryEvents.Start()` from `StoryGoalInitialSyncProcessor`, may break it. Instrument `target`,
   the animator parameters and `VFXSunbeam.main` on both clients (`/sunbeam storystart|countdown|gunaim`) before
   changing anything.

**Automated check:**
- L1 can't cover this: `StoryGoalCustomEventHandler` is a MonoBehaviour that MSTest can't construct. If you extract the
  key comparison into a static helper, unit-test only that.
- **The real check is L3.** Scenario `GunDisableSyncs`: give A the cure goal (console `cureplayergoaltrigger` triggers `Infection_Progress5`,
  which is assumed to be the terminal's cure goal, *[asset]*). Teleport both near the terminal and A uses it. Assert
  `StoryGoalCustomEventHandler.main.gunDisabled` on B, and that `LaunchRocket.OnHandClick` on B passes the gun check.
  Don't use `forcerocketready`, which bypasses the check.
  **Built:** `tools/scenarios/S3-GunDisableSyncs.ps1`. Read at runtime: the terminal's `onPlayerCuredGoal.key` is
  `Infection_Progress5` and `gunDeactivate.key` is `Goal_Disable_Gun` (both `GoalType.Story`). No rocket in a fresh
  world, so the `LaunchRocket` click isn't exercised; the scenario checks the `gunDisabled` field it reads.

**Related:**
- [#2649](https://github.com/SubnauticaNitrox/Nitrox/pull/2649) (precursor proximity triggers react to remote players)
  complements this.
- **#2652 "Wait for all players"** (Tier 1) builds on S3.

---

## S4: Creatures don't die for the other player (#2510, #1701), p/1

**Symptoms:**
- A Reaper or Ghost killed by one player freezes for the other, then swims away alive.
- Things destroyed with a knife or Prawn drill (e.g. mushrooms) disappear only for the player who did it.

**Root cause:** only the player who *simulates* a creature runs its death path; a kill by the other player isn't
replayed. Separately, `LiveMixin` kills of non-creature entities don't send `EntityDestroyed` in most cases.

**Plan: Adopt [#2902](https://github.com/SubnauticaNitrox/Nitrox/pull/2902), then [#2867](https://github.com/SubnauticaNitrox/Nitrox/pull/2867) with a fix.**
- **#2902** (+171/−24):
  - A non-simulating player may run `OnKill`, which broadcasts the death. Receivers replay `OnKill` with packets and
    sounds suppressed.
  - The corpse position is sent in world space, and the death goes to all players, not only those who can see it.
  - Respawners are spawned only by the simulator.
  - No new packets or save changes.
  - Known gaps (from the author): no respawner when nobody holds the creature's lock; heat (cooked) kills.
  - It has no review yet and CI has never run on it.
- **#2867** (+11/−8) sends `EntityDestroyed` whenever `broadcastKillOnDeath || passDamageDataOnDeath`. Unfixed, it
  breaks things two ways:
  - It races #2902 for creatures: `EntityDestroyed` can arrive first and delete the partner's copy, so no corpse and no
    cooked fish.
  - It may fire for the **player's own** LiveMixin and delete the `PlayerEntity` and its inventory on the server.
    *Hypothesis*: vanilla needs `broadcastKillOnDeath` on the player, but this wasn't confirmed in game.
  - **Fix:** in `LiveMixin_Kill_Patch`, return early when the object has a `CreatureDeath` component (creature deaths go
    through #2902) or a `Player` component.
- **Later, not now:** [#2903](https://github.com/SubnauticaNitrox/Nitrox/pull/2903) (damage from both players adds up)
  contains #2902. It is large and unreviewed, and its tests have never run. Skip #2842; it's superseded and conflicts.

**Automated check:**
- L3 scenario `CreatureDeathSyncs`: spawn a peeper/stalker near both players and kill it from the non-simulating
  client. Both should show a corpse, and the server's `query <id>` should show it dead or removed.
- Second case: knife a pickupable mushroom; it should be gone on both.
- **Regression check for the fix:** A dies (`/kill` or damage). `query <playerId>` still returns the player, and B still
  sees A after respawn.

**Acceptance:** during the playthrough, kill a big creature together. It dies for both players.

---

## S5: Base leaks and flooding desync (#2769, #2808)

**Symptoms:**
- After a restart, leaks repaired by one player are still leaking for the other, so both have to weld everything.
- A reconnecting player sees the flood level restart from zero.

**S5a, leaks: Adopt [#2798](https://github.com/SubnauticaNitrox/Nitrox/pull/2798)** (+17/−4, approved twice):
- `BaseLeakManager.EnsureLeak` records the leak id before looking up the cell. Before, a missing cell created a new
  orphan leak entity every tick.
- Repairs always clear their tracking entry.
- No save-format change.

**S5b, flood level: Finish [#2811](https://github.com/SubnauticaNitrox/Nitrox/pull/2811)**. Don't merge it as is:
- The rework the author described on 2026-09-03 was never pushed.
- As pushed, it reads every base's flood grid every frame (`BaseFloodSim.Update`, before the throttle), every client
  sends the full grid every 2 s, "more flooding wins" decides conflicts, and there is no lock or resync guard.
- It adds `BaseFloodMetadata` to base entities, which is a one-way save change.

Implement the described design ourselves (or adopt it if the author pushes it):
- Only the simulating/owning player sends, and only on change, throttled to 2 s, skipping the base's `Sum` check.
- Seed the flood state on rejoin from metadata.
- Put the extractor on `Base`.
- Add a `ProtoInclude`.

**Automated check:**
- L1: a unit test for the server `BaseLeakManager` repair path.
- L3 `BaseLeaksPersist`: damage the base hull, A repairs, restart, B sees no leak.
- L3 `BaseFloodResync`: let the base flood partly, B relogs, B's water level equals A's.

**Status (branch `fix/s1-s5b-cyclops-flood`, 2026-11-02):** S5b built (not #2811's code), tag 93. The S5a and S5b L3 scenarios pass. See the Log.

---

## S6: Player stuck "swimming" inside the Cyclops or precursor bases (#2712, #2306)

**Symptoms** (#2712, verified):
- Sometimes after entering the Cyclops the player keeps swimming and using oxygen, and can drown.
- The same happens inside precursor bases (e.g. past the Lost River waterfall entrance), and after rejoining inside an
  alien base (#2306).
- A server restart clears it, but it comes back at random.

**Vanilla mechanics** (IL):
- Swimming and oxygen loss inside a sub happen only when `Player.currentSub == null`: `IsUnderwater`, the walk/swim
  switch and `CanBreathe` all key off it.
- Hatch entry ends in `SetCurrentSub(sub, false)`. That call **silently does nothing** if the sub changed within the
  last 2 physics ticks (`CanChangeSub()`).
- The sub is cleared by `EnterExitHelper.Exit`, by `ValidateCurrentSub` (every 1 s, if the player is more than 35 m from
  the sub's origin) and by `OnPlayerPositionCheat`.
- Inside precursor bases the state is `precursorOutOfWater`. Only triggers set it, `OnPlayerPositionCheat` clears it,
  `waterPlaneOverride` takes priority, and nothing ever recomputes it.

**Nitrox defects:**
- *Confirmed:* `NitroxPatcher/Patches/Dynamic/Player_SetCurrentSub_Patch.cs` broadcasts the sub change in a **Prefix**,
  before vanilla's `CanChangeSub` check. The server and the partner record "inside" even when the local game rejected
  it, and nothing re-checks, which hides the mismatch.
- *Confirmed:* `NitroxClient/MonoBehaviours/Cyclops/NitroxCyclops.cs` makes the local player a child of the Cyclops
  (vanilla never does), and `CyclopsMotor.cs` turns off player collisions.
  - Precursor and room triggers (`PrecursorDoorMotorModeSetter`, `PrecursorMoonPoolTrigger`, `RoomLinks`,
    `DisableWaterRenderingVolume`, `PlayerSoundTrigger`) search the parent tree around whatever touched them.
  - So while the local player is inside a Cyclops, the hull, docked vehicles or a remote body (which carries a rigidbody
    and a trigger capsule, `RemotePlayer.cs`) can resolve to the *local* player and flip `precursorOutOfWater` /
    `waterPlaneOverride`.
  - Remote bodies can't change `currentSub` itself: base triggers need a `Player` component directly on the collider,
    and remote bodies have none.
- *Confirmed (#2306 / precursor):*
  - `PlayerPositionInitialSyncProcessor` restores the saved `InPrecursor` *before* the world finishes loading, so
    triggers firing during load can overwrite it.
  - `PrecursorTracker` takes its starting value after loading and never sends it, so the server keeps a stale value
    for the next join.
  - `PlayerTeleportedProcessor` calls `OnPlayerPositionCheat`, which forces the flag to false, and never applies the
    destination's value. Warping to a partner inside a precursor base leaves you swimming.

**Root cause:** *hypothesis, ranked.*
1. The local `currentSub` stays or becomes null while the player is physically inside: a rejected hatch entry or a
   later exit, never corrected.
2. Triggers reached through the Cyclops parenting flip the water flags.
3. Precursor-flag restore and teleport ordering.

Needs an instrumented two-player repro (step 3 of the plan helps).

**Plan: Build:**
1. `Player_SetCurrentSub_Patch`: record the old sub in a Prefix (`__state`) and broadcast in a **Postfix**, only if
   `currentSub` actually changed.
2. After `EnterExitHelper.Enter` (Postfix patch): if the target is a Cyclops hatch and `currentSub` isn't that sub, log
   a warning and call `SetCurrentSub(sub, true)`.
3. Log Cyclops exits (`SubRoot_OnPlayerExited_Patch`) at debug level with a stack trace, to catch the real trigger in the
   wild. The `/report` bundle (S0.6) will capture it.
4. In the precursor/room triggers listed above, skip the player branch when the collider belongs to a Cyclops subtree
   and isn't the local player's own collider. Keep the vehicle branch unchanged (Exosuit inside a Cyclops).
5. Precursor flag:
   - Restore `InPrecursor` *after* the world has loaded.
   - Send the current value once when `PrecursorTracker` starts.
   - Add `InPrecursor` to the `PlayerTeleported` packet (server `Models/Player.cs`, `WarpCommand`, `BackCommand`) and
     apply it after `OnPlayerPositionCheat`.
6. Drive-by bug, confirmed: `CyclopsPawn.cs` sets `Handle.layer = 1 << PLAYER_LAYER`, but `PLAYER_LAYER` is already
   shifted, so the pawn ends up on the wrong layer.

**Automated check:**
- L3 `InteriorStateHolds`: A enters and leaves the Cyclops by hatch about 50 times while B is inside, the sub is moving,
  and vehicles dock and undock.
  - A's `currentSub` matches the server's `SubRootId` (`query`).
  - Oxygen never drops inside.
- L3 `PrecursorRejoin`: A relogs inside an alien-base antechamber, and B warps to A inside a precursor base. Both are
  walking, not swimming.

**Risks:** forcing the sub could fire in the middle of a cinematic, so skip it while `cinematicModeActive`.

---

## S7: Containers overfill, duplicate, or items keep respawning (#2495, #2494, #2047, #2691)

**Symptoms:**
- #2495 (verified): a Water Filtration Machine overflows (4 salt + 4 water instead of 2 + 2), and the overflow comes
  back after looting.
- #2494: storages overfill with message spam, and items sometimes disappear (e.g. big eggs in a Bioreactor).
- #2047 (2023, to verify): a wall locker duplicated food every time it was opened.
- #2691 (verified): resources float inside the base, respawn for one player when they approach, and differ between
  players. They appear to be items removed from the non-host player's inventory, appearing where that player stood.

**#2495, filtration overflow: root cause confirmed.**
- Nitrox has **no code for the filtration machine**, so every client near the base runs it.
- Each time it produces an item (IL), it:
  1. checks for room only in its *own* copy of the container,
  2. calls `Pickupable.Pickup`, so Nitrox's `Pickupable_Pickup_Patch` sends a `PickupItem` "by the local player",
  3. adds the item, which sends `EntityReparented(item, machine)` (`ItemContainers.cs`).
- Other clients apply the add with `UnsafeAdd`, which has **no capacity check** (`EntityReparentedProcessor`,
  `InventoryItemEntitySpawner`, `ItemContainers`).
- Two players therefore get 4 + 4, and the extras are persisted.

**#2494, bioreactor overfill / vanishing: root cause confirmed.**
- Each client's `BaseBioReactor.ProducePower` burns (removes and destroys) items on its own (IL), and Nitrox sends
  nothing.
- So items vanish on one client and come back on reload, while adds from the other client still overfill through
  `UnsafeAdd`.
- The source of the text spam wasn't found.

**#2047, storage duplication: likely stale.**
- The 2023 report is against 1.7.x, and that code path was replaced in `e95a78b2` (1.8.0.0).
- The "storage version" text isn't Nitrox's. It probably comes from vanilla `StorageContainer.CleanUpDuplicatedStorage`
  (*verify*).
- A related live risk remains: `UnsafeAdd` (IL) never removes the item from its previous container. On the receiving
  client, the source container (e.g. the partner's inventory copy) keeps a stale reference.

**#2691, floating resources respawn: server side confirmed.**
- The server's per-cell list of world items is in-memory and rebuilt only at startup (`WorldService`).
- Only `PickupItemPacketProcessor` removes items from it, and only while the item is still recorded as a world item.
- `EntitySpawnedByClientProcessor` (`AddOrUpdate` + `TrackEntityInTheWorld`) and `EntityReparentedProcessor` replace
  or move an item's record **without removing its old cell entry**.
- When a player loads a cell, `CellVisibilityChangedProcessor` re-sends everything on the list and forces it to spawn.
- So a world item that later moves into a container leaves a **ghost entry** in its old cell. That explains every
  symptom:
  - it respawns per player on approach,
  - pickups don't stick,
  - the count grows over a session,
  - a restart clears it.
- *Hypothesis:* the client action that first puts the item into the world where the non-host player stood. A suspect
  is `EntityReparentedProcessor` silently ignoring a move while the item is still spawning.
- #2665 is unrelated: it only touched the prefab cache.

**Plan: Build:**
1. **Ghost cell entries (#2691):**
   - Remove the old cell entry in `EntitySpawnedByClientProcessor`, and in `EntityReparentedProcessor` when an item is
     moved under a parent.
   - Make `PickupItemPacketProcessor` remove the entry by id, whatever the item is currently recorded as.
   - In `WorldEntityManager.GetEntities` (cell list), skip and log entries that no longer match the item's current
     record. This doubles as a diagnostic.
   - Add Nitrox.Test cases for these processors. **This part is pure server logic, so L1 covers it.**
2. **One producer per machine (#2495, #2494):**
   - Only the client holding a simulation lock on the machine or its base produces or burns (pattern:
     `WaterParkCreature_ManagedUpdate_Patch` for water parks).
   - Design how that lock is granted for base modules, including handover when the holder leaves.
   - Suppress `PickupItem` while the machine produces.
   - The bioreactor sends `EntityDestroyed` for each burned item (as `ItemsContainer_DestroyItem_Patch` does for
     crafting).
3. **Remote adds:**
   - Before `UnsafeAdd`, remove the item from its old container (`pickupable.inventoryItem?.container?.RemoveItem(pickupable, true)`).
   - Log a warning when the target container has no room.
4. **#2047:** mark it stale, unless a scenario reproduces it on current master.
5. **Found by L2 (2026-11-02), open:** the step-1 cleanup doesn't survive a restart for items whose record is still a
   `WorldEntity` (moved from the world straight into a container). On load, `WorldService` calls
   `WorldEntityManager.SetWorldEntities` with every non-global-root `WorldEntity`, parented ones included, so the item
   is back in its old cell and re-sent there. Repro: `GhostCellEntryTest.ItemReparentedIntoContainer_IsNotResentAsWorldEntityAfterRestart`
   (`[Ignore]`d until fixed). Items picked up first (`InventoryItemEntity`) aren't affected. Check whether other
   parented world entities (e.g. nested `PrefabPlaceholderEntity`, see Log "Findings (diff)" (2)) get cell entries
   after every restart too.

**Automated check:**
- L1: processor tests for ghost-entry removal (spawn into world → reparent into locker → the cell list no longer
  contains it).
- L3 `FiltrationNoOverflow`: 1–4 filtration machines with both players nearby. At most 2 + 2 each, on both clients, on
  the server, and after relog.
- L3 `BioreactorSync`: both clients add big eggs. Counts never exceed capacity and burned items are gone everywhere.
- L3 `NoFloatingLeftovers`: B moves a full inventory into a locker, and A leaves and returns. Nothing floats, and the
  server log shows no ghost warnings.

**Risks:**
- If the lock holder leaves without handing over the lock, production stops.
- `RemoveItem` on the old container notifies its listeners (planters, trash cans).
- Removing cell entries at the wrong moment could hide real world items.

---

## S8: Battery duplication from empty tools (#2668), p/0

**Symptom:** drop a battery-powered tool whose battery was removed, pick it up again, and it has a new full battery.
It repeats after relog.

**Root cause** (confirmed by upstream dev note + PR): when converting a picked-up tool to an `InventoryItemEntity`
(`NitroxClient` `Items.ConvertToInventoryItemEntity`), a tool with an empty battery slot is still serialized with the
default installed battery.

**Plan: Adopt [#2809](https://github.com/SubnauticaNitrox/Nitrox/pull/2809)** (+134/−13, approved by a maintainer, tested
across players by two people):
- Existing tools report only the battery actually in their slot.
- Dropping a tool records its battery.
- A "pending battery" table covers a battery from another player that is still spawning.

Follow-ups we can do ourselves:
1. Clear the static pending table on disconnect.
2. Trim the very long comments.
3. *Hypothesis to check:* `GetPrefabChildren` may also list the installed battery as a `PrefabChildEntity`, which would
   duplicate the id under the tool after drop/pickup. Inspect with `query <toolId>`.

**Automated check:** L3 `EmptyToolNoDupe`: A crafts a flashlight, removes the battery, drops the tool, B picks it up.
The flashlight has no battery and the battery count is unchanged. Also run `query` on the tool entity.
**Built:** `tools/scenarios/S8-EmptyToolNoDupe.ps1` (spawns the flashlight with `item flashlight` instead of crafting).

---

## S9: Player spawns outside the escape pod after initial sync (#2898)

**Symptom:** on a new world or a player's first join, the player can appear outside the escape pod. The log shows
`Game object required from id …` from `PlayerPositionInitialSyncProcessor`.

**Root cause:** a newly created escape pod isn't registered in `WorldEntityManager`'s cache, so the player's parent
(the pod) can't be resolved during initial sync.

**Plan: Adopt [#2899](https://github.com/SubnauticaNitrox/Nitrox/pull/2899)** (+3/−2, CI green, a draft only because
the description is empty):
- `EscapePodManager` registers new pods with `worldEntityManager.AddOrUpdateGlobalRootEntity`, which includes the old
  registry add.
- It only affects pods created during the current server run.
- It conflicts with #2881 on one constructor line (see S1).

**Automated check:** L3 `FreshWorldSpawnsInPod`: `instantlaunch` a brand-new save with A and B. Both are inside a pod
(`Player.main.currentEscapePod != null`) and there's no `OptionalEmptyException` in either log.

---

## S10: Scanner room isn't shared (#2410, #2046, #2151, #1425), p/1

**Symptoms:**
- Each player's scanner room highlights a different resource.
- Pings remain for outcrops that are already broken, or for resources that aren't there.
- Cameras aren't synced.
- Upgrades disappear on relog (that last one is S2).

**Synced today:** only that the room exists (id, cell) and live upgrade add/remove. There's a warning about it at
`NitroxClient/GameLogic/Bases/BuildingPostSpawner.cs`.

**Not synced** (confirmed):
- The selected `typeToScan` (set by `uGUI_MapRoomScanner.OnStartScan`/`OnCancelScan` → `StartScanning`).
- Cameras and their docked state.
- Camera piloting.
- Removals from `ResourceTracker`, which has no `OnDestroy` (IL). It unregisters only through `OnPickedUp`,
  `OnBreakResource` and similar callbacks, and those never run for remote destroys. The server also sends
  `EntityDestroyed` only to players who can see the cell (`EntityDestroyedPacketProcessor`).

**Plan: Build (minimum viable, in this order):**
1. **Upgrades on load:** S2 step 3. Do it first.
2. **Shared scan target:**
   - New `MapRoomMetadata { NitroxTechType TypeToScan; NitroxId[] DockedCameraIds }` with a new `ProtoInclude` tag.
   - `MapRoomMetadataExtractor : EntityMetadataExtractor<MapRoomFunctionality, MapRoomMetadata>`.
   - A processor that calls `StartScanning(type)` under `PacketSuppressor<EntityMetadataUpdate>`.
   - Postfixes on `uGUI_MapRoomScanner.OnStartScan`/`OnCancelScan` that call `Entities.EntityMetadataChanged`. Hook the
     UI, not `StartScanning`, because `Start` also calls it and that would echo.
   - No server change: the generic `EntityMetadataUpdateProcessor` stores it.
3. **Cameras:** on restore, spawn the docked cameras from metadata (with S2 step 4). `DockCamera`/`UndockCamera`
   postfixes update the metadata. Undocked cameras use the normal pickup flow.
4. **Ghost pings (#2046/#2151):**
   - In the client `EntityDestroyedProcessor`, call `ResourceTracker.Unregister()` on the object. If it isn't loaded,
     remove the id from every bucket in `ResourceTrackerDatabase.resources` and raise `onResourceRemoved`.
   - On the server, broadcast destroys of resource-tracked entities to **all** players, not only those who can see the
     cell.
   - *Hypothesis* for the single-player variant: `ResourceTracker` captures `prefabIdentifier.Id` in `Start` before
     Nitrox assigns its id.
5. **Out of scope:** camera piloting (#1425). `MapRoomCamera` isn't a `Vehicle`, so it would need a simulation lock and
   movement replication.

**Automated check:**
- L3 `MapRoomScanShared`: A selects titanium. B's room scans titanium and lists the same blips, and this holds after a
  rejoin and a restart.
- L3 `MapRoomPingsClear`: B breaks an outcrop that A's scanner shows, and it disappears from A's scanner.

**Acceptance:** during the playthrough, one player sets the scanner and the other uses it to find resources.

---

## S11: Players rejoin at an unrelated position after a restart: no game bug, a test-rig artifact

**Reported by:** the S1 and S7 workers, independently. After restarts in dual-client runs, players rejoined at random
near-surface spots (Alice at (-146, -3, 56) and (40, -4, -48), Bob at (132, -5, -88)).

**Findings** (2026-11-02):
- **The server's position path is correct.** `PlayerMovement` → `Player.Position` → `PersistedPlayerData.SpawnPosition`
  → `InitialPlayerSync.PlayerSpawnData`.
  - L2 `PlayerPositionPersistenceTest`: the last movement survives a save and restart and comes back on rejoin.
  - L3 `S11-QuitKeepsPosition.ps1`: quitting to the main menu doesn't change the stored position or sub. Tested both
    swimming and inside the escape pod. **PASS, 6 checks.**
- **Cause: culture-dependent number formatting in scenario scripts.**
  - On this PC (German culture), PowerShell's `-f` writes -53.74 as `-53,74`.
  - The dev bridge parsed that with `InvariantCulture`'s default styles, which allow thousands separators, so the player
    was teleported to -53 *quadrillion*.
  - The game then put the player at a random safe spot near the surface.
  - Reproduced by sampling the client and server position after such a teleport: the game jumped from
    (-5.4e16, -6.1e16, 378783) to (-51.0, 3.2, 43.8), and the server faithfully stored that.
  - The reported positions match that pattern (near the surface, all different).

**Fix (tooling):**
- The bridge parses numbers with `NumberStyles.Float` (`BridgeCommands.ParseFloat`, `BridgeReflection.ConvertArg`), so
  a comma-formatted number now fails loudly.
- `ScenarioHelpers.ps1` switches scripts to invariant number formatting.
- Earlier scenario results that involved computed teleports (some S2 steps use raw `-f`) may have run at the wrong
  spot. Re-check them in the next suite run.

## S12: A partner offline at cure time stays infected forever

**Cause** (confirmed in game code and an L2 test):
- The game changes infection only through story goals (`game:StoryGoalCustomEventHandler.cs:155-174`):
  - `Infection_Progress1`–`4` raise an existing infection to 0.25 / 0.5 / 0.75 / 1 (`InfectedMixin.IncreaseInfectedAmount`
    only raises a value above 0);
  - `Infection_Progress5`, the cure, removes it.
- Goals run only on the games that are online. A partner who was offline keeps their stale stats, which
  `PlayerInitialSyncProcessor.cs:118` restores on rejoin.
- Late joiners get completed goals without their listeners, so nothing ever cures them. The same gap applies to
  progress steps 1–4 missed while offline.

**Fix:**
- New `Nitrox.Server.Subnautica/Models/GameLogic/InfectionStory.cs` applies the game's own rules to the stored infection,
  based on the world's completed goals.
- `JoiningManager.SendInitialSyncAsync` runs it before building the join packet, and logs when it changes something.

**Verified:**
- L1 `InfectionStoryTest` (6 tests).
- L2 `OfflineInfectionTest`: Bob is infected (1.0) and offline, Alice completes `Infection_Progress5`, restart, Bob
  rejoins with infection 0. **Negative control:** without the `JoiningManager` change, the test fails with 1.0.

**Not verified in game:** the cure cinematic itself. The game-side cure for both players at once is planned in
COOP_FEATURES_SPEC G3 (with the solo lock, the cure won't happen while one player is offline anyway).

## S14: Aggressive creatures ignore players

**Report** (the players, upstream #2781): "AI fish that should be aggressive ignore players."

**Confirmed symptoms** (L3 `S14-CreatureAggro.ps1`, game 83031, Safe Shallows). A→B means Alice's game simulates the
creature and Bob is next to it, Alice 30–50 m away and invisible to creatures. Damage is what the victim's own game
applied.

| Case | Before (coop `f0ddf7bf`) | After |
|---|---|---|
| Stalker A→A (vanilla control) | bitten, 20 | bitten, 20 |
| Stalker A→B, B→A | chases the victim (target = victim, aggression 1), **0 damage**, also when the contact is forced | bitten, 20 |
| Stalker, both players next to it | chases Bob's body, bites **Alice** (2×) who is in the way, Bob 0 | Bob bitten, 20 |
| Biter / Sandshark / Bone shark A→B | targeted, **0 damage** (forced contact too) | bitten, 7 / 30 / 30 |
| Crash fish A→B | static Bob 3 m away **not targeted**; explosion next to him: **0 damage** | targeted, explosion 24–45 |
| Reaper A→B (aggression-synced leviathan) | bitten by his own game (80, and once killed) | unchanged: 80 |
| Bob's game holds the Stalker aggressive too | Bob's own game bites him | his own game doesn't; the simulator's bite arrives once |
| "Far": Alice spawns it next to Bob, leaves 400 m | lock moves to Bob, his game runs the AI, bitten | same |

**Root causes:**
- *Confirmed (IL + game):* only the simulating game runs a creature's AI (`Creature_ChooseBestAction_Patch`, and
  `AggressiveWhenSeeTarget_ScanForAggressionTarget_Patch` scans only with the lock). There, the other player's body is a
  valid target (it has an `EcoTarget` of type `Shark`, the player's own type *[asset, read in game]*) and gets chased,
  but `MeleeAttack.CanDealDamageTo` only bites a player that has a `Player` component, which a body hasn't. A body is
  invincible anyway.
- *Confirmed:* the other game never bites its own player: its copy of the creature has no aggression, because target
  and aggression are synced only for `AI.syncedCreatureWhitelist` (Reaper, Sea dragon, Sea treader, Ghost). For those,
  the bitten player's own game bites (this is why leviathans did hurt the partner).
- *Confirmed:* a crash fish explodes only in its simulator's game; `DamageSystem.RadiusDamage` passes no dealer and can't
  hurt a body. Its 4 m `AggressiveWhenSeePlayer` scan only looks at `Player.main`, and its long-range scan needs a
  target moving faster than 0.2 m/s *[asset]*, so a partner floating still next to it was never targeted.
- *Not a cause:* simulation following cells. When the simulator leaves, the lock moves to the player who still sees the
  cell and that game attacks normally.
- *Hypothesis for "passive on both sides":* with two players together, each creature is simulated by whoever loaded its
  cell first, so each player is safe from roughly half the creatures around them, and a creature chasing the partner's
  body often ends up idle.

**Fix** (branch `fix/s14-creature-aggro`), for creatures outside `syncedCreatureWhitelist` (`AI.AttacksPlayersFromSimulator`):
- `MeleeAttack_CanDealDamageTo_Patch` (new Prefix): the simulating game bites other players' bodies under the same
  rules as its own player (`canBitePlayer`, `RemotePlayer.CanBeAttacked`, vanilla's line-of-sight raycast). Games that
  don't simulate the creature don't bite their own player with it.
- `LiveMixin_TakeDamage_Patch`: damage that a simulated creature deals to a body is sent as the new
  `CreatureAttackedPlayer` (creature id, target session, raw damage, damage type). The hurt player's game applies it to
  the real player (`CreatureAttackedPlayerProcessor`), so its own reductions apply, and skips it inside a base or sub,
  or if it already saw the creature die.
- Server `CreatureAttackedPlayerProcessor`: relays it only to the target, and only from the creature's lock holder.
- `Crash_Detonate_Patch`: the exploding crash fish sends the same falloff damage to players in its radius.
- `AggressiveWhenSeePlayer_GetAggressionTarget_Patch` / `_OnMeleeAttack_Patch`: the nearest valid player, local or
  remote, is preferred, and biting a body starts the same "leave players alone" interval.
- Guards against new exposure:
  - No bite on a body that sits in a vehicle or the Cyclops chair, and the hurt player's game skips a bite while it is
    in a vehicle (the creature bites the vehicle; reasoning, not tested in game).
  - `Creature_IsFriendlyTo_Patch`: a creature friendly to the player (born in an alien containment) spares other
    players' bodies too. Vanilla only spares `Player.main` and vehicles, which before S14 meant a harmless chase.
- Leviathans keep their path. No save change. The protocol gains one packet, so all three must run the same build (as
  always).

**Verification:**
- L2 `CreatureAttackRoutingTest` (2 tests): the bite reaches only the bitten player, a non-simulator's bite and a bite
  on oneself are dropped, and a bite sent after the lock moved is dropped. Both fail with the lock check removed. The
  first test assumes the server handles one sender's packets in order: if it ever flakes, check that assumption first.
- L3 `S14-CreatureAggro.ps1`: **PASS, 65 checks** (one run, fixed build). Each case first waits up to 30 s for a natural
  bite. Wild AI is fickle (small fish, shinies, a teleported player getting no new trigger contact), so if none comes,
  the simulating game moves its creature next to the victim and runs the creature's own `MeleeAttack.OnTouch`
  (`Crash.Detonate`), as the mouth trigger would. Damage from anything but the case's creature (starvation, wild fauna)
  isn't counted. In the passing run, 9 of 11 hits were natural.
- The "Before" column comes from three pre-fix runs: two earlier, natural-only versions of the scenario (40 s windows)
  and the negative control below. A Reaper probe showed Bob's own game biting him.
- **Negative control** (client patches reverted, 6 cases): the vanilla control passes; A→B, B→A, Biter and Crash fish
  get no damage even with the forced contact; and Bob's own game bites him in the double-bite case. 7 checks fail.

**Not verified:** Crabsnake, Ampeel, Warper, Mesmer, Lava lizard, River prowler, Crabsquid and Bleeder in game; network
latency; a player dying from a relayed bite; vehicles (their path is unchanged).

**Risks (fear factor and balance):**
- The partner is now exactly as exposed as the simulating player, which is vanilla danger for both players, not double.
  - Each creature still has one AI, bites once per contact, and keeps its bite interval and its pause after an attack
    (`AttackLastTarget.pauseInterval`).
  - It picks the nearest target, as in single player.
  - Damage numbers are solo numbers (Stalker 20, Biter 7, Sandshark and Bone shark 30, Reaper 80), reduced by the
    victim's own suit and difficulty.
- The bite is decided where the creature is simulated, using the victim's body position there (a few hundred ms old).
  A partner may be bitten while their view shows the creature about a metre away, or be hit by a bite they outran on
  their screen.
- Lost for the partner: feeding a held fish to an attacking Stalker (they get bitten instead), and the Crabsnake's
  grab cinematic and its extra cinematic damage (they get the plain bite).
- A creature befriended by one player (`CreatureFriend`, e.g. a fed Stalker) still treats the other player as prey,
  now with bites, as it would treat a stranger in the game.
- The Ampeel's electric touch already hurt the partner in their own game (it ignores aggression); now its bite does
  too. That is parity with the game, not double damage, but it's reasoning only.
- A creature that nobody simulates (e.g. the Bleeder, missing from `SimulationWhitelist.MovementWhitelist`, so it has
  no AI at all) can no longer bite a player whose own game made it aggressive by hurting it.

**Follow-ups (not done):**
- Bleeder: add it to `MovementWhitelist`, and check `AttachAndSuck`, which probably targets only `Player.main`.
- `SeaDragonAttackTargetProcessor` sets `timeLastBite = Time.time` right before replaying `OnTouch`, so `CanBite`
  rejects the replayed non-lethal bite. *Hypothesis*, not tested.

## S13: Cyclops movement and sync glitches

**Report** (players, unconfirmed): "Cyclops movement / syncing is strange and can cause glitches."

**How it was measured** (2026-11-02, branch `fix/s13-cyclops-movement`):
- Two new Debug-only bridge commands:
  - `record start|stop` samples an object every frame in `LateUpdate`: wall clock, pose, pose relative to the sub,
    rigidbody velocity, the replicator's `MaxAllowedLatency` and buffer, the pawn handle, and state changes (lock,
    replicator, kinematic, player mode, cinematic).
  - `input move x y z` holds the game's movement input, so a pilot drives through `SubControl.Update` (power, RPM,
    driven-vehicle packets) and a passenger walks through the Cyclops motor.
- Both games run on one PC, so their tracks share a clock. `tools/scenarios/S13Helpers.ps1` (C# via `Add-Type`) compares
  them:
  - **lag**: the time shift that best aligns the partner's track with the simulating game's;
  - **residual**: what's left after that shift;
  - stalled, jumping and backward frames.
- Setup: Bob spawns the Cyclops (he simulates it), and Alice pilots it. This is the case S6 never ran.
- Baseline: `coop` + tooling (runs 1 and 2, two probes). After the fixes: runs 4 and 5. Both games ran at 120 fps.

**Confirmed and fixed:**

| Glitch | Before | After | Root cause (confirmed) |
|---|---|---|---|
| **#2379:** the pilot leaves the helm with a key held. The partner sees her walk off, then snap back | Pawn walked 2.76 m on Bob's screen while Alice moved 0.5 m. Snap back 2.6 m, view error max 2.66 m | View error max 0.17 m | `Player.ExitPilotingMode` starts the release cinematic, which disables the controller. The Nitrox postfix then ran `ToggleCyclopsMotor`, which always re-enabled it. So the pawn walked during the animation, and `PlayerMovementBroadcaster` sends the pawn. The same applied to hatch and docking cinematics |
| A single long frame on the watching game (cells loading) keeps the partner's view of the sub further behind for 10+ s, then the sub skips forward | Forced 300 ms frame: `MaxAllowedLatency` 0.077 → 0.309 s, still there 15 s later. Natural 100–260 ms frames while driving: 0.346 s → lag 498 ms, 3.6–4.2 m behind at 7.2 m/s. 20 slow-motion frames after the bump. Lowering it later skipped the sub 0.98 m in 3 frames | 309 ms frame: 0.073 s stays, lag 210 ms, no stalls, residual 8–10 mm | `MovementReplicator.AddSnapshot` counted the time a packet waited for the next frame as network latency |
| **Helm hand-over while moving:** the sub stops dead for the new pilot and snaps back for the old one | At 5.4 m/s: new owner 5.38 → 0.05 m/s. Old owner coasted on stale physics for 0.5 s, then a 1.9 m backward snap, then 0.1–0.3 m per-frame jitter for about 1 s (leftover velocity against the replicator) | At 7.0 m/s: new owner 7.03 → 6.33 m/s. Largest backward step for the old owner 0.044 m | The replicated body is moved by its transform, so it has no velocity, and it lags by the replication delay. The new owner continued from that pose at rest |
| **A passenger who leaves the game inside a Cyclops that the partner then drives on** comes back in the water where the sub was | L2: z 68 instead of 268 (sub moved 200 m) | L2 passes, also after a restart. In game: Bob quit, Alice drove the sub 92 m on, restart: Bob is back inside, at the spot where he quit (23.6 m from the origin, at the helm), currentSub = the Cyclops, not underwater | The join payload used `Player.Position`, which only the player's own packets update. Inside a sub, the player entity is a child of the sub entity and follows it |

**Measured and fine (no change):**
- **Replication itself is smooth.** At 3–7 m/s, straight and turning, the partner's track matches the pilot's with
  5–22 mm RMS residual, 0 backward frames and 0–1 stalled frames per 600–1000 frames. The large steps all lined up with
  the watching game's own long frames.
- **The replicated sub's rigidbody** picks up at most 0.09 m/s during replication, so physics barely fights the
  replicator, except right after a hand-over (fixed above).
- **Passengers standing in a moving, turning sub:** 0.000 m drift in their own game. The partner sees them at the
  same spot.
- **A passenger walking** (4 directions, 12–15 s) doesn't fall through the floor (lowest local y −4.71 against −4.43 at
  the start). The partner sees them within 0.09–0.13 m, about 20 ms late.
- **Docked Seamoth** (Alice's, in the sub Bob simulates): 0 m drift in the bay in both games, at rest and while
  driving. The simulating game's sub doesn't move at rest (0.007–0.06 m in 10 s). The non-kinematic remote docked
  vehicle (`VehicleDockingProcessor`) pushes nothing.
- **Coasting with nobody at the helm**, simulated by the player who left it: both games agree where it comes to rest
  (0.01–0.12 m).
- **Terrain:** the sub hit rising ground about 100 m west of the spawn point in runs 2 and 3. The replicated residual
  stayed ≤ 5 cm.
- **#2832 (low FPS inside the Cyclops):** not reproduced on this PC, where both games stayed at their 120 fps cap inside the
  moving sub. A slower PC may still show it.
- **#2372 (NRE in `CyclopsPawn.MaintainPosition`):** not reproduced in any run or earlier log. `MaintainPawns` now skips
  pawns whose objects are gone (defensive).
- **Pilot after leaving the helm:** can walk (probe); the scenario's "no movement" was the console in front of the
  chair.

**Fix:**
- `CyclopsMotor.ToggleCyclopsMotor` leaves the player controller as it found it. Cinematics, locked mode and initial
  sync re-enable it when they end; all paths that disable it re-enable it (read in 83031). While the local player
  isn't in control, the local pawn follows the player (`CyclopsPawn.FollowRealObject`) instead of pinning them, so the
  broadcast position is where the player is.
- `MovementReplicator.AddSnapshot` subtracts `Multiplayer.PacketWaitBound` from the latency sample. That's the real
  time since the previous packet processing, which a packet waited at most in this game. The previous frame's
  `deltaTime` wasn't enough: the long frame can come before `Multiplayer.Update` in the same frame (run 3).
- `MovementReplicator.HandOverMotion`, called by `SimulationOwnership.TreatVehicleEntity` when this game takes over:
  it puts the body at the latest snapshot (carried forward by its age) with the snapshots' velocity and angular
  velocity. It skips disabled replicators (docked vehicles) and snapshots older than 0.67 s.
- `NitroxCyclops.SetReceiving` zeroes the sub's leftover velocity.
- `JoiningManager`: a returning player whose sub is a vehicle gets the position of their player entity (child of the
  sub's entity). Only vehicles: see the finding below.

**Verification:**
- L2 `CyclopsMovementTest` (3):
  - the lock follows the helm: Bob spawns, Alice pilots, Bob is denied, Alice downgrades, Bob takes over, Alice gets it
    back when Bob leaves;
  - pilot and passenger positions follow the sub;
  - passenger rejoin after the partner drove on, also after a restart. **Fails without the `JoiningManager` change**
    (z 68 instead of 268).
- `dotnet test`: 315 pass, 13 skipped.
- **L3 `tools/scenarios/S13-CyclopsMovement.ps1`: PASS, run 5, 63 checks** (fresh world, all fixes, `eb0a7033` + the
  rejoin-drive tweak):
  - lag 204–216 ms, residual 7–8 mm RMS, 0 stalled and 0 backward frames, while the sub drives at 4.4–7.3 m/s;
  - after a 313 ms frame, `MaxAllowedLatency` stays at 0.080 s;
  - #2379: view error max 0.14 m;
  - hand-over at 6.95 m/s: 5.97 m/s after, largest backward step 0.003 m;
  - passengers 0 m drift, walking error ≤ 0.12 m;
  - docked Seamoth 0 m in both games;
  - the rejoin after 92 m.
  
  Run 4 had the same results except the rejoin, where the sub hit the rising ground. Run 3 was with the first latency
  attempt (previous frame's `deltaTime`), which didn't work: `MaxAllowedLatency` still 0.36 s. The before numbers
  come from the same measurements on the baseline build (runs 1–2 and the two probes).
- **Negative controls:** the L2 rejoin test fails without the fix. In game, the baseline runs and probes are the
  before measurements for #2379, the latency and the hand-over (same scenario steps, same thresholds' quantities). No
  separate negative-control run: all three causes are confirmed in code and in the recorded tracks.

**Finding, not fixed:** a new player's `PlayerEntity` gets the escape pod as parent id but an unparented transform
(local = world). After a restart it's linked under the pod, so its derived position is wrong.
`PlayerPositionPersistenceTest` caught that when the rejoin fix wasn't limited to vehicles. Nothing reads it today.

**Not verified:**
- Two defensive lines added to `HandOverMotion` after run 5, not run in game:
  - the snapshot age is clamped at 0, because on two PCs the clocks can differ by more than the latency, and a
    negative age would have skipped the hand-over;
  - the replicator disables itself so its own `Update` can't put the delayed pose back before `Destroy` runs.
- Two-PC clock skew in general (the rig is one PC).
- A long frame on the *pilot's* game. It delays the watcher's view by design, and wasn't measured.
- The hull pushing a swimming player outside the sub (collisions with the local player).
- Entering or leaving through a hatch at speed (S6 ran 50 hatch cycles on a slowly moving sub).
- The partner's game crashing while inside: Debug builds use a 5-minute network timeout, so it wasn't run. A clean quit
  is covered by the rejoin step.
- Latency or packet loss (localhost only).
- Hand-overs of Seamoth or Prawn: `HandOverMotion` is in the shared `MovementReplicator`.

**Risks:**
- At a hand-over, the new pilot's view jumps forward by the replication delay × speed (about 1.4 m at 7 m/s) during
  their sit-down cinematic. Before, the old pilot saw a bigger jump backwards.
- The latency sample is now a lower bound. On a client below about 15 fps, the interpolation delay (133 ms) plus the
  50 ms margin may not cover the wait (estimated, not measured).
- `ToggleCyclopsMotor` no longer forces the controller on. A path that enters or leaves a Cyclops with the controller
  off and never turns it back on would leave the player frozen. None was found in the IL, and the rejoin and docking
  steps passed. Rerun S6 `InteriorStateHolds` at the next `coop` integration.

---

## After the top 10 (Tier 1, in rough order)

| Issue | What | Notes from investigation |
|---|---|---|
| [#2508](https://github.com/SubnauticaNitrox/Nitrox/issues/2508) | Read PDA logs show unread again after relog | See detail below. |
| [#2652](https://github.com/SubnauticaNitrox/Nitrox/issues/2652) | "Wait for all players" at gun terminal and rocket | See detail below. |
| [#1811](https://github.com/SubnauticaNitrox/Nitrox/issues/1811) | Base power: stored power resets on rejoin; power only usable by its builder | Old (2022), verified. Reproduce on master. Hard if still true. |
| [#2168](https://github.com/SubnauticaNitrox/Nitrox/issues/2168) | Everyone freezes briefly when a player joins | PR #2825 is unreviewed and ships a binary AssetBundle. Skip it, or rebuild the bundle from the included shader source. Low impact with 2 players. |
| [#2250](https://github.com/SubnauticaNitrox/Nitrox/issues/2250) | Stalker tooth drops not synced | Teeth are needed for Enameled Glass (Cyclops). PR #2255 has changes requested. |
| [#2643](https://github.com/SubnauticaNitrox/Nitrox/issues/2643) | Precursor triggers ignore remote players | PR #2649; complements S3 and #2652. |
| [#2639](https://github.com/SubnauticaNitrox/Nitrox/issues/2639) | Player animation sync disabled | PR #2859. Nice for immersion. |

**#2508, PDA notifications:**
- Vanilla stores unread notifications per player (`NotificationManager`, serialized with the player). Nitrox never
  persists them (*confirmed*).
- The orange badges probably come from an initial-sync ordering issue (*hypothesis*): `PdaInitialSyncProcessor` can run
  before time is synced, so `PDALog.OnUpdate` re-notifies every entry when time jumps from 480 to now.
- Quick fix: `AddDependency<StoryGoalInitialSyncProcessor>()` in `PdaInitialSyncProcessor`.
- Full fix: about 8 files and 150 LOC, following the `RecipePinned` pattern. Patch `NotificationManager.NotifyAdd` and
  `NotifyRemove` for the Log and Encyclopedia groups, send a new `PdaNotificationChanged` packet, persist it per player,
  and restore it in `PlayerPreferencesInitialSyncProcessor`.
- It collides with the open refactor #2894.

**#2652, "Wait for all players":**
- Feasible, about 12–15 files and 400–600 LOC. It needs S3 and #2649 first.
- Gate `PrecursorDisableGunTerminal.OnPlayerCinematicModeEnd`'s `DisableGun()` and `LaunchRocket_OnHandClick_Patch`
  behind a server-side readiness service (`StoryEventReady` / `StoryEventReadyStatus` / `StoryEventStart` packets), with
  a `server.cfg` toggle and an admin override.

## Verify-first list (play-testing or bot runs, before planning)

- #2761 vehicle movement regression: probably fixed by #2864. **Phase 0.** From code and PR history (2026-11-01):
  #2864 (merge `c5eda87f`, in our base) is a maintainer's replacement for #2753, which was closed as "Superseded by
  #2864". The #2761 reporter had confirmed that #2753 fixed the symptom. #2864's author found the cause on the server: since
  #2314, `RealTimeElapsed` was coupled to game time, so the movement timestamps clients compare could shift. #2864
  decouples them again. That fits #2753's observation that it depended on join order. Upstream #2761 is still open and
  nobody has confirmed it in game on the current build. An in-game check on 2026-11-01 got as far as both players in one
  world with the dev console. It stopped there because the game ignores synthetic mouse-look, so a Seamoth can't be
  aimed at or driven by UI automation. **Still unverified in game:** do it with the S0.4 runner (A drives a Seamoth by
  script, B asserts its position), or watch for it in the first real session.
- Possible item or vehicle loss: #2768 (2nd Seamoth despawns 1st), #2448 (lockers vanish in Cyclops), #2538
  (eggs vanish in containment). For #2538, a probable cause was found in code: the egg is destroyed on the game of the
  player who doesn't hold the tank's lock. See COOP_FEATURES_SPEC P2.
- #2800 birds doubling per restart.
- Players rejoining at an unrelated position: **resolved, a test-rig artifact** (see S11).
- A partner offline at cure time staying infected: **fixed** (see S12).

See ISSUE_TRIAGE.md for the full verify list.

## Log

| Date | Item | Change | Verified by |
|---|---|---|---|
| 2026-11-01 | — | Spec created from upstream snapshot `62ef043a` | — |
| 2026-11-01 | Phase 1 | Branch `fix/phase1` (from `coop`, not merged into `coop` yet). All five PR heads were re-checked before merging: no new commits or review comments since the trial merge (#2899 `eefabc1e`, #2798 `029d320f`, #2809 `bb34919e`, #2902 `7efed983`, #2867 `ce7f689d`). Every step was built and tested on its own commit. | Each step: `dotnet build Nitrox.slnx` 0 errors (only the known DIMA001 + NU190x warnings), `dotnet test` all pass. Building needed `-m:1` (parallel builds hit file locks). `ThreadSafeList/SetTest.ReadAndWriteSimultaneous` failed once and passed 3/3 on rerun; it's a pre-existing flake. **No dual-client smoke test:** the S0 rig is still on the unmerged `fix/s0-test-rig` and hasn't had a first run yet. **Nothing in Phase 1 was verified in game.** |
| 2026-11-01 | S9 | Merged #2899 as is. No DI cycle: `WorldEntityManager` doesn't depend on `EscapePodManager`. | Build + test (256 pass). L3 `FreshWorldSpawnsInPod`: not run. |
| 2026-11-01 | S5a | Merged #2798 as is. | Build + test (256 pass). L1 `BaseLeakManager` test: not written (the fix is client-side MonoBehaviour code). L3 `BaseLeaksPersist`: not run. |
| 2026-11-01 | S8 | Merged #2809. Follow-up: `BatteryChildEntityHelper` clears its pending-battery table on `Multiplayer.OnAfterMultiplayerEnd`, the same hook `VirtualCyclops` uses. Didn't trim the comments, and didn't check the `GetPrefabChildren` duplicate-battery hypothesis. | Build + test (256 pass). L3 `EmptyToolNoDupe`: not run. |
| 2026-11-01 | S4 | Merged #2902, then #2867, then the fix in `LiveMixin_Kill_Patch`. Players return early: a `Player` component, `Player.main.liveMixin`, or a `RemotePlayerIdentifier`. For creatures (`CreatureDeath`), only #2867's new `broadcastKillOnDeath`/`passDamageDataOnDeath` case is skipped. **Deviation from the spec:** a full early return for creatures would also have dropped master's `destroyOnDeath` / `ShouldBroadcastDeath` path (e.g. the whitelisted Crash fish). #2902's `ReplayRemoteKill` skips those because it expects that `EntityDestroyed`. `GetComponent<CreatureDeath>()` assumes the component sits on the LiveMixin's own GameObject (`Kill` uses `BroadcastMessage`, which also reaches children): *[asset]*, unverified. Findings (Nitrox code): `Player.mainObject` carries the player's NitroxId (`PlayerInitialSyncProcessor`). Remote bodies carry the remote player's id and get their own `LiveMixin` (`RemotePlayer`). So unfixed #2867 could send `EntityDestroyed(playerId)`. IL: `LiveMixin.Kill` broadcasts `OnKill` for `passDamageDataOnDeath`/`broadcastKillOnDeath`. `Player.liveMixin` is a serialized field, so which GameObject holds it is *[asset]*; both checks are kept. Vehicles and the Cyclops already return earlier (`IsWhitelistedUpdateType`), so there's no double send for them. tornac1234's review question on #2867 (do these flags mean the object is destroyed?) is still open upstream. | Build + test (256 pass). L3 `CreatureDeathSyncs` and the player-death regression check: not run. |
| 2026-11-01 | S3 | New `StoryGoalCustomEventHandler_NotifyGoalComplete_Patch` (Prefix). Sets `gunDisabled = true` when `key` matches `gunDeactivate.key` (ordinal ignore-case). Verified in decompiled IL (game 83031): `NotifyGoalComplete(string)`; `gunDeactivate` is a `StoryGoal` with `key`; remote `StoryGoalExecuted` → `StoryGoal.Execute` → `StoryGoalManager.OnGoalComplete` (only for newly completed keys) → `customEventHandler.NotifyGoalComplete`. The method itself also compares against the literal `"Goal_Disable_Gun"`. Optional step 2 (close the partner's open terminal) and step 3 (gun animation) not done. | Build + test (258 pass, +2: target method exists, key comparison). L3 `GunDisableSyncs`: not run. |
| 2026-11-01 | S7 step 1 | `WorldEntityManager` now has an id → cell index. Registering in a new cell drops the old entry, and entries can be removed by id whatever the current record is (`StopTrackingEntity(NitroxId)`, `UnregisterWorldEntity(NitroxId)`, `SetWorldEntities` used by `WorldService` on load). `EntitySpawnedByClient`, `EntityReparented` (non-global-root entities), `PickupItem` and `TryDestroyEntity` remove entries by id. `GetEntities` skips and removes, with a warning, entries whose entity is gone or is no longer a cell entity. `GetEntities` does *not* check whether a record still lies in the same cell: children of moving global roots can legitimately drift. Steps 2–4 not started. | Build + test (270 pass, +12 in `WorldEntityCellTrackingTest`). With the processor changes reverted, 5 of them fail. L3 `NoFloatingLeftovers`: not run. |
| 2026-11-01 | S0.1, S0.5, S0.6 | Branch `fix/s0-test-rig`: `tools/dual-client.ps1` + instantlaunch fixes (second game, per-game Unity log), `SaveLoadRoundTrip` helper + 3 tests, `/report` + F4 bug bundle (client + server `report` command). #2761 assessed from code: probably fixed by #2864 | `dotnet build` + `dotnet test` (259 pass, 13 skipped). 3 dual-client runs that started the games (a 4th used the wrong syntax and started nothing): both joined every time, PASS; `/report` (both players) and F4 in game. #2761 **not** verified in game (mouse-look can't be automated) |
| 2026-11-01 | S0 merge | `fix/s0-test-rig` merged into `coop` on top of Phase 1. The only conflict was this Log table. | `dotnet build` + `dotnet test` on the merge (273 pass, 13 skipped). No dual-client run on the merged build yet, so Phase 1 is still not checked in game. |
| 2026-11-01 | Phase 1 smoke test | Dual-client run on merged `coop` (`962f7c51`, Debug, `-m:1`) with save `dual-client`. Run 1 started from an empty world. Both games were then closed, and the server saved the generated world when it hibernated (`EntityData.json` 10 MB). Run 2 restarted from that save with both players returning. | Both runs PASS (`-WaitForJoin`, both joined within about 30 s). Server log: no warnings or errors in either run, including no stale-entry warnings from the S7 `GetEntities` cleanup, and no batch respawn after the restart. Client logs: no exceptions. The only warnings are known startup noise plus two "Couldn't find an id" warnings. The Skyray warning appears in every pre-Phase-1 run too. The escape-pod Radio warning comes from `EscapePodMetadataProcessor` calling `pod.DamageRadio()` for a returning player; the Radio has no NitroxId, and Phase 1 doesn't touch that path. **Only joining and save/load were exercised.** None of the Phase 1 behaviour (kills, batteries, gun deactivation, leaks, pickups) was triggered in game. |
| 2026-11-02 | S0.4 dev bridge | Branch `fix/dev-bridge` (from `coop`). Debug-only (`#if DEBUG`) localhost TCP endpoint per game (named after the instant-launch player) and on the server; `tools/bridge.ps1` CLI; `bridge.ps1 diff` desync detector; `tools/scenarios/` with S3 and S8. Game commands: `state`, `teleport`, `look`, `console`, `chat`, `entity`, `find`, `get`/`set`/`call` (reflection, entity id or static path), `use` (`OnHandClick`), `screenshot`, `entities`. Server: any console command with its output, plus `entities` and `players`. `CommandService.ExecuteCommand` now returns the handler's task on the normal path too (before, callers such as `ServerCommandProcessor` couldn't await it). Decompiled game 83031 at `F:\nitrox\game-src\83031` (AGENTS.md §5.3). | `dotnet build Nitrox.slnx -m:1` (only NU190x/DIMA001 warnings), `dotnet test` 273 pass, 13 skipped. Release build (`Nitrox.Launcher -c Release -r win-x64`): no bridge types in any Nitrox DLL. Every command was used against two real games and the server (dual-client runs, save `dual-client` and fresh `bridge-scenario` worlds). |
| 2026-11-02 | S3 | L3 `GunDisableSyncs` (`tools/scenarios/S3-GunDisableSyncs.ps1`): Alice gets `Infection_Progress5` via `goal Story ...`, teleports into the terminal area (the terminal opens), uses the terminal through `OnHandClick`; the cinematic ends after about 15 s and calls `DisableGun`. | **PASS** on 3 runs (one by hand, two scripted, fresh world each): Bob's `gunDisabled` turns true within 2 s of Alice's. **Negative control:** with `StoryGoalCustomEventHandler_NotifyGoalComplete_Patch.cs` removed, the same scenario FAILS exactly as #2640 describes: Bob has `Goal_Disable_Gun` but `gunDisabled` stays false. Not covered: the `LaunchRocket` click (no rocket in a fresh world), optional steps 2-3. |
| 2026-11-02 | S8 | L3 `EmptyToolNoDupe` (`tools/scenarios/S8-EmptyToolNoDupe.ps1`): `item flashlight` → `EnergyMixin.Select(null)` (battery to inventory) → `Inventory.InternalDropItem` → Bob `use` (Pickupable.OnHandClick). | **PASS** on 3 runs (17 checks; a 4th run passed every S8 check but its final `diff` flagged a 0.01 battery-charge drift, which is why `diff` now treats numbers within 0.5 as equal): Bob's flashlight has no battery, server has 0 children under it, server battery count unchanged, `diff` clean for the flashlight and battery. **Negative control:** with #2809's four files reverted, it FAILS as #2668 describes: after Bob's pickup the flashlight has a new battery (new id, charge 100), the server stores it as an `InstalledBatteryEntity` and has 3 batteries instead of 2. The `GetPrefabChildren` duplicate-id hypothesis didn't show in this path (no duplicate child). Relog not exercised. |
| 2026-11-02 | Findings (diff) | First `bridge.ps1 diff` runs on a fresh world turned up three things to triage, none fixed: **(1)** escape pod metadata: in Alice's game `RadioRepaired` is true while the server and Bob have false (the radio isn't damaged in one player's game?). **(2)** Some nested `PrefabPlaceholderEntity` records (e.g. the `Bench` in `ExplorableWreck_KelpForest_17`, id `0ebe98e9-…`) have a bogus server `LocalPosition` (-782, 418, -1081 under a group at (67, -25, 394)), so the server puts them in a cell about 1.4 km away (-832, -1120, 320), while the game places them correctly. Possible source of S7-style ghost-cell entries; hidden from `diff` by a commented `$KnownNoise` entry. **(3)** Loose physics objects (ScrapMetal) end up about 30 m apart between the two games. | Seen in every fresh-world run. Not investigated further. |
| 2026-11-02 | S1 | Branch `fix/s1-s5b-cyclops-flood`. Merged #2881 at `ebf88434`, its newest head. The author's last comment says sear0M's fire-douse, `oldHPPercent` and overshield findings are addressed; the "two ids" and "killing blow" findings were declined. Conflict in the `EscapePodManager` constructor: kept #2899's `WorldEntityManager` and #2881's `IPacketSender`. Added `[ProtoInclude(92, typeof(CyclopsDamagePointMetadata))]`. Fixed `CyclopsDamagePointEntity.ToString`, which called itself `CyclopsFireEntity` (the server's `query` command prints it). LiveMixin: #2881 only touches `LiveMixin_AddHealth_Patch`, with a new `CyclopsDamagePoint` case before the `BaseCell` case. `LiveMixin_Kill_Patch` is untouched. Damage points (repaired via `AddHealth` → `OnRepair`) and fires (`Fire.Douse` sets health directly) never go through `Kill` (decompiled 83031). | `dotnet build` (only NU190x/DIMA001 warnings); `dotnet test` 281 pass. L1 `CyclopsDamagePointsAndFiresSurviveSaveAndLoad`. **L3 `S1-CyclopsDamagePersists.ps1`: PASS, 1 run, 48 checks.** The lock holder damages the Cyclops with `LiveMixin.TakeDamage` until a fire starts (2 damage points and 1 fire at 74 %). Both games and the server have the same point indexes and ids, the same fire and the same health. Bob welds a point (`LiveMixin.AddHealth`); it closes for Alice and the hull heals. After a save and a restart of the server and both games, the points, ids, fire and health are unchanged and the repaired point stays closed. **No in-game negative control** (coordinator rule): the cause is confirmed in code, and without #2881 the server stores no damage entities at all. Not covered: fire dousing, extinguisher FX, the intro escape-pod fire, a Cyclops destroyed by damage, latency. **Save format: one-way** (new entity types under the Cyclops; a build without them refuses the save). Back up the world first. |
| 2026-11-02 | S5a | No code change (#2798 is already in `coop`). | **L3 `S5-BaseLeaksPersist.ps1`: PASS, 1 run, 18 checks.** Alice builds a corridor with the scripted builder (`BaseHelpers.ps1`). The lock holder crushes the hull (`BaseHullStrength.totalStrength` < 0, then the game's own `CrushDamageUpdate`) until a cell is at 60 health. Bob and the server get the leak. After a restart, the server has the same leak id and both games still leak. Bob welds the leak; the server drops it and Alice sees it repaired. After a second restart, the hull is whole for both players and the server has no leak. **#2798's trigger was not reproduced:** neither game logged `Cell object not found`, so the cell object existed when the leak spawned. An in-game negative control would most likely pass even without #2798, so none was run (coordinator rule). Not covered: a base far from the joining player, many leaks, an L1 test for `BaseLeakManager` (a MonoBehaviour). |
| 2026-11-02 | S5b | Built from scratch; #2811 was only a reference. `BaseFloodMetadata` (grid size + `BaseFloodSim.cellWaterLevel`; an empty array means dry) on the base's `BuildEntity`, with `[ProtoInclude(93)]`. The extractor is on `Base`. Client `BaseFlooding`: only the holder of the base's simulation lock sends. Each frame it first checks a 2 s timer, so other frames cost one table lookup per base. It sends only when a cell's level changed by at least 0.01. It doesn't send during a building resync or before the server's levels are applied. Levels from the server are applied on spawn, rejoin and resync, deferred until the flood grid exists, and dropped if the grid size differs. The lock holder ignores later updates. `BuildingResyncProcessor`: one added line (apply the base's metadata after `SetupBase`). The first game run found an Autofac cycle (`BaseFlooding` → `Entities` → `EntityMetadataManager` → processor → `BaseFlooding`), so the client couldn't join. Fixed by sending through `IPacketSender`, and guarded by the new L1 test `ClientContainerResolvesAllPacketProcessors` (it fails on the cyclic version). | L1: `BaseFloodLevelsSurviveSaveAndLoad`, `BaseFloodLevelsTest` (5 tests), the container test. **L3 `S5-BaseFloodResync.ps1`: PASS, 1 run, 15 checks.** A crush leak floods the corridor to 1.0, then Bob welds it. 5 s later Alice and Bob are both at 0.89 and the server stores `BaseFloodMetadata`. After a save and restart, both players rejoin at 0.41 each instead of 0 (it drained during loading and the checks). **No in-game negative control** (cause confirmed: nothing stored the levels). Not covered: only a full restart was run, not one player relogging while the other stays; bases with several rooms or bulkheads; a base rebuilt between save and load (the levels are dropped and the base starts dry). **Save format: one-way** (new metadata type on bases), as for S1. |
| 2026-11-02 | Findings (diff) (1) | **Cause: the scenario rig, not Nitrox.** The Debug intro skip ends with `EscapePod.StopIntroCinematic` → `DamageRadio` (`LiveMixin.TakeDamage(80)`), and `DamageSystem.CalculateDamage` returns 0 while `NoDamageConsoleCommand` is on. `Start-ScenarioGames` turned that cheat on right after the join, so whichever game skipped its intro later kept a full-health radio. Fix: `Start-ScenarioGames` now waits until `uGUI.main.intro.showing` is false before turning on the cheats. | Fresh worlds in game: without cheats, both radios at 20. With the old timing, one radio at 100 (Bob in that run, Alice in earlier ones). With the fix, both at 20, and `diff` reports no escape-pod metadata mismatch. Findings (2) and (3) not investigated. |
| 2026-11-02 | S0.3 / L2 harness | Branch `fix/l2-server-harness`. `Nitrox.Test/Helper/Headless/`: `ServerHarness` runs the real server host in-process (free UDP port, temp save, `SaveAsync`/`RestartAsync`, DI access); `FakePlayer` does the real LiteNetLib join handshake with only `Nitrox.Model`. Server changes, no processor touched: `Program.CreateHostBuilder(..., interactive)` (tests leave out console input, launcher connection, dev bridge and the hosted status log, whose WAN lookup took about 6 s), save/log paths follow the server's own `--data-path`, pooled processor contexts no longer reused across servers in one process. Tests in `Nitrox.Test/Server/Headless/` (`TestCategory("L2")`): S7 step 1 in session, S7 pickup-then-store across restart, S9 join payload, S8 tool battery count across restart. **Finding:** the S7 cleanup is undone by a restart for items still recorded as `WorldEntity` (S7 plan item 5); that test is `[Ignore]`d. | `dotnet build Nitrox.slnx -m:1` (no new warnings), `dotnet test` 277 pass, 14 skipped (13 Linux-only + the ignored one), 11 s; `--filter TestCategory=L2` 4 pass, 1 skipped, about 6 s. Server start 0.5–0.6 s (first in a run about 1.2 s), restart about 1 s. **Negative control:** with the S7 step-1 processor lines reverted, the in-session S7 L2 test fails (the item comes back as an entry of its old cell), as do the 5 L1 tests. No game started. |
| 2026-11-02 | S2 | Branch `fix/s2-s10-equipment-maproom`. **Root cause A is already fixed upstream:** `Pickupable_OnDestroy_Patch` (#2322, June 2025) skips `Pickupable.OnDestroy` while `BuildingHandler.Resyncing`, which spans `ClearBaseChildren` and the frame wait, so step 1 was not built. **Root cause B:** `EntityDestroyedProcessor` already wraps `DestroyPickupable`/`DestroyObject` in the suppressor; only `Entities.DestroyObject` from `CleanupExistingEntities` (force respawn) was open, now suppressed (step 2). **Root cause C (built):** `RestoreMapRoom` applies the room's metadata and replaces/spawns its `InventoryItemEntity` children (a resync keeps the room, so existing items are cleaned up first; without that a resync doubled the upgrades locally, found in game). **Cameras (built):** `MapRoomCameraDocking_Start_Patch` sets `deserialized`; docked drones are spawned by `MapRoomCameras` from `MapRoomMetadata.DockedCameraIds`, or with ids room id +1/+2 for a room without metadata (new, or saved before this). Taking a drone (`MapRoomCamera.OnPickedUp`) or docking one updates the metadata. Remote clients now give a scanner room built as a new base the builder's id (`BuildingHandler.BuildBase`; before, the partner's room had no id). **Save format:** additive only (`MapRoomMetadata`, see S10); existing rooms get two drones once, with fixed ids. Bridge: `call` resolves `InventoryItem`/`Equipment`/`ItemsContainer` arguments; scenarios in `tools/scenarios/` with `BaseEquipmentHelpers.ps1` (building through `Builder`/`Constructable`, docked Seamoth). | `dotnet test` 291 pass, 14 skipped. L1: `ScannerRoomStateSurvivesSaveAndLoad`, `MapRoomCamerasTest`. L2 (`BaseEquipmentAndScannerRoomTest`): charger batteries survive a resync request, an unequip and a restart; scanner room children and metadata survive a restart and reach a late joiner (passes on the old server too: the server half was never broken). **L3, one PASS each:** `S2-MapRoomUpgradesPersist` (38 checks: 2 upgrades and exactly 1 docked drone after a resync and after a restart + rejoin, taken drone still in inventory), `S2-BaseEquipmentSurvivesResync` (all 40 functional checks pass: resync outside and inside, Alice logging out, restart; its final `diff` flagged the docked Seamoth's parent and stale `LightsOn`, unrelated, so the script now checks only the equipment), `S2-UnequipEcho` (22). **Negative control (game):** with the child spawning removed from `RestoreMapRoom`, `S2-MapRoomUpgradesPersist` FAILS as #2696 describes: after the restart both games show 0 upgrades while the server has 2. **Not reproduced:** module loss on resync (#2259, docked Seamoth modules + power cell), the echo, and "batteries gone after a plain logout": `IngameMenu.QuitGame` → `Application.Quit`, and Nitrox disconnects in `uGUI.OnApplicationQuit` before objects are destroyed; the server kept everything. **Not covered:** base chargers and reactors in game (placing a wall module inside a base through the bridge failed: teleporting doesn't set `currentSub`, and setting it made the player fall through the floor; they use the same `Equipment` path as the Seamoth modules), the camera negative control (cause confirmed in IL), drones re-docked after being dropped (metadata updates, but the dropped world entity also stays on the server), stalkers carrying a docked drone off (vanilla `OnShinyPickUp`, only on the simulating player, not synced). |
| 2026-11-02 | S10 steps 2–4 | Same branch. **Shared scan target:** `MapRoomMetadata { TypeToScan, DockedCameraIds }` (`ProtoInclude` 94) with extractor/processor; `uGUI_MapRoomScanner.OnStartScan`/`OnCancelScan` postfixes broadcast it. Found in game: a room is unpowered for a moment after each load, and `uGUI_MapRoomScanner.OnDisable` then cancels the scan, so every join reset it; that cancel is skipped in multiplayer (the cancel button still cancels for everyone). **Cameras:** step 3 as in S2. **Ghost pings:** `ResourceTrackerHelper` unregisters an object's `ResourceTracker`s before a remote destroy or force respawn, and removes the id from `ResourceTrackerDatabase` when the object isn't loaded (verified in game: `ResourceTracker.uniqueId` is the NitroxId, so the "captured before Nitrox sets the id" hypothesis doesn't hold for batch outcrops). The server sends `EntityDestroyed` to every other player, not only those who see the cell (it can't tell which prefabs carry a `ResourceTracker`); the client's "could not find entity" is now Debug. Camera piloting (#1425) out of scope. | L1 `EntityDestroyedPacketProcessorTest` and L2 `DestroyedOutcrop_ReachesPartnerWithoutTheCellLoaded`: both FAIL on the previous server processor. L2 scan target/metadata test (see S2). **L3, one PASS each** (run with `-NoLaunch` on the S2 games): `S10-MapRoomScanShared` (11 checks: Bob scans the same resource and lists the same 23 nodes, server stores it, still scanned by both after a restart + rejoin, Bob's cancel stops Alice's), `S10-MapRoomPingsClear` (11 checks: an outcrop Bob breaks leaves Alice's database and room, loaded and unloaded). The run before the `OnDisable` fix failed exactly there (scan `None` after the restart, server still `LimestoneChunk`). No in-game negative control for the client unregister (confirmed in IL: `ResourceTracker` has no `OnDestroy`). **Not covered:** scanner HUD blips, resources picked up by the partner in unloaded cells (same `CleanupExistingEntities` path, not run), power-dependent scanning. |
| 2026-11-02 | S7 step 2 | Branch `fix/s7-containers`. **Producer = holder of the base's simulation lock** (`NitroxClient/GameLogic/Bases/BaseModuleSimulation.cs`, same convention as `Trashcan_Update_Patch`). Bases are global roots, so the server already grants it (first joiner via `AssignGlobalRootEntitiesAndGetData`, builder via `TryClaimBuildPieceAsync`) and hands it over on disconnect (`CalculateSimulationChangesFromPlayerDisconnect`; every player can see a global root). `FiltrationMachine_Spawn_Patch`: other games skip `Spawn` (timers and power draw keep running); the producer's item is announced once as `EntitySpawnedByClient(InventoryItemEntity, parent = machine, no lock)` instead of `PickupItem` + `EntityReparented` (`Items.MachineProducingCount`, `Items.ProducedInContainer`). `BaseBioReactor_ProducePower_Patch`: other games return the requested power without burning (base power isn't synced, so the partner's base stays powered); the producer sends `EntityDestroyed` per burned item (transpiler replaces the `Object.Destroy` call). Read in decompiled 83031: `FiltrationMachine.Spawn/UpdateFiltering/TryFilter*`, `BaseBioReactor.Update/ProducePower`, `ItemsContainer.UnsafeAdd/HasRoomFor`. | Build + test (294 pass, 13 skipped). L2 `MachineItemsTest` (server side of both packets, across a restart). **L3 in game, one PASS each** on the new fixture `tools/fixtures/s7-machine-base` (a room with 2 filtration machines and a bioreactor, built through the bridge): `S7-FiltrationNoOverflow` (37 checks: 2 + 2 per machine on the server and in both games, never above 2 while running, after looting, lock handed over when the producer quits and the other game refills, after a restart) and `S7-BioreactorSync` (31 checks: only what fits goes in, 4 eggs burned by the producer are gone on the server and in both games, 1 egg after a restart, the burned ones don't come back). **No in-game negative control** (rig time; the causes are confirmed by code reading, not by a failing test). Not covered: a player rejoining without a server restart (quitting to the menu ends an instant-launched game), a base whose lock nobody holds. |
| 2026-11-02 | S7 step 3 | `ItemContainers.AddFromRemote`, used by all three mirror paths (`EntityReparentedProcessor`, `InventoryItemEntitySpawner`, `ItemContainers.AddItem`). **The planned extra `RemoveItem` isn't needed:** `new InventoryItem(pickupable)` → `Pickupable.SetInventoryItem` already force-removes the item from its old container (83031). That removal runs the old container's listeners, though, and `Equipment_RemoveItem_Patch` then sends `EntityDestroyed` for an item leaving a charger or module slot (an echo that deletes the item on the server; possibly an S2 cause). So every packet an add or removal can send is now suppressed while mirroring. A full target container logs a warning; the add still happens (the server already accepted it). | Build + test. In game (`S7-NoFloatingLeftovers`): Bob's 8 items moved into the escape pod locker show up only in the locker, in both games and on the server. The full-container warning and the equipment echo were not triggered in game. |
| 2026-11-02 | S7 step 4 (#2047) | **Stale.** Not reproduced: in `S7-NoFloatingLeftovers` both players opened and closed the escape pod locker 6 times, then again after a restart; the content stayed `Copper=4, Titanium=4` on the server and in both games. The "storage version" text from the report isn't in the 83031 source (`StorageContainer.CleanUpDuplicatedStorage` shows no message). | L3 `S7-NoFloatingLeftovers` PASS once (60 checks; also: no moved item outside a container after Alice quit and everything restarted, no stale-cell warning in the server log). Wall lockers weren't tested (placing one through the bridge failed); the pod locker is a `StorageContainer` too. |
| 2026-11-02 | S7 item 5 + diff finding (2) | **Item 5:** `WorldEntityManager.SetWorldEntities` (save load) now registers only records that are in a cell during a session (`BelongsInCell`: top level or inside a hierarchy of cell entities; not under a container, inventory or global root). `GhostCellEntryTest` restart case un-ignored. **Finding (2) has a different cause:** some prefab groups (e.g. `34d3f7a1-…` in the KelpForest wrecks) put placeholders under an intermediate object. The game spawns them under that parent; the server parented them straight to the group, so their position and cell were about 1.4 km off. The prefab cache now stores each placeholder's parent transform relative to the group root (whole `m_Father` chain, matrix-composed; cache v5 under a new file name so builds on one PC don't rebuild each other's cache), and `BatchEntitySpawner` (at creation, before the entity's own children) and `WorldService` (on load) link through it. Stored local transforms are unchanged: **no save-format or protocol change**, existing saves get the right cells on load. The placeholder `$KnownNoise` entry is removed. | L1 `PlaceholderTransformsTest` (3), `WorldEntityCellTrackingTest` +1; L2 `PlaceholderCellTest` (real wreck batch, before and after a restart). **Negative controls:** without the load filter `GhostCellEntryTest` fails; without the spawn link `PlaceholderCellTest` fails (Techlight 1398 m off), without the load link it fails after the restart (StarshipCargoCrate 1396 m off). In game: the Bench `0ebe98e9-…` is at (62.4, -39.0, 382.7) on the server and (62.443, -38.983, 382.709) in both games. Still in `diff`: one placeholder 1.6 m off with a different parent (`049368f2-…`), not investigated. Entity-slot placeholders still use the old one-level offset. |
| 2026-11-02 | S6 | Branch `fix/s6-swimming`. **Code:** `Player_SetCurrentSub_Patch` broadcasts from a Postfix, only when `currentSub` really changed. New `EnterExitHelper_Enter_Patch` forces the sub when a hatch entry didn't set it; when `Enter` runs during a cinematic it waits for the cinematic to end instead of skipping (IL: `PlayerCinematicController.OnPlayerCinematicModeEnd` sends the end message *before* `EndCinematicMode` when an end transform is used, so "skip while `cinematicModeActive`" would skip exactly that case). `SubRoot_OnPlayerExited_Patch` logs every non-base exit (distance to origin, cinematic, vehicle, stack trace, debug level). `PlayerTeleported` carries `InPrecursor` (warp: the target's, `back`: the stored one, `tp`: false), the client applies it after `OnPlayerPositionCheat`; server `Player.Teleport` updates `InPrecursor` and stores the origin's `SubRootId`/`InPrecursor` for `back` (it stored the *destination's* sub). `PlayerPositionInitialSyncProcessor` sets `InPrecursor` again after the world loaded; `PrecursorTracker` sends `InPrecursor` and `DisplaySurfaceWater` once at start. `CyclopsTriggerFilter` + Prefixes on `PrecursorDoorMotorModeSetter`, `PrecursorMoonPoolTrigger` (enter/exit), `DisableWaterRenderingVolume`, `RoomLinks`, `PlayerSoundTrigger` (enter/exit): skip colliders inside a Cyclops that aren't the local player's own (for the door setter this also skips the exosuit branch, only relevant for an exosuit docked in a Cyclops). `CyclopsPawn` drive-by: pawn layer index 19 instead of `1 << (1 << 19)` = layer 1 (TransparentFX). Dev bridge: `find:<Type>#<GameObject name>` targets. **Asset facts (read in game):** the Cyclops `EntranceHatch` calls `EnterExitHelper.CinematicEnter` from `onCinematicStart` (forced, so `CanChangeSub` can't reject a hatch entry); `InternalDiveHatch` calls `CinematicExit` from `onCinematicEnd`. `SubRoot.IsUnderwater` is always false for a Cyclops (IL), so swimming inside one needs `currentSub == null`. **Which root cause fires:** (1) *Cyclops, `currentSub` lost:* not reproduced. 50 hatch cycles (45 hatch, 5 Seamoth dock/undock) with Bob inside and the sub moving: 0 problems, 51 exit traces all from `EnterExitHelper.Exit` (46) or `OnUndockingComplete` (5), 0 heal warnings. #2712's own estimate is 1 in 25 entries, so the hatch path is unlikely to be the cause on its own; the exit logging is there to catch it in the playthrough. (2) *Triggers through the Cyclops parenting:* **confirmed** for Cyclops child colliders without their own PrefabIdentifier (the docking bay's): fed to the QEP moon pool trigger and a ForceWalk door trigger in Alice's game, they gave her the moon pool's water plane and `precursorOutOfWater` true while she stood in the Cyclops. Not via a remote body (it resolves to its own entity root), contrary to the spec's guess. (3) *Warp into a dry precursor room:* **confirmed** (`warp Bob Alice`, Alice behind the PCF main door: Bob Dive mode, underwater, in the air). (4) *#2306 rejoin:* **not reproduced** on coop (rejoin inside the PCF moon pool volume and behind the main door: both walk), so the restore-after-load and tracker changes are unproven defensive fixes. | `dotnet build` (only NU190x/DIMA001 warnings), `dotnet test` 289 pass, 14 skipped (+3 L1 `PlayerTeleportTest`, +1 L2 `PrecursorWarpTest`; both fail with the server half reverted). **L3 in game (1 PASS each, merged build):** `S6-InteriorStateHolds` (50 cycles, 14 checks), `S6-PrecursorRejoin` (30 checks: walk in, warp, `/back` + `/warp`, both relog, Cyclops colliders on precursor triggers, pawn on Player layer and grounded). **Negative controls (client fixes reverted, server kept):** `S6-PrecursorRejoin` FAILS at "after the warp" and "after /warp" (Bob Dive, underwater), relog still passes; Cyclops part FAILS 3 checks (docking bay collider → water plane + `precursorOutOfWater` true); with the fix restored the Cyclops part PASSES again (15 checks, `-CyclopsOnly`). Interior: no negative control possible (no repro). All patches applied without errors (no Harmony/patch errors in the client logs of these runs). **Not verified:** a Cyclops simulated by the *other* player while entering (Alice spawned it, so she held its lock in every cycle; the network-driven case is the most plausible untested path for cause 1), the `EnterExitHelper` heal (never fired), real walking through triggers (moves use `SetPosition` + the trigger's own `OnTriggerEnter/Exit`; `SetPosition` alone fires no triggers), the hull touching a trigger by physics, rejoin inside a Cyclops, `RoomLinks`/`PlayerSoundTrigger`/`DisableWaterRenderingVolume` effects, real pawn walking (only grounded checked). |
| 2026-11-02 | S4 | No code change. L3 `S4-CreatureDeathSyncs.ps1`: Peeper and Stalker spawned by Alice (she holds the lock), killed by Bob with `LiveMixin.TakeDamage(10000)` (no-damage cheat off for that call: `DamageSystem.CalculateDamage` zeroes all damage, creatures included, while it's on); acid mushroom (`destroyOnDeath` false, `broadcastKillOnDeath` true, so this is #2867's path) damaged to death by Bob; Alice killed with `Player.main.liveMixin.Kill()`. | **PASS, 1 run, 23 checks** (merged build, `-NoLaunch` on the S6 world): corpses (`IsAlive` false) in both games and still dead 5 s later, server entity gone for both creatures; mushroom gone in both games and on the server; Alice's PlayerEntity still on the server while dead and after respawn, Bob has her body within 5 m of her respawn point. Not covered: knife/Prawn-drill hits (damage called directly), heat kills, respawners. |
| 2026-11-02 | S9 | No code change. L3 `S9-FreshWorldSpawnsInPod.ps1` (new save `s9-fresh`); `ScenarioHelpers` gained `Get-LogMarks`/`Get-LogTextSince` (only the log text written during the run). | **PASS, 1 run, 19 checks:** both players have `currentEscapePod` and `Player.escapePod` true, 2.1 m from the pod, right after joining and 10 s later; the server has 1 pod; no `OptionalEmptyException` or "Game object required from id" in the run's part of `game[Alice/Bob]`, `Player-Alice/Bob.log` and the server log. |
| 2026-11-02 | S11 | Not a game bug. Cause: culture-formatted numbers (`-53,74`) in scenario scripts, accepted by the dev bridge as thousands-grouped, teleported players out of the world and the game reset them to random surface spots. Bridge now parses with `NumberStyles.Float`; `ScenarioHelpers.ps1` sets invariant culture. New L2 `PlayerPositionPersistenceTest`, L3 `S11-QuitKeepsPosition.ps1`. | L2 pass. L3 PASS (6 checks, 1 run after the fix); before the fix, the same run stored Alice ~570 m away and a sampling run showed the out-of-world teleport and reset. |
| 2026-11-02 | S12 | `InfectionStory.Reconcile` applies the game's infection rules for completed `Infection_Progress1-5` goals in `JoiningManager.SendInitialSyncAsync`. | L1 `InfectionStoryTest` (6), L2 `OfflineInfectionTest` pass; negative control (fix removed) fails with infection 1.0. Not run in game. |
| 2026-11-02 | S14 | Branch `fix/s14-creature-aggro`. New packet `CreatureAttackedPlayer` + server/client processors. For creatures outside `AI.syncedCreatureWhitelist`, the simulating game bites other players' bodies (`MeleeAttack_CanDealDamageTo_Patch` Prefix) and sends the damage (`LiveMixin_TakeDamage_Patch`); other games don't bite their own player with a creature they don't simulate; crash fish explosions are relayed too (`Crash_Detonate_Patch`); `AggressiveWhenSeePlayer` prefers the nearest player, local or remote. Leviathans unchanged. Read in decompiled 83031: `AggressiveWhenSeeTarget`, `AggressiveWhenSeePlayer`, `AttackLastTarget`, `MeleeAttack` (+ Reaper/Crabsnake/Shocker/Warper/SeaDragon subclasses), `LastTarget`, `Creature`, `EcoTarget`/`EcoRegionManager`, `Crash`, `ProtectCrashHome`, `DamageSystem`, `LiveMixin.TakeDamage`, `OnTouch`, `Player.CanBeAttacked`, `BehaviourLOD` (movement only, not AI). Asset values read in game: each creature's aggression scans, bite values (see S14). | `dotnet build Nitrox.slnx -m:1` (no new warnings), `dotnet test` 314 pass, 13 skipped (+2 L2 `CreatureAttackRoutingTest`, both fail without the lock check). **L3 `S14-CreatureAggro.ps1`: PASS, 65 checks** (11 cases, 9 natural bites, 2 forced contacts). **In-game negative control** (client patches reverted): 7 of 35 checks fail; the partner gets no damage even from a forced contact, and the victim's own game bites in the double-bite case. Follow-up commit (no bite through a vehicle hull, `Creature_IsFriendlyTo_Patch`): `dotnet test` 314 pass, and S14 re-run in game with stalker A→B, B→A, double-bite and crash fish: **PASS, 24 checks**. Not run: Crabsnake, Ampeel, Warper, Mesmer, Lava lizard, River prowler, Crabsquid, vehicles, containment-born creatures, latency. |
| 2026-11-02 | S13 | Branch `fix/s13-cyclops-movement`. New bridge commands `record` (per-frame tracks) and `input move` (held game input), `S13Helpers.ps1` (track comparison) and L3 `S13-CyclopsMovement.ps1`. Fixes: #2379 (`ToggleCyclopsMotor` no longer enables the controller during cinematics; the local pawn follows the player while not in control), replication latency excludes the local wait between packet processings (`Multiplayer.PacketWaitBound`), helm hand-over keeps the sub's pose and velocity (`MovementReplicator.HandOverMotion`, `NitroxCyclops.SetReceiving` zeroes leftover velocity), a passenger rejoins inside the sub that moved on (`JoiningManager`, vehicles only), `MaintainPawns` skips dead pawns (#2372, defensive). Not reproduced: passenger sliding or falling through, docked-Seamoth drift, physics fighting the replicator (outside hand-overs), #2832 FPS. | L2 `CyclopsMovementTest` (3; the rejoin test fails without the fix). `dotnet build` (only NU190x/DIMA001 warnings), `dotnet test` 315 pass, 13 skipped. **L3: PASS, run 5, 63 checks.** Before/after: #2379 partner view error 2.66 m -> 0.14 m; one 300 ms frame raised `MaxAllowedLatency` 0.077 -> 0.309 s for 15+ s (lag 498 ms, 3.6-4.2 m behind at 7 m/s, 0.98 m skip when lowered) -> stays 0.08 s, lag 208 ms; hand-over of a moving sub: new pilot 5.4 -> 0.05 m/s and 1.9 m snap back for the old one -> 6.95 -> 5.97 m/s and 0.003 m; rejoin after the sub moved 92 m: inside. Run 5 was on `eb0a7033`; two defensive lines in `HandOverMotion` came after it and weren't run in game (the snapshot age is clamped at 0 because two PCs' clocks can differ by more than the latency; the replicator disables itself so its own `Update` can't put the delayed pose back before `Destroy`). Not verified: hatch use at speed, a crashing partner (5-min Debug timeout), latency/loss, two-PC clock skew, Seamoth/Prawn hand-overs, a long frame on the *pilot's* game, the hull pushing a swimming player. |
| 2026-11-02 | Suite run (partial) | `tools/run-suite.ps1` on merged `coop` @ `41c4ab03` (S1–S14 all merged). Stopped on request after the first scenario. | **S6-InteriorStateHolds PASS (14 checks, 50 hatch cycles)**, i.e. S13's controller change didn't break hatch entry. The other 19 scenarios were **not run** on the merged build. |
| 2026-11-02 | Tooling: windowless games, parallel rigs | `dual-client.ps1 -Windowless` (Unity `-batchmode`, started without the launcher; `$env:NITROX_WINDOWLESS` reaches scenario restarts, `run-suite.ps1 -Windowless`) and `-Rig 1..9` (port 11000+n, `--data-path` data folder, own game folder of junctions + 270 MB copies; `tools/rig.ps1`, per-rig `game-lock.ps1`, `bridge.ps1 -Rig`). Instant launch takes `--instantlaunch-port`. Debug-only game changes for batch mode: `BehaviourLOD_CheckVisibility_Patch` (camera-frustum visibility, since `Renderer.isVisible` is always false), sound muted, bridge `screenshot` renders the main camera. `-Stop` waits and stops only the recorded processes; scenario helpers no longer wait for every Subnautica process to exit. | Measured: CPU 0.35–0.4 cores vs 1.5, GPU 0.3 % vs 9 %, RAM and VRAM unchanged (`-nographics` ignored), so RAM fits one two-player rig on the dev PC. S3 PASS windowless on rig 1; rigs 1 and 2 ran at once; LOD patch checked on creatures; screenshot checked; `dotnet build` clean, `dotnet test` 317 pass, 13 skipped. **S13 windowless: 3 runs, 2–5 of 63 path checks fail** when the observing game drops to 9–25 fps for seconds; not compared with a windowed S13 on the merged build, so S13 stays windowed. Other scenarios not run windowless. Details: TESTING.md "Windowless games and parallel rigs". |
