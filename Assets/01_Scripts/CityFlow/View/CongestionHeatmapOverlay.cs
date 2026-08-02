using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.View
{
    /// <summary>
    /// Displays the completed previous day's congestion ratio as a pooled tile overlay.
    /// </summary>
    public sealed class CongestionHeatmapOverlay : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Colors")]
        [SerializeField] private Color transparentColor = new Color(1f, 0.8f, 0.1f, 0f);
        [SerializeField] private Color warmColor = new Color(1f, 0.65f, 0.05f, 0.38f);
        [SerializeField] private Color jamColor = new Color(0.95f, 0.12f, 0.05f, 0.72f);
        [SerializeField] private float surfaceOffset = 0.07f;
        [SerializeField] private bool useXYPlane;

        private readonly List<GameObject> _pool = new List<GameObject>();
        private CityFlowServices _services;
        private ICongestionHistory _history;
        private Sprite _sprite;
        private Texture2D _texture;
        private bool _subscribed;
        private int _width;
        private int _height;

        public void Initialize(CityFlowServices services)
        {
            Unsubscribe();
            _services = services;
            _history = services?.Placement as ICongestionHistory;
            _width = services?.WorldGrid != null ? services.WorldGrid.WorldWidth : 0;
            _height = services?.WorldGrid != null ? services.WorldGrid.WorldHeight : 0;
            EnsureSprite();
            Subscribe();
            if (_services?.Events != null && _services.Events.IsHeatmapViewEnabled)
            {
                Refresh();
            }
        }

        private void Awake() => EnsureSprite();

        private void Subscribe()
        {
            if (_services == null || _subscribed) return;
            _services.Events.HeatmapViewToggled += OnHeatmapToggled;
            _services.GameCalendarRegistered += OnGameCalendarRegistered;
            if (_services.GameCalendar != null)
            {
                _services.GameCalendar.HourChanged += OnHourChanged;
            }
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (_services == null || !_subscribed) return;
            _services.Events.HeatmapViewToggled -= OnHeatmapToggled;
            _services.GameCalendarRegistered -= OnGameCalendarRegistered;
            if (_services.GameCalendar != null)
            {
                _services.GameCalendar.HourChanged -= OnHourChanged;
            }
            _subscribed = false;
        }

        private void OnGameCalendarRegistered(IGameCalendarService calendar)
        {
            calendar.HourChanged += OnHourChanged;
            Refresh();
        }

        private void OnHourChanged(int _) 
        {
            if (_services != null && _services.Events.IsHeatmapViewEnabled)
            {
                Refresh();
            }
        }

        private void OnHeatmapToggled(bool enabled)
        {
            if (enabled) Refresh();
            else HideAll();
        }

        private void Refresh()
        {
            if (_history == null || _width <= 0 || _height <= 0)
            {
                HideAll();
                return;
            }

            int required = _width * _height;
            while (_pool.Count < required)
            {
                _pool.Add(CreateTile());
            }

            int poolIndex = 0;
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    Vector2Int tile = new Vector2Int(x, y);
                    float ratio = Mathf.Clamp01(_history.LastDayJamRatio01(tile));
                    GameObject tileObject = _pool[poolIndex++];
                    if (ratio <= 0f)
                    {
                        tileObject.SetActive(false);
                        continue;
                    }

                    SetupTile(tileObject, tile, HeatmapColor(ratio));
                }
            }
            for (int i = poolIndex; i < _pool.Count; i++)
            {
                _pool[i].SetActive(false);
            }
        }

        private Color HeatmapColor(float ratio)
        {
            if (ratio < 0.5f)
            {
                return Color.Lerp(transparentColor, warmColor, ratio * 2f);
            }
            return Color.Lerp(warmColor, jamColor, (ratio - 0.5f) * 2f);
        }

        private void SetupTile(GameObject tileObject, Vector2Int tile, Color color)
        {
            IWorldCoordinateSpace coordinates = _services?.WorldCoordinates;
            if (coordinates != null)
            {
                tileObject.transform.position = coordinates.GridToWorld(tile, surfaceOffset);
                tileObject.transform.rotation = coordinates.CoordinateRotation;
            }
            else if (useXYPlane)
            {
                tileObject.transform.position = new Vector3(tile.x + 0.5f, tile.y + 0.5f, -surfaceOffset);
                tileObject.transform.rotation = Quaternion.identity;
            }
            else
            {
                tileObject.transform.position = new Vector3(tile.x, surfaceOffset, tile.y);
                tileObject.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }

            tileObject.GetComponent<SpriteRenderer>().color = color;
            tileObject.SetActive(true);
        }

        private GameObject CreateTile()
        {
            GameObject tile = new GameObject("CongestionHeatmapTile");
            tile.transform.SetParent(transform);
            SpriteRenderer renderer = tile.AddComponent<SpriteRenderer>();
            renderer.sprite = _sprite;
            renderer.sortingOrder = 90;
            tile.SetActive(false);
            return tile;
        }

        private void EnsureSprite()
        {
            if (_sprite != null) return;
            _texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "CongestionHeatmapTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _texture.SetPixel(0, 0, Color.white);
            _texture.Apply();
            _sprite = Sprite.Create(_texture, new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f), 1f);
            _sprite.name = "CongestionHeatmapSprite";
            _sprite.hideFlags = HideFlags.HideAndDontSave;
        }

        public void HideAll()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i] != null) _pool[i].SetActive(false);
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (_sprite != null) Destroy(_sprite);
            if (_texture != null) Destroy(_texture);
        }
    }
}
