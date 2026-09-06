using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace ScalerCore.AprilFools
{
    // Lives on PunManager.instance's GameObject and piggybacks its existing PhotonView,
    // so map-collapse start/request route through [PunRPC] method names instead of
    // arbitrary bytes for PhotonNetwork.RaiseEvent (which collide easily across mods).
    //
    // Attached on EVERY client the moment PunManager wakes up. The old code attached
    // it lazily from OnMapHit, which only runs on the machine that fired the shot, so
    // the start RPC arrived at a PhotonView with no component to receive it and the
    // collapse never showed on anyone else. That was the whole "sometimes nothing
    // happens for non-hosts" bug.
    internal class MapCollapseRelay : MonoBehaviourPunCallbacks
    {
        internal static MapCollapseRelay? Instance;

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

        // While a collapse runs, the master re-sends the same anchor every few seconds.
        // TryBegin is a no-op on a machine that is already collapsing, so this only
        // matters to a client that missed the start (level not generated yet on its
        // side when the RPC landed, or the RPC lost). Cheap, and it makes the start
        // self-healing instead of a single shot.
        const float ReanchorEvery = 5f;
        float _nextReanchor;

        void Awake() => Instance = this;

        void Update()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || photonView == null) return;
            if (!MapCollapse.IsActive(out double startTime)) { _nextReanchor = 0f; return; }
            if (Time.time < _nextReanchor) return;
            _nextReanchor = Time.time + ReanchorEvery;
            photonView.RPC(nameof(RPC_MapCollapseStart), RpcTarget.Others, startTime);
        }

        internal void BroadcastStart()
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (photonView == null || !PhotonNetwork.InRoom) { MapCollapse.TryBegin(PhotonNetwork.Time); return; }
            // The timestamp is the sync anchor: every client derives collapse
            // progress from the same PhotonNetwork.Time origin, so the blink
            // period, alarm pitch, and scale curve stay in lockstep no matter
            // when the RPC landed.
            Plugin.Log.LogInfo($"[MapCollapse] host broadcasting start, anchor {PhotonNetwork.Time:F2}, view {photonView.ViewID}");
            photonView.RPC(nameof(RPC_MapCollapseStart), RpcTarget.All, PhotonNetwork.Time);
        }

        internal void RequestStart()
        {
            if (SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (photonView == null || !PhotonNetwork.InRoom) return;
            Plugin.Log.LogInfo("[MapCollapse] client asking the host to start");
            photonView.RPC(nameof(RPC_MapCollapseRequest), RpcTarget.MasterClient);
        }

        [PunRPC]
        void RPC_MapCollapseStart(double startTime, PhotonMessageInfo info)
        {
            // Only the master commands a collapse; anyone else spoofing this is dropped.
            if (PhotonNetwork.InRoom && (info.Sender == null || info.Sender != PhotonNetwork.MasterClient)) return;
            bool began = MapCollapse.TryBegin(startTime);
            if (began || !MapCollapse.IsActive(out _))
                Plugin.Log.LogInfo($"[MapCollapse] start RPC from {(info.Sender != null ? info.Sender.NickName : "local")}, anchor {startTime:F2}, now {MapCollapse.Clock:F2}, began={began}, generated={LevelGenerator.Instance != null && LevelGenerator.Instance.Generated}");
        }

        [PunRPC]
        void RPC_MapCollapseRequest(PhotonMessageInfo info)
        {
            if (!SemiFunc.IsMasterClientOrSingleplayer()) return;
            if (info.Sender == null) return;
            BroadcastStart();
        }

        // A collapse in progress reaches late joiners with the ORIGINAL timestamp,
        // so they fast-forward to the same point everyone else is at instead of
        // watching a fresh 100-second collapse nobody else sees.
        public override void OnPlayerEnteredRoom(Player newPlayer)
        {
            if (!PhotonNetwork.IsMasterClient) return;
            if (!MapCollapse.IsActive(out double startTime)) return;
            photonView.RPC(nameof(RPC_MapCollapseStart), newPlayer, startTime);
        }
    }

    // The attach point: every client gets the relay as soon as PunManager exists,
    // so the [PunRPC]s are routable before any shot is ever fired.
    [HarmonyPatch(typeof(PunManager), "Awake")]
    static class MapCollapseRelayAttachPatch
    {
        static void Postfix(PunManager __instance)
        {
            MapCollapseRelay.EnsureFor(__instance);
        }
    }
}
