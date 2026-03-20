using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace ScalerCore.Handlers
{
    /// <summary>
    /// Enemy-specific scaling logic.
    /// All enemy-specific fields live in EnemyHandler.State, stored on ctrl.HandlerState.
    /// State is created ONCE in Setup and NEVER cleared or recreated.
    /// </summary>
    internal class EnemyHandler : IScaleHandler
    {
        // NavMesh speed control — only enemies use these.
        internal static readonly FieldInfo _navDefaultSpeedField =
            AccessTools.Field(typeof(EnemyNavMeshAgent), "DefaultSpeed");
        internal static readonly FieldInfo _navAgentField =
            AccessTools.Field(typeof(EnemyNavMeshAgent), "Agent");

        /// <summary>
        /// Holds all enemy-specific component references and saved originals.
        /// Created once in Setup, stored on ScaleController.HandlerState, never cleared.
        /// </summary>
        internal sealed class State
        {
            // Components
            internal EnemyRigidbody EnemyRb = null!;
            internal EnemyNavMeshAgent? NavAgent;

            // Visual
            internal Transform? AnimTarget;
            internal Vector3 AnimOriginalScale;
            internal Vector3 AnimOriginalLocalPos;
            internal EnemyBombThrowerHead? BtHead;
            internal Vector3 BtHeadOriginalScale;

            // Rigidbody original local position (for mesh Y compensation)
            internal Vector3 RbOriginalLocalPos;

            // Saved originals
            internal float OriginalDefaultSpeed;
            internal float OriginalAgentRadius;
            internal float OriginalSpeedChase;
            internal float OriginalSpeedIdle;
            internal float OriginalRotSpeedChase;
            internal float OriginalRotSpeedIdle;
            internal GrabForce? OriginalGrabForce;
            internal GrabForce? MiniGrabForce;

            // Tracking
            internal bool OriginalsCaptured;
        }

        /// <summary>
        /// Find the visual root (AnimTarget), cache NavMeshAgent, EnemyRigidbody, and BombThrowerHead.
        /// Called from ScaleController.Start() via the handler interface.
        /// Creates the State once and stores it on ctrl.HandlerState.
        /// </summary>
        public void Setup(ScaleController ctrl)
        {
            var ep = ctrl.GetComponentInParent<EnemyParent>();
            if (ep == null) return;

            var state = new State();

            // Find the visual root: the GO with the most renderers that has an
            // Animator (or BotSystemSpringPoseAnimator) and is NOT on the EnemyRigidbody GO.
            // Walk up to the highest ancestor under EnemyParent that covers more renderers
            // (catches siblings like Cleanup's separate body mesh).
            Transform? bestVisual = null;
            int bestRenderers = 0;

            void CheckVisualGO(GameObject go)
            {
                if (go.GetComponent<EnemyRigidbody>() != null) return;
                if (go.GetComponentInChildren<Renderer>() == null) return;
                // Skip IK look-at targets (Animator with no controller)
                var anim = go.GetComponent<Animator>();
                if (anim != null && anim.runtimeAnimatorController == null) return;
                int renderers = go.GetComponentsInChildren<Renderer>().Length;
                if (renderers > bestRenderers) { bestRenderers = renderers; bestVisual = go.transform; }
            }

            foreach (var anim in ep.GetComponentsInChildren<Animator>())
                CheckVisualGO(anim.gameObject);
            foreach (var bssa in ep.GetComponentsInChildren<BotSystemSpringPoseAnimator>())
                CheckVisualGO(bssa.gameObject);

            // Walk up: if parent covers more renderers (sibling meshes), adopt it.
            // Stop at EnemyParent or if parent has sibling Animator GOs (independent rigs).
            while (bestVisual != null
                   && bestVisual.parent != null
                   && bestVisual.parent != ep.transform)
            {
                int current = bestVisual.GetComponentsInChildren<Renderer>().Length;
                int parent  = bestVisual.parent.GetComponentsInChildren<Renderer>().Length;
                if (parent <= current) break;
                bool hasSiblingAnimator = false;
                foreach (Transform sib in bestVisual.parent)
                {
                    if (sib == bestVisual) continue;
                    var a = sib.GetComponent<Animator>();
                    if (a != null && a.runtimeAnimatorController != null) { hasSiblingAnimator = true; break; }
                }
                if (hasSiblingAnimator) break;
                bestVisual = bestVisual.parent;
            }

            // Fallback: direct children with renderers (spring-animated enemies with no Animator)
            if (bestVisual == null)
            {
                foreach (Transform child in ep.transform)
                {
                    if (child == ctrl._t) continue;
                    if (child.GetComponent<EnemyRigidbody>() != null) continue;
                    if (child.GetComponent<Rigidbody>() != null) continue;
                    int r = child.GetComponentsInChildren<Renderer>().Length;
                    if (r > bestRenderers) { bestRenderers = r; bestVisual = child; }
                }
            }

            if (bestVisual != null)
            {
                state.AnimTarget           = bestVisual;
                state.AnimOriginalScale    = state.AnimTarget.localScale;
                state.AnimOriginalLocalPos = state.AnimTarget.localPosition;
            }

            state.NavAgent  = ep.GetComponentInChildren<EnemyNavMeshAgent>();
            state.EnemyRb   = ctrl.GetComponent<EnemyRigidbody>();

            // BombThrower: track the throwable head (has its own Rigidbody).
            var headComp = ep.GetComponentInChildren<EnemyBombThrowerHead>(includeInactive: true);
            if (headComp != null)
            {
                state.BtHead = headComp;
                state.BtHeadOriginalScale = headComp.transform.localScale;
            }

            // PhysGrabObject is on the same GO for enemies.
            ctrl._physGrabObject = ctrl.GetComponent<PhysGrabObject>();

            state.RbOriginalLocalPos = ctrl._t.localPosition;
            ctrl.HandlerState = state;
        }

        /// <summary>
        /// NavMesh speed/radius + grab force + follow force scaling at shrink time.
        /// </summary>
        public void OnScale(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;

            if (state.NavAgent != null)
            {
                state.OriginalDefaultSpeed = (float)_navDefaultSpeedField.GetValue(state.NavAgent);
                _navDefaultSpeedField.SetValue(state.NavAgent, state.OriginalDefaultSpeed * ShrinkConfig.EnemyShrinkSpeedFactor);
                var agent = (NavMeshAgent)_navAgentField.GetValue(state.NavAgent);
                if (agent != null)
                {
                    agent.speed  *= ShrinkConfig.EnemyShrinkSpeedFactor;
                    state.OriginalAgentRadius = agent.radius;
                    agent.radius *= ShrinkConfig.Factor;
                    Plugin.Log.LogInfo($"[SC]   navSpeed {state.OriginalDefaultSpeed:F2} → {(float)_navDefaultSpeedField.GetValue(state.NavAgent):F2}  radius {state.OriginalAgentRadius:F2} → {agent.radius:F2}");
                }
            }

            if (state.EnemyRb != null)
            {
                state.OriginalGrabForce = state.EnemyRb.grabForceNeeded;
                state.MiniGrabForce = ScriptableObject.CreateInstance<GrabForce>();
                state.MiniGrabForce.amount = 0f;
                state.EnemyRb.grabForceNeeded = state.MiniGrabForce;

                // Scale PhysFollowPosition/Rotation speeds proportionally to size.
                // These forces pull the rb toward the NavMesh path every FixedUpdate.
                // At full-size values, even low-mass enemies feel too heavy to carry.
                state.OriginalSpeedChase    = state.EnemyRb.positionSpeedChase;
                state.OriginalSpeedIdle     = state.EnemyRb.positionSpeedIdle;
                state.OriginalRotSpeedChase = state.EnemyRb.rotationSpeedChase;
                state.OriginalRotSpeedIdle  = state.EnemyRb.rotationSpeedIdle;
                // Factor^2: follow force scales with both size and physical presence.
                // A 40% enemy has 16% follow force — weak enough for 0-strength grab.
                float ff = ShrinkConfig.Factor * ShrinkConfig.Factor;
                state.EnemyRb.positionSpeedChase = state.OriginalSpeedChase * ff;
                state.EnemyRb.positionSpeedIdle  = state.OriginalSpeedIdle  * ff;
                state.EnemyRb.rotationSpeedChase = state.OriginalRotSpeedChase * ff;
                state.EnemyRb.rotationSpeedIdle  = state.OriginalRotSpeedIdle  * ff;

                Plugin.Log.LogInfo($"[SC]   grabForceNeeded {(state.OriginalGrabForce != null ? state.OriginalGrabForce.amount.ToString("F2") : "null")} → 0 (instant grab)");
                Plugin.Log.LogInfo($"[SC]   posSpeedChase {state.OriginalSpeedChase:F2} → {state.EnemyRb.positionSpeedChase:F2}  posSpeedIdle {state.OriginalSpeedIdle:F2} → {state.EnemyRb.positionSpeedIdle:F2}");
            }

            state.OriginalsCaptured = true;
        }

        /// <summary>
        /// NavMesh restore + grab force + follow force restore at expand time.
        /// When isBonk is true, also Warps the agent to its current position.
        /// </summary>
        public void OnRestore(ScaleController ctrl, bool isBonk)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;

            if (state.NavAgent != null)
            {
                var agentBefore = (NavMeshAgent)_navAgentField.GetValue(state.NavAgent);
                Plugin.Log.LogInfo($"[SC]   EXPAND{(isBonk ? "(bonk)" : "")} navSpeed {(agentBefore != null ? agentBefore.speed.ToString("F2") : "N/A")} → {state.OriginalDefaultSpeed:F2}  radius {(agentBefore != null ? agentBefore.radius.ToString("F2") : "N/A")} → {state.OriginalAgentRadius:F2}");
                _navDefaultSpeedField.SetValue(state.NavAgent, state.OriginalDefaultSpeed);
                var agent = (NavMeshAgent)_navAgentField.GetValue(state.NavAgent);
                if (agent != null)
                {
                    agent.speed      = state.OriginalDefaultSpeed;
                    agent.radius     = state.OriginalAgentRadius;
                    if (isBonk && agent.isOnNavMesh)
                        agent.Warp(agent.nextPosition);
                }
            }

            if (state.EnemyRb != null)
            {
                Plugin.Log.LogInfo($"[SC]   EXPAND{(isBonk ? "(bonk)" : "")} posSpeedChase {state.EnemyRb.positionSpeedChase:F2} → {state.OriginalSpeedChase:F2}  posSpeedIdle {state.EnemyRb.positionSpeedIdle:F2} → {state.OriginalSpeedIdle:F2}");
                if (state.OriginalGrabForce != null) state.EnemyRb.grabForceNeeded = state.OriginalGrabForce;
                if (state.MiniGrabForce != null) { Object.Destroy(state.MiniGrabForce); state.MiniGrabForce = null; }
                state.EnemyRb.positionSpeedChase = state.OriginalSpeedChase;
                state.EnemyRb.positionSpeedIdle  = state.OriginalSpeedIdle;
                state.EnemyRb.rotationSpeedChase = state.OriginalRotSpeedChase;
                state.EnemyRb.rotationSpeedIdle  = state.OriginalRotSpeedIdle;
            }
        }

        /// <summary>
        /// Per-frame mass enforcement + grab boost for shrunken enemies.
        /// Called from Update() on host when IsScaled.
        /// </summary>
        public void OnUpdate(ScaleController ctrl)
        {
            // Game code overrides rb.mass (EnemyRigidbody.stunMassOverride, etc.).
            // Re-enforce our target mass every frame while shrunken.
            if (ctrl._rb != null)
            {
                float wanted = Mathf.Clamp(ctrl._originalMass * ShrinkConfig.Factor, 0.5f, ShrinkConfig.ShrunkMassCap);
                if (Mathf.Abs(ctrl._rb.mass - wanted) > 0.001f)
                    ctrl._rb.mass = wanted;
            }

            // Boost grab spring while held so it overcomes follow forces on all enemies.
            // Same API melee weapons use — no speed zeroing, no soul-ripping.
            if (ctrl._physGrabObject != null
                && ctrl._physGrabObject.playerGrabbing.Count > 0)
                ctrl._physGrabObject.OverrideMinGrabStrength(5f, 0.1f);
        }

        /// <summary>
        /// AnimTarget ratio scaling + BtHead scaling each LateUpdate.
        /// </summary>
        public void OnLateUpdate(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            if (ctrl.OriginalScale.x == 0f) return;
            if (!ctrl.IsScaled && !ctrl._transitioning) return;
            float ratio = ctrl._t.localScale.x / ctrl.OriginalScale.x;

            if (state.AnimTarget != null)
            {
                state.AnimTarget.localScale = state.AnimOriginalScale * ratio;
                // The game positions AnimTarget to track the Rigidbody each frame.
                // Some enemies naturally scale the rb-to-mesh gap, others don't.
                // Measure the actual gap vs expected gap and correct the difference.
                float actualGap = ctrl._t.localPosition.y - state.AnimTarget.localPosition.y;
                float expectedGap = state.RbOriginalLocalPos.y * ratio;
                float correction = actualGap - expectedGap;
                if (Mathf.Abs(correction) > 0.01f)
                {
                    var pos = state.AnimTarget.localPosition;
                    state.AnimTarget.localPosition = new Vector3(pos.x, pos.y + correction, pos.z);
                }
            }

            if (state.BtHead != null)
                state.BtHead.transform.localScale = state.BtHeadOriginalScale * ratio;
        }

        /// <summary>
        /// Reset AnimTarget and BtHead scales on destroy.
        /// </summary>
        public void OnDestroy(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            if (state.AnimTarget != null)
            {
                state.AnimTarget.localScale    = state.AnimOriginalScale;
                state.AnimTarget.localPosition = state.AnimOriginalLocalPos;
            }
            if (state.BtHead != null)
                state.BtHead.transform.localScale = state.BtHeadOriginalScale;
        }
    }
}
