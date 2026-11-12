using System;
using HarmonyLib;
using Vintagestory.API.Common;

namespace vanillatickfix;

public class TickFixConfig
{
    public bool RepulseAgentsDelegateCache = true;
    public bool AnimationManagerAnyFix = true;
    public bool MechanicalPowerPooledList = true;
}

public class vanillatickfixModSystem : ModSystem
{
    // Harmony patches are process-wide, and single-player runs the client and the
    // integrated server in ONE process, so this ModSystem is instantiated twice.
    // Patch once per process and unpatch only when the last user disposes.
    private static readonly object gate = new();
    private static Harmony harmony;
    private static int users;
    private bool counted;
    public const string HarmonyId = "vanillatickfix";

    public override bool ShouldLoad(EnumAppSide forSide) => true;

    public override void Start(ICoreAPI api)
    {
        lock (gate)
        {
        users++;
        counted = true;
        if (harmony != null)
        {
            api.Logger.Notification("[vanillatickfix] patches already applied in this process, skipping on {0} side", api.Side);
            return;
        }

        TickFixConfig cfg;
        try
        {
            cfg = api.LoadModConfig<TickFixConfig>("vanillatickfix.json");
            if (cfg == null)
            {
                cfg = new TickFixConfig();
            }
            api.StoreModConfig(cfg, "vanillatickfix.json");
        }
        catch (Exception e)
        {
            api.Logger.Warning("[vanillatickfix] config load failed, using defaults: {0}", e.Message);
            cfg = new TickFixConfig();
        }

        harmony = new Harmony(HarmonyId);
        Try(api, "RepulseAgentsDelegateCache", cfg.RepulseAgentsDelegateCache, () => RepulseAgentsPatch.Apply(harmony));
        Try(api, "AnimationManagerAnyFix", cfg.AnimationManagerAnyFix, () => AnimationManagerPatch.Apply(harmony));
        Try(api, "MechanicalPowerPooledList", cfg.MechanicalPowerPooledList, () => MechanicalPowerPatch.Apply(harmony));
        }
    }

    private static void Try(ICoreAPI api, string name, bool enabled, Action apply)
    {
        if (!enabled)
        {
            api.Logger.Notification("[vanillatickfix] {0}: disabled by config", name);
            return;
        }
        try
        {
            apply();
            api.Logger.Notification("[vanillatickfix] {0}: applied", name);
        }
        catch (Exception e)
        {
            api.Logger.Warning("[vanillatickfix] {0}: FAILED, left vanilla. {1}", name, e);
        }
    }

    public override void Dispose()
    {
        lock (gate)
        {
            if (!counted) return;
            counted = false;
            users--;
            if (users <= 0)
            {
                harmony?.UnpatchAll(HarmonyId);
                harmony = null;
                users = 0;
            }
        }
    }
}
