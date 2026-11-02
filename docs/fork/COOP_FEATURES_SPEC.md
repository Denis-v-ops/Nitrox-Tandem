# Co-op features spec: playing the story *together*

Subnautica is a single-player game about isolation, awe and fear. This spec adds what two people need to experience the
story **together** without turning it into a different game. It complements [FIX_SPEC.md](FIX_SPEC.md), which covers bugs.
This spec covers new features. How to build and verify them: [AGENTS.md](../../AGENTS.md),
[TESTING.md](TESTING.md).

Researched 2026-11-02 against `coop` @ `2edaf402` and the decompiled game 83031 (`F:\nitrox\game-src\83031`). The fear
and difficulty review (§2.1–§3, §8, the prices on T1/T2/F2), R2, the basics sweep and B items (§12), the wave-0
groundwork (§4) and the README rule (§13.1) were added the same day against `coop` @ `f0ddf7bf`. File
references are `path:line` in the repo; `game:` means the decompiled source. **(H)** marks an unverified hypothesis that
must be checked before coding.

## 1. Design decisions

The target is two players playing together **in the same room**, so voice chat is out of scope. Decisions from
2026-11-02:

| Question | Choice |
|---|---|
| Focus areas | Shared story moments, teamwork survival, finding & signalling, cozy home |
| Cozy home means | **Resting together** (benches, beds, visible animations); **pets & aquariums** |
| Partner visibility | **Subtle signal**: a faint beacon-like marker with name and distance; name tag only when close |
| Partner's signal | **Always on, but faint**: fades when close or in sight. In the deep it drops to "last contact" (D2, second round) |
| Running out of health | **Rescue window**: the player goes down; the partner revives with a first aid kit, or by hand at low health; oxygen keeps draining |
| Story beats that wait for both | **Disabling the gun, rocket launch, Sea Emperor & cure, Aurora drive core** |
| One player alone | **Solo is OK, but the story waits**: those beats stay locked until both are online |
| Teamwork | **Buddy breathing, hand items over, Cyclops crew roles** |
| Seamoth (one seat) | **Hitch a ride**: the partner holds on outside, still underwater, still using air |

Second round, on fear and difficulty (2026-11-02):

| Question | Choice |
|---|---|
| What balances all the safety features | **Hear each other** (D1), **the signal fades in the deep** (D2), **a distress call when downed** (D3) |
| Pricing the safety features | **Every help has a price** (§2.2): repeat downs shrink the rescue window, buddy breathing is slow and lossy, revives take time |
| The partner's discoveries | **Hear your own firsts** (D5): the partner's biome and location lines reach you silently and play when you get there yourself |
| Progression pace | **Keep vanilla numbers**: scans stay shared; no changes to fragments, resources or creatures |
| Together-plays | **Lights in the dark** (D6) |
| Distress where the signal has faded | **Pulses through**: the distress call shows the real position every few seconds, even in the deep |

Not chosen, so out of scope for now: voice chat, decorating together, notes for each other, discovery notifications,
freezing the world while one player is away, predators preferring a lone player, a Hardcore variant, more fragments for
two players, Overwatch (guiding the diver from the camera drone), dragging a downed partner.

## 2. Design principles: keep it Subnautica

1. **The partner is a signal in the dark, not an MMO HUD.** Use the game's own visual language: beacon markers, the hand
   reticle and its progress bar, PDA-style messages, existing sounds. **No new art.** Show the partner's state through
   sound and the world (pain, breathing, their light) before any marker.
2. **Danger stays dangerous, and every help has a price.** The rescue window is a chance, not immunity. Drowning, lava,
   heat and explosions still kill. A downed player keeps losing air. Every help in this spec has a cost, a limit or an
   escalation (§2.2). A help without a price doesn't ship.
3. **The deep cuts the line.** The deeper and darker it gets, the less you know about your partner: a faint marker in the
   shallows, only a last contact in caves and the deep, sound only up close. The players sit in the same room and can
   always talk, so in-game *information* is the lever. Without a marker they describe landmarks to each other and worry
   about each other, and that is the fun.
4. **Firsts belong to whoever is there.** Each player gets their own first time in a place. The partner's PDA never
   narrates it from across the map.
5. **Story beats are lived together, never spoiled.** One player can't trigger a major beat alone. Both see it at the
   same moment.
6. **Vanilla balance.** No changes to fragment counts, resources, creature stats or survival needs. Co-op's natural
   speed-up (shared scans, two inventories) is accepted: it only pays off when you split up, and being alone is where
   the fear is.
7. **Server-authoritative, no new dupes.** Every transfer (items, air, revives) is validated by the server. Reuse the S7/S8
   bookkeeping.
8. **Everything has a switch.** Server options in `server.cfg` for rules, client settings for presentation. The defaults
   follow the decisions in §1.
9. **Bilingual strings.** New texts go into `Nitrox.Assets.Subnautica/LanguageFiles/en.json`, and into `de.json` if it
   exists. A missing key fails the build (NXLZ001).

### 2.1 What a partner does to Subnautica

**What makes it work solo:**
- **A curiosity loop.** A radio call or signal leads to an expedition, the expedition to fragments, fragments to a
  blueprint, and the blueprint to the next depth. Depth is both the progression gate (vehicle depth modules) and the
  fear gauge.
- **Fear of the unknown, not combat.** You hear the Reaper before you see it. Darkness, limited air, and no weapon that
  solves a leviathan.
- **Isolation.** The PDA is your only companion. Empty lifepods and the Degasi logs tell of people who didn't make it.
- **Home and mastery.** A base, then a moving base (the Cyclops). But the deep never becomes safe.

**What a partner puts at risk:**
1. **Safety in numbers.** A friend nearby makes the dark less frightening. Co-op horror games win the fear back with
   separation, silence, and not knowing what the other player is facing.
2. **Too much information.** An always-visible marker, vitals bars and pings turn "where are you?" into a HUD.
3. **Remote narration.** Verified in code: the partner's PDA lines play on your game wherever you are (D5).
4. **Stacked helps.** Every feature in the first version of this spec made the game safer. Without prices, two players
   who stay together can hardly die.

**What a partner can add (why co-op can beat solo):**
- **Fear for someone else.** In co-op horror you fear for yourself *and* for your partner (HICSS 2024 study). D1–D3 turn
  that into play: you hear them get hurt, lose their signal, race to their distress call.
- **Roles.** One pilots and one runs sonar and shields (T4). One draws a leviathan and the other scans it (needs D4).
  One lights the way (D6).
- **Shared awe.** The big beats happen to both at the same moment (§5).
- **Needing each other.** Air from a buddy (T2), a ride (T5), a rescue (T1). Each has a price, so each stays an
  emergency.

**What Subnautica 2 does** (co-op from the start, Early Access since May 2026): it balances for single player first, and
co-op is optional. Leviathans decide which player to react to, so one can bait while the other scans. The one-seat
Tadpole has handles that companions hang on to (our T5). Scans are shared, and wearables have to be crafted by each
player. The coverage doesn't mention a revive.

