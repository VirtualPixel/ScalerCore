using BepInEx;
using BepInEx.Configuration;
using ScalerCore.Utilities;
using System.IO;

namespace ScalerCore.Configuration
{
    /// <summary>
    /// ScalerCore is a library and keeps its behaviour in the calling mod's hands, so the only
    /// thing it binds for itself is the respawn channel, which changes what people in the lobby
    /// see whichever mod asked for the scale.
    /// </summary>
    internal static class PluginConfig
    {
        internal static ConfigEntry<bool> NativeRespawn = null!;

        // Session kill switch for a lobby where the respawn misbehaves and nobody wants to go
        // digging through settings mid-run. Beats the config entry either way.
        static bool _sentinelOff;

        internal static void Init(ConfigFile config)
        {
            _sentinelOff = File.Exists(Path.Combine(Paths.ConfigPath, "scalercore_nativesync_off"));

            NativeRespawn = config.Bind("Experimental", "Native scale respawn",
                RespawnRules.NativeSyncDefault,
                "Experimental. When the host scales a resting item, valuable or cart, respawn it "
              + "through the game's own instantiate with the size in the spawn data. Every client "
              + "applies that itself, so players without ScalerCore see the object at its real size "
              + "with physics to match instead of a full-size copy shaking against the host's. "
              + "Turn it off if scaled objects go missing or lose what they were carrying.");

            NativeRespawn.SettingChanged += (_, _) => Apply();
            Apply();

            if (_sentinelOff)
                Plugin.Log.LogInfo("[SC] native respawn off for this session (scalercore_nativesync_off in BepInEx/config)");
        }

        static void Apply() => ScaleManager.NativeSync = NativeRespawn.Value && !_sentinelOff;
    }
}
