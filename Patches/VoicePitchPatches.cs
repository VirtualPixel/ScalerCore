using HarmonyLib;
using ScalerCore.Handlers;
using ScalerCore.Utilities;

namespace ScalerCore.Patches
{
    // PlayerVoiceChat.OverridePitchLogic writes its pitch multiplier straight onto the
    // AudioSource, and for a remote player that AudioSource is Photon Voice's playback sink:
    // Speaker hands it to UnityAudioOut, which streams decoded frames into a looping clip and
    // reads the play head back as AudioSource.timeSamples. The network fills that ring buffer
    // at the sample rate while the source drains it at pitch times the sample rate, so a
    // raised pitch eats the jitter cushion at a fixed rate (about two thirds of a second at
    // the 1.30 a shrunk player runs), AudioOutDelayControl underruns, and the write head jumps
    // a full cushion ahead. The gap is what people heard as the voice cutting out, and it
    // repeated for as long as they stayed small.
    //
    // These patches hold the speaker at pitch 1.0 and hand the multiplier to VoicePitchShifter,
    // which does the pitch in the stream where it costs no buffer. Everything that used to
    // cancel the pitch still cancels it: the game drives its multiplier back to 1 on
    // OverridePitchCancel and the shifter passes the voice straight through.

    /// <summary>Give every voice chat a shifter as it comes up.</summary>
    [HarmonyPatch(typeof(PlayerVoiceChat), "Start")]
    internal static class VoiceShifterAttachPatch
    {
        static void Postfix(PlayerVoiceChat __instance)
        {
            if (__instance.audioSource != null)
                VoicePitchShifter.Attach(__instance);
        }
    }

    /// <summary>Take the pitch off the speaker and give it to the shifter.</summary>
    [HarmonyPatch(typeof(PlayerVoiceChat), "OverridePitchLogic")]
    internal static class VoiceSpeakerPitchPatch
    {
        static void Postfix(PlayerVoiceChat __instance)
        {
            var shifter = VoicePitchShifter.For(__instance);
            if (shifter == null || __instance.audioSource == null) return;

            // Whatever the game settled on this frame is sitting on the source right now,
            // ramps and oscillation included. Take that number and leave the source at 1.
            shifter.Pitch = __instance.audioSource.pitch;
            if (shifter.InChain) __instance.audioSource.pitch = 1f;
        }
    }

    /// <summary>Keep the chat voice pitched with the player.</summary>
    [HarmonyPatch(typeof(PlayerVoiceChat), "TtsFollowVoiceSettings")]
    internal static class VoiceTtsPitchPatch
    {
        static void Postfix(PlayerVoiceChat __instance)
        {
            var shifter = VoicePitchShifter.For(__instance);
            if (shifter == null || !shifter.InChain) return;

            // TTS plays an ordinary clip, not a Photon stream, so pitching its AudioSource is
            // fine and vanilla builds the number as audioSource.pitch times a look-angle term.
            // Holding the speaker at 1 would flatten the chat voice with it, so put the
            // multiplier back, under exactly the conditions that made the game write one.
            if (__instance.ttsVoice == null || __instance.playerAvatar == null) return;
            if (__instance.ttsAudioSource == null) return;
            if (!SemiFunc.IsMultiplayer()) return;

            __instance.ttsAudioSource.pitch *= shifter.Pitch;
        }
    }

    /// <summary>
    /// Treat the voice of a player who was already scaled when their voice chat arrived.
    /// PlayerAvatar.voiceChat is filled in by an RPC that routinely lands after the scale
    /// does; in the 1.0.4 field logs every avatar on both machines, local and remote, skipped
    /// the voice step for a null voiceChat, which cost the grown-voice reverb and carry
    /// entirely and left the pitch to snap in rather than ramp.
    /// </summary>
    [HarmonyPatch(typeof(PlayerAvatar), nameof(PlayerAvatar.UpdateMyPlayerVoiceChat))]
    internal static class VoiceChatFetchPatch
    {
        static void Postfix(PlayerAvatar __instance)
        {
            if (!__instance.voiceChatFetched) return;
            var ctrl = __instance.GetComponent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;
            PlayerHandler.ApplyVoiceTreatment(ctrl);
        }
    }
}
