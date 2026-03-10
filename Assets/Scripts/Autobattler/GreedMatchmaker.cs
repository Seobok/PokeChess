using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;

namespace PokeChess.Autobattler
{
    /// <summary>
    /// Immutable pairing record for one combat matchup.
    /// </summary>
    public readonly struct MatchPair
    {
        public readonly PlayerRef A;
        public readonly PlayerRef B; // B==None이면 BYE/고스트
        public MatchPair(PlayerRef a, PlayerRef b) { A = a; B = b; }
        public bool IsBye => B == PlayerRef.None;
        public override string ToString() => IsBye ? $"{A} vs BYE" : $"{A} vs {B}";
    }

    /// <summary>
    /// Greedy matchmaker that avoids recent rematches when possible.
    /// </summary>
    public static class GreedyMatchmaker
    {
        /// <summary>
        /// 최근 recentExclude명 제외 매칭을 시도하되, 막히면 4→3→2→1→0으로 완화해서 항상 결과를 만듭니다.
        /// seed는 “예측 난이도”를 위해 동률 선택에만 사용(서버에서만 호출 권장).
        /// </summary>
        public static List<MatchPair> BuildPairings(
            IReadOnlyList<PlayerRef> activePlayers,
            RecentOpponentHistory history,
            int recentExclude = 4,
            int seed = 0,
            bool allowBye = true)
        {
            // 안정적인 순서를 위해 정렬(결정적)
            var players = activePlayers
                .Where(p => p != PlayerRef.None)
                .Distinct()
                .OrderBy(p => p.AsIndex)
                .ToList();

            // 인원이 0~1이면 그대로
            if (players.Count <= 1)
                return players.Count == 1 ? new List<MatchPair> { new(players[0], PlayerRef.None) } : new List<MatchPair>();

            // recentExclude부터 0까지 단계적으로 시도
            for (int k = Math.Max(0, recentExclude); k >= 0; k--)
            {
                if (TryBuild(players, history, k, seed, allowBye, out var pairs))
                    return pairs;
            }

            // 마지막 안전망: 완전 랜덤 셔플 후 순차 매칭
            return FallbackRandom(players, seed, allowBye);
        }

        private static bool TryBuild(
            List<PlayerRef> players,
            RecentOpponentHistory history,
            int recentExclude,
            int seed,
            bool allowBye,
            out List<MatchPair> result)
        {
            result = new List<MatchPair>();
            var rng = new Random(seed ^ (recentExclude * 10007));

            var unpaired = new HashSet<PlayerRef>(players);

            // 홀수면 BYE 가능 여부
            if (!allowBye && (unpaired.Count % 2 == 1))
                return false;

            while (unpaired.Count >= 2)
            {
                // 1) 후보가 가장 적은 플레이어(MRV)부터 선택 → 막힘 방지에 강함
                PlayerRef pick = PlayerRef.None;
                List<PlayerRef> pickCandidates = null;
                int bestCount = int.MaxValue;

                foreach (var p in unpaired)
                {
                    var cand = GetCandidates(p, unpaired, history, recentExclude);
                    int c = cand.Count;

                    if (c < bestCount)
                    {
                        bestCount = c;
                        pick = p;
                        pickCandidates = cand;

                        if (bestCount == 0) break;
                    }
                }

                // 후보가 0이면 이 recentExclude로는 실패
                if (pick == PlayerRef.None || pickCandidates == null || pickCandidates.Count == 0)
                    return false;

                // 2) pick의 후보 중에서 "상대도 후보가 적은 쪽"을 우선(추가로 막힘 방지)
                //    동률은 seed rng로 섞어 예측 난이도 ↑
                ShuffleInPlace(pickCandidates, rng);

                PlayerRef chosen = PlayerRef.None;
                int chosenOppCandCount = int.MaxValue;

                foreach (var opp in pickCandidates)
                {
                    if (!unpaired.Contains(opp)) continue;

                    var oppCand = GetCandidates(opp, unpaired, history, recentExclude);
                    int oppCount = oppCand.Count;

                    if (oppCount < chosenOppCandCount)
                    {
                        chosenOppCandCount = oppCount;
                        chosen = opp;
                    }
                }

                if (chosen == PlayerRef.None)
                    return false;

                result.Add(new MatchPair(pick, chosen));
                unpaired.Remove(pick);
                unpaired.Remove(chosen);
            }

            // 홀수 남으면 BYE
            if (unpaired.Count == 1)
            {
                if (!allowBye) return false;
                var leftover = unpaired.First();
                result.Add(new MatchPair(leftover, PlayerRef.None));
            }

            return true;
        }

        /// <summary>
        /// 후보: 아직 안 매칭된 플레이어 중, 최근 recentExclude명에 포함되지 않는 상대
        /// </summary>
        private static List<PlayerRef> GetCandidates(
            PlayerRef player,
            HashSet<PlayerRef> unpaired,
            RecentOpponentHistory history,
            int recentExclude)
        {
            var recent = history.GetRecent(player);
            // recentExclude만큼만 제한하고 싶으면 history 자체 capacity를 4로 두고,
            // 여기서 recentExclude가 4보다 작을 때만 prefix만 비교하는 방식도 가능.
            // 간단히: history cap을 4로 두고 recentExclude==4일 때만 의미가 큼.
            // (최근 3/2/1 완화는 아래처럼 구현)
            var recentList = recent.ToList();
            if (recentExclude < recentList.Count)
            {
                // 최근 N명만 제외
                recentList = recentList.Skip(Math.Max(0, recentList.Count - recentExclude)).ToList();
            }

            var candidates = new List<PlayerRef>();

            foreach (var opp in unpaired)
            {
                if (opp == player) continue;

                // a가 최근에 opp를 만난 적 있으면 제외
                bool met = false;
                foreach (var x in recentList)
                {
                    if (x == opp) { met = true; break; }
                }
                if (met) continue;

                // 기록이 양방향으로 들어가므로 사실상 위 체크만으로 충분하지만,
                // 안전하게 반대 방향도 확인하고 싶으면 아래 한 줄 추가 가능:
                // if (history.HasMetRecently(opp, player)) continue;

                candidates.Add(opp);
            }

            return candidates;
        }

        private static List<MatchPair> FallbackRandom(List<PlayerRef> players, int seed, bool allowBye)
        {
            var rng = new Random(seed ^ 0x51ED);
            var list = players.ToList();
            ShuffleInPlace(list, rng);

            var res = new List<MatchPair>();
            int i = 0;
            for (; i + 1 < list.Count; i += 2)
                res.Add(new MatchPair(list[i], list[i + 1]));

            if (i < list.Count)
            {
                if (allowBye) res.Add(new MatchPair(list[i], PlayerRef.None));
            }
            return res;
        }

        private static void ShuffleInPlace<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
