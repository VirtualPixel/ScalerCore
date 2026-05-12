using Photon.Pun;
using UnityEngine;

namespace ScalerCore.Handlers
{
    /// <summary>
    /// Player-specific scaling logic.
    /// All player-specific fields live in PlayerHandler.State, stored on ctrl.HandlerState.
    /// State is created ONCE in Setup and NEVER cleared or recreated.
    /// </summary>
    internal class PlayerHandler : IScaleHandler
    {
  
        /// <summary>Returns (strengthFactor, rangeFactor) for the given scale factor.</summary>
        private static (float strength, float range) GetGrabFactors(float factor)
        {
            var rawStrength = factor * 1.5f;
            var maxStrength = factor < 1.0f ? 1.0f : 2.0f;
            float strengthFactor = Mathf.Clamp(rawStrength, 0.6f, maxStrength);
            return (strengthFactor, factor);
        }

        // Pupil override constants
        public const float Multiplier = 3f;
        public const int Priority = 4;          // matches vanilla ceiling eye, tranq, etc.
        public const float SpringSpeedIn = 3f;   // vanilla range is 1–5
        public const float SpringDampIn = 0.5f;
        public const float SpringSpeedOut = 5f;
        public const float SpringDampOut = 0.5f;
        public const float PupilReturnSpeed = 3f;

        /// <summary>
        /// Holds all player-specific component references and saved originals.
        /// Created once in Setup, stored on ScaleController.HandlerState, never cleared.
        /// </summary>
        internal sealed class State
        {
            // Components
            internal PlayerAvatar PlayerAvatar = null!;
            internal PlayerExpression? PlayerExpression;

            // Camera offsets
            internal Vector3 OriginalCamOffset;
            internal float   OriginalCrouchPos;
            internal float   OriginalCrawlPos;
            internal CameraCrawlPosition? CameraCrawl;

            // Vision targets
            internal PlayerVisionTarget? VisionTarget;
            internal float VisionStandPos;
            internal float VisionCrouchPos;
            internal float VisionCrawlPos;

            // Collision
            internal CapsuleCollider? StandCheckCollider;
            internal float OriginalStandCheckHeight;
            internal float OriginalStandCheckRadius;
            internal Vector3 OriginalStandCheckOffset;
            internal Vector3 OriginalStandCollision;
            internal Vector3 OriginalCrouchCollision;

            // Grab stats
            internal float OriginalGrabMinDist;
            internal float OriginalGrabMaxDist;

            // FOV / camera
            internal float OriginalFOV;
            internal float OriginalNearClip;

            // Pupil transition tracking
            internal bool WasExpressing;

            // Tumble
            internal float TumbleFollowOriginalY = float.NaN;
            internal bool WasTumbling;

            // Menu avatar
            internal Transform? MenuAvatarTransform;
            internal Vector3 MenuAvatarOriginalScale;
            internal PlayerAvatar? MenuPlayerAvatar;
            internal PlayerExpression? MenuExpression;
            internal PlayerEyes? MenuEyes;

            // Set by ApplyLocalPlayerShrinkEffects, cleared by RestoreLocalPlayerShrinkEffects.
            // Guards against a second Apply (e.g. RPC_Shrink during rescale) re-capturing
            // already-scaled singleton values as originals and compounding to factor².
            internal bool LocalEffectsApplied;
        }

