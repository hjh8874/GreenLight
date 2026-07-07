using UnityEngine;

namespace CityFlow.TilemapTest
{
    public sealed class TilemapTestVehicleLaneMotion : MonoBehaviour
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField] private float laneOffset = 0.22f;
        [SerializeField] private float travelDistance = 0.7f;
        [SerializeField] private float speed = 0.75f;

        private static int nextMotionIndex;

        private int laneDirection;
        private float phaseOffset;

        public Transform VisualRoot { get; private set; }

        private void Awake()
        {
            VisualRoot = visualRoot != null ? visualRoot : FindFallbackVisual();

            int motionIndex = nextMotionIndex++;
            laneDirection = motionIndex % 2 == 0 ? 1 : -1;
            phaseOffset = Mathf.Repeat(motionIndex * 0.217f, 1f);
        }

        private void Update()
        {
            if (VisualRoot == null)
            {
                return;
            }

            float t = Mathf.Repeat((Time.time * speed) + phaseOffset, 1f);
            float halfDistance = travelDistance * 0.5f;
            float z = laneDirection > 0
                ? Mathf.Lerp(-halfDistance, halfDistance, t)
                : Mathf.Lerp(halfDistance, -halfDistance, t);

            VisualRoot.localPosition = new Vector3(laneDirection * laneOffset, 0f, z);
        }

        private Transform FindFallbackVisual()
        {
            if (transform.childCount > 0)
            {
                return transform.GetChild(0);
            }

            return transform;
        }
    }
}

/*
Unity implementation:
1. Put this on the root of a small test vehicle prefab.
2. Assign a child cube as VisualRoot so VehicleDensityView can position the root while this script moves the visual.
3. The visual alternates between left and right lanes to create a simple two-lane traffic motion.
*/
