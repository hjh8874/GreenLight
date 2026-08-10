using System.Reflection;
using CityFlow.Contracts;
using CityFlow.View;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public class RoadCongestionViewTests
{
    [Test]
    public void ApplyColor_CreatesPropertyBlockWhenAwakeHasNotRun()
    {
        var owner = new GameObject("RoadCongestionViewTest");
        MeshRenderer renderer = owner.AddComponent<MeshRenderer>();

        try
        {
            RoadCongestionView view =
                owner.AddComponent<RoadCongestionView>();
            SetPrivate(view, "cachedRenderer", renderer);
            SetPrivate(view, "propertyBlock", null);

            Assert.DoesNotThrow(() => InvokeApplyColor(view));
            Assert.That(
                GetPrivate(view, "propertyBlock"),
                Is.TypeOf<MaterialPropertyBlock>());
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    private static void InvokeApplyColor(RoadCongestionView view)
    {
        typeof(RoadCongestionView).GetMethod(
                "ApplyColor",
                BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(view, new object[] { CongestionLevel.Free });
    }

    private static void SetPrivate(
        object target,
        string fieldName,
        object value)
    {
        target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);
    }

    private static object GetPrivate(object target, string fieldName)
    {
        return target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);
    }
}
