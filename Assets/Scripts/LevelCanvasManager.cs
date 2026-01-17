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

        blockTypeSelectionPanel?.Hide();
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

        blockTypeSelectionPanel?.Hide();
        blockManager.PowerShuffle();
        Audio?.PlayPowerShuffle();
        GameManager.Instance?.ForceResolveAfterPowerup();
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
            GameManager.Instance?.ForceResolveAfterPowerup();
        }
        else
        {
            Audio?.PlayInvalidSelection();
        }
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
