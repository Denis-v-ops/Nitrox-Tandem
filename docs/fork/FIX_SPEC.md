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
  ahead of where the players are in the story.
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
   → S3 (small build) → S7 step 1 (ghost cell entries; pure server logic, L1-testable). The players can start the run on
   this build.
3. **Phase 2:** S1 (#2881). This is a **one-way save-format change**, so back up the world first.
4. **Phase 3, builds (after S0.4, the scenario runner):** S2 → S7 → S6 → S5b → S10.
   S3 must be in before the players reach the Disable-Gun terminal. S10 is the biggest and least urgent.

Merge-order facts from a trial merge of all candidate PRs on `62ef043a` (no build):
- Everything merges cleanly except **#2881 × #2899**, which both add a constructor parameter to
  `Nitrox.Server.Subnautica/Models/GameLogic/EscapePodManager.cs`. Keep both.
- **#2867 merges cleanly but breaks #2902 at runtime** (see S4).
- Network type indices come from type names, so **the server and both players must run the same build**.

---

## S0: Test rig (prerequisite, not a bug)

See [TESTING.md](TESTING.md#build-order-spec-item-s0). Minimum before Phase 1: S0.1 (one-command dual client through the
existing Debug-only `Nitrox.Launcher.exe instantlaunch <save> A B`), S0.5 (save → load round-trip test helper) and
S0.6 (`/report` bundle). Minimum before Phase 3: S0.4 (scenario runner).

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

- #2761 vehicle movement regression: probably fixed by #2864. **Phase 0.**
- Possible item or vehicle loss: #2768 (2nd Seamoth despawns 1st), #2448 (lockers vanish in Cyclops), #2538
  (eggs vanish in containment).
- #2800 birds doubling per restart.

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
