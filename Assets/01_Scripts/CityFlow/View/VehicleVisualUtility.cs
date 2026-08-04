using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityFlow.View
{
    public static class VehicleVisualUtility
    {
        private static readonly Dictionary<Material, Material>
            LitMaterials = new();

        public static void PrepareLit(
            GameObject root,
            int renderQueue =
                (int)RenderQueue.Geometry + 10)
        {
            if (root == null)
            {
                return;
            }

            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Material[] sourceMaterials =
                    renderer.sharedMaterials;
                var litMaterials =
                    new Material[sourceMaterials.Length];

                for (int materialIndex = 0;
                     materialIndex < sourceMaterials.Length;
                     materialIndex++)
                {
                    litMaterials[materialIndex] =
                        GetOrCreateLitMaterial(
                            sourceMaterials[materialIndex],
                            renderQueue);
                }

                renderer.sharedMaterials = litMaterials;
                renderer.enabled = true;
                renderer.forceRenderingOff = false;
                renderer.allowOcclusionWhenDynamic = false;
                renderer.shadowCastingMode =
                    ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
        }

        private static Material GetOrCreateLitMaterial(
            Material source,
            int renderQueue)
        {
            if (source != null &&
                LitMaterials.TryGetValue(
                    source,
                    out Material cached))
            {
                return cached;
            }

            Shader shader =
                Shader.Find(
                    "Universal Render Pipeline/Lit");
            shader ??=
                Shader.Find(
                    "Universal Render Pipeline/Simple Lit");
            shader ??= Shader.Find("Standard");
            shader ??= source != null
                ? source.shader
                : Shader.Find("Hidden/InternalErrorShader");

            var material = new Material(shader)
            {
                name =
                    source != null
                        ? $"{source.name}_Lit"
                        : "VehicleLit",
                renderQueue = renderQueue
            };

            Texture texture = GetMainTexture(source);
            Color color = GetMainColor(source);
            color.a = 1f;

            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", texture);
            }
            if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", texture);
            }
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 0f);
            }
            if (material.HasProperty("_AlphaClip"))
            {
                material.SetFloat("_AlphaClip", 0f);
            }
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat(
                    "_Metallic",
                    source != null &&
                    source.HasProperty("_Metallic")
                        ? source.GetFloat("_Metallic")
                        : 0f);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat(
                    "_Smoothness",
                    source != null &&
                    source.HasProperty("_Smoothness")
                        ? source.GetFloat("_Smoothness")
                        : 0.25f);
            }
            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 1f);
            }
            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat(
                    "_SrcBlend",
                    (float)BlendMode.One);
            }
            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat(
                    "_DstBlend",
                    (float)BlendMode.Zero);
            }

            material.SetOverrideTag(
                "RenderType",
                "Opaque");
            material.DisableKeyword(
                "_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword(
                "_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHATEST_ON");

            if (source != null)
            {
                LitMaterials.Add(source, material);
            }

            return material;
        }

        private static Texture GetMainTexture(
            Material source)
        {
            if (source == null)
            {
                return null;
            }
            if (source.HasProperty("_BaseMap"))
            {
                return source.GetTexture("_BaseMap");
            }
            if (source.HasProperty("_MainTex"))
            {
                return source.GetTexture("_MainTex");
            }
            return source.mainTexture;
        }

        private static Color GetMainColor(
            Material source)
        {
            if (source == null)
            {
                return Color.white;
            }
            if (source.HasProperty("_BaseColor"))
            {
                return source.GetColor("_BaseColor");
            }
            if (source.HasProperty("_Color"))
            {
                return source.GetColor("_Color");
            }
            return source.color;
        }
    }
}
