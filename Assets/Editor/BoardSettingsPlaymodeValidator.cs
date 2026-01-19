#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class BoardSettingsPlaymodeValidator
{
    static BoardSettingsPlaymodeValidator()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode)
        {
            return;
        }

        if (ValidateBoardSettings())
        {
            return;
        }

        EditorApplication.isPlaying = false;
        EditorUtility.DisplayDialog(
            "Board Settings Validation Failed",
            "Play Mode iptal edildi. Ayrıntılar için Console penceresini kontrol edin.",
            "Tamam");
    }

    private static bool ValidateBoardSettings()
    {
        string[] guids = AssetDatabase.FindAssets("t:BoardSettings");
        if (guids == null || guids.Length == 0)
        {
            Debug.LogWarning("BoardSettingsPlaymodeValidator herhangi bir BoardSettings asset'i bulamadı.");
            return true;
        }

        bool allValid = true;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var settings = AssetDatabase.LoadAssetAtPath<BoardSettings>(path);
            if (settings == null)
            {
                continue;
            }

            if (!settings.IsValid(out string message))
            {
                Debug.LogError($"BoardSettings validation failed for {path}: {message}", settings);
                allValid = false;
            }
        }

        return allValid;
    }
}
#endif
