#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PokeChess.Content;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class SkillContentAuthoringTests
    {
        private static RosterData Roster=>Resources.Load<RosterData>("GameRoster");
        public static IEnumerable EveryPokemonAndRank()
        {
            foreach(var p in Roster.Pokemon)foreach(var rank in new[]{UnitRank.One,UnitRank.Two,UnitRank.Three})
                yield return new TestCaseData(p.Id,rank).SetName("Skill_"+p.Id+"_"+(int)rank);
        }
        private sealed class OnlyCaster:SkillCombatBehaviorPolicy
        {
            public OnlyCaster(SkillCatalog catalog):base(catalog){}
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
            public override bool TryGetSkillDefinition(BattleState b,UnitCombatState u,out SkillDefinition s)
            {if(u.UnitInstanceId=="a")return base.TryGetSkillDefinition(b,u,out s);s=null;return false;}
        }
        [TestCaseSource(nameof(EveryPokemonAndRank))]
        public void EveryAuthoredSkillCastsAtEveryRankWithoutInvalidParticipants(string id,UnitRank rank)
        {
            var definitions=Roster.Pokemon.Select(p=>p.ToCore()).Concat(new[]{new PokemonDefinition("dummy","Dummy",1,
                new PokemonStats(1000000,0,0,.1f,0,0,1,1,0,0,0),"test","none")});
            var c=new PokemonCatalog(definitions);var a=c.CreateUnit("a",id,"p1",rank,0,UnitPlacement.Unplaced);
            var positions=new[]{new BoardPosition(2,3),new BoardPosition(3,3),new BoardPosition(4,3),new BoardPosition(4,4),new BoardPosition(1,2),new BoardPosition(2,2)};
            var units=new[]{a}.Concat(Enumerable.Range(1,5).Select(i=>c.CreateUnit("d"+i,"dummy",i<4?"p2":"p1",UnitRank.One,0,UnitPlacement.Unplaced))).ToArray();
            var b=BattleStateFactory.Create("skill-"+id+"-"+rank,1,123,30,c,units.Select((u,i)=>new BattleUnitSetup(u,i==0||i>=4?1:2,positions[i])));
            var caster=b.Units.Single(u=>u.UnitInstanceId=="a");caster.SetVitals(caster.Stats.MaxHP*.8f,caster.Stats.MaxEnergy);
            var sim=new BattleSimulation(b,new OnlyCaster(Roster.CreateSkills()),statusCatalog:Roster.CreateStatuses());
            Assert.DoesNotThrow(()=>{for(int i=0;i<240&&b.Result==BattleResult.InProgress;i++)sim.Step();},id+"/"+rank);
            Assert.That(sim.GetRuntime("a").SkillCast,Is.Not.Null,id+": never started");
            Assert.That(sim.GetRuntime("a").SkillCast.EffectApplied,Is.True,id+": no effect executed");
        }
        [Test] public void RegisteredContentValidatesWithoutAnExternalNumericSource()
        {
            Assert.DoesNotThrow(()=>Roster.Validate());
            Assert.That(Roster.Pokemon.Length,Is.EqualTo(109));
            foreach(var p in Roster.Pokemon)
            {
                Assert.That(p.LocalizedName,Is.Not.Empty,p.Id);Assert.That(p.Skill.DisplayName,Is.Not.Empty,p.Id);
                Assert.That(p.RankHP.Length,Is.EqualTo(3),p.Id);Assert.That(p.RankAttack.Length,Is.EqualTo(3),p.Id);
            }
            Assert.That(Roster.Pokemon.Any(p=>p.Id=="eevee"),Is.False);
            Assert.That(Roster.Pokemon.Single(p=>p.Id=="nidoran-f").Evolutions.Single().Target.Id,Is.EqualTo("nidorina"));
        }
        [TestCase(77, 100, 95)]
        [TestCase(100.1f, 100, 114.95f)]
        [TestCase(77, 150, 109.25f)]
        [TestCase(100.1f, 150, 129.2f)]
        public void WeedleMixedDamageUsesSameRankBaseline(float attack,float ap,float expected)
        {
            var effect = Roster.FindSkill("skill-weedle").ToCore(UnitRank.Three).Effects.First(e => e.IsDamage);
            Assert.That(effect.ResolveValue(UnitRank.Three,ap,attack,77), Is.EqualTo(expected).Within(.001f));
        }

        [Test] public void ExtremeThreeStarValuesAreAuthoredLiterally()
        {
            var snorlax=Roster.CreateStatuses().Get(Roster.FindSkill("skill-snorlax").ToCore(UnitRank.Three).Effects.Single(e => e.Type == SkillEffectType.ApplyStatus).ResolveStatusId(UnitRank.Three));
            Assert.That(snorlax.RecoveryFraction,Is.EqualTo(4));Assert.That(snorlax.RestReduction,Is.EqualTo(.99f));Assert.That(snorlax.ReflectionMultiplier,Is.EqualTo(9.99f));
            var lapras=Roster.FindSkill("skill-lapras").ToCore(UnitRank.Three);Assert.That(lapras.Effects[0].RankValues[2],Is.EqualTo(4000f).Within(.0001));
            Assert.That(Roster.CreateStatuses().Get(lapras.Effects[1].ResolveStatusId(UnitRank.Three)).Duration,Is.EqualTo(99));
        }
    }
}
#endif
