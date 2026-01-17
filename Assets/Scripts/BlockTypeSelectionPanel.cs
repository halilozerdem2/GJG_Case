using System;
using UnityEngine;

public class BlockTypeSelectionPanel : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CanvasGroup canvasGroup;

    private Action<int> selectionCallback;
    private bool skipInitialHide;

    private void Awake()
    {
        if (panelRoot == null)
        {
            panelRoot = gameObject;
        }

        if (canvasGroup == null && panelRoot != null)
        {
            canvasGroup = panelRoot.GetComponent<CanvasGroup>();
        }

        if (!skipInitialHide)
        {
            SetVisibility(false);
        }

        skipInitialHide = false;
    }

    public void Show(Action<int> onSelected)
    {
        selectionCallback = onSelected;

        if (ShouldSkipInitialHide())
        {
            skipInitialHide = true;
        }

        SetVisibility(true);
        skipInitialHide = false;
    }

    public void Hide()
    {
        selectionCallback = null;
        SetVisibility(false);
    }

    public void SelectBlockType(int blockType)
    {
        if (!IsVisible)
        {
            return;
        }

        selectionCallback?.Invoke(blockType);
        Hide();
    }

    public void CancelSelection()
    {
        Hide();
    }

    private void SetVisibility(bool visible)
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }
        else if (panelRoot != null)
        {
            panelRoot.SetActive(visible);
        }
        else
        {
            gameObject.SetActive(visible);
        }
    }

    private bool ShouldSkipInitialHide()
    {
        if (canvasGroup != null)
        {
            return false;
        }

        if (panelRoot != null)
        {
            return !panelRoot.activeSelf;
        }

        return !gameObject.activeSelf;
    }

    public bool IsVisible
    {
        get
        {
            if (canvasGroup != null)
            {
                return canvasGroup.alpha > 0.01f && canvasGroup.interactable;
            }

            if (panelRoot != null)
            {
                return panelRoot.activeSelf;
            }

            return gameObject.activeSelf;
        }
    }
}
