using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private CombatSoundLibrary combatSounds;
        private CombatSoundPlayer combatAudio;
        private BattleState soundBattle;
        private long soundTick=-1;
        private string soundOnlineBattle;
        private readonly Dictionary<string,long> soundAttacks=new Dictionary<string,long>();
        private void EnsureCombatAudio()
        {
            if(combatSounds==null)combatSounds=Resources.Load<CombatSoundLibrary>("CombatSounds");
            if(combatAudio==null)combatAudio=gameObject.AddComponent<CombatSoundPlayer>();
        }
        private void CombatSound(AudioClip clip,CombatSoundKind kind,float pitch=1)
        {combatAudio.Play(clip,kind,Time.unscaledTimeAsDouble,pitch);}
        private AudioClip ImpactSound(string species,bool ranged)
        {
            if(ranged)return combatSounds.arrow;
            if(species=="mankey"||species=="charmander")return combatSounds.kick;
            if(species=="geodude"||species=="magnemite")return combatSounds.heavy;
            return combatSounds.punch;
        }
        private void SkillSound(string species,string skillId)
        {
            if(species=="charmander"){CombatSound(combatSounds.fire,CombatSoundKind.Skill);return;}
            if(contextSkills.TryGet(skillId,out var skill)){
                if(skill.Effects.Any(e=>e.Type==SkillEffectType.Heal)){CombatSound(combatSounds.heal,CombatSoundKind.Heal);return;}
                if(skill.Effects.Any(e=>e.Type==SkillEffectType.Shield)){CombatSound(combatSounds.heavy,CombatSoundKind.Skill,.85f);return;}
            }
            CombatSound(combatSounds.whip,CombatSoundKind.Skill,species=="abra"||species=="gastly"?1.1f:1);
        }
        private void RenderCombatAudio()
        {
            EnsureCombatAudio();if(IsOnlineMatch||combatSounds==null)return;
            var pair=Match.Phase==MatchPhase.Combat?RoundLoop.GetBattleFor(localViewedPlayerId):null;var battle=pair?.Battle;
            if(soundBattle!=battle){soundBattle=battle;soundTick=-1;soundAttacks.Clear();combatAudio.ResetPlayback();}
            if(battle==null||soundTick==battle.CurrentTick)return;soundTick=battle.CurrentTick;
            if(Simulation?.Status==SimulationStatus.Running)return;
            // Skill effects have priority over the background of basic attacks.
            foreach(var e in battle.SkillEventsThisTick.Where(e=>e.Kind==SkillEventKind.EffectApplied)){
                var caster=battle.Units.FirstOrDefault(u=>u.UnitInstanceId==e.UnitId);if(caster!=null)SkillSound(caster.DefinitionId,e.SkillId);
            }
            foreach(var damage in battle.DamageResultsThisTick){
                if(damage.Request.Origin!=DamageOrigin.Direct||damage.AppliedDamage+damage.ShieldAbsorbed<=0)continue;
                var attacker=battle.Units.FirstOrDefault(u=>u.UnitInstanceId==damage.Request.SourceId);if(attacker==null)continue;
                CombatSound(ImpactSound(attacker.DefinitionId,attacker.Stats.AttackRange>1),CombatSoundKind.Impact,damage.IsCritical?.9f:1);
            }
            foreach(var unit in battle.Units.Where(u=>u.IsAlive&&u.ActionState==CombatActionState.Attacking)){
                var action=pair.GetActionRuntime(unit.UnitInstanceId);
                if(soundAttacks.TryGetValue(unit.UnitInstanceId,out var start)&&start==action.StartTick)continue;
                soundAttacks[unit.UnitInstanceId]=action.StartTick;
                CombatSound(unit.Stats.AttackRange>1?combatSounds.whip:action.StartTick%2==0?combatSounds.swingOne:combatSounds.swingTwo,CombatSoundKind.Swing);
            }
        }
        private void PlayOnlineCombatSound(CombatEventView e)
        {
            EnsureCombatAudio();var frame=onlineService.Combat.Current;
            if(combatSounds==null||frame==null||frame.baseline||onlineService.IsRecovering||!onlineService.StateSynchronized)return;
            if(soundOnlineBattle!=frame.battleId){soundOnlineBattle=frame.battleId;combatAudio.ResetPlayback();}
            var unit=frame.units.FirstOrDefault(u=>u.id==e.source);if(unit==null)return;
            if(e.kind=="SkillEffectApplied")SkillSound(unit.definitionId,e.detail);
            else if(e.kind=="Damage"&&e.value>0&&e.origin==DamageOrigin.Direct)CombatSound(ImpactSound(unit.definitionId,catalog.Get(unit.definitionId).BaseStats.AttackRange>1),CombatSoundKind.Impact,e.detail=="Critical"?.9f:1);
            else if(e.kind=="ProjectileSpawned")CombatSound(combatSounds.whip,CombatSoundKind.Swing);
        }
    }
}

