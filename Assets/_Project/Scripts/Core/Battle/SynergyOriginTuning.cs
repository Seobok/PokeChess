using System;
namespace PokeChess.Core.Battle {
// Prototype assumptions only: v0.9 does not provide these origin magnitudes.
public sealed class SynergyOriginTuning {
public float FlameDamage { get; }
public float FlameExplosion { get; }
public float StormDamage { get; }
public float TorrentShield { get; }
public float TorrentDamage { get; }
public float UltimateWaveDamage { get; }
public float EarthReduction { get; }
public float EarthShield { get; }
public float EarthPulseSeconds { get; }
public float EarthSlow { get; }
public float EarthSlowSeconds { get; }
public float GuardianHP { get; }
public float WorldTreeHP { get; }
public float GuardianAttack { get; }
public float WorldTreeAttack { get; }
public float GuardianPeriod { get; }
public float GuardianDamage { get; }
public float WorldTreeDamage { get; }
public float GuardianHeal { get; }
public float WorldTreeRootSeconds { get; }
public float VenomDamageMultiplier { get; }
public float VenomHealingReduction { get; }
public float VenomReactionDamage { get; }
public SynergyOriginTuning(float flameDamage=25f,float flameExplosion=60f,float stormDamage=45f,float torrentShield=60f,float torrentDamage=30f,float ultimateWaveDamage=120f,float earthReduction=0.1f,float earthShield=40f,float earthPulseSeconds=4f,float earthSlow=0.2f,float earthSlowSeconds=2f,float guardianHP=650f,float worldTreeHP=3000f,float guardianAttack=40f,float worldTreeAttack=100f,float guardianPeriod=5f,float guardianDamage=50f,float worldTreeDamage=150f,float guardianHeal=0.05f,float worldTreeRootSeconds=1.5f,float venomDamageMultiplier=1.25f,float venomHealingReduction=0.25f,float venomReactionDamage=30f) {
DamageCalculator.Validate(flameDamage,true);FlameDamage=flameDamage;
DamageCalculator.Validate(flameExplosion,true);FlameExplosion=flameExplosion;
DamageCalculator.Validate(stormDamage,true);StormDamage=stormDamage;
DamageCalculator.Validate(torrentShield,true);TorrentShield=torrentShield;
DamageCalculator.Validate(torrentDamage,true);TorrentDamage=torrentDamage;
DamageCalculator.Validate(ultimateWaveDamage,true);UltimateWaveDamage=ultimateWaveDamage;
DamageCalculator.Validate(earthReduction,true);EarthReduction=earthReduction;
DamageCalculator.Validate(earthShield,true);EarthShield=earthShield;
DamageCalculator.Validate(earthPulseSeconds,true);EarthPulseSeconds=earthPulseSeconds;
DamageCalculator.Validate(earthSlow,true);EarthSlow=earthSlow;
DamageCalculator.Validate(earthSlowSeconds,true);EarthSlowSeconds=earthSlowSeconds;
DamageCalculator.Validate(guardianHP,true);GuardianHP=guardianHP;
DamageCalculator.Validate(worldTreeHP,true);WorldTreeHP=worldTreeHP;
DamageCalculator.Validate(guardianAttack,true);GuardianAttack=guardianAttack;
DamageCalculator.Validate(worldTreeAttack,true);WorldTreeAttack=worldTreeAttack;
DamageCalculator.Validate(guardianPeriod,true);GuardianPeriod=guardianPeriod;
DamageCalculator.Validate(guardianDamage,true);GuardianDamage=guardianDamage;
DamageCalculator.Validate(worldTreeDamage,true);WorldTreeDamage=worldTreeDamage;
DamageCalculator.Validate(guardianHeal,true);GuardianHeal=guardianHeal;
DamageCalculator.Validate(worldTreeRootSeconds,true);WorldTreeRootSeconds=worldTreeRootSeconds;
DamageCalculator.Validate(venomDamageMultiplier,true);VenomDamageMultiplier=venomDamageMultiplier;
DamageCalculator.Validate(venomHealingReduction,true);VenomHealingReduction=venomHealingReduction;
DamageCalculator.Validate(venomReactionDamage,true);VenomReactionDamage=venomReactionDamage;
if(EarthReduction>1||EarthSlow>1||GuardianHeal>1||VenomHealingReduction>1||EarthPulseSeconds<=0||EarthSlowSeconds<=0||GuardianPeriod<=0||WorldTreeRootSeconds<=0||GuardianHP<=0||WorldTreeHP<=0)throw new ArgumentOutOfRangeException();
}
}
}