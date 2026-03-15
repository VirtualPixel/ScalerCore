using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using ScalerCore.Handlers;
using ScalerCore.Utilities;
using UnityEngine;

namespace ScalerCore
{
    // Attached to valuables (same GO as PhysGrabObject) and enemy rigidbodies.
    //
    // For enemies: scales EnemyRigidbody GO directly (shrinks grab collider, world position
    // stays fixed since physics owns it) and scales the Anim GO separately for visuals.
    // EnemyParent is never scaled — scaling it shifts all children's world positions via
    // localPosition * parentScale, causing enemies like Robe to sink into the floor.
    //
    // Host calls DispatchShrink/DispatchExpand, RPCs to clients.
    public class ScaleController : MonoBehaviourPunCallbacks
    {
        public static readonly HashSet<ScaleController> Scaled = new();

        // Set by FootstepPitchPatch Prefix, cleared by Postfix.
        // Sound.Play Postfix reads this to pitch-shift footsteps for shrunken players.
        internal static float FootstepPitchMult = 1f;

        public Transform? ScaleTarget; // visual root to scale; null = own transform

        public Vector3 OriginalScale { get; internal set; }
        public bool    IsScaled        { get; private set; }

        // Handler resolved once in Start via ScaleHandlerRegistry.
        internal IScaleHandler? Handler;
        internal object? HandlerState;

        internal PhysGrabObject? _physGrabObject;

        // ItemEquippable.currentState is private. We check it via reflection to detect
        // equipping/equipped/unequipping states where we must yield to the inventory system.
        static readonly FieldInfo? _itemCurrentStateField =
            AccessTools.Field(typeof(ItemEquippable), "currentState");

        internal Transform _t = null!;
        internal Rigidbody? _rb;
        internal float      _originalMass;

        // RoomVolumeCheck: extraction zone detection uses currentSize for OverlapBox.
        // We scale it on shrink and restore on expand so shrunken items don't register
        // as being in the extraction zone when they physically aren't.
        RoomVolumeCheck? _roomVolumeCheck;
        Vector3 _originalRoomVolumeSize;
        bool       _isItem;       // ItemAttributes present — timer restore only, no bonk expand
        ItemEquippable? _itemEquippable; // cached — null for non-items

        // Cross-cutting item effect scaling state — managed by ItemHandler static utilities.
        internal List<ItemHandler.ScaledField>? _scaledItemFields;

        internal Vector3    _target;
        internal Vector3    _animScale;  // tracks intended scale independently of _t.localScale
        internal bool       _transitioning;
        Coroutine? _playerBounceAnim;
        float      _shrinkTimer;
        internal float _bonkImmuneTimer; // prevents the gun's own bullet from immediately restoring the target
        float      _logTimer;        // throttle periodic status logs to once/second
        internal string _displayName = ""; // enemy parent name or GO name, set in Start

        // The PhotonView used for RPCs. For players, this == photonView (same GO).
        // For enemies, photonView (GetComponent<PhotonView>()) is null because the PhotonView
        // sits on EnemyParent, not EnemyRigidbody. GetComponentInParent finds it correctly.
        internal PhotonView? _networkPV;

        // Sound pitch control — instance manages per-entity pitch state.
        internal AudioPitchHelper _audioPitch = new();

        void Awake()
        {
            _t            = transform;
            OriginalScale = transform.localScale;
            _target       = OriginalScale;
            _animScale    = OriginalScale;
        }

