using UnityEngine;

public class SettingsService : MonoBehaviour
{
    private static SettingsService instance;

    public static SettingsService Instance
    {
        get
        {
            if (!Application.isPlaying)
            {
                return instance;
            }

            if (instance == null)
            {
                EnsureInstanceExists();
            }

            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstanceExists();
    }

    private static void EnsureInstanceExists()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (instance != null)
        {
            return;
        }

        var existing = FindObjectOfType<SettingsService>();
        if (existing != null)
        {
            instance = existing;
            return;
        }

        var go = new GameObject("SettingsService");
        instance = go.AddComponent<SettingsService>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        ApplySavedStates();
    }

    public bool MusicEnabled => PlayerSettings.MusicEnabled;
    public bool SfxEnabled => PlayerSettings.SfxEnabled;
    public bool VibrationEnabled => PlayerSettings.VibrationEnabled;

    public void SetMusicEnabled(bool enabled)
    {
        if (PlayerSettings.MusicEnabled != enabled)
        {
            PlayerSettings.MusicEnabled = enabled;
        }

        ApplyMusicState(enabled);
    }

    public void SetSfxEnabled(bool enabled)
    {
        if (PlayerSettings.SfxEnabled != enabled)
        {
            PlayerSettings.SfxEnabled = enabled;
        }

        ApplySfxState(enabled);
    }

    public void SetVibrationEnabled(bool enabled)
    {
        if (PlayerSettings.VibrationEnabled != enabled)
        {
            PlayerSettings.VibrationEnabled = enabled;
        }

        VibrationManager.SetEnabled(enabled);
    }

    private void ApplySavedStates()
    {
        ApplyMusicState(PlayerSettings.MusicEnabled);
        ApplySfxState(PlayerSettings.SfxEnabled);
        VibrationManager.Initialize(PlayerSettings.VibrationEnabled);
    }

    private void ApplyMusicState(bool enabled)
    {
        var audio = AudioManager.Instance;
        if (audio != null)
        {
            audio.SetMusicEnabled(enabled);
        }
    }

    private void ApplySfxState(bool enabled)
    {
        var audio = AudioManager.Instance;
        if (audio != null)
        {
            audio.SetSfxEnabled(enabled);
        }
    }
}
