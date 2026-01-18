using UnityEngine;

public class Node : MonoBehaviour
{
    public Block OccupiedBlock;
    public Vector2 Pos => transform.position;
    public Vector2Int gridPosition;
    [SerializeField] private int sortingOrder;

    public int SortingOrder => sortingOrder;

    public void SetSortingOrder(int order)
    {
        sortingOrder = order;
    }
}
