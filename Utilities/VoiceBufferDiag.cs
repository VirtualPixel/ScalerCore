using System.Collections.Generic;
using System.Text;
using Photon.Voice.Unity;
using UnityEngine;

namespace ScalerCore.Utilities
{
    // Instrumentation for the "voices cut out while shrunk" report.
    //
    // PlayerVoiceChat.OverridePitchLogic writes pitchMultiplier straight onto its
    // AudioSource, and that AudioSource is Photon Voice's playback sink: Speaker
    // hands GetComponent<AudioSource>() to UnityAudioOut, which streams incoming
    // voice frames into a looping clip and tracks the read head as
    // "OutPos => source.timeSamples". The network fills that ring buffer at exactly
    // the sample rate; the AudioSource drains it at pitch x the sample rate. Raise
    // the pitch and the read head closes on the write head at a constant rate until
    // it overtakes, at which point AudioOutDelayControl.processFrame takes the
    // underrun branch and slams the write head back to a full target delay ahead.
    // That resync is the audible dropout, and because a scaled player holds a raised
    // pitch indefinitely (PlayerHandler.OnUpdate re-arms overridePitchTimer every
    // frame) it repeats for as long as they stay scaled.
    //
    // This component does not try to prove that story by ear. It samples Speaker.Lag
    // (the buffer's write-minus-read distance in ms) alongside the pitch driving it,
    // and reports the sawtooth: lag draining while pitch > 1, then jumping back up.
    // A lag trace that drains and resets in lockstep with pitch is the mechanism;
    // a flat lag trace under raised pitch means the cutouts are something else.
    //
    // Dev/tester tool, armed the same two ways as Plugin.DebugDraw so it reaches
    // people launching through Gale, where env vars don't: SCALERCORE_VOICEDIAG=1 or
    // a "scalercore_voicediag" sentinel file in BepInEx/config. A shipped profile has
    // neither and never attaches this. Set the var or the file's first line to
    // "photon" to additionally drop Photon Voice's own logger to Debug, which prints
    // its authoritative "underrun"/"overrun" lines at the cost of a much noisier log.
    public class VoiceBufferDiag : MonoBehaviour
    {
        // Fast enough to resolve a sawtooth that turns over in well under a second
        // at the shrink pitches in play, slow enough to stay cheap.
        const float SampleInterval = 0.05f;
        // Lag only climbs when the delay control moves the write head itself. Normal
        // jitter between two samples is a few ms, so anything past this is a resync.
        const int ResyncJumpMs = 40;
        // Don't report a player who is sitting at vanilla pitch.
        const float PitchEpsilon = 0.02f;
        const float SummaryInterval = 5f;

        class Track
        {
            internal Speaker? Speaker;
            internal string Name = "?";
            internal bool Configured;

            internal int LastLag;
            internal bool HasLastLag;

            internal int Resyncs;
            internal int MinLag = int.MaxValue;
            internal int MaxLag = int.MinValue;
            internal float PitchSum;
            internal int PitchSamples;
        }

        readonly Dictionary<PlayerVoiceChat, Track> _tracks = new();
        readonly List<PlayerVoiceChat> _stale = new();
        float _sampleTimer;
        float _summaryTimer;

        void Update()
        {
            var rm = RunManager.instance;
            if (rm == null || rm.voiceChats == null) return;

            _sampleTimer -= Time.deltaTime;
            if (_sampleTimer > 0f) return;
            _sampleTimer = SampleInterval;

            foreach (var vc in rm.voiceChats)
            {
                if (vc == null) continue;
                if (!_tracks.TryGetValue(vc, out var track))
                {
                    track = new Track();
                    _tracks[vc] = track;
                }
                Sample(vc, track);
            }

            PruneDeadTracks();

            _summaryTimer -= SampleInterval;
            if (_summaryTimer <= 0f)
            {
                _summaryTimer = SummaryInterval;
                LogSummary();
            }
        }

