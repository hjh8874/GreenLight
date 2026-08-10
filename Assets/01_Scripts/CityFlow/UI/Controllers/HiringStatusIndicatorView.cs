using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityFlow.UI
{
    public sealed class HiringStatusIndicatorView : MonoBehaviour
    {
        private const float CanvasSize = 160f;
        private const float FirstSlotRotation = -113f;

        [Header("Layer Lab")]
        [SerializeField] private Sprite segmentSprite;
        [SerializeField] private TMP_FontAsset fontAsset;

        [Header("Presentation")]
        [SerializeField] private Color vacantColor = Color.white;
        [SerializeField] private Color filledColor =
            new(0.4117647f, 0.654902f, 0.76862746f, 1f);

        private readonly List<Image> _segments = new();
        private RectTransform _segmentRoot;
        private TextMeshProUGUI _statusText;
        private int _segmentCount = -1;

        public int SegmentCount => _segments.Count;

        public int FilledSegmentCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < _segments.Count; index++)
                {
                    if (_segments[index] != null &&
                        _segments[index].color == filledColor)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public string StatusText =>
            _statusText != null ? _statusText.text : string.Empty;

        public bool TrySetScreenPosition(
            RectTransform canvasRect,
            Camera cam,
            Vector3 worldPosition)
        {
            if (canvasRect == null || cam == null)
            {
                return false;
            }

            Vector3 screenPosition = cam.WorldToScreenPoint(worldPosition);
            if (screenPosition.z <= 0f ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect,
                    screenPosition,
                    null,
                    out Vector2 localPosition))
            {
                return false;
            }

            if (transform is not RectTransform indicatorRect)
            {
                return false;
            }

            indicatorRect.anchoredPosition = localPosition;
            return true;
        }

        private void Awake()
        {
            TextMeshPro legacyLabel = GetComponent<TextMeshPro>();
            if (legacyLabel != null)
            {
                legacyLabel.enabled = false;
            }
        }

        public void Configure(int filled, int capacity)
        {
            int safeCapacity = Mathf.Max(1, capacity);
            int safeFilled = Mathf.Clamp(filled, 0, safeCapacity);

            EnsureVisualRoot();
            EnsureSegments(safeCapacity);

            for (int index = 0; index < _segments.Count; index++)
            {
                _segments[index].color =
                    index < safeFilled ? filledColor : vacantColor;
            }

            if (_statusText != null)
            {
                _statusText.text = "채용 중";
            }
        }

        private void EnsureVisualRoot()
        {
            if (_segmentRoot != null && _statusText != null)
            {
                return;
            }

            if (transform is not RectTransform indicatorRect)
            {
                Debug.LogError(
                    "[HiringStatusIndicatorView] RectTransform이 필요합니다.",
                    this);
                return;
            }

            indicatorRect.anchorMin = new Vector2(0.5f, 0.5f);
            indicatorRect.anchorMax = new Vector2(0.5f, 0.5f);
            indicatorRect.pivot = new Vector2(0.5f, 0.5f);
            indicatorRect.sizeDelta = new Vector2(CanvasSize, CanvasSize);
            indicatorRect.localScale = Vector3.one;

            var segmentRootObject = new GameObject(
                "Segments",
                typeof(RectTransform));
            segmentRootObject.transform.SetParent(indicatorRect, false);
            _segmentRoot = segmentRootObject.GetComponent<RectTransform>();
            StretchToParent(_segmentRoot);

            var textObject = new GameObject(
                "StatusText",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(indicatorRect, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0.5f, 0.5f);
            textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = new Vector2(72f, 24f);

            _statusText = textObject.GetComponent<TextMeshProUGUI>();
            if (fontAsset != null)
            {
                _statusText.font = fontAsset;
            }
            _statusText.enableAutoSizing = true;
            _statusText.fontSizeMin = 8f;
            _statusText.fontSizeMax = 13f;
            _statusText.fontStyle = FontStyles.Bold;
            _statusText.alignment = TextAlignmentOptions.Center;
            _statusText.color = Color.white;
            _statusText.textWrappingMode = TextWrappingModes.NoWrap;
            _statusText.raycastTarget = false;
        }

        private void EnsureSegments(int capacity)
        {
            if (_segmentCount == capacity)
            {
                return;
            }

            ClearSegments();
            _segmentCount = capacity;
            float anglePerSegment = 360f / capacity;

            for (int index = 0; index < capacity; index++)
            {
                var segmentObject = new GameObject(
                    $"Segment_{index + 1}",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image));
                segmentObject.transform.SetParent(_segmentRoot, false);

                RectTransform rect =
                    segmentObject.GetComponent<RectTransform>();
                StretchToParent(rect);
                rect.localEulerAngles =
                    new Vector3(
                        0f,
                        0f,
                        FirstSlotRotation - anglePerSegment * index);

                Image image = segmentObject.GetComponent<Image>();
                image.sprite = segmentSprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
                image.raycastTarget = false;
                image.color = vacantColor;
                _segments.Add(image);
            }
        }

        private void ClearSegments()
        {
            for (int index = _segments.Count - 1; index >= 0; index--)
            {
                Image segment = _segments[index];
                if (segment == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(segment.gameObject);
                }
                else
                {
                    DestroyImmediate(segment.gameObject);
                }
            }

            _segments.Clear();
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
        }
    }
}
