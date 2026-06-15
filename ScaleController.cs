using System.Collections.Generic;
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
    // EnemyParent is never scaled, scaling it shifts all children's world positions via
    // localPosition * parentScale, causing enemies like Robe to sink into the floor.
    //
    // Host calls DispatchShrink/DispatchExpand, RPCs to clients.
    public class ScaleController : MonoBehaviourPunCallbacks
    {
        public static readonly HashSet<ScaleController> Scaled = [];

        // Set by FootstepPitchPatch Prefix, cleared by Postfix.
        // Sound.Play Postfix reads this to pitch-shift footsteps for shrunken players.
        internal static float FootstepPitchMult = 1f;

        // Gate for manual shrink/expand requests (F9/F10 debug keys).
        // Consuming mods set this to control whether debug keys are allowed.
        // Checked on the host when processing RPC requests.
        public static bool AllowManualScale = true;

        // Challenge mode: players start shrunken, guns grow them, damage re-shrinks.
        // Set by consuming mods (e.g. ShrinkerGun) to enable challenge gameplay.
        public static bool ChallengeMode { get; set; }

        public Transform? ScaleTarget; // visual root to scale; null = own transform

        public Vector3 OriginalScale { get; internal set; }
        public bool    IsScaled        { get; private set; }

        /// <summary>Snapshot of the active session's options. Read-only.</summary>
        public ScaleOptions CurrentOptions => _options;

        /// <summary>
        /// What kind of object this controller is on (player, enemy, item, valuable).
        /// </summary>
        public ScaleTargets TargetType =>
            Handler is Handlers.PlayerHandler   ? ScaleTargets.Players   :
            Handler is Handlers.EnemyHandler     ? ScaleTargets.Enemies   :
            Handler is Handlers.ItemHandler      ? ScaleTargets.Items     :
            Handler is Handlers.VehicleHandler   ? ScaleTargets.Items     :
                                                   ScaleTargets.Valuables;

        // Handler resolved once in Start via ScaleHandlerRegistry.
        internal IScaleHandler? Handler;
        internal object? HandlerState;

        // When true, the handler owns all scaling, controller won't touch _t.localScale.
        // Available for handlers that need to scale children individually.
        internal bool HandlerOwnsScale = false;

        internal PhysGrabObject? _physGrabObject;

        internal Transform _t = null!;
        internal Rigidbody? _rb;
        internal float      _originalMass;

        // RoomVolumeCheck: extraction zone detection feeds Physics.OverlapBox a box at
        //   center = transform.position + transform.rotation * CheckPosition
        //   size   = currentSize
        // We scale BOTH on shrink and restore on expand so shrunken items don't register
        // as being in the extraction zone when they physically aren't. Scaling currentSize
        // alone shrinks the box but leaves it offset by the full CheckPosition: the probe
        // drifts off the shrunken body and can overhang the zone. CheckPosition has to
        // scale by the same factor to stay centered on the body.
        RoomVolumeCheck? _roomVolumeCheck;
        Vector3 _originalRoomVolumeSize;
        Vector3 _originalCheckPosition;
        bool       _isItem;       // ItemAttributes present, timer restore only, no bonk expand
        internal ItemEquippable? _itemEquippable; // cached, null for non-items

        // Cross-cutting item effect scaling state, managed by ItemHandler static utilities.
        internal List<ItemHandler.ScaledField>? _scaledItemFields;

        // True when this controller was shrunk with InvertedMode.
        // Stays set across expand/shrink so bonk knows to re-shrink.
        internal bool _invertedActive;

        internal Vector3    _target;
        internal Vector3    _animScale;  // tracks intended scale independently of _t.localScale
        internal bool       _transitioning;
        // Animation speed for the current transition. Set on each dispatch; lets shrink and
        // expand use different speeds via RestoreSpeed.
        internal float      _currentAnimSpeed = ScaleOptions.Default.Speed;
        Coroutine? _playerBounceAnim;
        float      _shrinkTimer;
        internal ScaleOptions _options;
        internal float _bonkImmuneTimer; // prevents the gun's own bullet from immediately restoring the target
        internal string _displayName = ""; // enemy parent name or GO name, set in Start

        // The PhotonView used for RPCs. For players, this == photonView (same GO).
        // For enemies, photonView (GetComponent<PhotonView>()) is null because the PhotonView
        // sits on EnemyParent, not EnemyRigidbody. GetComponentInParent finds it correctly.
        internal PhotonView? _networkPV;

        // Sound pitch control, instance manages per-entity pitch state.
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
            if (_roomVolumeCheck != null)
            {
                _originalRoomVolumeSize = _roomVolumeCheck.currentSize;
                _originalCheckPosition = _roomVolumeCheck.CheckPosition;
            }

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
            Plugin.Log.LogDebug($"[SC] Registered {_displayName} ({kind})" +
                $"  scale={OriginalScale}" +
                $"  mass={(_rb != null ? _rb.mass.ToString("F2") : "none")}" +
                $"  animTarget={(enemyState?.AnimTarget != null ? enemyState.AnimTarget.gameObject.name : "NONE")}" +
                $"  navAgent={(enemyState?.NavAgent != null ? "yes" : "no")}");

            // Voice pitch in menu lobby: apply or cancel depending on challenge mode.
            // Deferred because remote PlayerVoiceChat components may not exist yet at Start.
            if (Handler is PlayerHandler && SemiFunc.RunIsLobbyMenu())
                StartCoroutine(LobbyPitchDeferred());

            // Challenge mode: auto-shrink players during actual runs.
            // Deferred via coroutine because voiceChat and Photon aren't ready at Start time.
            // Only skip the menu lobby, the truck lobby counts as a run.
            if (ChallengeMode && Handler is PlayerHandler
                && !SemiFunc.RunIsLobbyMenu())
            {
                StartCoroutine(ChallengeModeDeferred());
            }
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

            // Player handler runs on all clients (grab stats, voice pitch, etc.).
            if (!isHost && IsScaled && Handler is PlayerHandler)
                Handler.OnUpdate(this);

            // Diagnostic: runs on host AND client for valuables so we can correlate
            // weight-not-reducing reports across both sides. Self-throttling inside.
            if (IsScaled && Handler is ValuableHandler)
                ValuableHandler.OnDiagnoseMass(this, isHost);

            // Scale animation and force-apply moved to LateUpdate so they always
            // override any game code (PhysGrabObject, ItemEquippable, etc.) that
            // resets transform.localScale during Update or coroutines.
        }

        // Returns true when the item is in inventory (equipping, equipped, or unequipping).
        // While in inventory, we must NOT fight the game's scale changes, the inventory
        // system shrinks the item to 1% and disables colliders.
        bool IsItemInInventory()
        {
            if (_itemEquippable == null) return false;
            if (_itemEquippable.IsEquipped()) return true;
            // Idle = 0; any other value means the inventory system is managing the item.
            return (int)_itemEquippable.currentState != 0;
        }

        // LateUpdate runs after all Updates, coroutines, and Animators.
        // ALL scale application happens here so it overrides any game code
        // (PhysGrabObject, ItemEquippable, Animator) that resets localScale.
        void LateUpdate()
        {
            // While in inventory, yield to the inventory system's scale management.
            // Without this, our force-apply undoes AnimateEquip's 1% shrink, leaving
            // the item at shrunken scale with colliders disabled, a ghost that falls
            // through the floor.
            bool inInventory = IsItemInInventory();

            // --- Transition animation (non-player objects) ---
            // Uses _animScale to track progress independently of _t.localScale,
            // so game code resetting the transform between frames can't stall the animation.
            // Players use the bounce coroutine instead (it sets _t.localScale via yield).
            bool isPlayer = Handler is PlayerHandler;
            if (_transitioning && !isPlayer && !inInventory)
            {
                float speed = _currentAnimSpeed * OriginalScale.magnitude;
                _animScale = Vector3.MoveTowards(_animScale, _target, speed * Time.deltaTime);
                if (!HandlerOwnsScale)
                    _t.localScale = ClampPhysical(_animScale);

                if (_animScale == _target)
                {
                    _transitioning = false;
                    Plugin.Log.LogDebug($"[SC] LATE_ANIM DONE  {_displayName}  finalScale={_animScale}");
                }
            }

            // --- Handler LateUpdate (player per-frame, enemy animTarget, etc.) ---
            Handler?.OnLateUpdate(this);

            // Items and valuables: force-apply every frame while shrunken and not animating.
            // Skip while in inventory, let the game manage the item's scale there.
            // Skip when handler owns scaling (e.g. doors scale children individually).
            if (!isPlayer && !HandlerOwnsScale && IsScaled && !_transitioning && !inInventory)
            {
                Vector3 physTarget = ClampPhysical(_target);
                if (_t.localScale != physTarget)
                    Plugin.Log.LogDebug($"[SC] LATE_FORCE  {_displayName}  was={_t.localScale}  forcing={physTarget}");
                _t.localScale = physTarget;
            }
        }

        // --- enemy grow split: physical ceiling ---
        // Visuals (and reach, audio, mass) keep climbing to Factor; the collider
        // scale and nav agent radius stop at EnemyPhysicalFactorCap so a giant
        // still fits through the doorways the navmesh was baked for. Grow-only:
        // never engages while shrinking, never for non-enemies.
        internal bool PhysicallyCapped =>
            Handler is EnemyHandler
            && _options.EnemyPhysicalFactorCap > 0f
            && _options.Factor > 1f
            && _options.Factor > _options.EnemyPhysicalFactorCap;

        internal float PhysicalFactor =>
            PhysicallyCapped ? Mathf.Max(1f, _options.EnemyPhysicalFactorCap) : _options.Factor;

        Vector3 ClampPhysical(Vector3 intended)
        {
            if (!PhysicallyCapped || OriginalScale.x <= 0f) return intended;
            Vector3 cap = OriginalScale * Mathf.Max(1f, _options.EnemyPhysicalFactorCap);
            return new Vector3(
                Mathf.Min(intended.x, cap.x),
                Mathf.Min(intended.y, cap.y),
                Mathf.Min(intended.z, cap.z));
        }

        // PhysGrabObject keeps its own massOriginal and ResetMass() writes it
        // back to rb.mass whenever an alter-mass episode ends, silently undoing
        // the scaled mass mid-session (why some grown valuables stopped feeling
        // heavy while others never hit that path and stayed heavy). Scale
        // massOriginal alongside rb.mass so the game's own resets land on the
        // scaled value; the vanilla number goes back at expand.
        float _pgoVanillaMassOriginal;

        void ScalePgoMassOriginal(float f)
        {
            if (_physGrabObject == null || _isItem || _options.PreserveMass) return;
            if (_pgoVanillaMassOriginal == 0f)
                _pgoVanillaMassOriginal = _physGrabObject.massOriginal > 0f
                    ? _physGrabObject.massOriginal
                    : _originalMass;
            if (_pgoVanillaMassOriginal <= 0f) return;
            _physGrabObject.massOriginal = Mathf.Clamp(_pgoVanillaMassOriginal * f, 0.5f, _options.MassCap);
        }

        void RestorePgoMassOriginal()
        {
            if (_physGrabObject != null && _pgoVanillaMassOriginal > 0f)
                _physGrabObject.massOriginal = _pgoVanillaMassOriginal;
            _pgoVanillaMassOriginal = 0f;
        }

        // Enemies despawn via SetActive(false), the same GO is re-enabled on respawn.
        // Restore scale here so respawned enemies aren't still shrunken.
        public override void OnDisable()
        {
            base.OnDisable();
            if (!IsScaled || Handler is not Handlers.EnemyHandler) return;

            Plugin.Log.LogDebug($"[SC] DISABLE (despawn) {_displayName}  restoring scale");

            _t.localScale  = OriginalScale;
            _animScale     = OriginalScale;
            _target        = OriginalScale;
            _transitioning = false;
            IsScaled       = false;
            _invertedActive = false;
            _shrinkTimer   = 0f;
            Scaled.Remove(this);

            if (_rb != null) _rb.mass = _originalMass;
            RestorePgoMassOriginal();
            if (_roomVolumeCheck != null)
            {
                _roomVolumeCheck.currentSize = _originalRoomVolumeSize;
                _roomVolumeCheck.CheckPosition = _originalCheckPosition;
            }

            Handler?.OnRestore(this, isBonk: false);
            _audioPitch.RestorePitch();
            ItemHandler.OnRestoreFields(_scaledItemFields);
            _scaledItemFields = null;
        }

        void OnDestroy()
        {
            if (IsScaled)
            {
                _t.localScale = OriginalScale;
                // Cancel voice pitch so it doesn't persist on the PlayerVoiceChat
                // component, which survives level changes.
                var playerState = HandlerState as Handlers.PlayerHandler.State;
                playerState?.PlayerAvatar.voiceChat?.OverridePitchCancel();
                Handler?.OnDestroy(this);
            }
            Scaled.Remove(this);
        }

        // --- host calls ---

        public void DispatchShrink(ScaleOptions options)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!ScaleManager.AllowDeadHeads && GetComponent<PlayerDeathHead>() != null)
            {
                Plugin.Log.LogDebug($"[SC] DispatchShrink ignored: {_displayName} is a dead Semibot head and ScaleManager.AllowDeadHeads is off");
                return;
            }

            Plugin.Log.LogDebug($"[SC] DispatchShrink ENTER  {_displayName}  instanceID={GetInstanceID()}  IsScaled={IsScaled}  currentScale={_t.localScale}  GO={gameObject.name}");
            // Hitch tracing: the whole apply runs in one frame, so if it ever
            // costs real milliseconds the player sees a freeze-frame. Warn with
            // the object name so slow targets can be reported and broken down
            var swApply = System.Diagnostics.Stopwatch.StartNew();
            void WarnIfSlow(string path)
            {
                swApply.Stop();
                if (swApply.ElapsedMilliseconds >= 10)
                    Plugin.Log.LogWarning($"[SC] slow scale apply ({path}): {swApply.ElapsedMilliseconds}ms on {_displayName}");
            }
            if (IsScaled)
            {
                // Same factor → toggle (restore). Different factor → rescale in place.
                if (Mathf.Approximately(options.Factor, _options.Factor))
                {
                    DispatchExpand();
                    // Inverted: set fresh bonk immunity so the gun's own damage
                    // doesn't immediately trigger re-shrink via PlayerBonkPatch.
                    if (_invertedActive)
                        _bonkImmuneTimer = _options.BonkImmuneDuration;
                    return;
                }
                // Different factor: update options and animate to the new target without restoring first.
                Plugin.Log.LogDebug($"[SC] RESCALE {_displayName}  {_options.Factor} → {options.Factor}");
                _options = options;
                _shrinkTimer = _options.Duration;
                if (_shrinkTimer < 0f) _shrinkTimer = 0f;
                float rf = _options.Factor;
                var newTarget = OriginalScale * rf;
                _bonkImmuneTimer = _options.BonkImmuneDuration;
                _currentAnimSpeed = _options.Speed;
                ApplyScale(newTarget);
                if (_roomVolumeCheck != null)
                {
                    _roomVolumeCheck.currentSize = _originalRoomVolumeSize * rf;
                    _roomVolumeCheck.CheckPosition = _originalCheckPosition * rf;
                }
                if (_rb != null && !_isItem && !_options.PreserveMass)
                    _rb.mass = Mathf.Clamp(_originalMass * rf, 0.5f, _options.MassCap);
                ScalePgoMassOriginal(rf);

                // A rescale changes factor mid-session, so the per-session
                // treatments the fresh path applies below must follow it too.
                // Without these, a grown object re-shot as shrunken keeps its
                // giant audio treatment and its disabled-when-shrinking grab
                // point never updates, so it can't be picked up
                SetForceGrabPoint(rf >= 1f);
                if (!_options.SuppressVoicePitch)
                {
                    var ep = GetComponentInParent<EnemyParent>();
                    _audioPitch.ApplyPitch(ep != null ? (Component)ep : this, rf, _options.AudioPresence);
                }
                ItemHandler.OnRestoreFields(_scaledItemFields);
                _scaledItemFields = ItemHandler.OnShrinkFields(this, rf);

                if (_networkPV != null && PhotonNetwork.InRoom)
                    _networkPV.RPC(nameof(RPC_Shrink), RpcTarget.Others, newTarget, PackOpts(), PackBools());
                WarnIfSlow("rescale");
                return;
            }
            // Guard against bare `new ScaleOptions()` (all zeroes), fall back to defaults for critical fields.
            if (options.Factor <= 0f) options.Factor = ScaleOptions.Default.Factor;
            if (options.Speed  <= 0f) options.Speed  = ScaleOptions.Default.Speed;
            _options = options;
            _invertedActive = _options.InvertedMode;
            IsScaled = true;
            _shrinkTimer = _options.Duration;
            if (_shrinkTimer < 0f) _shrinkTimer = 0f;
            _currentAnimSpeed = _options.Speed;

            Scaled.Add(this);

            float f = _options.Factor;
            var target = OriginalScale * f;

            // Bonk immunity: at least the animation time, but no less than the configured minimum.
            float animDist    = (OriginalScale - target).magnitude;
            float animSpeed   = _options.Speed * OriginalScale.magnitude;
            float animTime    = animSpeed > 0f ? (animDist / animSpeed) * 1.1f : 0.75f;
            _bonkImmuneTimer = Mathf.Max(animTime, _options.BonkImmuneDuration);

            ApplyScale(target);

            // Only disable ForceGrabPoint when shrinking, enlarged items don't need it.
            if (f < 1f) SetForceGrabPoint(false);

            // Scale extraction detection box (size and the offset to its center)
            if (_roomVolumeCheck != null)
            {
                _roomVolumeCheck.currentSize = _originalRoomVolumeSize * f;
                _roomVolumeCheck.CheckPosition = _originalCheckPosition * f;
            }

            if (_networkPV != null && PhotonNetwork.InRoom)
            {
                Plugin.Log.LogDebug($"[SC] RPC_Shrink SEND  {_displayName}  viewID={_networkPV.ViewID}  isMine={_networkPV.IsMine}  target={target}");
                _networkPV.RPC(nameof(RPC_Shrink), RpcTarget.Others, target, PackOpts(), PackBools());
            }
            else
            {
                Plugin.Log.LogDebug($"[SC] RPC_Shrink SKIP  {_displayName}  networkPV={(_networkPV == null ? "null" : "set")}  inRoom={PhotonNetwork.InRoom}");
            }
            PlayImpactEffect();

            Plugin.Log.LogDebug($"[SC] SHRINK {_displayName}" +
                $"  factor={_options.Factor}" +
                $"  scale {OriginalScale} → {target}" +
                $"  animTime={animDist / (animSpeed > 0f ? animSpeed : 1f):F2}s" +
                $"  bonkImmune={_bonkImmuneTimer:F2}s" +
                $"  shrinkDuration={(_shrinkTimer > 0f ? _shrinkTimer.ToString("F0") + "s" : "infinite")}");

            if (_rb != null)
            {
                // Items: keep original mass. Enemies/valuables: clamp between 0.5 and cap.
                // The grab spring divides force by mass (PhysGrabObject line 788), so mass
                // below ~0.5 causes violent oscillation when held.
                float wantRaw = _originalMass * f;
                if (!_isItem && !_options.PreserveMass)
                    _rb.mass = Mathf.Clamp(wantRaw, 0.5f, _options.MassCap);
                bool clamped = !_isItem && !_options.PreserveMass && wantRaw < 0.5f;
                if (Plugin.DiagMass)
                Plugin.Log.LogDebug(
                    $"[SC-DIAG][HOST] SHRINK_APPLY  {_displayName}  " +
                    $"mass {_originalMass:F3} → {_rb.mass:F3}  wantRaw={wantRaw:F3}  cap={_options.MassCap:F2}" +
                    (clamped ? "  *FLOOR_HIT*" : "") +
                    (_isItem ? "  (item, mass untouched)" : "") +
                    (_options.PreserveMass ? "  (PreserveMass, mass untouched)" : "") +
                    (_physGrabObject != null
                        ? $"  pgo.massOrig={_physGrabObject.massOriginal:F3}  pgo.timerAlter={_physGrabObject.timerAlterMass:F2}"
                        : ""));
            }
            ScalePgoMassOriginal(f);

            // Handler-specific shrink logic (enemy nav/grab, player voice/camera, etc.)
            Handler?.OnScale(this);

            // Brief indestructibility after shrinking prevents fall damage from the
            // slight drop when colliders resize. Only needed when shrinking.
            if (f < 1f && _physGrabObject != null && Handler is Handlers.ValuableHandler)
                _physGrabObject.OverrideIndestructible(0.5f);

            // Pitch all Sound objects for this entity (unless suppressed for this session).
            if (!_options.SuppressVoicePitch)
            {
                var ep = GetComponentInParent<EnemyParent>();
                _audioPitch.ApplyPitch(ep != null ? (Component)ep : this, _options.Factor, _options.AudioPresence);
            }

            // Scale item-specific effect fields (explosion size, orb radius, etc.), cross-cutting.
            _scaledItemFields = ItemHandler.OnShrinkFields(this, _options.Factor);

            // Shrink the map icon to match.
            ScaleMapIcon(f);
            WarnIfSlow("fresh");
        }

        public void DispatchExpand()
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!IsScaled) return;
            IsScaled = false;
            Scaled.Remove(this);

            float sizeNow = OriginalScale.x > 0f ? _t.localScale.x / OriginalScale.x : 0f;
            Plugin.Log.LogDebug($"[SC] EXPAND (timer/shot) {_displayName}" +
                $"  currentSize={sizeNow * 100f:F0}%" +
                $"  mass {(_rb != null ? _rb.mass.ToString("F3") : "N/A")} → {_originalMass:F3}");

            _currentAnimSpeed = ResolveExpandSpeed();
            ApplyScale(OriginalScale);
            SetForceGrabPoint(true);
            if (_networkPV != null && PhotonNetwork.InRoom)
                _networkPV.RPC(nameof(RPC_Expand), RpcTarget.Others);
            PlayImpactEffect();
            PlayCameraShake();

            if (_rb != null) _rb.mass = _originalMass;
            RestorePgoMassOriginal();
            if (_roomVolumeCheck != null)
            {
                _roomVolumeCheck.currentSize = _originalRoomVolumeSize;
                _roomVolumeCheck.CheckPosition = _originalCheckPosition;
            }

            Handler?.OnRestore(this, isBonk: false);

            if (!_options.SuppressVoicePitch) _audioPitch.RestorePitch();
            ItemHandler.OnRestoreFields(_scaledItemFields);
            _scaledItemFields = null;
            ScaleMapIcon(1f);
        }

        // Instant restore, no animation. Used for bonk (player/valuable/enemy/cosmetic damage).
        // Skipped when the session set IgnoreBonkExpand.
        public void DispatchExpandNow()
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!IsScaled) return;
            if (_options.IgnoreBonkExpand) return;
            if (_bonkImmuneTimer > 0f)
            {
                Plugin.Log.LogDebug($"[SC] BONK BLOCKED {_displayName}  immune={_bonkImmuneTimer:F2}s remaining");
                return;
            }
            IsScaled = false;
            _shrinkTimer = 0f;
            Scaled.Remove(this);

            float sizeNow = OriginalScale.x > 0f ? _t.localScale.x / OriginalScale.x : 0f;
            Plugin.Log.LogDebug($"[SC] EXPAND (bonk/instant) {_displayName}" +
                $"  currentSize={sizeNow * 100f:F0}%" +
                $"  mass {(_rb != null ? _rb.mass.ToString("F3") : "N/A")} → {_originalMass:F3}");

            _currentAnimSpeed = ResolveExpandSpeed();
            ApplyScale(OriginalScale);
            SetForceGrabPoint(true);

            if (_networkPV != null && PhotonNetwork.InRoom)
                _networkPV.RPC(nameof(RPC_Expand), RpcTarget.Others);
            PlayImpactEffect();
            PlayCameraShake();

            if (_rb != null) _rb.mass = _originalMass;
            RestorePgoMassOriginal();
            if (_roomVolumeCheck != null)
            {
                _roomVolumeCheck.currentSize = _originalRoomVolumeSize;
                _roomVolumeCheck.CheckPosition = _originalCheckPosition;
            }

            Handler?.OnRestore(this, isBonk: true);

            if (!_options.SuppressVoicePitch) _audioPitch.RestorePitch();
            ItemHandler.OnRestoreFields(_scaledItemFields);
            _scaledItemFields = null;
            ScaleMapIcon(1f);
        }


        void ApplyScale(Vector3 target)
        {
            _target        = target;
            // Snapshot current scale for animation. Under a physical cap the
            // transform sits BELOW the intended scale and _animScale already
            // tracks the intended value; restarting from the capped transform
            // would snap the visuals down at the start of the next transition
            if (!PhysicallyCapped)
                _animScale = _t.localScale;
            _transitioning = true;
            if (Handler is PlayerHandler)
            {
                if (_playerBounceAnim != null) StopCoroutine(_playerBounceAnim);
                _playerBounceAnim = StartCoroutine(PlayerBounceAnim(_t.localScale, target));
            }
        }

        void PlayImpactEffect()
        {
            if (_options.SuppressImpactFlash) return;
            AssetManager.instance?.PhysImpactEffect(_t.position);
        }

        void PlayCameraShake()
        {
            if (_options.SuppressCameraShake) return;
            SemiFunc.CameraShakeImpactDistance(_t.position, 2f, 0.1f, 1f, 8f);
        }

        float ResolveExpandSpeed() =>
            _options.RestoreSpeed > 0f ? _options.RestoreSpeed : _options.Speed;

        // Scale the map icon dot to match the shrunken size.
        // factor=1 restores to original, factor<1 shrinks the dot.
        void ScaleMapIcon(float factor)
        {
            var mapCustom = GetComponent<MapCustom>();
            if (mapCustom == null) mapCustom = GetComponentInParent<MapCustom>();
            if (mapCustom?.mapCustomEntity != null)
                mapCustom.mapCustomEntity.transform.localScale = Vector3.one * Mathf.Max(factor, 0.3f);
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

        System.Collections.IEnumerator ChallengeModeDeferred()
        {
            // Wait for level generation. In multiplayer, also wait for voice chat
            // so the pitch override works. Singleplayer has no voice chat.
            while (LevelGenerator.Instance == null || !LevelGenerator.Instance.Generated)
                yield return null;
            if (SemiFunc.IsMultiplayer())
            {
                var playerState = HandlerState as Handlers.PlayerHandler.State;
                while (playerState?.PlayerAvatar.voiceChat == null)
                    yield return null;
            }

            if (!IsScaled && SemiFunc.IsMasterClientOrSingleplayer())
            {
                var opts = ScaleOptions.Default;
                opts.InvertedMode = true;
                opts.Duration = 0f;
                DispatchShrink(opts);
            }
        }

        System.Collections.IEnumerator LobbyPitchDeferred()
        {
            // Wait a bit for all PlayerVoiceChat components to be created and linked.
            for (int i = 0; i < 10; i++)
                yield return null;

            foreach (var vc in Object.FindObjectsOfType<PlayerVoiceChat>())
            {
                if (ChallengeMode)
                    vc.OverridePitch(1.3f, 0.2f, 0.5f, 9999f);
                else
                    vc.OverridePitchCancel();
            }
        }

        // --- client receivers ---
        // These run on non-host clients. They mirror the host's IsScaled/Scaled state so
        // pitch and any client-side logic that checks IsScaled work correctly.

        float[] PackOpts() => new[] {
            _options.Factor, _options.Speed, _options.MassCap,
            _options.SpeedFactor, _options.AnimSpeedMultiplier,
            _options.FootstepPitchMultiplier, _options.BonkImmuneDuration,
            _options.RestoreSpeed, _options.AudioPresence,
            _options.EnemyPhysicalFactorCap
        };

        bool[] PackBools() => new[] {
            _options.PreserveMass, _options.InvertedMode, _options.SuppressImpactFlash,
            _options.SuppressVoicePitch, _options.IgnoreBonkExpand, _options.RejectExternalApply,
            _options.SuppressCameraShake
        };

        [PunRPC]
        void RPC_Shrink(Vector3 target, float[] opts, bool[] flags, PhotonMessageInfo info = default)
        {
            // Scale state only ever flows host -> clients; drop spoofed sends.
            if (PhotonNetwork.InRoom && (info.Sender == null || info.Sender != PhotonNetwork.MasterClient)) return;
            Plugin.Log.LogDebug($"[SC] RPC_Shrink RECV  {_displayName}  target={target}  factor={opts[0]}  speed={opts[1]}  handler={Handler?.GetType().Name ?? "null"}");
            _options.Factor                = opts[0];
            _options.Speed                 = opts[1];
            _options.MassCap               = opts[2];
            _options.SpeedFactor           = opts[3];
            _options.AnimSpeedMultiplier   = opts[4];
            _options.FootstepPitchMultiplier = opts[5];
            _options.BonkImmuneDuration    = opts[6];
            // Slots 7+ added later. Length-guarded so old hosts can drive new clients.
            _options.RestoreSpeed          = opts.Length > 7 ? opts[7] : 0f;
            _options.AudioPresence         = opts.Length > 8 ? opts[8] : 1f;
            _options.EnemyPhysicalFactorCap = opts.Length > 9 ? opts[9] : 0f;
            _options.PreserveMass          = flags[0];
            _options.InvertedMode          = flags[1];
            _options.SuppressImpactFlash   = flags.Length > 2 && flags[2];
            _options.SuppressVoicePitch    = flags.Length > 3 && flags[3];
            _options.IgnoreBonkExpand      = flags.Length > 4 && flags[4];
            _options.RejectExternalApply   = flags.Length > 5 && flags[5];
            _options.SuppressCameraShake   = flags.Length > 6 && flags[6];
            _invertedActive = flags[1];
            float f = _options.Factor;
            IsScaled = true;
            _currentAnimSpeed = _options.Speed;
            Scaled.Add(this);
            if (_rb != null && !_isItem && !_options.PreserveMass) _rb.mass = Mathf.Clamp(_originalMass * f, 0.5f, _options.MassCap);
            ScalePgoMassOriginal(f);
            if (_rb != null && Handler is ValuableHandler)
            {
                float wantRaw = _originalMass * f;
                bool clamped = !_isItem && !_options.PreserveMass && wantRaw < 0.5f;
                if (Plugin.DiagMass)
                Plugin.Log.LogDebug(
                    $"[SC-DIAG][CLIENT] RPC_SHRINK_APPLY  {_displayName}  " +
                    $"mass {_originalMass:F3} → {_rb.mass:F3}  wantRaw={wantRaw:F3}  cap={_options.MassCap:F2}" +
                    (clamped ? "  *FLOOR_HIT*" : "") +
                    (_physGrabObject != null
                        ? $"  pgo.massOrig={_physGrabObject.massOriginal:F3}"
                        : ""));
            }
            if (_roomVolumeCheck != null)
            {
                _roomVolumeCheck.currentSize = _originalRoomVolumeSize * f;
                _roomVolumeCheck.CheckPosition = _originalCheckPosition * f;
            }
            ApplyScale(target);
            if (f < 1f) SetForceGrabPoint(false);
            PlayImpactEffect();
            if (!_options.SuppressVoicePitch)
            {
                var ep = GetComponentInParent<EnemyParent>();
                _audioPitch.ApplyPitch(ep != null ? (Component)ep : this, f, _options.AudioPresence);
            }
            _scaledItemFields = ItemHandler.OnShrinkFields(this, f);

            // Handler-specific client-side shrink (player voice/camera, etc.)
            Handler?.OnScale(this);

            // Match host-side indestructibility for valuables (shrink only).
            if (f < 1f && _physGrabObject != null && Handler is Handlers.ValuableHandler)
                _physGrabObject.OverrideIndestructible(0.5f);
            ScaleMapIcon(f);
        }

        [PunRPC]
        void RPC_Expand(PhotonMessageInfo info = default)
        {
            if (PhotonNetwork.InRoom && (info.Sender == null || info.Sender != PhotonNetwork.MasterClient)) return;
            IsScaled = false;
            Scaled.Remove(this);
            if (_rb != null) _rb.mass = _originalMass;
            RestorePgoMassOriginal();
            if (_roomVolumeCheck != null)
            {
                _roomVolumeCheck.currentSize = _originalRoomVolumeSize;
                _roomVolumeCheck.CheckPosition = _originalCheckPosition;
            }
            _currentAnimSpeed = ResolveExpandSpeed();
            ApplyScale(OriginalScale);
            SetForceGrabPoint(true);
            PlayImpactEffect();
            PlayCameraShake();
            if (!_options.SuppressVoicePitch) _audioPitch.RestorePitch();
            ItemHandler.OnRestoreFields(_scaledItemFields);
            _scaledItemFields = null;

            // Handler-specific client-side restore (player voice/camera, etc.)
            Handler?.OnRestore(this, isBonk: false);
            ScaleMapIcon(1f);
        }

        [PunRPC]
        void RPC_PlayerPitchCancel(PhotonMessageInfo info = default)
        {
            if (PhotonNetwork.InRoom && (info.Sender == null || info.Sender != PhotonNetwork.MasterClient)) return;
            var state = HandlerState as PlayerHandler.State;
            state?.PlayerAvatar.voiceChat?.OverridePitchCancel();
            if (state != null) PlayerHandler.RestoreVoicePresence(state);
        }

        // --- client-to-host expand requests ---

        // Requests are sent through the controller's own view with IsMine, so a
        // legit sender always owns the view. Anyone else asking to (un)shrink
        // somebody is dropped.
        bool SenderOwnsView(PhotonMessageInfo info)
        {
            if (!PhotonNetwork.InRoom) return true;
            return info.Sender != null && _networkPV != null && info.Sender == _networkPV.Owner;
        }

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

        // Called by PlayerBonkPatch when an inverted player takes damage while at full size.
        // Re-shrinks them back to their home (small) state.
        public void RequestInvertedReshrink()
        {
            if (IsScaled || !_invertedActive) return;
            if (_bonkImmuneTimer > 0f) return;
            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                DispatchShrink(_options);
            }
            else if (_networkPV != null && _networkPV.IsMine && PhotonNetwork.InRoom)
            {
                _networkPV.RPC(nameof(RPC_RequestInvertedReshrink), RpcTarget.MasterClient);
            }
        }

        [PunRPC]
        void RPC_RequestInvertedReshrink(PhotonMessageInfo info = default)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!SenderOwnsView(info)) return;
            if (IsScaled || !_invertedActive) return;
            var opts = ScaleOptions.Default;
            opts.InvertedMode = true;
            opts.Duration = 0f;
            DispatchShrink(opts);
        }

        // Called when the local player presses F10 to manually unshrink.
        // Skips bonk immunity, manual input should always work.
        public void RequestManualShrink()
        {
            if (IsScaled) return;
            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                if (!AllowManualScale) return;
                DispatchShrink(ScaleOptions.Default);
            }
            else if (_networkPV != null && _networkPV.IsMine && PhotonNetwork.InRoom)
            {
                _networkPV.RPC(nameof(RPC_RequestShrink), RpcTarget.MasterClient);
            }
        }

        [PunRPC]
        void RPC_RequestShrink(PhotonMessageInfo info = default)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!SenderOwnsView(info)) return;
            if (!AllowManualScale) return;
            if (IsScaled) return;
            DispatchShrink(ScaleOptions.Default);
        }

        public void RequestManualExpand()
        {
            if (!IsScaled) return;
            if (SemiFunc.IsMasterClientOrSingleplayer())
            {
                if (!AllowManualScale) return;
                DispatchExpand();
            }
            else if (_networkPV != null && _networkPV.IsMine && PhotonNetwork.InRoom)
            {
                _networkPV.RPC(nameof(RPC_RequestExpand), RpcTarget.MasterClient, false);
            }
        }

        [PunRPC]
        void RPC_RequestExpand(bool checkImmunity, PhotonMessageInfo info = default)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (!SenderOwnsView(info)) return;
            if (!IsScaled) return;
            if (checkImmunity)
            {
                DispatchExpandNow(); // respects _bonkImmuneTimer
            }
            else
            {
                if (!AllowManualScale) return;
                DispatchExpand();    // no immunity check, manual request
            }
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

                // Cancel voice pitch so it doesn't leak into the lobby.
                var playerState = ctrl.HandlerState as Handlers.PlayerHandler.State;
                playerState?.PlayerAvatar.voiceChat?.OverridePitchCancel();

                // Handler-specific cleanup.
                ctrl.Handler?.OnRestore(ctrl, isBonk: false);

                ctrl.IsScaled = false;
                ctrl._invertedActive = false;
                ctrl._transitioning = false;
                ctrl._target    = ctrl.OriginalScale;
                ctrl._animScale = ctrl.OriginalScale;
                ctrl._t.localScale = ctrl.OriginalScale;
            }
            Scaled.Clear();

            // Challenge mode re-shrink is handled by ChallengeModeDeferred in Start()
            // when new player ScaleControllers are created for the next level.
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

            // Re-apply challenge mode lobby pitch when a new player joins.
            // Their voice chat won't exist yet, so defer a few frames.
            if (ChallengeMode && Handler is Handlers.PlayerHandler)
                StartCoroutine(LobbyPitchDeferred());

            if (!IsScaled) return;
            if (_networkPV == null) return;
            _networkPV.RPC(nameof(RPC_Shrink), newPlayer, _target, PackOpts(), PackBools());
        }
    }
}
