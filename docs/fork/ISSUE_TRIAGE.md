# Upstream issue triage for the co-op fork

Snapshot of **all 107 open issues** in [SubnauticaNitrox/Nitrox](https://github.com/SubnauticaNitrox/Nitrox/issues), taken
2026-11-01 against upstream `master` @ `62ef043a` (the latest release is 1.8.1.0 from 2026-01-07; master is ~10 months
ahead of it). Upstream also had 29 open PRs, and several of them fix issues ranked here.

## How issues were ranked

Upstream ranks by its own roadmap, and reaction counts favour Linux packaging, Docker and VR. Neither fits our goal, so we
rank by:

1. **Impact on a 2-player co-op story playthrough on Windows**, in this order:
   story-blocking → permanent loss (items, vehicles, base parts) → duplication / economy break → desync that changes what
   happens (creatures, damage, flooding) → cosmetic.
2. **How likely we are to hit it** in a normal playthrough (every session vs. one specific wreck).
3. **Confidence**: `Status: verified` beats `to verify`, and recent reports beat reports from 2023 against an old build.
4. **Fix readiness**: an open upstream PR we can adopt beats a from-scratch fix. Upstream's `p/0`–`p/2` labels are shown but
   are not the sort key.

Related issues are merged into one spec item when they share a root cause.

## Summary

| Tier | Meaning | Count |
|---|---|---|
| **T0** | In the fix spec ([FIX_SPEC.md](FIX_SPEC.md)), items S1–S10 | 22 issues → 10 items |
| **V** | Verify on current master first (likely fixed, old, or no repro) | 12 |
| **T1** | Next up after the top 10 | 10 |
| **T2** | Minor or cosmetic | 25 |
| **T3** | Not relevant to our goal (other OSes, packaging, CI/dev tooling, public-server features) | 38 |

### The top 10 at a glance

| # | Item | Issues | Bites in | Mode |
|---|---|---|---|---|
| 1 | S1 Cyclops damage, leaks and fires persist | #2220 #2230 | Mid | Adopt + fix PR #2881 |
| 2 | S2 Items in charger / scanner-room / upgrade slots vanish | #2259 #2696 #2576 | Early–Mid | Build |
| 3 | S3 Precursor gun deactivation not seen by partner | #2640 | Late (story gate) | Build (small) |
| 4 | S4 Creatures don't die for the other player | #2510 #1701 | Early → | Adopt PR #2902 + PR #2867 with fix |
| 5 | S5 Base leaks and flooding desync | #2769 #2808 | Mid | Adopt PR #2798 + finish PR #2811 |
| 6 | S6 Player stuck "swimming" indoors | #2712 #2306 | Mid–Late | Build |
| 7 | S7 Containers overfill, duplicate, items respawn | #2495 #2494 #2047 #2691 | Early → | Build |
| 8 | S8 Battery duplication from empty tools | #2668 | Early | Adopt PR #2809 |
| 9 | S9 Player spawns outside escape pod | #2898 | Start | Adopt PR #2899 |
| 10 | S10 Scanner room isn't shared | #2410 #2046 #2151 #1425 | Early–Mid | Build |

### Open upstream PRs we looked at

| PR | Targets | Our verdict |
|---|---|---|
| [#2798](https://github.com/SubnauticaNitrox/Nitrox/pull/2798) | #2769 leaks | Adopt (approved ×2, small) |
| [#2899](https://github.com/SubnauticaNitrox/Nitrox/pull/2899) | #2898 escape pod | Adopt (3-line change) |
| [#2809](https://github.com/SubnauticaNitrox/Nitrox/pull/2809) | #2668 battery dupe | Adopt (maintainer approved) |
| [#2902](https://github.com/SubnauticaNitrox/Nitrox/pull/2902) | #2510 creature deaths | Adopt (unreviewed, so test it) |
| [#2867](https://github.com/SubnauticaNitrox/Nitrox/pull/2867) | #1701 LiveMixin | Adopt with fix (clashes with #2902; may affect player death) |
| [#2881](https://github.com/SubnauticaNitrox/Nitrox/pull/2881) | #2220 #2230 Cyclops / fires | Adopt with fix (constructor conflict with #2899; one-way save change) |
| [#2903](https://github.com/SubnauticaNitrox/Nitrox/pull/2903) | creature damage adds up | Wait (contains #2902; large, unreviewed, tests never run) |
| [#2811](https://github.com/SubnauticaNitrox/Nitrox/pull/2811) | #2808 flood state | Wait / finish ourselves (the described rework was never pushed) |
| [#2842](https://github.com/SubnauticaNitrox/Nitrox/pull/2842) | #2510 | Skip (superseded, conflicts) |
| [#2825](https://github.com/SubnauticaNitrox/Nitrox/pull/2825) | #2168 join freeze | Skip for now (unreviewed, ships a binary AssetBundle) |

Other open PRs that match Tier 1/2 issues without linking them: #2840 → #2261 (picture frames), #2859 → #2639
(animations), #2866 → #2673 (crashfish), #2649 → #2643 (precursor triggers), #2873 → #2543/#2473, #2874 → #2870,
#2839 → #2545, #2890 → #2731, #2891 → #2460, #2255 → #2250, #2804 → #2705.

## Tier 0 — in the fix spec (top 10) (22)

Grouped into spec items S1–S10 in [FIX_SPEC.md](FIX_SPEC.md).

| Issue | Title | Upstream | Status | Our note |
|---|---|---|---|---|
| [#2220](https://github.com/SubnauticaNitrox/Nitrox/issues/2220) | Cyclops health/damages aren't well synced |  |  | S1 |
| [#2230](https://github.com/SubnauticaNitrox/Nitrox/issues/2230) | Add sync for Fires | p/0 |  | S1 |
| [#2259](https://github.com/SubnauticaNitrox/Nitrox/issues/2259) | Resync base buildings button makes some objects disappear |  | to verify | S2 |
| [#2576](https://github.com/SubnauticaNitrox/Nitrox/issues/2576) | Scanner cameras respawn in the slots after server restart |  | duplicate | S2 (duplicate of #2696) |
| [#2696](https://github.com/SubnauticaNitrox/Nitrox/issues/2696) | Scanner room losing upgrades on server restart |  | verified | S2 |
| [#2640](https://github.com/SubnauticaNitrox/Nitrox/issues/2640) | Investigate sync for PrecursorGun | p/0 |  | S3 |
| [#1701](https://github.com/SubnauticaNitrox/Nitrox/issues/1701) | LiveMixin is not synced for most of the instances |  | verified | S4 |
| [#2510](https://github.com/SubnauticaNitrox/Nitrox/issues/2510) | Creatures won't die for one player | p/1 | verified | S4 |
| [#2769](https://github.com/SubnauticaNitrox/Nitrox/issues/2769) | Base leaks are not synced |  | verified | S5 |
| [#2808](https://github.com/SubnauticaNitrox/Nitrox/issues/2808) | FloodState is not synchronized correctly between players |  | verified | S5 |
| [#2306](https://github.com/SubnauticaNitrox/Nitrox/issues/2306) | Swimming inside alien base when rejoining |  |  | S6 |
| [#2712](https://github.com/SubnauticaNitrox/Nitrox/issues/2712) | [BUG] When entering the cyclops, rarely the player is still swimming. |  | verified | S6 |
| [#2047](https://github.com/SubnauticaNitrox/Nitrox/issues/2047) | All items in storage are duplicated (potentially when a member joins) |  | to verify | S7 |
| [#2494](https://github.com/SubnauticaNitrox/Nitrox/issues/2494) | Container Overfill |  | to verify | S7 |
| [#2495](https://github.com/SubnauticaNitrox/Nitrox/issues/2495) | Water Filtration overflows past 4 storage slots |  | verified | S7 |
| [#2691](https://github.com/SubnauticaNitrox/Nitrox/issues/2691) | Floating resources in base keep respawning |  | verified | S7 |
| [#2668](https://github.com/SubnauticaNitrox/Nitrox/issues/2668) | Battery duplication issue | p/0 | verified | S8 |
| [#2898](https://github.com/SubnauticaNitrox/Nitrox/issues/2898) | Player might appear out of EscapePod after InitialSync |  |  | S9 |
| [#1425](https://github.com/SubnauticaNitrox/Nitrox/issues/1425) | Scanner room cameras are not synced |  |  | S10 |
| [#2046](https://github.com/SubnauticaNitrox/Nitrox/issues/2046) | Scanner Room reveals the location of resources which are not there |  | verified | S10 |
| [#2151](https://github.com/SubnauticaNitrox/Nitrox/issues/2151) | Scanner room detecting already broken outcrops |  | verified | S10 |
| [#2410](https://github.com/SubnauticaNitrox/Nitrox/issues/2410) | Add scanner rooms sync | p/1 |  | S10 |

## Verify first — reproduce on current master (12)

Reported against 1.8.x or not yet reproduced. Master is ~10 months ahead of the 1.8.1.0 release; check these during play-testing before planning work.

| Issue | Title | Upstream | Status | Our note |
|---|---|---|---|---|
| [#2832](https://github.com/SubnauticaNitrox/Nitrox/issues/2832) | Low framerate inside cyclops. |  | Needs testing | Low FPS inside Cyclops |
| [#2800](https://github.com/SubnauticaNitrox/Nitrox/issues/2800) | Every time the server restarts, the number of birds may double. |  | to verify | Birds may double on every server restart (entity leak) |
| [#2781](https://github.com/SubnauticaNitrox/Nitrox/issues/2781) | Creatures seem passive in survival mode |  | to verify | Creatures passive in survival |
| [#2768](https://github.com/SubnauticaNitrox/Nitrox/issues/2768) | Build second seamoth despawns first one |  | to verify | Building a 2nd Seamoth despawns the 1st (vehicle loss); reporter links #1223 |
| [#2761](https://github.com/SubnauticaNitrox/Nitrox/issues/2761) | Vehicles do not synchronize between players |  | to verify | Vehicle movement regression on master; superseding fix #2864 merged 2026-09-06 - verify in Phase 0 |
| [#2745](https://github.com/SubnauticaNitrox/Nitrox/issues/2745) | Abnormal Blighter spawn rate near the geothermal vent at the spawn location |  | to verify | Too many Blighters near spawn vent |
| [#2574](https://github.com/SubnauticaNitrox/Nitrox/issues/2574) | Battery discharge will randomly get stuck. |  | to verify | Tool battery stops draining (exploit, not loss) |
| [#2547](https://github.com/SubnauticaNitrox/Nitrox/issues/2547) | Wreck 2 is not loading |  | to verify | Wreck missing on a world converted from 1.8.0.0; fresh world OK |
| [#2538](https://github.com/SubnauticaNitrox/Nitrox/issues/2538) | Cuddlefish egg dissapeared when inside alien containment |  | to verify | Cuddlefish eggs vanish in alien containment; several reports |
| [#2448](https://github.com/SubnauticaNitrox/Nitrox/issues/2448) | standing lockers randomly disappear in the cyclops |  | to verify | Standing lockers vanish in Cyclops (item loss); no repro yet |
| [#2130](https://github.com/SubnauticaNitrox/Nitrox/issues/2130) | CrashFish spawning close to you random times |  | to verify | Crashfish spawning next to player |
| [#2127](https://github.com/SubnauticaNitrox/Nitrox/issues/2127) | wrecks not appearing for my friend |  | to verify | Wrecks not appearing for 2nd player (2024 report) |

## Tier 1 — next up for a co-op story run (10)

Real co-op impact, but less severe or less frequent than Tier 0, or a feature we want.

| Issue | Title | Upstream | Status | Our note |
|---|---|---|---|---|
| [#2673](https://github.com/SubnauticaNitrox/Nitrox/issues/2673) | Sulfur home doesn't always spawn with crash fish. More issues seen with 2+ players. | p/1 |  | Crashfish/sulfur homes misbehave with 2 players; PR #2866 (crashfish sync) |
| [#2652](https://github.com/SubnauticaNitrox/Nitrox/issues/2652) | Add "Wait for all players" option for major story events |  |  | 'Wait for all players' at gun terminal / rocket - fits our goal exactly |
| [#2643](https://github.com/SubnauticaNitrox/Nitrox/issues/2643) | Ion cube pedestals don't react to remote players |  |  | Precursor ion-cube pedestals ignore remote players; PR #2649 |
| [#2639](https://github.com/SubnauticaNitrox/Nitrox/issues/2639) | Re-enable player animation sync |  |  | Re-enable player animation sync; PR #2859 |
| [#2627](https://github.com/SubnauticaNitrox/Nitrox/issues/2627) | Creature Sync EPIC |  |  | Creature sync EPIC (umbrella) |
| [#2508](https://github.com/SubnauticaNitrox/Nitrox/issues/2508) | Voice Log in PDA are not synced for notification after reading/listening |  | verified | Read PDA voice logs show unread again after relog |
| [#2460](https://github.com/SubnauticaNitrox/Nitrox/issues/2460) | PlayerHeldItemChanged NRE |  |  | NRE in PlayerHeldItemChanged; draft PR #2891 |
| [#2250](https://github.com/SubnauticaNitrox/Nitrox/issues/2250) | Add sync for Stalker | p/0 |  | Stalker tooth drops not synced (needed for Enameled Glass); PR #2255 has changes requested |
| [#2168](https://github.com/SubnauticaNitrox/Nitrox/issues/2168) | Freezes when someone is joining the game |  |  | Hitch for everyone when a player joins; PR #2825 (large) |
| [#1811](https://github.com/SubnauticaNitrox/Nitrox/issues/1811) | Properly syncing Base Power  |  | verified | Base power: stored power resets on rejoin, power only usable by builder (old, verify) |

## Tier 2 — minor / cosmetic (25)

Noticeable but doesn't lose progress or block the story.

| Issue | Title | Upstream | Status | Our note |
|---|---|---|---|---|
| [#2895](https://github.com/SubnauticaNitrox/Nitrox/issues/2895) | Spawn rotation when inside a base is wrongly interpreted as opposite facing |  |  | Spawn rotation inside base reversed |
| [#2893](https://github.com/SubnauticaNitrox/Nitrox/issues/2893) | Fish size changes when being dropped |  | verified | Fish size changes when dropped |
| [#2870](https://github.com/SubnauticaNitrox/Nitrox/issues/2870) | Flare energy isn't correctly synced | p/2 | verified | Flare energy; PR #2874 (changes requested) |
| [#2868](https://github.com/SubnauticaNitrox/Nitrox/issues/2868) | Sync Radius-Driven Damage Effects and BreakFX |  | verified | Radius damage FX / BreakFX not synced |
| [#2834](https://github.com/SubnauticaNitrox/Nitrox/issues/2834) | UseShellExecute error when launching Subnautica via Heroic |  | to verify | Heroic launcher UseShellExecute error (only if game is from Epic via Heroic) |
| [#2822](https://github.com/SubnauticaNitrox/Nitrox/issues/2822) | Unable to properly ping ground on islands & bases | p/2 |  | Can't ping ground on islands/bases |
| [#2713](https://github.com/SubnauticaNitrox/Nitrox/issues/2713) | Got TP fallback when in lavazone |  |  | Teleport fallback in lava zone |
| [#2705](https://github.com/SubnauticaNitrox/Nitrox/issues/2705) | Reduce network bandwidth requirement by optimizing movement sync |  |  | Movement bandwidth; PR #2804 (irrelevant on LAN) |
| [#2611](https://github.com/SubnauticaNitrox/Nitrox/issues/2611) | Thermoblade not cooking fish on hit if fish is simulated by different player | p/2 | verified | Thermoblade doesn't cook fish simulated by other player |
| [#2549](https://github.com/SubnauticaNitrox/Nitrox/issues/2549) | Docking a Seamoth to the Cyclops plays for all players |  | verified | Seamoth docking sound plays for everyone |
| [#2545](https://github.com/SubnauticaNitrox/Nitrox/issues/2545) | Change quit button to not close the game |  |  | Quit to main menu instead of closing game; PR #2839 |
| [#2543](https://github.com/SubnauticaNitrox/Nitrox/issues/2543) | Floating fragments |  |  | Floating fragments; PR #2873 |
| [#2473](https://github.com/SubnauticaNitrox/Nitrox/issues/2473) | Lights are missing on Databox in wrecks |  |  | Databox lights missing in wrecks; PR #2873 |
| [#2465](https://github.com/SubnauticaNitrox/Nitrox/issues/2465) | Rocket stage building sounds are global |  |  | Rocket build sounds global |
| [#2463](https://github.com/SubnauticaNitrox/Nitrox/issues/2463) | Laser cutting on sealed doors animations are broken | p/1 |  | Laser-cutter beam renders from world origin (cosmetic, cutting works) |
| [#2461](https://github.com/SubnauticaNitrox/Nitrox/issues/2461) | GhostLeviathanVoid missing MP sync |  |  | Ghost Leviathan (void) not synced |
| [#2379](https://github.com/SubnauticaNitrox/Nitrox/issues/2379) | Remote player cyclops pawn can move during pilot exit animation | p/2 |  | Remote Cyclops pawn moves during exit animation |
| [#2372](https://github.com/SubnauticaNitrox/Nitrox/issues/2372) | NRE in CyclopsPawn | p/2 |  | NRE in CyclopsPawn |
| [#2281](https://github.com/SubnauticaNitrox/Nitrox/issues/2281) | Player appears as if running when swimming at the surface (incorrect animation) | p/2 |  | Running animation when swimming at surface |
| [#2261](https://github.com/SubnauticaNitrox/Nitrox/issues/2261) | Picture Frames aren't synced | p/2 |  | Picture frames not synced; PR #2840 |
| [#2256](https://github.com/SubnauticaNitrox/Nitrox/issues/2256) | Creatures during EscapePod First Cinematics should be synced |  |  | Creatures in escape-pod intro cinematic not synced |
| [#2251](https://github.com/SubnauticaNitrox/Nitrox/issues/2251) | Garbages around wrecks will respawn upon relog |  |  | Wreck debris respawns on relog |
| [#2237](https://github.com/SubnauticaNitrox/Nitrox/issues/2237) | Inventory warning while loading |  |  | Inventory warning while loading |
| [#2163](https://github.com/SubnauticaNitrox/Nitrox/issues/2163) | Performance improvements |  |  | General performance |
| [#116](https://github.com/SubnauticaNitrox/Nitrox/issues/116) | Player Movement Enhancements |  |  | Player movement polish |

## Tier 3 — not relevant to our goal (38)

Linux/macOS/VR/packaging, CI and dev tooling, public-server features. Leave to upstream.

| Issue | Title | Upstream | Status | Our note |
|---|---|---|---|---|
| [#2897](https://github.com/SubnauticaNitrox/Nitrox/issues/2897) | Debuggers can disappear because of NitroxChat |  |  | Debugger windows vs chat |
| [#2871](https://github.com/SubnauticaNitrox/Nitrox/issues/2871) | [MSBuild] Add support for trimming |  |  | Trimming |
| [#2851](https://github.com/SubnauticaNitrox/Nitrox/issues/2851) | Fix remaining 8 DIMA001 warnings |  |  | DIMA001 warnings; draft PR #2878 |
| [#2829](https://github.com/SubnauticaNitrox/Nitrox/issues/2829) | [CI] Move from self-hosted runner to github trusted runner |  |  | CI runner |
| [#2828](https://github.com/SubnauticaNitrox/Nitrox/issues/2828) | [MSBuild] Add support for SelfContained build |  |  | Self-contained build |
| [#2732](https://github.com/SubnauticaNitrox/Nitrox/issues/2732) | Add a player command (can be done server side) to unlock an achievement |  |  | Achievement command |
| [#2731](https://github.com/SubnauticaNitrox/Nitrox/issues/2731) | Allow user to configure if they want achievements and persist Player.hasUsedConsole |  |  | Achievement settings; PR #2890 |
| [#2698](https://github.com/SubnauticaNitrox/Nitrox/issues/2698) | Implement a CircuitBreaker policy for NtpSync |  |  | NTP circuit breaker |
| [#2681](https://github.com/SubnauticaNitrox/Nitrox/issues/2681) | Add a flake.nix for NixOS support |  |  | NixOS flake |
| [#2672](https://github.com/SubnauticaNitrox/Nitrox/issues/2672) | Use AvaloniaEdit for embedded server output to improve UX |  |  | Launcher server output view |
| [#2581](https://github.com/SubnauticaNitrox/Nitrox/issues/2581) | Launch does not proceed when -vrmode option is set to anything but none |  | to verify | VR mode launch |
| [#2559](https://github.com/SubnauticaNitrox/Nitrox/issues/2559) | Add plugin system licensed as LGPLv3 |  |  | Plugin system |
| [#2509](https://github.com/SubnauticaNitrox/Nitrox/issues/2509) | Disallow players outside group from entering base. |  |  | Base access control |
| [#2501](https://github.com/SubnauticaNitrox/Nitrox/issues/2501) | Docker or podman image for Nitrox.Server.Subnautica |  |  | Docker image |
| [#2499](https://github.com/SubnauticaNitrox/Nitrox/issues/2499) | Support reverse Proxy Protocol V2 to get actual client IP |  |  | Proxy protocol |
| [#2491](https://github.com/SubnauticaNitrox/Nitrox/issues/2491) | Steam Deck text keyboard support for password and username on login screen |  | to verify | Steam Deck keyboard |
| [#2479](https://github.com/SubnauticaNitrox/Nitrox/issues/2479) | Provide a packaged build for Linux releases |  |  | Linux packaging |
| [#2377](https://github.com/SubnauticaNitrox/Nitrox/issues/2377) | Improve UX when copying server output in launcher |  |  | Launcher copy UX |
| [#2297](https://github.com/SubnauticaNitrox/Nitrox/issues/2297) | Discord integration broken on linux/macOS |  |  | Discord on Linux/macOS |
| [#2285](https://github.com/SubnauticaNitrox/Nitrox/issues/2285) | Modify game directory on Linux |  |  | Linux game directory |
| [#2233](https://github.com/SubnauticaNitrox/Nitrox/issues/2233) | Unify Transpilers code |  |  | Unify transpilers |
| [#2231](https://github.com/SubnauticaNitrox/Nitrox/issues/2231) | Upgrade AddressablesTools nuget version |  |  | AddressablesTools upgrade |
| [#2226](https://github.com/SubnauticaNitrox/Nitrox/issues/2226) | [PvP] Add more damage sources |  |  | PvP damage sources |
| [#2198](https://github.com/SubnauticaNitrox/Nitrox/issues/2198) | Packet suppression should happen automatically via source generated interceptors |  |  | Packet suppression source-gen |
| [#2160](https://github.com/SubnauticaNitrox/Nitrox/issues/2160) | Support online port checkers |  |  | Online port checkers |
| [#2158](https://github.com/SubnauticaNitrox/Nitrox/issues/2158) | Improve player info UI just above the players heads |  |  | Name-plate UI |
| [#2147](https://github.com/SubnauticaNitrox/Nitrox/issues/2147) | Implement UDP NAT hole punching (via Steam P2P) |  |  | NAT hole punching (not needed on LAN) |
| [#2081](https://github.com/SubnauticaNitrox/Nitrox/issues/2081) | Add "Service" project that handles system configuration, which demands admin permissions |  |  | Admin service project |
| [#2017](https://github.com/SubnauticaNitrox/Nitrox/issues/2017) | Fix namespaces in Nitrox.Test to match folder structure |  |  | Test namespaces |
| [#1996](https://github.com/SubnauticaNitrox/Nitrox/issues/1996) | Packet encryption using RSA |  |  | Packet encryption |
| [#1945](https://github.com/SubnauticaNitrox/Nitrox/issues/1945) | Support Multiple Escapepods |  |  | Multiple escape pods (feature) |
| [#1689](https://github.com/SubnauticaNitrox/Nitrox/issues/1689) | Expand nitrox command system to wrap subnautica one |  |  | Command system |
| [#1653](https://github.com/SubnauticaNitrox/Nitrox/issues/1653) | Overhaul ingame server menu |  |  | In-game server menu |
| [#1639](https://github.com/SubnauticaNitrox/Nitrox/issues/1639) | Permission-based access system on storages |  |  | Storage permissions |
| [#1623](https://github.com/SubnauticaNitrox/Nitrox/issues/1623) | Add split screen shortcut to Nitrox (Already possible with Nucleus Coop) |  | out of scope | Split screen (out of scope) |
| [#1533](https://github.com/SubnauticaNitrox/Nitrox/issues/1533) | Project Nitrox.Test.EndToEnd.Subnautica |  |  | End-to-end test project (would help agents, large) |
| [#1474](https://github.com/SubnauticaNitrox/Nitrox/issues/1474) | Add a diagnostic summary |  |  | Launcher diagnostics |
| [#138](https://github.com/SubnauticaNitrox/Nitrox/issues/138) | Brainstorm new multiplayer friendly story |  |  | New story brainstorm |

## Refreshing this snapshot

```bash
gh issue list -R SubnauticaNitrox/Nitrox --state open --limit 300 --json number,title,labels,createdAt,updatedAt,comments,reactionGroups,author,body > open_issues.json
gh pr list -R SubnauticaNitrox/Nitrox --state open --limit 100 --json number,title,author,updatedAt,isDraft,closingIssuesReferences > open_prs.json
```
Compare against the tables above. New issues go into a tier by the criteria at the top. Closed ones get removed (and, if
they were in the spec, marked in its Log).
