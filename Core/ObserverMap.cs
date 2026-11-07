using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;

namespace QuanChunkLoader.Core
{
    /// <summary>
    /// The admin map (docs/design.md, "Limits and admin controls"). An admin who turns on "Show observers on the
    /// map" asks the server for the observer list; the server sends it, and sends it again whenever an observer is
    /// built, switched or removed. Other players never receive anyone's observers. Pins are only rebuilt when the
    /// list arrives: no timers while the map is open and no client-side scans.
    /// </summary>
    internal static class ObserverMap
    {
        private const string RequestRpc = "QCL_MapRequest";
        private const string ListRpc = "QCL_MapList";

        private struct Entry
        {
            internal Vector3 Position;
            internal bool On;
            internal bool Cluster;
            internal string Builder;
        }

        // Server: admins who asked for the list this session.
        private static readonly HashSet<long> Subscribers = new HashSet<long>();

        // Client: the last list, and the pins drawn from it.
        private static readonly List<Entry> Entries = new List<Entry>();
        private static readonly List<Minimap.PinData> Pins = new List<Minimap.PinData>();
        private static Minimap _pinsOn;
        private static ZNet _requestedIn;

        internal static void RegisterRpcs()
        {
            Subscribers.Clear();
            Entries.Clear();
            Pins.Clear();
            _requestedIn = null;
            ZRoutedRpc.instance.Register(RequestRpc, RPC_Request);
            ZRoutedRpc.instance.Register<ZPackage>(ListRpc, RPC_List);
        }

        internal static void Hook()
        {
            Observers.Changed += SendToSubscribers;
            Settings.ShowOnMap.SettingChanged += (_, __) =>
            {
                _requestedIn = null;
                Redraw();
            };
            SynchronizationManager.OnAdminStatusChanged += () => _requestedIn = null;
        }

        /// <summary>Client, every frame: asks for the list once per session while the map option is on and we're an admin.</summary>
        internal static void ClientUpdate()
        {
            if (!Settings.ShowOnMap.Value || ZNet.instance == null || ZRoutedRpc.instance == null || Player.m_localPlayer == null)
            {
                return;
            }
            if (_requestedIn == ZNet.instance || !SynchronizationManager.Instance.PlayerIsAdmin)
            {
                return;
            }
            _requestedIn = ZNet.instance;
            ZRoutedRpc.instance.InvokeRoutedRPC(RequestRpc);
        }

        // ---- Server --------------------------------------------------------------------------------------

        private static void RPC_Request(long sender)
        {
            if (!ZNet.instance.IsServer())
            {
                return;
            }
            if (!Observers.IsAdmin(sender))
            {
                Log.Info($"Refused {Observers.PlayerName(sender)} the observer map: not an admin.");
                return;
            }
            Subscribers.Add(sender);
            Send(sender);
        }

        private static void SendToSubscribers()
        {
            if (Subscribers.Count == 0)
            {
                return;
            }
            Subscribers.RemoveWhere(uid => uid != ZDOMan.GetSessionID() && ZNet.instance.GetPeer(uid) == null);
            foreach (var uid in Subscribers)
            {
                // Admin status is checked on every send, so someone removed from the admin list stops receiving it.
                if (Observers.IsAdmin(uid))
                {
                    Send(uid);
                }
            }
        }

        private static void Send(long target)
        {
            var package = new ZPackage();
            package.Write(Observers.All.Count);
            foreach (var o in Observers.All.Values)
            {
                package.Write(o.Position);
                package.Write(o.On);
                package.Write(o.Prefab == ObserverPiece.ClusterHash);
                package.Write(o.BuilderName ?? "");
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(target, ListRpc, package);
        }

        // ---- Client --------------------------------------------------------------------------------------

        private static void RPC_List(long sender, ZPackage package)
        {
            if (!ZNet.instance.IsServer())
            {
                var server = ZNet.instance.GetServerPeer();
                if (server == null || sender != server.m_uid)
                {
                    return;
                }
            }
            Entries.Clear();
            var count = package.ReadInt();
            for (var i = 0; i < count; i++)
            {
                Entries.Add(new Entry
                {
                    Position = package.ReadVector3(),
                    On = package.ReadBool(),
                    Cluster = package.ReadBool(),
                    Builder = package.ReadString()
                });
            }
            Redraw();
        }

        private static void Redraw()
        {
            var map = Minimap.instance;
            if (_pinsOn != null && _pinsOn == map)
            {
                foreach (var pin in Pins)
                {
                    map.RemovePin(pin);
                }
            }
            Pins.Clear();
            _pinsOn = map;
            if (map == null || !Settings.ShowOnMap.Value || !SynchronizationManager.Instance.PlayerIsAdmin)
            {
                return;
            }
            foreach (var e in Entries)
            {
                // Icon only, no label (Quandru, 6 Nov 2026). PinType.None, as vanilla's own location icons use:
                // AddPin turns a hidden pin type's map filter back on, and None has no filter button.
                var pin = map.AddPin(e.Position, Minimap.PinType.None, "", false, false);
                var icon = e.On ? ObserverPiece.IconActive : ObserverPiece.IconDisabled;
                if (pin != null && icon != null)
                {
                    pin.m_icon = icon;
                }
                Pins.Add(pin);
            }
        }
    }
}
