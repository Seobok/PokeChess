using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private string onlineBattleId;
        private readonly Dictionary<string,UnityEngine.UI.Text> onlineEffects=new Dictionary<string,UnityEngine.UI.Text>();
        private readonly Dictionary<string,float> onlineEffectUntil=new Dictionary<string,float>();
        private readonly LevelRules onlineLevelRules=new LevelRules();
        private void ClearOnlineFighters()
        {
            foreach(var label in combatTokens.Values)if(label!=null)Destroy(label.transform.parent.gameObject);combatTokens.Clear();ClearFighterSprites();
            foreach(var shot in combatShots.Values)Destroy(shot.gameObject);combatShots.Clear();onlineEffects.Clear();onlineEffectUntil.Clear();onlineBattleId=null;lastVisualBattle=null;
        }
        private void RenderOnlineHud(OwnerMatchState owner,PublicMatchState pub)
        {
            hudRound.text="ROUND "+pub.round;
            HudTimerKind=pub.phase.ToString();double remaining=onlineService.PhaseRemainingSeconds;
            double duration=pub.phase==MatchPhase.Preparation?30:3;
            var frame=onlineService.Combat.Current;
            bool overtime=frame!=null&&frame.overtimeTick>0&&frame.tick>=frame.overtimeTick;
            if(pub.phase==MatchPhase.Combat&&frame!=null)HudTimerKind=overtime?"Overtime":"Combat";
            HudTimerProgress=pub.hasPhaseDeadline?Mathf.Clamp01((float)(remaining/duration)):pub.phase==MatchPhase.Combat&&frame!=null?Mathf.Clamp01(overtime?(frame.timeLimitTick-frame.tick)/(float)Math.Max(1,frame.timeLimitTick-frame.overtimeTick):(frame.overtimeTick-frame.tick)/(float)Math.Max(1,frame.overtimeTick)):0;
            hudTimer.rectTransform.sizeDelta=new Vector2(280*HudTimerProgress,8);hudTimer.color=pub.phase==MatchPhase.Preparation?new Color(.2f,.6f,1):pub.phase==MatchPhase.Result?new Color(.7f,.4f,.95f):new Color(1,.58f,.18f);
            hudTimerBorder.gameObject.SetActive(pub.phase!=MatchPhase.Finished);
            hudNotice.text=onlineService.CombatError??(pub.phase==MatchPhase.Finished?"MATCH COMPLETE":!onlineService.StateSynchronized?"Synchronizing…":pub.hasPhaseDeadline?Math.Ceiling(remaining)+"s · "+pub.phase:pub.phase==MatchPhase.Combat?"Watching "+PlayerLabel(onlineService.WatchingPlayerId,owner.playerId)+(frame?.complete==true?" · Battle complete":""):pub.phase.ToString());
            hudPlayers.text="ALIVE "+pub.players.Count(p=>!p.eliminated)+" / "+pub.players.Length;
            float top=135;
            foreach(var player in pub.players.OrderBy(p=>p.id==owner.playerId?0:p.eliminated?2:1)){
                bool self=player.id==owner.playerId;float height=self?48:32;
                if(!hudProfiles.TryGetValue(player.id,out var row)){
                    var background=HudImage("Profile_"+player.id,sidebar,new Vector2(195,height),Vector2.zero,Color.white);background.raycastTarget=true;
                    row=new ProfileRow{Rect=background.rectTransform,Background=background,PreviousHP=player.hp};
                    row.Name=Label("Name",row.Rect,"",self?13:11,new Vector2(182,18),new Vector2(0,self?12:8));
                    row.State=Label("Status",row.Rect,"",10,new Vector2(182,16),new Vector2(0,-4));
                    var track=HudImage("HPTrack",row.Rect,new Vector2(160,3),new Vector2(0,-height/2+4),new Color(.05f,.08f,.11f));
                    row.HP=HudImage("HPFill",track.transform,new Vector2(160,3),Vector2.zero,new Color(.25f,.85f,.55f));row.HP.rectTransform.anchorMin=row.HP.rectTransform.anchorMax=row.HP.rectTransform.pivot=new Vector2(0,.5f);
                    row.Damage=Label("Damage",row.Rect,"",11,new Vector2(35,18),new Vector2(80,8));row.Damage.color=new Color(1,.45f,.4f);
                    var button=row.Rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=background;string id=player.id;button.onClick.AddListener(()=>WatchOnlinePlayer(id));hudProfiles[player.id]=row;
                }
                row.Rect.gameObject.SetActive(true);row.Rect.anchoredPosition=new Vector2(0,top-height/2);top-=height+4;
                if(player.hp<row.PreviousHP){row.DamageValue=row.PreviousHP-player.hp;row.DamageUntil=Time.unscaledTime+1.5f;}
                row.PreviousHP=player.hp;row.Damage.text=Time.unscaledTime<row.DamageUntil?"-"+row.DamageValue:"";
                row.Name.text=PlayerLabel(player.id,owner.playerId)+" · "+player.hp+" HP";
                row.State.text=player.eliminated?"#"+player.placement+" OUT":player.placement==1?"#1 WINNER":"Lv "+player.level+" · "+(pub.phase==MatchPhase.Combat?"IN BATTLE":"IN MATCH");
                row.Background.color=player.id==onlineService.WatchingPlayerId?new Color(.1f,.27f,.36f):player.eliminated?new Color(.09f,.12f,.16f):new Color(.12f,.18f,.25f);
                row.HP.rectTransform.sizeDelta=new Vector2(160*Mathf.Clamp01(player.hp/(float)Math.Max(1,player.maxHP)),3);
            }
        }
        private void RenderOnlineCombat(OwnerMatchState owner,PublicMatchState pub)
        {
            var playback=onlineService.Combat;var frame=playback.Current;
            bool show=frame!=null&&frame.round==pub.round&&(pub.phase==MatchPhase.Combat||playback.IsPlaying);
            combatRoot.gameObject.SetActive(show);if(!show)return;
            if(onlineBattleId!=frame.battleId){ClearOnlineFighters();onlineBattleId=frame.battleId;}
            combatInfo.text="VS "+PlayerLabel(frame.one==onlineService.WatchingPlayerId?frame.two:frame.one,owner.playerId)+(frame.shadow?" / SHADOW":"");
            double tick=playback.Tick+(frame.complete?Math.Min(.4,playback.EndHold)*30:0);
            foreach(var unit in frame.units){
                if(!combatTokens.TryGetValue(unit.id,out var label)){
                    var node=Rect("Fighter_"+unit.id,combatRoot,new Vector2(43,35),Vector2.zero);var image=node.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.12f,.44f,.58f,.2f);
                    var button=node.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;string id=unit.id;button.onClick.AddListener(()=>SelectOnlineUnit(id));
                    combatTokens[unit.id]=label=Label("HP",node,"",9,new Vector2(60,26),new Vector2(0,-15));fighterSprites[unit.id]=AddSprite(node,unit.definitionId);
                    onlineEffects[unit.id]=Label("Effect",node,"",11,new Vector2(90,18),new Vector2(0,25));
                }
                Vector2 position=CombatPoint(new BoardPosition(unit.column,unit.row)),facing=Vector2.zero;
                if(unit.action==CombatActionState.Moving){var destination=CombatPoint(new BoardPosition(unit.moveColumn,unit.moveRow));facing=destination-position;position=Vector2.Lerp(position,destination,(float)((tick-unit.startTick)/Math.Max(1,unit.endTick-unit.startTick)));}
                else{var target=frame.units.FirstOrDefault(u=>u.id==unit.target);if(target!=null)facing=CombatPoint(new BoardPosition(target.column,target.row))-position;}
                ((RectTransform)label.transform.parent).anchoredPosition=position;fighterSprites[unit.id].ShowPlayback(unit,tick,30,facing,1.5f);
                label.transform.parent.gameObject.SetActive(unit.onBoard&&(unit.hp>0||fighterSprites[unit.id].Visible));
                label.text=unit.hp<=0?"":"R"+unit.rank+" "+Math.Ceiling(unit.hp)+"HP\n"+Math.Floor(unit.energy)+"E"+(unit.shield>0?" S"+Math.Ceiling(unit.shield):"")+(unit.statuses.Length>0?" · "+unit.statuses[0]:"");
                label.color=unit.owner==onlineService.WatchingPlayerId?new Color(.6f,.9f,1):new Color(1,.65f,.6f);label.transform.SetAsLastSibling();
                onlineEffects[unit.id].gameObject.SetActive(onlineEffectUntil.TryGetValue(unit.id,out var end)&&end>Time.unscaledTime);
            }
            foreach(var shot in combatShots.Values)shot.gameObject.SetActive(false);
            foreach(var projectile in frame.projectiles){
                if(!combatShots.TryGetValue(projectile.id,out var shot)){shot=Rect("Projectile_"+projectile.id,combatRoot,new Vector2(7,7),Vector2.zero);shot.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(1,.8f,.15f);shot.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;combatShots[projectile.id]=shot;}
                shot.gameObject.SetActive(true);shot.anchoredPosition=Vector2.Lerp(CombatPoint(new BoardPosition(projectile.column,projectile.row)),CombatPoint(new BoardPosition(projectile.targetColumn,projectile.targetRow)),(float)((tick-projectile.spawnTick)/Math.Max(1,projectile.arrivalTick-projectile.spawnTick)));
            }
        }
        private void ShowOnlineEffect(CombatEventView effect)
        {
            if(effect.target==null||!onlineEffects.TryGetValue(effect.target,out var label))return;
            if(effect.kind=="Damage"||effect.kind=="EffectHeal"||effect.kind.StartsWith("Status")){
                label.text=effect.kind=="Damage"?"-"+Math.Ceiling(effect.value):effect.kind=="EffectHeal"?"+"+Math.Ceiling(effect.value):effect.detail;
                label.color=effect.kind=="Damage"?Color.red:Color.green;onlineEffectUntil[effect.target]=Time.unscaledTime+.45f;
            }
        }
        private void RenderOnlineResults(OwnerMatchState owner,PublicMatchState pub)
        {
            if(presentationRound!=pub.round){presentationRound=pub.round;resultExpanded=false;}
            bool playing=onlineService.Combat.Latest?.round==pub.round&&onlineService.Combat.IsPlaying;
            bool final=pub.phase==MatchPhase.Finished&&!playing;
            finalRoot.gameObject.SetActive(final);finalBackdrop.gameObject.SetActive(final);
            var own=pub.players.First(p=>p.id==owner.playerId);
            bool eliminated=own.eliminated&&!final&&!eliminationDismissed&&!playing;
            eliminationRoot.gameObject.SetActive(eliminated);
            if(eliminated){eliminationInfo.text="ELIMINATED · PLACEMENT #"+own.placement+"\n\nRound "+own.eliminationRound+" · "+own.eliminationReason+"\nThe remaining match continues.";eliminationRoot.SetAsLastSibling();}
            resultRoot.gameObject.SetActive(!final&&!eliminated&&!owner.eliminated&&!playing&&pub.phase==MatchPhase.Result);
            if(resultRoot.gameObject.activeSelf){
                var result=pub.results.FirstOrDefault(r=>r.playerId==owner.playerId);var reward=owner.reward;
                resultRoot.sizeDelta=new Vector2(600,resultExpanded?360:230);resultRoot.anchoredPosition=new Vector2(0,105);float shift=resultExpanded?65:0;
                resultTitle.rectTransform.anchoredPosition=new Vector2(0,88+shift);resultVitals.rectTransform.anchoredPosition=new Vector2(0,44+shift);resultRewards.rectTransform.anchoredPosition=new Vector2(0,6+shift);resultNext.rectTransform.anchoredPosition=new Vector2(0,-34+shift);((RectTransform)resultDetailsButton.transform).anchoredPosition=new Vector2(0,-76+shift);resultDetails.rectTransform.anchoredPosition=new Vector2(0,-95);
                resultTitle.text="ROUND "+pub.round+" · "+(result?.outcome=="Win"?"VICTORY":result?.outcome=="Loss"?"DEFEAT":"DRAW");
                resultVitals.text=reward?.round==pub.round?"HP "+reward.hpBefore+" → "+reward.hpAfter+" (−"+reward.damage+")":"HP "+owner.hp;
                resultRewards.text=reward?.round==pub.round?"+"+reward.totalIncome+"G · +"+reward.xpGranted+" XP":"Rewards pending";
                resultNext.text=(result==null?"":"VS "+PlayerLabel(result.opponentId,owner.playerId)+(result.shadow?" / SHADOW":"")+"\n")+"Next preparation starts automatically.";
                resultDetailsButton.gameObject.SetActive(true);resultDetails.gameObject.SetActive(resultExpanded);ButtonText(resultDetailsButton,resultExpanded?"Hide details":"Show details");
                resultDetails.text=reward==null?"":"Damage "+reward.damage+" = base "+reward.baseDamage+" + survivors "+reward.survivorDamage+"\nIncome "+reward.totalIncome+" = base "+reward.baseIncome+" + interest "+reward.interest+" + streak "+reward.streakBonus+"\nXP +"+reward.xpGranted+" · Level "+reward.levelBefore+" → "+reward.levelAfter;
                resultRoot.SetAsLastSibling();
            }
            if(final){
                bool winner=pub.winnerPlayerId==owner.playerId;finalTitle.text=winner?"VICTORY · CHAMPION":"MATCH COMPLETE · PLACEMENT #"+own.placement;
                finalTitle.color=winner?new Color(1,.85f,.4f):Color.white;finalSummary.text="Winner: "+PlayerLabel(pub.winnerPlayerId,owner.playerId)+" · End round "+pub.round;
                var standings=pub.players.OrderBy(p=>p.placement).ToArray();finalInfo.text=finalTitle.text+"\n"+string.Join("\n",standings.Select(p=>"#"+p.placement+" "+PlayerLabel(p.id,owner.playerId)+" / HP "+p.hp));
                for(int i=0;i<8;i++){finalRowBackgrounds[i].gameObject.SetActive(i<standings.Length);if(i>=standings.Length)continue;var p=standings[i];finalCells[i,0].text="#"+p.placement;finalCells[i,1].text=PlayerLabel(p.id,owner.playerId);finalCells[i,2].text=p.hp.ToString();finalCells[i,3].text=p.id==pub.winnerPlayerId?"WINNER":p.eliminationReason+" / R"+p.eliminationRound;finalRowBackgrounds[i].color=p.id==owner.playerId?new Color(.13f,.32f,.43f):new Color(.1f,.17f,.24f);}
                newMatchButton.interactable=true;finalBackdrop.SetAsLastSibling();finalRoot.SetAsLastSibling();
            }
            if(owner.eliminated&&eliminationDismissed&&!playing&&!final&&onlineService.WatchingPlayerId==owner.playerId){var alive=pub.players.FirstOrDefault(p=>!p.eliminated);if(alive!=null)WatchOnlinePlayer(alive.id);}
        }
    }
}
