using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Battle;
using PokeChess.Network.Session;

namespace PokeChess.Client.UI
{
    public sealed partial class RoomFlowView
    {
        private RectTransform combatLayer;
        private UnityEngine.UI.Text combatCaption,combatResults;
        private sealed class Fighter {public RectTransform root;public PokemonSpriteView sprite;public UnityEngine.UI.Text label,popup;public double popupUntil;}
        private readonly Dictionary<string,Fighter> combatFighters=new Dictionary<string,Fighter>();
        private readonly Dictionary<long,RectTransform> combatProjectiles=new Dictionary<long,RectTransform>();
        private string visibleBattle;
        private CombatPlayback subscribedPlayback;
        private void OnDestroy(){if(subscribedPlayback!=null)subscribedPlayback.EventPlayed-=ReplayEffect;}
        private void BuildOnlineCombatUI(RectTransform parent)
        {
            combatLayer=Rect("CombatPlayback",parent,parent.sizeDelta,Vector2.zero);
            combatCaption=Label("CombatCaption",combatLayer,"",new Vector2(450,20),new Vector2(0,52),11);
            combatResults=Label("CombatResults",parent,"",new Vector2(530,185),new Vector2(0,-28),13);
            combatLayer.gameObject.SetActive(false);combatResults.gameObject.SetActive(false);
        }
        private static Vector2 ReplayPoint(int column,int row)=>new Vector2(-190+column*60+(row%2)*30,35-row*22);
        private void UpdateOnlineCombatUI(OnlineConnection service)
        {
            if(subscribedPlayback!=service.Combat){if(subscribedPlayback!=null)subscribedPlayback.EventPlayed-=ReplayEffect;subscribedPlayback=service.Combat;subscribedPlayback.EventPlayed+=ReplayEffect;}
            var playback=service.Combat;var frame=playback.Current;var pub=service.PublicState;
            bool show=pub!=null&&frame!=null&&frame.round==pub.round&&(pub.phase==MatchPhase.Combat||playback.IsPlaying);
            combatLayer.gameObject.SetActive(show);
            bool result=pub!=null&&(pub.phase==MatchPhase.Result||pub.phase==MatchPhase.Finished)&&!show;
            combatResults.gameObject.SetActive(result);
            foreach(var cell in onlineBoard)cell.gameObject.SetActive(!show&&!result);
            foreach(var cell in onlineBench){var r=cell.GetComponent<RectTransform>();r.anchoredPosition=new Vector2(r.anchoredPosition.x,show?-155:-130);r.sizeDelta=new Vector2(74,show?26:42);}
            onlineMatchPanel.transform.Find("Observe next").GetComponent<UnityEngine.UI.Button>().interactable=!show&&!result;
            onlineMatchPanel.transform.Find("My board").GetComponent<UnityEngine.UI.Button>().interactable=!show&&!result;
            if(pub?.hasPhaseDeadline==true)onlineEconomy.text+=" / "+Math.Ceiling(service.PhaseRemainingSeconds)+"s";
            if(service.CombatError!=null)onlineActionStatus.text=service.CombatError;
            if(result){
                if(pub.phase==MatchPhase.Finished)combatResults.text="FINAL RESULT\n"+string.Join("\n",pub.players.OrderBy(p=>p.placement).Select(p=>"#"+p.placement+" "+(p.id==service.LocalMatchState.playerId?"You":p.id.Substring(Math.Max(0,p.id.Length-6)))+" / HP "+p.hp));
                else {var own=pub.results.FirstOrDefault(r=>r.playerId==service.LocalMatchState.playerId);var reward=service.LocalMatchState.reward;combatResults.text="ROUND "+pub.round+" / "+(own?.outcome??"Waiting for other battles")+"\nHP "+service.LocalMatchState.hp+(reward?.round==pub.round?"\nIncome +"+reward.totalIncome+"G (base "+reward.baseIncome+", interest "+reward.interest+", streak "+reward.streakBonus+")":"");}
            }
            if(!show)return;
            onlineObserved.text="Your combat";
            combatCaption.text="Round "+frame.round+" / "+(frame.shadow?"Shadow battle":"Combat")+" / "+(frame.tick/30d).ToString("0.0")+"s";
            if(visibleBattle!=frame.battleId){foreach(var f in combatFighters.Values)Destroy(f.root.gameObject);combatFighters.Clear();foreach(var p in combatProjectiles.Values)Destroy(p.gameObject);combatProjectiles.Clear();visibleBattle=frame.battleId;}
            double renderTick=playback.Tick+(frame.complete?Math.Min(.4,playback.EndHold)*30:0);
            foreach(var u in frame.units){
                if(!combatFighters.TryGetValue(u.id,out var f)){
                    var node=Rect("Fighter "+u.id,combatLayer,new Vector2(40,36),Vector2.zero);
                    var spriteNode=Rect("Sprite",node,new Vector2(32,32),Vector2.zero);var sprite=spriteNode.gameObject.AddComponent<PokemonSpriteView>();sprite.Initialize(u.definitionId);
                    f=new Fighter{root=node,sprite=sprite,label=Label("Vitals",node,"",new Vector2(70,22),new Vector2(0,-14),8),popup=Label("Effect",node,"",new Vector2(65,17),new Vector2(0,20),10)};
                    f.label.raycastTarget=f.popup.raycastTarget=false;combatFighters.Add(u.id,f);
                }
                f.root.gameObject.SetActive(u.onBoard||u.hp<=0);var position=ReplayPoint(u.column,u.row);Vector2 facing=Vector2.zero;
                if(u.action==CombatActionState.Moving){var destination=ReplayPoint(u.moveColumn,u.moveRow);facing=destination-position;position=Vector2.Lerp(position,destination,(float)((playback.Tick-u.startTick)/Math.Max(1,u.endTick-u.startTick)));}
                else {var target=frame.units.FirstOrDefault(v=>v.id==u.target);if(target!=null)facing=ReplayPoint(target.column,target.row)-position;}
                f.root.anchoredPosition=position;f.sprite.ShowPlayback(u,renderTick,30,facing);
                f.label.text="R"+u.rank+" / "+Math.Ceiling(u.hp)+"HP"+(u.shield>0?" S"+Math.Ceiling(u.shield):"")+"\n"+Math.Floor(u.energy)+"E"+(u.statuses.Length>0?" / "+string.Join("/",u.statuses):"");
                f.label.color=u.team==1?new Color(.5f,.85f,1):new Color(1,.65f,.55f);f.popup.gameObject.SetActive(Time.unscaledTimeAsDouble<f.popupUntil);
            }
            foreach(var id in combatProjectiles.Keys.Where(id=>!frame.projectiles.Any(p=>p.id==id)).ToArray()){Destroy(combatProjectiles[id].gameObject);combatProjectiles.Remove(id);}
            foreach(var p in frame.projectiles){if(!combatProjectiles.TryGetValue(p.id,out var marker)){marker=Rect("Projectile "+p.id,combatLayer,new Vector2(5,5),Vector2.zero);var image=marker.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=Color.yellow;image.raycastTarget=false;combatProjectiles[p.id]=marker;}
                var target=frame.units.FirstOrDefault(u=>u.id==p.target);var end=target==null?ReplayPoint(p.targetColumn,p.targetRow):ReplayPoint(target.column,target.row);
                marker.anchoredPosition=Vector2.Lerp(ReplayPoint(p.column,p.row),end,(float)((playback.Tick-p.spawnTick)/Math.Max(1,p.arrivalTick-p.spawnTick)));}
        }
        private void ReplayEffect(CombatEventView effect)
        {
            if(effect.target==null||!combatFighters.TryGetValue(effect.target,out var f))return;
            if(effect.kind=="Damage"||effect.kind=="EffectHeal"||effect.kind.StartsWith("Status")){
                f.popup.text=effect.kind=="Damage"?"-"+Math.Ceiling(effect.value):effect.kind=="EffectHeal"?"+"+Math.Ceiling(effect.value):effect.detail;
                f.popup.color=effect.kind=="Damage"?Color.red:Color.green;f.popupUntil=Time.unscaledTimeAsDouble+.45;
            }
        }
    }
}
