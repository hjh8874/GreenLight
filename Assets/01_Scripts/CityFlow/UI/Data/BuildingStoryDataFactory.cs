using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.UI.Data
{
    /// <summary>
    /// IReadOnlyTileData의 Density01 및 CongestionLevel 시드를 기반으로
    /// 건물 상세 카드용 가짜 스토리 데이터를 온더플라이(On-the-fly)로 역산 조립하는 팩토리.
    /// UI 레이어 단에서만 구동되며, 코어 Sim 모듈을 직접 참조하지 않습니다 (Contracts 경유).
    /// </summary>
    public static class BuildingStoryDataFactory
    {
        // ─── 주거지(House) 이름 풀 ───────────────────────────────────
        private static readonly string[] HouseNames =
        {
            "그린빌 아파트",
            "해오름 주공",
            "파크뷰 맨션",
            "레이크 타운",
            "센트럴 하이츠"
        };

        // ─── 회사(Office) 이름 풀 ────────────────────────────────────
        private static readonly string[] OfficeNames =
        {
            "넥슨 판교 사옥",
            "그린테크 본사",
            "스카이랩 R&D",
            "아이들 소프트",
            "블루웨이브 스튜디오"
        };

        // ─── 학교(School) 이름 풀 ────────────────────────────────────
        private static readonly string[] SchoolNames =
        {
            "그린라이트 초등학교",
            "해오름 중학교",
            "파크뷰 고등학교",
            "센트럴 국제학교",
            "아이들 학원"
        };

        // ─── 병원(Hospital) 이름 풀 ──────────────────────────────────
        private static readonly string[] HospitalNames =
        {
            "그린 종합병원",
            "해오름 의료원",
            "파크뷰 클리닉",
            "센트럴 대학병원",
            "아이들 건강센터"
        };

        /// <summary>
        /// 타일 좌표와 코어 엔진 신호(density, congestion)를 시드로 받아
        /// 건물 상세 카드용 BuildingStoryData를 즉석 역산 조립합니다.
        /// </summary>
        /// <param name="tile">타일 좌표 (Random Seed용 및 데이터 참조용)</param>
        /// <param name="type">타일 종류 (House, Office, School, Hospital)</param>
        /// <param name="density01">혼잡도 (0.0~1.0)</param>
        /// <param name="congestion">혼잡 레벨 열거형</param>
        /// <param name="accumulatedDelay">누적 지연 시간 (외부에서 200ms 주기로 증가시킨 값)</param>
        /// <param name="staffingFilled">채용 인원 (IReadOnlyCityStats에서 가져온 값, 없으면 -1)</param>
        /// <param name="staffingCapacity">채용 정원 (IReadOnlyCityStats에서 가져온 값, 없으면 -1)</param>
        /// <param name="tilePopulation">타일 인구 (PopulationSystem에서 가져온 값, 없으면 -1)</param>
        public static BuildingStoryData Synthesize(
            Vector2Int tile,
            TileType type,
            float density01,
            CongestionLevel congestion,
            float accumulatedDelay,
            int staffingFilled = -1,
            int staffingCapacity = -1,
            int tilePopulation = -1,
            BuildingHoverCommentCatalogSO commentCatalog = null,
            string companyTypeId = "")
        {
            // 타일 좌표 기반 고정 시드 (같은 타일을 누르면 항상 같은 이름이 나오도록)
            int seed = tile.x * 1000 + tile.y;

            string buildingName = PickName(type, seed);

            // 총 인원 산출: 실제 데이터가 있으면 사용, 없으면 density 기반 합성
            int totalStaff = ResolveTotal(type, seed, density01, staffingCapacity, tilePopulation);

            // 지각 인원: density 비례 합성 (density01이 높을수록 지각률 상승)
            int tardyStaff = Mathf.RoundToInt(totalStaff * Mathf.Clamp01(density01 * 0.8f));
            if (staffingFilled >= 0 && staffingCapacity > 0)
            {
                // 실제 채용 데이터가 있는 경우, 미출근 인원 = 정원 - 출근
                tardyStaff = Mathf.Max(0, staffingCapacity - staffingFilled);
            }

            string storyComment = BuildingHoverCommentResolver.Resolve(
                commentCatalog,
                new BuildingHoverCommentContext(
                    tile,
                    type,
                    congestion,
                    buildingName,
                    companyTypeId));

            return new BuildingStoryData(
                buildingName,
                storyComment,
                totalStaff,
                tardyStaff,
                accumulatedDelay);
        }

        public static string ResolveBuildingName(
            TileType type,
            Vector2Int tile)
        {
            int seed = tile.x * 1000 + tile.y;
            return PickName(type, seed);
        }

        private static string PickName(TileType type, int seed)
        {
            return type switch
            {
                TileType.House => HouseNames[Mathf.Abs(seed) % HouseNames.Length],
                TileType.Office => OfficeNames[Mathf.Abs(seed) % OfficeNames.Length],
                TileType.School => SchoolNames[Mathf.Abs(seed) % SchoolNames.Length],
                TileType.Hospital => HospitalNames[Mathf.Abs(seed) % HospitalNames.Length],
                _ => "알 수 없는 건물"
            };
        }

        private static int ResolveTotal(
            TileType type,
            int seed,
            float density01,
            int staffingCapacity,
            int tilePopulation)
        {
            // 실제 데이터 우선 사용
            if (type == TileType.Office && staffingCapacity > 0)
            {
                return staffingCapacity;
            }

            if (type == TileType.House && tilePopulation > 0)
            {
                return tilePopulation;
            }

            // 합성 대체: 타입별 기본 인원 + 시드 기반 변동
            int baseCount = type switch
            {
                TileType.House => 12 + (Mathf.Abs(seed) % 20),
                TileType.Office => 20 + (Mathf.Abs(seed) % 30),
                TileType.School => 80 + (Mathf.Abs(seed) % 40),
                TileType.Hospital => 30 + (Mathf.Abs(seed) % 20),
                _ => 10
            };

            return baseCount;
        }

    }
}
