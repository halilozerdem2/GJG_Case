using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
    [Header("Powerup Cooldowns")]
    [SerializeField] private List<PowerupButtonBinding> powerupButtons = new List<PowerupButtonBinding>();

    private readonly Dictionary<PowerupType, float> powerupCooldownDurations = new Dictionary<PowerupType, float>();
    private readonly Dictionary<PowerupType, float> powerupCooldowns = new Dictionary<PowerupType, float>();
    private readonly List<PowerupType> cooldownUpdateKeys = new List<PowerupType>();

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
        InitializeCooldowns();
    }

    private void Update()
    {
        TickPowerupCooldowns(Time.deltaTime);
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

        if (!IsPowerupReady(PowerupType.Shuffle))
        {
            Audio?.PlayInvalidSelection();
            return;
        }

        blockTypeSelectionPanel?.Hide();
        GameManager.Instance.ForceShuffleInProgress();
        StartPowerupCooldown(PowerupType.Shuffle);
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

        if (!IsPowerupReady(PowerupType.PowerShuffle))
        {
            Audio?.PlayInvalidSelection();
            return;
        }

        blockTypeSelectionPanel?.Hide();
        GameManager.Instance.ForceShuffleInProgress();
        StartPowerupCooldown(PowerupType.PowerShuffle);
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

        if (!IsPowerupReady(PowerupType.DestroyAll))
        {
            Audio?.PlayInvalidSelection();
            return;
        }

        blockTypeSelectionPanel?.Hide();
        bool destroyed = blockManager.DestroyAllBlocks();
        if (destroyed)
        {
            StartPowerupCooldown(PowerupType.DestroyAll);
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
            if (!IsPowerupReady(PowerupType.DestroySpecific))
            {
                Audio?.PlayInvalidSelection();
                return;
            }

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
            StartPowerupCooldown(PowerupType.DestroySpecific);
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
        SetGameMode(GameManager.GameMode.Case);
        LoadSceneByIndex(1);
    }

    public void GoToPlayScene()
    {
        SetGameMode(GameManager.GameMode.Game);
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

    private void SetGameMode(GameManager.GameMode mode)
    {
        var manager = GameManager.Instance;
        if (manager == null)
        {
            return;
        }

        manager.SetGameMode(mode);
    }

    private void InitializeCooldowns()
    {
        powerupCooldownDurations.Clear();
        powerupCooldowns.Clear();

        var manager = GameManager.Instance;
        var cooldownEntries = manager != null ? manager.ActivePowerupCooldowns : null;
        if (cooldownEntries != null)
        {
            for (int i = 0; i < cooldownEntries.Count; i++)
            {
                var entry = cooldownEntries[i];
                powerupCooldownDurations[entry.Powerup] = Mathf.Max(0f, entry.CooldownSeconds);
            }
        }

        for (int i = 0; i < powerupButtons.Count; i++)
        {
            PowerupType type = powerupButtons[i].powerup;
            EnsurePowerupEntry(type);
            powerupCooldowns[type] = 0f;
            UpdatePowerupVisual(type);
        }
    }

    private void TickPowerupCooldowns(float deltaTime)
    {
        if (powerupCooldowns.Count == 0 || deltaTime <= 0f)
        {
            return;
        }

        cooldownUpdateKeys.Clear();
        cooldownUpdateKeys.AddRange(powerupCooldowns.Keys);

        for (int i = 0; i < cooldownUpdateKeys.Count; i++)
        {
            PowerupType type = cooldownUpdateKeys[i];
            float current = powerupCooldowns[type];
            if (current <= 0f)
            {
                continue;
            }

            current = Mathf.Max(0f, current - deltaTime);
            powerupCooldowns[type] = current;
            UpdatePowerupVisual(type);
        }
    }

    private bool IsPowerupReady(PowerupType type)
    {
        EnsurePowerupEntry(type);
        return !powerupCooldowns.TryGetValue(type, out float remaining) || remaining <= 0.01f;
    }

    private void StartPowerupCooldown(PowerupType type)
    {
        EnsurePowerupEntry(type);
        float duration = GetPowerupCooldownDuration(type);
        if (duration <= 0f)
        {
            powerupCooldowns[type] = 0f;
            UpdatePowerupVisual(type);
            return;
        }

        powerupCooldowns[type] = duration;
        UpdatePowerupVisual(type);
    }

    private void EnsurePowerupEntry(PowerupType type)
    {
        if (!powerupCooldownDurations.ContainsKey(type))
        {
            powerupCooldownDurations[type] = 0f;
        }

        if (!powerupCooldowns.ContainsKey(type))
        {
            powerupCooldowns[type] = 0f;
        }
    }

    private float GetPowerupCooldownDuration(PowerupType type)
    {
        return powerupCooldownDurations.TryGetValue(type, out float duration) ? Mathf.Max(0f, duration) : 0f;
    }

    private void UpdatePowerupVisual(PowerupType type)
    {
        float remaining = powerupCooldowns.TryGetValue(type, out float value) ? value : 0f;
        bool ready = remaining <= 0.01f;

        for (int i = 0; i < powerupButtons.Count; i++)
        {
            if (powerupButtons[i].powerup != type)
            {
                continue;
            }

            var binding = powerupButtons[i];
            if (binding.button != null)
            {
                binding.button.interactable = ready;
            }

            if (binding.cooldownOverlay != null)
            {
                binding.cooldownOverlay.alpha = ready ? 0f : 1f;
                binding.cooldownOverlay.interactable = !ready;
                binding.cooldownOverlay.blocksRaycasts = !ready;
            }

            if (binding.cooldownLabel != null)
            {
                binding.cooldownLabel.text = ready ? string.Empty : Mathf.CeilToInt(remaining).ToString();
            }
        }
    }

    [System.Serializable]
    private struct PowerupButtonBinding
    {
        public PowerupType powerup;
        public Selectable button;
        public CanvasGroup cooldownOverlay;
        public TMP_Text cooldownLabel;
    }
}
