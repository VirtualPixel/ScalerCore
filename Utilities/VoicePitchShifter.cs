using System.Collections.Generic;
using UnityEngine;

namespace ScalerCore.Utilities
{
    /// <summary>
    /// Moves a scaled player's voice pitch off the AudioSource and into the audio stream.
    ///
    /// Photon Voice plays a remote player through the AudioSource on PlayerVoiceChat: Speaker
    /// hands that source to UnityAudioOut, which streams the decoded frames into a looping
    /// clip and reads the play head back as AudioSource.timeSamples. The network fills that
    /// ring buffer at exactly the sample rate, so raising AudioSource.pitch makes the read
    /// head close on the write head at (pitch - 1) times the sample rate. At the shrink pitch
    /// of 1.30 the 200 ms jitter cushion is gone in two thirds of a second;
    /// AudioOutDelayControl takes its underrun branch, slams the write head a full cushion
    /// ahead again, and the listener hears the gap while the read head walks back up to it.
    /// That repeats for as long as the player stays small, which is what "voices cut out
    /// while shrunk" was.
    ///
    /// So the source stays at pitch 1.0, where drain rate equals fill rate and the cushion
    /// never runs dry, and the pitch happens here instead. Two taps read the incoming samples
    /// back at <see cref="Pitch"/> through a circular buffer, half a buffer apart, crossfaded
    /// with a Hann window (which sums to exactly one at 50% overlap). Inside a grain that is
    /// the same resampling the AudioSource was doing, so the voice keeps its chipmunk
    /// character; the tap wrap pays for the rate difference instead of the jitter buffer.
    ///
    /// At vanilla pitch the wet path is faded out entirely and the samples pass through
    /// untouched, so an unscaled lobby sounds exactly as it did before.
    /// </summary>
    public class VoicePitchShifter : MonoBehaviour
    {
        // Half of this is the delay a shifted voice picks up, and the buffer wrap rate is
        // |1 - pitch| / this. 3072 frames is 64 ms at 48 kHz, which puts the wrap at under
        // 5 Hz at the shrink pitch: slow enough not to read as warble, short enough that the
        // added delay disappears next to Photon's own 200 ms play delay.
        const int Window = 3072;

        // Seconds to fade the shifted path in and out. Long enough that engaging it is not a
        // click, short enough to land inside the game's own 0.2 s pitch ramp.
        const float MixFade = 0.04f;

        // Below this the shift is inaudible and not worth the taps.
        const float PitchDeadband = 0.005f;

        static readonly float[] Hann = BuildHann();
        static readonly Dictionary<PlayerVoiceChat, VoicePitchShifter> ByVoice = new();

        PlayerVoiceChat _voice = null!;
        float[]? _history;
        int _channels;
        int _write;
        float _phase;
        float _mix;
        float _mixStep;

        volatile float _pitch = 1f;
        volatile bool _inChain;

        /// <summary>
        /// Pitch multiplier to apply to the stream. 1 passes the voice through. Clamped to the
        /// range AudioSource.pitch would have been driven over: a shrunk player runs about
        /// 1.30, and a shrunk player talking through a shrunk walkie or a shrunk set of teeth
        /// stacks on top of that.
        /// </summary>
        public float Pitch
        {
            get => _pitch;
            set => _pitch = Mathf.Clamp(value, 0.5f, 3f);
        }

        /// <summary>
        /// True once Unity has actually pulled this filter, which is the only proof the shift
        /// reaches the speaker. Until then the caller leaves the pitch on the AudioSource,
        /// so a voice with no Photon stream behind it (the local player's own, a player whose
        /// microphone never transmits) keeps vanilla behaviour instead of going flat.
        /// </summary>
        public bool InChain => _inChain;

        internal static VoicePitchShifter? For(PlayerVoiceChat voice) =>
            ByVoice.TryGetValue(voice, out var shifter) ? shifter : null;

