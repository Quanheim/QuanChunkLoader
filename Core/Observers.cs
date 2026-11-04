using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuanChunkLoader.Core
{
    /// <summary>
    /// The server's authoritative list of chunk observers and the forced set: the union of the zones every active
    /// observer keeps loaded (docs/design.md, "How loading and ownership work"). The set is rebuilt from every active
    /// observer whenever one is switched or removed, never by subtracting, because another may still cover a zone.
    /// </summary>
    internal static class Observers
    {
        // ZDO keys stored on each observer.
        internal const string OnKey = "QCL_on";
        internal const string BuilderKey = "QCL_builder";
        internal const string AdminKey = "QCL_admin";

        // Routed RPC names.
        internal const string SwitchRpc = "QCL_Switch";
        internal const string PlacedRpc = "QCL_Placed";
        internal const string MessageRpc = "QCL_Message";
        internal const string SetBoolRpc = "QCL_SetBool"; // ZNetView RPC on the observer, run by its owner

        internal sealed class Info
        {
            internal ZDOID Id;
            internal Vector3 Position;
            internal bool On;
            internal long BuilderId;
            internal string BuilderName;
            internal bool AdminPlaced;

            /// <summary>
            /// Zones loaded on each side of the observer's own zone. Per observer, so separate 1x1 and 3x3 models
            /// (an open point in the design) need no change here; for now every observer follows the setting.
            /// </summary>
            internal int Radius => Settings.Area.Value == AreaSize.OneByOne ? 0 : 1;
        }

        internal static readonly Dictionary<ZDOID, Info> All = new Dictionary<ZDOID, Info>();
        internal static readonly HashSet<Vector2s> Forced = new HashSet<Vector2s>();

        /// <summary>Goes up whenever the forced set changes, so the ownership pass can tell a moved object from a dropped zone.</summary>
        internal static int ForcedVersion;

        private struct Pending
        {
            internal ZDOID Id;
            internal bool Admin;
            internal float Until;
        }

        private static readonly List<Pending> PendingPlacements = new List<Pending>();

        internal static bool IsForced(Vector2s zone) => Forced.Contains(zone);

        /// <summary>A forced zone whose eight neighbours are all forced too: the inner part, where wear and support run.</summary>
        internal static bool IsInterior(Vector2s zone)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (!Forced.Contains(new Vector2s(zone.x + dx, zone.y + dy)))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        // ---- Server lifecycle ------------------------------------------------------------------------------

        internal static void ServerStart()
        {
            All.Clear();
            Forced.Clear();
            PendingPlacements.Clear();
            ForcedVersion++;

            var hash = ObserverPiece.PrefabHash;
            foreach (var zdo in GameAccess.ObjectsById(ZDOMan.instance).Values)
            {
                if (zdo.GetPrefab() == hash)
                {
                    All[zdo.m_uid] = Read(zdo);
                }
            }
            ZDOMan.instance.m_onZDODestroyed += OnZDODestroyed;
            Log.Info($"Found {All.Count} chunk observer(s) in the world, {All.Values.Count(o => o.On)} switched on.");
            foreach (var o in All.Values.Where(o => o.On))
            {
                Log.Info($"  on: {Describe(o)}");
            }
            Rebuild("server start");
        }

        internal static void ServerStop()
        {
            if (ZDOMan.instance != null)
            {
                ZDOMan.instance.m_onZDODestroyed -= OnZDODestroyed;
            }
            All.Clear();
            Forced.Clear();
            PendingPlacements.Clear();
            ForcedVersion++;
        }

        private static Info Read(ZDO zdo)
        {
            return new Info
            {
                Id = zdo.m_uid,
                Position = zdo.GetPosition(),
                On = zdo.GetBool(OnKey),
                BuilderId = zdo.GetLong(ZDOVars.s_creator),
                BuilderName = zdo.GetString(BuilderKey),
                AdminPlaced = zdo.GetBool(AdminKey)
            };
        }

        private static string Describe(Info o)
        {
            var zone = ZoneSystem.GetZone(o.Position);
            var builder = string.IsNullOrEmpty(o.BuilderName) ? "unknown builder" : o.BuilderName;
            return $"({o.Position.x:0}, {o.Position.z:0}) zone {zone.x},{zone.y}, built by {builder}" +
                   (o.AdminPlaced ? ", admin-placed" : "");
        }

        internal static void Rebuild(string reason)
        {
            var next = new HashSet<Vector2s>();
            foreach (var o in All.Values)
            {
                if (!o.On)
                {
                    continue;
                }
                var c = ZoneSystem.GetZone(o.Position);
                var r = o.Radius;
                for (var dy = -r; dy <= r; dy++)
                {
                    for (var dx = -r; dx <= r; dx++)
                    {
                        next.Add(new Vector2s(c.x + dx, c.y + dy));
                    }
                }
            }

            var added = next.Where(z => !Forced.Contains(z)).ToList();
            var removed = Forced.Where(z => !next.Contains(z)).ToList();
            if (added.Count == 0 && removed.Count == 0)
            {
                return;
            }
            Forced.Clear();
            Forced.UnionWith(next);
            ForcedVersion++;
            Log.Info($"Loaded zones changed ({reason}): " +
                     (added.Count > 0 ? $"+{added.Count} [{Zones(added)}] " : "") +
                     (removed.Count > 0 ? $"-{removed.Count} [{Zones(removed)}] " : "") +
                     $"now {Forced.Count} zone(s) from {All.Values.Count(o => o.On)} active observer(s).");
        }

        private static string Zones(IEnumerable<Vector2s> zones) =>
            string.Join(" ", zones.OrderBy(z => z.y).ThenBy(z => z.x).Select(z => $"{z.x},{z.y}"));

        private static void OnZDODestroyed(ZDO zdo)
        {
            if (!All.TryGetValue(zdo.m_uid, out var o))
            {
                return;
            }
            All.Remove(zdo.m_uid);
            Log.Info($"Observer removed: {Describe(o)} (was {(o.On ? "on" : "off")}).");
            if (o.On)
            {
                Rebuild("observer removed");
            }
        }

        /// <summary>Retries placements whose ZDO reached the server after the RPC did. Called about once a second.</summary>
        internal static void ServerTick()
        {
            for (var i = PendingPlacements.Count - 1; i >= 0; i--)
            {
                var p = PendingPlacements[i];
                var zdo = ZDOMan.instance.GetZDO(p.Id);
                if (zdo != null)
                {
                    PendingPlacements.RemoveAt(i);
                    Placed(zdo, p.Admin);
                }
                else if (Time.time > p.Until)
                {
                    PendingPlacements.RemoveAt(i);
                    Log.Debug($"Placed observer {p.Id} never reached the server; ignored.");
                }
            }
        }

        // ---- RPCs ------------------------------------------------------------------------------------------

        internal static void RegisterRpcs()
        {
            ZRoutedRpc.instance.Register<ZDOID, bool>(SwitchRpc, RPC_Switch);
            ZRoutedRpc.instance.Register<ZDOID>(PlacedRpc, RPC_Placed);
            ZRoutedRpc.instance.Register<string>(MessageRpc, RPC_Message);
        }

        /// <summary>Server: a player asks to switch an observer. Checks who may, then changes the forced set.</summary>
        private static void RPC_Switch(long sender, ZDOID id, bool on)
        {
            if (!ZNet.instance.IsServer())
            {
                return;
            }
            var zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null || zdo.GetPrefab() != ObserverPiece.PrefabHash)
            {
                return;
            }
            if (!All.TryGetValue(id, out var o))
            {
                o = Read(zdo);
                All[id] = o;
            }

            var who = PlayerName(sender);
            if (Settings.BuilderOrAdminSwitches.Value && !IsAdmin(sender) && PlayerId(sender) != o.BuilderId)
            {
                var builder = string.IsNullOrEmpty(o.BuilderName) ? "its builder" : o.BuilderName;
                SendMessage(sender, $"Only {builder} or an admin can switch this observer.");
                Log.Info($"Refused {who} switching {Describe(o)}: not the builder or an admin.");
                return;
            }

            if (o.On == on)
            {
                return;
            }
            o.On = on;
            WriteBool(zdo, OnKey, on);
            Log.Info($"Observer switched {(on ? "on" : "off")} by {who}: {Describe(o)}.");
            Rebuild($"switched {(on ? "on" : "off")} by {who}");
        }

        /// <summary>Server: a player has just built an observer. Records it, and whether an admin built it.</summary>
        private static void RPC_Placed(long sender, ZDOID id)
        {
            if (!ZNet.instance.IsServer())
            {
                return;
            }
            var admin = IsAdmin(sender);
            var zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null)
            {
                PendingPlacements.Add(new Pending { Id = id, Admin = admin, Until = Time.time + 30f });
                return;
            }
            Placed(zdo, admin);
        }

        private static void Placed(ZDO zdo, bool admin)
        {
            if (zdo.GetPrefab() != ObserverPiece.PrefabHash)
            {
                return;
            }
            if (admin)
            {
                WriteBool(zdo, AdminKey, true);
            }
            var o = Read(zdo);
            o.AdminPlaced = admin;
            All[zdo.m_uid] = o;
            Log.Info($"Observer built: {Describe(o)}.");
        }

        private static void RPC_Message(long sender, string text)
        {
            if (MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, text);
            }
        }

        internal static void SendMessage(long target, string text)
        {
            if (target == ZDOMan.GetSessionID())
            {
                RPC_Message(target, text);
            }
            else
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(target, MessageRpc, text);
            }
        }

        /// <summary>
        /// Server: sets a flag on an observer's ZDO. A player who owns it and is near it sets it themselves (so two
        /// machines never write the same ZDO at once); otherwise the server takes ownership and sets it. The
        /// ownership rule hands it back to a nearby player within one pass.
        /// </summary>
        private static void WriteBool(ZDO zdo, string key, bool value)
        {
            var owner = zdo.GetOwner();
            if (owner != 0 && owner != ZDOMan.GetSessionID())
            {
                var peer = ZNet.instance.GetPeer(owner);
                if (peer != null && ZNetScene.InActiveArea(zdo.GetPosition(), peer.GetRefPos()))
                {
                    ZRoutedRpc.instance.InvokeRoutedRPC(owner, zdo.m_uid, SetBoolRpc, key, value);
                    return;
                }
            }
            zdo.SetOwner(ZDOMan.GetSessionID());
            zdo.Set(key, value);
        }

        // ---- Who's asking ----------------------------------------------------------------------------------

        internal static bool IsAdmin(long sender)
        {
            if (sender == ZDOMan.GetSessionID())
            {
                return !ZNet.instance.IsDedicated(); // the host of a local game
            }
            var peer = ZNet.instance.GetPeer(sender);
            return peer != null && peer.m_socket != null && ZNet.instance.IsAdmin(peer.m_socket.GetHostName());
        }

        private static long PlayerId(long sender)
        {
            if (sender == ZDOMan.GetSessionID())
            {
                return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0;
            }
            var peer = ZNet.instance.GetPeer(sender);
            if (peer == null)
            {
                return 0;
            }
            if (peer.m_playerID != 0)
            {
                return peer.m_playerID;
            }
            var character = ZDOMan.instance.GetZDO(peer.m_characterID);
            return character != null ? character.GetLong(ZDOVars.s_playerID) : 0;
        }

        internal static string PlayerName(long uid)
        {
            if (uid == 0)
            {
                return "nobody";
            }
            if (uid == ZDOMan.GetSessionID())
            {
                return ZNet.instance.IsDedicated() || Player.m_localPlayer == null
                    ? "server"
                    : Player.m_localPlayer.GetPlayerName() + " (host)";
            }
            var peer = ZNet.instance.GetPeer(uid);
            return peer != null ? peer.m_playerName : $"offline player {uid}";
        }
    }
}
