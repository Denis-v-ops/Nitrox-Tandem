# Co-op features spec: playing the story *together*

Subnautica is a single-player game about isolation, awe and fear. This spec adds what two people need to experience the
story **together** without turning it into a different game. It complements [FIX_SPEC.md](FIX_SPEC.md), which covers bugs.
This spec covers new features. How to build and verify them: [AGENTS.md](../../AGENTS.md),
[TESTING.md](TESTING.md).

Researched 2026-11-02 against `coop` @ `2edaf402` and the decompiled game 83031 (`F:\nitrox\game-src\83031`). File
references are `path:line` in the repo; `game:` means the decompiled source. **(H)** marks an unverified hypothesis that
must be checked before coding.

## 1. Who it's for and what they chose

The players are a couple **in the same room**, so voice chat is out of scope. Choices from the alignment on 2026-11-02:

| Question | Choice |
|---|---|
| Focus areas | Shared story moments, teamwork survival, finding & signalling, cozy home |
| Cozy home means | **Resting together** (benches, beds, visible animations); **pets & aquariums** |
| Partner visibility | **Subtle signal**: a faint beacon-like marker with name and distance; name tag only when close |
| Partner's signal | **Always on, but faint**: fades when close or in sight |
| Running out of health | **Rescue window**: the player goes down; the partner revives with a first aid kit, or by hand at low health; oxygen keeps draining |
| Story beats that wait for both | **Disabling the gun, rocket launch, Sea Emperor & cure, Aurora drive core** |
| One player alone | **Solo is OK, but the story waits**: those beats stay locked until both are online |
| Teamwork | **Buddy breathing, hand items over, Cyclops crew roles** |
| Seamoth (one seat) | **Hitch a ride**: the partner holds on outside, still underwater, still using air |

Not chosen, so out of scope for now: voice chat, decorating together, notes for each other, discovery notifications,
freezing the world while one player is away.

## 2. Design principles: keep it Subnautica

1. **The partner is a signal in the dark, not an MMO HUD.** Use the game's own visual language: beacon markers, the hand
   reticle and its progress bar, PDA-style messages, existing sounds. **No new art.**
2. **Danger stays dangerous.** The rescue window is a chance, not immunity. Drowning, lava, heat and explosions still kill.
   A downed player keeps losing air.
3. **Story beats are lived together, never spoiled.** One player can't trigger a major beat alone. Both see it at the
   same moment.
4. **Server-authoritative, no new dupes.** Every transfer (items, air, revives) is validated by the server. Reuse the S7/S8
   bookkeeping.
5. **Everything has a switch.** Server options in `server.cfg` for rules, client settings for presentation. The defaults
   are this couple's choices.
6. **Bilingual strings.** New texts go into `Nitrox.Assets.Subnautica/LanguageFiles/en.json`, and into `de.json` if it
   exists. A missing key fails the build (NXLZ001).

## 3. Overview and order

