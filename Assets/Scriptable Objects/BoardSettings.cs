using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BoardSettings", menuName = "Scriptable Objects/BoardSettings")]
public class BoardSettings : ScriptableObject
{
    private const int MinSize = 2;
    private const int MaxSize = 10;
    private const int MinColorCount = 1;
    private const int MaxColorCount = 6;

    [Header("Board Dimensions")]
    [SerializeField, Range(MinSize, MaxSize)] private int rows = 7;
    [SerializeField, Range(MinSize, MaxSize)] private int columns = 5;

    [Header("Block Variations")]
    [SerializeField, Range(MinColorCount, MaxColorCount)] private int colors = 5;
    [SerializeField] private Block[] blockPrefabs;
    [Header("VFX")]
    [SerializeField] private ParticleSystem[] blastEffectPrefabs;

    [Header("Group Size Thresholds")]
    [SerializeField, Min(2)] private int thresholdA = 4;
    [SerializeField, Min(3)] private int thresholdB = 7;
    [SerializeField, Min(4)] private int thresholdC = 9;

    public int Rows => rows;
    public int Columns => columns;
    public int Colors => colors;
    public int ThresholdA => thresholdA;
    public int ThresholdB => thresholdB;
    public int ThresholdC => thresholdC;
    public Block[] BlockPrefabs => blockPrefabs;
    public ParticleSystem[] BlastEffectPrefabs => blastEffectPrefabs;
    public int MinRows => MinSize;
    public int MaxRows => MaxSize;
    public int MinColumns => MinSize;
    public int MaxColumns => MaxSize;

    public bool IsValid(out string message)
    {
        if (rows < MinSize || rows > MaxSize)
        {
            message = $"Rows must stay within [{MinSize}, {MaxSize}].";
            return false;
        }

        if (columns < MinSize || columns > MaxSize)
        {
            message = $"Columns must stay within [{MinSize}, {MaxSize}].";
            return false;
        }

        if (colors < MinColorCount || colors > MaxColorCount)
        {
            message = $"Color count must stay within [{MinColorCount}, {MaxColorCount}].";
            return false;
        }

        if (!(thresholdA < thresholdB && thresholdB < thresholdC))
        {
            message = "Thresholds must satisfy A < B < C.";
            return false;
        }

        if (blockPrefabs == null || blockPrefabs.Length != colors)
        {
            message = "Block prefab array length must match color count.";
            return false;
        }

        for (int i = 0; i < blockPrefabs.Length; i++)
        {
            if (blockPrefabs[i] == null)
            {
                message = $"Block prefab at index {i} is missing.";
                return false;
            }
        }

        if (!ValidateBlockPrefabs(out message))
        {
            return false;
        }

        if (blastEffectPrefabs == null || blastEffectPrefabs.Length != colors)
        {
            message = "Blast effect array length must match color count.";
            return false;
        }

        for (int i = 0; i < blastEffectPrefabs.Length; i++)
        {
            if (blastEffectPrefabs[i] == null)
            {
                message = $"Blast effect prefab at index {i} is missing.";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    public ParticleSystem GetBlastEffectPrefab(int blockType)
    {
        if (blockPrefabs == null || blastEffectPrefabs == null)
        {
            return null;
        }

        int count = Mathf.Min(blockPrefabs.Length, blastEffectPrefabs.Length);
        for (int i = 0; i < count; i++)
        {
            Block prefab = blockPrefabs[i];
            if (prefab == null)
            {
                continue;
            }

            if (prefab.blockType == blockType)
            {
                return blastEffectPrefabs[i];
            }
        }

        return null;
    }

    public void ApplyDimensions(int newRows, int newColumns)
    {
        rows = Mathf.Clamp(newRows, MinSize, MaxSize);
        columns = Mathf.Clamp(newColumns, MinSize, MaxSize);
    }

    private bool ValidateBlockPrefabs(out string message)
    {
        message = string.Empty;
        if (blockPrefabs == null || blockPrefabs.Length == 0)
        {
            message = "No block prefabs assigned.";
            return false;
        }

        Dictionary<Sprite, string> defaultIcons = new Dictionary<Sprite, string>();
        Dictionary<Sprite, string> tierOneIcons = new Dictionary<Sprite, string>();
        Dictionary<Sprite, string> tierTwoIcons = new Dictionary<Sprite, string>();
        Dictionary<Sprite, string> tierThreeIcons = new Dictionary<Sprite, string>();

        foreach (Block prefab in blockPrefabs)
        {
            if (prefab == null)
            {
                continue;
            }

            string ownerName = prefab.name;
            if (!TryRegisterSprite(prefab.DefaultIcon, ownerName, "default icon", defaultIcons, out message))
            {
                return false;
            }

            if (!TryRegisterSprite(prefab.TierOneIcon, ownerName, "tier 1 icon", tierOneIcons, out message))
            {
                return false;
            }

            if (!TryRegisterSprite(prefab.TierTwoIcon, ownerName, "tier 2 icon", tierTwoIcons, out message))
            {
                return false;
            }

            if (!TryRegisterSprite(prefab.TierThreeIcon, ownerName, "tier 3 icon", tierThreeIcons, out message))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryRegisterSprite(Sprite sprite, string owner, string label,
        Dictionary<Sprite, string> existingOwners, out string message)
    {
        if (sprite == null)
        {
            message = $"{owner} is missing its {label}.";
            return false;
        }

        if (existingOwners.TryGetValue(sprite, out string otherOwner) && otherOwner != owner)
        {
            message = $"{owner} reuses the {label} already assigned to {otherOwner}.";
            return false;
        }

        existingOwners[sprite] = owner;
        message = string.Empty;
        return true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        rows = Mathf.Clamp(rows, MinSize, MaxSize);
        columns = Mathf.Clamp(columns, MinSize, MaxSize);
        colors = Mathf.Clamp(colors, MinColorCount, MaxColorCount);

        thresholdA = Mathf.Max(2, thresholdA);
        thresholdB = Mathf.Max(thresholdA + 1, thresholdB);
        thresholdC = Mathf.Max(thresholdB + 1, thresholdC);

        if (blockPrefabs == null) return;

        if (blockPrefabs.Length != colors)
        {
            Array.Resize(ref blockPrefabs, colors);
        }

        if (blastEffectPrefabs == null || blastEffectPrefabs.Length != colors)
        {
            Array.Resize(ref blastEffectPrefabs, colors);
        }

        if (!Application.isPlaying && !ValidateBlockPrefabs(out string validationMessage) && !string.IsNullOrEmpty(validationMessage))
        {
            Debug.LogWarning($"BoardSettings validation warning: {validationMessage}", this);
        }
    }
#endif
}
