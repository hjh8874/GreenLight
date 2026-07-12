using System;

namespace CityFlow.Contracts.Save
{
    // 턴 제한 표지판은 좌표+모드(int) — OnewaySaveData의 자매지만 방향 벡터 대신 TurnMode 값을 들고 있다.
    [Serializable]
    public sealed class TurnSignSaveData
    {
        public int X;
        public int Y;
        public int Mode;
    }
}
