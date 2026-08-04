using UnityEngine;

namespace CityFlow.UI
{
    /// 씬 참조 호환용 빈 스텁. 오프라인 정산은 제거됐다(PR #219).
    /// 씬 8개가 아직 이 GUID를 참조한다 — 각 씬 소유자가 컴포넌트를 지운 뒤
    /// 이 파일을 삭제할 것. 새 코드에서 참조하지 마라.
    public sealed class OfflineSettlementPopup : MonoBehaviour
    {
    }
}
