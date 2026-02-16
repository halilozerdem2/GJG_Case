using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Persistent game controller that carries its own camera across scene loads.
/// Attach this to a GameObject, assign a Camera in the Inspector (or via SetCamera),
/// and it will persist together with the camera.
/// </summary>
public class LilController : MonoBehaviour
{
    public static LilController Instance { get; private set; }

    [SerializeField] private Camera lilCamera;
    [SerializeField] private LilStateMachine stateMachine;

    public Camera LilCamera => lilCamera;
    public LilStateMachine StateMachine => stateMachine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        PreserveCamera();

        if (stateMachine == null)
        {
            stateMachine = GetComponent<LilStateMachine>();
            if (stateMachine == null)
            {
                stateMachine = gameObject.AddComponent<LilStateMachine>();
            }
        }

        // Ensure a SafeWindowGate exists on the persistent root
        if (GetComponent<SafeWindowGate>() == null)
        {
            gameObject.AddComponent<SafeWindowGate>();
        }

        // Ensure an EffectRunner exists on the persistent root
        if (GetComponent<EffectRunner>() == null)
        {
            gameObject.AddComponent<EffectRunner>();
        }

        // Ensure an InterventionDirector exists on the persistent root
        if (GetComponent<InterventionDirector>() == null)
        {
            gameObject.AddComponent<InterventionDirector>();
        }

        // Optional: Debug HUD (can be disabled in Inspector)
        if (GetComponent<InterventionDebugHUD>() == null)
        {
            gameObject.AddComponent<InterventionDebugHUD>();
        }

        // Ensure BoardSafetyService exists on the persistent root
        if (GetComponent<BoardSafetyService>() == null)
        {
            gameObject.AddComponent<BoardSafetyService>();
        }
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += HandleSceneLoaded;
        GameEventBus.OnLevelStart += HandleLevelStart;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        GameEventBus.OnLevelStart -= HandleLevelStart;
    }

    /// <summary>
    /// Assigns and preserves the camera across scene loads.
    /// </summary>
    public void SetCamera(Camera cam)
    {
        if (cam == lilCamera)
        {
            PreserveCamera();
            return;
        }

        lilCamera = cam;
        PreserveCamera();
    }

    private void PreserveCamera()
    {
        if (lilCamera == null)
        {
            return;
        }

        // Optionally parent the camera under this controller so they move together
        if (lilCamera.transform.parent != transform)
        {
            lilCamera.transform.SetParent(transform, true);
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Re-affirm camera parenting and persistence after scene changes
        PreserveCamera();
        // Hook for future: rebind UI outputs or event listeners per scene if needed.

        // If main menu (build index 0), ensure Menu state
        if (stateMachine != null && scene.buildIndex == 0)
        {
            stateMachine.SetState(LilStateId.Menu, true);
        }
    }

    private void HandleLevelStart()
    {
        // When game level setup begins, transition Lil into Waiting state
        if (stateMachine != null)
        {
            stateMachine.SetState(LilStateId.Waiting);
        }
    }
}
