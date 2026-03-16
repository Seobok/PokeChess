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
    private Vector3 _initialLocalPosition;
    private bool _localPositionCached;

    private void Awake()
    {
        if (unit == null)
            unit = GetComponentInParent<UnitController>();

        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCanvas == null)
            targetCanvas = GetComponentInChildren<Canvas>(true);

        CacheInitialLocalPosition();
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

        FaceCameraIfNeeded();
        UpdateAnchoredHeightForCamera();
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

    private void FaceCameraIfNeeded()
    {
        if (!faceCamera)
            return;

        if (targetCamera == null || !targetCamera.isActiveAndEnabled)
            targetCamera = Camera.main;

        if (targetCamera == null)
            return;

        // Keep the bar upright on screen even when the combat camera is rolled 180 degrees.
        transform.rotation = Quaternion.LookRotation(targetCamera.transform.forward, Vector3.up);
    }

    private void UpdateAnchoredHeightForCamera()
    {
        CacheInitialLocalPosition();

        Vector3 localPosition = _initialLocalPosition;
        localPosition.y = ShouldInvertVerticalOffset() ? -Mathf.Abs(_initialLocalPosition.y) : Mathf.Abs(_initialLocalPosition.y);
        transform.localPosition = localPosition;
    }

    private void CacheInitialLocalPosition()
    {
        if (_localPositionCached)
            return;

        _initialLocalPosition = transform.localPosition;
        _localPositionCached = true;
    }

    private bool ShouldInvertVerticalOffset()
    {
        if (targetCamera == null)
            return false;

        float zRotation = Mathf.Repeat(targetCamera.transform.eulerAngles.z, 360f);
        return Mathf.Abs(Mathf.DeltaAngle(zRotation, 180f)) < 1f;
    }
}
