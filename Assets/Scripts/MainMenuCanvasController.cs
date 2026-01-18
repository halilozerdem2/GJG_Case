using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuCanvasController : MonoBehaviour
{
    [SerializeField] private int caseSceneIndex = 1;
    [SerializeField] private int playSceneIndex = 2;

    public void GoToCaseScene()
    {
        LoadScene(caseSceneIndex);
    }

    public void GoToPlayScene()
    {
        LoadScene(playSceneIndex);
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
}