        void Start()
        {
            // Resolve handler via registry. Done in Start so game Awake methods have run.
            Handler = ScaleHandlerRegistry.Resolve(gameObject);
            Handler?.Setup(this);

            // If handler set ScaleTarget (e.g. EnemyHandler finds AnimTarget), adopt it.
            if (ScaleTarget != null)
            {
                _t            = ScaleTarget;
                OriginalScale = _t.localScale;
                _target       = OriginalScale;
                _animScale    = OriginalScale;
            }

            _rb = GetComponent<Rigidbody>();
            if (_rb != null) _originalMass = _rb.mass; // capture once; game code may drift rb.mass later

            // Cache PhysGrabObject for all types. EnemyHandler.Setup also sets this,
            // but valuables/items need it too for indestructibility and ForceGrabPoint.
            if (_physGrabObject == null)
                _physGrabObject = GetComponent<PhysGrabObject>();

            _roomVolumeCheck = GetComponent<RoomVolumeCheck>();
            if (_roomVolumeCheck != null) _originalRoomVolumeSize = _roomVolumeCheck.currentSize;

            // Readable name: use EnemyParent.name for enemies (GO is just "Rigidbody" otherwise)
            var epForName = GetComponentInParent<EnemyParent>();
            _displayName = epForName != null ? epForName.gameObject.name : gameObject.name;
            _isItem       = GetComponent<ItemAttributes>() != null;
            _itemEquippable = GetComponent<ItemEquippable>();

            string kind = Handler != null ? Handler.GetType().Name.Replace("Handler", "").ToLower() : "base";

            // Duplicate check: warn if another ScaleController already registered under the same EnemyParent.
            var epCheck = GetComponentInParent<EnemyParent>();
            if (epCheck != null)
            {
                int existing = 0;
                foreach (var sc in epCheck.GetComponentsInChildren<ScaleController>())
                    if (sc != this) existing++;
                if (existing > 0)
                    Plugin.Log.LogWarning($"[SC] *** DUPLICATE: {_displayName} already has {existing} other ScaleController(s) under same EnemyParent ***");
            }

            // Cache the PhotonView used for RPCs. For players the PhotonView is on the same GO;
            // for enemies it's on EnemyParent (GetComponent misses it). GetComponentInParent
            // finds both. RefreshRpcMonoBehaviourCache makes PUN2 include this ScaleController
            // (just AddComponent'd, so not in the original cache) when routing incoming RPCs.
            _networkPV = photonView ?? GetComponentInParent<PhotonView>();
            _networkPV?.RefreshRpcMonoBehaviourCache();

            // Log handler-specific info for enemies.
            var enemyState = HandlerState as EnemyHandler.State;
            Plugin.Log.LogInfo($"[SC] Registered {_displayName} ({kind})" +
                $"  scale={OriginalScale}" +
                $"  mass={(_rb != null ? _rb.mass.ToString("F2") : "none")}" +
                $"  animTarget={(enemyState?.AnimTarget != null ? enemyState.AnimTarget.gameObject.name : "NONE")}" +
                $"  navAgent={(enemyState?.NavAgent != null ? "yes" : "no")}");
        }

        void Update()
        {
            bool isHost = SemiFunc.IsMasterClientOrSingleplayer();

            // Only the host/singleplayer owns the shrink timer and bonk immunity.
            if (isHost)
            {
                if (IsScaled && _shrinkTimer > 0f)
                {
                    _shrinkTimer -= Time.deltaTime;
                    if (_shrinkTimer <= 0f)
                        DispatchExpand();
                }

                if (_bonkImmuneTimer > 0f)
                    _bonkImmuneTimer -= Time.deltaTime;

                // Handler per-frame logic (enemy mass enforcement, valuable value tracking, item orb, etc.)
                if (IsScaled && Handler != null)
                    Handler.OnUpdate(this);
            }

            // Player handler runs on all clients (grab stats, voice pitch, debug keys).
            if (!isHost && Handler is PlayerHandler)
                Handler.OnUpdate(this);

            // Periodic status log while shrunken (once per second, host only).
            // Running string formatting + log writes on every non-host client for every
            // shrunken valuable in the cart adds measurable per-frame overhead at scale.
            if (isHost && (IsScaled || _transitioning))
            {
                _logTimer -= Time.deltaTime;
                if (_logTimer <= 0f)
                {
                    _logTimer = 1f;
                    float sizeRatio    = OriginalScale.x > 0f ? _t.localScale.x / OriginalScale.x : 0f;
                    float f = ShrinkConfig.Factor;
                    float expectedMass = IsScaled ? Mathf.Clamp(_originalMass * f, 0.5f, ShrinkConfig.ShrunkMassCap) : _originalMass;
                    bool  massWrong    = _rb != null && Mathf.Abs(_rb.mass - expectedMass) > 0.001f;
                    var enemyState = HandlerState as EnemyHandler.State;
                    Plugin.Log.LogInfo($"[SC] {_displayName}" +
                        $"  size={sizeRatio * 100f:F0}%" +
                        $"  scale={_t.localScale}" +
                        $"  animTargetScale={(enemyState?.AnimTarget != null ? enemyState.AnimTarget.localScale.ToString() : "N/A")}" +
                        $"  shrinkTimer={(_shrinkTimer > 0f ? _shrinkTimer.ToString("F1") + "s" : "inf")}" +
                        $"  bonkImmune={Mathf.Max(0f, _bonkImmuneTimer):F2}s" +
                        $"  mass={(_rb != null ? _rb.mass.ToString("F3") : "N/A")}" +
                        (massWrong ? $"  *** MASS OVERRIDE (expected {expectedMass:F3}) ***" : "") +
                        $"  host={isHost}");
                }
            }

            // Scale animation and force-apply moved to LateUpdate so they always
            // override any game code (PhysGrabObject, ItemEquippable, etc.) that
            // resets transform.localScale during Update or coroutines.
        }

