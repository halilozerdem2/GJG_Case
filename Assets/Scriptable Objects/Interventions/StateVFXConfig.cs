using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "StateVFXConfig", menuName = "Scriptable Objects/Interventions/State VFX Config")]
public class StateVFXConfig : ScriptableObject
{
    [SerializeField] private List<StateVFXEntry> entries = new List<StateVFXEntry>();

    public IReadOnlyList<StateVFXEntry> Entries => entries ?? (IReadOnlyList<StateVFXEntry>)Array.Empty<StateVFXEntry>();

    public bool TryGet(LilStateId state, out StateVFXEntry entry)
    {
        if (entries != null)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].State == state)
                {
                    entry = entries[i];
                    return true;
                }
            }
        }

        entry = default;
        return false;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (entries == null) entries = new List<StateVFXEntry>();
    }
#endif

    [Serializable]
    public struct StateVFXEntry
    {
        [SerializeField] private LilStateId state;
        [SerializeField] private string animatorTrigger;
        [SerializeField] private AudioClip sfx;
        [SerializeField] private ParticleSystem vfxPrefab;
        [SerializeField, Min(0f)] private float minDuration;
        [SerializeField] private bool oneShot;

        public LilStateId State => state;
        public string AnimatorTrigger => animatorTrigger;
        public AudioClip Sfx => sfx;
        public ParticleSystem VfxPrefab => vfxPrefab;
        public float MinDuration => Mathf.Max(0f, minDuration);
        public bool OneShot => oneShot;
    }
}

public enum LilStateId
{
    Menu,
    Waiting,
    Humiliation,
    Manipulation1,
    Manipulation2,
    Manipulation3,
    Sad,
    Lose,
    Win
}
