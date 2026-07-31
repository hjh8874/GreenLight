using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CityFlow.UI
{
    /// <summary>
    /// 신호 제어 패널의 UI 계층 참조를 보관하는 View 클래스.
    /// AnalysisCardController가 이 컴포넌트 하나만 참조하여 하위 엘리먼트들에 접근합니다.
    /// </summary>
    public class SignalControlPanelView : MonoBehaviour
    {
        [Header("Signal Control Elements")]
        public Slider sliderOffset;
        public Slider sliderGreen;
        public Button btnOverrideH;
        public Button btnOverrideV;

        [Header("Cooldown Overlay")]
        public Image imgCooldownH;
        public Image imgCooldownV;
        public TMP_Text txtCooldownH;
        public TMP_Text txtCooldownV;

        [Header("Signal Cycle Gauge UI")]
        public RectTransform cycleGaugeCursor;
        public LayoutElement leHG;
        public LayoutElement leHY;
        public LayoutElement leHC;
        public LayoutElement leVG;
        public LayoutElement leVY;
        public LayoutElement leVC;

        [Header("Waiting Vehicles UI")]
        public TMP_Text txtWaitN;
        public TMP_Text txtWaitS;
        public TMP_Text txtWaitE;
        public TMP_Text txtWaitW;

        [Header("Minimap (Intersection Preview)")]
        public RawImage minimapRawImage;
        [Tooltip("미니맵 카메라가 찍을 레이어 마스크 (도로+차량만 권장)")]
        public LayerMask minimapCullingMask = ~0;
        public float minimapCameraHeight = 8f;

        [Tooltip("미니맵 줌 크기 (직교 카메라 사이즈)")]
        public float minimapZoomSize = 3f;
        [Tooltip("미니맵 FOV (투시 카메라용)")]
        public float minimapFov = 20f;
        [Tooltip("미니맵 빈 공간의 배경색")]
        public Color minimapBackgroundColor = new Color(0.12f, 0.14f, 0.18f, 1f);
        [Tooltip("AR 숫자 오버레이가 타일 중앙에서 떨어지는 오프셋 거리")]
        public float arRoadOffset = 0.4f;

        public int minimapResolution = 256;
    }
}