        // Returns true when the item is in inventory (equipping, equipped, or unequipping).
        // While in inventory, we must NOT fight the game's scale changes — the inventory
        // system shrinks the item to 1% and disables colliders.
        bool IsItemInInventory()
        {
            if (_itemEquippable == null) return false;
            if (_itemEquippable.IsEquipped()) return true;
            // Check for equipping/unequipping transitions via private currentState field.
            // Idle = 0; any other value means the inventory system is managing the item.
            if (_itemCurrentStateField != null)
                return (int)_itemCurrentStateField.GetValue(_itemEquippable) != 0;
            return false;
        }

        // LateUpdate runs after all Updates, coroutines, and Animators.
        // ALL scale application happens here so it overrides any game code
        // (PhysGrabObject, ItemEquippable, Animator) that resets localScale.
        void LateUpdate()
        {
            // While in inventory, yield to the inventory system's scale management.
            // Without this, our force-apply undoes AnimateEquip's 1% shrink, leaving
            // the item at shrunken scale with colliders disabled — a ghost that falls
            // through the floor.
            bool inInventory = IsItemInInventory();

            // --- Transition animation (non-player objects) ---
            // Uses _animScale to track progress independently of _t.localScale,
            // so game code resetting the transform between frames can't stall the animation.
            // Players use the bounce coroutine instead (it sets _t.localScale via yield).
            bool isPlayer = Handler is PlayerHandler;
            if (_transitioning && !isPlayer && !inInventory)
            {
                float speed = ShrinkConfig.Speed * OriginalScale.magnitude;
                _animScale = Vector3.MoveTowards(_animScale, _target, speed * Time.deltaTime);
                _t.localScale = _animScale;

                if (_animScale == _target)
                {
                    _transitioning = false;
                    Plugin.Log.LogInfo($"[SC] LATE_ANIM DONE  {_displayName}  finalScale={_t.localScale}");
                }
            }

            // --- Handler LateUpdate (player per-frame, enemy animTarget, etc.) ---
            Handler?.OnLateUpdate(this);

            // Items and valuables: force-apply every frame while shrunken and not animating.
            // Skip while in inventory — let the game manage the item's scale there.
            if (!isPlayer && IsScaled && !_transitioning && !inInventory)
            {
                if (_t.localScale != _target)
                    Plugin.Log.LogWarning($"[SC] LATE_FORCE  {_displayName}  was={_t.localScale}  forcing={_target}");
                _t.localScale = _target;
            }
        }

        void OnDestroy()
        {
            if (IsScaled)
            {
                _t.localScale = OriginalScale;
                Handler?.OnDestroy(this);
            }
            Scaled.Remove(this);
        }

        // --- host calls ---