        /// <summary>
        /// Find PlayerAvatar, retarget ScaleTransform to PlayerAvatarVisuals,
        /// attach PlayerShrinkLink, cache PlayerExpression.
        /// </summary>
        public void Setup(ScaleController ctrl)
        {
            var pa = ctrl.GetComponent<PlayerAvatar>();
            if (pa == null) return;

            var state = new State
            {
                PlayerAvatar = pa
            };

            // For players, the visible mesh lives on PlayerAvatarVisuals GO, a completely
            // separate transform from PlayerAvatar GO. PlayerAvatarVisuals manually copies
            // position + rotation from PlayerAvatar each Update, but never touches localScale.
            // Retarget _t so our LateUpdate force and transition animation scale the right GO.
            if (pa.playerAvatarVisuals != null)
            {
                ctrl._t            = pa.playerAvatarVisuals.transform;
                ctrl.OriginalScale = ctrl._t.localScale;
                ctrl._target       = ctrl.OriginalScale;
                ctrl._animScale    = ctrl.OriginalScale;
                state.PlayerExpression = pa.playerAvatarVisuals.GetComponent<PlayerExpression>();

                // Menu avatar is cached lazily in LateUpdate, it may not exist at Start time.
            }

            // PlayerAvatarCollision.CollisionTransform is not a child of PlayerAvatar GO,
            // so a raycast hitting the player's capsule can't find ScaleController via
            // GetComponentInParent. Attach a PlayerShrinkLink to CollisionTransform so
            // ItemDownsizer can resolve it with GetComponentInParent<PlayerShrinkLink>().
            var pac = pa.GetComponent<PlayerAvatarCollision>();
            if (pac?.CollisionTransform != null)
            {
                var link = pac.CollisionTransform.GetComponent<PlayerShrinkLink>()
                         ?? pac.CollisionTransform.gameObject.AddComponent<PlayerShrinkLink>();
                link.Controller = ctrl;
                Plugin.Log.LogDebug($"[SC] PlayerShrinkLink attached → {pac.CollisionTransform.gameObject.name}  colliderGO={(pac.Collider != null ? pac.Collider.gameObject.name : "null")}  avatar={ctrl.gameObject.name}");
            }
            else
                Plugin.Log.LogWarning($"[SC] PlayerShrinkLink SKIP  pac={pac != null}  collisionXform={(pac?.CollisionTransform != null)}  avatar={ctrl.gameObject.name}");

            // Tumble link is attached lazily via PlayerTumbleLinkPatch (PlayerPatches.cs)
            // because pa.tumble is null at this point, the tumble GO hasn't been
            // instantiated yet.

            ctrl.HandlerState = state;
        }

        /// <summary>
        /// Voice pitch override + local player effects at shrink time.
        /// Called from DispatchShrink() and RPC_Shrink() via handler interface.
        /// </summary>
        public void OnScale(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;

            // Voice pitch: apply on all clients, unless the active session opted out.
            if (!ctrl._options.SuppressVoicePitch)
            {
                if (state.PlayerAvatar.voiceChat != null)
                {
                    float factor = ctrl.OriginalScale.x > 0f ? ctrl._target.x / ctrl.OriginalScale.x : ctrl._options.Factor;
                    float pitchMult = 1f + (1f - factor) * 0.5f;
                    state.PlayerAvatar.voiceChat.OverridePitch(pitchMult, 0.2f, 0.5f, 9999f);
                    Plugin.Log.LogDebug($"[SC] VOICE PITCH SET  {ctrl._displayName}  pitch={pitchMult:F2}  isLocal={state.PlayerAvatar.isLocal}  isMine={ctrl._networkPV?.IsMine}");
                }
                else
                {
                    Plugin.Log.LogDebug($"[SC] VOICE PITCH SKIP  {ctrl._displayName}  voiceChat=null  isLocal={state.PlayerAvatar.isLocal}  voiceChatFetched={state.PlayerAvatar.voiceChatFetched}");
                }
            }

            // Local-player-only: adjust camera, grab, and movement. In singleplayer
            // PhotonNetwork.InRoom is false so we skip the IsMine check entirely.
            bool isLocalPlayer = !PhotonNetwork.InRoom || (ctrl._networkPV != null && ctrl._networkPV.IsMine);
            if (isLocalPlayer)
            {
                ApplyLocalPlayerShrinkEffects(ctrl);
                EjectPocketedShrunkItems(ctrl);
            }
        }

