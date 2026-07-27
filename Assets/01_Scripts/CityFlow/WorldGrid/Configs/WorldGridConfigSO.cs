using UnityEngine;

namespace CityFlow.WorldGrid
{
    [CreateAssetMenu(
        fileName = "WorldGridConfig",
        menuName = "CityFlow/World Grid Config")]
    public sealed class WorldGridConfigSO : ScriptableObject
    {
        [Header("Logical World")]
        [SerializeField, Min(1)] private int worldWidth = 200;
        [SerializeField, Min(1)] private int worldHeight = 200;
        [SerializeField, Min(1)] private int chunkSize = 20;

        [Header("Initial Access")]
        [SerializeField, Min(1)] private int initialUnlockedColumns = 1;
        [SerializeField, Min(1)] private int initialUnlockedRows = 1;

        public int WorldWidth => Mathf.Max(1, worldWidth);
        public int WorldHeight => Mathf.Max(1, worldHeight);
        public int ChunkSize => Mathf.Max(1, chunkSize);
        public int ChunkColumns => Mathf.CeilToInt(WorldWidth / (float)ChunkSize);
        public int ChunkRows => Mathf.CeilToInt(WorldHeight / (float)ChunkSize);
        public int InitialUnlockedColumns =>
            Mathf.Clamp(initialUnlockedColumns, 1, ChunkColumns);
        public int InitialUnlockedRows =>
            Mathf.Clamp(initialUnlockedRows, 1, ChunkRows);

        private void OnValidate()
        {
            worldWidth = Mathf.Max(1, worldWidth);
            worldHeight = Mathf.Max(1, worldHeight);
            chunkSize = Mathf.Max(1, chunkSize);
            initialUnlockedColumns = Mathf.Clamp(
                initialUnlockedColumns,
                1,
                ChunkColumns);
            initialUnlockedRows = Mathf.Clamp(
                initialUnlockedRows,
                1,
                ChunkRows);
        }
    }
}
