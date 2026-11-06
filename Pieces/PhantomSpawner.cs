using QuanChunkLoader.Core;
using UnityEngine;

namespace QuanChunkLoader
{
    /// <summary>
    /// A Phantom Spawner in the world: hover text and placement. Whether it's active is the server's call (it alone
    /// knows the loaded zones) and is stored on the phantom as a flag, so every player's hover can show it.
    /// </summary>
    internal sealed class PhantomSpawner : MonoBehaviour, Hoverable
    {
        private ZNetView _nview;
        private Piece _piece;

        // Phantoms loaded on this machine, for despawn protection on a player's game.
        private static readonly System.Collections.Generic.List<PhantomSpawner> Loaded =
            new System.Collections.Generic.List<PhantomSpawner>();

        /// <summary>Whether an active phantom loaded on this machine stands within range of a point.</summary>
        internal static bool AnyActiveWithin(Vector3 point, float range)
        {
            foreach (var p in Loaded)
            {
                if (p != null && p._nview != null && p._nview.IsValid() && p._nview.GetZDO().GetBool(Phantoms.ActiveKey) &&
                    Vector3.Distance(p.transform.position, point) < range)
                {
                    return true;
                }
            }
            return false;
        }

        private void OnDestroy() => Loaded.Remove(this);

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _piece = GetComponent<Piece>();
            if (_nview == null || _nview.GetZDO() == null)
            {
                return;
            }
            _nview.Register<string, bool>(Observers.SetBoolRpc, RPC_SetBool);
            Loaded.Add(this);
        }

        private void Start()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner() || Player.m_localPlayer == null)
            {
                return;
            }
            var zdo = _nview.GetZDO();
            if (zdo.GetString(Observers.BuilderKey) == "" && _piece.GetCreator() == Player.m_localPlayer.GetPlayerID())
            {
                zdo.Set(Observers.BuilderKey, Player.m_localPlayer.GetPlayerName());
                ZRoutedRpc.instance.InvokeRoutedRPC(Observers.PlacedRpc, zdo.m_uid);
            }
        }

        private void RPC_SetBool(long sender, string key, bool value)
        {
            if (!_nview.IsOwner() || (key != Phantoms.ActiveKey && key != Observers.BlockedKey && key != Observers.AdminKey))
            {
                return;
            }
            var server = ZNet.instance.GetServerPeer();
            if (!ZNet.instance.IsServer() && (server == null || sender != server.m_uid))
            {
                return;
            }
            _nview.GetZDO().Set(key, value);
        }

        public string GetHoverName() => _piece != null ? Localization.instance.Localize(_piece.m_name) : "";

        public float GetHoverOffset() => 0f;

        public string GetHoverText()
        {
            if (_nview == null || !_nview.IsValid())
            {
                return "";
            }
            var zdo = _nview.GetZDO();
            string state;
            if (zdo.GetBool(Observers.BlockedKey))
            {
                state = "<color=#ff8080>Dormant</color>: built while only admins could build these";
            }
            else if (zdo.GetBool(Phantoms.ActiveKey))
            {
                state = "<color=#ff9070>Active</color>: nearby spawners act as if a player were here";
            }
            else
            {
                state = "<color=#a0a0a0>Dormant</color>: no active chunk observer";
            }
            var builder = zdo.GetString(Observers.BuilderKey);
            return GetHoverName() + "\n" + state + (string.IsNullOrEmpty(builder) ? "" : $"\nBuilt by {builder}");
        }
    }
}
