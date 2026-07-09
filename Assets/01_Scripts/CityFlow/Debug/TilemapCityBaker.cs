using UnityEngine;
using UnityEngine.Tilemaps;
using CityFlow.Bootstrap;
using CityFlow.Authoring;

namespace CityFlow.DebugTools
{
    // Tilemap에 붓칠한 도시를 게임 시작 시 엔진에 굽는다. DebugCitySeeder(하드코딩) 교체.
    // bake 후 Tilemap은 숨김 — SimTileRenderer가 실제 뷰라 이중 렌더 방지(authoring 전용).
    public sealed class TilemapCityBaker : MonoBehaviour, ICityFlowServiceConsumer
    {
        [SerializeField] private Tilemap sourceTilemap;

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled || sourceTilemap == null) return;

            var result = TilemapBake.Bake(sourceTilemap, services.Placement);

            var tilemapRenderer = sourceTilemap.GetComponent<TilemapRenderer>();
            if (tilemapRenderer != null) tilemapRenderer.enabled = false;   // authoring용, play 땐 숨김

            Debug.Log($"[TilemapCityBaker] {result.Placed}칸 bake 완료 — Tilemap 숨김");
            if (result.Skipped > 0)   // 격자 밖/중복은 조용히 사라지지 않게 경고
                Debug.LogWarning($"[TilemapCityBaker] {result.Skipped}칸이 격자 밖(0~크기 범위)이거나 이미 찬 칸이라 스킵됨 — 붓칠 위치 확인");
        }
    }
}
