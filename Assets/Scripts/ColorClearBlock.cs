using System;
using UnityEngine;

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

    private byte targetColorId = BoardModel.EmptyColorId;
    private int mappedVariantBlockType = -1;

    public void ConfigureTargetColor(int sourceBlockType)
    {
        targetColorId = (byte)Mathf.Clamp(sourceBlockType, 0, byte.MaxValue);
        ApplyVariantSprite(sourceBlockType);
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
}
