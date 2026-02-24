using System;
using UnityEngine;
using UnityEngine.Serialization;

public class ColorClearBlock : SpecialBlock
{
    [Serializable]
    private struct ColorVariant
    {
        public int blockType;
        public Sprite icon;
    }

    [SerializeField] private ColorVariant[] variantIcons = Array.Empty<ColorVariant>();
    [SerializeField] private float rotationSpeed = 180f;
    [Header("Beam Settings")]
    [SerializeField] private Color beamColor = Color.white;
    [SerializeField] private float beamTravelSpeed = 15f;
    [FormerlySerializedAs("beamDelayBetweenTargets")]
    [SerializeField] private float beamCompletionDelay = 0.05f;
    [SerializeField] private float beamLineWidth = 0.1f;

    private byte targetColorId = BoardModel.EmptyColorId;
    private int mappedVariantBlockType = -1;
    private static readonly Color32[] BeamColorLookup =
    {
        new Color32(64, 138, 255, 255),
        new Color32(76, 219, 143, 255),
        new Color32(255, 120, 198, 255),
        new Color32(181, 99, 255, 255),
        new Color32(255, 97, 97, 255),
        new Color32(255, 214, 96, 255)
    };

    public Color BeamColor => beamColor;
    public float BeamTravelSpeed => Mathf.Max(0.01f, beamTravelSpeed);
    public float BeamCompletionDelay => Mathf.Max(0f, beamCompletionDelay);
    public float BeamLineWidth => Mathf.Max(0.01f, beamLineWidth);
    public byte TargetColorId => targetColorId;

    public void ConfigureTargetColor(int sourceBlockType)
    {
        targetColorId = (byte)Mathf.Clamp(sourceBlockType, 0, byte.MaxValue);
        ApplyVariantSprite(sourceBlockType);
        ApplyBeamAppearance(targetColorId);
    }

    public override int GatherSearchResults(BlockSearchData searchData)
    {
        BoardModel board = searchData.BoardModel;
        int[] buffer = searchData.ResultBuffer;
        if (board == null || buffer == null)
        {
            return 0;
        }

        Cell startCell = board.GetCell(searchData.StartIndex);
        byte fallbackColor = startCell.occupied ? startCell.colorId : BoardModel.EmptyColorId;
        byte targetColor = targetColorId != BoardModel.EmptyColorId ? targetColorId : fallbackColor;

        int count = 0;
        if (targetColor != BoardModel.EmptyColorId)
        {
            count = GatherColorResults(searchData, targetColor);
        }

        ApplyBeamAppearance(targetColor);

        bool startMatchesTarget = startCell.occupied && startCell.colorId == targetColor;
        if (!startMatchesTarget && count < buffer.Length)
        {
            buffer[count++] = searchData.StartIndex;
        }

        return count;
    }


    protected override void OnStateReset()
    {
        base.OnStateReset();
        targetColorId = BoardModel.EmptyColorId;
        mappedVariantBlockType = -1;
    }

    private void ApplyVariantSprite(int blockType)
    {
        if (mappedVariantBlockType == blockType)
        {
            return;
        }

        Sprite variantSprite = null;
        for (int i = 0; i < variantIcons.Length; i++)
        {
            if (variantIcons[i].blockType == blockType)
            {
                variantSprite = variantIcons[i].icon;
                break;
            }
        }

        if (variantSprite != null)
        {
            SetSprite(variantSprite);
            mappedVariantBlockType = blockType;
        }
        else
        {
            mappedVariantBlockType = -1;
            SetSprite(DefaultIcon);
        }
    }

    private void Update()
    {
        if (!enabled || rotationSpeed == 0f)
        {
            return;
        }

        transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);
    }

    private void ApplyBeamAppearance(byte colorId)
    {
        if (colorId == BoardModel.EmptyColorId || BeamColorLookup == null || BeamColorLookup.Length == 0)
        {
            return;
        }

        int index = Mathf.Clamp(colorId, 0, BeamColorLookup.Length - 1);
        beamColor = BeamColorLookup[index];
    }
}
