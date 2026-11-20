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
        [Test] public void EmptyWildBoardsLoseHPAndReceiveNoReward()
        {
            var f=Match();Assert.That(f.rounds.StartCombat(1),Is.True);Assert.That(f.rounds.IsSettled,Is.True);
            Assert.That(f.rounds.LastResult.PlayerResults.Count,Is.EqualTo(2));Assert.That(f.rounds.Battles.All(b=>b.IsWild&&!b.IsShadow),Is.True);
            foreach(var p in f.match.Players){Assert.That(p.HP,Is.EqualTo(56));Assert.That(p.WinStreak+p.LoseStreak,Is.Zero);Assert.That(p.ItemInventory,Is.Empty);}
            f.rounds.SettleResult();Assert.That(f.match.GetPlayer("p1").ItemInventory,Is.Empty);
        }
        [Test] public void ImmediateWildTransitionPreservesGrantedComponents()
        {
            var f=RewardMatch();var flow=new RoundFlowController(f.rounds);flow.AdvanceTime(1000);
            Assert.That(flow.Fault,Is.Null);
            Assert.That(f.match.Phase,Is.EqualTo(MatchPhase.Preparation));
            Assert.That(f.match.RoundNumber,Is.EqualTo(4));
            Assert.That(flow.RemainingSeconds,Is.EqualTo(30));
            Assert.That(flow.ResultElapsedSeconds,Is.Zero);
            var player = f.match.GetPlayer("p1");
            Assert.That(player.ItemInventory.Count, Is.EqualTo(4));
            Assert.That(player.ItemInventory.All(ItemCatalog.IsComponent), Is.True);
            Assert.That(player.LastItemRewardRound, Is.EqualTo(3));
        }
        [Test] public void ClearingOneWildEncounterGrantsLootBeforeOtherBattlesFinish()
        {
            var f = Match(units: true);
            Assert.That(f.rounds.StartCombat(1), Is.True);
            var first = f.rounds.GetBattleFor("p1").Battle;
            foreach (var enemy in first.Units.Where(unit => unit.TeamId == 2))
                enemy.SetVitals(0, 0);
            f.rounds.Step();
            Assert.That(f.match.Phase, Is.EqualTo(MatchPhase.Combat));
            Assert.That(f.match.GetPlayer("p1").ItemInventory.Count, Is.EqualTo(1));
            Assert.That(f.match.GetPlayer("p2").ItemInventory, Is.Empty);
            f.rounds.Step();
            Assert.That(f.match.GetPlayer("p1").ItemInventory.Count, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ThirdWildRoundGrantsEachDropOnDeathEvenIfThePlayerLaterLoses(bool lose)
        {
            var f = Match(units: true);
            for (int round = 1; round <= 2; round++)
            {
                Assert.That(f.rounds.StartCombat(round), Is.True);
                for (int tick = 0; tick < 1400 && f.match.Phase == MatchPhase.Combat; tick++) f.rounds.Step();
                Assert.That(f.rounds.NextRound(round), Is.True);
            }

            Assert.That(f.rounds.StartCombat(3), Is.True);
            var player = f.match.GetPlayer("p1");
            var pair = f.rounds.GetBattleFor("p1");
            var drops = ItemSystem.PlanWildDrops(f.match, player, pair.Battle);
            var enemies = pair.Battle.Units.Where(unit => unit.TeamId == 2).ToArray();
            Assert.That(drops.Count, Is.EqualTo(2));
            Assert.That(drops.Values.All(items => items.Count == 1 && items.All(ItemCatalog.IsComponent)), Is.True);

            enemies.Single(enemy => !drops.ContainsKey(enemy.UnitInstanceId)).SetVitals(0, 0);
            long before = player.ItemRevision;
            f.rounds.Step();
            Assert.That(player.ItemInventory.Count, Is.EqualTo(2));
            Assert.That(player.ItemRevision, Is.EqualTo(before));

            var playedDrops = new System.Collections.Generic.List<(BoardPosition cell, string item, int index)>();
            f.rounds.WildItemDropped += (owner, cell, item, index) =>
            {
                if (owner == player.PlayerId) playedDrops.Add((cell, item, index));
            };
            var carriers = enemies.Where(enemy => drops.ContainsKey(enemy.UnitInstanceId)).ToArray();
            var firstDrop = (carriers[0].Position, drops[carriers[0].UnitInstanceId][0], 0);
            carriers[0].SetVitals(0, 0);
            f.rounds.Step();
            Assert.That(pair.IsComplete, Is.False);
            Assert.That(player.ItemInventory.Count, Is.EqualTo(3));
            Assert.That(player.ItemInventory, Does.Contain(drops[carriers[0].UnitInstanceId][0]));
            Assert.That(player.ItemRevision, Is.EqualTo(before + 1));
            Assert.That(playedDrops, Is.EqualTo(new[]{ firstDrop }));

            if (lose)
            {
                foreach (var unit in pair.Battle.Units.Where(unit => unit.TeamId == 1)) unit.SetVitals(0, 0);
            }
            else carriers[1].SetVitals(0, 0);
            f.rounds.Step();
            Assert.That(pair.OutcomeOf(player.PlayerId), Is.EqualTo(lose ? RoundOutcome.Loss : RoundOutcome.Win));
            Assert.That(player.ItemInventory.Count, Is.EqualTo(lose ? 3 : 4));
            Assert.That(player.ItemRevision, Is.EqualTo(before + (lose ? 1 : 2)));
            Assert.That(playedDrops.Count, Is.EqualTo(lose ? 1 : 2));
            for (int tick = 0; tick < 1400 && f.match.Phase == MatchPhase.Combat; tick++) f.rounds.Step();
            Assert.That(player.ItemInventory.Count, Is.EqualTo(lose ? 3 : 4));
        }

        [Test] public void RepeatedSettlementDoesNotGrantMoreComponents()
        {
            var f = RewardMatch();
            var player = f.match.GetPlayer("p1");
            var items = player.ItemInventory.ToArray();
            long revision = player.ItemRevision;
            f.rounds.SettleResult();
            Assert.That(player.ItemInventory, Is.EqualTo(items));
            Assert.That(player.ItemRevision, Is.EqualTo(revision));
            Assert.That(f.rounds.NextRound(3), Is.True);
            Assert.That(f.rounds.StartCombat(4), Is.True);
            Assert.That(player.ItemInventory, Is.EqualTo(items));
        }
        [Test] public void EquipCombinesComponentsAndKeepsExistingCompletedItems()
        {
            var fixture = Match(units: true);
            var player = fixture.match.GetPlayer("p1");
            var unit = player.Units.Single();
            var items = new ItemSystem();
            var crafted = new System.Collections.Generic.List<string>();
            for (int copy = 0; copy < 3; copy++)
            {
                string first = $"item:x-attack:first-{copy}:p1";
                string second = $"item:x-attack:second-{copy}:p1";
                player.AddInventoryItem(first);
                player.AddInventoryItem(second);
                long stale = player.ItemRevision;
                items.Equip(fixture.match, "p1", unit.InstanceId, first, stale);
                Assert.Throws<InvalidOperationException>(() => items.Equip(fixture.match, "p1", unit.InstanceId, second, stale));
                items.Equip(fixture.match, "p1", unit.InstanceId, second, player.ItemRevision);
                var completed = unit.ItemInstanceIds[copy];
                Assert.That(ItemCatalog.DefinitionId(completed), Is.EqualTo("crafted-kings-rock"));
                crafted.Add(completed);
                Assert.That(unit.ItemInstanceIds, Is.EqualTo(crafted));
                Assert.That(player.ItemInventory, Is.Empty);
            }
            Assert.That(crafted.Distinct().Count(), Is.EqualTo(3));

            string extra = Id(ItemCatalog.SitrusBerry);
            player.AddInventoryItem(extra);
            long revision = player.ItemRevision;
            Assert.Throws<InvalidOperationException>(() => items.Equip(fixture.match, "p1", unit.InstanceId, extra, revision));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => items.Equip(fixture.match, "p2", unit.InstanceId, extra, 0));
            Assert.That(player.ItemRevision, Is.EqualTo(revision));
            Assert.That(player.ItemInventory, Is.EqualTo(new[]{extra}));
            Assert.That(unit.ItemInstanceIds, Is.EqualTo(crafted));
        }

        [Test] public void RecipesCoverAllUnorderedPairsAndCompletedStatsStack()
        {
            Assert.That(ItemCatalog.Components.Count, Is.EqualTo(8));
            var results = ItemCatalog.Components.SelectMany(first => ItemCatalog.Components.Select(second =>
            {
                var recipe = ItemCatalog.Recipe(first.Id, second.Id);
                Assert.That(recipe, Is.Not.Null);
                Assert.That(recipe, Is.SameAs(ItemCatalog.Recipe(second.Id, first.Id)));
                return recipe.Id;
            }));
            Assert.That(results.Distinct().Count(), Is.EqualTo(36));

            var catalog = Catalog();
            var baseStats = catalog.Get("bulbasaur").BaseStats;
            var equipment = Enumerable.Range(1, 3).Select(i => $"item:crafted-kings-rock:{i}:p1");
            var setup = new[]{
                new BattleUnitSetup(catalog.CreateUnit("a","bulbasaur","p1",UnitRank.One,0,UnitPlacement.Unplaced,equipment.ToArray()),1,new BoardPosition(0,0)),
                new BattleUnitSetup(catalog.CreateUnit("z","bulbasaur","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(1,0))
            };
            var battle = BattleStateFactory.Create("stacked-items", 1, 123, 30, catalog, setup);
            Assert.That(U(battle, "a").Stats.Attack, Is.EqualTo(baseStats.Attack * 2.5f));
        }

        [Test] public void ThirdSlotComponentCanCompleteWithoutReplacingOtherEquipment()
        {
            var fixture = Match(units: true);
            var player = fixture.match.GetPlayer("p1");
            var unit = player.Units.Single();
            var items = new ItemSystem();
            string[] inventory = { Id(ItemCatalog.RockyHelmet), Id(ItemCatalog.SitrusBerry), Id("x-defense"), Id("x-attack") };
            foreach (string id in inventory)
            {
                player.AddInventoryItem(id);
                items.Equip(fixture.match, "p1", unit.InstanceId, id, player.ItemRevision);
            }
            Assert.That(unit.ItemInstanceIds.Take(2), Is.EqualTo(inventory.Take(2)));
            Assert.That(ItemCatalog.DefinitionId(unit.ItemInstanceIds[2]), Is.EqualTo("crafted-punching-glove"));
            Assert.That(player.ItemInventory, Is.Empty);
        }

        [Test] public void CraftedSashSpendsOneChargePerLethalHitAndHealsAfterRecovery()
        {
            var catalog = Catalog();
            var equipment = Enumerable.Range(1, 3).Select(i => $"item:crafted-focus-sash:{i}:p2").ToArray();
            var setup = new[]{
                new BattleUnitSetup(catalog.CreateUnit("a","bulbasaur","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(0,0)),
                new BattleUnitSetup(catalog.CreateUnit("z","bulbasaur","p2",UnitRank.One,0,UnitPlacement.Unplaced,equipment),2,new BoardPosition(1,0))
            };
            var battle = BattleStateFactory.Create("sash-charges", 1, 123, 30, catalog, setup);
            var unit = U(battle, "z");
            unit.SetVitals(1, 0);
            for (int copy = 0; copy < 3; copy++)
            {
                float before = unit.CurrentHP;
                Assert.That(Hit(battle, 10000).AppliedDamage, Is.Zero);
                Assert.That(unit.CurrentHP, Is.EqualTo(before));
                Assert.That(battle.StatusEffects.BlocksActions(unit), Is.True);
                Assert.That(Hit(battle, 10000).AppliedDamage, Is.Zero);
                battle.CurrentTick += battle.TickRate;
                battle.Items.BeginTick();
                Assert.That(unit.CurrentHP, Is.EqualTo(before + unit.Stats.MaxHP * .1f).Within(.001f));
            }
            Assert.That(Hit(battle, 10000).Killed, Is.True);
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
        [Test] public void HostSnapshotsIncludeGrantedComponentsWithoutAChoice()
        {
            var f = RewardMatch();
            var host = new HostCommandProcessor(f.match, f.catalog, f.rounds);
            var first = host.Snapshot("p1");
            var second = host.Snapshot("p2");
            Assert.That(first.inventory.Length, Is.EqualTo(4));
            Assert.That(second.inventory.Length, Is.EqualTo(4));
            Assert.That(first.inventory.Intersect(second.inventory), Is.Empty);
            Assert.That(first.itemCandidates, Is.Empty);
            Assert.That(first.itemRewardRound, Is.EqualTo(3));
            Assert.That(host.PublicSnapshot().wildRound, Is.True);
        }
        [Test] public void WildScheduleStartsWithThreeEncountersAndContinuesEveryFiveRounds()
        {Assert.That(Enumerable.Range(1,40).Where(WildRoundSystem.IsWildRound),Is.EqualTo(new[]{1,2,3,9,14,19,24,29,34,39}));}
        [Test] public void LivingWildBattleFinishesAndCanAdvanceIntoPvP()
        {
            var f=Match(units:true);Assert.That(f.rounds.StartCombat(1),Is.True);
            for(int i=0;i<1400&&f.match.Phase==MatchPhase.Combat;i++)f.rounds.Step();
            Assert.That(f.rounds.IsSettled,Is.True);Assert.That(f.rounds.LastResult.PlayerResults.Values.All(r=>r.Outcome==RoundOutcome.Win),Is.True);
            Assert.That(f.match.Players.All(p=>p.WinStreak==0&&p.LoseStreak==0&&p.HP==60),Is.True);
            Assert.That(f.match.Players.All(p=>p.ItemInventory.Count==1 && p.ItemInventory.All(ItemCatalog.IsComponent)),Is.True);
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
            Assert.That(one.ItemInventory.Count, Is.EqualTo(7));
            Assert.That(one.LastItemRewardRound, Is.EqualTo(9));
            Assert.That(two.ItemInventory, Is.Empty);
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
        [Test] public void EarlyWildWinsGrantOneOneAndTwoComponents()
        {
            var f=Match(units:true);
            for(int round=1;round<=3;round++){
                f.rounds.StartCombat(round);for(int tick=0;tick<1400&&f.match.Phase==MatchPhase.Combat;tick++)f.rounds.Step();
                foreach(var p in f.match.Players){
                    Assert.That(p.HP,Is.EqualTo(60));
                    Assert.That(p.LastItemRewardRound, Is.EqualTo(round));
                    Assert.That(p.ItemInventory.Count, Is.EqualTo(round < 3 ? round : 4));
                    Assert.That(p.ItemInventory.All(ItemCatalog.IsComponent), Is.True);
                    Assert.That(p.ItemInventory.Distinct().Count(), Is.EqualTo(p.ItemInventory.Count));
                    int gold=p.Gold;f.rounds.SettleResult();Assert.That(p.Gold,Is.EqualTo(gold));
                }
                if(round<3)f.rounds.NextRound(round);
            }
        }
        [Test] public void ThirdRoundLossGrantsNoComponentAndWildDamageCanEliminate()
        {
            var f=Match();
            for(int round=1;round<=3;round++){
                f.rounds.StartCombat(round);
                foreach(var p in f.match.Players)Assert.That(p.ItemInventory,Is.Empty);
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
        [Test] public void InventoryCombineRequiresTwoDistinctOwnedComponentsAtCurrentRevision()
        {
            var f = Match();
            var player = f.match.GetPlayer("p1");
            string source = Id("x-attack"), target = Id("x-attack", round: 2), completed = Id(ItemCatalog.RockyHelmet);
            foreach (var id in new[] { source, completed, target }) player.AddInventoryItem(id);
            var executor = new MatchCommandExecutor(f.match, f.catalog, "p1");
            long revision = player.ItemRevision;

            Assert.That(executor.CombineItems(source, source, revision).Accepted, Is.False);
            Assert.That(executor.CombineItems(source, completed, revision).Accepted, Is.False);
            Assert.That(executor.CombineItems(source, Id("x-defense", "p2"), revision).Accepted, Is.False);
            Assert.That(executor.CombineItems(source, target, revision + 1).Failure, Is.EqualTo(MatchCommandFailure.Changed));
            Assert.That(player.ItemInventory, Is.EqualTo(new[] { source, completed, target }));
            Assert.That(player.ItemRevision, Is.EqualTo(revision));

            Assert.That(executor.CombineItems(source, target, revision).Accepted, Is.True);
            Assert.That(player.ItemInventory, Is.EqualTo(new[] { completed, $"item:crafted-kings-rock:craft-{revision + 1}:p1" }));
            Assert.That(player.ItemRevision, Is.EqualTo(revision + 1));
        }

        [Test] public void HostInventoryCombineReplayConsumesMaterialsOnlyOnce()
        {
            var f = Match();
            var player = f.match.GetPlayer("p1");
            string source = Id("x-defense"), target = Id("x-attack");
            player.AddInventoryItem(source);
            player.AddInventoryItem(target);
            var host = new HostCommandProcessor(f.match, f.catalog, f.rounds);
            var state = host.Snapshot("p1");
            var command = new MatchCommand
            {
                matchId = state.matchId, playerId = state.playerId, commandId = "combine",
                sequence = state.nextSequence, round = state.round, phase = state.phase,
                playerRevision = state.playerRevision, kind = MatchCommandKind.CombineItems,
                itemId = source, targetItemId = target, itemRevision = state.itemRevision
            };

            Assert.That(host.Process("p1", command).accepted, Is.True);
            Assert.That(host.Process("p1", command).accepted, Is.True);
            var conflict = command.Copy();
            conflict.targetItemId = source;
            Assert.That(host.Process("p1", conflict).code, Is.EqualTo("DuplicateConflict"));
            var updated = host.Snapshot("p1");
            Assert.That(updated.inventory.Select(ItemCatalog.DefinitionId), Is.EqualTo(new[] { "crafted-punching-glove" }));
            Assert.That(updated.itemRevision, Is.EqualTo(state.itemRevision + 1));
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
