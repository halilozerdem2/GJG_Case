using UnityEngine;

/// <summary>
/// Holds references to the win/lose panels in a scene and exposes static access so GameManager
/// can toggle them when the game state reaches Win or Lose.
/// </summary>
public class WinLosePanelController : MonoBehaviour
{
    public static WinLosePanelController ActiveInstance { get; private set; }

    [SerializeField] private GameObject winPanel;
    [SerializeField] private GameObject losePanel;
    [SerializeField] private bool hidePanelsOnEnable = true;

    private void Awake()
    {
        if (hidePanelsOnEnable)
        {
            HidePanels();
        }
    }

    private void OnEnable()
    {
        ActiveInstance = this;
        if (hidePanelsOnEnable)
        {
            HidePanels();
        }
    }

    private void OnDisable()
    {
        if (ActiveInstance == this)
        {
            ActiveInstance = null;
        }
    }

    public void ShowWinPanel()
    {
        SetPanelStates(true, false);
    }

    public void ShowLosePanel()
    {
        SetPanelStates(false, true);
    }

    public void HidePanels()
    {
        SetPanelStates(false, false);
    }

    private void SetPanelStates(bool winActive, bool loseActive)
    {
        if (winPanel != null)
        {
            winPanel.SetActive(winActive);
        }

        if (losePanel != null)
        {
            losePanel.SetActive(loseActive);
        }
    }
}
