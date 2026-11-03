# MultiNaughtica spec: what we take over from the other co-op fork

[MultiNaughtica](https://github.com/Alexander1170/MultiNaughtica) is another Nitrox fork for co-op, GPLv3 like ours.
This spec lists what we take from it (fixes M1–M6, features M7–M9), what we don't, and why. It is separate from
[FIX_SPEC.md](FIX_SPEC.md) and [COOP_FEATURES_SPEC.md](COOP_FEATURES_SPEC.md) so it doesn't collide with work in
progress there; where an item overlaps one of theirs, it names it (S4, S15, B2, B3, F2, G3, G4, …).

**Review basis:** MultiNaughtica `main` @ `99428c8` (2026-11-02), one squashed commit on upstream `7815b4e4`
(2026-09-28), about 135k lines, mostly new vehicle modules, Cyclops crew stations and player models. Four read-only
code reviews (Cyclops/bases/power, creatures, story/player state/movement, restart and saving) compared it with `coop` @
`ddf06755`. Nothing was built or run. To diff it yourself: clone it and run `git diff 7815b4e4 main -- <paths>`.

**How to treat their code:** a lead, not proof. Nearly all their tests cover pure `*Rules` helpers; none runs the real
server or a save round trip, and the repo records no in-game results. Each item gets our own test that fails without
the fix (L2 or a windowless scenario), as for any FIX_SPEC item. Port lines, not files: most of their processors and
patches also call their crew-station, tow, rider-bar or drone code. Credit MultiNaughtica in the commit message of
anything ported.

**Evidence tags:** *confirmed* = read in our code or the decompiled game during this review and re-read since.
*Hypothesis (review)* = found by a reviewer reading both codebases and the decompiled game, not re-read; check it
before coding. A group is a tier, not a contract: drop a line whose cause doesn't hold up, and say so in the Log.

## Decisions (2026-11-02)

| Question | Decision |
|---|---|
| Fixes for bugs their work exposed in our code | **All four groups:** M1 Cyclops, power and building; M2 story and progress; M3 joining and save safety; M4 creature attack gaps |
| Bigger fixes | **M5** damage from both players adds up (#2903 + their hand-over grace); **M6** movement timestamps as `double`. Not chosen: their base power, the server-side Sunbeam landing-site decision |
| Features | **M7** saving indicator, **M8** crafting from lockers, **M9** ping aim. Not chosen: custom player models |
| Crafting from lockers | **Server option `CraftFromLockers`, on by default** |

## Overview and order

| ID | Item | Size | Overlaps | Branch |
|---|---|---|---|---|
| **M3** | Joining and save safety | S | FIX_SPEC Phase 0 (world backups) | `fix/m3-join-save-safety` |
| **M1** | Cyclops, power and building | S–M | FIX_SPEC S1, S7; COOP B2 | `fix/m1-cyclops-power-building` |
| **M4** | Creature attack gaps | S–M | FIX_SPEC S15a, S15d; COOP D4, B1 | `fix/m4-creature-attacks` |
| **M2** | Story and progress | M | FIX_SPEC S3, S12, S13; COOP G2, G3, G4, D5a | `fix/m2-story-progress` |
| **M7** | Saving indicator on the game's own notice | S | — | `feat/m7-saving-indicator` |
| **M9** | Ping aim | XS–S | COOP F2 | `feat/m9-ping-aim` |
| **M6** | Movement timestamps as `double` | S | FIX_SPEC S13 | `fix/m6-movement-time` |
| **M5** | Damage from both players adds up | M–L | FIX_SPEC S4, S14; COOP D4 | `fix/m5-creature-health` |
| **M8** | Crafting from base and Cyclops lockers | M–L | COOP B2; FIX_SPEC S7 | `feat/m8-locker-crafting` |

**Order:** cheapest safe wins first. M3 first: XS–S, server logic testable headless, and its start backups protect
saves before the items that change them. M1 next (its Cyclops bug can kill a player). M6 only after its open question
is answered. M5 and M8 last: largest, and both touch code other branches change (S14/D4 patches, B2 crafter locks).
Game-rig scenarios run once at the end, after all nine are implemented ("Testing" below).

## M1: Cyclops, power and building (S–M)

- **A Cyclops destroyed by damage blows up early for the partner.** *Confirmed:* `CyclopsMetadataExtractor` sends
  `isDestroyed = subRoot.subDestroyed || health <= 0f`, but the game waits 18 s after the killing blow
  (`game:SubRoot.OnTakeDamage`, `Invoke("DestroyCyclopsSubRoot", 18f)`). The partner's
  `CyclopsMetadataProcessor.SetDestroyed` runs `DestroyCyclops` at once, which ejects or kills a player inside
  (*hypothesis (review)*), and the server stores the Cyclops as destroyed. Their fix, taken as a set: the extractor
  sends only `subDestroyed`; a `CyclopsDestructionWatchdog` lets the lock holder destroy a Cyclops still at 0 HP after
  25 s (the player who took the hit left); the beacon spawns on the game whose countdown fired. FIX_SPEC S1's scenario
  never destroyed a Cyclops by damage. Tests: L2, metadata at health 0 keeps `IsDestroyed` false; windowless, kill the
  Cyclops with the partner aboard.
- **Battery charge reaches the server one step late.** *Confirmed:* `Battery_charge_set_Patch` is a Prefix, so the
  extractor reads `_charge` before the setter stores `Mathf.Min(value, _capacity)` (`game:Battery.cs:30`). A battery
  that drops to 0 is never reported. Fix: send `BatteryMetadata` built from the clamped new value. May explain the 0.01
  drift `bridge.ps1 diff` showed in FIX_SPEC S8 (*hypothesis*).
- **A power-cell swap leaves the partner's vehicle without power.** *Hypothesis (review):* the game's swap removes the
  old cell without a packet, then adds the new one; on the partner the new cell arrives while the slot is still full
  (`StorageSlot.HasRoomFor` refuses), and the old cell's pickup then empties the slot. Their
  `InstalledBatteryEntitySpawner` takes the old cell out first.
- **A building resync never brings back a base piece the game lost.** *Hypothesis (review):* in
  `BuildingResyncProcessor`, a failed `Find` calls `overwrite(null, entity)`, which throws (only logged), then
  `RemoveAt(i)` drops the entity before the "spawn missing" loop. Their pairing fix plus `BuildingResyncPairingTest`.
- **A deconstructed loose `Constructable` is half removed.** *Hypothesis (review):* `BuildingManager` calls
  `entityRegistry.RemoveEntity` without S7's cell-index cleanup and without releasing the lock. Visible only as a
  warning today.
- **Vehicle lights and the Cyclops thermal reactor run on both games.** `ToggleLights.UpdateLightEnergy` and
  `SubRoot.UpdateThermalReactorCharge` run wherever the vehicle is loaded: double drain, double charge
  (*hypothesis (review)*). One-line Prefixes that check the lock.
- **Build bot crash on a remote Mobile Vehicle Bay deploy.** *Hypothesis (review), read in decompiled code:* the
  `ConstructorBuildBot.launch` setter uses `assignedHoverLoopSound`, which only `Start` assigns.
- **Undocking is announced before the game decides.** *Hypothesis (review):* our patch sends `VehicleUndocking` when
  the lock is granted, but the game can still refuse the click (no undocking clearance, another cinematic, a tool that
  can't be put away; `game:DockedVehicleHandTarget.cs`, `CinematicModeTriggerBase.cs`): the partner's bay opens and the
  lock stays held. Theirs sends from `VehicleDockingBay.OnUndockingStart`; leave out their rider-bar lines.

**Findings (2026-11-02):** implemented on `fix/m1-cyclops-power-building`, not verified in game (scenario
`tools/scenarios/M1-CyclopsPowerBuilding.ps1` written, not run).
- **Cyclops blows up early: held, fixed.** The killing blow's metadata (`LiveMixin_TakeDamage_Patch` →
  `BroadcastDefaultTookDamage`, lock holder only) said `IsDestroyed` at health 0; the partner's `SetDestroyed` ran
  `DestroyCyclops` at once, and the server stored the Cyclops as a wreck. S1 (#2881) only changed the extractor's input
  type; `health <= 0f` predates it. Correction: `DestroyCyclops` → `Player.TryEject` only ejects a *pilot*; anyone else
  aboard is killed by `CyclopsDestructionEvent.Update` when the interior effect passes them, so the partner died 18 s
  early. Now `IsDestroyed` = `subDestroyed`, or `DestroyCyclops` ran and the model swap is pending
  (`IsInvoking(SwapToDamagedModels)`: its power-down turns shield, sonar or silent running off, and their patches extract
  in those 2.5 s); the decision is `CyclopsDestructionRules`. The partner hears the abandon-ship line at 0 HP (its
  `OnTakeDamage` is blocked for non-holders). `CyclopsDestructionWatchdog` (added from `SetHealth`, so also on spawn)
  lets the lock holder run `DestroyCyclopsSubRoot` when a Cyclops is still at 0 HP 25 s later. The beacon is kept by the
  game that isn't replaying (`PacketSuppressor<EntitySpawnedByClient>`, as the loot), not by the current lock holder.
  Game quirk kept: `SubRoot.OnTakeDamage` starts the countdown only when neither the hull-low nor the hull-critical line
  fires in that hit, so one hit from above 50 % to 0 never destroys it (vanilla too). Open, unverified: a Cyclops saved
  at 0 HP (the hitter left, the server restarted) is a wreck on load through the game's own
  `CyclopsDestructionEvent.Start` (no loot, no beacon, the server keeps it); a rejoiner holding its transient lock may run
  `SubRoot.OnTakeDamage` from `SyncRemoteHealth` and start the game's own countdown.
- **Battery one step late: held, fixed.** `BatteryChargeRules`: sent at a whole-number change or when it becomes empty,
  with the value the setter stores (`Mathf.Min(value, _capacity)`, no lower limit, `game:Battery.cs`).
- **Power-cell swap: held (code reading), fixed.** `EnergyMixin.Select` removes the old cell (no packet), adds the new
  one (`OnAddItem` → `EntitySpawnedByClient`), then picks the old one up (`PickupItem`). On the partner the new cell
  spawns at once when the power cell prefab is cached (`SpawnSyncSafe`, `StartCoroutine` runs to the first yield) while
  the old cell still fills the slot (`game:StorageSlot.AllowedToAdd`); the pickup then destroys the old cell and
  `Pickupable.SetInventoryItem(null)` empties the slot. With an uncached prefab the order is right; not seen in game. The
  spawner now takes the old cell out first (its power-off broadcast suppressed) and logs a refused install.
- **Resync never brings a base back: held, fixed.** `PairWithComponents` leaves out entities without a component, so the
  spawn loop gets them. COOP B2 had recorded the same gap.
- **Deconstructed loose constructable: held, fixed; lock part partly.** `entityRegistry.RemoveEntity` left the cell and
  id-index entries (S7's `GetEntities` dropped them later with a warning). Now `WorldEntityManager.TryDestroyEntity`
  (S7's path) and `SimulationOwnershipData.RevokeOwnerOfId`. Open: B2's `ConstructionLocks.ReleaseWhenDone` asks for a
  transient lock once the object is gone, which recreates a lock for the removed id (also for ghosts and modules, where
  nothing revokes); harmless (nothing simulates it), not changed here.
- **Lights and thermal reactor: partly.** Not a double drain: `BatteryMetadata` sets the charge, so same-direction
  changes just follow the faster game. Opposite directions fight and the charge sticks at a whole number (by reasoning,
  not measured: with the old prefix's stale value it stuck even when both drained). Lights: only the vehicle's lock holder runs `UpdateLightEnergy`
  (vehicles are global roots, so a connected player always holds it; `ToggleLights.Update` has no LOD gate), as
  `Vehicle_UpdateEnergyRecharge_Patch` does; tools keep the game's behaviour. Thermal reactor: "only the holder" would
  stop the charge when the last pilot (the lock follows piloting, not the players aboard) is away and `SubRoot.Update`
  stops at LOD Minimal, while the partner stays aboard. So the reactor skips only while another player pilots that
  Cyclops (the pilot's game drains the engine and charges). Open: holder-only drains left on while nobody pilots
  (silent running, shield) near a vent can still fight the partner's charge. Not covered by the scenario.
- **Build bot crash: held in code, timing unverified.** The `launch` setter skips a bot whose hover sound isn't assigned
  yet (only `Start` assigns it, and `Start` ends with `launch = false`). Whether Unity really runs `Constructor.Update`
  before the new bots' `Start` in the frame the partner's deploy arrives is only visible in game.
- **Undocking announced early: held, fixed.** `VehicleDockingBay_OnUndockingStart_Patch` sends the start (only
  `DockedVehicleHandTarget.OnStartCinematicMode` calls it, after `StartCinematicMode` accepted; the partner's processor
  doesn't). The click patch asks no lock without undocking clearance or a valid target, and after a refused click
  (`vehicle_docked_param` still set) gives the lock back as transient. Before, a player who already held the exclusive
  lock undocked without announcing it. Left out: their `DenyOwnershipHand`.
- Dev bridge: `UnityEngine.Object` and `IOut<T>` arguments (TESTING.md).

## M2: Story and progress (M)

- **Sea Emperor babies.** *Confirmed:* `SeaEmperorBaby_Start_Patch` returns when the baby has a parent;
  *hypothesis (review):* `DefaultWorldEntitySpawner` parents server-spawned babies to the cell root before `Start`, so in
  the partner's game they never swim to the mother. `SeaEmperorBaby.Teleport` destroys the baby without telling the
  server, so the babies come back after every restart; their `Teleport` patch sends `EntityDestroyed`. Also take the
  incubator metadata idempotency. Their 30 s cleanup of animation babies rests on an unverified claim; leave it out
  unless reproduced. Feeds COOP G3.
- **Radiation for a joining player.** *Confirmed:* `RadiationLeakEntitySpawner.RecalculateRadiationRadius` takes
  `registeredLeaksFixTime.Max()`, so one fixed leak sets `radiationFixed = true` as if all were fixed, and the game then
  skips `leaksFixedGoal` (*hypothesis (review)*). Take `RadiationRadiusModel.Compute` and the `IsProgress` guard (client
  and server `EntityMetadataUpdateProcessor`), not their clock-based recompute (needs their `TimeManager` rewrite).
  Blocks COOP G4.
- **Respawning in the pod leaves the player outside it on the server.** *Hypothesis (review):*
  `EscapePod_RespawnPlayer_Patch` sends the pod, then `Player_MovePlayerToRespawnPoint_Patch` sends "no sub root" and
  `SubRootChangedPacketProcessor` clears the parent. Their `PlayerParentRules` in both processors; their processor test
  `Process_LifepodRespawnPackets_LeaveThePlayerInTheLifepod` fails without it. Their `JoiningManager.CreatePlayerEntity`
  also parents the new player's transform, which touches FIX_SPEC S13's "Finding, not fixed".
- **Story goals that arrive while a player loads are lost.** *Hypothesis (review):* `StoryGoalExecutedClientProcessor`
  dereferences `StoryGoalScheduler.main`, and packets are processed during scene loading. Their per-packet wait with a
  session counter. `AuroraAndTimeUpdate` has the same gap. Unchecked: whether the server sends goals to a loading
  session at all.
- **Precursor key doors and teleporters.** *Hypothesis (review), contradicts COOP_FEATURES_SPEC §12.1 "fine today":*
  `PrecursorGlobalKeyActivator` opens doors through `IsGoalComplete(doorActivationKey)`, a goal completed outside
  `StoryGoal.Execute` and never sent, so doors nobody loaded in that session stay shut after a restart. Their
  `StoryGoalManager_OnGoalComplete_Patch` plus an `ExecuteDepth` guard sends it; filter out `Trigger_PDAIntroBegin/End`
  (what they drive lives in asset data). Teleporters ride on it: their `Nitrox_TeleporterActive:<id>` goal persists an
  active pair, and a guard in `OnActivateTeleporter` stops ours broadcasting `isOpen=false` for every other loaded
  teleporter. COOP D5a's suppression still applies. Skip their radio "played goal" fallback.
- **Gun-disable leftovers.** The server drops `RadioSunbeam4` from the radio queue as vanilla does, and the partner's
  gun terminals close (FIX_SPEC S3's optional step 2). Check against COOP §11's "Sunbeam radio waits for both" (G0).
- **Two players scanning one fragment.** `PDAScanner.fragments[...] = 1f` instead of `Add`, which throws on a key
  the partner already added.
- **Rocket progress overwritten.** `RocketMetadata.Merge` for two players' reports, and the `TryGetObjectFrom` change
  so a launch reaching a loading client doesn't throw. Not their "everyone aboard" transpiler: COOP G2 plans a server
  gate.
- **Lifepod beacons:** compare unlock keys ignoring case, as the game does (`SetupTrackers` uses `List.Contains`).
  Whether any key differs in case is asset data. Not their 30 s beacon watchdog.

**Findings (2026-11-02):** implemented on `fix/m2-story-progress`, not verified in game (scenario
`tools/scenarios/M2-StoryProgress.ps1` written, not run). No new metadata or `ProtoInclude` tag; no save format change.
- **Sea Emperor babies: held.** Our `DefaultWorldEntitySpawner.SetupObject` parents a baby of the server to the cell
  root (`LargeWorldEntity.Register` → `game:CellManager.RegisterCellEntity`) in the frame it's made; `Start` runs a
  frame later, so the old "has a parent" test skipped every one. The hatching game's own baby is disabled for the egg
  cinematic (`game:SeaEmperorBabyCinematicController.SetCinematicMode`), which on its end enables it, moves it to the
  cell root and calls `SwimToMother` before `Start` runs: it already has a swim target, which with "under an egg" now
  decides (`SeaEmperorBabyRules`; their `HatchedHereBaby` marker not taken). The `Start` patch waits up to 60 s for the
  Emperor and wraps the attach point id. `game:SeaEmperorBaby.Teleport` destroys the baby (from
  `SeaEmperorTeleporterTrigger`), unreported; vanilla never saves babies (`OnProtoDeserializeObjectTree` destroys them),
  Nitrox keeps them as entities. `SeaEmperorBaby_Teleport_Patch` sends `EntityDestroyed`. Incubator: held, the hatch
  sends (powered, hatched) after (powered, not hatched), so `SetPowered` (sound, goal) ran twice, and a second `Hatched`
  would restart every egg (`game:Incubator.OnHatched` → `StartHatch`): both now only once. Their 30 s animation-baby
  cleanup not taken. **Risk:** a save whose babies were never removed: on the next load they swim to the mother and the
  interaction plays once more, then they teleport and are removed. **Unverified:** that a copy the partner doesn't
  simulate really swims to the mother (their notes say the Emperor reacted on the second screen). **Open risk, bigger
  (pre-existing, not fixed):** every cell load makes a new incubator object (`hatched` false), and the spawner applies
  its metadata, so `OnHatched` runs on every load. That is harmless only if the metadata comes before `Incubator.Start`
  makes the eggs (`Entities.SpawnBatchAsync` applies it right after the spawn coroutine; whether a frame passes is
  unverified). If `Start` ran first, every egg hatches again: temporary babies for most games, but five new real babies
  from a game holding the platform's lock (`IncubatorEgg_HatchNow_Patch`), which with the new `Start` patch swim to the
  mother and replay the interaction. Rig check: hatch, unload and reload the incubator's cell, count the server's
  `SeaEmperorBaby` entities. The fix then: on a spawn set `hatched` and the eggs' `SetHatched` (the game's load path),
  `OnHatched` only for a live update.
- **Radiation for a joiner: held.** `RecalculateRadiationRadius` took the latest fix time of any leak as "all fixed";
  `game:LeakingRadiation.NotifyLeaksFixed` triggers `leaksFixedGoal` only while `!radiationFixed`, and `Update` grows
  the radius while any leak leaks. `RadiationRadiusModel.Compute` taken. **The `IsProgress` guard** protects against a
  stale lower report: leaks are created at 0 (`WorldService.InitNewWorld`) and only welded, so a report that doesn't
  raise the health is older (two welders: the 95 after the 100). The server stored it, which set `FixRealTime` back to
  -1 (persisted: the leak leaked again after a restart), and relayed it, and the partner's `SyncRemoteHealth` called
  `TakeDamage` (the leak leaked again at once). Taken on both sides. Not taken: their clock-based recompute,
  `SpawnedByServer`, the spawner's `ApplyMetadata`.
- **Respawn in the pod: held.** `game:Player.MovePlayerToRespawnPoint` calls `EscapePod.RespawnPlayer` (our Postfix
  sends the pod), `SetCurrentSub(null)`, `currentEscapePod = pod`, then our Postfix sends "no sub root", which cleared
  the parent and `SubRootId`. `PlayerParentRules` in both processors; also "no escape pod" (the game drops the pod by
  distance, `game:Player.cs:1014`) no longer takes a player out of a base after a warp. **FIX_SPEC S13 finding:
  taken**, it's clearly right: `NitroxTransform`'s world-pose setters convert into the parent's space and a restart
  links the saved local pose under the parent; a new player's `Position` is the default (0, 0, 0), so the transform is
  parented without keeping the world pose (the pod's origin until the first movement), a re-created one keeps it.
- **Story goals while loading: partly.** Held: the server broadcasts to every connected session from the connection on
  (`LiteNetLibServerService.contextBySessionId`, filled in `ConnectionRequest`), and `Multiplayer` processes packets from
  the main menu on (`JoinServerBackend.StartMultiplayerClientAsync`): a goal in the menu threw
  (`StoryGoalScheduler.main` null), one during the scene load ran on a half-loaded game. Didn't hold: "lost": the server
  stores a goal before relaying it and builds the joiner's initial sync only once the scene has loaded
  (`Multiplayer.LoadAsync` → `StartSession` → `JoinSession`), so it came back with the sync. Left: a goal between the
  snapshot and the sync steps was applied before them (`PDALog.Deserialize` replaces the log, `SetupTrackers` re-adds
  tracker goals), and an `AuroraAndTimeUpdate` was overwritten by the older snapshot. Built `InitialSyncDeferral` (one
  per session scope): both processors hold their packets until the initial sync is applied, then run them in order. Not
  their per-packet coroutine and session counter. Side effect: D5a's option now applies to those goals too (before,
  `CoopSettings` still held the defaults).
- **Precursor key doors and teleporters: held (code), contradicting COOP §12.1.** `game:PrecursorGlobalKeyActivator`
  completes `doorActivationKey` with `StoryGoalManager.OnGoalComplete` (never sent) and opens on `OnEnable` from
  `IsGoalComplete`. A door that is loaded when the key goes in syncs as `PrecursorDoorwayMetadata`; one nobody had loaded
  never opened for the partner, nor for anyone after a restart. Which doors hang on a global activator is asset data.
  The other direct `OnGoalComplete` callers: `IntroLifepodDirector` (`Trigger_PDAIntroBegin/End`, left out: each player
  has their own intro), `StoryGoalManager.ExecutePendingRadioMessage` ("OnPlay" + key), two Sunbeam console commands
  (Nitrox sends those to the server), and in Nitrox `PlaySunbeamEventProcessor` and `RadioPlayPendingMessageProcessor`
  (now suppressed: every game runs them itself) and D5a's silent path (suppressed already).
  `StoryGoalManager_OnGoalComplete_Patch` sends the rest as Story goals; `StoryGoal_Execute_Depth_Patch` (separate, so
  `StoryGoal_Execute_Patch` stays as it is) keeps `Execute`'s goals out. Side effect: the partner now gets the played
  radio message's "OnPlay" goal, which also covers the case of their "played goal" fallback (their code not taken).
  **Teleporters:** `game:TeleporterManager.activeTeleporters` is single-player save data; `PrecursorTeleporter.Start`
  opens from `GetTeleporterActive`. The `Nitrox_TeleporterActive:<id>` goal (`TeleporterGoalKey`, `TeleporterPairs`)
  is reported from `TeleporterManager.ActivateTeleporter`, switches the pair on in the partner's game, and goes into the
  game's set before a teleporter's `Start` (`PrecursorTeleporter_Start_Patch`; not a `GetTeleporterActive` Postfix: it's
  small enough for Mono to inline). `OnActivateTeleporter` runs on every loaded teleporter for every activation; the old
  Postfix broadcast each one's `isOpen` (mostly false), which the partner applied as `ToggleDoor(false)`, fading an open
  portal out. Now only a teleporter that just opened reports, and the processor only opens. No goal key in the game's
  code starts with "Nitrox"; asset keys can't be listed offline (scenario step 1 checks the keys in a fresh world).
- **Gun-disable leftovers: held.** `game:StoryGoalCustomEventHandler.NotifyGoalComplete` drops `RadioSunbeam4` on
  `Goal_Disable_Gun` (both literals in the game code); the server's queue kept it and every join brought it back:
  `StoryManager.RemoveRadioMessagesCancelledBy`. G0's radio lock holds the click without taking the message, so it
  doesn't conflict: once the gun is disabled the message is gone, as in vanilla (`Radio_PlayRadioMessage_Patch.cs`
  untouched). The partner's open terminal: `PrecursorDisableGunTerminal.OnHandClick` doesn't check `gunDisabled`, so an
  open one took a click and replayed the cinematic; it closes now (Postfix on our `NotifyGoalComplete` patch).
- **One fragment scanned by both: held.** `game:PDAScanner.fragments` is a `Dictionary<string, float>`; our
  `StoryManager.ScanCompleted` used `Add`, which throws on a key the game already has. Indexer now.
- **Rocket: held.** A report is the sender's whole rocket (`RocketMetadataExtractor`) and was stored over the last.
  `RocketMetadata.Merge` on the server (the report is relayed as it is: the games only add stages and checks).
  `Rockets.RocketLaunch` used `RequireObjectFrom`, which threw for a game far from the rocket too, not only a loading
  one. Also taken: the processor applies the stage before the elevator (a joiner's rocket starts at stage 0, and
  `UpdateElevator` skips below the gantry, so the joiner's elevator was never set).
- **Lifepod beacons: held as stated, code only.** Our `StoryGoalInitialSyncProcessor.SetupTrackers` checked the
  packet's `List<string>` (ordinal); `game:StoryGoalManager.completedGoals` and `OnGoalUnlockTracker.goalUnlocks` ignore
  case. It now asks the game's set. Whether any key differs in case: asset data, not checked; no test.
- **Tests** (each failed with its production change reverted): L2 `StoryProgressTest`: `LifepodRespawnPackets_LeaveThePlayerInThePod`,
  `NoEscapePod_KeepsASubRoot_AndClearsThePod` (processors), `NewPlayerEntity_KeepsItsWorldPositionAcrossRestart`
  (`JoiningManager`), `StaleRadiationLeakHealth_IsDropped`, `RocketReports_AreMerged` (`EntityMetadataUpdateProcessor`),
  `GunDisabled_DropsTheSunbeamRadioMessage` (`StoryGoalExecutedProcessor`); `DirectlyCompletedGoals_ComeBackAfterRestart`
  is a guard (the server needed nothing new). Unit: `RadiationRadiusModelTest` (the partial-fix case fails with the old
  `Max()` rule), `RocketMetadataTest`, and for the new helpers of game-only lines `SeaEmperorBabyRulesTest`,
  `DirectGoalCompletionsTest`, `TeleporterGoalKeyTest`, `InitialSyncDeferralTest`. Game-only, scenario only: the baby
  `Start` and `Teleport` patches, the incubator guard, the `OnGoalComplete` patch, teleporters, the terminal, the double
  scan, the deferral. Not tested at all: the beacon key comparison, the rocket launch lookup and stage order.
- Files that in-progress or merged-later branches also change: `StoryGoalExecutedClientProcessor.cs` (constructor and
  three lines at the top), `StoryGoalExecutedProcessor.cs` (one line), `JoiningManager.cs` (`SetupNewPlayerEntity` only),
  `StoryGoalInitialSyncProcessor.cs` (the beacon line only), `ClientAutoFacRegistrar.cs` (one line),
  `EntityMetadataUpdateProcessor.cs` (M1 may touch it).

## M3: Joining and save safety (S)

- **A backup at every server start.** Zip of the save's top-level files before the world is read, in
  `backups/server-starts/`, last 5 kept; outside the launcher's restore list and the rolling autosave pruning. Their
  tests use real file I/O. This is FIX_SPEC Phase 0's "set up world backups". It matters more because S1 and S5b are
  one-way save changes (*hypothesis (review)*).
- **A returning player who joins while the world loads is treated as new** (intro, new pod, starting items).
  *Hypothesis (review):* services start together (`Program.cs`), and the network listens before the world has loaded.
  Their reservation processor awaits `WaitForWorldLoadedAsync` and re-checks at join. Test: L2, a `FakePlayer` joins
  while `StartAsync` runs.
- **A name stays "in use" after a crash during loading** until the server restarts: free the reservation when the
  session drops before joining (about 3 lines in `PlayerManager`).
- **A dead session blocks rejoin** until it times out (5 min in Debug): kick a stale session with the same IP and the
  same name. Not their configurable `DisconnectTimeoutSeconds`.
- **An area can stay empty forever.** *Hypothesis (review):* `BatchEntitySpawner` records every batch key, including one
  still generating or failed; a save in that window or a generation exception records the batch with no entities. Drop
  entries whose task started and didn't complete successfully. (Their in-session retry doesn't work:
  `WorldEntityManager.batchRegistrationTasks` caches the failed task.)
- **NTP socket leak.** Every `TimeChange` calls `NtpSyncer.Setup`, which starts a new `NetManager`; `Complete()` returns
  early once done, so nothing stops it. One-shot retry.
- Optional: their Debug `EntityCountCommand` as the repro tool for #2800 (FIX_SPEC verify-first list).

**Findings (2026-11-02):** implemented on `fix/m3-join-save-safety`, not verified in game (scenario
`tools/scenarios/M3-JoinSaveSafety.ps1` written, not run).
- **Start backup: built.** Both other listers only look at files directly in `backups/`: the rolling pruning
  (`SaveService.PruneBackups`, names with "Backup - ") and the launcher's restore list (`BackupRestoreViewModel.GetBackups`,
  names starting with "Backup - "); the launcher's restore deletes only the save's top-level files. So the zips go to
  `backups/server-starts/ServerStart - <time>.zip` (`ServerStartBackups`), made at the top of `WorldService.StartAsync`,
  before `UpgradeSave` rewrites files. Not made for a new world or when `MaxBackups` is 0 (no backups wanted). A failed
  backup is logged and the server starts. The "one-way save change" motivation holds for S1 (FIX_SPEC Phase 2); S5b
  not checked.
- **Join while the world loads: held, fixed.** `ServicesStartConcurrently` (`Program.cs`); `LiteNetLibServerService`
  (registered in `AddPackets`, before `AddWorld`) listens at once; `ReservePlayerContext` decides "new" from
  `allPlayersByName`, which only `WorldService` fills. The L2 test holds the world load and showed the server answering
  RESERVED before the world was loaded, and the returning player joining with `FirstTimeConnecting` true. Fix: the
  reservation processor awaits `WorldService.WaitForWorldLoadedAsync` (cancelled when the host stops, so a failed load
  can't hang the shutdown); `PlayerManager.CreatePlayerData` re-checks at join and rebuilds a "new" reservation of a now
  known name as the saved player (id, game mode, intro completed). A reservation before the load could also have made the
  load throw on the duplicate name (`AddSavedPlayer`), and a new player's `PeerId` could collide with a saved one.
- **Name "in use" after a drop before joining: held, fixed.** `PlayerManager.OnEventAsync` freed the name only for
  joined players; a reservation's name stayed until restart.
- **Dead session blocks rejoin: held, fixed.** Sessions are keyed by address and port, the name check refuses the second
  session until the old one times out (30 s Release, 5 min Debug). The reservation processor now kicks another session
  holding the same name from the same IP address (IPv4-mapped addresses normalised), removes it, and waits up to 3 s
  for the name. Same address and same name is taken as the same person: on a public server that would let someone on
  the same address push a player out; accepted for a two-player private server.
- **Empty area: held, and wider.** `SerializableParsedBatches` listed every key, including running and failed batches.
  Now only batches whose task finished (`BatchLoadTasks.ListGenerated`), and the save leaves out batches whose entities
  aren't registered yet (`WorldEntityManager.GetGeneratedBatches`, read before the entities). Restored batches get
  already-created tasks, or the filter would drop every restored batch at the first save. A failed task was cached in
  **both** `BatchEntitySpawner.batchLoadTasks` and `WorldEntityManager.batchRegistrationTasks`; each is now forgotten on
  failure (`BatchLoadTasks.GetOrStart`), so the next visit retries. Before, every later `CellVisibilityChanged` naming
  that batch threw, and the player got no `SpawnEntities` reply for any cell of that packet. A real failing batch isn't
  reachable headless; only unit tests cover the failure path. Open: generation picks prefabs with `XorRandom` from
  `RandomFactory`, not the per-batch generator, so a batch generated twice isn't identical (their fork changed that;
  not taken here). Open: if registration throws after a successful generation (no known cause), the batch stays
  recorded, as before.
- **NTP socket leak: held, wider than stated.** Client: `TimeManager.ProcessUpdate` calls `AttemptNtpSync` for every
  `TimeChange` (every 60 s) while the game has no NTP correction and the server has one. Server: `TimeService` retries
  every 60 s while offline. Each `Setup` opened a `NetManager` without closing the last, and after the first round
  `Complete()` returned at once, so no later socket was closed and no later callback ran: the server's retry timer never
  stopped, even after a success. Now `Setup` stops the previous round and starts a fresh one, `Complete()` ends each
  round (sockets are stopped outside the lock: stopping waits for the socket's threads), and the client starts no round
  while one is running.
- **`EntityCountCommand`: taken** (Debug only, about 35 lines): `entitycount [filter]`.
- Edits outside the item's files: `JoiningManager.cs` untouched; `NitroxClient/GameLogic/CorrectedTimeManager.cs` (one
  guard). Test helpers: `FakePlayer.ConnectAsync`/`ReserveAsync`/`CompleteJoinAsync`/`Crash`,
  `ServerHarness.ConnectAsync`/`RestartHoldingWorldLoadAsync`/extra server arguments.

## M4: Creature attack gaps (S–M)

- **Reaper grab** shows in one game only. `ReaperGrabVehicle` / `ReaperReleaseVehicle` to players who can see it;
  the others call `GrabSeamoth`/`GrabExosuit` suppressed, with a 20 s fallback release. Temporary state, nothing to save.
- **FIX_SPEC S15a, Crabsquid EMP:** every game replays the vanilla blast at its own copy, so `EMPBlast.OnTouch` reaches
  that game's vehicles, tool and Cyclops. Unverified: if the partner's body carries a `Player` tag, the simulating
  player's own tool gets disabled too.
- **FIX_SPEC S15d, Bleeder:** the victim's game takes the Bleeder's exclusive lock, so the whole attack runs vanilla
  there; no packet. Sounder than S15's attach-point idea. Unverified: `AttachAndSuck.OnCollisionEnter` on a remotely
  controlled copy. Add only `TechType.Bleeder` to the whitelist (theirs adds three more without a reason; ours lists
  Spinefish and Shuttlebug twice).
- **Crash fish disappear** ([#2673](https://github.com/SubnauticaNitrox/Nitrox/issues/2673)). The game's
  `CrashHome.OnDestroy` destroys the fish when the cell unloads, and our `CrashHome_OnDestroy_Patch` then deletes it on
  the server (their world: 1 fish left for 66 homes). Their client gate also waits for the home's saved state before the
  20-minute "no fish" timer is broadcast. *Hypothesis:* `CrashHome_Spawn_Patch` sends the fish in world coordinates with
  the home as parent and the loader attaches it unconverted (the reviews disagree). Take the client part; their
  load-time save normalisation only behind M3's start backup.
- **Creature AI sends a packet on every update** (`AI.cs`) and calls `newAction.GetType()` on a null action
  (`Creature.ChooseBestAction` can return null). Send changes only, plus a refresh every 5 s.
- **Crash fish blast next to the partner's vehicle** passes no attacker, so COOP D4's relay misses it: send
  `CreatureAttackedVehicle` from our `Crash_Detonate_Patch` with the falloff we already compute for bodies.

**Findings (2026-11-02):** implemented on `fix/m4-creature-attacks`, not verified in game (scenario
`tools/scenarios/M4-CreatureAttacks.ps1` written, not run). No save format change; three new packets (all three
programs must run the same build). No metadata tags used.
- **Reaper grab: held, fixed.** The Reaper is in `AI.syncedCreatureWhitelist`, so every game's copy is aggressive and
  `ReaperMeleeAttack.OnTouch` grabs with that game's copy only (`game:ReaperMeleeAttack.cs`). New `ReaperGrabVehicle` /
  `ReaperReleaseVehicle`, sent to the players who see the Reaper (and the vehicle); the others grab with the packet
  suppressed and replace the game's 8–13 s timer with a 20 s fallback. Who does what: the pilot's game (vehicle lock)
  moves its vehicle to its Reaper's mouth (`ReaperLeviathan.Update`; a Prawn suit gets `cinematicMode` as in single
  player) and applies the grip's 15 per second (`DamageVehicle` passes no dealer, so the other games drop it in
  `LiveMixin_TakeDamage_Patch`: no double damage, and their `ReaperLeviathan_DamageVehicle_Patch` isn't needed); the
  other games stop the vehicle's movement replicator while it is held (existing patch). A release always comes: the
  grabbing game's timer or electrical/poison damage in any game sends it; a destroyed vehicle is let go by each game's
  own `Update`; a killed Reaper is disabled by `Creature.OnKill` and lets go in `OnDisable` wherever the death arrives
  (S4); a game that left or unloaded the Reaper sends nothing (`OnDisable`, `isActiveAndEnabled` false there,
  *unverified in game*) and the others' 20 s fallback lets go. Every release cancels a leftover timer.
- **S15a, Crabsquid EMP: held; the "Player tag" question can't be answered from code, guarded.** `EMPAttack` is a
  creature action, so `CastEMPBlast` runs only in the simulating game. New `EmpFired` (to the players who see the
  creature): their copy casts the same blast, which disables that game's vehicles, held tool and Cyclops
  (`game:EMPBlast.OnTouch`). Overlapping hits don't extend the disable (`DisableElectronicsForTime` takes the max).
  The partner's body (a copy of the player's `body` child, `LocalPlayer.CreateBodyPrototype`) has our rigidbody and a
  trigger collider on the Useable layer, which `OnTouch` lets through (`game:OnTouch.cs`), so blasts do touch it.
  Whether the copy carries the `Player` tag is asset data, not read. `EMPBlast_OnTouch_Patch` skips other players'
  bodies: if tagged, the blast would disable `Inventory.main`'s held tool, the local player's, when only the partner was
  in range; if not, the body has no `EnergyInterface`/`EnergyMixin` and skipping changes nothing. The scenario reports
  the tag. The partner sees no wind-up (animation, sound) before the blast; cosmetic, left out.
- **S15d, Bleeder: held, by reading the code.** `AttachAndSuck.OnCollisionEnter` is a physics callback that runs on
  every copy. A copy another game simulates still swims (`RemotelyControlled` drives its `SwimBehaviour`, its rigidbody
  stays non-kinematic) with its collider on, so it collides with the local player and writes the contact (target
  occupied, which also spared that player from every other Bleeder in that game); the attack itself (`Evaluate`,
  `StartPerform`, `Update` behind `active`) runs only where it is simulated. The partner's body collider is a trigger,
  so no copy ever attaches to a body. Ported their claim (`BleederClaims`, `BleederClaimRules`, the two `AttachAndSuck`
  patches, two `OnLockLost` lines in `SimulationOwnership`): the victim's game takes the exclusive lock; refused, it
  detaches; granted, the attack must start within 5 s; on detach the lock goes back to transient after 0.5 s (later
  while the Propulsion Cannon holds it); a lost lock detaches. Adapted: only contacts with the local player
  (`targetLiveMixin == Player.main.liveMixin`) claim; a non-simulating copy drops any other contact. Whitelist:
  `TechType.Bleeder` added; Spinefish and Shuttlebug really were listed twice (a `HashSet`, so harmless), removed. Not
  taken: their other three whitelist entries, `Bleeder_OnHit_Patch` (needs M5's `CreatureHealthManager`).
  **Superseded on the merge with `coop` (2026-11-03):** `coop` got its own S15d meanwhile (COOP_FEATURES_SPEC B1,
  verified in game with `S15d-BleederOnPartner.ps1`): only the simulating game lets a bleeder pick a target, it
  latches onto the partner's body, and the other games mirror the attach (`BleederAttachChanged`). Both claim the same two
  `AttachAndSuck` patches, and with S15d's Prefix a copy never takes a contact, so this claim would never run. Kept
  S15d and B1's whitelist line; removed the claim, its rules and test, the `OnLockLost` lines and M4's whitelist line
  (the duplicate removal stays). The claim was never checked in game (no M4 scenario case covers bleeders).
- **Crash fish disappear: held, fixed. Transform hypothesis: held, measured headless.** `CrashHome_OnDestroy_Patch`
  (the `EntityDestroyed` on unload) is deleted; a fish that dies is still sent by `LiveMixin_Kill_Patch`. Their client
  part taken: the gate (`CrashHomeMetadataGate`, 20 s grace) before the 20 minute timer is started and sent and before a
  fish is made; no second fish for a home that has one (`CrashHome_Spawn_Patch` sets the number back to -1 and sends
  it); `CrashEntitySpawner` destroys an object it can't place and replaces a second fish object of a home. Transform:
  the maker's game sends the fish with `ToWorldDto` (world position) and the home as parent; in a session the server
  never sets `Transform.Parent` (`EntityRegistry.AddToParent` only links the entities), but the loader
  (`WorldService`) hangs every child under its parent without converting, and BinaryPack sends `NitroxTransform.Parent`
  with the entity. In the L2 test the fish was 134 m from its home after a restart, in another cell, and the client
  (which places it at `Transform.Position`) received it there. Fix: the loader leaves the transform of a crash fish of a
  `CrashHome` unparented; no saved value changes. **Their load-time save normalisation (`CrashHomeStates`): left out.**
  It rewrites existing saves' metadata (homes at -1 without a fish set to 0, homes with a fish and a timer set to -1,
  `CrashHomeMetadata` removed from non-home entities) and deletes surplus fish entities. The client part already heals
  both home states in play (an empty home at -1 starts the game's 20 minute timer; a fish with a timer is set back to -1
  when the timer runs out), and the stray metadata comes from regenerated batches (M3's open point), not from this item.
  Open: a fish that moved after a restart on an older build saved a local position (the wrong parent turned position
  updates into local ones), which this fix reads as a world position, so that fish would appear near the world origin;
  rare, since older builds deleted the fish whenever its area unloaded. Not reproduced.
- **AI packet on every update: held.** `Creature.ChooseBestAction` (`game:Creature.cs`) returns null with no enabled
  action, or when none was running and none evaluates above 0; `BroadcastNewAction` then threw on `GetType()` (only
  for the four synced leviathans; the others never send). Now changes only plus a refresh every 5 s
  (`CreatureActionBroadcastRules`), and "no action" is sent as an empty name, which clears the action on the other games
  (they then stop it, as `Creature.UpdateBehaviour` does). A game that receives another game's action forgets what it
  sent, so it sends at once when the lock comes back.
- **Crash blast on the partner's vehicle: held, fixed.** `DamageSystem.RadiusDamage` passes no dealer, so D4's relay
  (needs a dealer) never sent it. `Crash_Detonate_Patch` now finds the vehicles in the radius the way `RadiusDamage`
  does (`OverlapSphereIntoSharedBuffer`, nearest `LiveMixin` above each collider, vehicles and Cyclops) and sends
  `CreatureAttackedVehicle` for those whose lock it doesn't hold, with the same falloff; the server passes it to the
  vehicle's lock holder only (D4).
- **Tests.** L2 `CreatureAttackGapsTest`: `CrashFish_StaysAtItsHomeThroughARestart` (fails without the loader fix:
  134 m off on the server, and in the packet the partner's game gets), `WorldBleeder_IsSimulated_AndItsVictimTakesItExclusively`
  (fails without the whitelist line: nobody gets the lock), `EmpBlastAndReaperGrab_ReachThePlayersWhoSeeTheCreature`
  (fails without the three server processors: a far player gets them). Unit (pure helpers, new code, so "fails without
  the fix" doesn't apply): `CreatureActionBroadcastRulesTest`, `BleederClaimRulesTest` and `CrashHomeRulesTest` (the
  last two ported). Client-only lines covered by the scenario only: the deleted `OnDestroy` send, the grab/release and
  EMP replays, the body guard, the crash blast on vehicles.
- Shared files touched: `SimulationOwnership.cs` (2 lines), `WorldService.cs` (loader), `CreatureAggroHelpers.ps1` and
  `D4-ThreatParity.ps1` (D4's Seamoth helpers moved to the shared file unchanged, for M4's scenario).

## M5: Damage from both players adds up (M–L)

FIX_SPEC S4's "Later" item. Their creature health is upstream
[#2903](https://github.com/SubnauticaNitrox/Nitrox/pull/2903) plus a 77-line hand-over grace delta: a hit by the player
who doesn't simulate the creature becomes `CreatureDamageAction`, routed to the lock holder (back to the sender if
nobody holds it); the holder applies it and sends `CreatureHealthChanged` (0.2 s throttle) to whoever can see it; the
previous holder's last report is still accepted for 1–1.5 s after a hand-over. Nothing is persisted (as today).
- Build from the PR head plus the grace delta, not their `LiveMixin_TakeDamage_Patch` (tangled with their drone and hit
  matcher, and it collides with S14 and D4 edits in that file). Read the PR's review comments first.
- **Known regression, *hypothesis (review)*:** a bite from another creature counts as environment damage, and is
  cosmetic when the other player simulates the prey; the prey's simulator never bites it either. So a predator and prey
  simulated by different players: the prey never dies. Needs a scenario case before merging.

**Findings (2026-11-02):** implemented on `fix/m5-creature-health`, not verified in game (scenario
`tools/scenarios/M5-CreatureHealth.ps1` written, not run). No save change, nothing persisted (as before); two new
packets, `CreatureDamageAction` and `CreatureHealthChanged` (all three programs must run the same build). No metadata
tags used.
- **PR review comments: none.** `gh pr view 2903` and the review-comments API return no comments and no reviews (open,
  CI never ran on it). Nothing to address.
- **Built from** the PR's four commits, cherry-picked (#2902, its base, is already in `coop`). The one conflict, in
  `LiveMixin_TakeDamage_Patch`, keeps the S14/D4 code and adds the PR's two hunks. Then the grace delta, then the
  regression fix below. Left out of MultiNaughtica's version: their `LiveMixin_TakeDamage_Patch` (drone, hit matcher),
  `CreatureDamageSource`, the `LiveMixin_Kill_Patch` destruction watcher, `LiveMixinManager`'s hit replay and drone
  lines, their `LogHealth` debug lines.
- **Hand-over grace: ported, one gap closed.** The server remembers who lost a creature's lock last
  (`SimulationOwnershipData`: revoke, revoke of an id, and an exclusive lock breaking a transient one, e.g. the
  propulsion cannon, which theirs misses); `CreatureHealthChangedProcessor` still relays that player's report for
  1.5 s, until the new holder reports. The new holder's game takes it for 1 s if its own health hasn't changed since the
  take-over (`SimulationOwnership.TryGetLockAge`, `CreatureDamageRules.AcceptsReportAfterHandOver`). A disconnect
  (`RevokeAllForOwner`) records no previous owner: that player sends nothing more. **Open (the PR's):** a hit that
  reaches the old holder after the lock moved is applied there (a hit is applied wherever the server sent it, for the
  "nobody simulates it" case), but the old holder no longer reports, so the new holder never learns of it. Lost, not
  doubled.
- **Known regression (predator and prey): held, fixed.** The PR's `GetHitSource` returns `World` for a creature
  dealer, and a game that doesn't hold the prey only shows a `World` hit on a streamed copy (`RemotelyControlled`, added
  only by `EntityTransformUpdatesProcessor`, i.e. on copies another game streams). `game:MeleeAttack.OnTouch` passes
  the creature itself as dealer, and `CanBite` needs `Aggression >= biteAggressionThreshold`, which for creatures
  outside `AI.syncedCreatureWhitelist` only the simulating game raises (FIX_SPEC S14, confirmed in game there). So with
  the predator simulated by A and the prey by B, A's bite was only shown and B's copy never bites. Fix
  (`CreatureDamageRules.CreatureDealerSource`): a bite by a creature this game simulates is `Local`, sent to the prey's
  simulating player like a player's hit; a streamed copy's bite is `RemoteCopy` (dropped by the holder, shown
  elsewhere); the four leviathans keep `World`, since every copy of them bites, each game's copy of the prey takes the
  bite and only the holder counts it (*unverified in game*: a leviathan biting a creature). Lost: the predator's game
  sees its prey alive after a deadly bite, so it doesn't eat it (`MeleeAttack.TryEat`); the prey leaves a corpse.
- **Other damage that isn't a player's** (each from `game:` code; none is applied twice):
  - Crash fish blast (M4): `DamageSystem.RadiusDamage` passes no dealer and runs only in the crash fish's simulating
    game, so on a creature the other player simulates it was only shown. Fixed: `Crash_Detonate_Patch` opens the PR's
    local-attack scope for the blast.
  - Environment (`TemperatureDamage` heat, `AcidicBrineDamage`, `Geyser`, `Lava`; which creatures carry the first two
    is asset data, not read): no dealer, every loaded copy takes it, only the holder counts it. Ampeel shock
    (`ShockerMeleeAttack.OnTouch`, no dealer, no aggression gate) and `HangingStinger`'s damage over time: the same, if
    the contact also happens in the holder's game.
  - Vehicle and Cyclops ramming (`DealDamageOnImpact`, the vehicle as dealer): `Local` in its pilot's game,
    `RemoteCopy` elsewhere (PR; its author tested rams in game).
  - **Open:** gas pods (`GasPod.Update`, Poison, no dealer; small fish and gasopods are immune). `GasoPod.Update` drops
    pods from any copy whose `Scared` is high, not gated by the AI; whether `Scared` rises on a non-simulating copy
    wasn't read. If only one game makes pods, their damage on the other player's creatures is only shown. The same for
    lava lizard rocks (`Projectile`) and warp balls, which exist only in the caster's simulating game. Gas torpedoes: the
    PR's own limitation (one explosion counts instead of two).
- **Death sync (FIX_SPEC S4) still works, by reading:** a forwarded hit is a plain `TakeDamage` in the holder's game,
  not under `IsRemoteHealthChanging`, so `CreatureDeath_OnKill_Patch` lets `OnKill` run and
  `CreatureDeath_OnKillAsync_Patch` sends `RemoveCreatureCorpse` (`LiveMixin_Kill_Patch` sends `EntityDestroyed` for
  `destroyOnDeath`). A hit sent back because nobody simulates the creature kills in the sender's game, which may run
  `OnKill` (#2902). Each scenario case checks the death in both games and on the server. Not changed, and not M5's:
  `MeleeAttack.OnTouch` follows a bite with `NotifyCreatureDeathsOfCreatureAttack`, which spawns the respawner of a
  creature that respawns only when killed by a creature (`game:CreatureDeath.OnAttackByCreature`,
  `respawnOnlyIfKilledByCreature`). In `coop` that never happens, even when one game simulates both creatures: the
  corpse path of `OnKillAsync` has no `yield`, so `CreatureDeath_OnKillAsync_Patch` drops the lock inside `Kill`, and
  `CreatureDeath_OnAttackByCreature_Patch` then refuses. A forwarded bite doesn't replay the call (it would be refused
  the same way). The ordinary respawner (`respawn && !respawnOnlyIfKilledByCreature`) is spawned by the holder's kill.
- **What the hitter sees:** its game runs the damage receivers (flinch, flee, aggression), the hit sound and the blood
  effect without changing the health (`PlayCosmeticDamage`), then shows the holder's health (0.2 s throttle). The
  scenario checks `LiveMixin.timeLastDamageEffect`.
- **A hit on a creature nobody sees or simulates:** routed by lock, not visibility. Nobody holds the lock (not in
  `SimulationWhitelist`, unknown to the server): the hit comes back and the sender applies it. A holder that hasn't
  loaded the creature keeps the hit 1 s, then sends it again (at most 2 hops); the server drops a re-sent hit when
  nobody holds the creature. Health reports reach only players who see the creature, none for an entity the server
  doesn't have.
- **S4's scenario changed with it:** its kill was a dealer-less `TakeDamage` from the game that doesn't simulate the
  creature, which M5 counts as environment damage there (only shown), so it would have failed. It now passes the player
  as dealer, as the knife does (`Knife_OnToolUseAnim_Patch`), with the no-damage cheat off in both games (the hit lands
  in the simulating game).
- **Tests.** L2 `CreatureHealthRoutingTest` (5): a hit reaches only the holder; with nobody holding it goes back to the
  sender and a re-sent hit is dropped; a report reaches only those who see the creature, a non-holder's is dropped (all
  five fail without the PR's two server processors: the default processor relays to everyone); the previous holder's
  report is relayed after a hand-over until the new holder reports (fails with the grace off, and with
  `ForgetPreviousOwner` removed), and not after the window. Unit: `CreatureDamageRulesTest` (13; the predator/prey and
  remote-copy cases fail with the creature-dealer rule reverted to `World`), `SimulationOwnershipDataTest` (5; the
  exclusive take-over fails without its line), the PR's processor tests plus the two ported grace cases. Not unit-tested:
  the 0.2 s report throttle (the existing `ThrottledPacketSender`) and the PR's 0.1 s hit batching (Unity-bound).
  **Covered only by the scenario** (the rules helper tests the decision, not the wiring): `CreatureHealthManager`'s
  use of it, the `Crash_Detonate_Patch` scope, the client half of the grace (`TryGetLockAge`, `IsInHandOverGrace`;
  the scenario's hand-over case doesn't provoke a late report either), and everything the games do with the packets.
- Shared files touched: `LiveMixin_AddHealth_Patch.cs` (the PR's two hunks; also changed by
  `feat/2-g0-g4-story-gates`), `LiveMixin_TakeDamage_Patch.cs`, `Crash_Detonate_Patch.cs` (M4), client and server
  `SimulationOwnership.cs`, `ClientAutoFacRegistrar.cs`, `Multiplayer.cs`, `S4-CreatureDeathSyncs.ps1`.

## M6: Movement timestamps as `double` (S)

*Hypothesis (review):* `MovementReplicator` keeps snapshot times as `float`. A float steps 3.9 ms from 2^15 s (about
9 h of world time) and 15.6 ms from about 36 h, so remote players and vehicles jitter more as the world ages (2.7–11 cm
per step at 7 m/s). FIX_SPEC S13's measurements ran on a world a few hundred seconds old and couldn't see it. Not a
second fix for #2761 (#2864 is in both bases). `VehicleMovements.RealTime` is already `double`; `PlayerMovement` gains a
time field. Wire change only, no save change. **Open:** which clock the snapshots use (world time or session time)
decides whether it bites; check before building, and age the server clock in the S13 rerun.

**Findings (2026-11-02):** implemented on `fix/m6-movement-time`, not verified in game (scenario
`tools/scenarios/M6-AgedWorldMovement.ps1` written, not run).
- **Which clock: world time, so it bites. Held.** `MovementBroadcaster.Update` stamped `(float)TimeManager.RealTimeElapsed`,
  the server's `TimeService.ActiveTime` (sent in `TimeChange`), which is saved as `StoryTiming.RealTimeElapsed` and
  loaded back (`WorldService.LoadPersistedWorldIntoServicesAsync`). So the step follows the world's total play time: 3.9 ms
  from 9.1 h, 7.8 ms from 18.2 h, 15.6 ms from 36.4 h. `VehicleMovements.RealTime` was already `double` but carried a
  value rounded at the sender; the receiver cast it again (`VehicleMovementsProcessor`), compared it with
  `(float)RealTimeElapsed` and kept `Snapshot.Time`/`SentTime` as floats. The clock itself is fine-grained: S13's
  recordings show its `rt` steps per frame at 0.1 ms resolution.
- **Jitter grows with the world's age: held for vehicles (arithmetic, not measured in game).** At 40 h and 120 fps the
  receiver's "now" is the same on about every second frame, so the sub stands for a frame and then moves two (11 cm
  steps at 7 m/s); each stamp is off by up to 7.8 ms; latency samples by up to 15.6 ms, which raises
  `MaxAllowedLatency`; the hand-over velocity (two stamps 33 ms apart, read as 31.3 or 46.9 ms) by -29 % to +7 %.
  **Remote players: dropped.** They aren't replayed from snapshot times (below); a player riding a replicated sub moves
  with it.
- **`PlayerMovement` gains a time field: dropped.** `RemotePlayer.UpdatePosition` sets the body's rigidbody velocity
  towards the received position (`MovementHelper.GetCorrectedVelocity`, `Time.fixedDeltaTime`), and
  `UpdatePositionInCyclops` sets the pawn's local pose; neither reads a time. Their field feeds their
  `RemotePlayerMovementReplicator`, which we don't take.
- **Built:** times are `double` from the broadcaster to the replicator: `MovementBroadcaster` (its clock, the period
  check, `BroadcastLocalData`), `VehicleMovementsProcessor` (no cast), `MovementReplicator` (`RealTime`, `AddSnapshot`,
  the latency bump time, `Snapshot.Time`/`SentTime`). A difference of two times becomes a float only after the double
  subtraction (`MovementTime.Age`, `MovementTime.InterpolationFactor`, used for the latency sample, the interpolation
  factor and `HandOverMotion`'s span and age). `MaxAllowedLatency` and `Multiplayer.PacketWaitBound` stay floats
  (small; the latter is session time). Packet unchanged; no save change; the server relays `VehicleMovements` as is.
- **Left as is:** `WatchedEntry` (rate limiter on game time, thresholds of seconds), `VehicleDockingProcessor` (reads
  `MaxAllowedLatency`), `BridgeRecorder` (records `RealTimeElapsed` as a double, S13's `rt`), the radiation leaks'
  `FixRealTime` and `TimeManager.AuroraRealExplosionTime` (the same clock as floats, compared in seconds; M2's area).
- **Not ported:** their `MovementClock` (`NITROX_TIME_TEST` offset and float emulation in Debug builds; we age the real
  clock through the save instead), `ReplicatorTuning`, vehicle extrapolation, the remote-player replicator.
- **Scenario:** `S13-CyclopsMovement.ps1 -AgeWorldSeconds <s>` saves its fresh world, closes everything, adds the
  seconds to `StoryTiming.RealTimeElapsed` and `AuroraRealExplosionTime` in its own `WorldData.json` (text edit, exactly
  one match; game time unchanged so no story timer fires), restarts, and checks both games' movement clock (the `rt`
  column, `Get-MovementClock`) reads it before driving and in the recorded drive. `M6-AgedWorldMovement.ps1` runs it at
  144000 s (40 h) without the docking and rejoin parts. Windowed, as S13.
- **Open:** whether S13's thresholds hold at 40 h with doubles (they were set on a young world) is the rig pass's
  question.

## M7: Saving indicator (S)

- **Theirs:** `SaveService` sends `ServerSaving(number)` to everyone when a save starts (timed autosave, the `save`
  command, the last player leaving); a failed send never blocks the save. The client draws its own ring and Arial text
  in the top left for 2.5 s.
- **Ours, rebuilt (COOP §2 principle 1, no new art):** the same server hook, shown through the game's own
  `SavingIndicator`, which shows the localized `SavingGame` text while `SaveLoadManager.main.isSaving`
  (`game:SavingIndicator.cs`). Nitrox games never save locally, so today it never shows. Send the start and the end
  (finished or failed) of each save, and let a Postfix on `SavingIndicator.Update` also show it while a server save
  runs, for at least about 2 s so a fast save still reads.
- **Open (H):** whether a `SavingIndicator` instance exists in the play scene, not only in the menus. Check with the
  bridge before building; decide the fallback then.
- **Tests:** L2, a `save` sends start and end to every player; windowless, the indicator's `CanvasGroup` alpha is 1
  during a save on both games and 0 after; negative control without the Postfix.

**Findings (2026-11-02):**
- *Held:* `SavingIndicator.Update` sets `canvasGroup.alpha` from `SaveLoadManager.main.isSaving`, and the text is
  `Language.main.Get("SavingGame")` (`game:SavingIndicator.cs`). *Held:* a Nitrox game never saves locally. The only
  game code that saves is `IngameMenu.SaveGameAsync` (`game:IngameMenu.cs`, from the save button and the permadeath
  branch of `QuitGameAsync`), and `isSaving` is set only in `SaveLoadManager`'s save coroutines. Nitrox hides the save
  button (`IngameMenu_OnSelect_Patch`) and skips the permadeath save (`IngameMenu_QuitGameAsync_Patch`).
- *Held (theirs):* the timed autosave (`AutoSaveService`), the `save` command (`SaveCommand`) and the save when the last
  player leaves (`IHibernate` sleep) all go through `SaveService`'s queue, which runs one save at a time. The server also
  saves once right after it starts, because it starts asleep (`HibernateService.StartingAsync`).
- *Still open (H):* whether a `SavingIndicator` instance runs in the play scene. No game code references the type. The
  pause-menu save sets `isSaving`, and `SavingIndicator` is the only code that shows it, which suggests one does
  (unverified). The scene bundles are compressed and weren't searched. Both paths are built, and the scenario prints
  which one each game used.
- **Built.** `SaveService` sends `ServerSaving(number, STARTED)` to every connected player before it writes the world,
  then `FINISHED` or `FAILED` once the world and the `ISaveState` files (bans, config) are written. A failed or cancelled
  save also sends its end, and a packet that can't be sent is only logged. The post-save command and the backup run
  after the end. The packet has no server processor: a copy a game sends is dropped (`DefaultServerPacketProcessor`
  blacklist), so it is never relayed or stored. On the client, `ServerSaveIndicator` (pure rules, one per session) shows
  a save from its start for at least 2 s, until its end, at most 60 s if the end never arrives. It ignores an end without
  its start (a player who joined during the save), and a new start replaces the running one. `SavingIndicator_Update_Patch`
  (Postfix) sets alpha 1 while it shows. The path is chosen at the start: if a `SavingIndicator.Update` ran in the last
  2 frames, the indicator path is used. Otherwise, in the world, the game's notice shows `ErrorMessage.AddMessage(
  Language.main.Get(SavingIndicator.savingGameMessage))` once per save. While the player still loads, nothing is shown;
  the indicator picks the save up if it starts running before the save ends. The path is logged once per session.
- **Left out of theirs:** their own ring and Arial text (§2 principle 1), their harness console command, their packet
  test (`PacketsSerializableTest` round-trips every packet). **No new strings** (the game's `SavingGame` key), and no
  `ProtoInclude` tags used.
- **Open:** a failed save only hides the indicator. The players aren't told that it failed (the game's own
  `SaveFailedFormat` dialog would fit; not decided). There is no client switch for it (§2 principle 8; it's the game's
  own indicator). It isn't checked whether a canvas that switches off windowless hides the indicator (TESTING.md
  windowless differences); the scenario reads the alpha. An indicator on an active object with its component disabled
  counts as not running (its `Update` doesn't run), so the notice shows; the scenario checks `isActiveAndEnabled` the
  same way. The server's own save right after it starts announces its start before the world has loaded, when no
  player can have joined yet (harmless).
- **Tests:** unit `ServerSaveIndicatorTest` (10). Each of these fails with its rule reverted: the 2 s minimum (2
  tests), the 60 s timeout, the start/end number match (2 tests), the "ran in the last 2 frames" check (2 tests). Three
  are guards. L2 `ServerSavingTest` (3), each failing with its line reverted: `save` sends start then end with the same
  number to both players (no announcements), a failed save (a folder where the serializer writes `Version.tmp`) still
  sends `FAILED` and the next save `FINISHED` (end sent only on success), and a game's copy isn't relayed (blacklist
  line). Scenario `tools/scenarios/M7-SavingIndicator.ps1`, written, not run.

## M8: Crafting from base and Cyclops lockers (M–L)

- **What players get (their design):** the Fabricator, the Modification Station, the moonpool upgrade console's
  fabricator and the Cyclops fabricator also use the lockers of the same base or Cyclops, inventory first. The recipe
  lights up and the tooltip counts include the lockers; an open menu updates when the partner takes items. Not counted:
  planters, aquariums, trash cans, filtration machines, scanner rooms, a docked vehicle's storage, reactors. Survival
  only. The Habitat Builder stays inventory-only (it takes items synchronously per step; a separate design).
- **No double spend (COOP §2 principle 7):** when the inventory alone doesn't pay, the game sends
  `CraftingClaimRequest(claimId, scopeId, itemIds)` and the craft waits. The server's `CraftingClaimArbiter` checks under
  a lock that the scope is a base or a Cyclops and that each item's parents lead to it without passing a player,
  planter, other vehicle or machine; removes the items all or nothing; sends `EntityDestroyed` for each to everyone,
  then `CraftingClaimResponse` to the claimant. A refused claim is retried once with other items, else cancelled with a
  message; if a granted craft can't start any more, the items come back.
- **Option:** `CraftFromLockers` in `server.cfg`, **on by default** (decided). It is a convenience, not a safety help,
  so COOP §2.2's price rule doesn't apply; no vanilla number changes (principle 6), but how storage is used does, so it
  has a switch (principle 8).
- **Port:** about 1.4k lines: `LockerCrafting` and `LockerCraftingRules` (client), `GhostCrafter_Craft_Patch`,
  `CrafterLogic_ConsumeResources_Patch`, the `CrafterLogic_IsCraftRecipeFulfilled` and `TooltipFactory_WriteIngredients`
  transpilers (register both in `PatchesTranspilerTest`), three `uGUI_CraftingMenu` patches, the arbiter and processor,
  two packets, a `SessionSettings` field. Not their `CrafterLogic_ConsumeEnergy_Patch` (their base power work). Their
  75 unit tests include a 50-round race of two claims for the same items; by their own notes it hasn't fully run in a
  live game.
- **Check while porting:** COOP B2's crafter locks (`GhostCrafter_OnHandClick_Patch`, `GhostCrafter_OnCraftingEnd_Patch`),
  since a granted claim re-runs `GhostCrafter.Craft`; FIX_SPEC S7's container mirror on the partner's game when the
  removals arrive as `EntityDestroyed` (open locker UI, `ItemsContainer` counts); the four messages in en and de.
- **Tests:** L2, two players claim the last item in a real locker at once: one granted, one refused, the server state
  is the same after a restart. Windowless: Alice crafts a knife with titanium from a base locker; Bob's locker loses it;
  `bridge.ps1 diff` is clean; the same in a Cyclops. Negative control: with the option off, a craft that needs the
  locker is refused.

**Findings (2026-11-02):** implemented on `feat/m8-locker-crafting`, not verified in game (scenario
`tools/scenarios/M8-LockerCrafting.ps1` written, not run). The item has no *hypothesis (review)* lines; each design and
"check while porting" line was read in our code and the decompiled game (83031):
- **Which stations: held.** Fabricator and Modification Station (`Workbench`) are `GhostCrafter`s
  (`game:GhostCrafter.cs` ProtoIncludes); the Cyclops' and the moonpool console's fabricators sit under a `SubRoot`. The
  escape pod has none, so its fabricator stays vanilla (B2's scenario is unaffected). `GhostCrafter.Craft` takes the
  power before the ingredients (`ConsumeEnergy(5) && ConsumeResources`), so a craft waiting for its claim takes no power.
- **Counts: held.** `CrafterLogic.IsCraftRecipeFulfilled` and `TooltipFactory.WriteIngredients` each have exactly one
  `Inventory.GetPickupCount` call (the transpilers expect one match, IL delta 0). `IsCraftRecipeFulfilled` is also what
  `ConsumeResources` asks, for the Mobile Vehicle Bay and the rocket too: the lockers only count while the crafting
  menu's client is a `GhostCrafter` in a sub root and no vanilla craft is running. Pinned recipes (`uGUI_RecipeEntry`)
  and the Habitat Builder keep inventory counts.
- **Game modes:** `RequiresIngredients()` is false only with `NoCost` (Creative, the nocost cheat;
  `game:GameModeOption.cs`). Survival, Freedom and Hardcore need ingredients, so it applies in all three.
- **Not lockers:** in the game by component (the `storageContainer` fields of `Planter`, `Aquarium`, `Trashcan`,
  `FiltrationMachine`, `MapRoomFunctionality`; a docked `Vehicle`, the Prawn Suit's storage is a `StorageContainer`), and,
  ours, a container inside an item (a carry-all in a locker). Bioreactors (`ItemsContainer`), reactors and the decoy
  tube (`Equipment`) have no `StorageContainer`. On the server by entity: player, planter, vehicle, interior piece, map
  room, another item, and the `Aquarium`/`Trashcans`/`LabTrashcan` modules. Lockers built in a Cyclops are modules whose
  parent is the Cyclops (`Builder_TryPlace_Patch`); its own lockers are `PathBasedChildEntity`s.
- **No double spend: held.** The arbiter (ported) decides one claim at a time and delivers its `EntityDestroyed`s and
  answer before the next. Both packets are `RELIABLE_ORDERED` on the default channel, so the loser hears of the removal
  before its refusal (the L2 test asserts the order). A `CraftingClaimResponse` sent by a game isn't relayed (default
  processor blacklist): a relayed grant would start the partner's craft with nothing taken.
- **"The items come back": decided differently.** Theirs gives fresh copies to the inventory (`CraftData.AddToInventory`
  → `PickupItem`): persisted and no duplicate, but a drained battery comes back full, and with a full inventory the copy
  lies in the world unannounced (gone after a restart). Ours: `CraftingClaimReturn`. The server keeps the last 32 granted
  claims in memory, puts the same entities back into the lockers they came from once (the claimant's inventory if the
  locker is gone) and spawns them for everyone. A late grant after the game's 15 s timeout is returned the same way.
- **Option:** `CraftFromLockers` (true) reaches the game through `CoopSettings` (the initial sync's co-op options), not
  `SessionSettings` (the server's mutable cheat state). With it off the server answers `Disabled` to any claim.
- **COOP B2: held, nothing to change.** A granted claim re-runs `GhostCrafter.Craft` → `Crafter.Craft` →
  `OnCraftingBegin`, so `GhostCrafter_OnCraftingBegin_Patch` sends the crafter state and takes the exclusive lock as for
  any craft. If the partner started a craft at that station while the claim was on its way (their `CrafterMetadata`
  came first), the craft doesn't start and the claim goes back.
- **FIX_SPEC S7: held, nothing to change.** `EntityDestroyedProcessor.DestroyPickupable` → `Pickupable.OnDestroy` →
  `SetInventoryItem(null)` → `container.RemoveItem(forced)` (`game:Pickupable.cs`): the partner's `ItemsContainer` count
  drops and `onRemoveItem` fires (open locker UI, open crafting menu), with `EntityDestroyed` suppressed.
- **Not taken from theirs:** `CrafterLogic_ConsumeEnergy_Patch` (base power), their Debug harness hooks, the static
  class (a DI service now; the transpilers' static target resolves it), `CraftingClaimRequestProcessorTest` (it builds
  server objects by hand that differ here; its cases run in L2 instead).
- **Open:** only the scenario covers the game half: the menu lock after a delayed start (`RestoreMenuLock`), the menu
  refresh when the partner takes items, and whether the crafting menu opens in a windowless game (scenario step 1).
  Placing the Fabricator and Locker in the S7 room through the bridge is untried (S7 couldn't place a wall locker); the
  Cyclops half doesn't need it.

## M9: Ping aim (XS–S, follow-up to COOP F2)

- F2 stays as built. Only the aim changes, following their `QuickPingAim`:
  - skip the player's own vehicle and, at the helm, the Cyclops itself. Ours relies on `RaycastHelper`'s 3 m minimum,
    which also makes anything closer than 3 m unpingable;
  - a minimum of about 0.5 m instead of 3 m;
  - a second ray against the Useable layer, triggers included, so the partner's body can be pinged (ours skips
    triggers). Which kind and label a pinged partner gets is decided in the branch.
- Not taken from their ping: the server-stamped sender, rate limit and text cleaning (protection for public servers),
  one ping per player, 300 m.
- **Tests:** extend `F2-ContextPings.ps1`: pings from the Seamoth seat and the Cyclops helm reach what is outside; an
  item at 1 m; the partner.
- **Findings (2026-11-02):**
  - *Ours relies on `RaycastHelper`'s 3 m minimum:* held. `PlayerPingManager` called `GetClosestHitFromAim(250)` with
    the default `minDistance = 3` and no check for the own vehicle or sub, so anything nearer than 3 m was unpingable.
    Whether a Seamoth's or the Cyclops' own colliders really lie in front of the camera, and how far, is prefab data,
    not in the decompiled code (unverified; the scenario prints every hit). Where the Cyclops reaches more than 3 m
    from the helm camera (looking down through the hull), the old ping would land on the sub itself (unverified, the
    scenario's helm step checks it).
  - *Ours skips triggers, the partner's body is one:* held. `RemotePlayer.SetupBody` puts the body on
    `LayerID.Useable` with a trigger `CapsuleCollider`; `RaycastHelper` skipped every trigger. The game's own hand
    targeting accepts exactly Useable triggers (`game:Targeting.Filter`), which is how T2 hovers the partner.
  - Layers, from the decompiled game: the local player is on the `Player` layer by every sign in code (the game's own
    rays from the player leave it out: `PlayerController`, `PropulsionCannon`, `Builder`, `PlaceTool`; `BaseUtils`
    treats a `LayerID.Player` hit as the player), not read from the prefab. Terrain chunks are layer 30
    (`game:VoxelandChunk`). `LayerID.Vehicle` is declared but used nowhere in code; vehicle and Cyclops collider
    layers are prefab data. So "own" is decided by component, not layer: `Player`, the `Vehicle` from
    `Player.GetVehicle()`, the `SubRoot` from `Player.currentSub`. "At the helm" is `Player.Mode.Piloting`, which only
    `PilotingChair` sets (`game:Player.EnterPilotingMode`); `IsPiloting()` is also true in a Seamoth or Prawn suit
    (`LockedPiloting`).
  - **Built:** `PingAim` (ping only; `RaycastHelper` had no other caller and is left as it came from upstream, now
    unused) casts two rays through the crosshair, a solid one (triggers ignored, the `Player` layer left out as the
    game does) and one against the Useable layer with triggers. `PingAimRules` (pure) picks the nearest hit that is
    not nearer than 0.5 m, not a trigger unless it is a partner's body, not the own body, not the own vehicle and, at
    the helm only, not the sub the player steers; a partner standing in that sub still counts. Standing in the Cyclops
    (passenger, or after leaving the helm) its walls stay pingable.
  - **The pinged partner:** a **Look** ping (10 s), target `Player`, Signal icon in the sender's colour, label
    "Alice: Look! Bob" from F2's own `Nitrox_Ping_Look`, no new strings. Not Come here: its arrival rule (partner
    within 4 m) would end it at once on both games, the partner is at it. Not a new kind: F2's three kinds keep the
    marker vocabulary small (COOP §2 principle 1), and the packet stays as F2 built it. The packet carries no player
    id, so each game names the player nearest the ping other than the sender; with two players that is always the
    pinged partner. On the pinged partner's own game the marker sits within the game's 5 m `minDist` of them and only
    shows once they move away; they hear the ping sound. The "come here" key (H) goes through the partner as before
    M9, for the same arrival reason.
  - Not taken from their ping (as decided): server-stamped sender, rate limit, text cleaning, one ping per player,
    300 m. Their single-ray stepping loop is replaced by one `RaycastNonAlloc` per ray (sorted by the rule).
  - Debug only: `PingAim.Describe` / `DescribeToward x,y,z` (every hit, what was skipped, the winner) and
    `PlayerPingManager.RequestPingToward x,y,z` (the ping key along a ray from the camera toward a point: the bridge
    can't turn a seated player's camera, T4).
  - **Tests:** `PingAimRulesTest` (12) and `PingRulesTest.APartnerIsLook_NotComeHere`. With the old rule in
    `IsSkipped` (3 m minimum, every trigger skipped) 7 fail; one line at a time: 3 m minimum fails the 1 m item and
    the minimum; no own-vehicle skip fails the Seamoth case; no helm skip fails the helm and partner-at-the-helm cases;
    no partner exception fails both partner cases; partners always included fails the come-here case; without the
    partner line in `SelectKind` its test fails. Scenario
    `tools/scenarios/M9-PingAim.ps1` (written, not run): an item at 1 m; the partner (kind, label, icon, colour, still
    there a second later on both games; the come-here key goes through him); the Seamoth seat (titanium 8 m ahead, a guard: the old 3 m minimum also stepped
    over the nose); the helm (15 m ahead, and 12 m ahead and 45 degrees down through the hull with `RequestPingToward`);
    a passenger's ping at the Cyclops floor is Come here.
  - **Open:** everything in game (implemented, not verified); the Prawn suit's arms (code path as the Seamoth, not in
    the scenario); a partner in the Cyclops pinged from the helm (unit test only); held-tool collider layers; three or
    more players standing together (the name is the nearest non-sender, may pick the wrong one); the Useable ray keeps
    32 hits, so past more Useable colliders than that (a base full of hand targets) a partner can be missed.

## Not taken, and parked

- **Base power (#1811), not taken; notes for COOP B3:** `PowerSourceMetadata {Power, ToConsume}` sent by the base's
  lock holder (5 s throttle, plus `SetPower`), the thermal plant excluded (it has its own metadata); `BasePowerConsumed`
  forwards fabricator draws to the lock holder (routed, not stored); the nuclear reactor burns rods only on the lock
  holder. Its risks: it deletes `SetPower(maxPower)` from both base spawners with no fallback, so generators in existing
  saves start at the prefab's `power` value (unverified); the patch runs on `ModifyPower`, every frame; draws that only
  the partner's game makes are lost. For B3: keep "full when no metadata", reuse `BaseModuleSimulation`, take the next
  free metadata tag (theirs reuse 93–98, which collide with ours).
- **Sunbeam landing site decided by the server, not taken:** each game checks only its own player's distance
  (`game:StoryGoalCustomEventHandler.IsPlayerAtLandingSite`), so both outcomes can play. Their packet has a real server
  processor (first proposal wins, rebuilt from completed goals after a restart) and a transpiler. Reconsider with G0.
- **Custom player models, not taken:** about 14k lines in 84 files (glTF reader, pose copy, hand IK, first person,
  emote wheel) inside the remote-player code that COOP F1, D1, T2 and R2 build on. Each player brings a humanoid `.glb`
  on Mixamo's 65-bone skeleton (one skinned mesh, one material, textures embedded, about 1.7 m, Y-up facing +Z, scale
  applied), and both players need the identical file.
- **World repair, never on our saves.** It runs on every save not written by their build (`RepairVersion < 1`), ours
  included. `DuplicateEntityRepair` deletes server-spawned twins, which their own measurement found in batches generated
  once; `ParsedBatchRepair` marks a batch generated at 16+ entities, so a wrongly marked area never fills;
  `IdCollisionRepair` ignores ids stored in metadata (`MapRoomMetadata.DockedCameraIds`). The damage it repairs came
  from their own earlier build. Its copy-first, report and dry-run design is the model if we ever need an opt-in repair
  command.
- **Their bite relays.** Ours (FIX_SPEC S14, COOP D4) are sounder: theirs lets a non-simulating copy bite its own
  player (S14 measured that double bite), relays the Ampeel shock that already lands locally (double shock), and misses
  crash fish blasts.
- **Vehicle extrapolation** contradicts S13's measured delayed playback. Their remote-player replicator is an idea for
  COOP R2 (needs a windowed check).
- **Parked:** dropping `CreatePoint` from `UpdateOvershield` (changes how many leaks appear; #2881's author added it in
  `827a8c99` for an unknown reason); handing a creature's lock to the closest player (no tests, aggression resets, can
  bounce); their spawner-creature rework (K15); their `TimeManager` frame-delta claim (measure with the bridge first);
  base flood and scanner-room changes (we have S5b and S10); vehicle modules, extra slots, crew stations (vanilla
  numbers, COOP §2 principle 6); their `CoopProtocol` connection key.

## Also found, not decided

Take verdicts from the reviews that weren't in the groups chosen above. Each needs its own decision.
- FIX_SPEC S10 gap: a scanner room added to an existing base gets no id on the partner. `GhostEntitySpawner` places and
  finishes the ghost before parenting it, so `occupiedBounds` is stale; their fix calls `Base.RecomputeOccupiedBounds`.
- S2/S10 gap, verify first: docked scanner-room drones may have no battery in survival (our blanked
  `EnergyMixin.SpawnDefaultAsync`), so they couldn't be piloted.
- S1 small deltas: spawns of a destroyed sub, fading fires, dousing while a fire spawns, a leaving player's douse rate,
  a per-frame warning.
- Hand-held extinguisher fuel resets on respawn (metadata, one-way save change).
- S7 step 3 gap: two players filling one container past its size; their server-side area check.
- Aurora keypad console on a child object; a second Precursor key terminal under one root; laser-cut doors that only
  move forward and panels healed through `AddHealth` (all to check in game first).
- Time capsules never reach the partner (`PDAEncyclopedia.Add` returns null for them); stored in `PdaStateData`.
- Entity-slot placeholders placed with the whole parent chain; may explain S7 item 5's open "1.6 m off" placeholder.
- Small: `PlayerTeleportedProcessor` freezing when the terrain is already loaded, spline updates for dead creatures,
  a `TriggerSickAnim` guard for S12, the partner's leftover corpse after `MeleeAttack.TryEat`.
- Leads: a Cyclops listing 8 path children twice (their `IdCollisionRepair` notes; maybe #2448); `VehicleEntitySpawner`
  setting the id before `Start` (maybe #2768).

## Testing: game-rig runs deferred to the end (decided 2026-11-02)

The game rigs are in use by other work, so this spec doesn't wait for them:
- **Per branch:** `dotnet build` + `dotnet test` (unit and L2 headless tests, no rig needed). Each item still gets its
  scenario **written** in `tools/scenarios/` (windowless unless TESTING.md says windowed), but not run. The Log row says
  "scenario written, not run".
- **At the end, once M1–M9 are all implemented:** one rig session runs every scenario of this spec, windowless first,
  windowed only where TESTING.md requires it. Each scenario also runs against the build without its fix (negative
  control), as AGENTS.md §7 asks. Hold the rig lock (`tools\game-lock.ps1`) for the whole pass.
- Until that pass, an item counts as **implemented, not verified**. Merge order into `coop` follows the same rule:
  branches may merge after their headless tests pass; a scenario that fails in the final pass reopens its item.

**Final pass checklist (2026-11-03, all nine implemented on branch `multinaughtica`).** One line per scenario: how to
run it and what its author flagged. Details are in each item's Findings.
- `M1-CyclopsPowerBuilding.ps1`, windowless. Unchecked assumptions: a loose constructable near the Aurora cargo room
  (`-LooseSpot`), `set` on `PlayerCinematicController.cinematicModeCount` (private setter), the partner has the power
  cell prefab cached, exactly one new beacon and 15 scrap metal. Lights, thermal reactor and build bots aren't covered.
- `M2-StoryProgress.ps1`, windowless. Which doors hang on a key's goal is asset data: the door step is the first real
  check of COOP §12.1.
- `M3-JoinSaveSafety.ps1`: `-Windowless` or `-Rig` above 0 (it restarts one game itself; in launcher mode it reports
  FAIL). The join-while-loading line is covered by L2 only.
- `M4-CreatureAttacks.ps1`, windowless. It prints whether the partner's body is tagged `Player` (EMP guard).
  `reaper-fallback-AB` is a guard, not a negative control.
- `M5-CreatureHealth.ps1`, windowless. `S4-CreatureDeathSyncs.ps1` changed with it (the kill hit now names the player as
  dealer): rerun S4 too.
- `M6-AgedWorldMovement.ps1`: **windowed** (frame timing, about 120 fps), like S13. `run-suite.ps1` has no windowed
  exclusion, so run M6 and S13 on their own, not in a `-Windowless` suite.
- `M7-SavingIndicator.ps1`, windowless. It prints which path each game used (the game's `SavingIndicator` or the
  notice), which answers M7's open question.
- `M8-LockerCrafting.ps1`, windowless, from the S7 room fixture; `-OptionOff` is the negative control. Building the
  Fabricator and Locker through the bridge is untried (the Cyclops half doesn't depend on it).
- `M9-PingAim.ps1`, windowless. The Seamoth step is a guard, not a negative control.
- Headless tests that sometimes fail under full-suite load and pass alone, on this branch and on its base:
  `ThreadSafeSetTest`/`ThreadSafeListTest.ReadAndWriteSimultaneous`, `DroppedOutOfSightTest.PartnerWithoutTheCell…`,
  once `ToolBatteryPersistenceTest`.
- Upstream sync: M4 deletes upstream's `CrashHome_OnDestroy_Patch.cs`, so a later `master` → `coop` merge may stop on a
  modify/delete conflict; keep it deleted.

**Final pass results (2026-11-03).** Windowless on rigs 0 and 1 (`run-suite.ps1 -Windowless`), the game code as at
`58a741fa` (the pass changed scenario scripts and the bridge diff only, no game or server code). Negative controls: the
same scripts against a build of `coop` @ `d595c18d`, the base this branch merged. Every failure with the fixes was a
script bug; the fixes are in the commits after `58a741fa`. With this pass M1–M9 count as **verified**, except the lines
marked open below and in each item's Findings.

| Scenario | With the fixes | Without (coop build) | Notes |
|---|---|---|---|
| M1 | 61 pass | 7 of 27 fail | Without: the refused click undocks for Bob, the empty cell stays at 200, then the resync step stops (its bridge call isn't in that build). `-LooseSpot` finds a `StarshipDesk` 79.5 m away; setting `cinematicModeCount` works; one beacon and 15 scrap metal. A Seamoth pilot is in mode `LockedPiloting`. The drained Seamoth carries no solar charger, which refilled the cell |
| M2 | 32 pass | 11 of 24 fail | The door step uses the scenario's own goal `M2TestDoorKey`: which doors hang on a key's goal is still unread asset data |
| M3 | 21 pass (rig 1) | 7 of 13 fail | |
| M4 | 41 pass, then `emp-tools-AB` alone 5 pass (windowless and windowed) | 12 of 39 fail | The partner's body is tagged `Untagged` in the other game, so `EMPBlast_OnTouch_Patch`'s skip is a guard. In this setup (players teleported next to the Crabsquid, `EMPAttack.CastEMPBlast` called) the blast's trigger never reached a player on foot, windowless or windowed, the vanilla control included (Alice's flashlight inside her own game's blast stayed on). So the case checks the relay (the copy is cast in Bob's game) and calls `EMPBlast.OnTouch` there: Bob's flashlight goes dark. Whether a natural blast reaches the partner on foot is **open** (the case judges it once its vanilla control works). Crash fish spawned from the console never reach the partner's game (`CrashEntitySpawner` only spawns one under its home), so the crash cases don't wait for them. The no-damage cheat zeroes vehicle damage too (`DamageSystem.CalculateDamage`) |
| M5 | 43 pass; S4 23 pass | 10 of 34 fail | S4's death step kills with heat: since T1 any other kill only downs the player |
| M6 | 53 pass, windowed | 4 of 53 fail, windowed | The four driving windows: 0 stalled frames in the partner's view with the fixes, 273–336 of 585–720 (about 46 %) without, both at about 120 fps (`M6-AgedWorldMovement.ps1`, rig 1, while a windowless suite ran on rig 0) |
| M7 | 12 pass | 6 of 10 fail | Both games used the game's own `SavingIndicator`, which runs in the play scene: M7's open question answered (read through its alpha; no screen check) |
| M8 | 50 pass | 20 of 50 fail | The base Fabricator and Locker were placed through the bridge once the helper also tried 2.5 m toward the walls (from the middle only it found no spot). `-OptionOff` not run: the coop build is the negative control |
| M9 | 45 pass | stops at setup | A weak negative control: the coop build has no `PingAim.Describe` for the scenario to call |

**After merging `coop` @ `4b726919` (`797dad49`):** the same windowless run passes M1–M5, M7–M9 and S4 again, plus
`coop`'s `S15d-BleederOnPartner.ps1` after two script changes that M5 makes necessary. Bob's punches on a bleeder
latched onto him are applied in Alice's game, which simulates it, so her no-damage cheat must be off. And there its AI
runs: the Bleeder has `FleeOnDamage` (`damageThreshold` 4, read in game; a punch does 3 of its 10 health), so after the
second punch it lets go and flees with 4 health in both games, as in the game. Before M5, Bob's copy, which runs no AI,
took every punch itself and died, which is what S15d checked. M4's EMP direct touch failed once in four runs (passed
alone right after, with Bob holding the flashlight): flaky, cause not found.

Also found by this pass (scripts and tools): `bridge.ps1 diff` compared a vector's `Normalized` text, which the game
writes in its own culture (`[0,47, ...]`), and a base module's position, which the server keeps local to its base;
both are now skipped. `$rig` in a scenario is `bridge.ps1`'s `[int]$Rig` parameter, dot-sourced through
`ScenarioHelpers.ps1` (PowerShell names are case-insensitive), and in `@($x[0] + 3, $y, $z)` the comma binds before the
`+`.

## Definition of done (per item)

- AGENTS.md §7: `dotnet build` + `dotnet test` green, a scenario or L2 test that fails without the fix, a note on what
  was verified and how. Rig scenarios run in the final pass (section above).
- First step on every branch: confirm the item's *hypothesis (review)* lines against our code; drop or re-scope the
  ones that don't hold.
- M7–M9 also follow COOP_FEATURES_SPEC §13: strings in en and de, the README updated when the feature lands (a "small
  comforts" line: the game's "Saving game" notice when the server saves, crafting from base and Cyclops lockers, pings
  that aim past your own vehicle).
- A Log row below. When an item lands, add a pointer to it from the FIX_SPEC or COOP item it overlaps.

## Log

| Date | Item | Change | Verified by |
|---|---|---|---|
| 2026-11-02 | — | Spec created from the review of MultiNaughtica `99428c8` (base upstream `7815b4e4`) against `coop` @ `ddf06755`. Decisions recorded above. Game-rig tests deferred to one pass after M1–M9 are implemented (rigs in use) | Code review only, nothing built or run. Re-read in our code: Cyclops `isDestroyed` at health 0, `Battery_charge_set_Patch` Prefix, radiation `Max()`, Sea Emperor baby parent check. Everything else is *hypothesis (review)* |
| 2026-11-02 | M3 | Implemented, not verified in game: start backups in `backups/server-starts/` (last 5); reservations wait for the world and the join re-checks the saved player; a reservation that never joined frees its name; a stale session with the same name and IP is kicked; only finished batches are recorded and failed batch loads are retried; one NTP socket per round, closed at its end; Debug `entitycount` | L2 (headless): `JoinSafetyTest` (5, each fails with its line reverted), `ServerStart_OnAnExistingSave_BacksItUpFirst` (fails without the start hook), `GeneratedBatch_StaysRecordedAcrossRestarts` (guard: fails if restored batches aren't marked done). Unit: `ServerStartBackupsTest` (zip and pruning, real files; the rolling-pruning test is a guard), `BatchLoadTasksTest` (running/failed batches not listed, failed loads retried: fail with the old behaviour), `NtpSyncerTest` (2, fail with the old round handling). Scenario written, not run |
| 2026-11-02 | M1 | Implemented, not verified in game: a Cyclops at 0 HP isn't sent as destroyed until it explodes, a watchdog ends a countdown whose player left, the beacon comes from the game whose countdown fired; battery charge sent after the change and when empty; the partner takes the old power cell out before installing the new one; a building resync respawns a base or module the game lost; a deconstructed loose constructable leaves its cell and lock; vehicle lights drain on the lock holder only, the thermal reactor skips while another player pilots; un-started build bots skip the launch setter; undocking announced at `OnUndockingStart`, the lock given back after a refused click | Unit (each fails with its line reverted): `CyclopsDestructionRulesTest` (old `\|\| health <= 0`), `BatteryChargeRulesTest` (old floor-only rule), `BuildingResyncPairingTest` (pairing with null), `BuildingManagerDeconstructTest` (old `RemoveEntity`, no revoke), `ConstructorBuildBot_launch_set_PatchTest` (prefix always true). L2 `CyclopsDyingPersistsTest`: a server-contract guard, passes without the fix. Not tested headless: power-cell swap, lights, thermal reactor, undocking, watchdog, beacon (scenario only, or not covered). Scenario written, not run |
| 2026-11-02 | M8 | Implemented, not verified in game: crafting stations of a base or Cyclops also use its lockers (menu counts, tooltip, claims at the server, one retry, the same items go back if the craft can't start); `CraftFromLockers` (true) via `CoopSettings`; a game's `CraftingClaimResponse` isn't relayed. No ProtoInclude tags | Unit: `CraftingClaimArbiterTest` (35, incl. the 50-round race; each fails with its line reverted: gate, player and item exclusions, return once), `LockerCraftingRulesTest` (32). L2: `LockerCraftingTest` (4: race over the protocol + restart, return once + restart, option off, no relay; each fails with its line reverted). Transpilers registered (delta 0). Scenario written, not run |
| 2026-11-02 | M4 | Implemented, not verified in game: the Reaper's grab and release are replayed in every game that sees it (20 s fallback; the pilot's game applies the grip); the Crabsquid's EMP is replayed at each game's copy and skips other players' bodies; the victim's game takes a Bleeder's exclusive lock and world bleeders are simulated (whitelist, duplicates removed); crash fish are no longer deleted when their area unloads, the holder waits for the home's saved state and makes no second fish, and the loader keeps a fish's world position; creature actions are sent on change plus every 5 s, null included; a crash fish blast hurts the partner's vehicle. Their `CrashHomeStates` save normalisation left out | L2 `CreatureAttackGapsTest` (3, each fails with its fix reverted: loader, whitelist line, server processors). Unit `CreatureActionBroadcastRulesTest`, `BleederClaimRulesTest`, `CrashHomeRulesTest` (pure helpers). Scenario written, not run |
| 2026-11-02 | M2 | Implemented, not verified in game: babies of the server swim to the mother and teleported ones are destroyed on the server, incubator metadata applied once; radiation radius for a joiner from all leaks, stale leak health dropped (both sides); the pod respawn keeps the player in the pod, a new player's entity transform is parented (S13 finding); story and Aurora packets wait for the initial sync; goals completed outside `StoryGoal.Execute` (door keys) are sent, teleporter pairs persist as `Nitrox_TeleporterActive:<id>` goals, only a teleporter that opened reports; the server drops `RadioSunbeam4` on `Goal_Disable_Gun`, the partner's open gun terminal closes; double fragment scan; rocket reports merged, a launch for an unloaded rocket ignored, stage before elevator; beacon keys compared ignoring case. Hypothesis "goals lost while loading" only partly held (see Findings) | L2 (headless): `StoryProgressTest` (6 fail with their fix reverted, 1 guard). Unit: `RadiationRadiusModelTest` (fails with the old `Max()` rule), `RocketMetadataTest`, `SeaEmperorBabyRulesTest`, `DirectGoalCompletionsTest`, `TeleporterGoalKeyTest`, `InitialSyncDeferralTest`. Scenario `M2-StoryProgress.ps1` written, not run |
| 2026-11-02 | M7 | Implemented, not verified in game: the server tells every player when a save starts and ends (finished or failed); the game's own `SavingIndicator` shows it (Postfix on `Update`) for at least 2 s, at most 60 s without an end; where no indicator runs in the play scene, the game's notice shows `SavingGame` once per save. A game's copy of the packet is dropped. No new strings, no `ProtoInclude` tags | Unit `ServerSaveIndicatorTest` (10; 7 fail with their rule reverted, 3 guards). L2 `ServerSavingTest` (3, each fails with its line reverted). Scenario written, not run |
| 2026-11-02 | M9 | Implemented, not verified in game: pings aim past the own body, the own Seamoth or Prawn suit and, at the helm, the Cyclops; minimum 0.5 m instead of 3 m; a second ray on the Useable layer with triggers reaches the partner's body, a Look ping "Look! *name*" (Signal icon, sender's colour, no new strings) | Unit: `PingAimRulesTest` (12; with the old rule 7 fail, each M9 line reverted alone fails its cases), `PingRulesTest.APartnerIsLook_NotComeHere` (fails without the partner line). Scenario `M9-PingAim.ps1` written, not run |
| 2026-11-02 | M6 | Implemented, not verified in game: movement snapshot times are `double` from the broadcaster to the replicator (the sender's clock and period check, the packet's value without the processor's cast, the replicator's clock, `Snapshot.Time`/`SentTime`, the latency bump time); a difference becomes a float only after the double subtraction (`MovementTime`). No `PlayerMovement` time field (nothing replays remote players from times). Scenario: `S13-CyclopsMovement.ps1 -AgeWorldSeconds` ages its own world through its save, `M6-AgedWorldMovement.ps1` runs it at 40 h | Unit `MovementTimeTest` (6): interpolation factor and snapshot age exact at 1-100 h of play; 3 fail when `MovementTime` rounds its arguments to float first (the old arithmetic); the wiring test (double types, no `conv.r4` in the broadcaster's `Update`, the processor's `Process` or the replicator's `RealTime`) fails on the old three files and on the processor's `(float)` cast alone. `dotnet build` clean (NU190x/DIMA001 only), `dotnet test` 417 pass, 13 skipped. Scenario written, not run (windowed) |
| 2026-11-02 | M5 | Implemented, not verified in game: upstream #2903 (only a creature's simulating game changes its health; other players' hits are sent there, or back when nobody simulates it; the health is shared with those who see it) plus MultiNaughtica's hand-over grace (server 1.5 s, new holder 1 s; also after an exclusive take-over). Regression fixed: a creature's bite on a creature the other player simulates, and a crash fish blast on one, are sent to the prey's simulating game. S4's scenario kill passes the player as dealer. No ProtoInclude tags | L2 `CreatureHealthRoutingTest` (5, all fail without the server processors; the grace case fails with the grace off and with `ForgetPreviousOwner` removed). Unit `CreatureDamageRulesTest` (13; predator/prey and remote-copy cases fail with the old creature-dealer rule), `SimulationOwnershipDataTest` (5; exclusive take-over fails without its line), PR processor tests + 2 ported grace cases. Scenario written, not run |
| 2026-11-03 | M1–M9 | All nine merged on branch `multinaughtica` (one `fix/`/`feat/` branch per item, merged `--no-ff`), together with `coop` @ `72ca17ae`. Final pass checklist added under "Testing". Not merged into `coop` yet | Full `dotnet build` green; `dotnet test` 724 passed, 13 skipped, 1 failed (`ReadAndWriteSimultaneous`, flaky under load, passes alone 3/3, its code untouched). No scenario run yet |
| 2026-11-03 | M1–M9 | Final rig pass (results under "Testing"): no game or server code changed. Scenario and tool fixes: `$rig` collision (M2, M3), comma precedence, pilot mode and solar charger (M1), no-damage cheat on vehicles (M4), placement spots (M8), heat kill past T1 (S4), crash fish not waited for in the partner's game, M4's sulfur plant search and EMP positioning, bridge diff noise (`Normalized`, module positions) | Windowless (rigs 0 and 1): M1 61, M2 32, M3 21, M4 41 + 5, M5 43, S4 23, M7 12, M8 50, M9 45 checks pass. Negative controls, the same scripts on a `coop` @ `d595c18d` build: all eight fail (M9 only at setup). Windowed: M6 53 pass, without the fix 4 of 53 fail (about 46 % stalled frames). Open: a natural Crabsquid blast on a partner on foot |
| 2026-11-03 | M1–M9 | Merged `coop` @ `4b726919` into `multinaughtica` (`797dad49`). Its S15d and M4's Bleeder claim patched the same two `AttachAndSuck` methods: S15d kept (verified in game), M4's claim removed (M4 Findings). `S15d-BleederOnPartner.ps1`: the knock-off step follows M5 (hits applied by the simulating game, which runs `FleeOnDamage`) | `dotnet build` clean, `dotnet test` 772 passed, 13 skipped. Windowless on the merged build: M1 61, M2 32, M3 21, M4 41 (+ `emp-tools-AB` 5), M5 43, M7 12, M8 50, M9 45, S4 23, S15d 53 checks pass |
