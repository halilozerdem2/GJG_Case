using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TargetCollectionAnimator : MonoBehaviour
{
    [System.Serializable]
    private struct TargetBinding
    {
        public int blockType;
        public RectTransform target;
        public Sprite icon;
        public Color color;
    }

    [SerializeField] private BlockManager blockManager;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private RectTransform canvasRoot;
    [SerializeField] private RectTransform indicatorPrefab;
    [SerializeField] private float travelDuration = 0.65f;
    [SerializeField] private AnimationCurve travelCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.5f);
    [SerializeField] private TargetBinding[] bindings = System.Array.Empty<TargetBinding>();

    private readonly Queue<RectTransform> indicatorPool = new Queue<RectTransform>();
    private readonly Dictionary<int, TargetBinding> bindingLookup = new Dictionary<int, TargetBinding>();
    private Camera canvasCamera;
    private Camera worldCamera;

    private void Awake()
    {
        BuildLookup();
        ResolveCameras();
    }

    private void OnEnable()
    {
        if (blockManager == null)
        {
            blockManager = FindObjectOfType<BlockManager>();
        }

        blockManager.StaticBlockCollected += HandleStaticBlockCollected;
    }

    private void OnDisable()
    {
        if (blockManager != null)
        {
            blockManager.StaticBlockCollected -= HandleStaticBlockCollected;
        }
    }

    private void BuildLookup()
    {
        bindingLookup.Clear();
        for (int i = 0; i < bindings.Length; i++)
        {
            TargetBinding binding = bindings[i];
            if (binding.target == null)
            {
                continue;
            }

            bindingLookup[binding.blockType] = binding;
        }
    }

    private void ResolveCameras()
    {
        worldCamera = Camera.main;
        if (targetCanvas != null)
        {
            canvasCamera = targetCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCanvas.worldCamera;
        }
        else
        {
            var canvas = GetComponentInParent<Canvas>();
            targetCanvas = canvas;
            if (canvas != null)
            {
                canvasCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            }
        }

        if (canvasRoot == null && targetCanvas != null)
        {
            canvasRoot = targetCanvas.transform as RectTransform;
        }
    }

    private void HandleStaticBlockCollected(int blockType, Vector3 worldPosition)
    {
        if (canvasRoot == null)
        {
            return;
        }

        if (!bindingLookup.TryGetValue(blockType, out TargetBinding binding))
        {
            return;
        }

        RectTransform indicator = GetIndicator();
        PrepareIndicator(indicator, binding);

        if (!TryGetCanvasPosition(worldPosition, false, out Vector2 start))
        {
            ReleaseIndicator(indicator);
            return;
        }

        if (!TryGetCanvasPosition(binding.target.position, true, out Vector2 targetPos))
        {
            ReleaseIndicator(indicator);
            return;
        }

        indicator.anchoredPosition = start;
        indicator.gameObject.SetActive(true);
        StartCoroutine(AnimateIndicator(indicator, targetPos));
    }

    private bool TryGetCanvasPosition(Vector3 worldPosition, bool useCanvasCamera, out Vector2 canvasPosition)
    {
        canvasPosition = Vector2.zero;
        if (canvasRoot == null)
        {
            return false;
        }

        Camera referenceCamera = useCanvasCamera ? canvasCamera : worldCamera;
        if (referenceCamera == null)
        {
            referenceCamera = Camera.main;
        }

        Vector3 screenPoint = referenceCamera != null
            ? referenceCamera.WorldToScreenPoint(worldPosition)
            : worldPosition;

        Camera uiCamera = canvasCamera;
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRoot, screenPoint, uiCamera, out canvasPosition);
    }

    private RectTransform GetIndicator()
    {
        if (indicatorPool.Count > 0)
        {
            return indicatorPool.Dequeue();
        }

        if (indicatorPrefab == null)
        {
            Debug.LogWarning("TargetCollectionAnimator is missing indicatorPrefab reference.");
            return null;
        }

        RectTransform instance = Instantiate(indicatorPrefab, canvasRoot);
        instance.gameObject.SetActive(false);
        return instance;
    }

    private void PrepareIndicator(RectTransform indicator, TargetBinding binding)
    {
        if (indicator == null)
        {
            return;
        }

        Image image = indicator.GetComponentInChildren<Image>();
        if (image != null)
        {
            if (binding.icon != null)
            {
                image.sprite = binding.icon;
            }

            if (binding.color.a > 0f)
            {
                image.color = binding.color;
            }
        }

        TMP_Text text = indicator.GetComponentInChildren<TMP_Text>();
        if (text != null)
        {
            text.text = string.Empty;
        }

        indicator.localScale = Vector3.one;
    }

    private IEnumerator AnimateIndicator(RectTransform indicator, Vector2 targetPosition)
    {
        if (indicator == null)
        {
            yield break;
        }

        Vector2 start = indicator.anchoredPosition;
        float duration = Mathf.Max(0.05f, travelDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            float curved = travelCurve != null ? travelCurve.Evaluate(t) : t;
            indicator.anchoredPosition = Vector2.LerpUnclamped(start, targetPosition, curved);

            float scaleFactor = scaleCurve != null ? scaleCurve.Evaluate(t) : 1f;
            indicator.localScale = Vector3.one * Mathf.Max(0.01f, scaleFactor);

            elapsed += Time.deltaTime;
            yield return null;
        }

        indicator.anchoredPosition = targetPosition;
        ReleaseIndicator(indicator);
    }

    private void ReleaseIndicator(RectTransform indicator)
    {
        if (indicator == null)
        {
            return;
        }

        indicator.gameObject.SetActive(false);
        indicatorPool.Enqueue(indicator);
    }
}
