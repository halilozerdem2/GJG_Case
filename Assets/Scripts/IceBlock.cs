using UnityEngine;

public class IceBlock : MonoBehaviour
{
    [SerializeField] private SpriteRenderer overlayRenderer;
    [SerializeField] private Sprite[] strengthSprites = new Sprite[3];
    [SerializeField, Min(1)] private int maxStrength = 3;
    [SerializeField] private int sortingOrderOffset = 5;

    private int currentStrength;
    private SpriteRenderer ownerRenderer;

    public int CurrentStrength => currentStrength;
    public int MaxStrength => Mathf.Max(0, maxStrength);
    public bool HasStrength => currentStrength > 0;

    private void Awake()
    {
        TryCacheOwnerRenderer();
        EnsureOverlayRenderer();
        ResetStrength();
    }

    private void OnEnable()
    {
        UpdateVisual();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        TryCacheOwnerRenderer();
        EnsureOverlayRenderer();
        if (!Application.isPlaying)
        {
            ResetStrength(false);
            return;
        }

        UpdateVisual();
    }
#endif

    public void AlignWith(SpriteRenderer baseRenderer)
    {
        ownerRenderer = baseRenderer;
        EnsureOverlayRenderer();
        if (overlayRenderer == null || baseRenderer == null)
        {
            return;
        }

        overlayRenderer.sortingLayerID = baseRenderer.sortingLayerID;
        overlayRenderer.sortingOrder = baseRenderer.sortingOrder + sortingOrderOffset;
    }

    public void ResetStrength()
    {
        ResetStrength(true);
    }

    public void ResetStrength(bool allowVisibilityToggle)
    {
        currentStrength = Mathf.Max(0, maxStrength);
        UpdateVisual(allowVisibilityToggle);
    }

    public bool ApplyHit()
    {
        if (currentStrength <= 0)
        {
            return false;
        }

        currentStrength = Mathf.Max(0, currentStrength - 1);
        UpdateVisual();
        return currentStrength == 0;
    }

    private void UpdateVisual(bool allowVisibilityToggle = true)
    {
        EnsureOverlayRenderer();
        if (overlayRenderer == null)
        {
            return;
        }

        if (currentStrength <= 0)
        {
            if (allowVisibilityToggle)
            {
                overlayRenderer.enabled = false;
            }
            return;
        }

        if (allowVisibilityToggle)
        {
            overlayRenderer.enabled = true;
        }

        if (strengthSprites != null && strengthSprites.Length > 0)
        {
            int spriteIndex = Mathf.Clamp(MaxStrength - currentStrength, 0, strengthSprites.Length - 1);
            Sprite sprite = strengthSprites[spriteIndex];
            if (sprite != null)
            {
                overlayRenderer.sprite = sprite;
            }
        }
    }

    private void EnsureOverlayRenderer()
    {
        if (overlayRenderer != null)
        {
            return;
        }

        overlayRenderer = FindOverlayRenderer();
        if (overlayRenderer == null)
        {
            GameObject overlay = new GameObject("IceOverlay");
            overlay.transform.SetParent(transform, false);
            overlayRenderer = overlay.AddComponent<SpriteRenderer>();
        }

        if (ownerRenderer != null)
        {
            overlayRenderer.sortingLayerID = ownerRenderer.sortingLayerID;
            overlayRenderer.sortingOrder = ownerRenderer.sortingOrder + sortingOrderOffset;
        }
    }

    private SpriteRenderer FindOverlayRenderer()
    {
        SpriteRenderer[] renderers = GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer candidate = renderers[i];
            if (candidate != null && candidate != ownerRenderer)
            {
                return candidate;
            }
        }

        return null;
    }

    private void TryCacheOwnerRenderer()
    {
        if (ownerRenderer != null)
        {
            return;
        }

        StaticBlock staticBlock = GetComponent<StaticBlock>();
        if (staticBlock == null)
        {
            staticBlock = GetComponentInParent<StaticBlock>();
        }

        if (staticBlock != null)
        {
            ownerRenderer = staticBlock.GetComponent<SpriteRenderer>();
        }
        else if (ownerRenderer == null)
        {
            ownerRenderer = GetComponent<SpriteRenderer>();
        }
    }
}