| ID | Feature | Size | Needed by (story) | Depends on |
|---|---|---|---|---|
| **F1** | Faint partner signal + close-range name tag | S–M | Start | — |
| **F2** | Context pings (danger / look / come here) | S | Start | — |
| **T4** | Cyclops crew roles | S + probe | Mid (Cyclops) | — |
| **T2** | Buddy breathing | S | Early | — |
| **R1a** | Sleep status names the player still awake; eating and drinking sounds audible | S | Early | — |
| **T3** | Hand items over | S–M | Early | S7 (merged) |
| **G0** | Story-gate foundation: roster, solo lock, ready/start, admin release | M | **Early** (drive core) | — |
| **G4** | Aurora drive core gate | M | **Early–mid** | G0 |
| **G1** | Gun gate | S | Late | G0, S3 (merged) |
| **G2** | Rocket gate: launch together | S–M | End | G0 |
| **G3** | Sea Emperor, hatching and cure gates (offline-cure fix done: FIX_SPEC S12) | L | Late | G0 |
| **T1** | Rescue window | L | Early | — |
| **P2** | Containment eggs survive (#2538) + aquarium check | M | Mid | — |
| **P1** | Cuddlefish follows and plays with both | M | Mid | P2 helps |
| **R1b** | See each other sit, sleep and use things (adopt PR #2859) | L | Any | — |
| **T5** | Seamoth hitch-a-ride | L | Early–mid | — |

**Execution order:**

1. **Wave 1, quick wins:** F1, F2, T4, T2, R1a. Each is small, and most need no server changes.
2. **Wave 2, before the first story gate in play:** T3, G0 + G4. The drive core comes early in the story, so G0 has to be
   ready by then.
3. **Wave 3:** T1 (rescue window), P2, P1.
4. **Wave 4, before late game:** G1, G2, G3, R1b, T5.

R1b and T5 are the riskiest. Do them when everything else is stable.

**New save-format tags:** `EntityMetadata` uses up to 94 (S1 = 92, S5b = 93, S10 = 94). This spec reserves **95**
(`CuddlefishMetadata`, P1) and **96–99** for later features. Any new entity or metadata type needs a `SaveLoadRoundTrip` test.

---

## 4. Story gates (shared story moments)

### G0: Gate foundation, the roster and the solo lock

**Goal:**
- A major beat starts only when both players are there and have each confirmed it.
- If one player is offline, the beat is locked and the game says why.
- Solo play stays possible for everything else.

**Today:**
- There is no gate.
- Story goals are world-wide: `StoryGoal_Execute_Patch` → server `StoryGoalExecutedProcessor` (stores the goal in
  `CompletedGoals`, relays it) → the partner runs `StoryGoal.Execute`, which fires every goal listener on the partner's
  game wherever they are (`game:Story/StoryGoalManager.cs:69-92`).
- **Template to copy:** `Nitrox.Server.Subnautica/Models/GameLogic/SleepManager.cs:22-49`. It keeps a ready set, broadcasts
  status, resets on disconnect, and shows its client message through `Log.InGame` (`SleepStatusUpdateProcessor.cs:15`).

**Design:**
- **Server `StoryGateService`** keeps ready sets per gate kind.
  - The roster comes from the new option `StoryGatePlayers = "Name1,Name2"` (comma-separated). If it's empty, all saved
    players count, but bot/test players would inflate that, so prefer the explicit list.
  - **Option `StoryGatesEnabled`** (default true for us).
- **Packets:**
  - `StoryGateReady(kind, ready)`: client → server.
  - `StoryGateStatus(kind, readyNames, missingNames, locked)`: server → all.
  - `StoryGateStart(kind)`: server → all.
- **Client pattern:**
  - Each gate is a Prefix that returns false and sends "ready".
  - On `StoryGateStart`, the client re-invokes the original method with a skip flag. The `skipPrefix` pattern already
    exists in `NitroxPatcher/Patches/Dynamic/Incubator_OnHandClick_Patch.cs:18-54`.
  - For click gates, the click itself proves presence, because the game only accepts clicks within range.
- **Ready is cleared** when the player leaves the area (where the game has an area exit), disconnects, or after a timeout.
- **Locked:** if someone on the roster is offline, the server answers `locked` and the client shows "Waiting for <name> to
  come online".
- **Admin command `storygate release <kind>`:** an escape hatch if one partner stops playing for good.
- **Time is not frozen** while solo. It drives day/night and every schedule, and shifting the Aurora and Sunbeam
  schedules is risky (`StoryManager.cs:119-124`). The Aurora explosion can happen solo; it's a world event, not a
  shared-moment beat.
- **Radio gate (decided):** block playing the `RadioSunbeam4` radio message while solo, so the Sunbeam countdown
  (2400 s, started by the radio, `game:StoryGoalCustomEventHandler.cs:63,122-126`) can't start without the partner.
  - Hook: the radio click goes through `Radio_PlayRadioMessage_Patch`. Answer "locked" with the absent partner's name.
  - It's a lock only, with no "both confirm" step: the message plays normally once both are online.

**Tests:**
- **L1:** the gate state machine as a plain class: ready set, roster parsing, locked logic, timeouts.
- **L2** (`ServerHarness` + 2 `FakePlayer`s):
  - both ready → `StoryGateStart` reaches both;
  - one ready → status 1/2 reaches both;
  - a disconnect clears ready;
  - solo with a roster → locked, naming the absent player;
  - option off → immediate start;
  - admin release works.

### G1: Disabling the gun
- **Gate:** a Prefix on `PrecursorDisableGunTerminal.OnHandClick` (`game:` 110-135), **only when `playerCured`**. The
  "access denied" path for an uncured player stays ungated. Clear ready on `OnTerminalAreaExit` (`game:` 191).
- **Why a hold is needed:** once `gunDisabled` is true, the terminal never opens again (`game:` 178). Without the gate, the
  second player can never use it.
- **Already in place:** our S3 patch syncs `gunDisabled`.
- **Tests:**
  - L2: the gate flow.
  - L3: both teleport to the terminal and use it; both cinematics start together; both have `gunDisabled`.
- **Size:** S.

### G2: Rocket launch together
- **Gate:** a Prefix on `LaunchRocket.OnHandClick` (`game:` 352), *before* `SetLaunchStarted`.
  - Don't gate inside our transpiled `BroadcastRocketLaunch`: `launchStarted` is already true there, and
    `NitroxClient/GameLogic/Rockets.cs:38-41` returns early, so the first player to click would never launch.
- **Today:** `RocketLaunch` has no server processor, so it's just relayed (`DefaultServerPacketProcessor.cs:53`). A partner
  who isn't aboard (more than 5.55 m from the `AtmosphereVolume`, underwater, or in a sub, `Rockets.cs:53-58`) is left
  behind while the other player watches the credits.
- **Design:** "ready" = clicked launch while aboard. The server checks aboard from the player's position. Add a real
  `RocketLaunch` processor so it's no longer relayed before everyone is ready.
- **Note:** the transpiler instruction count in `PatchesTranspilerTest` changes.
- **Tests:**
  - L2: no relay before both are ready; aboard check from positions.
  - L3: both aboard, both get `launchStarted`. Needs a fixture with a built rocket.
- **Size:** S–M.

### G3: Sea Emperor, hatching and the cure

Three beats. Size L in total.

- **3a, meeting the Emperor:**
  - There's no click to gate. `PrecursorAquariumPlatformTrigger.OnTriggerEnter` (`game:` 46) starts the Emperor's
    animation, and the dialog runs from animation events (H).
  - Gate the trigger: hold until both players are inside the platform volume, then start the same animator state on both.
  - The trigger won't fire again while a player stays inside it, so the start must drive the animator directly.
  - Goal `Precursor_Prison_Aquarium_EmperorLog1` (H: that it's `onDialog1Goal`).
  - **Today:** the telepathy effect plays for the partner wherever they are, and nothing else is synced.
- **3b, hatching:**
  - `Incubator.OnHandClick` (`game:` 126), hooked into the existing patch before its lock request.
  - Only the player carrying the enzymes hatches; the partner confirms by clicking the incubator.
  - The hatch already shows for both. The hatching music only plays for the clicker (`game:` 140-141), so play it for both
    on start.
- **3c, the cure:**
  - `EnzymeBall.OnHandClick` (`game:` 85-108): both click a ball, then both cures start together.
  - Nitrox has no `EnzymeBall` or `DropEnzymes` code today.
  - **The offline-partner part is already fixed** (FIX_SPEC S12, 2026-11-02): the server now applies the completed
    infection goals on join (`InfectionStory`).
    - What remains for G3: today an online partner is cured remotely and instantly, with no cinematic
      (`game:StoryGoalCustomEventHandler.cs:171-174`), and their cure balls stop working (`game:EnzymeBall.cs:53-59,87`).
    - The gate gives both players their own cure moment.
  - Each game spawns its own cure ball (`DropEnzymes.SpawnCureBallAsync`). Whether the partner even sees one is (H).
- **Tests:**
  - L2: the gate flows.
  - L3: a fixture inside the containment facility; both end at infection 0. Plus the offline-cure regression: cure with B
    offline (gate released by admin), B rejoins cured.

### G4: Aurora drive core
- **There's no single click.** Players weld many radiation leaks, and the last weld calls `LeakingRadiation.NotifyLeaksFixed`
  (`game:LeakingRadiation.cs:106-122`).
- **Today:** leak health is metadata, and the partner runs their own `NotifyLeaksFixed` through `LiveMixinManager.cs:146-153`.
  The beat needs no presence.
- **Rule (decided 2026-11-02):**
  - Gate only the **final** weld. When one leak is left, the welder doesn't finish it until both players are in the drive
    core room. The server knows positions (`PlayerMovementProcessor.cs:22`).
  - Message: "Wait for <name> to finish the repair together".
  - Hook: `Welder.Weld` (`game:` 38), or `LiveMixin.AddHealth` on a `LeakingRadiation` leak when it's the last one.
- **Goal key:** `leaksFixedGoal`, read it at runtime (H).
- **Tests:**
  - L2: the gate flow.
  - L3: a fixture after the explosion; the `fixleaks` console command (`game:LeakingRadiation.cs:130`) on all leaks but
    one; the last weld is held until B arrives.
- **Size:** M.

### Seeing each other in cinematics
Most of this is covered by R1b (adopting #2859). Story cinematics are first-person, so "together" means each player sees
their own cinematic **at the same moment**, which is what the gates do. The partner's body in the animation (e.g. at the
gun terminal) comes with R1b, where the object has a Nitrox entity. The enzyme ball has none (H).

---

## 5. Teamwork survival

### T1: Rescue window
**Goal:** at 0 health you go **down** instead of dying. Your partner has `RescueWindowSeconds` (default 60) to revive you:
- a **first aid kit** gives 50 health (`kFirstAidKitHealth`);
- **holding the interact key** gives about 20 health.

Oxygen keeps draining while down. Drowning, explosions and lava still kill.

**Today:**
- `LiveMixin.TakeDamage` sets health first (`game:LiveMixin.cs:214`), then calls `Kill` (`:259`). Kills during
  cinematics are put off and run later (`:257-265`, `:407-411`).
- `Kill` is also called directly: drowning (`game:Player.cs:2616-2621`), the Cyclops explosion
  (`CyclopsDestructionEvent.cs:131`), the `kill` command.
- `Player.OnKill` (`game:` 2110) disables camera and input, then runs `ResetPlayerOnDeath` after 5 s → `playerDeathEvent`
  → Nitrox's `PlayerDeathBroadcaster` (death beacon, `PlayerDeathEvent`). `KeepInventoryOnDeath` is handled by
  `Inventory_LoseItems_Patch`.
- The partner's body has an invincible `LiveMixin` and a trigger capsule on the Useable layer (`RemotePlayer.cs:422-430,516`).
  `GUIHand` can already target it within 2 m (`game:GUIHand.cs:85`, `Targeting.cs:77,139`).

**Design:**
- **One hook:** a Prefix on `LiveMixin.Kill` for `Player.main.liveMixin`.
  - It **lets the kill through** if the player is already downed, oxygen is about 0, `currentSub.subDestroyed` is true, the
    damage type is lava/heat (recommended), or the feature is off.
  - **Otherwise the player goes down:**
    - `health = 1`. It must stay above 0, or `SuffocationDie`'s `IsAlive()` check stops drowning from killing.
    - `invincible = true`, so other damage is ignored. `Kill` ignores invincibility, so the timer and drowning still work.
    - Input is locked as in `OnKill`, but the camera stays free.
    - `IsFreeToInteract` is blocked.
    - A pulsing suffocation-style fade, and a small countdown.
    - **Do not `FreezeStats`**: that would stop oxygen draining.
- **Server owns the timer:**
  - Packets: `PlayerDowned`, `PlayerReviveRequest(target, method, kitId?)`, `PlayerRevived(target, health)` (to all), and
    `PlayerRescueExpired(target)`.
  - It accepts a revive only from another player who isn't downed. The kit must be in the rescuer's inventory; the server
    removes it and sends `EntityDestroyed`.
  - If everyone is down, or nobody else is online, it expires at once.
  - On expiry the client turns off `invincible` and lets `Kill` through, so vanilla and Nitrox death run unchanged.
- **Partner UX:**
  - A new `IHandTarget` on the remote body (`RemotePlayer.SetupBody`, `:404`).
  - "Hold to revive" with `GameInput.GetButtonHeld` + `HandReticle.SetProgress`.
  - A first aid kit in hand = instant revive.
  - The downed pose from the `player_death` animator trigger (H: Nitrox's `AnimationController` may override it).
- **Option `RescueWindowSeconds`:** 0 disables the feature. It's sent in `InitialPlayerSync`.

**Decided (2026-11-02):**
- **Downed in a vehicle:** the pilot is ejected (`ToNormalMode`) next to the Seamoth or Prawn, so the partner can reach them.
  The Cyclops needs no ejection; a powered Cyclops is breathable.
- **Quitting while down:** you rejoin still down, with the remaining time.
  - The server keeps `downedUntil` in the player's saved record, so it survives a server restart.
  - It is cleared on revive or death, and the server's timer is paused while that player is offline.
- **Lava/heat kills** skip the downed state.

**Tests:**
- **L2:**
  - the timer expires;
  - a revive is only accepted from the partner;
  - the kit is removed for both;
  - everyone down → expires;
  - death clears the state.
- **L3** (no-damage/no-oxygen cheats **off**):
  - lethal damage → downed;
  - B revives → A is up;
  - the timer runs out → death and beacon;
  - underwater, oxygen reaches 0 → death.
  - The bridge needs a new "hold use" command; `use` only clicks once.

**Size:** L.

### T2: Buddy breathing
- **Today:**
  - `OxygenManager` (`game:` 11, 88, 104, 120) holds the lungs plus tanks.
  - The partner's oxygen arrives every 3 s in `PlayerStats` (`PlayerStatsBroadcaster.cs:9,33`).
- **Design:**
  - Hold interact on a partner whose oxygen is below about 30%, or who is downed. The donor needs a tank and keeps a
    reserve.
  - Every 0.5 s: donor `RemoveOxygen(n)` → `OxygenShare(target, n)` → a **server processor** validates it, clamps it, and
    sends it **only to the target**. Without a processor, `DefaultServerPacketProcessor` would broadcast it.
  - The receiver calls `AddOxygen(n)`.
  - Feel: the reticle progress bar plus a bubble sound from the remote player's emitter. No new HUD.
- **Tests:**
  - L2: target-only delivery, clamping, unknown target dropped.
  - L3: A's oxygen rises by about what B's falls.
- **Size:** S.

### T3: Hand items over
- **Today:**
  - The partner's inventory already exists on both sides: `InventoryItemEntity` children of the `PlayerEntity`, mirrored
    into `RemotePlayer.Inventory` (`RemotePlayer.cs:96-98`).
  - A reparent to the local player already lands in the local inventory (`EntityReparentedProcessor`, `InventoryItemReparented`).
  - `ItemContainers.cs:33` deliberately sends nothing for remote-player containers.
  - The server's `EntityReparentedProcessor` checks neither ownership nor capacity.
- **Design:**
  - A "Give" action on the partner's body opens the vanilla two-panel transfer screen:
    `Inventory.main.SetUsedStorage(remote.Inventory)` + `PDA.Open(PDATab.Inventory)`, as the bioreactor does
    (`game:BaseBioReactor.cs:888-891`).
  - On the partner's container copy:
    - `isAllowedToRemove` = false, so nobody can take things;
    - `isAllowedToAdd` = false + send `ItemGiveRequest`, so nothing moves until the server agrees.
  - **Server checks:** the item's parent is the sender, it's an `InventoryItemEntity`, it's not equipped, and the target is
    online.
  - **Server applies:** reparent, then `EntityReparented` to everyone. The receiving side uses S7's `AddFromRemote`.
  - Same id, never destroyed or respawned, so no dupes. A tool's battery (S8) moves with it (H).
  - Put away the held item first.
- **Tests:**
  - **L2 does most of it:** owner check, equipped items refused, late joiner sees the item under the receiver, both
    clients get the reparent, `SaveLoadRoundTrip`.
  - L3: give one item; both inventories are right; relog.
- **Size:** S (one-step) to M (with a capacity check on the receiving client).

### T4: Cyclops crew roles
- **The game doesn't block it:**
  - No ability button checks piloting; they only check `currentSub`: sonar `game:CyclopsSonarButton.cs:71,95`, shield
    `CyclopsShieldButton.cs:47,64`, decoy `CyclopsDecoyLaunchButton.cs:46,69`, silent running
    `CyclopsSilentRunningAbilityButton.cs:39,56`, fire suppression `CyclopsFireSuppressionSystemButton.cs:54,76`, cameras
    `CyclopsExternalCamsButton.cs:28,45`.
  - The helm screen only fades for non-pilots.
- **Nitrox is the blocker:**
  - `CyclopsHelmHUDManager_Update_Patch.cs:15-22` forces the screen visible but stops non-pilots clicking it.
  - Shield, sonar and silent running are already synced as `CyclopsMetadata`. Decoy and fire suppression are synced as
    packets (`Cyclops.cs:36-72`).
  - Power use runs only on the client that simulates the sub, so it won't double.
- **Design:**
  - Let non-pilots click the helm screen.
  - Gate only engine, motor mode and horn behind `Player.main.isPiloting` (Prefixes).
  - The co-pilot stands behind the helm and runs abilities and cameras.
  - **First probe** (bridge or one game run): can a standing player reach the buttons (H)?
- **Tests:**
  - L2: Cyclops state set by the player who isn't steering is stored and relayed.
  - L3: B toggles each ability and A sees it; power drains once.
- **Size:** S.

### T5: Seamoth hitch-a-ride
- **The game has no towing.**
  - Vehicles attach the pilot through `EnterLockedMode`, which makes the game treat the player as not underwater
    (`game:Player.cs:1859-1862`), so no air is used. **Don't reuse it.**
- **Design:**
  - **Hitcher's client:**
    - stays in Normal mode, so air drains;
    - `playerController.SetEnabled(false)`;
    - parents to a handle object we create on the Seamoth hull (no handle in the prefab, H);
    - the body is kinematic and ignores collisions with the vehicle;
    - stops sending `PlayerMovement`.
  - **Packets:** `PlayerHitched(vehicleId, handle)` / `PlayerUnhitched(position)`.
  - **Pilot's client:** attaches the partner's body to the handle (`RemotePlayer.Attach`, `RemotePlayer.cs:128`).
  - **Server:** keeps the hitch state for late joiners; it isn't saved.
  - **The hitcher lets go on:**
    - the Exit key;
    - being downed;
    - the vehicle being destroyed: detach in a Prefix in `Vehicle_OnKill_Patch`, or the attached player is destroyed
      with it (`game:Vehicle.cs:752`);
    - docking starting (`VehicleDockingBay.DockVehicle`, `:233`);
    - the pilot getting out.
  - **Seamoth only (decided 2026-11-02).** No Prawn suit hitching.
- **Risks:**
  - physics fighting;
  - terrain clipping;
  - world streaming while being dragged;
  - doubled interpolation lag on the pilot's screen (`MovementReplicator.cs:12`).
- **Tests:**
  - L2: hitch relay, join payload, release position.
  - L3: hitch, drive 200 m, air drains, release, dock-release, destroy-release.
- **Size:** L. Do it last.

---

## 6. Finding & signalling

### F1: Faint partner signal, close-range name tag
- **Today:**
  - **The marker:** each remote player already has a `PingInstance` (a clone of `Signal.prefab`, labelled "Player <name>",
    in the player's colour, hidden from the beacon manager) (`PlayerModelManager.cs:68-89`). A per-partner show/hide toggle
    is stored on the server (`uGUI_PlayerPingEntry.cs:121-130`, `SignalPingPreferenceChangedProcessor.cs:9`).
  - **The game's own rendering is already faint:** the icon is hidden within `minDist`, fades in up to 0.5 alpha over
    `range`, and the label shows only when you look at it. All markers are hidden while sitting, in cinematics or in beds
    (`game:uGUI_Pings.cs:89-119,160,195,202-221`).
  - **The name tag** (`RemotePlayerVitals`: name + 4 vital bars) is **always** shown (`RemotePlayerVitals.cs:86-99,199-215`).
  - **Client settings exist:** `NitroxPrefs`, `NitroxSettingsManager.AddSetting` (toggle/slider/list).
- **Design:** a component on each remote body, updated about 4 times a second.
  - `minDist` is about 12 m, or "hide" while the partner is in line of sight and under about 25 m.
  - `range` is about 150 m, so the marker stays faint.
  - The name tag is only visible under about 12 m.
  - Depth goes into the label via `Ocean.GetDepthOf` (`game:Ocean.cs:51`).
  - **Settings:** partner signal Off / Faint / Always; a name-tag range slider.
  - **Never call `SetVisible`/`SetColor`** on these markers: `PingInstance_Set_Patches` would send
    `SignalPingPreferenceChanged` and overwrite the stored preference. Only change fields.
- **Tests:**
  - L1: the visibility rule as a pure function.
  - L3: teleport to 5/30/200 m, read `minDist` and the name-tag state through the bridge, take screenshots.
- **Size:** S–M.

### F2: Context pings
- **Today:**
  - `PlayerPingCreated` carries session, text, position and id. The label is built in English on the sender side
    (`PlayerPingManager.cs:114`).
  - Limits: 250 m, 3 pings per player, 0.5 s cooldown, 10 s lifetime.
  - The server only relays it.
  - Nitrox key bindings fire on key-down only.
- **Design:**
  - Add a `PingKind` field; the **receiver** builds a translated label.
  - A tap picks the kind from the crosshair hit, which is already known:
    - aggressive creature → **Danger** (red Signal icon, 15 s);
    - pickupable/breakable/drillable → **Look** (that item's own `SpriteManager` icon, 10 s);
    - else → **Come here** (Beacon icon, until the partner arrives or 30 s).
  - A second key binding, "Ping: come here" (`KeyBindingManager.cs:11-17` + a language key).
  - A hold-radial menu comes later; it needs key-release wiring.
- **Risk:** a custom icon may be reset when `uGUI_Pings` re-adds markers (H). Patch `uGUI_Pings.OnAdd` if so.
- **Tests:**
  - L2: the kind reaches B, the sender gets nothing back.
  - L3: B's marker icon and label, read through the bridge.
- **Size:** S.

---

## 7. Cozy home

### R1a: Small comforts (S)
- **Sleep:** `SleepManager` already waits for everyone, but says only "{SLEEPING}/{TOTAL} players sleeping" (`en.json:96`).
  Name who's still awake: "Waiting for <name>…".
- **Eating and drinking:** the game has no eating animation, only a sound (`game:Survival.cs:187`). Those sounds are marked
  not synced in `SoundWhitelist_Subnautica.csv:376-378`. Turn them on with about a 10 m radius, so you hear your partner
  eat.
- **Tests:**
  - L2: sleep flow: A in bed, both see "1/2" with the right name; B in bed, sleep completes; a disconnect mid-wait.
  - L1: whitelist entries.

### R1b: See each other sit, sleep and use things (adopt upstream PR #2859, L)
- **Why it's off:** all remote-player cinematic sync patches have been in `#if false` since 1.8 (#2349 → #2287: benches
  broke and players got stuck after animations). So today the partner doesn't see you lie down, and the sitting pose may
  be fought by walking animations (`RemotePlayer.cs:161`, H).
- **PR #2859** (open, no reviews; 32 files, +1045/−127):
  - re-enables the patches;
  - adds an "in cinematic" state that stops movement packets fighting animations;
  - seat positions and the chair pose;
  - two players sharing a bed, with a lock per side;
  - escape-pod ladder animations.
- **Merge cautions:**
  - It builds on the closed #2642; check that its review points were addressed (per-pod state, per-frame logging,
    missing serializer/world-test entries).
  - It **changes `EscapePodMetadata`**, a save-format change that needs a `SaveDataUpgrades` step.
  - It conflicts with S9 in `EscapePodManager.cs`.
  - Its bed patch lets the game's own bed code run, which starts a **local time skip** (`game:Bed.cs:203`). Our clock is
    server-driven (`DayNightCycle_Update_Patch`): verify that, or keep our skip suppression. `SleepManager` waiting for
    everyone stays.
- **Tests:**
  - L3: remote animation flags and seat position (within 0.3 m); sit/stand 20 times to catch the #2287 regression; lying
    down in the bed visible to the partner; gun terminal / ladder animations.
  - L2: any new packet fields.

## 8. Pets & aquariums

### P2: Containment eggs survive (#2538) + aquarium check (M)
- **Likely cause of #2538 (H):**
  - Containment tanks are global entities, so their simulation lock goes to whoever joined first.
  - Birth, hatching and growing are gated on that lock (`WaterParkCreature_BornAsync_Patch.CanBeBorn`,
    `CreatureEgg_Hatch_Patch`, `WaterParkCreature_ManagedUpdate_Patch`).
  - But the game **destroys the ripe egg on every player's game, unconditionally** (`game:CreatureEgg.cs:149-165`).
  - If the egg ripens on the game of the player who doesn't hold the lock, while the lock holder is away, the egg is
    destroyed there, no creature is born, and the server is never told.
- **Fix:** the player without the lock keeps the egg until the lock holder's hatch arrives. Lock handover for base modules
  follows the S7 step 2 rule (`BaseModuleSimulation`).
- **Aquarium:** fish are items in the aquarium's storage, and the swimming models are local copies
  (`game:Aquarium.cs:105-168`). So the partner sees them as long as the container owner resolves
  (`InventoryContainerHelper.cs:67-69`, H). **Verify only.**
- **Tests:**
  - L2: lock owner of the tank; handover on disconnect.
  - L3: an egg ripens (fast-hatch cheat, `game:CreatureEgg.cs:129`) while only the player without the lock is near; it must
    not be lost. Reverting the fix must bring the loss back. B sees the aquarium fish.

### P1: Cuddlefish follows and plays with both (M)
- **Today:**
  - In the game, the cuddlefish befriends and follows the **local** player (`game:CuteFish.cs:81`,
    `CreatureFollowPlayer.cs:24-38`). It warps to within 40 m of them every 5 s (`CuteFish.cs:99-121`). Tricks only happen
    within 6 m and in view (`PlayAnimation.cs:20-42`).
  - In Nitrox, the AI runs only on the simulating player's game (`SimulationWhitelist.cs:45`,
    `Creature_ChooseBestAction_Patch`). That lock often belongs to whoever joined first and never moves
    (`EntitySimulation.cs:133-147`). So **it follows only one of you**.
  - Follow/stay and the farewell flag aren't synced or saved. Tricks aren't synced: creature action sync covers only four
    leviathans (`AI.cs:24-36`).
- **Design: last interaction wins.**
  - Playing with it or giving a command requests an **exclusive** lock (bench pattern), then drops back to a normal lock
    after the play scene (`Bench_ExitSittingMode_Patch.cs:30` pattern). It then follows whoever played with it last.
    The game's own "friend" logic needs no change.
  - New `CuddlefishMetadata` (**tag 95**) holds follow/stay and the farewell flag.
  - Only the simulating player runs the warp.
  - "Nearest player" handover is not recommended: the pet would bounce between you.
  - Visible tricks for the partner come with R1b (H).
- **Tests:**
  - L1: metadata round trip.
  - L2: an exclusive request moves the lock and keeps it after dropping back; handover on disconnect.
  - L3: B plays with it, and it follows B; follow/stay persists across a restart.

---

## 9. Decisions (made with the players, 2026-11-02)

| Topic | Decision |
|---|---|
| G4 drive core | **The last weld waits.** Weld as you like; the final leak only seals once both players are in the drive core room. |
| Sunbeam radio | **Waits for both.** While one player is offline, the `RadioSunbeam4` message can't be played, so the Sunbeam arc never starts alone (part of G0). |
| T1 downed in a vehicle / quitting while down | **Eject + rejoin downed.** A downed pilot is pushed out of the Seamoth or Prawn. Quitting while down means rejoining still down, with the remaining time. |
| T5 vehicles | **Seamoth only.** |

## 10. Bugs found during this research (tracked in FIX_SPEC)

- **The partner offline at cure time stayed infected forever:** fixed (FIX_SPEC S12).
- **#2538:** probable cause found (P2).
- **Players rejoining at an unrelated position after a restart:** a test-rig artifact, not a game bug (FIX_SPEC S11).
- **Benches:** walking animations may fight the sitting pose (R1b).

## 11. Definition of done (per feature)

Same as AGENTS.md §7:
- a branch `feat/<id>-<slug>` from `coop`;
- `dotnet build` + `dotnet test` green;
- headless tests for the server half; **one** in-game scenario for the game half (budget rules in TESTING.md);
- new strings in the language files;
- options documented in `docs/fork/PLAYING.md` (once it exists) or here;
- a Log row at the bottom of this file.

## Log

| Date | Feature | Change | Verified by |
|---|---|---|---|
| 2026-11-02 | — | Spec created from player alignment + feasibility research (3 read-only investigations) | — |
