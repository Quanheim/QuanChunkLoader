using UnityEngine;

namespace QuanChunkLoader.Core
{
    /// <summary>
    /// The empty-server hour (docs/design.md, "The empty-server hour"). Vanilla only advances the world clock while
    /// a player is connected (ZNet.UpdateNetTime). When the last player logs off and an observer is on, this keeps
    /// it running in real time for up to the configured window, then lets it stop as vanilla does.
    /// - Real time only, never a lump: each frame adds that frame's real seconds.
    /// - The window restarts on each logout, but the clock never runs faster than real time.
    /// - No window after a restart with nobody online: it only opens when a player leaves.
    /// </summary>
    internal static class EmptyServer
    {
        private static int _lastPlayers;
        private static bool _open;
        private static float _openedAt;
        private static double _added;

        internal static void Reset()
        {
            _lastPlayers = 0;
            _open = false;
            _added = 0;
        }

        internal static void Update(float dt)
        {
            if (!ZNet.instance.IsDedicated())
            {
                return; // a local game ends when its host leaves
            }
            var players = ZNet.instance.GetNrOfPlayers();
            if (players > 0)
            {
                if (_open)
                {
                    Close($"a player joined after {Minutes(Time.realtimeSinceStartup - _openedAt)}");
                }
                _lastPlayers = players;
                return;
            }

            if (_lastPlayers > 0)
            {
                _lastPlayers = 0;
                var window = Settings.EmptyServerMinutes.Value;
                if (window > 0 && Observers.Forced.Count > 0)
                {
                    _open = true;
                    _openedAt = Time.realtimeSinceStartup;
                    _added = 0;
                    Log.Info($"Last player left: the world clock keeps running for up to {window} min while " +
                             $"{Observers.Forced.Count} zone(s) are loaded.");
                }
            }
            if (!_open)
            {
                return;
            }

            var elapsed = Time.realtimeSinceStartup - _openedAt;
            if (elapsed >= Settings.EmptyServerMinutes.Value * 60f)
            {
                Close("the window ended");
                return;
            }
            if (Observers.Forced.Count == 0)
            {
                Close("no observer is on any more");
                return;
            }
            GameAccess.NetTime(ZNet.instance) += dt;
            _added += dt;
        }

        private static void Close(string why)
        {
            _open = false;
            Log.Info($"Empty-server clock stopped ({why}); world time advanced {Minutes((float)_added)} with nobody online.");
        }

        private static string Minutes(float seconds) => $"{seconds / 60f:0.0} min";
    }
}
