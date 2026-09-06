# Changelog

## 1.1.0

### New
- Scaled objects show up right for players who do not have ScalerCore. Items, valuables, carts, vehicles and cosmetic boxes are scaled by respawning them through the game's own network instantiate with the scale in the instantiation data, which the game applies on every client whether or not it runs the mod. An unmodded client gets the object at its real size with the right colliders, instead of a full-size body shaking against the host's small one and walking through walls, which is what a shrunken cart or the contents of a shrink cart looked like to them. On a client with ScalerCore nothing looks different: the clone reads the rest of the data and runs the usual session, animation in, audio, mass, pocketing, timers, late joiners included. Players, enemies and doors have no prefab to respawn from and stay on the shrink RPC as before.
- An object in someone's hand, an inventory slot or a seat is not swapped under them: it scales on the spot over the RPC as before, and the respawn happens the moment it is free.
- `ScaleManager.NativeSync` (default on) and `ScaleManager.NativeSyncWhileHeld` (default on) for consuming mods. A `scalercore_nativesync_off` file in BepInEx/config turns it off for a session. A respawn replaces the GameObject, so a mod that keeps a controller reference across an Apply should re-resolve it with `ScaleManager.GetController`.

### Fixed
- The frame drop at the end of the map collapse. Hundreds of loose objects squeezed into a few metres were a contact-solver pile-up. The destroy queue now clears them by 60% of the collapse, whatever is left stops generating contacts at 80% (it still rides the shrink), and the level's own scale, which re-cooks every mesh collider on each write, is written every other frame.

### Internal
- No API removals or signature changes. Mods built against 1.0.4, 1.0.5 or 1.0.6 need nothing.
- The session options that ride the shrink RPC have a codec of their own now, shared with the respawn data.
- `ScalerCore.Tests` covers the respawn data round trip, the old-host short arrays, the prefab path off a clone name, and the respawn decision table.

## 1.0.6

### Fixed
- The map collapse warning lights blink at the speed they were meant to, somewhere between every four seconds at the start and every second and a half at the end, instead of strobing. The blink took the clock reading modulo a period that shrinks every frame, and on a clock that has been counting for a while (Photon's server time in a lobby, or the game's own clock half an hour in) that jumps hundreds of seconds a frame, so every light in the level flickered at frame rate and the siren restarted on each false edge. That is the "lights and sounds going way too fast" collapse, and it was a photosensitivity hazard. The blink now runs off the collapse's own elapsed time, so it is smooth, identical on every machine, and has a regression test walking the whole collapse at 60 fps.
- The map collapse now shrinks the level for everyone, not just the host. Three things on the client side, all in one go. The level-change cleanup was hung off a game method that a non-host returns out of on its first line, and the cleanup ran anyway, so anything on a client that poked that method mid-level restored the level to full size on that machine while the host kept collapsing, which is where players standing inside walls and outside the map came from. It now checks the same condition the game does before touching anything, on top of the real per-client cleanup that already rode the level-change message. The host also re-sends the collapse anchor every five seconds while it runs, so a client that missed the start (level not finished loading on their side, or the message lost) picks it up at the right point instead of never. And the level root is taken from the game's own field for it rather than found by name.
- A collapse effect failing on one machine no longer stops the collapse there. The lights, audio, camera and chat extras run guarded now; if one throws, it is logged once with the stack and the level keeps shrinking.
- Every machine logs the collapse start, buildup, end and restore, with the shared clock values, so a report from a lobby says exactly where a client stopped.

### Internal
- No API changes. Mods built against 1.0.4 or 1.0.5 need nothing.
- `ScalerCore.Tests` covers the collapse blink and the level-change gate.

## 1.0.5

