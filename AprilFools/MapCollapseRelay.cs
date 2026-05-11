using Photon.Pun;
using UnityEngine;

namespace ScalerCore.AprilFools
{
    // Lives on PunManager.instance's GameObject and piggybacks its existing PhotonView,
    // so map-collapse start/request route through [PunRPC] method names instead of
    // arbitrary bytes for PhotonNetwork.RaiseEvent (which collide easily across mods).
    internal class MapCollapseRelay : MonoBehaviourPun
    {
        internal static MapCollapseRelay? EnsureFor(MonoBehaviour? host)
        {
            if (host == null) return null;
            var existing = host.GetComponent<MapCollapseRelay>();
            if (existing != null) return existing;

            var relay = host.gameObject.AddComponent<MapCollapseRelay>();
            // Late-added MonoBehaviour with [PunRPC]s, refresh the cache or PUN
            // won't route incoming RPCs to it.
            var pv = host.GetComponent<PhotonView>();
            if (pv != null) pv.RefreshRpcMonoBehaviourCache();
            return relay;
        }

        internal void BroadcastStart()
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (photonView == null || !PhotonNetwork.InRoom) { MapCollapse.TryBegin(); return; }
            photonView.RPC(nameof(RPC_MapCollapseStart), RpcTarget.All);
        }

        internal void RequestStart()
        {
            if (SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (photonView == null || !PhotonNetwork.InRoom) return;
            photonView.RPC(nameof(RPC_MapCollapseRequest), RpcTarget.MasterClient);
        }

        [PunRPC]
        void RPC_MapCollapseStart() => MapCollapse.TryBegin();

        [PunRPC]
        void RPC_MapCollapseRequest()
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            BroadcastStart();
        }
    }
}
