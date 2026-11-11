using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Battle
{
    public sealed partial class SynergySystem
    {
        private sealed class Contamination { public UnitCombatState Source; public long End; }
        private readonly Dictionary<string,Contamination> contamination=new Dictionary<string,Contamination>(StringComparer.Ordinal);
        private readonly HashSet<string> firstContamination=new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> sampledDeaths=new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<int> contaminatedPvP=new HashSet<int>();
        private readonly Dictionary<string,long> venomReady=new Dictionary<string,long>(StringComparer.Ordinal);
        private readonly Dictionary<int,int> researchSamples=new Dictionary<int,int>();
        private readonly Dictionary<int,int> stormEnergy=new Dictionary<int,int>();
        private readonly Dictionary<string,float> circuitBonus=new Dictionary<string,float>();
        public long ContaminationEnd(UnitCombatState unit)
            => contamination.TryGetValue(unit.UnitInstanceId, out var entry) && entry.End > battle.CurrentTick ? entry.End : 0;
        public bool IsContaminated(UnitCombatState unit) => ContaminationEnd(unit) > 0;
        public float StormCircuitBonus(UnitCombatState unit)=>circuitBonus.TryGetValue(unit.UnitInstanceId,out var bonus)?bonus:0;
        public int StormEnergy(int team)=>stormEnergy.TryGetValue(team,out var n)?n:0;
        public int VenomResearch(int team) {
            int tier=TeamTier(team,SynergyTraits.Venom),samples=researchSamples.TryGetValue(team,out var n)?n:0;
            return tier>=6&&samples>=120?120:tier>=4&&samples>=40?40:tier>=2&&samples>=10?10:0;
        }
        public int VenomReward(int team) {
            if(!battle.IsPvP||TeamTier(team,SynergyTraits.Venom)==0)return 0;
            int earned=battle.VenomEarned.TryGetValue(team,out var n)?n:0;
            return earned>0?earned:contaminatedPvP.Contains(team)?1:0;
        }
        internal void OnEnemySkillEffectApplied(UnitCombatState source,UnitCombatState target) {
            if(target.CurrentHP>0&&target.IsOnBoard)Contaminate(source,target,true);
        }
        private void Contaminate(UnitCombatState source,UnitCombatState target,bool direct, BoardPosition? transferFrom = null) {
            if(Tier(source,SynergyTraits.Venom)==0||source.TeamId==target.TeamId)return;
            bool refreshed = IsContaminated(target);
            bool first=direct&&firstContamination.Add(target.UnitInstanceId);
            contamination[target.UnitInstanceId]=new Contamination{Source=source,End=battle.CurrentTick+T(5)};
            if(battle.IsPvP&&target.IsOriginalCombatUnit&&!target.IsSynergySummon)contaminatedPvP.Add(source.TeamId);
            VenomSignal(source, target, direct ? (refreshed ? "REFRESH" : "CONTAMINATE") : "TRANSFER",
                5, transferFrom ?? target.Position, transferFrom.HasValue ? target.Position : (BoardPosition?)null);
            if (first && Deal(source, target, rules.Origins.VenomInitialDamage) is DamageResult hit && hit.PostMitigationDamage > 0)
                VenomSignal(source, target, "FIRST_HIT", hit.PostMitigationDamage, target.Position);
        }
        // Entries survive death cleanup so attribution and transfer do not depend on the killer.
        private void ContaminatedDeath(UnitCombatState target) {
            if(!sampledDeaths.Add(target.UnitInstanceId)||!IsContaminated(target))return;
            var source=contamination[target.UnitInstanceId].Source;
            int tier=TeamTier(source.TeamId,SynergyTraits.Venom);
            if(battle.IsPvP&&target.IsOriginalCombatUnit&&!target.IsSynergySummon) {
                battle.VenomEarned.TryGetValue(source.TeamId,out int earned);
                battle.VenomEarned[source.TeamId]=checked(earned+tier/2);
                VenomSignal(source, target, "SAMPLE", tier / 2, target.Position);
            }
            if(tier<4)return;
            foreach(var next in Enemies(source).Where(e=>!IsContaminated(e)&&HexCoordinates.Distance(target.Position,e.Position)<=2)
                .OrderBy(e=>HexCoordinates.Distance(target.Position,e.Position)).ThenBy(e=>e.UnitInstanceId,StringComparer.Ordinal).Take(tier>=6?2:1).ToArray())
                Contaminate(source,next,false,target.Position);
        }
        private void VenomReaction(UnitCombatState caster) {
            if(!IsContaminated(caster)||venomReady.TryGetValue(caster.UnitInstanceId,out long ready)&&battle.CurrentTick<ready)return;
            var source=contamination[caster.UnitInstanceId].Source;int research=VenomResearch(source.TeamId);
            if(research<40)return;
            venomReady[caster.UnitInstanceId]=battle.CurrentTick+T(5);
            var targets=research==120?battle.Units.Where(e=>e.TeamId==caster.TeamId&&e.IsAreaTargetable&&HexCoordinates.Distance(e.Position,caster.Position)<=1).ToArray():new[]{caster};
            bool mutation = research == 120;
            if (mutation && targets.Length > 0) VenomSignal(source, caster, "MUTATION", 120, caster.Position);
            foreach (var target in targets)
            {
                var damage = Deal(source, target, mutation ? rules.Origins.VenomMutationDamage : rules.Origins.VenomReactionDamage);
                if (damage is DamageResult hit && hit.PostMitigationDamage > 0)
                    VenomSignal(source, target, mutation ? "MUTATION_HIT" : "REACTION", hit.PostMitigationDamage, target.Position);
            }
        }
        private void VenomSignal(UnitCombatState source, UnitCombatState target, string detail, float value,
            BoardPosition cell, BoardPosition? destination = null)
            => events.Add(new SynergyEvent(SynergyTraits.Venom, source.UnitInstanceId, target.UnitInstanceId,
                detail, value, battle.CurrentTick, cell, destination));

        public bool HasVenomManaErosion(UnitCombatState unit)
        {
            float venom = ContaminationManaReduction(unit);
            return unit.Stats.MaxEnergy > 0 && venom > 0 && venom >= battle.StatusEffects.ManaGainPenalty(unit);
        }

        internal void RecordVenomManaErosion(UnitCombatState unit, float before, float reducedGain)
        {
            if (reducedGain <= 0 || !HasVenomManaErosion(unit) || before + reducedGain >= unit.Stats.MaxEnergy)
                return;
            VenomSignal(contamination[unit.UnitInstanceId].Source, unit, "EROSION", 0, unit.Position);
        }

        internal float ContaminationManaReduction(UnitCombatState u) {
            return IsContaminated(u)&&VenomResearch(contamination[u.UnitInstanceId].Source.TeamId)>=10?rules.Origins.VenomManaReduction:0;
        }
        private void StormCompleted(UnitCombatState u) {
            int tier=Tier(u,SynergyTraits.Storm);
            if(tier==0||stormReady.TryGetValue(u.UnitInstanceId,out long ready)&&battle.CurrentTick<ready)return;
            var target=Enemies(u).OrderBy(e=>HexCoordinates.Distance(u.Position,e.Position)).ThenBy(e=>e.UnitInstanceId,StringComparer.Ordinal).FirstOrDefault();
            if(target==null)return;
            stormReady[u.UnitInstanceId]=battle.CurrentTick+T(2);
            float value=rules.StormDamage*(1+(circuitBonus.TryGetValue(u.UnitInstanceId,out var bonus)?bonus:0));
            var visited=new HashSet<string>();var previous=u;
            for(int hit=0;target!=null&&hit<(tier>=4?3:1);hit++) {
                visited.Add(target.UnitInstanceId);Record(SynergyTraits.Storm,previous,target,hit==0?"ARC":"CHAIN",value);
                Deal(u,target,value);previous=target;value*=rules.Origins.StormChainRatio;
                target=Enemies(u).Where(e=>!visited.Contains(e.UnitInstanceId)).OrderBy(e=>HexCoordinates.Distance(previous.Position,e.Position)).ThenBy(e=>e.UnitInstanceId,StringComparer.Ordinal).FirstOrDefault();
            }
            if(tier<4)return;
            int energy=StormEnergy(u.TeamId)+1;
            if(tier>=6&&energy>=4) {
                energy=0;
                foreach(var enemy in Enemies(u).ToArray()) {
                    Record(SynergyTraits.Storm,u,enemy,"DISCHARGE",rules.Origins.StormOverchargeDamage);
                    Deal(u,enemy,rules.Origins.StormOverchargeDamage);
                    if(enemy.IsTargetable)battle.StatusEffects.Apply(new StatusEffectDefinition("synergy-storm-overcharge",1,
                        modifiers:new[]{new StatModifierDefinition(CombatStat.AttackSpeed,StatModifierType.Percentage,-.30f)}),u.UnitInstanceId,enemy.UnitInstanceId);
                }
            }
            stormEnergy[u.TeamId]=energy;
            // One stable team anchor; every event updates it, including multiple casts on one tick.
            var anchor=battle.Units.Where(a=>a.TeamId==u.TeamId&&a.TraitIds.Contains(SynergyTraits.Storm)).OrderBy(a=>a.UnitInstanceId,StringComparer.Ordinal).First();
            Record(SynergyTraits.Storm,anchor,u,"CHARGE",energy);
        }
        internal SkillDefinition GuardianDefinition(UnitCombatState u)=>new SkillDefinition(u.SkillId,0.3f,0.2f,
            SkillTargetRule.Self,TargetLostPolicy.ContinueWithoutTarget,SkillEffectKind.SelfShield,0,energyLock:1);
        public static string GuardianForm(int tier)=>tier>=10?"세계수 나시":tier>=6?"강화 나시":tier>=4?"나시":"아라리";

        public static PokemonDefinition NatureGuardianDefinition(int tier, SynergyOriginTuning tuning = null)
        {
            tuning ??= new SynergyOriginTuning();
            var stats = new PokemonStats(
                tier == 10 ? tuning.WorldTreeHP : tier == 6 ? 2050 : tier == 4 ? 1200 : tuning.GuardianHP,
                tier == 10 ? tuning.WorldTreeAttack : tier == 6 ? 65 : tier == 4 ? 50 : tuning.GuardianAttack,
                100, tier == 10 ? .7f : tier == 6 ? .6f : .55f,
                tier == 10 ? 100 : tier == 6 ? 65 : tier == 4 ? 45 : 30,
                tier == 10 ? 100 : tier == 6 ? 60 : tier == 4 ? 45 : 30,
                tier == 10 ? 2 : 1, 2, 100, tier == 10 ? 60 : tier == 6 ? 40 : 30, .2f);
            return new PokemonDefinition(tier == 2 ? "exeggcute" : "exeggutor", GuardianForm(tier),
                0, stats, "tank", "synergy-nature-" + tier, new[] { "grass", "psychic" });
        }

        internal void GuardianSkill(UnitCombatState guardian) {
            int tier=TeamTier(guardian.TeamId,SynergyTraits.Nature);
            float duration=tier>=6?1.5f:1.25f;
            foreach(var enemy in Enemies(guardian).Where(e=>HexCoordinates.Distance(guardian.Position,e.Position)<=(tier==10?2:1)).ToArray())
                Control(guardian,enemy,CrowdControlKind.Taunt,duration);
            if(tier>=4&&tier<10)foreach(var enemy in Enemies(guardian).Where(e=>HexCoordinates.Distance(guardian.Position,e.Position)<=2)
                .OrderBy(e=>HexCoordinates.Distance(guardian.Position,e.Position)).ThenBy(e=>e.UnitInstanceId,StringComparer.Ordinal).Take(tier>=6?4:3).ToArray())
                Control(guardian,enemy,CrowdControlKind.Root,duration);
            if(tier<10)Shield(SynergyTraits.Nature,guardian,guardian,tier>=6?300:tier>=4?200:120,3);
            else
            {
                foreach (var enemy in Enemies(guardian).ToArray())
                    NatureHit(guardian, enemy, rules.Origins.GuardianDamage * guardian.EffectiveStats.SpellPower / 100, "JUDGMENT_HIT");
                foreach (var ally in Allies(guardian).ToArray())
                {
                    float healed = battle.Healing.Apply(ally, (ally.Stats.MaxHP - ally.CurrentHP) * rules.Origins.GuardianHeal);
                    if (healed > 0) RecordNature(guardian, ally, "HEAL", healed);
                }
            }
            RecordNature(guardian, guardian, "GUARDIAN_CAST", tier);
        }

        private void RecordNature(UnitCombatState source, UnitCombatState target, string detail, float value)
            => events.Add(new SynergyEvent(SynergyTraits.Nature, source.UnitInstanceId, target.UnitInstanceId,
                detail, value, battle.CurrentTick, target.Position));

        private void NatureHit(UnitCombatState guardian, UnitCombatState enemy, float amount, string detail)
        {
            var hit = Deal(guardian, enemy, amount);
            if (hit.HasValue && hit.Value.PostMitigationDamage > 0)
                RecordNature(guardian, enemy, detail, hit.Value.PostMitigationDamage);
        }

        internal void WorldTreePulse(UnitCombatState guardian)
        {
            RecordNature(guardian, guardian, "PULSE", 10);
            foreach (var enemy in Enemies(guardian).ToArray())
            {
                NatureHit(guardian, enemy, rules.Origins.WorldTreeDamage, "PULSE_HIT");
                if (enemy.IsTargetable) Control(guardian, enemy, CrowdControlKind.Root, 1.5f);
            }
        }
    }
}
