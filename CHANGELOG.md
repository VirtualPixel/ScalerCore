# Changelog

## 0.7.0

### New
- Dead Semibot heads can be scaled. Off by default and policy lives with the calling mod: ScalerCore exposes `ScaleManager.AllowDeadHeads`, ShrinkerGun binds the user-facing `Targets / ShrinkDeadHeads` setting. Heads scale like any other prop, minus pocketing (a pocketed head would fight the revive logic).
- The shop radio is scalable now. It was the one grabbable prop in the truck the ray refused to touch.

### New (growth support rounded out)
- `ScaleOptions.Growth` preset: twice the size, heavier, a touch faster, slower deliberate animation, low footsteps. Voice and entity sounds already deepen automatically from the factor; the preset covers the knobs that don't derive from it.
- Voice and entity sound pitch clamps to a usable band (0.35x to 2x). The linear curve ran negative past 3x growth, which inverted the audio.

### Fixed
- Every ScaleController RPC validates its sender now. Scale state (`RPC_Shrink`/`RPC_Expand`/pitch cancel) only accepts the master; client requests (manual scale, bonk expand, inverted reshrink) only accept the player who owns the view. Before this, anyone in the room could spoof a shrink at any controller.
- The mass-drift diagnostics (per-physics-call logs with a stack walk) only arm with SCALERCORE_DIAG=1 in the environment. They were logging at Info for every scaled valuable.
- Map collapse (the April 1st event) actually shows up for everyone now. The relay component only existed on the machine that fired the shot, so the start RPC arrived at a PhotonView with nothing listening and non-hosts saw nothing. It attaches on every client at PunManager.Awake.
- Map collapse runs off Photon's synchronized clock instead of each client integrating its own deltaTime from whenever the RPC landed. Blink period, alarm pitch, and the scale curve are identical on every machine at every instant; late joiners fast-forward into the running collapse instead of missing it.
- The collapse start RPC validates its sender (master only), and the request RPC checks the sender exists.
- The CartSteer transpiler announces itself loudly when a game update breaks its pattern instead of silently disabling cart pull-distance scaling. Both failure paths (missing grabber local, no Lerp callsites) log what died and ask for a report.

## 0.6.2

### New
- RepoXR (VR) compatibility. Shrinking a player in VR used to leave their headset viewpoint and hands full-size. RepoXR replaces the flat-screen camera ScalerCore steers with a head-tracked rig, so the camera and FOV nudges had nothing to act on. A shrunk VR player now drops to the right eye height, the hand rig scales with them, and room-scale walking covers proportionally less ground. RepoXR is found by reflection at runtime, so ScalerCore neither references nor depends on it and non-VR play is unchanged. New config toggle `Compatibility / RepoXR VR support` (default on) turns it off.

## 0.6.1

### New
- `ScaleManager.UpdateOptions(GameObject, ScaleOptions)` plus a `ForceUpdateOptions` variant that skips the lock check. Replaces the stored options on a live session so cart mods can retune `RestoreSpeed` / `SuppressImpactFlash` / `SuppressCameraShake` when a config slider moves mid-session, instead of reflecting into the private `_options` field. Pairs with the existing read-only `CurrentOptions` getter: read, mutate, pass back. Fields consumed once at dispatch (Factor, MassCap, BonkImmuneDuration) don't reapply retroactively, the next `Apply` picks them up.

## 0.6.0

### New
- Added `ScaleOptions.RestoreSpeed`, animation speed for the expand direction. 0 falls back to `Speed`.
- Added `ScaleOptions.SuppressImpactFlash`, skips the impact flash on shrink/expand.
- Added `ScaleOptions.SuppressCameraShake`, skips the camera shake on expand. Pair with `SuppressImpactFlash` for a silent restore.
- Added `ScaleOptions.SuppressVoicePitch`, skips the audio pitch shift and per-frame voice chat overrides.
- Added `ScaleOptions.IgnoreBonkExpand`, damage doesn't restore the controller. Gated inside `DispatchExpandNow` so every bonk path honors it (player, valuable, enemy, cosmetic).
- Added `ScaleOptions.RejectExternalApply` plus `ScaleManager.ForceApply` / `ScaleManager.ForceRestore`. Opt-in lock so other mods' `Apply`/`Restore`/`RestoreImmediate` no-op on your controller; the `Force*` variants bypass.
- Added `ScaleController.CurrentOptions`, read-only snapshot of the active session's options.

