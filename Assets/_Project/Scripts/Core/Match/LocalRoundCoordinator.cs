using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    public sealed class PlayerRoundResult
    {
        public string PlayerId { get; }
        public string OpponentId { get; }
        public RoundOutcome Outcome { get; }
        public bool IsShadow { get; }
        public int OpponentSurvivors { get; }
        public BattleEndReason Reason { get; }
        public long EndTick { get; }
        internal PlayerRoundResult(string id,string opponent,RoundOutcome outcome,BattleEndReason reason,long tick,bool isShadow=false,int opponentSurvivors=0)
        { PlayerId=id;OpponentId=opponent;Outcome=outcome;Reason=reason;EndTick=tick;IsShadow=isShadow;OpponentSurvivors=opponentSurvivors; }
    }

    public sealed class LocalPairBattle
    {
        private readonly BattleSimulation simulation;
        public RoundPairing Pairing { get; }
        public bool IsShadow => Pairing.IsShadow;
        public ShadowBoardSnapshot ShadowSnapshot { get; }
        public BattleState Battle { get; }
        public BattleResult Result => Battle?.Result ?? emptyResult;
        public BattleEndReason Reason => Battle?.EndReason ?? BattleEndReason.Elimination;
        public long EndTick => Battle?.EndTick ?? 0;
        public bool IsComplete => Result!=BattleResult.InProgress;
        private readonly BattleResult emptyResult;
        private int oneSurvivors,twoSurvivors;
        private bool survivorCountsFrozen;
        internal LocalPairBattle(RoundPairing pair,BattleState battle,BattleResult result,ShadowBoardSnapshot shadowSnapshot=null,int oneCount=0,int twoCount=0)
        { Pairing=pair;Battle=battle;emptyResult=result;ShadowSnapshot=shadowSnapshot;oneSurvivors=oneCount;twoSurvivors=twoCount;if(battle!=null)simulation=new BattleSimulation(battle);FreezeSurvivors(); }
        internal void Step() { if(!IsComplete)simulation.Step();FreezeSurvivors(); }
        private void FreezeSurvivors()
        {
            if(!IsComplete || survivorCountsFrozen)return;
            if(Battle!=null)
            {
                oneSurvivors=Battle.Units.Count(u=>u.TeamId==1 && u.IsAlive && u.IsOnBoard);
                twoSurvivors=Battle.Units.Count(u=>u.TeamId==2 && u.IsAlive && u.IsOnBoard);
            }
            survivorCountsFrozen=true;
        }
        public int OpponentSurvivorsOf(string playerId)
        {
            if(!Pairing.Contains(playerId))throw new ArgumentException("Player is not in this battle.",nameof(playerId));
            if(!IsComplete)throw new InvalidOperationException("Battle is still running.");
            FreezeSurvivors();return Pairing.PlayerOneId==playerId ? twoSurvivors : oneSurvivors;
        }
        public RoundOutcome OutcomeOf(string playerId)
        {
            if(!Pairing.Contains(playerId))throw new ArgumentException("Player is not in this battle.",nameof(playerId));
            if(!IsComplete)throw new InvalidOperationException("Battle is still running.");
            if(Result==BattleResult.Draw)return RoundOutcome.Draw;
            return (Result==BattleResult.TeamOneWin)==(Pairing.PlayerOneId==playerId) ? RoundOutcome.Win : RoundOutcome.Loss;
        }
    }

    public sealed class LocalRoundResult
    {
        public int Round { get; }
        // Compatibility for the original two-player sandbox: these refer to its first pair.
        public BattleResult Result { get; }
        public BattleEndReason Reason { get; }
        public long EndTick { get; }
        public IReadOnlyDictionary<string,PlayerRoundResult> PlayerResults { get; }
        private readonly Dictionary<string,RoundIncome> income=new Dictionary<string,RoundIncome>();
        private readonly Dictionary<string,PlayerDamageResult> damage=new Dictionary<string,PlayerDamageResult>();
        public IReadOnlyDictionary<string,PlayerDamageResult> Damage => new ReadOnlyDictionary<string,PlayerDamageResult>(damage);
        private readonly Dictionary<string,LevelProgress> xp=new Dictionary<string,LevelProgress>();
        public IReadOnlyDictionary<string,RoundIncome> Income => new ReadOnlyDictionary<string,RoundIncome>(income);
        public IReadOnlyDictionary<string,LevelProgress> XP => new ReadOnlyDictionary<string,LevelProgress>(xp);
        internal LocalRoundResult(int round,IReadOnlyList<LocalPairBattle> battles)
        {
            Round=round;Result=battles[0].Result;Reason=battles[0].Reason;EndTick=battles[0].EndTick;
            var results=new Dictionary<string,PlayerRoundResult>(StringComparer.Ordinal);
            foreach(var battle in battles)
                foreach(var id in battle.Pairing.ParticipantIds)
                    results.Add(id,new PlayerRoundResult(id,battle.Pairing.OpponentOf(id),battle.OutcomeOf(id),battle.Reason,battle.EndTick,battle.IsShadow,battle.OpponentSurvivorsOf(id)));
            PlayerResults=new ReadOnlyDictionary<string,PlayerRoundResult>(results);
        }
        internal void Record(RoundIncome value) => income[value.PlayerId]=value;
        internal void Record(PlayerDamageResult value) => damage[value.PlayerId]=value;
        internal void Record(LevelProgress value) => xp[value.PlayerId]=value;
    }

    public sealed class LocalRoundCoordinator : IRoundFlowRuntime
    {
        private readonly PokemonCatalog catalog;
        private readonly EconomySystem economy=new EconomySystem();
        private readonly LevelSystem levels=new LevelSystem();
        private readonly PlayerDamageSystem playerDamage;
        private readonly ShopSystem shops=new ShopSystem();
        private readonly PairingSystem pairing=new PairingSystem();
        private IReadOnlyList<LocalPairBattle> battles=Array.Empty<LocalPairBattle>();
        private bool refreshing;
        private readonly List<UnitRankedUp> preparationRankUps=new List<UnitRankedUp>();
        public IReadOnlyList<UnitRankedUp> PreparationRankUps => preparationRankUps.AsReadOnly();
        public MatchState Match { get; }
        public IReadOnlyList<LocalPairBattle> Battles => battles;
        public BattleState Battle => battles.FirstOrDefault()?.Battle;
        public LocalRoundResult LastResult { get; private set; }
        public bool NextPreparationPending => refreshing;
        public int TickRate => 30;
        private IEnumerable<PlayerState> Survivors => Match.Players.Where(p=>p.HP>0 && !p.IsEliminated);
        public bool PreparationReady => Match.Phase==MatchPhase.Preparation && !refreshing &&
            Survivors.All(p=>p.Shop.IsInitialized && p.Shop.LastRefreshRound==Match.RoundNumber);
        public bool IsSettled => LastResult!=null && LastResult.Round==Match.RoundNumber &&
            LastResult.PlayerResults.Keys.Select(Match.GetPlayer).All(p=>p.LastEconomyRound==Match.RoundNumber && p.LastAutomaticXPRound==Match.RoundNumber && p.LastDamageRound==Match.RoundNumber);
        public LocalRoundCoordinator(MatchState match,PokemonCatalog catalog,PlayerDamageRules damageRules=null)
        {
            Match=match??throw new ArgumentNullException(nameof(match));this.catalog=catalog??throw new ArgumentNullException(nameof(catalog));playerDamage=new PlayerDamageSystem(damageRules);
            if(match.Rules.BoardWidth!=7 || match.Rules.BoardHeight!=4)
                throw new ArgumentException("Local round loop requires 7x4 preparation boards.");
            if(match.Phase==MatchPhase.Preparation)PreparePairings(match.RoundNumber);
        }
        public RoundPairingPlan PreparePairings(int expectedRound) => pairing.PrepareRound(Match,expectedRound);
        public LocalPairBattle GetBattleFor(string playerId) => battles.FirstOrDefault(b=>b.Pairing.Contains(playerId));
        public bool StartCombat(int expectedRound)
        {
            if(Match.Phase!=MatchPhase.Preparation || Match.RoundNumber!=expectedRound || !PreparationReady)return false;
            var plan=PreparePairings(expectedRound);
            if(plan.NoBattleRequired)return false;
            if(plan.RequiresShadow && plan.ShadowPair==null)throw new InvalidOperationException("Missing Shadow source.");
            var players=plan.SurvivorIds.Select(Match.GetPlayer).ToArray();
            // Validate every deployment and construct every snapshot before committing any placement.
            var deployments=players.ToDictionary(p=>p.PlayerId,PlanAutomaticDeployment,StringComparer.Ordinal);
            var nextBattles=new List<LocalPairBattle>();
            foreach(var pair in plan.Pairs.Concat(plan.ShadowPair==null ? Array.Empty<RoundPairing>() : new[]{plan.ShadowPair}))
            {
                var one=DeployedUnits(Match.GetPlayer(pair.PlayerOneId),deployments[pair.PlayerOneId]);
                var two=DeployedUnits(Match.GetPlayer(pair.PlayerTwoId),deployments[pair.PlayerTwoId]);
                BattleState battle=null;
                var shadowSnapshot=pair.IsShadow ? ShadowBoardSnapshot.Capture(Match.GetPlayer(pair.PlayerTwoId),expectedRound,catalog,deployments[pair.PlayerTwoId]) : null;
                if(one.Length>0 && two.Length>0)
                {
                    var setup=one.Select(u=>new BattleUnitSetup(u,1,DeploymentPosition(u,deployments[pair.PlayerOneId])))
                        .Concat(two.Select(u=>new BattleUnitSetup(u,2,HexCoordinates.MirrorCombat(DeploymentPosition(u,deployments[pair.PlayerTwoId])))));
                    // Canonical pair order is stable; a distinct seed isolates each battle's RNG.
                    ulong seed=BattleSeed(expectedRound,pair);
                    string battleId=Match.MatchId+"-round-"+expectedRound+"-pair-"+nextBattles.Count;
                    battle=pair.IsShadow ? shadowSnapshot.CreateBattle(battleId,seed,TickRate,catalog,setup.Where(s=>s.TeamId==1))
                        : BattleStateFactory.Create(battleId,expectedRound,seed,TickRate,catalog,setup);
                }
                var emptyResult=one.Length==0 && two.Length==0 ? BattleResult.Draw : one.Length==0 ? BattleResult.TeamTwoWin : BattleResult.TeamOneWin;
                nextBattles.Add(new LocalPairBattle(pair,battle,emptyResult,shadowSnapshot,one.Length,two.Length));
            }
            foreach(var player in players)if(deployments[player.PlayerId].Count>0)player.ApplyPlacements(deployments[player.PlayerId]);
            Match.TransitionTo(MatchPhase.Combat);battles=nextBattles.AsReadOnly();LastResult=null;
            if(battles.All(b=>b.IsComplete))Finish();
            return true;
        }
        private ulong BattleSeed(int round,RoundPairing pair)
        {
            ulong hash=14695981039346656037UL;
            foreach(char c in pair.PlayerOneId)hash=unchecked((hash^c)*1099511628211UL);
            hash=unchecked((hash^0xFFFFUL)*1099511628211UL);
            foreach(char c in pair.PlayerTwoId)hash=unchecked((hash^c)*1099511628211UL);
            return unchecked(Match.MatchSeed ^ hash ^ (pair.IsShadow ? 0x5348424154544C45UL : 0x424154544C450001UL) ^ (ulong)round*0x9E3779B97F4A7C15UL);
        }
        private static UnitInstance[] DeployedUnits(PlayerState player,IReadOnlyDictionary<string,UnitPlacement> plan) =>
            player.Units.Where(u=>u.Placement.Kind==PlacementKind.Board || plan.ContainsKey(u.InstanceId)).ToArray();
        private static IReadOnlyDictionary<string,UnitPlacement> PlanAutomaticDeployment(PlayerState player)
        {
            var plan=new Dictionary<string,UnitPlacement>(StringComparer.Ordinal);
            int available=player.RemainingDeploymentCapacity;
            if(available<=0)return plan;
            var bench=player.Units.Where(u=>u.Placement.Kind==PlacementKind.Bench).OrderBy(u=>u.Placement.BenchSlot.Value).Take(available).ToArray();
            var cells=player.BoardCells.Where(c=>c.IsEmpty).Take(bench.Length).ToArray();
            for(int i=0;i<cells.Length;i++)plan.Add(bench[i].InstanceId,UnitPlacement.OnBoard(cells[i].Position));
            if(plan.Count>0)player.ValidatePlacementChanges(1);
            return plan;
        }
        private static BoardPosition DeploymentPosition(UnitInstance unit,IReadOnlyDictionary<string,UnitPlacement> plan)
            => plan.TryGetValue(unit.InstanceId,out var placement) ? placement.Position.Value : unit.Placement.Position.Value;
        public void Step()
        {
            if(Match.Phase==MatchPhase.Result) { SettleResult();return; }
            if(Match.Phase!=MatchPhase.Combat)return;
            foreach(var battle in battles)battle.Step();
            if(battles.All(b=>b.IsComplete))Finish();
        }
        private void Finish()
        {
            LastResult=new LocalRoundResult(Match.RoundNumber,battles);
            Match.TransitionTo(MatchPhase.Result);SettleResult();
        }
        public void SettleResult()
        {
            if(Match.Phase!=MatchPhase.Result || LastResult==null || LastResult.Round!=Match.RoundNumber)
                throw new InvalidOperationException("No current completed round.");
            var recipients=LastResult.PlayerResults.Keys.Select(Match.GetPlayer).ToArray();
            var damagePlans=recipients.ToDictionary(p=>p.PlayerId,p=>playerDamage.Preview(p,Match.RoundNumber,LastResult.PlayerResults[p.PlayerId].Outcome,LastResult.PlayerResults[p.PlayerId].OpponentSurvivors));
            foreach(var p in recipients)
            {
                if(p.LastEconomyRound!=Match.RoundNumber)economy.Preview(p,Match.RoundNumber,LastResult.PlayerResults[p.PlayerId].Outcome);
                if(p.LastAutomaticXPRound!=Match.RoundNumber)
                {
                    if((long)p.LastAutomaticXPRound+1!=Match.RoundNumber)throw new InvalidOperationException("Automatic XP round gap.");
                    levels.PreviewXP(p,levels.Rules.AutomaticXP);
                }
            }
            foreach(var p in recipients)
            {
                if(p.LastEconomyRound!=Match.RoundNumber)LastResult.Record(economy.Settle(p,Match.RoundNumber,LastResult.PlayerResults[p.PlayerId].Outcome));
                if(p.LastAutomaticXPRound!=Match.RoundNumber)LastResult.Record(levels.AwardAutomaticXP(p,Match.RoundNumber));
                playerDamage.Apply(p,damagePlans[p.PlayerId]);LastResult.Record(damagePlans[p.PlayerId]);
            }
        }
        public bool NextRound(int completedRound)
        {
            if(!refreshing)
            {
                if(Match.Phase!=MatchPhase.Result || completedRound!=Match.RoundNumber || !IsSettled)return false;
                Match.TransitionTo(MatchPhase.Preparation);refreshing=true;preparationRankUps.Clear();
                PreparePairings(Match.RoundNumber);
            }
            else if(Match.RoundNumber!=completedRound+1)return false;
            foreach(var player in Survivors)preparationRankUps.AddRange(new RankUpSystem(catalog).Resolve(Match,player.PlayerId,player.PlacementRevision));
            foreach(var player in Survivors)if(player.Shop.LastRefreshRound!=Match.RoundNumber)shops.RefreshForRound(Match,player.PlayerId);
            PreparePairings(Match.RoundNumber);
            refreshing=false;battles=Array.Empty<LocalPairBattle>();return true;
        }
    }
}



