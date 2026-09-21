using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ScalerCore;
using UnityEngine;
using UnityEngine.AI;

namespace ScalerCoreProbe
{
    /// <summary>
    /// Dev harness for the one question a player asks about a scaled monster: are its feet on
    /// the floor. It drives a singleplayer run, summons enemies through the game's own
    /// EnemyDirector setups, puts each one through a spread of ScalerCore factors and measures
    /// the gap between its feet and the floor under it twice a second.
    ///
    /// Armed only by a scalercore-probe.flag file in the profile's BepInEx/config, which it
    /// deletes on the way in, so a profile that has the DLL but no flag loads an idle plugin.
    /// </summary>
    [BepInPlugin("Vippy.ScalerCoreProbe", "ScalerCoreProbe", "0.1.0")]
    [BepInDependency("Vippy.ScalerCore")]
    public class Probe : BaseUnityPlugin
    {
        // A monster whose feet are under the floor reads as sunk; the eye catches that at a
        // couple of centimetres. Hovering is harder to see, so it gets twice the rope before
        // it counts.
        const float SinkLimit  = 0.05f;
        const float FloatLimit = 0.10f;

        const float HoldSeconds    = 3f;
        const float SampleStep     = 0.5f;
        const float SettleSeconds  = 2f;
        const float FloorCastRange = 50f;
        const float GenerateWait   = 180f;

        static string[] _levels  = { "Museum", "Manor", "Arctic", "Wizard" };
        // One per visual handler ScalerCore registers, plus a flyer as the control: two sinkers
        // that carry their own sole marker, the Loom, a plain walker on the default handler,
        // and the Floater, whose body hangs off a marker on purpose.
        static string[] _enemies = { "Slow Walker", "Runner", "Shadow", "Hunter", "Floater" };
        static float[]  _scales  = { 0.5f, 0.75f, 1.5f, 2f, 3f };
        static float    _budget  = 600f;

        internal static Level? Forced;

        ManualLogSource _log = null!;
        readonly List<Row> _rows = new();
        readonly List<string> _notes = new();
        float _deadline;

        void Awake()
        {
            _log = Logger;
            string flag = Path.Combine(Paths.ConfigPath, "scalercore-probe.flag");
            if (!File.Exists(flag))
                return;
            ReadFlag(File.ReadAllLines(flag));
            File.Delete(flag);

            gameObject.transform.parent = null;
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            new Harmony("Vippy.ScalerCoreProbe").PatchAll();
            _log.LogInfo($"[Probe] armed: levels={Join(_levels)} enemies={Join(_enemies)} scales={JoinScales()} budget={_budget:0}s");
            StartCoroutine(Run());
        }

