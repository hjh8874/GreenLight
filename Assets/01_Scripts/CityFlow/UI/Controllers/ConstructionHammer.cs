using UnityEngine;

namespace CityFlow.UI
{
    // 공사장 위에서 망치가 위아래로 콩콩 찍는 시늉을 한다. 스스로 움직이고 스스로
    // 카메라를 보므로 BuildingConstructionOverlay 는 스폰·파괴만 하면 된다.
    //
    // ponytail: 애니메이션 클립도 DOTween 도 아니고 sin 한 줄이다. 타격 리듬을 실제
    // 먼지 퍼프와 맞추고 싶어지면 그때 Overlay 가 Speed 를 주입하게 바꾼다.
    public sealed class ConstructionHammer : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float bobHeight = 0.14f;
        [SerializeField, Min(0f)] private float swingDegrees = 18f;
        [SerializeField, Min(0f)] private float swingsPerSecond = 2.2f;
        [SerializeField, Min(1)] private int hitsPerSpot = 2;
        [SerializeField, Range(0f, 1f)] private float phase;

        private Camera _cam;
        private Vector3 _basePosition;
        private Vector3[] _patrol;

        // 여러 자루가 한 몸처럼 움직이면 기계 같다. 자루마다 위상을 어긋나게 준다.
        public void SetPhase(float value) => phase = Mathf.Repeat(value, 1f);

        // 풋프린트 각 칸의 로컬 좌표. 한 칸에서 hitsPerSpot 번 치고 다음 칸으로 옮겨간다.
        // 이걸 주면 스포너가 잡아둔 localPosition 은 무시된다(순회가 위치를 정한다).
        public void SetPatrol(Vector3[] localPoints) => _patrol = localPoints;

        // Awake 가 아니라 Start 다 — 스포너가 Instantiate 직후 localPosition 을 옮기는데,
        // Awake 에서 기준점을 잡으면 그 이전 위치로 매 프레임 되돌려버린다.
        private void Start() => _basePosition = transform.localPosition;

        private void Update()
        {
            float cycle = Time.time * swingsPerSecond + phase;

            // Abs(sin) 이라 아래로만 찍는다 — 그냥 sin 이면 위로도 같은 만큼 젖혀져
            // 망치질이 아니라 시계추가 된다.
            float t = Mathf.Abs(Mathf.Sin(cycle * Mathf.PI));

            // 타점은 사이클 번호에서 바로 뽑는다 — 타이머 상태를 안 들고 있어도
            // 칸 이동이 타격 리듬과 어긋나지 않는다.
            Vector3 spot = _basePosition;
            if (_patrol != null && _patrol.Length > 0)
            {
                int hit = Mathf.Max(0, Mathf.FloorToInt(cycle));
                spot = _patrol[hit / hitsPerSpot % _patrol.Length];
            }

            transform.localPosition = spot + Vector3.up * (bobHeight * t);

            if (_cam == null)
            {
                _cam = Camera.main;
            }

            Quaternion swing = Quaternion.Euler(0f, 0f, -swingDegrees * t);
            transform.rotation = _cam != null
                ? _cam.transform.rotation * swing
                : swing;
        }
    }
}
