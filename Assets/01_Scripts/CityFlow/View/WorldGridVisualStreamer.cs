using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Configs;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using CityFlow.WorldGrid;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityFlow.View
{
    [DisallowMultipleComponent]
    public sealed class WorldGridVisualStreamer :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        private const int MaxInstancesPerBatch = 1023;

        [Header("References")]
        [SerializeField] private WorldGridService worldGrid;
        [SerializeField] private MainCityView cityView;
        [SerializeField] private GameObject fieldTilePrefab;
        [SerializeField] private TerrainDecorationCatalogSO decorationCatalog;

        [Header("Presentation")]
        [SerializeField] private float fieldTileZ = 0.14f;
        [SerializeField] private float decorationGroundZ = 0.1f;
        [SerializeField] private bool frameCameraOnExpansion = true;

        private readonly List<Matrix4x4[]> renderBatches = new();
        private readonly List<DecorationRenderPart> decorationParts = new();
        private readonly Dictionary<GameObject, DecorationRenderSource>
            decorationSources = new();
        private readonly Dictionary<Material, Material> ownedMaterials = new();

        private IWorldGridAccess worldGridAccess;
        private IReadOnlyTileData tileData;
        private ITerrainDecorationSaveSource terrainDecorations;
        private CityFlowServices services;
        private Mesh sourceMesh;
        private Material renderMaterial;
        private Material ownedMaterial;
        private Matrix4x4 sourceRelativeMatrix;
        private Mesh gridLineMesh;
        private MaterialPropertyBlock gridLineProperties;
        private bool initialized;
        private bool visualsDirty;
        private int visibleWidth;
        private int visibleHeight;

        public int RenderedTileCount { get; private set; }
        public int RenderBatchCount => renderBatches.Count;
        public int RenderedDecorationCount { get; private set; }
        public MainCityView CityView => cityView;
        public WorldGridService WorldGrid => worldGrid;
        public GameObject FieldTilePrefab => fieldTilePrefab;
        public TerrainDecorationCatalogSO DecorationCatalog => decorationCatalog;

        private sealed class DecorationRenderSource
        {
            public readonly List<DecorationRenderPart> Parts = new();
        }

        private sealed class DecorationRenderPart
        {
            public Mesh Mesh;
            public int SubMeshIndex;
            public Material Material;
            public Matrix4x4 RelativeMatrix;
            public readonly List<Matrix4x4> Matrices = new();
            public readonly List<Matrix4x4[]> Batches = new();
        }

        public void Initialize(CityFlowServices services)
        {
            if (!isActiveAndEnabled || initialized)
            {
                return;
            }

            if (services == null || worldGrid == null)
            {
                Debug.LogWarning(
                    "[WorldGridVisualStreamer] World grid reference is missing.",
                    this);
                return;
            }

            this.services = services;
            tileData = services.TileData;
            worldGrid.Initialize(services);
            worldGridAccess = services.WorldGrid;
            if (!ReferenceEquals(worldGridAccess, worldGrid) ||
                !TryInstall())
            {
                Debug.LogWarning(
                    "[WorldGridVisualStreamer] Installation failed.",
                    this);
                return;
            }

            worldGridAccess.ChunkUnlocked += OnChunkUnlocked;
            worldGridAccess.AccessRestored += OnAccessRestored;
            services.Events.Placed += OnPlaced;
            services.TerrainDecorationsRegistered +=
                OnTerrainDecorationsRegistered;
            SetTerrainDecorations(services.TerrainDecorations);
            initialized = true;
            visualsDirty = true;
        }

        public bool TryInstall(MainCityView target = null)
        {
            if (target != null)
            {
                if (cityView != null && cityView != target)
                {
                    cityView.CoordinateSpaceChanged -=
                        OnCoordinateSpaceChanged;
                }

                cityView = target;
            }
            else if (cityView == null)
            {
                cityView = FindAnyObjectByType<MainCityView>(
                    FindObjectsInactive.Include);
            }

            if (cityView == null || fieldTilePrefab == null)
            {
                return false;
            }

            if (!cityView.TryConfigureFieldTiles(
                    fieldTilePrefab,
                    fieldTileZ))
            {
                return false;
            }

            MeshFilter sourceFilter =
                fieldTilePrefab.GetComponentInChildren<MeshFilter>(true);
            MeshRenderer sourceRenderer =
                fieldTilePrefab.GetComponentInChildren<MeshRenderer>(true);
            if (sourceFilter == null ||
                sourceFilter.sharedMesh == null ||
                sourceRenderer == null ||
                sourceRenderer.sharedMaterial == null)
            {
                Debug.LogWarning(
                    "[WorldGridVisualStreamer] Field tile render data is missing.",
                    this);
                return false;
            }

            sourceMesh = sourceFilter.sharedMesh;
            renderMaterial = sourceRenderer.sharedMaterial;
            if (!renderMaterial.enableInstancing)
            {
                ownedMaterial = new Material(renderMaterial)
                {
                    name = $"{renderMaterial.name}_WorldGridRuntime",
                    enableInstancing = true
                };
                renderMaterial = ownedMaterial;
            }

            sourceRelativeMatrix =
                fieldTilePrefab.transform.worldToLocalMatrix *
                sourceFilter.transform.localToWorldMatrix;
            gridLineProperties = new MaterialPropertyBlock();
            gridLineProperties.SetColor("_BaseColor", cityView.GridLineColor);
            gridLineProperties.SetColor("_Color", cityView.GridLineColor);
            BuildDecorationSources();
            cityView.CoordinateSpaceChanged -= OnCoordinateSpaceChanged;
            cityView.CoordinateSpaceChanged += OnCoordinateSpaceChanged;
            return true;
        }

        public void RefreshVisuals()
        {
            if (!initialized ||
                worldGridAccess == null ||
                cityView == null ||
                sourceMesh == null ||
                renderMaterial == null)
            {
                return;
            }

            var matrices = new List<Matrix4x4>();
            ClearDecorationMatrices();
            Vector2Int initialOrigin =
                worldGridAccess.InitialPlayableOrigin;
            int baseMinX = initialOrigin.x;
            int baseMinY = initialOrigin.y;
            int minUnlockedX = worldGridAccess.WorldWidth;
            int minUnlockedY = worldGridAccess.WorldHeight;
            int maxUnlockedX = 0;
            int maxUnlockedY = 0;

            for (int chunkY = 0;
                 chunkY < worldGridAccess.ChunkRows;
                 chunkY++)
            {
                for (int chunkX = 0;
                     chunkX < worldGridAccess.ChunkColumns;
                     chunkX++)
                {
                    GridChunkId chunk = new GridChunkId(chunkX, chunkY);
                    if (!worldGridAccess.IsChunkUnlocked(chunk))
                    {
                        continue;
                    }

                    int logicalMinX = chunkX * worldGridAccess.ChunkSize;
                    int logicalMinY = chunkY * worldGridAccess.ChunkSize;
                    int logicalMaxX = Mathf.Min(
                        logicalMinX + worldGridAccess.ChunkSize,
                        worldGridAccess.WorldWidth);
                    int logicalMaxY = Mathf.Min(
                        logicalMinY + worldGridAccess.ChunkSize,
                        worldGridAccess.WorldHeight);
                    minUnlockedX = Mathf.Min(minUnlockedX, logicalMinX);
                    minUnlockedY = Mathf.Min(minUnlockedY, logicalMinY);
                    maxUnlockedX = Mathf.Max(maxUnlockedX, logicalMaxX);
                    maxUnlockedY = Mathf.Max(maxUnlockedY, logicalMaxY);

                    AddChunkMatrices(
                        matrices,
                        logicalMinX,
                        logicalMinY,
                        logicalMaxX,
                        logicalMaxY,
                        baseMinX,
                        baseMinY);
                }
            }

            BuildRenderBatches(matrices);
            BuildDecorationBatches();
            RebuildGridLineMesh(
                minUnlockedX - baseMinX,
                minUnlockedY - baseMinY,
                maxUnlockedX - baseMinX,
                maxUnlockedY - baseMinY);
            RenderedTileCount = matrices.Count;

            int nextVisibleWidth = Mathf.Max(
                cityView.GridWidth,
                maxUnlockedX - minUnlockedX);
            int nextVisibleHeight = Mathf.Max(
                cityView.GridHeight,
                maxUnlockedY - minUnlockedY);
            bool expanded =
                nextVisibleWidth > visibleWidth ||
                nextVisibleHeight > visibleHeight;
            visibleWidth = nextVisibleWidth;
            visibleHeight = nextVisibleHeight;
            cityView.SetVisualGridExtent(
                visibleWidth,
                visibleHeight,
                frameCameraOnExpansion && expanded && RenderedTileCount > 0);
            cityView.SetBaseGridLinesVisible(false);

            visualsDirty = false;
            Debug.Log(
                $"[WorldGridVisualStreamer] Rendering {RenderedTileCount} " +
                $"expanded field tiles and {RenderedDecorationCount} " +
                $"decorations inside {visibleWidth}x{visibleHeight} " +
                $"unlocked bounds.",
                this);
        }

        private void LateUpdate()
        {
            if (visualsDirty)
            {
                RefreshVisuals();
            }

            for (int batchIndex = 0;
                 batchIndex < renderBatches.Count;
                 batchIndex++)
            {
                Matrix4x4[] batch = renderBatches[batchIndex];
                Graphics.DrawMeshInstanced(
                    sourceMesh,
                    0,
                    renderMaterial,
                    batch,
                    batch.Length,
                    null,
                    ShadowCastingMode.Off,
                    receiveShadows: false,
                    cityView.gameObject.layer,
                    camera: null,
                    LightProbeUsage.Off,
                    lightProbeProxyVolume: null);
            }

            DrawDecorationBatches();
            if (gridLineMesh != null &&
                cityView != null &&
                cityView.GridLineMaterial != null)
            {
                Graphics.DrawMesh(
                    gridLineMesh,
                    cityView.transform.localToWorldMatrix,
                    cityView.GridLineMaterial,
                    cityView.gameObject.layer,
                    camera: null,
                    submeshIndex: 0,
                    properties: gridLineProperties,
                    castShadows: false,
                    receiveShadows: false,
                    useLightProbes: false);
            }
        }

        private void OnDestroy()
        {
            if (worldGridAccess != null)
            {
                worldGridAccess.ChunkUnlocked -= OnChunkUnlocked;
                worldGridAccess.AccessRestored -= OnAccessRestored;
            }

            if (services != null)
            {
                services.Events.Placed -= OnPlaced;
                services.TerrainDecorationsRegistered -=
                    OnTerrainDecorationsRegistered;
            }

            SetTerrainDecorations(null);

            if (ownedMaterial != null)
            {
                Destroy(ownedMaterial);
            }

            foreach (Material material in ownedMaterials.Values)
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }

            if (gridLineMesh != null)
            {
                Destroy(gridLineMesh);
            }

            if (cityView != null)
            {
                cityView.CoordinateSpaceChanged -= OnCoordinateSpaceChanged;
                cityView.SetBaseGridLinesVisible(true);
            }
        }

        private void OnCoordinateSpaceChanged()
        {
            visualsDirty = true;
        }

        private void AddChunkMatrices(
            List<Matrix4x4> matrices,
            int logicalMinX,
            int logicalMinY,
            int logicalMaxX,
            int logicalMaxY,
            int baseMinX,
            int baseMinY)
        {
            Matrix4x4 cityLocalToWorld = cityView.transform.localToWorldMatrix;
            int baseMaxX = baseMinX + cityView.GridWidth;
            int baseMaxY = baseMinY + cityView.GridHeight;

            for (int logicalY = logicalMinY;
                 logicalY < logicalMaxY;
                 logicalY++)
            {
                for (int logicalX = logicalMinX;
                     logicalX < logicalMaxX;
                     logicalX++)
                {
                    if (logicalX >= baseMinX && logicalX < baseMaxX &&
                        logicalY >= baseMinY && logicalY < baseMaxY)
                    {
                        continue;
                    }

                    int visualX = logicalX - baseMinX;
                    int visualY = logicalY - baseMinY;
                    Matrix4x4 tileMatrix = Matrix4x4.TRS(
                        new Vector3(
                            (visualX + 0.5f) * cityView.TileSize,
                            (visualY + 0.5f) * cityView.TileSize,
                            fieldTileZ),
                        Quaternion.identity,
                        Vector3.one);
                    matrices.Add(
                        cityLocalToWorld *
                        tileMatrix *
                        sourceRelativeMatrix);
                    AddDecorationMatrices(
                        new Vector2Int(logicalX, logicalY),
                        visualX,
                        visualY,
                        cityLocalToWorld);
                }
            }
        }

        private void BuildRenderBatches(List<Matrix4x4> matrices)
        {
            renderBatches.Clear();
            for (int offset = 0;
                 offset < matrices.Count;
                 offset += MaxInstancesPerBatch)
            {
                int count = Mathf.Min(
                    MaxInstancesPerBatch,
                    matrices.Count - offset);
                var batch = new Matrix4x4[count];
                matrices.CopyTo(offset, batch, 0, count);
                renderBatches.Add(batch);
            }
        }

        private void BuildDecorationSources()
        {
            decorationParts.Clear();
            decorationSources.Clear();
            if (decorationCatalog == null)
            {
                return;
            }

            IReadOnlyList<TerrainDecorationCatalogSO.Entry> entries =
                decorationCatalog.Entries;
            for (int entryIndex = 0;
                 entryIndex < entries.Count;
                 entryIndex++)
            {
                GameObject prefab = entries[entryIndex]?.Prefab;
                if (prefab == null || decorationSources.ContainsKey(prefab))
                {
                    continue;
                }

                var source = new DecorationRenderSource();
                MeshFilter[] filters =
                    prefab.GetComponentsInChildren<MeshFilter>(true);
                for (int filterIndex = 0;
                     filterIndex < filters.Length;
                     filterIndex++)
                {
                    MeshFilter filter = filters[filterIndex];
                    MeshRenderer renderer =
                        filter.GetComponent<MeshRenderer>();
                    Mesh mesh = filter.sharedMesh;
                    if (renderer == null || mesh == null)
                    {
                        continue;
                    }

                    Material[] materials = renderer.sharedMaterials;
                    int subMeshCount = Mathf.Min(
                        mesh.subMeshCount,
                        materials.Length);
                    for (int subMeshIndex = 0;
                         subMeshIndex < subMeshCount;
                         subMeshIndex++)
                    {
                        Material material = materials[subMeshIndex];
                        if (material == null)
                        {
                            continue;
                        }

                        var part = new DecorationRenderPart
                        {
                            Mesh = mesh,
                            SubMeshIndex = subMeshIndex,
                            Material = ResolveInstancedMaterial(material),
                            RelativeMatrix =
                                prefab.transform.worldToLocalMatrix *
                                filter.transform.localToWorldMatrix
                        };
                        source.Parts.Add(part);
                        decorationParts.Add(part);
                    }
                }

                if (source.Parts.Count > 0)
                {
                    decorationSources.Add(prefab, source);
                }
            }
        }

        private Material ResolveInstancedMaterial(Material source)
        {
            if (source.enableInstancing)
            {
                return source;
            }

            if (ownedMaterials.TryGetValue(source, out Material existing))
            {
                return existing;
            }

            var instance = new Material(source)
            {
                name = $"{source.name}_WorldGridRuntime",
                enableInstancing = true
            };
            ownedMaterials.Add(source, instance);
            return instance;
        }

        private void ClearDecorationMatrices()
        {
            RenderedDecorationCount = 0;
            for (int partIndex = 0;
                 partIndex < decorationParts.Count;
                 partIndex++)
            {
                DecorationRenderPart part = decorationParts[partIndex];
                part.Matrices.Clear();
                part.Batches.Clear();
            }
        }

        private void AddDecorationMatrices(
            Vector2Int logicalTile,
            int visualX,
            int visualY,
            Matrix4x4 cityLocalToWorld)
        {
            if (decorationCatalog == null ||
                (tileData != null &&
                 tileData.GetTileType(logicalTile) != TileType.Empty) ||
                (terrainDecorations != null &&
                 terrainDecorations.IsCleared(logicalTile)) ||
                !decorationCatalog.TryCreateSample(
                    logicalTile,
                    cityView.TileSize,
                    out TerrainDecorationSample sample) ||
                !decorationSources.TryGetValue(
                    sample.Prefab,
                    out DecorationRenderSource source))
            {
                return;
            }

            Matrix4x4 decorationMatrix = Matrix4x4.TRS(
                new Vector3(
                    (visualX + 0.5f) * cityView.TileSize + sample.Offset.x,
                    (visualY + 0.5f) * cityView.TileSize + sample.Offset.y,
                    decorationGroundZ),
                Quaternion.Euler(0f, 0f, sample.RotationDegrees),
                Vector3.one * sample.Scale);
            for (int partIndex = 0;
                 partIndex < source.Parts.Count;
                 partIndex++)
            {
                DecorationRenderPart part = source.Parts[partIndex];
                part.Matrices.Add(
                    cityLocalToWorld *
                    decorationMatrix *
                    part.RelativeMatrix);
            }

            RenderedDecorationCount++;
        }

        private void BuildDecorationBatches()
        {
            for (int partIndex = 0;
                 partIndex < decorationParts.Count;
                 partIndex++)
            {
                DecorationRenderPart part = decorationParts[partIndex];
                for (int offset = 0;
                     offset < part.Matrices.Count;
                     offset += MaxInstancesPerBatch)
                {
                    int count = Mathf.Min(
                        MaxInstancesPerBatch,
                        part.Matrices.Count - offset);
                    var batch = new Matrix4x4[count];
                    part.Matrices.CopyTo(offset, batch, 0, count);
                    part.Batches.Add(batch);
                }
            }
        }

        private void DrawDecorationBatches()
        {
            for (int partIndex = 0;
                 partIndex < decorationParts.Count;
                 partIndex++)
            {
                DecorationRenderPart part = decorationParts[partIndex];
                for (int batchIndex = 0;
                     batchIndex < part.Batches.Count;
                     batchIndex++)
                {
                    Matrix4x4[] batch = part.Batches[batchIndex];
                    Graphics.DrawMeshInstanced(
                        part.Mesh,
                        part.SubMeshIndex,
                        part.Material,
                        batch,
                        batch.Length,
                        null,
                        ShadowCastingMode.Off,
                        receiveShadows: false,
                        cityView.gameObject.layer,
                        camera: null,
                        LightProbeUsage.Off,
                        lightProbeProxyVolume: null);
                }
            }
        }

        private void RebuildGridLineMesh(
            int minX,
            int minY,
            int maxX,
            int maxY)
        {
            if (minX >= maxX || minY >= maxY)
            {
                return;
            }

            if (gridLineMesh == null)
            {
                gridLineMesh = new Mesh
                {
                    name = "WorldGridUnlockedLines"
                };
                gridLineMesh.MarkDynamic();
            }
            else
            {
                gridLineMesh.Clear();
            }

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float tileSize = cityView.TileSize;
            float halfThickness = cityView.GridLineThickness * 0.5f;
            float lineZ = fieldTileZ - 0.01f;
            float worldMinX = minX * tileSize;
            float worldMinY = minY * tileSize;
            float worldMaxX = maxX * tileSize;
            float worldMaxY = maxY * tileSize;

            for (int x = minX; x <= maxX; x++)
            {
                float lineX = x * tileSize;
                AddLineQuad(
                    vertices,
                    triangles,
                    lineX - halfThickness,
                    worldMinY,
                    lineX + halfThickness,
                    worldMaxY,
                    lineZ);
            }

            for (int y = minY; y <= maxY; y++)
            {
                float lineY = y * tileSize;
                AddLineQuad(
                    vertices,
                    triangles,
                    worldMinX,
                    lineY - halfThickness,
                    worldMaxX,
                    lineY + halfThickness,
                    lineZ);
            }

            gridLineMesh.SetVertices(vertices);
            gridLineMesh.SetTriangles(triangles, 0);
            gridLineMesh.RecalculateBounds();
        }

        private static void AddLineQuad(
            List<Vector3> vertices,
            List<int> triangles,
            float minX,
            float minY,
            float maxX,
            float maxY,
            float z)
        {
            int firstVertex = vertices.Count;
            vertices.Add(new Vector3(minX, minY, z));
            vertices.Add(new Vector3(minX, maxY, z));
            vertices.Add(new Vector3(maxX, maxY, z));
            vertices.Add(new Vector3(maxX, minY, z));
            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 1);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex + 3);
        }

        private void OnChunkUnlocked(GridChunkId _)
        {
            visualsDirty = true;
        }

        private void OnAccessRestored()
        {
            visualsDirty = true;
        }

        private void OnPlaced(PlacedEvent _)
        {
            visualsDirty = true;
        }

        private void OnTerrainDecorationsRegistered(
            ITerrainDecorationSaveSource source)
        {
            SetTerrainDecorations(source);
            visualsDirty = true;
        }

        private void SetTerrainDecorations(
            ITerrainDecorationSaveSource source)
        {
            if (terrainDecorations != null)
            {
                terrainDecorations.StateChanged -=
                    OnTerrainDecorationStateChanged;
            }

            terrainDecorations = source;
            if (terrainDecorations != null)
            {
                terrainDecorations.StateChanged +=
                    OnTerrainDecorationStateChanged;
            }
        }

        private void OnTerrainDecorationStateChanged()
        {
            visualsDirty = true;
        }
    }
}
