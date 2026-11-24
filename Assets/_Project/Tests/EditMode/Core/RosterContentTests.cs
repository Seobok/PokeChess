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
        [TestCase(1,"magikarp","잉어킹",2)]
        [TestCase(2,"magikarp","잉어킹",3)]
        [TestCase(3,"rattata","꼬렛",3)]
        [TestCase(9,"gyarados","갸라도스",1)]
        [TestCase(15,"gyarados","갸라도스",1)]
        [TestCase(21,"gyarados","갸라도스",1)]
        [TestCase(39,"gyarados","갸라도스",1)]
        public void TutorialWildBattleUsesDisplayedSpecies(int round,string species,string name,int count)
        {
            var catalog=GameContent.CreateCatalog();
            var match=PokeChess.Core.Match.MatchStateFactory.CreateWithPool("wild-species",new[]{"p1","p2"},catalog,new[]{"caterpie"});
            match.TransitionTo(PokeChess.Core.Match.MatchPhase.Starting);
            match.TransitionTo(PokeChess.Core.Match.MatchPhase.Preparation);
            for(int i=1;i<round;i++){
                match.TransitionTo(PokeChess.Core.Match.MatchPhase.Combat);
                match.TransitionTo(PokeChess.Core.Match.MatchPhase.Result);
                match.TransitionTo(PokeChess.Core.Match.MatchPhase.Preparation);
            }
            var factory=typeof(PokeChess.Core.Match.WildRoundSystem).GetMethod("CreateBattle",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            var ally=catalog.CreateUnit("ally","caterpie","p1",UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(0,0)));
            var battle=(BattleState)factory.Invoke(null,new object[]{match,catalog,match.GetPlayer("p1"),new[]{new BattleUnitSetup(ally,1,new BoardPosition(0,0))},30});
            var enemies=battle.Units.Where(u=>u.TeamId==2).ToArray();
            Assert.That(enemies.Length,Is.EqualTo(count));
            foreach(var unit in enemies){
                Assert.That(unit.DefinitionId,Is.EqualTo(species));
                Assert.That(catalog.Get(unit.DefinitionId).DisplayName,Is.EqualTo(name));
                Assert.That(unit.Stats.MaxEnergy,Is.Zero);
                if(round>=9){
                    Assert.That(unit.Stats.MaxHP,Is.EqualTo(PokeChess.Core.Match.WildRoundSystem.BossStats(round).MaxHP));
                    Assert.That(unit.Stats.Attack,Is.EqualTo(PokeChess.Core.Match.WildRoundSystem.BossStats(round).Attack));
                    Assert.That(unit.Rank,Is.EqualTo(UnitRank.One));
                }
            }
            Assert.That(GameContent.CreateDefinitions().Any(d=>d.Id==species),Is.False);
        }
        [Test]
        public void WildBossGrowsEveryEncounterWithBoundedSpeedAndSize()
        {
            var previous=PokeChess.Core.Match.WildRoundSystem.BossStats(9);
            for(int round=15;round<=99;round+=6){
                var next=PokeChess.Core.Match.WildRoundSystem.BossStats(round);
                Assert.That(next.MaxHP,Is.GreaterThan(previous.MaxHP));
                Assert.That(next.Attack,Is.GreaterThan(previous.Attack));
                Assert.That(next.AttackSpeed,Is.InRange(previous.AttackSpeed,1.1f));
                Assert.That(PokeChess.Core.Match.WildRoundSystem.BossVisualScale(round),Is.InRange(1f,1.42f));
                previous=next;
            }
            var animations=PokeChess.Client.Data.PokemonAnimationLibrary.Load();
            foreach(var action in new[]{"Idle","Walk","Attack","Hurt"})
                Assert.That(animations.Find("gyarados",action)?.Frames,Is.Not.Null.And.Not.Empty);
            var node=new GameObject("WildBossScaleTest",typeof(RectTransform));
            try {
                var view=node.AddComponent<PokeChess.Client.UI.PokemonSpriteView>();
                view.Initialize("gyarados","wild-9-p1-0");
                Assert.That(view.ShowIdle(0),Is.True);
                var initial=view.CharacterImage.rectTransform.sizeDelta;
                view.Initialize("gyarados","wild-45-p1-0");view.ShowIdle(0);
                Assert.That(view.CharacterImage.rectTransform.sizeDelta.x/initial.x,Is.EqualTo(1.42f).Within(.001f));
                view.Initialize("gyarados","ordinary-unit");view.ShowIdle(0);
                Assert.That(view.CharacterImage.rectTransform.sizeDelta.x,Is.EqualTo(initial.x).Within(.001f));
            } finally {UnityEngine.Object.DestroyImmediate(node);}
        }
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
        [Test]
        public void NatureSummonsHaveCommonDefinitionsButAreNotShopCandidates()
        {
            var catalog = PokeChess.Content.GameContent.CreateCatalog();
            var shop = PokeChess.Content.GameContent.CreateDefinitions().Select(definition => definition.Id).ToArray();
            foreach (string id in new[] { "exeggcute", "exeggutor" })
            {
                var definition = catalog.Get(id);
                Assert.That(definition.Evolutions, Is.Empty);
                Assert.That(definition.TraitIds, Is.Empty);
                Assert.That(shop.Contains(id), Is.False);
            }
            Assert.That(PokeChess.Content.GameContent.DisplayName("exeggcute"), Is.EqualTo("아라리"));
            Assert.That(PokeChess.Content.GameContent.DisplayName("exeggutor"), Is.EqualTo("나시"));
        }
    }
}
