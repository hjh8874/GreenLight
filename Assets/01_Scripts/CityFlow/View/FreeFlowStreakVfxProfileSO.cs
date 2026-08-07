using UnityEngine;

namespace CityFlow.View
{
    [CreateAssetMenu(
        fileName = "FreeFlowStreakVfxProfile",
        menuName = "CityFlow/Traffic/Free Flow Streak VFX Profile")]
    public sealed class FreeFlowStreakVfxProfileSO : ScriptableObject
    {
        [SerializeField] private Color stageOneTint =
            new Color(0.82f, 1f, 0.82f, 1f);
        [SerializeField] private Color stageTwoTint =
            new Color(0.55f, 1f, 0.58f, 1f);
        [SerializeField] private Color stageThreeTint =
            new Color(0.18f, 1f, 0.28f, 1f);
        [SerializeField, Min(0.01f)] private float vfxScale = 0.45f;
        [SerializeField] private GameObject stageTwoPrefab;
        [SerializeField] private GameObject stageThreeGlowPrefab;
        [SerializeField] private GameObject stageThreeStarsPrefab;

        public Color GetTint(int stage)
        {
            switch (Mathf.Clamp(stage, 0, 3))
            {
                case 1:
                    return stageOneTint;
                case 2:
                    return stageTwoTint;
                case 3:
                    return stageThreeTint;
                default:
                    return Color.white;
            }
        }

        public float VfxScale => Mathf.Max(0.01f, vfxScale);
        public GameObject StageTwoPrefab => stageTwoPrefab;
        public GameObject StageThreeGlowPrefab => stageThreeGlowPrefab;
        public GameObject StageThreeStarsPrefab => stageThreeStarsPrefab;
    }
}
