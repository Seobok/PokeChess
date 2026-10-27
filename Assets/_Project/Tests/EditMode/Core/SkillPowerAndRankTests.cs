using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Board;
namespace PokeChess.Core.Tests
{
    public sealed class SkillPowerAndRankTests
    {
        [TestCase(0,0)][TestCase(100,200)][TestCase(150,300)]
        public void TotalPowerUsesHundredPointBaseline(float power,float expected)
        {
            foreach(var type in new[]{DamageType.Physical,DamageType.Magic}) {
                var effect=new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,200,
                    spellPowerScalable:true,damageType:type,powerScaling:SkillPowerScaling.TotalSpellPower);
                Assert.That(effect.ResolveValue(UnitRank.One,power,999),Is.EqualTo(expected));
            }
        }
        [Test] public void LegacyBonusAndAttackRatioRemainCompatible()
        {
            Assert.That(new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,200,true).ResolveValue(50),Is.EqualTo(300));
            var attack=new SkillEffectDefinition(SkillEffectType.Damage,EffectTargetSelector.CastTarget,1.4f,valueSource:SkillValueSource.Attack);
            Assert.That(attack.ResolveValue(UnitRank.One,150,100),Is.EqualTo(140).Within(.001));
            Assert.Throws<OverflowException>(()=>SkillNumericMath.Magnitude(float.MaxValue,SkillValueSource.Flat,0,200,true,SkillPowerScaling.TotalSpellPower));
        }
        private static PokemonDefinition Definition(float[] hp=null,float[] attack=null)=>new PokemonDefinition("unit","Unit",1,
            new PokemonStats(530,42,100,.65f,20,20,2,2,70,10,.2f),"role","skill",rankHP:hp,rankAttack:attack);
        [Test] public void ExplicitRankStatsAreCopiedAndDoNotReceiveLegacyMultiplier()
        {
            var hp=new[]{530f,901,1537};var attack=new[]{42f,55,71};var d=Definition(hp,attack);hp[2]=1;attack[2]=1;
            var stats=RankRules.Default.Apply(d,UnitRank.Three);
            Assert.That(stats.MaxHP,Is.EqualTo(1537));Assert.That(stats.Attack,Is.EqualTo(71));
            Assert.That(stats.SpellPower,Is.EqualTo(100));Assert.That(stats.MaxEnergy,Is.EqualTo(70));Assert.That(stats.AttackSpeed,Is.EqualTo(.65f));
            Assert.That(RankRules.Default.Apply(Definition(),UnitRank.Three).MaxHP,Is.EqualTo(530*2.7f));
        }
        [Test] public void PartialOrMismatchedRankStatsAreRejected()
        {
            Assert.Throws<ArgumentException>(()=>Definition(new[]{530f,901,1537}));
            Assert.Throws<ArgumentException>(()=>Definition(new[]{531f,901,1537},new[]{42f,55,71}));
            Assert.Throws<ArgumentException>(()=>Definition(new[]{530f,901},new[]{42f,55,71}));
        }
        private static BattleState Battle()
        {
            var d=new PokemonDefinition("unit","Unit",1,new PokemonStats(10000,999,150,1,0,0,3,2,100,0,.2f),"role","skill");
            var c=new PokemonCatalog(new[]{d});
            return BattleStateFactory.Create("power",1,17,20,c,new[]{
                new BattleUnitSetup(c.CreateUnit("a","unit","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(1,3)),
                new BattleUnitSetup(c.CreateUnit("b","unit","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(2,3))});
        }
        private sealed class Idle:SkillCombatBehaviorPolicy
        {
            public Idle():base(new SkillCatalog(Array.Empty<SkillDefinition>())){}
            public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
            public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        }
        [Test] public void ConditionalDamageKeepsReleasePowerWhileCheckingImpactHP()
        {
            var b=Battle();var source=b.Units.First();var target=b.Units.Last();
            var effect=new SkillEffectDefinition(SkillEffectType.ProjectileDamage,EffectTargetSelector.CastTarget,100,true,
                numeric:new SkillNumericDefinition(SkillValueCondition.TargetLowHP,conditionalValues:new[]{200f,200,200}),powerScaling:SkillPowerScaling.TotalSpellPower);
            var snapshot=SkillNumericMath.Capture(b,source);var value=SkillNumericMath.SourceValue(source,effect);
            b.StatusEffects.Apply(new StatusEffectDefinition("power",4,StatusTargetTeam.Ally,modifiers:new[]{new StatModifierDefinition(CombatStat.SpellPower,StatModifierType.Flat,100)}),"a","a");
            target.SetVitals(2000,0);
            var result=new DamageProcessor().Apply(b,SkillNumericMath.Request(b,source,target,effect,value,snapshot:snapshot));
            Assert.That(result.AppliedDamage,Is.EqualTo(300));
        }
        [Test] public void BindingDamageUsesFrozenSpellPowerAndIncludesFinalPulse()
        {
            var b=Battle();var target=b.Units.Last();
            b.StatusEffects.Apply(new StatusEffectDefinition("binding",2,crowdControl:CrowdControlKind.Root,effectValue:40,fixedSpellDamage:true),"a","b");
            b.StatusEffects.Apply(new StatusEffectDefinition("power",4,StatusTargetTeam.Ally,modifiers:new[]{new StatModifierDefinition(CombatStat.SpellPower,StatModifierType.Flat,100)}),"a","a");
            var sim=new BattleSimulation(b,new Idle());while(b.CurrentTick<=40)sim.Step();
            Assert.That(target.CurrentHP,Is.EqualTo(9880));
        }
    }
}
