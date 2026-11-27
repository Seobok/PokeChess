using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class SynergyTests
    {
        private static PokemonDefinition Def(string id,params string[] traits)=>new PokemonDefinition(id,id,1,new PokemonStats(1000,100,100,.5f,0,0,1,2,100,0,.2f,critChance:0),"test","test",traitIds:traits);
        private static BattleState Battle(string trait,int count)
        {
            var defs=Enumerable.Range(0,count).Select(i=>Def("member"+i,trait)).Concat(new[]{Def("ally"),Def("enemy")}).ToArray();
            var catalog=new PokemonCatalog(defs);
            var setups=defs.Select((d,i)=>new BattleUnitSetup(catalog.CreateUnit(d.Id,d.Id,d.Id=="enemy"?"p2":"p1",UnitRank.One,0,UnitPlacement.Unplaced),d.Id=="enemy"?2:1,new BoardPosition(i%7,i/7+2)));
            var b=BattleStateFactory.Create("test",1,1,30,catalog,setups);b.Synergies.Initialize();return b;
        }
        private static UnitCombatState U(BattleState b,string id="member0")=>b.Units.First(u=>u.UnitInstanceId==id);
        private static DamageResult Hit(BattleState b,string source="member0",string target="enemy",bool basic=true,long cast=0,DamageOrigin origin=DamageOrigin.Direct,float amount=10)=>new DamageProcessor().Apply(b,new DamageRequest(source,target,amount,DamageType.Physical,isBasicAttack:basic,origin:origin,skillPolicy:basic?null:new SkillDamagePolicy(null,false,0,1000,cast)));
        [Test] public void NewCatalogHasEightOriginsNineTraitsAndNoLegacyIds(){Assert.That(SynergyTraits.Origins.Count,Is.EqualTo(8));Assert.That(SynergyTraits.CombatTraits.Count,Is.EqualTo(9));Assert.That(SynergyTraits.All,Does.Not.Contain("assault"));Assert.That(SynergyTraits.ForPokemon("gastly"),Does.Contain(SynergyTraits.Disruptor));Assert.That(SynergyTraits.ForPokemon("psyduck"),Does.Contain(SynergyTraits.Invoker));Assert.That(SynergyTraits.ForPokemon("hitmonlee"),Does.Contain(SynergyTraits.Assassin));}
        [Test] public void EvolutionsShareOneFamilyCount(){var count=SynergySystem.Count(new[]{Def("charmander",SynergyTraits.Sorcerer),Def("charizard",SynergyTraits.Sorcerer),Def("pikachu",SynergyTraits.Sorcerer)}).First(c=>c.TraitId==SynergyTraits.Sorcerer);Assert.That(count.Count,Is.EqualTo(2));Assert.That(count.Tier,Is.EqualTo(2));}
        [TestCase("guardian",3,2,1)][TestCase("guardian",5,5,0)][TestCase("tracker",3,3,1)][TestCase("torrent",8,6,2)][TestCase("assassin",4,3,0)] public void ThresholdsAreSpecificToEachTrait(string trait,int count,int tier,int needed){var c=new SynergyCount(trait,count);Assert.That(c.Tier,Is.EqualTo(tier));Assert.That(c.Needed,Is.EqualTo(needed));}
        [Test] public void BruiserAppliesHighestTierAndPreservesHealthRatio(){var b=Battle(SynergyTraits.Bruiser,6);Assert.That(U(b).Stats.MaxHP,Is.EqualTo(1460).Within(.01));Assert.That(U(b,"ally").Stats.MaxHP,Is.EqualTo(1080).Within(.01));Assert.That(U(b).CurrentHP,Is.EqualTo(U(b).Stats.MaxHP));}
        [Test] public void GuardianMarksDoNotStackAndFirstHitOnlyOnce(){var b=Battle(SynergyTraits.Guardian,4);Assert.That(U(b).EffectiveStats.Armor,Is.EqualTo(20));Assert.That(b.Synergies.IsGuarded(U(b,"ally")),Is.True);var a=Hit(b,"enemy","ally",amount:100);var c=Hit(b,"enemy","ally",amount:100);Assert.That(a.PostMitigationDamage,Is.EqualTo(79));Assert.That(c.PostMitigationDamage,Is.EqualTo(93));Assert.That(b.Synergies.EventsThisTick.Count(e=>e.Detail=="FIRST_HIT"&&e.TargetId=="ally"),Is.EqualTo(1));}
        [TestCase(2, 2)][TestCase(2, 3)][TestCase(4, 2)][TestCase(5, 3)]
        public void GuardianProtectsBothHorizontalSidesAndExcludesDiagonals(int tier, int row)
        {
            var definitions = Enumerable.Range(0,tier).Select(i=>Def("guard"+i,SynergyTraits.Guardian))
                .Concat(new[]{Def("left"),Def("right"),Def("diagonal"),Def("enemy")}).ToArray();
            var catalog = new PokemonCatalog(definitions);
            var roster = definitions.Select((d,i)=>catalog.CreateUnit(d.Id,d.Id,d.Id=="enemy"?"p2":"p1",UnitRank.One,0,
                UnitPlacement.OnBoard(d.Id=="left"?new BoardPosition(2,row):d.Id=="right"?new BoardPosition(4,row):
                d.Id=="diagonal"?new BoardPosition(3,row+1):d.Id=="enemy"?new BoardPosition(6,6):
                i==0?new BoardPosition(3,row):new BoardPosition(i-1,0)))).ToArray();
            var battle=BattleStateFactory.Create("guardian-sides",1,1,30,catalog,
                roster.Select(u=>new BattleUnitSetup(u,u.OwnerPlayerId=="p2"?2:1,u.Placement.Position.Value)));
            battle.Synergies.Initialize();
            Assert.That(battle.Synergies.IsGuarded(U(battle,"left")),Is.True);
            Assert.That(battle.Synergies.IsGuarded(U(battle,"right")),Is.True);
            Assert.That(battle.Synergies.IsGuarded(U(battle,"diagonal")),Is.False);
            var links=SynergySystem.PreviewGuardianLinks(roster.Where(u=>u.OwnerPlayerId=="p1"),catalog).ToArray();
            Assert.That(links,Does.Contain(("guard0","left")));
            Assert.That(links,Does.Contain(("guard0","right")));
            Assert.That(links.Any(link=>link.target=="diagonal"),Is.False);
            var emptyPreview=SynergySystem.PreviewGuardianTiles(roster.Where(u=>u.DefinitionId.StartsWith("guard")),catalog).ToArray();
            Assert.That(emptyPreview,Does.Contain(new BoardPosition(2,row)));
            Assert.That(emptyPreview,Does.Contain(new BoardPosition(4,row)));
            Assert.That(battle.TryMoveUnit("left",new BoardPosition(6,4)),Is.EqualTo(BoardOperationResult.Success));
            Assert.That(battle.Synergies.IsGuarded(U(battle,"left")),Is.True);
        }

        [TestCase(0,1)][TestCase(6,5)]
        public void GuardianSideTilesStayInsideBoardAtEdges(int column,int expected)
        {
            Assert.That(SynergySystem.GuardianSideTiles(new BoardPosition(column,2)),
                Is.EquivalentTo(new[]{new BoardPosition(expected,2)}));
        }

        [Test] public void DuelistOnlyStacksSuccessfulBasicsAndCapsAtSix(){var b=Battle(SynergyTraits.Duelist,4);Hit(b,basic:false,cast:1);Assert.That(b.Synergies.DuelistStacks(U(b)),Is.Zero);for(int i=0;i<8;i++)Hit(b);Assert.That(b.Synergies.DuelistStacks(U(b)),Is.EqualTo(6));Assert.That(U(b).EffectiveStats.AttackSpeed,Is.EqualTo(.68f).Within(.001));}
        [Test] public void TrackerCountsOneHitPerCastAndIgnoresDotAndExpires(){var b=Battle(SynergyTraits.Tracker,3);Hit(b,basic:false,cast:10);Hit(b,basic:false,cast:10);Hit(b,basic:false,cast:11,origin:DamageOrigin.Periodic);Assert.That(b.Synergies.TrackerStacks(U(b)),Is.EqualTo(1));Hit(b);Assert.That(b.Synergies.TrackerStacks(U(b)),Is.EqualTo(2));b.CurrentTick=90;Assert.That(b.Synergies.TrackerStacks(U(b)),Is.Zero);}
        [Test] public void TrackerCompletionDamageDoesNotRecursivelyStack(){var b=Battle(SynergyTraits.Tracker,3);Hit(b);Hit(b);int before=b.DamageResultsThisTick.Count;Hit(b);Assert.That(b.Synergies.TrackerStacks(U(b)),Is.EqualTo(3));Assert.That(b.DamageResultsThisTick.Count-before,Is.EqualTo(2));}
        [Test] public void SorcererCompletionStacksCapAndDoesNotTriggerAtRelease(){var b=Battle(SynergyTraits.Sorcerer,4);Assert.That(U(b).EffectiveStats.SpellPower,Is.EqualTo(124));b.Synergies.OnSkillEffect(U(b));Assert.That(U(b).EffectiveStats.SpellPower,Is.EqualTo(124));for(int i=0;i<8;i++)b.Synergies.OnSkillCompleted(U(b));Assert.That(U(b).EffectiveStats.SpellPower,Is.EqualTo(149));}
        [Test] public void InvokerStartsAtThreeSecondsAndClampsManaEvenDuringCasting(){var b=Battle(SynergyTraits.Invoker,4);U(b).ActionState=CombatActionState.Casting;b.CurrentTick=89;b.Synergies.Advance();Assert.That(U(b).CurrentEnergy,Is.Zero);b.CurrentTick=90;b.Synergies.Advance();Assert.That(U(b).CurrentEnergy,Is.EqualTo(10));Assert.That(U(b,"ally").CurrentEnergy,Is.EqualTo(6));U(b).SetVitals(1000,99);b.CurrentTick=180;b.Synergies.Advance();Assert.That(U(b).CurrentEnergy,Is.EqualTo(100));}
        [Test] public void SupportShieldSharedAcrossCastersAndManaHasTargetCooldown(){var b=Battle(SynergyTraits.Supporter,4);U(b,"ally").SetVitals(100,0);b.Synergies.OnSkillCompleted(U(b));b.Synergies.OnSkillCompleted(U(b,"member1"));Assert.That(U(b,"ally").CurrentShield,Is.EqualTo(140));Assert.That(U(b,"ally").CurrentEnergy,Is.EqualTo(8));b.CurrentTick=120;b.Synergies.OnSkillCompleted(U(b));Assert.That(U(b,"ally").CurrentEnergy,Is.EqualTo(8));b.CurrentTick=240;Assert.That(U(b,"ally").CurrentShield,Is.Zero);}
        [Test] public void DisruptorExcludesPoisonAndRefreshesWithoutStacking(){var b=Battle(SynergyTraits.Disruptor,6);var s=b.StatusEffects;s.Apply(new StatusEffectDefinition("poison",4,behavior:StatusBehavior.Poison),"member0","enemy");Assert.That(U(b,"enemy").EffectiveStats.Attack,Is.EqualTo(100));s.Apply(new StatusEffectDefinition("root",1,crowdControl:CrowdControlKind.Root),"member0","enemy");s.Apply(new StatusEffectDefinition("root",1,crowdControl:CrowdControlKind.Root),"member1","enemy");Assert.That(U(b,"enemy").EffectiveStats.Attack,Is.EqualTo(82).Within(.001));Assert.That(U(b,"enemy").EffectiveStats.EnergyGain,Is.EqualTo(.75f));}
        [Test] public void AssassinTakedownStealthAllowsExistingDot(){var b=Battle(SynergyTraits.Assassin,3);Hit(b,amount:2000);Assert.That(U(b).IsUntargetable,Is.True);Assert.DoesNotThrow(()=>Hit(b,"enemy","member0",false,origin:DamageOrigin.Debuff));}
        [Test] public void NatureSummonDoesNotCountAndHasNoPersistentPlacement(){var b=Battle(SynergyTraits.Nature,2);Assert.That(b.Units.Count(u=>u.IsSynergySummon),Is.EqualTo(1));Assert.That(b.Synergies.TeamCounts(1).First(c=>c.TraitId==SynergyTraits.Nature).Count,Is.EqualTo(2));}
        [Test] public void DisabledRulesGiveNoEffects(){var b=Battle(SynergyTraits.Bruiser,2);var catalog=new PokemonCatalog(new[]{Def("a",SynergyTraits.Bruiser),Def("b",SynergyTraits.Bruiser),Def("z")});var units=new[]{"a","b","z"}.Select((id,i)=>new BattleUnitSetup(catalog.CreateUnit(id,id,i==2?"p2":"p1",UnitRank.One,0,UnitPlacement.Unplaced),i==2?2:1,new BoardPosition(i,0)));var d=BattleStateFactory.Create("disabled",1,1,30,catalog,units,synergyRules:SynergyRules.Disabled);d.Synergies.Initialize();Assert.That(U(d,"a").Stats.MaxHP,Is.EqualTo(1000));}

        [Test] public void FlamePersistsForThreeFullDamagePulses(){var b=Battle(SynergyTraits.Flame,2);Hit(b,basic:false,cast:1);for(int tick=30;tick<=90;tick+=30){b.CurrentTick=tick;b.Synergies.Advance();}Assert.That(U(b,"enemy").CurrentHP,Is.EqualTo(915));Assert.That(b.Synergies.FireTiles,Is.Empty);}
        [Test] public void StormCircuitHasTwoSecondPerCasterCooldown(){var b=Battle(SynergyTraits.Storm,2);b.Synergies.OnSkillCompleted(U(b));b.Synergies.OnSkillCompleted(U(b));Assert.That(U(b,"enemy").CurrentHP,Is.EqualTo(908));b.CurrentTick=60;b.Synergies.OnSkillCompleted(U(b));Assert.That(U(b,"enemy").CurrentHP,Is.EqualTo(816));}
        [Test] public void TorrentEbbShieldsMembersOnlyAtTwo(){var b=Battle(SynergyTraits.Torrent,2);b.CurrentTick=330;b.Synergies.Advance();Assert.That(U(b).CurrentShield,Is.EqualTo(60));Assert.That(U(b,"ally").CurrentShield,Is.Zero);}
        [TestCase(2, 1)]
        [TestCase(4, 2)]
        public void EarthRocksBlockCellsWithoutAddingOccupants(int count, int markers)
        {
            var battle = Battle(SynergyTraits.Earth, count);
            var tiles = battle.Synergies.RockTiles(1);
            Assert.That(tiles.Count, Is.EqualTo(markers));
            Assert.That(battle.Board.OccupiedCount, Is.EqualTo(battle.Units.Count));
            foreach (var tile in tiles)
            {
                Assert.That(battle.Board.IsOccupied(tile), Is.True);
                Assert.That(battle.Board.TryGetOccupant(tile, out _), Is.False);
                Assert.That(battle.TryMoveUnit("ally", tile), Is.EqualTo(BoardOperationResult.CellOccupied));
                Assert.That(battle.TryMoveUnit("enemy", tile), Is.EqualTo(BoardOperationResult.CellOccupied));
            }
        }

        [Test] public void EarthRockForcesDetourAndStopsKnockback()
        {
            var battle = EarthDefenseBattle(4);
            var rock = new BoardPosition(4, 2);
            var start = U(battle, "ally").Position;
            var path = HexPathfinder.FindPath(battle.Board, start, new BoardPosition(5, 2));
            Assert.That(path.Succeeded, Is.True);
            Assert.That(path.MoveCount, Is.GreaterThan(2));
            Assert.That(path.Path.Contains(rock), Is.False);

            Assert.That(battle.TryMoveUnit("enemy", new BoardPosition(1, 2)), Is.EqualTo(BoardOperationResult.Success));
            Assert.That(battle.Movement.TryStart(U(battle, "enemy"), U(battle, "ally"),
                new SkillMovementDefinition(SkillMovementKind.Knockback, distance: 3)), Is.False);
            Assert.That(U(battle, "ally").Position, Is.EqualTo(start));
        }
        [Test] public void VenomLethalBasicContaminatesWithoutPoison(){var b=Battle(SynergyTraits.Venom,2);b.StatusEffects.Apply(new StatusEffectDefinition("test-poison",4,behavior:StatusBehavior.Poison),"member0","enemy");Hit(b,amount:2000);Assert.That(b.VenomEarned[1],Is.EqualTo(1));}
        [Test] public void StealthBlocksDirectTargetingButNotAreaOrExistingDot(){var b=Battle(SynergyTraits.Assassin,3);b.StatusEffects.Apply(new StatusEffectDefinition("synergy-stealth",1.2f,StatusTargetTeam.Ally,behavior:StatusBehavior.Untargetable),"member0","member0");Assert.That(U(b).IsTargetable,Is.False);Assert.Throws<InvalidOperationException>(()=>Hit(b,"enemy","member0"));Assert.DoesNotThrow(()=>new DamageProcessor().Apply(b,new DamageRequest("enemy","member0",20,DamageType.Magic,isAreaDamage:true)));Assert.DoesNotThrow(()=>Hit(b,"enemy","member0",false,origin:DamageOrigin.Periodic));}

        [Test] public void OneLongCastNeverAddsAnotherTrackerStackAfterExpiration(){var b=Battle(SynergyTraits.Tracker,3);Hit(b,basic:false,cast:77);b.CurrentTick=91;Hit(b,basic:false,cast:77);Assert.That(b.Synergies.TrackerStacks(U(b)),Is.Zero);Hit(b,basic:false,cast:78);Assert.That(b.Synergies.TrackerStacks(U(b)),Is.EqualTo(1));}
        [Test] public void InvalidOriginTuningIsRejected(){Assert.Throws<ArgumentOutOfRangeException>(()=>new SynergyOriginTuning(flameDamage:float.NaN));Assert.Throws<ArgumentOutOfRangeException>(()=>new SynergyOriginTuning(earthReduction:2));}

        [Test] public void MountainImmunityIncludesCarryMovement(){var c=new PokemonCatalog(new[]{Def("snorlax",SynergyTraits.Earth,SynergyTraits.Bruiser),Def("enemy")});var b=BattleStateFactory.Create("mountain",1,1,30,c,new[]{new BattleUnitSetup(c.CreateUnit("mountain","snorlax","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(2,2)),new BattleUnitSetup(c.CreateUnit("enemy","enemy","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(2,3))});b.Synergies.Initialize();Assert.That(b.Movement.TryStart(U(b,"enemy"),U(b,"mountain"),new SkillMovementDefinition(SkillMovementKind.CarryTarget)),Is.False);Assert.That(b.Movement.Active,Is.Empty);}

        [Test] public void AlreadyLaunchedAreaProjectileStillHitsStealthedTarget(){var b=Battle(SynergyTraits.Assassin,2);var source=U(b);var target=U(b,"enemy");Assert.That(b.Projectiles.TrySpawnSkill(source,target,56,6,new DamageRequest(source.UnitInstanceId,target.UnitInstanceId,50,DamageType.Magic),1),Is.True);b.StatusEffects.Apply(new StatusEffectDefinition("synergy-stealth",1.2f,StatusTargetTeam.Ally,behavior:StatusBehavior.Untargetable),target.UnitInstanceId,target.UnitInstanceId);b.CurrentTick=30;b.Projectiles.Advance(new DamageProcessor());Assert.That(target.CurrentHP,Is.EqualTo(950));}
        private static BattleState RevisionBattle(string trait,int count,int samples=0,int enemies=3) {
            var defs=Enumerable.Range(0,count).Select(i=>Def("r"+i,trait)).Concat(Enumerable.Range(0,enemies).Select(i=>Def("e"+i))).ToArray();
            var c=new PokemonCatalog(defs);
            var setup=defs.Select((d,i)=>new BattleUnitSetup(c.CreateUnit(d.Id,d.Id,d.Id.StartsWith("e")?"p2":"p1",UnitRank.One,0,UnitPlacement.Unplaced),
                d.Id.StartsWith("e")?2:1,d.Id.StartsWith("e")?new BoardPosition((i-count)%7,5+(i-count)/7):new BoardPosition(i%7,i/7)));
            var b=BattleStateFactory.Create("revision",1,1,30,c,setup);b.VenomSamples[1]=samples;b.Synergies.Initialize();return b;
        }
        [Test] public void StormDisconnectedCasterStillStrikesAndOnlyOnCompletion() {
            var b=RevisionBattle(SynergyTraits.Storm,2);U(b,"r1").SetPosition(new BoardPosition(6,3));
            // Rebuild after dispersing: initial circuit snapshot is what matters.
            var c=new PokemonCatalog(new[]{Def("a",SynergyTraits.Storm),Def("b",SynergyTraits.Storm),Def("z")});
            b=BattleStateFactory.Create("split",1,1,30,c,new[]{
                new BattleUnitSetup(c.CreateUnit("a","a","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(0,0)),
                new BattleUnitSetup(c.CreateUnit("b","b","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(6,0)),
                new BattleUnitSetup(c.CreateUnit("z","z","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(0,5))});
            b.Synergies.Initialize();b.Synergies.OnSkillEffect(U(b,"a"));Assert.That(U(b,"z").CurrentHP,Is.EqualTo(1000));
            b.Synergies.OnSkillCompleted(U(b,"a"));Assert.That(U(b,"z").CurrentHP,Is.EqualTo(920));
        }
        [Test] public void StormChainDecaysAndChargesOncePerCast() {
            var b=RevisionBattle(SynergyTraits.Storm,4);b.Synergies.OnSkillCompleted(U(b,"r0"));
            var hits=b.DamageResultsThisTick.Where(d=>d.Request.Origin==DamageOrigin.Synergy).ToArray();
            Assert.That(hits.Select(d=>d.Request.RawDamage),Is.EqualTo(new[]{100f,70f,49f}).Within(.01));
            Assert.That(hits.Select(d=>d.Request.TargetId).Distinct().Count(),Is.EqualTo(3));
            Assert.That(b.Synergies.StormEnergy(1),Is.EqualTo(1));
        }
        [Test] public void StormSharedEnergyDischargesAtFourAndResets() {
            var b=RevisionBattle(SynergyTraits.Storm,6);
            foreach(var u in b.Units.Where(u=>u.TeamId==1).Take(4))b.Synergies.OnSkillCompleted(u);
            Assert.That(b.Synergies.StormEnergy(1),Is.Zero);
            Assert.That(b.Synergies.EventsThisTick.Count(e=>e.Detail=="DISCHARGE"),Is.EqualTo(3));
            Assert.That(U(b,"e0").EffectiveStats.AttackSpeed,Is.EqualTo(.35f).Within(.001));
            Assert.That(b.Synergies.EventsThisTick.Last(e=>e.Detail=="CHARGE").Value,Is.Zero);
        }
        [Test] public void StormNoTargetsGivesNoCharge() {
            var b=RevisionBattle(SynergyTraits.Storm,4);
            foreach(var u in b.Units.Where(u=>u.TeamId==2)){u.SetVitals(0,0);b.TryRemoveUnit(u.UnitInstanceId);}
            b.Synergies.OnSkillCompleted(U(b,"r0"));Assert.That(b.Synergies.StormEnergy(1),Is.Zero);
        }
        [TestCase(2,700,35,30,30)][TestCase(4,1200,50,45,30)][TestCase(6,2050,65,65,40)][TestCase(10,7500,135,100,60)]
        public void NatureUsesFinalTierPreset(int count,int hp,int attack,int armor,int mana) {
            var b=RevisionBattle(SynergyTraits.Nature,count);var g=b.Units.Single(u=>u.IsSynergySummon);
            Assert.That(g.Stats.MaxHP,Is.EqualTo(hp));Assert.That(g.Stats.Attack,Is.EqualTo(attack));
            Assert.That(g.Stats.Armor,Is.EqualTo(armor));Assert.That(g.CurrentEnergy,Is.EqualTo(mana));Assert.That(g.SkillId,Is.EqualTo("synergy-nature-"+count));
        }
        [Test] public void NatureGuardianCastsItsOwnManaSkillAndIgnoresAlliedCasts() {
            var b=RevisionBattle(SynergyTraits.Nature,6);var g=b.Units.Single(u=>u.IsSynergySummon);
            g.SetPosition(new BoardPosition(0,4));
            for(int i=0;i<9;i++)b.Synergies.OnSkillCompleted(U(b,"r0"));
            Assert.That(g.CurrentShield,Is.Zero);
            g.SetEnergy(100);var sim=new BattleSimulation(b);
            for(int i=0;i<25;i++)sim.Step();
            Assert.That(g.CurrentShield,Is.GreaterThan(0));
            Assert.That(b.Synergies.EventsThisTick.Any(e=>e.Detail=="GUARDIAN_CAST")||g.CurrentEnergy<100,Is.True);
        }
        [Test] public void WorldTreeHasStartingShieldAndEightSecondPulse() {
            var b=RevisionBattle(SynergyTraits.Nature,10);var g=b.Units.Single(u=>u.IsSynergySummon);
            Assert.That(g.CurrentShield,Is.EqualTo(1000));
            b.CurrentTick=239;b.Synergies.Advance();Assert.That(U(b,"e0").CurrentHP,Is.EqualTo(1000));
            b.CurrentTick=240;b.Synergies.Advance();Assert.That(U(b,"e0").CurrentHP,Is.EqualTo(850));
            Assert.That(U(b,"e0").HasCrowdControl(CrowdControlKind.Root),Is.True);
            Assert.That(b.Synergies.EventsThisTick.Count(e=>e.Detail=="SUMMON" && e.TargetId==g.UnitInstanceId),Is.EqualTo(1));
            Assert.That(b.Synergies.EventsThisTick.Count(e=>e.Detail=="PULSE"),Is.EqualTo(1));
            Assert.That(b.Synergies.EventsThisTick.Count(e=>e.Detail=="PULSE_HIT"),Is.EqualTo(3));

            b.Synergies.BeginTick();
            U(b,"r0").SetVitals(500,0);
            b.Synergies.GuardianSkill(g);
            var heal=b.Synergies.EventsThisTick.Single(e=>e.Detail=="HEAL");
            Assert.That(heal.TargetId,Is.EqualTo("r0"));
            Assert.That(heal.Value,Is.EqualTo(75).Within(.001));
            Assert.That(b.Synergies.EventsThisTick.Count(e=>e.Detail=="JUDGMENT_HIT"),Is.EqualTo(3));
            Assert.That(b.Synergies.EventsThisTick.Any(e=>e.Detail=="PULSE"),Is.False);
        }
        [Test] public void VenomFirstDirectDamageOnceAndFiveSecondRefresh() {
            var b=RevisionBattle(SynergyTraits.Venom,2);Hit(b,"r0","e0");
            Assert.That(U(b,"e0").CurrentHP,Is.EqualTo(930));
            b.CurrentTick=149;Hit(b,"r1","e0");Assert.That(U(b,"e0").CurrentHP,Is.EqualTo(920));
            Assert.That(b.Synergies.EventsThisTick.Count(e=>e.Detail=="FIRST_HIT"),Is.EqualTo(1));
            Assert.That(b.Synergies.EventsThisTick.Count(e=>e.Detail=="REFRESH"),Is.EqualTo(1));
            Assert.That(b.Synergies.ContaminationEnd(U(b,"e0")),Is.EqualTo(299));
            b.CurrentTick=298;Assert.That(b.Synergies.IsContaminated(U(b,"e0")),Is.True);
            b.CurrentTick=299;Assert.That(b.Synergies.IsContaminated(U(b,"e0")),Is.False);
            Hit(b,"r0","e0");Assert.That(U(b,"e0").CurrentHP,Is.EqualTo(910));
        }
        [TestCase(2,1)][TestCase(4,2)][TestCase(6,3)]
        public void VenomAnyAllyKillAwardsTierSamples(int tier,int samples) {
            var b=RevisionBattle(SynergyTraits.Venom,tier);Hit(b,"r0","e0");Hit(b,"r1","e0",amount:2000);
            Assert.That(b.Synergies.VenomReward(1),Is.EqualTo(samples));
        }
        [TestCase(2,9,10)][TestCase(4,38,40)][TestCase(6,117,120)]
        public void VenomLiveCountIncludesEachEarnedSampleWithoutDoubleCounting(int tier,int start,int total)
        {
            var battle=RevisionBattle(SynergyTraits.Venom,tier,start);
            Assert.That(battle.Synergies.VenomSampleCount(1),Is.EqualTo(start));
            Hit(battle,"r0","e0",amount:2000);
            Assert.That(battle.Synergies.VenomSampleCount(1),Is.EqualTo(total));
            Assert.That(battle.Synergies.VenomSampleCount(2),Is.Zero);
            battle.VenomSamples[1]=total; // Settlement/public progress may arrive while the combat snapshot remains visible.
            Assert.That(battle.Synergies.VenomSampleCount(1),Is.EqualTo(total));
        }
        [Test] public void VenomLiveCountDoesNotPreAwardMinimumRoundReward()
        {
            var battle=RevisionBattle(SynergyTraits.Venom,2,9);
            Hit(battle,"r0","e0");Assert.That(battle.Synergies.VenomReward(1),Is.EqualTo(1));
            Assert.That(battle.Synergies.VenomSampleCount(1),Is.EqualTo(9));
            Hit(battle,"r1","e0",amount:2000);Assert.That(battle.Synergies.VenomSampleCount(1),Is.EqualTo(10));
            Assert.That(battle.Synergies.VenomResearch(1),Is.Zero);
        }
        [Test] public void VenomTransferIsLimitedAndDealsNoInitialDamage() {
            var b=RevisionBattle(SynergyTraits.Venom,6);Hit(b,"r0","e0",amount:2000);
            Assert.That(b.Synergies.IsContaminated(U(b,"e1")),Is.True);
            Assert.That(U(b,"e1").CurrentHP,Is.EqualTo(1000));
            var transfer = b.Synergies.EventsThisTick.Single(e=>e.Detail=="TRANSFER" && e.TargetId=="e1");
            Assert.That(transfer.Cell.Value, Is.EqualTo(U(b,"e0").Position));
            Assert.That(transfer.Destination.Value, Is.EqualTo(U(b,"e1").Position));
            Assert.That(b.Synergies.EventsThisTick.Any(e=>e.Detail=="FIRST_HIT" && e.TargetId=="e1"), Is.False);
            Hit(b,"r0","e1",amount:2000);Assert.That(b.VenomEarned[1],Is.EqualTo(6));
        }
        [Test] public void VenomMinimumRewardRequiresEligibleOriginalPvPTarget() {
            var b=RevisionBattle(SynergyTraits.Venom,2);Assert.That(b.Synergies.VenomReward(1),Is.Zero);
            Hit(b,"r0","e0");Assert.That(b.Synergies.VenomReward(1),Is.EqualTo(1));
            b.IsPvP=false;Assert.That(b.Synergies.VenomReward(1),Is.Zero);
            b=RevisionBattle(SynergyTraits.Venom,2);U(b,"e0").IsOriginalCombatUnit=false;Hit(b,"r0","e0");
            Assert.That(b.Synergies.VenomReward(1),Is.Zero);
        }
        [TestCase(2,9,0)][TestCase(2,10,10)][TestCase(4,39,10)][TestCase(4,40,40)][TestCase(6,119,40)][TestCase(6,120,120)][TestCase(2,120,10)]
        public void VenomResearchRequiresStartSamplesAndTier(int tier,int samples,int research) {
            var b=RevisionBattle(SynergyTraits.Venom,tier,samples);Assert.That(b.Synergies.VenomResearch(1),Is.EqualTo(research));
            b.VenomSamples[1]=999;Assert.That(b.Synergies.VenomResearch(1),Is.EqualTo(research));
        }
        [Test] public void VenomManaSuppressionUsesStrongestPenaltyAndLeavesExistingMana() {
            var b=RevisionBattle(SynergyTraits.Venom,2,10);var e=U(b,"e0");e.SetEnergy(20);Hit(b,"r0","e0");
            b.Synergies.BeginTick();
            e.SetEnergy(20);b.Energy.Gain(e,10,EnergyChangeReason.Item);Assert.That(e.CurrentEnergy,Is.EqualTo(28));
            b.StatusEffects.Apply(new StatusEffectDefinition("synergy-disruptor-mana",3,modifiers:new[]{new StatModifierDefinition(CombatStat.EnergyGain,StatModifierType.Percentage,-.25f)}),"r0","e0");
            b.Energy.Refund(e,10);Assert.That(e.CurrentEnergy,Is.EqualTo(35.5f));
            Assert.That(e.EffectiveStats.EnergyGain,Is.EqualTo(.75f));
            Assert.That(b.Synergies.HasVenomManaErosion(e), Is.False);
            Assert.That(b.Synergies.EventsThisTick.Count(signal=>signal.Detail=="EROSION"), Is.EqualTo(1));
        }
        [TestCase(4,40,50)][TestCase(6,120,120)]
        public void VenomReactionCompletesWithCooldownAndReplacement(int tier,int samples,int damage) {
            var b=RevisionBattle(SynergyTraits.Venom,tier,samples);var e=U(b,"e0");Hit(b,"r0","e0");float before=e.CurrentHP;
            b.Synergies.OnSkillEffect(e);Assert.That(e.CurrentHP,Is.EqualTo(before));
            b.Synergies.OnSkillCompleted(e);Assert.That(e.CurrentHP,Is.EqualTo(before-damage));
            Assert.That(U(b,"e1").CurrentHP,Is.EqualTo(samples==120?880:1000));
            Assert.That(b.Synergies.EventsThisTick.Count(signal=>signal.Detail=="MUTATION"), Is.EqualTo(samples==120?1:0));
            Assert.That(b.Synergies.EventsThisTick.Count(signal=>signal.Detail=="MUTATION_HIT"), Is.EqualTo(samples==120?2:0));
            Assert.That(b.Synergies.EventsThisTick.Count(signal=>signal.Detail=="REACTION"), Is.EqualTo(samples==40?1:0));
            b.Synergies.OnSkillCompleted(e);Assert.That(e.CurrentHP,Is.EqualTo(before-damage));
        }
        [Test] public void VenomIndependentPoisonAndNoSampleCap() {
            var b=RevisionBattle(SynergyTraits.Venom,6,120,10);
            foreach(var e in b.Units.Where(u=>u.TeamId==2).ToArray())Hit(b,"r0",e.UnitInstanceId,amount:2000);
            Assert.That(b.VenomEarned[1],Is.EqualTo(30));
            Assert.That(b.StatusEffects.Active.Any(s=>s.Definition.Behavior==StatusBehavior.Poison),Is.False);
        }

        [Test] public void VenomControlSkillContaminatesWithoutPoisonAndSelfBuffDoesNot() {
            var b=RevisionBattle(SynergyTraits.Venom,2);var source=U(b,"r0");var target=U(b,"e0");
            b.StatusEffects.UseCatalog(new StatusCatalog(new[]{new StatusEffectDefinition("test-sleep",2,behavior:StatusBehavior.Sleep)}));
            var skill=new SkillDefinition("control",0,0,SkillTargetRule.Enemy,TargetLostPolicy.Cancel,new[]{new SkillEffectDefinition(SkillEffectType.ApplyStatus,EffectTargetSelector.CastTarget,0,statusId:"test-sleep")});
            new SkillEffectExecutor().Execute(b,source,target,skill);
            Assert.That(b.Synergies.IsContaminated(target),Is.True);Assert.That(target.CurrentHP,Is.EqualTo(940));
            var self=new SkillDefinition("self",0,0,SkillTargetRule.Self,TargetLostPolicy.ContinueWithoutTarget,SkillEffectKind.SelfShield,10);
            new SkillEffectExecutor().Execute(b,source,source,self);
            Assert.That(b.Synergies.IsContaminated(source),Is.False);
        }
        [Test] public void VenomKillByNonVenomAllyStillCountsAndExpiredMarkDoesNot() {
            var b=Battle(SynergyTraits.Venom,4);Hit(b);Hit(b,"ally","enemy",amount:2000);
            Assert.That(b.VenomEarned[1],Is.EqualTo(2));
            b=Battle(SynergyTraits.Venom,4);Hit(b);b.CurrentTick=150;Hit(b,"ally","enemy",amount:2000);
            Assert.That(b.Synergies.VenomReward(1),Is.EqualTo(1));Assert.That(b.VenomEarned.ContainsKey(1),Is.False);
        }
        [Test] public void VenomPeriodicAndSynergyHitsNeverApplyContamination() {
            var b=RevisionBattle(SynergyTraits.Venom,2);
            Hit(b,"r0","e0",false,origin:DamageOrigin.Periodic);Hit(b,"r0","e1",false,origin:DamageOrigin.Synergy);
            Assert.That(b.Synergies.IsContaminated(U(b,"e0")),Is.False);Assert.That(b.Synergies.IsContaminated(U(b,"e1")),Is.False);
            Assert.That(b.Synergies.VenomReward(1),Is.Zero);
        }
        [Test] public void VenomReactionCooldownExpiresAfterFiveSecondsWhileRefreshed() {
            var b=RevisionBattle(SynergyTraits.Venom,4,40);Hit(b,"r0","e0");var e=U(b,"e0");
            b.Synergies.OnSkillCompleted(e);b.CurrentTick=149;Hit(b,"r0","e0");
            float hp=e.CurrentHP;b.Synergies.OnSkillCompleted(e);Assert.That(e.CurrentHP,Is.EqualTo(hp));
            b.CurrentTick=150;b.Synergies.OnSkillCompleted(e);Assert.That(e.CurrentHP,Is.EqualTo(hp-50));
        }

        private static BattleState StarterBattle(int count)
        {
            var definitions=new[]{Def("bulbasaur"),Def("charmander"),Def("squirtle")}.Take(count)
                .Concat(new[]{Def("ally"),Def("enemy0"),Def("enemy1")}).ToArray();
            var catalog=new PokemonCatalog(definitions);
            var setups=definitions.Select((d,i)=>new BattleUnitSetup(catalog.CreateUnit(d.Id,d.Id,d.Id.StartsWith("enemy")?"p2":"p1",UnitRank.One,0,UnitPlacement.Unplaced),
                d.Id.StartsWith("enemy")?2:1,new BoardPosition(i,0)));
            var battle=BattleStateFactory.Create("starter",1,1,30,catalog,setups);battle.Synergies.Initialize();return battle;
        }
        private static void StarterHit(BattleState battle,string source,string target,float amount)
            => new DamageProcessor().Apply(battle,new DamageRequest(source,target,amount,DamageType.True));

        [TestCase(1,0)][TestCase(2,15)][TestCase(3,15)]
        public void StarterManaRequiresTwoFamiliesAndTakedownParticipation(int count,int energy)
        {
            var battle=StarterBattle(count);StarterHit(battle,"bulbasaur","enemy0",10);
            StarterHit(battle,"ally","enemy0",2000);
            Assert.That(U(battle,"bulbasaur").CurrentEnergy,Is.EqualTo(energy));
            if(count>=2)Assert.That(U(battle,"charmander").CurrentEnergy,Is.Zero);
            StarterHit(battle,"ally","enemy1",2000);
            Assert.That(U(battle,"bulbasaur").CurrentEnergy,Is.EqualTo(energy));
        }
        [Test] public void StarterThreeHealsLowestCurrentHealthOncePerDefeatedEnemy()
        {
            var battle=StarterBattle(3);U(battle,"bulbasaur").SetVitals(300,0);
            U(battle,"charmander").AddRoundStats(3000,0);U(battle,"charmander").SetVitals(500,0);
            StarterHit(battle,"bulbasaur","enemy0",10);StarterHit(battle,"charmander","enemy0",10);
            StarterHit(battle,"ally","enemy0",2000);
            Assert.That(U(battle,"bulbasaur").CurrentHP,Is.EqualTo(400));
            Assert.That(U(battle,"charmander").CurrentHP,Is.EqualTo(500));
            Assert.That(U(battle,"bulbasaur").CurrentEnergy,Is.EqualTo(15));Assert.That(U(battle,"charmander").CurrentEnergy,Is.EqualTo(15));
            Assert.That(U(battle,"squirtle").CurrentEnergy,Is.Zero);
            Assert.That(battle.Synergies.EventsThisTick.Count(e=>e.TraitId=="starter"&&e.Detail=="HEAL"),Is.EqualTo(1));
            StarterHit(battle,"squirtle","enemy1",2000);
            Assert.That(U(battle,"bulbasaur").CurrentHP,Is.EqualTo(500));Assert.That(U(battle,"squirtle").CurrentEnergy,Is.EqualTo(15));
        }
        [Test] public void StarterTwoDoesNotHealAndThreeCannotResurrect()
        {
            var battle=StarterBattle(2);U(battle,"bulbasaur").SetVitals(300,0);StarterHit(battle,"bulbasaur","enemy0",2000);
            Assert.That(U(battle,"bulbasaur").CurrentHP,Is.EqualTo(300));
            battle=StarterBattle(3);U(battle,"bulbasaur").SetVitals(0,0);U(battle,"charmander").SetVitals(950,0);
            StarterHit(battle,"squirtle","enemy0",2000);
            Assert.That(U(battle,"bulbasaur").CurrentHP,Is.Zero);Assert.That(U(battle,"charmander").CurrentHP,Is.EqualTo(1000));
        }
        [Test] public void StarterManaCapsWithoutReducingExistingOverflow()
        {
            var battle=StarterBattle(2);U(battle,"bulbasaur").SetVitals(1000,95);StarterHit(battle,"bulbasaur","enemy0",2000);
            Assert.That(U(battle,"bulbasaur").CurrentEnergy,Is.EqualTo(100));
            U(battle,"bulbasaur").SetVitals(1000,120);StarterHit(battle,"bulbasaur","enemy1",2000);
            Assert.That(U(battle,"bulbasaur").CurrentEnergy,Is.EqualTo(120));
        }

        private static BattleState EarthDefenseBattle(int tier, float allyHP = 2000)
        {
            var ally = new PokemonDefinition("ally", "ally", 1,
                new PokemonStats(allyHP,100,100,.5f,0,0,1,2,100,0,.2f,critChance:0), "test", "test");
            var definitions = Enumerable.Range(0, tier).Select(i => Def("member"+i, SynergyTraits.Earth))
                .Concat(new[]{ ally, Def("enemy") }).ToArray();
            var catalog = new PokemonCatalog(definitions);
            var setups = definitions.Select((definition, index) =>
            {
                bool enemy = definition.Id == "enemy";
                var position = enemy ? new BoardPosition(3, 5) : definition.Id == "ally"
                    ? new BoardPosition(3, 2) : new BoardPosition(index, 0);
                return new BattleUnitSetup(catalog.CreateUnit(definition.Id, definition.Id, enemy ? "p2" : "p1",
                    UnitRank.One, 0, UnitPlacement.Unplaced), enemy ? 2 : 1, position);
            });
            var battle = BattleStateFactory.Create("earth-defense",1,1,30,catalog,setups);
            battle.PreparedRocks[1] = new[]{ new BoardPosition(2, 2), new BoardPosition(4, 2) };
            battle.Synergies.Initialize();
            return battle;
        }

        [TestCase(2, 2000)][TestCase(4, 2200)][TestCase(6, 2200)]
        public void EarthStartBonusesStackAndShieldExpiresWithoutRefresh(int tier, float hp)
        {
            var battle = EarthDefenseBattle(tier);
            var ally = U(battle,"ally");
            Assert.That(ally.Stats.MaxHP, Is.EqualTo(hp).Within(.001));
            Assert.That(ally.CurrentHP, Is.EqualTo(hp).Within(.001));
            Assert.That(ally.CurrentShield, Is.EqualTo(160));
            Assert.That(battle.Shields.Active.Single(e=>e.TargetId=="ally").ExpireTick, Is.EqualTo(180));
            battle.CurrentTick = 179;
            battle.Shields.BeginTick();
            Assert.That(ally.CurrentShield, Is.EqualTo(160));
            battle.CurrentTick = 180;
            battle.Shields.BeginTick();
            Assert.That(ally.CurrentShield, Is.Zero);
            Hit(battle,"enemy","ally",amount:100);
            battle.CurrentTick = 600;
            battle.Synergies.Advance();
            Assert.That(ally.CurrentShield, Is.Zero);
            Assert.That(battle.Synergies.EventsThisTick.Any(e=>e.Detail=="protective-pulse"), Is.False);
        }

        [TestCase(2, 100)][TestCase(4, 100)][TestCase(6, 90)]
        public void EarthBonusesFollowStartingRecipientAfterMovement(int tier, float damage)
        {
            var battle = EarthDefenseBattle(tier);
            Assert.That(battle.TryMoveUnit("ally",new BoardPosition(6,0)), Is.EqualTo(BoardOperationResult.Success));
            Assert.That(U(battle,"ally").CurrentShield, Is.EqualTo(160));
            Assert.That(Hit(battle,"enemy","ally",amount:100).PostMitigationDamage, Is.EqualTo(damage).Within(.001));
            var trueHit = new DamageProcessor().Apply(battle,new DamageRequest("enemy","ally",100,DamageType.True,origin:DamageOrigin.Debuff));
            Assert.That(trueHit.PostMitigationDamage, Is.EqualTo(damage).Within(.001));
            Assert.That(Hit(battle,"enemy","ally",basic:false,origin:DamageOrigin.Periodic,amount:100).PostMitigationDamage, Is.EqualTo(damage).Within(.001));
            Assert.That(U(battle,"member0").CurrentShield, Is.Zero);
            battle.TryMoveUnit("member0",new BoardPosition(3,2));
            Assert.That(U(battle,"member0").CurrentShield, Is.Zero);
            Assert.That(Hit(battle,"enemy","member0",amount:100).PostMitigationDamage, Is.EqualTo(100).Within(.001));
        }
    }
}
