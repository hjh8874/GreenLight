namespace CityFlow.Sim
{
    // 엔진의 유일한 public 창구(파사드). Bootstrap이 생성하고 매 프레임 Tick(dt) 호출.
    // D1: 고정 틱 누산기만. Step()은 빈 껍데기 — 파이프라인은 D2부터.
    public sealed class SimEngine
    {
        readonly SimConfig _config;
        float _acc;   // 아직 소비되지 않고 저금된 시간

        // 테스트 관찰용 seam. internal이라 테스트 어셈블리만 봄(InternalsVisibleTo).
        internal int StepCount { get; private set; }

        public SimEngine(SimConfig config)
        {
            _config = config;
        }
        // ponytail: SimEvents는 #5에서 생성자 주입 추가 (Step 실체화 시).

        // 고정 틱 누산기: 프레임 dt가 들쭉날쭉해도 Step은 정확히 TickInterval마다 1번.
        public void Tick(float deltaTime)
        {
            _acc += deltaTime;
            int steps = 0;
            // steps 캡: 렉으로 dt가 튀어도 한 프레임에 폭주하지 않게(죽음의 나선 방지).
            while (_acc >= _config.TickInterval && steps < _config.MaxStepsPerFrame)
            {
                _acc -= _config.TickInterval;
                Step();
                steps++;
            }
            // ponytail: 캡에 걸린 잔여 _acc는 다음 프레임들로 이월(백로그). 폭주보단 지연 선택.
        }

        // 고정 0.1s 시뮬 한 칸. 순서가 곧 파이프라인 — D2~에서 채움.
        void Step()
        {
            StepCount++;
        }
    }
}
