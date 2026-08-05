using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using CityFlow.Contracts;
using UnityEngine;

namespace CityFlow.Feed
{
    public static class CitizenFeedFormatter
    {
        private static readonly Regex TokenPattern = new Regex(
            @"\{(?<token>[A-Za-z]+)\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static string Format(string template, in CitizenFeedContext context)
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return string.Empty;
            }

            CitizenFeedContext contextCopy = context;
            return TokenPattern.Replace(
                template,
                match => ResolveToken(match.Groups["token"].Value, contextCopy, match.Value));
        }

        /// <summary>
        /// SNS 글의 시각. 연·월·일은 넣지 않는다 —
        /// 한 줄 UI에 "Y1 M01 D03 17:00"은 들어가지 않아 잘리고, 날짜는 상단 HUD가
        /// 이미 보여준다. 글에 필요한 건 "몇 시에 한 말인가"뿐이다.
        /// 하루가 3분이라 시(hour)만 쓰면 여러 글이 같은 값으로 찍히므로 분까지 쓴다.
        /// </summary>
        public static string FormatTimestamp(int hour, int minute)
        {
            return $"{hour:00}:{minute:00}";
        }

        public static CitizenFeedTimePeriod GetTimePeriod(int hour)
        {
            if (hour >= 6 && hour < 10)
            {
                return CitizenFeedTimePeriod.MorningRush;
            }

            if (hour >= 10 && hour < 17)
            {
                return CitizenFeedTimePeriod.Day;
            }

            if (hour >= 17 && hour < 21)
            {
                return CitizenFeedTimePeriod.EveningRush;
            }

            if (hour >= 21 || hour < 1)
            {
                return CitizenFeedTimePeriod.Evening;
            }

            return CitizenFeedTimePeriod.Night;
        }

        public static string Decorate(
            string message,
            FeedAuthorProfileSO author,
            float decorationChance)
        {
            if (string.IsNullOrWhiteSpace(message) || author == null ||
                Random.value > Mathf.Clamp01(decorationChance))
            {
                return message;
            }

            StringBuilder builder = new StringBuilder(message.TrimEnd());
            IReadOnlyList<string> emojiSuffixes = author.EmojiSuffixes;
            IReadOnlyList<string> hashtags = author.CommonHashtags;
            bool hasEmoji = emojiSuffixes != null && emojiSuffixes.Count > 0;
            bool hasHashtag = hashtags != null && hashtags.Count > 0;

            if (hasEmoji)
            {
                builder.Append(' ')
                    .Append(emojiSuffixes[Random.Range(0, emojiSuffixes.Count)]);
            }

            if (hasHashtag && (!hasEmoji || Random.value < 0.6f))
            {
                builder.Append(' ')
                    .Append(hashtags[Random.Range(0, hashtags.Count)]);
            }

            return builder.ToString();
        }

        private static string ResolveToken(
            string token,
            in CitizenFeedContext context,
            string originalToken)
        {
            switch (token)
            {
                case "Location":
                    return $"{context.Tile.x + 1}-{context.Tile.y + 1} 교차로";
                // 교차로가 아닌 자리를 가리킬 때 쓴다. 건물이 선 타일이나 구급 현장에
                // {Location}을 쓰면 "3-5 교차로에 건물이 들어섰다"가 된다.
                case "Spot":
                    return $"{context.Tile.x + 1}-{context.Tile.y + 1} 일대";
                case "DensityPercent":
                    return $"{context.Density01 * 100f:0}%";
                case "Hour":
                    return $"{context.GameHour:00}시";
                case "TimePeriod":
                    return FormatTimePeriod(GetTimePeriod(context.GameHour));
                case "PreviousCongestion":
                    return FormatCongestion(context.PreviousCongestion);
                case "CurrentCongestion":
                    return FormatCongestion(context.CurrentCongestion);
                case "SignalChange":
                    return FormatSignalChange(context);

                case "RouteDistance":
                    return $"{context.RouteDistanceTiles:0.#}타일";
                case "VehicleCount":
                    return $"{context.ActiveVehicleCount}대";
                case "Facility":
                    return FormatInfrastructure(context.InfrastructureType);
                case "Home":
                    return FormatLocation(context.Home);
                case "OldWork":
                    return FormatLocation(context.OldWork);
                case "NewWork":
                    return FormatLocation(context.NewWork);
                default:
                    return originalToken;
            }
        }

        private static string FormatLocation(Vector2Int tile) => $"{tile.x + 1}-{tile.y + 1}";

        private static string FormatSignalChange(in CitizenFeedContext context)
        {
            StringBuilder builder = new StringBuilder();
            if (context.PreviousGreenSlots != context.CurrentGreenSlots)
            {
                builder.Append("초록 구간 ")
                    .Append(context.PreviousGreenSlots)
                    .Append("→")
                    .Append(context.CurrentGreenSlots)
                    .Append("슬롯");
            }

            if (context.PreviousOffsetSlots != context.CurrentOffsetSlots)
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append("신호 시차 ")
                    .Append(context.PreviousOffsetSlots)
                    .Append("→")
                    .Append(context.CurrentOffsetSlots)
                    .Append("슬롯");
            }

            return builder.Length > 0 ? builder.ToString() : "신호 설정 조정";
        }

        private static string FormatTimePeriod(CitizenFeedTimePeriod period)
        {
            switch (period)
            {
                case CitizenFeedTimePeriod.MorningRush:
                    return "출근 시간";
                case CitizenFeedTimePeriod.Day:
                    return "낮 시간";
                case CitizenFeedTimePeriod.EveningRush:
                    return "퇴근 시간";
                case CitizenFeedTimePeriod.Evening:
                    return "저녁";
                default:
                    return "늦은 밤";
            }
        }

        private static string FormatCongestion(CongestionLevel level)
        {
            switch (level)
            {
                case CongestionLevel.Jam:
                    return "정체";
                case CongestionLevel.Slow:
                    return "서행";
                default:
                    return "원활";
            }
        }

        private static string FormatInfrastructure(CitizenFeedInfrastructureType infrastructureType)
        {
            switch (infrastructureType)
            {
                case CitizenFeedInfrastructureType.Signal:
                    return "신호등";
                case CitizenFeedInfrastructureType.Roundabout:
                    return "회전교차로";
                case CitizenFeedInfrastructureType.Overpass:
                    return "입체교차로";
                case CitizenFeedInfrastructureType.PriorityRoad:
                    return "우선도로";
                default:
                    return "교통 시설";
            }
        }
    }
}
