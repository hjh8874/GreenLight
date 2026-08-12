using UnityEngine;

namespace CityFlow.View
{
    [DisallowMultipleComponent]
    public sealed class FreeFlowStreakStageThreePresentation : MonoBehaviour
    {
        private const float RearInset = 0.02f;
        private const float RoadClearance = 0.01f;
        private const float LabelHeightRatio = 0.2f;

        [SerializeField] private TrailRenderer trail;
        [SerializeField] private TextMesh stageLabel;

        internal TrailRenderer Trail => trail;
        internal TextMesh StageLabel => stageLabel;

        private void Awake()
        {
            ResolveComponents();
        }

        private void OnEnable()
        {
            ActivatePresentation();
        }

        private void OnDisable()
        {
            DeactivatePresentation();
        }

        internal void ActivatePresentation()
        {
            ResolveComponents();
            RefreshLayout();

            if (trail != null)
            {
                trail.Clear();
                trail.emitting = true;
            }

            if (stageLabel != null)
            {
                stageLabel.gameObject.SetActive(true);
            }
        }

        internal void DeactivatePresentation()
        {
            if (trail != null)
            {
                trail.emitting = false;
                trail.Clear();
            }

            if (stageLabel != null)
            {
                stageLabel.gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            BillboardStageLabel(Camera.main);
        }

        internal void RefreshLayout()
        {
            ResolveComponents();
            Transform vehicleRoot = transform.parent;
            if (vehicleRoot == null ||
                !TryGetVehicleLocalBounds(vehicleRoot, out Bounds bounds))
            {
                return;
            }

            if (trail != null)
            {
                Vector3 trailLocalPosition = new(
                    bounds.min.x + RearInset,
                    bounds.center.y,
                    bounds.max.z - RoadClearance);
                trail.transform.position =
                    vehicleRoot.TransformPoint(trailLocalPosition);

                float vehicleWorldWidth = vehicleRoot
                    .TransformVector(Vector3.up * bounds.size.y)
                    .magnitude;
                trail.widthMultiplier = Mathf.Clamp(
                    vehicleWorldWidth * 0.18f,
                    0.015f,
                    0.06f);
            }

            if (stageLabel != null)
            {
                float labelClearance = Mathf.Max(
                    0.08f,
                    bounds.size.z * LabelHeightRatio);
                Vector3 labelLocalPosition = new(
                    bounds.center.x,
                    bounds.center.y,
                    bounds.min.z - labelClearance);
                stageLabel.transform.position =
                    vehicleRoot.TransformPoint(labelLocalPosition);

                float wrapperScale = Mathf.Max(
                    0.01f,
                    Mathf.Abs(transform.localScale.x));
                stageLabel.characterSize = Mathf.Max(
                    0.18f,
                    bounds.size.x * 0.25f / wrapperScale);
            }
        }

        internal void BillboardStageLabel(Camera targetCamera)
        {
            if (stageLabel == null || targetCamera == null)
            {
                return;
            }

            Vector3 toCamera =
                targetCamera.transform.position -
                stageLabel.transform.position;
            if (toCamera.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            stageLabel.transform.rotation = Quaternion.LookRotation(
                -toCamera.normalized,
                targetCamera.transform.up);
        }

        private void ResolveComponents()
        {
            if (trail == null)
            {
                Transform trailTransform =
                    transform.Find("Stage3Trail");
                trail = trailTransform != null
                    ? trailTransform.GetComponent<TrailRenderer>()
                    : GetComponentInChildren<TrailRenderer>(true);
            }

            if (stageLabel == null)
            {
                Transform labelTransform =
                    transform.Find("Stage3Number");
                stageLabel = labelTransform != null
                    ? labelTransform.GetComponent<TextMesh>()
                    : GetComponentInChildren<TextMesh>(true);
            }
        }

        private bool TryGetVehicleLocalBounds(
            Transform vehicleRoot,
            out Bounds localBounds)
        {
            bool initialized = false;
            Vector3 minimum = default;
            Vector3 maximum = default;

            Collider[] colliders =
                vehicleRoot.GetComponentsInChildren<Collider>(true);
            for (int index = 0; index < colliders.Length; index++)
            {
                Collider collider = colliders[index];
                if (collider == null ||
                    collider.transform.IsChildOf(transform))
                {
                    continue;
                }

                EncapsulateWorldBounds(
                    vehicleRoot,
                    collider.bounds,
                    ref initialized,
                    ref minimum,
                    ref maximum);
            }

            if (!initialized)
            {
                Renderer[] vehicleRenderers =
                    vehicleRoot.GetComponentsInChildren<Renderer>(true);
                for (int index = 0;
                     index < vehicleRenderers.Length;
                     index++)
                {
                    Renderer renderer = vehicleRenderers[index];
                    if (renderer == null ||
                        renderer.transform.IsChildOf(transform))
                    {
                        continue;
                    }

                    EncapsulateWorldBounds(
                        vehicleRoot,
                        renderer.bounds,
                        ref initialized,
                        ref minimum,
                        ref maximum);
                }
            }

            if (!initialized)
            {
                localBounds = default;
                return false;
            }

            localBounds = new Bounds(
                (minimum + maximum) * 0.5f,
                maximum - minimum);
            return true;
        }

        private static void EncapsulateWorldBounds(
            Transform vehicleRoot,
            Bounds worldBounds,
            ref bool initialized,
            ref Vector3 minimum,
            ref Vector3 maximum)
        {
            Vector3 worldMinimum = worldBounds.min;
            Vector3 worldMaximum = worldBounds.max;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 worldCorner = new(
                            x == 0 ? worldMinimum.x : worldMaximum.x,
                            y == 0 ? worldMinimum.y : worldMaximum.y,
                            z == 0 ? worldMinimum.z : worldMaximum.z);
                        Vector3 localCorner =
                            vehicleRoot.InverseTransformPoint(worldCorner);
                        if (!initialized)
                        {
                            minimum = localCorner;
                            maximum = localCorner;
                            initialized = true;
                        }
                        else
                        {
                            minimum = Vector3.Min(minimum, localCorner);
                            maximum = Vector3.Max(maximum, localCorner);
                        }
                    }
                }
            }
        }
    }
}
