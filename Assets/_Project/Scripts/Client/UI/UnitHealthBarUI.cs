using UnityEngine;
using System.Collections.Generic;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private const float UnitHealthHeight=48, UnitEnergyHeight=43, UnitItemHeight=64;
        private float UnitBarHeight(Transform unit)=>Mathf.Max(UnitHealthHeight,(unit.GetComponentInChildren<StatusSpriteView>()?.Top??0)+8);
        private void RenderUnitRank(Transform unit,int rank,int evolutionStage,bool visible=true)
        {
            var label=unit.Find("RankIndicator")?.GetComponent<UnityEngine.UI.Text>();
            if(label==null) {
                label=Label("RankIndicator",unit,"",9,new Vector2(48,16),new Vector2(-48,UnitHealthHeight));
                label.alignment=TextAnchor.MiddleRight;label.raycastTarget=false;
            }
            label.text=new string('★',rank)+(evolutionStage>0?" E"+evolutionStage:"");
            label.rectTransform.anchoredPosition=new Vector2(-48,UnitBarHeight(unit));
            label.color=Color.white;label.gameObject.SetActive(visible);label.transform.SetAsLastSibling();
        }
        private void RenderPreparationVitals(Transform unit,string definition,int rank,bool friendly)
        {
            var stats=RankRules.Default.Apply(catalog.Get(definition),(UnitRank)rank);
            RenderUnitHealthBar(unit,stats.MaxHP,stats.MaxHP,friendly);
            RenderUnitEnergyBar(unit,stats.StartingEnergy,stats.MaxEnergy,true);
            RenderUnitRank(unit,rank,PokeChess.Content.GameContent.Stage(definition));
        }
        private void RenderEquippedItemIcon(Transform unit, IEnumerable<string> items, bool visible=true, float height=UnitItemHeight)
        {
            height=Mathf.Max(height,UnitBarHeight(unit)+16);
            Sprite sprite=null;
            if(items!=null)foreach(var item in items){sprite=itemIcons?.Get(ItemCatalog.DefinitionId(item));if(sprite!=null)break;}
            var icon=unit.Find("EquippedItemIcon")?.GetComponent<UnityEngine.UI.Image>();
            if(icon==null&&sprite==null)return;
            if(icon==null){
                icon=HudImage("EquippedItemIcon",unit,new Vector2(16,16),new Vector2(0,height),Color.white);
                icon.preserveAspect=true;icon.raycastTarget=false;
            }
            icon.sprite=sprite;icon.rectTransform.anchoredPosition=new Vector2(0,height);
            icon.gameObject.SetActive(visible&&sprite!=null);icon.transform.SetAsLastSibling();
        }
        private void RenderUnitEnergyBar(Transform fighter, double energy, double maxEnergy, bool alive)
        {
            var track = fighter.Find("EnergyBar") as RectTransform;
            if (track == null)
            {
                track = HudImage("EnergyBar", fighter, new Vector2(42, 4),
                    new Vector2(0, UnitEnergyHeight), new Color(.025f, .04f, .06f, .95f)).rectTransform;
                var fill = HudImage("Fill", track, new Vector2(0, 2), Vector2.zero, new Color(.15f, .55f, 1f));
                fill.rectTransform.anchorMin = fill.rectTransform.anchorMax =
                    fill.rectTransform.pivot = new Vector2(0, .5f);
                fill.rectTransform.anchoredPosition = new Vector2(1, 0);
            }
            track.anchoredPosition=new Vector2(0,UnitBarHeight(fighter)-5);
            track.gameObject.SetActive(alive && maxEnergy > 0);
            track.GetChild(0).GetComponent<RectTransform>().sizeDelta =
                new Vector2(maxEnergy > 0 ? 40 * Mathf.Clamp01((float)(energy / maxEnergy)) : 0, 2);
            track.SetAsLastSibling();
        }

        private void RenderUnitHealthBar(Transform fighter, double hp, double maxHP, bool friendly, double shield = 0)
        {
            var track = fighter.Find("HealthBar") as RectTransform;
            if (track == null)
            {
                track = HudImage("HealthBar", fighter, new Vector2(42, 6),
                    new Vector2(0, UnitHealthHeight), new Color(.025f, .04f, .06f, .95f)).rectTransform;
                var fill = HudImage("Fill", track, new Vector2(40, 4), Vector2.zero, Color.white);
                fill.rectTransform.anchorMin = fill.rectTransform.anchorMax =
                    fill.rectTransform.pivot = new Vector2(0, .5f);
                fill.rectTransform.anchoredPosition = new Vector2(1, 0);
                var shieldFill = HudImage("Shield", track, new Vector2(0, 4), Vector2.zero, Color.white);
                shieldFill.rectTransform.anchorMin = shieldFill.rectTransform.anchorMax =
                    shieldFill.rectTransform.pivot = new Vector2(1, .5f);
                shieldFill.rectTransform.anchoredPosition = new Vector2(-1, 0);
            }

            track.anchoredPosition=new Vector2(0,UnitBarHeight(fighter));
            track.gameObject.SetActive(hp > 0);
            var image = track.GetChild(0).GetComponent<UnityEngine.UI.Image>();
            // Keep the bar width fixed, including when full health gains a shield.
            double capacity = maxHP > 0 ? System.Math.Max(maxHP, System.Math.Max(0, hp) + System.Math.Max(0, shield)) : 0;
            float ratio = capacity > 0 ? Mathf.Clamp01((float)(hp / capacity)) : 0;
            image.rectTransform.sizeDelta = new Vector2(40 * ratio, 4);
            image.color = friendly ? new Color(.25f, .9f, .5f) : new Color(1f, .38f, .34f);
            var protection = track.Find("Shield").GetComponent<UnityEngine.UI.Image>();
            protection.gameObject.SetActive(shield > 0);
            protection.rectTransform.sizeDelta = new Vector2(capacity > 0 ? 40 * Mathf.Clamp01((float)(shield / capacity)) : 0, 4);
            track.SetAsLastSibling();
        }
    }
}
