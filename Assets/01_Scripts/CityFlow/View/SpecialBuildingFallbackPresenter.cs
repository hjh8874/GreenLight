using CityFlow.Content;
using UnityEngine;

namespace CityFlow.View
{
    [DisallowMultipleComponent]
    public sealed class SpecialBuildingFallbackPresenter : MonoBehaviour
    {
        private static readonly int BaseColorId =
            Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId =
            Shader.PropertyToID("_Color");

        [SerializeField]
        private Material sharedMaterial;

        [SerializeField, Range(0f, 0.45f)]
        private float footprintInset = 0.12f;

        private bool configured;

        public void Configure(
            BuildingDefinitionSO definition,
            float tileSize)
        {
            if (configured || definition == null)
            {
                return;
            }

            configured = true;
            gameObject.name = $"Fallback_{definition.buildingId}";

            Vector2Int footprint = definition.Footprint;
            float width = Mathf.Max(
                0.1f,
                footprint.x * tileSize * (1f - footprintInset));
            float depth = Mathf.Max(
                0.1f,
                footprint.y * tileSize * (1f - footprintInset));
            float height = definition.FallbackHeight * tileSize;
            Color bodyColor = definition.FallbackColor;

            CreatePart(
                "Body",
                new Vector3(width, depth, height),
                new Vector3(0f, 0f, -height * 0.5f),
                bodyColor);

            float roofHeight = Mathf.Max(0.06f, tileSize * 0.08f);
            CreatePart(
                "Roof",
                new Vector3(width * 1.06f, depth * 1.06f, roofHeight),
                new Vector3(0f, 0f, -height - roofHeight * 0.5f),
                Color.Lerp(bodyColor, Color.white, 0.3f));

            float markerHeight = Mathf.Max(0.08f, height * 0.16f);
            CreatePart(
                "FrontMarker",
                new Vector3(width * 0.42f, tileSize * 0.06f, markerHeight),
                new Vector3(
                    0f,
                    -depth * 0.5f - tileSize * 0.031f,
                    -height * 0.5f),
                Color.Lerp(bodyColor, Color.white, 0.65f));
        }

        private void CreatePart(
            string partName,
            Vector3 scale,
            Vector3 position,
            Color color)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            part.transform.SetParent(transform, false);
            part.transform.localPosition = position;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = scale;

            Collider partCollider = part.GetComponent<Collider>();
            if (partCollider != null)
            {
                partCollider.enabled = false;
                DestroyUnityObject(partCollider);
            }

            Renderer partRenderer = part.GetComponent<Renderer>();
            if (partRenderer == null)
            {
                return;
            }

            partRenderer.sharedMaterial = sharedMaterial;
            partRenderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            partRenderer.receiveShadows = true;

            var properties = new MaterialPropertyBlock();
            properties.SetColor(BaseColorId, color);
            properties.SetColor(ColorId, color);
            partRenderer.SetPropertyBlock(properties);
        }

        private static void DestroyUnityObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}

// Unity setup: This component is prewired in SpecialBuildingFallback.prefab.
