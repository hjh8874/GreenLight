using System;
using UnityEngine;

namespace CityFlow.WorldGrid
{
    [Serializable]
    public sealed class WorldGridUnlockStage
    {
        [SerializeField] private string stageId = "center_020";
        [SerializeField, Min(1)] private int unlockedWidth = 20;
        [SerializeField, Min(1)] private int unlockedHeight = 20;

        public string StageId => stageId;
        public int UnlockedWidth => Mathf.Max(1, unlockedWidth);
        public int UnlockedHeight => Mathf.Max(1, unlockedHeight);
    }
}
