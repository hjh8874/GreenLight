using System.Text;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.View
{
    public sealed partial class MainCityView
    {
        public void LogSchoolVisualDiagnostics(Vector2Int selectedTile)
        {
            var report = new StringBuilder(1024);
            report.AppendLine("[SchoolVisualDiagnostics] School visual inspection");
            report.AppendLine($"Selected tile: {selectedTile}");

            if (tileData == null)
            {
                report.AppendLine("Result: FAILED - IReadOnlyTileData is not initialized.");
                Debug.LogWarning(report.ToString(), this);
                return;
            }

            Vector2Int anchor = selectedTile;
            bool hasAnchor = tileData.TryGetFootprintAnchor(
                selectedTile,
                out Vector2Int resolvedAnchor);
            if (hasAnchor)
            {
                anchor = resolvedAnchor;
            }

            TileType selectedType = tileData.GetTileType(selectedTile);
            TileType anchorType = tileData.GetTileType(anchor);
            BuildingVisualCatalogSO catalog = ResolveBuildingVisualCatalog();
            GameObject catalogPrefab = catalog != null
                ? catalog.SchoolPrefab
                : null;
            GameObject resolvedPrefab = GetPrefab(TileType.School);

            report.AppendLine(
                $"Tile data: selectedType={selectedType}, anchor={anchor}, " +
                $"anchorResolved={hasAnchor}, anchorType={anchorType}");
            report.AppendLine(
                $"Catalog: instance={DescribeObject(catalog)}, " +
                $"assetPath={GetEditorAssetPath(catalog)}");
            report.AppendLine(
                $"Catalog school prefab: instance={DescribeObject(catalogPrefab)}, " +
                $"assetPath={GetEditorAssetPath(catalogPrefab)}");
            report.AppendLine(
                $"Fallback school prefab: instance={DescribeObject(schoolPrefab)}, " +
                $"assetPath={GetEditorAssetPath(schoolPrefab)}");
            report.AppendLine(
                $"Resolved school prefab: instance={DescribeObject(resolvedPrefab)}, " +
                $"assetPath={GetEditorAssetPath(resolvedPrefab)}");

            string failureReason = null;
            if (selectedType != TileType.School && anchorType != TileType.School)
            {
                failureReason =
                    "the selected footprint is not registered as TileType.School";
            }
            else if (resolvedPrefab == null)
            {
                failureReason =
                    "neither BuildingVisualCatalog nor MainCityView has a school prefab";
            }

            if (!tileVisuals.TryGetValue(anchor, out TileVisual visual))
            {
                report.AppendLine(
                    "Runtime visual: MISSING - no TileVisual exists at the school anchor.");
                failureReason ??=
                    "the school TileVisual was not created or its unlocked chunk is not streamed";
                AppendSchoolVisualResult(report, failureReason);
                Debug.LogWarning(report.ToString(), this);
                return;
            }

            GameObject root = visual.Object;
            report.AppendLine(
                $"Runtime TileVisual: type={visual.Type}, " +
                $"authoredMaterial={visual.UsesAuthoredMaterial}, " +
                $"root={DescribeObject(root)}");

            if (visual.Type != TileType.School)
            {
                failureReason ??=
                    $"the cached visual type is {visual.Type} instead of School";
            }
            else if (resolvedPrefab != null && !visual.UsesAuthoredMaterial)
            {
                failureReason ??=
                    "the prefab resolves now, but this TileVisual was created with the fallback model; rebuild the city visuals";
            }

            if (root == null)
            {
                failureReason ??= "the cached TileVisual root was destroyed";
                AppendSchoolVisualResult(report, failureReason);
                Debug.LogWarning(report.ToString(), this);
                return;
            }

            report.AppendLine(
                $"Root state: activeSelf={root.activeSelf}, " +
                $"activeInHierarchy={root.activeInHierarchy}, " +
                $"localPosition={root.transform.localPosition}, " +
                $"worldPosition={root.transform.position}, " +
                $"localRotation={root.transform.localEulerAngles}, " +
                $"localScale={root.transform.localScale}");

            Transform body = root.transform.Find("BuildingBody");
            report.AppendLine(
                body != null
                    ? $"BuildingBody: found, activeSelf={body.gameObject.activeSelf}, " +
                      $"activeInHierarchy={body.gameObject.activeInHierarchy}, " +
                      $"localPosition={body.localPosition}, " +
                      $"worldPosition={body.position}, " +
                      $"localRotation={body.localEulerAngles}, " +
                      $"localScale={body.localScale}"
                    : "BuildingBody: MISSING");

            if (!root.activeInHierarchy)
            {
                failureReason ??= "the school visual root is inactive in the hierarchy";
            }
            else if (body == null)
            {
                failureReason ??= "the generated school visual has no BuildingBody child";
            }

            Renderer[] renderers = body != null
                ? body.GetComponentsInChildren<Renderer>(true)
                : new Renderer[0];
            report.AppendLine($"BuildingBody renderers: count={renderers.Length}");

            bool hasRenderableRenderer = false;
            bool hasMissingMaterial = false;
            bool hasUnsupportedShader = false;
            Bounds combinedBounds = default;
            bool hasBounds = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                bool isRenderable = renderer.enabled &&
                                    !renderer.forceRenderingOff &&
                                    renderer.gameObject.activeInHierarchy;
                hasRenderableRenderer |= isRenderable;

                Bounds bounds = renderer.bounds;
                if (!hasBounds)
                {
                    combinedBounds = bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(bounds);
                }

                var materials = new StringBuilder();
                Material[] sharedMaterials = renderer.sharedMaterials;
                for (int materialIndex = 0;
                     materialIndex < sharedMaterials.Length;
                     materialIndex++)
                {
                    if (materialIndex > 0)
                    {
                        materials.Append(", ");
                    }

                    Material material = sharedMaterials[materialIndex];
                    if (material == null)
                    {
                        hasMissingMaterial = true;
                        materials.Append("<missing>");
                        continue;
                    }

                    Shader shader = material.shader;
                    bool shaderSupported = shader != null && shader.isSupported;
                    hasUnsupportedShader |= !shaderSupported;
                    materials.Append(material.name)
                        .Append(" [")
                        .Append(shader != null ? shader.name : "missing shader")
                        .Append(", supported=")
                        .Append(shaderSupported)
                        .Append(']');
                }

                report.AppendLine(
                    $"Renderer[{i}]: name={renderer.name}, " +
                    $"type={renderer.GetType().Name}, enabled={renderer.enabled}, " +
                    $"forceOff={renderer.forceRenderingOff}, " +
                    $"active={renderer.gameObject.activeInHierarchy}, " +
                    $"boundsCenter={bounds.center}, boundsSize={bounds.size}, " +
                    $"materials={materials}");
            }

            if (renderers.Length == 0)
            {
                failureReason ??=
                    "the runtime school BuildingBody contains no Renderer";
            }
            else if (!hasRenderableRenderer)
            {
                failureReason ??=
                    "all school renderers are disabled, forced off, or inactive";
            }
            else if (hasMissingMaterial)
            {
                failureReason ??= "one or more school renderer materials are missing";
            }
            else if (hasUnsupportedShader)
            {
                failureReason ??=
                    "one or more school materials use a missing or unsupported shader";
            }
            else if (!hasBounds || combinedBounds.size.sqrMagnitude < 0.0001f)
            {
                failureReason ??= "the school renderer bounds are effectively zero";
            }

            if (hasBounds)
            {
                report.AppendLine(
                    $"Combined renderer bounds: center={combinedBounds.center}, " +
                    $"size={combinedBounds.size}");
            }

            AppendSchoolVisualResult(report, failureReason);
            if (failureReason == null)
            {
                Debug.Log(report.ToString(), root);
            }
            else
            {
                Debug.LogWarning(report.ToString(), root);
            }
        }

        private static void AppendSchoolVisualResult(
            StringBuilder report,
            string failureReason)
        {
            report.AppendLine(
                failureReason == null
                    ? "Result: Prefab and live renderers are connected. " +
                      "If the model is still invisible, compare the reported transform " +
                      "and bounds with the camera and ground surface."
                    : $"Result: SUSPECTED CAUSE - {failureReason}.");
        }

        private static string DescribeObject(Object target)
        {
            return target != null
                ? target.name
                : "<null>";
        }

        private static string GetEditorAssetPath(Object target)
        {
#if UNITY_EDITOR
            return target != null
                ? UnityEditor.AssetDatabase.GetAssetPath(target)
                : "<none>";
#else
            return target != null ? "<editor only>" : "<none>";
#endif
        }

        // Unity setup: no extra component is required. Selecting a school calls
        // this diagnostic through TileSelectionController in Play Mode.
    }
}
