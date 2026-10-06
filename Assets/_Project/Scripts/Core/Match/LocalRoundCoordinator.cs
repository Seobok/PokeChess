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
        public UnitActionRuntime GetActionRuntime(string unitId) => simulation.GetRuntime(unitId);
        private BattleResult? surrenderResult;
        public BattleResult Result => surrenderResult ?? Battle?.Result ?? emptyResult;
        public BattleEndReason Reason => surrenderResult.HasValue ? BattleEndReason.Surrender : Battle?.EndReason ?? BattleEndReason.Elimination;
        public long EndTick => Battle?.EndTick ?? 0;
        public bool IsComplete => Result!=BattleResult.InProgress;
        private readonly BattleResult emptyResult;
        private int oneSurvivors,twoSurvivors;
        private bool survivorCountsFrozen;
        internal LocalPairBattle(RoundPairing pair,BattleState battle,BattleResult result,ShadowBoardSnapshot shadowSnapshot=null,int oneCount=0,int twoCount=0,
            SkillCatalog skills=null,StatusCatalog statuses=null)
        { Pairing=pair;Battle=battle;emptyResult=result;ShadowSnapshot=shadowSnapshot;oneSurvivors=oneCount;twoSurvivors=twoCount;if(battle!=null)simulation=new BattleSimulation(battle,skillCatalog:skills,statusCatalog:statuses);FreezeSurvivors(); }
        internal void Step() { if(!IsComplete)simulation.Step();FreezeSurvivors(); }
        internal void Surrender(string playerId)
        {
            int team=Pairing.PlayerOneId==playerId ? 1 : 2;
            if(Battle!=null)
            {
                foreach(var unit in Battle.Units.Where(u=>u.TeamId==team && u.IsAlive))
                {
                    unit.SetVitals(0,unit.CurrentEnergy);unit.DeathCause=DeathReason.Surrender;
                    if(Battle.Result!=BattleResult.InProgress)Battle.TryRemoveUnit(unit.UnitInstanceId);
                }
                if(Battle.Result==BattleResult.InProgress)simulation.Step();
            }
            bool opponentAlive=Battle!=null ? Battle.Units.Any(u=>u.TeamId!=team && u.IsAlive && u.IsOnBoard) : (team==1 ? twoSurvivors : oneSurvivors)>0;
            surrenderResult=opponentAlive ? (team==1 ? BattleResult.TeamTwoWin : BattleResult.TeamOneWin) : BattleResult.Draw;
            survivorCountsFrozen=false;FreezeSurvivors();
        }
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

    public sealed partial class LocalRoundCoordinator : IRoundFlowRuntime
    {
        private readonly PokemonCatalog catalog;
        private readonly SkillCatalog skills;
        private readonly StatusCatalog statuses;
        private readonly EconomySystem economy=new EconomySystem();
        private readonly LevelSystem levels=new LevelSystem();
        private readonly PlayerDamageSystem playerDamage;
        private readonly ShopSystem shops=new ShopSystem();
        private readonly PairingSystem pairing=new PairingSystem();
        private readonly EliminationSystem elimination=new EliminationSystem();
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
            LastResult.PlayerResults.Keys.Select(Match.GetPlayer).All(p=>p.Elimination?.Reason==EliminationReason.Surrender || p.LastEconomyRound==Match.RoundNumber && p.LastAutomaticXPRound==Match.RoundNumber && p.LastDamageRound==Match.RoundNumber) &&
            Match.Players.All(p=>p.HP>0 || p.IsEliminated);
        public LocalRoundCoordinator(MatchState match,PokemonCatalog catalog,PlayerDamageRules damageRules=null,
            SkillCatalog skillCatalog=null,StatusCatalog statusCatalog=null)
        {
            Match=match??throw new ArgumentNullException(nameof(match));this.catalog=catalog??throw new ArgumentNullException(nameof(catalog));playerDamage=new PlayerDamageSystem(damageRules);
            skills=skillCatalog;statuses=statusCatalog;
            if(match.Rules.BoardWidth!=7 || match.Rules.BoardHeight!=4)
                throw new ArgumentException("Local round loop requires 7x4 preparation boards.");
            if(match.Phase==MatchPhase.Preparation) {PreparePairings(match.RoundNumber);TryFinishMatch(match.RoundNumber);}
        }
        public RoundPairingPlan PreparePairings(int expectedRound) => pairing.PrepareRound(Match,expectedRound);
        public LocalPairBattle GetBattleFor(string playerId) => battles.FirstOrDefault(b=>b.Pairing.Contains(playerId));
        public bool StartCombat(int expectedRound)
        {
            if(Match.Phase!=MatchPhase.Preparation || Match.RoundNumber!=expectedRound || !PreparationReady)return false;
            if(TryFinishMatch(expectedRound))return false;
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
                var two=pair.IsShadow ? Array.Empty<UnitInstance>() : DeployedUnits(Match.GetPlayer(pair.PlayerTwoId),deployments[pair.PlayerTwoId]);
                BattleState battle=null;
                var shadowSnapshot=pair.IsShadow ? plan.FrozenShadow ?? ShadowBoardSnapshot.Capture(Match.GetPlayer(pair.PlayerTwoId),expectedRound,catalog,deployments[pair.PlayerTwoId]) : null;
                int twoCount=shadowSnapshot?.Units.Count ?? two.Length;
                if(one.Length>0 && twoCount>0)
                {
                    var setup=one.Select(u=>new BattleUnitSetup(u,1,DeploymentPosition(u,deployments[pair.PlayerOneId])))
                        .Concat(two.Select(u=>new BattleUnitSetup(u,2,HexCoordinates.MirrorCombat(DeploymentPosition(u,deployments[pair.PlayerTwoId])))));
                    // Canonical pair order is stable; a distinct seed isolates each battle's RNG.
                    ulong seed=BattleSeed(expectedRound,pair);
                    string battleId=Match.MatchId+"-round-"+expectedRound+"-pair-"+nextBattles.Count;
                    battle=pair.IsShadow ? shadowSnapshot.CreateBattle(battleId,seed,TickRate,catalog,setup.Where(s=>s.TeamId==1))
                        : BattleStateFactory.Create(battleId,expectedRound,seed,TickRate,catalog,setup);
                }
                var emptyResult=one.Length==0 && twoCount==0 ? BattleResult.Draw : one.Length==0 ? BattleResult.TeamTwoWin : BattleResult.TeamOneWin;
                nextBattles.Add(new LocalPairBattle(pair,battle,emptyResult,shadowSnapshot,one.Length,twoCount,skills,statuses));
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
            if(Match.Phase==MatchPhase.Finished && Match.FinalResult!=null)return;
            if(Match.Phase!=MatchPhase.Result || LastResult==null || LastResult.Round!=Match.RoundNumber)
                throw new InvalidOperationException("No current completed round.");
            var recipients=LastResult.PlayerResults.Keys.Select(Match.GetPlayer).Where(p=>p.Elimination?.Reason!=EliminationReason.Surrender).ToArray();
            var damagePlans=recipients.ToDictionary(p=>p.PlayerId,p=>playerDamage.Preview(p,Match.RoundNumber,LastResult.PlayerResults[p.PlayerId].Outcome,LastResult.PlayerResults[p.PlayerId].OpponentSurvivors));
            foreach(var p in recipients)
            {
                if(damagePlans[p.PlayerId].HPAfter==0 && !p.IsEliminated)EliminationSystem.ValidateRelease(Match,p);
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
            elimination.SettleDamage(Match);
            TryFinishMatch(Match.RoundNumber);
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



