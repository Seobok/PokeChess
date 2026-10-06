using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Random;

namespace PokeChess.Core.Match
{
    public enum SimulationStatus { Running, Completed, RoundLimit, TickLimit, Faulted, Cancelled }

    public sealed class LocalSimulationSettings
    {
        public ulong Seed { get; }
        public int PlayerCount { get; }
        public int MaxRounds { get; }
        public long MaxCombatTicks { get; }
        public RoundFlowRules FlowRules { get; }
        public LocalSimulationSettings(ulong seed = 123, int playerCount = 8, int maxRounds = 200,
            long maxCombatTicks = 270000, RoundFlowRules flowRules = null)
        {
            if (playerCount < 2 || playerCount > 8) throw new ArgumentOutOfRangeException(nameof(playerCount));
            if (maxRounds < 1) throw new ArgumentOutOfRangeException(nameof(maxRounds));
            if (maxCombatTicks < 1) throw new ArgumentOutOfRangeException(nameof(maxCombatTicks));
            Seed=seed;PlayerCount=playerCount;MaxRounds=maxRounds;MaxCombatTicks=maxCombatTicks;
            FlowRules=flowRules??new RoundFlowRules();
        }
    }

    public sealed class SimulationPlayerRound
    {
        public string PlayerId { get; }
        public int HPBefore { get; }
        public int HPAfter { get; }
        public int Damage { get; }
        public int? Placement { get; }
        public string EliminationReason { get; }
        public PlayerRoundResult Battle { get; }
        internal SimulationPlayerRound(PlayerState p,int before,LocalRoundResult result)
        {
            PlayerId=p.PlayerId;HPBefore=before;HPAfter=p.HP;
            Damage=result!=null && result.Damage.TryGetValue(p.PlayerId,out var d) ? d.TotalDamage : 0;
            Placement=p.FinalPlacement;EliminationReason=p.Elimination?.Reason.ToString();
            Battle=result!=null && result.PlayerResults.TryGetValue(p.PlayerId,out var b) ? b : null;
        }
    }
    public sealed class SimulationRoundRecord
    {
        public int Round { get; }
        public int AliveBefore { get; }
        public int AliveAfter { get; }
        public double PreparationSeconds { get; }
        public double CombatSeconds { get; }
        public double ResultSeconds { get; }
        public bool Settled { get; }
        public IReadOnlyList<SimulationPlayerRound> Players { get; }
        internal SimulationRoundRecord(LocalMatchSimulation run,double finalResultSeconds=0)
        {
            Round=run.RecordRound;AliveBefore=run.HPBefore.Count(p=>p.Value>0);AliveAfter=run.AliveCount;
            PreparationSeconds=run.Flow.PreparationElapsedSeconds-run.StartPreparation;
            CombatSeconds=run.Flow.CombatElapsedSeconds-run.StartCombat;
            ResultSeconds=run.Flow.ResultElapsedSeconds-run.StartResult+finalResultSeconds;
            var result=run.Coordinator.LastResult;
            if(result?.Round!=Round)result=null;
            Settled=result!=null && run.Coordinator.IsSettled;
            Players=Array.AsReadOnly(run.Match.Players.Select(p=>new SimulationPlayerRound(p,run.HPBefore[p.PlayerId],result)).ToArray());
        }
    }