### Fixed
- Voice chat stopped cutting out for scaled players. The pitch was written straight onto the AudioSource that Photon Voice uses as its playback sink, and that source is fed by a ring buffer the network fills at exactly the sample rate. Raising the pitch made the source drain faster than the network filled it, so the jitter cushion ran out on a fixed clock (about every two thirds of a second at the pitch a shrunk player runs), Photon threw the write head back to a full cushion ahead, and the gap in between is what everyone heard as the voice dropping. It repeated for as long as the player stayed small. The speaker now runs at normal speed and the pitch happens inside the stream instead, so the buffer never runs dry. Same voice, it just stops disappearing. Voices routed through a scaled walkie-talkie, teeth or dead Semibot head go down the same path, and chat text-to-speech is untouched since it plays an ordinary clip rather than a stream.
- Scaled players get the whole voice treatment even when their voice chat turns up late. The game hands a player their voice chat object on a network message that usually lands after the scale has already been applied, and the voice step was skipped when that happened: grown players lost the room reverb and the extra carry entirely, and the pitch snapped in instead of easing. It now runs the moment the voice chat attaches.
- Shrunken enemies stopped floating above the floor. The mesh grounding measured from a height captured when the enemy spawned, which only holds for an enemy whose mesh nothing else moves. The NavMesh agent owns the position of whichever transform it sits on, remote clients walk the whole enemy toward the host's position every frame, and a couple of enemies copy their follow target's position onto their own mesh. All of those ended up pinned at their spawn height and floated (or sank) the moment they reached a floor at a different level. The correction reads the pose the game set this frame now. The pivot-to-feet distance is also measured the first time the enemy is actually scaled rather than at spawn, so a body that switches part of itself on later gets grounded as well.
- Scaled items and valuables blow up at their own size. A shrunken grenade was going off at full vanilla radius and full damage: the size and damage numbers only ever land on the effect object the game builds at detonation, so no amount of field scaling on the grenade itself could reach them. They are set at the spawn call now, the same place enemy explosions have been scaled since 1.0.0.
- A scaled explosion no longer drags every other explosion in the session down with it. The one explosion field that did scale lives on a preset asset shared by every clone that uses it, so two grenades shrunk at once compounded, and a grenade that detonated while shrunk destroyed itself without ever putting the value back. Nothing shared gets written any more.
- Shrunken vehicles are slow again. Three of the five speed caps were being written to fields the vehicle recomputes from its own stored originals every physics step, so the change lasted about a frame and a tiny chassis kept most of its full-size top speed.
- A cart or vehicle that was sitting in someone's inventory when it grew back stayed pocketable at full size for the rest of the level. The injected inventory component is deliberately left alone while the item is stashed, but the restore was marking it removed anyway and never came back for it.
- Map collapse actually collapses on everyone. The crush at the end asked the game to damage each player from the host, which the game ignores for anybody who isn't the host, so nobody but the host died. With the rest of the lobby alive the round never ended, and because the cleanup rides the level change, the level stayed at 2% with the lights red and the alarm looping until someone quit. The crush also ignored player size (the size lookup was reading the wrong object, so a shrunken player got crushed at full-size distances), and the warning-light blink was the one part of the event running off local time instead of the shared clock, so it strobed out of phase between players.
- The collapse cleanup no longer runs on level changes where nothing collapsed. It fires on every truck-to-level and level-to-shop transition, and it was writing a field-of-view of zero onto the camera and re-applying the previous level's fog before any collapse had ever happened.
- VR support no longer switches itself off for the session when RepoXR happens to load after ScalerCore. Plugin load order between the two is arbitrary and the check ran once at startup.

### Internal
- No API changes. `ScaleManager`, `ScaleOptions` and `IScaleHandler` are identical to 1.0.4, so mods built against 1.0.4 need nothing.
- `ScalerCore.Tests` covers the grounding decision and the voice shifter's buffer wrap, which are the two bits of pure arithmetic in here that go quiet rather than loud when they are wrong.

## 1.0.4

### Fixed
- Player scaling no longer errors on the latest game update. The speed adjustment applied while a player shrinks or grows is now matched to the game's current movement call, so scaling a player applies cleanly instead of raising a missing-method error mid-effect.

## 1.0.3

### New
- Two per-axis grow caps for enemies next to the uniform `EnemyPhysicalFactorCap`: `EnemyWidthFactorCap` holds the body's X/Z at the cap while the height keeps climbing to `Factor`, so a giant reads its full height but stays doorway-sized, and `EnemyHeightFactorCap` holds the collider's Y down so a giant bulks out instead of wedging under ceilings or getting shoved over. `EnemyNavRadiusFactorCap` splits the pathing footprint from the body so a wide enemy still paths through the doorways the navmesh was baked for. A per-axis cap wins over the uniform cap; all three are grow-only and enemy-only, 0 disables. They ride the sync RPC so client colliders match the host's.
- Grown enemies keep their vision origin at its vanilla height. The vision raycasts start at a transform that rides up with a tall mesh, so a giant whose head clipped the ceiling went blind. The eye point holds its vanilla offset from the body while grown; shrinking leaves it alone.

