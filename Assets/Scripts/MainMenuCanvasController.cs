using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuCanvasController : MonoBehaviour
{
    [SerializeField] private int caseSceneIndex = 1;
    [SerializeField] private int playSceneIndex = 2;
    [Header("Game Mode Selection")]
    [SerializeField] private TMP_Dropdown gameModeDropdown;
    [SerializeField] private float invalidModePulseScale = 1.1f;
    [SerializeField] private float invalidModePulseDuration = 0.2f;
    [SerializeField] private int[] modeSceneBuildIndices = { -1, 1, 2, 3 };
    [Header("Level Configurations")]
    [SerializeField] private GameObject difficultySelectionPanel;

    private Vector3 dropdownBaseScale = Vector3.one;
    private int pendingSceneBuildIndex = -1;

    private void Awake()
    {
        if (gameModeDropdown != null)
        {
            dropdownBaseScale = gameModeDropdown.transform.localScale;
        }
    }

    public void GoToCaseScene()
    {
        SetExplicitGameMode(GameManager.GameMode.Case);
        LoadScene(caseSceneIndex);
    }

    public void GoToPlayScene()
    {
        SetExplicitGameMode(GameManager.GameMode.Easy);
        LoadScene(playSceneIndex);
    }

    public void HandlePlayButtonClicked()
    {
        pendingSceneBuildIndex = -1;

        if (gameModeDropdown == null)
        {
            LoadScene(playSceneIndex);
            return;
        }

        int selection = Mathf.Clamp(gameModeDropdown.value, 0, modeSceneBuildIndices.Length - 1);
        if (selection <= 0 || selection >= modeSceneBuildIndices.Length)
        {
            PulseInvalidDropdown();
            return;
        }

        int targetScene = modeSceneBuildIndices[selection];
        if (targetScene < 0)
        {
            PulseInvalidDropdown();
            return;
        }

        if (selection == 1)
        {
            if (difficultySelectionPanel == null)
            {
                ApplyGameModeFromSelection(selection);
                LoadScene(targetScene);
                return;
            }

            pendingSceneBuildIndex = targetScene;
            ShowDifficultyPanel();
            return;
        }

        ApplyGameModeFromSelection(selection);
        LoadScene(targetScene);
    }

    private void LoadScene(int buildIndex)
    {
        if (buildIndex < 0)
        {
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(buildIndex);
    }

    private void PulseInvalidDropdown()
    {
        if (gameModeDropdown == null)
        {
            return;
        }

        Transform target = gameModeDropdown.transform;
        target.DOKill();
        target.localScale = dropdownBaseScale;
        target.DOScale(dropdownBaseScale * invalidModePulseScale, invalidModePulseDuration)
            .SetLoops(2, LoopType.Yoyo)
            .SetEase(Ease.OutQuad);
    }

    private void ApplyGameModeFromSelection(int selection)
    {
        if (selection <= 0)
        {
            return;
        }

        var gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            return;
        }

        if (selection == 1)
        {
            gameManager.SetGameMode(GameManager.GameMode.Easy);
            return;
        }

        gameManager.SetGameMode(GameManager.GameMode.Case);
    }

    private void ShowDifficultyPanel()
    {
        if (difficultySelectionPanel != null)
        {
            difficultySelectionPanel.SetActive(true);
        }
    }

    private void HideDifficultyPanel()
    {
        if (difficultySelectionPanel != null)
        {
            difficultySelectionPanel.SetActive(false);
        }

        pendingSceneBuildIndex = -1;
    }

    public void SelectEasyDifficulty()
    {
        ApplyDifficultySelection(GameManager.GameMode.Easy);
    }

    public void SelectMediumDifficulty()
    {
        ApplyDifficultySelection(GameManager.GameMode.Medium);
    }

    public void SelectHardDifficulty()
    {
        ApplyDifficultySelection(GameManager.GameMode.Hard);
    }

    private void ApplyDifficultySelection(GameManager.GameMode mode)
    {
        var gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            return;
        }

        gameManager.SetGameMode(mode);

        int targetScene = pendingSceneBuildIndex >= 0 ? pendingSceneBuildIndex : playSceneIndex;
        HideDifficultyPanel();
        LoadScene(targetScene);
    }

    private void SetExplicitGameMode(GameManager.GameMode mode)
    {
        var gameManager = GameManager.Instance;
        if (gameManager == null)
        {
            return;
        }

        gameManager.SetGameMode(mode);
    }
}
