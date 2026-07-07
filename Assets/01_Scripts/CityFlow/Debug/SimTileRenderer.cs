using CityFlow.Bootstrap;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.DebugTools
{
    // 미니 모터웨이즈풍 간이 렌더러: 크림 배경 + 회색 도로 + 작은 건물 + 움직이는 차 점.
    // 혼잡은 별도 색이 아니라 '차 밀도'로 보인다(빽빽+느림=막힘). 정체 딱지 없음.
    // 한주석 View가 진짜 아트로 대체할 때까지 임시 개발 도구.
    public sealed class SimTileRenderer : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private int size = 20;
        private const int CellPx = 12;   // 타일 하나 = 12x12 픽셀
        private const int MaxCars = 2;   // 타일당 차 최대 수(밀도 1.0) — 적게

        private static readonly Color32 Cream = new Color32(238, 232, 210, 255);
        private static readonly Color32 Road  = new Color32(150, 150, 156, 255);
        private static readonly Color32 Car   = new Color32( 40,  40,  48, 255); // 차 = 어두운 점
        private static readonly Color32 House  = new Color32( 90, 150, 220, 255);
        private static readonly Color32 Office = new Color32(235, 150,  60, 255);
        private static readonly Color32 School = new Color32(170, 110, 210, 255);

        private IReadOnlyTileData _data;
        private Texture2D _tex;
        private Color32[] _px;
        private int _w;

        public void Initialize(CityFlowServices services)
        {
            _data = services.TileData;
            _w = size * CellPx;
            _tex = new Texture2D(_w, _w, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            _px = new Color32[_w * _w];

            var sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(_tex, new Rect(0, 0, _w, _w), Vector2.zero, CellPx); // CellPx px = 1 유닛
            transform.position = Vector3.zero;
        }

        private void Update()
        {
            if (_data == null) return;

            for (int i = 0; i < _px.Length; i++) _px[i] = Cream;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var t = new Vector2Int(x, y);
                    switch (_data.GetTileType(t))
                    {
                        case TileType.Road:   FillCell(x, y, 0, Road); DrawCars(x, y, t); break;
                        case TileType.House:  FillCell(x, y, 3, House);  break;
                        case TileType.Office: FillCell(x, y, 3, Office); break;
                        case TileType.School: FillCell(x, y, 3, School); break;
                    }
                }

            _tex.SetPixels32(_px);
            _tex.Apply(false);
        }

        // 도로 위 차 점: 밀도만큼 개수, 정체일수록 느리게 도로 축을 따라 흐름.
        private void DrawCars(int tx, int ty, Vector2Int t)
        {
            float density = _data.GetDensity01(t);
            if (density <= 0.1f) return;   // 거의 빈 도로는 차 생략(덜 어수선)

            int count = Mathf.Clamp(Mathf.CeilToInt(density * MaxCars), 1, MaxCars);
            bool horizontal = IsRoad(tx - 1, ty) || IsRoad(tx + 1, ty);   // 가로 도로 이웃 있으면 가로 흐름
            float speed = Mathf.Lerp(1.6f, 0.25f, density);               // 막힐수록 느림
            float basePhase = Time.time * speed + (tx * 0.13f + ty * 0.31f);

            for (int i = 0; i < count; i++)
            {
                float f = Frac(basePhase + (float)i / count);            // 0..1 축 진행도
                int along = Mathf.Clamp(Mathf.FloorToInt(f * CellPx), 0, CellPx - 1);
                int px = horizontal ? tx * CellPx + along : tx * CellPx + CellPx / 2;
                int py = horizontal ? ty * CellPx + CellPx / 2 : ty * CellPx + along;
                DrawCar(px, py, horizontal, Car);
            }
        }

        private bool IsRoad(int x, int y)
        {
            if (x < 0 || x >= size || y < 0 || y >= size) return false;
            return _data.GetTileType(new Vector2Int(x, y)) == TileType.Road;
        }

        private static float Frac(float v) => v - Mathf.Floor(v);

        private void FillCell(int tx, int ty, int pad, Color32 col)
        {
            int x0 = tx * CellPx + pad, x1 = (tx + 1) * CellPx - pad;
            int y0 = ty * CellPx + pad, y1 = (ty + 1) * CellPx - pad;
            for (int py = y0; py < y1; py++)
                for (int px = x0; px < x1; px++)
                    _px[py * _w + px] = col;
        }

        // 차 = 진행 방향으로 길쭉한 사각(가로 7x3 / 세로 3x7). 크고 길게.
        private void DrawCar(int px, int py, bool horizontal, Color32 col)
        {
            int hx = horizontal ? 3 : 1;   // 반너비
            int hy = horizontal ? 1 : 3;
            for (int j = py - hy; j <= py + hy; j++)
                for (int k = px - hx; k <= px + hx; k++)
                    if (k >= 0 && k < _w && j >= 0 && j < _w) _px[j * _w + k] = col;
        }
    }
}
