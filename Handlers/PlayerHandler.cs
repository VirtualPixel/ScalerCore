using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
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
        // Reflection fields — used only by PlayerHandler methods.
        internal static readonly FieldInfo? _menuAvatarField =
            AccessTools.Field(typeof(PlayerAvatar), "playerAvatarMenu");
        internal static readonly FieldInfo? _expressionsField =
            AccessTools.Field(typeof(PlayerExpression), "expressions");
        internal static readonly FieldInfo? _grabMinDistOrigField =
            AccessTools.Field(typeof(PhysGrabber), "minDistanceFromPlayerOriginal");

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

            // Menu avatar
            internal Transform? MenuAvatarTransform;
            internal Vector3 MenuAvatarOriginalScale;
            internal PlayerAvatar? MenuPlayerAvatar;
            internal PlayerExpression? MenuExpression;
        }

        /// <summary>
        /// Find PlayerAvatar, retarget ScaleTransform to PlayerAvatarVisuals,
        /// attach PlayerShrinkLink, cache PlayerExpression.
        /// </summary>
        public void Setup(ScaleController ctrl)
        {
            var pa = ctrl.GetComponent<PlayerAvatar>();
            if (pa == null) return;

            var state = new State();
            state.PlayerAvatar = pa;

            // For players, the visible mesh lives on PlayerAvatarVisuals GO — a completely
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

                // Menu avatar is cached lazily in LateUpdate — it may not exist at Start time.
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
                Plugin.Log.LogInfo($"[SC] PlayerShrinkLink attached → {pac.CollisionTransform.gameObject.name}  colliderGO={(pac.Collider != null ? pac.Collider.gameObject.name : "null")}  avatar={ctrl.gameObject.name}");
            }
            else
                Plugin.Log.LogWarning($"[SC] PlayerShrinkLink SKIP  pac={pac != null}  collisionXform={(pac?.CollisionTransform != null)}  avatar={ctrl.gameObject.name}");

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

            // Voice pitch: apply on all clients.
            if (state.PlayerAvatar.voiceChat != null)
            {
                float factor = ctrl.OriginalScale.x > 0f ? ctrl._target.x / ctrl.OriginalScale.x : ShrinkConfig.Factor;
                float pitchMult = 1f + (1f - factor) * 0.5f;
                state.PlayerAvatar.voiceChat.OverridePitch(pitchMult, 0.2f, 0.5f, 9999f);
            }

            // Local-player-only: adjust camera, grab, and movement. In singleplayer
            // PhotonNetwork.InRoom is false so we skip the IsMine check entirely.
            bool isLocalPlayer = !PhotonNetwork.InRoom || (ctrl._networkPV != null && ctrl._networkPV.IsMine);
            if (isLocalPlayer)
                ApplyLocalPlayerShrinkEffects(ctrl);
        }

        /// <summary>
        /// Voice pitch cancel + restore local effects at expand time.
        /// </summary>
        public void OnRestore(ScaleController ctrl, bool isBonk)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;

            if (ctrl._networkPV != null && PhotonNetwork.InRoom)
                ctrl._networkPV.RPC("RPC_PlayerPitchCancel", RpcTarget.All);
            else
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
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;

            if (ctrl.IsScaled)
            {
                // Per-frame enforcement of grab stats — local player only.
                // PhysGrabber.instance is a singleton; running it for remote players'
                // ScaleControllers would corrupt the local player's grab strength.
                bool isLocalPlayer = !PhotonNetwork.InRoom || (ctrl._networkPV != null && ctrl._networkPV.IsMine);
                if (isLocalPlayer)
                {
                    float f = ShrinkConfig.Factor;
                    if (PhysGrabber.instance != null)
                    {
                        var (baseStr, baseRange, baseThrow) = GetBaseGrabStats(ctrl);
                        PhysGrabber.instance.grabStrength  = baseStr   * f;
                        PhysGrabber.instance.grabRange     = baseRange;
                        PhysGrabber.instance.throwStrength = baseThrow * f;
                    }
                }
                // Re-assert voice pitch every frame on ALL clients so other systems
                // (e.g. hourglass, spewer face-detach) can't permanently replace it.
                if (state.PlayerAvatar.voiceChat != null)
                {
                    float factor = ctrl.OriginalScale.x > 0f ? ctrl._target.x / ctrl.OriginalScale.x : ShrinkConfig.Factor;
                    float pitchMult = 1f + (1f - factor) * 0.5f;
                    state.PlayerAvatar.voiceChat.OverridePitch(pitchMult, 0.2f, 0.5f, 0.2f);
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

            // Pause menu avatar: scale to match shrunk state so the preview shows mini player.
            // Lazy-cached because PlayerAvatarMenu may not exist at Start time.
            if (state.MenuAvatarTransform == null && _menuAvatarField != null)
            {
                var menuComp = _menuAvatarField.GetValue(state.PlayerAvatar) as MonoBehaviour;
                if (menuComp != null)
                {
                    state.MenuAvatarTransform = menuComp.transform;
                    state.MenuAvatarOriginalScale = state.MenuAvatarTransform.localScale;
                    state.MenuPlayerAvatar = state.MenuAvatarTransform.GetComponentInChildren<PlayerAvatar>();
                    if (state.MenuPlayerAvatar == null)
                        state.MenuExpression = state.MenuAvatarTransform.GetComponentInChildren<PlayerExpression>();
                    Plugin.Log.LogInfo($"[SC] MenuAvatar cached  scale={state.MenuAvatarOriginalScale}" +
                        $"  pupilCtrl={( state.MenuPlayerAvatar != null ? "PlayerAvatar" : state.MenuExpression != null ? "PlayerExpression" : "NONE")}");
                }
            }
            if (state.MenuAvatarTransform != null)
            {
                if (ctrl.IsScaled)
                    state.MenuAvatarTransform.localScale = state.MenuAvatarOriginalScale * ShrinkConfig.Factor;
                else if (state.MenuAvatarTransform.localScale != state.MenuAvatarOriginalScale)
                    state.MenuAvatarTransform.localScale = state.MenuAvatarOriginalScale;
            }

            // Per-frame pupil and animation overrides for shrunken players (all clients).
            // Done in LateUpdate so we override the game's eye-tracking/gaze system that
            // sets pupil size during Update, which was overwriting our enlarged pupils.
            if (ctrl.IsScaled)
            {
                float pupilMult = 3f;
                if (state.PlayerExpression != null && _expressionsField != null)
                {
                    var expList = _expressionsField.GetValue(state.PlayerExpression)
                                      as List<ExpressionSettings>;
                    if (expList != null && expList.Count > 0)
                    {
                        float expressionFraction = 1f - Mathf.Clamp01(expList[0].weight / 100f);
                        pupilMult = Mathf.Lerp(3f, 1f, expressionFraction);
                    }
                }
                // Timer must be long enough (1s) that the RPC-synced override doesn't expire
                // between our per-frame refreshes. At 0.1s the timer expired every ~6 frames,
                // sending deactivation RPCs that made remote clients see normal pupils.
                state.PlayerAvatar.OverridePupilSize(pupilMult, 10, 20f, 0.5f, 5f, 0.5f, 1f);
                state.PlayerAvatar.OverrideAnimationSpeed(ShrinkConfig.ShrunkAnimSpeedMult, 5f, 5f, 1f);

                // Apply big pupils to the pause menu avatar preview too.
                if (state.MenuPlayerAvatar != null)
                    state.MenuPlayerAvatar.OverridePupilSize(pupilMult, 10, 20f, 0.5f, 5f, 0.5f, 1f);
                else if (state.MenuExpression != null && _expressionsField != null)
                {
                    // Fallback: set expression weight to force big pupils via PlayerExpression.
                    // PlayerExpression controls expressions; idle expression at weight 100 = no
                    // expression active, so pupils should be at our overridden size.
                    var expList = _expressionsField.GetValue(state.MenuExpression)
                                      as List<ExpressionSettings>;
                    if (expList != null && expList.Count > 0)
                        expList[0].weight = 100f; // force idle expression so pupils stay big
                }
            }
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

            float f = ShrinkConfig.Factor;
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
                state.OriginalGrabMinDist  = PhysGrabber.instance.minDistanceFromPlayer;
                state.OriginalGrabMaxDist  = PhysGrabber.instance.maxDistanceFromPlayer;
                PhysGrabber.instance.grabStrength          = baseStr   * f;
                PhysGrabber.instance.grabRange             = baseRange;
                PhysGrabber.instance.throwStrength         = baseThrow * f;
                PhysGrabber.instance.minDistanceFromPlayer = state.OriginalGrabMinDist * f;
                PhysGrabber.instance.maxDistanceFromPlayer = state.OriginalGrabMaxDist * f;
                _grabMinDistOrigField?.SetValue(PhysGrabber.instance, state.OriginalGrabMinDist * f);
                Plugin.Log.LogInfo($"[SC] player grab  strength {baseStr:F2}→{PhysGrabber.instance.grabStrength:F2}  range {baseRange:F2}→{PhysGrabber.instance.grabRange:F2}  throw {baseThrow:F2}→{PhysGrabber.instance.throwStrength:F2}  minDist {state.OriginalGrabMinDist:F2}→{PhysGrabber.instance.minDistanceFromPlayer:F2}  maxDist {state.OriginalGrabMaxDist:F2}→{PhysGrabber.instance.maxDistanceFromPlayer:F2}");
            }
            else Plugin.Log.LogWarning("[SC] PhysGrabber.instance null — grab range not scaled");
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
                AssetManager.instance.mainCamera.nearClipPlane = state.OriginalNearClip * f;
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
            // PlayerCollisionGrounded: intentionally NOT scaled.
            // Scaling the sphere radius/offset breaks ground detection (can't walk or jump).
            // Wall-jumping near objects needs a different fix (e.g. filtering by contact normal).
            PlayerShrinkScreenEffects(ctrl, shrinking: true);
        }

        /// <summary>
        /// Restore local-player effects. Called from OnRestore, RPC_Expand, CleanupAll.
        /// </summary>
        internal static void RestoreLocalPlayerShrinkEffects(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;

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
                _grabMinDistOrigField?.SetValue(PhysGrabber.instance, state.OriginalGrabMinDist);
            }
            PlayerController.instance?.OverrideSpeed(1f, 0.1f);
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
            // Ground check collider intentionally not modified (see ApplyLocalPlayerShrinkEffects).
            PlayerShrinkScreenEffects(ctrl, shrinking: false);
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
            if (!StatsManager.instance.playerUpgradeStrength.ContainsKey(steamID))
                return (1f, 4f, 0f);
            int strLvl = StatsManager.instance.playerUpgradeStrength[steamID];
            int rngLvl = StatsManager.instance.playerUpgradeRange[steamID];
            int thrLvl = StatsManager.instance.playerUpgradeThrow[steamID];
            return (1f + strLvl * 0.2f, 4f + rngLvl * 1f, thrLvl * 0.3f);
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
