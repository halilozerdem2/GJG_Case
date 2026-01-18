using UnityEngine;

public static class PlayerSettings
{
    private const string MusicKey = "MusicEnabled";
    private const string SfxKey = "SfxEnabled";
    private const string VibrationKey = "VibrationEnabled";

    public static bool MusicEnabled
    {
        get => PlayerPrefs.GetInt(MusicKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(MusicKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static bool SfxEnabled
    {
        get => PlayerPrefs.GetInt(SfxKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(SfxKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    public static bool VibrationEnabled
    {
        get => PlayerPrefs.GetInt(VibrationKey, 1) == 1;
        set
        {
            PlayerPrefs.SetInt(VibrationKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
