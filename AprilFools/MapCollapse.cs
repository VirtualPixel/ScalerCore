using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Audio;

namespace ScalerCore.AprilFools
{
    // Shrinks the level geometry over 90 seconds when a shrink bullet hits the map.
    // Synced via Photon RaiseEvent — each client runs the coroutine locally.
    // Implementations call OnMapHit() to trigger; ScalerCore doesn't gate this.
    public class MapCollapse : MonoBehaviour, IOnEventCallback
    {
        const float Factor   = 0.02f;
        const float Duration = 90f;
        const byte EvtStart   = 198;
        const byte EvtRequest = 199;

        static MapCollapse? _instance;

        Transform? _level;
        Vector3 _origScale, _pivot;
        bool _shrinking, _shrunken;
        Coroutine? _routine;
        bool _registered;

        readonly List<(Transform t, Transform parent)> _detached = new();
        readonly List<Light> _lights = new();
        readonly List<(Color c, float i)> _origLights = new();
        readonly List<(NavMeshAgent agent, float speed)> _enemySpeeds = new();
        readonly List<(EnemyParent ep, EnemyState state)> _enemyStates = new();

        TruckHealer? _healer;
        GameObject? _alarmGO;
        AudioSource? _loopSrc, _beepSrc, _truckSrc, _sirenSrc;
        AudioClip? _beepClip, _sirenClip;
        float _nextMsg, _nextGlitch, _nextScare;
        bool _lastBlinkOn;
        int _lastCountdown = -1;
        float _origFogEnd, _origFogStart, _origFov;

        void Awake() => _instance = this;

        void OnDestroy()
        {
            if (_registered) PhotonNetwork.RemoveCallbackTarget(this);
        }

        void EnsureRegistered()
        {
            if (_registered) return;
            _registered = true;
            PhotonNetwork.AddCallbackTarget(this);
        }

        void Update()
        {
            if (LevelGenerator.Instance != null && LevelGenerator.Instance.Generated)
                EnsureRegistered();
        }

        /// <summary>
        /// Trigger the map collapse event. Call from implementation hit patches
        /// after checking your own config. Safe to call multiple times — ignored
        /// if a collapse is already running or completed.
        /// </summary>
        public static void OnMapHit()
        {
            if (_instance == null || _instance._shrinking || _instance._shrunken) return;
            if (LevelGenerator.Instance == null || !LevelGenerator.Instance.Generated) return;

            _instance.EnsureRegistered();
            if (SemiFunc.IsMasterClientOrSingleplayer())
                _instance.BroadcastStart();
            else if (SemiFunc.IsMultiplayer())
                PhotonNetwork.RaiseEvent(EvtRequest, null,
                    new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient }, SendOptions.SendReliable);
        }

        internal static void OnLevelChange()
        {
            if (_instance != null) _instance.Restore();
        }

        // --- network ---------------------------------------------------------

        void BroadcastStart()
        {
            EnsureRegistered();
            if (SemiFunc.IsMultiplayer())
                PhotonNetwork.RaiseEvent(EvtStart, null,
                    new RaiseEventOptions { Receivers = ReceiverGroup.All }, SendOptions.SendReliable);
            else
                Begin();
        }

        public void OnEvent(EventData evt)
        {
            if (evt.Code == EvtStart && !_shrinking && !_shrunken)
                Begin();
            else if (evt.Code == EvtRequest && SemiFunc.IsMasterClientOrSingleplayer()
                     && !_shrinking && !_shrunken)
                BroadcastStart();
        }

        // --- setup -----------------------------------------------------------

        void Begin()
        {
            var root = LevelGenerator.Instance.transform.parent;
            _level = root?.Find("Level");
            if (_level == null) return;

            _origScale = _level.localScale;
            _pivot     = _level.position;
            _shrinking = true;
            _nextMsg = 0f;
            _lastCountdown = -1;

            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(Run());
        }