        /// <summary>
        /// Voice pitch cancel + restore local effects at expand time.
        /// </summary>
        public void OnRestore(ScaleController ctrl, bool isBonk)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;

            if (ctrl._networkPV != null && PhotonNetwork.InRoom)
            {
                bool isHost = SemiFunc.IsMasterClientOrSingleplayer();
                var physGrabber = ctrl.GetComponent<PhysGrabber>();
                var (baseStr, baseRange, baseThrow) = GetBaseGrabStats(ctrl);

                if (!ctrl._options.SuppressVoicePitch)
                    ctrl._networkPV.RPC("RPC_PlayerPitchCancel", RpcTarget.All);
                if (physGrabber != null && isHost)
                {
                    physGrabber.grabStrength = baseStr;
                    physGrabber.grabRange = baseRange;
                    physGrabber.throwStrength = baseThrow;
                }
            }
            else if (!ctrl._options.SuppressVoicePitch)
                state.PlayerAvatar.voiceChat?.OverridePitchCancel();
            bool isLocalPlayer = !PhotonNetwork.InRoom || (ctrl._networkPV != null && ctrl._networkPV.IsMine);
            if (isLocalPlayer)
                RestoreLocalPlayerShrinkEffects(ctrl);
        }

        /// <summary>
        /// Per-frame grab stats enforcement, voice pitch re-assertion, and debug key handling.
        /// </summary>
        public void OnUpdate(ScaleController ctrl)
        {
            bool isHost = SemiFunc.IsMasterClientOrSingleplayer();
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;

            if (ctrl.IsScaled)
            {
                var (baseStr, baseRange, baseThrow) = GetBaseGrabStats(ctrl);
                var (strengthFactor, rangeFactor) = GetGrabFactors(ctrl._options.Factor);

                // Host enforces grab stats for ALL shrunken players (physics runs on host).
                // Non-host enforces locally via PhysGrabber.instance as a fallback.
                // PhysGrabber.instance is a singleton; running it for remote players'
                // ScaleControllers would corrupt the local player's grab strength.
                bool isLocalPlayer = !PhotonNetwork.InRoom || (ctrl._networkPV != null && ctrl._networkPV.IsMine);
                // Re-assert voice pitch every frame on ALL clients so other systems
                // (e.g. hourglass, spewer face-detach) can't permanently replace it.
                if (!ctrl._options.SuppressVoicePitch && state.PlayerAvatar.voiceChat != null)
                {
                    float factor = ctrl.OriginalScale.x > 0f ? ctrl._target.x / ctrl.OriginalScale.x : ctrl._options.Factor;
                    float pitchMult = 1f + (1f - factor) * 0.5f;
                    state.PlayerAvatar.voiceChat.overridePitchMultiplierTarget = pitchMult;
                    state.PlayerAvatar.voiceChat.overridePitchTimer = 0.2f;
                    state.PlayerAvatar.voiceChat.overridePitchIsActive = true;
                }
                // Host must enforce strength, range, and throw nerfs across all clients
                if (isHost)
                {
                    var physGrabber = ctrl.GetComponent<PhysGrabber>();

                    if (physGrabber != null)
                    {
                        physGrabber.grabStrength = baseStr * strengthFactor;
                        physGrabber.grabRange = baseRange * rangeFactor;
                        physGrabber.throwStrength = baseThrow * rangeFactor;
                    }
                }
                else if (isLocalPlayer)
                {
                    if (PhysGrabber.instance != null)
                    {
                        PhysGrabber.instance.grabStrength = baseStr * strengthFactor;
                        PhysGrabber.instance.grabRange = baseRange * rangeFactor;
                        PhysGrabber.instance.throwStrength = baseThrow * rangeFactor;
                    }
                }
            }

            // F9/F10 debug keys are handled in ScaleController.Update
            // so they work even when not scaled (F9 needs !IsScaled).
        }