### Fixed
- Shrunken monsters stopped floating. Scaling a mesh whose pivot sits above its feet pulls the feet up toward the pivot, so a shrunken enemy hovered off the ground by the pivot-to-feet distance times the shrink. Enemy meshes now measure that distance at setup (character renderers only, so shadows and ground decals don't inflate it) and hold their feet on the floor in both directions; the same lift keeps a capped giant's feet planted instead of sunk. The Floater keeps the shrink correction but skips the grow grounding, it hovers on purpose.
- Valuables that spawn after the level has started and get scaled in the same frame no longer stay full-size on other clients. A late-spawned networked object instantiates and gets its shrink RPC from the host in the same network pass, but the `ScaleController` was attached and registered in `Start`, a frame later. So on the host the shrink broadcast was skipped against a not-yet-resolved PhotonView, and on clients the RPC arrived before the component existed and PUN logged `RPC method 'RPC_Shrink' not found`. Objects present at level load took a later path, well after the controller was up, so they synced fine and only late spawns broke. The controller now attaches during the object's `Awake` and registers its RPC routing there, so the host resolves the view in time to broadcast and clients can receive. A shrink RPC that still lands before the controller finishes initializing is held and applied once `Start` has run, so it lands with mass and the extraction-detection box scaled rather than half-applied. The host's own late-spawn scaling got the same init guarantee, so its mass and extraction-box scaling no longer depend on `Start` having run first.

## 1.0.2

### Fixed
- A grown player re-shot as shrunken without expanding between (switching the gun from grow to shrink and firing again) kept the giant's camera height, FOV, and collision while the body and its shadow animated down to the small size, so the view sat far above a tiny Semibot. The rescale path now re-runs the player's one-shot local effects, and the apply restores the captured originals before re-scaling when the factor changes, so the camera follows the body in either direction. Toggling back through normal size always worked; this covers the in-place switch.
- Shrunken players no longer over-bob the head. The body-size stride correction was applying to shrink as well as growth, and with the shrink animation multiplier running faster than the movement slowdown it drove the camera bob to roughly twice the vanilla rate. The correction is growth-only again: a shrunken player keeps the vanilla coupling, where the smaller body's slower movement already eases the bob. Growth still gets the long heavy stomps.
- The first scale of a session no longer freeze-frames for ~200ms. The RepoXR (VR) compatibility probe resolved its types with a name lookup that, when RepoXR is absent, scans every type in every loaded assembly before giving up. It ran lazily on the first scale and stalled that frame. The probe now checks whether a RepoXR assembly is even loaded first (a cheap enumeration) and skips the full scan when it isn't, and it runs once at plugin load so any residual cost lands during startup instead of mid-game. VR detection is unchanged when RepoXR is installed.

## 1.0.1

### Fixed
- Shrunken valuables no longer read as inside the extraction point when they physically aren't. The detection probe (`RoomVolumeCheck`) is a box built from a size (`currentSize`) and an offset to its center (`CheckPosition`); ScalerCore scaled the size on shrink but left the offset at full length, so the probe drifted off the shrunken body and could still overhang the extraction zone. The offset now scales with the factor alongside the size, captured once and put back on expand.

### Internal
- Dropped the `Compatibility / RepoXR VR support` config toggle. The bridge already no-ops unless RepoXR is installed and the player is in VR, both found by reflection at runtime, so the toggle was redundant gating. Back to a pure library with no user-facing config.

## 1.0.0

### New
- Dead Semibot heads can be scaled. Off by default and policy lives with the calling mod: ScalerCore exposes `ScaleManager.AllowDeadHeads`, ShrinkerGun binds the user-facing `Targets / ShrinkDeadHeads` setting. Heads scale like any other prop, minus pocketing (a pocketed head would fight the revive logic).
- The shop radio is scalable now. It was the one grabbable prop in the truck the ray refused to touch.
- Objects that re-broadcast a player's voice now pitch that voice with their own size. The teeth valuable, the walkie-talkie, and a dead Semibot head re-assert the voice override every frame while transmitting, so scaling the routing object now rides the routed voice: a shrunken teeth toy squeaks, a grown one rumbles, a shrunken dead head chipmunks its owner. Runs locally on every client off synced scale state, so everyone hears the same thing.

### New (growth support rounded out)
- Giant feel pass from playtesting: FOV shift for growth runs at 40% strength floored at -8 degrees (the linear curve hit -20 at 2x and kept going); head bob and footstep cadence follow body size instead of the speed change (CameraBob couples its cycle to the speed multiplier, and footsteps fire per bob cycle): long heavy stomps when big, quick patter when small; pupils go small and hard when big instead of inheriting the tiny-player dilation; growth footsteps land deeper (0.55x pitch).
- Grown players hold grabbed items at proportional arm's length. The vanilla hold distance and look-target offset are flat world units that sit inside a giant's body, so an item rode half-buried in the chest. Both scale with the factor when grown; shrunken keeps the existing 0.7x hold and zeroed offset.
- Enemy attack reach follows body size. The AI's "close enough to attack" distances were fixed numbers, so a grown Loom's own bulk kept players outside a reach check it could never satisfy, and shrunk enemies (the Clown among them) swung from distances their tiny bodies don't cover. The Loom's reach fields scale with its factor, and the Clown's hardcoded melee trigger gets the same treatment through an IL swap that reports loudly if a game update moves it. Both directions.
- The Loom's hand-reach envelope scales too, not just the distance check. EnemyShadow clamps its wrists to a box built from marker transforms whose localScale never inherits the body's, so a grown Loom told to attack would reach and reach and the slap never fired, her hands penned in the vanilla envelope. The markers scale with the factor, captured and restored like everything else.
- Grown enemies stop growing PHYSICALLY before they wedge in the level. New `ScaleOptions.EnemyPhysicalFactorCap` (1.4x in the Growth preset, 0 disables): past the cap the colliders and nav agent radius hold while the visuals, reach, audio, and mass keep climbing to `Factor`, so a giant still fits the doorways the navmesh was baked for instead of jamming in the geometry. Grow-only, enemies only; shrinking and other types ignore it.
- Scaled enemies' explosions scale with them. Enemy ability code passes hardcoded numbers to the explosion spawner (a Bang head always went up at size 1, damage 30), so size, damage, and force now multiply by the enemy's factor at spawn, and the blast audio pitches with it. A grown Bang is a bassy BOOM; a shrunk one pops.
- Grown things sound the part beyond pitch, and it all rides one knob: `ScaleOptions.AudioPresence` (0 = pitch only, 1 = full effect, default 1 in both presets). Volume lifts a touch, reverb gets a light lift (lean in too hard and the wet tail just buries the dry transient that reads as "big"), sound falloff scales with the factor so a giant carries across the level while a tiny one goes sneaky-quiet at range, and grown players' voice chat gets the same light-reverb-and-carry treatment (mic volume stays the player's own). Captured and restored with the rest of the audio state, and the knob rides the sync RPC so every client hears the same presence. Items are untouched, their explosion fields already scale.
- `ScaleOptions.Growth` preset: twice the size, heavier, a touch faster, slower deliberate animation, low footsteps. Voice and entity sounds already deepen automatically from the factor; the preset covers the knobs that don't derive from it.
- Pitch curves got two fixes. Entity and effect sounds use a shallower grow slope floored at 0.5: deep reads as "big" well before half pitch, and below that the highs are gone and everything turns to mud (the old linear curve also ran negative past 3x growth and inverted the audio). Voices get their own curve apart from the SFX one, with an intelligibility floor: a grown voice only drops to 0.8 (a growl can go deep, a callout still has to land), shrink keeps the chipmunk up to 2x and stays understandable. The teeth, walkie, and dead-head routing above ride the same voice curve.