        void DetachPhysObjects()
        {
            _detached.Clear();
            foreach (var pgo in _level!.GetComponentsInChildren<PhysGrabObject>(true))
            {
                _detached.Add((pgo.transform, pgo.transform.parent));
                pgo.transform.SetParent(null, true);
            }
        }

        // --- lights & sound --------------------------------------------------

        void SetupLightsAndSound()
        {
            _lights.Clear(); _origLights.Clear();

            foreach (var ep in FindObjectsOfType<ExtractionPoint>())
                GrabLights(ep.gameObject);

            _healer = TruckHealer.instance;
            if (_healer != null)
            {
                if (_healer.healerLight != null)
                {
                    _lights.Add(_healer.healerLight);
                    _origLights.Add((_healer.healerLight.color, _healer.healerLight.intensity));
                    _healer.healerLight.color = Color.red;
                    _healer.healerLight.intensity = 5f;
                    _healer.healerLight.enabled = true;
                }
                if (_healer.transform.parent != null) GrabLights(_healer.transform.parent.gameObject);
            }

            if (TruckScreenText.instance?.background != null)
                TruckScreenText.instance.background.color = TruckScreenText.instance.evilBackgroundColor;

            SetupAudio();
        }

        void GrabLights(GameObject root)
        {
            foreach (var l in root.GetComponentsInChildren<Light>(true))
            {
                if (_lights.Contains(l)) continue;
                _lights.Add(l);
                _origLights.Add((l.color, l.intensity));
                l.color = Color.red;
                l.intensity = Mathf.Max(l.intensity * 3f, 5f);
                l.gameObject.SetActive(true);
            }
        }

        static AudioMixerGroup? SfxMixerGroup =>
            AudioManager.instance != null ? AudioManager.instance.SoundMasterGroup : null;

        void SetupAudio()
        {
            var extraction = FindObjectOfType<ExtractionPoint>();
            var exSnd = SoundsFrom(extraction);
            var trSnd = SoundsFrom(_healer);

            _alarmGO = new GameObject("MapCollapse_Audio");
            _alarmGO.transform.SetParent(extraction != null ? extraction.transform : transform);
            _alarmGO.transform.localPosition = Vector3.zero;

            var loopClip = Clip(exSnd, "soundWarningLightsLoop") ?? Clip(exSnd, "soundAlarm");
            if (loopClip != null)
            {
                _loopSrc = MakeSource(_alarmGO, loopClip, loop: true, spatial: 0.3f, vol: 0.7f);
                _loopSrc.Play();
            }

            var trClip = Clip(trSnd, "soundLoop") ?? Clip(trSnd, "soundOpen");
            if (trClip != null && trClip != loopClip && _healer != null)
            {
                var go = new GameObject("MapCollapse_TruckAudio");
                go.transform.SetParent(_healer.transform);
                go.transform.localPosition = Vector3.zero;
                _truckSrc = MakeSource(go, trClip, loop: true, spatial: 0.5f, vol: 0.6f, pitch: 0.8f);
                _truckSrc.Play();
            }

            _beepClip = Clip(exSnd, "soundAlarmFinal") ?? Clip(exSnd, "soundAlarmGlobal") ?? Clip(exSnd, "soundActivate1");
            if (_beepClip == loopClip)
                _beepClip = Clip(exSnd, "soundActivate1") ?? Clip(exSnd, "soundButton");
            if (_beepClip != null)
                _beepSrc = MakeSource(_alarmGO, null, loop: false, spatial: 0f, vol: 0.8f);

            _sirenClip = Clip(exSnd, "soundAlarm") ?? Clip(exSnd, "soundAlarmGlobal")
                      ?? Clip(exSnd, "soundAlarmFinal") ?? Clip(exSnd, "soundActivate3");
            if (_sirenClip != null)
            {
                var sirenGO = new GameObject("MapCollapse_Siren");
                sirenGO.transform.SetParent(_healer != null ? _healer.transform : _alarmGO.transform);
                sirenGO.transform.localPosition = Vector3.zero;
                _sirenSrc = MakeSource(sirenGO, null, loop: false, spatial: 0.8f, vol: 0.7f);
                _sirenSrc.minDistance = 2f;
                _sirenSrc.maxDistance = 80f;
            }
        }

