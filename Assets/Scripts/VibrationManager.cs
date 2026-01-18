using UnityEngine;

public static class VibrationManager
{
    private static bool isInitialized;
    private static bool isEnabled;

    public static bool IsEnabled
    {
        get
        {
            if (!isInitialized)
            {
                isEnabled = PlayerSettings.VibrationEnabled;
                isInitialized = true;
            }

            return isEnabled;
        }
    }

    public static void Initialize(bool enabled)
    {
        isEnabled = enabled;
        isInitialized = true;
    }

    public static void SetEnabled(bool enabled)
    {
        isEnabled = enabled;
        isInitialized = true;
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
