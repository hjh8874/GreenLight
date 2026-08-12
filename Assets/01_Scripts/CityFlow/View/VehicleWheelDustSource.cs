using UnityEngine;

namespace CityFlow.View
{
    [DisallowMultipleComponent]
    public sealed class VehicleWheelDustSource : MonoBehaviour
    {
        [SerializeField] private Material particleMaterial;
        [SerializeField, Min(0.01f)] private float emissionDistance = 0.14f;
        [SerializeField, Min(0f)] private float movementThreshold = 0.01f;
        [SerializeField, Min(0.1f)] private float teleportDistance = 1.5f;

        private const float RoadSurfaceClearance = 0.01f;

        private Bounds localBounds;
        private Vector3 previousPosition;
        private float accumulatedDistance;
        private bool hasPreviousPosition;

        public Material ParticleMaterial => particleMaterial;

        public void Configure(Material material)
        {
            particleMaterial = material;
        }

        private void Awake()
        {
            localBounds = CalculateLocalRendererBounds();
        }

        private void OnEnable()
        {
            ResetTracking();
        }

        private void OnDisable()
        {
            ResetTracking();
        }

        private void LateUpdate()
        {
            Vector3 currentPosition = transform.position;
            if (!hasPreviousPosition)
            {
                previousPosition = currentPosition;
                hasPreviousPosition = true;
                return;
            }

            float movedDistance =
                Vector3.Distance(previousPosition, currentPosition);
            previousPosition = currentPosition;

            if (movedDistance > teleportDistance)
            {
                accumulatedDistance = 0f;
                return;
            }

            if (movedDistance < movementThreshold)
            {
                accumulatedDistance = 0f;
                return;
            }

            accumulatedDistance += movedDistance;
            if (accumulatedDistance < emissionDistance)
            {
                return;
            }

            int burstCount = Mathf.Min(
                2,
                Mathf.FloorToInt(
                    accumulatedDistance / emissionDistance));
            accumulatedDistance %= emissionDistance;

            VehicleWheelDustSystem system =
                VehicleWheelDustSystem.GetOrCreate(
                    particleMaterial,
                    gameObject.scene);
            if (system == null)
            {
                return;
            }

            float speed = movedDistance /
                Mathf.Max(Time.deltaTime, 0.0001f);
            float intensity = Mathf.InverseLerp(0.15f, 1.25f, speed);
            Vector3 left;
            Vector3 right;
            GetRearWheelPositions(out left, out right);

            for (int i = 0; i < burstCount; i++)
            {
                system.Emit(left, intensity);
                system.Emit(right, intensity);
            }
        }

        internal void GetRearWheelPositions(
            out Vector3 left,
            out Vector3 right)
        {
            float rearX =
                localBounds.min.x + localBounds.size.x * 0.14f;
            float halfTrack = localBounds.size.y * 0.3f;
            float surfaceZ =
                localBounds.max.z - RoadSurfaceClearance;

            left = transform.TransformPoint(
                new Vector3(rearX, halfTrack, surfaceZ));
            right = transform.TransformPoint(
                new Vector3(rearX, -halfTrack, surfaceZ));
        }

        private Bounds CalculateLocalRendererBounds()
        {
            Renderer[] renderers =
                GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            Bounds result = default;

            for (int rendererIndex = 0;
                 rendererIndex < renderers.Length;
                 rendererIndex++)
            {
                Renderer renderer = renderers[rendererIndex];
                Bounds rendererBounds = renderer.localBounds;
                Vector3 min = rendererBounds.min;
                Vector3 max = rendererBounds.max;

                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 rendererLocalPoint = new(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    Vector3 localPoint =
                        transform.InverseTransformPoint(
                            renderer.transform.TransformPoint(
                                rendererLocalPoint));

                    if (!hasBounds)
                    {
                        result = new Bounds(
                            localPoint,
                            Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        result.Encapsulate(localPoint);
                    }
                }
            }

            return hasBounds
                ? result
                : new Bounds(Vector3.zero, Vector3.one);
        }

        private void ResetTracking()
        {
            previousPosition = transform.position;
            accumulatedDistance = 0f;
            hasPreviousPosition = false;
        }
    }
}