        static AudioSource MakeSource(GameObject go, AudioClip? clip, bool loop, float spatial, float vol, float pitch = 1f)
        {
            var src = go.AddComponent<AudioSource>();
            src.clip = clip; src.loop = loop;
            src.spatialBlend = spatial; src.maxDistance = 500f;
            src.volume = vol; src.pitch = pitch;
            src.outputAudioMixerGroup = SfxMixerGroup;
            return src;
        }

        static Dictionary<string, Sound> SoundsFrom(MonoBehaviour? target)
        {
            var map = new Dictionary<string, Sound>();
            if (target == null) return map;
            foreach (var f in target.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                if (f.FieldType == typeof(Sound) && f.GetValue(target) is Sound s) map[f.Name] = s;
            return map;
        }

        static AudioClip? Clip(Dictionary<string, Sound> m, string k)
        {
            if (!m.TryGetValue(k, out var s)) return null;
            // clips live in Sounds[], not Source.clip
            if (s.Sounds != null && s.Sounds.Length > 0 && s.Sounds[0] != null) return s.Sounds[0];
            if (s.Source?.clip != null) return s.Source.clip;
            return null;
        }

        // --- truck / enemies / valuables --------------------------------------

        readonly List<(Animator anim, float speed)> _enemyAnims = new();

        void PanicEnemies()
        {
            _enemySpeeds.Clear(); _enemyStates.Clear(); _enemyAnims.Clear();
            foreach (var a in FindObjectsOfType<NavMeshAgent>()) { _enemySpeeds.Add((a, a.speed)); a.speed *= 1.3f; }
            foreach (var ep in FindObjectsOfType<EnemyParent>())
            {
                if (ep.Enemy == null) continue;
                _enemyStates.Add((ep, ep.Enemy.CurrentState));
                ep.Enemy.CurrentState = EnemyState.Roaming;
                if (ep.Enemy.Vision != null)
                    ep.Enemy.Vision.DisableVision(Duration + 30f);
                // match anim speed to movement
                foreach (var anim in ep.GetComponentsInChildren<Animator>(true))
                {
                    _enemyAnims.Add((anim, anim.speed));
                    anim.speed *= 1.3f;
                }
            }
        }

        void KeepEnemiesPanicking()
        {
            foreach (var (ep, _) in _enemyStates)
            {
                if (ep?.Enemy == null) continue;
                var s = ep.Enemy.CurrentState;
                if (s != EnemyState.Roaming && s != EnemyState.Despawn && s != EnemyState.Stunned)
                    ep.Enemy.CurrentState = EnemyState.Roaming;
                // keep vision disabled in case the timer expired
                if (ep.Enemy.Vision != null)
                    ep.Enemy.Vision.DisableVision(5f);
            }
        }

        // --- taxman ----------------------------------------------------------

        static readonly string[] Panic1 = {
            "IS THE CEILING GETTING LOWER\nOR AM I LOSING IT {:'(}",
            "OK NO THE WALLS ARE\nDEFINITELY MOVING {fedup}",
            "THIS IS NOT IN MY CONTRACT\nTHIS IS NOT IN MY CONTRACT {:(}",
            "I DONT GET PAID ENOUGH\nFOR WHATEVER THIS IS {fedup}{fedup}",
            "GET YOUR STUFF AND GET\nBACK HERE NOW {:(}",
            "THE BUILDING IS EATING ITSELF\nHOW IS THAT EVEN POSSIBLE {:o}",
            "I CAN HEAR THE WALLS\nSCREAMING {:'(}{:'(}",
            "FORGET THE VALUABLES\nSAVE YOURSELVES {heartbreak}",
        };

        static readonly string[] Panic2 = {
            "OH GOD OH NO\nOH GOD OH NO {:'(}{:'(}{:'(}",
            "WHY IS EVERYTHING SO SMALL\nWHY WHY WHY {fedup}",
            "I CANT FEEL MY LEGS\nIS THAT NORMAL {:o}",
            "TELL MY WIFE I LOVE\nHER MONEY {:(}",
            "IF YOU CAN READ THIS\nYOU ARE TOO CLOSE {heartbreak}",
        };

        static readonly string[] Countdown = { "10", "9", "8", "7", "6", "5... {:'(}", "4...", "3... GOODBYE {heartbreak}", "2...", "1..." };

        void TaxmanSay(string msg)
        {
            var screen = TruckScreenText.instance;
            if (screen == null) return;
            screen.isTyping = false;
            screen.MessageSendCustom("", msg, 2); // empty name = taxman
        }

        // --- main loop -------------------------------------------------------

        IEnumerator Run()
        {
            // buildup — shake + single soft scare to set the tone
            float buildupStart = Time.time;
            bool taxmanReacted = false;
            bool buildupScared = false;
            while (Time.time - buildupStart < 10f && _shrinking)
            {
                float elapsed = Time.time - buildupStart;
                float bp = elapsed / 10f;

                if (GameDirector.instance?.CameraShake != null)
                {
                    float intensity = 0.1f + bp * bp * 0.8f;
                    GameDirector.instance.CameraShake.Shake(intensity, 0.3f);
                }

                // first scare stinger
                if (elapsed >= 3f && !buildupScared)
                {
                    buildupScared = true;
                    AudioScare.instance?.PlaySoft();
                }

                if (elapsed >= 6f && !taxmanReacted)
                {
                    taxmanReacted = true;
                    TaxmanSay("...{:o}\n\nDID THE BUILDING JUST\nSHUDDER");
                }

                yield return null;
            }
            if (!_shrinking) yield break;

            DetachPhysObjects();
            SetupLightsAndSound();
            PanicEnemies();

            // let players send the truck — set completed count without triggering
            // the extraction state machines (which play tube/slam sounds)
            var rd = RoundDirector.instance;
            if (rd != null)
            {
                rd.extractionPointsCompleted = rd.extractionPoints;
                rd.allExtractionPointsCompleted = true;
            }
            _origFogEnd = RenderSettings.fogEndDistance;
            _origFogStart = RenderSettings.fogStartDistance;
            var cam = Camera.main;
            _origFov = cam != null ? cam.fieldOfView : 66f;
            _nextGlitch = 5f;
            _nextScare = 8f;

            // queue objects for gradual destruction during collapse
            var pendingHinges = new List<PhysGrabHinge>(FindObjectsOfType<PhysGrabHinge>());
            pendingHinges.RemoveAll(h => h == null || h.broken);
            var pendingObjects = new List<PhysGrabObject>(FindObjectsOfType<PhysGrabObject>());
            pendingObjects.RemoveAll(o => o == null || o.GetComponent<PlayerAvatar>() != null);
            TaxmanSay("WHAT THE-\nWHAT IS HAPPENING TO THE BUILDING\n{:o}{:o}{:o}");

            // collapse
            var from = _level!.localScale;
            var to   = _origScale * Factor;
            float t = 0f, nextBeep = 2f, nextDestroy = 1f;
            int totalToDestroy = Mathf.Max(pendingHinges.Count, pendingObjects.Count);
            float destroyInterval = totalToDestroy > 0 ? (Duration * 0.8f) / totalToDestroy : 2f;
            destroyInterval = Mathf.Clamp(destroyInterval, 0.2f, 3f);
            int msgIdx = 0;
            bool killed = false;

            while (t < Duration && _shrinking)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / Duration);
                float ease = p * p;

                var prev = _level.localScale;
                var next = Vector3.Lerp(from, to, ease);
                _level.localScale = next;

                if (prev.x > 0.001f)
                    TrackObjects(next.x / prev.x);

                PulseLights(p);
                PitchAlarms(p);
                Beep(ref nextBeep, t, p);
                Shake(t, p);
                Visuals(t, p);

                if (TruckScreenText.instance?.background != null)
                {
                    float flash = Mathf.PingPong(Time.time * 3f, 1f);
                    TruckScreenText.instance.background.color =
                        Color.Lerp(TruckScreenText.instance.evilBackgroundColor, new Color(0.7f, 0f, 0f), flash);
                }

                if (Time.frameCount % 30 == 0) KeepEnemiesPanicking();
                if (Time.frameCount % 120 == 0)
                {
                    float enemyMult = 1.3f + p * 0.5f;
                    foreach (var (a, orig) in _enemySpeeds)
                        if (a != null && a.gameObject != null) a.speed = orig * enemyMult;
                    foreach (var (anim, orig) in _enemyAnims)
                        if (anim != null) anim.speed = orig * enemyMult;
                }

                if (t >= nextDestroy && SemiFunc.IsMasterClientOrSingleplayer())
                {
                    nextDestroy = t + destroyInterval;
                    // ramp batch size to clear physics objects before crush
                    int batch = p < 0.5f ? 1 : p < 0.75f ? 2 : 3;
                    for (int d = 0; d < batch; d++)
                    {
                        if (pendingHinges.Count > 0)
                        {
                            var h = pendingHinges[pendingHinges.Count - 1];
                            pendingHinges.RemoveAt(pendingHinges.Count - 1);
                            if (h != null && !h.broken)
                                h.HingeBreakImpulse();
                        }
                        if (pendingObjects.Count > 0)
                        {
                            var pgo = pendingObjects[pendingObjects.Count - 1];
                            pendingObjects.RemoveAt(pendingObjects.Count - 1);
                            if (pgo != null)
                            {
                                var impact = pgo.GetComponent<PhysGrabObjectImpactDetector>();
                                if (impact != null)
                                    impact.Break(9999f, pgo.transform.position, impact.breakLevelHeavy, true);
                                else
                                    pgo.DestroyPhysGrabObject();
                            }
                        }
                    }
                }

                if (!killed)
                {
                    float scaleRatio = next.x / _origScale.x;

                    if (scaleRatio < 0.50f && Time.frameCount % 30 == 0 && SemiFunc.IsMasterClientOrSingleplayer())
                        CrushEnemies();

                    var pc = PlayerController.instance;
                    bool crushed = false;
                    if (pc != null)
                    {
                        bool clipped = pc.transform.position.y < _pivot.y - 5f;
                        var crushCam = Camera.main;
                        int levelMask = LayerMask.GetMask("Default");
                        // scale raycast distances by player size so shrunken players
                        // aren't crushed prematurely by their already-small hitbox
                        var playerCtrl = pc.GetComponent<ScaleController>();
                        float playerScale = (playerCtrl != null && playerCtrl.IsScaled)
                            ? playerCtrl.ScaleTarget != null
                                ? playerCtrl.ScaleTarget.localScale.y / playerCtrl.OriginalScale.y
                                : pc.transform.localScale.y
                            : 1f;
                        float upDist = 0.2f * Mathf.Max(playerScale, 0.1f);
                        float downDist = 0.3f * Mathf.Max(playerScale, 0.1f);
                        bool ceilingClose = crushCam != null && Physics.Raycast(crushCam.transform.position, Vector3.up, upDist, levelMask);
                        bool floorClose = crushCam != null && Physics.Raycast(crushCam.transform.position, Vector3.down, downDist, levelMask);
                        crushed = clipped || (ceilingClose && floorClose);
                    }

                    // hard cap at 5% in case raycast never hits (big open room)
                    if (crushed || scaleRatio < 0.05f)
                    {
                        killed = true;
                        TaxmanSay("I CANT STAND UP\nTHE CEILING IS ON MY HEAD {:'(}");
                        yield return StartCoroutine(CrushSequence());
                    }
                }

                if (t >= _nextMsg && !killed)
                {
                    if (p < 0.8f)
                    {
                        TaxmanSay(Panic1[msgIdx % Panic1.Length]);
                        _nextMsg = t + Mathf.Lerp(10f, 4f, p);
                    }
                    else
                    {
                        TaxmanSay(Panic2[msgIdx % Panic2.Length]);
                        _nextMsg = t + 3f;
                    }
                    msgIdx++;
                }

                int left = Mathf.CeilToInt(Duration - t);
                if (left <= 10 && left != _lastCountdown && left >= 0 && !killed)
                {
                    _lastCountdown = left;
                    int i = 10 - left;
                    if (i >= 0 && i < Countdown.Length)
                        TaxmanSay($"<color=#FF0000><size=250%><b>{Countdown[i]}</b></size></color>");
                }

                yield return null;
            }

