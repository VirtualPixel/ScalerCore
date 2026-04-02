# ScalerCore API Documentation

ScalerCore is a scaling library for R.E.P.O. mods. It handles physics, audio, animation, colliders, NavMesh, and multiplayer sync for scaled game objects.

This page covers the public API. If you're building a mod that changes the size of things, this is what you need.

## Quick Start

Add a hard dependency in your plugin:

```csharp
[BepInDependency("Vippy.ScalerCore", BepInDependency.DependencyFlags.HardDependency)]
```

Reference `ScalerCore.dll` in your project. ScalerCore auto-attaches `ScaleController` components to enemies, players, valuables, items, and doors at runtime.

Basic usage:

```csharp
using ScalerCore;

// Shrink (uses configured ShrinkFactor, default 0.4)
ScaleManager.Apply(targetGameObject);

// Restore with animation
ScaleManager.Restore(targetGameObject);

// Restore instantly (bonk/damage)
ScaleManager.RestoreImmediate(targetGameObject);

// Check state
bool tiny = ScaleManager.IsScaled(targetGameObject);
```

`ScaleManager.Apply` is host-authoritative. The host calls it and RPCs propagate to all clients. Late-joining players receive current state on connect.

## Custom Handler Registration

Built-in handlers cover enemies, players, valuables, and items. To add custom behavior for specific objects, implement `IScaleHandler` and register it:

```csharp
using ScalerCore;
using ScalerCore.Handlers;

public class MyBossHandler : IScaleHandler
{
    public void Setup(ScaleController ctrl)
    {
        // Cache components. Set ctrl.ScaleTarget if the visual root
        // differs from the physics root.
    }

    public void OnScale(ScaleController ctrl)
    {
        // Called when the object is scaled down.
    }

    public void OnRestore(ScaleController ctrl, bool isBonk)
    {
        // Called on restore. isBonk = true for instant (damage),
        // false for animated (timer/gun toggle).
    }

    public void OnUpdate(ScaleController ctrl)    { }
    public void OnLateUpdate(ScaleController ctrl) { }
    public void OnDestroy(ScaleController ctrl)    { }
}
```

Register at plugin startup. Higher priority wins over built-in handlers (priority 0):

```csharp
ScaleHandlerRegistry.Register(
    new MyBossHandler(),
    go => go.GetComponentInParent<EnemyParent>()?.name.Contains("MyBoss") == true,
    priority: 10
);
```

The registry checks predicates in descending priority order. First match wins.

## API Reference

### ScaleManager (static)

| Method | Description |
|--------|-------------|
| `Apply(GameObject target)` | Scale with default options (ScaleOptions.Default). |
| `Apply(GameObject target, ScaleOptions options)` | Scale with custom options. Same factor toggles; different factor rescales. |
| `Restore(GameObject target)` | Restore with smooth animation. |
| `RestoreImmediate(GameObject target)` | Restore instantly (respects bonk immunity timer). |
| `IsScaled(GameObject target)` | Returns true if currently scaled. |
| `CleanupAll()` | Restore all scaled objects. Called automatically on level change. |

### ScaleController (MonoBehaviourPunCallbacks)

Auto-attached to game objects. Key public members:

| Member | Type | Description |
|--------|------|-------------|
| `IsScaled` | `bool` | Whether the object is currently scaled. |
| `OriginalScale` | `Vector3` | Scale before any modification. |
| `ScaleTarget` | `Transform?` | Override in `Setup` to scale a different transform. |
| `RequestBonkExpand()` | method | Client-safe expand request (RPCs to host if non-host). |
| `RequestManualExpand()` | method | Manual expand (skips bonk immunity). |
| `RequestManualShrink()` | method | Manual shrink request. |

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

Each `Apply()` call takes a `ScaleOptions` struct. Use `ScaleOptions.Default` and override what you need.

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

## Configuration

ScalerCore has one user-facing setting in `ScalerCore.cfg`:

| Setting | Default | Description |
|---------|---------|-------------|
| `ShrinkChallengeMode` | `false` | Players start shrunken. Guns temporarily grow you; damage shrinks you back. |

Scaling behavior is controlled per-call via `ScaleOptions` — consuming mods expose whatever settings make sense for them.

## What ScalerCore Handles Automatically

**Enemies:** Visual mesh + physics rigidbody scaled separately (EnemyParent untouched), NavMesh speed/radius, grab force zeroed, follow force scaled, damage/knockback reduced, bonk restore with immunity window.

**Players:** Camera offset, collision capsules, grab stats, movement speed, FOV, voice pitch, footstep pitch, animation speed, enlarged pupils, pause menu avatar, near clip plane.

**Valuables:** Mass scaled/clamped, extraction box scaled, brief indestructibility after shrink, value-drop bonk detection, ForceGrabPoint disabled.

**Items:** Effect fields auto-scaled (explosions, orbs), inventory system compatibility, ForceGrabPoint handling.

**All types:** Audio pitch shift, smooth animation with LateUpdate force-apply, Photon RPC sync, late-join state sync, level change cleanup.
