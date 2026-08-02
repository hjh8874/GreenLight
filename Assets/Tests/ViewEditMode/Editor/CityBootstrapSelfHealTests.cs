using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System.Text.RegularExpressions;
using CityFlow.Bootstrap;

namespace CityFlow.Tests
{
    // 도메인 리로드 백업-복원(AwakeInstancesAfterBackupRestoration)은 Awake를 다시
    // 부르지 않으면서 비직렬화 상태(Services·simEngine)를 증발시킨다(2026-08-02 실측:
    // 리로드×플레이 진입 경합 → 부트스트랩이 예외 없이 빈 껍데기, 전 소비자 NRE 폭주).
    // 이 테스트는 그 상태(= Awake 미실행·Services null)에서 Update가 자가 치유하는지 본다.
    public class CityBootstrapSelfHealTests
    {
        private static readonly BindingFlags Flags =
            BindingFlags.NonPublic | BindingFlags.Instance;

        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
        }

        private CityBootstrap CreateRestoredLikeBootstrap()
        {
            // EditMode에선 Awake가 자동 실행되지 않는다 — 복원 직후(비직렬화 상태
            // 증발 + Awake 미호출)와 동일한 상태가 자연스럽게 만들어진다.
            _go = new GameObject("~BootstrapSelfHealTest");
            _go.SetActive(false);
            var boot = _go.AddComponent<CityBootstrap>();
            typeof(CityBootstrap)
                .GetField("useFakeServices", Flags)
                .SetValue(boot, true); // SO 에셋 없이 초기화 가능한 페이크 경로
            return boot;
        }

        [Test]
        public void Update_WithLostServices_RebuildsAndWarns()
        {
            CityBootstrap boot = CreateRestoredLikeBootstrap();
            Assert.IsNull(boot.Services, "전제: 복원 직후 Services는 유실 상태");

            LogAssert.Expect(
                LogType.Warning,
                new Regex("^\\[CityBootstrap\\] Services 유실 감지.*"));
            typeof(CityBootstrap).GetMethod("Update", Flags)
                .Invoke(boot, null);

            Assert.IsNotNull(boot.Services, "Update가 Services를 재구축해야 한다");
        }

        [Test]
        public void Update_AfterRecovery_DoesNotRebuildAgain()
        {
            CityBootstrap boot = CreateRestoredLikeBootstrap();
            MethodInfo update = typeof(CityBootstrap).GetMethod("Update", Flags);

            LogAssert.Expect(
                LogType.Warning,
                new Regex("^\\[CityBootstrap\\] Services 유실 감지.*"));
            update.Invoke(boot, null);
            object first = boot.Services;

            update.Invoke(boot, null); // 두 번째 호출: 경고 없이 같은 인스턴스 유지
            Assert.AreSame(first, boot.Services);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
