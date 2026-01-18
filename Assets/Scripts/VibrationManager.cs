using UnityEngine;

public static class VibrationManager
{
    public static bool IsEnabled => PlayerSettings.VibrationEnabled;

    public static void SetEnabled(bool enabled)
    {
        PlayerSettings.VibrationEnabled = enabled;
    }

    public static void Pulse()
    {
        if (!IsEnabled)
        {
            return;
        }

#if UNITY_ANDROID || UNITY_IOS
        Handheld.Vibrate();
#else
        // Optional: log in editor/platforms without vibration
#endif
    }
}
