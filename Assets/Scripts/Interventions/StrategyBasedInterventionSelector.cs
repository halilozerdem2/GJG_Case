using System;
using System.Collections.Generic;
using UnityEngine;

public struct InterventionContext
{
    public float Progress01; // 0..1 objective progress (collected/total)
    public bool NearFail;
    public int LastBigComboSize;
    public float AvailableBudget;
}

public class StrategyBasedInterventionSelector
{
    private readonly InterventionStrategySet strategySet;
    private readonly Dictionary<StrategyId, float> lastCooldownRelease = new Dictionary<StrategyId, float>();

    public StrategyBasedInterventionSelector(InterventionStrategySet set)
    {
        strategySet = set;
    }

    public bool TrySelect(InterventionContext ctx, out StrategyId selected)
    {
        selected = default;
        var strategies = strategySet != null ? strategySet.Strategies : Array.Empty<InterventionStrategySet.StrategyEntry>();
        float now = Time.unscaledTime;

        float totalWeight = 0f;
        StrategyId best = default;
        float bestRoll = -1f;

        for (int i = 0; i < strategies.Count; i++)
        {
            var entry = strategies[i];

            // Cooldown gate
            if (lastCooldownRelease.TryGetValue(entry.Id, out float releaseAt) && now < releaseAt)
            {
                continue;
            }

            // Budget gate
            if (ctx.AvailableBudget < entry.MinBudgetRequired)
            {
                continue;
            }

            // Near-fail gate
            if (!entry.AllowWhenNearFail && ctx.NearFail)
            {
                continue;
            }

            // Min combo gate (optional)
            if (entry.MinComboRequired > 0 && ctx.LastBigComboSize < entry.MinComboRequired)
            {
                continue;
            }

            float w = entry.BaseWeight;
            if (entry.WeightOverProgress != null)
            {
                w *= Mathf.Max(0f, entry.WeightOverProgress.Evaluate(Mathf.Clamp01(ctx.Progress01)));
            }

            if (w <= 0f) continue;

            // Weighted random: simple roulette
            float roll = UnityEngine.Random.value * (totalWeight + w);
            if (roll > totalWeight)
            {
                best = entry.Id;
                bestRoll = roll;
            }
            totalWeight += w;
        }

        if (totalWeight <= 0f || bestRoll < 0f)
        {
            return false;
        }

        selected = best;
        // Apply per-strategy cooldown
        var sel = FindEntry(selected);
        if (sel.HasValue)
        {
            lastCooldownRelease[selected] = now + Mathf.Max(0f, sel.Value.CooldownSeconds);
        }
        return true;
    }

    private InterventionStrategySet.StrategyEntry? FindEntry(StrategyId id)
    {
        var strategies = strategySet != null ? strategySet.Strategies : Array.Empty<InterventionStrategySet.StrategyEntry>();
        for (int i = 0; i < strategies.Count; i++)
        {
            if (strategies[i].Id == id) return strategies[i];
        }
        return null;
    }
}

