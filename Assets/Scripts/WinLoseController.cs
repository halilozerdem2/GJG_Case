using UnityEngine;
using UnityEngine.SceneManagement;

public class WinLoseController : MonoBehaviour
{
    [SerializeField] private GameManager gameManager;
    [SerializeField] private CanvasGroup winPanel;
    [SerializeField] private CanvasGroup losePanel;
    [SerializeField] private AudioClip overrideWinClip;
    [SerializeField] private AudioClip overrideLoseClip;
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private float musicFadeDuration = 0.3f;

    private bool hasShownResult;
    private float originalMusicVolume;

    private void Awake()
    {
        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }
    }

    private void OnEnable()
    {
        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }

        if (gameManager != null)
        {
            gameManager.StateChanged += HandleStateChanged;
        }
    }

    private void OnDisable()
    {
        if (gameManager != null)
        {
            gameManager.StateChanged -= HandleStateChanged;
        }
    }

    private void HandleStateChanged(GameManager.GameState state)
    {
        if (hasShownResult)
        {
            return;
        }

        if (state == GameManager.GameState.Win)
        {
            ShowPanel(winPanel, true);
        }
        else if (state == GameManager.GameState.Lose)
        {
            ShowPanel(losePanel, false);
        }
    }

    private void ShowPanel(CanvasGroup panel, bool isWin)
    {
        if (panel == null)
        {
            return;
        }

        panel.gameObject.SetActive(true);
        panel.alpha = 1f;
        panel.interactable = true;
        panel.blocksRaycasts = true;
        hasShownResult = true;

        FadeOutMusic();
        PlayResultClip(isWin);
    }

    private void FadeOutMusic()
    {
        if (musicSource == null)
        {
            musicSource = FindMusicSource();
        }

        if (musicSource == null)
        {
            return;
        }

        originalMusicVolume = musicSource.volume;
        StartCoroutine(FadeMusicCoroutine());
    }

    private System.Collections.IEnumerator FadeMusicCoroutine()
    {
        if (musicSource == null)
        {
            yield break;
        }

        float duration = Mathf.Max(0.05f, musicFadeDuration);
        float elapsed = 0f;
        float startVolume = originalMusicVolume;

        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            musicSource.volume = Mathf.Lerp(startVolume, 0f, t);
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        musicSource.volume = 0f;
        musicSource.Stop();
    }

    private void PlayResultClip(bool isWin)
    {
        AudioManager audio = AudioManager.Instance;
        AudioClip clip = isWin ? overrideWinClip : overrideLoseClip;

        if (audio != null)
        {
            if (clip != null)
            {
                audio.PlayCustomSfx(clip);
                return;
            }

            if (isWin)
            {
                audio.PlayWin();
            }
            else
            {
                audio.PlayLose();
            }
            return;
        }

        if (clip == null)
        {
            return;
        }

        var fallbackSource = GetComponent<AudioSource>();
        if (fallbackSource == null)
        {
            fallbackSource = gameObject.AddComponent<AudioSource>();
        }
        fallbackSource.PlayOneShot(clip);
    }

    public void OnRetryButton()
    {
        RestoreMusic();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void OnHomeButton()
    {
        RestoreMusic();
        SceneManager.LoadScene(0);
    }

    private void RestoreMusic()
    {
        StopAllCoroutines();
        if (musicSource != null)
        {
            musicSource.Stop();
            musicSource.volume = originalMusicVolume;
        }

        hasShownResult = false;
        HidePanel(winPanel);
        HidePanel(losePanel);
    }

    private void HidePanel(CanvasGroup panel)
    {
        if (panel == null)
        {
            return;
        }

        panel.interactable = false;
        panel.blocksRaycasts = false;
        panel.alpha = 0f;
        panel.gameObject.SetActive(false);
    }

    private AudioSource FindMusicSource()
    {
        AudioManager audio = AudioManager.Instance;
        if (audio != null)
        {
            var musicField = typeof(AudioManager).GetField("musicSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (musicField != null)
            {
                return musicField.GetValue(audio) as AudioSource;
            }
        }

        return FindObjectOfType<AudioSource>();
    }
}
