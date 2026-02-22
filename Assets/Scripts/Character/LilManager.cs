using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class LilManager : MonoBehaviour
{
    public static LilManager Instance { get; private set; }

    [SerializeField] private LilStateMachine stateMachine;
    [SerializeField] private BlockManager blockManager;
    [SerializeField] private Camera lilCamera;
    [SerializeField, Min(0.1f)] private float defaultStateDuration = 2f;

    private Coroutine introSequenceRoutine;
    private Coroutine temporaryStateRoutine;
    private Coroutine waitForGameManagerRoutine;
    private Coroutine manipulationLoopRoutine;
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
        StopManipulationLoop();
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
        TriggerConfiguredManipulation(ResolveLilManipulationSettings(), LilManipulationVariant.ManipulationOne);
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

        StopManipulationLoop();
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
                StopManipulationLoop();
                StartLevelIntroSequence();
                break;
            case GameManager.GameState.WaitingInput:
                if (!introSequenceActive)
                {
                    stateMachine.EnterState(LilStateMachine.LilState.Waiting);
                    StartManipulationLoop();
                }
                break;
            case GameManager.GameState.Win:
                CancelIntroSequence();
                CancelTransientStateRoutine();
                StopManipulationLoop();
                stateMachine.EnterState(LilStateMachine.LilState.Win, true);
                break;
            case GameManager.GameState.Lose:
                CancelIntroSequence();
                CancelTransientStateRoutine();
                StopManipulationLoop();
                stateMachine.EnterState(LilStateMachine.LilState.Lose, true);
                break;
            case GameManager.GameState.Pause:
                StopManipulationLoop();
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

        StopManipulationLoop();
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
        if (GameManager.Instance != null && GameManager.Instance.IsWaitingForInput)
        {
            StartManipulationLoop();
        }
        introSequenceRoutine = null;
    }

    private void PlayTransientState(LilStateMachine.LilState state, float durationOverride = -1f)
    {
        if (stateMachine == null)
        {
            return;
        }

        CancelIntroSequence();
        CancelTransientStateRoutine();
        temporaryStateRoutine = StartCoroutine(RunTransientState(state, durationOverride));
    }

    private IEnumerator RunTransientState(LilStateMachine.LilState state, float durationOverride)
    {
        stateMachine.EnterState(state, true);
        float duration = durationOverride > 0f ? durationOverride : GetStateDuration(state);
        yield return new WaitForSeconds(duration);
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

    private void StartManipulationLoop()
    {
        if (manipulationLoopRoutine != null || stateMachine == null)
        {
            return;
        }

        GameModeConfig.LilManipulationSettings settings = ResolveLilManipulationSettings();
        if (!settings.Enabled)
        {
            return;
        }

        manipulationLoopRoutine = StartCoroutine(RunLilManipulationLoop(settings));
    }

    private void StopManipulationLoop()
    {
        if (manipulationLoopRoutine == null)
        {
            return;
        }

        StopCoroutine(manipulationLoopRoutine);
        manipulationLoopRoutine = null;
    }

    private IEnumerator RunLilManipulationLoop(GameModeConfig.LilManipulationSettings settings)
    {
        while (true)
        {
            float interval = GetRandomManipulationInterval(settings);
            if (interval > 0f)
            {
                yield return new WaitForSeconds(interval);
            }
            else
            {
                yield return null;
            }

            while (!IsReadyForAutoManipulation())
            {
                yield return null;
            }

            float settleDelay = TriggerConfiguredManipulation(settings, GetRandomManipulationVariant());
            if (settleDelay > 0f)
            {
                yield return new WaitForSeconds(settleDelay);
            }
        }
    }

    private GameModeConfig.LilManipulationSettings ResolveLilManipulationSettings()
    {
        GameManager manager = GameManager.Instance;
        GameModeConfig config = manager != null ? manager.ActiveGameModeConfig : null;
        return config != null ? config.LilManipulation : GameModeConfig.LilManipulationSettings.Default;
    }

    private bool IsReadyForAutoManipulation()
    {
        if (introSequenceActive || stateMachine == null)
        {
            return false;
        }

        GameManager manager = GameManager.Instance;
        if (manager == null || !manager.IsWaitingForInput)
        {
            return false;
        }

        return stateMachine.CurrentState == LilStateMachine.LilState.Waiting;
    }

    private float TriggerConfiguredManipulation(GameModeConfig.LilManipulationSettings settings, LilManipulationVariant variant)
    {
        if (stateMachine == null)
        {
            return 0f;
        }

        LilStateMachine.LilState state;
        switch (variant)
        {
            case LilManipulationVariant.ManipulationOne:
                Debug.Log("Lil triggered Manipulation 1.");
                ApplyManipulationOne();
                state = LilStateMachine.LilState.ManipulationOne;
                break;
            case LilManipulationVariant.ManipulationTwo:
                Debug.Log("Lil triggered Manipulation 2.");
                ApplyManipulationTwo();
                state = LilStateMachine.LilState.ManipulationTwo;
                break;
            default:
                state = LilStateMachine.LilState.ManipulationOne;
                break;
        }

        float duration = ResolveManipulationDuration(settings, state);
        PlayTransientState(state, duration);
        return duration;
    }

    private float ResolveManipulationDuration(GameModeConfig.LilManipulationSettings settings, LilStateMachine.LilState manipulationState)
    {
        float configured = settings.ManipulationDurationSeconds;
        if (configured > 0f)
        {
            return configured;
        }

        return GetStateDuration(manipulationState);
    }

    private void ApplyManipulationOne()
    {
        BlockManager activeBlockManager = ResolveBlockManager();
        if (activeBlockManager == null)
        {
            Debug.LogWarning("Lil manipulation requested but BlockManager is missing.");
            return;
        }

        activeBlockManager.RestoreAllStaticIce();
    }

    private void ApplyManipulationTwo()
    {
        BlockManager activeBlockManager = ResolveBlockManager();
        if (activeBlockManager == null)
        {
            Debug.LogWarning("Lil manipulation requested but BlockManager is missing.");
            return;
        }

        int converted = activeBlockManager.ConvertRandomBlocksToStaticTargets(3);
        if (converted <= 0)
        {
            Debug.Log("Lil could not convert any blocks to targets.");
        }
    }

    private static LilManipulationVariant GetRandomManipulationVariant()
    {
        return UnityEngine.Random.value < 0.5f ? LilManipulationVariant.ManipulationOne : LilManipulationVariant.ManipulationTwo;
    }

    private static float GetRandomManipulationInterval(GameModeConfig.LilManipulationSettings settings)
    {
        float min = settings.MinIntervalSeconds;
        float max = settings.MaxIntervalSeconds;
        if (max <= min)
        {
            return Mathf.Max(0f, min);
        }

        return UnityEngine.Random.Range(min, max);
    }

    private BlockManager ResolveBlockManager()
    {
        if (blockManager != null)
        {
            return blockManager;
        }

        GameManager manager = GameManager.Instance;
        if (manager != null)
        {
            blockManager = manager.BlockManager;
        }

        if (blockManager == null)
        {
            blockManager = FindObjectOfType<BlockManager>();
        }

        return blockManager;
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

    private enum LilManipulationVariant
    {
        ManipulationOne = 0,
        ManipulationTwo = 1
    }
}
