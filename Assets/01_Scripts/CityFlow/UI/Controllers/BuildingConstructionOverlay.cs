using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using TMPro;
using UnityEngine;

namespace CityFlow.UI
{
    // 공사 중인 건물 위에 진행도 %를 띄우고 먼지를 튀긴다. 표시 전담 — 이 컴포넌트가 씬에 없어도
    // 시뮬레이션은 그대로 돈다(공사·승격은 Sim 이 혼자 처리한다).
    //
    // 계획서는 "0.5초 주기 전수 스캔"을 지시했지만 그대로 하지 않았다. PR #169 가
    // 틱당 전체 그리드 스캔으로 비용을 100배 늘린 전례가 있고, 공사장은 보통 한 자리
    // 수인데 그리드는 20×20~대형까지 커진다. Placed 이벤트로 활성 앵커만 들고
    // O(공사장 수)로 갱신한다.
    public sealed class BuildingConstructionOverlay
        : MonoBehaviour, ICityFlowServiceConsumer
    {
        [Header("Label")]
        [SerializeField] private TextMeshPro labelTemplate;
        [SerializeField] private float heightOffset = 1.2f;

        [Header("Dust FX")]
        [SerializeField] private GameObject workPuffPrefab;
        [SerializeField] private GameObject completePuffPrefab;
        [SerializeField, Min(0.05f)] private float puffInterval = 0.7f;
        [SerializeField, Min(0f)] private float workPuffScale = 0.2f;
        [SerializeField, Min(0.05f)] private float workPuffSpeed = 0.55f;
        [SerializeField, Min(0f)] private float completePuffScale = 1f;
        [SerializeField] private float fxHeightOffset = 0.1f;

        [Header("Site FX (공사 내내 유지)")]
        [SerializeField] private GameObject smokeLoopPrefab;
        [SerializeField] private GameObject hammerPrefab;
        [SerializeField, Range(1, 6)] private int hammerCount = 1;
        [SerializeField, Min(0f)] private float hammerRingRadius = 0.22f;
        // 타일은 1유닛인데 받아온 VFX 프리팹은 대개 그보다 크게 만들어져 있다.
        // 눈으로 맞춰야 하는 값이라 인스펙터에 남긴다.
        [SerializeField, Min(0f)] private float smokeLoopScale = 0.22f;
        [SerializeField, Min(0.05f)] private float smokeLoopSpeed = 0.45f;

        private CityFlowServices _services;
        private readonly Dictionary<Vector2Int, TextMeshPro> _labels = new();
        private readonly Dictionary<Vector2Int, float> _nextPuff = new();
        // 사이트당 루트 하나에 연기·망치를 담는다. 완공/철거는 루트 하나만 Destroy 하면 끝.
        private readonly Dictionary<Vector2Int, GameObject> _siteFx = new();
        private readonly List<Vector2Int> _finished = new();
        private bool _subscribed;

        public void Initialize(CityFlowServices services)
        {
            Unsubscribe();
            _services = services;

            if (_services?.Events == null) return;
            _services.Events.Placed += OnPlaced;
            if (_services.Save != null)
            {
                _services.Save.RestoreCompleted += OnRestoreCompleted;
            }
            _subscribed = true;

            CollectExistingSites();   // 씬 진입 시점에 이미 공사 중인 것들
        }

        private void OnRestoreCompleted(RestoreCompletedEvent _) => CollectExistingSites();

        // 세이브 복원은 PlacedEvent 를 쏘지 않는다 — `SimEngine.RestoreSnapshot`이 "복원은 '건설'이
        // 아니다"라며 의도적으로 생략한다. 이벤트만 기다리면 **복원된 공사장은 영구히 라벨이 없다**
        // (공사 중 저장 → 로드 시 진행도는 도는데 표시가 안 됨. 리뷰 지적 2026-07-30).
        // ponytail: 틱당 스캔이 아니라 로드당 1회 전수 훑기다. 그리드 크기에 비례하지만 프레임
        // 비용이 아니므로 허용 — Sim 에 공사 사이트 열거 API 가 생기면 그걸로 갈아탄다.
        private void CollectExistingSites()
        {
            IReadOnlyTileData tiles = _services?.TileData;
            IWorldGridAccess grid = _services?.WorldGrid;
            if (tiles == null || grid == null) return;

            for (int y = 0; y < grid.WorldHeight; y++)
            {
                for (int x = 0; x < grid.WorldWidth; x++)
                {
                    var tile = new Vector2Int(x, y);
                    if (_labels.ContainsKey(tile)) continue;
                    // 앵커에만 라벨 하나. 진행도 조회는 풋프린트 어느 타일로 물어도 답하므로
                    // 앵커 필터가 없으면 여러 칸 건물에 라벨이 칸마다 생긴다.
                    if (!tiles.IsFootprintAnchor(tile)) continue;
                    if (!tiles.TryGetConstructionProgress01(tile, out _)) continue;

                    RegisterSite(tile);
                }
            }
        }

        private void OnPlaced(PlacedEvent e)
        {
            // 철거·완공은 Update 의 진행도 조회가 false 를 받아 정리한다. 여기서는
            // 새 공사장만 등록한다 — 완공 이벤트가 따로 없어도 상태가 어긋나지 않는다.
            if (e.IsRemove || e.Type != TileType.UnderConstruction) return;
            if (_labels.ContainsKey(e.Tile)) return;

            RegisterSite(e.Tile);
        }

        // 라벨과 먼지 타이머를 한 번에 등록한다. 추적 기준은 _labels 하나뿐이므로
        // labelTemplate 이 비면 먼지도 안 난다 — 프리팹에는 둘 다 꽂혀 있다.
        private void RegisterSite(Vector2Int tile)
        {
            TextMeshPro label = CreateLabel(tile);
            if (label == null) return;

            _labels.Add(tile, label);
            _nextPuff[tile] = 0f;   // 첫 먼지는 다음 프레임에 바로
            CreateSiteFx(tile);
        }

        // 연기와 망치는 공사 내내 살아 있다. 위치는 Update 가 루트 하나만 옮기고,
        // 망치는 ConstructionHammer 가 스스로 흔들며 카메라를 본다.
        private void CreateSiteFx(Vector2Int tile)
        {
            if (smokeLoopPrefab == null && hammerPrefab == null) return;

            var root = new GameObject($"ConstructionFX_{tile.x}_{tile.y}");
            root.transform.position = SiteFxPosition(tile);

            if (smokeLoopPrefab != null)
            {
                Transform smoke = Instantiate(smokeLoopPrefab, root.transform).transform;
                smoke.localPosition = Vector3.zero;
                smoke.localScale = smokeLoopPrefab.transform.localScale * smokeLoopScale;
                ApplyParticleSpeed(smoke.gameObject, smokeLoopSpeed);
            }

            for (int i = 0; i < hammerCount && hammerPrefab != null; i++)
            {
                Transform hammer = Instantiate(hammerPrefab, root.transform).transform;

                // 여러 자루면 겹치지 않게 둘러 세운다. 한 자루면 반경을 무시하고
                // 건물 한가운데 — 안 그러면 혼자 옆으로 비켜 서 있다.
                float radius = hammerCount > 1 ? hammerRingRadius : 0f;
                float angle = i * Mathf.PI * 2f / hammerCount;
                hammer.localPosition = hammerPrefab.transform.localPosition +
                    new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;

                var motion = hammer.GetComponent<ConstructionHammer>();
                if (motion != null)
                {
                    motion.SetPhase(i / (float)hammerCount);
                    motion.SetPatrol(BuildHammerPatrol(tile));
                }
            }

            _siteFx.Add(tile, root);
        }

        private Vector3 SiteFxPosition(Vector2Int tile) =>
            FootprintCenter(tile, fxHeightOffset);

        // 망치가 돌아다닐 타점 = 풋프린트 칸마다 하나. FX 루트 기준 로컬 좌표다.
        private Vector3[] BuildHammerPatrol(Vector2Int tile)
        {
            IWorldCoordinateSpace space = _services?.WorldCoordinates;
            if (space == null || hammerPrefab == null) return null;

            Vector2Int size = FootprintSize(tile);
            Vector3 center = FootprintCenter(tile, fxHeightOffset);
            float height = fxHeightOffset + hammerPrefab.transform.localPosition.y;

            var points = new List<Vector3>(size.x * size.y);
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    points.Add(
                        space.GridToWorld(tile + new Vector2Int(x, y), height) - center);
                }
            }

