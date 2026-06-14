using HarmonyLib;
using ScalerCore.Handlers;

namespace ScalerCore
{
    // Objects that route a player's voice (the teeth valuable, the walkie-talkie,
    // a dead Semibot head) re-assert voiceChat.OverridePitch every frame while
    // transmitting, and the last call each frame wins. These postfixes re-issue
    // the override with a scale-adjusted pitch when the ROUTING OBJECT is scaled,
    // so a shrunken teeth toy squeaks, a grown one rumbles, and a shrunken dead
    // head chipmunks its owner. Pitch rides PlayerHandler.VoicePitchMult, the
    // same intelligibility-floored curve scaled players' own voices use, and the
    // overrides run locally on every client from synced scale state, so everyone
    // hears the same thing.

    [HarmonyPatch(typeof(ValuableTeethBot), "Update")]
    static class TeethVoiceScalePatch
    {
        static void Postfix(ValuableTeethBot __instance)
        {
            var ctrl = __instance.GetComponent<ScaleController>()
                       ?? __instance.GetComponentInParent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;
            float mult = PlayerHandler.VoicePitchMult(ctrl._options.Factor);
            foreach (var grabber in __instance.physGrabObject.playerGrabbing)
            {
                if (grabber.playerAvatar.voiceChatFetched)
                    grabber.playerAvatar.voiceChat.OverridePitch(1.25f * mult, 0.1f, 0.1f, 0.2f, 0.05f, 100f);
            }
        }
    }

    [HarmonyPatch(typeof(ItemWalkieTalkie), "Update")]
    static class WalkieVoiceScalePatch
    {
        static void Postfix(ItemWalkieTalkie __instance)
        {
            var ctrl = __instance.GetComponent<ScaleController>()
                       ?? __instance.GetComponentInParent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;
            var pa = __instance.inThisWalkie;
            if (pa == null || !pa.voiceChatFetched || pa.isLocal) return;
            float mult = PlayerHandler.VoicePitchMult(ctrl._options.Factor);
            // Vanilla issues 1.15 then 1.5 back to back and the last call wins,
            // so 1.5 is the effective radio pitch to scale.
            pa.voiceChat.OverridePitch(1.5f * mult, 0.1f, 0.1f);
        }
    }

    [HarmonyPatch(typeof(PlayerDeathHead), "Update")]
    static class DeathHeadVoiceScalePatch
    {
        static void Postfix(PlayerDeathHead __instance)
        {
            if (!__instance.spectated || __instance.playerAvatar == null) return;
            if (!__instance.playerAvatar.voiceChatFetched) return;
            var ctrl = __instance.GetComponent<ScaleController>()
                       ?? __instance.GetComponentInParent<ScaleController>();
            if (ctrl == null || !ctrl.IsScaled) return;
            float mult = PlayerHandler.VoicePitchMult(ctrl._options.Factor);
            // Keep the game's low-energy croak as the base while it's active;
            // vanilla applies no pitch at all otherwise.
            float basePitch = __instance.spectatedLowEnergy ? 0.5f : 1f;
            __instance.playerAvatar.voiceChat.OverridePitch(basePitch * mult, 0.2f, 0.2f);
        }
    }
}
