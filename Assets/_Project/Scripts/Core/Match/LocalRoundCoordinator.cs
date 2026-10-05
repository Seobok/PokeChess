using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    public sealed class LocalRoundResult
    {
        public int Round { get; }
        public BattleResult Result { get; }
        public BattleEndReason Reason { get; }
        public long EndTick { get; }
        private readonly Dictionary<string,RoundIncome> income=new Dictionary<string,RoundIncome>();
        private readonly Dictionary<string,LevelProgress> xp=new Dictionary<string,LevelProgress>();
        public IReadOnlyDictionary<string,RoundIncome> Income => income;
        public IReadOnlyDictionary<string,LevelProgress> XP => xp;
        internal LocalRoundResult(int round,BattleResult result,BattleEndReason reason,long tick)
        { Round=round;Result=result;Reason=reason;EndTick=tick; }
        internal void Record(RoundIncome value) => income[value.PlayerId]=value;
        internal void Record(LevelProgress value) => xp[value.PlayerId]=value;
    }

    // Local two-player sandbox. Full-match pairing, HP damage and elimination are later work.
    public sealed class LocalRoundCoordinator
    {
        private readonly PokemonCatalog catalog;
        private readonly EconomySystem economy=new EconomySystem();
        private readonly LevelSystem levels=new LevelSystem();
        private readonly ShopSystem shops=new ShopSystem();
        private BattleSimulation simulation;
        private bool refreshing;
        private readonly List<UnitRankedUp> preparationRankUps=new List<UnitRankedUp>();
        public IReadOnlyList<UnitRankedUp> PreparationRankUps => preparationRankUps.AsReadOnly();
        public MatchState Match { get; }
        public BattleState Battle { get; private set; }
        public LocalRoundResult LastResult { get; private set; }
        public bool NextPreparationPending => refreshing;
        public bool IsSettled => LastResult!=null && LastResult.Round==Match.RoundNumber &&
            Match.Players.All(p=>p.LastEconomyRound==Match.RoundNumber && p.LastAutomaticXPRound==Match.RoundNumber);
        public LocalRoundCoordinator(MatchState match,PokemonCatalog catalog)
        {
            Match=match??throw new ArgumentNullException(nameof(match));this.catalog=catalog??throw new ArgumentNullException(nameof(catalog));
            if(match.Players.Count!=2 || match.Rules.BoardWidth!=7 || match.Rules.BoardHeight!=4)
                throw new ArgumentException("Local round loop requires two players and 7x4 preparation boards.");
        }
        public bool StartCombat(int expectedRound)
        {
            if(Match.Phase!=MatchPhase.Preparation || Match.RoundNumber!=expectedRound || refreshing) return false;
            if(Match.Players.Any(p=>p.IsEliminated || p.HP<=0)) return false;
            var one=Match.Players[0].Units.Where(u=>u.Placement.Kind==PlacementKind.Board).ToArray();
            var two=Match.Players[1].Units.Where(u=>u.Placement.Kind==PlacementKind.Board).ToArray();
            BattleState battle=null;BattleSimulation nextSimulation=null;
            if(one.Length>0 && two.Length>0)
            {
                var setup=one.Select(u=>new BattleUnitSetup(u,1,u.Placement.Position.Value))
                    .Concat(two.Select(u=>new BattleUnitSetup(u,2,HexCoordinates.MirrorCombat(u.Placement.Position.Value))));
                ulong seed=unchecked(Match.MatchSeed+(ulong)expectedRound*0x9E3779B97F4A7C15UL);
                battle=BattleStateFactory.Create(Match.MatchId+"-round-"+expectedRound,expectedRound,seed,30,catalog,setup);
                nextSimulation=new BattleSimulation(battle);
            }
            Match.TransitionTo(MatchPhase.Combat);Battle=battle;simulation=nextSimulation;LastResult=null;
            if(battle==null)
            {
                var result=one.Length==0 && two.Length==0 ? BattleResult.Draw : one.Length==0 ? BattleResult.TeamTwoWin : BattleResult.TeamOneWin;
                Finish(result,BattleEndReason.Elimination,0);
            }
            return true;
        }
        public void Step()
        {
            if(Match.Phase==MatchPhase.Result) { SettleResult();return; }
            if(Match.Phase!=MatchPhase.Combat) return;
            simulation.Step();
            if(Battle.Result!=BattleResult.InProgress) Finish(Battle.Result,Battle.EndReason,Battle.EndTick.Value);
        }
        private void Finish(BattleResult result,BattleEndReason reason,long tick)
        {
            LastResult=new LocalRoundResult(Match.RoundNumber,result,reason,tick);
            Match.TransitionTo(MatchPhase.Result);SettleResult();
        }
        private RoundOutcome Outcome(int index)
        {
            if(LastResult.Result==BattleResult.Draw)return RoundOutcome.Draw;
            bool win=(LastResult.Result==BattleResult.TeamOneWin)==(index==0);
            return win ? RoundOutcome.Win : RoundOutcome.Loss;
        }
        public void SettleResult()
        {
            if(Match.Phase!=MatchPhase.Result || LastResult==null || LastResult.Round!=Match.RoundNumber)
                throw new InvalidOperationException("No current completed round.");
            // Validate every recipient before the first payout. Resume only unfinished work on retry.
            for(int i=0;i<Match.Players.Count;i++)
            {
                var p=Match.Players[i];
                if(p.LastEconomyRound!=Match.RoundNumber) economy.Preview(p,Match.RoundNumber,Outcome(i));
                if(p.LastAutomaticXPRound!=Match.RoundNumber)
                {
                    if((long)p.LastAutomaticXPRound+1!=Match.RoundNumber)throw new InvalidOperationException("Automatic XP round gap.");
                    levels.PreviewXP(p,levels.Rules.AutomaticXP);
                }
            }
            for(int i=0;i<Match.Players.Count;i++)
            {
                var p=Match.Players[i];
                if(p.LastEconomyRound!=Match.RoundNumber) LastResult.Record(economy.Settle(p,Match.RoundNumber,Outcome(i)));
                if(p.LastAutomaticXPRound!=Match.RoundNumber) LastResult.Record(levels.AwardAutomaticXP(p,Match.RoundNumber));
            }
        }
        public bool NextRound(int completedRound)
        {
            if(!refreshing)
            {
                if(Match.Phase!=MatchPhase.Result || completedRound!=Match.RoundNumber || !IsSettled)return false;
                Match.TransitionTo(MatchPhase.Preparation);refreshing=true;preparationRankUps.Clear();
            }
            else if(Match.RoundNumber!=completedRound+1)return false;
            foreach(var player in Match.Players)preparationRankUps.AddRange(new RankUpSystem(catalog).Resolve(Match,player.PlayerId,player.PlacementRevision));
            foreach(var player in Match.Players)if(player.Shop.LastRefreshRound!=Match.RoundNumber)shops.RefreshForRound(Match,player.PlayerId);
            refreshing=false;Battle=null;simulation=null;return true;
        }
    }
}
