using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ScalerCore.Compat
{
    /// <summary>
    /// Optional bridge to RepoXR, the VR mod for R.E.P.O.
    ///
    /// RepoXR swaps the flat-screen camera for a head-tracked rig, so ScalerCore's normal
    /// trick -- nudging CameraPosition / CameraZoom offsets -- can't shrink a VR player's
    /// viewpoint: the headset reports a real eye height no offset can cancel, and the hands
    /// live on a rig ScalerCore never touches. This bridge scales RepoXR's tracking space
    /// and hand rig so a shrunken VR player genuinely sees and reaches like a small player,
    /// and rescales room-scale walking to match.
    ///
    /// Everything is resolved by reflection. With RepoXR absent the first probe fails, the
    /// bridge stays dormant, and nothing else here runs. ScalerCore neither references nor
    /// depends on RepoXR, and non-VR play is untouched.
    /// </summary>
    internal static class RepoXRBridge
    {
        // -1 not probed, 0 absent, 1 present.
        static int _probe = -1;

        static PropertyInfo _sessionInstance = null!; // VRSession.Instance  (static)
        static PropertyInfo _sessionInVR     = null!; // VRSession.InVR      (static bool)
        static PropertyInfo _sessionCamera   = null!; // VRSession.MainCamera
        static PropertyInfo _sessionPlayer   = null!; // VRSession.Player
        static PropertyInfo _playerRig       = null!; // VRPlayer.Rig
        static FieldInfo    _rigHeadOffset   = null!; // VRRig.headOffset
        static MethodInfo?  _handleMovement;          // VRPlayer.HandleMovement
        static AccessTools.FieldRef<object, Vector3>? _lastPosition; // VRPlayer.lastPosition

        static bool    _applied;
        static float   _factor = 1f;
        static Vector3 _origTrackingScale = Vector3.one;
        static Vector3 _origRigScale      = Vector3.one;
        static Vector3 _origHeadOffset;
        static bool    _trackingCaptured;
        static bool    _rigCaptured;
        static bool    _movementPatched;
        static bool    _failed;

        /// <summary>True when the local player shrink should be mirrored onto the VR rig.</summary>
        internal static bool Active
        {
            get
            {
                if (_failed) return false;
                if (!Probe()) return false;
                try
                {
                    return (bool)_sessionInVR.GetValue(null) && _sessionInstance.GetValue(null) != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>Resolve RepoXR's API once. Caches the outcome; safe to call repeatedly.</summary>
        static bool Probe()
        {
            if (_probe >= 0) return _probe == 1;
            _probe = 0;
            try
            {
                // AccessTools.TypeByName scans every type in every loaded assembly before
                // it gives up, ~200ms when the type is absent, and it landed on the first
                // in-game scale as a freeze-frame. RepoXR is absent in the common case, so
                // check for its assembly by name first (a cheap enumeration) and skip the
                // full type scan when it isn't loaded. When RepoXR is present TypeByName
                // finds it early, so VR detection stays fast and unchanged.
                if (!RepoXRAssemblyLoaded())
                {
                    // Not loaded YET is not the same as not installed. Warm() runs at our own
                    // plugin Awake and BepInEx load order between two independent plugins is
                    // arbitrary, so latching a no here would leave VR support off for the whole
                    // session on a machine that has RepoXR. Leave the probe unresolved and ask
                    // again next time; the whole point of the check is that it is cheap.
                    _probe = -1;
                    return false;
                }

                var session = AccessTools.TypeByName("RepoXR.Managers.VRSession");
                var player  = AccessTools.TypeByName("RepoXR.Player.VRPlayer");
                var rig     = AccessTools.TypeByName("RepoXR.Player.VRRig");
                if (session == null || player == null || rig == null) return false;

                _sessionInstance = AccessTools.Property(session, "Instance");
                _sessionInVR     = AccessTools.Property(session, "InVR");
                _sessionCamera   = AccessTools.Property(session, "MainCamera");
                _sessionPlayer   = AccessTools.Property(session, "Player");
                _playerRig       = AccessTools.Property(player, "Rig");
                _rigHeadOffset   = AccessTools.Field(rig, "headOffset");
                _handleMovement  = AccessTools.Method(player, "HandleMovement");

                if (_sessionInstance == null || _sessionInVR == null || _sessionCamera == null ||
                    _sessionPlayer == null || _playerRig == null || _rigHeadOffset == null)
                {
                    Plugin.Log.LogWarning("[SC-VR] RepoXR found but its API didn't resolve; VR scaling disabled.");
                    return false;
                }

                _probe = 1;
                Plugin.Log.LogInfo("[SC-VR] RepoXR detected; VR shrink support armed.");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[SC-VR] RepoXR probe failed; VR scaling disabled. {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Cheap presence test: is any RepoXR assembly loaded? Enumerating loaded assembly
        /// names is fast; it spares the full type-by-name scan when RepoXR isn't installed.
        /// </summary>
        static bool RepoXRAssemblyLoaded()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = asm.GetName().Name;
                if (name != null && name.IndexOf("RepoXR", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Resolve RepoXR presence once, off the hot path. Called at plugin load so the
        /// one-time probe (and any type resolution when RepoXR is present) happens during
        /// startup instead of on the first scale. Idempotent: Probe caches its outcome.
        /// </summary>
        internal static void Warm() => Probe();

        /// <summary>Shrink the VR viewpoint and hand rig to the given factor. Local player only.</summary>
        internal static void ApplyViewpointShrink(float factor)
        {
            if (!Active) return;
            _factor  = factor;
            _applied = true;
            EnsureMovementPatch();
            Reassert();
        }

        /// <summary>Restore the VR viewpoint and hand rig to full size.</summary>
        internal static void RestoreViewpointShrink()
        {
            if (!_applied) return;
            _applied = false;
            _factor  = 1f;
            try
            {
                var track = CameraTransform()?.parent;
                if (track != null && _trackingCaptured)
                    track.localScale = _origTrackingScale;

                var rig = Rig();
                if (rig != null && _rigCaptured)
                {
                    rig.transform.localScale = _origRigScale;
                    _rigHeadOffset.SetValue(rig, _origHeadOffset);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[SC-VR] viewpoint restore failed: {e.GetType().Name}: {e.Message}");
            }
            _trackingCaptured = false;
            _rigCaptured      = false;
        }

        /// <summary>
        /// Re-apply the scale while shrunk. RepoXR rebuilds the rig on scene changes, so
        /// the scale has to be reasserted rather than set once.
        /// </summary>
        internal static void Tick()
        {
            if (_applied) Reassert();
        }

        static void Reassert()
        {
            try
            {
                var track = CameraTransform()?.parent;
                if (track != null)
                {
                    if (!_trackingCaptured)
                    {
                        _origTrackingScale = track.localScale;
                        _trackingCaptured  = true;
                    }
                    var want = _origTrackingScale * _factor;
                    if (track.localScale != want)
                        track.localScale = want;
                }

                var rig = Rig();
                if (rig != null)
                {
                    if (!_rigCaptured)
                    {
                        _origRigScale   = rig.transform.localScale;
                        _origHeadOffset = (Vector3)_rigHeadOffset.GetValue(rig);
                        _rigCaptured    = true;
                    }
                    var wantRig = _origRigScale * _factor;
                    if (rig.transform.localScale != wantRig)
                        rig.transform.localScale = wantRig;
                    _rigHeadOffset.SetValue(rig, _origHeadOffset * _factor);
                }
            }
            catch (Exception e)
            {
                // Reassert runs every frame, so a persistent failure can't be allowed to
                // log per-frame: warn once, then stand down for the rest of the session.
                _failed  = true;
                _applied = false;
                Plugin.Log.LogWarning($"[SC-VR] viewpoint scale failed, VR scaling disabled for this session: {e.GetType().Name}: {e.Message}");
            }
        }

        // --- RepoXR object resolution ---------------------------------------------------

        static object? Session() => _sessionInstance.GetValue(null);

        static Transform? CameraTransform()
        {
            var s = Session();
            if (s == null) return null;
            return _sessionCamera.GetValue(s) is Camera cam && cam != null ? cam.transform : null;
        }

        static Component? Rig()
        {
            var s = Session();
            var player = s != null ? _sessionPlayer.GetValue(s) : null;
            return player != null ? _playerRig.GetValue(player) as Component : null;
        }

        // --- Room-scale movement -------------------------------------------------------
        //
        // RepoXR.VRPlayer.HandleMovement converts physical head motion into body motion at
        // 1:1. A shrunken player should cover proportionally less ground per real step, so
        // the prefix captures the head delta RepoXR is about to apply and the postfix
        // re-issues the body move scaled by the shrink factor.

        struct MoveCapture
        {
            public bool    Valid;
            public Vector3 Head;
            public Vector3 Last;
        }

        static void EnsureMovementPatch()
        {
            if (_movementPatched) return;
            _movementPatched = true; // one attempt, success or fail
            try
            {
                if (_handleMovement == null)
                {
                    Plugin.Log.LogWarning("[SC-VR] VRPlayer.HandleMovement not found; room-scale walking won't be rescaled.");
                    return;
                }
                var player = AccessTools.TypeByName("RepoXR.Player.VRPlayer");
                _lastPosition = AccessTools.FieldRefAccess<Vector3>(player, "lastPosition");

                var self = typeof(RepoXRBridge);
                Plugin.Harmony.Patch(_handleMovement,
                    prefix:  new HarmonyMethod(self.GetMethod(nameof(MovementPrefix),  BindingFlags.Static | BindingFlags.NonPublic)),
                    postfix: new HarmonyMethod(self.GetMethod(nameof(MovementPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                Plugin.Log.LogInfo("[SC-VR] room-scale movement patch installed.");
            }
            catch (Exception e)
            {
                _lastPosition = null;
                Plugin.Log.LogWarning($"[SC-VR] movement patch failed; room-scale walking won't be rescaled. {e.GetType().Name}: {e.Message}");
            }
        }

        static void MovementPrefix(object __instance, out MoveCapture __state)
        {
            __state = default;
            if (!_applied || _lastPosition == null) return;
            try
            {
                var cam = CameraTransform();
                if (cam == null) return;
                __state.Head  = cam.localPosition;
                __state.Last  = _lastPosition(__instance);
                __state.Valid = true;
            }
            catch
            {
                // Leave Valid false; the postfix becomes a no-op.
            }
        }

        static void MovementPostfix(object __instance, MoveCapture __state)
        {
            if (!__state.Valid || !_applied || _lastPosition == null) return;
            try
            {
                // RepoXR sets lastPosition = head only on the frames it actually moved the
                // body. If lastPosition didn't land on the head we captured, it bailed out
                // early (disabled controls, position override, no tracking) -- leave it be.
                if ((_lastPosition(__instance) - __state.Head).sqrMagnitude > 1e-6f) return;
                if (__state.Last == Vector3.zero) return; // first tracked frame, no delta yet

                var movement = new Vector3(__state.Head.x - __state.Last.x, 0f, __state.Head.z - __state.Last.z);
                if (movement == Vector3.zero) return;

                var cam = CameraTransform();
                if (cam == null || cam.parent == null) return;
                var rb = PlayerController.instance != null ? PlayerController.instance.rb : null;
                if (rb == null) return;

                // RepoXR already moved the body by the full head delta; this overrides that
                // target with the size-scaled delta (MovePosition is deferred, so rb.position
                // is still the pre-move value here).
                rb.MovePosition(rb.position + cam.parent.rotation * (movement * _factor));
            }
            catch
            {
                // A failed correction just leaves RepoXR's unscaled move in place.
            }
        }
    }
}
