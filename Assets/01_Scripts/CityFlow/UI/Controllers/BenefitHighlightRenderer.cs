using System.Collections.Generic;
using UnityEngine;
using CityFlow.Contracts;

namespace CityFlow.UI.Controllers
{
    /// <summary>
    /// 인프라(학교, 병원 등) 건설 호버 시 혜택을 받는 주거 타일들을 시각적으로 강조(하이라이트)해주는 렌더러.
    /// GameObject Pooling을 활용하여 가비지(GC) 생성을 방지합니다.
    /// </summary>
    public class BenefitHighlightRenderer : MonoBehaviour
    {
        [Header("School Settings")]
        [SerializeField] private Color schoolAreaColor = new Color(0f, 0.7f, 1f, 0.15f); // 옅은 파란색
        [SerializeField] private Color schoolHouseColor = new Color(0f, 0.7f, 1f, 0.6f); // 짙은 파란색

        [Header("Hospital Settings")]
        [SerializeField] private Color hospitalAreaColor = new Color(0f, 1f, 0.3f, 0.15f); // 옅은 초록색
        [SerializeField] private Color hospitalHouseColor = new Color(0f, 1f, 0.3f, 0.6f); // 짙은 초록색


        [Header("General Settings")]
        [SerializeField] private float yOffset = 0.05f;

        private readonly List<GameObject> _pool = new List<GameObject>();
        private Texture2D _highlightTexture;
        private Sprite _highlightSprite;

        private void Awake()
        {
            InitializeSprite();
        }

        private void InitializeSprite()
        {
            if (_highlightSprite != null)
            {
                return;
            }

            _highlightTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "BenefitHighlightTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            _highlightTexture.SetPixel(0, 0, Color.white);
            _highlightTexture.Apply();

            _highlightSprite = Sprite.Create(
                _highlightTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            _highlightSprite.name = "BenefitHighlightSprite";
            _highlightSprite.hideFlags = HideFlags.HideAndDontSave;
        }

        public void ShowHighlights(
            IReadOnlyList<Vector2Int> areaCoords,
            IReadOnlyList<Vector2Int> houseCoords,
            bool useXYPlane = false,
            bool isHospital = false,
            IWorldCoordinateSpace coordinateSpace = null)
        {
            if (_highlightSprite == null) InitializeSprite();

            Color areaColor = isHospital ? hospitalAreaColor : schoolAreaColor;
            Color houseColor = isHospital ? hospitalHouseColor : schoolHouseColor;

            int required = areaCoords.Count + houseCoords.Count;
            while (_pool.Count < required)
            {
                GameObject tile = CreateHighlightTile();
                _pool.Add(tile);
            }

            int poolIndex = 0;

            // 1. 넓은 반경 영역 그리기 (바닥에 더 가깝게)
            for (int i = 0; i < areaCoords.Count; i++)
            {
                SetupTile(
                    _pool[poolIndex++],
                    areaCoords[i],
                    areaColor,
                    useXYPlane,
                    yOffset - 0.02f,
                    coordinateSpace);
            }

            // 2. 집 타일 진하게 그리기 (기존 높이 유지)
            for (int i = 0; i < houseCoords.Count; i++)
            {
                SetupTile(
                    _pool[poolIndex++],
                    houseCoords[i],
                    houseColor,
                    useXYPlane,
                    yOffset,
                    coordinateSpace);
            }

            // 나머지 비활성화
            for (int i = poolIndex; i < _pool.Count; i++)
            {
                if (_pool[i] != null && _pool[i].activeSelf)
                {
                    _pool[i].SetActive(false);
                }
            }
        }

        private void SetupTile(
            GameObject tileGo,
            Vector2Int coord,
            Color color,
            bool useXYPlane,
            float yOff,
            IWorldCoordinateSpace coordinateSpace)
        {
            Vector3 pos;
            Quaternion rotation;
            if (coordinateSpace != null)
            {
                pos = coordinateSpace.GridToWorld(coord, 0.06f + yOff);
                rotation = coordinateSpace.CoordinateRotation;
            }
            else
            {
                pos = useXYPlane
                    ? new Vector3(
                        coord.x + 0.5f,
                        coord.y + 0.5f,
                        -0.06f - yOff)
                    : new Vector3(coord.x, yOff, coord.y);
                rotation = useXYPlane
                    ? Quaternion.identity
                    : Quaternion.Euler(90f, 0f, 0f);
            }

            tileGo.transform.position = pos;
            tileGo.transform.rotation = rotation;
            tileGo.SetActive(true);

            if (tileGo.TryGetComponent<SpriteRenderer>(out var renderer))
            {
                renderer.color = color;
            }
        }

        public void HideAll()
        {
            for (int i = 0; i < _pool.Count; i++)
            {
                if (_pool[i] != null)
                {
                    _pool[i].SetActive(false);
                }
            }
        }

        private GameObject CreateHighlightTile()
        {
            GameObject go = new GameObject("BenefitHighlightTile");
            go.transform.SetParent(transform);
            go.transform.localScale = Vector3.one;

            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = _highlightSprite;
            renderer.sortingOrder = 90;

            go.SetActive(false);
            return go;
        }

        private void OnDestroy()
        {
            if (_highlightSprite != null)
            {
                Destroy(_highlightSprite);
            }

            if (_highlightTexture != null)
            {
                Destroy(_highlightTexture);
            }
        }
    }
}