### Bug fixes
- Fixed Loom's arms when shrunken (finally). The IK solver (`BotSystemSpringPoseAnimator`) walks down a cached `LimbChain.lenBind[]` to place joints in world space, those values are full-size, the body wasn't. Idle path put joints outside the shrunken body, reach path left a gap between elbow mesh and the hand target. `LoomVisualHandler` now snapshots `lenBind` at Setup and writes `orig * ratio` every LateUpdate while shrunken, restoring the originals on expand.
- Fixed shrunken players holding guns (or any forceGrabPoint item) at waist height instead of eye height. The game bakes a fixed `-up*0.3` offset in `StartGrabbingPhysObject` that doesn't scale, the new postfix lifts the puller back proportionally to player size.
- Fixed shrunken players ending up mis-sized after a rescale. `Apply` with a different factor on an already-scaled controller re-fired `Handler.OnScale` on non-host clients, which read already-scaled singleton values as the new originals and compounded to factor². `ApplyLocalPlayerShrinkEffects` and `RestoreLocalPlayerShrinkEffects` are now guarded by a `LocalEffectsApplied` flag.
- Shrunken players who died and got revived stayed shrunken on everyone else's screen but looked normal on their own. The bonk-expand path normally cancels the shrink on damage, but the bonk-immunity timer blocks the expand for the duration of the shrink animation, so anything that kills you in that window (most often the kill plane after falling out of map while shrunken) puts you into the death sequence still shrunken, and revive doesn't resync the scale state. Host now auto-cancels the shrink on `PlayerAvatar.PlayerDeathRPC`. Inverted/challenge mode skipped so dying small in that mode stays small.

### Internal
- RPC payload extended for the new options (`PackOpts` slot 7, `PackBools` slots 2-6). Decoder length-guards every slot, old hosts can still drive new clients.
- `EnemyBonkPatch` now calls `ctrl.DispatchExpandNow()` directly instead of going through `ScaleManager.RestoreImmediate`. Internal bonk paths bypass the `RejectExternalApply` lock, only external mod calls honor it. Matches what `ValuableHandler` and `CosmeticHandler` already did.
- `ScaleManager` deduplication, the handler-type filter and lock check moved into `IsTargetAllowed` / `IsLockedFromExternal` helpers used by `Apply`/`ApplyIfNotScaled`/`Restore`/`RestoreImmediate`.
- `ScaleController` deduplication, `PlayImpactEffect()`, `PlayCameraShake()`, and `ResolveExpandSpeed()` helpers replace the repeated gate + ternary that had been inlined at every dispatch site.

## 0.5.2

- `PlayerHandler.GetBaseGrabStats` was guarding only one of three StatsManager upgrade dictionaries with `ContainsKey` before indexing all three. v0.4 (or some combination of mods + game state) leaves players with a strength entry but no range/throw entry, so the indexer threw `KeyNotFoundException`. The throw aborted `OnRestore` before `RPC_PlayerPitchCancel` could fire, killed `ApplyLocalPlayerShrinkEffects` partway through (camera/FOV/collision/grab range never scaled, so shrunken players looked tiny but played full-size), and inside `CleanupAll`'s foreach it killed the whole loop after the first bad controller, leaving every player past that one stuck in shrunken state across level transitions. Switched to `TryGetValue`, missing entries default to 0 which matches the "no upgrades purchased" baseline.
- Non-host clients didn't reset shrink state on level transitions. `RunManager.ChangeLevel` early-returns on non-host during gameplay, so the existing `LevelChangePatch` postfix never reached `CleanupAll` for them. Their `OnUpdate` kept re-asserting voice pitch every frame, overriding the cancel the host RPC'd in. Added a `RunManagerPUN.UpdateLevelRPC` postfix gated to non-host; that RPC fires on every client via `AllBuffered`, so the cleanup runs everywhere.

