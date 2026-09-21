# Grounding probe

A dev-only second plugin that answers one question: with ScalerCore holding a monster at some
size, are its feet on the floor. It drives the game itself, so the answer comes from a running
level with a real navmesh rather than from a unit test.

Never shipped. It lives in the Developer Gale profile and stays asleep unless a flag file tells
it to run.

## What it measures

For every level, every enemy kind and every scale factor:

1. **Summon.** An `EnemySetup` off `EnemyDirector`'s difficulty lists, handed to
   `LevelGenerator.EnemySpawn`, at the level point furthest from the tester. That is the game's
   own spawn path, so the enemy is registered, timed and pathed exactly as a rolled one.
2. **Scale.** `ScaleManager.ApplyIfNotScaled` with `AllowedTargets = ScaleTargets.Enemies` at
   0.5, 0.75, 1.5, 2 and 3, two seconds to settle, then three seconds held. Shrink factors go
   through `ScaleOptions.Default` and grow factors through `ScaleOptions.Growth`, which is what
   a real caller passes. Growth matters beyond its name: it caps the physical body at 1.4 while
   the visual climbs to the full factor, so above x1.4 the grounding is holding up a mesh
   larger than its own colliders and nav agent. The report's Preset column says which base a
   row used and whether the body was capped for it.
3. **Sample, twice a second:**
   - **Feet.** The enemy's sole marker when it carries one (the Chef, the Mentalist, Trudge and
     Elsa each steer off a `feetTransform`), otherwise the lowest point of its character
     renderers. That is the same choice `VisualGrounding` makes, so the probe measures the point
     ScalerCore grounds to rather than some other transform nearby.
   - **Floor.** A downward cast on the Default layer from `Enemy.CenterTransform`, skipping any
     collider belonging to the enemy itself.
   - **Agent.** The `NavMeshAgent`'s own height over the same floor, which is what the enemy's
     navigation believes.
   - **Mesh Y.** `AnimTarget.localPosition.y`, the height `GroundingTrack` writes every frame.
     The min/max over a hold shows whether the grounding is holding a value or chasing one.
4. **Restore**, two seconds, next factor.

A sample is thrown away when the enemy is stunned, spawning, despawning, mid-jump
(`EnemyJump.jumping` and its delays) or off the ground (`EnemyGrounded.grounded`), because none
of those are standing on a floor.

## Sink and float

- **SINK**: feet more than 5 cm below the floor.
- **FLOAT**: feet more than 10 cm above it.

Airborne kinds are measured and reported but never decide the verdict: their feet are meant to
be off the floor. An enemy counts as airborne when its brain is `EnemyFloater`, `EnemyDuck`,
`EnemyBang` or `EnemyCeilingEye`, or when the prefab's own `EnemyRigidbody.gravity` is off.
Their rows read `float 0.42 m, airborne (EnemyFloater)` in lower case, and they are excluded
from the PASS/FAIL count.

## Running it

**Never while R.E.P.O. is up.** The runner refuses, and so should you.

```bash
/home/justin/Code/REPO-Mods/ScalerCore/tools/probe-grounding.sh
```

That builds ScalerCore's staged Release DLL and the probe into the Developer profile, writes the
flag, launches the game through Steam with Gale's doorstop arguments, waits for the report,
prints it and cleans up. Options:

```
--levels  Museum,Manor      levels to sweep, in order
--enemies "Hunter,Runner"   enemy kinds, matched against the spawn prefab name
--scales  0.5,2,3           factors to test
--budget  600               seconds the probe may spend in game before it quits itself
--profile Developer         Gale profile to run in
--build-core                rebuild ScalerCore from source instead of copying BuildZip
--wait    720               seconds to wait for the report
```

The runner stops on any of four things, never on success alone: the report appearing, the game
exiting without one, the BepInEx log going quiet for five minutes, or twelve minutes passing.
It also refuses when any Gale profile was in a game less than two minutes ago, so a probe never
lands on top of a restart. Two probe runs back to back wait out that same two minutes.

## Reading the report

`tools/Probe/report.md`, copied from the profile's `BepInEx/scalercore-probe-report.md`. One row
per level, enemy and factor, with the feet, agent and mesh ranges over the hold, and a last line:

- `PASS: N enemy/scale combinations, every one of them inside 0.05 m below and 0.10 m above the floor.`
- `FAIL: N of M enemy/scale combinations left the floor.` The rows name which.
- `NO DATA` on a row means every sample was thrown away. The reason is in the cell; the full
  per-sample trace is in the profile's `BepInEx/LogOutput.log` at debug level.

A partial report is written after each level, so a run that dies early still leaves what it got.

## Budget

Five enemies at five factors is roughly two minutes of measuring per level on top of generation.
The default 600 second budget covers about three of the four levels; the probe quits when the
budget runs out and the report says which combinations it never reached. For a full four-level
sweep raise `--budget` and `--wait` together, or run one level at a time.

## The flag file

`BepInEx/config/scalercore-probe.flag` in the profile. The probe deletes it on the way in, so a
profile carrying the DLL without the flag loads an idle plugin. The runner writes it, but it can
be written by hand for a launch through Gale's own UI:

```
Museum,Manor
enemies=Slow Walker,Runner,Shadow,Hunter,Floater
scales=0.5,0.75,1.5,2,3
budget=600
```

The first bare line is the level list; the rest is key=value and anything left out keeps its
default.
