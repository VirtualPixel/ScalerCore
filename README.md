# ScalerCore

A scaling library for [R.E.P.O.](https://store.steampowered.com/app/3241660/REPO/) modders. Handles the hard parts of scaling game objects -- physics, audio, animation, colliders, NavMesh, multiplayer sync -- so you don't have to.

If you're building a mod that changes the size of things (shrink rays, growth potions, whatever), ScalerCore gives you a clean API and takes care of the edge cases.

**This is a library, not a standalone mod.** End users don't need to interact with it directly. If you're a player, you probably got here because a mod like [ShrinkerGun: COMPRESSOR](https://thunderstore.io/c/repo/p/Vippy/ShrinkerGun/) depends on it.

> **v0.2.0 -- Early Access.** The API surface is small and may change. Pin your dependency version.

## Installation

Reference `ScalerCore.dll` in your project. Add a hard dependency in your plugin:

```csharp
[BepInDependency("Vippy.ScalerCore", BepInDependency.DependencyFlags.HardDependency)]
```

ScalerCore automatically attaches `ScaleController` components to all enemies, players, valuables, items, and doors at runtime via Harmony patches. You don't need to add them yourself.

## Quick Start

```csharp
using ScalerCore;

// Shrink something (uses the configured ShrinkFactor, default 0.4)
ScaleManager.Apply(targetGameObject);

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
| `Apply(GameObject target, float factor = 0)` | Scale the target down. Factor param is reserved -- currently uses `ShrinkConfig.Factor`. |
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
| `ScaleTarget` | Override in handler's `Setup` to scale a different transform than the controller's. |
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

### ScaleFactor / ScaleOptions

`ScaleFactor` is a thin float wrapper with named presets (`ScaleFactor.Shrink`, `.Grow`, `.Normal`). `ScaleOptions` groups per-type settings (duration, animation speed, bonk immunity, etc.) with presets for Enemy, Player, Valuable, and Item.

## Configuration

All values are configurable via BepInEx config (`ScalerCore.cfg`). These are the defaults:

| Setting | Default | Description |
|---------|---------|-------------|
| `ShrinkFactor` | `0.4` | Scale multiplier (0.4 = 40% of original size) |
| `ShrinkSpeed` | `2.0` | Scale animation speed |
| `EnemyDamageMultiplier` | `0.1` | Damage dealt by shrunken enemies (10% of normal) |
| `EnemyShrinkDuration` | `120` | Seconds until enemy auto-restores (0 = never) |
| `ValuableShrinkDuration` | `0` | Seconds until valuable auto-restores (0 = never) |
| `ItemShrinkDuration` | `0` | Seconds until item auto-restores (0 = never) |
| `PlayerShrinkDuration` | `0` | Seconds until player auto-restores (0 = never) |
| `EnemyBonkImmuneDuration` | `5` | Grace period after shrinking before damage can restore |
| `ValuableBonkImmuneDuration` | `5` | Grace period for valuables |
| `EnemyShrinkSpeedFactor` | `0.65` | Movement speed multiplier for shrunken enemies |
| `ShrunkMassCap` | `5.0` | Max rigidbody mass while shrunken |
| `ShrunkAnimSpeedMult` | `1.5` | Player animation speed while shrunken |
| `ShrunkFootstepPitchMult` | `1.5` | Player footstep pitch while shrunken |

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

- Not all enemy types have been tested -- some may float or clip into the ground while shrunken
- Grab strength/range values are still being tuned for the best feel
- Non-host grab strength may not scale correctly in multiplayer (physics runs on host)
- Early access 0.2.0 -- API may evolve, pin your dependency version

## Dependencies

- [BepInEx 5](https://github.com/BepInEx/BepInEx) (5.4.2100+)
- [REPOLib](https://thunderstore.io/c/repo/p/Zehs/REPOLib/) (3.0.3+)

## Reference Implementation

[ShrinkerGun: COMPRESSOR](https://github.com/Vippy/ShrinkerGun-COMPRESSOR) is a shrink ray gun built on ScalerCore. It's a good example of how to call `ScaleManager.Apply/Restore` from a weapon mod.
