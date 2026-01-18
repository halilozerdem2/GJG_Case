using UnityEngine;

public class SettingsPanelController : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private SettingsService settingsService;
    [Header("Toggle Animators")]
    [SerializeField] private ToggleSwitchAnimator musicAnimator;
    [SerializeField] private ToggleSwitchAnimator sfxAnimator;
    [SerializeField] private ToggleSwitchAnimator vibrationAnimator;

    private SettingsService Service
    {
        get
        {
            if (settingsService == null)
            {
                settingsService = SettingsService.Instance;
            }

            return settingsService;
        }
    }

    private void Awake()
    {
        if (panelRoot == null)
        {
            panelRoot = gameObject;
        }

    }

    private void OnEnable()
    {
        SyncToggleStates();
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
        var service = Service;
        if (service == null)
        {
            return;
        }

        bool newValue = !service.MusicEnabled;
        service.SetMusicEnabled(newValue);
        musicAnimator?.SetState(newValue);
    }

    public void ToggleSfx()
    {
        var service = Service;
        if (service == null)
        {
            return;
        }

        bool newValue = !service.SfxEnabled;
        service.SetSfxEnabled(newValue);
        sfxAnimator?.SetState(newValue);
    }

    public void ToggleVibration()
    {
        var service = Service;
        if (service == null)
        {
            return;
        }

        bool newValue = !service.VibrationEnabled;
        service.SetVibrationEnabled(newValue);
        vibrationAnimator?.SetState(newValue);
    }

    private void SyncToggleStates()
    {
        var service = Service;
        if (service == null)
        {
            return;
        }

        musicAnimator?.SetState(service.MusicEnabled);
        sfxAnimator?.SetState(service.SfxEnabled);
        vibrationAnimator?.SetState(service.VibrationEnabled);
    }
}