        /// <summary>
        /// Per-frame pupil size, animation speed, and menu avatar overrides.
        /// </summary>
        public void OnLateUpdate(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;

            // Force-apply target scale when at rest (bounce coroutine finished).
            if (ctrl.IsScaled && !ctrl._transitioning)
                ctrl._t.localScale = ctrl._target;

            // Tumble visual offset: the tumble body stays full-size (scaling it causes
            // fall-through), but the shrunken visual mesh sinks into the tumble sphere.
            // Nudge followPosition upward so the mini player sits on top of the ball.
            var tumble = state.PlayerAvatar.tumble;
            if (tumble != null && tumble.followPosition != null)
            {
                // Cache the original Y on first sight.
                if (float.IsNaN(state.TumbleFollowOriginalY))
                    state.TumbleFollowOriginalY = tumble.followPosition.localPosition.y;

                if (ctrl.IsScaled && state.PlayerAvatar.isTumbling)
                {
                    // When entering tumble while shrunken, nudge the body up so the
                    // full-size colliders don't spawn inside thin floor geometry.
                    if (!state.WasTumbling && tumble.rb != null)
                        tumble.rb.position += Vector3.up * 0.3f;
                    state.WasTumbling = true;

                    float offset = (1f - ctrl._options.Factor) * 0.1f;
                    var pos = tumble.followPosition.localPosition;
                    pos.y = state.TumbleFollowOriginalY + offset;
                    tumble.followPosition.localPosition = pos;
                }
                else
                {
                    state.WasTumbling = false;
                    if (tumble.followPosition.localPosition.y != state.TumbleFollowOriginalY)
                    {
                        var pos = tumble.followPosition.localPosition;
                        pos.y = state.TumbleFollowOriginalY;
                        tumble.followPosition.localPosition = pos;
                    }
                }
            }

            // Pause menu avatar: scale to match shrunk state so the preview shows mini player.
            // Lazy-cached because PlayerAvatarMenu may not exist at Start time.
            if (state.MenuAvatarTransform == null && PlayerAvatarMenu.instance != null)
            {
                state.MenuAvatarTransform = PlayerAvatarMenu.instance.transform;
                state.MenuAvatarOriginalScale = state.MenuAvatarTransform.localScale;
                state.MenuPlayerAvatar = state.MenuAvatarTransform.GetComponentInChildren<PlayerAvatar>();
                if (state.MenuPlayerAvatar == null)
                {
                    state.MenuExpression = state.MenuAvatarTransform.GetComponentInChildren<PlayerExpression>();
                    state.MenuEyes = state.MenuAvatarTransform.GetComponentInChildren<PlayerEyes>();
                }
                Plugin.Log.LogDebug($"[SC] MenuAvatar cached  scale={state.MenuAvatarOriginalScale}" +
                    $"  pupilCtrl={( state.MenuPlayerAvatar != null ? "PlayerAvatar" : state.MenuExpression != null ? "PlayerExpression" : "NONE")}");
            }
            bool isLocal = state.PlayerAvatar.isLocal;

            if (state.MenuAvatarTransform != null && isLocal)
            {
                var current = state.MenuAvatarTransform.localScale;
                var target = state.MenuAvatarOriginalScale * ctrl._options.Factor;
                var speed = Time.deltaTime * 10f;
                if (ctrl.IsScaled)
                    state.MenuAvatarTransform.localScale = Vector3.Lerp(current, target, speed);
                else if (Vector3.Distance(current, state.MenuAvatarOriginalScale) > 0.001)
                    state.MenuAvatarTransform.localScale = Vector3.Lerp(current, state.MenuAvatarOriginalScale, speed);
                else if (current != state.MenuAvatarOriginalScale)
                    state.MenuAvatarTransform.localScale = state.MenuAvatarOriginalScale;
            }

            // Per-frame pupil and animation overrides for shrunken players.
            // OverridePupilSizeActivate only sends the RPC when called on the LOCAL
            // player (isLocal check at line 703 of PlayerAvatar). For remote players,
            // the game's own OverridePupilSizeLogic per-frame refresh handles it
            // once overridePupilSizeActive is set via the initial RPC.
            // Done in LateUpdate to override the game's gaze system from Update.
            bool expressing = state.PlayerExpression != null && state.PlayerExpression.isExpressing;

            if (ctrl.IsScaled)
            {
                if (isLocal)
                {
                    // Animation speed always applies while shrunken, regardless of expression.
                    state.PlayerAvatar.OverrideAnimationSpeed(ctrl._options.AnimSpeedMultiplier, SpringSpeedIn, SpringSpeedOut, 9999f);

                    if (!expressing)
                    {
                        // Use a gentle spring when returning from expression so pupils don't pop.
                        // Normal shrink uses SpringSpeedIn (fast), expression recovery uses a slower rate.
                        float springIn = state.WasExpressing ? PupilReturnSpeed : SpringSpeedIn;
                        state.PlayerAvatar.OverridePupilSize(Multiplier, Priority, springIn, SpringDampIn, SpringSpeedOut, SpringDampOut, 9999f);
                    }
                    else
                    {
                        // Cancel the big-pupil override so the expression can control pupils
                        state.PlayerAvatar.overridePupilSizeMultiplier = 1f;
                        state.PlayerAvatar.overridePupilSizeMultiplierTarget = 1f;
                        state.PlayerAvatar.overridePupilSizeTimer = 0f;
                    }
                }
                else
                {
                    // Remote players: force big pupils unless expressing.
                    if (!expressing)
                    {
                        var eyes = state.PlayerAvatar.playerAvatarVisuals.playerEyes;
                        if (eyes != null)
                        {
                            // Gentle lerp when returning from expression, instant otherwise.
                            if (state.WasExpressing)
                                eyes.pupilSizeMultiplier = Mathf.Lerp(eyes.pupilSizeMultiplier, Multiplier, Time.deltaTime * PupilReturnSpeed);
                            else
                                eyes.pupilSizeMultiplier = Multiplier;
                        }
                    }
                    else
                    {
                        // Cancel the big-pupil override so the expression can control pupils
                        state.PlayerAvatar.overridePupilSizeMultiplier = 1f;
                        state.PlayerAvatar.overridePupilSizeMultiplierTarget = 1f;
                        state.PlayerAvatar.overridePupilSizeTimer = 0f;
                    }
                }

                state.WasExpressing = expressing;

                // Apply big pupils to the pause menu avatar preview too (only when not expressing).
                if (state.MenuEyes != null && isLocal)
                    state.MenuEyes.pupilSizeMultiplier = expressing ? 1f : Multiplier;
            }
            else if (state.MenuEyes != null && state.MenuEyes.pupilSizeMultiplier > 1f && isLocal)
                state.MenuEyes.pupilSizeMultiplier = 1f;
        }

