using UnityEngine;

namespace CityFlow.DebugTools
{
    // 에디팅 보조: 엔진 격자(셀 0~W-1, 0~H-1 = 월드 0~W, 0~H) 경계를 Scene 뷰에 그린다.
    // 붓칠이 격자 밖으로 나가면 bake 때 스킵되므로, 이 박스 안에서만 칠하도록 안내.
    // OnDrawGizmos는 에디터에서만 그려짐 — 런타임 비용 0.
    public sealed class GridBoundsGizmo : MonoBehaviour
    {
        [SerializeField] private int width = 20;
        [SerializeField] private int height = 20;

        private void OnDrawGizmos()
        {
            Vector3 o = transform.position;
            Vector3 bl = o, br = o + new Vector3(width, 0, 0), tr = o + new Vector3(width, height, 0), tl = o + new Vector3(0, height, 0);

            // 경계 사각형(밝은 초록)
            Gizmos.color = new Color(0.2f, 0.9f, 0.5f, 0.9f);
            Gizmos.DrawLine(bl, br); Gizmos.DrawLine(br, tr); Gizmos.DrawLine(tr, tl); Gizmos.DrawLine(tl, bl);

            // 5칸마다 옅은 눈금(위치 감 잡기)
            Gizmos.color = new Color(0.2f, 0.9f, 0.5f, 0.22f);
            for (int x = 5; x < width; x += 5) Gizmos.DrawLine(o + new Vector3(x, 0, 0), o + new Vector3(x, height, 0));
            for (int y = 5; y < height; y += 5) Gizmos.DrawLine(o + new Vector3(0, y, 0), o + new Vector3(width, y, 0));
        }
    }
}
