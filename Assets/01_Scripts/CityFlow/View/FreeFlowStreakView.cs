using CityFlow.Sim;
using UnityEngine;

namespace CityFlow.View
{
    public sealed class FreeFlowStreakView : MonoBehaviour
    {
        private const string ProfileResourcePath =
            "CityFlow/FreeFlowStreakVfxProfile";
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private FreeFlowStreakVfxProfileSO profile;
        private Renderer[] renderers;
        private MaterialPropertyBlock propertyBlock;
        private GameObject stageTwoVfx;
        private GameObject stageThreeGlowVfx;
        private GameObject stageThreeStarsVfx;
        private int appliedStage = -1;
        // 원본 색 캐시. 0단계 복원용 — 없으면 CarStyle 팔레트가 흰색으로 파괴된다.
        private bool originalColorsCached;
        private Color[] originalBaseColors;
        private Color[] originalColors;
        private bool[] hasBaseColor;
        private bool[] hasColor;

        private void Awake()
        {
            profile = Resources.Load<FreeFlowStreakVfxProfileSO>(
                ProfileResourcePath);
            renderers = GetComponentsInChildren<Renderer>(true);
            propertyBlock = new MaterialPropertyBlock();
        }

        internal void ApplySnapshot(CarSnapshot snapshot)
        {
            ApplyStage(snapshot.FreeFlowStreak);
        }

        private void ApplyStage(int stage)
        {
            int sanitizedStage = Mathf.Clamp(stage, 0, 3);
            if (sanitizedStage == appliedStage)
            {
                return;
            }

            appliedStage = sanitizedStage;
            ApplyTint(sanitizedStage);
            EnsureVfxObjects();

            if (stageTwoVfx != null)
            {
                stageTwoVfx.SetActive(sanitizedStage >= 2);
            }

            bool showStageThree = sanitizedStage >= 3;
            if (stageThreeGlowVfx != null)
            {
                stageThreeGlowVfx.SetActive(showStageThree);
            }

            if (stageThreeStarsVfx != null)
            {
                stageThreeStarsVfx.SetActive(showStageThree);
            }
        }

        // 0단계는 "연출 없음"이다. 흰색으로 덮는 게 아니라 원래 색으로 되돌려야 한다.
        // 첫 적용 직전의 렌더러별 색을 한 번만 캐시해 두고, 0단계에서 그대로 복원한다.
        // (캐시가 없으면 CarStyle 팔레트가 흰색으로 파괴되고 복구 경로가 사라진다.)
        private void CacheOriginalColorsOnce()
        {
            if (originalColorsCached || renderers == null)
            {
                return;
            }

            originalColorsCached = true;
            originalBaseColors = new Color[renderers.Length];
            originalColors = new Color[renderers.Length];
            hasBaseColor = new bool[renderers.Length];
            hasColor = new bool[renderers.Length];

            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                Material material = renderer.sharedMaterial;
                if (material == null)
                {
                    continue;
                }

                // 이미 다른 시스템(CarStyle 등)이 블록을 칠해 뒀으면 그 값이 원본이다.
                renderer.GetPropertyBlock(propertyBlock);
                if (material.HasProperty(BaseColorId))
                {
                    hasBaseColor[index] = true;
                    originalBaseColors[index] = propertyBlock.HasColor(BaseColorId)
                        ? propertyBlock.GetColor(BaseColorId)
                        : material.GetColor(BaseColorId);
                }

                if (material.HasProperty(ColorId))
                {
                    hasColor[index] = true;
                    originalColors[index] = propertyBlock.HasColor(ColorId)
                        ? propertyBlock.GetColor(ColorId)
                        : material.GetColor(ColorId);
                }
            }
        }

        private void ApplyTint(int stage)
        {
            if (renderers == null)
            {
                return;
            }

            CacheOriginalColorsOnce();
            bool restore = stage <= 0;
            Color tint = profile != null ? profile.GetTint(stage) : Color.white;

            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(propertyBlock);
                if (hasBaseColor[index])
                {
                    propertyBlock.SetColor(
                        BaseColorId,
                        restore ? originalBaseColors[index] : tint);
                }

                if (hasColor[index])
                {
                    propertyBlock.SetColor(
                        ColorId,
                        restore ? originalColors[index] : tint);
                }

                renderer.SetPropertyBlock(propertyBlock);
            }
        }

        private void EnsureVfxObjects()
        {
            if (profile == null)
            {
                return;
            }

            if (stageTwoVfx == null && profile.StageTwoPrefab != null)
            {
                stageTwoVfx = InstantiateVfx(
                    profile.StageTwoPrefab,
                    "FreeFlowStreakStage2");
            }

            if (stageThreeGlowVfx == null &&
                profile.StageThreeGlowPrefab != null)
            {
                stageThreeGlowVfx = InstantiateVfx(
                    profile.StageThreeGlowPrefab,
                    "FreeFlowStreakStage3Glow");
            }

            if (stageThreeStarsVfx == null &&
                profile.StageThreeStarsPrefab != null)
            {
                stageThreeStarsVfx = InstantiateVfx(
                    profile.StageThreeStarsPrefab,
                    "FreeFlowStreakStage3Stars");
            }
        }

        private GameObject InstantiateVfx(GameObject prefab, string objectName)
        {
            GameObject instance = Instantiate(prefab, transform, false);
            instance.name = objectName;
            instance.transform.localScale =
                Vector3.one * profile.VfxScale;
            instance.SetActive(false);
            return instance;
        }
    }
}
