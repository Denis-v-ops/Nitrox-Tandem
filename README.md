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

## What Tandem adds to Nitrox

Tandem is based on Nitrox `master` (1.9.0.0 in development). Everything Nitrox does, Tandem does too. On top of that
it brings two things:

1. **Dozens of bug fixes** for the desyncs that break a two-player story run: lost progress, things that only happen in
   one player's game, and state that comes back wrong after a restart.
2. **Co-op features** that make it feel like playing *together*, built around three ideas:
   - **It stays scary.** Your partner is a faint signal, not a minimap dot. In caves and the deep the signal is lost,
     and you find each other by sound and light.
   - **Every help has a price.** Reviving, sharing air and hitching a ride all cost something. Creatures, resources and
     fragments keep the game's own numbers.
   - **The big story beats wait for both of you.** Nobody meets the Sea Emperor or watches the credits alone.

Every rule can be switched off in the server's `server.cfg`. The full list of features, prices and options is in
[docs/fork/PLAYING.md](docs/fork/PLAYING.md).

## Co-op features

### The story waits for both of you
- **The Sunbeam radio** only plays while both of you are online, so the Sunbeam never arrives for one player alone.
- **The Aurora's drive core:** the last leak only seals once both of you are in the drive core room.
- **The Sea Emperor** comes to her platform once you both stand on it, and **the eggs** hatch once you've both used
  the incubator.
- **The cure:** her babies drop cure balls in both games, and each of you gets your own cure scene at the same moment.
- **Disabling the gun:** you both use the terminal and see your own scene together.
- **The rocket** only launches with both of you in the cockpit.

While one of you waits, the game says who it's waiting for and the partner sees that you're waiting. If a partner
stops playing for good, an admin can let a single beat through (`storygate release <beat>`).

### Help each other, at a price
- **Rescue window:** at 0 health you go down instead of dying, and your partner has 60 s to get you back up (faster
  with a first aid kit). The window shrinks each time you go down until you rest. Lava, heat, drowning and a
  Cyclops explosion still kill.
- **Distress call:** when your partner goes down, their signal turns red and shows the time left, at any distance.
- **Buddy breathing:** give a partner who is running out of air some of yours. They get only half of it.
- **Hand items over** straight from your inventory into theirs.
- **Hitch a ride:** the Seamoth has one seat, so the second diver holds on to its hull. Your air keeps draining.
- **Cyclops crew:** one steers, the other runs sonar, shield, silent running, decoys and cameras.
- **Craft from your lockers:** base and Cyclops fabricators also use what's in their lockers.
- **Sleeping together:** the night passes once everyone is in bed, and the game says who is still up.

### Find each other, and keep it scary
- **Your partner's signal:** a faint marker with their name and depth that fades out when they're close or in sight.
  Their name tag and vitals show only up close.
- **The deep cuts the line:** in caves, the Lost River, the lava zones, alien facilities and below 300 m the signal
  drops to a flickering "last contact". Beacons and scanner rooms relay it.
- **Context pings** (middle mouse button): "Danger!" at a hunting creature, the item's icon at a resource, otherwise
  "Come here".
- **Hear your partner:** their eating and drinking, pain and breathing come from their body, close by only.
- **Their flare burns in your game too.**
- **Your own firsts:** the PDA describes a new place when *you* get there, not when your partner does across the map.

### See each other
- Your partner looks right the moment you join: how they stand or swim, and what they hold.
- You see them scan, build, use the PDA, jump, sit on benches, lie in bed and climb the escape pod's hatches.
- You see their tools at work: scanner and builder beams, welder sparks, the laser cutter's glow and the propulsion
  and repulsion cannons.
- **A cuddlefish for both of you:** it follows whoever played with it last, and both of you see it.
- The game's own "Saving game" notice shows on both screens.

## Bug fixes