    // Small legal-command bot. Its random stream never touches pairing, shops or battles.
    public sealed class LocalSimulationBot
    {
        private readonly DeterministicRandom random;
        private readonly ShopTransactionSystem trades;
        private readonly ShopSystem shops=new ShopSystem();
        private readonly LevelSystem levels=new LevelSystem();
        private readonly PlayerPlacementSystem placements=new PlayerPlacementSystem();
        public LocalSimulationBot(PokemonCatalog catalog,ulong seed)
        { trades=new ShopTransactionSystem(catalog);random=new DeterministicRandom(seed ^ 0x424F54504F4C4943UL); }
        public void Prepare(MatchState match)
        {
            if(match.Phase!=MatchPhase.Preparation)throw new InvalidOperationException("Bot requires preparation.");
            foreach(var p in match.Players.Where(p=>p.HP>0 && !p.IsEliminated).OrderBy(p=>p.PlayerId,StringComparer.Ordinal))
            {
                int reserve=random.NextInt(3);
                BuyOffers(match,p,reserve);
                if(p.Level<p.Rules.MaxLevel && p.Gold>=levels.Rules.PurchaseGoldCost+reserve && p.Units.Count>=p.BoardCapacity)
                    levels.BuyXP(match,p.PlayerId);
                if(p.Gold>=shops.Rules.RerollGoldCost+reserve+1 && p.FindFirstEmptyBenchSlot()!=null)
                { shops.Reroll(match,p.PlayerId);BuyOffers(match,p,reserve); }
                // Strongest owned units occupy a stable front-to-back formation; moves may swap.
                var best=p.Units.OrderByDescending(u=>(int)u.Rank).ThenBy(u=>u.InstanceId,StringComparer.Ordinal).Take(p.BoardCapacity).ToArray();
                var cells=p.BoardCells.OrderBy(c=>Math.Abs(c.Position.Column-3)).ThenBy(c=>c.Position.Row).ThenBy(c=>c.Position.Column).ToArray();
                for(int i=0;i<best.Length;i++)
                {
                    var move=placements.Move(match,p.PlayerId,best[i].InstanceId,UnitPlacement.OnBoard(cells[i].Position),p.PlacementRevision);
                    if(!move.Accepted)throw new InvalidOperationException("Bot deployment rejected: "+move.Reason);
                }
            }
        }
        private void BuyOffers(MatchState match,PlayerState p,int reserve)
        {
            int offset=random.NextInt(ShopRules.SlotCount);
            var slots=Enumerable.Range(0,ShopRules.SlotCount).OrderByDescending(i=>!p.Shop.Slots[i].IsEmpty && p.Units.Any(u=>u.DefinitionId==p.Shop.Slots[i].DefinitionId))
                .ThenBy(i=>(i+offset)%ShopRules.SlotCount).ToArray();
            foreach(int i in slots)
            {
                var slot=p.Shop.Slots[i];if(slot.IsEmpty || p.Gold-slot.Cost<reserve)continue;
                // Capacity and depleted shared stock are expected purchase rejections.
                try { trades.PreviewBuy(match,p.PlayerId,i,p.Shop.Revision); }
                catch(InvalidOperationException) { continue; }
                trades.BuyWithRankUp(match,p.PlayerId,i,p.Shop.Revision);
            }
        }
    }

