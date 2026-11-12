using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace vanillatickfix;

/// <summary>
/// MechanicalPowerMod.OnServerGameTick snapshots networksById.Values with ToList() each tick
/// (the snapshot is needed because ServerTick may add/remove networks). Replace with a reused
/// snapshot list (cleared after the tick by a finalizer) so the snapshot semantics are kept.
/// </summary>
public static class MechanicalPowerPatch
{
    [ThreadStatic] private static List<MechanicalNetwork> pooled;

    public static void Apply(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(MechanicalPowerMod), "OnServerGameTick", new[] { typeof(float) })
            ?? throw new Exception("MechanicalPowerMod.OnServerGameTick not found");
        harmony.Patch(target,
            transpiler: new HarmonyMethod(typeof(MechanicalPowerPatch), nameof(Transpiler)),
            finalizer: new HarmonyMethod(typeof(MechanicalPowerPatch), nameof(Finalizer)));
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var helper = AccessTools.Method(typeof(MechanicalPowerPatch), nameof(Snapshot));
        int replaced = 0;
        foreach (var ins in codes)
        {
            if (ins.opcode != OpCodes.Call || ins.operand is not MethodInfo mi) continue;
            if (!mi.IsGenericMethod || mi.DeclaringType != typeof(Enumerable) || mi.Name != "ToList") continue;
            var ga = mi.GetGenericArguments();
            if (ga.Length != 1 || ga[0] != typeof(MechanicalNetwork)) continue;
            ins.operand = helper;
            replaced++;
        }
        if (replaced != 1) throw new Exception("expected 1 ToList site, found " + replaced);
        return codes;
    }

    public static List<MechanicalNetwork> Snapshot(IEnumerable<MechanicalNetwork> source)
    {
        var list = pooled ??= new List<MechanicalNetwork>();
        list.Clear();
        list.AddRange(source);
        return list;
    }

    // Runs on normal exit and on exception; drops network references so nothing is kept alive.
    public static Exception Finalizer(Exception __exception)
    {
        pooled?.Clear();
        return __exception;
    }
}
