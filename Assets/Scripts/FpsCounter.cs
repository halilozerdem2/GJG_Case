using UnityEngine;
using TMPro;

public class FpsCounter : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField, Range(0.05f, 1f)] private float updateInterval = 0.2f;

    private float accumulatedTime;
    private int frameCount;

    private void Awake()
    {
        if (label == null)
        {
            label = GetComponentInChildren<TMP_Text>();
        }
    }

    private void OnEnable()
    {
        accumulatedTime = 0f;
        frameCount = 0;
        UpdateLabel(0);
    }

    private void Update()
    {
        frameCount++;
        accumulatedTime += Time.unscaledDeltaTime;
        if (accumulatedTime < Mathf.Max(0.01f, updateInterval))
        {
            return;
        }

        float fps = frameCount / accumulatedTime;
        UpdateLabel(Mathf.RoundToInt(fps));
        accumulatedTime = 0f;
        frameCount = 0;
    }

    private void UpdateLabel(int fps)
    {
        if (label != null)
        {
            label.text = fps > 0 ? $"{fps} FPS" : string.Empty;
        }
    }
}
