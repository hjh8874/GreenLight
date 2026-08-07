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
            Color tint = profile != null
                ? profile.GetTint(sanitizedStage)
                : Color.white;
            ApplyTint(tint);
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

        private void ApplyTint(Color tint)
        {
            if (renderers == null)
            {
                return;
            }

            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetPropertyBlock(propertyBlock);
                Material material = renderer.sharedMaterial;
                if (material != null && material.HasProperty(BaseColorId))
                {
                    propertyBlock.SetColor(BaseColorId, tint);
                }

                if (material != null && material.HasProperty(ColorId))
                {
                    propertyBlock.SetColor(ColorId, tint);
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
