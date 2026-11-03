# Playing Nitrox Tandem: the full reference

The [README](../../README.md) shows the highlights. This page lists every co-op feature in detail: what you get, how to use it,
its price or limit, and the server option that switches it. Design and test results are in
[COOP_FEATURES_SPEC.md](COOP_FEATURES_SPEC.md), [MULTINAUGHTICA_SPEC.md](MULTINAUGHTICA_SPEC.md) and
[FIX_SPEC.md](FIX_SPEC.md).

Server options live in the world's `server.cfg` (`%APPDATA%\Nitrox\saves\<world>\server.cfg`), or in the launcher's
server settings. The value in brackets is the default.

## The story waits for both of you

All story gates share two options: `StoryGatesEnabled` (true) and `StoryGatePlayers` ("" = every saved player; better
list your two names, e.g. `"Alice,Bob"`). If a partner stops playing for good, an admin can let one gate through with
`storygate release <gate>`; `storygate status` shows what each gate waits for.

| Story beat | What happens | Release name |
|---|---|---|
| **The Sunbeam radio** | The radio message that starts the Sunbeam countdown only plays while both of you are online. Until then the radio keeps blinking and says "Waiting for *name* to come online". Only that one message waits; everything else, day and night included, goes on while you play alone | `sunbeam_radio` |
| **The Aurora's drive core** | Weld the radiation leaks as you like; the last one only seals once both of you are in the drive core room, then it seals for both at once. Alone you see "Wait for *name* to finish the repair together". The radiation keeps spreading until you finish it together | `drive_core` |
| **The Sea Emperor** | She only comes to the platform in her aquarium once both of you stand on it, so you meet her and hear her together. The first one on the platform waits there; the partner sees "*name* is waiting for you on the aquarium platform" | `sea_emperor` |
| **Hatching the eggs** | The eggs hatch once both of you have used the incubator, in any order, and both of you hear the hatching music | `hatching` |
| **The cure** | The babies drop cure balls in both games. The cure starts once both of you have taken one, and each of you gets your own cure scene at the same time. The first one waits by the ball (swim away and the wait ends). While your partner is offline the cure waits for them; releasing it cures one player, and the partner is cured when they come back | `cure` |
| **Disabling the gun** | Once you're cured, each of you uses the alien terminal and both of you see your own scene at the same moment. Walking away takes your turn back. Not cured yet? The terminal turns you away as usual | `disable_gun` |
| **The rocket launch** | The rocket takes off for both of you at the same moment, and only with both of you in the cockpit having pressed launch. Climb down and you have to press it again | `rocket_launch` |

## Helping each other, at a price

- **Rescue window.** At 0 health you go down instead of dying: the screen dims and pulses, a countdown runs, and you can
  look around but not move or use anything. Your partner gets you back up by swimming to you, looking at you and holding
  the interact key for 6 s (about 20 health), or 2 s with a first aid kit (50 health, the kit is used up).
  *Price:* 60 s on the first down, 30 s on the second, none on the third, until you sleep in a bed or go 15 min without
  going down. Your air keeps draining. Lava, heat, drowning and the Cyclops exploding still kill; explosions and fire put
  you down. No window when nobody else is up to help. Quit while down and you come back down with the time you had left.
  A downed pilot is pushed out of the Seamoth or Prawn suit.
  *Options:* `RescueWindowSeconds` (60, 0 = off), `RescueEscalation` (true). Admin: `rescue status`, `rescue rest <player>`.
- **Distress call.** When your partner goes down, their signal turns red with the time left ("Alice · down · 42 s"), at
  any distance, even in caves and the deep. A short ping sounds with each update, and up close you hear them groan. It
  shows where they were at the last pulse (every 5 s), not a live track.
- **Buddy breathing.** Swim up to a partner below 30 % air, look at them and hold the interact key to give them air from
  your tank. *Price:* they get half of what you give, so it slows drowning but doesn't refill; you need a tank and keep a
  reserve of about 15 s. *Option:* `BuddyBreathingEfficiency` (0.5).
- **Hand items over.** Look at your partner and press the interact key, then move items to their side as with a locker;
  they go straight into their inventory, battery and all. Only from your inventory, nothing you wear, and only if they
  have room. You can't take anything from them.
- **Hitch a ride on the Seamoth.** While your partner pilots, swim up, look at the hull and press interact ("Hold on");
  press **E** to let go. You ride on the outside: your air keeps draining, creatures can still go for you and you can't
  use tools. You let go when you go down, the pilot gets out, or the Seamoth docks or is destroyed. One passenger.
  *Option:* `SeamothHitch` (true).