        public void DispatchShrink()
        {
            Plugin.Log.LogInfo($"[SC] DispatchShrink ENTER  {_displayName}  instanceID={GetInstanceID()}  IsScaled={IsScaled}  currentScale={_t.localScale}  GO={gameObject.name}");
            if (IsScaled) return;
            IsScaled = true;
            _shrinkTimer = Handler is EnemyHandler     ? ShrinkConfig.EnemyShrinkDuration
                         : _isItem                     ? ShrinkConfig.ItemShrinkDuration
                         : Handler is PlayerHandler    ? ShrinkConfig.PlayerShrinkDuration
                                                       : ShrinkConfig.ValuableShrinkDuration;
            Scaled.Add(this);

            float f = ShrinkConfig.Factor;
            var target = OriginalScale * f;

            // Bonk immunity: at least the animation time, but no less than the configured minimum.
            float animDist    = (OriginalScale - target).magnitude;
            float animSpeed   = ShrinkConfig.Speed * OriginalScale.magnitude;
            float animTime    = animSpeed > 0f ? (animDist / animSpeed) * 1.1f : 0.75f;
            float minImmune   = Handler is EnemyHandler ? ShrinkConfig.EnemyBonkImmuneDuration
                                                        : ShrinkConfig.ValuableBonkImmuneDuration;
            _bonkImmuneTimer = Mathf.Max(animTime, minImmune);
            _logTimer = 0f; // fire status log on next frame

            ApplyScale(target);
            SetForceGrabPoint(false);

            // Scale extraction detection box so shrunken items don't register as in-zone
            if (_roomVolumeCheck != null)
                _roomVolumeCheck.currentSize = _originalRoomVolumeSize * f;

            if (_networkPV != null && PhotonNetwork.InRoom)
            {
                Plugin.Log.LogInfo($"[SC] RPC_Shrink SEND  viewID={_networkPV.ViewID}  isMine={_networkPV.IsMine}  target={target}");
                _networkPV.RPC(nameof(RPC_Shrink), RpcTarget.Others, target);
            }
            else
            {
                Plugin.Log.LogInfo($"[SC] RPC_Shrink SKIP  networkPV={(_networkPV == null ? "null" : "set")}  inRoom={PhotonNetwork.InRoom}");
            }
            AssetManager.instance?.PhysImpactEffect(_t.position);

            Plugin.Log.LogInfo($"[SC] SHRINK {_displayName}" +
                $"  factor={ShrinkConfig.Factor}" +
                $"  scale {OriginalScale} → {target}" +
                $"  animTime={animDist / (animSpeed > 0f ? animSpeed : 1f):F2}s" +
                $"  bonkImmune={_bonkImmuneTimer:F2}s" +
                $"  shrinkDuration={(_shrinkTimer > 0f ? _shrinkTimer.ToString("F0") + "s" : "infinite")}");

            if (_rb != null)
            {
                // Items: keep original mass. Enemies/valuables: clamp between 0.5 and cap.
                // The grab spring divides force by mass (PhysGrabObject line 788), so mass
                // below ~0.5 causes violent oscillation when held.
                if (!_isItem)
                    _rb.mass = Mathf.Clamp(_originalMass * f, 0.5f, ShrinkConfig.ShrunkMassCap);
                Plugin.Log.LogInfo($"[SC]   mass {_originalMass:F3} → {_rb.mass:F3}  (cap={ShrinkConfig.ShrunkMassCap:F2}  originalMass locked at {_originalMass:F3})");
            }

            // Handler-specific shrink logic (enemy nav/grab, player voice/camera, etc.)
            Handler?.OnScale(this);

            // Brief indestructibility after shrinking prevents fall damage from the
            // slight drop when colliders resize. The game's built-in indestructible
            // timer on PhysGrabObject suppresses all impact damage for the duration.
            if (_physGrabObject != null && Handler is Handlers.ValuableHandler)
                _physGrabObject.OverrideIndestructible(0.5f);

            // Pitch all Sound objects for this entity.
            var ep = GetComponentInParent<EnemyParent>();
            _audioPitch.ApplyPitch(ep != null ? (Component)ep : this);

            // Scale item-specific effect fields (explosion size, orb radius, etc.) — cross-cutting.
            _scaledItemFields = ItemHandler.OnShrinkFields(this);
        }

        public void DispatchExpand()
        {
            if (!IsScaled) return;
            IsScaled = false;
            Scaled.Remove(this);

            float sizeNow = OriginalScale.x > 0f ? _t.localScale.x / OriginalScale.x : 0f;
            Plugin.Log.LogInfo($"[SC] EXPAND (timer/shot) {_displayName}" +
                $"  currentSize={sizeNow * 100f:F0}%" +
                $"  mass {(_rb != null ? _rb.mass.ToString("F3") : "N/A")} → {_originalMass:F3}");

            ApplyScale(OriginalScale);
            SetForceGrabPoint(true);
            if (_networkPV != null && PhotonNetwork.InRoom)
                _networkPV.RPC(nameof(RPC_Expand), RpcTarget.Others);
            AssetManager.instance?.PhysImpactEffect(_t.position);
            SemiFunc.CameraShakeImpactDistance(_t.position, 2f, 0.1f, 1f, 8f);

            if (_rb != null) _rb.mass = _originalMass;
            if (_roomVolumeCheck != null) _roomVolumeCheck.currentSize = _originalRoomVolumeSize;

            Handler?.OnRestore(this, isBonk: false);

            _audioPitch.RestorePitch();
            ItemHandler.OnRestoreFields(_scaledItemFields);
            _scaledItemFields = null;
        }

