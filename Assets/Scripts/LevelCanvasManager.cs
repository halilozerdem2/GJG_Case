using UnityEngine;

public class LevelCanvasManager : MonoBehaviour
{
    [SerializeField] private BlockManager blockManager;
    [SerializeField] private BlockTypeSelectionPanel blockTypeSelectionPanel;
    [SerializeField] private AudioManager audioManager;

    private AudioManager Audio => audioManager != null ? audioManager : AudioManager.Instance;

    public void OnShuffleButton()
    {
        if (blockManager == null)
        {
            Debug.LogWarning("LevelCanvasManager requires a BlockManager reference for shuffle.");
            return;
        }

        if (GameManager.Instance == null || !GameManager.Instance.IsWaitingForInput)
        {
            Audio?.PlayInvalidSelection();
            return;
        }

        blockTypeSelectionPanel?.Hide();
        GameManager.Instance.ForceShuffleInProgress();
        Audio?.PlayShuffle();
        blockManager.ResolveDeadlock(HandleShuffleCompleted);
    }

    public void OnPowerShuffleButton()
    {
        if (blockManager == null)
        {
            Debug.LogWarning("LevelCanvasManager requires a BlockManager reference for power shuffle.");
            return;
        }

        if (GameManager.Instance == null || !GameManager.Instance.IsWaitingForInput)
        {
            Audio?.PlayInvalidSelection();
            return;
        }

        blockTypeSelectionPanel?.Hide();
        GameManager.Instance.ForceShuffleInProgress();
        blockManager.PowerShuffle(HandlePowerShuffleCompleted);
        Audio?.PlayPowerShuffle();
    }

    public void OnDestroyAllButton()
    {
        if (blockManager == null)
        {
            Debug.LogWarning("LevelCanvasManager requires a BlockManager reference for destroy all.");
            return;
        }

        blockTypeSelectionPanel?.Hide();
        bool destroyed = blockManager.DestroyAllBlocks();
        if (destroyed)
        {
            Audio?.PlayDestroyAll();
            GameManager.Instance?.ForceSpawnAfterBoardClear();
        }
        else
        {
            Audio?.PlayInvalidSelection();
        }
    }

    public void OnDestroySpecificButton()
    {
        if (blockTypeSelectionPanel == null)
        {
            Debug.LogWarning("Block type selection panel not assigned.");
            return;
        }

        if (blockTypeSelectionPanel.IsVisible)
        {
            blockTypeSelectionPanel.Hide();
        }
        else
        {
            blockTypeSelectionPanel.Show(HandleDestroySpecificSelection);
        }
    }

    private void HandleShuffleCompleted(bool success)
    {
        if (success)
        {
            GameManager.Instance?.ForceWaitingAfterShuffle();
        }
        else
        {
            Audio?.PlayInvalidSelection();
            GameManager.Instance?.ForceWaitingAfterShuffle();
        }
    }

    private void HandlePowerShuffleCompleted()
    {
        GameManager.Instance?.ForceWaitingAfterShuffle();
    }

    private void HandleDestroySpecificSelection(int blockType)
    {
        if (blockManager == null)
        {
            Debug.LogWarning("LevelCanvasManager requires a BlockManager reference for destroy specific.");
            return;
        }

        bool destroyed = blockManager.DestroyBlocksOfType(blockType);
        if (destroyed)
        {
            Audio?.PlayDestroySpecific();
            GameManager.Instance?.ForceResolveAfterPowerup();
        }
        else
        {
            Audio?.PlayInvalidSelection();
        }
    }
}
