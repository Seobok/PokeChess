using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PokeChess.Content;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;

namespace PokeChess.Core.Tests
{
    public sealed class RosterContentTests
    {
        [Test]
        public void NumericStatusAndBossAuthoringConvertsWithoutChangingLegacyDefaults()
        {
            var skill=ScriptableObject.CreateInstance<SkillData>();var poison=ScriptableObject.CreateInstance<StatusData>();
            var rest=ScriptableObject.CreateInstance<StatusData>();var pokemon=ScriptableObject.CreateInstance<PokemonData>();
            var roster=ScriptableObject.CreateInstance<RosterData>();
            try {
                poison.Id="new-poison";poison.TargetTeam=StatusTargetTeam.Enemy;poison.Behavior=StatusBehavior.Poison;
                rest.Id="new-rest";rest.TargetTeam=StatusTargetTeam.Ally;rest.Behavior=StatusBehavior.RestSleep;rest.Duration=2;rest.RecoveryFraction=.4f;
                skill.Id="numeric-content";skill.HasActivation=true;
                skill.Effects=new[]{new SkillEffectData{Type=SkillEffectType.ProjectileDamage,ValueSource=SkillValueSource.TargetMaxHP,
                    RankValues=new float[]{.1f,.15f,.2f},HasNumeric=true,Numeric=new SkillNumericData{HasDamageCap=true,DamageCap=100,Lifesteal=.6f},
                    PathImpactEffects=new[]{new SkillEffectData{Type=SkillEffectType.ApplyStatus,Status=poison,StatusTargetTeam=StatusTargetTeam.Enemy,
                        RankStatuses=new[]{poison,poison,poison}}}}};
                roster.Skills=new[]{skill};roster.Statuses=new[]{poison,rest};roster.Validate();
                var core=skill.ToCore();Assert.That(core.Effects.Single().Numeric.DamageCap,Is.EqualTo(100));
                Assert.That(core.Activation.MinimumAdjacentEnemies,Is.EqualTo(2));Assert.That(poison.ToCore().MaxStack,Is.EqualTo(3));
                Assert.That(rest.ToCore().RecoveryFraction,Is.EqualTo(.4f));
                pokemon.Id="boss";pokemon.DisplayName="Boss";pokemon.RoleId="tank";pokemon.IsBoss=true;pokemon.Skill=skill;
                Assert.That(pokemon.ToCore().IsBoss,Is.True);
            } finally {foreach(var o in new UnityEngine.Object[]{skill,poison,rest,pokemon,roster})UnityEngine.Object.DestroyImmediate(o);}
        }
        [Test]
        public void SpatialMovementAndNestedPathImpactAuthoringValidateAndConvert()
        {
            var skill=ScriptableObject.CreateInstance<SkillData>();var status=ScriptableObject.CreateInstance<StatusData>();
            var roster=ScriptableObject.CreateInstance<RosterData>();
            try {
                status.Id="root";status.CrowdControl=CrowdControlKind.Root;status.TargetTeam=StatusTargetTeam.Enemy;
                var cc=new SkillEffectData{Type=SkillEffectType.ApplyStatus,Status=status,StatusTargetTeam=StatusTargetTeam.Enemy,
                    RankStatuses=new[]{status,status,status}};
                var push=new SkillEffectData{Type=SkillEffectType.Move,Movement=new SkillMovementData{
                    Kind=SkillMovementKind.Knockback,ImpactEffects=new[]{cc}}};
                skill.Id="spatial-content";skill.Effects=new[]{new SkillEffectData{Type=SkillEffectType.PathProjectileDamage,
                    TargetSelector=EffectTargetSelector.Spatial,Spatial=new SkillSpatialData{Shape=SkillAreaShape.Line,Extent=4},
                    RankValues=new float[]{10,20,30},Returns=true,PathImpactEffects=new[]{push}}};
                roster.Skills=new[]{skill};Assert.Throws<InvalidOperationException>(()=>roster.Validate());
                roster.Statuses=new[]{status};roster.Validate();
                var core=skill.ToCore().Effects.Single();Assert.That(core.Returns,Is.True);
                Assert.That(core.Spatial.Extent,Is.EqualTo(4));Assert.That(skill.AllEffects.Length,Is.EqualTo(3));
                Assert.That(core.PathImpactEffects.Single().Movement.ImpactEffects.Single().StatusId,Is.EqualTo("root"));
            } finally {UnityEngine.Object.DestroyImmediate(skill);UnityEngine.Object.DestroyImmediate(status);UnityEngine.Object.DestroyImmediate(roster);}
        }
        [Test]
        public void TimedSkillAuthoringConvertsAndValidatesStatusReferences()
        {
            var skill=ScriptableObject.CreateInstance<SkillData>();
            var status=ScriptableObject.CreateInstance<StatusData>();
            var roster=ScriptableObject.CreateInstance<RosterData>();
            try
            {
                skill.Id="timed-content";skill.CastTime=1;
                var hit=new SkillEffectData{Type=PokeChess.Core.Battle.SkillEffectType.Damage,
                    RankValues=new float[]{10,20,30}};
                skill.Steps=new[]{new SkillStepData{Effects=new[]{hit},Interval=.25f,Count=3}};
                var model=skill.ToCore();Assert.That(model.Steps.Count,Is.EqualTo(1));
                Assert.That(model.Steps[0].Count,Is.EqualTo(3));Assert.That(model.Effects.Count,Is.EqualTo(1));
                Assert.That(skill.AllEffects.Single(),Is.SameAs(hit));
                roster.Skills=new[]{skill};roster.Validate();
                skill.Steps[0].Effects=new[]{new SkillEffectData{Type=PokeChess.Core.Battle.SkillEffectType.ApplyStatus,
                    Status=status,RankStatuses=new[]{status,status,status}}};
                Assert.Throws<InvalidOperationException>(()=>roster.Validate());
            }
            finally{UnityEngine.Object.DestroyImmediate(skill);UnityEngine.Object.DestroyImmediate(status);UnityEngine.Object.DestroyImmediate(roster);}
        }
        static void Same(object expected,object actual,string path)
        {
            if(expected==null||actual==null){Assert.That(actual,Is.EqualTo(expected),path);return;}
            var type=expected.GetType();
            if(type.IsPrimitive||type.IsEnum||expected is string){Assert.That(actual,Is.EqualTo(expected),path);return;}
            if(expected is IEnumerable sequence)
            {
                var a=sequence.Cast<object>().ToArray();var b=((IEnumerable)actual).Cast<object>().ToArray();
                Assert.That(b.Length,Is.EqualTo(a.Length),path);
                for(int i=0;i<a.Length;i++)Same(a[i],b[i],path+"["+i+"]");return;
            }
            foreach(var p in type.GetProperties().Where(p=>p.GetIndexParameters().Length==0))
                Same(p.GetValue(expected),p.GetValue(actual),path+"."+p.Name);
        }
        [Test]
        public void ContentHasExplicitRankStatsAndValidatesNewRoster()
        {
            var roster=Resources.Load<RosterData>("GameRoster");roster.Validate();
            var definitions=PrototypeEvolutionRoster.WithEvolutions(PrototypeRoster.CreateDefinitions()).ToArray();
            Assert.That(roster.Pokemon.Length,Is.EqualTo(109));Assert.That(roster.ShopPokemon.Length,Is.EqualTo(57));
            var catalog=roster.CreateCatalog();foreach(var p in roster.Pokemon) {
                var d=catalog.Get(p.Id);Assert.That(p.HasProvisionalStats,Is.False,p.Id);
                Assert.That(d.RankHP.Count,Is.EqualTo(3));Assert.That(d.RankAttack.Count,Is.EqualTo(3));
                Assert.That(d.BaseStats.SpellPower,Is.EqualTo(100));
            }
            foreach(var s in roster.Skills)Assert.DoesNotThrow(()=>s.ToCore(),s.Id);
            foreach(var s in roster.Statuses)Assert.DoesNotThrow(()=>s.ToCore(),s.Id);
        }
        [Test]
        public void DuplicateAndUnregisteredEntriesAreRejected()
        {
            var source=Resources.Load<RosterData>("GameRoster");var clone=UnityEngine.Object.Instantiate(source);
            try
            {
                clone.Pokemon=source.Pokemon.Concat(new[]{source.Pokemon[0]}).ToArray();
                Assert.Throws<InvalidOperationException>(()=>clone.Validate());
                clone.Pokemon=source.Pokemon;clone.Skills=source.Skills.Skip(1).ToArray();
                Assert.Throws<InvalidOperationException>(()=>clone.Validate());
                clone.Skills=source.Skills;clone.Statuses=Array.Empty<StatusData>();
                Assert.Throws<InvalidOperationException>(()=>clone.Validate());
            }
            finally{UnityEngine.Object.DestroyImmediate(clone);}
        }
        [Test]
        public void EvolutionCyclesAndMissingVisualsAreRejected()
        {
            var source=Resources.Load<RosterData>("GameRoster");var roster=UnityEngine.Object.Instantiate(source);
            var a=UnityEngine.Object.Instantiate(source.Pokemon[0]);var b=UnityEngine.Object.Instantiate(source.Pokemon[1]);
            try
            {
                b.Cost=a.Cost;roster.Pokemon=new[]{a,b};roster.ShopPokemon=new[]{a};
                a.Evolutions=new[]{new EvolutionData{Target=b}};b.Evolutions=new[]{new EvolutionData{Target=a}};
                Assert.Throws<InvalidOperationException>(()=>roster.Validate());
                a.Evolutions=Array.Empty<EvolutionData>();b.Evolutions=Array.Empty<EvolutionData>();a.Visual=null;
                Assert.Throws<InvalidOperationException>(()=>roster.Validate());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);UnityEngine.Object.DestroyImmediate(roster);
            }
        }
        [Test]
        public void PresentationUsesAuthoredNamesAndClefairySprites()
        {
            Assert.That(GameContent.DisplayName("geodude"),Is.EqualTo("꼬마돌"));
            Assert.That(GameContent.DisplayName("clefairy"),Is.EqualTo("삐삐"));
            Assert.That(GameContent.DisplayName("skill-clefairy"),Is.EqualTo("손가락흔들기"));
            Assert.That(GameContent.DisplayName("skill-gastly"),Is.EqualTo("이상한빛"));
            var library=PokeChess.Client.Data.PokemonAnimationLibrary.Load();
            Assert.That(library.Find("geodude","Idle").PokemonId,Is.EqualTo("geodude"));
            Assert.That(library.Find("clefairy","Idle").PokemonId,Is.EqualTo("clefairy"));
        }
    }
}
