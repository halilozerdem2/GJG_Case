using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LevelsPanel : MonoBehaviour
{
    [Header("Resources Settings")]
    [SerializeField] private string levelsResourceRoot = "Levels";
    // Root under Resources that contains Level_XX folders

    [Header("UI Settings")]
    [SerializeField] private Transform levelsContainer;
    [SerializeField] private Button levelButtonTemplate;

    [Header("Scene Settings")]
    [SerializeField] private int gameSceneBuildIndex = 1;

    private readonly List<GameObject> spawnedButtons = new List<GameObject>();

    private void Awake()
    {
        // Ensure template exists and stays disabled
        if (levelButtonTemplate != null)
        {
            var go = levelButtonTemplate.gameObject;
            if (go.activeSelf)
            {
                go.SetActive(false);
            }
        }
    }

    private void OnEnable()
    {
        TryBuildLevelButtons();
    }

    public IReadOnlyList<(int level, GameModeConfig config)> LoadAllLevels()
    {
        var list = new List<(int level, GameModeConfig config)>();
        var configs = Resources.LoadAll<GameModeConfig>(levelsResourceRoot);
        if (configs == null || configs.Length == 0)
        {
            return list;
        }

        foreach (var cfg in configs)
        {
            if (cfg == null)
            {
                continue;
            }

            int level = ExtractLevelNumber(cfg);
            list.Add((level, cfg));
        }

        list.Sort((a, b) => a.level.CompareTo(b.level));
        return list;
    }

    private void TryBuildLevelButtons()
    {
        if (levelsContainer == null || levelButtonTemplate == null)
        {
            Debug.LogWarning("LevelsPanel requires references to Levels Container and Level Button Template.", this);
            return;
        }

        ClearSpawnedButtons();

        var levels = LoadAllLevels();
        foreach (var entry in levels)
        {
            int level = entry.level;
            var button = Instantiate(levelButtonTemplate, levelsContainer);
            button.gameObject.SetActive(true);

            var controller = button.GetComponent<LevelButtonController>();
            if (controller == null)
            {
                controller = button.gameObject.AddComponent<LevelButtonController>();
            }
            controller.Initialize(level, levelsResourceRoot, gameSceneBuildIndex, entry.config);

            var status = ComputeStatus(level);
            controller.SetStatus(status);
            int stars = LevelProgressService.Instance.GetStars(level);
            controller.SetStars(stars);

            spawnedButtons.Add(button.gameObject);
        }
    }

    private void ClearSpawnedButtons()
    {
        for (int i = 0; i < spawnedButtons.Count; i++)
        {
            var go = spawnedButtons[i];
            if (go != null)
            {
                Destroy(go);
            }
        }
        spawnedButtons.Clear();
    }

    private int ExtractLevelNumber(GameModeConfig cfg)
    {
        // Try parse from configId like "Level_01" else from asset name
        string id = cfg != null ? cfg.ConfigId : string.Empty;
        if (!string.IsNullOrEmpty(id))
        {
            int parsed = ParseTrailingNumber(id);
            if (parsed > 0) return parsed;
        }

        string name = cfg != null ? cfg.name : string.Empty;
        int parsedFromName = ParseTrailingNumber(name);
        return parsedFromName > 0 ? parsedFromName : 0;
    }

    private int ParseTrailingNumber(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int number = 0;
        int multiplier = 1;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            char c = text[i];
            if (char.IsDigit(c))
            {
                number += (c - '0') * multiplier;
                multiplier *= 10;
            }
            else if (multiplier > 1)
            {
                break;
            }
        }
        return number;
    }

    private LevelButtonController.LevelStatus ComputeStatus(int level)
    {
        var progress = LevelProgressService.Instance;
        if (progress.IsCompleted(level))
        {
            return LevelButtonController.LevelStatus.Completed;
        }

        int unlocked = progress.GetHighestUnlockedLevel();
        if (level == unlocked)
        {
            return LevelButtonController.LevelStatus.Current;
        }

        return LevelButtonController.LevelStatus.Locked;
    }
}
