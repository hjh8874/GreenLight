using System.Collections;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using TMPro;
using UnityEngine;

namespace CityFlow.View
{
    /// <summary>
    /// Shows a pooled, billboarded before/after congestion label over an installation.
    /// </summary>
    public sealed class InfrastructureEffectPopView : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private float displayDuration = 1.8f;
        [SerializeField] private float floatHeight = 1.2f;
        [SerializeField] private float fontSize = 5f;
        [SerializeField] private Color textColor = new Color(1f, 0.8f, 0.2f);
        [SerializeField] private int initialPoolSize = 4;
        [SerializeField] private bool useXYPlane;

        private readonly Queue<TextMeshPro> _pool = new Queue<TextMeshPro>();
        private Transform _poolRoot;
        private CityFlowServices _services;
        private bool _subscribed;

        public void Initialize(CityFlowServices services)
        {
            Unsubscribe();
            _services = services;
            Subscribe();
            EnsurePool();
        }

        private void Awake() => EnsurePool();

        private void Subscribe()
        {
            if (_services?.Events == null || _subscribed) return;
            _services.Events.InfrastructureEffect += OnInfrastructureEffect;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (_services?.Events == null || !_subscribed) return;
            _services.Events.InfrastructureEffect -= OnInfrastructureEffect;
            _subscribed = false;
        }

        private void OnInfrastructureEffect(InfrastructureEffectEvent effect)
        {
            TextMeshPro text = GetFromPool();
            text.text = $"정체 {Mathf.RoundToInt(effect.BeforeRatio01 * 100f)}%→{Mathf.RoundToInt(effect.AfterRatio01 * 100f)}%";
            text.color = textColor;
            Vector3 start = GetWorldPosition(effect.Tile);
            text.transform.position = start;
            ApplyBillboard(text.transform);
            text.gameObject.SetActive(true);
            StartCoroutine(Animate(text, start));
        }

        private IEnumerator Animate(TextMeshPro text, Vector3 start)
        {
            float elapsed = 0f;
            while (elapsed < Mathf.Max(0.01f, displayDuration))
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, displayDuration));
                Vector3 direction = _services?.WorldCoordinates == null
                    ? Vector3.up
                    : (_services.WorldCoordinates.Plane == WorldCoordinatePlane.XY
                        ? _services.WorldCoordinates.GridYAxis
                        : _services.WorldCoordinates.GroundNormal);
                text.transform.position = start + direction * (floatHeight * Mathf.Clamp01(t));
                ApplyBillboard(text.transform);
                Color color = textColor;
                color.a = t < 0.55f ? 1f : Mathf.Lerp(1f, 0f, (t - 0.55f) / 0.45f);
                text.color = color;
                yield return null;
            }
            ReturnToPool(text);
        }

        private Vector3 GetWorldPosition(Vector2Int tile)
        {
            if (_services?.WorldCoordinates != null)
            {
                return _services.WorldCoordinates.GridToWorld(tile, 0.5f);
            }
            return useXYPlane
                ? new Vector3(tile.x + 0.5f, tile.y + 0.5f, -0.1f)
                : new Vector3(tile.x + 0.5f, 0.5f, tile.y + 0.5f);
        }

        private void ApplyBillboard(Transform target)
        {
            if (_services?.WorldCoordinates != null &&
                _services.WorldCoordinates.Plane == WorldCoordinatePlane.XY)
            {
                target.rotation = _services.WorldCoordinates.CoordinateRotation;
            }
            else if (!useXYPlane && Camera.main != null)
            {
                target.rotation = Camera.main.transform.rotation;
            }
            else
            {
                target.rotation = Quaternion.identity;
            }
        }

        private void EnsurePool()
        {
            if (_poolRoot == null)
            {
                GameObject root = new GameObject("InfrastructureEffectPopPool");
                root.transform.SetParent(transform);
                _poolRoot = root.transform;
            }
            while (_pool.Count < Mathf.Max(0, initialPoolSize))
            {
                _pool.Enqueue(CreateText());
            }
        }

        private TextMeshPro CreateText()
        {
            GameObject go = new GameObject("UI_InfrastructureEffectText");
            go.transform.SetParent(_poolRoot);
            TextMeshPro text = go.AddComponent<TextMeshPro>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontSize = fontSize;
            text.sortingOrder = 200;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            go.SetActive(false);
            return text;
        }

        private TextMeshPro GetFromPool()
        {
            EnsurePool();
            return _pool.Count > 0 ? _pool.Dequeue() : CreateText();
        }

        private void ReturnToPool(TextMeshPro text)
        {
            if (text == null) return;
            text.gameObject.SetActive(false);
            _pool.Enqueue(text);
        }

        private void OnDestroy() => Unsubscribe();
    }
}
