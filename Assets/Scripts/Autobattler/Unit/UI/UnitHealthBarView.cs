using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using PokeChess.Autobattler;

/// <summary>
/// Keeps a world-space health bar synced with a unit's replicated HP.
/// </summary>
public class UnitHealthBarView : MonoBehaviour
{
    [SerializeField] private UnitController unit;
    [SerializeField] private RectTransform fillRect;
    [SerializeField] private RectTransform barRect;
    [SerializeField] private RectTransform tickContainer;
    [SerializeField] private RectTransform tickPrefab;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Canvas targetCanvas;
    [SerializeField] private bool faceCamera = true;

    private float _lastMaxHp = -1f;
    private bool _uiInitialized;
    private bool _lastVisibleState = true;
    private readonly List<RectTransform> _ticks = new();

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

        if (!_uiInitialized)
        {
            RebuildTicksIfNeeded(force: true);
            RefreshFill();
            _uiInitialized = true;
        }
        else
        {
            RebuildTicksIfNeeded();
            RefreshFill();
        }

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

        float normalized = unit.HealthNormalized;
        fillRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, barRect.rect.width * normalized);
    }

    private void RebuildTicksIfNeeded(bool force = false)
    {
        if (unit == null || tickContainer == null || tickPrefab == null || barRect == null)
            return;

        float maxHp = unit.MaxHp;
        if (!force && Mathf.Approximately(_lastMaxHp, maxHp))
            return;

        _lastMaxHp = maxHp;

        for (int i = 0; i < _ticks.Count; i++)
        {
            if (_ticks[i] != null)
                Destroy(_ticks[i].gameObject);
        }
        _ticks.Clear();

        int tickCount = Mathf.FloorToInt(maxHp / 100f);
        if (tickCount <= 0)
            return;

        float width = barRect.rect.width;
        for (int i = 1; i <= tickCount; i++)
        {
            float hpMark = i * 100f;
            if (hpMark >= maxHp)
                break;

            float t = hpMark / maxHp;
            RectTransform tick = Instantiate(tickPrefab, tickContainer);
            tick.anchorMin = new Vector2(0f, 0f);
            tick.anchorMax = new Vector2(0f, 1f);
            tick.pivot = new Vector2(0.5f, 0.5f);
            tick.anchoredPosition = new Vector2(width * t, 0f);
            _ticks.Add(tick);
        }
    }
}
