using System.Collections.Generic;
using CityFlow.Sim;
using UnityEngine;

namespace CityFlow.View
{
    [DisallowMultipleComponent]
    public sealed class TrafficLightLensView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [Tooltip("체크하면 가로축 신호 상태를, 해제하면 세로축 신호 상태를 표시합니다.")]
        [SerializeField] private bool usesHorizontalAxis;
        [SerializeField] private Renderer modelRenderer;
        [SerializeField] private Renderer redLens;
        [SerializeField] private Renderer yellowLens;
        [SerializeField] private Renderer greenLens;
        [Tooltip("체크하면 현재 신호등 에셋을 90도씩 회전 복제하여 교차로의 네 진입 방향을 모두 표시합니다.")]
        [SerializeField] private bool showFourWayIntersection = true;
        [SerializeField, Min(0.1f)] private float visualHeight = 1.4f;
        [SerializeField, Min(0.01f)] private float lensDiameter = 0.1f;
        [SerializeField, Range(0.2f, 1f)] private float lensDepthRatio = 0.55f;
        [SerializeField, Min(0f)] private float lensSurfaceOffset = 0.008f;
        [SerializeField, Min(0f)] private float emissionIntensity = 2.5f;
        [SerializeField, Range(0f, 1f)] private float inactiveBrightness = 0.08f;
        [SerializeField] private Vector3 modelBaseAnchor =
            new Vector3(-0.007656f, 0.004203f, -0.158569f);
        [SerializeField] private Vector3 redLensAnchor =
            new Vector3(0.014627f, 6.711149f, 0.417816f);
        [SerializeField] private Vector3 yellowLensAnchor =
            new Vector3(0.014627f, 6.154455f, 0.417816f);
        [SerializeField] private Vector3 greenLensAnchor =
            new Vector3(0.014627f, 5.601828f, 0.417816f);

        private MaterialPropertyBlock redBlock;
        private MaterialPropertyBlock yellowBlock;
        private MaterialPropertyBlock greenBlock;
        private readonly List<DirectionalHead> directionalHeads = new();
        private Camera viewCamera;

        private sealed class DirectionalHead
        {
            public Transform ViewTransform;
            public bool UsesHorizontalAxis;
            public Renderer RedLens;
            public Renderer YellowLens;
            public Renderer GreenLens;
            public MaterialPropertyBlock RedBlock = new();
            public MaterialPropertyBlock YellowBlock = new();
            public MaterialPropertyBlock GreenBlock = new();
        }

        public bool UsesHorizontalAxis => usesHorizontalAxis;

        private void Awake()
        {
            FitModelAndLenses();
            BuildDirectionalHeads();
        }

        public void ApplyPhase(SignalPhase phase)
        {
            redBlock ??= new MaterialPropertyBlock();
            yellowBlock ??= new MaterialPropertyBlock();
            greenBlock ??= new MaterialPropertyBlock();

            bool showLenses = IsFacingCamera(transform);
            ApplyLens(
                redLens,
                redBlock,
                new Color(1f, 0.03f, 0.02f),
                phase == SignalPhase.Red,
                showLenses);
            ApplyLens(
                yellowLens,
                yellowBlock,
                new Color(1f, 0.62f, 0.02f),
                phase == SignalPhase.Yellow,
                showLenses);
            ApplyLens(
                greenLens,
                greenBlock,
                new Color(0.03f, 1f, 0.12f),
                phase == SignalPhase.Green,
                showLenses);
        }

        public void ApplyPhases(SignalPhase horizontalPhase, SignalPhase verticalPhase)
        {
            ApplyPhase(usesHorizontalAxis ? horizontalPhase : verticalPhase);

            foreach (DirectionalHead head in directionalHeads)
            {
                SignalPhase phase = head.UsesHorizontalAxis
                    ? horizontalPhase
                    : verticalPhase;
                bool showLenses = IsFacingCamera(head.ViewTransform);
                ApplyLens(
                    head.RedLens,
                    head.RedBlock,
                    new Color(1f, 0.03f, 0.02f),
                    phase == SignalPhase.Red,
                    showLenses);
                ApplyLens(
                    head.YellowLens,
                    head.YellowBlock,
                    new Color(1f, 0.62f, 0.02f),
                    phase == SignalPhase.Yellow,
                    showLenses);
                ApplyLens(
                    head.GreenLens,
                    head.GreenBlock,
                    new Color(0.03f, 1f, 0.12f),
                    phase == SignalPhase.Green,
                    showLenses);
            }
        }

