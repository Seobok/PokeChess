using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Random;

namespace PokeChess.Core.Match
{
    public sealed class RoundPairing
    {
        public string PlayerOneId { get; }
        public string PlayerTwoId { get; }
        internal RoundPairing(string one, string two) { PlayerOneId=one; PlayerTwoId=two; }
        public bool Contains(string id) => PlayerOneId==id || PlayerTwoId==id;
        public string OpponentOf(string id) => PlayerOneId==id ? PlayerTwoId : PlayerTwoId==id ? PlayerOneId : throw new ArgumentException("Player is not in this pair.",nameof(id));
    }

    // Host-internal. Preparation pairings must not be included in public client projections.
    public sealed class RoundPairingPlan
    {
        public int Round { get; }
        public IReadOnlyList<string> SurvivorIds { get; }
        public IReadOnlyList<RoundPairing> Pairs { get; }
        public string UnpairedPlayerId { get; }
        public bool RequiresShadow => UnpairedPlayerId!=null && SurvivorIds.Count>1;
        public bool NoBattleRequired => SurvivorIds.Count<2;
        public int PreviousRoundRematches { get; }
        public int TwoRoundsAgoRematches { get; }
        internal RoundPairingPlan(int round,string[] survivors,RoundPairing[] pairs,string unpaired,int recent,int older)
        {
            Round=round;SurvivorIds=Array.AsReadOnly(survivors);Pairs=Array.AsReadOnly(pairs);
            UnpairedPlayerId=unpaired;PreviousRoundRematches=recent;TwoRoundsAgoRematches=older;
        }
    }

    public sealed class PairingSystem
    {
        public RoundPairingPlan PrepareRound(MatchState match,int expectedRound)
        {
            Validate(match,expectedRound);
            var ids=Survivors(match);
            if(match.PairingPlan!=null && match.PairingPlan.Round==expectedRound && match.PairingPlan.SurvivorIds.SequenceEqual(ids))
                return match.PairingPlan;
            var plan=Calculate(match,expectedRound);
            match.CommitPairings(plan);
            return plan;
        }
        public RoundPairingPlan Calculate(MatchState match,int expectedRound)
        {
            Validate(match,expectedRound);
            var ids=Survivors(match);
            if(ids.Length<2)return new RoundPairingPlan(expectedRound,ids,Array.Empty<RoundPairing>(),ids.FirstOrDefault(),0,0);
            var candidates=new List<RoundPairingPlan>();
            int bestRecent=int.MaxValue,bestOlder=int.MaxValue;
            Action<List<RoundPairing>,string> consider=(pairs,unpaired)=>
            {
                int recent=0,older=0;
                foreach(var pair in pairs)
                {
                    if(WasPair(match,expectedRound-1,pair))recent++;
                    if(WasPair(match,expectedRound-2,pair))older++;
                }
                if(recent>bestRecent || recent==bestRecent && older>bestOlder)return;
                if(recent<bestRecent || older<bestOlder) { candidates.Clear();bestRecent=recent;bestOlder=older; }
                candidates.Add(new RoundPairingPlan(expectedRound,ids,pairs.ToArray(),unpaired,recent,older));
            };
            if(ids.Length%2==0)Enumerate(ids,new List<RoundPairing>(),p=>consider(p,null));
            else foreach(var unpaired in ids)Enumerate(ids.Where(id=>id!=unpaired).ToArray(),new List<RoundPairing>(),p=>consider(p,unpaired));
            var random=new DeterministicRandom(unchecked(match.MatchSeed ^ 0x50414952494E4701UL ^ (ulong)expectedRound*0x9E3779B97F4A7C15UL));
            return candidates[random.NextInt(candidates.Count)];
        }
        private static string[] Survivors(MatchState match) => match.Players.Where(p=>p.HP>0 && !p.IsEliminated).Select(p=>p.PlayerId).OrderBy(id=>id,StringComparer.Ordinal).ToArray();
        private static bool WasPair(MatchState match,int round,RoundPairing pair) =>
            match.PairingHistory.TryGetValue(round,out var prior) && prior.Pairs.Any(p=>p.Contains(pair.PlayerOneId) && p.Contains(pair.PlayerTwoId));
        private static void Validate(MatchState match,int round)
        {
            if(match==null)throw new ArgumentNullException(nameof(match));
            if(match.Phase!=MatchPhase.Preparation || round!=match.RoundNumber)throw new InvalidOperationException("Pairing requires the current preparation round.");
        }
        private static void Enumerate(string[] remaining,List<RoundPairing> pairs,Action<List<RoundPairing>> consider)
        {
            if(remaining.Length==0) { consider(pairs);return; }
            for(int i=1;i<remaining.Length;i++)
            {
                pairs.Add(new RoundPairing(remaining[0],remaining[i]));
                Enumerate(remaining.Where((id,index)=>index!=0 && index!=i).ToArray(),pairs,consider);
                pairs.RemoveAt(pairs.Count-1);
            }
        }
    }
}

