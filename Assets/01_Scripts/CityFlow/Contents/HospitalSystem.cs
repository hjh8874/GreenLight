using System;
using System.Collections.Generic;
using CityFlow.Bootstrap;
using CityFlow.Contracts;
using CityFlow.Contracts.Save;
using UnityEngine;

namespace CityFlow.Content
{
    /// <summary>
    /// 병원과 주거 타일의 배치 상태를 기준으로
    /// 병원 의료 혜택과 안정도 보너스를 관리합니다.
    /// </summary>
    public sealed class HospitalSystem :
        MonoBehaviour,
        ICityFlowServiceConsumer
    {
        public void Initialize(CityFlowServices services)
        {
            // 안정도(Stability) 시스템이 제거되면서 기능이 모두 삭제되었습니다.
            // 통합 씬에서 Missing Script 에러를 방지하기 위해 껍데기만 남겨둡니다.
        }
    }
}
