using CityFlow.Bootstrap;
using UnityEngine;

namespace CityFlow.DebugTools
{
    // 디버그 씬 전용 세이브 가드: 부팅 시 SaveService.IsSavingEnabled를 꺼서
    // 이 씬의 어떤 저장 경로(오프라인 정산 되쓰기·수동 저장 등)도 라이브 슬롯
    // (save_v1.json)에 절대 안 쓰이게 한다. 로드는 그대로 동작(도시·경제 로드됨).
    // CityBootstrap.InstallServices(기본 실행순서)가 GameSaveLifecycleService
    // (DefaultExecutionOrder 1000)의 로드-정산-저장보다 먼저 이 Initialize를 호출하므로
    // 첫 저장 전에 꺼진다.
    // ponytail: 디버그 전용 — 라이브(출시) 씬엔 절대 붙이지 말 것.
    public sealed class DebugDisableSaving : MonoBehaviour, ICityFlowServiceConsumer
    {
        public void Initialize(CityFlowServices services)
        {
            var save = services?.Save;
            if (save == null)
            {
                return;
            }

            // 공개 API로 라이브 세이브 쓰기 비활성 (리플렉션 우회 제거 — 사일런트 실패 없음).
            save.SetSavingEnabled(false);

            Debug.Log($"[DebugDisableSaving] 라이브 세이브 쓰기 비활성 (IsSavingEnabled={save.IsSavingEnabled}). 이 씬은 save_v1.json에 안 씀.");
        }
    }
}
