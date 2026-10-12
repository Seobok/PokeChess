using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Tests
{
    public sealed class ItemWildRoundTests
    {
        private static string Id(string key,string owner="p1",int round=1)=>"item:"+key+":"+round+":"+owner;
        private static PokemonCatalog Catalog()=>new PokemonCatalog(new[]{new PokemonDefinition("bulbasaur","Test",1,
            new PokemonStats(100,10,0,1,0,0,1,2,100,0,.25f),"role","skill")});
        private static BattleState Battle(string sourceItem=null,string targetItem=null)
        {
            var c=Catalog();return BattleStateFactory.Create("items",1,123,30,c,new[]{
                new BattleUnitSetup(c.CreateUnit("a","bulbasaur","p1",UnitRank.One,0,UnitPlacement.Unplaced,sourceItem==null?null:new[]{Id(sourceItem)}),1,new BoardPosition(0,0)),
                new BattleUnitSetup(c.CreateUnit("z","bulbasaur","p2",UnitRank.One,0,UnitPlacement.Unplaced,targetItem==null?null:new[]{Id(targetItem,"p2")}),2,new BoardPosition(1,0))});
        }
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private static DamageResult Hit(BattleState b,float amount,DamageOrigin origin=DamageOrigin.Direct,DamageType type=DamageType.Physical,bool basic=true)
            =>new DamageProcessor().Apply(b,new DamageRequest("a","z",amount,type,isBasicAttack:basic,origin:origin));
        [Test] public void HelmetReflectsActualHPAfterReductionAndShield()
        {
            var b=Battle(targetItem:ItemCatalog.RockyHelmet);U(b,"z").SetDamageModifiers(new DamageModifiers(reduction:.2f));U(b,"z").SetShield(30);
            var d=Hit(b,100);Assert.That(d.AppliedDamage,Is.EqualTo(50));Assert.That(U(b,"a").CurrentHP,Is.EqualTo(90));
            Assert.That(b.DamageResultsThisTick.Last().Request.Origin,Is.EqualTo(DamageOrigin.Reflection));
        }
        [TestCase(DamageOrigin.Periodic)] [TestCase(DamageOrigin.Reflection)]
        public void HelmetDoesNotReflectIndirectDamage(DamageOrigin origin)
        {var b=Battle(targetItem:ItemCatalog.RockyHelmet);Hit(b,50,origin);Assert.That(U(b,"a").CurrentHP,Is.EqualTo(100));Assert.That(b.DamageResultsThisTick.Count,Is.EqualTo(1));}
        [Test] public void TwoHelmetsDoNotRecurseOrGrantReflectionEnergy()
        {
            var b=Battle(ItemCatalog.RockyHelmet,ItemCatalog.RockyHelmet);Hit(b,50,basic:false);
            Assert.That(b.DamageResultsThisTick.Count,Is.EqualTo(2));Assert.That(U(b,"a").CurrentHP,Is.EqualTo(90));Assert.That(U(b,"a").CurrentEnergy,Is.Zero);
        }
        [Test] public void LethalHelmetReflectsOnlyRemainingHP()
        {var b=Battle(targetItem:ItemCatalog.RockyHelmet);U(b,"z").SetVitals(20,0);Hit(b,1000);Assert.That(U(b,"a").CurrentHP,Is.EqualTo(96));Assert.That(U(b,"z").IsOnBoard,Is.False);}
        [Test] public void FullyShieldedHelmetDoesNotReflect()
        {var b=Battle(targetItem:ItemCatalog.RockyHelmet);U(b,"z").SetShield(100);Hit(b,50);Assert.That(U(b,"a").CurrentHP,Is.EqualTo(100));}
        [Test] public void HelmetReflectsToAnUntargetableLivingAttacker()
        {var b=Battle(targetItem:ItemCatalog.RockyHelmet);U(b,"a").SetUntargetable(true);Hit(b,50);Assert.That(U(b,"a").CurrentHP,Is.EqualTo(90));}
        [Test] public void BerryHealsOnceAndCannotResurrect()
        {
            var b=Battle(targetItem:ItemCatalog.SitrusBerry);Hit(b,65);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(60));
            Hit(b,30);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(30));
            var lethal=Battle(targetItem:ItemCatalog.SitrusBerry);Hit(lethal,200);Assert.That(U(lethal,"z").CurrentHP,Is.Zero);
        }
        [Test] public void SashSurvivesLethalDamageAndResetsForNewBattle()
        {
            var b=Battle(targetItem:ItemCatalog.FocusSash);Hit(b,200);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1));Hit(b,200);Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1));
            var next=Battle(targetItem:ItemCatalog.FocusSash);Hit(next,200);Assert.That(U(next,"z").CurrentHP,Is.EqualTo(1));
        }
        [TestCase(DamageType.Magic,60)] [TestCase(DamageType.Physical,80)] [TestCase(DamageType.True,80)]
        public void VestReducesOnlyMagic(DamageType type,float expected)
        {var b=Battle(targetItem:ItemCatalog.AssaultVest);Assert.That(Hit(b,80,type:type).AppliedDamage,Is.EqualTo(expected));}
        [Test] public void GlassesBoostSkillsButNotBasicOrPeriodicDamage()
        {
            Assert.That(Hit(Battle(ItemCatalog.WiseGlasses),50,basic:false).AppliedDamage,Is.EqualTo(60));
            Assert.That(Hit(Battle(ItemCatalog.WiseGlasses),50).AppliedDamage,Is.EqualTo(50));
            Assert.That(Hit(Battle(ItemCatalog.WiseGlasses),50,DamageOrigin.Periodic,basic:false).AppliedDamage,Is.EqualTo(50));
        }
        [Test] public void ScarfChangesCombatSnapshotWithoutChangingDefinition()
        {
            var b=Battle(ItemCatalog.ChoiceScarf);Assert.That(U(b,"a").Stats.AttackSpeed,Is.EqualTo(1.25f));
            Assert.That(Hit(b,60,basic:false).AppliedDamage,Is.EqualTo(51));Assert.That(Catalog().Get("bulbasaur").BaseStats.AttackSpeed,Is.EqualTo(1));
        }
        [Test] public void LeftoversHealAtThreeSecondsAndClampToMaxHP()
        {
            var b=Battle(ItemCatalog.Leftovers);var sim=new BattleSimulation(b,new CombatBehaviorPolicy());U(b,"a").SetVitals(50,0);
            for(int i=0;i<90;i++)sim.Step();Assert.That(U(b,"a").CurrentHP,Is.EqualTo(50));sim.Step();Assert.That(U(b,"a").CurrentHP,Is.EqualTo(54));
            U(b,"a").SetVitals(99,0);for(int i=0;i<90;i++)sim.Step();Assert.That(U(b,"a").CurrentHP,Is.EqualTo(100));
        }
        [Test] public void SashProtectionExpiresAndCannotTriggerTwice()
        {
            var b=Battle(targetItem:ItemCatalog.FocusSash);Hit(b,200);var sim=new BattleSimulation(b,new CombatBehaviorPolicy());
            for(int i=0;i<31;i++)sim.Step();Hit(b,200);Assert.That(U(b,"z").CurrentHP,Is.Zero);
        }
        [Test] public void ExpShareReceivesAdjacentSkillEnergyOnceWithinCooldown()
        {
            var c=Catalog();var setup=new[]{
                new BattleUnitSetup(c.CreateUnit("a","bulbasaur","p1",UnitRank.One,0,UnitPlacement.Unplaced,new[]{Id(ItemCatalog.ExpShare)}),1,new BoardPosition(0,0)),
                new BattleUnitSetup(c.CreateUnit("caster1","bulbasaur","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(1,0)),
                new BattleUnitSetup(c.CreateUnit("caster2","bulbasaur","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(0,1)),
                new BattleUnitSetup(c.CreateUnit("enemy","bulbasaur","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(6,7))};
            var b=BattleStateFactory.Create("share",1,1,30,c,setup);U(b,"caster1").SetVitals(100,100);U(b,"caster2").SetVitals(100,100);
            var skills=new SkillCatalog(new[]{new SkillDefinition("skill",0,0,SkillTargetRule.Self,TargetLostPolicy.Cancel,SkillEffectKind.SelfShield,1)});
            var sim=new BattleSimulation(b,skillCatalog:skills);for(int i=0;i<3;i++)sim.Step();Assert.That(U(b,"a").CurrentEnergy,Is.EqualTo(10));
        }
        private static (MatchState match,LocalRoundCoordinator rounds,PokemonCatalog catalog) Match(bool units=false,int playerCount=2)
        {
            var c=Catalog();var m=MatchStateFactory.CreateWithPool("wild",Enumerable.Range(1,playerCount).Select(i=>"p"+i),c,new[]{"bulbasaur"},rules:new MatchRules(enableWildRounds:true));
            m.TransitionTo(MatchPhase.Starting);m.TransitionTo(MatchPhase.Preparation);
            foreach(var p in m.Players){new ShopSystem().RefreshForRound(m,p.PlayerId);if(units)SharedPoolSystem.RegisterUnit(m,c.CreateUnit("unit-"+p.PlayerId,"bulbasaur",p.PlayerId,UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(0,0))),"bulbasaur");}
            return(m,new LocalRoundCoordinator(m,c),c);
        }
        private static (MatchState match,LocalRoundCoordinator rounds,PokemonCatalog catalog) RewardMatch()
        {
            var f=Match(units:true);
            for(int round=1;round<=3;round++){
                f.rounds.StartCombat(round);
                for(int tick=0;tick<1400&&f.match.Phase==MatchPhase.Combat;tick++)f.rounds.Step();
                Assert.That(f.rounds.LastResult.PlayerResults.Values.All(r=>r.Outcome==RoundOutcome.Win),Is.True);
                if(round<3)f.rounds.NextRound(round);
            }
            return f;
        }
        [Test] public void EmptyWildBoardsLoseHPAndReceiveNoOffer()
        {
            var f=Match();Assert.That(f.rounds.StartCombat(1),Is.True);Assert.That(f.rounds.IsSettled,Is.True);
            Assert.That(f.rounds.LastResult.PlayerResults.Count,Is.EqualTo(2));Assert.That(f.rounds.Battles.All(b=>b.IsWild&&!b.IsShadow),Is.True);
            foreach(var p in f.match.Players){Assert.That(p.HP,Is.EqualTo(56));Assert.That(p.WinStreak+p.LoseStreak,Is.Zero);Assert.That(p.PendingItemReward,Is.Null);}
            f.rounds.SettleResult();Assert.That(f.match.GetPlayer("p1").ItemInventory,Is.Empty);
        }
        [Test] public void ImmediateWildTransitionPreservesRewardChoiceForPreparation()
        {
            var f=RewardMatch();var flow=new RoundFlowController(f.rounds);flow.AdvanceTime(1000);
            Assert.That(flow.Fault,Is.Null);
            Assert.That(f.match.Phase,Is.EqualTo(MatchPhase.Preparation));
            Assert.That(f.match.RoundNumber,Is.EqualTo(4));
            Assert.That(flow.RemainingSeconds,Is.EqualTo(30));
            Assert.That(flow.ResultElapsedSeconds,Is.Zero);
            var p=f.match.GetPlayer("p1");string expected=p.PendingItemReward.Candidates[2];
            string chosen=new ItemSystem().Choose(f.match,"p1",2,p.ItemRevision);
            Assert.That(ItemCatalog.DefinitionId(chosen),Is.EqualTo(expected));
            Assert.That(p.PendingItemReward,Is.Null);
            Assert.That(p.LastItemRewardRound,Is.EqualTo(3));
        }
        [Test] public void RewardChoiceIsOnceOnlyAndTimeoutSelectsFirst()
        {
            var f=RewardMatch();var p=f.match.GetPlayer("p1");var items=new ItemSystem();
            string expected=f.match.GetPlayer("p2").PendingItemReward.Candidates[0];
            items.Choose(f.match,"p1",2,p.ItemRevision);Assert.Throws<InvalidOperationException>(()=>items.Choose(f.match,"p1",0,p.ItemRevision));
            Assert.That(f.rounds.NextRound(3),Is.True);Assert.That(p.ItemInventory.Count,Is.EqualTo(1));
            Assert.That(f.match.GetPlayer("p2").PendingItemReward,Is.Not.Null); f.rounds.StartCombat(4);
            Assert.That(ItemCatalog.DefinitionId(f.match.GetPlayer("p2").ItemInventory.Single()),Is.EqualTo(expected));Assert.That(p.PendingItemReward,Is.Null);
        }
        [Test] public void EquipSwapRecoveryAndForeignOwnershipAreValidated()
        {
            var f=Match(units:true);var p=f.match.GetPlayer("p1");var unit=p.Units.Single();var items=new ItemSystem();
            string helmet=Id(ItemCatalog.RockyHelmet),berry=Id(ItemCatalog.SitrusBerry);p.AddInventoryItem(helmet);p.AddInventoryItem(berry);
            items.Equip(f.match,"p1",unit.InstanceId,helmet,p.ItemRevision);long stale=p.ItemRevision;
            items.Equip(f.match,"p1",unit.InstanceId,berry,p.ItemRevision);Assert.That(unit.ItemInstanceIds,Is.EqualTo(new[]{berry}));Assert.That(p.ItemInventory,Is.EqualTo(new[]{helmet}));
            Assert.Throws<InvalidOperationException>(()=>items.Unequip(f.match,"p1",unit.InstanceId,stale));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(()=>items.Equip(f.match,"p2",unit.InstanceId,helmet,0));
            items.Unequip(f.match,"p1",unit.InstanceId,p.ItemRevision);Assert.That(p.ItemInventory.Count,Is.EqualTo(2));Assert.That(unit.ItemInstanceIds,Is.Empty);
            f.rounds.StartCombat(1);Assert.Throws<InvalidOperationException>(()=>items.Equip(f.match,"p1",unit.InstanceId,helmet,p.ItemRevision));
        }
        [Test] public void SaleReturnsEquippedTool()
        {
            var f=Match(units:true);var p=f.match.GetPlayer("p1");string id=Id(ItemCatalog.RockyHelmet);p.AddInventoryItem(id);new ItemSystem().Equip(f.match,"p1",p.Units.Single().InstanceId,id,p.ItemRevision);
            new ShopTransactionSystem(f.catalog).Sell(f.match,"p1",p.Units.Single().InstanceId);Assert.That(p.ItemInventory,Is.EqualTo(new[]{id}));f.match.Pool.AssertConservation(f.match);
        }
        [Test] public void RankMergeKeepsSurvivorToolAndReturnsConsumedTools()
        {
            var f=Match(units:true);var p=f.match.GetPlayer("p1");string helmet=Id(ItemCatalog.RockyHelmet),berry=Id(ItemCatalog.SitrusBerry);p.AddInventoryItem(helmet);p.AddInventoryItem(berry);
            SharedPoolSystem.RegisterUnit(f.match,f.catalog.CreateUnit("extra1","bulbasaur","p1",UnitRank.One,0,UnitPlacement.OnBench(0)),"bulbasaur");
            SharedPoolSystem.RegisterUnit(f.match,f.catalog.CreateUnit("extra2","bulbasaur","p1",UnitRank.One,0,UnitPlacement.OnBench(1)),"bulbasaur");
            var items=new ItemSystem();items.Equip(f.match,"p1","unit-p1",helmet,p.ItemRevision);items.Equip(f.match,"p1","extra1",berry,p.ItemRevision);
            new RankUpSystem(f.catalog).Resolve(f.match,"p1",p.PlacementRevision);Assert.That(p.Units.Count,Is.EqualTo(1));Assert.That(p.Units.Single().ItemInstanceIds,Is.EqualTo(new[]{helmet}));Assert.That(p.ItemInventory,Is.EqualTo(new[]{berry}));f.match.Pool.AssertConservation(f.match);
        }
        [Test] public void HostCommandReplayCannotDuplicateRewardAndInventoryStaysPrivate()
        {
            var f=RewardMatch();var host=new HostCommandProcessor(f.match,f.catalog,f.rounds);var s=host.Snapshot("p1");
            var cmd=new MatchCommand{matchId=s.matchId,playerId="p1",commandId="reward",sequence=s.nextSequence,round=s.round,phase=s.phase,playerRevision=s.playerRevision,kind=MatchCommandKind.ChooseItem,slot=1,itemRevision=s.itemRevision};
            Assert.That(host.Process("p1",cmd).accepted,Is.True);Assert.That(host.Process("p1",cmd).accepted,Is.True);
            Assert.That(f.match.GetPlayer("p1").ItemInventory.Count,Is.EqualTo(1));Assert.That(host.Snapshot("p2").inventory,Is.Empty);Assert.That(host.PublicSnapshot().wildRound,Is.True);
            var changed=cmd.Copy();changed.slot=2;Assert.That(host.Process("p1",changed).code,Is.EqualTo("DuplicateConflict"));
        }
        [Test] public void WildScheduleStartsWithThreeEncountersAndContinuesEveryFiveRounds()
        {Assert.That(Enumerable.Range(1,40).Where(WildRoundSystem.IsWildRound),Is.EqualTo(new[]{1,2,3,9,14,19,24,29,34,39}));}
        [Test] public void LivingWildBattleFinishesAndCanAdvanceIntoPvP()
        {
            var f=Match(units:true);Assert.That(f.rounds.StartCombat(1),Is.True);
            for(int i=0;i<1400&&f.match.Phase==MatchPhase.Combat;i++)f.rounds.Step();
            Assert.That(f.rounds.IsSettled,Is.True);Assert.That(f.rounds.LastResult.PlayerResults.Values.All(r=>r.Outcome==RoundOutcome.Win),Is.True);
            Assert.That(f.match.Players.All(p=>p.WinStreak==0&&p.LoseStreak==0&&p.HP==60),Is.True);
            Assert.That(f.match.Players.All(p=>p.PendingItemReward==null),Is.True);
            Assert.That(f.rounds.LastResult.Income.Values.All(i=>i.TotalIncome>=7),Is.True);
            Assert.That(f.rounds.NextRound(1),Is.True);Assert.That(f.rounds.StartCombat(2),Is.True);Assert.That(f.rounds.Battles.All(b=>b.IsWild),Is.True);
        }
        [Test] public void WildBattlePreservesStreaksButLossCostsHP()
        {
            var f=Match();SharedPoolSystem.RegisterUnit(f.match,f.catalog.CreateUnit("winner","bulbasaur","p1",UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(0,0))),"bulbasaur");
            for(int round=1;round<=8;round++){
                Assert.That(f.rounds.StartCombat(round),Is.True);for(int tick=0;tick<1400&&f.match.Phase==MatchPhase.Combat;tick++)f.rounds.Step();Assert.That(f.rounds.NextRound(round),Is.True);
            }
            var one=f.match.GetPlayer("p1");var two=f.match.GetPlayer("p2");int hp1=one.HP,hp2=two.HP;
            Assert.That(one.WinStreak,Is.EqualTo(5));Assert.That(two.LoseStreak,Is.EqualTo(5));
            f.rounds.StartCombat(9);for(int i=0;i<1400&&f.match.Phase==MatchPhase.Combat;i++)f.rounds.Step();
            Assert.That(one.WinStreak,Is.EqualTo(5));Assert.That(two.LoseStreak,Is.EqualTo(5));Assert.That(one.HP,Is.EqualTo(hp1));Assert.That(two.HP,Is.LessThan(hp2));
        }
        [Test] public void WildPreparationSurrenderRepairsOnlyPvEPairings()
        {
            var f=Match(playerCount:3);Assert.That(f.rounds.Surrender("p2",1,MatchPhase.Preparation).Accepted,Is.True);
            Assert.That(f.match.Pairings.All(pair=>pair.IsWild&&pair.ParticipantIds.Count==1),Is.True);
            Assert.That(f.rounds.StartCombat(1),Is.True);Assert.That(f.rounds.LastResult.PlayerResults.Keys,Is.EquivalentTo(new[]{"p1","p3"}));f.match.Pool.AssertConservation(f.match);
        }
        [TestCase(1,2)] [TestCase(2,3)] [TestCase(3,3)] [TestCase(9,1)] [TestCase(24,1)]
        public void WildEncounterHasExpectedCountPositionAndEarlySkillsDisabled(int round,int count)
        {
            var f=Match(units:true);var p=f.match.GetPlayer("p1");
            for(int r=1;r<round;r++){
                // Build encounter snapshots without advancing the match economy or damaging players.
                f.match.TransitionTo(MatchPhase.Combat);f.match.TransitionTo(MatchPhase.Result);f.match.TransitionTo(MatchPhase.Preparation);
            }
            var factory=typeof(WildRoundSystem).GetMethod("CreateBattle",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            var battle=(BattleState)factory.Invoke(null,new object[]{f.match,f.catalog,p,new[]{new BattleUnitSetup(p.Units.Single(),1,new BoardPosition(0,0))},30});
            var enemies=battle.Units.Where(u=>u.TeamId==2).ToArray();
            Assert.That(enemies.Length,Is.EqualTo(count));
            if(round<=3){
                foreach(var enemy in enemies){enemy.SetVitals(enemy.CurrentHP,1000);Assert.That(battle.Energy.IsSkillReady(enemy),Is.False);}
            }else Assert.That(enemies.Single().Position,Is.EqualTo(new BoardPosition(3,4)));
        }
        [Test] public void FirstTwoWinsGrantGoldOnlyThirdWinGrantsIdenticalItemOffers()
        {
            var f=Match(units:true);
            for(int round=1;round<=3;round++){
                f.rounds.StartCombat(round);for(int tick=0;tick<1400&&f.match.Phase==MatchPhase.Combat;tick++)f.rounds.Step();
                foreach(var p in f.match.Players){
                    Assert.That(p.HP,Is.EqualTo(60));
                    Assert.That(p.PendingItemReward==null,Is.EqualTo(round<3));
                    int gold=p.Gold;f.rounds.SettleResult();Assert.That(p.Gold,Is.EqualTo(gold));
                }
                if(round<3)f.rounds.NextRound(round);
            }
            Assert.That(f.match.Players[0].PendingItemReward.Candidates,Is.EqualTo(f.match.Players[1].PendingItemReward.Candidates));
        }
        [Test] public void ThirdRoundLossOffersNoToolAndWildDamageCanEliminate()
        {
            var f=Match();
            for(int round=1;round<=3;round++){
                f.rounds.StartCombat(round);
                foreach(var p in f.match.Players)Assert.That(p.PendingItemReward,Is.Null);
                if(round<3)f.rounds.NextRound(round);
            }
            Assert.That(f.match.Players.All(p=>p.HP==46),Is.True);
            var lethal=Match(playerCount:8);
            for(int round=1;round<=3;round++){
                if(round==3)lethal.match.GetPlayer("p1").SetHP(1);
                lethal.rounds.StartCombat(round);
                if(round<3)lethal.rounds.NextRound(round);
            }
            // Repeat completed-round settlement to verify HP cannot be charged twice.
            int hp=lethal.match.GetPlayer("p1").HP;lethal.rounds.SettleResult();
            Assert.That(lethal.match.GetPlayer("p1").HP,Is.EqualTo(hp));
            Assert.That(hp,Is.Zero);Assert.That(lethal.match.GetPlayer("p1").IsEliminated,Is.True);
        }
        [Test] public void HostEquipReplayAndRecoverySnapshotRetainExactlyOneTool()
        {
            var f=Match(units:true);var p=f.match.GetPlayer("p1");string helmet=Id(ItemCatalog.RockyHelmet);p.AddInventoryItem(helmet);
            var host=new HostCommandProcessor(f.match,f.catalog,f.rounds);var s=host.Snapshot("p1");
            var command=new MatchCommand{matchId=s.matchId,playerId="p1",commandId="equip",sequence=s.nextSequence,round=s.round,phase=s.phase,playerRevision=s.playerRevision,kind=MatchCommandKind.EquipItem,unitId="unit-p1",itemId=helmet,itemRevision=s.itemRevision};
            Assert.That(host.Process("p1",command).accepted,Is.True);Assert.That(host.Process("p1",command).accepted,Is.True);
            var recovery=host.FullSnapshot("p1");var store=new MatchSnapshotStore();Assert.That(store.Apply(recovery,f.match.MatchId,"p1"),Is.True);
            Assert.That(store.Owner.inventory,Is.Empty);Assert.That(store.Owner.units.Single().items,Is.EqualTo(new[]{helmet}));Assert.That(store.Public.players.Single(v=>v.id=="p1").board.Single().items,Is.EqualTo(new[]{helmet}));
        }
    }
}