        public void OnDestroy(ScaleController ctrl)
        {
            // No player-specific destroy logic needed.
        }

        /// <summary>
        /// Local-player-only effects: camera, grab, speed, FOV, collision.
        /// Called from OnScale (host-is-player) and RPC_Shrink (non-host player).
        /// </summary>
        internal static void ApplyLocalPlayerShrinkEffects(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            if (state.LocalEffectsApplied) return;

            float f = ctrl._options.Factor;
            if (CameraPosition.instance != null)
            {
                state.OriginalCamOffset = CameraPosition.instance.playerOffset;
                CameraPosition.instance.playerOffset = state.OriginalCamOffset * f;
            }
            if (CameraCrouchPosition.instance != null)
            {
                state.OriginalCrouchPos = CameraCrouchPosition.instance.Position;
                CameraCrouchPosition.instance.Position = state.OriginalCrouchPos * f;
            }
            if (state.CameraCrawl == null)
                state.CameraCrawl = Object.FindObjectOfType<CameraCrawlPosition>();
            if (state.CameraCrawl != null)
            {
                state.OriginalCrawlPos = state.CameraCrawl.Position;
                state.CameraCrawl.Position = state.OriginalCrawlPos * f;
            }
            if (state.VisionTarget == null)
                state.VisionTarget = state.PlayerAvatar.GetComponent<PlayerVisionTarget>();
            if (state.VisionTarget != null)
            {
                // VisionTransform.localPosition.y = CurrentPosition, which lerps toward StandPosition
                // (or Crouch/Crawl). PhysGrabber uses VisionTransform to position held guns via
                // OverrideGrabDistance. Without scaling these, guns are pulled to full-size eye height
                // even though the player is 40% tall.
                state.VisionStandPos  = state.VisionTarget.StandPosition;
                state.VisionCrouchPos = state.VisionTarget.CrouchPosition;
                state.VisionCrawlPos  = state.VisionTarget.CrawlPosition;
                state.VisionTarget.StandPosition  = state.VisionStandPos  * f;
                state.VisionTarget.CrouchPosition = state.VisionCrouchPos * f;
                state.VisionTarget.CrawlPosition  = state.VisionCrawlPos  * f;
            }
            if (PhysGrabber.instance != null)
            {
                var (baseStr, baseRange, baseThrow) = GetBaseGrabStats(ctrl);
                var (strengthFactor, rangeFactor) = GetGrabFactors(ctrl._options.Factor);

                state.OriginalGrabMinDist  = PhysGrabber.instance.minDistanceFromPlayer;
                state.OriginalGrabMaxDist  = PhysGrabber.instance.maxDistanceFromPlayer;
                PhysGrabber.instance.grabStrength          = baseStr   * strengthFactor;
                PhysGrabber.instance.grabRange             = baseRange * rangeFactor;
                PhysGrabber.instance.throwStrength         = baseThrow * rangeFactor;
                PhysGrabber.instance.minDistanceFromPlayer = state.OriginalGrabMinDist * f;
                PhysGrabber.instance.maxDistanceFromPlayer = state.OriginalGrabMaxDist * f;
                PhysGrabber.instance.minDistanceFromPlayerOriginal = state.OriginalGrabMinDist * f;
                Plugin.Log.LogDebug($"[SC] player grab  strength {baseStr:F2}→{PhysGrabber.instance.grabStrength:F2} range {baseRange:F2}→{PhysGrabber.instance.grabRange:F2}  throw {baseThrow:F2}→{PhysGrabber.instance.throwStrength:F2}  minDist {state.OriginalGrabMinDist:F2}→{PhysGrabber.instance.minDistanceFromPlayer:F2}  maxDist {state.OriginalGrabMaxDist:F2}→{PhysGrabber.instance.maxDistanceFromPlayer:F2}");
            }
            else Plugin.Log.LogWarning("[SC] PhysGrabber.instance null, grab range not scaled");
            float speedMult = Mathf.Lerp(1f, f, 0.5f);
            PlayerController.instance?.OverrideSpeed(speedMult, 9999f);
            if (CameraZoom.Instance != null)
            {
                state.OriginalFOV = CameraZoom.Instance.playerZoomDefault;
                float newFOV = state.OriginalFOV + 20f * (1f - f);
                CameraZoom.Instance.playerZoomDefault = newFOV;
                CameraZoom.Instance.OverrideZoomSet(newFOV, 9999f, 3f, 3f, ctrl.gameObject, 999);
            }
            if (AssetManager.instance?.mainCamera != null)
            {
                state.OriginalNearClip = AssetManager.instance.mainCamera.nearClipPlane;
                AssetManager.instance.mainCamera.nearClipPlane = state.OriginalNearClip * f * 0.5f;
            }
            if (PlayerCollision.instance != null)
            {
                state.OriginalStandCollision  = PlayerCollision.instance.StandCollision.localScale;
                state.OriginalCrouchCollision = PlayerCollision.instance.CrouchCollision.localScale;
                PlayerCollision.instance.StandCollision.localScale  = state.OriginalStandCollision  * f;
                PlayerCollision.instance.CrouchCollision.localScale = state.OriginalCrouchCollision * f;
            }
            // PlayerCollisionStand: the "can I uncrouch?" overlap check capsule.
            // Without scaling this, shrunken players get stuck crouching under areas
            // they could walk under at shrunken size.
            if (PlayerCollisionStand.instance != null)
            {
                if (state.StandCheckCollider == null)
                    state.StandCheckCollider = PlayerCollisionStand.instance.GetComponent<CapsuleCollider>();
                if (state.StandCheckCollider != null)
                {
                    state.OriginalStandCheckHeight = state.StandCheckCollider.height;
                    state.OriginalStandCheckRadius = state.StandCheckCollider.radius;
                    state.StandCheckCollider.height = state.OriginalStandCheckHeight * f;
                    state.StandCheckCollider.radius = state.OriginalStandCheckRadius * f;
                }
                state.OriginalStandCheckOffset = PlayerCollisionStand.instance.Offset;
                PlayerCollisionStand.instance.Offset = state.OriginalStandCheckOffset * f;
            }
            // Ground check sphere: not scaled. The game's OverlapSphere doesn't filter
            // by contact direction, so shrunken players can wall-jump. Fixing this
            // properly requires a normal check or raycast, not just radius scaling.
            PlayerShrinkScreenEffects(ctrl, shrinking: true);

            state.LocalEffectsApplied = true;
        }

