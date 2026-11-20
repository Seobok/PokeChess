using System;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Content
{
    // Only exceptional rank behavior belongs here. Ordinary magnitudes use RankValues/RankStatuses.
    [Serializable]
    public sealed class SkillRankOverride
    {
        public UnitRank Rank=UnitRank.Three;
        public bool OverrideTiming;
        public float CastTime=.4f,PostCastTime=.4f,EnergyLock=1;
        public bool Interruptible=true,HasEnergyLock=true;
        public bool OverrideTargeting;
        public SkillTargetRule TargetRule;
        public TargetLostPolicy TargetLostPolicy;
        public int Range=3;
        public bool OverrideActivation,HasActivation;
        public SkillActivationData Activation=new SkillActivationData();
        public bool OverrideEffects;
        public SkillEffectData[] Effects=Array.Empty<SkillEffectData>();
        public SkillStepData[] Steps=Array.Empty<SkillStepData>();
        public bool OverrideVariants;
        public string[] VariantIds=Array.Empty<string>();
        public SkillVariantMode VariantMode;
    }
}
