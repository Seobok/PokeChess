using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private RectTransform detailPopup, developerPanel;
        private UnityEngine.UI.Button developerToggle;
        private string hoveredUnitId;
        private bool unitInfoPinned,selectingHover;
        public bool UnitInfoPinned=>unitInfoPinned;
        private int hoveredShopSlot=-1;
        private long hoveredShopRevision;

        public string HoveredUnitId => hoveredUnitId;
        public int HoveredShopSlot => hoveredShopSlot;
        public void HoverShopUnit(int slot)
        {
            if(unitInfoPinned||IsDragging||IsItemDragging||slot<0||slot>=shopCards.Length||!ShopExpanded)return;
            selectedUnitId=null;hoveredUnitId=null;HideUnitInfoHint();hoveredShopSlot=slot;
            hoveredShopRevision=IsOnlineMatch?onlineService.LocalMatchState.shopRevision:Player.Shop.Revision;
            UpdateTablePresentation();
        }
        public void EndShopUnitHover(int slot){if(hoveredShopSlot==slot)DeselectUnit();}
        private bool RenderShopHoverInfo()
        {
            if(hoveredShopSlot<0)return false;
            long revision=IsOnlineMatch?onlineService.LocalMatchState.shopRevision:Player.Shop.Revision;
            string definitionId=IsOnlineMatch?onlineService.LocalMatchState.shop[hoveredShopSlot].definitionId:Player.Shop.Slots[hoveredShopSlot].DefinitionId;
            if(!ShopExpanded||revision!=hoveredShopRevision||string.IsNullOrEmpty(definitionId)){
                hoveredShopSlot=-1;HideUnitInfoHint();return false;
            }
            var definition=catalog.Get(definitionId);
            var stats=RankRules.Default.Apply(definition,UnitRank.One);
            int cost = IsOnlineMatch ? onlineService.LocalMatchState.shop[hoveredShopSlot].purchasePrice : shopViews[hoveredShopSlot].PurchasePrice;
            int owned = IsOnlineMatch ? onlineService.LocalMatchState.units.Count(unit => unit.definitionId == definitionId)
                : Player.Units.Count(unit => unit.DefinitionId == definitionId);
            RenderUnitInfoIcons(definition, UnitRank.One, stats, stats.MaxHP, stats.MaxHP, stats.StartingEnergy,
                stats.MaxEnergy, 0, System.Array.Empty<string>(), baseStats: true, shopCost: cost, shopOwned: owned);
            return true;
        }
        public void HoverUnit(string id)
        {
            if(synergyTargetKey!=null||unitInfoPinned||IsDragging||IsItemDragging||draggingNatureGuardian)return;
            hoveredShopSlot=-1;hoveredUnitId=id;
            selectingHover=true;try{SelectUnit(id);}finally{selectingHover=false;}
        }
        public void EndUnitHover(string id)
        {
            if(unitInfoPinned||id==null||hoveredUnitId!=id)return;
            DeselectUnit();
        }
        public bool UnitPopupVisible => detailPopup != null && detailPopup.gameObject.activeSelf;
        public bool BoardOutlineVisible => boardMarks.Any(mark => mark != null && mark.isActiveAndEnabled);
        public void DeselectUnit(){hoveredSynergyPokemon=null;unitInfoPinned=false;selectedUnitId=null;hoveredUnitId=null;hoveredShopSlot=-1;HideUnitInfoHint();UpdateTablePresentation();}
        private void Place(RectTransform rect,Vector2 size,Vector2 position)
        { rect.sizeDelta=size;rect.anchoredPosition=position; }
        private void ApplyTableLayout()
        {
            var background=canvasRect.Find("Background").gameObject.AddComponent<UnityEngine.UI.Button>();
            background.targetGraphic=background.GetComponent<UnityEngine.UI.Image>();background.transition=UnityEngine.UI.Selectable.Transition.None;
            background.onClick.AddListener(DeselectUnit);
            canvasRect.Find("BenchTitle").gameObject.SetActive(false);
            canvasRect.Find("Title").gameObject.SetActive(false);
            inventoryLabel.gameObject.SetActive(false);
            header.gameObject.SetActive(false);
            BuildSpectatorHud();
            Place(wildPreviewLabel.rectTransform,new Vector2(610,20),new Vector2(0,288));wildPreviewLabel.fontSize=10;
            combatRoot.anchoredPosition=Vector2.zero;
            foreach(Transform child in combatRoot)if(child.name.StartsWith("CombatCell_"))child.gameObject.SetActive(false);
            Place(combatInfo.rectTransform,new Vector2(420,20),new Vector2(0,267));

            detailLabel.gameObject.SetActive(false);
            BindUnitInfoIcons();

            foreach (var label in shopFrame.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                if (label.font == null) label.font = itemFont;
            developerPanel=Panel("DeveloperTools",new Vector2(240,475),new Vector2(510,70));
            string[] names={"DebugTitle","DebugState","P01-P04 demo","P05-P08 demo","P09-P12 demo","Reset match","Rank demo","Full bench","Fixed foe","4 players","6 players","8 players","3 players","5 players","7 players","DEV: Start battle","Surrender","Synergy demo","Auto play 20","Fast simulation","Save report"};
            int index=0;
            foreach(var name in names){
                var node=canvasRect.Find(name);if(node==null)continue;
                node.SetParent(developerPanel,false);Place((RectTransform)node,new Vector2(215,20),new Vector2(0,220-index++*22));
            }
            foreach(var button in new[]{SimulationStartButton,SimulationSpeedButton}){button.transform.SetParent(developerPanel,false);Place((RectTransform)button.transform,new Vector2(215,20),new Vector2(0,220-index++*22));}
            developerPanel.sizeDelta=new Vector2(240,Mathf.Max(475,index*22+20));
            developerPanel.gameObject.SetActive(false);
            developerToggle=ActionButton("DEV",new Vector2(45,22),new Vector2(374,332),()=>{developerPanel.gameObject.SetActive(!developerPanel.gameObject.activeSelf);developerPanel.SetAsLastSibling();});
        }
        private void UpdateTablePresentation()
        {
            if(detailPopup==null)return;
            if(selectedUnitId!=null && Mouse.current?.leftButton.wasPressedThisFrame==true && !IsDragging&&!IsItemDragging){
                var data=new PointerEventData(EventSystem.current){position=Mouse.current.position.ReadValue()};
                var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
                bool keep=hits.Count>0&&(hits[0].gameObject.transform.IsChildOf(detailPopup)||hits[0].gameObject.GetComponentInParent<PlacementDragToken>()!=null||hits[0].gameObject.transform.IsChildOf(combatRoot)&&hits[0].gameObject.GetComponentInParent<UnityEngine.UI.Button>()!=null);
                if(!keep){unitInfoPinned=false;selectedUnitId=null;hoveredUnitId=null;HideUnitInfoHint();}
            }
            if(Keyboard.current?.escapeKey.wasPressedThisFrame==true){hoveredSynergyPokemon=null;unitInfoPinned=false;selectedUnitId=null;hoveredShopSlot=-1;}
            if(IsDragging||IsItemDragging||selectedUnitId!=hoveredUnitId){
                unitInfoPinned=false;hoveredUnitId=null;hoveredShopSlot=-1;HideUnitInfoHint();
            }
            if (IsDragging || IsItemDragging) hoveredSynergyPokemon = null;
            bool synergyInfo = RenderSynergyPokemonInfo();
            bool shopInfo = !synergyInfo && RenderShopHoverInfo();
            sellButton.gameObject.SetActive(!shopInfo && !synergyInfo);
            detailPopup.gameObject.SetActive(synergyInfo||shopInfo||hoveredUnitId!=null&&selectedUnitId==hoveredUnitId);
            if(detailPopup.gameObject.activeSelf && equippedRecipeSource == null)detailPopup.SetAsLastSibling();
            if (equippedRecipeSource != null && !equippedRecipeSource.gameObject.activeInHierarchy) HideItemHint();
            RenderPlacementArt();
            header.gameObject.SetActive(false);status.gameObject.SetActive(false);inventoryLabel.gameObject.SetActive(false);detailLabel.gameObject.SetActive(false);
            hudNotice.gameObject.SetActive(false);wildPreviewLabel.gameObject.SetActive(false);
        }
    }
}