Sources: [Inven: co-op in Subnautica 2](https://www.invenglobal.com/articles/22118/better-together-even-as-squid-bait-co-op-in-subnautica-2),
[Comicbook: co-op not at the expense of solo horror](https://comicbook.com/gaming/feature/subnautica-2-leans-into-co-op-gameplay-but-not-at-the-expense-of-solo-horror/),
[GameWatcher: impact on isolation and terror](https://www.gamewatcher.com/news/subnautica-2-developer-admits-it-wont-be-able-to-predict-multiplayer-impact-feelings-isolation-terror),
[HICSS 2024: fear in co-op survival horror](https://scholarspace.manoa.hawaii.edu/items/3f16df70-ddd8-47d8-8b58-a7698eee2d62),
[Game Developer: Cursed Companions postmortem](https://gamedeveloper.com/design/how-we-developed-the-co-op-horror-game-cursed-companions),
[GDC 2019: The Design of Subnautica](https://gdcvault.com/play/1025745/The-Design-of-Subnautica).

### 2.2 The price of each help

| Help | Price or limit | Item |
|---|---|---|
| Rescue window | Shrinks on repeat downs: 60 s → 30 s → none, until a sleep in a bed or 15 min without going down. A hand revive is a 6 s hold, a first aid kit 2 s. Air keeps draining. Lava, heat, drowning and explosions still kill. At expiry, vanilla death drops your unsecured items where you died | T1 |
| Buddy breathing | Slow and lossy: the donor pays 2 for every 1 the receiver gets. Tank only; the donor keeps a reserve. It slows drowning, it doesn't refill | T2 |
| Partner signal | Faint in the shallows, only a last contact in caves and the deep | F1, D2 |
| Pings | Travel like the signal: when either player is in interference, a ping only shows within about 50 m of the sender | F2 |
| Hand items over | No equipped items; the receiver needs room. It allows nothing that dropping and picking up didn't already | T3 |
| Cyclops crew | Needs both of you in the Cyclops; the co-pilot does nothing else meanwhile | T4 |
| Hitch a ride | The hitcher's air drains, no tools, creatures can still target them, they let go when downed | T5 |
| Distress call | Only while downed; the position pulses every few seconds, it isn't a live track | D3 |

## 3. The journey: key moments and what co-op does to them

The story in play order. Use it to check that each beat keeps what makes it work solo, and to schedule features ahead
of where the players are.

| Beat | Solo, it feels like | Co-op today, and the risk | Items |
|---|---|---|---|
| **Lifepod 5, Safe Shallows** (start) | Waking in a burning pod; wonder; the first fabricator | Both start in the same pod (FIX_SPEC S9). Low risk | F1, F2, R1a |
| **First dives:** Kelp Forest, Grassy Plateaus, Crash Zone | The first Stalker; a Reaper heard near the Aurora before it's seen | Marker and vitals always shown; the partner's PDA chatter reaches you; creatures hurt both players since S14 (the rest: D4) | D1, D4, D5, F1 |
| **Aurora explosion** (seeded timer, early) | Countdown, shockwave, radiation | A world event both get at once; it may happen solo (G0) | — |
| **Radio calls, lifepods, the Degasi trail:** Floating Island, Jellyshroom Cave, Deep Grand Reef | Loneliness; the first dark cave | The partner's marker still shows in caves; logs are narrated remotely | D2, D5, D6 |
| **Inside the Aurora:** radiation suit, laser cutter, fires, Prawn bay, Cuddlefish egg, drive core | Claustrophobia, radiation | The drive core beat needs no presence today | G4, P1 |
| **Mountains:** the gun, the infection, the Sunbeam | Hope that rescue is coming, then the Sunbeam is shot down | The radio can be played solo today | G0 (radio lock) |
| **Vehicles and bases:** Seamoth, Prawn suit, Cyclops | Mastery; the Cyclops as a moving home | The Seamoth has one seat; non-pilots can't use the helm screen | T4, T5, R1a, R1b |
| **Lost River and the Disease Research Facility** | Peak dread: brine, skeletons, Ghost Leviathans | The marker shows the partner everywhere; each game spawns its own warpers (B1); this is what the D items are for | D2, D3, D4, D6, T1, T2 |
| **Lava zones:** Alien Thermal Plant, Sea Dragon, Primary Containment Facility | Hostile and deepest; Prawn suit country | As the Lost River; T5 doesn't reach (Seamoth depth) | D2, D3, D4, T1 |
| **Sea Emperor, hatching, the cure** | Awe; a mother's plea; release | Telepathy plays for an absent partner; an online partner is cured remotely without the cinematic | G3 |
| **Disabling the gun** | Freedom | The terminal opens only once | G1 |
| **Neptune launch** | A bittersweet goodbye | A partner who isn't aboard is left behind | G2 |

## 4. Overview and order

Sorted by wave. "Needed by" is when the players first meet it in the story.

| ID | Item | Size | Wave | Needed by | Depends on |
|---|---|---|---|---|---|
| **A0** | Probe run: answer the (H) marks in one rig session, plus the bridge additions | S | 0 | — | — |
| **A1** | Shared plumbing: all options, saved player fields, `PlayerStats` flag | S | 0 | — | — |
| **A2** | Trial merge of PR #2859 (no build): list conflicts with R2, T1, T5, D1 | XS | 0 | — | — |
| **D4** | Threat parity: the creatures FIX_SPEC S14 didn't test hurt both players too | S probe (+ fixes) | 1 | **Early** | A0 |
| **B1** | Creature leftovers: Stalker teeth now; warpers and bleeders in wave 3 | XS (+ M + S) | 1 / 3 | Early / Lost River | — |
| **B2** | Building and crafting without loss or dupes | S–M | 1 | Early (first base) | — |
| **D5a** | Your own firsts, game side: the partner's biome and location lines stay silent for you | S | 1 | **Start** | — |
| **R2a** | The partner's pose and held item survive a rejoin | S | 1 | Start | — |
| **F1** | Faint partner signal + close-range name tag; marker rule slots for D2/D3 | S–M | 1 | Start | A1 |
| **D1** | Hear each other: pain and breathing from the partner's body | S | 1 | Early | A0 |
| **D6** | Lights in the dark: fixes from the A0 probe | S | 1 | Early | A0 |
| **T2** | Buddy breathing (slow, lossy) + the partner hand-target that T1 and T3 reuse | S–M | 1 | Early | A1 |
| **F2** | Context pings (danger / look / come here) | S | 1 | Start | — |
| **T4** | Cyclops crew roles | S | 1 | Mid (Cyclops) | A0 |
| **R1a** | Sleep status names the player still awake; eating and drinking sounds audible | S | 1 | Early | — |
| **D5b** | Your own firsts, server side: per-player heard set, so rejoins keep it | S–M | 2 | Second session | D5a, A1 |
| **T3** | Hand items over | S–M | 2 | Early | T2, S7 (merged) |
| **G0** | Story-gate foundation: roster, solo lock, ready/start, admin release | M | 2 | **Early** (drive core) | A1 |
| **G4** | Aurora drive core gate | M | 2 | **Early–mid** | G0 |
| **B4** | Aurora fires | M | 2 | Early–mid (Aurora) | A0 |
| **D2** | The deep cuts the line: the signal drops to last contact in caves and the deep | M | 2 | Early–mid (Jellyshroom Cave) | F1, A1 |
| **R2b** | The partner's arm states: tool use, PDA, surface swimming, jumping | M | 2 | Early | A2 |
| **T1** | Rescue window, with escalation | L | 3 | Early | T2, A1 |
| **D3** | Distress call while downed | S | 3 | with T1 | T1, F1 |
| **B3** | Base power synced and saved; nuclear reactor rods | L | 3 | Mid | — |
| **B5** | Wild fruit regrowth | S | 3 | Mid | A0 |
| **P2** | Containment eggs survive (#2538) + aquarium check | M | 3 | Mid | — |
| **P1** | Cuddlefish follows and plays with both | M | 3 | Mid | P2 helps |
| **G1** | Gun gate | S | 4 | Late | G0, S3 (merged) |
| **G2** | Rocket gate: launch together | S–M | 4 | End | G0 |
| **G3** | Sea Emperor, hatching and cure gates (offline-cure fix done: FIX_SPEC S12) | L | 4 | Late | G0 |
| **R1b** | See each other sit, sleep and use things (adopt PR #2859) | L | 4 | Any | A2 |
| **R2c** | The partner's tool effects: scanner, builder and welder beams | M–L | 4 | Early | R2b |
| **T5** | Seamoth hitch-a-ride | L | 4 | Early–mid | — |
| **B6** | Cosmetic leftovers: cannon beams, picture frames | S | 4 | Mid | — |

**Execution order:**

0. **Wave 0, groundwork (before any feature):** A0, A1, A2. They remove repeated work later: one rig session answers
   most (H) marks, one change breaks the serialization and save tests once instead of eight times, and the trial merge
   shows early where #2859 collides with our own `RemotePlayer` work.
1. **Wave 1, quick wins:** D4 first (if creatures can't hurt both players, no fear lever matters; FIX_SPEC S14 fixed it
   for the creatures it tested, D4 covers the rest), B1 teeth, B2, D5a (the first biome line comes minutes into a
   new game), R2a, F1, D1, D6, T2, F2, T4, R1a. F1, D1, D6 and R2a all touch `RemotePlayer`; build them on one branch
   (§13).
2. **Wave 2, before the first story gate in play:** D5b, T3, G0 + G4, B4, D2, R2b. The drive core comes early in the
   story, so G0 has to be ready by then, and B4 before the players go into the Aurora. D2 matters from the first cave.
3. **Wave 3:** T1 with D3, B3, B1 warpers and bleeders (before the Lost River), B5, P2, P1.
4. **Wave 4, before late game:** G1, G2, G3, R1b, R2c, T5, B6.

R1b and T5 are the riskiest. Do them when everything else is stable. A2 decided (2026-11-02): R2b is built before #2859,
on its own (see R1b). Until T1 lands, the death penalty is plain vanilla, which fits principle 2.

### Wave 0 items

**A0: Probe run (S).** One dual-client session on the rig, one script (`tools/scenarios/A0-Probes.ps1`), answering the
(H) marks that only the game can answer. Each probe writes its finding into the item it belongs to.
- D4: the creatures S14 didn't run, through the extended `S14-CreatureAggro.ps1` (see D4).
- D6: the partner's flashlight, Seaglide and a flare at night at 20, 60 and 120 m (screenshots).
- D1: does the partner's breathing loop play on their body?
- D2: the real biome names at known spots (Jellyshroom Cave, Lost River, lava zones, Precursor facilities).
- R2: the partner body's animator values while scanning, building and opening the PDA.
- T4: can a standing player reach the Cyclops helm buttons?
- T1: does the `player_death` pose survive Nitrox's `AnimationController`?
- P2: does the partner see the aquarium fish?
- B4: does an Aurora fire's parent have a Nitrox id? B5: does a wild fruit plant regrow?
- F2: does `uGUI_Pings` reset a custom ping icon?
- Probes that need a late-game fixture (G2, G3) stay with their item.
- **Bridge additions, in one change:** `hold <id> <seconds>` (T1, T2, T3), a read of recently played FMOD events (D1,
  D3), a read of `PDASounds.queue` (D5), and the partner body's animator values if `get` can't reach them (R2).
- **Done 2026-11-02** (branch `feat/0-a0-probes`): bridge commands `hold`, `sounds`, `pdaqueue`, `animator`
  (TESTING.md, dev bridge section); `tools/scenarios/A0-Probes.ps1` run windowless. Findings are in D1, D2, D6, R2, T1,
  T4, B4 and B5 ("A0 probe"), F2 from code. Not probed: P2 (needs a base with an aquarium, left to P2), D4 (own item).

**A1: Shared plumbing (S). Done (2026-11-02, branch `feat/0-a1-shared-plumbing`).** One change that every later item
only reads:
- all server options from the table below, with their defaults, in `SubnauticaServerOptions` and, where the game needs
  them, `InitialPlayerSync`. They do nothing until their item lands; don't list them in the README before that;
  - **Sent to the game** in `InitialPlayerSync.CoopSettings` (stored in `LocalPlayer.CoopSettings`):
    `RescueWindowSeconds` (the `Kill` prefix must know 0 = off), `PartnerSignalInterference` and
    `SignalInterferenceDepth` (each game decides its own interference), `PartnerDiscoveriesSilent` (D5a mutes on the
    receiver). A later item that needs another option in the game adds a field to `CoopSettings`.
  - **Server only:** `StoryGatesEnabled`, `StoryGatePlayers`, `RescueEscalation` (the server sends the window with
    `PlayerDowned`), `BuddyBreathingEfficiency` (the server scales the oxygen).
- the saved player fields `downedUntil`, `downsSinceRest`, `lastDownedAt` (T1) and `heardGoals` (D5b), with one
  `SaveLoadRoundTrip` test and one check whether `SaveDataUpgrades` needs a step (they're additive);
  - On the server `Player` and in `PersistedPlayerData` (`PlayerData.json`): `double? DownedUntil`, `int DownsSinceRest`,
    `double? LastDownedAt`, `HeardGoals` (`ThreadSafeSet<string>`, saved as a `HashSet<string>`).
  - Times are seconds of `TimeService.ActiveTime`: it is saved (`RealTimeElapsed`), stops while the server sleeps with
    nobody online, and doesn't jump on sleep or time skips. Null means not downed / never downed. An absolute
    `DownedUntil` doesn't pause while only the downed player is offline: T1 must shift it on rejoin (or keep the
    remaining time) to get "paused while that player is offline".
  - No upgrade step: a save without the fields loads with null/0/empty (`PlayerDataSaveLoadTest`).
- an `Interfered` flag on the `PlayerStats` packet for D2, kept in memory on the server (not saved).
  - `Player.Interfered` on the server, set by `PlayerStatsProcessor` and relayed with the stats. The game sends false
    until D2 (`PlayerStatsBroadcaster`).

**A2: Trial merge of #2859 (XS).** Merge PR #2859 into a throwaway branch from `coop` without building, and list the
conflicts with this spec's planned edits to `RemotePlayer`, `AnimationController` and the escape pod. Record the list
in R1b. **Done 2026-11-02:** no textual conflicts; the coupling and the decision are in R1b ("A2 trial merge").

**New save-format tags:** `EntityMetadata` uses up to 94 (S1 = 92, S5b = 93, S10 = 94). This spec reserves **95**
(`CuddlefishMetadata`, P1) and **96–99** for later features. Any new entity or metadata type needs a `SaveLoadRoundTrip` test.
B3 stores power as metadata on existing power-source entities; pick its tag from 96–99.

**Options this spec adds** (defaults follow the decisions in §1):

| Option | Default | Item |
|---|---|---|
| `StoryGatesEnabled` | true | G0 |
| `StoryGatePlayers` | `""` (all saved players) | G0 |
| `RescueWindowSeconds` | 60 (0 = off) | T1 |
| `RescueEscalation` | true | T1 |
| `BuddyBreathingEfficiency` | 0.5 (receiver gets half of what the donor pays) | T2 |
| `PartnerSignalInterference` | true | D2 |
| `SignalInterferenceDepth` | 300 (m) | D2 |
| `PartnerDiscoveriesSilent` | true | D5a |

Client settings: partner signal Off / Faint / Always (F1; "Always" also ignores D2), name tag range (F1).

---

## 5. Story gates (shared story moments)

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

## 6. Teamwork survival

### T1: Rescue window
**Goal:** at 0 health you go **down** instead of dying. Your partner has `RescueWindowSeconds` (default 60) to revive you:
- a **first aid kit** gives 50 health (`kFirstAidKitHealth`), after a 2 s hold;
- **holding the interact key** for 6 s gives about 20 health.

The window shrinks if you go down again soon (see **Price** below).

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
  - A "revive" action on the partner hand-target that T2 builds (no new component).
  - "Hold to revive" with the hand-target's hold progress: 6 s by hand, 2 s with a first aid kit in hand. Letting go or
    moving out of range resets the progress.
  - The server checks `PlayerReviveRequest` with T2's partner-interaction check, plus "target is downed".
  - The downed pose from the `player_death` animator trigger. **A0 probe (2026-11-02, windowless):**
    `AnimationController` doesn't override it: on B's copy of A's body the `Death` layer plays `quick_death`. But the
    clip plays **once** (about 1.2 s) and the layer goes back to its empty state; `player_death_drown` (`drown_death`,
    about 3.4 s), `_choke`, `_fire`, `_explosion` behave the same. So the trigger gives a collapse, not a held pose:
    hold the last frame on the downed body (e.g. `Animator.Play(state, deathLayer, 1f)` and keep re-applying it while
    downed, or stop that body's animator speed) and check the pose windowed (windowless bodies are
    `CullUpdateTransforms`: the state machine runs, the bones don't).
  - The downed player is heard (D1) and sends a distress call (D3).
- **Option `RescueWindowSeconds`:** 0 disables the feature. It's sent in `InitialPlayerSync`.

**Price (decided 2026-11-02, §2.2):**
- **Escalation** (option `RescueEscalation`): the server counts downs since the player last rested, in the player's saved
  record (`downsSinceRest`, `lastDownedAt`).
  - 1st down: the full window. 2nd: half (30 s). 3rd: no window; the kill goes through.
  - The count resets to 0 when a sleep completes with that player in bed (`SleepManager`), or after 15 min of real time
    without going down. The second reset matters early on: you can't sleep before the Aurora explodes
    (`game:Bed.cs:160`), and there's no bed before the first base.
  - The server sends the window length with `PlayerDowned`, so the client never decides it.
- **Revives take time in the open**: the hold times above. The rescuer can do nothing else while holding.
- **Death stays vanilla when the window runs out:** unsecured items drop where you died (`game:Inventory.cs:305-331`,
  `LoseItems` → `InternalDropItem`), so the partner can go and get them. Keep `KeepInventoryOnDeath` off. Check that those
  drops reach the partner and survive a restart (H).

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
  - death clears the state;
  - escalation: the 2nd down gets 30 s, the 3rd no window; a completed sleep and 15 min without a down both reset it;
    the count survives a restart.
- **L3** (no-damage/no-oxygen cheats **off**):
  - lethal damage → downed;
  - B revives → A is up;
  - the timer runs out → death and beacon;
  - underwater, oxygen reaches 0 → death.
  - Hold to revive with the bridge's `hold <partnerId> 6` (added in A0); `use` only clicks once.

**Size:** L.

### T2: Buddy breathing
- **Today:**
  - `OxygenManager` (`game:` 11, 88, 104, 120) holds the lungs plus tanks.
  - The partner's oxygen arrives every 3 s in `PlayerStats` (`PlayerStatsBroadcaster.cs:9,33`).
- **Design:**
  - Hold interact on a partner whose oxygen is below about 30%, or who is downed. The donor needs a tank and keeps a
    reserve (stops at about 15 s of their own air).
  - Every 0.5 s: donor `RemoveOxygen(n)` → `OxygenShare(target, n)` → a **server processor** validates it, clamps it, and
    sends `n × BuddyBreathingEfficiency` **only to the target**. Without a processor, `DefaultServerPacketProcessor` would
    broadcast it.
  - The receiver calls `AddOxygen` with that amount.
  - Feel: the reticle progress bar plus a bubble sound from the remote player's emitter. No new HUD.
- **Build it once (T1 and T3 reuse it):**
  - **Partner hand-target:** one `IHandTarget` on the partner's body (`RemotePlayer.SetupBody`, `:404`) with pluggable
    actions (share air here, revive in T1, give in T3), the hold progress (`GameInput.GetButtonHeld` +
    `HandReticle.SetProgress`) and the reticle text. `GUIHand` can already target the body within 2 m
    (`game:GUIHand.cs:85`).
  - **Partner-interaction check on the server:** sender and target online, neither downed unless the action allows it,
    within about 2 m by the server's positions. `OxygenShare`, `PlayerReviveRequest` and `ItemGiveRequest` all use it.
- **Price (decided 2026-11-02, §2.2):** slow and lossy. With `BuddyBreathingEfficiency` 0.5 the donor pays 2 for every 1
  the receiver gets. Tune `n` in the L3 run so that buddy breathing *slows* drowning at depth without refilling: it buys
  time to reach air, it doesn't replace it. Without a Rebreather, deeper depth classes use 1.5× and 2× air
  (`game:Player.cs:2216-2237`), so tune at about 300 m.
- **Tests:**
  - L2: target-only delivery, the efficiency factor applied by the server, clamping, unknown target dropped.
  - L3: A's oxygen rises by about half of what B's falls.
- **Size:** S.

### T3: Hand items over
- **Today:**
  - The partner's inventory already exists on both sides: `InventoryItemEntity` children of the `PlayerEntity`, mirrored
    into `RemotePlayer.Inventory` (`RemotePlayer.cs:96-98`).
  - A reparent to the local player already lands in the local inventory (`EntityReparentedProcessor`, `InventoryItemReparented`).
  - `ItemContainers.cs:33` deliberately sends nothing for remote-player containers.
  - The server's `EntityReparentedProcessor` checks neither ownership nor capacity.
- **Design:**
  - A "Give" action on T2's partner hand-target opens the vanilla two-panel transfer screen:
    `Inventory.main.SetUsedStorage(remote.Inventory)` + `PDA.Open(PDATab.Inventory)`, as the bioreactor does
    (`game:BaseBioReactor.cs:888-891`).
  - On the partner's container copy:
    - `isAllowedToRemove` = false, so nobody can take things;
    - `isAllowedToAdd` = false + send `ItemGiveRequest`, so nothing moves until the server agrees.
  - **Server checks:** T2's partner-interaction check, plus: the item's parent is the sender, it's an
    `InventoryItemEntity`, and it's not equipped.
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
  - **First probe** (bridge or one game run): can a standing player reach the buttons?
    **A0 probe (2026-11-02, windowless + code): reach isn't the blocker.** Standing 0.8–2.5 m behind the pilot chair,
    the helm HUD is 1.4–3 m away. Its `uGUI_GraphicRaycaster` has `interactionDistance` 0 and no status delegate, so
    it's always enabled; `FPSInputModule`'s 3 m limit only applies to `uGUI_InputGroup` canvases (code). There
    `canvasGroup.interactable` is false and `hudActive` true: Nitrox's patch is the only block. Not shown: an actual
    click by a standing player with `currentSub` set (teleporting inside the sub clears `currentSub`; walk there with
    `input move` in T4's scenario).
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

## 7. Finding & signalling

### F1: Faint partner signal, close-range name tag — done 2026-11-02 (branch `feat/1-remote-player`)
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
  - **In caves and the deep** the marker drops to a last contact (D2). Build the component so D2 and D3 can plug in a
    rule: "faint" (this item), "last contact" (D2), "distress" (D3).
- **Tests:**
  - L1: the visibility rule as a pure function.
  - L3: teleport to 5/30/200 m, read `minDist` and the name-tag state through the bridge, take screenshots.
- **Size:** S–M.
- **What was built:**
  - `PartnerSignal` (`NitroxClient/MonoBehaviours/PartnerSignal.cs`) on each partner's body, 4 times a second: distance
    from the local camera to the marker, line of sight under 25 m (`Physics.Linecast` against the terrain mask only, as
    `Creature.GetCanSeeObject` does: base walls don't block it), then sets `PingInstance.minDist`/`range` and the name
    tag. The marker is hidden by setting `minDist` to `float.MaxValue` (`uGUI_Pings` hides a ping closer than
    `minDist`); `visible` stays the player's own PDA toggle.
  - The rule slot: `PartnerSignalRules.Rules` (`NitroxClient/GameLogic/PlayerLogic/PartnerSignalRules.cs`), a list of
    `IPartnerSignalRule` with plain inputs (`PartnerSignalContext`: mode, distance, sight, name-tag range, the
    prefab's distances) and output (`PartnerSignalLook`: rule name, `minDist`, `range`, name tag). The first rule
    that answers wins; `FaintSignalRule` answers always and stays last. D2 and D3 insert theirs in front and extend the
    context (interference, downed).
  - Faint: hidden within 12 m, or while in sight under 25 m; else `minDist` 12, `range` 150. Off: hidden at any
    distance. Always: the signal prefab's own distances (`minDist` 10, `range` 5, read in game), so the marker is as it
    was before F1. The name tag follows its own range in every mode.
  - Name tag: `RemotePlayerVitals.SetNameTagVisible` switches its canvas (name and bars), independent of the
    creative-mode `SetStatsVisible`.
  - Label: "<name> · <depth> m deep" (`Nitrox_PartnerSignalLabel`, `Ocean.GetDepthOf` of the marker), only the name at
    the surface; set with `SetLabel`, which Nitrox doesn't sync, only when the rounded depth changes. It replaces
    "Player <name>"; nothing reads the label as a key (`TryGetKeyForPingInstance` uses the body's id).
  - Client settings, Options → Nitrox → "Your partner": partner's signal Off / Faint / Always (`NitroxPrefs.PartnerSignal`,
    default Faint) and name-tag range 0–50 m (`NitroxPrefs.NameTagRange`, default 12; 0 hides it). en + de.
- **Verified:** L1 `PartnerSignalRulesTest` (distances, sight, name-tag range, Off/Always, rule order). Windowless L3
  `tools/scenarios/F1-PartnerSignal.ps1` PASS (12 checks): at 5 m (in sight) the marker is hidden and the tag shows;
  at 30 and 200 m `minDist` 12, `range` 150, no tag; label "Alice · 59 m deep"; Off hides the marker at 30 m, Always
  uses 10/5; the setting is put back afterwards (`PlayerPrefs.DeleteKey`). No in-game negative control: the scenario
  reads fields the pre-F1 code never set (budget rule; L1 is the negative control).
- **Open:** screenshots of the faint marker and the tag (windowed only, not run). Whether the 150 m fade feels right is
  a play question. Line of sight ignores base walls and vehicles.

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
  - **Price (§2.2):** pings travel like the signal. If the sender or the receiver is in interference (D2), the receiver
    only shows the ping when they're within about 50 m of the sender. The receiver decides, so the server stays a relay.
- **Risk:** a custom icon may be reset when `uGUI_Pings` re-adds markers. Patch `uGUI_Pings.OnAdd` if so.
  **A0 (2026-11-02, from code, not checked in game): it is reset.** `uGUI_Pings.OnAdd` sets the sprite from
  `pingType`, and it runs for every ping when the component is enabled (`OnEnable` re-adds all); `OnIconChange` does
  the same. The game itself never toggles `uGUI_Pings`. Danger (Signal) and Come here (Beacon) are ping types, so they
  survive. Look (an item's sprite) needs a Postfix on `OnAdd` and `OnIconChange` that re-applies the sprite by ping id.
- **Tests:**
  - L2: the kind reaches B, the sender gets nothing back.
  - L3: B's marker icon and label, read through the bridge.
- **Size:** S.

---

## 8. Dread: keeping it scary

These items balance the helps in §6 and §7 (principles 2–4). Most are small: sound switches, marker rules, probes.

### D1: Hear each other (S)
- **Goal:** you hear your partner get hurt, struggle for air or go down, positionally and only nearby (about 30 m). No HUD
  alert.
- **Today:**
  - Pain isn't synced. `PlayerDamageSounds.OnTakeDamage` (`game:PlayerDamageSounds.cs:21`) plays one of several emitters
    on the local player: with a tank, without a tank, above water without a mask, smoke. Radiation is silent.
  - `event:/player/damage` is off in `SoundWhitelist_Subnautica.csv:372`. The breathing loop and bubbles are on (20 m,
    `:364-365`), but breathing is a looping emitter on the local player (`game:BreathingSound.cs`). Whether either plays
    on the partner's body is (H).
  - Sounds on the player's own body need their own packet, as footsteps do (`FootstepSounds_OnStep_Patch` →
    `FootstepPacket`).
- **A0 probe (2026-11-02, windowless, `sounds` on both games):**
  - **Breathing: not heard** (in game and from code). `BreathingSound` starts its loop with
    `FMOD_StudioEventEmitter.StartEvent`, which Nitrox doesn't patch, so the breathing emitter `SetupPlayerSounds`
    puts on the partner's body is never played. A's game started `event:/player/breathing`; B's played nothing on A's
    body.
  - **Bubbles: heard, but not positional.** `event:/player/bubbles` reaches B as a 2D event instance
    (`FMODEmitterController.PlayEventInstance`, volume by distance). Seen once on each body in about a minute;
    the scripted 30 s window caught none, so the cadence is unknown.
  - **Pain: not synced** (confirmed). A played `event:/player/Pain_no_tank` and `event:/player/pain_no_tank_light`; B
    nothing. The paths can be read from the local emitters at startup: `painWithTank` `event:/player/Pain`,
    `painWithoutTank` `.../Pain_no_tank`, `painWithoutMask` `.../Pain_surface`, `painSmoke` `.../coughing` (all
    `.asset.path`), `painWithoutTankLight` `.../pain_no_tank_light` (FMODUnity emitter: `.Event`; the whitelist spells
    it `Pain_no_tank_light`, so match case-insensitively).
  - With a tank *equipped*, the game still chose "no tank": it counts tanks in the inventory
    (`GetPickupCount(TechType.Tank)`). Send the kind the local game chose rather than recomputing it.
- **Design:**
  - Postfix on `PlayerDamageSounds.OnTakeDamage` → `PlayerPainSound(kind)`, at most 2 per second. Nothing to persist,
    so the default relay is enough.
  - The receiver plays the same event on the partner's body, positional, silent beyond about 30 m. Read the event paths
    from the local player's emitters at startup (H: the asset field), so no paths are hard-coded.
  - **Downed:** a weak pain or breath sound every few seconds from the downed body, so a rescuer can follow it in the
    dark.
  - **Breathing:** the A0 probe showed the partner's loop doesn't play on their body, so drive the breathing emitter
    that `SetupPlayerSounds` already adds to the body from their underwater state (and tank).
- **Tests:**
  - L2: the relay.
  - L3: damage A through the bridge; B's game played the pain event on A's body (bridge `sounds 5 player/`,
    `remotePlayer` = A); nothing when B is 100 m away.
- **Done 2026-11-02** (branch `feat/1-remote-player`), pain and breathing; the downed sound waits for T1.
- **What was built:**
  - `PlayerDamageSounds_OnTakeDamage_Patch` (Postfix, local player only) sends `PlayerPainSound(session, kind)`, at
    most every 0.5 s. The kind comes from the game's own branches with the same inputs at the same moment
    (`TryGetKind`: damage, type, `isUnderwater`, `GetPickupCount(Tank)`), so it is the emitter the game just picked,
    including "no tank" with a tank only on the back. No server processor: the default relay forwards it (kept out of
    its debug log).
  - `RemotePlayerSounds` (`NitroxClient/MonoBehaviours/RemotePlayerSounds.cs`) on each partner's body. **Pain:**
    `PlayPain(kind)` plays the event at the body, silent beyond 30 m, and doesn't restart a kind that is still playing
    (as the game's `GetIsPlaying` checks). It is the hook for T1's downed sound. **Breathing:** every 0.25 s, like
    `BreathingSound.UpdateSound`: underwater (`is_underwater` from the partner's `AnimationChangeEvent`), not in a vehicle
    or the Cyclops helm, a tank equipped (`Tank`, `DoubleTank`, `PlasteelTank`, `HighCapacityTank` from the partner's
    player metadata; an approximation of `OxygenManager.HasOxygenTank`); within the whitelist's 20 m. Sets the `depth`
    and `rebreather` parameters as the game does. Own event instances, not the body's emitters: `FMOD_CustomEmitter.Play`
    is patched and would echo a packet.
  - Paths are read once from the local `PlayerDamageSounds` and `BreathingSound` (`.asset.path`; `painWithoutTankLight`
    `.Event`). Read in game: `event:/player/Pain`, `Pain_no_tank`, `pain_no_tank_light`, `Pain_surface`, `coughing`,
    `breathing`.
  - **Positional:** a 3D event is placed at the body (min 1 m, max 30 m or the breathing radius; the breathing loop
    follows the body every frame); a 2D event gets its volume by distance (`FMODSystem.CalculateVolume`), like bubbles.
    **`event:/player/breathing` is 2D** (read in game), so the breathing is heard by distance, not from a direction.
    Whether the pain events are 3D wasn't read (H); both cases are handled.
- **Verified:** L1 `PlayerDamageSounds_OnTakeDamage_PatchTest` (the branches), L2 `PainSoundRelayTest` (Bob gets the
  kind, Alice doesn't). Windowless L3 `tools/scenarios/D1-HearPartner.ps1` PASS: Alice with a tank 6 m from Bob; Bob's
  game starts `event:/player/breathing` on her body; she is hurt for 10 then 4 and Bob's game plays
  `event:/player/Pain_no_tank` and then `pain_no_tank_light` on her body, the events her own game played; at 100 m
  no pain and the loop stops (she still breathes). Negative control: the A0 probe, same steps without D1: nothing on
  her body in Bob's game.
- **Open:** how it sounds (windowed, with sound; windowless games are muted): volume, and the 2D breathing. Bubbles stay
  2D (unchanged). The downed sound (T1, through `PlayPain`).

### D2: The deep cuts the line (M)
- **Goal:** the partner's signal works in open water and drops to a **last contact** in caves, below 300 m, in the Lost
  River, the lava zones and Precursor facilities. Beacons and scanner rooms you build bring it back. (Decided
  2026-11-02.)
- **Rule** (a pure function, L1-testable): a player is **in interference** when
  - they are deeper than `SignalInterferenceDepth` (300 m, `Ocean.GetDepthOf`), **or** their biome name matches the
    interference list (caves, Lost River, lava zones, Precursor facilities),
  - **and** no relay covers them: a deployed beacon within about 150 m, or a powered scanner room within its scan range
    (300 m + 50 m per range upgrade, `game:MapRoomFunctionality.cs:84-86`).
- **Who decides:**
  - Each game decides it **for its own player** (`Player.main.GetBiomeString()`, `game:Player.cs:2645`) and sends it as
    the `Interfered` flag on `PlayerStats` (added in A1). That packet already goes out every 3 s and the server keeps
    the latest per player (`PlayerStatsProcessor`), so there's no new packet and a late joiner has it within 3 s. That avoids
    looking up the partner's biome in cells that aren't loaded.
  - Biome names come from game data, not code. Collect the real names at known spots through the bridge first.
    **A0 probe (2026-11-02, in game):** `GetBiomeString` is, in order, a special zone (`observatory`,
    `generatorRoom`, `crashedShip` inside the Aurora), the atmosphere-volume override (`Lifepod` in the pod, caves,
    Precursor rooms), else the 2D biome map by x/z. Read at known spots: Jellyshroom Cave (Degasi base, 258 m)
    `JellyshroomCaves`; Deep Grand Reef (503 m) `deepGrandReef`; Mushroom Forest cave `mushroomForest_cave_light`;
    Lost River `LostRiver_Junction` (791 m), `LostRiver_GhostTree` (622 m); Inactive Lava Zone `ILZCastleChamber`;
    Thermal Plant inside `Precursor_LavaCastleBase`; outside the Primary Containment Facility `LavaLakes`; Prison
    aquarium `Prison_Aquarium_Upper`; open water `safeShallows`, `GrassyPlateaus`, `CragField`, `bloodKelpTwo`
    (500 m). The full list (182 names, 145 without the `Sky*` ones) is `WaterBiomeManager.main.biomeSettings[].name`;
    it has `LostRiver_*` (20 names), `ILZ*`/`ilz*`, `InactiveLavaZone`, `LavaLakes*`, `LavaFalls`, `LavaPit`, `ALZ*`, `Precursor_*`,
    `PrecursorCave_*`, `precursorCache`, `Prison_*` and `*_Cave*` names for many biomes. **Case varies** between the map
    and the settings (`JellyshroomCaves` vs `jellyshroomCaves`, `GrassyPlateaus` vs `grassyPlateaus`): match by
    case-insensitive prefix (`LostRiver`, `ILZ`, `InactiveLavaZone`, `Lava`, `ALZ`, `Precursor`, `Prison`,
    `jellyshroomCaves`) plus "contains `cave`". Depth covers the rest (`deepGrandReef`, `bloodKelp*` below 300 m).
  - The link is cut when **either** player is in interference: someone in the Lost River loses their partner too.
- **Presentation** (fields only, never `SetVisible`/`SetColor`, see F1):
  - The marker freezes where contact was lost: point `PingInstance.origin` (`game:PingInstance.cs:12`) at a static proxy
    object.
  - The label becomes "<name> · last contact 2 min" via `SetLabel` (`:176`; Nitrox doesn't hook it).
  - A slow flicker of the frozen marker shows it's stale.
  - Up close nothing changes: the name tag still appears under about 12 m, and you see their light (D6) and hear them (D1).
  - Contact comes back as soon as neither player is in interference.
  - The client setting "Always" (F1) ignores interference.
- **Why relays:** a beacon line or a scanner room at a deep base becomes a lifeline you build, with the game's own tools.
- **Tests:**
  - L1: the rule (depth, biome, beacon distance, scanner range).
  - L2: state relay; a late joiner gets the partner's state.
  - L3: teleport B to 400 m: A's marker freezes and the label counts up. Place a beacon next to B: contact returns. Remove
    it and bring B back to 50 m: contact returns.

### D3: Distress call (S, with T1)
- **Goal:** when your partner goes down, their marker becomes a distress signal you can follow, even where the signal
  has faded. It turns the rescue window into a race through the dark. (Decided 2026-11-02: it **pulses through**
  interference.)
- **Design:**
  - On `PlayerDowned`, every other client switches the partner's marker to the "distress" rule: full alpha at any
    distance, red (the `colorIndex` field; H that `uGUI_Pings` picks it up without `SetColor`), and the label
    "<name> · 42 s" counting down the server's window.
  - The position **pulses**: every 5 s the marker jumps to the partner's real position and stays there until the next
    pulse. It isn't a live track. Each pulse plays an existing short signal sound (pick one with the `SoundDebugger`, H).
  - Pulses ignore D2: a distress call always gets through.
  - It ends on `PlayerRevived` (back to the normal rule) or on death (Nitrox's death beacon, if
    `MarkDeathPointsWithBeacon` is on).
- **Tests:** L3, as part of T1: B goes down at 400 m; A's marker turns red, counts down, and moves only every 5 s.

### D4: Threat parity for the rest of the creatures (S probe, then fixes)
- **Question:** does a creature that A's game simulates actually hurt B? If not, one player is close to immune and no
  fear lever matters. It also decides whether "one baits, the other scans" works.
- **Today:** mostly answered by [FIX_SPEC S14](FIX_SPEC.md#s14-aggressive-creatures-ignore-players), merged into
  `coop`:
  - Before S14, a creature outside the four fully synced ones (Reaper, Sea Dragon, Sea Treader, Ghost Leviathan,
    `AI.cs:33-36`) never attacked the player whose game didn't simulate it: aggression from seeing a target only rose on
    the simulating game, and the partner's body has an invincible `LiveMixin` (`RemotePlayer.cs:516`). S14's scenario
    confirmed this.
  - Since S14, the simulating game bites the partner's body and sends the damage (`CreatureAttackedPlayer`); the other
    game no longer bites its own player with a creature it doesn't simulate; crash fish explosions are relayed;
    `AggressiveWhenSeePlayer` picks the nearest player, local or remote. Leviathans are unchanged.
  - **Tested in game:** Stalker (both lock directions, both players next to it, both games holding it aggressive),
    Biter, Sand Shark, Bone Shark, crash fish, and the Reaper (unchanged, it already bit). **Not run:**
    Crabsnake, Ampeel, Warper, Mesmer, Lava Lizard, River Prowler, Crabsquid, attacks on vehicles, creatures born in
    containment, latency.
  - **Warpers** are worse: each game spawns its own warpers and warp balls (`game:WarperSpawner.cs:31-34`, no Nitrox
    hooks), so each player meets different warpers in the Lost River. **Bleeders** are never simulated, so they do
    nothing for either player (H). Both are B1.
- **Probe:** extend `tools/scenarios/S14-CreatureAggro.ps1` with one case per creature on the "not run" list, both lock
  directions, plus a bite on a vehicle. Keep S14's method: wait for a natural bite, force the contact only as a
  fallback.
- **Done when:** every creature on the list either hurts both players or has a FIX_SPEC item.

### D5: Your own firsts (D5a S + D5b S–M)
- **Goal:** each player hears the PDA's biome and location lines when *they* get there, not when the partner does.
  (Decided 2026-11-02.) §1 rejected discovery notifications; this removes the shared narration that exists today.
- **Today (verified in code):**
  - Every story goal is relayed (`StoryGoal_Execute_Patch`). The partner runs `StoryGoal.Execute`
    (`StoryGoalExecutedClientProcessor`), which calls `PDALog.Add(key)` for PDA goals (`game:Story/StoryGoal.cs:31-49`).
  - `PDALog.Add` plays the voice line by default (`playSound = true`, `game:PDALog.cs:159-189`). So when A enters the
    Lost River, B's PDA describes it while B sits in the base.
  - Logs found in the world arrive the same way (`PDALogEntryAddProcessor`) and play too.
- **D5a, game side (S, wave 1):**
  - **Experiential goals** are the keys in `BiomeGoalData` and `LocationGoalData` (read at runtime from the trackers'
    `goalData`). Everything else syncs as today.
  - On the receiver, an experiential PDA goal completes **without** `PDALog.Add`: only
    `StoryGoalManager.main.OnGoalComplete(key)`. Side effects that hang off goal completion (unlocks, signals) still run.
  - When B later gets there, B's own tracker fires. `BiomeGoalTracker.TrackBiome` → `BiomeGoal.Trigger` doesn't check
    completion, `PDALog.Add` finds no entry, and the line plays for B. (H: check that the trackers keep completed goals
    in their lists after a load.)
  - Logs found in the world (PDAs, data boxes) arrive for the partner with `playSound: false`. They wait in the PDA log;
    there's no place to "get to".
  - Option `PartnerDiscoveriesSilent` (default true); false restores today's behaviour.
  - Within one session this gives the whole effect.
- **D5b, server side (S–M, wave 2):**
  - **Why it's needed:** on join, the initial sync restores the whole PDA log silently with `PDALog.Deserialize`
    (`PdaInitialSyncProcessor.cs`, `RestorePDALog`). A line the partner triggered in an earlier session is then already
    in your log, so `PDALog.Add` finds it when you get there and stays silent. With daily sessions, most lines would
    be lost without D5b.
  - A per-player `heardGoals` set in the player's saved record (field added in A1). A player's own `StoryGoalExecuted`
    for an experiential key adds it. Initial sync leaves out experiential entries the joining player hasn't heard.
- **Tests:**
  - L1: classification of keys.
  - L2 (D5b): `heardGoals` per player, the initial-sync filter, `SaveLoadRoundTrip`.
  - L3 (D5a): A teleports into the Kelp Forest (an early biome line); B's PDA queue stays empty (bridge read of
    `PDASounds.queue`, from A0); B teleports there and hears it once. D5b adds: B rejoins before going there, then
    still hears it once.

### D6: Lights in the dark (S probe, then fixes)
- **Goal:** at night and in caves you see your partner's light from far off: the flashlight beam, the Seaglide light, a
  flare they threw. When it goes out, you notice. (Decided 2026-11-02.)
- **Today:**
  - The held tool is drawn on the partner's body (`PlayerHeldItemChangedProcessor.cs:82-108`). Its layer is set to
    `viewModelLayer` (H: does that hide its light or beam from your camera?).
  - Light toggles are synced: flashlight (`FlashLight_onLightsToggled_Patch` → `ToggleLights`), Seaglide
    (`Seaglide_onLightsToggled_Patch`), vehicles (`ToggleLights_SetLightsActive_Patch`). Flares have `FlareMetadata`.
- **Probe:** at night, A holds a lit flashlight, then a Seaglide, then throws a flare, at 20, 60 and 120 m from B. Take a
  screenshot from B each time. Also check a light that was switched on *before* the tool was drawn.
- **A0 probe (2026-11-02, windowless, light state read in both games):**
  - The layer isn't a problem: B's main camera renders the `Viewmodel` layer (8), where the partner's tool sits.
  - Flashlight at 20, 60 and 120 m: B's copy has `lightsActive` true and the same spotlight as A's (enabled,
    intensity 1, range 50). Switched off or on while put away and then drawn: B matches A both times. Seaglide: same
    state on both (lights off by default, range 40).
  - **Thrown flare: not synced in this run.** A's flare landed lit; 5 s later B still had it as A's inventory item,
    unlit, at A's position. The throw was started by calling `Flare.OnToolUseAnim` (the animation event a windowless
    game doesn't fire), then the game's own `Throw` → `Pickupable.Drop` ran. (H) until a windowed throw confirms it.
  - With the flashlight put away (first run), B's copy read `lightsActive` false while A's read true; once drawn
    they matched.
  - No screenshots: a windowless game crashed around a night screenshot (TESTING.md). Whether the light is still
    drawn at 60–120 m (culling) needs windowed screenshots.
- **Fixes from the probe**, for example the layer, the light state when a tool is drawn, or the culling distance. Keep
  vanilla light ranges; don't make lights brighter.
- **Downed:** a downed player's held light stays on. It helps the rescue, and it goes out when they die.
- **Flare fixed 2026-11-02** (branch `feat/1-remote-player`); the drawn-at-distance question stays open.
  - **Cause (verified in game, windowless):** `FlareMetadata` applies fine. Thrown 20 m from B, the flare burns in both
    games (lit, light on, out of A's hands in B's game). Thrown 120 m from B it lands in a cell B hasn't loaded:
    `EntitySpawnedByClientProcessor` sends the respawn only to players who `CanSee` the new cell, so B got nothing and
    his copy stayed unlit in A's inventory (A0's observation). When B swam over, the cell's spawn brought it in lit.
    Any item dropped or thrown out of the partner's loaded cells left the same copy in the dropper's inventory.
  - **Fix:** the server now sends `EntityDestroyed` to every other player who could see the item's previous record
    (an inventory or locker item, or a loaded cell) but can't see the new one. Their copy goes; the cell's spawn brings
    it back when they come near. Vanilla light ranges are untouched.
  - **The A0 throw, explained:** `Flare.OnToolUseAnim` only throws a flare that is *drawn*. A flare of a stack that has
    no quick slot (`GetSlotByItem` -1) can't be selected; called on it in the inventory, it lights up but never runs
    `Throw` (its `Update` doesn't run while inactive). The scenario picks a flare that has a slot and asserts it was
    thrown.
  - **Verified:** L2 `DroppedOutOfSightTest` (B without the cell gets `EntityDestroyed`, no spawn; B with the cell gets
    the force respawn and no destroy; fails without the fix). Windowless L3 `tools/scenarios/D6-ThrownFlare.ps1` PASS
    (14 checks: 20 m lit; 120 m gone from A's inventory in B's game; lit when B comes near). Negative control in game:
    the same run without the fix fails only the 120 m check.
  - **Open (windowed only, not run):** whether the flashlight, Seaglide light and flare are *drawn* for B at 60–120 m
    (culling, light range), with night screenshots. The downed light (T1).

---

## 9. Cozy home

### R1a: Small comforts (S) — done 2026-11-02 (branch `feat/1-r1a-small-comforts`)
- **Sleep:** `SleepManager` already waits for everyone, but says only "{SLEEPING}/{TOTAL} players sleeping" (`en.json:96`).
  Name who's still awake: "Waiting for <name>…".
- **Eating and drinking:** the game has no eating animation, only a sound (`game:Survival.cs:187`). Those sounds are marked
  not synced in `SoundWhitelist_Subnautica.csv:376-378`. Turn them on with about a 10 m radius, so you hear your partner
  eat.
- **Tests:**
  - L2: sleep flow: A in bed, both see "1/2" with the right name; B in bed, sleep completes; a disconnect mid-wait.
  - L1: whitelist entries.
- **What was built:**
  - `SleepStatusUpdate` carries `AwakePlayerNames` (connected players not in bed, sorted). The client builds the text:
    new key `Nitrox_SleepingPlayersWaitingFor` ("{SLEEPING}/{TOTAL} players sleeping. Waiting for {PLAYERS}…", en +
    de); with nobody awake it keeps `Nitrox_SleepingPlayers`. The other 37 language files show the English line for the new
    key: `Language_LoadLanguageFile_Patch` loads `en.json` first as the fallback, so their files stay untouched.
  - `SleepManager` on disconnect: counts the remaining *joined* players (before: raw sessions, `NewSessionTotal`,
    which also counted someone still in the join queue) and leaves the leaver out explicitly, because the session
    cleaners run in no fixed order and `PlayerManager` may not have dropped them yet. It now also sends a status when
    the only sleeper leaves (before: nothing, so the last status still counted them in bed). The client shows nothing
    for a status with nobody in bed, so this keeps the packets consistent; the earlier "Waiting for…" line simply
    fades from the message log.
  - `event:/player/eat`, `drink`, `drink_stillsuit`: synced, 10 m, not global (the partner must be in the same base or
    vehicle, or both outside, like `bubbles`).
- **Findings (read, not guessed):**
  - `Survival.Eat` and `Survival.Use` play `TechData.GetSoundUse(techType)` through `FMODUWE.PlayOneShot` at
    `Player.main.transform.position`. The default is `event:/player/eat` (`game:TechData.cs:141`); the game's
    `Balance/TechData` (in `resources.assets`) overrides it with `drink` (water items, e.g. disinfected and big filtered water),
    `drink_stillsuit` and `use_first_aid` (first aid kit, left as is).
  - The whitelist flip is enough: `FMODUWE_PlayOneShotImpl_Patch` sends whitelisted paths as `FMODAssetPacket`; the
    server's `FMODAssetProcessor` forwards it to players within the radius and the same sub root, with the volume
    scaled by distance; the client's `FMODAssetProcessor` plays it at the eater's position via
    `FMODEmitterController.PlayEventOneShot` (`FMODUWE.GetEventImpl`, which isn't patched, so no echo).
  - Not changed, noted: the server's `FMODAssetProcessor` writes the scaled volume back into the shared packet inside
    its loop, so with three or more players a later recipient gets an already-scaled volume. Harmless for two players.
- **Verified:** headless. L2 `SleepStatusTest` (A in bed → both get 1/2 with `["Bob"]`; B in bed → 2/2, then
  `SleepComplete`; awake player leaves → sleeper sleeps; sleeper leaves → status 0/1) and `EatingSoundTest` (6 m: the
  partner gets the sound at the eater's position; 15 m: nothing). L1 `FMODWhitelistTest`, `SleepStatusUpdateProcessorTest`
  (text from `en.json`). Negative controls: with the old CSV the whitelist tests and `EatingSoundTest` fail; with the
  old disconnect early return the sleeper-leaves test fails; the other sleep tests need the new packet field to
  compile. **Not verified:** hearing the sound and seeing the message in a real game. No `tools/scenarios/` script was
  written and no game rig was run for this item, on purpose (headless-only build pass). The eating sound takes the same
  path as `bubbles` and `cold_damage` (volume scaled by distance on the server, then by FMOD on the client), so if it's
  too quiet in play, that's this shared path. Seen, not changed: `it.json` uses `{DORMENDO}/{TOTALE}` in
  `Nitrox_SleepingPlayers`, so Italian shows the placeholders unfilled.

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
  - It **changes `EscapePodMetadata`** (two new lists). The change is additive, so old saves load with empty lists, but
    the lists are keyed wrongly (see the A2 notes below), so fix that before it ships.
  - It touches `EscapePodManager.cs` next to S9, but the merge is textually clean (see the A2 notes below).
  - Its bed patch lets the game's own bed code run, which starts a **local time skip** (`game:Bed.cs:203`). Our clock is
    server-driven (`DayNightCycle_Update_Patch`): verify that, or keep our skip suppression. `SleepManager` waiting for
    everyone stays.
- **A2 trial merge (2026-11-02, not built):** `pr-2859` (head `3bc00d9a`, base `eb4535fd`) merged into `coop` with
  `--no-ff --no-commit`, then aborted.
  - **Conflicts: none.** Only two of the 32 files were also changed on `coop` since the base, and both merged cleanly:
    - `EscapePodManager.cs`: ours is S9 (`SetupIntroSequenceAsync`, `AddOrUpdateGlobalRootEntity`, new constructor
      parameters); theirs is one line in `CreateNewEscapePodAsync` for the new `EscapePodMetadata` constructor.
    - `InteriorPieceEntitySpawner.cs`: theirs is a blank line; ours is S2/S10 (`RestoreMapRoom`).
    - Everything else is new files or files `coop` hasn't touched. `AnimationController.cs`, `AnimChangeType` and
      `PlayerHeldItemChangedProcessor.cs` are **untouched**; `AnimationController` is reached only through
      `RemotePlayer`. So the cost is design coupling, not merge work. Build and tests were not run; the compiler may
      still find things.
  - **#2642's review points:**
    - per-frame logging: fixed (no `Log.` calls in the movement broadcaster, `RemotePlayer`'s update paths or the bed
      positioner);
    - the extra `BedEnterAnimation`/`BedExitAnimation` packets and the string session id: gone (beds use a lock per side
      plus `PlayerCinematicControllerCall`);
    - the "denied" message uses the existing `Nitrox_DenyOwnershipHand` key;
    - serializer test: the new `Dictionary<string, bool>` and `List<SessionId>` fields are covered by the faker without
      changes (not run);
    - world/save test: **not addressed**. The PR's only test change is a flaky-test fix in `ThreadSafeSetTest`, although
      its checklist says "Has tests";
    - per-pod state: **only half fixed**. It is now a list per pod, but keyed by `SessionId` (see below).
  - **Still open on #2859:** no review yet. The author's notes: direction changes while swimming look choppy, and Cyclops
    movement stutters (may predate the PR). From #2642's six-player test, not retested: the intro cinematic wouldn't
    let players interact, using a hatch blocks everyone in the pod, benches untested; unfixed there: bulkhead door,
    purple tablet in hand, Cyclops moonpool.
  - **New cautions, found by reading (H unless said):**
    - **Hatch state is keyed by `SessionId`.** Session ids are connection indexes: they start at 1, are reused after 10
      minutes and start over after a restart (`SessionId.cs`). After a rejoin or restart the flags belong to whoever
      gets that number, and the ids pile up. The PR text says "player, not session specific", but the code is
      session-specific. Key it on the persistent player identity instead.
    - **It likely doesn't survive a save.** The server has JSON converters for `NitroxId`, `PeerId` and `TechType`, none
      for `SessionId`, whose only public member is the get-only `IsPlayer`. A scratch test with a same-shaped struct
      wrote `{"IsPlayer":true}` and read it back as 0 (checked with Newtonsoft only; the protobuf save path and the real
      type were not run). Add a `SaveLoadRoundTrip` test for a populated `EscapePodMetadata`; it would catch this.
    - **A dropped guard.** `MultiplayerCinematicController.CallStartCinematicMode` no longer checks whether the local
      player is occupying the animator. Two players in one pod doing the ladder at once could clobber the shared pod
      animator. The opposite failure (one hatch use blocks the whole pod) is also possible, because hatches and ladders
      lock on the controller's parent entity id, while only beds get one lock per side. Test both in the pod.
    - **Bed, side effects of running vanilla `EnterInUseMode`:** `DayNightCycle.SkipTime` sets `skipTimeMode`.
      `DayNightCycle_deltaTime_Patch` means `_dayNightSpeed` doesn't drive the clock, so the skip is probably inert for
      time, but `skipTimeMode` stays set on that client until the server time passes `skipModeEndTime` or the bed is
      left, and `Bed.CanSleep` returns false meanwhile. The patch also calls `StopSleepScreen` right after vanilla's
      `StartSleepScreen`. Verify the fade, and that the second player can still get into the other side.
    - **Every cinematic controller under a Nitrox entity now goes through an exclusive lock round trip** to the server
      before it starts, and a stale lock is released by the ownership-change processor if an end packet is lost.
      A hatch or ladder press now waits for the server's answer (and shows "denied" if another player holds the lock).
  - **Overlap with this spec's items** (no textual conflicts anywhere; "coupling" means they share state in
    `RemotePlayer`). #2859 adds `InCinematicEntityId`/`InCinematic`, `SetInCinematic`/`ClearInCinematic`, an early
    return in `UpdatePosition`, and `ResetStates` now calls `ClearInCinematic`, which sets
    `AnimationController.UpdatePlayerAnimations = true`.

    | Item | Coupling | Order |
    |---|---|---|
    | R2a | Light. Server-side replay and held item; the PR's cinematic poses go through the lock/bed initial sync, not `AnimChangeType`. | Any |
    | R2b | Medium. No file overlap, but the PR writes `cinematics_enabled` and `using_tool_first` on the partner's animator. A `using_tool` watcher could fire mid-cinematic. | **Before**, with one `RemotePlayer` method for all arm-state writes, so one `if (!InCinematic)` can go in later |
    | R2c | None (tool effects on the body). | After R2b, any |
    | F1 | Light. Model, ping and name-tag code; the PR's hunks are in `UpdatePosition`/`SetVehicle`. | Any; same branch as the other `RemotePlayer` items |
    | D1 | Light. Emitters on the partner's body. | Any |
    | D6 | Light. Held tool and lights (`PlayerHeldItemChangedProcessor`), untouched. | Any |
    | T1 | **Heavy.** The downed pose needs the same two things: skip `UpdatePosition`, keep `UpdatePlayerAnimations` off. `ClearInCinematic` would unfreeze a downed body. | T1 is wave 3, #2859 wave 4: give T1 its own flag that gates the same two spots, and make the animation switch a combined test (any lock set), not an assignment, so the two fold into one pose lock later |
    | T5 | Light. `RemotePlayer.Attach` (now `RemotePlayer.cs:145`) is untouched; a hitched body must not count as in a cinematic. | After |
    | R1b | This is #2859. | Wave 4, after the rekey and tests below |

  - **Recommendation:** build **R2b first, on its own**, not on top of #2859. There is no textual conflict, #2859 has no
    review and an unfinished save-format change, and R2b is two waves earlier. Keep R2b's writes in one method.
    Before R1b: key the hatch state on player identity, add the save round-trip test, and run the pod test with both
    players.
- **Tests:**
  - L3: remote animation flags and seat position (within 0.3 m); sit/stand 20 times to catch the #2287 regression; lying
    down in the bed visible to the partner; gun terminal / ladder animations.
  - L2: any new packet fields.
  - L1/L2: `SaveLoadRoundTrip` for `EscapePodMetadata` with both hatch lists filled, and one for a rejoin under a new
    session id.

### R2: The partner's body reads right (from the basics sweep, §12.1)

Principle 1 says the partner's state shows through the world. Today their body shows walking, swimming, equipment,
the held tool and vehicles correctly, but **only two animator values are synced**: `is_underwater` and
`player_infected`. Everything else comes from velocity, held-item packets and vehicle state. The local
`ArmsController.Update` is skipped on the partner's body (`ArmsController_Update_Patch.cs:9-22`), so nothing that it
sets ever reaches them.

- **R2a: Pose and held item survive a rejoin (S)** — done 2026-11-02 (branch `feat/1-remote-player`).
  - The server keeps only the *last* animation change, overwriting the one before
    (`Nitrox.Server.Subnautica/Models/Packets/Processors/AnimationChangeEventProcessor.cs:12`). After the infection reveal,
    a rejoining player can see the partner swimming while they stand in a base.
  - The held item isn't stored at all (server `PlayerHeldItemChangedProcessor.cs:9-14`), so the partner has empty hands
    until they switch items.
  - Fix: keep a map of animation type → state plus the held item id per player, and replay both on join. In memory
    only, no save change.
  - **What was built:** the server `Player` keeps `Animations` (type → state) and `HeldItem` (the latest draw packet;
    cleared on put-away of that item). Both are cleared when that player joins again: their game starts with empty
    hands and sends its underwater state anew. `JoiningManager` sends them to a player once their initial sync is done
    (`ReplayOtherPlayersPoseAsync`, after `PlayerJoinedMultiplayerSession`), as ordinary `AnimationChangeEvent` and
    `PlayerHeldItemChanged` packets: by then the game has the partner's body and inventory, and its usual processors
    apply them. No client change. The first-use flag isn't replayed (the first-use animation would play again).
  - **Finding (from code):** the infection reveal isn't a pose. The game plays it with `SetTrigger("player_infected")`
    once (`game:Player.cs:2409`); Nitrox relays it as `INFECTION_REVEAL ON`. Kept as the player's animation (before R2a
    too, when it was the last change), it would play again on the partner's body for every late joiner. It is now
    relayed live only, not kept in `PlayerContext.Animation` or the map.
  - **Verified:** headless. L2 `PartnerPoseReplayTest`: a late joiner gets the standing pose and the held flashlight
    (no first-use flag), no reveal, and the join payload's pose is the standing one; a put-away item isn't replayed; a
    rejoin starts empty. Negative controls: without the replay call both tests fail; with the reveal kept, the first
    fails. **Not verified in game:** the rejoin itself (B rejoins, A's body stands and holds the tool). The packets are
    the ones the game already applies live.
- **R2b: Arm states (M).** Sync `using_tool`, `using_tool_alt`, `using_builder`, `using_pda`, `on_surface`, `jump` and
  `diving` (`game:ArmsController.cs:255-261,454-577`) as new `AnimChangeType` values, sent on change from a watcher like
  `UnderwaterStateTracker`. The partner then visibly scans, builds, welds, cuts, opens the PDA, swims at the surface
  (#2281) and jumps. No new packet; `PacketsSerializableTest` covers it.
  - (H) Also call `ArmsController.Reconfigure(tool)` on the partner's body when a tool is drawn or put away, for aim and
    the two-handed grip.
  - **A0 probe (2026-11-02, windowless, `animator` on both games):** with the PDA open, A's own animator has
    `using_pda` true, B's copy false. With the scanner or builder drawn, A has `holding_tool` true, B false;
    `holding_scanner`/`holding_builder` and `using_tool_first` do reach B (held-item packet). `using_tool` stayed false
    on A's *own* animator in the samples taken while A scanned a fish (the scan completed), so check the sender side of
    `using_tool` windowed before relying on it (H). Add `holding_tool` to R2b's list.
- **R2c: Tool effects (M–L).** Each tool plays its beam or sparks only for the local user (`game:BuilderTool.cs:362`,
  `Welder.cs:61`, `LaserCutter.cs:100`, `ScannerTool.cs:146`, the Seaglide propeller `Seaglide.cs:110-117`). Play them on
  the partner's body when R2b's `using_tool` turns on. Scanner, builder and welder first; the laser cutter beam also
  fixes #2463.
- **Covered elsewhere:** hatches, the escape-pod ladder, beds, benches and story cinematics come with R1b (#2859: the
  receiving side already exists, only the three senders are `#if false`). Death has no animation for the partner
  (cosmetic). Precursor teleporters show the partner snapping away (cosmetic).
- **Tests:** L2: R2a's replay to a late joiner. L3: read the partner body's animator values through the bridge while A
  scans, builds and opens the PDA; rejoin B and check A's pose and held item.

## 10. Pets & aquariums

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
  (`InventoryContainerHelper.cs:67-69`, H). **Verify only.** Not probed in A0 (2026-11-02): it needs a base with an
  aquarium and fish, more setup than the probe run's budget; do it in P2's scenario.
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

## 11. Design decisions (2026-11-02)

| Topic | Decision |
|---|---|
| G4 drive core | **The last weld waits.** Weld as you like; the final leak only seals once both players are in the drive core room. |
| Sunbeam radio | **Waits for both.** While one player is offline, the `RadioSunbeam4` message can't be played, so the Sunbeam arc never starts alone (part of G0). |
| T1 downed in a vehicle / quitting while down | **Eject + rejoin downed.** A downed pilot is pushed out of the Seamoth or Prawn. Quitting while down means rejoining still down, with the remaining time. |
| T5 vehicles | **Seamoth only.** |
| Fear levers (second round) | **D1 hear each other, D2 the deep cuts the line, D3 distress call.** Not chosen: predators preferring a lone player. |
| Pricing the helps | **Every help has a price** (§2.2): rescue escalation 60 s → 30 s → none, reset by a sleep or 15 min without a down; buddy breathing at half efficiency; revives are holds (6 s by hand, 2 s with a kit). Not chosen: "as specified", Hardcore pair. |
| The partner's PDA lines | **Hear your own firsts** (D5). |
| Progression pace | **Vanilla numbers.** Not chosen: more fragments for two. |
| Together-plays | **D6 lights in the dark.** Not chosen: Overwatch, dragging a downed partner. |
| Distress in interference | **Pulses through** every 5 s with the real position. |

## 12. Basics that must work (gaps found during this research)

These are bugs rather than features, but they are part of this spec (decided 2026-11-02), so the co-op build plan in §4
covers them. FIX_SPEC links here instead of duplicating them.

Earlier findings:
- **The partner offline at cure time stayed infected forever:** fixed (FIX_SPEC S12).
- **#2538:** probable cause found (P2).
- **Players rejoining at an unrelated position after a restart:** a test-rig artifact, not a game bug (FIX_SPEC S11).
- **Benches:** walking animations may fight the sitting pose (R1b).
- **The partner's PDA speaks your discoveries** wherever they are (D5). Not a bug in the code's terms, but it breaks
  principle 4.
- **Pain isn't audible for the partner:** `event:/player/damage` is off and the pain emitters aren't synced (D1).
- **Creatures didn't hurt the player who doesn't simulate them.** Fixed by FIX_SPEC S14 for the creatures it tested;
  the rest is D4.

### 12.1 Basics sweep (2026-11-02)

Three read-only code audits (no game runs) of what the partner sees and what survives a restart: the partner's body,
building and bases, and the world/story systems. Rows marked (H) are reasoned from code only.

**Fine today** (synced live and persisted): placing, building and deconstructing base pieces and furniture, including
in the Cyclops; Neptune rocket stages, elevator and preflight; signs, locker labels, beacon names, vehicle names and
colours; planters; aquariums and containment breeding (apart from #2538); water filtration; chargers; moonpool docking
and upgrade consoles; trash cans; Aurora keypads, doors, laser-cut seals and radiation leaks; Precursor keys, doors,
teleporters, terminals and blueprints; player stats and infection; escape pod and radio repair; the clock; vehicle
health and power; PDA, known tech and fragments; walking, swimming, equipment, held tools and vehicle poses on the
partner's body.

**Gaps**, each an item below except two that live elsewhere: unprovoked creature attacks (fixed by FIX_SPEC S14 for the
creatures it tested; D4 covers the rest) and the partner's body (R2).

| ID | Gap | Impact | Size | Wave |
|---|---|---|---|---|
| **B1** | Creature leftovers: Stalker teeth, warpers, bleeders | Dupe; desync in the Lost River | XS + M + S | 1 (teeth), 3 (warpers, bleeders) |
| **B2** | Building and crafting without loss or dupes: non-empty storage, two builders on one ghost, fabricator pickup | Loss, dupe | S–M | 1 |
| **B3** | Base power synced and saved (#1811), nuclear reactor rods | Hurts co-op; free power; dupe | L | 3 |
| **B4** | Aurora fires | Desync inside the Aurora | M (probe first) | 2 |
| **B5** | Wild fruit regrowth | Minor | S (probe first) | 3 |
| **B6** | Cosmetic leftovers: repulsion and propulsion cannon beams, picture frames | Cosmetic | S | 4 |

#### B1: Creature leftovers (XS + M + S)
- **Stalker teeth (XS, confirmed):** `Stalker_CheckLoseTooth_Patch.cs:16-17` uses `&&` where it needs `||`. When the id
  lookup succeeds, the ownership check never applies, so every game runs `CheckLoseTooth` and both may drop a tooth for
  one bite. Fix the operator; teeth are needed early (Enameled Glass). L1 or L3: one bite, one tooth.
- **Warpers (M):** each game spawns its own warpers and warp balls (`game:WarperSpawner.cs:31-34`, `game:WarpBall.cs:188`;
  no Nitrox hooks). Only the player who simulates a `WarperSpawner` spawns its warper and registers it with the server
  (`EntitySpawnedByClient`); the other game gets it as an entity. Needed before the Lost River.
- **Bleeders (S, H):** not in `SimulationWhitelist.cs`, so nobody simulates them and they do nothing. Add
  `TechType.Bleeder` to the movement whitelist; check the attach behaviour with D4's scenario.

#### B2: Building and crafting without loss or dupes (S–M)
- **Non-empty storage (S, confirmed):** when a module's constructed amount reaches 0, the server removes the entity and
  its contents without checking (`BuildingManager.cs:129-133`). The game blocks deconstructing non-empty storage locally
  (`game:Constructable.cs:639-642`), so a loss needs a game whose copy wrongly looks empty (H). The server refuses to
  remove a storage module that still has items and tells that game to resync.
- **Two builders on one ghost (S, H):** no lock on ghosts; the cooldown is keyed by the parent's id
  (`BuildingHandler.cs:142`), the remote amount overwrites the local one (`:158`), and each game charges materials at its
  own thresholds (`game:Constructable.cs:268-281`). Take an exclusive lock on the ghost when building starts, as the
  fabricator does.
- **Fabricator pickup (S–M, H):** taking the finished item by hand isn't locked (`game:GhostCrafter.cs:250-254`). Take the
  exclusive lock before the pickup, or block it while the partner holds the lock.
- **Tests:** L2 for the server refusal; one L3 run where both players build the same ghost and both click the same
  finished item, checking materials and inventories.

#### B3: Base power (L)
- **Today:** base power isn't synced and refills to full on every load: `InteriorPieceEntitySpawner.cs:81-84` and
  `ModuleEntitySpawner.cs:36-40` say "TODO: Have synced/restored power" (#1811). Base oxygen depends on each game's own
  power state (`game:Player.cs:1434`), and S7 lets only the base's lock holder produce. So if the lock holder's game
  thinks the base is unpowered, filtration and the bioreactor stop for both players.
- **Design:** the base's simulating player sends each power source's charge as metadata, throttled. Spawners apply the
  stored value instead of filling to max; the other game only mirrors it.
- **Nuclear reactor (S, H):** every game with the base loaded removes rods and adds depleted ones itself
  (`game:BaseNuclearReactor.cs:279-331`, no Nitrox patch). Copy the bioreactor fix: only the producer swaps rods.
- **Tests:** `SaveLoadRoundTrip` of stored power; L3: drain a base at night, restart, power is the same for both.

#### B4: Aurora fires (M, probe first)
- **Today (H):** the fire code takes its id from `fire.transform.parent`, which fits the Cyclops layout
  (`NitroxClient/GameLogic/Fires.cs:74,101`), and only `CyclopsFireEntity` is registered (`Entities.cs:81`). Aurora fires
  are probably local to each game.
- **Probe (A0):** does an Aurora fire's parent have an id? If not, create the fires on the server, the way the radiation
  leaks are created (`WorldService.cs:418`). Needed before the players go into the Aurora.
- **A0 probe (2026-11-02, windowless): yes.** After `/aurora explode`, the cargo room, generator room and Prawn bay had
  33, 24 and 40 `Fire` components. Each one's direct parent is an `Aurora_ExtinguishableFire_Small`/`_Medium`/
  `_Medium_Tall` world entity with a NitroxId (from the batch), so `Fires.cs`'s `fire.transform.parent` lookup finds an
  id; no server-side creation is needed. What's left for B4: whether dousing them is relayed, persisted on the server
  and kept after a restart (headless first: `FireDoused` / `EntityDestroyed` for these world entities).

#### B5: Wild fruit regrowth (S, probe first)
- **Today (H):** `FruitPlant_Update_Patch.cs:22-26` waits for a lock; planters get one, wild plants may not. Check which
  wild plants regrow in vanilla, then put them on the utility whitelist or let regrowth run when nobody holds a lock.
- **A0 probe (2026-11-02, code + game data + in game): wild fruit doesn't regrow in vanilla either, so B5 has nothing
  to fix.** `FruitPlant.Update` only regrows when `fruitSpawnEnabled`, which defaults to false and is set only by
  `OnGrown`, i.e. a plant grown in a planter (`game:FruitPlant.cs:14,64,85-95`). The world's batch cells carry no
  serialized `FruitPlant` (none of the 4582 `.bin` files in `SNUnmanagedData/Build18` names it), and in game every wild
  plant with fruit had `fruitSpawnEnabled` false and `timeNextFruit` -1 (9 creepvines near the pod, 5 lantern trees on
  the Floating Island). Planted fruit plants regrow through the planter lock already. Suggest dropping B5.

#### B6: Cosmetic leftovers (S)
- The repulsion cannon has no sync; the propulsion cannon moves objects (`PropulsionCannon_GrabObject_Patch.cs:44`) but
  shows no beam. Picture frames aren't synced (#2261; adopt PR #2840 after reading its review).

**Doc correction (done 2026-11-02):** AGENTS.md §6 said `Drillable_ManagedUpdate_Patch`'s transpiler may never run. It
does: the class overrides `Patch()` and applies it (`Drillable_ManagedUpdate_Patch.cs:42-46`), and
`PatchesTranspilerTest.cs:44` registers it. AGENTS.md now says so.

## 13. Definition of done (per feature)

Same as AGENTS.md §7, with one deliberate exception for build efficiency:
- a branch `feat/<id>-<slug>` from `coop`. **Exception:** items of the same wave that touch the same files may share one
  branch `feat/<wave>-<slug>` (e.g. F1, D1, D6 and R2a all edit `RemotePlayer`). AGENTS.md §7 says one item per branch;
  this spec deviates on purpose, because separate branches would conflict with each other in the same files;
- `dotnet build` + `dotnet test` green;
- headless tests for the server half; an in-game scenario for the game half (budget rules in TESTING.md). One rig
  session may run the scenarios of several items, but each item keeps its own negative control (the scenario fails
  without its fix);
- new strings in the language files;
- options documented in `docs/fork/PLAYING.md` (once it exists) or here;
- **the README updated** to showcase the feature (§13.1);
- a Log row at the bottom of this file.

### 13.1 README: showcase the features

The [README](../../README.md) is the fork's front page. It should show what playing together feels like, not list
code. Every feature that lands, and every change to this spec that players would notice, updates it in the same branch:

- **Shipped features** move out of "Planned co-op features" into a co-op features table next to the bug-fix table:
  feature, what you get, how to use it (key or interaction), its price or limit (§2.2), and the server option with its
  default.
- **Lead with the design idea.** Say why it's built this way: it stays scary (the deep cuts the line, you hear your
  partner), every help has a price, the big story beats wait for both of you. Mention that every rule can be switched
  off in `server.cfg`.
- **Players' language.** No class names or file paths. Upstream issues and PRs only as links.
- **Keep it clean.** Remove dropped features and stale "planned" lines. Keep "Planned co-op features" short and grouped
  like this spec: story moments, teamwork survival, finding each other, keeping it scary, cozy home.
- **Show it** where a picture helps: a screenshot or short clip from the dual-client rig (`tools\bridge.ps1` can take
  them), such as the faint partner marker or the distress call.
- The README carries the highlights; `docs/fork/PLAYING.md` (once it exists) carries the full option reference.

The README's "Planned co-op features" list was brought in line with this spec on 2026-11-02 (the design idea, the
prices, "keeping it scary").

## Log

| Date | Feature | Change | Verified by |
|---|---|---|---|
| 2026-11-02 | — | Spec created from design decisions + feasibility research (3 read-only investigations) | — |
| 2026-11-02 | §2–§3, §8, T1, T2, F1, F2 | Fear and difficulty review: principles 2–4 and 6, analysis, price table, journey map, D1–D6, prices on T1/T2/F2, options table; second round of design decisions; README showcase rule (§13.1); basics sweep (§12.1) with R2 and the D4 update; gaps made items B1–B6 (§12); build-efficiency pass: wave 0 (A0 probe run, A1 shared plumbing, A2 #2859 trial merge), D5 split into D5a/D5b, shared partner hand-target (T2), D2 state on `PlayerStats`, shared branches per wave (§13) | Code and decompiled-game reads (cited inline) |
| 2026-11-02 | D4, §12, §13.1 | D4 rebased on FIX_SPEC S14 (merged): what S14 fixed and tested, the creatures left, the probe extends S14's scenario. AGENTS.md Drillable note corrected; README "Planned co-op features" rewritten per §13.1 and S13/S14 added to its fix table | FIX_SPEC S13/S14 entries; `Drillable_ManagedUpdate_Patch.cs`, `PacketSuppressorPatch.cs` read |
| 2026-11-02 | A2, R1b | Trial merge of #2859 into `coop` (not built): no conflicts, two overlapping files merge cleanly; its hatch state is keyed by session id and has no save test; coupling table per item; decision: build R2b before #2859 | `git merge --no-commit` of `pr-2859` (aborted); PR and #2642 review comments, decompiled `Bed.cs`/`DayNightCycle.cs` read; scratch Newtonsoft check of a `SessionId`-shaped struct |
| 2026-11-02 | A1 | Shared plumbing: the 8 co-op options in `server.cfg` (4 of them sent to the game in `InitialPlayerSync.CoopSettings`), the saved player fields for T1 and D5b, `Interfered` on `PlayerStats` | Headless: `PlayerDataSaveLoadTest` (save and load, old save without the fields), `CoopPlumbingTest` (L2: options reach the game, `Interfered` kept and relayed), `PacketsSerializableTest`; full suite green. No game run (no in-game behaviour yet) |
| 2026-11-02 | R1a | Done: the sleep status names who is still awake (names in `SleepStatusUpdate`, new en/de key); disconnect mid-wait counts joined players and updates when the sleeper leaves; eat/drink/stillsuit-drink sounds synced at 10 m. README co-op features table created | Headless: L2 `SleepStatusTest`, `EatingSoundTest`; L1 `FMODWhitelistTest`, `SleepStatusUpdateProcessorTest`; negative controls. Not in game |
| 2026-11-02 | A0 | Probe run: bridge `hold`, `sounds`, `pdaqueue`, `animator`; findings in D1 (breathing and pain not heard, bubbles 2D), D2 (real biome strings), D6 (light state matches; thrown flare not synced, H), R2 (`using_pda`, `holding_tool` missing), T1 (death clips play once, no held pose), T4 (reach fine; a click with `currentSub` set not shown), B4 (fire parents have ids), B5 (no wild regrowth in vanilla: drop), F2 (icon reset, from code). P2 not probed | `tools/scenarios/A0-Probes.ps1` windowless (two runs), game code, batch cell files |
| 2026-11-02 | R2a | Done: the server keeps one animation state per type and the held item per player (in memory) and sends both to a player after their initial sync; the infection reveal is relayed but no longer kept as a pose | Headless: L2 `PartnerPoseReplayTest` with negative controls. The rejoin not run in game |
| 2026-11-02 | F1 | Done: `PartnerSignal` on the partner's body with a rule list (`PartnerSignalRules`, "faint" last); marker hidden within 12 m or in sight under 25 m, range 150; name tag only within its range; depth in the label; settings Off/Faint/Always and name-tag range (en + de) | L1 `PartnerSignalRulesTest`; windowless `F1-PartnerSignal` PASS. No screenshots |
| 2026-11-02 | D1 | Done (pain, breathing): `PlayerPainSound` from a Postfix on `PlayerDamageSounds.OnTakeDamage`, played on the partner's body within 30 m; the breathing loop on the body follows their underwater state and tank; paths read from the local emitters. Breathing is a 2D event | L1, L2 `PainSoundRelayTest`; windowless `D1-HearPartner` PASS; negative control: A0 probe. Not listened to |
| 2026-11-02 | D6 | Flare fixed: thrown out of the partner's loaded cells it stayed unlit in the thrower's inventory in the partner's game (no spawn sent); the server now sends those players `EntityDestroyed` for their stale copy. Metadata was fine | L2 `DroppedOutOfSightTest`; windowless `D6-ThrownFlare` PASS, fails the 120 m check without the fix. Lights drawn at 60–120 m: open (windowed) |
