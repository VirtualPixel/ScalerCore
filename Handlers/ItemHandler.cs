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
        // Item effect scaling: explosion size, radius, force, damage — scaled at shrink, restored at expand.
        // Uses reflection so we don't need hard dependencies on specific item classes.
        internal struct ScaledField { public object comp; public FieldInfo field; public float original; }

        // Field names on item components (or referenced ScriptableObjects) that should scale.
        static readonly string[] _floatFieldsToScale = {
            "explosionSize", "explosionForceMultiplier",
            "orbRadiusMultiplier",
        };
        static readonly string[] _intFieldsToScale = {
            "explosionDamage", "explosionDamageEnemy",
        };
        // ScriptableObject field names to follow (e.g. ItemGrenadeExplosive.explosionPreset -> ExplosionPreset)
        static readonly string[] _soFieldNames = {
            "explosionPreset",
        };

        /// <summary>
        /// State for pure items (resolved via registry). Holds ItemOrb ref for per-frame enforcement.
        /// </summary>
        internal sealed class State
        {
            internal ItemOrb? ItemOrb;
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
            // Item-specific effect field scaling is handled by the cross-cutting
            // static methods called from ScaleController.
        }

        public void OnRestore(ScaleController ctrl, bool isBonk)
        {
            // Item-specific effect field restoration is handled by the cross-cutting
            // static methods called from ScaleController.
        }

        public void OnUpdate(ScaleController ctrl)
        {
            // Orb radius: game recalculates orbRadius each frame — override to match shrunken size.
            var state = (State?)ctrl.HandlerState;
            if (state?.ItemOrb != null)
                OnUpdateOrb(state.ItemOrb);
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
        internal static List<ScaledField>? OnShrinkFields(ScaleController ctrl)
        {
            var scaledFields = new List<ScaledField>();
            float f = ShrinkConfig.Factor;

            // Collect targets: all MonoBehaviours on this GO + any SOs they reference.
            var targets = new List<object>();
            foreach (var mb in ctrl.GetComponents<MonoBehaviour>())
            {
                if (mb == null || mb == ctrl) continue;
                targets.Add(mb);
                // Follow SO references (e.g. explosionPreset on grenades).
                var mbType = mb.GetType();
                foreach (var soName in _soFieldNames)
                {
                    var soFi = mbType.GetField(soName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (soFi == null) continue;
                    var so = soFi.GetValue(mb);
                    if (so != null)
                    {
                        targets.Add(so);
                        Plugin.Log.LogDebug($"[SC]   following SO ref {mbType.Name}.{soName} -> {so.GetType().Name}");
                    }
                }
            }

            foreach (var target in targets)
            {
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
                foreach (var name in _intFieldsToScale)
                {
                    var fi = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (fi == null || fi.FieldType != typeof(int)) continue;
                    int orig = (int)fi.GetValue(target);
                    int scaled = Mathf.RoundToInt(orig * f);
                    scaledFields.Add(new ScaledField { comp = target, field = fi, original = orig });
                    fi.SetValue(target, scaled);
                    Plugin.Log.LogDebug($"[SC]   itemField {type.Name}.{name} {orig} -> {scaled}");
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
                if (sf.comp == null) continue;
                if (sf.field.FieldType == typeof(int))
                    sf.field.SetValue(sf.comp, (int)sf.original);
                else
                    sf.field.SetValue(sf.comp, sf.original);
            }
        }

        /// <summary>
        /// Per-frame orb radius enforcement — game recalculates orbRadius each frame from orbRadiusOriginal * multiplier.
        /// Override it every frame to keep the effective radius matched to shrunken size.
        /// </summary>
        internal static void OnUpdateOrb(ItemOrb itemOrb)
        {
            itemOrb.orbRadius = itemOrb.orbRadiusOriginal * ShrinkConfig.Factor;
        }
    }
}
