using UnityEngine;

public class SettingsPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [Header("Toggle Animators")]
    [SerializeField] private ToggleSwitchAnimator musicAnimator;
    [SerializeField] private ToggleSwitchAnimator sfxAnimator;
    [SerializeField] private ToggleSwitchAnimator vibrationAnimator;

    private void Awake()
    {
        if (panelRoot == null)
        {
            panelRoot = gameObject;
        }

    }

    public void ClosePanel()
    {
        var target = panelRoot != null ? panelRoot : gameObject;
        target.SetActive(false);
        Time.timeScale = 1f;
        GameManager.Instance?.ForceWaitingAfterShuffle();
    }

    public void ToggleMusic()
    {
        musicAnimator?.Toggle();
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetMusicEnabled(!AudioManager.Instance.IsMusicEnabled);
        }
        else
        {
            PlayerSettings.MusicEnabled = !PlayerSettings.MusicEnabled;
        }
    }

    public void ToggleSfx()
    {
        sfxAnimator?.Toggle();
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetSfxEnabled(!AudioManager.Instance.IsSfxEnabled);
        }
        else
        {
            PlayerSettings.SfxEnabled = !PlayerSettings.SfxEnabled;
        }
    }

    public void ToggleVibration()
    {
        vibrationAnimator?.Toggle();
        VibrationManager.SetEnabled(!VibrationManager.IsEnabled);
    }

    private void SyncToggleStates()
    {
        var audio = AudioManager.Instance;
        if (audio != null)
        {
            musicAnimator?.SetState(audio.IsMusicEnabled);
            sfxAnimator?.SetState(audio.IsSfxEnabled);
        }

        vibrationAnimator?.SetState(VibrationManager.IsEnabled);
    }
}
