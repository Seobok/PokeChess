using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;

namespace PokeChess.Client.UI
{
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

