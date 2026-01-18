using UnityEngine;

public class CaseCanvasManager : MonoBehaviour
{
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private SettingsPanelController settingsPanelController;
    [SerializeField] private ToggleSwitchAnimator musicAnimator;
    [SerializeField] private ToggleSwitchAnimator sfxAnimator;
    [SerializeField] private ToggleSwitchAnimator vibrationAnimator;

    private void Start()
    {
        SyncToggleStates();
    }

    public void OpenSettingsPanel()
    {
        if (settingsPanel == null)
        {
            return;
        }

        SyncToggleStates();
        settingsPanel.SetActive(true);
        Time.timeScale = 0f;
        GameManager.Instance?.ForceShuffleInProgress();
    }

    public void CloseSettingsPanel()
    {
        settingsPanelController?.ClosePanel();
    }

    private void SyncToggleStates()
    {
        var audio = AudioManager.Instance;
        bool musicOn = audio != null ? audio.IsMusicEnabled : PlayerSettings.MusicEnabled;
        bool sfxOn = audio != null ? audio.IsSfxEnabled : PlayerSettings.SfxEnabled;

        musicAnimator?.SetStateImmediate(musicOn);
        sfxAnimator?.SetStateImmediate(sfxOn);
        vibrationAnimator?.SetStateImmediate(VibrationManager.IsEnabled);
    }
}
