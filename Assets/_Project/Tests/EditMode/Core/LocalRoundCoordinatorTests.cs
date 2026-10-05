using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Match;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Board;

namespace PokeChess.Core.Tests
{
    public sealed class LocalRoundCoordinatorTests
    {
        private PokemonCatalog catalog;private MatchState match;private LocalRoundCoordinator loop;
        private PlayerState One => match.GetPlayer("p1");private PlayerState Two => match.GetPlayer("p2");
        [SetUp] public void Setup()
        {
            catalog=new PokemonCatalog(new[]{new PokemonDefinition("test","Test",1,new PokemonStats(100,20,0,1,0,0,1,2,0,0,.25f),"role","skill")});
            match=MatchStateFactory.CreateWithPool("round",new[]{"p1","p2"},catalog,new[]{"test"},matchSeed:123);
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);
            foreach(var p in match.Players)new ShopSystem().RefreshForRound(match,p.PlayerId);
            loop=new LocalRoundCoordinator(match,catalog);
        }
        private UnitInstance Add(string id,string owner,UnitPlacement placement,UnitRank rank=UnitRank.One)
        {
            var unit=catalog.CreateUnit(id,"test",owner,rank,0,placement);SharedPoolSystem.RegisterUnit(match,unit,"test");return match.GetPlayer(owner).GetUnit(id);
        }
        private void Run()
        {
            Assert.That(loop.StartCombat(match.RoundNumber),Is.True);
            for(int i=0;i<1400 && match.Phase==MatchPhase.Combat;i++)loop.Step();
            Assert.That(match.Phase,Is.EqualTo(MatchPhase.Result));Assert.That(loop.IsSettled,Is.True);
        }
        [Test] public void CombatBoardMergeIsDeferredUntilNextPreparation()
        {
            Add("A","p1",UnitPlacement.OnBoard(new BoardPosition(2,0)));Add("bench","p1",UnitPlacement.OnBench(0));Add("foe","p2",UnitPlacement.OnBoard(new BoardPosition(2,0)));
            loop.StartCombat(1);var battle=loop.Battle;var fighter=battle.Units.Single(u=>u.UnitInstanceId=="A");
            var preview=new ShopTransactionSystem(catalog).PreviewBuy(match,"p1",0,One.Shop.Revision);Assert.That(preview.FinalRank,Is.EqualTo(UnitRank.One));Assert.That(preview.MergeNextPreparation,Is.True);
            var buy=new ShopTransactionSystem(catalog).BuyWithRankUp(match,"p1",0,One.Shop.Revision);
            Assert.That(buy.Unit.Placement.Kind,Is.EqualTo(PlacementKind.Bench));Assert.That(buy.MergeNextPreparation,Is.True);Assert.That(One.GetUnit("A").Rank,Is.EqualTo(UnitRank.One));Assert.That(One.Units.Count,Is.EqualTo(3));Assert.That(One.Gold,Is.EqualTo(4));
            Assert.That(loop.Battle,Is.SameAs(battle));Assert.That(fighter.Rank,Is.EqualTo(UnitRank.One));Assert.That(fighter.CurrentHP,Is.EqualTo(100));Assert.That(battle.Units.Count,Is.EqualTo(2));Assert.That(battle.CurrentTick,Is.Zero);match.Pool.AssertConservation(match);
            for(int i=0;i<1400 && match.Phase==MatchPhase.Combat;i++)loop.Step();Assert.That(loop.NextRound(1),Is.True);Assert.That(loop.StartCombat(2),Is.True);Assert.That(loop.Battle.Units.Single(u=>u.UnitInstanceId=="A").Rank,Is.EqualTo(UnitRank.Two));
        }
        [Test] public void CombatBenchOnlyMergeIsImmediateAndDoesNotConsumeBoard()
        {
            Add("A","p1",UnitPlacement.OnBoard(new BoardPosition(2,0)));Add("b","p1",UnitPlacement.OnBench(0));Add("c","p1",UnitPlacement.OnBench(1));Add("foe","p2",UnitPlacement.OnBoard(new BoardPosition(2,0)));loop.StartCombat(1);
            var buy=new ShopTransactionSystem(catalog).BuyWithRankUp(match,"p1",0,One.Shop.Revision);
            Assert.That(buy.Unit.InstanceId,Is.EqualTo("b"));Assert.That(buy.Unit.Rank,Is.EqualTo(UnitRank.Two));Assert.That(buy.Unit.Placement.Kind,Is.EqualTo(PlacementKind.Bench));Assert.That(buy.MergeNextPreparation,Is.False);Assert.That(One.GetUnit("A").Rank,Is.EqualTo(UnitRank.One));Assert.That(One.Units.Count,Is.EqualTo(2));match.Pool.AssertConservation(match);
        }
        [Test] public void FullBenchCannotBorrowBoardSpaceForDeferredMerge()
        {
            Add("A","p1",UnitPlacement.OnBoard(new BoardPosition(2,0)));Add("foe","p2",UnitPlacement.OnBoard(new BoardPosition(2,0)));
            // Unpooled Rank Three gifts fill eight slots without consuming fixture pool stock.
            Add("b","p1",UnitPlacement.OnBench(0));for(int i=1;i<9;i++)One.AddUnit(catalog.CreateUnit("gift"+i,"test","p1",UnitRank.Three,0,UnitPlacement.OnBench(i)));
            loop.StartCombat(1);var trades=new ShopTransactionSystem(catalog);int gold=One.Gold;long revision=One.Shop.Revision;
            Assert.Throws<InvalidOperationException>(()=>trades.PreviewBuy(match,"p1",0,revision));Assert.Throws<InvalidOperationException>(()=>trades.Buy(match,"p1",0,revision));
            Assert.That(One.Gold,Is.EqualTo(gold));Assert.That(One.Shop.Revision,Is.EqualTo(revision));Assert.That(One.GetUnit("A").Rank,Is.EqualTo(UnitRank.One));match.Pool.AssertConservation(match);
        }
        [Test] public void FirstPreparationDoesNotAwardIncomeOrXP()
        { Assert.That(One.Gold,Is.EqualTo(5));Assert.That(One.Level,Is.EqualTo(1));Assert.That(One.XP,Is.Zero);Assert.That(loop.NextRound(1),Is.False); }
        [TestCase(false,false,BattleResult.Draw)]
        [TestCase(true,false,BattleResult.TeamOneWin)]
        [TestCase(false,true,BattleResult.TeamTwoWin)]
        public void EmptyBoardsSettleWithoutInvalidBattle(bool a,bool b,BattleResult result)
        {
            if(a)Add("A","p1",UnitPlacement.OnBoard(new BoardPosition(0,0)));if(b)Add("B","p2",UnitPlacement.OnBoard(new BoardPosition(0,0)));
            Run();Assert.That(loop.Battle,Is.Null);Assert.That(loop.LastResult.Result,Is.EqualTo(result));Assert.That(loop.LastResult.EndTick,Is.Zero);
            Assert.That(One.Gold,Is.EqualTo(10));Assert.That(One.Level,Is.EqualTo(2));Assert.That(One.LastAutomaticXPRound,Is.EqualTo(1));
        }
        [Test] public void SnapshotMirrorsOpponentAndIgnoresBench()
        {
            Add("A","p1",UnitPlacement.OnBoard(new BoardPosition(2,0)));Add("B","p2",UnitPlacement.OnBoard(new BoardPosition(2,0)));Add("C","p1",UnitPlacement.OnBench(0));
            Assert.That(loop.StartCombat(1),Is.True);Assert.That(loop.Battle.Units.Count,Is.EqualTo(2));
            Assert.That(loop.Battle.Units.Single(u=>u.UnitInstanceId=="B").Position,Is.EqualTo(HexCoordinates.MirrorCombat(new BoardPosition(2,0))));
            Assert.That(loop.Battle.Board.OccupiedCount,Is.EqualTo(2));
        }
        [Test] public void RealCombatPreservesOwnedUnitsItemsPlacementAndPool()
        {
            var a=Add("A","p1",UnitPlacement.OnBoard(new BoardPosition(2,0)));a.AddItem("item");Add("B","p2",UnitPlacement.OnBoard(new BoardPosition(2,0)));var bench=Add("C","p1",UnitPlacement.OnBench(0));
            int stock=match.Pool.GetStock("test").Available;var pos=a.Placement;loop.StartCombat(1);
            var moved=new PlayerPlacementSystem().Move(match,"p1","C",UnitPlacement.OnBench(1),One.PlacementRevision);Assert.That(moved.Accepted,Is.True);
            for(int i=0;i<1400 && match.Phase==MatchPhase.Combat;i++)loop.Step();
            Assert.That(loop.IsSettled,Is.True);Assert.That(One.GetUnit("A"),Is.SameAs(a));Assert.That(a.Placement,Is.EqualTo(pos));Assert.That(a.ItemInstanceIds,Does.Contain("item"));
            Assert.That(bench.Placement.BenchSlot,Is.EqualTo(1));Assert.That(match.Pool.GetStock("test").Available,Is.EqualTo(stock));match.Pool.AssertConservation(match);
        }
        [Test] public void RoundCommandsAndSettlementAreIdempotent()
        {
            Add("A","p1",UnitPlacement.OnBoard(new BoardPosition(0,0)));Add("B","p2",UnitPlacement.OnBoard(new BoardPosition(0,0)));
            Assert.That(loop.StartCombat(0),Is.False);Assert.That(loop.StartCombat(1),Is.True);var battle=loop.Battle;Assert.That(loop.StartCombat(1),Is.False);Assert.That(loop.Battle,Is.SameAs(battle));
            for(int i=0;i<1400 && match.Phase==MatchPhase.Combat;i++)loop.Step();int gold=One.Gold;int xp=One.XP;
            loop.SettleResult();loop.Step();Assert.That(One.Gold,Is.EqualTo(gold));Assert.That(One.XP,Is.EqualTo(xp));
            Assert.That(loop.NextRound(0),Is.False);Assert.That(loop.NextRound(1),Is.True);long revision=One.Shop.Revision;
            Assert.That(loop.NextRound(1),Is.False);Assert.That(match.RoundNumber,Is.EqualTo(2));Assert.That(One.Shop.Revision,Is.EqualTo(revision));
        }
        [Test] public void LockedShopPreservesPurchasedHoleAndRngAfterAutomaticLevelUp()
        {
            new ShopSystem().SetLocked(match,"p1",true);new ShopTransactionSystem(catalog).Buy(match,"p1",0,One.Shop.Revision);
            var ids=One.Shop.Slots.Select(s=>s.DefinitionId).ToArray();var rng=One.Shop.RandomState;long revision=One.Shop.Revision;
            Run();Assert.That(One.Level,Is.EqualTo(2));Assert.That(loop.NextRound(1),Is.True);
            Assert.That(One.Shop.LastRefreshRound,Is.EqualTo(2));Assert.That(One.Shop.Slots[0].IsEmpty,Is.True);Assert.That(One.Shop.Revision,Is.EqualTo(revision));Assert.That(One.Shop.RandomState,Is.EqualTo(rng));Assert.That(One.Shop.Slots.Select(s=>s.DefinitionId),Is.EqualTo(ids));
        }
        [Test] public void FailedSettlementPreflightsAllPlayersAndCanRetry()
        {
            Two.SetProgress(int.MaxValue,0,1);
            Assert.Throws<OverflowException>(()=>loop.StartCombat(1));Assert.That(match.Phase,Is.EqualTo(MatchPhase.Result));Assert.That(One.Gold,Is.EqualTo(5));Assert.That(One.LastEconomyRound,Is.Zero);
            Two.SetProgress(5,0,1);loop.SettleResult();Assert.That(loop.IsSettled,Is.True);Assert.That(One.Gold,Is.EqualTo(10));loop.SettleResult();Assert.That(One.Gold,Is.EqualTo(10));
        }
        [Test] public void TenRoundsPreserveStateAndRefreshOncePerRound()
        {
            Add("A","p1",UnitPlacement.OnBoard(new BoardPosition(2,0)));Add("B","p2",UnitPlacement.OnBoard(new BoardPosition(2,0)));
            for(int round=1;round<=10;round++)
            {
                Run();Assert.That(One.LastEconomyRound,Is.EqualTo(round));Assert.That(One.LastAutomaticXPRound,Is.EqualTo(round));Assert.That(One.Units.Count,Is.EqualTo(1));match.Pool.AssertConservation(match);
                Assert.That(loop.NextRound(round),Is.True);Assert.That(One.Shop.LastRefreshRound,Is.EqualTo(round+1));
            }
            Assert.That(match.RoundNumber,Is.EqualTo(11));
        }
        [Test] public void ShopRefreshRetryDoesNotRedrawCompletedPlayer()
        {
            Run();long old=Two.Shop.Revision;typeof(ShopState).GetProperty("Revision").SetValue(Two.Shop,long.MaxValue);
            Assert.Throws<OverflowException>(()=>loop.NextRound(1));Assert.That(loop.NextPreparationPending,Is.True);
            var rng=One.Shop.RandomState;long revision=One.Shop.Revision;Assert.That(loop.StartCombat(2),Is.False);
            typeof(ShopState).GetProperty("Revision").SetValue(Two.Shop,old);Assert.That(loop.NextRound(1),Is.True);
            Assert.That(One.Shop.RandomState,Is.EqualTo(rng));Assert.That(One.Shop.Revision,Is.EqualTo(revision));Assert.That(loop.NextPreparationPending,Is.False);
        }
        [Test] public void LongBattleReachesOvertimeAndTimeLimitDraw()
        {
            catalog=new PokemonCatalog(new[]{new PokemonDefinition("test","Test",1,new PokemonStats(100000,1,0,1,0,0,1,2,0,0,.25f),"role","skill")});
            match=MatchStateFactory.CreateWithPool("long",new[]{"p1","p2"},catalog,new[]{"test"});match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);
            foreach(var p in match.Players)new ShopSystem().RefreshForRound(match,p.PlayerId);loop=new LocalRoundCoordinator(match,catalog);
            Add("A","p1",UnitPlacement.OnBoard(new BoardPosition(2,0)));Add("B","p2",UnitPlacement.OnBoard(new BoardPosition(2,0)));Run();
            Assert.That(loop.Battle.IsOvertime,Is.True);Assert.That(loop.LastResult.Result,Is.EqualTo(BattleResult.Draw));Assert.That(loop.LastResult.Reason,Is.EqualTo(BattleEndReason.TimeLimit));Assert.That(loop.LastResult.EndTick,Is.EqualTo(1350));
        }
        [Test] public void TickSteppingIsDeterministicForSameSnapshotAndSeed()
        {
            Add("A","p1",UnitPlacement.OnBoard(new BoardPosition(2,0)));Add("B","p2",UnitPlacement.OnBoard(new BoardPosition(2,0)));Run();var result=loop.LastResult.Result;long tick=loop.LastResult.EndTick;
            Setup();Add("A","p1",UnitPlacement.OnBoard(new BoardPosition(2,0)));Add("B","p2",UnitPlacement.OnBoard(new BoardPosition(2,0)));Run();Assert.That(loop.LastResult.Result,Is.EqualTo(result));Assert.That(loop.LastResult.EndTick,Is.EqualTo(tick));
        }
        [Test] public void ResultRejectsPrematureSettlementAndDeadPlayerCannotStart()
        { Assert.Throws<InvalidOperationException>(()=>loop.SettleResult());One.SetHP(0);Assert.That(loop.StartCombat(1),Is.False);Assert.That(match.Phase,Is.EqualTo(MatchPhase.Preparation)); }
    }
}
