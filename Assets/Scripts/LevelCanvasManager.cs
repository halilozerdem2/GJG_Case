using UnityEngine;
using UnityEngine.SceneManagement;

public class LevelCanvasManager : MonoBehaviour
{
    [SerializeField] private BlockManager blockManager;
    [SerializeField] private BlockTypeSelectionPanel blockTypeSelectionPanel;
    [SerializeField] private AudioManager audioManager;
    [SerializeField] private SettingsService settingsService;
    [Header("Settings Panel")]
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private SettingsPanelController settingsPanelController;
    [SerializeField] private ToggleSwitchAnimator musicAnimator;
    [SerializeField] private ToggleSwitchAnimator sfxAnimator;
    [SerializeField] private ToggleSwitchAnimator vibrationAnimator;

    private AudioManager Audio => audioManager != null ? audioManager : AudioManager.Instance;
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

    private void Start()
    {
        SyncToggleStates();
    }

    public void OnShuffleButton()
    {
        if (blockManager == null)
        {
            Debug.LogWarning("LevelCanvasManager requires a BlockManager reference for shuffle.");
            return;
        }

        if (GameManager.Instance == null || !GameManager.Instance.IsWaitingForInput)
        {
            Audio?.PlayInvalidSelection();
            return;
        }

        blockTypeSelectionPanel?.Hide();
        GameManager.Instance.ForceShuffleInProgress();
        Audio?.PlayShuffle();
        blockManager.ResolveDeadlock(HandleShuffleCompleted);
    }

    public void OnPowerShuffleButton()
    {
        if (blockManager == null)
        {
            Debug.LogWarning("LevelCanvasManager requires a BlockManager reference for power shuffle.");
            return;
        }

        if (GameManager.Instance == null || !GameManager.Instance.IsWaitingForInput)
        {
            Audio?.PlayInvalidSelection();
            return;
        }

        blockTypeSelectionPanel?.Hide();
        GameManager.Instance.ForceShuffleInProgress();
        blockManager.PowerShuffle(HandlePowerShuffleCompleted);
        Audio?.PlayPowerShuffle();
    }

    public void OnDestroyAllButton()
    {
        if (blockManager == null)
        {
            Debug.LogWarning("LevelCanvasManager requires a BlockManager reference for destroy all.");
            return;
        }

        blockTypeSelectionPanel?.Hide();
        bool destroyed = blockManager.DestroyAllBlocks();
        if (destroyed)
        {
            Audio?.PlayDestroyAll();
            GameManager.Instance?.ForceSpawnAfterBoardClear();
        }
        else
        {
            Audio?.PlayInvalidSelection();
        }
    }

    public void OnDestroySpecificButton()
    {
        if (blockTypeSelectionPanel == null)
        {
            Debug.LogWarning("Block type selection panel not assigned.");
            return;
        }

        if (blockTypeSelectionPanel.IsVisible)
        {
            blockTypeSelectionPanel.Hide();
        }
        else
        {
            blockTypeSelectionPanel.Show(HandleDestroySpecificSelection);
        }
    }

    private void HandleShuffleCompleted(bool success)
    {
        if (success)
        {
            GameManager.Instance?.ForceWaitingAfterShuffle();
        }
        else
        {
            Audio?.PlayInvalidSelection();
            GameManager.Instance?.ForceWaitingAfterShuffle();
        }
    }

    private void HandlePowerShuffleCompleted()
    {
        GameManager.Instance?.ForceWaitingAfterShuffle();
    }

    private void HandleDestroySpecificSelection(int blockType)
    {
        if (blockManager == null)
        {
            Debug.LogWarning("LevelCanvasManager requires a BlockManager reference for destroy specific.");
            return;
        }

        bool destroyed = blockManager.DestroyBlocksOfType(blockType);
        if (destroyed)
        {
            Audio?.PlayDestroySpecific();
            GameManager.Instance?.ForceResolveAfterPowerup();
        }
        else
        {
            Audio?.PlayInvalidSelection();
        }
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

    public void GoToHomeScene()
    {
        LoadSceneByIndex(0);
    }

    public void GoToCaseScene()
    {
        LoadSceneByIndex(1);
    }

    public void GoToPlayScene()
    {
        LoadSceneByIndex(2);
    }

    private void SyncToggleStates()
    {
        var service = Service;
        if (service != null)
        {
            musicAnimator?.SetStateImmediate(service.MusicEnabled);
            sfxAnimator?.SetStateImmediate(service.SfxEnabled);
            vibrationAnimator?.SetStateImmediate(service.VibrationEnabled);
            return;
        }

        var audio = Audio;
        bool musicFallback = audio != null ? audio.IsMusicEnabled : PlayerSettings.MusicEnabled;
        bool sfxFallback = audio != null ? audio.IsSfxEnabled : PlayerSettings.SfxEnabled;

        musicAnimator?.SetStateImmediate(musicFallback);
        sfxAnimator?.SetStateImmediate(sfxFallback);
        vibrationAnimator?.SetStateImmediate(VibrationManager.IsEnabled);
    }

    private void LoadSceneByIndex(int index)
    {
        if (index < 0)
        {
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(index);
    }
}
