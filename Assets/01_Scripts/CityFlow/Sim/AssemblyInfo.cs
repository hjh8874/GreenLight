using System.Runtime.CompilerServices;

// internal 클래스를 유지하면서 테스트 어셈블리에만 내부를 공개한다.
[assembly: InternalsVisibleTo("CityFlow.Sim.Tests")]
