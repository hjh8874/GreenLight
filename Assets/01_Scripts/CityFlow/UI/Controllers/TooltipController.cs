using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace CityFlow.UI
{
    public class TooltipController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI txtName;
        [SerializeField] private TextMeshProUGUI txtCost;
        [SerializeField] private TextMeshProUGUI txtDescription;

        [Header("Settings")]
        [Tooltip("마우스 커서 위치에서 툴팁을 얼마나 떨어뜨릴지 결정합니다.")]
        [SerializeField] private Vector2 offset = new Vector2(20f, 20f);

        private void Awake()
        {
            // 기본적으로 숨겨둠
            gameObject.SetActive(false);
        }

        private void Update()
        {
            // 켜져 있을 때만 마우스 커서를 따라다님
            if (Mouse.current != null)
            {
                Vector2 mousePos = Mouse.current.position.ReadValue();
                transform.position = mousePos + offset;
            }
        }

        public void ShowTooltip(string name, int cost, string description)
        {
            gameObject.SetActive(true);
            
            if (txtName != null) txtName.text = name;
            if (txtCost != null) txtCost.text = $"{cost} 코인";
            if (txtDescription != null) txtDescription.text = description;

            // 켜지는 순간 랙 방지를 위해 즉시 위치 동기화
            if (Mouse.current != null)
            {
                transform.position = Mouse.current.position.ReadValue() + offset;
            }
        }

        public void HideTooltip()
        {
            gameObject.SetActive(false);
        }
    }
}