## 0.5.1

### New v0.4 content support
- Cosmetic boxes (`CosmeticWorldObject`) are shrinkable. They have `PhysGrabObject` but no `ValuableObject` or `ItemAttributes`, so neither the handler predicates nor the `ScaleController` attach patch matched, added a `CosmeticHandler` and extended `AttachToValuablePatch` to also pick up `CosmeticWorldObject`. Handler tracks `NotValuableObject.healthCurrent` for bonk-on-damage expand, same pattern `ValuableHandler` uses for dollar value.
- Vehicles now actually shrink visually instead of leaving the mesh full-size with a tiny collider hidden inside. `ItemVehicle.DeparentMesh` runs `meshTransform.SetParent(null, true)` whenever a player sits in the vehicle. The visible mesh becomes a scene-root object that no longer inherits the vehicle's transform scale, while ItemVehicle drives `meshTransform.position` directly each frame. Shrinking the root therefore shrunk the colliders but left a normal-size mesh visibly floating around them; worse, the next time the game ran `ReparentMesh` (`SetParent(originalParent, worldPositionStays: true)`) Unity rewrote the child's localScale to 1/factor to preserve world scale, so when expand fired the mesh ended up at 1/factor times original size. Added a `VehicleHandler` that matches `ItemVehicle` ahead of `ItemHandler` and per-frame enforces `meshTransform.localScale` to track the intended world scale regardless of current parent state. Pocketing is picked up from the same path ItemHandler used.
- Shrunken vehicles (`ItemVehicle`, `ValuableArcticSnowBike`) now drive at proportionally lower top speed instead of full speed on a tiny chassis. Scaling the transform but leaving `maxSpeedKmh` / `bikeForwardSpeed` at their full 100 / 10 values made a half-size car try to do 100 km/h with full-size forces. Felt like driving a brick on ice. Added vehicle speed-cap fields (`maxSpeedKmh`, `softMaxSpeedKmh`, `maxForwardSpeed`, `maxReverseSpeed`, `hyperMaxSpeed`, `bikeForwardSpeed`) to the existing reflection-based field-scaling pass; they're restored verbatim on expand. Vehicles stay pocketable via the same path they did under ItemHandler.

### Bug fixes
- Carts vanished on the second shrink. `PocketHelper.CreateIconMaker` was adding a `SemiIconMaker` component on an active GameObject, which fires `OnEnable` synchronously inside `AddComponent`, before we'd assigned `iconCamera` and `renderTexture`. `OnEnable`'s `if (renderTexture)` branch skipped, `renderTextureInstance` stayed null, then the game's `CreateIconFromRenderTexture` NRE'd after teleporting the item to `(-1000, -1000, -1000)` for the render. The position-restore line never ran, the item fell out of world, the kill-zone destroyed it. The IconMaker is now created inactive, configured, then activated so OnEnable fires with everything in place.
- Shrunken `ItemVehicle.Semiscooter` had no inventory icon (the small Semiscooter did). The icon-camera bounds calculation used `item.GetComponentsInChildren<Renderer>` on the vehicle root, which missed the deparented `meshTransform` and rendered an empty 5 KB PNG. Bounds now traverses `meshTransform` separately when it isn't a descendant of the root.
- Vehicles could be pocketed while the player was shrunken (carts and items already blocked this). `ShrunkEquipBlockPatch` was checking `CartHandler.State` and `ItemHandler.State` for `AddedEquippable` but not `VehicleHandler.State`, vehicles fell through and the block didn't fire. Added the missing case.
- Shrunken vehicles wouldn't steer. Throttle still applied, so they accelerated straight forward with no turn input response. `ItemVehicle.UpdateSteering` gates on `DriverFullyMounted`, which only becomes true once the player's tumble body comes within a hardcoded 0.05 world units of `firstMountTransform`. On a 0.4-scale vehicle that's basically inside the seat geometry; the player couldn't reach it, `reachedFirstMount` stayed false, steering clamped to 0. Postfixed the getter to return true when the vehicle has a seated player and is scaled.

