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

            int placed = TilemapBake.Bake(sourceTilemap, services.Placement);

            var tilemapRenderer = sourceTilemap.GetComponent<TilemapRenderer>();
            if (tilemapRenderer != null) tilemapRenderer.enabled = false;   // authoring용, play 땐 숨김

            Debug.Log($"[TilemapCityBaker] {placed}칸 bake 완료 — Tilemap 숨김");
        }
    }
}
