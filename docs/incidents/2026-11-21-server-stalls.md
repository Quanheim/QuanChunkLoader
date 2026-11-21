# Incident, 21 Nov 2026: server stalls, boats jolting, a pile of protected drops

Status: **cause found and cleared; measuring the result.** Written 21 Nov 2026.

## Summary

The dedicated server was losing a growing share of real time to long frames: 3.7% of the time with players
online on 18 Nov, 17.9% on 21 Nov, against 0.0–0.3% on most days under the old chunk loader in October. Players
saw it as boats jolting and taking damage at sea, fish hanging in the air, and bases taking up to 30 s to load.

The leading cause is our own despawn protection. Inside loaded zones it keeps items within 40 m of an active
Phantom Spawner from despawning, with no limit. The phantom-run surtling farms had built up a carpet of coal and
cores thousands of items deep, and the server ran physics on all of it whenever nobody was near. The pile was
removed and the farms' observers switched off on 21 Nov; whether the stalls fall back to October's level is still
to be measured (see [What's next](#whats-next)).

## What players saw

- **18:21, Nige (TechKai):** "continuously taking damage on my longboat whether I am in the ocean or not": about
  10 damage every few seconds, even on flat water, sounding like hitting rocks. Outside any loaded zone.
- **Quandru flew to him and watched:** the boat jumped above the water, then was sucked below it, even when
  stationary; fish hung in mid-air beside it. "Like the boat location is desyncing."
- **Quandru, anecdotally, the days before:** bases that used to appear instantly on arrival, even with tens of
  thousands of objects, were taking up to about 30 s to load.

## How the boats and the stalls connect (vanilla code, 1.0.21)

- The server sends its network time to every client every 2 s (`ZNet.SendNetTime`), and each client overwrites its
  own clock with it (`ZNet.RPC_NetTime`: `m_netTime = time`).
- The wave height that boats and fish physically float on is computed from that clock
  (`WaterVolume.GetWaterSurface` → `CalcWave(…, s_wrappedDayTimeSeconds)`). The water players *see* uses a separate
  clock eased towards it (`s_waterTime`, `Lerp(…, 0.05)`).
- The server only advances its clock by the frame time Unity gives it, which is capped per frame, so during a long
  frame its clock falls behind real time. Each client's clock runs on in the meantime and is then pulled back at the
  next sync.
- So whenever the server stalls, every client's physics waves jump while the visible water doesn't: boats pop up
  and drop, hulls jolt against the seabed (the likely source of the damage; not confirmed in code), and fish float
  above or below the visible surface. Being a world clock, it happens everywhere, loaded zones or not.

## Measuring the stalls

**The measure.** With players online, vanilla advances world time by Unity's per-frame time, which is capped, so
world time only falls behind real time when server frames run long. The chunk loader's 5-minute heartbeat logs world
time and the player count, so `real seconds − world seconds` per heartbeat, while someone is online, is time lost to
stalls. (With nobody online our empty-server clock adds real elapsed time instead, so those stretches always read
1.00 and are left out.) The old diagnostic plugin logged the same thing, so October can be compared directly.

**A second measure that needs nobody online.** Vanilla prints `Connections N ZDOS:…` to the server console
(`Game.ServerLog`), apparently every 600 s of game time. On 21 Nov, with nobody online, the lines came about
**664 s** apart, so the server was stalling roughly 10% of the time even when empty. With players online the gaps
reached 700–850 s. (Where `ServerLog` is scheduled wasn't found; the reading assumes it runs on Unity's game clock.)

| Period | Loader | Time lost to stalls, players online |
|---|---|---|
| 13–31 Oct (14 days) | Old (NerdLounge), diagnostic logs | 0.0–0.3% most days; worst 1.9% (28 Oct) |
| 18 Nov | Ours, 1.2.0 | 3.7% |
| 19 Nov | Ours | 8.7% |
| 20 Nov | Ours | 11.4% |
| 21 Nov | Ours | 17.9%; worst 5-minute window lost 124 s of 311 |

**The server's CPU** (a VM: 8 virtual Xeon Silver 4210 cores at 2.2 GHz, 16 GB) never looked busy overall: 28–43%
total. Per logical processor, one core sat at 50–80% while the others idled in spikes: the main thread, busy but
not maxed. So the server isn't simply out of CPU; the lost time comes from bursts of long frames that Task Manager's
1-second samples average away.

**Ruled out or unlikely**
- **Debug logging volume:** per-window log line counts don't track the stalls (busier windows, if anything, lost
  less). Log detail was still on Debug from the deer watch; it should go back to Info regardless.
- **World size:** the save holds 1,638,349 objects (`ZDOMan.LoadChunks`), but that barely changed during the
  period.
- **Loaded objects:** 23,429 (18 Nov) to 24,812 (21 Nov), +6%: nowhere near a fivefold rise in stalls.

## The cause: protected drops piling up at the phantom farms

**Items on the ground in loaded zones** (heartbeat): 35–95 under the old loader in October; on our loader 2,897
on 18 Nov rising to 3,563 on 21 Nov. They rose only while players were online (the world clock and the farms only run
then), by 40–50 a session.

**Despawn protection** ([WorldPatches.cs](../../Patches/WorldPatches.cs), `DespawnProtection`): on the server, an
item in a loaded zone is kept if it's within 8 m of a production station or within 40 m of an **active** Phantom
Spawner. A phantom is active only while it stands in a zone a switched-on observer keeps loaded
([Phantoms.cs](../../Core/Phantoms.cs), `Refresh`, re-run whenever the loaded zones change). The phantom-run farms
made over 1,000 surtlings a day; they die in the farm water and drop coal and cores, and nothing ever collected or
expired those drops.

**What Quandru found at the farm (21 Nov, evening):** the ground around the pool carpeted in drops, with the phantoms
buried underneath.

**Why it costs so much:** each drop is a physics body. Thousands resting on one another keep touching, so they
rarely sleep, and the work grows with the contacts, not just with the count: a deeper pile means more contacts per
item. That fits stalls rising far faster than the item count (+23% items, about five times the stalls). With nobody
near, the server owns the drops and does that physics on its main thread. The 3,563 counts item *objects*;
nearby identical drops merge into stacks, so the coal underneath was probably far more.

**But the busy core didn't drop afterwards.** On returning to the farms after the clear-up, with their observers
already off, the server's busiest core was at about 75–90% (screenshot, 19:07). At 19:13, with Quandru away at
Tim's Halls, it was unchanged. So the main thread's steady load isn't those zones, and the pile may only have been
part of the stalls. Task Manager shows CPU use, not long frames, so the stall measure from the logs is still the
real test (see [What's next](#whats-next)).

**"Simulate tames" off, shortly before 19:17 (Quandru, in-game as admin):** the busy core began falling at once, from about
75–90% to about 65–75% within two minutes and still trending down (screenshot, 19:17). So server-run tames are a
real share of the main thread's steady load, but not all of it. It also confirms that an admin's in-game change to
this setting reaches the server live (Jotunn config sync), no restart needed. While it's off, tames in loaded zones
pause with nobody near, so there's no offline breeding.

## What was done (21 Nov, Quandru)

1. Ran `removedrops` (admin, `devcommands`) standing in each farm. It deletes every item drop the player's own
   game owns, which is everything on the ground nearby that no other player is nearer to; not range-limited beyond
   that. It deleted the piles in one go.
2. Moved the phantoms to more sensible spots (the hammer's remove targeting ignores the item layer,
   `Player.m_removeRayMask`, so buried pieces can be reached through a pile anyway).
3. Switched off the farms' chunk observers, so the phantoms there are dormant and the server no longer runs those
   spawners. The geysers still spawn surtlings as vanilla does while a player is near. All those observers were
   still keeping running were two chests per farm.

Note for next time: switching the observers off **without** clearing the drops would only have frozen the pile, not
cleared it. Despawning needs a machine to have the item loaded and running its timer, and vanilla doesn't despawn
items within 25 m of a player. Removing the phantoms while the observer stays on lets the server expire everything
older than an hour of world time; `removedrops` is immediate.

## What's not proven

- **That the pile was the whole cause.** Two things changed at once: the pile went, and up to 18 zones (two 3×3
  clusters) stopped being loaded. If the stalls fall to October's level, the farms were the problem; switching one
  cluster back on with no phantom would separate "the pile" from "running those zones" if it matters.
- **Whether the stalls were already high on 7 Nov** or crept up since. Logs here start 18 Nov.
- **Other changes since October:** BetterTames is back on the server, and the modpack has moved on. "Simulate tames"
  is on, so the server also runs 127 tames' AI that players' games carried under the old loader.

## What's next

1. **Measure.** After a session or two, take the server's `BepInEx/LogOutput.log` and the chunk loader's daily
   logs (`BepInEx/QuanChunkLoaderLogs`) and recompute:
   - time lost to stalls with players online (17.9% on 21 Nov; target October's ~0.1%);
   - the empty-server gap between `Connections … ZDOS` lines (~664 s on 21 Nov; target 600 s);
   - items on the ground in loaded zones (3,563 on 21 Nov; expect double figures).
2. **Quick live checks, watching the server's busiest core:** "Simulate tames" off (done about 19:15: the core dropped,
   see above), then all remaining observers off for 5 minutes (reversible). If the core drops further, the rest of
   the chunk loader's load is significant; if not, the remaining load is outside it.
3. **If the stalls stay high:** turn "Simulate tames in loaded zones" off for an hour with nobody online and read the
   `Connections` gap, then switch every observer off for an hour (the vanilla baseline). If the gap returns to
   600 s, it's the chunk loader; switch bases back on one at a time to find the heavy one. Don't test by turning
   despawn protection off: hour-old items would be destroyed for good.
4. **Log detail back to Info** on the server.

## Design follow-up (chunk loader)

Despawn protection near phantoms has no limit, so an unattended farm grows its pile until the server pays for all
of it. Before any phantom goes back into a loaded zone, the design needs one of:
- a cap on protected items per zone or per phantom, beyond which the oldest expire as vanilla;
- merging protected drops into full stacks;
- protection that switches off once a pile passes a threshold, with a log warning.

The 5-minute summary should also warn when protected items near one phantom pass a threshold, so a growing pile
shows up in the log before players feel it. To add to `design.md` (Despawn protection) and the test table when
agreed.

## Separate: Quandru's PC crash the same evening

Quandru's PC hard-reset at 18:44 on arriving at the farms (the game was building terrain at (200–320, 3600–3700)).
The game's crash log ends in `d3d11: failed to create buffer [0x887A0005]` (`DXGI_ERROR_DEVICE_REMOVED`), and Windows
recorded an unclean reboot with bug-check code 0 and no dump, the same as on 15 Nov (twice, each after
`nvlddmkm stopped responding`). That's the PC's graphics card or power supply (an RTX 3080, due for replacement);
the farms' load was only what set it off.
