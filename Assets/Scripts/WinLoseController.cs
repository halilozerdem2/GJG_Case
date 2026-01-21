using UnityEngine;

/// <summary>
/// Scene-level helper that forwards win/lose requests to the persistent GameManager singleton.
/// Attach this to any scene object (e.g., UI buttons) and wire the public methods from the inspector.
/// </summary>
public class WinLoseController : MonoBehaviour
{
    [SerializeField] private GameManager gameManager;

    private void Awake()
    {
        BindGameManagerIfNeeded();
    }

    private void OnEnable()
    {
        BindGameManagerIfNeeded();
    }

    public void TriggerWin()
    {
        if (!BindGameManagerIfNeeded())
        {
            return;
        }

        gameManager.TriggerWinState();
    }

    public void TriggerLose()
    {
        if (!BindGameManagerIfNeeded())
        {
            return;
        }

        gameManager.TriggerLoseState();
    }

    private bool BindGameManagerIfNeeded()
    {
        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }

        if (gameManager == null)
        {
            Debug.LogWarning("WinLoseController could not locate a GameManager instance in the scene.");
            return false;
        }

        return true;
    }
}
