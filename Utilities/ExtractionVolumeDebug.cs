using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ScalerCore.Utilities
{
    // Debug overlay for extraction membership. Draws two things:
    //
    //  1. The extraction point detection volumes (blue, the RoomVolume gizmo color).
    //     Membership is decided by RoomVolumeCheck overlapping a RoomVolume whose
    //     Extraction flag is set (RoomVolumeCheck.CheckSet), and each such volume is
    //     built from child BoxColliders. The volume lives on ExtractionPoint.roomVolume,
    //     a GameObject the game keeps INACTIVE until the point activates mid-run, so we
    //     reach it through the ExtractionPoint reference (FindObjectsOfType<RoomVolume>
    //     would skip it) and read its colliders with includeInactive.
    //
    //  2. Each valuable's probe box (green normally, RED when it currently reads as
    //     in-extraction). This is the exact box RoomVolumeCheck.CheckSet feeds to
    //     Physics.OverlapBox:
    //         center = transform.position + transform.rotation * CheckPosition
    //         size   = currentSize (or transform.localScale if currentSize is zero)
    //         orient = transform.rotation
    //     ScaleController scales both currentSize and CheckPosition with a valuable's
    //     factor so the probe tracks the shrunken body. This overlay is how that was
    //     verified: before the offset was scaled too, the probe kept a full-size
    //     CheckPosition and drifted off a shrunken valuable into the zone, reading as
    //     inside while the body wasn't. Drawing it makes any size/offset mismatch obvious.
    //
    // A dev tool, not a player setting: Plugin only attaches this component when armed
    // via SCALERCORE_DEBUG=1 or the scalercore_debug sentinel file in BepInEx/config
    // (see Plugin.DebugDraw), so it never reaches a shipped build's config UI. Only
    // renders in an actual gameplay level and clears its caches otherwise, so nothing
    // leaks into the truck or menu.
    public class ExtractionVolumeDebug : MonoBehaviour
    {
        // Extraction volume colliders are static geometry: cache once per level.
        readonly List<(BoxCollider col, Color color)> _boxes = new();
        // Valuable probes move and spawn/despawn: refresh the ref list each tick,
        // recompute the box live at draw time.
        readonly List<RoomVolumeCheck> _checks = new();

        bool _built;
        float _checkTimer;
        Material? _lineMat;

        // Scratch buffer for the 8 corners of a box, reused every draw.
        readonly Vector3[] _corners = new Vector3[8];

        static readonly Color ProbeIn = Color.red;
        static readonly Color ProbeOut = new(0.2f, 1f, 0.4f, 1f);

        void Update()
        {
            // Cheap state poll: the toggle is flipped live and levels load/unload,
            // so re-evaluate twice a second rather than every frame.
            _checkTimer -= Time.deltaTime;
            if (_checkTimer > 0f) return;
            _checkTimer = 0.5f;

            bool shouldShow = SemiFunc.RunIsLevel()
                && LevelGenerator.Instance != null
                && LevelGenerator.Instance.Generated;

            if (shouldShow)
            {
                if (!_built) RebuildVolumes();
                RefreshChecks();
            }
            else if (_built || _checks.Count > 0)
            {
                Clear();
            }
        }

        void RebuildVolumes()
        {
            _boxes.Clear();
            foreach (var ep in FindObjectsOfType<ExtractionPoint>())
            {
                if (ep == null || ep.roomVolume == null) continue;

                // The roomVolume GameObject is usually inactive; read its colliders
                // and gizmo color through the reference with includeInactive.
                var rv = ep.roomVolume.GetComponentInChildren<RoomVolume>(true);
                Color color = rv != null ? rv.Color : Color.cyan;
                color.a = 1f;

                foreach (var bc in ep.roomVolume.GetComponentsInChildren<BoxCollider>(true))
                    if (bc != null) _boxes.Add((bc, color));
            }

            // Only latch once we actually have geometry. Extraction points can lag a
            // frame behind LevelGenerator.Generated, so an empty pass retries next tick.
            _built = _boxes.Count > 0;
            if (_built)
                Plugin.Log.LogInfo($"[ExtractionDebug] showing {_boxes.Count} extraction volume collider(s)");
        }

        void RefreshChecks()
        {
            _checks.Clear();
            // Only valuables (RoomVolumeCheck also rides players and death heads).
            foreach (var c in FindObjectsOfType<RoomVolumeCheck>())
                if (c != null && c.valuableObject != null) _checks.Add(c);
        }

        void Clear()
        {
            _boxes.Clear();
            _checks.Clear();
            _built = false;
        }

        void OnRenderObject()
        {
            if (_boxes.Count == 0 && _checks.Count == 0) return;

            // Draw into the main world camera ONLY. REPO composites the world through
            // more than one camera (a viewmodel/overlay camera renders on top of the
            // main one), and OnRenderObject fires once per camera. Drawing in every
            // one ghosts each box: the same world-space lines project from slightly
            // different camera positions, obvious up close, negligible far away.
            // GameDirector.MainCamera is the canonical world view (SemiFunc.MainCamera).
            GameDirector gd = GameDirector.instance;
            if (gd == null || Camera.current != gd.MainCamera) return;

            EnsureMaterial();
            _lineMat!.SetPass(0);

            GL.PushMatrix();
            GL.Begin(GL.LINES);

            foreach (var (col, color) in _boxes)
            {
                if (col == null) continue;
                GL.Color(color);
                EmitBoxFromCollider(col);
            }

            foreach (var c in _checks)
            {
                if (c == null) continue;
                GL.Color(c.inExtractionPoint ? ProbeIn : ProbeOut);
                EmitProbeBox(c);
            }

            GL.End();
            GL.PopMatrix();
        }

        // Extraction volume box: a BoxCollider, so transform scale and the local
        // center offset both matter. TransformPoint folds in lossyScale exactly.
        void EmitBoxFromCollider(BoxCollider col)
        {
            Transform t = col.transform;
            Vector3 c = col.center;
            Vector3 h = col.size * 0.5f;
            for (int i = 0; i < 8; i++)
                _corners[i] = t.TransformPoint(c + Vector3.Scale(h, Signs(i)));
            EmitEdges();
        }

        // Valuable probe box: matches RoomVolumeCheck.CheckSet exactly. The size comes
        // from currentSize (NOT the transform scale), so this is what reveals the bug.
        void EmitProbeBox(RoomVolumeCheck check)
        {
            Transform t = check.transform;
            Vector3 center = t.position + t.rotation * check.CheckPosition;
            Vector3 size = check.currentSize == Vector3.zero ? t.localScale : check.currentSize;
            Vector3 h = size * 0.5f;
            Quaternion rot = t.rotation;
            for (int i = 0; i < 8; i++)
                _corners[i] = center + rot * Vector3.Scale(h, Signs(i));
            EmitEdges();
        }

        // The 12 edges of _corners: connect every pair that differs in exactly one
        // axis bit. j > i keeps each edge unique.
        void EmitEdges()
        {
            for (int i = 0; i < 8; i++)
                for (int b = 0; b < 3; b++)
                {
                    int j = i ^ (1 << b);
                    if (j > i)
                    {
                        GL.Vertex(_corners[i]);
                        GL.Vertex(_corners[j]);
                    }
                }
        }

        // Corner sign vector from the low 3 bits: 0 -> -1, 1 -> +1 per axis.
        static Vector3 Signs(int i) => new(
            (i & 1) == 0 ? -1f : 1f,
            (i & 2) == 0 ? -1f : 1f,
            (i & 4) == 0 ? -1f : 1f);

        void EnsureMaterial()
        {
            if (_lineMat != null) return;
            // Unity's built-in vertex-colored line shader. One material, reused for
            // every box, so nothing leaks per-frame. ZTest Always draws the boundary
            // through walls so the zone is visible even when geometry occludes it.
            _lineMat = new Material(Shader.Find("Hidden/Internal-Colored"))
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _lineMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _lineMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _lineMat.SetInt("_Cull", (int)CullMode.Off);
            _lineMat.SetInt("_ZWrite", 0);
            _lineMat.SetInt("_ZTest", (int)CompareFunction.Always);
        }
    }
}
