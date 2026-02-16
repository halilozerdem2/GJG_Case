using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class InterventionDirector : MonoBehaviour
{
    [Header("Configs")]
    [SerializeField] private InterventionLevelConfig levelConfig;
    [SerializeField] private InterventionStrategySet strategySet;
    [SerializeField, HideInInspector] private bool verboseLogging = false;

    [Header("Runtime State (Read-Only)")]
    [SerializeField] private float currentBudget;
    [SerializeField] private bool nearFail;
    [SerializeField] private int lastBigComboSize;
    [SerializeField] private int moveIndex;
    [SerializeField] private float globalCooldownRelease;

    public static InterventionDirector Instance { get; private set; }

    private StrategyBasedInterventionSelector selector;
    private readonly Queue<int> interventionMoveHistory = new Queue<int>();
    private readonly HashSet<LilStateId> runningStates = new HashSet<LilStateId>();

    private float progress01; // objective progress 0..1
    private bool safeWindowOpen;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        selector = new StrategyBasedInterventionSelector(strategySet);
    }

    private void OnEnable()
    {
        GameEventBus.OnLevelStart += HandleLevelStart;
        GameEventBus.OnLevelEnd += HandleLevelEnd;
        GameEventBus.OnMoveCommitted += HandleMoveCommitted;
        GameEventBus.OnBigCombo += HandleBigCombo;
        GameEventBus.OnNearFail += HandleNearFail;
        GameEventBus.OnObjectiveAlmostDone += HandleObjectiveAlmostDone;

        var gate = SafeWindowGate.Instance;
        if (gate != null)
        {
            gate.WindowOpened += HandleSafeWindowOpened;
            gate.WindowClosed += HandleSafeWindowClosed;
        }

        SceneManager.sceneLoaded += HandleSceneLoaded;

        SubscribeBlockManager();
    }

    private void OnDisable()
    {
        GameEventBus.OnLevelStart -= HandleLevelStart;
        GameEventBus.OnLevelEnd -= HandleLevelEnd;
        GameEventBus.OnMoveCommitted -= HandleMoveCommitted;
        GameEventBus.OnBigCombo -= HandleBigCombo;
        GameEventBus.OnNearFail -= HandleNearFail;
        GameEventBus.OnObjectiveAlmostDone -= HandleObjectiveAlmostDone;

        var gate = SafeWindowGate.Instance;
        if (gate != null)
        {
            gate.WindowOpened -= HandleSafeWindowOpened;
            gate.WindowClosed -= HandleSafeWindowClosed;
        }

        UnsubscribeBlockManager();
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SubscribeBlockManager();
    }

    private void SubscribeBlockManager()
    {
        var bm = FindObjectOfType<BlockManager>();
        if (bm != null)
        {
            bm.StaticTargetProgressChanged += HandleStaticProgress;
        }
    }

    private void UnsubscribeBlockManager()
    {
        var bm = FindObjectOfType<BlockManager>();
        if (bm != null)
        {
            bm.StaticTargetProgressChanged -= HandleStaticProgress;
        }
    }

    private void HandleLevelStart()
    {
        // Resolve configs from active GameModeConfig if not assigned
        var gm = GameManager.Instance;
        var activeCfg = gm != null ? gm.ActiveGameModeConfig : null;
        if (activeCfg != null)
        {
            if (activeCfg.InterventionConfig != null) levelConfig = activeCfg.InterventionConfig;
            if (activeCfg.InterventionStrategies != null)
            {
                strategySet = activeCfg.InterventionStrategies;
                selector = new StrategyBasedInterventionSelector(strategySet);
            }
        }

        // Fallback defaults from Resources if still missing
        if (levelConfig == null)
        {
            levelConfig = Resources.Load<InterventionLevelConfig>("Interventions/DefaultLevelConfig");
        }
        if (strategySet == null)
        {
            strategySet = Resources.Load<InterventionStrategySet>("Interventions/DefaultStrategySet");
            if (strategySet != null) selector = new StrategyBasedInterventionSelector(strategySet);
        }

        

        currentBudget = levelConfig != null ? levelConfig.StartingBudget : 0f;
        moveIndex = 0;
        interventionMoveHistory.Clear();
        progress01 = 0f;
        nearFail = false;
        lastBigComboSize = 0;
        globalCooldownRelease = 0f;
    }

    private void HandleLevelEnd(LevelEndResult result)
    {
        // Outcome states via EffectRunner if needed
        var outcome = result == LevelEndResult.Win ? LilStateId.Win : LilStateId.Lose;
        EffectRunner.Instance?.PlayOutcome(outcome);
        safeWindowOpen = false;
    }

    private void HandleMoveCommitted()
    {
        moveIndex++;
        GainBudget(BudgetGainTrigger.OnMoveCommitted, 0);
        TryTriggerIfAllowed();
    }

    private void HandleBigCombo(int size)
    {
        lastBigComboSize = size;
        GainBudget(BudgetGainTrigger.OnBigCombo, size);
        TryTriggerIfAllowed();
    }

    private void HandleNearFail()
    {
        nearFail = true;
        GainBudget(BudgetGainTrigger.OnNearFail, 0);
        TryTriggerIfAllowed();
    }

    private void HandleObjectiveAlmostDone()
    {
        GainBudget(BudgetGainTrigger.OnObjectiveAlmostDone, 0);
        TryTriggerIfAllowed();
    }

    private void HandleStaticProgress(int blockType, int collected, int total)
    {
        progress01 = total > 0 ? Mathf.Clamp01((float)collected / total) : 0f;
    }

    private void HandleSafeWindowOpened()
    {
        safeWindowOpen = true;
        
        TryTriggerIfAllowed();
    }

    private void HandleSafeWindowClosed()
    {
        safeWindowOpen = false;
        
    }

    private void GainBudget(BudgetGainTrigger trigger, int comboSize)
    {
        if (levelConfig == null) return;
        var rules = levelConfig.BudgetGainRules;
        for (int i = 0; i < rules.Count; i++)
        {
            var r = rules[i];
            if (r.Trigger != trigger) continue;
            if (trigger == BudgetGainTrigger.OnBigCombo && comboSize < r.MinComboSize) continue;
            currentBudget = Mathf.Min(levelConfig.MaxBudget, currentBudget + r.Amount);
        }
    }

    private void TryTriggerIfAllowed()
    {
        if (!safeWindowOpen) return;
        if (levelConfig == null) return;

        // Global cooldown
        if (Time.unscaledTime < globalCooldownRelease) return;

        // Min moves between interventions
        if (interventionMoveHistory.Count > 0)
        {
            int lastMove = 0;
            foreach (var m in interventionMoveHistory) { lastMove = m; }
            if (moveIndex - lastMove < levelConfig.MinMovesBetweenInterventions) return;
        }

        // Max interventions per window
        TrimHistoryToWindow();
        if (interventionMoveHistory.Count >= levelConfig.MaxInterventionsPerWindow) return;

        // Random chance with progress curve
        float chance = levelConfig.BaseChance;
        if (levelConfig.ChanceOverProgress != null)
        {
            chance *= Mathf.Clamp01(levelConfig.ChanceOverProgress.Evaluate(progress01));
        }
        float roll = UnityEngine.Random.value;
        if (roll > chance) return;

        // State busy + Board safety
        var sm = LilController.Instance != null ? LilController.Instance.StateMachine : null;
        if (sm != null && sm.IsOneShotActive) return;

        var safety = BoardSafetyService.Instance;
        if (levelConfig.EnsureLegalMoveAfter && safety != null && !safety.HasAtLeastOneLegalMove()) return;

        // Compose context and select strategy
        var ctx = new InterventionContext
        {
            Progress01 = progress01,
            NearFail = nearFail,
            LastBigComboSize = lastBigComboSize,
            AvailableBudget = currentBudget
        };

        if (strategySet == null || strategySet.Strategies.Count == 0) return;

        if (!selector.TrySelect(ctx, out StrategyId chosen)) return;

        // Spend budget (use min required as cost for now)
        var entry = FindEntry(chosen);
        float cost = entry.HasValue ? Mathf.Max(0f, entry.Value.MinBudgetRequired) : 0f;
        if (currentBudget < cost) return;
        currentBudget = Mathf.Max(0f, currentBudget - cost);

        // Trigger effect
        
        EffectRunner.Instance?.PlayStrategy(chosen);
        interventionMoveHistory.Enqueue(moveIndex);

        // Start global cooldown
        globalCooldownRelease = Time.unscaledTime + Mathf.Max(0f, levelConfig.GlobalCooldownSeconds);
    }

    private void TrimHistoryToWindow()
    {
        int windowStart = Mathf.Max(0, moveIndex - Mathf.Max(1, levelConfig.WindowSizeMoves) + 1);
        while (interventionMoveHistory.Count > 0 && interventionMoveHistory.Peek() < windowStart)
        {
            interventionMoveHistory.Dequeue();
        }
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
