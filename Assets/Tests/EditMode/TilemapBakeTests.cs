using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using CityFlow.Contracts;
using CityFlow.Authoring;

namespace CityFlow.Authoring.Tests
{
    public class TilemapBakeTests
    {
        // Place 호출을 기록하는 테스트용 배치 서비스(20x20 범위 흉내).
        sealed class RecordingPlacement : IPlacementService
        {
            public readonly Dictionary<Vector2Int, TileType> Placed = new Dictionary<Vector2Int, TileType>();
            readonly int w, h;
            public RecordingPlacement(int w, int h) { this.w = w; this.h = h; }
            public bool CanPlace(Vector2Int t, TileType type) =>
                t.x >= 0 && t.x < w && t.y >= 0 && t.y < h && type != TileType.Empty;
            public bool Place(Vector2Int t, TileType type)
            {
                if (!CanPlace(t, type)) return false;
                Placed[t] = type; return true;
            }
            public bool Remove(Vector2Int t) => Placed.Remove(t);
        }

        static CityTile MakeTile(TileType type)
        {
            var t = ScriptableObject.CreateInstance<CityTile>();
            t.type = type; return t;
        }

        static (Tilemap map, GameObject root) NewTilemap()
        {
            var root = new GameObject("grid", typeof(Grid));
            var child = new GameObject("tilemap", typeof(Tilemap));
            child.transform.SetParent(root.transform);
            return (child.GetComponent<Tilemap>(), root);
        }

        [Test]
        public void Bake_PlacesPaintedCityTiles_ByTypeAndCoord()
        {
            var (map, root) = NewTilemap();
            map.SetTile(new Vector3Int(1, 2, 0), MakeTile(TileType.Road));
            map.SetTile(new Vector3Int(3, 4, 0), MakeTile(TileType.House));
            var rec = new RecordingPlacement(20, 20);

            var result = TilemapBake.Bake(map, rec);

            Assert.AreEqual(2, result.Placed);
            Assert.AreEqual(0, result.Skipped);
            Assert.AreEqual(TileType.Road, rec.Placed[new Vector2Int(1, 2)]);   // 셀=그리드 좌표
            Assert.AreEqual(TileType.House, rec.Placed[new Vector2Int(3, 4)]);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void Bake_SkipsNonCityTiles_AndOutOfBounds()
        {
            var (map, root) = NewTilemap();
            map.SetTile(new Vector3Int(0, 0, 0), ScriptableObject.CreateInstance<Tile>());  // 비-CityTile
            map.SetTile(new Vector3Int(99, 99, 0), MakeTile(TileType.Road));                // 범위 밖(20x20)
            map.SetTile(new Vector3Int(5, 5, 0), MakeTile(TileType.Office));                // 유효

            var rec = new RecordingPlacement(20, 20);

            var result = TilemapBake.Bake(map, rec);

            Assert.AreEqual(1, result.Placed);
            Assert.AreEqual(1, result.Skipped);   // 격자 밖 CityTile 1개(99,99)가 스킵으로 집계
            Assert.AreEqual(TileType.Office, rec.Placed[new Vector2Int(5, 5)]);
            Assert.IsFalse(rec.Placed.ContainsKey(new Vector2Int(99, 99)));
            Object.DestroyImmediate(root);
        }
    }
}
