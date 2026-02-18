using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class LilManager : MonoBehaviour
{
    public static LilManager Instance { get; private set; }

    [SerializeField] private LilStateMachine stateMachine;
    [SerializeField] private Camera lilCamera;
    [SerializeField, Min(0.1f)] private float defaultStateDuration = 2f;

    private Coroutine introSequenceRoutine;
    private Coroutine temporaryStateRoutine;
    private Coroutine waitForGameManagerRoutine;
    private bool introSequenceActive;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (stateMachine == null)
        {
            stateMachine = GetComponentInChildren<LilStateMachine>();
        }

        CacheCamera();
        EnsureCameraPersistence();
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        SubscribeToGameManager();
    }

    private void Start()
    {
        EvaluateScene(SceneManager.GetActiveScene());
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        UnsubscribeFromGameManager();
        if (waitForGameManagerRoutine != null)
        {
            StopCoroutine(waitForGameManagerRoutine);
            waitForGameManagerRoutine = null;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnValidate()
    {
        CacheCamera();
        EnsureCameraParented();
    }

    public void TriggerHumiliation()
    {
        PlayTransientState(LilStateMachine.LilState.Humiliation);
    }

    public void TriggerManipulation()
    {
        PlayTransientState(LilStateMachine.LilState.Manipulation);
    }

    private void SubscribeToGameManager()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.StateChanged += HandleGameStateChanged;
        }
        else if (waitForGameManagerRoutine == null)
        {
            waitForGameManagerRoutine = StartCoroutine(WaitForGameManager());
        }
    }

    private void UnsubscribeFromGameManager()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.StateChanged -= HandleGameStateChanged;
        }
    }

    private IEnumerator WaitForGameManager()
    {
        while (GameManager.Instance == null)
        {
            yield return null;
        }

        GameManager.Instance.StateChanged += HandleGameStateChanged;
        waitForGameManagerRoutine = null;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EvaluateScene(scene);
    }

    private void EvaluateScene(Scene scene)
    {
        if (stateMachine == null)
        {
            return;
        }

        EnsureCameraParented();

        if (IsMenuScene(scene))
        {
            CancelIntroSequence();
            CancelTransientStateRoutine();
            stateMachine.EnterState(LilStateMachine.LilState.Menu, true);
            return;
        }

        StartLevelIntroSequence();
    }

    private static bool IsMenuScene(Scene scene)
    {
        if (!scene.IsValid())
        {
            return false;
        }

        return scene.buildIndex == 0;
    }

    private void HandleGameStateChanged(GameManager.GameState newState)
    {
        if (stateMachine == null)
        {
            return;
        }

        switch (newState)
        {
            case GameManager.GameState.GenerateLevel:
                StartLevelIntroSequence();
                break;
            case GameManager.GameState.WaitingInput:
                if (!introSequenceActive)
                {
                    stateMachine.EnterState(LilStateMachine.LilState.Waiting);
                }
                break;
            case GameManager.GameState.Win:
                CancelIntroSequence();
                CancelTransientStateRoutine();
                stateMachine.EnterState(LilStateMachine.LilState.Win, true);
                break;
            case GameManager.GameState.Lose:
                CancelIntroSequence();
                CancelTransientStateRoutine();
                stateMachine.EnterState(LilStateMachine.LilState.Lose, true);
                break;
            case GameManager.GameState.Pause:
                // Scene-loaded hook handles switching back to the menu visual.
                break;
        }
    }

    private void StartLevelIntroSequence()
    {
        if (stateMachine == null)
        {
            return;
        }

        CancelIntroSequence();
        introSequenceRoutine = StartCoroutine(RunLevelIntro());
    }

    private IEnumerator RunLevelIntro()
    {
        introSequenceActive = true;
        stateMachine.EnterState(LilStateMachine.LilState.LevelBeginning, true);
        yield return new WaitForSeconds(GetStateDuration(LilStateMachine.LilState.LevelBeginning));
        introSequenceActive = false;
        stateMachine.EnterState(LilStateMachine.LilState.Waiting);
        introSequenceRoutine = null;
    }

    private void PlayTransientState(LilStateMachine.LilState state)
    {
        if (stateMachine == null)
        {
            return;
        }

        CancelIntroSequence();
        CancelTransientStateRoutine();
        temporaryStateRoutine = StartCoroutine(RunTransientState(state));
    }

    private IEnumerator RunTransientState(LilStateMachine.LilState state)
    {
        stateMachine.EnterState(state, true);
        yield return new WaitForSeconds(GetStateDuration(state));
        temporaryStateRoutine = null;
        if (stateMachine.CurrentState == state)
        {
            stateMachine.EnterState(LilStateMachine.LilState.Waiting);
        }
    }

    private void CancelIntroSequence()
    {
        if (introSequenceRoutine != null)
        {
            StopCoroutine(introSequenceRoutine);
            introSequenceRoutine = null;
        }
        introSequenceActive = false;
    }

    private void CancelTransientStateRoutine()
    {
        if (temporaryStateRoutine != null)
        {
            StopCoroutine(temporaryStateRoutine);
            temporaryStateRoutine = null;
        }
    }

    private float GetStateDuration(LilStateMachine.LilState state)
    {
        if (stateMachine == null)
        {
            return defaultStateDuration;
        }

        float recommended = stateMachine.GetRecommendedDuration(state);
        return recommended > 0f ? recommended : defaultStateDuration;
    }

    private void CacheCamera()
    {
        if (lilCamera == null)
        {
            lilCamera = GetComponentInChildren<Camera>();
        }
    }

    private void EnsureCameraPersistence()
    {
        if (lilCamera == null)
        {
            return;
        }

        EnsureCameraParented();
        DontDestroyOnLoad(lilCamera.gameObject);
    }

    private void EnsureCameraParented()
    {
        if (lilCamera == null)
        {
            return;
        }

        Transform cameraTransform = lilCamera.transform;
        if (cameraTransform.parent != transform)
        {
            cameraTransform.SetParent(transform, true);
        }
    }
}
