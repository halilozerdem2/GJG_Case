using UnityEngine;

public class BombBlock : SpecialBlock
{
    [SerializeField, Min(1)] private int radius = 1;

    protected override int BombRadius => Mathf.Max(1, radius);
}
