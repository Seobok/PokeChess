using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class SimultaneousAttackTests
    {
        private static BattleState Create(bool reverse=false,bool swapIds=false,float hit=.25f,bool ranged=false,bool far=false)
        {
            var stats=new PokemonStats(100,10,0,1,0,0,1,2,0,0,hit,ranged?AttackDeliveryType.Projectile:AttackDeliveryType.Melee,6);
            var catalog=new PokemonCatalog(new[]{new PokemonDefinition("t","T",1,stats,"r","s")});
            var setup=new[]{new BattleUnitSetup(catalog.CreateUnit(swapIds?"z":"a","t","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(0,0)),
                new BattleUnitSetup(catalog.CreateUnit(swapIds?"a":"z","t","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(far?6:1,far?7:0))};
            return BattleStateFactory.Create("b",1,123,30,catalog,reverse?setup.Reverse():setup);
        }
        [TestCase(false,false,0f)][TestCase(true,false,0f)][TestCase(false,true,0f)][TestCase(true,true,.25f)]
        public void EqualMeleeDuelDrawsRegardlessOfIdsAndSetupOrder(bool reverse,bool swap,float hit)
        {
            var b=Create(reverse,swap,hit);var sim=new BattleSimulation(b);
            for(int i=0;i<1400 && b.Result==BattleResult.InProgress;i++)sim.Step();
            Assert.That(b.Result,Is.EqualTo(BattleResult.Draw));Assert.That(b.EndReason,Is.EqualTo(BattleEndReason.Elimination));
            Assert.That(b.Units.All(u=>u.CurrentHP==0 && !u.IsAlive && !u.IsOnBoard),Is.True);
            Assert.That(b.DamageResultsThisTick.Count,Is.EqualTo(2));Assert.That(b.DamageResultsThisTick.All(d=>d.Killed),Is.True);
            Assert.That(b.LifecycleEventsThisTick.Count(e=>e.Kind==BattleLifecycleEventKind.UnitDied),Is.EqualTo(2));
        }
        [TestCase(false)][TestCase(true)]
        public void MirroredDeploymentDuelDraws(bool swap)
        {
            var b=Create(swapIds:swap,far:true);var sim=new BattleSimulation(b);
            for(int i=0;i<1400 && b.Result==BattleResult.InProgress;i++)sim.Step();
            Assert.That(b.Result,Is.EqualTo(BattleResult.Draw));Assert.That(b.EndReason,Is.EqualTo(BattleEndReason.Elimination));
        }
        [Test] public void UnequalHpStillProducesWinner()
        {
            var b=Create();var sim=new BattleSimulation(b);b.Units.Single(u=>u.TeamId==2).SetVitals(10,0);
            for(int i=0;i<40 && b.Result==BattleResult.InProgress;i++)sim.Step();
            Assert.That(b.Result,Is.EqualTo(BattleResult.TeamOneWin));Assert.That(b.Units.Single(u=>u.TeamId==1).CurrentHP,Is.EqualTo(90));
        }
        private sealed class Delayed:BasicAttackCombatBehaviorPolicy
        {
            public override CombatActionTiming GetAttackTiming(BattleState b,UnitCombatState u)=>new CombatActionTiming(10,u.TeamId==1?0:1);
        }
        [Test] public void LethalHitFromEarlierTickCancelsLaterAttack()
        {
            var b=Create();foreach(var u in b.Units)u.SetVitals(10,0);var sim=new BattleSimulation(b,new Delayed());sim.Step();
            Assert.That(b.Result,Is.EqualTo(BattleResult.TeamOneWin));Assert.That(b.DamageResultsThisTick.Count,Is.EqualTo(1));
            Assert.That(b.Units.Single(u=>u.TeamId==1).CurrentHP,Is.EqualTo(10));
        }
    }
}