Each fix is verified automatically, usually with two real game clients running a scripted scenario that fails
without the fix. Root causes and results: [docs/fork/FIX_SPEC.md](docs/fork/FIX_SPEC.md) and
[docs/fork/MULTINAUGHTICA_SPEC.md](docs/fork/MULTINAUGHTICA_SPEC.md) (fixes taken over from the MultiNaughtica co-op
fork).

### Story and progress
- Disabling the alien gun counts for both players, so the rocket can launch without relogging
  ([#2640](https://github.com/SubnauticaNitrox/Nitrox/issues/2640)).
- A partner who was offline when the other took the cure no longer stays infected forever.
- The Sea Emperor's babies visit their mother and drop cure balls in both games, and they don't come back after a
  restart.
- Story goals that arrive while a game is still loading are no longer lost, and opened alien teleporters stay open.
- A player who joins while the Aurora's leaks are only partly fixed gets the right radiation, and the "all leaks fixed"
  story step still comes.
- Two players building one rocket at the same moment don't lose a stage.

### Joining, saves and the world
- A new player reliably starts inside the escape pod
  ([#2898](https://github.com/SubnauticaNitrox/Nitrox/issues/2898)), and respawning in the pod keeps them inside it.
- The server makes a backup of the world every time it starts (the last 5 are kept).
- A returning player who joins while the world is still loading is no longer treated as a new player.
- A crash while joining, or a dropped connection, no longer blocks you from rejoining.
- Remote players and vehicles no longer jitter more and more as a world gets older.
- Some wreck objects were placed about 1.4 km from where they belong; now they're correct.

### Cyclops and vehicles
- Cyclops damage, leaks and fires are saved and shared, so no phantom explosions or leaks that come back
  ([#2220](https://github.com/SubnauticaNitrox/Nitrox/issues/2220), [#2230](https://github.com/SubnauticaNitrox/Nitrox/issues/2230)).
- A Cyclops destroyed by damage no longer blows up early in the partner's game.
- The Cyclops drives smoothly for the passenger: no lagging behind, no snapping back when the helm changes hands
  ([#2379](https://github.com/SubnauticaNitrox/Nitrox/issues/2379)). A passenger who rejoins is back inside.
- No more "swimming" inside the Cyclops or dry alien rooms ([#2712](https://github.com/SubnauticaNitrox/Nitrox/issues/2712)).
- Battery charge is saved exactly, including a battery that runs flat.

### Bases and power
- Base power is shared and saved: generators keep their charge after a restart, no free refill, and a reactor rod is
  used up once, not once per player ([#1811](https://github.com/SubnauticaNitrox/Nitrox/issues/1811)).
- Repaired leaks stay repaired, and the flood water level survives restarts
  ([#2769](https://github.com/SubnauticaNitrox/Nitrox/issues/2769), [#2808](https://github.com/SubnauticaNitrox/Nitrox/issues/2808)).
- The scanner room is shared: same resource, same blips, upgrades and drones kept, no multiplying drones, and broken
  outcrops disappear for both players
  ([#2410](https://github.com/SubnauticaNitrox/Nitrox/issues/2410), [#2696](https://github.com/SubnauticaNitrox/Nitrox/issues/2696),
  [#2046](https://github.com/SubnauticaNitrox/Nitrox/issues/2046)).
- Two players building the same piece pay for it once.

### Items
- No more duplicates: an empty tool doesn't come back with a full battery
  ([#2668](https://github.com/SubnauticaNitrox/Nitrox/issues/2668)), and two players taking the same item out of a
  fabricator get one between them.
- No more losses: a locker that still holds items isn't deleted, and items in lockers don't reappear as floating
  "ghosts" after a restart ([#2691](https://github.com/SubnauticaNitrox/Nitrox/issues/2691)). Items in chargers and
  upgrade slots are safe during a "resync" ([#2576](https://github.com/SubnauticaNitrox/Nitrox/issues/2576)).
- The water filtration machine no longer overflows, and the bioreactor no longer overfills or loses items
  ([#2495](https://github.com/SubnauticaNitrox/Nitrox/issues/2495), [#2494](https://github.com/SubnauticaNitrox/Nitrox/issues/2494)).

### Creatures
Each creature is run by one player's game, and much went wrong for the other player. Now:
- Creatures attack both of you, and bite your vehicles too, not only the one whose game runs them
  ([#2781](https://github.com/SubnauticaNitrox/Nitrox/issues/2781)).
- Damage from both players adds up, and a creature killed by either of you dies for both
  ([#2510](https://github.com/SubnauticaNitrox/Nitrox/issues/2510)).
- Leviathans are where your partner sees them, and a creature that swims over to the other player is handed to that
  player's game.
- A Reaper grabbing a vehicle, a Crabsquid's EMP and a bleeder latching on happen in both games.
- Warpers are the same for both of you, and crash fish no longer disappear
  ([#2673](https://github.com/SubnauticaNitrox/Nitrox/issues/2673)).
- Eggs in an alien containment hatch into exactly one creature
  ([#2538](https://github.com/SubnauticaNitrox/Nitrox/issues/2538)). A stalker drops one tooth, not one per player.

### Comfort
- **One-key bug reports:** press **F4**, or type `/report <what happened>` in chat. Logs, the server log and a save
  snapshot are zipped to `%APPDATA%\Nitrox\reports`. Attach the zips from both PCs to an issue here.
- **The launcher knows it's Tandem.** It shows "Nitrox Tandem" and never offers to "update" to official Nitrox,
  which would remove these fixes and couldn't load your Tandem world.

## Planned

- **Cozy base life:** seeing the cuddlefish's tricks when it plays with your partner.

The design for everything above and what comes next: [docs/fork/COOP_FEATURES_SPEC.md](docs/fork/COOP_FEATURES_SPEC.md).

## Compatibility — read before playing

- **Everyone must run the same Nitrox Tandem build:** the server and every player. Mixing builds, or mixing Tandem
  with official Nitrox, doesn't work.
- **Official Nitrox can't load worlds saved with Tandem.** Tandem saves extra data (Cyclops damage, base power and
  floods, scanner rooms, story gates, and more).
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
- Nobody play-tests each fix by hand, so every change has to pass automated checks: unit tests, headless server tests
  with simulated players, and scripted scenarios with two real game clients. Where possible, a "negative control"
  shows the test fails without the fix.
- For developers: [AGENTS.md](AGENTS.md) is the guide to the codebase. [docs/fork/TESTING.md](docs/fork/TESTING.md)
  covers the two-client test rig (`tools\dual-client.ps1`), the dev bridge that drives running games from the shell
  and detects desyncs (`tools\bridge.ps1`), and the scenarios in `tools\scenarios`. Every upstream issue is triaged
  for co-op story play in [docs/fork/ISSUE_TRIAGE.md](docs/fork/ISSUE_TRIAGE.md).
- Fixes that could help everyone may be offered back to upstream as separate, small pull requests. Those follow
  Nitrox's [contribution rules](CONTRIBUTING.md), including declaring AI use.

## Credits and license

- **Nitrox** is the work of the [Nitrox team and contributors](https://github.com/SubnauticaNitrox/Nitrox/graphs/contributors).
  Tandem only builds on top of it. Several fixes here are open upstream pull requests by community contributors, merged
  early, and some come from the MultiNaughtica co-op fork.
- **For Nitrox itself** (installation, the official Discord, translations), see the
  [official repository](https://github.com/SubnauticaNitrox/Nitrox) and [nitrox.rux.gg](https://nitrox.rux.gg).
- **License:** [GNU GPL v3](LICENSE.txt), same as Nitrox.
- **Trademarks:** Subnautica is a trademark of Unknown Worlds Entertainment. This project is not affiliated with Unknown
  Worlds.
