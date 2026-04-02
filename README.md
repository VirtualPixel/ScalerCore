# ScalerCore

A scaling library for [R.E.P.O.](https://store.steampowered.com/app/3241660/REPO/) modders. Handles the hard parts of scaling game objects -- physics, audio, animation, colliders, NavMesh, multiplayer sync -- so you don't have to.

If you're building a mod that changes the size of things (shrink rays, growth potions, whatever), ScalerCore gives you a clean API and takes care of the edge cases.

**This is a library, not a standalone mod.** End users don't need to interact with it directly. If you're a player, you probably got here because a mod like [ShrinkerGun: COMPRESSOR](https://thunderstore.io/c/repo/p/Vippy/ShrinkerGun/) depends on it.

> **For modders building size-changing mechanics.** If you're looking for a shrink ray, check out [ShrinkerGun: COMPRESSOR](https://thunderstore.io/c/repo/p/Vippy/ShrinkerGun/) which is built on ScalerCore.

## Installation

Reference `ScalerCore.dll` in your project. Add a hard dependency in your plugin:

```csharp
[BepInDependency("Vippy.ScalerCore", BepInDependency.DependencyFlags.HardDependency)]
```

ScalerCore automatically attaches `ScaleController` components to all enemies, players, valuables, items, and doors at runtime via Harmony patches. You don't need to add them yourself.

## Quick Start

```csharp
using ScalerCore;

// Shrink with default options (40% scale)
ScaleManager.Apply(targetGameObject);

// Shrink with custom options
var opts = ScaleOptions.Default;
opts.Factor = 0.5f;
opts.Duration = 60f;
ScaleManager.Apply(targetGameObject, opts);

// Restore with animation
ScaleManager.Restore(targetGameObject);

// Restore instantly (for bonk/damage reactions)
ScaleManager.RestoreImmediate(targetGameObject);

// Check state
bool tiny = ScaleManager.IsScaled(targetGameObject);
```

`ScaleManager.Apply` is host-authoritative. The host calls it, and RPCs propagate to all clients automatically. Late-joining players receive the current state on connect.

## How It Works

ScalerCore attaches a `ScaleController` (a `MonoBehaviourPunCallbacks`) to every enemy, player, valuable, item, and door. On the first scale operation, the controller:

1. Resolves a **handler** via `ScaleHandlerRegistry` based on what the object is
2. Applies the target scale with a smooth animation (back-out easing for players, linear interpolation for objects)
3. Adjusts physics (mass, grab force, collider radius), audio pitch, and NavMesh speed
4. Syncs state to all clients via Photon RPCs
5. Force-applies the target scale every `LateUpdate` to fight game code that resets `localScale`

Built-in handlers cover enemies, players, valuables, and items. Objects without a matching handler (like doors) use the base `ScaleController` behavior.

## Custom Handlers

Implement `IScaleHandler` and register it with a predicate:

```csharp
using ScalerCore;
using ScalerCore.Handlers;

public class MyBossHandler : IScaleHandler
{
    public void Setup(ScaleController ctrl)
    {
        // Cache components, set ScaleTarget if the visual root
        // is different from the physics root
    }

    public void OnScale(ScaleController ctrl)
    {
        // Called when the object is scaled down
    }

    public void OnRestore(ScaleController ctrl, bool isBonk)
    {
        // Called when restored. isBonk = true means instant (damage),
        // false means animated (timer/gun toggle)
    }

    public void OnUpdate(ScaleController ctrl)    { }
    public void OnLateUpdate(ScaleController ctrl) { }
    public void OnDestroy(ScaleController ctrl)    { }
}
```

Register it at plugin startup. Use `priority > 0` to override built-in handlers:

```csharp
ScaleHandlerRegistry.Register(
    new MyBossHandler(),
    go => go.GetComponentInParent<EnemyParent>()?.name.Contains("MyBoss") == true,
    priority: 10
);
```

The registry resolves handlers by checking predicates in descending priority order. First match wins.

## API Reference

### ScaleManager (static)

| Method | Description |
|--------|-------------|
| `Apply(GameObject target)` | Scale with default options (ScaleOptions.Default). |
| `Apply(GameObject target, ScaleOptions options)` | Scale with custom options. Same factor on an already-scaled target toggles it back; different factor rescales. |
| `Restore(GameObject target)` | Restore with smooth animation. |
| `RestoreImmediate(GameObject target)` | Restore instantly (respects bonk immunity timer). |
| `IsScaled(GameObject target)` | Returns true if the object is currently scaled. |
| `CleanupAll()` | Restore all scaled objects. Called automatically on level change. |

### ScaleController (MonoBehaviourPunCallbacks)

Attached automatically to game objects. Key public members:

| Member | Description |
|--------|-------------|
| `IsScaled` | Whether the object is currently scaled. |
| `OriginalScale` | The object's scale before any modification. |
| `TargetType` | What kind of object this is (`ScaleTargets.Players`, `.Enemies`, etc.). |
| `ScaleTarget` | Override in handler's `Setup` to scale a different transform than the controller's. |
| `AllowManualScale` | Static bool — gates F9/F10 debug key requests. Host sets it. |
| `RequestBonkExpand()` | Client-safe expand request (sends RPC to host if called on non-host). |
| `RequestManualExpand()` | Manual expand (skips bonk immunity). |
| `RequestManualShrink()` | Manual shrink request. |

### IScaleHandler

```csharp
public interface IScaleHandler
{
    void Setup(ScaleController ctrl);
    void OnScale(ScaleController ctrl);
    void OnRestore(ScaleController ctrl, bool isBonk);
    void OnUpdate(ScaleController ctrl);
    void OnLateUpdate(ScaleController ctrl);
    void OnDestroy(ScaleController ctrl);
}
```

### ScaleHandlerRegistry (static)

| Method | Description |
|--------|-------------|
| `Register(IScaleHandler handler, Func<GameObject, bool> predicate, int priority = 0)` | Register a handler. Higher priority wins. Built-ins use priority 0. |
| `Resolve(GameObject target)` | Returns the highest-priority matching handler, or null. |

### ScaleOptions

Each `Apply()` call takes a `ScaleOptions` struct. Use `ScaleOptions.Default` as a starting point and override what you need.

| Field | Default | Description |
|-------|---------|-------------|
| `Factor` | `0.4` | Scale multiplier (0.4 = 40% of original size) |
| `Duration` | `0` | Seconds until auto-restore (0 = permanent) |
| `Speed` | `2.0` | Scale animation speed |
| `BonkImmuneDuration` | `5.0` | Grace period after scaling before damage can restore |
| `MassCap` | `5.0` | Max rigidbody mass while scaled |
| `SpeedFactor` | `0.65` | Enemy NavMesh speed multiplier |
| `DamageMultiplier` | `0.1` | Damage dealt by scaled enemies |
| `AnimSpeedMultiplier` | `1.5` | Player animation speed while scaled |
| `FootstepPitchMultiplier` | `1.5` | Player footstep pitch while scaled |
| `AllowedTargets` | `All` | Flags: `Players`, `Enemies`, `Items`, `Valuables`, `All` |
| `InvertedMode` | `false` | If true, scaled state is the default — bonk temporarily grows back |
| `SuppressValueDropExpand` | `false` | If true, valuables won't expand when damaged while scaled (for cart mods) |

### ScaleTargets

Flags enum for filtering what `Apply()` affects:

```csharp
var opts = ScaleOptions.Default;
opts.AllowedTargets = ScaleTargets.Enemies | ScaleTargets.Valuables;
ScaleManager.Apply(target, opts); // skips players and items
```

## Configuration

ScalerCore has one user-facing setting in `ScalerCore.cfg`:

| Setting | Default | Description |
|---------|---------|-------------|
| `ShrinkChallengeMode` | `false` | Players start shrunken. Guns temporarily grow you; damage shrinks you back. Can be toggled live. |

All scaling behavior (factor, speed, duration, etc.) is controlled per-call via `ScaleOptions` -- consuming mods expose whatever settings make sense for them.

## What ScalerCore Handles

For enemies:
- Visual mesh + physics rigidbody scaled separately (EnemyParent is never scaled)
- NavMesh agent speed and radius
- Grab force reduced to zero (instantly grabbable when shrunken)
- Follow force scaled so grabbed enemies don't fight back
- Damage output reduced
- Knockback force reduced
- Bonk restore on taking damage (with immunity window)

For players:
- Camera offset, crouch/crawl positions, vision targets
- Collision capsules (stand, crouch, stand-check)
- Grab strength, range, throw strength
- Movement speed
- FOV adjustment
- Voice chat pitch
- Footstep sound pitch
- Animation speed
- Enlarged pupils (big cute eyes)
- Pause menu avatar scaled to match
- Near clip plane adjusted

For valuables:
- Mass scaled and clamped
- Extraction zone detection box scaled
- Brief indestructibility after shrinking (prevents fall damage from collider resize)
- Value-drop detection triggers bonk restore
- ForceGrabPoint disabled to prevent grab oscillation

For items:
- Effect fields auto-scaled (explosion size, orb radius, damage)
- Inventory system compatibility (yields during equip/unequip)
- ForceGrabPoint handling

For all types:
- Audio pitch shifted on all Sound objects
- Smooth scale animation with force-apply in LateUpdate
- Multiplayer sync via Photon RPCs
- Late-join state sync
- Automatic cleanup on level change

## Known Issues

- Loom (Shadow) arms don't scale proportionally when shrunken (IK solver conflict — cosmetic only, attack distance still scales)
- Some untested enemy types may float or clip into the ground while shrunken

## Dependencies

- [BepInEx 5](https://github.com/BepInEx/BepInEx) (5.4.2100+)
- [REPOLib](https://thunderstore.io/c/repo/p/Zehs/REPOLib/) (3.0.3+)

## Reference Implementation

[ShrinkerGun: COMPRESSOR](https://github.com/Vippy/ShrinkerGun-COMPRESSOR) is a shrink ray gun built on ScalerCore. Shows how to build `ScaleOptions`, call `Apply`, and handle per-target-type durations.
