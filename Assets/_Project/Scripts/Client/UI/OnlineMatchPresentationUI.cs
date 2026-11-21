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
            HudTimerKind = pub.phase.ToString();
            bool synchronized = onlineService.StateSynchronized;
            bool watchingOwnBoard = onlineService.WatchingPlayerId == owner.playerId;
            var playback = onlineService.Combat;
            var frame = playback.Current;
            bool hasBattle = pub.phase == MatchPhase.Combat && frame != null && frame.round == pub.round && !frame.complete;
            double? remaining = null;
            double duration = 0;
            if (synchronized && pub.hasPhaseDeadline)
            {
                remaining = onlineService.PhaseRemainingSeconds;
                duration = pub.phase == MatchPhase.Preparation ? 30 : RoundFlowRules.DefaultResultSeconds;
            }
            else if (synchronized && hasBattle)
            {
                bool overtime = frame.overtimeTick > 0 && playback.Tick >= frame.overtimeTick;
                HudTimerKind = overtime ? "Overtime" : "Combat";
                long endTick = overtime ? frame.timeLimitTick : frame.overtimeTick;
                remaining = (endTick - playback.Tick) / frame.tickRate;
                duration = (endTick - (overtime ? frame.overtimeTick : 0)) / (double)frame.tickRate;
            }
            HudTimerProgress = remaining.HasValue && duration > 0 ? Mathf.Clamp01((float)(remaining.Value / duration)) : 0;
            // Results can arrive together with the following preparation snapshot.
            int resultRound = pub.resultRound > 0 ? pub.resultRound :
                pub.phase == MatchPhase.Preparation ? pub.round - 1 : pub.round;
            RecordRoundLosses(pub.matchId, resultRound, (pub.results ?? Array.Empty<PublicRoundResult>())
                .Where(result => resultRound > 0 && result.outcome == "Loss").Select(result => result.playerId));
            RenderRoundStrip(pub.round, true, remaining);
            RenderHudTimer(pub.phase != MatchPhase.Finished && (pub.phase != MatchPhase.Combat || hasBattle),
                HudTimerKind == "Overtime" || pub.phase == MatchPhase.Preparation && remaining <= 5);
            hudNotice.text = onlineService.CombatError ?? (pub.phase == MatchPhase.Finished ? "MATCH COMPLETE"
                : !synchronized ? "Synchronizing…"
                : pub.phase == MatchPhase.Combat && !hasBattle ? "Waiting for battle" : "");
            RenderBoardCapacity(synchronized && pub.phase == MatchPhase.Preparation && watchingOwnBoard && !owner.eliminated,
                owner.units.Count(u => u.placement == PlacementKind.Board), owner.BoardCapacity);
            RenderOnlineSpectators(owner, pub, synchronized, frame);
        }
        private void RenderOnlineCombat(OwnerMatchState owner,PublicMatchState pub)
        {
            var playback=onlineService.Combat;var frame=playback.Current;
            bool show=frame!=null&&frame.round==pub.round&&(pub.phase.IsBattlePhase()||playback.IsPlaying);
            combatRoot.gameObject.SetActive(show);if(!show)return;
            if(onlineBattleId!=frame.battleId){ClearOnlineFighters();onlineBattleId=frame.battleId;}
            combatInfo.text=string.Empty;
            double tick=playback.Tick+(frame.complete?Math.Min(.4,playback.EndHold)*30:0);
            var visuals=SkillVisualBattleView.Get(combatRoot,SkillVisuals);
            visuals.SetVenomHud(venomHud, frame.units.FirstOrDefault(unit => unit.owner == onlineService.WatchingPlayerId)?.team ?? 0);
            visuals.SetStormHud(stormBattleHud, frame.units.FirstOrDefault(unit => unit.owner == onlineService.WatchingPlayerId)?.team ?? 0);
            visuals.RenderPlayback(frame,tick,(column,row)=>CombatPoint(new BoardPosition(column,row)));
            foreach(var unit in frame.units){
                if(!combatTokens.TryGetValue(unit.id,out var label)){
                    var node=Rect("Fighter_"+unit.id,combatRoot,new Vector2(43,35),Vector2.zero);var image=node.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.12f,.44f,.58f,.2f);
                    var button=node.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;string id=unit.id;button.onClick.AddListener(()=>SelectOnlineUnit(id));var hover=node.gameObject.AddComponent<UnitHoverTarget>();hover.View=this;hover.UnitId=id;
                    combatTokens[unit.id]=label=Label("HP",node,"",9,new Vector2(60,26),new Vector2(0,-15));fighterSprites[unit.id]=AddSprite(node,unit.definitionId,unit.id);
                    onlineEffects[unit.id]=Label("Effect",node,"",11,new Vector2(90,18),new Vector2(0,98));
                }
                Vector2 position=CombatPoint(new BoardPosition(unit.column,unit.row)),facing=Vector2.zero;
                if(unit.action==CombatActionState.Moving){var destination=CombatPoint(new BoardPosition(unit.moveColumn,unit.moveRow));facing=destination-position;position=Vector2.Lerp(position,destination,(float)((tick-unit.startTick)/Math.Max(1,unit.endTick-unit.startTick)));}
                else{var target=frame.units.FirstOrDefault(u=>u.id==unit.target);if(target!=null)facing=CombatPoint(new BoardPosition(target.column,target.row))-position;}
                position=ThrowPresentation.Playback(frame,unit.id,tick,position,(c,r)=>CombatPoint(new BoardPosition(c,r)));
                var sprite = fighterSprites[unit.id];
                if (sprite.PokemonId != unit.definitionId) sprite.Initialize(unit.definitionId, unit.id);
                ((RectTransform)label.transform.parent).anchoredPosition=position;sprite.ShowPlayback(unit,tick,30,facing,1.5f);
                label.transform.parent.gameObject.SetActive(unit.onBoard&&(unit.hp>0||fighterSprites[unit.id].Visible));
                RenderEquippedItemIcon(label.transform.parent,unit.items,unit.hp>0);
                var healthBar = RenderUnitHealthBar(label.transform.parent, unit.hp, unit.maxHP, unit.owner == onlineService.WatchingPlayerId, unit.shield);
                BruiserVisuals.Render(label.transform.parent, healthBar, unit.hp > 0 && unit.onBoard, unit.bruiserHealth,
                    unit.bruiserSurvivalEndTick, tick, 30);
                RenderUnitEnergyBar(label.transform.parent, unit.energy, unit.maxEnergy, unit.hp > 0);

                RenderUnitRank(label.transform.parent,unit.rank,PokeChess.Content.GameContent.Stage(unit.definitionId),unit.hp>0);
                RenderOnlineUnitStatuses(label.transform.parent, unit, tick);
                label.rectTransform.anchoredPosition=new Vector2(0,UnitBarHeight(label.transform.parent)+UnitStatusStackHeight(label.transform.parent)+32);
                onlineEffects[unit.id].rectTransform.anchoredPosition=new Vector2(0,UnitBarHeight(label.transform.parent)+UnitStatusStackHeight(label.transform.parent)+52);
                label.text="";
                label.color=unit.owner==onlineService.WatchingPlayerId?new Color(.6f,.9f,1):new Color(1,.65f,.6f);label.transform.SetAsLastSibling();
                onlineEffects[unit.id].gameObject.SetActive(onlineEffectUntil.TryGetValue(unit.id,out var end)&&end>Time.unscaledTime);
            }
            visuals.SortCombatDepth(combatTokens.Values.Select(label=>(RectTransform)label.transform.parent));
            foreach(var shot in combatShots.Values)shot.gameObject.SetActive(false);
            foreach(var projectile in frame.projectiles){
                if(!combatShots.TryGetValue(projectile.id,out var shot)){shot=Rect("Projectile_"+projectile.id,combatRoot,new Vector2(7,7),Vector2.zero);shot.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(1,.8f,.15f);shot.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;combatShots[projectile.id]=shot;}
                shot.gameObject.SetActive(!SkillVisualBattleView.UsesProjectileVisual(SkillVisuals,projectile.skillId,projectile.stepIndex,projectile.iteration));shot.anchoredPosition=Vector2.Lerp(CombatPoint(new BoardPosition(projectile.column,projectile.row)),CombatPoint(new BoardPosition(projectile.targetColumn,projectile.targetRow)),(float)((tick-projectile.spawnTick)/Math.Max(1,projectile.arrivalTick-projectile.spawnTick)));
            }
        }
        private void ShowOnlineEffect(CombatEventView effect)
        {
            if (effect.kind == "WildItemDrop")
            {
                var owner = onlineService.LocalMatchState;
                if (owner != null && effect.target == owner.playerId && effect.target == onlineService.WatchingPlayerId &&
                    effect.itemRewardCell != null)
                    PlayItemReward(new BoardPosition(effect.itemRewardCell.column, effect.itemRewardCell.row), effect.detail, effect.iteration);
                return;
            }
            var visualFrame=onlineService.Combat.Current;
            if(visualFrame!=null)SkillVisualBattleView.Get(combatRoot,SkillVisuals).PlaybackEvent(effect,visualFrame,
                (column,row)=>CombatPoint(new BoardPosition(column,row)));
            PlayOnlineUnitCry(effect);
            PlayOnlineCombatSound(effect);
            if(effect.target==null||!onlineEffects.TryGetValue(effect.target,out var label))return;
            if(effect.kind=="Item") { label.font=itemFont;label.text=ItemCatalog.Get(effect.detail).Name;label.color=new Color(.5f,1,.7f);onlineEffectUntil[effect.target]=Time.unscaledTime+.65f; }
            if(effect.kind=="Damage"||effect.kind=="EffectHeal"){
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
            resultRoot.gameObject.SetActive(false);
            if(resultRoot.gameObject.activeSelf){
                var result=pub.results.FirstOrDefault(r=>r.playerId==owner.playerId);var reward=owner.reward;
                resultRoot.sizeDelta=new Vector2(600,resultExpanded?360:230);resultRoot.anchoredPosition=new Vector2(0,105);float shift=resultExpanded?65:0;
                resultTitle.rectTransform.anchoredPosition=new Vector2(0,88+shift);resultVitals.rectTransform.anchoredPosition=new Vector2(0,44+shift);resultRewards.rectTransform.anchoredPosition=new Vector2(0,6+shift);resultNext.rectTransform.anchoredPosition=new Vector2(0,-34+shift);((RectTransform)resultDetailsButton.transform).anchoredPosition=new Vector2(0,-76+shift);resultDetails.rectTransform.anchoredPosition=new Vector2(0,-95);
                resultTitle.text="ROUND "+pub.round+" · "+(result?.outcome=="Win"?"VICTORY":result?.outcome=="Loss"?"DEFEAT":"DRAW");
                resultVitals.text=reward?.round==pub.round?"HP "+reward.hpBefore+" → "+reward.hpAfter+" (−"+reward.damage+")":"HP "+owner.hp;
                resultRewards.text=reward?.round==pub.round?"+"+reward.totalIncome+"G · +"+reward.xpGranted+" XP":"Rewards pending";
                resultNext.text=(result==null?"":"VS "+PlayerLabel(result.opponentId,owner.playerId)+(result.shadow?" / SHADOW":"")+"\n")+"Next preparation starts automatically.";
                resultDetailsButton.gameObject.SetActive(true);resultDetails.gameObject.SetActive(resultExpanded);ButtonText(resultDetailsButton,resultExpanded?"Hide details":"Show details");
                resultDetails.text=reward==null?"":"Damage "+reward.damage+" = base "+reward.baseDamage+" + survivors "+reward.survivorDamage+"\nIncome "+reward.totalIncome+" = base "+reward.baseIncome+" + interest "+reward.interest+" + streak "+reward.streakBonus+(reward.wildClearBonus>0?" + wild clear "+reward.wildClearBonus:"")+"\nXP +"+reward.xpGranted+" · Level "+reward.levelBefore+" → "+reward.levelAfter;
                resultRoot.SetAsLastSibling();
            }
            if(final){
                bool winner=pub.winnerPlayerId==owner.playerId;finalTitle.text=winner?"VICTORY · CHAMPION":"MATCH COMPLETE · PLACEMENT #"+own.placement;
                finalTitle.color=winner?new Color(1,.85f,.4f):Color.white;finalSummary.text="Winner: "+PlayerLabel(pub.winnerPlayerId,owner.playerId)+" · End round "+pub.round;
                var standings=pub.players.OrderBy(p=>p.placement).ToArray();finalInfo.text=finalTitle.text+"\n"+string.Join("\n",standings.Select(p=>"#"+p.placement+" "+PlayerLabel(p.id,owner.playerId)+" / HP "+p.hp));
                for(int i=0;i<8;i++){finalRowBackgrounds[i].gameObject.SetActive(i<standings.Length);if(i>=standings.Length)continue;var p=standings[i];finalCells[i,0].text="#"+p.placement;finalCells[i,1].text=PlayerLabel(p.id,owner.playerId);finalCells[i,2].text=p.hp.ToString();finalCells[i,3].text=p.id==pub.winnerPlayerId?"WINNER":p.eliminationReason+" / R"+p.eliminationRound;finalRowBackgrounds[i].color=p.id==owner.playerId?new Color(.13f,.32f,.43f):new Color(.1f,.17f,.24f);}
                newMatchButton.interactable=true;finalPresentation.SetAsLastSibling();
            }
        }
    }
}



