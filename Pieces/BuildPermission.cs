using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Managers;

namespace QuanChunkLoader
{
    /// <summary>
    /// "Only admins can build observers and phantoms" on the client: hides this mod's pieces from non-admins'
    /// hammers (Piece.m_enabled, which PieceTable.UpdateAvailable reads). The server is the safety net: a piece a
    /// non-admin builds anyway stays switched off (Observers.Placed).
    /// </summary>
    internal static class BuildPermission
    {
        private static readonly List<Piece> Pieces = new List<Piece>();

        private static readonly System.Action<Player> UpdateAvailablePieces =
            AccessTools.MethodDelegate<System.Action<Player>>(AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList"));

        internal static void Track(Piece piece)
        {
            if (piece != null)
            {
                Pieces.Add(piece);
                Apply();
            }
        }

        internal static void Hook()
        {
            Settings.AdminOnlyBuilding.SettingChanged += (_, __) => Apply();
            SynchronizationManager.OnAdminStatusChanged += Apply;
            SynchronizationManager.OnConfigurationSynchronized += (_, __) => Apply();
        }

        internal static void Apply()
        {
            var allowed = !Settings.AdminOnlyBuilding.Value || SynchronizationManager.Instance.PlayerIsAdmin;
            var changed = false;
            foreach (var piece in Pieces)
            {
                if (piece != null && piece.m_enabled != allowed)
                {
                    piece.m_enabled = allowed;
                    changed = true;
                }
            }
            if (changed && Player.m_localPlayer != null)
            {
                UpdateAvailablePieces(Player.m_localPlayer);
            }
        }
    }
}
