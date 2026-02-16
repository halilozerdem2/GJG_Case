using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "InterventionLevelConfig", menuName = "Scriptable Objects/Interventions/Level Config")]
public class InterventionLevelConfig : ScriptableObject
{
    [Header("Chance / Intensity")]
    [SerializeField, Range(0f, 1f)] private float baseChance = 0.25f;
    [SerializeField] private AnimationCurve chanceOverProgress = AnimationCurve.Linear(0f, 1f, 1f, 1f);
    [SerializeField] private AnimationCurve intensityOverProgress = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Budget Settings")]
    [SerializeField, Min(0f)] private float startingBudget = 0f;
    [SerializeField, Min(0f)] private float maxBudget = 5f;
    [SerializeField] private List<BudgetGainRule> budgetGainRules = new List<BudgetGainRule>();

    [Header("Cooldowns & Guardrails")]
    [SerializeField, Min(0f)] private float globalCooldownSeconds = 5f;
    [SerializeField, Min(0)] private int minMovesBetweenInterventions = 3;
    [SerializeField, Min(1)] private int windowSizeMoves = 10;
    [SerializeField, Min(0)] private int maxInterventionsPerWindow = 2;

    [Header("Safety")]
    [SerializeField] private bool ensureLegalMoveAfter = true;
    [SerializeField, Min(1)] private int minLegalMovesAfter = 1;

    [Header("Severity Thresholds (context gates)")]
    [SerializeField, Range(0f, 1f)] private float nearFailMovesRatioThreshold = 0.15f;
    [SerializeField, Min(0)] private int bigComboThreshold = 6;

    public float BaseChance => Mathf.Clamp01(baseChance);
    public AnimationCurve ChanceOverProgress => chanceOverProgress;
    public AnimationCurve IntensityOverProgress => intensityOverProgress;
    public float StartingBudget => Mathf.Max(0f, startingBudget);
    public float MaxBudget => Mathf.Max(0f, maxBudget);
    public IReadOnlyList<BudgetGainRule> BudgetGainRules => budgetGainRules ?? (IReadOnlyList<BudgetGainRule>)Array.Empty<BudgetGainRule>();
    public float GlobalCooldownSeconds => Mathf.Max(0f, globalCooldownSeconds);
    public int MinMovesBetweenInterventions => Mathf.Max(0, minMovesBetweenInterventions);
    public int WindowSizeMoves => Mathf.Max(1, windowSizeMoves);
    public int MaxInterventionsPerWindow => Mathf.Max(0, maxInterventionsPerWindow);
    public bool EnsureLegalMoveAfter => ensureLegalMoveAfter;
    public int MinLegalMovesAfter => Mathf.Max(1, minLegalMovesAfter);
    public float NearFailMovesRatioThreshold => Mathf.Clamp01(nearFailMovesRatioThreshold);
    public int BigComboThreshold => Mathf.Max(0, bigComboThreshold);

#if UNITY_EDITOR
    private void OnValidate()
    {
        baseChance = Mathf.Clamp01(baseChance);
        startingBudget = Mathf.Max(0f, startingBudget);
        maxBudget = Mathf.Max(0f, maxBudget);
        globalCooldownSeconds = Mathf.Max(0f, globalCooldownSeconds);
        minMovesBetweenInterventions = Mathf.Max(0, minMovesBetweenInterventions);
        windowSizeMoves = Mathf.Max(1, windowSizeMoves);
        maxInterventionsPerWindow = Mathf.Max(0, maxInterventionsPerWindow);
        minLegalMovesAfter = Mathf.Max(1, minLegalMovesAfter);
        nearFailMovesRatioThreshold = Mathf.Clamp01(nearFailMovesRatioThreshold);
        bigComboThreshold = Mathf.Max(0, bigComboThreshold);
        if (budgetGainRules == null) budgetGainRules = new List<BudgetGainRule>();
    }
#endif

    [Serializable]
    public struct BudgetGainRule
    {
        [SerializeField] private BudgetGainTrigger trigger;
        [SerializeField, Min(0f)] private float amount;
        [SerializeField, Min(0)] private int minComboSize;

        public BudgetGainTrigger Trigger => trigger;
        public float Amount => Mathf.Max(0f, amount);
        public int MinComboSize => Mathf.Max(0, minComboSize);
    }
}

public enum BudgetGainTrigger
{
    OnMoveCommitted,
    OnCascadeEnded,
    OnBigCombo,
    OnNearFail,
    OnObjectiveAlmostDone
}

