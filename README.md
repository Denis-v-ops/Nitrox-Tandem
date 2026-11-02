# Nitrox Tandem

**Subnautica co-op, from the first dive to the rocket — together.**

Nitrox Tandem is an unofficial fork of [Nitrox](https://github.com/SubnauticaNitrox/Nitrox), the open-source multiplayer
mod for [Subnautica](https://unknownworlds.com/subnautica/). It focuses on one thing: two players playing through the
**whole story** without desyncs that lose progress, while keeping Subnautica's atmosphere.

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
[![Fork of Nitrox](https://img.shields.io/badge/fork%20of-SubnauticaNitrox%2FNitrox-informational)](https://github.com/SubnauticaNitrox/Nitrox)

> [!IMPORTANT]
> **Unofficial.** Nitrox Tandem is not made, endorsed or supported by the Nitrox team or by Unknown Worlds.
> Please report problems with this fork **here**
> ([issues](https://github.com/Denis-v-ops/Nitrox-Tandem/issues)), not on the Nitrox Discord or issue tracker.

## What's different from official Nitrox

Nitrox Tandem is based on Nitrox `master` (1.9.0.0 in development, snapshot of 2026-11-01). Everything Nitrox does,
Tandem does too. On top of that:

### Bug fixes for co-op story play

Each fix is verified automatically: unit tests, headless server tests, and/or scripted two-client in-game scenarios.
The details, root causes and test results are in [docs/fork/FIX_SPEC.md](docs/fork/FIX_SPEC.md).

| Area | What's fixed | Upstream issue / PR | Verified |
|---|---|---|---|
| **Story** | When one player disables the alien gun, the partner's game now knows too, so the rocket can launch without relogging | [#2640](https://github.com/SubnauticaNitrox/Nitrox/issues/2640) | In game, plus a negative control (the test fails without the fix) |
| **Story** | A partner who was offline when the other took the cure no longer stays infected forever | new | Headless server test + negative control |
| **Cyclops** | Damage points, leaks and fires are saved, shared and survive restarts, so no more phantom explosions or leaks that come back | [#2220](https://github.com/SubnauticaNitrox/Nitrox/issues/2220), [#2230](https://github.com/SubnauticaNitrox/Nitrox/issues/2230) (PR [#2881](https://github.com/SubnauticaNitrox/Nitrox/pull/2881)) | In game |
| **Bases** | Repaired leaks stay repaired; the flood water level survives restarts | [#2769](https://github.com/SubnauticaNitrox/Nitrox/issues/2769) (PR [#2798](https://github.com/SubnauticaNitrox/Nitrox/pull/2798)), [#2808](https://github.com/SubnauticaNitrox/Nitrox/issues/2808) | In game |
| **Bases** | Scanner room upgrades and camera drones are kept, with no more multiplying drones; charger and upgrade-slot items are safe during "resync" | [#2696](https://github.com/SubnauticaNitrox/Nitrox/issues/2696), [#2576](https://github.com/SubnauticaNitrox/Nitrox/issues/2576), [#2259](https://github.com/SubnauticaNitrox/Nitrox/issues/2259) | In game (partly) + headless |
| **Bases** | The scanner room is shared: same resource, same blips, and broken outcrops disappear for both players | [#2410](https://github.com/SubnauticaNitrox/Nitrox/issues/2410), [#2046](https://github.com/SubnauticaNitrox/Nitrox/issues/2046), [#2151](https://github.com/SubnauticaNitrox/Nitrox/issues/2151) | In game |
| **Items** | The water filtration machine no longer overflows, and the bioreactor no longer overfills or loses items | [#2495](https://github.com/SubnauticaNitrox/Nitrox/issues/2495), [#2494](https://github.com/SubnauticaNitrox/Nitrox/issues/2494) | In game |
| **Items** | Items moved into lockers no longer reappear as floating "ghost" items after a restart | [#2691](https://github.com/SubnauticaNitrox/Nitrox/issues/2691) | In game + headless |
| **Items** | Empty tools no longer come back with a free full battery | [#2668](https://github.com/SubnauticaNitrox/Nitrox/issues/2668) (PR [#2809](https://github.com/SubnauticaNitrox/Nitrox/pull/2809)) | In game + negative control |
| **Bases** | Two players building the same piece pay for it once: one builds, the other is told someone else is using it | new | In game + negative control |
| **Bases** | A locker or charger that still holds items is no longer deleted when one player's game wrongly sees it empty; that player gets it back | new | Headless + negative control |
| **Items** | Two players taking the same finished item out of a fabricator get one item between them, not one each | new | In game + negative control |
| **World** | Some wreck objects were placed about 1.4 km from where they belong on the server; now they're correct | new | In game + headless |
| **Creatures** | A creature killed by the player who isn't "simulating" it now dies for both players, too | [#2510](https://github.com/SubnauticaNitrox/Nitrox/issues/2510), [#1701](https://github.com/SubnauticaNitrox/Nitrox/issues/1701) (PRs [#2902](https://github.com/SubnauticaNitrox/Nitrox/pull/2902), [#2867](https://github.com/SubnauticaNitrox/Nitrox/pull/2867) with a fix) | In game |
| **Player** | No more "swimming" inside the Cyclops or dry alien rooms in two confirmed cases: Cyclops colliders, and warping to a partner | [#2712](https://github.com/SubnauticaNitrox/Nitrox/issues/2712) | In game + negative control |
| **Start** | New players reliably spawn inside the escape pod | [#2898](https://github.com/SubnauticaNitrox/Nitrox/issues/2898) (PR [#2899](https://github.com/SubnauticaNitrox/Nitrox/pull/2899)) | In game |
| **Cyclops** | The partner sees the Cyclops drive smoothly: no falling behind after a stutter, no snap back when the other player takes the helm, no moving while leaving the helm. A passenger who rejoins is back inside, even if the sub drove on | [#2379](https://github.com/SubnauticaNitrox/Nitrox/issues/2379) | In game + headless, negative control for the rejoin |
| **Creatures** | Aggressive creatures attack both players, not only the one whose game "simulates" them; crash fish explode for both | [#2781](https://github.com/SubnauticaNitrox/Nitrox/issues/2781) | In game + headless + negative control |

### For players
- **One-key bug reports:** press **F4**, or type `/report <what happened>` in chat. Logs, the server log and a save
  snapshot are zipped to `%APPDATA%\Nitrox\reports`. Attach the zips from both PCs to an issue here.
- **The launcher knows it's Tandem.** It shows "Nitrox Tandem" in the window title and in game, sends crash reports to
  this repo, and never offers to "update" to official Nitrox. That would remove these fixes and can't load your
  Tandem world.

### Planned co-op features
Not built yet; designed in [docs/fork/COOP_FEATURES_SPEC.md](docs/fork/COOP_FEATURES_SPEC.md). The idea: Subnautica
stays lonely, scary and hard, just shared.
- **It stays scary.** The deep cuts the line: in caves and below 300 m your partner's signal drops to a last contact,
  unless a beacon or scanner room you built relays it. You find each other by sound and light.
- **Every help has a price.** A revive takes a few seconds of holding, and the rescue window gets shorter each time you
  go down before resting. Sharing air costs the donor twice what the receiver gets. Creatures, resources and fragments
  keep the game's own numbers.
- **The big story beats wait for both of you.**
- **Each rule gets a switch** in the server's `server.cfg`.

What's planned:
- **Story moments together:** the Aurora drive core, disabling the gun, the Sea Emperor and the cure, and the rocket
  launch wait until both players are there. Playing alone in between is fine.
- **Teamwork survival:** a rescue window instead of instant death (revive with a first aid kit, or by hand), buddy
  breathing, handing items over, Cyclops crew roles, and hitching a ride on the Seamoth.
- **Finding each other:** your partner as a faint signal with name and distance, plus "danger / look / come here" pings.
- **Keeping it scary:** hear your partner get hurt or run out of air nearby; a distress call when they go down that
  pulses through even in the deep; see their flashlight and flares in the dark; each of you hears the PDA describe a
  new place when *you* get there.
- **Cozy base life:** seeing each other sit, sleep and use things, and a cuddlefish that plays with both of you.

### For developers
- An agent- and human-friendly guide to the codebase: [AGENTS.md](AGENTS.md).
- **A dev bridge** to drive and inspect running game clients from the shell, plus a desync detector (`tools\bridge.ps1`).
- **A one-command two-client test rig** (`tools\dual-client.ps1`), with scripted scenarios in `tools\scenarios`.
- **Headless server tests:** a real server in-process with simulated players. Tests run in seconds; no game window.
- Every upstream issue triaged for co-op story play: [docs/fork/ISSUE_TRIAGE.md](docs/fork/ISSUE_TRIAGE.md).

## Compatibility — read before playing

- **Everyone must run the same Nitrox Tandem build:** the server and every player. Mixing builds, or mixing Tandem
  with official Nitrox, doesn't work.
- **Official Nitrox can't load worlds saved with Tandem.** Tandem stores extra data (Cyclops damage and fires, base
  flood levels, scanner rooms).
- **Opening an existing official Nitrox world in Tandem hasn't been tested yet.** Back up first:
  `%APPDATA%\Nitrox\saves`.
- Game requirements are the same as Nitrox: a current Subnautica (Steam, Epic, …), not the legacy branch.

## Installing

There are no packaged releases yet; for now, build it yourself.
1. Install the [.NET SDK 10](https://dotnet.microsoft.com/download) and Subnautica.
2. Clone and build:
   ```bash
   git clone https://github.com/Denis-v-ops/Nitrox-Tandem.git
   cd Nitrox-Tandem
   dotnet build Nitrox.Launcher -c Release -r win-x64
   ```
3. Start `Nitrox.Launcher/bin/Release/net10.0/win-x64/Nitrox.Launcher.exe` (the exact folder may differ, see
   [AGENTS.md](AGENTS.md#3-build-and-test-verified-on-this-machine)). Host a server, launch the game, and join through
   **Multiplayer** in the main menu.

The branch to use is `coop` (the default). `master` mirrors official Nitrox.

## How it's developed

- Nitrox Tandem is developed with heavy AI assistance ([Claude Code](https://claude.com/claude-code)).
- Because nobody play-tests each fix by hand, every change has to pass automated checks:
  - unit and headless server tests;
  - scripted scenarios with two real game clients;
  - where possible, a "negative control" showing the test fails without the fix.
- How that works: [docs/fork/TESTING.md](docs/fork/TESTING.md).
- Fixes that could help everyone may be offered back to upstream as separate, small pull requests. Those follow
  Nitrox's [contribution rules](CONTRIBUTING.md), including declaring AI use.

## Credits and license

- **Nitrox** is the work of the [Nitrox team and contributors](https://github.com/SubnauticaNitrox/Nitrox/graphs/contributors).
  Tandem only builds on top of it. Several fixes here are open upstream pull requests by community contributors, merged
  early (linked above).
- **For Nitrox itself** (installation, the official Discord, translations), see the
  [official repository](https://github.com/SubnauticaNitrox/Nitrox) and [nitrox.rux.gg](https://nitrox.rux.gg).
- **License:** [GNU GPL v3](LICENSE.txt), same as Nitrox.
- **Trademarks:** Subnautica is a trademark of Unknown Worlds Entertainment. This project is not affiliated with Unknown
  Worlds.
