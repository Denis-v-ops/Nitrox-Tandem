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

## M6: Movement timestamps as `double` (S)

*Hypothesis (review):* `MovementReplicator` keeps snapshot times as `float`. A float steps 3.9 ms from 2^15 s (about
9 h of world time) and 15.6 ms from about 36 h, so remote players and vehicles jitter more as the world ages (2.7–11 cm
per step at 7 m/s). FIX_SPEC S13's measurements ran on a world a few hundred seconds old and couldn't see it. Not a
second fix for #2761 (#2864 is in both bases). `VehicleMovements.RealTime` is already `double`; `PlayerMovement` gains a
time field. Wire change only, no save change. **Open:** which clock the snapshots use (world time or session time)
decides whether it bites; check before building, and age the server clock in the S13 rerun.

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
| 2026-11-02 | M4 | Implemented, not verified in game: the Reaper's grab and release are replayed in every game that sees it (20 s fallback; the pilot's game applies the grip); the Crabsquid's EMP is replayed at each game's copy and skips other players' bodies; the victim's game takes a Bleeder's exclusive lock and world bleeders are simulated (whitelist, duplicates removed); crash fish are no longer deleted when their area unloads, the holder waits for the home's saved state and makes no second fish, and the loader keeps a fish's world position; creature actions are sent on change plus every 5 s, null included; a crash fish blast hurts the partner's vehicle. Their `CrashHomeStates` save normalisation left out | L2 `CreatureAttackGapsTest` (3, each fails with its fix reverted: loader, whitelist line, server processors). Unit `CreatureActionBroadcastRulesTest`, `BleederClaimRulesTest`, `CrashHomeRulesTest` (pure helpers). Scenario written, not run |
