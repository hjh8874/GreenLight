using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

namespace CityFlow.UI
{
    public class ConfirmPopupController : MonoBehaviour
    {
        [Header("UI Elements")]
        [SerializeField] private TextMeshProUGUI txtMessage;
        [SerializeField] private Button btnYes;
        [SerializeField] private Button btnNo;

        [Header("Layout Override (Title Scene Only)")]
        [Tooltip("타이틀 씬 등에서 팝업 크기를 강제 고정해야 할 경우 체크하세요. (체크 시 다른 씬 팝업이 깨질 수 있음)")]
        [SerializeField] private bool forceTitleLayout = false;

        private Action _onYes;
        private Action _onNo;

        private void Awake()
        {
            if (btnYes != null) btnYes.onClick.AddListener(OnYesClicked);
            if (btnNo != null) btnNo.onClick.AddListener(OnNoClicked);

            bool isTitleScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "TitleScene";

            if ((forceTitleLayout || isTitleScene) && txtMessage != null)
            {
                // 텍스트가 팝업 바깥으로 나가는(Overflow) 문제를 해결하기 위해 줄바꿈 및 레이아웃 강제 지정
                txtMessage.enableWordWrapping = true;
                txtMessage.alignment = TextAlignmentOptions.Center;

                // 텍스트가 부모 크기를 무시하고 가로로 무한정 늘어나는 원인인 ContentSizeFitter 제거
                var csf = txtMessage.GetComponent<ContentSizeFitter>();
                if (csf != null) Destroy(csf);

                // 팝업 패널 내부에 맞게 텍스트의 앵커와 여백(offset)을 코드로 강제 고정합니다.
                var rect = txtMessage.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchorMin = new Vector2(0f, 0.35f); // 팝업 하단의 35%는 버튼을 위해 비움
                    rect.anchorMax = new Vector2(1f, 1f);    // 팝업 상단 끝까지
                    rect.offsetMin = new Vector2(40f, 0f);   // 왼쪽 40px 여백
                    rect.offsetMax = new Vector2(-40f, -40f);// 오른쪽 40px, 위쪽 40px 여백
                }
            }
        }

        public void Show(string message, Action onYes, Action onNo = null)
        {
            if (txtMessage != null) txtMessage.text = message;
            _onYes = onYes;
            _onNo = onNo;

            gameObject.SetActive(true);

            // DOTween 팝업 애니메이션
            transform.DOKill();
            transform.localScale = Vector3.zero;
            transform.DOScale(Vector3.one, 0.3f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        public void Hide()
        {
            transform.DOKill();
            transform.DOScale(Vector3.zero, 0.2f).SetEase(Ease.InBack).SetUpdate(true).OnComplete(() =>
            {
                gameObject.SetActive(false);
            });
        }

        public void HideImmediate()
        {
            transform.DOKill();
            transform.localScale = Vector3.one;
            gameObject.SetActive(false);
            _onYes = null;
            _onNo = null;
        }

        private void OnYesClicked()
        {
            _onYes?.Invoke();
            Hide();
        }

        private void OnNoClicked()
        {
            _onNo?.Invoke();
            Hide();
        }
    }
}
