using System;
namespace PokeChess.Core.Battle {
// v1.2 origin tuning. Values remain prototype balance assumptions.
public sealed class SynergyOriginTuning {
public float StormChainRatio => .7f;
public float StormOverchargeDamage => 140f;
public float VenomInitialDamage => 60f;
public float VenomMutationDamage => 120f;
public float VenomManaReduction => .20f;
public float FlameDamage { get; }
public float FlameExplosion { get; }
public float StormDamage { get; }
public float TorrentShield { get; }
public float TorrentDamage { get; }
public float UltimateWaveDamage { get; }
public float EarthReduction { get; }
public float EarthShield { get; }
public float EarthPulseSeconds { get; }
public float EarthFortressReduction { get; }
public float EarthPulseHealthFraction { get; }
public float EarthPulseShieldCap { get; }
public float EarthFirstPulseSeconds { get; }
public float GuardianHP { get; }
public float WorldTreeHP { get; }
public float GuardianAttack { get; }
public float WorldTreeAttack { get; }
public float GuardianPeriod { get; }
public float GuardianDamage { get; }
public float WorldTreeDamage { get; }
public float GuardianHeal { get; }
public float WorldTreeRootSeconds { get; }
public float VenomReactionDamage { get; }
public SynergyOriginTuning(float flameDamage=25f,float flameExplosion=60f,float stormDamage=80f,float torrentShield=60f,float torrentDamage=30f,float ultimateWaveDamage=120f,float earthReduction=.12f,float earthShield=160f,float earthPulseSeconds=8f,float earthFortressReduction=.10f,float earthPulseHealthFraction=.12f,float guardianHP=700f,float worldTreeHP=7500f,float guardianAttack=35f,float worldTreeAttack=135f,float guardianPeriod=8f,float guardianDamage=250f,float worldTreeDamage=150f,float guardianHeal=0.15f,float worldTreeRootSeconds=2f,float venomReactionDamage=50f,float earthPulseShieldCap=250f,float earthFirstPulseSeconds=4f) {
DamageCalculator.Validate(flameDamage,true);FlameDamage=flameDamage;
DamageCalculator.Validate(flameExplosion,true);FlameExplosion=flameExplosion;
DamageCalculator.Validate(stormDamage,true);StormDamage=stormDamage;
DamageCalculator.Validate(torrentShield,true);TorrentShield=torrentShield;
DamageCalculator.Validate(torrentDamage,true);TorrentDamage=torrentDamage;
DamageCalculator.Validate(ultimateWaveDamage,true);UltimateWaveDamage=ultimateWaveDamage;
DamageCalculator.Validate(earthReduction,true);EarthReduction=earthReduction;
DamageCalculator.Validate(earthShield,true);EarthShield=earthShield;
DamageCalculator.Validate(earthPulseSeconds,true);EarthPulseSeconds=earthPulseSeconds;
DamageCalculator.Validate(earthFortressReduction,true);EarthFortressReduction=earthFortressReduction;
DamageCalculator.Validate(earthPulseHealthFraction,true);EarthPulseHealthFraction=earthPulseHealthFraction;
DamageCalculator.Validate(earthPulseShieldCap,true);EarthPulseShieldCap=earthPulseShieldCap;
DamageCalculator.Validate(earthFirstPulseSeconds,true);EarthFirstPulseSeconds=earthFirstPulseSeconds;
DamageCalculator.Validate(guardianHP,true);GuardianHP=guardianHP;
DamageCalculator.Validate(worldTreeHP,true);WorldTreeHP=worldTreeHP;
DamageCalculator.Validate(guardianAttack,true);GuardianAttack=guardianAttack;
DamageCalculator.Validate(worldTreeAttack,true);WorldTreeAttack=worldTreeAttack;
DamageCalculator.Validate(guardianPeriod,true);GuardianPeriod=guardianPeriod;
DamageCalculator.Validate(guardianDamage,true);GuardianDamage=guardianDamage;
DamageCalculator.Validate(worldTreeDamage,true);WorldTreeDamage=worldTreeDamage;
DamageCalculator.Validate(guardianHeal,true);GuardianHeal=guardianHeal;
DamageCalculator.Validate(worldTreeRootSeconds,true);WorldTreeRootSeconds=worldTreeRootSeconds;
DamageCalculator.Validate(venomReactionDamage,true);VenomReactionDamage=venomReactionDamage;
if(EarthReduction>1||EarthFortressReduction>1||EarthPulseHealthFraction>1||GuardianHeal>1||EarthPulseSeconds<=0||EarthFirstPulseSeconds<=0||GuardianPeriod<=0||WorldTreeRootSeconds<=0||GuardianHP<=0||WorldTreeHP<=0)throw new ArgumentOutOfRangeException();
}
}
}