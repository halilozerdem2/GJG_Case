using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class LevelButtonController : MonoBehaviour
{
    [Header("Level Info")]
    [SerializeField, Min(1)] private int levelNumber = 1;
    [SerializeField] private string levelsResourceRoot = "Levels";

    [Header("Scene Settings")]
    [SerializeField] private int gameSceneBuildIndex = 1;

    [Header("UI References")]
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text tmpLabel;

    [Header("Status Icon")]
    [SerializeField] private Image statusIcon;
    [SerializeField] private Sprite lockedIcon;
    [SerializeField] private Sprite currentIcon;
    [SerializeField] private Sprite completedIcon;

    [Header("Stars")]
    [SerializeField] private GameObject starSlot;
    [SerializeField] private StarView[] starViews;

    [Serializable]
    private struct StarView
    {
        public GameObject root;
        public GameObject visual;
        public GameObject slot;
    }

    private int starsEarned;

    public enum LevelStatus
    {
        Locked,
        Current,
        Completed
    }

    private LevelStatus status = LevelStatus.Current;

    // Optional: If provided by the spawner, avoids reloading from Resources at click time
    private GameModeConfig boundConfig;

    private void Awake()
    {
        if (button == null)
        {
            button = GetComponent<Button>();
        }

        if (tmpLabel == null)
        {
            tmpLabel = GetComponentInChildren<TMP_Text>(true);
        }

        UpdateLabel();

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
        }

        ApplyStatus();
        UpdateStarVisuals();
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClick);
        }
    }

    public void Initialize(int level, string resourceRoot, int buildIndex, GameModeConfig preboundConfig = null)
    {
        levelNumber = Mathf.Max(1, level);
        levelsResourceRoot = string.IsNullOrEmpty(resourceRoot) ? "Levels" : resourceRoot;
        gameSceneBuildIndex = Mathf.Max(0, buildIndex);
        boundConfig = preboundConfig;
        UpdateLabel();

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(HandleClick);
        }

        ApplyStatus();
        UpdateStarVisuals();
    }

    private void UpdateLabel()
    {
        string text = levelNumber.ToString();
        if (tmpLabel != null) tmpLabel.text = text;
    }

    private void HandleClick()
    {
        if (status == LevelStatus.Locked)
        {
            return;
        }

        var manager = GameManager.Instance;
        if (manager != null)
        {
            manager.SetCurrentLevelNumber(levelNumber);
        }

        var config = boundConfig != null ? boundConfig : LoadLevelConfig(levelNumber);
        if (config == null)
        {
            Debug.LogWarning($"LevelButtonController could not load config for Level {levelNumber} at Resources/{levelsResourceRoot}/Level_{levelNumber:D2}/GameModeConfig", this);
            return;
        }

        if (manager != null)
        {
            manager.SetActiveLevelConfig(config);
        }

        Time.timeScale = 1f;
        if (gameSceneBuildIndex >= 0)
        {
            SceneManager.LoadScene(gameSceneBuildIndex);
        }
    }

    private GameModeConfig LoadLevelConfig(int level)
    {
        string path = $"{levelsResourceRoot}/Level_{level:D2}/GameModeConfig";
        return Resources.Load<GameModeConfig>(path);
    }

    public void SetStatus(LevelStatus newStatus)
    {
        status = newStatus;
        ApplyStatus();
        UpdateStarVisuals();
    }

    private void ApplyStatus()
    {
        if (button != null)
        {
            button.interactable = status != LevelStatus.Locked;
        }

        // Update icon based on status, if references are assigned
        // Icon overlay: show per-status if corresponding sprite is assigned
        if (statusIcon != null)
        {
            Sprite sprite = null;
            switch (status)
            {
                case LevelStatus.Completed:
                    sprite = completedIcon;
                    break;
                case LevelStatus.Current:
                    sprite = currentIcon;
                    break;
                default:
                    sprite = lockedIcon;
                    break;
            }

            if (sprite != null)
            {
                statusIcon.sprite = sprite;
                statusIcon.gameObject.SetActive(true);
            }
            else
            {
                statusIcon.gameObject.SetActive(false);
            }
        }

        // Stars visibility: show only when Completed
        if (starSlot != null)
        {
            bool showStars = status == LevelStatus.Completed;
            starSlot.SetActive(showStars);
        }

        // If star views not assigned in inspector, try to auto-resolve from hierarchy
        EnsureStarViews();
    }

    public void SetStars(int stars)
    {
        starsEarned = Mathf.Clamp(stars, 0, 3);
        UpdateStarVisuals();
    }

    private void UpdateStarVisuals()
    {
        if (starSlot == null)
        {
            return;
        }

        bool active = status == LevelStatus.Completed;
        starSlot.SetActive(active);
        if (!active)
        {
            return;
        }

        EnsureStarViews();
        if (starViews == null)
        {
            return;
        }

        for (int i = 0; i < starViews.Length; i++)
        {
            bool filled = i < starsEarned;
            var view = starViews[i];
            if (view.root != null)
            {
                view.root.SetActive(true);
            }
            if (view.visual != null)
            {
                view.visual.SetActive(filled);
            }
            if (view.slot != null)
            {
                view.slot.SetActive(!filled);
            }
        }
    }

    private void EnsureStarViews()
    {
        if (starSlot == null)
        {
            return;
        }

        if (starViews != null && starViews.Length > 0)
        {
            return;
        }

        var t = starSlot.transform;
        int count = t.childCount;
        if (count <= 0)
        {
            return;
        }

        var list = new System.Collections.Generic.List<StarView>(count);
        for (int i = 0; i < count; i++)
        {
            var child = t.GetChild(i);
            GameObject visual = null;
            GameObject slot = null;

            for (int j = 0; j < child.childCount; j++)
            {
                var sub = child.GetChild(j);
                string name = sub.name.ToLowerInvariant();
                if (name.Contains("visual")) visual = sub.gameObject;
                else if (name.Contains("slot")) slot = sub.gameObject;
            }

            list.Add(new StarView
            {
                root = child.gameObject,
                visual = visual,
                slot = slot
            });
        }

        starViews = list.ToArray();
    }
}