            // timer ran out before scale threshold
            if (_level != null) _level.localScale = to;
            _shrinking = false;
            _shrunken  = true;
            _routine   = null;

            if (!killed) yield return StartCoroutine(CrushSequence());
        }

        IEnumerator CrushSequence()
        {
            var cam = Camera.main;

            // slam FOV down — feels like being compressed
            if (cam != null)
            {
                float startFov = cam.fieldOfView;
                float crushFov = 25f;
                float elapsed = 0f;
                while (elapsed < 0.3f)
                {
                    elapsed += Time.deltaTime;
                    float t = elapsed / 0.3f;
                    if (cam != null) cam.fieldOfView = Mathf.Lerp(startFov, crushFov, t * t);

                    // heavy shake during the slam
                    if (GameDirector.instance?.CameraShake != null)
                        GameDirector.instance.CameraShake.Shake(5f, 0.2f);
                    if (GameDirector.instance?.CameraImpact != null)
                        GameDirector.instance.CameraImpact.Shake(8f, 0.15f);

                    yield return null;
                }
            }

            // impact + vignette slam
            var pp = PostProcessing.Instance;
            if (pp != null)
                pp.VignetteOverride(Color.black, 0.8f, 0.1f, 0.5f, 0.1f, 0.5f, gameObject);

            AudioScare.instance?.PlayImpact();
            if (CameraGlitch.Instance != null)
                CameraGlitch.Instance.PlayLong();

            // brief hold so the player feels the crush before dying
            yield return new WaitForSeconds(0.4f);

            TaxmanSay("<color=#FF0000><size=300%><b>CRUSHED</b></size></color>\n{skull}{skull}{skull}");
            if (PlayerController.instance != null)
                SemiFunc.CameraShakeImpactDistance(PlayerController.instance.transform.position, 30f, 5f, 20f, 100f);

            foreach (var avatar in GetPlayers())
            {
                if (avatar == null) continue;
                AssetManager.instance?.PhysImpactEffect(avatar.transform.position);
                var hp = avatar.GetComponent<PlayerHealth>();
                if (hp != null && hp.health > 0) hp.Hurt(999, false);
            }
        }

        void CrushEnemies()
        {
            int levelMask = LayerMask.GetMask("Default");
            foreach (var (ep, _) in _enemyStates)
            {
                if (ep == null || ep.Enemy == null) continue;
                var hp = ep.GetComponentInChildren<EnemyHealth>();
                if (hp == null || hp.healthCurrent <= 0) continue;
                var pos = ep.transform.position;
                bool ceiling = Physics.Raycast(pos, Vector3.up, 0.5f, levelMask);
                bool floor = Physics.Raycast(pos, Vector3.down, 0.5f, levelMask);
                if (ceiling && floor) hp.Hurt(9999, Vector3.down);
            }
        }

        void PulseLights(float p)
        {
            float period = Mathf.Lerp(4f, 1.5f, p);
            bool on = (Time.time % period) < period * 0.4f;
            float intensity = on ? 5f + p * 15f : 0.2f;
            foreach (var l in _lights)
            {
                if (l == null) continue;
                l.color = Color.red;
                l.intensity = intensity;
                l.gameObject.SetActive(true);
            }

            // siren on blink-on edge
            if (on && !_lastBlinkOn && _sirenSrc != null && _sirenClip != null)
            {
                _sirenSrc.Stop();
                _sirenSrc.pitch = Mathf.Max(0.1f, 0.8f + p * 1.2f);
                _sirenSrc.clip = _sirenClip;
                _sirenSrc.volume = 0.5f;
                _sirenSrc.time = 0f;
                _sirenSrc.Play();
            }
            _lastBlinkOn = on;
        }

        IEnumerator StopAfter(AudioSource src, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (src != null && src.isPlaying) src.Stop();
        }

        void PitchAlarms(float p)
        {
            if (_loopSrc != null) { _loopSrc.pitch = Mathf.Max(0.1f, 1f + p * 1.0f); _loopSrc.volume = Mathf.Min(0.5f, 0.3f + p * 0.2f); }
            if (_truckSrc != null) { _truckSrc.pitch = Mathf.Max(0.1f, 0.8f + p * 0.8f); _truckSrc.volume = Mathf.Min(0.5f, 0.3f + p * 0.2f); }
        }

        void Beep(ref float next, float t, float p)
        {
            if (_beepSrc == null || _beepClip == null || t < next) return;
            float gap = p < 0.3f  ? 4f
                      : p < 0.5f  ? 2f
                      : p < 0.65f ? 1f
                      : p < 0.75f ? 0.5f
                      : p < 0.85f ? 0.25f
                      : p < 0.92f ? 0.15f : 0.08f;
            _beepSrc.Stop();
            _beepSrc.pitch  = Mathf.Max(0.1f, 1f + p * 0.8f);
            _beepSrc.volume = Mathf.Min(0.6f, 0.4f + p * 0.2f);
            _beepSrc.clip = _beepClip;
            _beepSrc.time = 0f;
            _beepSrc.Play();
            StartCoroutine(StopAfter(_beepSrc, 0.5f));
            next = t + gap;
        }

        void Shake(float t, float p)
        {
            if (GameDirector.instance?.CameraShake == null) return;
            float intensity = Mathf.Min(3f, 0.2f + p * p * 4f);
            GameDirector.instance.CameraShake.Shake(intensity, 0.3f);
        }

        void Visuals(float t, float p)
        {
            var pp = PostProcessing.Instance;
            if (pp != null)
            {
                pp.VignetteOverride(Color.black, p * 0.45f, 0.4f, 2f, 1f, 2f, gameObject);
                pp.SaturationOverride(-p * 40f, 2f, 1f, 2f, gameObject);
                pp.ContrastOverride(p * 15f, 2f, 1f, 2f, gameObject);

                if (pp.grain != null)
                {
                    pp.grain.active = true;
                    pp.grain.intensity.value = Mathf.Lerp(0.3f, 0.6f, p);
                    pp.grain.size.value = Mathf.Lerp(1f, 1.5f, p);
                }
            }

            // fog closes in
            if (_origFogEnd > 0f)
            {
                RenderSettings.fogEndDistance = Mathf.Lerp(_origFogEnd, _origFogEnd * 0.15f, p * p);
                RenderSettings.fogStartDistance = Mathf.Lerp(_origFogStart, 0f, p * p);
            }

            // narrow FOV — sells "walls closing in" instead of "I'm growing"
            var cam = Camera.main;
            if (cam != null)
                cam.fieldOfView = Mathf.Lerp(_origFov, _origFov * 0.7f, p * p);

            // glitches escalate: tiny → short → long
            if (t >= _nextGlitch && CameraGlitch.Instance != null)
            {
                if (p < 0.4f)      { CameraGlitch.Instance.PlayTiny();  _nextGlitch = t + Mathf.Lerp(12f, 5f, p); }
                else if (p < 0.75f) { CameraGlitch.Instance.PlayShort(); _nextGlitch = t + Mathf.Lerp(5f, 2f, p); }
                else                { CameraGlitch.Instance.PlayLong();  _nextGlitch = t + Mathf.Lerp(2f, 0.5f, p); }
            }

            // scare stingers — soft early, impacts late
            if (t >= _nextScare && AudioScare.instance != null)
            {
                if      (p < 0.3f) { AudioScare.instance.PlaySoft();   _nextScare = t + 15f; }
                else if (p < 0.6f) { AudioScare.instance.PlaySoft();   _nextScare = t + 10f; }
                else if (p < 0.85f){ AudioScare.instance.PlayImpact(); _nextScare = t + 6f; }
                else               { AudioScare.instance.PlayImpact(); _nextScare = t + 3f; }
            }
        }

        PlayerAvatar[]? _cachedPlayers;
        float _playerCacheTime;

        PlayerAvatar[] GetPlayers()
        {
            // cached to avoid per-frame FindObjectsOfType
            if (_cachedPlayers == null || Time.time - _playerCacheTime > 2f)
            {
                _cachedPlayers = FindObjectsOfType<PlayerAvatar>();
                _playerCacheTime = Time.time;
            }
            return _cachedPlayers;
        }

        void TrackObjects(float ratio)
        {
            foreach (var av in GetPlayers())
            {
                if (av == null) continue;
                var rb = av.GetComponent<Rigidbody>();
                if (rb != null) rb.position = _pivot + (rb.position - _pivot) * ratio;
                else av.transform.position = _pivot + (av.transform.position - _pivot) * ratio;
            }
            foreach (var (tr, _) in _detached)
            {
                if (tr == null) continue;
                var rb = tr.GetComponent<Rigidbody>();
                if (rb != null) rb.position = _pivot + (rb.position - _pivot) * ratio;
            }
        }

        // --- restore ---------------------------------------------------------

        void Restore()
        {
            _shrinking = _shrunken = false;
            StopAllCoroutines();
            _routine = null;

            if (_level != null) _level.localScale = _origScale;

            foreach (var (t, par) in _detached)
                if (t != null && par != null) t.SetParent(par, true);
            _detached.Clear();

            foreach (var (a, spd) in _enemySpeeds) if (a != null) a.speed = spd;
            foreach (var (anim, spd) in _enemyAnims) if (anim != null) anim.speed = spd;
            _enemySpeeds.Clear(); _enemyStates.Clear(); _enemyAnims.Clear();

            for (int i = 0; i < _lights.Count; i++)
                if (i < _origLights.Count && _lights[i] != null)
                    { _lights[i].color = _origLights[i].c; _lights[i].intensity = _origLights[i].i; }
            _lights.Clear(); _origLights.Clear();

            // restore fog + FOV
            if (_origFogEnd > 0f)
            {
                RenderSettings.fogEndDistance = _origFogEnd;
                RenderSettings.fogStartDistance = _origFogStart;
            }
            var cam = Camera.main;
            if (cam != null) cam.fieldOfView = _origFov;

            if (TruckScreenText.instance?.background != null)
                TruckScreenText.instance.background.color = TruckScreenText.instance.mainBackgroundColor;

            if (_sirenSrc != null) { Destroy(_sirenSrc.gameObject); _sirenSrc = null; }
            if (_alarmGO != null) { Destroy(_alarmGO); _alarmGO = null; _loopSrc = _beepSrc = null; }
            if (_truckSrc != null) { Destroy(_truckSrc.gameObject); _truckSrc = null; }

            _healer = null;
        }
    }
}
