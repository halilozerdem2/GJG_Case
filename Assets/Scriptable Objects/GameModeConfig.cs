using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "GameModeConfig", menuName = "Scriptable Objects/Game Mode Config")]
public class GameModeConfig : ScriptableObject
{
    [SerializeField] private string configId = "Game";
    [SerializeField] private MoveTimeLimitSettings limits = MoveTimeLimitSettings.Default;
    [SerializeField] private List<PowerupCooldownEntry> powerupCooldowns = new List<PowerupCooldownEntry>();
    [SerializeField] private List<SpecialBlockThreshold> specialBlockThresholds = new List<SpecialBlockThreshold>();
    [SerializeField] private List<StaticTargetSpawn> staticTargetSpawns = new List<StaticTargetSpawn>();
    [SerializeField] private List<SpecialBlockPrefab> specialBlockPrefabs = new List<SpecialBlockPrefab>();

    private static readonly PowerupCooldownEntry[] EmptyPowerupCooldowns = Array.Empty<PowerupCooldownEntry>();
    private static readonly SpecialBlockThreshold[] EmptySpecialBlockThresholds = Array.Empty<SpecialBlockThreshold>();
    private static readonly StaticTargetSpawn[] EmptyStaticTargets = Array.Empty<StaticTargetSpawn>();
    private static readonly SpecialBlockPrefab[] EmptySpecialBlockPrefabs = Array.Empty<SpecialBlockPrefab>();

    public string ConfigId => configId;
    public MoveTimeLimitSettings Limits => limits;
    public IReadOnlyList<PowerupCooldownEntry> PowerupCooldowns => powerupCooldowns != null ? powerupCooldowns : (IReadOnlyList<PowerupCooldownEntry>)EmptyPowerupCooldowns;
    public IReadOnlyList<SpecialBlockThreshold> SpecialBlockThresholds => specialBlockThresholds != null ? specialBlockThresholds : (IReadOnlyList<SpecialBlockThreshold>)EmptySpecialBlockThresholds;
    public IReadOnlyList<StaticTargetSpawn> StaticTargetSpawns => staticTargetSpawns != null ? staticTargetSpawns : (IReadOnlyList<StaticTargetSpawn>)EmptyStaticTargets;
    public IReadOnlyList<SpecialBlockPrefab> SpecialBlockPrefabs => specialBlockPrefabs != null ? specialBlockPrefabs : (IReadOnlyList<SpecialBlockPrefab>)EmptySpecialBlockPrefabs;

    public bool TryGetPowerupCooldown(PowerupType type, out float cooldownSeconds)
    {
        var cooldownEntries = PowerupCooldowns;
        for (int i = 0; i < cooldownEntries.Count; i++)
        {
            if (cooldownEntries[i].Powerup == type)
            {
                cooldownSeconds = Mathf.Max(0f, cooldownEntries[i].CooldownSeconds);
                return true;
            }
        }

        cooldownSeconds = 0f;
        return false;
    }

    public Block GetSpecialBlockPrefab(Block.BlockArchetype archetype, int blockType)
    {
        Block fallback = null;
        var prefabs = SpecialBlockPrefabs;
        for (int i = 0; i < prefabs.Count; i++)
        {
            SpecialBlockPrefab entry = prefabs[i];
            if (entry.Archetype != archetype || entry.Prefab == null)
            {
                continue;
            }

            if (entry.Prefab.blockType == blockType)
            {
                return entry.Prefab;
            }

            fallback ??= entry.Prefab;
        }

        return fallback;
    }

    public bool TryGetSpecialBlockPrefab(Block.BlockArchetype archetype, int blockType, out Block prefab)
    {
        prefab = GetSpecialBlockPrefab(archetype, blockType);
        return prefab != null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        limits.Clamp();
        EnsureList(ref powerupCooldowns);
        EnsureList(ref specialBlockThresholds);
        EnsureList(ref staticTargetSpawns);
        EnsureList(ref specialBlockPrefabs);
    }

    private void EnsureList<T>(ref List<T> list)
    {
        if (list == null)
        {
            list = new List<T>();
        }
    }
#endif

    [Serializable]
    public struct MoveTimeLimitSettings
    {
        [SerializeField] private bool useMoveLimit;
        [SerializeField, Min(0)] private int moveLimit;
        [SerializeField] private bool useTimeLimit;
        [SerializeField, Min(0f)] private float timeLimitSeconds;

        public static MoveTimeLimitSettings Default => new MoveTimeLimitSettings
        {
            useMoveLimit = false,
            moveLimit = 0,
            useTimeLimit = false,
            timeLimitSeconds = 0f
        };

        public bool UseMoveLimit => useMoveLimit && moveLimit > 0;
        public int MoveLimit => Mathf.Max(0, moveLimit);
        public bool UseTimeLimit => useTimeLimit && timeLimitSeconds > 0f;
        public float TimeLimitSeconds => Mathf.Max(0f, timeLimitSeconds);

        public void Clamp()
        {
            moveLimit = Mathf.Max(0, moveLimit);
            timeLimitSeconds = Mathf.Max(0f, timeLimitSeconds);
        }
    }

    [Serializable]
    public struct PowerupCooldownEntry
    {
        [SerializeField] private PowerupType powerup;
        [SerializeField, Min(0f)] private float cooldownSeconds;

        public PowerupType Powerup => powerup;
        public float CooldownSeconds => Mathf.Max(0f, cooldownSeconds);
    }

    [Serializable]
    public struct SpecialBlockThreshold
    {
        [SerializeField] private BoardThresholdKey thresholdKey;
        [SerializeField] private Block.BlockArchetype spawnArchetype;
        [SerializeField] private bool useGroupColor;
        [SerializeField] private int overrideBlockType;

        public BoardThresholdKey ThresholdKey => thresholdKey;
        public Block.BlockArchetype SpawnArchetype => spawnArchetype;
        public bool UseGroupColor => useGroupColor;
        public int OverrideBlockType => overrideBlockType;

        public int ResolveMinimumGroupSize(BoardSettings settings)
        {
            if (settings == null)
            {
                return 0;
            }

            switch (thresholdKey)
            {
                case BoardThresholdKey.ThresholdB:
                    return settings.ThresholdB;
                case BoardThresholdKey.ThresholdC:
                    return settings.ThresholdC;
                default:
                    return settings.ThresholdA;
            }
        }

        public int ResolveSpawnBlockType(int groupBlockType)
        {
            return useGroupColor ? groupBlockType : overrideBlockType;
        }
    }

    public enum BoardThresholdKey
    {
        ThresholdA,
        ThresholdB,
        ThresholdC
    }

    [Serializable]
    public struct StaticTargetSpawn
    {
        [SerializeField] private Block targetPrefab;
        [SerializeField, Min(0)] private int minPerBoard;
        [SerializeField, Min(0)] private int maxPerBoard;
        [SerializeField, Range(0f, 1f)] private float normalizedWeight;

        public Block TargetPrefab => targetPrefab;
        public int MinPerBoard => Mathf.Max(0, Mathf.Min(minPerBoard, maxPerBoard));
        public int MaxPerBoard => Mathf.Max(MinPerBoard, maxPerBoard);
        public float NormalizedWeight => Mathf.Clamp01(normalizedWeight);
    }

    [Serializable]
    public struct SpecialBlockPrefab
    {
        [SerializeField] private Block.BlockArchetype archetype;
        [SerializeField] private Block prefab;

        public Block.BlockArchetype Archetype => archetype;
        public Block Prefab => prefab;
    }
}

public enum PowerupType
{
    Shuffle,
    PowerShuffle,
    DestroyAll,
    DestroySpecific
}