    public sealed class LocalMatchSimulation
    {
        public const string PolicyVersion="basic-v1";
        public const string ContentVersion="simulation-fixture-v1";
        private readonly LocalSimulationBot bot;
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private readonly List<SimulationRoundRecord> records=new List<SimulationRoundRecord>();
        private double pendingTime;
        private int preparedRound;
        internal int RecordRound;
        internal Dictionary<string,int> HPBefore;
        internal double StartPreparation,StartCombat,StartResult;
        public LocalSimulationSettings Settings { get; }
        public PokemonCatalog Catalog { get; }
        public string ContentId { get; }
        public MatchState Match { get; }
        public LocalRoundCoordinator Coordinator { get; }
        public RoundFlowController Flow { get; }
        public SimulationStatus Status { get; private set; }=SimulationStatus.Running;
        public string Error { get; private set; }
        public double ExecutionSeconds=>clock.Elapsed.TotalSeconds;
        public int AliveCount=>Match.Players.Count(p=>p.HP>0 && !p.IsEliminated);
        public IReadOnlyList<SimulationRoundRecord> Rounds=>records.AsReadOnly();
        public LocalMatchSimulation(LocalSimulationSettings settings=null, PokemonCatalog catalog=null,
            IEnumerable<string> shopDefinitionIds=null, MatchRules matchRules=null)
        {
            Settings=settings??new LocalSimulationSettings();
            var definitions=CreateFixtureDefinitions();Catalog=catalog??new PokemonCatalog(definitions);
            ContentId=catalog==null ? ContentVersion : "custom-catalog";
            var ids=shopDefinitionIds??(catalog==null ? definitions.Select(d=>d.Id) : throw new ArgumentNullException(nameof(shopDefinitionIds)));
            Match=MatchStateFactory.CreateWithPool("local-simulation-"+Settings.Seed,Enumerable.Range(1,Settings.PlayerCount).Select(i=>"p"+i),Catalog,ids,rules:matchRules,matchSeed:Settings.Seed);
            Match.TransitionTo(MatchPhase.Starting);Match.TransitionTo(MatchPhase.Preparation);
            foreach(var p in Match.Players)new ShopSystem().RefreshForRound(Match,p.PlayerId);
            Coordinator=new LocalRoundCoordinator(Match,Catalog);Flow=new RoundFlowController(Coordinator,Settings.FlowRules);
            bot=new LocalSimulationBot(Catalog,Settings.Seed);BeginRecord();
        }
        // Explicit test content, not a claim about production Pokemon balance.
        public static PokemonDefinition[] CreateFixtureDefinitions()
            => Enumerable.Range(1,5).SelectMany(cost=>Enumerable.Range(0,4).Select(i=>new PokemonDefinition("sim-c"+cost+"-"+i,"Sim "+cost+" / "+i,cost,
                new PokemonStats(90+cost*20+i*8,9+cost*3+i,0,.8f+i*.12f,0,0,1,2,0,0,.25f),"role","skill"))).ToArray();
        private void BeginRecord()
        {
            RecordRound=Match.RoundNumber;HPBefore=Match.Players.ToDictionary(p=>p.PlayerId,p=>p.HP);
            StartPreparation=Flow.PreparationElapsedSeconds;StartCombat=Flow.CombatElapsedSeconds;StartResult=Flow.ResultElapsedSeconds;
        }
        private void SealRecord() { if(records.Count==0 || records[records.Count-1].Round!=RecordRound)records.Add(new SimulationRoundRecord(this)); }
        private void Stop(SimulationStatus status,string error=null)
        { SealRecord();Status=status;Error=error;clock.Stop();pendingTime=0; }
        public void Cancel() { if(Status==SimulationStatus.Running)Stop(SimulationStatus.Cancelled); }
        // Normal speed and fast mode both execute identical fixed 30 Hz updates.
        public void Advance(double gameSeconds,int maxSteps=300)
        {
            if(double.IsNaN(gameSeconds)||double.IsInfinity(gameSeconds)||gameSeconds<0)throw new ArgumentOutOfRangeException(nameof(gameSeconds));
            if(maxSteps<1)throw new ArgumentOutOfRangeException(nameof(maxSteps));
            if(Status!=SimulationStatus.Running)return;
            pendingTime=Math.Min(double.MaxValue-gameSeconds,pendingTime)+gameSeconds;
            for(int i=0;i<maxSteps && pendingTime+1e-9>=1d/30 && Status==SimulationStatus.Running;i++)
            { pendingTime=Math.Max(0,pendingTime-1d/30);StepFrame(); }
        }
        public void RunToEnd()
        { while(Status==SimulationStatus.Running)Advance(10,300); }
        private void StepFrame()
        {
            try
            {
                if(Match.Phase==MatchPhase.Finished) { Stop(SimulationStatus.Completed);return; }
                if(Match.RoundNumber>Settings.MaxRounds) { Stop(SimulationStatus.RoundLimit);return; }
                if(Flow.CombatTicks>=Settings.MaxCombatTicks && Match.Phase==MatchPhase.Combat) { Stop(SimulationStatus.TickLimit);return; }
                if(Match.Phase==MatchPhase.Preparation && Coordinator.PreparationReady && preparedRound!=Match.RoundNumber)
                { bot.Prepare(Match);preparedRound=Match.RoundNumber; }
                int round=Match.RoundNumber;
                var completedRound=Match.Phase==MatchPhase.Result && Coordinator.IsSettled && Flow.IsTimerRunning && Flow.RemainingSeconds<=1d/30+1e-9
                    ? new SimulationRoundRecord(this,Flow.RemainingSeconds) : null;
                Flow.AdvanceTime(1d/30);
                if(Flow.Fault!=null) { Stop(SimulationStatus.Faulted,Flow.Fault.ToString());return; }
                if(Match.Phase==MatchPhase.Finished)Stop(SimulationStatus.Completed);
                else if(Match.RoundNumber!=round)
                {
                    if(completedRound==null)throw new InvalidOperationException("Missing round record at transition.");
                    records.Add(completedRound);
                    BeginRecord();
                }
            }
            catch(Exception e) { Stop(SimulationStatus.Faulted,e.ToString()); }
        }
    }
}
