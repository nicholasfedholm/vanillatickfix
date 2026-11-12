using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;

namespace vanillatickfix;

/// <summary>
/// AnimationManager.OnServerTick: ActiveAnimationsByAnimCode.Any(pred) goes through the
/// IEnumerable interface and boxes the dictionary enumerator every call. Replace with a
/// helper that takes the Dictionary directly: Count==0 short-circuit, struct enumerator otherwise.
/// Same predicate, same iteration order, same result.
/// </summary>
public static class AnimationManagerPatch
{
    public static void Apply(Harmony harmony)
    {
        var target = AccessTools.Method(typeof(AnimationManager), "OnServerTick", new[] { typeof(float) })
            ?? throw new Exception("AnimationManager.OnServerTick not found");
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(AnimationManagerPatch), nameof(Transpiler)));
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var helper = AccessTools.Method(typeof(AnimationManagerPatch), nameof(AnyDict));
        int replaced = 0;
        foreach (var ins in codes)
        {
            if (ins.opcode != OpCodes.Call || ins.operand is not MethodInfo mi) continue;
            if (!mi.IsGenericMethod || mi.DeclaringType != typeof(Enumerable) || mi.Name != "Any" || mi.GetParameters().Length != 2) continue;
            var ga = mi.GetGenericArguments();
            if (ga.Length != 1 || ga[0] != typeof(KeyValuePair<string, AnimationMetaData>)) continue;
            ins.operand = helper;
            replaced++;
        }
        if (replaced != 1) throw new Exception("expected 1 Any site, found " + replaced);
        return codes;
    }

    public static bool AnyDict(Dictionary<string, AnimationMetaData> dict, System.Func<KeyValuePair<string, AnimationMetaData>, bool> pred)
    {
        if (dict.Count == 0) return false;
        foreach (var kv in dict)
        {
            if (pred(kv)) return true;
        }
        return false;
    }
}
