using UnityEngine;
using UnityEngine.Tilemaps;
using CityFlow.Contracts;

namespace CityFlow.Authoring
{
    // 붓칠 authoring용 타일. 자기 TileType을 들고 있어 bake가 별도 매핑 없이 타입을 읽는다.
    [CreateAssetMenu(fileName = "CityTile", menuName = "CityFlow/Authoring/City Tile")]
    public sealed class CityTile : Tile
    {
        public TileType type = TileType.Road;
    }
}
