using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ScalerCore.Handlers
{
    /// <summary>
    /// Shared logic for making non-equippable items pocketable while shrunken.
    /// Any item that has ItemAttributes but no ItemEquippable gets one added at
    /// shrink time so the player can stash it with an inventory key. The component
    /// is removed on restore so full-size items can't be pocketed.
    /// </summary>
    internal static class PocketHelper
    {
        static readonly Dictionary<string, Sprite> _iconCache = new();

        // Maps item name substrings to embedded resource filenames.
        static readonly (string match, string resource)[] _iconMap = {
            ("Cart Laser",  "CartLaserIcon.png"),
            ("Cart Cannon", "CartCannonIcon.png"),
            ("Cart",        "CartIcon.png"),
        };

        /// <summary>
        /// If the object doesn't already have ItemEquippable, adds one and wires up
        /// the ScaleController cache and Photon RPC table. Returns true if a component
        /// was added (so the caller knows to clean it up on restore).
        /// </summary>
        internal static bool InjectEquippable(ScaleController ctrl)
        {
            if (ctrl.GetComponent<ItemEquippable>() != null) return false;

            var attrs = ctrl.GetComponent<ItemAttributes>();
            if (attrs == null) return false;

            // Don't make upgrades, power crystals, or health packs pocketable —
            // they're consumed on use, not carried as inventory items.
            var itemType = attrs.itemType;
            if (itemType == SemiFunc.itemType.item_upgrade ||
                itemType == SemiFunc.itemType.player_upgrade ||
                itemType == SemiFunc.itemType.power_crystal ||
                itemType == SemiFunc.itemType.healthPack)
                return false;

            var equippable = ctrl.gameObject.AddComponent<ItemEquippable>();
            ctrl._itemEquippable = equippable;

            // ItemEquippable.Start() caches physGrabObject, but it already ran (we're
            // adding the component after Start). Set it manually so equip/unequip works.
            var pgo = ctrl.GetComponent<PhysGrabObject>();
            if (pgo != null)
                equippable.physGrabObject = pgo;

            ctrl._networkPV?.RefreshRpcMonoBehaviourCache();

            // Try to get an icon for the inventory slot. ItemAttributes.GenerateIcon
            // already ran at Start and skipped because there was no ItemEquippable yet.
            if (attrs.icon == null)
            {
                // Re-run the game's own icon generator in case the prefab has a SemiIconMaker.
                attrs.itemEquippable = equippable;
                attrs.StartCoroutine(attrs.GenerateIcon());

                // Fall back to our embedded icons matched by item name.
                attrs.icon = FindEmbeddedIcon(ctrl.gameObject.name);
            }

            Plugin.Log.LogDebug($"[SC] PocketHelper: injected ItemEquippable on {ctrl._displayName}");
            return true;
        }

        /// <summary>
        /// Removes the ItemEquippable that was added by InjectEquippable.
        /// Skips removal if the item is currently in someone's inventory — it'll
        /// come out at full size naturally since IsScaled is already false.
        /// </summary>
        internal static void RemoveEquippable(ScaleController ctrl)
        {
            var equippable = ctrl.GetComponent<ItemEquippable>();
            if (equippable == null) return;
            if (equippable.IsEquipped()) return;

            Object.Destroy(equippable);
            ctrl._itemEquippable = null;
            ctrl._networkPV?.RefreshRpcMonoBehaviourCache();
            Plugin.Log.LogDebug($"[SC] PocketHelper: removed ItemEquippable from {ctrl._displayName}");
        }

        /// <summary>
        /// Finds the best matching embedded icon for an item by name.
        /// Checks longer matches first so "Cart Laser" matches before "Cart".
        /// </summary>
        static Sprite? FindEmbeddedIcon(string itemName)
        {
            foreach (var (match, resource) in _iconMap)
            {
                if (itemName.IndexOf(match, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                return LoadEmbeddedIcon(resource);
            }
            return null;
        }

        /// <summary>
        /// Loads a PNG from the DLL's embedded resources by filename. Cached.
        /// </summary>
        static Sprite? LoadEmbeddedIcon(string filename)
        {
            if (_iconCache.TryGetValue(filename, out var cached))
                return cached;

            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(filename, System.StringComparison.OrdinalIgnoreCase));
            if (resName == null) return null;

            using var stream = asm.GetManifestResourceStream(resName);
            if (stream == null) return null;

            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var tex = new Texture2D(2, 2);
            if (!ImageConversion.LoadImage(tex, ms.ToArray(), false))
            {
                Object.Destroy(tex);
                return null;
            }

            var sprite = Sprite.Create(tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f));
            _iconCache[filename] = sprite;
            return sprite;
        }
    }
}
