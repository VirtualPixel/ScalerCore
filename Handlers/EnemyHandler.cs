using UnityEngine;

namespace ScalerCore.Handlers
{
    /// <summary>
    /// Enemy-specific scaling logic.
    /// All enemy-specific fields live in EnemyHandler.State, stored on ctrl.HandlerState.
    /// State is created ONCE in Setup and NEVER cleared or recreated.
    /// </summary>
    internal class EnemyHandler : IScaleHandler
    {

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

            // Per-enemy visual scaling strategy
            internal IEnemyVisualHandler? VisualHandler;
            internal object? VisualState;
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
            // Stop at EnemyParent, if parent has sibling Animator GOs (independent rigs),
            // or if a sibling has EnemyRigidbody (scaling the parent would cascade to the
            // physics body, causing double-scaling — HeartHugger, Shadow/Loom hit this).
            while (bestVisual != null
                   && bestVisual.parent != null
                   && bestVisual.parent != ep.transform)
            {
                int current = bestVisual.GetComponentsInChildren<Renderer>().Length;
                int parent  = bestVisual.parent.GetComponentsInChildren<Renderer>().Length;
                if (parent <= current) break;
                bool stopWalkUp = false;
                foreach (Transform sib in bestVisual.parent)
                {
                    if (sib == bestVisual) continue;
                    var a = sib.GetComponent<Animator>();
                    if (a != null && a.runtimeAnimatorController != null) { stopWalkUp = true; break; }
                    if (sib.GetComponent<EnemyRigidbody>() != null) { stopWalkUp = true; break; }
                }
                if (stopWalkUp) break;
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
            else
            {
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

            // Resolve per-enemy visual handler by internal name.
            string enemyName = EnemyVisualRegistry.ExtractEnemyName(ep);
            state.VisualHandler = EnemyVisualRegistry.Resolve(enemyName);
            state.VisualState = state.VisualHandler.Setup(ctrl, state, ep);
            Plugin.Log.LogInfo($"[SC]   visualHandler={state.VisualHandler.GetType().Name} for '{enemyName}'");

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
                state.OriginalDefaultSpeed = state.NavAgent.DefaultSpeed;
                state.NavAgent.DefaultSpeed = state.OriginalDefaultSpeed * ctrl._options.SpeedFactor;
                var agent = state.NavAgent.Agent;
                if (agent != null)
                {
                    agent.speed  *= ctrl._options.SpeedFactor;
                    state.OriginalAgentRadius = agent.radius;
                    agent.radius *= ctrl._options.Factor;
                    Plugin.Log.LogInfo($"[SC]   navSpeed {state.OriginalDefaultSpeed:F2} → {state.NavAgent.DefaultSpeed:F2}  radius {state.OriginalAgentRadius:F2} → {agent.radius:F2}");
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
                float ff = ctrl._options.Factor * ctrl._options.Factor;
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
                var agentBefore = state.NavAgent.Agent;
                Plugin.Log.LogInfo($"[SC]   EXPAND{(isBonk ? "(bonk)" : "")} navSpeed {(agentBefore != null ? agentBefore.speed.ToString("F2") : "N/A")} → {state.OriginalDefaultSpeed:F2}  radius {(agentBefore != null ? agentBefore.radius.ToString("F2") : "N/A")} → {state.OriginalAgentRadius:F2}");
                state.NavAgent.DefaultSpeed = state.OriginalDefaultSpeed;
                var agent = state.NavAgent.Agent;
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

            // Visual handler restore — only after the transition animation completes.
            // During _transitioning, OnLateUpdate still runs and manages the visual.
            // Calling OnRestore mid-transition would reset localPosition and flicker.
            if (!ctrl._transitioning)
                state.VisualHandler?.OnRestore(ctrl, state, state.VisualState);
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
                float wanted = Mathf.Clamp(ctrl._originalMass * ctrl._options.Factor, 0.5f, ctrl._options.MassCap);
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
        /// Delegate visual scaling to per-enemy visual handler each LateUpdate.
        /// </summary>
        public void OnLateUpdate(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;
            if (ctrl.OriginalScale.x == 0f) return;
            if (!ctrl.IsScaled && !ctrl._transitioning) return;
            float ratio = ctrl._t.localScale.x / ctrl.OriginalScale.x;

            if (state.VisualHandler != null)
                state.VisualHandler.OnLateUpdate(ctrl, state, state.VisualState, ratio);
        }

        /// <summary>
        /// Delegate visual restore to per-enemy visual handler on destroy.
        /// </summary>
        public void OnDestroy(ScaleController ctrl)
        {
            var state = (State?)ctrl.HandlerState;
            if (state == null) return;

            if (state.VisualHandler != null)
                state.VisualHandler.OnRestore(ctrl, state, state.VisualState);
        }
    }
}