        private void ApplyLens(
            Renderer lens,
            MaterialPropertyBlock block,
            Color lensColor,
            bool isActive,
            bool isVisible)
        {
            if (lens == null)
            {
                return;
            }

            lens.enabled = isVisible;
            if (!isVisible)
            {
                return;
            }

            Color baseColor = isActive ? lensColor : lensColor * inactiveBrightness;
            baseColor.a = 1f;
            Color emissionColor = isActive ? lensColor * emissionIntensity : Color.black;

            lens.GetPropertyBlock(block);
            block.SetColor(BaseColorId, baseColor);
            block.SetColor(ColorId, baseColor);
            block.SetColor(EmissionColorId, emissionColor);
            lens.SetPropertyBlock(block);
        }

        private bool IsFacingCamera(Transform headTransform)
        {
            if (headTransform == null)
            {
                return false;
            }

            if (viewCamera == null || !viewCamera.isActiveAndEnabled)
            {
                viewCamera = Camera.main;
            }

            if (viewCamera == null)
            {
                return true;
            }

            Vector3 headFront = headTransform.TransformDirection(Vector3.up).normalized;
            Vector3 directionToCamera = -viewCamera.transform.forward;
            return Vector3.Dot(headFront, directionToCamera) > 0f;
        }

        private void FitModelAndLenses()
        {
            if (modelRenderer == null)
            {
                return;
            }

            Transform model = modelRenderer.transform;
            Bounds sourceBounds = modelRenderer.localBounds;
            Vector3 sourceSize = sourceBounds.size;
            if (sourceSize.y <= Mathf.Epsilon)
            {
                return;
            }

            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            model.localScale = Vector3.one * (visualHeight / sourceSize.y);

            Vector3 basePosition = transform.InverseTransformPoint(
                model.TransformPoint(modelBaseAnchor));
            model.localPosition = -basePosition;

            float lensDepth = Mathf.Max(0.01f, lensDiameter * lensDepthRatio);
            PositionLens(redLens, redLensAnchor, lensDepth);
            PositionLens(yellowLens, yellowLensAnchor, lensDepth);
            PositionLens(greenLens, greenLensAnchor, lensDepth);
        }

        private void BuildDirectionalHeads()
        {
            directionalHeads.Clear();
            if (!showFourWayIntersection
                || modelRenderer == null
                || redLens == null
                || yellowLens == null
                || greenLens == null)
            {
                return;
            }

            CreateDirectionalHead(90f, usesHorizontal: !usesHorizontalAxis);
            CreateDirectionalHead(180f, usesHorizontal: usesHorizontalAxis);
            CreateDirectionalHead(270f, usesHorizontal: !usesHorizontalAxis);
        }

        private void CreateDirectionalHead(float angle, bool usesHorizontal)
        {
            GameObject headRoot = new GameObject($"DirectionalHead_{angle:000}");
            headRoot.transform.SetParent(transform, false);
            headRoot.transform.localRotation = Quaternion.Euler(0f, 0f, angle);

            Renderer modelClone = CloneRenderer(modelRenderer, headRoot.transform, "Model");
            Renderer redClone = CloneRenderer(redLens, headRoot.transform, "RedLens");
            Renderer yellowClone = CloneRenderer(yellowLens, headRoot.transform, "YellowLens");
            Renderer greenClone = CloneRenderer(greenLens, headRoot.transform, "GreenLens");

            if (modelClone == null
                || redClone == null
                || yellowClone == null
                || greenClone == null)
            {
                Destroy(headRoot);
                return;
            }

            directionalHeads.Add(new DirectionalHead
            {
                ViewTransform = headRoot.transform,
                UsesHorizontalAxis = usesHorizontal,
                RedLens = redClone,
                YellowLens = yellowClone,
                GreenLens = greenClone
            });
        }

        private static Renderer CloneRenderer(
            Renderer source,
            Transform parent,
            string objectName)
        {
            if (source == null)
            {
                return null;
            }

            GameObject clone = Instantiate(source.gameObject, parent, false);
            clone.name = objectName;
            return clone.GetComponent<Renderer>();
        }

        private void PositionLens(
            Renderer lens,
            Vector3 modelAnchor,
            float lensDepth)
        {
            if (lens == null)
            {
                return;
            }

            Transform lensTransform = lens.transform;
            Vector3 lensPosition = transform.InverseTransformPoint(
                modelRenderer.transform.TransformPoint(modelAnchor));
            lensPosition.y += lensSurfaceOffset;
            lensTransform.localPosition = lensPosition;
            lensTransform.localRotation = Quaternion.identity;
            lensTransform.localScale = new Vector3(lensDiameter, lensDepth, lensDiameter);
        }
    }
}
