using UnityEngine;
using System.Collections.Generic;
using PokeChess.Core.Match;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private void RenderEquippedItemIcon(Transform unit, IEnumerable<string> items, bool visible=true, float height=33)
        {
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
                    new Vector2(0, 13), new Color(.025f, .04f, .06f, .95f)).rectTransform;
                var fill = HudImage("Fill", track, new Vector2(0, 2), Vector2.zero, new Color(.15f, .55f, 1f));
                fill.rectTransform.anchorMin = fill.rectTransform.anchorMax =
                    fill.rectTransform.pivot = new Vector2(0, .5f);
                fill.rectTransform.anchoredPosition = new Vector2(1, 0);
            }
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
                    new Vector2(0, 18), new Color(.025f, .04f, .06f, .95f)).rectTransform;
                var fill = HudImage("Fill", track, new Vector2(40, 4), Vector2.zero, Color.white);
                fill.rectTransform.anchorMin = fill.rectTransform.anchorMax =
                    fill.rectTransform.pivot = new Vector2(0, .5f);
                fill.rectTransform.anchoredPosition = new Vector2(1, 0);
                var shieldFill = HudImage("Shield", track, new Vector2(0, 4), Vector2.zero, Color.white);
                shieldFill.rectTransform.anchorMin = shieldFill.rectTransform.anchorMax =
                    shieldFill.rectTransform.pivot = new Vector2(1, .5f);
                shieldFill.rectTransform.anchoredPosition = new Vector2(-1, 0);
            }

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
