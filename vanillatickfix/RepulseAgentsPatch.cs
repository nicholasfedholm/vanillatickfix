using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace vanillatickfix;

/// <summary>
/// EntityBehaviorRepulseAgents.OnGameTick builds two delegates per call
/// (WalkEntity, IsInRangePartition). Replace the ldftn+newobj pairs with cached delegates.
/// WalkEntity: one delegate per behavior instance. IsInRangePartition always returns true and
/// ignores 'this', so one static delegate is equivalent.
/// </summary>
public static class RepulseAgentsPatch
{
    private static readonly ConditionalWeakTable<EntityBehaviorRepulseAgents, ActionConsumable<Entity>> walkCache = new();
    private static readonly ConditionalWeakTable<EntityBehaviorRepulseAgents, ActionConsumable<Entity>>.CreateValueCallback createWalk = CreateWalk;
    private static MethodInfo walkEntity;
    private static EntityPartitioning.RangeTestDelegate rangeAlwaysTrue = AlwaysInRange;

    public static void Apply(Harmony harmony)
    {
        walkEntity = AccessTools.Method(typeof(EntityBehaviorRepulseAgents), "WalkEntity", new[] { typeof(Entity) })
            ?? throw new Exception("WalkEntity not found");
        if (AccessTools.Method(typeof(EntityBehaviorRepulseAgents), "IsInRangePartition") == null)
            throw new Exception("IsInRangePartition not found");
        var target = AccessTools.Method(typeof(EntityBehaviorRepulseAgents), "OnGameTick", new[] { typeof(float) })
            ?? throw new Exception("OnGameTick not found");
        harmony.Patch(target, transpiler: new HarmonyMethod(typeof(RepulseAgentsPatch), nameof(Transpiler)));
    }

    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var getWalk = AccessTools.Method(typeof(RepulseAgentsPatch), nameof(GetWalk));
        var getRange = AccessTools.Method(typeof(RepulseAgentsPatch), nameof(GetRange));
        int replaced = 0;
        for (int i = 0; i + 1 < codes.Count; i++)
        {
            if (codes[i].opcode != OpCodes.Ldftn || codes[i + 1].opcode != OpCodes.Newobj) continue;
            if (codes[i].operand is not MethodInfo m) continue;
            // the instance (ldarg.0) is already on the stack
            if (m.Name == "WalkEntity" && i > 0 && codes[i - 1].opcode == OpCodes.Ldarg_0)
            {
                codes[i].opcode = OpCodes.Call; codes[i].operand = getWalk;
                codes[i + 1].opcode = OpCodes.Nop; codes[i + 1].operand = null;
                replaced++;
            }
            else if (m.Name == "IsInRangePartition" && i > 0 && codes[i - 1].opcode == OpCodes.Ldarg_0)
            {
                codes[i].opcode = OpCodes.Call; codes[i].operand = getRange;
                codes[i + 1].opcode = OpCodes.Nop; codes[i + 1].operand = null;
                replaced++;
            }
        }
        if (replaced != 2) throw new Exception("expected 2 delegate sites, found " + replaced);
        return codes;
    }

    private static ActionConsumable<Entity> CreateWalk(EntityBehaviorRepulseAgents inst)
    {
        return (ActionConsumable<Entity>)Delegate.CreateDelegate(typeof(ActionConsumable<Entity>), inst, walkEntity);
    }

    public static ActionConsumable<Entity> GetWalk(EntityBehaviorRepulseAgents inst)
    {
        return walkCache.GetValue(inst, createWalk);
    }

    public static EntityPartitioning.RangeTestDelegate GetRange(EntityBehaviorRepulseAgents inst)
    {
        return rangeAlwaysTrue;
    }

    private static bool AlwaysInRange(Entity e, double posX, double posY, double posZ, double radiusSq) => true;
}
