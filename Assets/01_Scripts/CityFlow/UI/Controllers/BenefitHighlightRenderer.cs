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
        [SerializeField] private Color hospitalAreaColor = new Color(0.2f, 0.9f, 0.2f, 0.15f); // 옅은 초록색
        [SerializeField] private Color hospitalHouseColor = new Color(0.2f, 0.9f, 0.2f, 0.6f); // 짙은 초록색

        [Header("General Settings")]
        [SerializeField] private float yOffset = 0.05f;

        private readonly List<GameObject> _pool = new List<GameObject>();
        private Material _sharedMaterial;
        private MaterialPropertyBlock _propBlock;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            InitializeMaterial();
        }

        private void InitializeMaterial()
        {
            if (_sharedMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                
                _sharedMaterial = new Material(shader)
                {
                    color = Color.white // PropertyBlock으로 색상을 덮어씌움
                };
            }
        }

        public void ShowHighlights(IReadOnlyList<Vector2Int> areaCoords, IReadOnlyList<Vector2Int> houseCoords, TileType facilityType, bool useXYPlane = false)
        {
            if (_sharedMaterial == null) InitializeMaterial();
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();

            Color areaColor = facilityType == TileType.School ? schoolAreaColor : hospitalAreaColor;
            Color houseColor = facilityType == TileType.School ? schoolHouseColor : hospitalHouseColor;

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
                SetupTile(_pool[poolIndex++], areaCoords[i], areaColor, useXYPlane, yOffset - 0.02f);
            }

            // 2. 집 타일 진하게 그리기 (기존 높이 유지)
            for (int i = 0; i < houseCoords.Count; i++)
            {
                SetupTile(_pool[poolIndex++], houseCoords[i], houseColor, useXYPlane, yOffset);
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

        private void SetupTile(GameObject tileGo, Vector2Int coord, Color color, bool useXYPlane, float yOff)
        {
            Vector3 pos = useXYPlane
                ? new Vector3(coord.x + 0.5f, coord.y + 0.5f, -0.06f)
                : new Vector3(coord.x, yOff, coord.y);

            tileGo.transform.position = pos;
            tileGo.SetActive(true);

            if (tileGo.TryGetComponent<MeshRenderer>(out var renderer))
            {
                _propBlock.SetColor("_Color", color);
                renderer.SetPropertyBlock(_propBlock);
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
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "BenefitHighlightTile";
            go.transform.SetParent(transform);
            
            // XZ 평면(3D)에 눕히기 위해 90도 회전
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = Vector3.one;

            // MeshCollider 제거 (클릭 및 레이캐스트 방해 방지)
            if (go.TryGetComponent<Collider>(out var collider))
            {
                Destroy(collider);
            }

            if (go.TryGetComponent<MeshRenderer>(out var renderer))
            {
                renderer.sharedMaterial = _sharedMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            go.SetActive(false);
            return go;
        }

        private void OnDestroy()
        {
            if (_sharedMaterial != null)
            {
                Destroy(_sharedMaterial);
            }
        }
    }
}
