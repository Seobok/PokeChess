using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class LaprasPassengerTests
    {
        private static UnitCombatState U(BattleState b,string id)=>b.Units.Single(u=>u.UnitInstanceId==id);
        private static BattleState Battle(int team=1,bool right=true)
        {
            PokemonDefinition Def(string id,float hp,float attack)=>new PokemonDefinition(id,id,1,new PokemonStats(hp,attack,100,.5f,0,0,1,2,100,0,.2f,critChance:0),"test","test",traitIds:Array.Empty<string>());
            var catalog=new PokemonCatalog(new[]{Def("lapras",1000,100),Def("rider",2000,200),Def("enemy",1000,100)});
            var boat=new BoardPosition(3,team==1?2:5);var rider=new BoardPosition(3+(right?(team==1?1:-1):(team==1?-1:1)),boat.Row);
            BattleUnitSetup Setup(string id,int unitTeam,BoardPosition p)=>new BattleUnitSetup(catalog.CreateUnit(id,id,"owner"+unitTeam,UnitRank.One,0,UnitPlacement.OnBoard(p)),unitTeam,p);
            var b=BattleStateFactory.Create("boarding",1,42,30,catalog,new[]{Setup("lapras",team,boat),Setup("rider",team,rider),Setup("enemy",3-team,new BoardPosition(6,7))});
            b.Synergies.Initialize();return b;
        }
        [TestCase(1)][TestCase(2)] public void RightSideBoardsAndBonusesPersistAfterDisembarking(int team)
        {
            var b=Battle(team);var boat=U(b,"lapras");var rider=U(b,"rider");
            Assert.That(rider.IsAlive&&rider.IsEmbarked&&!rider.IsOnBoard&&!rider.IsTargetable,Is.True);
            Assert.That(b.Board.OccupiedCount,Is.EqualTo(2));Assert.That(boat.Stats.MaxHP,Is.EqualTo(1300).Within(.001));
            Assert.That(boat.CurrentHP,Is.EqualTo(1300).Within(.001));Assert.That(boat.EffectiveStats.Attack,Is.EqualTo(140).Within(.001));
            boat.SetVitals(975,0);b.Synergies.AdvancePassengers();Assert.That(rider.IsOnBoard,Is.False);
            boat.SetVitals(974,0);b.Synergies.AdvancePassengers();Assert.That(rider.IsOnBoard&&!rider.IsEmbarked,Is.True);
            Assert.That(rider.Position,Is.EqualTo(new BoardPosition(3,team==1?1:6)));Assert.That(rider.CurrentShield,Is.EqualTo(300).Within(.001));
            Assert.That(b.Shields.Active.Single(s=>s.TargetId=="rider").ExpireTick,Is.EqualTo(120));
            b.CurrentTick=3000;boat.RefreshEquipment();b.Synergies.AdvancePassengers();b.Shields.BeginTick();
            Assert.That(boat.Stats.MaxHP,Is.EqualTo(1300).Within(.001));Assert.That(boat.EffectiveStats.Attack,Is.EqualTo(140).Within(.001));
            Assert.That(rider.CurrentShield,Is.Zero);Assert.That(rider.CurrentHP,Is.EqualTo(2000));
        }
        [TestCase(1)][TestCase(2)] public void WrongSideDoesNotBoard(int team)
        {var b=Battle(team,false);Assert.That(U(b,"rider").IsOnBoard,Is.True);Assert.That(U(b,"lapras").Stats.MaxHP,Is.EqualTo(1000));}
        [TestCase(CrowdControlKind.Stun)][TestCase(CrowdControlKind.Root)][TestCase(CrowdControlKind.Slow)]
        [TestCase(CrowdControlKind.Silence)][TestCase(CrowdControlKind.Disarm)][TestCase(CrowdControlKind.Taunt)]
        public void AppliedCrowdControlDisembarksOnlyOnce(CrowdControlKind control)
        {
            var b=Battle();b.StatusEffects.Apply(new StatusEffectDefinition("test-cc",2,crowdControl:control,slowFraction:control==CrowdControlKind.Slow?.2f:0),"enemy","lapras");
            Assert.That(U(b,"lapras").HasCrowdControl(control),Is.True);Assert.That(U(b,"rider").IsOnBoard,Is.True);
            Assert.That(U(b,"rider").CurrentShield,Is.EqualTo(300));b.Synergies.Disembark(U(b,"lapras"));
            Assert.That(b.Shields.EventsThisTick.Count(e=>e.Kind==ShieldEventKind.Applied&&e.Shield.TargetId=="rider"),Is.EqualTo(1));
        }
        [TestCase(false)][TestCase(true)] public void BlockedRearUsesNearestEmptyCellDeterministically(bool blockNeighbors)
        {
            BoardPosition Run()
            {
                var b=Battle();var boat=U(b,"lapras");b.TryAddObstacle(new BoardPosition(3,1));
                if(blockNeighbors)foreach(var cell in b.Board.EmptyNeighbors(boat.Position))b.TryAddObstacle(cell);
                int distance=b.Board.Cells.Where(p=>!b.Board.IsOccupied(p)).Min(p=>HexCoordinates.Distance(boat.Position,p));
                b.Synergies.Disembark(boat);var rider=U(b,"rider");Assert.That(HexCoordinates.Distance(boat.Position,rider.Position),Is.EqualTo(distance));
                Assert.That(b.Board.TryGetOccupant(rider.Position,out var id)&&id=="rider",Is.True);return rider.Position;
            }
            Assert.That(Run(),Is.EqualTo(Run()));
        }
        [Test] public void ForcedMovementDisembarksWithoutBlockingItsReservedLanding()
        {
            var b=Battle();var boat=U(b,"lapras");var simulation=new BattleSimulation(b);
            b.TryMoveUnit("enemy",new BoardPosition(3,3));
            Assert.That(b.Movement.TryStart(U(b,"enemy"),boat,new SkillMovementDefinition(SkillMovementKind.Knockback)),Is.True);
            var movement=b.Movement.Active.Single(m=>m.UnitId=="lapras");var landing=movement.Path.Last();
            Assert.That(U(b,"rider").IsOnBoard,Is.True);Assert.That(U(b,"rider").Position,Is.Not.EqualTo(landing));
            b.CurrentTick=movement.EndTick;b.Movement.Advance();Assert.That(boat.Position,Is.EqualTo(landing));
        }
        [Test] public void ReservedRearCellIsNotUsedForDisembarking()
        {
            var b=Battle();var rear=new BoardPosition(3,1);b.Movement.Reservations.TryReserve("traveler",rear);
            b.Synergies.Disembark(U(b,"lapras"));Assert.That(U(b,"rider").Position,Is.Not.EqualTo(rear));
        }
        [TestCase(StatusBehavior.Sleep)][TestCase(StatusBehavior.Freeze)][TestCase(StatusBehavior.Paralysis)]
        [TestCase(StatusBehavior.Confusion)][TestCase(StatusBehavior.Blind)]
        public void ControlBehaviorsDisembark(StatusBehavior behavior)
        {
            var b=Battle();b.StatusEffects.Apply(new StatusEffectDefinition("test-behavior",2,behavior:behavior),"enemy","lapras");
            Assert.That(U(b,"rider").IsOnBoard,Is.True);
        }
        [Test] public void LethalDamageReleasesPassengerBeforeElimination()
        {
            var b=Battle();var simulation=new BattleSimulation(b);simulation.Step();Assert.That(U(b,"rider").IsOnBoard,Is.False);
            new DamageProcessor().Apply(b,new DamageRequest("enemy","lapras",5000,DamageType.True));
            Assert.That(U(b,"rider").IsOnBoard,Is.True);Assert.That(U(b,"rider").ActionState,Is.EqualTo(CombatActionState.Idle));
            simulation.Step();Assert.That(b.Result,Is.EqualTo(BattleResult.InProgress));
        }
        [Test] public void FullBoardWaitsAndUsesCarriersVacatedCellOnDeath()
        {
            var b=Battle();var simulation=new BattleSimulation(b);
            foreach(var cell in b.Board.Cells.Where(p=>!b.Board.IsOccupied(p)).ToArray())b.TryAddObstacle(cell);
            b.Synergies.Disembark(U(b,"lapras"));Assert.That(U(b,"rider").IsEmbarked,Is.True);
            U(b,"lapras").SetVitals(0,0);simulation.Step();Assert.That(U(b,"rider").IsOnBoard,Is.True);
            Assert.That(U(b,"rider").Position,Is.EqualTo(U(b,"lapras").Position));Assert.That(b.Result,Is.EqualTo(BattleResult.InProgress));
        }
    }
}
