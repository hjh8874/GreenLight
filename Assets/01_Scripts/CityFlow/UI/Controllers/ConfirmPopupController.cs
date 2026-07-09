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

        private Action _onYes;
        private Action _onNo;

        private void Awake()
        {
            if (btnYes != null) btnYes.onClick.AddListener(OnYesClicked);
            if (btnNo != null) btnNo.onClick.AddListener(OnNoClicked);
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
