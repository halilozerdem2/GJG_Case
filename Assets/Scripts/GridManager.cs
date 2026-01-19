using System;
using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    [SerializeField] private BoardSettings boardSettings;
    [SerializeField] private Node nodePrefab;
    [SerializeField] private GameObject boardPrefab;
    [SerializeField] private Vector2 boardSurroundPadding = new Vector2(0.2f, 0.2f);
    [SerializeField] private Vector3 gridWorldCenter = Vector3.zero;
    [SerializeField] private float boardScreenPadding = 0.5f;
    [SerializeField] private RectTransform playableAreaRect;

    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    private readonly Vector3[] playableAreaCorners = new Vector3[4];
    private readonly HashSet<Node> freeNodes = new HashSet<Node>();
    private Node[] allNodes = Array.Empty<Node>();
    private Node[,] nodeGrid;
    private Transform gridRoot;
    private GameObject boardInstance;
    private Vector3 boardBaseScale = Vector3.one;
    private Vector2 boardEnvelopeSize;
    private int totalNodeCount;

    public BoardSettings BoardSettings => boardSettings;
    public Node[,] NodeGrid => nodeGrid;
    public HashSet<Node> FreeNodes => freeNodes;
    public Transform GridRoot => gridRoot;
    public int TotalNodeCount => totalNodeCount;

    public void InitializeGrid()
    {
        if (!ValidateBoardSettings())
        {
            return;
        }

        freeNodes.Clear();
        nodeGrid = new Node[boardSettings.Columns, boardSettings.Rows];
        AllocateNodeStorage();

        SetupBoard();
        CreateNodes();
        FitBoardToScreen();
        UpdateFreeNodes();
    }

    private bool ValidateBoardSettings()
    {
        if (boardSettings == null)
        {
            Debug.LogError("GridManager is missing BoardSettings reference.");
            return false;
        }

        if (!boardSettings.IsValid(out string validationMessage))
        {
            Debug.LogError($"Invalid BoardSettings: {validationMessage}");
            return false;
        }

        if (nodePrefab == null)
        {
            Debug.LogError("GridManager is missing Node prefab reference.");
            return false;
        }

        return true;
    }

    private void CreateNodes()
    {
        if (nodeGrid == null)
        {
            return;
        }

        for (int x = 0; x < boardSettings.Columns; x++)
        {
            for (int y = 0; y < boardSettings.Rows; y++)
            {
                Node node;
                if (gridRoot != null)
                {
                    node = Instantiate(nodePrefab, gridRoot);
                    node.transform.localPosition = new Vector3(x - (boardSettings.Columns / 2f - 0.5f),
                        y - (boardSettings.Rows / 2f - 0.5f), 0f);
                }
                else
                {
                    node = Instantiate(nodePrefab, new Vector3(x, y, 0f), Quaternion.identity);
                }

                node.gridPosition = new Vector2Int(x, y);
                node.SetSortingOrder(5 + y);
                nodeGrid[x, y] = node;
                int flatIndex = GetFlatIndex(x, y);
                if (flatIndex >= 0 && flatIndex < allNodes.Length)
                {
                    allNodes[flatIndex] = node;
                }
                freeNodes.Add(node);
            }
        }
    }

    private void SetupBoard()
    {
        if (boardInstance != null)
        {
            Destroy(boardInstance);
        }

        if (gridRoot != null)
        {
            Destroy(gridRoot.gameObject);
        }

        Vector3 boardCenter = GetGridCenter();
        boardEnvelopeSize = new Vector2(
            Mathf.Max(0.01f, boardSettings.Columns + Mathf.Max(0f, boardSurroundPadding.x)),
            Mathf.Max(0.01f, boardSettings.Rows + Mathf.Max(0f, boardSurroundPadding.y)));

        if (boardPrefab != null)
        {
            boardInstance = Instantiate(boardPrefab, boardCenter, Quaternion.identity);
            boardBaseScale = CalculateBoardScaleForGrid(boardInstance);
            boardInstance.transform.localScale = boardBaseScale;
        }

        gridRoot = new GameObject("GridRoot").transform;
        gridRoot.position = boardCenter;
    }

    public void UpdateFreeNodes()
    {
        freeNodes.Clear();
        if (allNodes == null)
        {
            return;
        }

        for (int i = 0; i < totalNodeCount; i++)
        {
            Node node = allNodes[i];
            if (node != null && node.OccupiedBlock == null)
            {
                freeNodes.Add(node);
            }
        }
    }

    public int GetMatchingNeighbours(Block block, List<Block> results)
    {
        if (results == null || block == null || block.node == null)
        {
            return 0;
        }

        foreach (var dir in CardinalDirections)
        {
            Vector2Int neighbourPosition = block.node.gridPosition + dir;
            if (TryGetNode(neighbourPosition, out Node neighbourNode))
            {
                Block neighbourBlock = neighbourNode.OccupiedBlock;
                if (neighbourBlock != null && neighbourBlock.blockType == block.blockType)
                {
                    results.Add(neighbourBlock);
                }
            }
        }

        return results.Count;
    }

    public bool TryGetNode(Vector2Int gridPosition, out Node node)
    {
        node = null;
        if (nodeGrid == null)
        {
            return false;
        }

        if (gridPosition.x < 0 || gridPosition.y < 0)
        {
            return false;
        }

        if (gridPosition.x >= nodeGrid.GetLength(0) || gridPosition.y >= nodeGrid.GetLength(1))
        {
            return false;
        }

        node = nodeGrid[gridPosition.x, gridPosition.y];
        return node != null;
    }

    private void AllocateNodeStorage()
    {
        int columns = Mathf.Max(0, boardSettings.Columns);
        int rows = Mathf.Max(0, boardSettings.Rows);
        totalNodeCount = columns * rows;

        if (totalNodeCount <= 0)
        {
            allNodes = Array.Empty<Node>();
            return;
        }

        if (allNodes == null || allNodes.Length != totalNodeCount)
        {
            allNodes = new Node[totalNodeCount];
        }
        else
        {
            Array.Clear(allNodes, 0, allNodes.Length);
        }
    }

    private int GetFlatIndex(int x, int y)
    {
        if (boardSettings == null)
        {
            return -1;
        }

        return y * boardSettings.Columns + x;
    }

    public Vector3 GetGridCenter()
    {
        return gridWorldCenter;
    }


    private Vector3 CalculateBoardScaleForGrid(GameObject instance)
    {
        if (instance == null)
        {
            return Vector3.one;
        }

        Vector3 originalScale = instance.transform.localScale;
        Vector2 visualSize = GetBoardVisualSize(instance);

        if (visualSize.x <= 0f || visualSize.y <= 0f)
        {
            return new Vector3(
                boardEnvelopeSize.x,
                boardEnvelopeSize.y,
                originalScale.z);
        }

        float widthMultiplier = boardEnvelopeSize.x / visualSize.x;
        float heightMultiplier = boardEnvelopeSize.y / visualSize.y;

        return new Vector3(
            originalScale.x * widthMultiplier,
            originalScale.y * heightMultiplier,
            originalScale.z);
    }

    private Vector2 GetBoardVisualSize(GameObject instance)
    {
        if (instance == null)
        {
            return Vector2.one;
        }

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            return new Vector2(
                Mathf.Max(0.01f, boardSettings.Columns),
                Mathf.Max(0.01f, boardSettings.Rows));
        }

        Bounds combinedBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            combinedBounds.Encapsulate(renderers[i].bounds);
        }

        return new Vector2(
            Mathf.Max(0.01f, combinedBounds.size.x),
            Mathf.Max(0.01f, combinedBounds.size.y));
    }

    public void FitBoardToScreen()
    {
        Camera cam = Camera.main;
        if (cam == null || !cam.orthographic)
        {
            return;
        }

        float boardWidth = boardEnvelopeSize.x > 0.01f ? boardEnvelopeSize.x : boardSettings.Columns;
        float boardHeight = boardEnvelopeSize.y > 0.01f ? boardEnvelopeSize.y : boardSettings.Rows;

        if (TryFitBoardInsidePlayableArea(cam, boardWidth, boardHeight))
        {
            return;
        }

        float verticalView = Mathf.Max(0.01f, cam.orthographicSize * 2f - boardScreenPadding * 2f);
        float horizontalView = verticalView * cam.aspect;

        float scaleX = horizontalView / boardWidth;
        float scaleY = verticalView / boardHeight;
        float scale = Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.01f, 100f);

        Vector3 scaled = new Vector3(scale, scale, 1f);
        if (boardInstance != null)
        {
            boardInstance.transform.localScale = Vector3.Scale(boardBaseScale, scaled);
        }
        if (gridRoot != null)
        {
            gridRoot.localScale = scaled;
            Vector3 boardCenter = GetGridCenter();
            gridRoot.position = boardCenter;
        }
    }

    private bool TryFitBoardInsidePlayableArea(Camera cam, float boardWidth, float boardHeight)
    {
        if (!TryGetPlayableViewport(cam, out Rect viewportRect, out Vector3 worldCenter))
        {
            return false;
        }

        float totalVerticalWorld = Mathf.Max(0.01f, cam.orthographicSize * 2f);
        float totalHorizontalWorld = totalVerticalWorld * cam.aspect;
        float availableHeight = Mathf.Max(0.01f, totalVerticalWorld * viewportRect.height);
        float availableWidth = Mathf.Max(0.01f, totalHorizontalWorld * viewportRect.width);

        float scaleX = availableWidth / boardWidth;
        float scaleY = availableHeight / boardHeight;
        float scale = Mathf.Clamp(Mathf.Min(scaleX, scaleY), 0.01f, 100f);
        Vector3 scaled = new Vector3(scale, scale, 1f);

        if (boardInstance != null)
        {
            boardInstance.transform.localScale = Vector3.Scale(boardBaseScale, scaled);
            boardInstance.transform.position = worldCenter;
        }

        if (gridRoot != null)
        {
            gridRoot.localScale = scaled;
            gridRoot.position = worldCenter;
        }

        return true;
    }

    private bool TryGetPlayableViewport(Camera cam, out Rect viewportRect, out Vector3 worldCenter)
    {
        viewportRect = default;
        worldCenter = GetGridCenter();

        if (cam == null || playableAreaRect == null)
        {
            return false;
        }

        Canvas canvas = playableAreaRect.GetComponentInParent<Canvas>();
        Camera uiCamera = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            uiCamera = canvas.worldCamera != null ? canvas.worldCamera : cam;
        }

        playableAreaRect.GetWorldCorners(playableAreaCorners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);

        for (int i = 0; i < playableAreaCorners.Length; i++)
        {
            Vector3 screenPoint;
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                screenPoint = playableAreaCorners[i];
            }
            else
            {
                Camera referenceCamera = uiCamera != null ? uiCamera : cam;
                screenPoint = RectTransformUtility.WorldToScreenPoint(referenceCamera, playableAreaCorners[i]);
            }

            min = Vector2.Min(min, screenPoint);
            max = Vector2.Max(max, screenPoint);
        }

        if (max.x <= min.x || max.y <= min.y || Screen.width <= 0 || Screen.height <= 0)
        {
            return false;
        }

        Vector2 viewportMin = new Vector2(min.x / Screen.width, min.y / Screen.height);
        Vector2 viewportMax = new Vector2(max.x / Screen.width, max.y / Screen.height);

        viewportRect = Rect.MinMaxRect(
            Mathf.Clamp01(viewportMin.x),
            Mathf.Clamp01(viewportMin.y),
            Mathf.Clamp01(viewportMax.x),
            Mathf.Clamp01(viewportMax.y));

        if (viewportRect.width <= 0f || viewportRect.height <= 0f)
        {
            return false;
        }

        float boardPlaneZ = gridRoot != null ? gridRoot.position.z : gridWorldCenter.z;
        float distance = Mathf.Abs(cam.transform.position.z - boardPlaneZ);
        Vector3 viewportCenter = new Vector3(viewportRect.center.x, viewportRect.center.y, distance);
        worldCenter = cam.ViewportToWorldPoint(viewportCenter);
        worldCenter.z = boardPlaneZ;
        return true;
    }
}