        /// <summary>
        /// Restore local-player effects. Called from OnRestore, RPC_Expand, CleanupAll.
        /// </summary>
        internal static void RestoreLocalPlayerShrinkEffects(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            if (!state.LocalEffectsApplied) return;

            if (CameraPosition.instance != null)
                CameraPosition.instance.playerOffset = state.OriginalCamOffset;
            if (CameraCrouchPosition.instance != null)
                CameraCrouchPosition.instance.Position = state.OriginalCrouchPos;
            if (state.CameraCrawl != null)
                state.CameraCrawl.Position = state.OriginalCrawlPos;
            if (state.VisionTarget != null)
            {
                state.VisionTarget.StandPosition  = state.VisionStandPos;
                state.VisionTarget.CrouchPosition = state.VisionCrouchPos;
                state.VisionTarget.CrawlPosition  = state.VisionCrawlPos;
            }
            if (PhysGrabber.instance != null)
            {
                var (baseStr, baseRange, baseThrow) = GetBaseGrabStats(ctrl);
                PhysGrabber.instance.grabStrength          = baseStr;
                PhysGrabber.instance.grabRange             = baseRange;
                PhysGrabber.instance.throwStrength         = baseThrow;
                PhysGrabber.instance.minDistanceFromPlayer = state.OriginalGrabMinDist;
                PhysGrabber.instance.maxDistanceFromPlayer = state.OriginalGrabMaxDist;
                PhysGrabber.instance.minDistanceFromPlayerOriginal = state.OriginalGrabMinDist;
            }
            PlayerController.instance?.OverrideSpeed(1f, 0.1f);
            state.PlayerAvatar.OverridePupilSizeActivate(false, 1f, Priority, SpringSpeedIn, SpringDampIn, SpringSpeedOut, SpringDampOut, 0.1f);
            state.PlayerAvatar.OverrideAnimationSpeed(1f, SpringSpeedIn, SpringSpeedOut);
            if (CameraZoom.Instance != null)
            {
                CameraZoom.Instance.playerZoomDefault = state.OriginalFOV;
                CameraZoom.Instance.OverrideZoomSet(state.OriginalFOV, 0.5f, 3f, 3f, ctrl.gameObject, 999);
            }
            if (AssetManager.instance?.mainCamera != null)
                AssetManager.instance.mainCamera.nearClipPlane = state.OriginalNearClip;
            if (PlayerCollision.instance != null)
            {
                PlayerCollision.instance.StandCollision.localScale  = state.OriginalStandCollision;
                PlayerCollision.instance.CrouchCollision.localScale = state.OriginalCrouchCollision;
            }
            if (state.StandCheckCollider != null)
            {
                state.StandCheckCollider.height = state.OriginalStandCheckHeight;
                state.StandCheckCollider.radius = state.OriginalStandCheckRadius;
            }
            if (PlayerCollisionStand.instance != null)
                PlayerCollisionStand.instance.Offset = state.OriginalStandCheckOffset;
            // Ground check sphere not modified (see ApplyLocalPlayerShrinkEffects).
            PlayerShrinkScreenEffects(ctrl, shrinking: false);

            state.LocalEffectsApplied = false;
        }

