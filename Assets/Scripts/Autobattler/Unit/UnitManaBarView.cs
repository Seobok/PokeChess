using UnityEngine;
using PokeChess.Autobattler;

/// <summary>
/// Keeps a world-space mana bar synced with a unit's replicated mana.
/// </summary>
public class UnitManaBarView : MonoBehaviour
{
    [SerializeField] private UnitController unit;
    [SerializeField] private RectTransform fillRect;
    [SerializeField] private RectTransform barRect;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private bool faceCamera = true;

    private bool _lastVisibleState = true;

    private void Awake()
    {
        if (unit == null)
            unit = GetComponentInParent<UnitController>();

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCanvas == null)
            targetCanvas = GetComponentInChildren<Canvas>(true);
    }

    private void LateUpdate()
    {
        if (unit == null)
            return;

        if (!unit.IsNetworkStateReady)
            return;

        UpdateVisibility();
        if (!_lastVisibleState)
            return;

        RefreshFill();

        if (faceCamera && targetCamera != null)
        {
            transform.forward = targetCamera.transform.forward;
        }
    }

    private void UpdateVisibility()
    {
        bool visible = true;
        var flow = Object.FindAnyObjectByType<GameFlowManager>();
        if (flow != null)
        {
            visible = flow.ShouldUnitBeVisible(unit);
        }

        if (visible == _lastVisibleState)
            return;

        _lastVisibleState = visible;
        if (targetCanvas != null)
        {
            targetCanvas.enabled = visible;
        }
    }

    private void RefreshFill()
    {
        if (fillRect == null || barRect == null || unit == null)
            return;

        float normalized = unit.ManaNormalized;
        fillRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, barRect.rect.width * normalized);
    }
}
