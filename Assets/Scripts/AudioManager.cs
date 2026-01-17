using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Serializable]
    public struct BlockSfxEntry
    {
        public int blockType;
        public AudioClip clip;
    }

    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private List<BlockSfxEntry> blockSfxEntries = new List<BlockSfxEntry>();
    [SerializeField] private AudioClip invalidSelectionClip;

    private readonly Dictionary<int, AudioClip> clipLookup = new Dictionary<int, AudioClip>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        CacheAudioSource();
        RebuildLookup();
        DontDestroyOnLoad(gameObject);
    }

    public void PlayBlockSfx(int blockType)
    {
        if (sfxSource == null)
        {
            return;
        }

        if (!clipLookup.TryGetValue(blockType, out AudioClip clip) || clip == null)
        {
            return;
        }

        sfxSource.PlayOneShot(clip);
    }

    public void PlayInvalidSelection()
    {
        if (sfxSource == null || invalidSelectionClip == null)
        {
            return;
        }

        sfxSource.PlayOneShot(invalidSelectionClip);
    }

    public void RebuildLookup()
    {
        clipLookup.Clear();
        if (blockSfxEntries == null)
        {
            return;
        }

        foreach (var entry in blockSfxEntries)
        {
            if (entry.clip == null)
            {
                continue;
            }

            clipLookup[entry.blockType] = entry.clip;
        }
    }

    private void CacheAudioSource()
    {
        if (sfxSource == null)
        {
            sfxSource = GetComponent<AudioSource>();
        }

        if (sfxSource != null)
        {
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheAudioSource();
        RebuildLookup();
    }
#endif
}