            // 격자 순서 그대로면 줄 끝에서 반대편으로 대각 점프한다. 중심 기준 각도로
            // 정렬해 둘레를 돌게 만든다(표준 XZ 평면 기준. 한 줄짜리 풋프린트는 무관).
            points.Sort((a, b) =>
                Mathf.Atan2(a.z, a.x).CompareTo(Mathf.Atan2(b.z, b.x)));
            return points.ToArray();
        }

        // 공사 중이면 지어질 건물의 크기를, 완공 뒤라면 지어진 건물의 크기를 쓴다.
        // 덕분에 완공 poof 도 앵커 구석이 아니라 건물 한가운데서 터진다(캐시 불필요).
        //
        // 방향을 반드시 함께 본다. CityGrid 는 GetRotatedSize 로 칸을 점유하므로
        // 동/서로 놓인 1x2 집은 실제로 2x1 이다. GetFootprintSize 만 쓰면 FX 중심과
        // 망치 타점이 점유하지도 않은 칸을 가리킨다.
        private Vector2Int FootprintSize(Vector2Int tile)
        {
            IReadOnlyTileData tiles = _services?.TileData;
            if (tiles == null) return Vector2Int.one;

            TileType type = tiles.TryGetConstructionTargetType(tile, out TileType target)
                ? target
                : tiles.GetTileType(tile);

            Vector2Int size =
                TileFootprint.GetRotatedSize(type, tiles.GetDirection(tile));
            return new Vector2Int(Mathf.Max(1, size.x), Mathf.Max(1, size.y));
        }

        // 앵커는 풋프린트의 한쪽 구석이다. 2x2·4칸 건물에서 앵커에 그대로 붙이면
        // 망치질이 건물 모서리에서만 일어나 어색하다.
        private Vector3 FootprintCenter(Vector2Int tile, float height)
        {
            IWorldCoordinateSpace space = _services?.WorldCoordinates;
            if (space == null) return Vector3.zero;

            Vector2Int far = tile + FootprintSize(tile) - Vector2Int.one;
            return (space.GridToWorld(tile, height) +
                    space.GridToWorld(far, height)) * 0.5f;
        }

        // 풋프린트 안의 아무 자리. 넓은 건물일수록 먼지가 여기저기서 튄다.
        private Vector3 FootprintRandomPoint(Vector2Int tile, float height)
        {
            IWorldCoordinateSpace space = _services?.WorldCoordinates;
            if (space == null) return Vector3.zero;

            Vector3 near = space.GridToWorld(tile, height);
            Vector2Int far = tile + FootprintSize(tile) - Vector2Int.one;
            Vector3 span = space.GridToWorld(far, height) - near;

            // 성분별 난수라 좌표 평면(XZ/XY)이 뭐든 그 평면 안에서만 흩어진다 —
            // 높이 성분은 span 에서 0 이므로 흔들리지 않는다.
            return near + new Vector3(
                span.x * Random.value,
                span.y * Random.value,
                span.z * Random.value);
        }

        private TextMeshPro CreateLabel(Vector2Int tile)
        {
            if (labelTemplate == null) return null;

            TextMeshPro label =
                Instantiate(labelTemplate, labelTemplate.transform.parent);
            label.name = $"ConstructionProgress_{tile.x}_{tile.y}";
            label.gameObject.SetActive(true);
            return label;
        }

        private void Update()
        {
            if (_labels.Count == 0) return;

            IReadOnlyTileData tiles = _services?.TileData;
            IWorldCoordinateSpace space = _services?.WorldCoordinates;
            if (tiles == null) return;
            Camera cam = Camera.main;   // 프레임당 1회 조회(라벨마다 부르지 않는다)

            foreach (KeyValuePair<Vector2Int, TextMeshPro> pair in _labels)
            {
                if (!tiles.TryGetConstructionProgress01(pair.Key, out float progress))
                {
                    // 완공됐거나 철거됐다. 둘 다 라벨을 없앤다.
                    _finished.Add(pair.Key);
                    continue;
                }

                // 뚝딱뚝딱: 공사가 도는 동안 일정 간격으로 작은 먼지를 튀긴다.
                if (_nextPuff.TryGetValue(pair.Key, out float next) && Time.time >= next)
                {
                    SpawnPuff(
                        workPuffPrefab,
                        FootprintRandomPoint(pair.Key, fxHeightOffset),
                        workPuffScale,
                        workPuffSpeed);
                    _nextPuff[pair.Key] = Time.time + puffInterval;
                }

                pair.Value.text = $"{Mathf.RoundToInt(progress * 100f)}%";
                if (space != null)
                {
                    // 타일은 안 움직이지만 WorldCoordinates 가 늦게 등록되면 스폰 시점
                    // 위치가 원점이다. 매 프레임 다시 찍어 원점에 연기가 쌓이는 걸 막는다.
                    if (_siteFx.TryGetValue(pair.Key, out GameObject fx) && fx != null)
                    {
                        fx.transform.position = FootprintCenter(pair.Key, fxHeightOffset);
                    }

                    pair.Value.transform.position =
                        space.GridToWorld(pair.Key, heightOffset);
                    // 위치만 바꾸면 XZ 평면(표준 WorldCoordinateProfile)에서 라벨 면이 카메라를
                    // 향하지 않아 옆면으로 눕는다. FlowBurstFloatingText:225-232 와 같은 규칙 —
                    // XY 는 좌표계 회전, XZ 는 카메라 빌보드.
                    if (space.Plane == WorldCoordinatePlane.XY)
                    {
                        pair.Value.transform.rotation = space.CoordinateRotation;
                    }
                    else if (cam != null)
                    {
                        pair.Value.transform.rotation = cam.transform.rotation;
                    }
                }
            }

            for (int i = 0; i < _finished.Count; i++)
            {
                if (_labels.TryGetValue(_finished[i], out TextMeshPro label)
                    && label != null)
                {
                    Destroy(label.gameObject);
                }

                // 완공이면 한 방 터뜨린다. 철거(Empty)면 축하 연출이 아니므로 건너뛴다.
                // 완공 poof 만 건물 한가운데다 — 공사 중 먼지는 풋프린트 여기저기 튄다.
                if (tiles.GetTileType(_finished[i]) != TileType.Empty)
                {
                    SpawnPuff(
                        completePuffPrefab,
                        FootprintCenter(_finished[i], fxHeightOffset),
                        completePuffScale,
                        1f);
                }

                if (_siteFx.TryGetValue(_finished[i], out GameObject fxRoot)
                    && fxRoot != null)
                {
                    Destroy(fxRoot);
                }

                _labels.Remove(_finished[i]);
                _nextPuff.Remove(_finished[i]);
                _siteFx.Remove(_finished[i]);
            }

            _finished.Clear();
        }

        // 수명은 이쪽이 책임진다. "CFXR 이 clearBehavior=Destroy 로 스스로 지운다"에
        // 기대고 있었는데, 꽂힌 게 CFXR_Effect 없는 자식 오브젝트라 0.7초마다 하나씩
        // 영원히 쌓였다(리뷰 지적 2026-08-06). 어떤 파티클을 꽂아도 새지 않게
        // 재생 길이를 재서 직접 파괴한다 — 프리팹 참조 실수에 다시 당하지 않는다.
        // ponytail: 풀링 없음 — 공사장은 한 자리 수고 간격도 0.7초다. 동시 공사가
        // 수십 개로 늘면 InfrastructureEffectPopView 처럼 큐 풀로 바꾼다.
        private void SpawnPuff(
            GameObject prefab,
            Vector3 position,
            float scale,
            float speed)
        {
            IWorldCoordinateSpace space = _services?.WorldCoordinates;
            if (prefab == null || space == null || scale <= 0f) return;

            // 라벨과 같은 규칙(FlowBurstFloatingText:225-232): XY 평면은 좌표계 회전을
            // 물려줘야 파티클이 눕지 않는다. 표준 XZ 는 프리팹 회전 그대로.
            Quaternion rotation = space.Plane == WorldCoordinatePlane.XY
                ? space.CoordinateRotation
                : prefab.transform.rotation;

            GameObject fx = Instantiate(prefab, position, rotation);
            fx.transform.localScale = prefab.transform.localScale * scale;
            ApplyParticleSpeed(fx, speed);
            Destroy(fx, PlaybackSeconds(fx));
        }

        // 재생이 끝나는 시각 = (가장 긴 duration + 그 시스템의 최대 수명) / 재생 속도.
        // 루프 파티클은 스스로 끝나지 않으므로 여기서 쓰면 안 된다(연기는 _siteFx
        // 루트에 붙어 완공 때 함께 파괴된다).
        private static float PlaybackSeconds(GameObject fx)
        {
            float longest = 0f;
            ParticleSystem[] systems =
                fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.MainModule main = systems[i].main;
                float span =
                    (main.duration + main.startLifetime.constantMax) /
                    Mathf.Max(0.01f, main.simulationSpeed);
                longest = Mathf.Max(longest, span);
            }

            return longest + 0.5f;   // 여유 — 조금 늦게 지워지는 편이 잘리는 것보다 낫다
        }

        // 스케일만 줄이면 알갱이만 작아지고 뿜는 기세는 그대로다. 재생 속도는 따로 낮춘다.
        private static void ApplyParticleSpeed(GameObject fx, float speed)
        {
            if (Mathf.Approximately(speed, 1f)) return;

            ParticleSystem[] systems =
                fx.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.MainModule main = systems[i].main;
                main.simulationSpeed *= speed;
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();

            // FX 루트는 부모 없이 만들었으므로 이 컴포넌트만 죽으면 씬에 남는다.
            foreach (KeyValuePair<Vector2Int, GameObject> pair in _siteFx)
            {
                if (pair.Value != null) Destroy(pair.Value);
            }

            _siteFx.Clear();
        }

        private void Unsubscribe()
        {
            if (_subscribed && _services?.Events != null)
            {
                _services.Events.Placed -= OnPlaced;
                if (_services.Save != null)
                {
                    _services.Save.RestoreCompleted -= OnRestoreCompleted;
                }
            }

            _subscribed = false;
        }
    }
}