        // Instant restore — no animation. Used for bonk.
        public void DispatchExpandNow()
        {
            if (!IsScaled) return;
            if (_bonkImmuneTimer > 0f)
            {
                Plugin.Log.LogInfo($"[SC] BONK BLOCKED {_displayName}  immune={_bonkImmuneTimer:F2}s remaining");
                return;
            }
            IsScaled = false;
            _shrinkTimer = 0f;
            Scaled.Remove(this);

            float sizeNow = OriginalScale.x > 0f ? _t.localScale.x / OriginalScale.x : 0f;
            Plugin.Log.LogInfo($"[SC] EXPAND (bonk/instant) {_displayName}" +
                $"  currentSize={sizeNow * 100f:F0}%" +
                $"  mass {(_rb != null ? _rb.mass.ToString("F3") : "N/A")} → {_originalMass:F3}");

            ApplyScale(OriginalScale);
            SetForceGrabPoint(true);

            if (_networkPV != null && PhotonNetwork.InRoom)
                _networkPV.RPC(nameof(RPC_Expand), RpcTarget.Others);
            AssetManager.instance?.PhysImpactEffect(_t.position);
            SemiFunc.CameraShakeImpactDistance(_t.position, 2f, 0.1f, 1f, 8f);

            if (_rb != null) _rb.mass = _originalMass;
            if (_roomVolumeCheck != null) _roomVolumeCheck.currentSize = _originalRoomVolumeSize;

            Handler?.OnRestore(this, isBonk: true);

            _audioPitch.RestorePitch();
            ItemHandler.OnRestoreFields(_scaledItemFields);
            _scaledItemFields = null;
        }


        void ApplyScale(Vector3 target)
        {
            _target        = target;
            _animScale     = _t.localScale; // snapshot current scale for animation
            _transitioning = true;
            if (Handler is PlayerHandler)
            {
                if (_playerBounceAnim != null) StopCoroutine(_playerBounceAnim);
                _playerBounceAnim = StartCoroutine(PlayerBounceAnim(_t.localScale, target));
            }
        }

        // Melee weapons use a forceGrabPoint child to position the item in-hand.
        // The grab spring uses a hardcoded 1-unit distance which doesn't account for
        // shrunken scale, causing violent oscillation. Deactivating the GO makes
        // PhysGrabber fall through to normal grab positioning (which works fine).
        void SetForceGrabPoint(bool active)
        {
            if (_physGrabObject != null && _physGrabObject.forceGrabPoint != null)
                _physGrabObject.forceGrabPoint.gameObject.SetActive(active);
        }

        // Back-out easing: starts at 0, overshoots ~10% past 1.0, then settles at 1.0.
        // Applied to LerpUnclamped so the scale briefly passes the target before bouncing back.
        static float BackOutEase(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        System.Collections.IEnumerator PlayerBounceAnim(Vector3 from, Vector3 to)
        {
            float duration = 0.4f;
            float elapsed  = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float easedT = BackOutEase(Mathf.Clamp01(elapsed / duration));
                _t.localScale = Vector3.LerpUnclamped(from, to, easedT);
                yield return null;
            }
            _t.localScale     = to;
            _transitioning    = false;
            _playerBounceAnim = null;
        }

        // --- client receivers ---
        // These run on non-host clients. They mirror the host's IsScaled/Scaled state so
        // pitch and any client-side logic that checks IsScaled work correctly.

        [PunRPC]
        void RPC_Shrink(Vector3 target)
        {
            Plugin.Log.LogInfo($"[SC] RPC_Shrink RECV  {_displayName}  target={target}");
            IsScaled = true;
            Scaled.Add(this);
            if (_rb != null && !_isItem) _rb.mass = Mathf.Clamp(_originalMass * ShrinkConfig.Factor, 0.5f, ShrinkConfig.ShrunkMassCap);
            if (_roomVolumeCheck != null) _roomVolumeCheck.currentSize = _originalRoomVolumeSize * ShrinkConfig.Factor;
            ApplyScale(target);
            SetForceGrabPoint(false);
            AssetManager.instance?.PhysImpactEffect(_t.position);
            var ep = GetComponentInParent<EnemyParent>();
            _audioPitch.ApplyPitch(ep != null ? (Component)ep : this);
            _scaledItemFields = ItemHandler.OnShrinkFields(this);

            // Handler-specific client-side shrink (player voice/camera, etc.)
            Handler?.OnScale(this);

            // Match host-side indestructibility for valuables.
            if (_physGrabObject != null && Handler is Handlers.ValuableHandler)
                _physGrabObject.OverrideIndestructible(0.5f);
        }

