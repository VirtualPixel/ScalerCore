using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ScalerCore.Handlers
{
    /// <summary>
    /// Item-specific scaling logic (objects with ItemAttributes but NOT ValuableObject).
    /// Handles effect field scaling (explosion size, orb radius, etc.) and per-frame orb enforcement.
    ///
    /// NOTE: This handler is resolved via the registry for pure items (ItemAttributes without ValuableObject).
    /// Its static utility methods (OnShrinkFields/OnRestoreFields/OnUpdateOrb) are cross-cutting and
    /// called from ScaleController for ALL object types (enemies, players, valuables, items).
    /// </summary>
    internal class ItemHandler : IScaleHandler
    {
        // Item effect scaling: explosion size, radius, force, damage, scaled at shrink, restored at expand.
        // Uses reflection so we don't need hard dependencies on specific item classes.
        internal struct ScaledField { public MonoBehaviour comp; public FieldInfo field; public float original; }

        // Field names on item components that should scale.
        //
        // Nothing explosion-related is in here any more. explosionSize, explosionDamage and
        // explosionDamageEnemy only exist on ParticlePrefabExplosion, the effect object the
        // game instantiates at detonation, so they never matched anything on an item and item
        // explosions were going off at full vanilla radius and damage. The one that did match,
        // explosionForceMultiplier, lives on the ExplosionPreset ScriptableObject that every
        // clone shares: two grenades shrunk at once compounded, and a grenade that detonated
        // while shrunk destroyed itself without ever restoring it, so the asset stayed scaled
        // for the rest of the session and every explosion in the game with it. Explosions are
        // scaled at the Spawn callsite now, in ExplosionScalePatches, which is per-blast.
        //
        // The vehicle entries are the authored originals, not the live fields. ItemVehicle
        // recomputes maxForwardSpeed / maxReverseSpeed / hyperMaxSpeed from those every
        // physics step in ApplyTuningMultipliers, so writing the live ones lasted a frame.
        // maxSpeedKmh and softMaxSpeedKmh are real caps with no per-frame rewrite.
        // Scaling them is what stops a half-size vehicle driving at full speed on a tiny
        // chassis (the source of the "shrunk vehicle is undrivable" feel).
        static readonly string[] _floatFieldsToScale = {
            "orbRadiusMultiplier",
            // ItemVehicle speed caps
            "maxSpeedKmh", "softMaxSpeedKmh",
            "originalMaxForwardSpeed", "originalMaxReverseSpeed", "originalHyperMaxSpeed",
            // ValuableArcticSnowBike forward speed
            "bikeForwardSpeed",
        };

        /// <summary>
        /// State for pure items (resolved via registry). Holds ItemOrb ref for per-frame enforcement.
        /// </summary>
        internal sealed class State
        {
            internal ItemOrb? ItemOrb;
            internal bool AddedEquippable;
        }

        // --- IScaleHandler (for pure items resolved via registry) ---

        public void Setup(ScaleController ctrl)
        {
            var state = new State();
            state.ItemOrb = ctrl.GetComponent<ItemOrb>();
            ctrl.HandlerState = state;
        }

        public void OnScale(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state != null)
                state.AddedEquippable = PocketHelper.InjectEquippable(ctrl);
        }

        public void OnRestore(ScaleController ctrl, bool isBonk)
        {
            var state = (State?)ctrl.HandlerState;
            if (state is { AddedEquippable: true })
            {
                PocketHelper.RemoveEquippable(ctrl);
                state.AddedEquippable = false;
            }
        }

        public void OnUpdate(ScaleController ctrl)
        {
            // Orb radius: game recalculates orbRadius each frame, override to match shrunken size.
            var state = (State?)ctrl.HandlerState;
            if (state?.ItemOrb != null)
                OnUpdateOrb(state.ItemOrb, ctrl._options.Factor);
        }

        public void OnLateUpdate(ScaleController ctrl)
        {
            // No item-specific LateUpdate logic.
        }

        public void OnDestroy(ScaleController ctrl)
        {
            // No item-specific destroy logic.
        }

        // --- Cross-cutting static utilities (called from ScaleController for ALL object types) ---

        /// <summary>
        /// Scale item-specific effect fields (explosion size, orb radius, etc.) at shrink time.
        /// Scans all MonoBehaviours on the GO (and referenced ScriptableObjects) for matching fields.
        /// Called from ScaleController for ALL object types.
        /// </summary>
        internal static List<ScaledField>? OnShrinkFields(ScaleController ctrl, float factor)
        {
            var scaledFields = new List<ScaledField>();
            float f = factor;

            // Components on this GameObject only. Never follow a reference off the object:
            // anything shared (a ScriptableObject preset) belongs to every other clone too.
            foreach (var target in ctrl.GetComponents<MonoBehaviour>())
            {
                if (target == null || target == ctrl) continue;
                var type = target.GetType();
                foreach (var name in _floatFieldsToScale)
                {
                    var fi = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (fi == null || fi.FieldType != typeof(float)) continue;
                    float orig = (float)fi.GetValue(target);
                    scaledFields.Add(new ScaledField { comp = target, field = fi, original = orig });
                    fi.SetValue(target, orig * f);
                    Plugin.Log.LogDebug($"[SC]   itemField {type.Name}.{name} {orig:F2} -> {orig * f:F2}");
                }
            }

            if (scaledFields.Count == 0)
            {
                Plugin.Log.LogDebug($"[SC]   itemField scan: no scalable fields found on {ctrl.gameObject.name}");
                return null;
            }
            return scaledFields;
        }

        /// <summary>
        /// Restore item-specific effect fields at expand time.
        /// Called from ScaleController for ALL object types.
        /// </summary>
        internal static void OnRestoreFields(List<ScaledField>? scaledFields)
        {
            if (scaledFields == null) return;
            foreach (var sf in scaledFields)
            {
                // comp is a MonoBehaviour, not object, so this is Unity's overloaded ==
                // and a destroyed component reads as null the way it should.
                if (sf.comp == null) continue;
                sf.field.SetValue(sf.comp, sf.original);
            }
        }

        /// <summary>
        /// Per-frame orb radius enforcement, game recalculates orbRadius each frame from orbRadiusOriginal * multiplier.
        /// Override it every frame to keep the effective radius matched to shrunken size.
        /// </summary>
        internal static void OnUpdateOrb(ItemOrb itemOrb, float factor)
        {
            itemOrb.orbRadius = itemOrb.orbRadiusOriginal * factor;
        }
    }
}