- **Cyclops crew.** One of you steers; the other stands behind the pilot and runs sonar, shield, silent running, decoys
  and cameras from the helm screen. The sub pays for each once. Engine, speed and horn stay with the pilot.
- **Craft from your lockers.** A base's fabricator and modification station, the moonpool's upgrade console and the
  Cyclops fabricator also use what's in that base's or Cyclops' lockers, after your own inventory. If your partner takes
  the last item first, the craft is cancelled and nothing is lost. Lockers only (not planters, aquariums, trash cans,
  filtration machines, scanner rooms or a docked vehicle). The Habitat Builder still uses only your inventory.
  *Option:* `CraftFromLockers` (true).
- **Sleeping together.** Lying down tells everyone who is still up ("1/2 players sleeping. Waiting for *name*…"). The
  night passes once everyone is in bed.

## Finding each other, and keeping it scary

- **Your partner's signal.** A faint marker with their name and depth. No signal within 12 m or while you can see them;
  it fades in slowly with distance. Their name tag and vitals show only within 12 m. **Options → Nitrox → Your partner**
  sets the signal to Off, Faint or Always, and the name tag's range.
- **The deep cuts the line.** In caves, the Lost River, the lava zones, Precursor facilities and below 300 m, the signal
  drops to a last contact that stays where you lost them, flickers, and says how long ago. It works both ways. A beacon
  within about 150 m, or a powered scanner room within its range, relays it again. Up close nothing changes, and the
  "Always" setting ignores it. *Options:* `PartnerSignalInterference` (true), `SignalInterferenceDepth` (300).
- **Context pings.** **Middle mouse button** pings what you look at: a red "Danger!" at a creature that hunts you (15 s),
  the item's icon at something to pick up, break or drill (10 s), "Look! *name*" at your partner, and otherwise a
  "Come here" beacon that stays until your partner gets there (at most 30 s). **H** always pings "Come here". Pings aim
  past your own Seamoth, Prawn suit or (at the helm) Cyclops. Up to 3 pings each, 250 m range; in caves and the deep a
  ping only shows within about 50 m of its sender. Each game writes the text in its own language.
- **Hear your partner.** Eating and drinking within about 10 m when you're both in the same base or vehicle (or both
  outside), pain sounds within about 30 m, breathing from a tank within about 20 m. No alert, no marker.
- **Their flare burns for you too**, also when it lands far from you.
- **Your own firsts.** The PDA describes a new place when *you* get there, not when your partner does, also days later
  after a rejoin. A data log your partner picks up reaches your PDA quietly and waits to be read. Places reached before
  this feature stay shared. *Option:* `PartnerDiscoveriesSilent` (true).

## Seeing each other

- Right after you join, your partner stands or swims as they really do and holds what they hold.
- You see them scan, build, use the PDA, swim at the surface, jump, and hold their tool the way they do.
- They sit on benches and chairs, lie in a bed (one on each side) and climb the escape pod's hatches. One player per
  seat, bed side and hatch at a time. Each of you gets the pod's first-time hatch scene once, also after a rejoin.
- Their tools at work: the scanner beam and sound, the builder's beam on the piece going up, welder sparks and sound,
  the laser cutter's glow, the propulsion cannon's beam on what they hold, and their propulsion and repulsion shots. The
  Seaglide's propeller doesn't spin for you yet.
- **A cuddlefish for both of you.** It follows whoever played with it last, and both of you see it swim with that
  player. Tell it to stay and it stays for both, also after a restart. While your partner plays with it, you can't, and
  you only see its tricks when it plays with you.
- The game's own "Saving game" notice shows on both screens while the server saves.

## Other server options added by Tandem

| Option | Default | Effect |
|---|---|---|
| `StoryGatesEnabled` | true | Story beats wait for every player on the roster |
| `StoryGatePlayers` | "" | Who the story gates wait for |
| `RescueWindowSeconds` | 60 | Time a downed player has; 0 turns the rescue window off |
| `RescueEscalation` | true | The window halves on the 2nd down and is gone on the 3rd until you rest |
| `BuddyBreathingEfficiency` | 0.5 | Share of the given air the partner gets |
| `PartnerSignalInterference` | true | Caves and the deep cut the partner's signal |
| `SignalInterferenceDepth` | 300 | Depth (m) where the signal starts to drop |
| `PartnerDiscoveriesSilent` | true | Places and logs reached by the partner stay quiet for you |
| `SeamothHitch` | true | A second diver can hold on to the Seamoth |
| `CraftFromLockers` | true | Base and Cyclops crafting stations use their lockers |