## 0.5.0

### Updated for R.E.P.O. v0.4
- Rebuilt against the latest game release. 0.4.4 will not load on v0.4 (recompile is required because of internal type changes on the game side). All 22 patch points checked, 20 OK and 2 transpilers (`PhysGrabCart.CartSteer`, `EnemyVision.Vision`) verified intact.

### Bug fixes
- Map collapse messages, sirens, and the truck-arrival cascade ran way too fast in multiplayer. Every client was firing them locally; host-only now, so each fires exactly once and PUN broadcasts.
- Final crush damage was stacking on every player in multiplayer. Every client was running the kill loop and `PlayerHealth.Hurt` routes through `HurtRPC`. Host-only now.

### Improvements
- Map collapse network sync moved from `PhotonNetwork.RaiseEvent` byte codes (198/199) to a `[PunRPC]` component piggybacked on `PunManager`, no more arbitrary 0-199 numbers that could collide with another mod.
- Map collapse chat is more chaotic now: taxman reacts in emojis only, panic messages come from random players in the lobby, larger pool of lines, no immediate repeats.

## 0.4.4

### Bug fixes
- Fixed non-host clients running Dispatch methods (DispatchShrink, DispatchExpand, DispatchExpandNow), these are now gated behind a host/singleplayer check

## 0.4.3

### New
- `MapCollapse` is now public, other mods call `MapCollapse.OnMapHit()` to trigger the collapse event
- `ScaleController.ChallengeMode` public property, implementations set this to enable challenge mode
- Runtime `SemiIconMaker` generation for pocketed items, no more embedded PNGs, works for any item

### Bug fixes
- Fixed map collapse audio ignoring master volume (now routes through the game's SFX mixer group)
- Fixed map collapse alarm doubling when the truck had no unique sounds
- Fixed camera glitch effect not covering the full screen while shrunken
- Fixed shrunken players getting crushed too early during map collapse (raycast distances now scale with player size)
- Fixed pocketed item icons disappearing after level transitions
- Map collapse crush sequence reworked, FOV slam, heavy shake, vignette, and a brief hold before death

### Improvements
- ScalerCore is now a pure library with no user-facing config entries
- `ShrinkChallengeMode` and `MapCollapse` config removed, implementations own their settings
- `MapCollapseHitPatch` removed, implementations provide their own hit detection
- Map collapse enemy speed toned down (1.3x base, up to 1.8x, was 2.5x to 6.5x)
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
- Any non-pocketable item becomes pocketable while shrunken, carts, cart cannons, cart lasers, trackers. Press an inventory key to stash, shoot again to restore.
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
- Logging cleaned up, only warnings and errors in the console
- InvertedMode synced to clients via RPC for proper multiplayer challenge mode

## 0.4.0

### New
- Added `SuppressValueDropExpand` option to ScaleOptions, valuables won't expand on damage while scaled. For cart mods where items bump into each other constantly.
- Added `PreserveMass` option to ScaleOptions, rigidbody mass stays at its original value while scaled. For cart mods where items should weigh the same regardless of visual size.
- Added `ScaleManager.ApplyIfNotScaled()`, scales only if not already scaled, no-op otherwise. Safe to call every frame from continuous triggers.
- Added `ScaleManager.GetController()`, returns the ScaleController for a game object, resolving through PlayerShrinkLink.

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
- ScaleManager.Apply() now takes a ScaleOptions struct, per-call config replaces global ShrinkConfig
- ScaleManager.Apply() without options uses ScaleOptions.Default
- Added ScaleTargets flags enum for filtering what object types can be scaled
- Added ScaleController.TargetType for mods to check what kind of object a controller manages
- Removed ShrinkConfig and ScaleFactor (replaced by ScaleOptions)
- Zero Factor/Speed in ScaleOptions falls back to defaults

### Bug fixes
- Fixed players in tumble/object mode (Q) not being shrinkable by guns
- ScaleManager API now resolves PlayerShrinkLink when target GO doesn't have ScaleController directly
- Fixed Tricycle (Bella) trike mesh not scaling, rider shrank but the bike stayed full size
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