        [PunRPC]
        void RPC_Expand()
        {
            IsScaled = false;
            Scaled.Remove(this);
            if (_rb != null) _rb.mass = _originalMass;
            if (_roomVolumeCheck != null) _roomVolumeCheck.currentSize = _originalRoomVolumeSize;
            ApplyScale(OriginalScale);
            SetForceGrabPoint(true);
            AssetManager.instance?.PhysImpactEffect(_t.position);
            SemiFunc.CameraShakeImpactDistance(_t.position, 2f, 0.1f, 1f, 8f);
            _audioPitch.RestorePitch();
            ItemHandler.OnRestoreFields(_scaledItemFields);
            _scaledItemFields = null;

            // Handler-specific client-side restore (player voice/camera, etc.)
            Handler?.OnRestore(this, isBonk: false);
        }

        [PunRPC]
        void RPC_PlayerPitchCancel()
        {
            var state = HandlerState as PlayerHandler.State;
            state?.PlayerAvatar.voiceChat?.OverridePitchCancel();
        }

        // --- client-to-host expand requests ---

        // Called by PlayerBonkPatch when ANY client detects local player damage while shrunken.
        // Host processes directly; non-host sends RPC to host.
        public void RequestBonkExpand()
        {
            if (!IsScaled) return;
            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                DispatchExpandNow();
            }
            else if (_networkPV != null && _networkPV.IsMine && PhotonNetwork.InRoom)
            {
                _networkPV.RPC(nameof(RPC_RequestExpand), RpcTarget.MasterClient, true);
            }
        }

        // Called when the local player presses F10 to manually unshrink.
        // Skips bonk immunity — manual input should always work.
        public void RequestManualShrink()
        {
            if (IsScaled) return;
            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                DispatchShrink();
            }
            else if (_networkPV != null && _networkPV.IsMine && PhotonNetwork.InRoom)
            {
                _networkPV.RPC(nameof(RPC_RequestShrink), RpcTarget.MasterClient);
            }
        }

        [PunRPC]
        void RPC_RequestShrink()
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (IsScaled) return;
            DispatchShrink();
        }

        public void RequestManualExpand()
        {
            if (!IsScaled) return;
            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                DispatchExpand();
            }
            else if (_networkPV != null && _networkPV.IsMine && PhotonNetwork.InRoom)
            {
                _networkPV.RPC(nameof(RPC_RequestExpand), RpcTarget.MasterClient, false);
            }
        }

        [PunRPC]
        void RPC_RequestExpand(bool checkImmunity)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!IsScaled) return;
            if (checkImmunity)
                DispatchExpandNow(); // respects _bonkImmuneTimer
            else
                DispatchExpand();    // no immunity check — manual request
        }

        // --- level/extraction cleanup ---

        // Properly restores all shrunken players before clearing the set.
        // Scene objects (enemies, valuables) are destroyed on level change; players persist.
        public static void CleanupAll()
        {
            foreach (var ctrl in Scaled)
            {
                if (ctrl == null) continue;
                ctrl._audioPitch.RestorePitch();
                ItemHandler.OnRestoreFields(ctrl._scaledItemFields);
                ctrl._scaledItemFields = null;

                // Handler-specific cleanup.
                ctrl.Handler?.OnRestore(ctrl, isBonk: false);

                ctrl.IsScaled = false;
                ctrl._transitioning = false;
                ctrl._target    = ctrl.OriginalScale;
                ctrl._animScale = ctrl.OriginalScale;
                ctrl._t.localScale = ctrl.OriginalScale;
            }
            Scaled.Clear();
        }

        // Re-register after joining so Photon's internal initialization (which may rebuild the
        // MonoBehaviour cache after Awake/Start) doesn't lose us.
        public override void OnJoinedRoom()
        {
            _networkPV?.RefreshRpcMonoBehaviourCache();
        }

        // Late-join sync: when a new player enters the room, the host re-sends the current
        // shrink state for every shrunken object so the joining client sees correct state.
        // Only fires on the host; each ScaleController handles its own photonView RPC.
        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!IsScaled) return;
            if (_networkPV == null) return;
            _networkPV.RPC(nameof(RPC_Shrink), newPlayer, _target);
        }
    }
}
