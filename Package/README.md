# Quan's Chunk Loader

Keeps your bases running while you're elsewhere in the world: smelters smelt, kilns burn, fermenters ferment and
plants regrow, even when nobody is near them. Build a **Chunk Observer** in your base (or a **Chunk Observer
Cluster** for a big one), switch it on, and the server keeps that part of the world loaded.

It's a ground-up rewrite that fixes the side effects of earlier chunk loaders: auto-fuel and auto-store mods that
stop working, plants that never regrow, boats that sink, tames that ignore you, and bases run by a player on the
other side of the map.

> **Dedicated to Frogger**, who first brought chunk loading to Valheim. As a tribute, the Chunk Observer uses
> Frogger's original chunk loader model, and the Cluster is our own adaptation of it in his style. Their build
> cards say so. The model is the only thing reused: no code
> comes from Frogger's mod or any other chunk loader. See [Credits and licence](#credits-and-licence).

**Install it on the server and on every client.** Players without it can't connect. It needs
[Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/), which installs alongside it. **Remove any
older chunk loader first** (see [Moving from an older chunk loader](#moving-from-an-older-chunk-loader)).

## What it fixes

If you've run a chunk loader before, these may look familiar. Each one has the same root cause, explained in
[How chunk loading works](#how-chunk-loading-works-and-why-it-goes-wrong).

| What you see | Why it happened |
|---|---|
| Auto-fuel and auto-store stop working in a loaded base, even while you're standing in it. | The server kept ownership of everything in loaded zones, and those mods only act on objects your game owns. |
| Plants say "Ready" but never regrow. | The player who picked them walked away but kept ownership, and only the owner's game regrows a plant. |
| A boat moored at the edge of a loaded area is found sitting on the seabed. | Part of the boat was over a zone that wasn't loaded, which reads as "no water", so the boat stopped floating. |
| Picking crops or berries sometimes does nothing. | The server owned them, and the game's picking code expects the owner to be a player. |
| Surtling farms stop spawning when nobody is near. | Spawners only spawn with a player nearby. |
| Opening the large map stalls, and portal selection on the map stops responding. | The loader rebuilt every map pin every few seconds. |
| Tames in a loaded base ignore E, or all jump somewhere else at once. | Whoever switched the loader on kept running the whole base from wherever they went. Commands went to their game, and their mods acted on tames they weren't near. |

## The pieces

All three pieces are in the **QuanPieces** tab of the hammer. None needs a crafting station, and none counts as a
base (they don't stop monsters spawning nearby).

### Chunk Observer and Chunk Observer Cluster

<p><img src="https://raw.githubusercontent.com/Quanheim/QuanChunkLoader/main/docs/images/observer-card.png" alt="The Chunk Observer's build card: a carved stone column. Keeps its own zone loaded while switched on, so your base keeps working while you're elsewhere. Nearby players still own everything as usual. Dedicated to Frogger, who first brought chunk loading to Valheim. This is his original chunk loader model, used as a tribute. Costs 10 Stone." width="480"></p>

The world is divided into zones 64 m square. While an observer is switched on, the server keeps its zones loaded,
so everything there keeps working with nobody nearby. There are two, so you can match the observer to the base:

| Piece | Cost | Loads |
|---|---|---|
| **Chunk Observer**: one rune column | 10 Stone | Its own zone (64 × 64 m) |
| **Chunk Observer Cluster**: three columns on a round plinth | 30 Stone | Its own zone and the eight around it (192 × 192 m) |

- **Switch it on or off** with E. The runes glow blue and it lights up while it's on, and turn red when it's off,
  at once for everyone nearby.
- **Only the player who built it, or an admin, can switch it.** Hover over it to see what it loads and who built
  it. Servers can change this so that anyone with access through the wards around it can switch it.
- **See what it covers.** Look at an observer and tap your alternative-placement key (shown on its hover text;
  Left Shift by default), or hold it and press E. Thin white lines mark the borders of the zones it loads, with a
  white post at every zone corner, for 15 seconds.
- **No fuel.** It stays on until someone switches it off.
- **It can't be damaged, and monsters ignore it.** Remove it with the hammer as usual.
- **Observers can overlap.** Loaded zones are a set, so two observers covering the same zone cost nothing extra.
  Put a single observer at your dock if it's just outside a cluster's area.
- **Limits count zones:** by default each player's switched-on observers can load 18 zones (two clusters, or any
  mix), and the whole server 108. A single observer counts 1, a cluster 9. Admins' observers don't count. The limits
  are checked when you switch an observer on, and the message says which one you hit.
- **Which zone am I in?** An observer loads the zone it stands in, wherever in that zone you put it. Put a cluster
  near the middle of your base and its 3 × 3 block covers the zones all around.

### Phantom Spawner

| Cost | Crafting station | Range |
|---|---|---|
| 10 Stone | None | The range each nearby spawner already uses (60 m for surtling spawners) |

Creature spawners only spawn when a player is near. A Phantom Spawner makes the spawners around it act as if a
player were standing where the phantom is, so a surtling farm keeps producing while you're away.

- **It only works inside a zone a switched-on Chunk Observer keeps loaded.** Its hover text says "Active" there,
  and "Dormant: no active chunk observer" anywhere else.
- **It drives surtling spawners.** Admins can add other spawners, and nests and bone piles, in the config, but
  then those monsters run on the server while nobody is there and can attack the base.

> **Spawner names, ready to copy.** You don't need to look up spawner names. Each time the game starts, the mod
> writes **`BepInEx/config/quandru.QuanChunkLoader.spawners.txt`**, next to its config file. It lists every creature
> spawner in your game, with what each one spawns, how often and from how far away, and marks the ones you already
> use. It reads them from the game itself, so **spawners added by other mods are included too**, and the list
> keeps up as you add mods. Copy names from it into "Creature spawners", separated by commas.

- It looks like a small tar-black ward with glowing red runes, but it isn't one: it doesn't protect anything or
  control access.
- No fuel, no map pin, and it can't be damaged.
- Surtlings that spawn into water die at once, so a farm built over water keeps producing coal. You still need to
  come and collect it; despawn protection keeps the drops waiting for you.

## What else it does

### Ownership goes to whoever is nearby

In Valheim, each object in the world is run by one game at a time: its **owner**. The owner's game moves it, burns
its fuel and decides what happens to it. Normally the owner is the nearest player.

With this mod:
- **A player near an object always owns it.** Walk into a loaded base and the game hands you everything around you
  within about 2 seconds, exactly as it would without a chunk loader. Auto-fuel, auto-store, plant timers and
  creature commands all work again.
- **The server only runs what no player is near.** When you leave, the server takes over within about 2 seconds.
- **Nobody runs a base from far away.** If a player far from a loaded base ends up owning something in it (they
  teleported away, or a mod claimed it), the server takes it back within 2 seconds.
- **Switching an observer on doesn't give you anything.** It only decides which zones stay loaded.

### Boats stay afloat

A boat at the edge of a loaded area no longer sinks because part of it is over a zone that isn't loaded. Readings
from unloaded zones are ignored, so it floats as if the water carried on. A boat pulled up on a beach still sits on
the sand.

### Buildings at the edge stay up

The game only checks building support and wear where the zones all around are loaded, so a piece can always see
whatever holds it up. This mod keeps that rule for loaded areas: pieces at the outer edge of an observer's area
don't weather or re-check their support while nobody is near, so nothing collapses because its neighbour wasn't
loaded. With a 3 × 3 observer the centre zone gets normal wear.

### Production after the last player logs off

Normally the world clock stops the moment the last player logs off, so nothing makes progress overnight. With at
least one observer switched on, the clock keeps running at normal speed for up to one real hour (the server can
set anything up to four) after the last
logout (the server can change or turn this off). Loaded smelters, kilns, furnaces, spinning wheels, windmills,
fermenters, beehives, crops and regrowing plants carry on, and whoever logs in next finds the work done.

- It runs in real time and never in a lump: a 10-minute logout gains 10 minutes.
- Logging in and out again can't stretch it; the clock never runs faster than real time.
- After a server restart with nobody online, there's no hour.

### Your output doesn't despawn

Items on the ground normally vanish after an hour with no player within 25 m. Inside loaded zones, drops near a
production station's output and near an active Phantom Spawner are kept until someone collects them.

### Wild creatures and tames wait for you

- **Wild creatures** in a loaded base aren't loaded while nobody is near. No monsters wander through or attack an
  empty base. They're exactly where they were when you come back. Spawners and nests in loaded zones don't run
  either, unless a Phantom Spawner drives them; the creatures those make run as normal, so farms work.
- **Tames** aren't loaded either, by default. They pause while nobody is near, exactly as without a chunk loader,
  and wake up as soon as a player arrives. This is deliberate: tame mods expect to act on tames near a player, and a
  base's tames run by the server (or by a player far away) is how whole pens have ended up somewhere else. Young
  tames still grow up on time, because growing up catches up. Breeding doesn't. Servers that want pens to keep
  breeding with nobody there can switch it on (see [Configuration](#configuration)).

### Objects from mods the server doesn't have are kept safe

When a server loads something whose mod it doesn't have, the game deletes it permanently. A normal server rarely
loads anything, but a chunk loader makes it load whole bases. Inside loaded zones this mod skips those objects and
names them in the log instead, so a mod missing from the server can't wipe out what players built or tamed with it.

### Map and admin tools

- **Admins can see every observer on the map**, lit when it's on, labelled with its size and builder. It's off by
  default; turn on "Show observers on the map" in the config. Other players never receive anyone else's observers.
- **Admins see "Admin-placed"** when hovering over an observer an admin built.
- **Admin-only building** can be switched on, which hides all three pieces from other players' hammers. Useful for
  trying the mod out on a live server.
- **The map stays fast:** pins are only updated when an observer is built, switched or removed.

### Fixes for server-owned objects

For the moment between you arriving and ownership passing to you:
- Picking a crop or berry the server owns passes it to you and completes the pick, instead of silently failing.
- Changing a ship's sail where the server has it loaded no longer throws an error that disrupts the server's
  physics for that moment.

### Logging for admins

The server logs every observer switched on or off (where, by whom, and who built it), every change to the loaded
zones, a summary every 5 minutes of who owns what, and a warning if a large number of objects suddenly leave the
loaded zones or are taken back from players far away. At the default level that's a few lines a minute at most.
More detail and a daily log file can be switched on.

## How chunk loading works, and why it goes wrong

This is how the game works, not a criticism of anyone's mod. Chunk loading is harder than it looks.

- **"Loaded" isn't the same as "owned by a player".** The game decides two separate things: which parts of the
  world are loaded, and which player's game runs each object. A chunk loader only needs to change the first.
- **The common shortcut changes both.** Earlier loaders tell the game that every point in a loaded zone is "near"
  everyone: the server and every player, wherever they are. That keeps the zone loaded, but it also tells the game
  that whoever owns an object is still near it. So the server never hands objects to the player standing next to
  them, and a player who walks away keeps everything they touched. Their game keeps running that base from the
  other side of the map, along with all their client-side mods.
- **Some of the game's own code assumes the owner is a player.** Picking, sail changes and some cooking stations
  look up "the local player", and a dedicated server doesn't have one, so those actions fail when the server owns
  the object.
- **A boat needs water under all of it.** A boat reads the water at five points. A point over a zone that isn't
  loaded reads as "no water", and one bad reading in five is enough to switch its floating off.

This mod only changes which zones the server keeps loaded. It never changes who counts as "near", so ownership
works exactly as vanilla does.

## What no chunk loader can do

- **Nothing runs all night.** The world clock only moves while someone is online, and every smelter, kiln, crop and
  fermenter measures progress against that clock. With nobody online, time stands still everywhere. This mod adds
  up to an hour after the last logout by default (servers can allow up to four); beyond that, nothing can progress.
- **Nothing refuels or empties itself.** Auto-fuel and auto-store mods run on players' computers and need a player
  nearby. With nobody near, a smelter only gets through what's already in it. Bigger stations get far more out of
  the empty-server hour (see [Pairs well with](#pairs-well-with)).

## Configuration

The config file is `BepInEx/config/quandru.QuanChunkLoader.cfg`. Every setting is the server's: it syncs to all
players, and only admins can change it in game. The one exception is the map option, which each admin sets for
themselves.

| Section | Setting | Default | What it does |
|---|---|---|---|
| 0 - General | Only admins can build observers and phantoms | Off | Hides all three pieces from non-admins' hammers. Any a non-admin builds anyway stay off. Pieces already built are untouched. |
| | Only the builder or an admin can switch an observer | On | Off: anyone with access through the wards around it can switch it. |
| 1 - Limits | Zones per player | 18 | How many zones one player's switched-on observers may load (a single observer is 1, a cluster 9). |
| | Zones in total | 108 | Across the whole server. |
| | Admins ignore limits | On | Admin-placed observers don't count, and admins can switch any observer on. |
| | Expire after hours without a refresh | 0 (never) | An observer nobody has switched on for this many real hours switches off. It's never deleted. |
| 2 - Tamed Creatures | Simulate tames in loaded zones | Off | On: the server runs tames in loaded bases while nobody is near, so pens keep breeding. See the risks below. |
| 3 - Empty server | Empty-server window in minutes | 60 | How long the world clock keeps running after the last logout, up to 240. 0 turns it off. |
| 4 - Phantom spawner | Creature spawners | Spawner_imp_respawn | Which creature spawners a Phantom Spawner keeps running, by prefab name, comma-separated. Every name in your game, other mods' too, is in `quandru.QuanChunkLoader.spawners.txt` next to the config. |
| | Spawner areas | Off | Whether it also runs spawner areas (nests, bone piles, other mods' too), anywhere in a loaded zone. All or none. |
| 5 - Despawn protection | Despawn protection | On | Keeps drops near production stations and phantoms in loaded zones. |
| 6 - Map (personal) | Show observers on the map | Off | Admins only. Each admin's own choice. |
| 7 - Logging | Log detail | Info | Info, Debug (a line per object) or Trace (every decision, for chasing a bug). Don't leave Debug or Trace on. |
| | Write a daily log file | Off | Also writes to `BepInEx/QuanChunkLoader/logs`, one file per day. |
| | Days of daily files to keep | 14 | Older daily files are deleted. |

**Before switching on "Simulate tames in loaded zones":** an unattended pen can keep breeding past the numbers you'd
normally see, because nobody is there to notice, and any tame mod's feeding and breeding code then runs on the
server. Keep an eye on pen sizes.

## Pairs well with

- **Bigger or faster production stations.** Offline, a station only gets through what it's already holding. One
  that holds 10 ore at 30 seconds a bar runs dry after 5 minutes, so more capacity means far more from the
  empty-server hour.
- **Auto-fuel mods.** They refill stations while you're in the base, and now work in loaded bases again.
- **Auto-store mods.** They collect output into nearby chests while you're in the base, and also work again.

Our own picks are in [Tested with](#tested-with).

## Moving from an older chunk loader

This mod replaces Frogger's ChunkLoader, ChunkLoader Continued and ChunkLoaderFix, and won't run alongside any of
them: if one is installed it stays switched off and says so in the log.

1. **Before the swap**, remove your old chunk loaders with the hammer. Once the old mod is gone its pieces can't
   load, and whatever they cost is lost.
2. **Swap the mods on the server and every client at the same time.**
3. Build new Chunk Observers (or clusters) where you want them.

## Known limits

- **An hour at most by default** (up to four if the server sets it) of offline production after the last logout,
  and only while an observer is on. Dedicated
  servers only: a local game ends when its host leaves.
- **Windmills vary offline.** With nobody online, the server works out the wind at one fixed spot rather than at
  each windmill, so offline output can be higher or lower than you'd see in person.
- **Tames pause while nobody is near**, unless the server turns on "Simulate tames in loaded zones". They don't
  eat, breed or heal while paused. Young ones still grow up on time.
- **Seed Totems** only plant while a player is near, like auto-fuel.
- **Clients don't load anything extra.** The server keeps loaded bases running; your own game only loads what's
  around you, as normal.

## Tested with

The Quanheim server's mod list, all running together on a dedicated server with Valheim 1.0.21:

<details>
<summary>71 mods</summary>

| Mod | Version |
|---|---|
| Advize-PlantEverything | 1.21.4 |
| Azumatt-AzuAreaRepair | 1.1.8 |
| Azumatt-AzuAutoStore | 3.1.7 |
| Azumatt-AzuClock | 1.1.1 |
| Azumatt-AzuContainerSizes | 1.1.8 |
| Azumatt-AzuCraftyBoxes | 1.8.28 |
| Azumatt-AzuExtendedPlayerInventory | 2.6.6 |
| Azumatt-AzuMiscPatches | 1.2.10 |
| Azumatt-AzuWearNTearPatches | 1.0.10 |
| Azumatt-AzuWorkbenchTweaks | 1.0.8 |
| Azumatt-Build_Camera_Custom_Hammers_Edition | 1.3.4 |
| Azumatt-Official_BepInEx_ConfigurationManager | 19.0.0 |
| Azumatt-Recycle_N_Reclaim | 1.4.5 |
| Azumatt-WardIsLove | 4.0.9 |
| BentoG-MissingPieces | 2.3.2 |
| blacks7ar-BowPlugin | 1.8.8 |
| blacks7ar-CoreWoodPieces | 1.2.6 |
| blacks7ar-FineWoodPieces | 1.6.7 |
| blacks7ar-TarCollector | 1.1.9 |
| blacks7ar-VikingsDoSwim | 1.4.2 |
| blacks7ar-WieldEquipmentWhileSwimming | 1.1.4 |
| ComfyMods-ComfyLadders | 1.1.0 |
| ComfyMods-Gizmo | 1.16.0 |
| Digitalroot-Digitalroots_Slope_Combat_Assistance | 2.0.30 |
| Digitalroot-Heightmap_Unlimited_JvL | 2.0.2 |
| Digitalroot-Max_Dungeon_Rooms | 2.0.42 |
| Geda_Giminei-FixAchievements | 1.0.2 |
| Ivvty-FarLands | 1.0.8 |
| Ivvty-SteadyFrame | 1.2.0 |
| Ivvty-ValheimVisuals | 1.4.5 |
| Ivvty-WeatherOverhaul | 1.0.2 |
| JereKuusela-Ore_Support | 1.12.0 |
| JereKuusela-Server_devcommands | 1.116.0 |
| lunarbin-Expanded_Boat_Minimap | 1.0.0 |
| MagicMike-MyDirtyHoe | 2.3.4 |
| MathiasDecrock-PlanBuild | 0.20.0 |
| Meldurson-LetMeTameYou | 0.1.0 |
| MilkMediaProductions-ExpertExplorer | 1.8.0 |
| MSchmoecker-PressurePlate | 0.10.1 |
| Mydayyy-ServerSideMap | 1.3.14 |
| OdinPlus-OdinArchitect | 1.9.4 |
| OdinPlus-OdinBanners | 1.1.14 |
| OdinPlus-OdinsFoodBarrels | 1.4.1 |
| OdinPlus-TeleportCarts | 0.2.3 |
| plumga-Clutter | 0.1.7 |
| potto007-OttoFuel | 1.7.2 |
| Qua8ion-LetMeTameYouServerFix | 1.0.1 |
| Quandru-QuanDupeLightsources | 1.1.0 |
| Quandru-QuanPieces | 1.2.2 |
| R3ddTeamRules-Warmth | 1.0.2 |
| Ross-RossItemDrawers | 1.3.0 |
| Searica-CameraTweaks | 1.4.1 |
| shudnal-ConditionalConfigSync | 1.0.11 |
| shudnal-LongshipUpgrades | 1.0.24 |
| shudnal-MoreGatesExtended | 1.0.5 |
| Smoothbrain-ComfortTweaks | 3.3.11 |
| Smoothbrain-ConversionSizeAndSpeed | 1.0.18 |
| Smoothbrain-TargetPortal | 1.2.7 |
| Swmarly-SwmarlyValheimSeedTotem | 4.3.13 |
| TastyChickenLegs-NoSmokeStayLit | 2.4.4 |
| TastyChickenLegs-TreesReborn | 1.1.9 |
| ValheimModding-Jotunn | 2.30.2 |
| ValheimModding-JsonDotNET | 13.0.4 |
| ValheimModding-YamlDotNet | 16.3.1 |
| ValMedia-OdinOnDemand | 1.3.0 |
| ValMedia-OOD_LIB | 1.2.0 |
| Vapok-ShieldMeBruh | 2.1.5 |
| Vismeneer-MoreMissingPieces | 1.1.0 |
| ZenDragon-Zen_ModLib | 1.14.22 |
| ZenDragon-ZenItemStands | 1.1.1 |
| Zenox-ServerConnect | 1.0.14 |

</details>

## Credits and licence

**Frogger** had the original idea of a Valheim chunk loader. The Chunk Observers use Frogger's original chunk loader
model (the Cluster as our own adaptation of it) from [JFHeim/ChunkLoader](https://github.com/JFHeim/ChunkLoader), under the MIT licence (Copyright (c) 2023
Vyacheslav). The full notice is in `THIRD-PARTY-NOTICES.md` in this package. Thank you, Frogger.

Bugs and suggestions: the Issues page of this mod's GitHub repository (linked from this page).
