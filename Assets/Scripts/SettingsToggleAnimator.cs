using UnityEngine;

public class SettingsToggleAnimator : MonoBehaviour
{
    [SerializeField] private ToggleSwitchAnimator musicAnimator;
    [SerializeField] private ToggleSwitchAnimator sfxAnimator;
    [SerializeField] private ToggleSwitchAnimator vibrationAnimator;

    public void ToggleMusicVisual()
    {
        musicAnimator?.Toggle();
    }

    public void ToggleSfxVisual()
    {
        sfxAnimator?.Toggle();
    }

    public void ToggleVibrationVisual()
    {
        vibrationAnimator?.Toggle();
    }

    public void SetMusicState(bool enabled)
    {
        musicAnimator?.SetState(enabled);
    }

    public void SetSfxState(bool enabled)
    {
        sfxAnimator?.SetState(enabled);
    }

    public void SetVibrationState(bool enabled)
    {
        vibrationAnimator?.SetState(enabled);
    }
}
