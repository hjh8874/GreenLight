using UnityEngine;
using UnityEngine.InputSystem;

namespace CityFlow.View
{
    public sealed class DriveViewCamera : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float height = 0.9f;
        [SerializeField, Min(0f)] private float followDistance = 0.35f;
        [SerializeField, Min(0f)] private float lookDown = 0.45f;
        [SerializeField, Min(0.01f)] private float positionSmoothTime = 0.08f;
        [SerializeField, Min(0.1f)] private float rotationSharpness = 12f;

        private Transform viewRoot;
        private Transform followTarget;
        private Vector3 followLocalTravelAxis =
            Vector3.right;
        private Camera mainCamera;
        private Camera driveCamera;
        private Vector3 positionVelocity;

        public bool IsFollowing => driveCamera != null && driveCamera.enabled;

        public void Init(Transform root, Camera sourceCamera)
        {
            viewRoot = root;
            mainCamera = sourceCamera;

            GameObject cameraObject = new GameObject("DriveViewCamera");
            cameraObject.transform.SetParent(viewRoot, false);
            driveCamera = cameraObject.AddComponent<Camera>();
            driveCamera.rect = new Rect(0f, 0f, 1f, 1f);
            driveCamera.depth = mainCamera != null ? mainCamera.depth + 1f : 1f;
            driveCamera.fieldOfView = 65f;
            driveCamera.orthographic = false;
            driveCamera.nearClipPlane = 0.05f;
            driveCamera.enabled = false;

            if (mainCamera != null)
            {
                driveCamera.clearFlags = mainCamera.clearFlags;
                driveCamera.backgroundColor = mainCamera.backgroundColor;
                driveCamera.cullingMask = mainCamera.cullingMask;
                driveCamera.farClipPlane = mainCamera.farClipPlane;
                driveCamera.allowHDR = mainCamera.allowHDR;
                driveCamera.allowMSAA = mainCamera.allowMSAA;
            }
        }

        public void Follow(Transform target)
        {
            Follow(target, Vector3.right);
        }

        public void Follow(
            Transform target,
            Vector3 localTravelAxis)
        {
            if (target == null || driveCamera == null)
            {
                return;
            }

            followTarget = target;
            followLocalTravelAxis =
                localTravelAxis.sqrMagnitude > 0.0001f
                    ? localTravelAxis.normalized
                    : Vector3.right;
            positionVelocity = Vector3.zero;
            SnapToTarget();

            if (mainCamera != null)
            {
                mainCamera.enabled = false;
            }

            driveCamera.enabled = true;
        }

        public void StopFollowing()
        {
            followTarget = null;
            followLocalTravelAxis = Vector3.right;
            positionVelocity = Vector3.zero;

            if (driveCamera != null)
            {
                driveCamera.enabled = false;
            }

            if (mainCamera != null)
            {
                mainCamera.enabled = true;
            }
        }

        private void Update()
        {
            if (!IsFollowing)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                StopFollowing();
            }
        }

        private void LateUpdate()
        {
            if (!IsFollowing)
            {
                return;
            }

            if (followTarget == null || !followTarget.gameObject.activeInHierarchy)
            {
                StopFollowing();
                return;
            }

            GetTargetPose(out Vector3 desiredPosition, out Quaternion desiredRotation);
            driveCamera.transform.localPosition = Vector3.SmoothDamp(
                driveCamera.transform.localPosition,
                desiredPosition,
                ref positionVelocity,
                positionSmoothTime);

            float rotationT = 1f - Mathf.Exp(-rotationSharpness * Time.deltaTime);
            driveCamera.transform.localRotation = Quaternion.Slerp(
                driveCamera.transform.localRotation,
                desiredRotation,
                rotationT);
        }

        private void SnapToTarget()
        {
            GetTargetPose(out Vector3 desiredPosition, out Quaternion desiredRotation);
            driveCamera.transform.SetLocalPositionAndRotation(desiredPosition, desiredRotation);
        }

        private void GetTargetPose(out Vector3 position, out Quaternion rotation)
        {
            Vector3 targetPosition = viewRoot.InverseTransformPoint(followTarget.position);
            Vector3 direction =
                viewRoot.InverseTransformDirection(
                    followTarget.TransformDirection(
                        followLocalTravelAxis));
            direction.z = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;

            position = targetPosition
                - direction * followDistance
                + new Vector3(0f, 0f, -height);
            Vector3 forward = (direction + new Vector3(0f, 0f, lookDown)).normalized;
            rotation = Quaternion.LookRotation(forward, Vector3.back);
        }

        private void OnDestroy()
        {
            if (mainCamera != null)
            {
                mainCamera.enabled = true;
            }

            if (driveCamera != null)
            {
                Destroy(driveCamera.gameObject);
            }
        }
    }
}
