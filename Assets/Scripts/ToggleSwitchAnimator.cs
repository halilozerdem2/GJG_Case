using UnityEngine;

public class ToggleSwitchAnimator : MonoBehaviour
{
    [SerializeField] private RectTransform handle;
    [SerializeField] private Vector2 onPosition = new Vector2(16f, 0f);
    [SerializeField] private Vector2 offPosition = new Vector2(-16f, 0f);
    [SerializeField] private float moveSpeed = 10f;

    private bool isOn = true;
    private Vector2 targetPosition;

    private void Awake()
    {
        if (handle == null)
        {
            handle = GetComponent<RectTransform>();
        }

        targetPosition = isOn ? onPosition : offPosition;
        if (handle != null)
        {
            handle.anchoredPosition = targetPosition;
        }
    }

    private void Update()
    {
        if (handle == null)
        {
            return;
        }

        handle.anchoredPosition = Vector2.Lerp(
            handle.anchoredPosition,
            targetPosition,
            Time.unscaledDeltaTime * moveSpeed);
    }

    public void Toggle()
    {
        SetStateInternal(!isOn, false);
    }

    public void SetState(bool value)
    {
        SetStateInternal(value, false);
    }

    public void SetStateImmediate(bool value)
    {
        SetStateInternal(value, true);
    }

    private void SetStateInternal(bool value, bool instant)
    {
        isOn = value;
        targetPosition = isOn ? onPosition : offPosition;
        if (instant && handle != null)
        {
            handle.anchoredPosition = targetPosition;
        }
    }

    public bool IsOn => isOn;
}
