using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuanChunkLoader.Core
{
    /// <summary>
    /// The server's authoritative list of chunk observers and the forced set: the union of the zones every active
    /// observer keeps loaded (docs/design.md, "How loading and ownership work"). The set is rebuilt from every active
    /// observer whenever one is switched or removed, never by subtracting, because another may still cover a zone.
    /// Also the limits, expiry and admin-only building (docs/design.md, "Limits and admin controls").
    /// </summary>
    internal static class Observers
    {
        // ZDO keys stored on each observer (and phantom).
        internal const string OnKey = "QCL_on";
        internal const string BuilderKey = "QCL_builder";
        internal const string AdminKey = "QCL_admin";
        internal const string BlockedKey = "QCL_blocked";     // built by a non-admin while only admins could build
        internal const string RefreshedKey = "QCL_refreshed"; // real UTC ticks when last switched on (expiry)

        // Routed RPC names.
        internal const string SwitchRpc = "QCL_Switch";
        internal const string PlacedRpc = "QCL_Placed";
        internal const string MessageRpc = "QCL_Message";
        internal const string SetBoolRpc = "QCL_SetBool"; // ZNetView RPCs on the piece, run by its owner
        internal const string SetLongRpc = "QCL_SetLong";

        internal sealed class Info
        {
            internal ZDOID Id;
            internal int Prefab;
            internal Vector3 Position;
            internal bool On;
            internal long BuilderId;
            internal string BuilderName;
            internal bool AdminPlaced;
            internal bool Blocked;
            internal long RefreshedTicks;

            /// <summary>Zones loaded on each side of the observer's own zone: 0 for the single column, 1 for the cluster.</summary>
            internal int Radius => ObserverPiece.RadiusOf(Prefab);

            internal int Zones => ObserverPiece.ZonesOf(Prefab);

            /// <summary>Whether this observer counts towards the limits while it's on.</summary>
            internal bool Counts => !(AdminPlaced && Settings.AdminsIgnoreLimits.Value);
        }

        internal static readonly Dictionary<ZDOID, Info> All = new Dictionary<ZDOID, Info>();
        internal static readonly HashSet<Vector2s> Forced = new HashSet<Vector2s>();

        /// <summary>Goes up whenever the forced set changes, so the ownership pass can tell a moved object from a dropped zone.</summary>
        internal static int ForcedVersion;

        /// <summary>Raised on the server whenever an observer is added, removed or switched (the admin map listens).</summary>
        internal static event Action Changed;

        private struct Pending
        {
            internal ZDOID Id;
            internal bool Admin;
            internal long Sender;
            internal float Until;
        }

        private static readonly List<Pending> PendingPlacements = new List<Pending>();
        private static float _expiryTimer;

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
            Phantoms.Clear();

            var now = DateTime.UtcNow.Ticks;
            foreach (var zdo in GameAccess.ObjectsById(ZDOMan.instance).Values)
            {
                var prefab = zdo.GetPrefab();
                if (ObserverPiece.IsObserver(prefab))
                {
                    var o = Read(zdo);
                    if (o.On && o.RefreshedTicks == 0 && Settings.ExpireAfterHours.Value > 0)
                    {
                        // Expiry counts from now for observers that were on before it was turned on.
                        o.RefreshedTicks = now;
                        WriteLong(zdo, RefreshedKey, now);
                    }
                    All[zdo.m_uid] = o;
                }
                else if (prefab == PhantomPiece.PrefabHash)
                {
                    Phantoms.Add(zdo);
                }
            }
            ZDOMan.instance.m_onZDODestroyed += OnZDODestroyed;
            Log.Info($"Found {All.Count} chunk observer(s) in the world, {All.Values.Count(o => o.On)} switched on, " +
                     $"and {Phantoms.Count} phantom spawner(s).");
            foreach (var o in All.Values.Where(o => o.On))
            {
                Log.Info($"  on: {Describe(o)}");
            }
            Rebuild("server start");
            Phantoms.Refresh();
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
            Phantoms.Clear();
            ForcedVersion++;
        }

        private static Info Read(ZDO zdo)
        {
            return new Info
            {
                Id = zdo.m_uid,
                Prefab = zdo.GetPrefab(),
                Position = zdo.GetPosition(),
                On = zdo.GetBool(OnKey),
                BuilderId = zdo.GetLong(ZDOVars.s_creator),
                BuilderName = zdo.GetString(BuilderKey),
                AdminPlaced = zdo.GetBool(AdminKey),
                Blocked = zdo.GetBool(BlockedKey),
                RefreshedTicks = zdo.GetLong(RefreshedKey)
            };
        }

        internal static string Describe(Info o)
        {
            var zone = ZoneSystem.GetZone(o.Position);
            var builder = string.IsNullOrEmpty(o.BuilderName) ? "unknown builder" : o.BuilderName;
            return $"{ObserverPiece.SizeLabel(o.Prefab)} at ({o.Position.x:0}, {o.Position.z:0}) zone {zone.x},{zone.y}, " +
                   $"built by {builder}" + (o.AdminPlaced ? ", admin-placed" : "");
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
            Phantoms.Refresh();
        }

        private static string Zones(IEnumerable<Vector2s> zones) =>
            string.Join(" ", zones.OrderBy(z => z.y).ThenBy(z => z.x).Select(z => $"{z.x},{z.y}"));

        private static void OnZDODestroyed(ZDO zdo)
        {
            if (Phantoms.Remove(zdo.m_uid))
            {
                return;
            }
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
            Changed?.Invoke();
        }

        /// <summary>Retries placements whose ZDO reached the server after the RPC did, and runs expiry. About once a second.</summary>
        internal static void ServerTick()
        {
            for (var i = PendingPlacements.Count - 1; i >= 0; i--)
            {
                var p = PendingPlacements[i];
                var zdo = ZDOMan.instance.GetZDO(p.Id);
                if (zdo != null)
                {
                    PendingPlacements.RemoveAt(i);
                    Placed(zdo, p.Admin, p.Sender);
                }
                else if (Time.time > p.Until)
                {
                    PendingPlacements.RemoveAt(i);
                    Log.Debug($"Placed piece {p.Id} never reached the server; ignored.");
                }
            }
            if ((_expiryTimer += 1f) >= 60f)
            {
                _expiryTimer = 0f;
                Expire();
            }
        }

        // ---- Limits and expiry -----------------------------------------------------------------------------

        private static int ZonesOn(Func<Info, bool> which) => All.Values.Where(o => o.On && o.Counts && which(o)).Sum(o => o.Zones);

        /// <summary>Why switching this observer on would break a limit, or null if it wouldn't.</summary>
        private static string LimitRefusal(Info o)
        {
            if (!o.Counts)
            {
                return null;
            }
            var player = ZonesOn(x => x.BuilderId == o.BuilderId) + o.Zones;
            if (player > Settings.ZonesPerPlayer.Value)
            {
                var builder = string.IsNullOrEmpty(o.BuilderName) ? "this observer's builder" : o.BuilderName;
                return $"That would load {player} zones for {builder}; the limit is {Settings.ZonesPerPlayer.Value} " +
                       "per player. Switch off or remove another of their observers first.";
            }
            var total = ZonesOn(x => true) + o.Zones;
            if (total > Settings.ZonesInTotal.Value)
            {
                return $"This server's observers would load {total} zones; the limit is {Settings.ZonesInTotal.Value}. " +
                       "Ask an admin to raise it or switch off unused observers.";
            }
            return null;
        }

        /// <summary>Switches off observers not refreshed within the expiry time. They're never deleted.</summary>
        private static void Expire()
        {
            var hours = Settings.ExpireAfterHours.Value;
            if (hours <= 0)
            {
                return;
            }
            var cutoff = DateTime.UtcNow.AddHours(-hours).Ticks;
            var expired = All.Values.Where(o => o.On && o.RefreshedTicks > 0 && o.RefreshedTicks < cutoff).ToList();
            foreach (var o in expired)
            {
                var zdo = ZDOMan.instance.GetZDO(o.Id);
                if (zdo == null)
                {
                    continue;
                }
                o.On = false;
                WriteBool(zdo, OnKey, false);
                Log.Info($"Observer expired after {hours} h without being switched on again: {Describe(o)}.");
            }
            if (expired.Count > 0)
            {
                Rebuild("expiry");
                Changed?.Invoke();
            }
        }

        /// <summary>Expiry turned on (or its time changed): every observer that's on counts from now.</summary>
        internal static void ExpiryChanged()
        {
            if (!ServerLoader.Running || Settings.ExpireAfterHours.Value <= 0)
            {
                return;
            }
            var now = DateTime.UtcNow.Ticks;
            foreach (var o in All.Values.Where(o => o.On))
            {
                var zdo = ZDOMan.instance.GetZDO(o.Id);
                if (zdo != null)
                {
                    o.RefreshedTicks = now;
                    WriteLong(zdo, RefreshedKey, now);
                }
            }
            Log.Info($"Observer expiry set to {Settings.ExpireAfterHours.Value} h; every observer that's on counts from now.");
        }

        // ---- RPCs ------------------------------------------------------------------------------------------

        internal static void RegisterRpcs()
        {
            ZRoutedRpc.instance.Register<ZDOID, bool>(SwitchRpc, RPC_Switch);
            ZRoutedRpc.instance.Register<ZDOID>(PlacedRpc, RPC_Placed);
            ZRoutedRpc.instance.Register<string>(MessageRpc, RPC_Message);
            ObserverMap.RegisterRpcs();
        }

        /// <summary>Server: a player asks to switch an observer. Checks who may and the limits, then changes the forced set.</summary>
        private static void RPC_Switch(long sender, ZDOID id, bool on)
        {
            if (!ZNet.instance.IsServer())
            {
                return;
            }
            var zdo = ZDOMan.instance.GetZDO(id);
            if (zdo == null || !ObserverPiece.IsObserver(zdo.GetPrefab()))
            {
                return;
            }
            if (!All.TryGetValue(id, out var o))
            {
                o = Read(zdo);
                All[id] = o;
            }

            var who = PlayerName(sender);
            var admin = IsAdmin(sender);
            if (Settings.BuilderOrAdminSwitches.Value && !admin && PlayerId(sender) != o.BuilderId)
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
            if (on)
            {
                if (o.Blocked && !admin)
                {
                    SendMessage(sender, "This observer was built while only admins could build them, so it stays off. Ask an admin.");
                    Log.Info($"Refused {who} switching on {Describe(o)}: built while only admins could build.");
                    return;
                }
                var refusal = admin && Settings.AdminsIgnoreLimits.Value ? null : LimitRefusal(o);
                if (refusal != null)
                {
                    SendMessage(sender, refusal);
                    Log.Info($"Refused {who} switching on {Describe(o)}: {refusal}");
                    return;
                }
                o.RefreshedTicks = DateTime.UtcNow.Ticks;
                WriteLong(zdo, RefreshedKey, o.RefreshedTicks);
            }
            o.On = on;
            WriteBool(zdo, OnKey, on);
            Log.Info($"Observer switched {(on ? "on" : "off")} by {who}: {Describe(o)}.");
            Rebuild($"switched {(on ? "on" : "off")} by {who}");
            Changed?.Invoke();
        }

        /// <summary>Server: a player has just built an observer or phantom. Records it, and whether an admin built it.</summary>
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
                PendingPlacements.Add(new Pending { Id = id, Admin = admin, Sender = sender, Until = Time.time + 30f });
                return;
            }
            Placed(zdo, admin, sender);
        }

        private static void Placed(ZDO zdo, bool admin, long sender)
        {
            var prefab = zdo.GetPrefab();
            var observer = ObserverPiece.IsObserver(prefab);
            if (!observer && prefab != PhantomPiece.PrefabHash)
            {
                return;
            }
            if (admin)
            {
                WriteBool(zdo, AdminKey, true);
            }
            var blocked = !admin && Settings.AdminOnlyBuilding.Value;
            if (blocked)
            {
                WriteBool(zdo, BlockedKey, true);
                SendMessage(sender, "Only admins can build these on this server, so this one stays switched off.");
            }

            if (!observer)
            {
                Phantoms.Add(zdo, admin, blocked);
                return;
            }
            var o = Read(zdo);
            o.AdminPlaced = admin;
            o.Blocked = blocked;
            All[zdo.m_uid] = o;
            Log.Info($"Observer built: {Describe(o)}" + (blocked ? " (blocked: only admins can build)." : "."));
            Changed?.Invoke();
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
        /// Server: sets a value on an observer's or phantom's ZDO. A player who owns it and is near it sets it
        /// themselves (so two machines never write the same ZDO at once); otherwise the server takes ownership and
        /// sets it. The ownership rule hands it back to a nearby player within one pass.
        /// </summary>
        internal static void WriteBool(ZDO zdo, string key, bool value)
        {
            if (!RouteToNearOwner(zdo, SetBoolRpc, key, value))
            {
                zdo.SetOwner(ZDOMan.GetSessionID());
                zdo.Set(key, value);
            }
        }

        internal static void WriteLong(ZDO zdo, string key, long value)
        {
            if (!RouteToNearOwner(zdo, SetLongRpc, key, value))
            {
                zdo.SetOwner(ZDOMan.GetSessionID());
                zdo.Set(key, value);
            }
        }

        private static bool RouteToNearOwner(ZDO zdo, string rpc, string key, object value)
        {
            var owner = zdo.GetOwner();
            if (owner == 0 || owner == ZDOMan.GetSessionID())
            {
                return false;
            }
            var peer = ZNet.instance.GetPeer(owner);
            if (peer == null || !ZNetScene.InActiveArea(zdo.GetPosition(), peer.GetRefPos()))
            {
                return false;
            }
            ZRoutedRpc.instance.InvokeRoutedRPC(owner, zdo.m_uid, rpc, key, value);
            return true;
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
