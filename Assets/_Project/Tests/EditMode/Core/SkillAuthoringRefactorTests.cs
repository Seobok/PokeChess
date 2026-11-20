#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PokeChess.Content;
using PokeChess.Client.UI;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class SkillAuthoringRefactorTests
    {
        private static RosterData Roster=>Resources.Load<RosterData>("GameRoster");
        [Test] public void RankedAssetsGenerateStableDefinitionsWithoutSeparateRankFiles()
        {
            var catalog=Roster.CreateSkills();
            foreach(var data in Roster.Skills.Where(s=>s.Ranked))
            {
                Assert.That(data.ToDefinitions().Count(),Is.EqualTo(4),data.Id);
                for(int rank=1;rank<=3;rank++)
                {
                    Assert.That(catalog.TryGet(data.RankId(rank),out _),Is.True,data.Id);
                    Assert.That(File.Exists("Assets/_Project/Content/Skills/"+data.RankId(rank)+".asset"),Is.False,data.Id);
                }
            }
            Assert.DoesNotThrow(()=>Roster.Validate());
        }
        [Test] public void PikachuUsesOneEffectSetAndRankSpecificMagnitudes()
        {
            var data=Roster.FindSkill("skill-pikachu");
            Assert.That(data.Ranked,Is.True);Assert.That(data.RankOverrides,Is.Empty);
            Assert.That(data.Effects[0].RankValues,Is.EqualTo(new[]{85f,115f,155f}));
            foreach(var rank in new[]{UnitRank.One,UnitRank.Two,UnitRank.Three})
            {
                var skill=data.ToCore(rank);Assert.That(skill.Id,Is.EqualTo("skill-pikachu-r"+(int)rank));
                Assert.That(skill.Effects[0].ResolveValue(rank,150,0),Is.EqualTo(data.Effects[0].RankValues[(int)rank-1]*1.5f));
                Assert.That(Roster.CreateStatuses().Get(skill.Effects[1].ResolveStatusId(rank)).Duration,Is.EqualTo(2));
            }
        }
        [Test] public void DescriptionReadsEditedRankValuesAndStatusDurations()
        {
            var data=UnityEngine.Object.Instantiate(Roster.FindSkill("skill-pikachu"));
            try
            {
                data.DescriptionTemplate="{value:0} 피해 / {duration:1}초 / {range}칸";
                data.Effects[0].RankValues[1]=230;
                var stats=RankRules.Default.Apply(Roster.Pokemon.Single(p=>p.Id=="pikachu").ToCore(),UnitRank.Two);
                Assert.That(SkillDescription.Format(data,UnitRank.Two,stats,Roster.CreateStatuses()),Is.EqualTo("230 피해 / 2초 / 3칸"));
                Assert.That(Roster.FindSkill("skill-pikachu").Effects[0].RankValues[1],Is.EqualTo(115));
                data.DescriptionTemplate="{unknown}";
                Assert.Throws<InvalidOperationException>(()=>SkillDescription.Format(data,UnitRank.Two,stats,Roster.CreateStatuses()));
            }
            finally{UnityEngine.Object.DestroyImmediate(data);}
        }
        [Test] public void DuplicateRankOverridesAreRejectedBeforeConversion()
        {
            var roster=UnityEngine.Object.Instantiate(Roster);var skill=UnityEngine.Object.Instantiate(Roster.FindSkill("skill-pikachu"));
            try
            {
                roster.Skills=roster.Skills.Select(s=>s.Id==skill.Id?skill:s).ToArray();
                // Pokemon references must follow the temporary replacement for this isolated validation.
                roster.Pokemon=Array.Empty<PokemonData>();roster.ShopPokemon=Array.Empty<PokemonData>();
                skill.RankOverrides=new[]{new SkillRankOverride{Rank=UnitRank.Two},new SkillRankOverride{Rank=UnitRank.Two}};
                Assert.Throws<InvalidOperationException>(()=>roster.Validate());
            }
            finally{UnityEngine.Object.DestroyImmediate(skill);UnityEngine.Object.DestroyImmediate(roster);}
        }
        [Test] public void LabRankDefinitionSelectionKeepsCasterAndDescriptionAtTheSelectedRank()
        {
            var lab=new EffectLabSession(Roster);lab.SelectSkill("skill-pikachu-r3");
            Assert.That(lab.Unit(lab.CasterId).Rank,Is.EqualTo(UnitRank.Three));
            lab.SetRank(lab.CasterId,UnitRank.Two);
            Assert.That(lab.SkillId,Is.EqualTo("skill-pikachu"));
        }
        [Test] public void LabReloadRebuildsTheCatalogFromEditedAssetsAndPreservesPlacement()
        {
            var roster=UnityEngine.Object.Instantiate(Roster);
            var skill=UnityEngine.Object.Instantiate(Roster.FindSkill("skill-caterpie"));
            var pokemon=UnityEngine.Object.Instantiate(Roster.Pokemon.Single(p=>p.Id=="caterpie"));
            try
            {
                pokemon.Skill=skill;roster.Pokemon=roster.Pokemon.Select(p=>p.Id==pokemon.Id?pokemon:p).ToArray();
                roster.ShopPokemon=roster.ShopPokemon.Select(p=>p.Id==pokemon.Id?pokemon:p).ToArray();
                roster.Skills=roster.Skills.Select(s=>s.Id==skill.Id?skill:s).ToArray();
                var lab=new EffectLabSession(roster);lab.SetRank(lab.CasterId,UnitRank.Two);
                var position=lab.Unit(lab.CasterId).Position;var before=lab.Battle;
                skill.CastTime=.9f;lab.ReloadContent();
                Assert.That(lab.Battle,Is.Not.SameAs(before));Assert.That(lab.Unit(lab.CasterId).Position,Is.EqualTo(position));
                Assert.That(lab.Unit(lab.CasterId).Rank,Is.EqualTo(UnitRank.Two));
                lab.Cast();lab.Step();Assert.That(lab.Simulation.GetRuntime(lab.CasterId).SkillCast.Definition.CastTime,Is.EqualTo(.9f));
            }
            finally{UnityEngine.Object.DestroyImmediate(pokemon);UnityEngine.Object.DestroyImmediate(skill);UnityEngine.Object.DestroyImmediate(roster);}
        }
        [Test] public void RandomBranchesRemainRegisteredAndReachable()
        {
            var data=Roster.FindSkill("skill-mew");var catalog=Roster.CreateSkills();
            foreach(var rank in new[]{UnitRank.One,UnitRank.Two,UnitRank.Three})
            {
                var choice=data.ToCore(rank);Assert.That(choice.VariantIds.Count,Is.EqualTo(3));
                foreach(var id in choice.VariantIds)Assert.That(catalog.TryGet(id,out _),Is.True,id);
            }
        }
    }
}
#endif
