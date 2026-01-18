using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BoardRegeneratorUI : MonoBehaviour
{
    [SerializeField] private BoardSettings boardSettings;
    [SerializeField] private Slider rowsSlider;
    [SerializeField] private Slider columnsSlider;
    [SerializeField] private TMP_Text rowsValueLabel;
    [SerializeField] private TMP_Text columnsValueLabel;

    private int pendingRows;
    private int pendingColumns;

    private void Awake()
    {
        ConfigureSlider(rowsSlider, HandleRowsSliderChanged, true);
        ConfigureSlider(columnsSlider, HandleColumnsSliderChanged, false);
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
    }

    public void OnRegenerateButtonClicked()
    {
        if (boardSettings == null || GameManager.Instance == null)
        {
            return;
        }

        boardSettings.ApplyDimensions(pendingRows, pendingColumns);
        GameManager.Instance.RegenerateBoardFromSettings();
    }

    private void ConfigureSlider(Slider slider, UnityEngine.Events.UnityAction<float> callback, bool isRowSlider)
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

        if (rowsSlider != null)
        {
            rowsSlider.SetValueWithoutNotify(pendingRows);
        }
        if (columnsSlider != null)
        {
            columnsSlider.SetValueWithoutNotify(pendingColumns);
        }

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

    private static void UpdateLabel(TMP_Text label, int value)
    {
        if (label != null)
        {
            label.text = value.ToString();
        }
    }
}
