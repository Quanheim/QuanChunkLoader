using System.Collections.Generic;
using UnityEngine;

namespace QuanChunkLoader.Core
{
    /// <summary>
    /// Debug log only: follows every tame in the loaded zones, to find out how tames end up outside their pens
    /// (live server, 7 Nov 2026: three tame deer were outside a pen with no gaps after the empty-server hour).
    /// Each ownership pass it notes each tame's position and logs a line when one has moved 10 m or more from where
    /// it was last logged, jumps a long way in one pass, or changes who runs it. Off unless Log detail is Debug or
    /// more, and then it only reads the tames' saved data.
    /// </summary>
    internal static class TameWatch
    {
        private const float MoveStep = 10f;   // logged each time a tame is this far from where it was last logged
        private const float JumpDistance = 25f; // further than a tame walks or runs in one 2 s pass

        private struct Track
        {
            internal Vector3 Logged;   // where it was when last logged (or first seen)
            internal Vector3 Last;     // where it was last pass
            internal long Owner;
        }

        private static readonly Dictionary<ZDOID, Track> Tracks = new Dictionary<ZDOID, Track>();
        private static readonly HashSet<ZDOID> Seen = new HashSet<ZDOID>();
        private static int _moves;
        private static int _jumps;
        private static int _handovers;

        internal static void Reset()
        {
            Tracks.Clear();
            Seen.Clear();
            _moves = _jumps = _handovers = 0;
        }

        /// <summary>Called once per tame per ownership pass, after the pass has settled who owns it.</summary>
        internal static void Note(ZDO zdo, long owner)
        {
            if (!Log.DebugOn)
            {
                return;
            }
            var id = zdo.m_uid;
            var pos = zdo.GetPosition();
            Seen.Add(id);
            if (!Tracks.TryGetValue(id, out var t))
            {
                Tracks[id] = new Track { Logged = pos, Last = pos, Owner = owner };
                return;
            }

            if (owner != t.Owner)
            {
                _handovers++;
                Log.Debug($"Tame watch: {Describe(zdo)} at {Where(pos)} passed from {Observers.PlayerName(t.Owner)} " +
                          $"to {Observers.PlayerName(owner)}.");
                t.Owner = owner;
            }

            var step = Distance(t.Last, pos);
            if (step >= JumpDistance)
            {
                _jumps++;
                Log.Debug($"Tame watch: {Describe(zdo)} jumped {step:0} m in one pass, from {Where(t.Last)} to " +
                          $"{Where(pos)}; run by {Observers.PlayerName(owner)}.");
                t.Logged = pos;
            }
            else if (Distance(t.Logged, pos) >= MoveStep)
            {
                _moves++;
                Log.Debug($"Tame watch: {Describe(zdo)} moved {Distance(t.Logged, pos):0} m, from {Where(t.Logged)} to " +
                          $"{Where(pos)}; run by {Observers.PlayerName(owner)}.");
                t.Logged = pos;
            }

            t.Last = pos;
            Tracks[id] = t;
        }

        /// <summary>Forgets tames not seen this pass (left the loaded zones, died, or the zones changed).</summary>
        internal static void EndPass()
        {
            if (!Log.DebugOn)
            {
                if (Tracks.Count > 0)
                {
                    Reset();
                }
                return;
            }
            if (Tracks.Count != Seen.Count)
            {
                var gone = new List<ZDOID>();
                foreach (var id in Tracks.Keys)
                {
                    if (!Seen.Contains(id))
                    {
                        gone.Add(id);
                    }
                }
                foreach (var id in gone)
                {
                    Tracks.Remove(id);
                }
            }
            Seen.Clear();
        }

        /// <summary>One line with the 5-minute summary.</summary>
        internal static void Summary()
        {
            if (!Log.DebugOn)
            {
                return;
            }
            Log.Debug($"Tame watch: {Tracks.Count} tame(s) followed; in the last 5 minutes {_moves} move(s) of " +
                      $"{MoveStep:0} m or more, {_jumps} jump(s), {_handovers} change(s) of who runs them.");
            _moves = _jumps = _handovers = 0;
        }

        private static string Describe(ZDO zdo)
        {
            var prefab = ZNetScene.instance.GetPrefab(zdo.GetPrefab());
            var text = prefab != null ? prefab.name : $"tame {zdo.GetPrefab()}";
            var name = zdo.GetString(ZDOVars.s_tamedName);
            if (!string.IsNullOrEmpty(name))
            {
                text += $" \"{name}\"";
            }
            text += $" {zdo.m_uid}";
            var follow = zdo.GetString(ZDOVars.s_follow);
            if (!string.IsNullOrEmpty(follow))
            {
                text += $" (following {follow})";
            }
            return text;
        }

        private static string Where(Vector3 p) => $"({p.x:0}, {p.y:0}, {p.z:0})";

        // Ground distance: a tame falling or being lifted isn't walking.
        private static float Distance(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
