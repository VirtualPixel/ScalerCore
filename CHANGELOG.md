# Changelog

## 0.4.4

### Bug fixes
- Fixed non-host clients running Dispatch methods (DispatchShrink, DispatchExpand, DispatchExpandNow) — these are now gated behind a host/singleplayer check

## 0.4.3

### New
- `MapCollapse` is now public — other mods call `MapCollapse.OnMapHit()` to trigger the collapse event
- `ScaleController.ChallengeMode` public property — implementations set this to enable challenge mode
- Runtime `SemiIconMaker` generation for pocketed items — no more embedded PNGs, works for any item

### Bug fixes
- Fixed map collapse audio ignoring master volume (now routes through the game's SFX mixer group)
- Fixed map collapse alarm doubling when the truck had no unique sounds
- Fixed camera glitch effect not covering the full screen while shrunken
- Fixed shrunken players getting crushed too early during map collapse (raycast distances now scale with player size)
- Fixed pocketed item icons disappearing after level transitions
- Map collapse crush sequence reworked — FOV slam, heavy shake, vignette, and a brief hold before death

### Improvements
- ScalerCore is now a pure library with no user-facing config entries
- `ShrinkChallengeMode` and `MapCollapse` config removed — implementations own their settings
- `MapCollapseHitPatch` removed — implementations provide their own hit detection
- Map collapse enemy speed toned down (1.3x base, up to 1.8x — was 2.5x to 6.5x)
- Map collapse no longer unshrinks everything when it starts
- Map collapse FOV narrows during collapse for a claustrophobic feel
- Embedded cart/cannon/laser icon PNGs replaced with runtime SemiIconMaker

## 0.4.2

### Improvements
- Added MapCollapse config option (Auto/On/Off). You'll know it when you see it. Turn it on and shoot the map, go ahead, I dare you.
- No, Loom's arms still aren't fixed. I was busy with the super ultra important above feature for next year's april fools.
- Removed Herobrine

## 0.4.1

### New
- Any non-pocketable item becomes pocketable while shrunken — carts, cart cannons, cart lasers, trackers. Press an inventory key to stash, shoot again to restore.
- Shrunken players can't pocket shrunken items. If you get shrunk while carrying one, it drops automatically.

### Bug fixes
- Fixed shrunken players being able to wall-jump infinitely by touching walls
- Fixed Shrink Challenge mode not working on clients (inverted re-shrink was blocked by debug key gate)
- Fixed Shrink Challenge mode not working in singleplayer (was stuck waiting for voice chat)
- Fixed Shrink Challenge mode firing in the lobby instead of waiting for the level to load
- Fixed voice pitch not cleaning up when returning to lobby
- Fixed camera occasionally clipping through walls at shrunken size
- Pupil override priority and spring speed now match vanilla ranges
- Items now stay shrunken permanently until shot again (was 5 minutes)
- Enemy speed while shrunken is now 75% (was 65%)

### Improvements
- Shrink Challenge mode config changes apply instantly in the lobby
- Shrunken items show as smaller dots on the map
- Smoother pupil transition when expressions end
- Embedded inventory icons for cart, cart cannon, and cart laser
- Logging cleaned up — only warnings and errors in the console
- InvertedMode synced to clients via RPC for proper multiplayer challenge mode

## 0.4.0

### New
- Added `SuppressValueDropExpand` option to ScaleOptions — valuables won't expand on damage while scaled. For cart mods where items bump into each other constantly.
- Added `PreserveMass` option to ScaleOptions — rigidbody mass stays at its original value while scaled. For cart mods where items should weigh the same regardless of visual size.
- Added `ScaleManager.ApplyIfNotScaled()` — scales only if not already scaled, no-op otherwise. Safe to call every frame from continuous triggers.
- Added `ScaleManager.GetController()` — returns the ScaleController for a game object, resolving through PlayerShrinkLink.

### Bug fixes
- Fixed shrunken objects appearing full-size on non-host clients. The RPC was only sending the target vector without ScaleOptions fields, so clients had all-zero options and the animation never ran. Also broke late-join sync.
- Fixed shrunken player mesh freezing in place on the host. AnimSpeedMultiplier wasn't synced to clients, so the client sent a zero-speed animation override back via RPC, killing the host's visual position interpolation.
- Fixed players reverting to full size when another player jumped or tumbled into them. Bonk expand now only triggers when health actually decreases, not on zero-damage contact.

## 0.3.0

### New
- Shrink Challenge Mode: players start shrunken, guns temporarily grow you, damage shrinks you back
- Shooting an already-shrunken target with the same factor toggles it back
- Shooting an already-shrunken target with a different factor rescales it smoothly (no flash)

### API changes
- ScaleManager.Apply() now takes a ScaleOptions struct — per-call config replaces global ShrinkConfig
- ScaleManager.Apply() without options uses ScaleOptions.Default
- Added ScaleTargets flags enum for filtering what object types can be scaled
- Added ScaleController.TargetType for mods to check what kind of object a controller manages
- Removed ShrinkConfig and ScaleFactor (replaced by ScaleOptions)
- Zero Factor/Speed in ScaleOptions falls back to defaults

### Bug fixes
- Fixed players in tumble/object mode (Q) not being shrinkable by guns
- ScaleManager API now resolves PlayerShrinkLink when target GO doesn't have ScaleController directly
- Fixed Tricycle (Bella) trike mesh not scaling — rider shrank but the bike stayed full size
- Single doors now cleanly break off their hinges when shrunken instead of floating in place
- Birthday Boy's balloons now shrink along with him
- Enemies killed or despawned while shrunken no longer respawn at shrunken size
- HeartHugger mesh and collision now align properly when shrunken (including when tipped)
- HeartHugger gas pull distance scales with enemy size
- Loom attack distance scales with enemy size
- Fixed bonk expand not restoring visual scale on enemies with handler-owned scaling
- Replaced most reflection with direct publicizer access for better performance
- Fixed Loom (Shadow) NRE spam in EnemyShadow.HandLogic after unshrinking

## 0.2.0

### Bug fixes
- Pupils no longer stay huge after unshrinking
- Animation speed resets properly on unshrink
- Grab range actually scales down while shrunken now
- Voice pitch no longer gets nuked by spewer/hourglass events
- Menu preview shows big pupils while shrunken
- Host now enforces grab stats for all shrunken players, not just local
- Remote players see big pupils in the shop
- Menu preview only shrinks for the shrunken player, not everyone
- Non-host sees their own big pupils in menu preview
- Non-host grab strength/range/throw restores properly after unshrinking
- Big pupils yield to expressions while shrunken (no more bleeding through eyelids)
- Cart pull distance no longer leaks the host's shrink state to other players
- Shrunken enemies deal scaled damage across the board (mace swings, tumble impacts, instakills)
- Enemies like Trudge whose mace has `playerKill` no longer instakill when shrunken
- Damage scaling works even when the HurtCollider doesn't have `enemyHost` set
- NavMesh agent radius scales with enemy size
- Fixed Chef, Mentalist, Reaper, Trudge, and Elsa sinking into the ground when shrunken
- Fixed Loom (Shadow) arms detaching from body when shrunken
- Fixed AnimTarget discovery walking up to Enable container on enemies with renderers on the Rigidbody (caused double-scaling on Hearthugger and Loom)
- Known: Hearthugger still has visual/grab misalignment when shrunken (cosmetic, gameplay unaffected)

### Balance
- Grab strength less punishing (1.5x scale factor, capped at 100% when shrunk)
- Added MinimumStrength and MaximumStrength config options
- Grab range and throw scale directly with size (no mercy bonus)
- Enemy damage scales by shrink factor directly (was a flat 0.1x)
- Enemy bonk immunity down from 5s to 3s
- Items stay shrunk indefinitely (was 300s)

### Improvements
- Menu avatar animates smoothly when shrinking/unshrinking instead of snapping
- Negative shrink durations from bad configs get clamped to 0
- Version auto-stamped from csproj via BuildInfo

### Internal
- Assembly publicizer replaces all reflection in PlayerHandler
- ItemHandler uses standard GetField instead of AccessTools
- All enemy-to-player damage scaling lives in one patch now (KnockbackPatch)
- Deduplicated grab strength formula into GetGrabFactors helper
- Noisy item field logs downgraded to LogDebug
- Dropped REPOLib dependency (wasn't actually used)
- Updated Thunderstore description

## 0.1.0

Initial early access release.
