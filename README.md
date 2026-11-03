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
| **Bases** | Base power is shared and saved: solar panels, thermal plants and reactors keep their charge after a restart (no more free refill, and both players see the same power), and a nuclear reactor rod is used up once, not once per player | [#1811](https://github.com/SubnauticaNitrox/Nitrox/issues/1811) | In game + headless + negative control |
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
| **Creatures** | A stalker that loses a tooth drops one tooth, not one in each player's game | new | In game + negative control |
| **Creatures** | Creatures bite your Seamoth or Prawn suit even when your partner's game "simulates" them; before, a vehicle was safe from half the creatures around it | — | In game (Seamoth) + headless + negative control |
| **Creatures** | A leviathan (or any creature) that swims away from one player towards the other is now run by the game of the player it is with; before, the first player's game kept running it from hundreds of metres away | new | In game (Reaper) + headless + negative control |
| **Creatures** | Leviathans and other creatures are where your partner sees them: they no longer drift a few metres and then jump, and a leviathan you swim up to appears where it is instead of up to 30 m off | new | In game (Reaper, Reefback) + headless + negative control |
| **Creatures** | Eggs in an alien containment no longer vanish when they hatch: each egg gives exactly one creature for both players, also in a tank split off by deconstructing part of a stacked one | [#2538](https://github.com/SubnauticaNitrox/Nitrox/issues/2538) | In game + headless + negative control |
| **Creatures** | Warpers are the same for both of you: each warper spawner sends out one warper that you both see and that warps out for both of you, instead of different warpers in each game. Bleeders now swim about as in single player; before, no game ran them. | new | In game + headless + negative control |
| **Creatures** | A bleeder latches onto either of you, not only onto the player whose game runs it: it sucks your blood, you both see it on you, it lets go when you get into a vehicle, go inside or go down, and you can fight it off yourself | new | In game + negative control |

### Co-op features

Your partner shows up through the game's own messages and sounds, not a new HUD. Designed in
[docs/fork/COOP_FEATURES_SPEC.md](docs/fork/COOP_FEATURES_SPEC.md).

| Feature | What you get | How to use it | Price or limit | Server option |
|---|---|---|---|---|
| **Sleeping together** | Lying down tells everyone who is still up: "1/2 players sleeping. Waiting for *name*…". The night passes once everyone is in bed | Use a bed | Nobody sleeps until everyone is in bed (as before) | None, always on |
| **Hear your partner eat and drink** | The eating and drinking sounds play for your partner, from where you are | Eat or drink as usual | Only within about 10 m, and only when you're both in the same base or vehicle, or both outside | None, always on |
| **Your own firsts** | The PDA describes a new place when *you* get there, not when your partner does from across the map, also days later after a rejoin. A data log your partner picks up reaches your PDA quietly and waits there to be read | Go there yourself; read found logs in your PDA | Places reached before this update stay shared. A delayed line pending when you quit plays again only after you return to where it started | `PartnerDiscoveriesSilent` (true) |
| **Buddy breathing** | Give a partner who is running out of air some of yours, from your tank | Swim up to your partner while they're below 30 % air, look at them and hold the interact key | Slow and lossy: your partner gets half of what you give, so it slows drowning but doesn't refill. You need a tank and keep a reserve of about 15 s of your own air | `BuddyBreathingEfficiency` 0.5 (share of the air the partner gets) |
| **Your partner's signal** | Your partner is a faint signal with their name and depth. Their name tag and vitals show only up close | Nothing to do; **Options → Nitrox → Your partner** sets the signal to Off, Faint or Always, and the name tag's range | Faint: no signal within 12 m or while you can see them; it fades in slowly with distance. The name tag shows within 12 m. A distress call (below) shows even with Off | None (your own setting) |
| **Hear your partner get hurt and breathe** | Their pain sounds and their breathing from a tank play from their body | Nothing to do | Pain within about 30 m, breathing within about 20 m. No alert, no marker | None, always on |
| **Their flare burns for you too** | A flare your partner throws is lit in your game as well, also when it lands far from you | Throw a flare as usual | You see it when you get close enough for the game to load it, as with your own | None, always on |
| **The story waits for both of you: the Sunbeam radio** | The radio message that starts the Sunbeam countdown only plays while both of you are online, so the Sunbeam never arrives for one player alone. Until then the radio keeps blinking and says "Waiting for *name* to come online" | Play the radio message as usual | Only that one message waits; everything else, day and night included, goes on while you play alone | `StoryGatesEnabled` (true), `StoryGatePlayers` ("" = every saved player; better list your two names, e.g. `"Alice,Bob"`). Admin escape hatch if a partner stops playing for good: `storygate release sunbeam_radio` (and `storygate status`) |
| **The story waits for both of you: the Aurora's drive core** | Weld the radiation leaks as you like; the last one only seals once both of you are in the drive core room, and then it seals for both at once | Weld the last leak while your partner comes in. Alone you see "Wait for *name* to finish the repair together"; your partner sees that you're waiting in the drive core room | The radiation keeps spreading until you finish it together | The same two options; `storygate release drive_core` lets the next weld through alone |
| **Your partner looks right after you join** | When you join or rejoin, your partner stands or swims as they really do and holds what they hold | Nothing to do | — | None, always on |
| **See what your partner is doing** | You see your partner scan, build, use the PDA, swim at the surface and jump, and hold their tool the way they do. Also right after you join | Nothing to do | Only their body moves: the scanner and builder beams don't show for you yet | None, always on |
| **Cyclops crew** | One of you steers, the other runs sonar, shield, silent running, decoys and the cameras. The pilot's game follows what the co-pilot switches on, and the sub pays for it once | Stand behind the pilot and use the helm screen | Needs both of you in the Cyclops, and the co-pilot does nothing else meanwhile. Engine, speed and horn stay with the pilot | None, always on |
| **Context pings** | Point and ping, and the marker says why: at a creature that hunts you it's a red "Danger!" (15 s); at something to pick up, break or drill it shows that item's icon (10 s); anywhere else it's a "Come here" beacon that stays until your partner gets there (at most 30 s). Pinging your partner says "Look! *name*". Pings aim past your own Seamoth or Prawn suit and, at the helm, past the Cyclops, and reach things right in front of you. Each game writes the text in its own language | **Middle mouse button**: ping what you look at. **H**: always "Come here". Both can be changed in the key bindings | Pings fade with the signal: when either of you is in a cave or the deep, a ping only shows within about 50 m of whoever sent it. Up to 3 pings each, 250 m range | `PartnerSignalInterference` (true) |
| **Craft from your lockers** | The fabricator and modification station of a base, the moonpool's upgrade console and the Cyclops fabricator also use what's in the lockers of that base or Cyclops, after your own inventory. Recipes light up and the tooltip counts include the lockers. If your partner takes the last item first, the craft is cancelled and nothing is lost | Craft as usual | Lockers only: not planters, aquariums, trash cans, filtration machines, scanner rooms or a docked vehicle's storage. The Habitat Builder still uses only your inventory. Not yet tested in game | `CraftFromLockers` (true) |
| **Hand items over** | Give your partner something from your inventory: it moves straight into theirs, battery and all | Swim up to your partner, look at them and press the interact key, then move items to their side as with a locker | Only from your inventory, nothing you wear, and only if they have room. You can't take anything from them; it's no more than dropping an item for them to pick up | None, always on |
| **The deep cuts the line** | In caves, the Lost River, the lava zones, Precursor facilities and below 300 m, your partner's signal drops to a last contact: it stays where you lost them, flickers, and says how long ago ("last contact 2 min"). It comes back as soon as you're both out of there | Build relays: a beacon you place within about 150 m of you, or a powered scanner room within its scan range (300 m, more with range upgrades), carries your signal again | It works both ways: when either of you is down there, both lose the other. Up close nothing changes: you still see their name tag, their light and hear them. The partner signal setting "Always" ignores it | `PartnerSignalInterference` (true), `SignalInterferenceDepth` (300 m) |
| **Rescue window** | At 0 health you go down instead of dying: the screen dims and pulses, a countdown runs, you can look around but not move or use anything. Your partner can get you back up before it runs out; if they don't, you die as usual and your things drop where you fell | Rescuer: swim up to your downed partner, look at them and hold the interact key for 6 s (about 20 health), or 2 s if you carry a first aid kit (50 health, the kit is used up) | 60 s on the first down, 30 s on the second, none on the third, until you sleep in a bed or go 15 min without going down. Your air keeps draining while you're down. Lava, heat, drowning and the Cyclops exploding still kill. No window when nobody else is up to help. Quit while down and you come back down, with the time you had left. A downed pilot is pushed out of the Seamoth or Prawn suit | `RescueWindowSeconds` (60, 0 = off), `RescueEscalation` (true). Admin: `rescue status`, `rescue rest <player>` |
| **Distress call** | When your partner goes down, their signal turns red and shows the time left ("Alice · down · 42 s"), at any distance and even in caves and the deep. A short ping sounds with each update, and up close you hear them groan | Nothing to do: follow it | It only shows where they were at the last pulse, every 5 s, not a live track. It ends when they're up again or dead | The rescue window's options |
| **A cuddlefish for both of you** | The cuddlefish follows whoever played with it last, and both of you see it swim with that player. Tell it to stay and it stays for both of you, also after a restart | Play with it or give it a command (follow/stay) as usual; to have it follow you, play with it | One cuddlefish follows one player at a time. While your partner is playing with it, you can't. For now you only see its tricks when it plays with you | None, always on |
| **Context pings** | Point and ping, and the marker says why: at a creature that hunts you it's a red "Danger!" (15 s); at something to pick up, break or drill it shows that item's icon (10 s); anywhere else it's a "Come here" beacon that stays until your partner gets there (at most 30 s). Each game writes the text in its own language | **Middle mouse button**: ping what you look at. **H**: always "Come here". Both can be changed in the key bindings | Pings fade with the signal: when either of you is in a cave or the deep, a ping only shows within about 50 m of whoever sent it (this starts working with the planned "the deep cuts the line"). Up to 3 pings each, 250 m range | `PartnerSignalInterference` (true) |
| **The story waits for both of you: the Sea Emperor and her eggs** | The Sea Emperor only comes to the platform in her aquarium once both of you stand on it, so you meet her and hear her together, not one of you from across the map. The eggs hatch once both of you have used the incubator, and both of you hear the hatching music | Step onto the platform; your partner sees "*name* is waiting for you on the aquarium platform". At the incubator, whoever carries the enzymes and the partner both use it, in any order | The first one on the platform waits there. The cure itself isn't shared yet: your partner is still cured at the same moment as you, without their own cure scene | The same two options; `storygate release sea_emperor` or `storygate release hatching` lets it happen for one player |
| **The story waits for both of you: disabling the gun** | Once you're cured, each of you uses the alien terminal, and both of you see your own scene at the same moment. Before, the first player disabled the gun alone and the terminal never opened again for the other | Both stand at the terminal and use it. Alone you see "Waiting for *name* to use the terminal too"; your partner sees that you're at the terminal | Walking away from the terminal takes your turn back. Not cured yet? The terminal still turns you away as usual | `StoryGatesEnabled`, `StoryGatePlayers`; `storygate release disable_gun` lets the next use through alone |
| **The story waits for both of you: the rocket launch** | Nobody watches the credits alone: the rocket takes off for both of you at the same moment, and only with both of you aboard | Both get into the cockpit and press launch. Alone you see "Waiting for *name* to board the cockpit and press launch" | Pressing launch only counts while you're in the cockpit; climb down and you have to press it again | The same options; `storygate release rocket_launch` |

### For players
- **One-key bug reports:** press **F4**, or type `/report <what happened>` in chat. Logs, the server log and a save
  snapshot are zipped to `%APPDATA%\Nitrox\reports`. Attach the zips from both PCs to an issue here.
- **The launcher knows it's Tandem.** It shows "Nitrox Tandem" in the window title and in game, sends crash reports to
  this repo, and never offers to "update" to official Nitrox. That would remove these fixes and can't load your
  Tandem world.
- **Small comforts:**
  - The game's own "Saving game" notice shows on both screens while the server saves the world (the autosave or
    `/save`).
  - Crafting stations in a base or Cyclops also use its lockers (see "Craft from your lockers" above).
  - Pings aim past your own Seamoth, Prawn suit or, at the helm, the Cyclops, and reach things right in front of you
    (see "Context pings" above).

### Planned co-op features
Not built yet; designed in [docs/fork/COOP_FEATURES_SPEC.md](docs/fork/COOP_FEATURES_SPEC.md). The idea: Subnautica
stays lonely, scary and hard, just shared.
- **It stays scary.** In caves and the deep the partner's signal is lost, and you find each other by sound and light.
- **Every help has a price.** Like the rescue window and buddy breathing above. Creatures, resources and fragments keep
  the game's own numbers.
- **The big story beats wait for both of you.**
- **Each rule gets a switch** in the server's `server.cfg`.

What's planned:
- **Story moments together:** the cure (each with your own cure scene) waits until both players are there, like
  the drive core, the Sea Emperor, the gun and the rocket launch already do. Playing alone in between is fine.
- **Teamwork survival:** hitching a ride on the Seamoth.
- **Cozy base life:** seeing each other sit, sleep and use things, and seeing the cuddlefish's tricks when it plays with
  your partner.

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
