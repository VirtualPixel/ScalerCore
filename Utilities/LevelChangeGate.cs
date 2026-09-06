namespace ScalerCore.Utilities
{
    // Whether RunManager.ChangeLevel actually proceeds on this machine. The game's own
    // first line is: return unless (menu level or master or singleplayer) and not restarting.
    // A Harmony postfix runs either way, so the cleanup hung off ChangeLevel has to ask
    // this first or a non-host tears down every scale and the collapse the moment
    // anything on that client calls ChangeLevel, which the game ignores.
    internal static class LevelChangeGate
    {
        public static bool Proceeds(bool menuLevel, bool masterOrSingleplayer, bool restarting)
        {
            if (restarting) return false;
            return menuLevel || masterOrSingleplayer;
        }
    }
}
