using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class BoardRegeneratorUI : MonoBehaviour
{
    [SerializeField] private BoardSettings boardSettings;
    [SerializeField] private Slider rowsSlider;
    [SerializeField] private Slider columnsSlider;
    [SerializeField] private TMP_Text rowsValueLabel;
    [SerializeField] private TMP_Text columnsValueLabel;
    [Header("Threshold Controls")]
    [SerializeField] private TMP_InputField thresholdAInput;
    [SerializeField] private TMP_InputField thresholdBInput;
    [SerializeField] private TMP_InputField thresholdCInput;
    [SerializeField] private TMP_Text thresholdALabel;
    [SerializeField] private TMP_Text thresholdBLabel;
    [SerializeField] private TMP_Text thresholdCLabel;
    [SerializeField] private TMP_Text ruleLabel;
    [SerializeField, Range(1f, 2f)] private float rulePulseScale = 1.1f;
    [SerializeField, Range(0.05f, 0.5f)] private float rulePulseDuration = 0.2f;
    [SerializeField] private Selectable applySettingsButton;
    [SerializeField] private Color successSelectedColor = Color.green;
    [SerializeField] private Color errorSelectedColor = Color.red;

    private int pendingRows;
    private int pendingColumns;
    private string thresholdAText;
    private string thresholdBText;
    private string thresholdCText;
    private Vector3 ruleBaseScale = Vector3.one;

    private void Awake()
    {
        ConfigureSlider(rowsSlider, HandleRowsSliderChanged, true);
        ConfigureSlider(columnsSlider, HandleColumnsSliderChanged, false);
        ConfigureInputField(thresholdAInput, HandleThresholdAChanged);
        ConfigureInputField(thresholdBInput, HandleThresholdBChanged);
        ConfigureInputField(thresholdCInput, HandleThresholdCChanged);

        if (ruleLabel != null)
        {
            ruleBaseScale = ruleLabel.transform.localScale;
        }
    }

    private void OnEnable()
    {
        SyncFromSettings();
    }

    private void OnDestroy()
    {
        if (rowsSlider != null)
        {
            rowsSlider.onValueChanged.RemoveListener(HandleRowsSliderChanged);
        }
        if (columnsSlider != null)
        {
            columnsSlider.onValueChanged.RemoveListener(HandleColumnsSliderChanged);
        }
        RemoveInputListener(thresholdAInput, HandleThresholdAChanged);
        RemoveInputListener(thresholdBInput, HandleThresholdBChanged);
        RemoveInputListener(thresholdCInput, HandleThresholdCChanged);
    }

    public void OnRegenerateButtonClicked()
    {
        if (boardSettings == null)
        {
            return;
        }

        bool thresholdsValid = TryValidateThresholds(out int validatedA, out int validatedB, out int validatedC);
        if (!thresholdsValid)
        {
            thresholdAText = boardSettings.ThresholdA.ToString();
            thresholdBText = boardSettings.ThresholdB.ToString();
            thresholdCText = boardSettings.ThresholdC.ToString();
            RefreshThresholdUI();
            TriggerRuleWarning();
            SetApplyButtonSelectedColor(errorSelectedColor);
            return;
        }

        boardSettings.ApplyDimensions(pendingRows, pendingColumns);
        boardSettings.ApplyThresholds(validatedA, validatedB, validatedC);
        SyncFromSettings();
        SetApplyButtonSelectedColor(successSelectedColor);
    }

    private void ConfigureSlider(Slider slider, UnityAction<float> callback, bool isRowSlider)
    {
        if (slider == null || boardSettings == null)
        {
            return;
        }

        slider.wholeNumbers = true;
        slider.onValueChanged.RemoveListener(callback);
        slider.onValueChanged.AddListener(callback);
        slider.minValue = isRowSlider ? boardSettings.MinRows : boardSettings.MinColumns;
        slider.maxValue = isRowSlider ? boardSettings.MaxRows : boardSettings.MaxColumns;
    }

    private void SyncFromSettings()
    {
        if (boardSettings == null)
        {
            return;
        }

        pendingRows = boardSettings.Rows;
        pendingColumns = boardSettings.Columns;
        thresholdAText = boardSettings.ThresholdA.ToString();
        thresholdBText = boardSettings.ThresholdB.ToString();
        thresholdCText = boardSettings.ThresholdC.ToString();

        if (rowsSlider != null)
        {
            rowsSlider.SetValueWithoutNotify(pendingRows);
        }
        if (columnsSlider != null)
        {
            columnsSlider.SetValueWithoutNotify(pendingColumns);
        }

        RefreshThresholdUI();
        UpdateLabel(rowsValueLabel, pendingRows);
        UpdateLabel(columnsValueLabel, pendingColumns);
    }

    private void HandleRowsSliderChanged(float value)
    {
        pendingRows = Mathf.RoundToInt(value);
        UpdateLabel(rowsValueLabel, pendingRows);
    }

    private void HandleColumnsSliderChanged(float value)
    {
        pendingColumns = Mathf.RoundToInt(value);
        UpdateLabel(columnsValueLabel, pendingColumns);
    }

    private void HandleThresholdAChanged(string value)
    {
        thresholdAText = value;
        RefreshThresholdUI();
    }

    private void HandleThresholdBChanged(string value)
    {
        thresholdBText = value;
        RefreshThresholdUI();
    }

    private void HandleThresholdCChanged(string value)
    {
        thresholdCText = value;
        RefreshThresholdUI();
    }

    private static void UpdateLabel(TMP_Text label, int value)
    {
        if (label != null)
        {
            label.text = value.ToString();
        }
    }

    private static void UpdateInputField(TMP_InputField input, string value)
    {
        if (input != null)
        {
            input.SetTextWithoutNotify(value ?? string.Empty);
        }
    }

    private static void UpdateThresholdLabel(TMP_Text label, string value)
    {
        if (label != null)
        {
            label.text = string.IsNullOrEmpty(value) ? "-" : value;
        }
    }

    private void ConfigureInputField(TMP_InputField input, UnityAction<string> callback)
    {
        if (input == null)
        {
            return;
        }

        input.onValueChanged.RemoveListener(callback);
        input.onValueChanged.AddListener(callback);
    }

    private void RemoveInputListener(TMP_InputField input, UnityAction<string> callback)
    {
        if (input == null)
        {
            return;
        }

        input.onValueChanged.RemoveListener(callback);
    }

    private bool TryValidateThresholds(out int thresholdA, out int thresholdB, out int thresholdC)
    {
        bool isValid = true;

        thresholdA = ParseOrClamp(thresholdAText, 2, ref isValid);
        thresholdB = ParseOrClamp(thresholdBText, CalculateMinimumThresholdB(thresholdA), ref isValid);
        thresholdC = ParseOrClamp(thresholdCText, Mathf.Max(thresholdB + 1, 4), ref isValid);

        return isValid;
    }

    private static int ParseOrClamp(string value, int minValue, ref bool isValid)
    {
        if (!int.TryParse(value, out int parsed) || parsed < minValue)
        {
            isValid = false;
            return minValue;
        }

        return parsed;
    }

    private void RefreshThresholdUI()
    {
        UpdateThresholdLabel(thresholdALabel, thresholdAText);
        UpdateThresholdLabel(thresholdBLabel, thresholdBText);
        UpdateThresholdLabel(thresholdCLabel, thresholdCText);
        UpdateInputField(thresholdAInput, thresholdAText);
        UpdateInputField(thresholdBInput, thresholdBText);
        UpdateInputField(thresholdCInput, thresholdCText);
    }

    private void TriggerRuleWarning()
    {
        if (ruleLabel == null)
        {
            return;
        }

        Transform target = ruleLabel.transform;
        target.DOKill();
        target.localScale = ruleBaseScale;
        target.DOScale(ruleBaseScale * rulePulseScale, rulePulseDuration)
            .SetLoops(2, LoopType.Yoyo)
            .SetEase(Ease.OutQuad);
    }

    private static int CalculateMinimumThresholdB(int thresholdA)
    {
        int minimum = thresholdA + 2;
        if (thresholdA == 3)
        {
            minimum = Mathf.Max(minimum, 5);
        }

        return minimum;
    }

    private void SetApplyButtonSelectedColor(Color color)
    {
        if (applySettingsButton == null)
        {
            return;
        }

        ColorBlock colors = applySettingsButton.colors;
        colors.selectedColor = color;
        applySettingsButton.colors = colors;
    }
}