        // The first bare line is the level list; everything else is key=value. Anything the
        // file does not mention keeps its default.
        static void ReadFlag(string[] lines)
        {
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;
                int eq = line.IndexOf('=');
                if (eq < 0)
                {
                    _levels = Split(line);
                    continue;
                }
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "levels":  _levels  = Split(value); break;
                    case "enemies": _enemies = Split(value); break;
                    case "scales":  _scales  = ParseScales(value); break;
                    case "budget":
                        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float b) && b > 0f)
                            _budget = b;
                        break;
                }
            }
        }

        static string[] Split(string value)
        {
            var parts = new List<string>();
            foreach (string piece in value.Split(','))
                if (piece.Trim().Length > 0) parts.Add(piece.Trim());
            return parts.Count > 0 ? parts.ToArray() : new[] { value.Trim() };
        }

        static float[] ParseScales(string value)
        {
            var parts = new List<float>();
            foreach (string piece in value.Split(','))
                if (float.TryParse(piece.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f) && f > 0f)
                    parts.Add(f);
            return parts.Count > 0 ? parts.ToArray() : _scales;
        }

        static string Join(string[] items) => string.Join(",", items);

        static string JoinScales()
        {
            var sb = new StringBuilder();
            foreach (float f in _scales)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(f.ToString("0.##", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        bool OutOfTime() => Time.realtimeSinceStartup > _deadline;

        IEnumerator Run()
        {
            while (RunManager.instance == null
                   || RunManager.instance.levelCurrent != RunManager.instance.levelMainMenu
                   || MainMenuOpen.instance == null
                   || StatsManager.instance == null)
                yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForSecondsRealtime(4f);

            // The clock starts at the main menu, so the game's own load does not eat the budget.
            _deadline = Time.realtimeSinceStartup + _budget;

            RunManager rm = RunManager.instance;
            var all = new List<Level>(rm.levels);
            for (int i = 0; i < _levels.Length; i++)
            {
                if (OutOfTime())
                {
                    Note($"out of time before {_levels[i]}: {_levels.Length - i} level(s) not reached");
                    break;
                }

                Level? target = all.Find(l => l.name.Contains(_levels[i]));
                if (target == null)
                {
                    Note($"no level named like {_levels[i]}");
                    continue;
                }

                Forced = target;
                if (i == 0)
                {
                    SemiFunc.MenuActionSingleplayerGame();
                }
                else
                {
                    // A death drops the run in the arena, where ChangeLevel would only reload it.
                    rm.gameOver = false;
                    rm.levelCurrent = rm.levelLobby;
                    rm.ChangeLevel(true, false, RunManager.ChangeLevelType.RunLevel);
                }
                yield return new WaitForSecondsRealtime(3f);

                float waited = 0f;
                while (waited < GenerateWait
                       && !(LevelGenerator.Instance != null && LevelGenerator.Instance.Generated && SemiFunc.RunIsLevel()))
                {
                    yield return new WaitForSecondsRealtime(1f);
                    waited += 1f;
                }
                Forced = null;
                if (waited >= GenerateWait)
                {
                    Note($"{_levels[i]}: the level never generated");
                    continue;
                }
                yield return new WaitForSecondsRealtime(6f);

                // Everything summoned here comes straight for the tester; the run has to survive
                // long enough to finish measuring.
                if (PlayerAvatar.instance != null && PlayerAvatar.instance.playerHealth != null)
                    PlayerAvatar.instance.playerHealth.godMode = true;

                yield return SweepLevel(_levels[i]);
                WriteReport(partial: true);
            }

            WriteReport(partial: false);
            _log.LogInfo("[Probe] DONE");
            yield return new WaitForSecondsRealtime(2f);
            Application.Quit();
        }

        IEnumerator SweepLevel(string level)
        {
            foreach (string want in _enemies)
            {
                if (OutOfTime())
                {
                    Note($"{level}: out of time, stopped before {want}");
                    yield break;
                }

                EnemyParent? ep = null;
                yield return Summon(level, want, found => ep = found);
                if (ep == null)
                    continue;

                yield return Sweep(level, ep);

                // Let it wander off rather than stacking every kind on the tester.
                if (ep != null && ep.Spawned)
                    ep.SpawnedTimerSet(0.1f);
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        // The game's own spawn: an EnemySetup off the director's difficulty lists handed to
        // LevelGenerator.EnemySpawn, which instantiates the prefab, marks the parent set up and
        // registers the first spawn point. Nothing here builds an enemy by hand.
        IEnumerator Summon(string level, string want, System.Action<EnemyParent?> got)
        {
            EnemyDirector? director = EnemyDirector.instance;
            if (director == null || LevelGenerator.Instance == null)
            {
                Note($"{level}: no enemy director, cannot summon {want}");
                got(null);
                yield break;
            }

            EnemySetup? pick = null;
            foreach (List<EnemySetup> list in new[] { director.enemiesDifficulty1, director.enemiesDifficulty2, director.enemiesDifficulty3 })
            {
                if (list == null) continue;
                foreach (EnemySetup setup in list)
                {
                    if (setup == null || setup.spawnObjects == null || setup.spawnObjects.Count == 0) continue;
                    PrefabRef first = setup.spawnObjects[0];
                    string prefabName = first.Prefab != null ? first.Prefab.name : first.ResourcePath ?? "";
                    if (prefabName.Contains(want)) { pick = setup; break; }
                }
                if (pick != null) break;
            }
            if (pick == null)
            {
                Note($"{level}: no enemy setup named like '{want}' in the director's lists");
                got(null);
                yield break;
            }

            Vector3 at = SpawnSpot();
            var before = new HashSet<EnemyParent>(Object.FindObjectsOfType<EnemyParent>());
            LevelGenerator.Instance.EnemySpawn(pick, at);

            EnemyParent? fresh = null;
            float t = 0f;
            while (t < 20f && fresh == null)
            {
                yield return new WaitForSecondsRealtime(1f);
                t += 1f;
                foreach (EnemyParent p in Object.FindObjectsOfType<EnemyParent>())
                    if (!before.Contains(p) && p.Enemy != null) { fresh = p; break; }
            }
            if (fresh == null)
            {
                Note($"{level}: {want} never appeared after {t:0}s");
                got(null);
                yield break;
            }
            if (!director.enemiesSpawned.Contains(fresh))
                director.enemiesSpawned.Add(fresh);

            // A fresh one can drop straight back into its despawn; hold it up until it stays up.
            float up = 0f;
            while (up < 20f && (!fresh.Spawned || fresh.Enemy == null
                                || fresh.Enemy.CurrentState == EnemyState.Despawn
                                || fresh.Enemy.CurrentState == EnemyState.Spawn))
            {
                fresh.SpawnedTimerSet(600f);
                fresh.SpawnedTimerPause(600f);
                yield return new WaitForSecondsRealtime(1f);
                up += 1f;
            }
            fresh.SpawnedTimerSet(600f);
            fresh.SpawnedTimerPause(600f);
            _log.LogInfo($"[Probe] {level} summoned {fresh.enemyName} from {pick.name} at {at} after {t:0}s, up after {up:0}s more");
            got(fresh);
        }

        // Far enough from the tester that the walk over is the enemy's own, close enough to stay
        // inside the generated level.
        static Vector3 SpawnSpot()
        {
            PlayerAvatar? me = PlayerAvatar.instance;
            Vector3 home = me != null ? me.transform.position : Vector3.zero;
            LevelPoint point = SemiFunc.LevelPointGetFurthestFromPlayer(home, 5f);
            Vector3 at = point != null ? point.transform.position : home;
            return NavMesh.SamplePosition(at, out NavMeshHit hit, 5f, NavMesh.AllAreas) ? hit.position : at;
        }

        IEnumerator Sweep(string level, EnemyParent ep)
        {
            GameObject? body = BodyOf(ep);
            string name = string.IsNullOrEmpty(ep.enemyName) ? ep.gameObject.name : ep.enemyName;
            if (body == null)
            {
                Note($"{level}: {name} has no ScaleController, nothing to measure");
                yield break;
            }

            bool flyer = IsFlyer(ep, out string why);
            if (flyer)
                _log.LogInfo($"[Probe] {level} {name}: airborne ({why}), its rows are recorded but do not decide the verdict");

            foreach (float factor in _scales)
            {
                if (OutOfTime())
                {
                    Note($"{level}: out of time, stopped {name} before x{factor.ToString("0.##", CultureInfo.InvariantCulture)}");
                    yield break;
                }
                if (ep == null || ep.Enemy == null)
                {
                    Note($"{level}: {name} was gone before x{factor.ToString("0.##", CultureInfo.InvariantCulture)}");
                    yield break;
                }

                // The base a real caller would pass. Growth matters beyond its name: it caps the
                // physical body at 1.4 while the visual climbs to the full factor, so a grown
                // enemy's colliders and nav agent stop moving long before its feet do. That is
                // the arrangement the grounding has to keep standing, and testing grow factors
                // off Default would never exercise it.
                bool grow = factor > 1f;
                var options = grow ? ScaleOptions.Growth : ScaleOptions.Default;
                options.Factor = factor;
                options.Duration = 0f;
                options.AllowedTargets = ScaleTargets.Enemies;

                if (!ScaleManager.ApplyIfNotScaled(body, options))
                {
                    Note($"{level}: {name} refused x{factor.ToString("0.##", CultureInfo.InvariantCulture)} (already scaled, locked, or filtered)");
                    ScaleManager.Restore(body);
                    yield return new WaitForSecondsRealtime(SettleSeconds);
                    continue;
                }

                ScaleController? ctrl = ScaleManager.GetController(body);
                yield return new WaitForSecondsRealtime(SettleSeconds);

                var row = new Row
                {
                    Level = level, Enemy = name, Scale = factor,
                    Flyer = flyer, FlyerReason = why,
                    Preset = grow ? "Growth" : "Default",
                    PhysicalCap = options.EnemyPhysicalFactorCap,
                };
                float held = 0f;
                while (held < HoldSeconds)
                {
                    Measure(ep, ctrl, row);
                    yield return new WaitForSecondsRealtime(SampleStep);
                    held += SampleStep;
                }

                row.Judge(SinkLimit, FloatLimit);
                _rows.Add(row);
                _log.LogInfo($"[Probe] {row.Level} {row.Enemy} x{row.ScaleText} ({row.PresetText}): {row.Verdict} feet {row.FeetText} agent {row.AgentText} mesh {row.MeshText} from {row.Source}, {row.Samples} samples, {row.Skipped} skipped");

                ScaleManager.Restore(body);
                yield return new WaitForSecondsRealtime(SettleSeconds);
                if (ctrl != null && ctrl.IsScaled)
                    Note($"{level}: {name} was still scaled {SettleSeconds:0}s after the restore of x{row.ScaleText}");
            }
        }

        // ScalerCore attaches its controller on the EnemyRigidbody's object, a child of the parent.
        static GameObject? BodyOf(EnemyParent ep)
        {
            var ctrl = ep.GetComponentInChildren<ScaleController>(includeInactive: true);
            if (ctrl != null) return ctrl.gameObject;
            return ep.Enemy != null && ep.Enemy.HasRigidbody ? ep.Enemy.Rigidbody.gameObject : null;
        }

        // Airborne kinds are asked the same questions but never decide the verdict: their feet
        // are meant to be off the floor. EnemyRigidbody.gravity is the prefab's own answer; the
        // four brains below hold themselves up in their state machines whatever that flag says
        // (the Floater hangs its body off a sole marker, the Duck has FlyBackToNavmesh states,
        // the Banger drives its own transform, the Ceiling Eye rides a cast into the ceiling).
        static bool IsFlyer(EnemyParent ep, out string why)
        {
            if (ep.GetComponentInChildren<EnemyFloater>(true) != null)    { why = "EnemyFloater"; return true; }
            if (ep.GetComponentInChildren<EnemyDuck>(true) != null)       { why = "EnemyDuck"; return true; }
            if (ep.GetComponentInChildren<EnemyBang>(true) != null)       { why = "EnemyBang"; return true; }
            if (ep.GetComponentInChildren<EnemyCeilingEye>(true) != null) { why = "EnemyCeilingEye"; return true; }
            Enemy? e = ep.Enemy;
            if (e != null && e.HasRigidbody && !e.Rigidbody.gravity)      { why = "EnemyRigidbody.gravity off"; return true; }
            why = "";
            return false;
        }

        // ScalerCore keeps the mesh transform and the sole marker on an internal handler state.
        // The probe has to read exactly those two to measure what the grounding adjusts, so it
        // reaches in rather than measuring some other transform that happens to be nearby.
        static FieldInfo? _handlerStateField;
        static FieldInfo? _animTargetField;
        static FieldInfo? _groundMarkerField;

        static void Resolve(ScaleController? ctrl, out Transform? animTarget, out Transform? marker)
        {
            animTarget = null;
            marker = null;
            if (ctrl == null) return;
            _handlerStateField ??= AccessTools.Field(typeof(ScaleController), "HandlerState");
            object? state = _handlerStateField?.GetValue(ctrl);
            if (state == null) return;
            _animTargetField   ??= AccessTools.Field(state.GetType(), "AnimTarget");
            _groundMarkerField ??= AccessTools.Field(state.GetType(), "GroundMarker");
            animTarget = _animTargetField?.GetValue(state) as Transform;
            marker     = _groundMarkerField?.GetValue(state) as Transform;
        }

        void Measure(EnemyParent ep, ScaleController? ctrl, Row row)
        {
            Enemy? e = ep != null ? ep.Enemy : null;
            if (e == null) { row.Skipped++; row.Reasons.Add("gone"); return; }

            // A stunned, spawning or despawning enemy is not standing on anything in particular,
            // and one in the air between a jump and a landing is off the floor by design.
            if (e.CurrentState == EnemyState.Stunned) { row.Skipped++; row.Reasons.Add("stunned"); return; }
            if (e.CurrentState == EnemyState.Spawn || e.CurrentState == EnemyState.Despawn) { row.Skipped++; row.Reasons.Add("spawning"); return; }
            if (e.HasJump && (e.Jump.jumping || e.Jump.jumpingDelay || e.Jump.landDelay)) { row.Skipped++; row.Reasons.Add("jumping"); return; }
            if (e.HasGrounded && !e.Grounded.grounded) { row.Skipped++; row.Reasons.Add("airborne"); return; }

            Resolve(ctrl, out Transform? animTarget, out Transform? marker);
            Transform visual = animTarget != null ? animTarget : ep!.transform;

            // The same renderer choice VisualGrounding makes: character mesh only, because
            // shadows and ground decals hang below the feet and would read as a float.
            float lowest = float.MaxValue;
            Renderer[] renderers = visual.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (renderers.Length == 0)
                renderers = visual.GetComponentsInChildren<MeshRenderer>();
            foreach (Renderer r in renderers)
                if (r != null && r.enabled) lowest = Mathf.Min(lowest, r.bounds.min.y);
            bool hasRender = lowest < float.MaxValue;

            bool hasMarker = marker != null;
            if (!hasMarker && !hasRender) { row.Skipped++; row.Reasons.Add("nothing rendered"); return; }

            Transform centre = e.CenterTransform != null ? e.CenterTransform : ep!.transform;
            if (!FloorUnder(ep!, centre.position, out float floorY)) { row.Skipped++; row.Reasons.Add("no floor under it"); return; }

            // The marker wins when the enemy carries one, because that is the point its own
            // navigation steers from and the one ScalerCore grounds to.
            float feet = hasMarker ? marker!.position.y : lowest;
            row.Source = hasMarker ? "feet marker" : "renderers";
            row.Add(feet - floorY);

            if (e.HasNavMeshAgent && e.NavMeshAgent.Agent != null && e.NavMeshAgent.Agent.enabled)
                row.AddAgent(e.NavMeshAgent.Agent.transform.position.y - floorY);

            if (animTarget != null)
                row.AddMesh(animTarget.localPosition.y);

            _log.LogDebug($"[Probe] {row.Level} {row.Enemy} x{row.ScaleText} feet={feet - floorY:0.000} floor={floorY:0.00} source={row.Source} meshLocalY={(animTarget != null ? animTarget.localPosition.y : 0f):0.000} state={e.CurrentState}");
        }

        // Straight down on Default, the layer the game's own floor casts use, skipping anything
        // belonging to this enemy so its own collider is not mistaken for ground.
        static bool FloorUnder(EnemyParent ep, Vector3 from, out float floorY)
        {
            floorY = 0f;
            RaycastHit[] hits = Physics.RaycastAll(from + Vector3.up * 0.1f, Vector3.down, FloorCastRange,
                                                   LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore);
            Transform root = ep.transform.root;
            float best = float.MinValue;
            bool found = false;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider == null || hit.collider.transform.root == root) continue;
                // Nearest surface below the body: the highest hit point still under the cast.
                if (hit.point.y > best) { best = hit.point.y; found = true; }
            }
            floorY = best;
            return found;
        }

        void Note(string text)
        {
            _notes.Add(text);
            _log.LogWarning($"[Probe] {text}");
        }

        void WriteReport(bool partial)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ScalerCore grounding probe");
            sb.AppendLine();
            sb.AppendLine($"Levels: {Join(_levels)}  ");
            sb.AppendLine($"Enemies: {Join(_enemies)}  ");
            sb.AppendLine($"Scales: {JoinScales()}  ");
            sb.AppendLine($"Sink limit {SinkLimit:0.00} m below the floor, float limit {FloatLimit:0.00} m above it, {HoldSeconds:0}s held per scale, sampled every {SampleStep:0.0}s.");
            sb.AppendLine();
            sb.AppendLine("Feet offset is the enemy's feet minus the floor under it: the sole marker when the enemy");
            sb.AppendLine("carries one, otherwise the lowest point of its character renderers. Negative is sunk into");
            sb.AppendLine("the floor, positive is hovering over it. Agent offset is the NavMeshAgent's own height over");
            sb.AppendLine("the same floor. Mesh Y is the local height ScalerCore's grounding writes on the mesh.");
            sb.AppendLine("Shrink factors go through ScaleOptions.Default and grow factors through ScaleOptions.Growth,");
            sb.AppendLine("which is what a real caller passes: Growth caps the physical body while the visual keeps");
            sb.AppendLine("climbing, so the grounding has to hold a mesh that is larger than its own colliders.");
            sb.AppendLine();
            sb.AppendLine("| Level | Enemy | Scale | Preset | Samples | Feet min/max (m) | Agent min/max (m) | Mesh Y min/max | From | Verdict |");
            sb.AppendLine("|---|---|---:|---|---:|---|---|---|---|---|");

            int fails = 0;
            foreach (Row row in _rows)
            {
                if (row.Verdict.StartsWith("SINK") || row.Verdict.StartsWith("FLOAT")) fails++;
                sb.AppendLine($"| {row.Level} | {row.Enemy} | x{row.ScaleText} | {row.PresetText} | {row.Samples} ({row.Skipped} skipped) | {row.FeetText} | {row.AgentText} | {row.MeshText} | {row.Source} | {row.Verdict} |");
            }

            sb.AppendLine();
            if (_notes.Count > 0)
            {
                sb.AppendLine("## Notes");
                sb.AppendLine();
                foreach (string note in _notes)
                    sb.AppendLine($"- {note}");
                sb.AppendLine();
            }

            int measured = 0;
            foreach (Row row in _rows)
                if (row.Samples > 0) measured++;

            if (partial)
                sb.AppendLine($"IN PROGRESS: {measured} measured combination(s), {fails} out of bounds so far.");
            else if (measured == 0)
                sb.AppendLine("FAIL: nothing was measured. Read BepInEx/LogOutput.log for what the run did instead.");
            else if (fails == 0)
                sb.AppendLine($"PASS: {measured} enemy/scale combinations, every one of them inside {SinkLimit:0.00} m below and {FloatLimit:0.00} m above the floor.");
            else
                sb.AppendLine($"FAIL: {fails} of {measured} enemy/scale combinations left the floor. The rows above name them.");

            string path = Path.Combine(Paths.BepInExRootPath,
                partial ? "scalercore-probe-report.partial.md" : "scalercore-probe-report.md");
            File.WriteAllText(path, sb.ToString());
            if (!partial)
            {
                string stale = Path.Combine(Paths.BepInExRootPath, "scalercore-probe-report.partial.md");
                if (File.Exists(stale)) File.Delete(stale);
            }
            _log.LogInfo($"[Probe] report -> {path}");
        }

        sealed class Row
        {
            internal string Level = "";
            internal string Enemy = "";
            internal float  Scale;
            internal bool   Flyer;
            internal string FlyerReason = "";
            internal string Source = "renderers";
            internal string Preset = "Default";
            internal float  PhysicalCap;
            internal string Verdict = "NO DATA";
            internal int    Samples;
            internal int    Skipped;
            internal readonly HashSet<string> Reasons = new();

            float _feetMin  = float.MaxValue, _feetMax  = float.MinValue;
            float _agentMin = float.MaxValue, _agentMax = float.MinValue;
            float _meshMin  = float.MaxValue, _meshMax  = float.MinValue;

            internal string ScaleText => Scale.ToString("0.##", CultureInfo.InvariantCulture);
            internal string PresetText => PhysicalCap > 0f && Scale > PhysicalCap
                ? $"{Preset}, body capped at x{PhysicalCap.ToString("0.##", CultureInfo.InvariantCulture)}"
                : Preset;
            internal string FeetText  => Range(_feetMin, _feetMax);
            internal string AgentText => Range(_agentMin, _agentMax);
            internal string MeshText  => Range(_meshMin, _meshMax);

            static string Range(float min, float max) =>
                min > max ? "-" : $"{min.ToString("0.000", CultureInfo.InvariantCulture)} / {max.ToString("0.000", CultureInfo.InvariantCulture)}";

            internal void Add(float offset)
            {
                Samples++;
                _feetMin = Mathf.Min(_feetMin, offset);
                _feetMax = Mathf.Max(_feetMax, offset);
            }

            internal void AddAgent(float offset)
            {
                _agentMin = Mathf.Min(_agentMin, offset);
                _agentMax = Mathf.Max(_agentMax, offset);
            }

            internal void AddMesh(float localY)
            {
                _meshMin = Mathf.Min(_meshMin, localY);
                _meshMax = Mathf.Max(_meshMax, localY);
            }

            internal void Judge(float sinkLimit, float floatLimit)
            {
                if (Samples == 0)
                {
                    Verdict = Reasons.Count > 0 ? $"NO DATA ({string.Join(", ", new List<string>(Reasons).ToArray())})" : "NO DATA";
                    return;
                }
                if (_feetMin < -sinkLimit)
                {
                    Verdict = Flyer ? $"sink {-_feetMin:0.00} m, airborne ({FlyerReason})" : $"SINK {-_feetMin:0.00} m";
                    return;
                }
                if (_feetMax > floatLimit)
                {
                    Verdict = Flyer ? $"float {_feetMax:0.00} m, airborne ({FlyerReason})" : $"FLOAT {_feetMax:0.00} m";
                    return;
                }
                Verdict = "ok";
            }
        }
    }

    // The probe picks the level instead of the run: SetRunLevel is where the game decides, so
    // overriding it there beats fighting the choice afterwards. Deep enough into a run for the
    // bigger grid and several extraction points.
    [HarmonyPatch(typeof(RunManager), "SetRunLevel")]
    [HarmonyPriority(Priority.First)]
    internal static class ForceLevelPatch
    {
        static bool Prefix(RunManager __instance)
        {
            if (Probe.Forced == null)
                return true;
            __instance.levelCurrent = Probe.Forced;
            if (__instance.levelsCompleted < 12)
                __instance.levelsCompleted = 12;
            return false;
        }
    }
}
