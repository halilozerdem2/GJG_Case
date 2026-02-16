using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "InterventionStrategySet", menuName = "Scriptable Objects/Interventions/Strategy Set")]
public class InterventionStrategySet : ScriptableObject
{
    [SerializeField] private List<StrategyEntry> strategies = new List<StrategyEntry>();

    public IReadOnlyList<StrategyEntry> Strategies => strategies ?? (IReadOnlyList<StrategyEntry>)Array.Empty<StrategyEntry>();

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (strategies == null) strategies = new List<StrategyEntry>();
    }
#endif

    [Serializable]
    public struct StrategyEntry
    {
        [SerializeField] private StrategyId id;
        [SerializeField, Min(0f)] private float baseWeight;
        [SerializeField] private AnimationCurve weightOverProgress;
        [SerializeField, Min(0f)] private float minBudgetRequired;
        [SerializeField, Min(0f)] private float cooldownSeconds;
        [SerializeField] private bool allowWhenNearFail;
        [SerializeField, Min(0)] private int minComboRequired;

        public StrategyId Id => id;
        public float BaseWeight => Mathf.Max(0f, baseWeight);
        public AnimationCurve WeightOverProgress => weightOverProgress;
        public float MinBudgetRequired => Mathf.Max(0f, minBudgetRequired);
        public float CooldownSeconds => Mathf.Max(0f, cooldownSeconds);
        public bool AllowWhenNearFail => allowWhenNearFail;
        public int MinComboRequired => Mathf.Max(0, minComboRequired);
    }
}

public enum StrategyId
{
    Manipulation1,
    Manipulation2,
    Manipulation3
}