        void Sample(PlayerVoiceChat vc, Track track)
        {
            // Speaker and PlayerVoiceChat share a GameObject, but the voice object is
            // built over a few frames, so resolve lazily and keep retrying until it's up.
            track.Speaker ??= vc.GetComponent<Speaker>();
            var speaker = track.Speaker;
            if (speaker == null) return;

            if (vc.playerAvatar != null && !string.IsNullOrEmpty(vc.playerAvatar.playerName))
                track.Name = vc.playerAvatar.playerName;

            // The buffer geometry is a serialized field on the Speaker prefab, so the
            // 200/200/1000 class default is not something IL inspection can confirm.
            // Print whatever this build actually shipped, once, per speaker.
            if (!track.Configured)
            {
                track.Configured = true;
                var cfg = speaker.PlayDelayConfig;
                Plugin.Log.LogInfo($"[SC-VOICE] {track.Name}: play delay low={cfg.Low}ms high={cfg.High}ms max={cfg.Max}ms speedUp={cfg.SpeedUpPerc}%");

                // Speaker already builds its UnityAudioOut with debugInfo: true, so the
                // underrun/overrun lines are written unconditionally and only the logger
                // level suppresses them. Photon's logger defaults to Warning.
                if (Plugin.VoiceDiag == "photon" && speaker.VoiceLogger != null)
                    speaker.VoiceLogger.LogLevel = Photon.Voice.LogLevel.Debug;
            }

            // Lag is meaningless while the stream is paused: the delay control leaves
            // the pointers wherever they were and reseeds them on the next unpause.
            if (!speaker.IsPlaying)
            {
                track.HasLastLag = false;
                return;
            }

            int lag = speaker.Lag;
            float pitch = vc.audioSource != null ? vc.audioSource.pitch : 1f;

            track.PitchSum += pitch;
            track.PitchSamples++;
            if (lag < track.MinLag) track.MinLag = lag;
            if (lag > track.MaxLag) track.MaxLag = lag;

            if (track.HasLastLag && lag - track.LastLag >= ResyncJumpMs)
            {
                track.Resyncs++;
                string driver = vc.overridePitchIsActive
                    ? $"override->{vc.overridePitchMultiplierTarget:F2}"
                    : "no override";
                Plugin.Log.LogInfo(
                    $"[SC-VOICE] RESYNC {track.Name}: lag {track.LastLag}ms -> {lag}ms  pitch={pitch:F2}  {driver}");
            }

            track.LastLag = lag;
            track.HasLastLag = true;
        }

        void LogSummary()
        {
            var line = new StringBuilder();
            foreach (var pair in _tracks)
            {
                var track = pair.Value;
                if (track.PitchSamples == 0) continue;

                float avgPitch = track.PitchSum / track.PitchSamples;
                // A player at vanilla pitch with no resyncs is the control case and
                // carries no signal, so keep them out of the log.
                bool pitched = Mathf.Abs(avgPitch - 1f) > PitchEpsilon;
                if (!pitched && track.Resyncs == 0)
                {
                    ResetWindow(track);
                    continue;
                }

                line.Length = 0;
                line.Append("[SC-VOICE] ").Append(track.Name)
                    .Append(": pitch~").Append(avgPitch.ToString("F2"))
                    .Append("  lag ").Append(track.MinLag).Append("..").Append(track.MaxLag).Append("ms")
                    .Append("  resyncs=").Append(track.Resyncs).Append('/').Append(SummaryInterval).Append('s');
                if (track.Resyncs > 0)
                    line.Append("  (~1 per ").Append((SummaryInterval / track.Resyncs).ToString("F2")).Append("s)");
                Plugin.Log.LogInfo(line.ToString());

                ResetWindow(track);
            }
        }

        static void ResetWindow(Track track)
        {
            track.Resyncs = 0;
            track.MinLag = int.MaxValue;
            track.MaxLag = int.MinValue;
            track.PitchSum = 0f;
            track.PitchSamples = 0;
        }

        void PruneDeadTracks()
        {
            foreach (var pair in _tracks)
            {
                // Unity's == overload reports a destroyed object as null while the
                // reference itself stays valid, which is why it's still a usable key.
                if (pair.Key == null) _stale.Add(pair.Key!);
            }
            foreach (var dead in _stale) _tracks.Remove(dead);
            _stale.Clear();
        }
    }
}
