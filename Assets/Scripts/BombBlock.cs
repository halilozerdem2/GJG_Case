using UnityEngine;

public class BombBlock : SpecialBlock
{
    private int radius = 2;

    protected override int BombRadius => Mathf.Max(1, radius);

    public int ExplosionRadius => BombRadius;
}
