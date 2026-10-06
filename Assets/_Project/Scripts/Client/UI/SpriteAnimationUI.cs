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
        private PokemonSpriteView AddSprite(Transform parent,string species)
        {
            var rect=Rect("PokemonSprite",parent,Vector2.one,Vector2.zero);
            var view=rect.gameObject.AddComponent<PokemonSpriteView>();view.Initialize(species);return view;
        }
        private void AnimateRosterSprites()
        {
            foreach(var unit in Player.Units)
            {
                if(!tokens.TryGetValue(unit.InstanceId,out var token))continue;
                if(!placementSprites.TryGetValue(unit.InstanceId,out var sprite)||sprite==null)
                    placementSprites[unit.InstanceId]=sprite=AddSprite(token,unit.DefinitionId);
                if(sprite.PokemonId!=unit.DefinitionId)sprite.Initialize(unit.DefinitionId);
                if(sprite.ShowIdle(Time.unscaledTimeAsDouble))
                {
                    var text=tokenLabels[unit.InstanceId];text.text="R"+(int)unit.Rank;text.fontSize=11;
                    text.rectTransform.anchoredPosition=new Vector2(0,25);text.color=Color.white;text.transform.SetAsLastSibling();
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
                    // Reserve the left side for artwork; preserve the existing buy button and text.
                    var text=shopCards[slot].GetComponentInChildren<UnityEngine.UI.Text>();
                    text.rectTransform.sizeDelta=new Vector2(115,100);text.rectTransform.anchoredPosition=new Vector2(25,0);
                    sprite.transform.localPosition+=new Vector3(-52,0,0);
                }
            }
            foreach(var entry in shopSprites)entry.Value.gameObject.SetActive(!Player.Shop.Slots[entry.Key].IsEmpty);
        }
        private bool RenderFighterSprite(LocalPairBattle pair,UnitCombatState unit,UnityEngine.UI.Text label)
        {
            if(!fighterSprites.TryGetValue(unit.UnitInstanceId,out var sprite)||sprite==null)
                fighterSprites[unit.UnitInstanceId]=sprite=AddSprite(label.transform.parent,unit.DefinitionId);
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
            if(sprite.Visible)
            {
                label.rectTransform.anchoredPosition=new Vector2(0,-15);label.rectTransform.sizeDelta=new Vector2(46,24);
                label.fontSize=9;label.transform.SetAsLastSibling();
                string state=label.text.Split('\n').Last();
                label.text="R"+(int)unit.Rank+" "+Mathf.CeilToInt(unit.CurrentHP)+"HP\n"+state;
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
