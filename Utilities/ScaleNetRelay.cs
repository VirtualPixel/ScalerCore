using System.Linq;
using Photon.Pun;
using Photon.Realtime;

namespace ScalerCore.Utilities
{
    /// <summary>
    /// The host's one late-join sender. Somebody arriving mid-run has none of the scaling that
    /// already happened: the shrink RPC goes to the players in the room at the time and Photon
    /// does not keep it, so the host reads its own registry of live sessions and sends each one
    /// again to the new player.
    ///
    /// It lives on the plugin's own object, which is never switched off, because the controllers
    /// cannot be trusted to hear the callback themselves: PUN drops a component as a callback
    /// target while it is disabled, and an enemy spends most of a level switched off between
    /// spawns. One target here also beats several hundred, one per scaled object in the level.
    /// </summary>
    internal class ScaleNetRelay : MonoBehaviourPunCallbacks
    {
        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (ScaleController.Scaled.Count == 0) return;

            int sent = 0;
            // Copied: a resend can dispatch handler work that touches the set.
            foreach (var ctrl in ScaleController.Scaled.ToArray())
            {
                if (ctrl == null) continue;
                ctrl.ResendTo(newPlayer);
                sent++;
            }
            Plugin.Log.LogDebug($"[SC] late join: re-sent {sent} live scale session(s) to {newPlayer.NickName}");
        }
    }
}
