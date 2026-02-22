using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class LilStateMachine : MonoBehaviour
{
    [Serializable]
    public class StateDefinition
    {
        public LilState state = LilState.Menu;
        [Tooltip("Animator trigger fired when the state is entered.")]
        public string animationTrigger = "Idle";
        [Tooltip("Optional speech clips for the state. A random clip is picked each time.")]
        public AudioClip[] speechClips;
        [Tooltip("Optional speech lines for the state. A random line is picked each time.")]
        [TextArea(2, 4)]
        public string[] speechLines;
        [Tooltip("Recommended minimum time to keep this state active before auto-transitioning.")]
        [Min(0f)]
        public float minimumDuration = 2f;
    }

    public enum LilState
    {
        Menu,
        LevelBeginning,
        Waiting,
        Humiliation,
        ManipulationOne,
        ManipulationTwo,
        Win,
        Lose
    }

    [SerializeField] private Animator animator;
    [SerializeField] private TMP_Text speechLabel;
    [SerializeField] private List<StateDefinition> stateDefinitions = new List<StateDefinition>();
    [SerializeField] private float speechLabelAutoClearDelay = 3f;

    private readonly Dictionary<LilState, StateDefinition> definitionLookup = new Dictionary<LilState, StateDefinition>();
    private Coroutine speechClearRoutine;

    [SerializeField] private LilState currentState = LilState.Menu;

    public LilState CurrentState => currentState;

    public event Action<LilState> StateChanged;

    private void Awake()
    {
        CacheComponents();
        BuildDefinitionLookup();
        ApplyState(currentState);
    }

    private void OnValidate()
    {
        CacheComponents();
        BuildDefinitionLookup();
    }

    private void CacheComponents()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
    }

    private void BuildDefinitionLookup()
    {
        definitionLookup.Clear();
        foreach (var def in stateDefinitions)
        {
            if (def == null)
            {
                continue;
            }

            if (!definitionLookup.ContainsKey(def.state))
            {
                definitionLookup.Add(def.state, def);
            }
        }
    }

    public void EnterState(LilState newState, bool force = false)
    {
        if (!force && newState == currentState)
        {
            return;
        }

        currentState = newState;
        ApplyState(currentState);
        StateChanged?.Invoke(currentState);
    }

    public float GetRecommendedDuration(LilState state)
    {
        if (definitionLookup.TryGetValue(state, out var def))
        {
            return Mathf.Max(0f, def.minimumDuration);
        }

        return 0f;
    }

    public bool TryGetDefinition(LilState state, out StateDefinition definition)
    {
        return definitionLookup.TryGetValue(state, out definition);
    }

    private void ApplyState(LilState state)
    {
        if (!definitionLookup.TryGetValue(state, out var definition))
        {
            return;
        }

        if (animator != null && !string.IsNullOrEmpty(definition.animationTrigger))
        {
            animator.ResetTrigger(definition.animationTrigger);
            animator.SetTrigger(definition.animationTrigger);
        }

        PlaySpeech(definition);
        ShowSpeechText(definition);
    }

    private void PlaySpeech(StateDefinition definition)
    {
        AudioManager audio = AudioManager.Instance;
        if (audio == null)
        {
            return;
        }

        if (definition.speechClips == null || definition.speechClips.Length == 0)
        {
            audio.StopLilSpeech();
            return;
        }

        int clipIndex = UnityEngine.Random.Range(0, definition.speechClips.Length);
        AudioClip clip = definition.speechClips[clipIndex];
        if (clip == null)
        {
            audio.StopLilSpeech();
            return;
        }

        audio.PlayLilSpeech(clip);
    }

    private void ShowSpeechText(StateDefinition definition)
    {
        if (speechLabel == null || definition.speechLines == null || definition.speechLines.Length == 0)
        {
            return;
        }

        var lineIndex = UnityEngine.Random.Range(0, definition.speechLines.Length);
        speechLabel.text = definition.speechLines[lineIndex];

        if (speechClearRoutine != null)
        {
            StopCoroutine(speechClearRoutine);
        }

        float autoClearDelay = Mathf.Max(definition.minimumDuration, speechLabelAutoClearDelay);
        speechClearRoutine = StartCoroutine(ClearSpeechAfterDelay(autoClearDelay));
    }

    private System.Collections.IEnumerator ClearSpeechAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (speechLabel != null)
        {
            speechLabel.text = string.Empty;
        }
        speechClearRoutine = null;
    }
}
