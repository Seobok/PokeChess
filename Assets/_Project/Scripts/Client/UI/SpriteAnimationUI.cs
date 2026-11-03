using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;

namespace PokeChess.Client.UI
{
    public static class ThrowPresentation
    {
        public static Vector2 CarryPosition(Vector2[] path,double tick,long start,long departure,long end,float height)
        {
            if(path.Length<3)return DashPosition(path,tick,start,end);
            if(tick<departure)return Vector2.Lerp(path[0],path[1],Mathf.Clamp01((float)((tick-start)/System.Math.Max(1,departure-start))));
            return Position(path[1],path[path.Length-1],tick,departure,end,height);
        }
        public static Vector2 DashPosition(Vector2[] path,double tick,long startTick,long endTick)
        {
            float progress=Mathf.Clamp01((float)((tick-startTick)/System.Math.Max(1,endTick-startTick)))*(path.Length-1);
            int segment=Mathf.Min(path.Length-2,Mathf.FloorToInt(progress));
            return Vector2.Lerp(path[segment],path[segment+1],progress-segment);
        }
        public static Vector2 KnockbackPosition(Vector2 start,Vector2 end,double tick,long departureTick,long endTick)
        {
            float t=Mathf.Clamp01((float)((tick-departureTick)/System.Math.Max(1,endTick-departureTick)));
            return Vector2.Lerp(start,end,1-Mathf.Pow(1-t,3));
        }
        public static Vector2 Position(Vector2 start,Vector2 end,double tick,long startTick,long endTick,float height)
        {
            float t=Mathf.Clamp01((float)((tick-startTick)/System.Math.Max(1,endTick-startTick)));
            return Vector2.Lerp(start,end,t)+Vector2.up*(4*t*(1-t)*height);
        }
        public static Vector2 Local(BattleState battle,string unit,Vector2 fallback,System.Func<int,int,Vector2> point)
        {
            var move=battle.Movement.Active.FirstOrDefault(m=>m.UnitId==unit&&(m.Kind==SkillMovementKind.Throw||m.Kind==SkillMovementKind.Knockback||m.Kind==SkillMovementKind.DashToTarget||m.Kind==SkillMovementKind.CarryTarget||m.Kind==SkillMovementKind.JumpToTarget||m.Kind==SkillMovementKind.JumpToCluster));
            if(move==null||move.Path.Count<2)return fallback;
            var a=move.Path.First();var b=move.Path.Last();
            if(move.Kind==SkillMovementKind.CarryTarget)return CarryPosition(move.Path.Select(p=>point(p.Column,p.Row)).ToArray(),battle.CurrentTick,move.StartTick,move.DepartureTick,move.EndTick,Vector2.Distance(point(0,0),point(1,0))*1.5f);
            if(move.Kind==SkillMovementKind.DashToTarget)return DashPosition(move.Path.Select(p=>point(p.Column,p.Row)).ToArray(),battle.CurrentTick,move.StartTick,move.EndTick);
            if(move.Kind==SkillMovementKind.Knockback)return KnockbackPosition(point(a.Column,a.Row),point(b.Column,b.Row),battle.CurrentTick,move.DepartureTick,move.EndTick);
            return Position(point(a.Column,a.Row),point(b.Column,b.Row),battle.CurrentTick,move.DepartureTick,move.EndTick,
                Vector2.Distance(point(0,0),point(1,0))*1.5f);
        }
        public static Vector2 Playback(CombatFrame frame,string unit,double tick,Vector2 fallback,System.Func<int,int,Vector2> point)
        {
            var move=frame.movements?.FirstOrDefault(m=>m.unit==unit&&(m.kind==(int)SkillMovementKind.Throw||m.kind==(int)SkillMovementKind.Knockback||m.kind==(int)SkillMovementKind.DashToTarget||m.kind==(int)SkillMovementKind.CarryTarget||m.kind==(int)SkillMovementKind.JumpToTarget||m.kind==(int)SkillMovementKind.JumpToCluster));
            if(move?.path==null||move.path.Length<2)return fallback;
            var a=move.path.First();var b=move.path.Last();
            if(move.kind==(int)SkillMovementKind.CarryTarget)return CarryPosition(move.path.Select(p=>point(p.column,p.row)).ToArray(),tick,move.startTick,System.Math.Max(move.startTick,move.departureTick),move.endTick,Vector2.Distance(point(0,0),point(1,0))*1.5f);
            if(move.kind==(int)SkillMovementKind.DashToTarget)return DashPosition(move.path.Select(p=>point(p.column,p.row)).ToArray(),tick,move.startTick,move.endTick);
            if(move.kind==(int)SkillMovementKind.Knockback)return KnockbackPosition(point(a.column,a.row),point(b.column,b.row),tick,System.Math.Max(move.startTick,move.departureTick),move.endTick);
            return Position(point(a.column,a.row),point(b.column,b.row),tick,System.Math.Max(move.startTick,move.departureTick),move.endTick,
                Vector2.Distance(point(0,0),point(1,0))*1.5f);
        }
    }
    public sealed partial class PlacementSandboxView
    {
        private readonly Dictionary<string,PokemonSpriteView> placementSprites=new Dictionary<string,PokemonSpriteView>();
        private readonly Dictionary<string,PokemonSpriteView> fighterSprites=new Dictionary<string,PokemonSpriteView>();
        private readonly Dictionary<int,PokemonSpriteView> shopSprites=new Dictionary<int,PokemonSpriteView>();
        private PokemonSpriteView AddSprite(Transform parent,string species,string instanceId=null)
        {
            var rect=Rect("PokemonSprite",parent,Vector2.one,Vector2.zero);
            var view=rect.gameObject.AddComponent<PokemonSpriteView>();view.Initialize(species,instanceId);return view;
        }
        private void AnimateRosterSprites()
        {
            foreach(var unit in ViewedPlayer.Units)
            {
                if(!tokens.TryGetValue(unit.InstanceId,out var token))continue;
                if(!placementSprites.TryGetValue(unit.InstanceId,out var sprite)||sprite==null)
                    placementSprites[unit.InstanceId]=sprite=AddSprite(token,unit.DefinitionId);
                if(sprite.PokemonId!=unit.DefinitionId)sprite.Initialize(unit.DefinitionId);
                if(sprite.ShowIdle(Time.unscaledTimeAsDouble))
                {
                    tokenLabels[unit.InstanceId].text="";
                    RenderPreparationVitals(token,unit.DefinitionId,(int)unit.Rank,localViewedPlayerId=="p1");
                }
            }
            for(int slot=0;slot<Player.Shop.Slots.Count;slot++)
            {
                var offer=Player.Shop.Slots[slot];
                if(offer.IsEmpty)continue;
                if(!shopSprites.TryGetValue(slot,out var sprite)||sprite==null)
                    shopSprites[slot]=sprite=AddSprite(shopCards[slot].transform,offer.DefinitionId);
                if(sprite.PokemonId!=offer.DefinitionId)sprite.Initialize(offer.DefinitionId);
                if(sprite.ShowIdle(Time.unscaledTimeAsDouble))
                {
                    sprite.transform.localPosition+=new Vector3(-50,16,0);
                }
            }
            foreach(var entry in shopSprites)entry.Value.gameObject.SetActive(!Player.Shop.Slots[entry.Key].IsEmpty);
        }
        private bool RenderFighterSprite(LocalPairBattle pair,UnitCombatState unit,UnityEngine.UI.Text label)
        {
            if(!fighterSprites.TryGetValue(unit.UnitInstanceId,out var sprite)||sprite==null)
                fighterSprites[unit.UnitInstanceId]=sprite=AddSprite(label.transform.parent,unit.DefinitionId,unit.UnitInstanceId);
            var action=pair.GetActionRuntime(unit.UnitInstanceId);
            Vector2 current=CombatPoint(unit.Position),facing=Vector2.zero;
            if(action.MoveDestination.HasValue)
            {
                Vector2 destination=CombatPoint(action.MoveDestination.Value);facing=destination-current;
                float progress=(float)(pair.Battle.CurrentTick-action.StartTick)/System.Math.Max(1,action.EndTick-action.StartTick);
                ((RectTransform)label.transform.parent).anchoredPosition=Vector2.Lerp(current,destination,progress);
            }
            else
            {
                string target=unit.ActionState==CombatActionState.Casting?action.SkillCast?.TargetId:unit.CurrentTargetId;
                var other=pair.Battle.Units.FirstOrDefault(u=>u.UnitInstanceId==target);
                if(other!=null && other!=unit)facing=CombatPoint(other.Position)-current;
            }
            var root=(RectTransform)label.transform.parent;
            root.anchoredPosition=ThrowPresentation.Local(pair.Battle,unit.UnitInstanceId,root.anchoredPosition,
                (c,r)=>CombatPoint(new PokeChess.Core.Pokemon.BoardPosition(c,r)));
            sprite.ShowCombat(pair.Battle,unit,action,facing);
            RenderUnitHealthBar(label.transform.parent, unit.CurrentHP, unit.Stats.MaxHP,
                unit.TeamId == (pair.Pairing.PlayerOneId == localViewedPlayerId ? 1 : 2), unit.CurrentShield);
            RenderUnitEnergyBar(label.transform.parent, unit.CurrentEnergy, unit.Stats.MaxEnergy, unit.IsAlive);
            RenderEquippedItemIcon(label.transform.parent,unit.ItemInstanceIds,unit.IsAlive);
            RenderUnitRank(label.transform.parent,(int)unit.Rank,unit.EvolutionStage,unit.IsAlive);
            if(sprite.Visible)
            {
                label.rectTransform.anchoredPosition=new Vector2(0,UnitBarHeight(label.transform.parent)+28);label.rectTransform.sizeDelta=new Vector2(64,18);
                label.fontSize=9;label.transform.SetAsLastSibling();
                string state=label.text.Split('\n').Last();
                int charges = pair.Battle.Synergies.AssaultCharges(unit);
                if (charges > 0) state = "AT+ " + charges;
                string synergyFlash = LocalSynergyFlash(unit);
                if (synergyFlash.Length > 0) state = synergyFlash;
                if(unit.ActionState!=CombatActionState.Casting)
                {
                    if(unit.HasCrowdControl(CrowdControlKind.Stun))state="STUN";
                    else if(unit.HasCrowdControl(CrowdControlKind.Taunt))state="TAUNT";
                    else if(unit.HasCrowdControl(CrowdControlKind.Slow))state="SLOW";
                }
                label.text=state;
                var background=label.transform.parent.GetComponent<UnityEngine.UI.Image>();
                var c=background.color;c.a=.2f;background.color=c;
                if(!unit.IsAlive)label.text="";
            }
            return unit.IsAlive||sprite.Visible;
        }
        private void ClearFighterSprites()
        {
            foreach(var sprite in fighterSprites.Values)if(sprite!=null){sprite.gameObject.SetActive(false);Destroy(sprite.gameObject);}
            fighterSprites.Clear();
        }
    }
}

