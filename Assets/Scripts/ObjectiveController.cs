using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ObjectiveController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private BlockManager blockManager;
    [SerializeField] private TargetCounterUI[] targetCounters;
    [Header("Moves UI")]
    [SerializeField] private CanvasGroup movesGroup;
    [SerializeField] private TMP_Text movesLabel;
    [Header("Time UI")]
    [SerializeField] private CanvasGroup timeGroup;
    [SerializeField] private TMP_Text timeLabel;
    [SerializeField] private Color timeWarningColor = Color.red;
    [SerializeField, Min(0f)] private float timeWarningThresholdSeconds = 10f;

    private GameManager gameManager;
    private readonly Dictionary<int, TargetProgress> targetProgressLookup = new Dictionary<int, TargetProgress>();
    private int currentMoves;
    private int maxMoves;
    private float currentTime;
    private float maxTime;
    private Color timeDefaultColor = Color.white;

    private void Awake()
    {
        ResolveReferences();
        if (timeLabel != null)
        {
            timeDefaultColor = timeLabel.color;
        }
    }

    private void OnEnable()
    {
        Subscribe();
        SyncInitialState();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void ResolveReferences()
    {
        if (blockManager == null)
        {
            blockManager = FindObjectOfType<BlockManager>();
        }

        gameManager = GameManager.Instance;
    }

    private void Subscribe()
    {
        ResolveReferences();

        if (blockManager != null)
        {
            blockManager.StaticTargetProgressChanged += HandleTargetProgressChanged;
        }

        if (gameManager != null)
        {
            gameManager.MovesChanged += HandleMovesChanged;
            gameManager.TimeChanged += HandleTimeChanged;
        }
    }

    private void Unsubscribe()
    {
        if (blockManager != null)
        {
            blockManager.StaticTargetProgressChanged -= HandleTargetProgressChanged;
        }

        if (gameManager != null)
        {
            gameManager.MovesChanged -= HandleMovesChanged;
            gameManager.TimeChanged -= HandleTimeChanged;
        }
    }

    private void SyncInitialState()
    {
        RefreshTargetCounters();

        if (gameManager != null)
        {
            HandleMovesChanged(gameManager.RemainingMoves, gameManager.MaxMoves);
            UpdateMovesVisibility(gameManager.HasMoveLimit);
            HandleTimeChanged(gameManager.RemainingTime, gameManager.TimeLimitSeconds);
            UpdateTimeVisibility(gameManager.HasTimeLimit);
        }
        else
        {
            UpdateMovesVisibility(false);
            UpdateTimeVisibility(false);
        }
    }

    private void RefreshTargetCounters()
    {
        targetProgressLookup.Clear();
        if (blockManager != null && targetCounters != null)
        {
            for (int i = 0; i < targetCounters.Length; i++)
            {
                int blockType = targetCounters[i].blockType;
                if (blockManager.TryGetStaticTargetProgress(blockType, out int collected, out int total))
                {
                    targetProgressLookup[blockType] = new TargetProgress
                    {
                        Collected = collected,
                        Total = total
                    };
                }
            }
        }

        UpdateTargetUI();
    }

    private void HandleTargetProgressChanged(int blockType, int collected, int total)
    {
        targetProgressLookup[blockType] = new TargetProgress
        {
            Collected = collected,
            Total = total
        };

        UpdateTargetUI();
    }

    private void UpdateTargetUI()
    {
        bool pendingTargets = false;

        if (targetCounters != null)
        {
            for (int i = 0; i < targetCounters.Length; i++)
            {
                var counter = targetCounters[i];
                if (targetProgressLookup.TryGetValue(counter.blockType, out TargetProgress progress) && progress.Total > 0)
                {
                    UpdateCanvasGroup(counter.group, true);
                    if (counter.label != null)
                    {
                        int remaining = Mathf.Max(0, progress.Total - progress.Collected);
                        counter.label.text = remaining.ToString();
                    }

                    if (progress.Collected < progress.Total)
                    {
                        pendingTargets = true;
                    }
                }
                else
                {
                    UpdateCanvasGroup(counter.group, false);
                    if (counter.label != null)
                    {
                        counter.label.text = string.Empty;
                    }
                }
            }
        }

        if (!pendingTargets)
        {
            foreach (var kvp in targetProgressLookup)
            {
                if (kvp.Value.Total > 0 && kvp.Value.Collected < kvp.Value.Total)
                {
                    pendingTargets = true;
                    break;
                }
            }
        }

        if (gameManager != null)
        {
            bool wasPending = !gameManager.AreObjectivesComplete;
            gameManager.SetObjectivesPending(pendingTargets);
            if (!pendingTargets && targetProgressLookup.Count > 0 && wasPending)
            {
                gameManager.ReportObjectivesCompletion();
            }
        }
    }

    private void HandleMovesChanged(int remaining, int max)
    {
        currentMoves = Mathf.Max(0, remaining);
        maxMoves = Mathf.Max(0, max);
        UpdateMovesVisibility(maxMoves > 0 && gameManager != null && gameManager.HasMoveLimit);

        if (movesLabel != null)
        {
            movesLabel.text = maxMoves > 0 ? currentMoves.ToString() : string.Empty;
        }

    }

    private void HandleTimeChanged(float remaining, float max)
    {
        currentTime = Mathf.Max(0f, remaining);
        maxTime = Mathf.Max(0f, max);
        UpdateTimeVisibility(maxTime > 0f && gameManager != null && gameManager.HasTimeLimit);

        if (timeLabel != null)
        {
            bool showTime = maxTime > 0f;
            timeLabel.text = showTime ? FormatTime(currentTime) : string.Empty;
            UpdateTimeColor(showTime);
        }

    }

    private static string FormatTime(float seconds)
    {
        int clamped = Mathf.Max(0, Mathf.CeilToInt(seconds));
        int minutes = clamped / 60;
        int secs = clamped % 60;
        return minutes > 0 ? $"{minutes:0}:{secs:00}" : secs.ToString();
    }

    private void UpdateMovesVisibility(bool visible)
    {
        UpdateCanvasGroup(movesGroup, visible);
        if (!visible && movesLabel != null)
        {
            movesLabel.text = string.Empty;
        }
    }

    private void UpdateTimeVisibility(bool visible)
    {
        UpdateCanvasGroup(timeGroup, visible);
        if (!visible && timeLabel != null)
        {
            timeLabel.text = string.Empty;
            timeLabel.color = timeDefaultColor;
        }
    }

    private void UpdateTimeColor(bool showTime)
    {
        if (timeLabel == null)
        {
            return;
        }

        if (!showTime)
        {
            timeLabel.color = timeDefaultColor;
            return;
        }

        bool useWarning = timeWarningThresholdSeconds > 0f && currentTime <= timeWarningThresholdSeconds;
        timeLabel.color = useWarning ? timeWarningColor : timeDefaultColor;
    }

    private static void UpdateCanvasGroup(CanvasGroup group, bool visible)
    {
        if (group == null)
        {
            return;
        }

        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
    }

    [System.Serializable]
    private struct TargetCounterUI
    {
        public int blockType;
        public CanvasGroup group;
        public TMP_Text label;
    }

    private struct TargetProgress
    {
        public int Collected;
        public int Total;
    }
}