        internal static VoicePitchShifter Attach(PlayerVoiceChat voice)
        {
            if (ByVoice.TryGetValue(voice, out var existing)) return existing;
            var shifter = voice.gameObject.AddComponent<VoicePitchShifter>();
            shifter._voice = voice;
            ByVoice[voice] = shifter;
            return shifter;
        }

        void Awake()
        {
            // Read on the main thread: OnAudioFilterRead runs on the audio thread, which has
            // no business calling into Unity.
            _mixStep = 1f / Mathf.Max(1f, MixFade * AudioSettings.outputSampleRate);
        }

        void OnDestroy() => ByVoice.Remove(_voice);

        void OnAudioFilterRead(float[] data, int channels)
        {
            _inChain = true;
            if (channels <= 0) return;

            // One allocation per speaker, on the frame its stream starts, because the channel
            // count is not known before Unity hands us a block.
            if (_history == null || _channels != channels)
            {
                _channels = channels;
                _history = new float[Window * channels];
                _write = 0;
                _phase = 0f;
                _mix = 0f;
            }

            float pitch = _pitch;
            bool wet = Mathf.Abs(pitch - 1f) > PitchDeadband;
            int frames = data.Length / channels;

            // Nothing shifted and nothing left to fade out: keep the history current so the
            // next engage has samples to read, and let the block through untouched.
            if (!wet && _mix <= 0f)
            {
                _phase = 0f;
                for (int frame = 0; frame < frames; frame++)
                {
                    int at = _write * channels;
                    int from = frame * channels;
                    for (int c = 0; c < channels; c++) _history[at + c] = data[from + c];
                    _write = _write + 1 == Window ? 0 : _write + 1;
                }
                return;
            }

            float target = wet ? 1f : 0f;
            float rate = (1f - pitch) / Window;

            for (int frame = 0; frame < frames; frame++)
            {
                int at = _write * channels;
                int slot = frame * channels;
                for (int c = 0; c < channels; c++) _history[at + c] = data[slot + c];

                float second = _phase + 0.5f;
                if (second >= 1f) second -= 1f;
                float gainFirst = Window0(_phase);
                float gainSecond = Window0(second);
                float delayFirst = _phase * Window;
                float delaySecond = second * Window;

                for (int c = 0; c < channels; c++)
                {
                    float dry = data[slot + c];
                    float shifted = gainFirst * Read(delayFirst, c) + gainSecond * Read(delaySecond, c);
                    data[slot + c] = dry + _mix * (shifted - dry);
                }

                _write = _write + 1 == Window ? 0 : _write + 1;
                _phase += rate;
                if (_phase >= 1f) _phase -= 1f;
                else if (_phase < 0f) _phase += 1f;

                _mix = _mix < target ? Mathf.Min(target, _mix + _mixStep) : Mathf.Max(target, _mix - _mixStep);
            }

            // Park the taps while faded out so re-engaging starts from the one phase where
            // the shifted path and the dry signal are the same thing.
            if (_mix <= 0f) _phase = 0f;
        }

        /// <summary>Sample `delay` frames back from the write head, linearly interpolated.</summary>
        float Read(float delay, int channel)
        {
            float pos = _write - delay;
            if (pos < 0f) pos += Window;
            int older = (int)pos;
            float blend = pos - older;
            int newer = older + 1 == Window ? 0 : older + 1;
            float a = _history![older * _channels + channel];
            float b = _history[newer * _channels + channel];
            return a + (b - a) * blend;
        }

        // Hann, so the two taps sum to exactly one and the wrap lands where the gain is zero.
        static float Window0(float phase)
        {
            int index = (int)(phase * Hann.Length);
            if (index < 0) index = 0;
            else if (index >= Hann.Length) index = Hann.Length - 1;
            return Hann[index];
        }

        static float[] BuildHann()
        {
            var table = new float[2048];
            for (int i = 0; i < table.Length; i++)
                table[i] = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / table.Length);
            return table;
        }
    }
}