### Fixed
- Every ScaleController RPC validates its sender now. Scale state (`RPC_Shrink`/`RPC_Expand`/pitch cancel) only accepts the master; client requests (manual scale, bonk expand, inverted reshrink) only accept the player who owns the view. Before this, anyone in the room could spoof a shrink at any controller.
- Grown valuables stay heavy through the game's own mass resets. PhysGrabObject keeps a private massOriginal and writes it back to the rigidbody whenever an alter-mass episode ends, silently undoing the scaled mass mid-session: some grown props stopped feeling heavy while others that never hit that path stayed heavy. ScalerCore scales massOriginal alongside the live mass so the game's resets land on the scaled value, and puts the vanilla number back at expand.
- A rescale mid-session (a grown object re-shot as shrunken, or the reverse) carries its per-session treatments across instead of stranding them. The audio treatment, mass, force-grab point, and item effect fields all re-derive from the new factor; before this a re-shot object kept its old giant audio and a grab point that stayed disabled, so it couldn't be picked up. The audio capture also restores any live treatment before re-capturing, so the already-treated values never get recorded as the originals.
- The mass-drift diagnostics (per-physics-call logs with a stack walk) only arm with SCALERCORE_DIAG=1 in the environment. They were logging at Info for every scaled valuable.
- Map collapse (the April 1st event) actually shows up for everyone now. The relay component only existed on the machine that fired the shot, so the start RPC arrived at a PhotonView with nothing listening and non-hosts saw nothing. It attaches on every client at PunManager.Awake.
- Map collapse runs off Photon's synchronized clock instead of each client integrating its own deltaTime from whenever the RPC landed. Blink period, alarm pitch, and the scale curve are identical on every machine at every instant; late joiners fast-forward into the running collapse instead of missing it.
- The collapse start RPC validates its sender (master only), and the request RPC checks the sender exists.
- The CartSteer transpiler announces itself loudly when a game update breaks its pattern instead of silently disabling cart pull-distance scaling. Both failure paths (missing grabber local, no Lerp callsites) log what died and ask for a report.
- Scale apply stays cheap on big hierarchies. The reflection field walk that finds Sound objects per component is cached by type (first encounter scans, the rest are a dictionary hit), so the whole apply runs in one frame without paying reflection across the tree every time. A slow apply (>=10ms) or slow sound pass (>=5ms) warns with the object name so any remaining hitch can be reported and narrowed down.

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
