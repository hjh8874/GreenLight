using UnityEngine;

namespace CityFlow.Feed
{
    /// <summary>
    /// 통합 담당자가 씬에 배치하는 단 하나의 물건. 씬에 이미 있는
    /// <see cref="CitizenFeedService"/>에 목록의 누락 항목만 병합한다.
    ///
    /// 이것이 있는 이유: 피드 데이터를 씬의 직렬화 배열로만 물리면, 규칙·템플릿이
    /// 늘거나 에셋이 재생성될 때 씬을 다시 굽기 전까지 해당 이벤트가 **에러 없이
    /// 조용히** 무동작이 된다. 실제로 개발 중 신규 5종 참조가 전부 null이 되어
    /// 아무 글도 나가지 않는 상태가 발생했다.
    ///
    /// 씬 계층을 추측하지 않는다 — 목록 에셋이라는 명시적 데이터 계약만 읽는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CitizenFeedIntegrationInstaller : MonoBehaviour
    {
        [SerializeField] private FeedCatalogSO catalog;
        [Tooltip("씬에 CitizenFeedService가 없으면 이 오브젝트에 직접 만든다.")]
        [SerializeField] private bool createServiceIfMissing = true;
        [SerializeField] private bool logResult = true;

        private void Awake()
        {
            Install();
        }

        public int Install()
        {
            if (catalog == null)
            {
                Debug.LogWarning(
                    "[CitizenFeedInstaller] 목록(FeedCatalogSO)이 비어 있어 아무것도 하지 않습니다.",
                    this);
                return 0;
            }

            CitizenFeedService service =
                FindAnyObjectByType<CitizenFeedService>(FindObjectsInactive.Include);

            if (service == null)
            {
                if (!createServiceIfMissing)
                {
                    Debug.LogWarning(
                        "[CitizenFeedInstaller] 씬에 CitizenFeedService가 없습니다.",
                        this);
                    return 0;
                }

                service = gameObject.AddComponent<CitizenFeedService>();
            }

            int added = service.MergeFrom(catalog);
            if (logResult)
            {
                Debug.Log(
                    $"[CitizenFeedInstaller] 목록 v{catalog.CatalogVersion} 적용 — " +
                    (added > 0
                        ? $"누락 항목 {added}개를 병합했습니다."
                        : "이미 최신이라 병합할 항목이 없습니다."),
                    this);
            }

            return added;
        }

        // Unity setup: 02_Prefabs/Feed/CitizenFeedIntegration.prefab 를 씬에 배치하면 끝.
    }
}
