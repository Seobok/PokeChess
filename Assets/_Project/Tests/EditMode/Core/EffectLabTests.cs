using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PokeChess.Client.UI;
using PokeChess.Content;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class EffectLabTests
    {
        private EffectLabSession lab;
        [SetUp] public void Setup()=>lab=new EffectLabSession(GameContent.Roster);
        private void Through(int ticks){for(int i=0;i<ticks;i++)lab.Step();}
        [Test] public void HealthLockKeepsLethalHitsAliveAndCanBeDisabled()
        {
            lab.SetPreserveHealth(true);var unit=lab.Unit("target");float hp=unit.CurrentHP;
            lab.Hit("target",1000000);Assert.That(unit.CurrentHP,Is.EqualTo(hp));Assert.That(unit.IsOnBoard,Is.True);
            Assert.That(lab.Battle.DamageResultsThisTick.Last().Killed,Is.False);
            Assert.That(lab.Battle.DamageResultsThisTick.Last().AppliedDamage,Is.GreaterThan(0));
            lab.Reset();Assert.That(lab.Battle.PreserveHealthForPreview,Is.True);
            lab.SetPreserveHealth(false);lab.Hit("target",1000000);Assert.That(lab.Unit("target").IsAlive,Is.False);
        }
        [Test] public void HealthLockAllowsThreeStarIceBeamStatusesAndUnlimitedPreviewTime()
        {
            lab.ReplaceCaster("lapras");lab.SetRank(lab.CasterId,UnitRank.Three);lab.SetPreserveHealth(true);
            var enemies=lab.Battle.Units.Where(u=>u.TeamId==2).ToArray();var hp=enemies.Select(u=>u.CurrentHP).ToArray();
            lab.Cast();Through(40);
            Assert.That(enemies.Select(u=>u.CurrentHP),Is.EqualTo(hp));Assert.That(enemies.All(u=>u.IsAlive&&u.IsOnBoard),Is.True);
            Assert.That(lab.Battle.StatusEffects.Active.Count(s=>s.Definition.Behavior==StatusBehavior.Freeze),Is.EqualTo(3));
            Through(1400);Assert.That(lab.Battle.CurrentTick,Is.GreaterThan(1350));Assert.That(lab.Battle.Result,Is.EqualTo(BattleResult.InProgress));
        }
        [Test] public void HealthLockPreventsExecutionWithoutSuppressingHitEvent()
        {
            lab.ReplaceCaster("pinsir");lab.SetPreserveHealth(true);var target=lab.Unit("target");target.SetVitals(target.Stats.MaxHP*.2f,0);float hp=target.CurrentHP;
            lab.Cast();Through(40);Assert.That(target.CurrentHP,Is.EqualTo(hp));Assert.That(target.IsAlive&&target.IsOnBoard,Is.True);
            Assert.That(lab.Log.Any(s=>s.Contains("Damage")),Is.True);
        }
        [Test] public void PassiveSceneDoesNotCastUntilRequested()
        {
            Through(20);Assert.That(lab.Unit(lab.CasterId).CurrentEnergy,Is.EqualTo(lab.Unit(lab.CasterId).Stats.StartingEnergy));
            Assert.That(lab.Battle.StatusEffects.Active,Is.Empty);
            lab.Cast();Through(20);
            Assert.That(lab.Battle.StatusEffects.Active.Any(s=>s.Definition.Id=="status-caterpie-r1-s0"),Is.True);
            Assert.That(lab.Log.Any(s=>s.Contains("skill-caterpie-r1")),Is.True);
            Through(130);Assert.That(lab.Battle.StatusEffects.Active,Is.Empty);
        }
        [Test] public void PoisonUsesRealStackLimitDamageAndRemoval()
        {
            string poison=lab.Roster.Statuses.First(s=>s.Behavior==StatusBehavior.Poison).Id;
            float hp=lab.Unit("target").CurrentHP;
            for(int i=0;i<4;i++)lab.ApplyStatus(poison,"target");
            var status=lab.Battle.StatusEffects.Active.Single(s=>s.TargetId=="target");
            Assert.That(status.StackCount,Is.EqualTo(3));Through(35);
            Assert.That(lab.Unit("target").CurrentHP,Is.LessThan(hp));
            lab.RemoveStatus(status.InstanceId);Assert.That(lab.Battle.StatusEffects.Active,Is.Empty);
        }
        [Test] public void SleepBlocksManualCastUntilItExpires()
        {
            var sleep=lab.Roster.Statuses.First(s=>s.Behavior==StatusBehavior.Sleep);
            lab.ApplyStatus(sleep.Id,lab.CasterId);lab.Cast();Through(5);
            Assert.That(lab.Requested,Is.True);Assert.That(lab.Unit(lab.CasterId).ActionState,Is.Not.EqualTo(CombatActionState.Casting));
            Through((int)Math.Ceiling(sleep.Duration*30)+20);
            Assert.That(lab.Requested,Is.False);Assert.That(lab.Log.Any(s=>s.Contains("CastStarted")),Is.True);
        }
        [Test] public void RankAndPlacementChangesRebuildWithoutEditingRoster()
        {
            var pokemon=lab.Roster.Pokemon.First(p=>p.Id=="caterpie");float original=pokemon.Stats.MaxHP;
            lab.SetRank(lab.CasterId,UnitRank.Three);Assert.That(lab.Unit(lab.CasterId).Rank,Is.EqualTo(UnitRank.Three));
            Assert.That(lab.Unit(lab.CasterId).Stats.MaxHP,Is.GreaterThan(original));
            lab.Move(lab.CasterId,new BoardPosition(0,0));Assert.That(lab.Unit(lab.CasterId).Position,Is.EqualTo(new BoardPosition(0,0)));
            Assert.That(lab.Battle.CurrentTick,Is.Zero);Assert.That(pokemon.Stats.MaxHP,Is.EqualTo(original));
            lab.Remove("enemy3");Assert.That(lab.Unit("enemy3"),Is.Null);
        }
        [Test] public void FuturePokemonAndStatusDefinitionsWorkWithoutNewLabCode()
        {
            var roster=UnityEngine.Object.Instantiate(GameContent.Roster);
            var pokemon=UnityEngine.Object.Instantiate(roster.Pokemon.First());
            var status=UnityEngine.Object.Instantiate(roster.Statuses.First(s=>s.Behavior==StatusBehavior.Poison));
            try
            {
                pokemon.Id="future-pokemon";status.Id=status.StackGroup="future-status";
                roster.Pokemon=roster.Pokemon.Concat(new[]{pokemon}).ToArray();roster.Statuses=roster.Statuses.Concat(new[]{status}).ToArray();
                var extended=new EffectLabSession(roster);string added=extended.Add(pokemon.Id,2,new BoardPosition(6,6));
                Assert.That(extended.Unit(added).DefinitionId,Is.EqualTo(pokemon.Id));
                extended.ApplyStatus(status.Id,added);
                Assert.That(extended.Battle.StatusEffects.Active.Single().Definition.Id,Is.EqualTo(status.Id));
            }
            finally{UnityEngine.Object.DestroyImmediate(roster);UnityEngine.Object.DestroyImmediate(pokemon);UnityEngine.Object.DestroyImmediate(status);}
        }
        [Test] public void SelectedSkillCanBeTestedOnAnotherPokemon()
        {
            lab.SkillId="skill-pidgeotto";lab.Cast();Through(20);
            Assert.That(lab.Log.Any(s=>s.Contains("skill-pidgeotto-r1")),Is.True);
            Assert.That(lab.LastArea.Count,Is.EqualTo(3));
        }
    }
}
