using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    [Serializable]
    public struct SceneMusicEntry
    {
        public string sceneName;
        public AudioClip clip;
    }

    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private List<BlockSfxEntry> blockSfxEntries = new List<BlockSfxEntry>();
    [SerializeField] private List<SceneMusicEntry> sceneMusicEntries = new List<SceneMusicEntry>();
    [SerializeField] private AudioClip invalidSelectionClip;
    [SerializeField] private AudioClip shuffleClip;
    [SerializeField] private AudioClip powerShuffleClip;
    [SerializeField] private AudioClip destroyAllClip;
    [SerializeField] private AudioClip destroySpecificClip;

    private readonly Dictionary<int, AudioClip> clipLookup = new Dictionary<int, AudioClip>();
    private readonly Dictionary<string, SceneMusicEntry> sceneMusicLookup = new Dictionary<string, SceneMusicEntry>(StringComparer.Ordinal);
    private AudioClip currentMusicClip;
    private bool musicEnabled = true;
    private bool sfxEnabled = true;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        musicEnabled = PlayerSettings.MusicEnabled;
        sfxEnabled = PlayerSettings.SfxEnabled;

        CacheAudioSources();
        RebuildLookup();
        RebuildMusicLookup();
        SceneManager.sceneLoaded += HandleSceneLoaded;
        DontDestroyOnLoad(gameObject);
        PlaySceneMusic(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }
    }

    public void PlayBlockSfx(int blockType)
    {
        if (sfxSource == null || !sfxEnabled)
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
        if (sfxSource == null || invalidSelectionClip == null || !sfxEnabled)
        {
            return;
        }

        sfxSource.PlayOneShot(invalidSelectionClip);
    }

    public void PlayShuffle()
    {
        PlayOneShot(shuffleClip);
    }

    public void PlayPowerShuffle()
    {
        PlayOneShot(powerShuffleClip);
    }

    public void PlayDestroyAll()
    {
        PlayOneShot(destroyAllClip);
    }

    public void PlayDestroySpecific()
    {
        PlayOneShot(destroySpecificClip);
    }

    public void SetMusicEnabled(bool enabled)
    {
        musicEnabled = enabled;
        if (musicSource != null)
        {
            musicSource.mute = !enabled;
            if (!enabled)
            {
                musicSource.Stop();
            }
            else if (currentMusicClip != null)
            {
                musicSource.clip = currentMusicClip;
                musicSource.loop = true;
                musicSource.Play();
            }
        }
    }

    public void SetSfxEnabled(bool enabled)
    {
        sfxEnabled = enabled;
        if (sfxSource != null)
        {
            sfxSource.mute = !enabled;
        }
    }

    public bool IsMusicEnabled => musicEnabled;
    public bool IsSfxEnabled => sfxEnabled;

    public void PlaySceneMusic(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            StopSceneMusic();
            return;
        }

        if (!sceneMusicLookup.TryGetValue(sceneName, out SceneMusicEntry entry) || entry.clip == null)
        {
            StopSceneMusic();
            return;
        }

        currentMusicClip = entry.clip;

        if (musicSource == null || !musicEnabled)
        {
            return;
        }

        if (musicSource.clip == entry.clip && musicSource.isPlaying)
        {
            return;
        }

        musicSource.clip = entry.clip;
        musicSource.loop = true;
        musicSource.Play();
    }

    public void StopSceneMusic()
    {
        if (musicSource == null)
        {
            return;
        }

        musicSource.Stop();
        musicSource.clip = null;
        currentMusicClip = null;
    }

    private void PlayOneShot(AudioClip clip)
    {
        if (sfxSource == null || clip == null || !sfxEnabled)
        {
            return;
        }

        sfxSource.PlayOneShot(clip);
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

    public void RebuildMusicLookup()
    {
        sceneMusicLookup.Clear();
        if (sceneMusicEntries == null)
        {
            return;
        }

        foreach (var entry in sceneMusicEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.sceneName) || entry.clip == null)
            {
                continue;
            }

            sceneMusicLookup[entry.sceneName] = entry;
        }
    }

    private void CacheAudioSources()
    {
        if (sfxSource == null)
        {
            sfxSource = GetComponent<AudioSource>();
        }

        if (sfxSource != null)
        {
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.mute = !sfxEnabled;
        }

        if (musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
        }
        else
        {
            musicSource.playOnAwake = false;
        }

        if (musicSource != null)
        {
            musicSource.mute = !musicEnabled;
        }
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        PlaySceneMusic(scene.name);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        CacheAudioSources();
        RebuildLookup();
        RebuildMusicLookup();
    }
#endif
}
