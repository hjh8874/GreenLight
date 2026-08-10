using System.Reflection;
using CityFlow.View;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

// PR #249 리뷰(lijinwoo·자동검증)에서 지적된 P1 회귀를 고정한다.
//
// 차량 GameObject 는 풀링된다(CarMotion.TakeFreeVehicle / DeactivateCommuteVehicle).
// FreeFlowStreakView 는 재사용되는 오브젝트에 그대로 붙어 있으므로,
// 원본 색 캐시를 비우지 않으면 다음 차량이 이전 차량의 CarStyle 색으로 복원된다.
// "전부 흰색"(#241 결함)은 막아도 "옆 차 색"이 남는 두 번째 회귀다.
public class FreeFlowStreakViewPoolReuseTests
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    [Test]
    public void PoolReuse_RestoresNewVehicleColor_NotPreviousOne()
    {
        GameObject vehicle = CreateVehicleWithRenderer(out Renderer renderer);
        try
        {
            FreeFlowStreakView view = vehicle.AddComponent<FreeFlowStreakView>();
            InvokeAwake(view);

            // ── 차량 A: CarStyle 이 팔레트를 칠한다 ──
            Color colorA = new Color(0.9f, 0.2f, 0.2f, 1f);
            PaintCarStyle(renderer, colorA);
            InvokeOnEnable(view);
            ApplyStage(view, 0);
            AssertColorApprox(
                colorA,
                ReadBlockColor(renderer),
                "전제: 0단계는 A 색을 그대로 둔다");

            // A 가 2단계까지 올라가 틴트가 적용된 채로 반납된다.
            ApplyStage(view, 2);
            Assert.IsFalse(
                Approximately(colorA, ReadBlockColor(renderer)),
                "전제: 2단계는 틴트가 적용된다");
            vehicle.SetActive(false);

            // ── 같은 오브젝트가 차량 B 로 재사용된다 ──
            vehicle.SetActive(true);
            InvokeOnEnable(view);
            Color colorB = new Color(0.15f, 0.45f, 0.95f, 1f);
            PaintCarStyle(renderer, colorB);   // ApplyCarStyle 이 B 팔레트를 다시 칠함
            ApplyStage(view, 0);

            AssertColorApprox(
                colorB,
                ReadBlockColor(renderer),
                "풀 재사용 후 0단계는 B 색으로 돌아와야 한다(A 색이 남으면 회귀)");
        }
        finally
        {
            Object.DestroyImmediate(vehicle);
        }
    }

    // 재사용된 차의 단계가 우연히 이전과 같으면 조기 반환으로 틴트가 재적용되지 않는다.
    // OnEnable 이 appliedStage 를 되돌리므로 같은 단계여도 다시 칠해져야 한다.
    [Test]
    public void PoolReuse_SameStageAsBefore_StillReappliesTint()
    {
        GameObject vehicle = CreateVehicleWithRenderer(out Renderer renderer);
        try
        {
            FreeFlowStreakView view = vehicle.AddComponent<FreeFlowStreakView>();
            InvokeAwake(view);

            PaintCarStyle(renderer, new Color(0.9f, 0.2f, 0.2f, 1f));
            InvokeOnEnable(view);
            ApplyStage(view, 0);
            vehicle.SetActive(false);

            vehicle.SetActive(true);
            InvokeOnEnable(view);
            Color colorB = new Color(0.15f, 0.45f, 0.95f, 1f);
            PaintCarStyle(renderer, colorB);
            ApplyStage(view, 0);   // A 와 같은 0단계 — 조기 반환하면 A 색이 남는다

            AssertColorApprox(
                colorB,
                ReadBlockColor(renderer),
                "같은 단계로 재사용돼도 새 차 색이 유지돼야 한다");
        }
        finally
        {
            Object.DestroyImmediate(vehicle);
        }
    }

    // Color 는 구조체 정확 비교라 SetColor/GetColor 왕복의 미세 오차에도 실패한다.
    // 채널별 허용 오차로 본다.
    private static void AssertColorApprox(Color expected, Color actual, string message)
    {
        Assert.AreEqual(expected.r, actual.r, 0.001f, message + " (r)");
        Assert.AreEqual(expected.g, actual.g, 0.001f, message + " (g)");
        Assert.AreEqual(expected.b, actual.b, 0.001f, message + " (b)");
    }

    private static bool Approximately(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.001f &&
        Mathf.Abs(a.g - b.g) < 0.001f &&
        Mathf.Abs(a.b - b.b) < 0.001f;

    private static GameObject CreateVehicleWithRenderer(out Renderer renderer)
    {
        GameObject vehicle = GameObject.CreatePrimitive(PrimitiveType.Cube);
        vehicle.name = "PooledVehicle";
        renderer = vehicle.GetComponent<Renderer>();
        return vehicle;
    }

    // MainCityView.ApplyCarStyle 이 하는 일과 같다 — 프로퍼티 블록에 팔레트 색을 쓴다.
    private static void PaintCarStyle(Renderer renderer, Color color)
    {
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetColor(BaseColorId, color);
        renderer.SetPropertyBlock(block);
    }

    private static Color ReadBlockColor(Renderer renderer)
    {
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        return block.GetColor(BaseColorId);
    }

    // Awake/OnEnable 은 AddComponent 로는 즉시 호출되지 않는다(에디트 모드).
    private static void InvokeAwake(FreeFlowStreakView view) =>
        InvokePrivate(view, "Awake");

    private static void InvokeOnEnable(FreeFlowStreakView view) =>
        InvokePrivate(view, "OnEnable");

    private static void ApplyStage(FreeFlowStreakView view, int stage) =>
        typeof(FreeFlowStreakView)
            .GetMethod("ApplyStage", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(view, new object[] { stage });

    private static void InvokePrivate(FreeFlowStreakView view, string method) =>
        typeof(FreeFlowStreakView)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(view, null);
}
