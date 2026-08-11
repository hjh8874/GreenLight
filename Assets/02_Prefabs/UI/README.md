# Gameplay UI Prefab Source of Truth

`Assets/02_Prefabs/UI/Gameplay` 아래의 모듈 Prefab이 게임 UI의 추적 가능한
런타임 원본이다. `UI_MainCanvasRoot.prefab`은 HUD, 우측 Dock, Build Panel,
Settings Panel, Green Feed 모듈을 nested Prefab 인스턴스로 조립한다. HUD는
`UI_TopLeftActionDock.prefab`을 다시 nested Prefab으로 포함한다.

`Assets/99_Download`의 Layer Lab 파일은 원본 에셋 보관 위치지만 Git에서
제외된다. 따라서 Prefab이 참조하는 필요한 의존성은 Baker가
`Assets/02_Prefabs/UI/Shared/LayerLab`로 복제한다. 추적되는 Shared 복제본을
임의로 원본과 별개로 편집하지 않는다. 시각 에셋을 교체할 때는 Layer Lab
원본을 선택한 뒤 Baker를 통해 Shared 복제본과 Prefab 참조를 갱신한다.

`GameplayUiPrefabBaker`는 통합 Scene을 저장하거나 수정하지 않고, 기존
UI 계층을 Prefab 구조로 이전할 때만 사용한다. 실행하면 Prefab을 통합
Scene의 현재 UI 계층으로 덮어쓰므로 일상적인 Prefab 편집 동기화 도구로
사용하지 않는다. 마이그레이션 이후의 UI 수정은 각 모듈 Prefab에 직접
적용하며 Root에는 nested Prefab 연결을 우회하는 개별 복사본을 추가하지
않는다. 생성된 Root Prefab에는
`GameplayUiRuntimeBinder`가 포함되어 Scene 전용 `PlacementController`
참조를 런타임에 자동 복구하므로 통합 담당자의 Inspector 재연결은 필요 없다.
