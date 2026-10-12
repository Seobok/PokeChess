using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Battle;

using PokeChess.Core.Match;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private sealed class CryUnit {public string Id,Species;public int Rank;}
        private PokemonCryPlayer cryPlayer;
        private readonly Dictionary<string,CryUnit> cryUnits=new Dictionary<string,CryUnit>();
        private object cryMatch;
        private BattleState cryBattle;
        private long cryTick=-1;

        private void RenderUnitCryAudio()
        {
            if(cryPlayer==null){cryPlayer=gameObject.AddComponent<PokemonCryPlayer>();cryPlayer.Initialize();}
            object key=IsOnlineMatch?(object)onlineMatchId:Match;
            bool baseline=!Equals(cryMatch,key);if(baseline){cryMatch=key;cryUnits.Clear();cryBattle=null;cryTick=-1;cryPlayer.ResetPlayback();}
            var owner=IsOnlineMatch?onlineService?.LocalMatchState:null;
            if(IsOnlineMatch&&owner==null)return;
            var current=IsOnlineMatch?owner.units.Select(u=>new CryUnit{Id=u.id,Species=u.definitionId,Rank=u.rank}).ToArray():Player.Units.Select(u=>new CryUnit{Id=u.InstanceId,Species=u.DefinitionId,Rank=(int)u.Rank}).ToArray();
            CryUnit chosen=null;
            foreach(var unit in current){
                if(cryUnits.TryGetValue(unit.Id,out var before)){
                    if(unit.Rank>before.Rank){chosen=unit;break;}

                }
            }
            bool muted=baseline||Simulation?.Status==SimulationStatus.Running||(IsOnlineMatch&&(!onlineService.StateSynchronized||onlineService.IsRecovering));
            if(!muted&&chosen!=null)cryPlayer.Play(chosen.Species,PokemonCryReason.RankUp);
            cryUnits.Clear();foreach(var unit in current)cryUnits[unit.Id]=unit;
            if(IsOnlineMatch)return;
            var battle=Match.Phase==MatchPhase.Combat?RoundLoop.GetBattleFor(localViewedPlayerId)?.Battle:null;
            if(cryBattle!=battle){cryBattle=battle;cryTick=-1;}
            if(battle==null||cryTick==battle.CurrentTick)return;
            cryTick=battle.CurrentTick;if(muted)return;
            foreach(var signal in battle.SkillEventsThisTick.Where(e=>e.Kind==SkillEventKind.CastStarted)){
                var unit=battle.Units.FirstOrDefault(u=>u.UnitInstanceId==signal.UnitId);
                if(unit!=null)cryPlayer.Play(unit.DefinitionId,PokemonCryReason.Skill);
            }
        }
        private void PlayOnlineUnitCry(CombatEventView signal)
        {
            if(signal.kind!="SkillCastStarted"||cryPlayer==null||onlineService.IsRecovering||!onlineService.StateSynchronized||onlineService.Combat.Current?.baseline==true)return;
            var unit=onlineService.Combat.Current?.units.FirstOrDefault(u=>u.id==signal.source);
            if(unit!=null)cryPlayer.Play(unit.definitionId,PokemonCryReason.Skill);
        }
    }
}


