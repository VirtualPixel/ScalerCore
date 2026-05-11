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

            // Don't make upgrades, power crystals, or health packs pocketable
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

            // Add a SemiIconMaker if the item doesn't have one, so the game's
            // own GenerateIcon produces a proper inventory icon automatically.
            if (ctrl.GetComponentInChildren<SemiIconMaker>(true) == null)
                CreateIconMaker(ctrl.gameObject);

            // Wire up and trigger icon generation.
            attrs.itemEquippable = equippable;
            attrs.icon = null;
            attrs.StartCoroutine(attrs.GenerateIcon());

            Plugin.Log.LogDebug($"[SC] PocketHelper: injected ItemEquippable on {ctrl._displayName}");
            return true;
        }

        /// <summary>
        /// Removes the ItemEquippable that was added by InjectEquippable.
        /// Skips removal if the item is currently in someone's inventory, it'll
        /// come out at full size naturally since IsScaled is already false.
        /// </summary>
        internal static void RemoveEquippable(ScaleController ctrl)
        {
            var equippable = ctrl.GetComponent<ItemEquippable>();
            if (equippable == null) return;
            if (equippable.IsEquipped()) return;

            Object.Destroy(equippable);
            ctrl._itemEquippable = null;

            // Clear the game's cached reference so GenerateIcon doesn't fire
            // again with a stale equippable reference.
            var attrs = ctrl.GetComponent<ItemAttributes>();
            if (attrs != null)
            {
                attrs.itemEquippable = null;
                attrs.icon = null;
            }

            ctrl._networkPV?.RefreshRpcMonoBehaviourCache();
            Plugin.Log.LogDebug($"[SC] PocketHelper: removed ItemEquippable from {ctrl._displayName}");
        }

        /// <summary>
        /// Creates a SemiIconMaker child with a camera that frames the item.
        /// Uses renderer bounds to position the camera at a 3/4 angle matching
        /// vanilla pocket cart style. Per-item overrides can be added here
        /// for items that need custom framing.
        /// </summary>
        static void CreateIconMaker(GameObject item)
        {
            // Calculate bounds from mesh/skinned renderers only, particle systems
            // and trail renderers inflate bounds wildly.
            Bounds? maybeBounds = null;
            AccumulateRendererBounds(item.transform, ref maybeBounds);

            // ItemVehicle.meshTransform deparents from the root when ridden, and the
            // regular Semiscooter has it deparented from spawn. GetComponentsInChildren
            // misses it in that state, traverse it separately.
            var vehicle = item.GetComponent<ItemVehicle>();
            if (vehicle != null && vehicle.meshTransform != null && !vehicle.meshTransform.IsChildOf(item.transform))
                AccumulateRendererBounds(vehicle.meshTransform, ref maybeBounds);

            if (maybeBounds == null) return;
            var bounds = maybeBounds.Value;

            // Create inactive so SemiIconMaker.OnEnable doesn't fire before iconCamera
            // and renderTexture are assigned, otherwise OnEnable skips creating
            // renderTextureInstance and the game's CreateIconFromRenderTexture NREs
            // *after* it teleports the item to (-1000,-1000,-1000), leaving the item
            // out of world to get destroyed.
            var go = new GameObject("ScalerCore_IconMaker");
            go.SetActive(false);
            go.transform.SetParent(item.transform, false);

            var localCenter = item.transform.InverseTransformPoint(bounds.center);
            float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);

            // 3/4 angle: left, slightly above, looking down. FOV widens for larger items.
            var dir = new Vector3(-0.8f, 0.35f, -1.0f).normalized;
            float dist = 2.5f;
            float fov = Mathf.Clamp(27f * Mathf.Max(1f, maxExtent / 0.55f), 27f, 80f);

            go.transform.localPosition = localCenter + dir * dist;
            var lookTarget = localCenter + new Vector3(0.1f, -0.05f, 0f);
            go.transform.LookAt(item.transform.TransformPoint(lookTarget));

            var cam = go.AddComponent<Camera>();
            cam.orthographic = false;
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.192f, 0.302f, 0.475f, 0f);
            cam.cullingMask = 9502721;
            cam.enabled = false;

            var rt = new RenderTexture(512, 512, 32, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;

            var maker = go.AddComponent<SemiIconMaker>();
            maker.iconCamera = cam;
            maker.renderTexture = rt;

            go.SetActive(true);
        }

        static void AccumulateRendererBounds(Transform root, ref Bounds? maybeBounds)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (maybeBounds == null)
                    maybeBounds = r.bounds;
                else
                {
                    var b = maybeBounds.Value;
                    b.Encapsulate(r.bounds);
                    maybeBounds = b;
                }
            }
        }
    }
}
