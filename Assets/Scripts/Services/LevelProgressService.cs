using System;
using System.Collections.Generic;
using UnityEngine;

public class LevelProgressService
{
    private const string PlayerPrefsKey = "user_progress_v1";

    [Serializable]
    private class LevelStars
    {
        public int level;
        public int stars;
    }

    [Serializable]
    private class UserProgressData
    {
        public int highestUnlockedLevel = 1;
        public int version = 1;
        public List<LevelStars> stars = new List<LevelStars>();
    }

    private static LevelProgressService _instance;
    public static LevelProgressService Instance => _instance ?? (_instance = new LevelProgressService());

    private readonly Dictionary<int, int> starsByLevel = new Dictionary<int, int>();
    private int highestUnlockedLevel = 1;
    private bool initialized;

    private LevelProgressService()
    {
        Load();
    }

    public void ResetAll()
    {
        highestUnlockedLevel = 1;
        starsByLevel.Clear();
        Save();
    }

    public int GetHighestUnlockedLevel()
    {
        EnsureInitialized();
        return Mathf.Max(1, highestUnlockedLevel);
    }

    public int GetStars(int level)
    {
        EnsureInitialized();
        return starsByLevel.TryGetValue(Mathf.Max(1, level), out var value) ? Mathf.Clamp(value, 0, 3) : 0;
    }

    public bool IsCompleted(int level)
    {
        return GetStars(level) > 0;
    }

    public void ReportLevelResult(int level, int stars)
    {
        EnsureInitialized();
        int lvl = Mathf.Max(1, level);
        int clampedStars = Mathf.Clamp(stars, 0, 3);

        if (starsByLevel.TryGetValue(lvl, out int existing))
        {
            if (clampedStars > existing)
            {
                starsByLevel[lvl] = clampedStars;
            }
        }
        else
        {
            starsByLevel[lvl] = clampedStars;
        }

        if (clampedStars > 0 && lvl >= highestUnlockedLevel)
        {
            highestUnlockedLevel = lvl + 1;
        }

        Save();
    }

    private void EnsureInitialized()
    {
        if (!initialized)
        {
            Load();
        }
    }

    private void Load()
    {
        initialized = true;
        starsByLevel.Clear();
        highestUnlockedLevel = 1;

        string json = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        try
        {
            var data = JsonUtility.FromJson<UserProgressData>(json);
            if (data == null)
            {
                return;
            }

            highestUnlockedLevel = Mathf.Max(1, data.highestUnlockedLevel);
            if (data.stars != null)
            {
                for (int i = 0; i < data.stars.Count; i++)
                {
                    var entry = data.stars[i];
                    int level = Mathf.Max(1, entry.level);
                    int s = Mathf.Clamp(entry.stars, 0, 3);
                    if (s > 0)
                    {
                        starsByLevel[level] = s;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Failed to load user progress: {e.Message}");
        }
    }

    private void Save()
    {
        var data = new UserProgressData
        {
            highestUnlockedLevel = Mathf.Max(1, highestUnlockedLevel),
            version = 1,
            stars = new List<LevelStars>()
        };

        foreach (var kvp in starsByLevel)
        {
            data.stars.Add(new LevelStars { level = kvp.Key, stars = Mathf.Clamp(kvp.Value, 0, 3) });
        }

        string json = JsonUtility.ToJson(data);
        PlayerPrefs.SetString(PlayerPrefsKey, json);
        PlayerPrefs.Save();
    }
}

