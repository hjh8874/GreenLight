## Purpose
- 건설 패널(인프라, 주거, 상업, 공공) 내의 UI 슬롯들이 GridLayoutGroup 안에서 비율이 깨지거나 버튼이 겹치고 사라지는 치명적 레이아웃 버그를 해결하기 위함

## Changes
- 공통 프리팹인 `UI_BuildSlot.prefab` 내부의 앵커(Anchor) 속성을 반응형으로 완벽하게 재정렬 (Top/Bottom Stretch)
- 씬 파일(`CityFlowIntegrated_Geon.unity`) 내부에 남아있던 193줄 이상의 수동 오버라이드(Override) 찌꺼기 데이터를 스크립트로 강제 삭제(Hard Revert)

## Check
- 씬 뷰 및 게임 뷰에서 모든 패널의 UI 슬롯 비율과 위치가 겹침 없이 깔끔하게 정렬되는지 확인 완료

## Risk
- Scene 변경: Yes (CityFlowIntegrated_Geon.unity)
- Prefab 변경: Yes (UI_BuildSlot.prefab)
- Meta 변경: No
- Package 변경: No

## Reviewer Notes
- 이 브랜치는 순수하게 프리팹 앵커 및 씬의 RectTransform 오버라이드를 초기화하는 작업만 수행했습니다. 기존 로직 및 DOTween 애니메이션과의 충돌은 발생하지 않습니다.
