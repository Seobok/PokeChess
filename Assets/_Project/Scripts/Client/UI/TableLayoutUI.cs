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
        private readonly List<PlacementHexGraphic> tableHexes = new List<PlacementHexGraphic>();
        private string hoveredUnitId;
        private int hoveredShopSlot=-1;
        private long hoveredShopRevision;

        public string HoveredUnitId => hoveredUnitId;
        public int HoveredShopSlot => hoveredShopSlot;
        public void HoverShopUnit(int slot)
        {
            if(IsDragging||IsItemDragging||slot<0||slot>=shopCards.Length||!ShopExpanded)return;
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
            var stats=RankRules.Default.Apply(definition.BaseStats,UnitRank.One);
            RenderUnitInfoIcons(definition,UnitRank.One,stats,stats.MaxHP,stats.MaxHP,stats.StartingEnergy,stats.MaxEnergy,0,System.Array.Empty<string>());
            return true;
        }
        public void HoverUnit(string id)
        {
            if(IsDragging||IsItemDragging)return;
            hoveredShopSlot=-1;hoveredUnitId=id;
            SelectUnit(id);
        }
        public void EndUnitHover(string id)
        {
            if(id==null||hoveredUnitId!=id)return;
            DeselectUnit();
        }
        public bool UnitPopupVisible => detailPopup != null && detailPopup.gameObject.activeSelf;
        public bool BoardOutlineVisible => tableHexes.Any(h=>h.gameObject.activeInHierarchy);
        public void DeselectUnit(){selectedUnitId=null;hoveredUnitId=null;hoveredShopSlot=-1;HideUnitInfoHint();UpdateTablePresentation();}
        private static Vector2 TablePoint(BoardPosition p) => new Vector2(-252+p.Column*72+(p.Row%2)*36,-70+p.Row*43);
        private void Place(RectTransform rect,Vector2 size,Vector2 position)
        { rect.sizeDelta=size;rect.anchoredPosition=position; }
        private void ApplyTableLayout()
        {
            var background=canvasRect.Find("Background").gameObject.AddComponent<UnityEngine.UI.Button>();
            background.targetGraphic=background.GetComponent<UnityEngine.UI.Image>();background.transition=UnityEngine.UI.Selectable.Transition.None;
            background.onClick.AddListener(DeselectUnit);
            var arena=HudImage("ArenaBackground",canvasRect,new Vector2(610,410),new Vector2(-18,96),new Color(.075f,.135f,.145f));
            arena.transform.SetSiblingIndex(1);
            for(int row=0;row<8;row++)for(int col=0;col<7;col++){
                var slot=slots.FirstOrDefault(s=>s.Placement.Kind==PlacementKind.Board&&s.Placement.Position.Value.Equals(new BoardPosition(col,row)));
                var rect=slot?.Rect??Rect("BoardOutline_"+col+"_"+row,canvasRect,new Vector2(68,58),TablePoint(new BoardPosition(col,row)));
                Place(rect,new Vector2(68,58),TablePoint(new BoardPosition(col,row)));
                var hex=rect.GetComponent<PlacementHexGraphic>()??rect.gameObject.AddComponent<PlacementHexGraphic>();
                hex.OutlineOnly=true;hex.raycastTarget=false;hex.color=new Color(.35f,.62f,.65f,.6f);tableHexes.Add(hex);
                foreach(Transform child in rect)child.gameObject.SetActive(false);
            }
            foreach(var slot in slots.Where(s=>s.Placement.Kind==PlacementKind.Bench)){
                Place(slot.Rect,new Vector2(62,58),new Vector2(-280+slot.Placement.BenchSlot.Value*70,-142));
                slot.Graphic.color=slot.Color=new Color(.13f,.21f,.24f);
                foreach(Transform child in slot.Rect)child.gameObject.SetActive(false);
            }
            canvasRect.Find("BenchTitle").gameObject.SetActive(false);
            canvasRect.Find("Title").gameObject.SetActive(false);
            canvasRect.Find("Inventory").gameObject.SetActive(false);
            inventoryLabel.gameObject.SetActive(false);
            header.gameObject.SetActive(false);
            Place(sidebar,new Vector2(205,334),new Vector2(522,136));
            Place(hudRound.rectTransform.parent as RectTransform,new Vector2(230,30),new Vector2(0,333));
            Place(hudRound.rectTransform,new Vector2(220,28),Vector2.zero);hudRound.fontSize=16;
            Place(hudTimerBorder.rectTransform,new Vector2(286,10),new Vector2(0,310));
            Place(hudNotice.rectTransform,new Vector2(600,20),new Vector2(0,288));
            Place(wildPreviewLabel.rectTransform,new Vector2(610,20),new Vector2(0,288));wildPreviewLabel.fontSize=10;
            combatRoot.anchoredPosition=Vector2.zero;
            foreach(Transform child in combatRoot)if(child.name.StartsWith("CombatCell_"))child.gameObject.SetActive(false);
            Place(combatInfo.rectTransform,new Vector2(420,20),new Vector2(0,267));

            detailPopup=Panel("UnitPopup",new Vector2(215,350),new Vector2(522,128));
            var old=canvasRect.Find("UnitDetail");old.gameObject.SetActive(false);
            detailLabel.gameObject.SetActive(false);
            BuildUnitInfoIcons();
            unitInfoRoot.SetParent(detailPopup,false);unitInfoRoot.anchoredPosition=new Vector2(0,40);
            sellButton.transform.SetParent(detailPopup,false);Place((RectTransform)sellButton.transform,new Vector2(180,30),new Vector2(0,-140));
            detailPopup.gameObject.SetActive(false);

            shopFrame.sizeDelta=new Vector2(1240,ShopHeight);shopFrame.anchoredPosition=new Vector2(0,10);
            Place((RectTransform)xpButton.transform,new Vector2(130,36),new Vector2(-540,82));
            Place((RectTransform)rerollButton.transform,new Vector2(130,36),new Vector2(-540,38));
            Place((RectTransform)lockButton.transform,new Vector2(58,26),new Vector2(570,137));
            Place((RectTransform)shopToggle.transform,new Vector2(68,24),new Vector2(500,137));
            Place(economyGold.rectTransform,new Vector2(120,25),new Vector2(360,123));economyGold.fontSize=18;
            Place(economyLevel.rectTransform,new Vector2(240,24),new Vector2(-420,128));economyLevel.fontSize=12;
            Place(economyXP.rectTransform.parent as RectTransform,new Vector2(200,4),new Vector2(-420,112));
            Place(economyDelta.rectTransform,new Vector2(250,18),new Vector2(110,123));
            economyLock.gameObject.SetActive(false);economyStreak.gameObject.SetActive(false);evolutionPointsLabel.gameObject.SetActive(false);
            status.gameObject.SetActive(false);
            for(int i=0;i<shopCards.Length;i++){
                Place((RectTransform)shopCards[i].transform,new Vector2(180,96),new Vector2(-365+i*188,57));
                var shopHover=shopCards[i].gameObject.AddComponent<UnitHoverTarget>();shopHover.View=this;shopHover.ShopSlot=i;
                var c=shopViews[i];Place(c.Name.rectTransform,new Vector2(124,20),new Vector2(21,30));
                Place(c.Price.rectTransform,new Vector2(90,18),new Vector2(33,9));
                Place(c.Types.rectTransform,new Vector2(168,16),new Vector2(0,-10));
                Place(c.Owned.rectTransform,new Vector2(168,14),new Vector2(0,-24));
                Place(c.Action.rectTransform,new Vector2(168,16),new Vector2(0,-39));
                Place(c.Stripe.rectTransform,new Vector2(4,88),new Vector2(-87,0));
            }
            foreach(Transform child in shopFrame){var rect=child as RectTransform;if(rect!=null)rect.anchorMin=rect.anchorMax=new Vector2(.5f,0);}
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
            developerToggle=ActionButton("DEV",new Vector2(45,22),new Vector2(590,332),()=>{developerPanel.gameObject.SetActive(!developerPanel.gameObject.activeSelf);developerPanel.SetAsLastSibling();});
        }
        private void UpdateTablePresentation()
        {
            if(detailPopup==null)return;
            if(selectedUnitId!=null && Mouse.current?.leftButton.wasPressedThisFrame==true && !IsDragging&&!IsItemDragging){
                var data=new PointerEventData(EventSystem.current){position=Mouse.current.position.ReadValue()};
                var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
                bool keep=hits.Count>0&&(hits[0].gameObject.transform.IsChildOf(detailPopup)||hits[0].gameObject.GetComponentInParent<PlacementDragToken>()!=null||hits[0].gameObject.transform.IsChildOf(combatRoot)&&hits[0].gameObject.GetComponentInParent<UnityEngine.UI.Button>()!=null);
                if(!keep){selectedUnitId=null;HideUnitInfoHint();}
            }
            if(Keyboard.current?.escapeKey.wasPressedThisFrame==true){selectedUnitId=null;hoveredShopSlot=-1;}
            if(IsDragging||IsItemDragging||selectedUnitId!=hoveredUnitId){
                hoveredUnitId=null;hoveredShopSlot=-1;HideUnitInfoHint();
            }
            bool shopInfo=RenderShopHoverInfo();
            sellButton.gameObject.SetActive(!shopInfo);
            detailPopup.gameObject.SetActive(shopInfo||hoveredUnitId!=null&&selectedUnitId==hoveredUnitId);
            if(detailPopup.gameObject.activeSelf)detailPopup.SetAsLastSibling();
            bool outline=selectedUnitId!=null||IsDragging||IsItemDragging;
            foreach(var hex in tableHexes)hex.gameObject.SetActive(outline);
            foreach(var slot in slots.Where(s=>s.Placement.Kind==PlacementKind.Bench))slot.Rect.gameObject.SetActive(true);
            header.gameObject.SetActive(false);status.gameObject.SetActive(false);inventoryLabel.gameObject.SetActive(false);detailLabel.gameObject.SetActive(false);
            economyLock.gameObject.SetActive(false);economyStreak.gameObject.SetActive(false);evolutionPointsLabel.gameObject.SetActive(false);
            ButtonText(shopToggle,ShopExpanded?"SHOP ▾":"SHOP ▴");shopToggle.GetComponentInChildren<UnityEngine.UI.Text>().fontSize=11;
            ButtonText(lockButton,(IsOnlineMatch?onlineService.LocalMatchState?.locked==true:Player.Shop.IsLocked)?"LOCK":"OPEN");
            hudNotice.gameObject.SetActive(true);wildPreviewLabel.gameObject.SetActive(false);
            foreach(var slot in slots.Where(s=>s.Placement.Kind==PlacementKind.Board))if(!IsDragging&&!IsItemDragging)slot.Graphic.color=new Color(.35f,.62f,.65f,.6f);
            if(!IsDragging)foreach(var hex in tableHexes)hex.color=new Color(.35f,.62f,.65f,.6f);
            if(IsDragging)ShadeUnavailablePlacements();
        }
        private void ShadeUnavailablePlacements()
        {
            var blocked=new Color(.95f,.24f,.27f,.85f);
            var available=new Color(.35f,.72f,.7f,.7f);
            var onlineUnit=IsOnlineMatch?onlineService.LocalMatchState?.units.FirstOrDefault(u=>u.id==dragged):null;
            for(int i=0;i<tableHexes.Count;i++){
                var destination=UnitPlacement.OnBoard(new BoardPosition(i%7,i/7));
                bool valid=i/7<4&&(IsOnlineMatch?CanOnlinePlace(onlineUnit,destination):placements.Preview(Match,"p1",dragged,destination,dragRevision).Accepted);
                if(valid&&IsOnlineMatch&&onlineUnit.placement==PlacementKind.Bench){
                    var owner=onlineService.LocalMatchState;
                    valid=owner.units.Count(u=>u.placement==PlacementKind.Board)<owner.level||owner.units.Any(u=>u.placement==PlacementKind.Board&&u.column==i%7&&u.row==i/7);
                }
                tableHexes[i].color=valid?available:blocked;
            }
            foreach(var slot in slots.Where(s=>s.Placement.Kind==PlacementKind.Bench)){
                bool valid=IsOnlineMatch?CanOnlinePlace(onlineUnit,slot.Placement):placements.Preview(Match,"p1",dragged,slot.Placement,dragRevision).Accepted;
                slot.Graphic.color=valid?slot.Color:blocked;
            }
        }
    }
}








