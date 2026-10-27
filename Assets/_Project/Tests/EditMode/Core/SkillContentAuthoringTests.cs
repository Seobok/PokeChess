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
        [Serializable] private sealed class Plan {public Row[] Rows;}
        [Serializable] private sealed class Row {
            public string Id,Name,Skill,Effect,Notes;public int Cost,AttackRange;
            public float[] HP,Attack;public float AttackSpeed,MaxEnergy,StartingEnergy,Armor,MagicResistance;
        }
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
        [Test] public void RegisteredContentMatchesEverySourceRowAndHasNoOldEeveeFamily()
        {
            var source=JsonUtility.FromJson<Plan>(System.IO.File.ReadAllText("Assets/_Project/Content/SkillPlan.json"));
            Assert.That(source.Rows.Length,Is.EqualTo(109));Assert.That(Roster.Pokemon.Length,Is.EqualTo(109));
            foreach(var row in source.Rows) {
                var p=Roster.Pokemon.Single(u=>u.Id==row.Id);Assert.That(p.Cost,Is.EqualTo(row.Cost),row.Id);Assert.That(p.LocalizedName,Is.EqualTo(row.Name));
                Assert.That(p.Skill.DisplayName,Is.EqualTo(row.Skill));Assert.That(p.Skill.Description,Is.EqualTo(row.Effect));Assert.That(p.Skill.Notes,Is.EqualTo(row.Notes));
                for(int i=0;i<3;i++) {
                    var stats=RankRules.Default.Apply(p.ToCore(),(UnitRank)(i+1));
                    Assert.That(stats.MaxHP,Is.EqualTo(row.HP[i]),row.Id);Assert.That(stats.Attack,Is.EqualTo(row.Attack[i]),row.Id);
                    Assert.That(stats.SpellPower,Is.EqualTo(100));Assert.That(stats.AttackSpeed,Is.EqualTo(row.AttackSpeed));
                    Assert.That(stats.MaxEnergy,Is.EqualTo(row.MaxEnergy));Assert.That(stats.StartingEnergy,Is.EqualTo(row.StartingEnergy));
                    Assert.That(stats.Armor,Is.EqualTo(row.Armor));Assert.That(stats.MagicResistance,Is.EqualTo(row.MagicResistance));
                    Assert.That(stats.AttackRange,Is.EqualTo(row.AttackRange));
                }
            }
            Assert.That(Roster.Pokemon.Any(p=>p.Id=="eevee"),Is.False);
            Assert.That(Roster.Pokemon.Single(p=>p.Id=="nidoran-f").Evolutions.Single().Target.Id,Is.EqualTo("nidorina"));
        }
        [Test] public void ExtremeThreeStarValuesAreAuthoredLiterally()
        {
            var snorlax=Roster.Skills.Single(s=>s.Id=="skill-snorlax-r3").Effects.Single().Status.ToCore();
            Assert.That(snorlax.RecoveryFraction,Is.EqualTo(4));Assert.That(snorlax.RestReduction,Is.EqualTo(.99f));Assert.That(snorlax.ReflectionMultiplier,Is.EqualTo(9.99f));
            var lapras=Roster.Skills.Single(s=>s.Id=="skill-lapras-r3");Assert.That(lapras.Effects[0].RankValues[2],Is.EqualTo(4000f).Within(.0001));
            Assert.That(lapras.Effects[1].Status.Duration,Is.EqualTo(99));
        }
    }
}
#endif
