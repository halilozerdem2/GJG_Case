using UnityEngine;

public class ButtonVibration : MonoBehaviour
{
    public void PlayClickVibration()
    {
        VibrationManager.Pulse();
    }
}
