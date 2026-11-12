# vanillatickfix 0.1.0

Behavior-neutral Harmony fixes for vanilla per-tick allocations (VS 1.22.x). Config: `ModConfig/vanillatickfix.json`, one bool per patch (default true). Each patch is applied in its own try/catch; a failure logs a warning and leaves the vanilla method untouched.

## Patches

| Config key | Target | Replaces | Risk |
|---|---|---|---|
| RepulseAgentsDelegateCache | EntityBehaviorRepulseAgents.OnGameTick (transpiler) | Two `new delegate` per call (WalkEntity, IsInRangePartition). WalkEntity delegate is cached per behavior instance (ConditionalWeakTable). IsInRangePartition always returns true and ignores `this`, so one static delegate is used. | Low. Same method bodies run. Transpiler throws (patch skipped) unless exactly 2 sites match. |
| AnimationManagerAnyFix | AnimationManager.OnServerTick (transpiler) | `ActiveAnimationsByAnimCode.Any(pred)` (interface call, boxes the dictionary enumerator) with a helper taking the Dictionary: Count==0 returns false, otherwise a struct-enumerator loop over the same predicate. | Low. Same predicate, order, result. |
| MechanicalPowerPooledList | MechanicalPowerMod.OnServerGameTick (transpiler + finalizer) | `networksById.Values.ToList()` with a reused thread-static snapshot list, cleared after the tick. Snapshot semantics kept (needed because ServerTick may add/remove networks). | Low. Server main-thread tick only; reentrancy would share the list (none exists in source). |

## Skipped (not provably neutral)

- EntityPartitioning pooling: the physics thread (RepulseAgents, ThreadSafe=true) walks `Partitions` lists concurrently with the 32 ms rebuild. Today the rebuild publishes fresh lists, so a reader holding an old list never sees it mutate. Reusing lists would let a reader's `foreach` hit "collection modified" or see half-built data. Also `EntityPlayer.entityListForPartitioning` keeps a reference to a list and `RePartitionPlayer` removes from it. Needs a double-buffer or generation scheme with reader audit; not a v1 patch.
- WeatherSimulationSnowAccum gating: the 3 s tick also feeds the snow scanner thread, which handles melting and other per-column updates, not only precipitation snow. Gating on "no precipitation" changes melting timing. Not equivalent.

## Verification plan

Build and JIT-check of patched IL done offline (all 3 targets patch and JIT). In-game verification not run. See the build report.
