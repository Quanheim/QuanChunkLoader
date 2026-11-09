using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using QuanChunkLoader.Core;

namespace QuanChunkLoader
{
    /// <summary>
    /// Pauses vanilla Eitr radiation in loaded zones while nobody is near (docs/design.md, "Unattended bases").
    /// Dropped Refined Eitr and a running eitr refinery carry a Radiator, which fires a 30 lightning + 30 poison bolt
    /// in a random direction every few seconds on whichever machine owns them. Without a chunk loader that only
    /// happens with a player there; with one, an unattended pile can wreck a base (Quanheim, 9 Nov 2026).
    /// The one IsOwner() call in Radiator.UpdateLoop is swapped for <see cref="MayFire"/>. A player who comes near
    /// takes ownership and their own game fires as normal.
    /// </summary>
    internal static class RadiationPause
    {
        private static int _heldBack;

        /// <summary>Applied on its own: if a game update breaks it, only this feature is lost.</summary>
        internal static void Apply(Harmony harmony)
        {
            try
            {
                var loop = AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(Radiator), "UpdateLoop"));
                harmony.Patch(loop, transpiler: new HarmonyMethod(typeof(RadiationPause), nameof(Transpiler)));
            }
            catch (Exception e)
            {
                Log.Warning($"Couldn't patch Radiator, so \"Pause Eitr radiation while nobody is near\" is off: {e.Message}");
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var isOwner = AccessTools.Method(typeof(ZNetView), nameof(ZNetView.IsOwner));
            var gate = AccessTools.Method(typeof(RadiationPause), nameof(MayFire));
            var list = instructions.ToList();
            var found = 0;
            foreach (var ins in list.Where(i => i.Calls(isOwner)))
            {
                // Same stack: the ZNetView in, a bool out. Labels stay on the instruction.
                ins.opcode = OpCodes.Call;
                ins.operand = gate;
                found++;
            }
            if (found != 1)
            {
                throw new InvalidOperationException($"expected one ZNetView.IsOwner call in Radiator.UpdateLoop, found {found}");
            }
            return list;
        }

        private static bool MayFire(ZNetView nview)
        {
            if (!nview.IsOwner())
            {
                return false;
            }
            if (!ServerLoader.Running || !Settings.PauseRadiation.Value)
            {
                return true;
            }
            var pos = nview.transform.position;
            if (!Observers.IsForced(ZoneSystem.GetZone(pos)) || ServerLoader.PlayerNear(pos))
            {
                return true;
            }
            _heldBack++;
            return false;
        }

        /// <summary>Bolts held back since the last call (for the 5-minute summary).</summary>
        internal static int TakeHeldBack()
        {
            var n = _heldBack;
            _heldBack = 0;
            return n;
        }
    }
}