        /// <summary>
        /// Derive current base grab stats from StatsManager upgrade levels.
        /// Used for per-frame enforcement and restore so stat upgrades purchased
        /// while shrunken are correctly accounted for.
        /// </summary>
        internal static (float strength, float range, float throwStr) GetBaseGrabStats(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null || StatsManager.instance == null)
                return (1f, 4f, 0f);
            string steamID = SemiFunc.PlayerGetSteamID(state.PlayerAvatar);
            var sm = StatsManager.instance;
            sm.playerUpgradeStrength.TryGetValue(steamID, out int strLvl);
            sm.playerUpgradeRange.TryGetValue(steamID, out int rngLvl);
            sm.playerUpgradeThrow.TryGetValue(steamID, out int thrLvl);
            return (1f + strLvl * 0.2f, 4f + rngLvl * 1f, thrLvl * 0.3f);
        }

        /// <summary>
        /// Force-unequip any pocketed shrunken items when the player shrinks.
        /// A shrunken player shouldn't carry oversized items in their inventory.
        /// </summary>
        static void EjectPocketedShrunkItems(ScaleController playerCtrl)
        {
            if (Inventory.instance == null) return;
            var grabber = PhysGrabber.instance;
            if (grabber == null) return;

            for (int i = 0; i < 3; i++)
            {
                var spot = Inventory.instance.GetSpotByIndex(i);
                if (spot == null || !spot.IsOccupied()) continue;
                var item = spot.CurrentItem;
                if (item == null) continue;

                // Only eject items that are shrunken (ones we made pocketable).
                var itemCtrl = item.GetComponent<ScaleController>();
                if (itemCtrl == null || !itemCtrl.IsScaled) continue;

                item.ForceUnequip(playerCtrl._t.position + Vector3.up * 0.5f, grabber.photonView.ViewID);
                Plugin.Log.LogDebug($"[SC] Ejected shrunken item from slot {i}");
            }
        }

        /// <summary>
        /// Screen effects played locally when the player shrinks or expands.
        /// Shrink: blue vignette + glitch + shake (compression/warp feel).
        /// Expand: warm vignette + glitch + bigger shake (release of tension).
        /// </summary>
        static void PlayerShrinkScreenEffects(ScaleController ctrl, bool shrinking)
        {
            CameraGlitch.Instance?.PlayTiny();
            GameDirector.instance?.CameraShake?.Shake(shrinking ? 2f : 3f, 0.35f);
            CameraNoise.Instance?.Override(0.7f, 0.5f);

            if (PostProcessing.Instance != null)
            {
                var col = shrinking ? new Color(0.1f, 0.35f, 1f) : new Color(1f, 0.55f, 0.1f);
                PostProcessing.Instance.VignetteOverride(col, 0.45f, 0.5f, 15f, 4f, 0.3f, ctrl.gameObject);
            }
        }
    }
}
