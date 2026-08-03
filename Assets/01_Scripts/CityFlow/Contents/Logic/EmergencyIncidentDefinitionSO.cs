using UnityEngine;

namespace CityFlow.Content
{
    [CreateAssetMenu(
        fileName = "EmergencyIncidentDefinition",
        menuName = "CityFlow/Emergency/Incident Definition")]
    public sealed class EmergencyIncidentDefinitionSO :
        ScriptableObject
    {
        [Header("Identity")]
        [SerializeField]
        private string incidentId = "medical_emergency";
        [SerializeField]
        private string title = "응급 환자 발생";
        [SerializeField, TextArea]
        private string description =
            "제한 시간 안에 구급차를 출동시키세요.";

        [Header("Response")]
        [SerializeField, Range(3, 24)]
        private int responseDeadlineHours = 6;
        [SerializeField, Min(0.01f)]
        private float selectionWeight = 1f;

        [Header("Outcome")]
        [SerializeField, TextArea]
        private string successMessage =
            "환자가 제시간에 응급 치료를 받았습니다.";
        [SerializeField, TextArea]
        private string timeoutMessage =
            "구급차가 제시간에 도착하지 못해 환자가 사망했습니다.";
        [SerializeField, TextArea]
        private string unreachableMessage =
            "응급 경로를 확보하지 못해 환자가 사망했습니다.";
        [SerializeField, Min(0f)]
        private float failureHappinessPenalty = 1f;
        [SerializeField]
        private Sprite icon;

        public string IncidentId =>
            string.IsNullOrWhiteSpace(incidentId)
                ? name
                : incidentId.Trim();
        public string Title =>
            string.IsNullOrWhiteSpace(title)
                ? name
                : title;
        public string Description => description ?? string.Empty;
        public int ResponseDeadlineHours =>
            Mathf.Clamp(responseDeadlineHours, 3, 24);
        public float SelectionWeight =>
            Mathf.Max(0.01f, selectionWeight);
        public string SuccessMessage =>
            successMessage ?? string.Empty;
        public string TimeoutMessage =>
            timeoutMessage ?? string.Empty;
        public string UnreachableMessage =>
            unreachableMessage ?? string.Empty;
        public float FailureHappinessPenalty =>
            Mathf.Max(0f, failureHappinessPenalty);
        public Sprite Icon => icon;

#if UNITY_EDITOR
        private void OnValidate()
        {
            incidentId = incidentId?.Trim();
            responseDeadlineHours =
                Mathf.Clamp(responseDeadlineHours, 3, 24);
            selectionWeight =
                Mathf.Max(0.01f, selectionWeight);
            failureHappinessPenalty =
                Mathf.Max(0f, failureHappinessPenalty);
        }
#endif
    }
}
