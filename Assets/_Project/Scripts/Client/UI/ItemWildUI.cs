using System;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private RectTransform itemRewardPanel,itemInventoryPanel,itemDragGhost,itemTooltip;
        private UnityEngine.UI.Text itemTooltipLabel;
        private UnityEngine.UI.Text wildPreviewLabel,itemInventoryTitle;
        private readonly UnityEngine.UI.Button[] itemChoices=new UnityEngine.UI.Button[3];
        private readonly UnityEngine.UI.Button[] inventoryChoices=new UnityEngine.UI.Button[ToolsPerPage + 1];
        private string[] visibleInventory=Array.Empty<string>();

        private object itemSlotOwner;
        private ItemIconLibrary itemIcons;
        private string draggedItem;
        private long draggedItemRevision;
        private const string EvolutionDragId="evolution-point";
        private long draggedEvolutionRevision,draggedEvolutionPlacementRevision;
        private int InventoryIndex(int slot)=>slot == 0 ? -1 : inventoryPage * ToolsPerPage + slot - 1;
        private int CurrentEvolutionPoints=>IsOnlineMatch?onlineService.LocalMatchState?.evolutionPoints??0:Player.EvolutionPoints;
        private long displayedItemRevision;
        private Font itemFont;
        public bool ItemRewardVisible=>itemRewardPanel!=null&&itemRewardPanel.gameObject.activeSelf;
        public UnityEngine.UI.Button ItemChoiceButton(int slot)=>itemChoices[slot];
        public UnityEngine.UI.Button InventoryItemButton(int slot)=>inventoryChoices[slot];
        private UnityEngine.UI.Text ItemLabel(string name,Transform parent,string value,int size,Vector2 dimensions,Vector2 position)
        {var label=Label(name,parent,value,size,dimensions,position);label.font=itemFont;return label;}
        private UnityEngine.UI.Button ItemButton(string name,RectTransform parent,Vector2 size,Vector2 position,Action action)
        {
            var rect=Rect(name,parent,size,position);var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.14f,.25f,.34f);
            var button=rect.gameObject.AddComponent<UnityEngine.UI.Button>();button.targetGraphic=image;button.onClick.AddListener(()=>action());
            ItemLabel("Label",rect,name,13,size-new Vector2(12,8),Vector2.zero);return button;
        }
        private void BuildItemWildUI()
        {
            itemFont=Font.CreateDynamicFontFromOSFont(new[]{"Malgun Gothic","Arial Unicode MS","Arial"},14);
            wildPreviewLabel=ItemLabel("WildPreview",canvasRect,"",12,new Vector2(720,22),new Vector2(0,285));
            itemIcons=Resources.Load<ItemIconLibrary>("ItemIcons");
            inventoryLabel.rectTransform.sizeDelta=new Vector2(195,20);
            inventoryLabel.rectTransform.anchoredPosition=new Vector2(-510,-53);
            BuildInventoryArt();
            itemDragGhost=Rect("ItemDragPreview",canvasRect,new Vector2(40,40),Vector2.zero);
            var ghostIcon=itemDragGhost.gameObject.AddComponent<UnityEngine.UI.Image>();ghostIcon.preserveAspect=true;ghostIcon.raycastTarget=false;
            itemDragGhost.gameObject.SetActive(false);
            BuildEvolutionDragPreview();
            itemTooltip=Panel("ItemTooltip",new Vector2(275,80),new Vector2(-260,-45));
            itemTooltip.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
            itemTooltipLabel=ItemLabel("Description",itemTooltip,"",12,new Vector2(255,70),Vector2.zero);
            itemTooltip.gameObject.SetActive(false);
            BuildWildRewardUI();
        }
        private void ChooseItemReward(int slot)
        {
            if(IsOnlineMatch){SendOnlineCommand(MatchCommandKind.ChooseItem,c=>{c.slot=slot;c.itemRevision=displayedItemRevision;});return;}
            Feedback(commands.ChooseItem(slot,displayedItemRevision).Message);Render();
        }
        public bool IsItemDragging=>draggedItem!=null;
        public void ShowItemHint(int slot)
        {
            if(IsItemDragging||slot<0||slot>=inventoryChoices.Length)return;
            if(slot==0){itemTooltipLabel.text="진화 포인트 · "+CurrentEvolutionPoints+" EP\n내 유닛에 드래그 · 필드·대기열의 같은 종류도 진화\n한 단계당 1 EP · 상점의 같은 계열도 즉시 진화";itemTooltip.gameObject.SetActive(true);itemTooltip.SetAsLastSibling();return;}
            int index=InventoryIndex(slot);if(index<0||index>=visibleInventory.Length)return;
            string key=ItemCatalog.DefinitionId(visibleInventory[index]);if(key==null)return;
            var definition=ItemCatalog.Get(key);itemTooltipLabel.text=definition.Name+"\n"+definition.Description;
            itemTooltip.gameObject.SetActive(true);itemTooltip.SetAsLastSibling();
        }
        public void HideItemHint(){if(itemTooltip!=null)itemTooltip.gameObject.SetActive(false);}
        public void BeginItemDrag(int slot,Vector2 point)
        {
            if(IsDragging||IsEarthRockDragging||draggingNatureGuardian||!CanDragItems()||slot<0||slot>=inventoryChoices.Length||!inventoryChoices[slot].interactable)return;
            if(slot==0){
                if(CurrentEvolutionPoints<=0)return;
                draggedItem=EvolutionDragId;
                draggedEvolutionRevision=IsOnlineMatch?onlineService.LocalMatchState.evolutionRevision:Player.EvolutionRevision;
                draggedEvolutionPlacementRevision=IsOnlineMatch?onlineService.LocalMatchState.placementRevision:Player.PlacementRevision;
            }
            else{
                int index=InventoryIndex(slot);if(index<0||index>=visibleInventory.Length)return;
                if(ItemCatalog.DefinitionId(visibleInventory[index])==null)return;
                draggedItem=visibleInventory[index];draggedItemRevision=displayedItemRevision;
            }
            HideItemHint();
            itemDragGhost.GetComponent<UnityEngine.UI.Image>().sprite=draggedItem==EvolutionDragId?inventoryMoonStone:itemIcons?.Get(ItemCatalog.DefinitionId(draggedItem));
            itemDragGhost.gameObject.SetActive(true);itemDragGhost.SetAsLastSibling();
            if (draggedItem == EvolutionDragId && ItemPhase == MatchPhase.Preparation) BeginEvolutionHighlights();
            DragItem(point);
        }
        private MatchPhase? ItemPhase => IsOnlineMatch ? onlineService.LocalMatchState?.phase : Match?.Phase;
        private bool CanDragItems() => CanDragItemsInPhase(ItemPhase);
        private bool CanDragItemsInPhase(MatchPhase? phase) =>
            (phase == MatchPhase.Preparation || phase == MatchPhase.Combat) && (IsOnlineMatch
            ? onlineService.WatchingPlayerId==onlineService.LocalMatchState?.playerId&&OnlineInputReady&&!onlineService.LocalMatchState.eliminated&&onlineService.LocalMatchState.hp>0
            : localViewedPlayerId=="p1"&&Match!=null&&!busy&&!RoundLoop.NextPreparationPending&&Simulation?.Status!=SimulationStatus.Running&&!Player.IsEliminated&&Player.HP>0);

        private string ItemTarget(Vector2 point)
        {
            var slot = Hit(point);
            if (ItemPhase != MatchPhase.Combat || slot?.Placement.Kind == PlacementKind.Bench)
                return ItemTarget(slot);

            if (combatRoot == null || !combatRoot.gameObject.activeInHierarchy) return null;
            for (int i = combatRoot.childCount - 1; i >= 0; i--)
            {
                var fighter = combatRoot.GetChild(i) as RectTransform;
                if (fighter == null || !fighter.gameObject.activeInHierarchy ||
                    !fighter.name.StartsWith("Fighter_", StringComparison.Ordinal) ||
                    !RectTransformUtility.RectangleContainsScreenPoint(fighter, point, null)) continue;

                string id = fighter.name.Substring("Fighter_".Length);
                bool owned = IsOnlineMatch
                    ? onlineService.LocalMatchState.units.Any(unit => unit.id == id && unit.placement == PlacementKind.Board)
                    : Player.Units.Any(unit => unit.InstanceId == id && unit.Placement.Kind == PlacementKind.Board);
                if (owned) return id;
            }
            return null;
        }
        private string ItemTarget(Slot slot)
        {
            if (slot == null) return null;
            if(IsOnlineMatch)return onlineService.LocalMatchState?.units.FirstOrDefault(u=>u.placement==slot.Placement.Kind&&(u.placement==PlacementKind.Board?u.column==slot.Placement.Position.Value.Column&&u.row==slot.Placement.Position.Value.Row:u.bench==slot.Placement.BenchSlot))?.id;
            return Player.Units.FirstOrDefault(u=>u.Placement.Kind==slot.Placement.Kind&&Nullable.Equals(u.Placement.Position,slot.Placement.Position)&&u.Placement.BenchSlot==slot.Placement.BenchSlot)?.InstanceId;
        }
        public void DragItem(Vector2 point)
        {
            if(!IsItemDragging)return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,point,null,out var local);itemDragGhost.anchoredPosition=local;
            var destination = Hit(point);
            if (draggedItem == EvolutionDragId && ItemPhase == MatchPhase.Preparation)
            {
                UpdateEvolutionHover(EvolutionSlotAt(point, destination));
                return;
            }
            foreach (var slot in slots) slot.Graphic.color = BaseSlotColor(slot);
            if (destination != null)
                destination.Graphic.color = ItemTarget(point) != null ? new Color(.08f,.36f,.29f) : new Color(.48f,.12f,.17f);
        }
        public void EndItemDrag(Vector2 point)
        {
            if(!IsItemDragging)return;
            string id = draggedItem;
            string target = null;
            if (CanDragItems())
                target = id == EvolutionDragId && ItemPhase == MatchPhase.Preparation
                    ? ItemTarget(EvolutionSlotAt(point, Hit(point))) : ItemTarget(point);
            long revision = draggedItemRevision;
            CancelItemDrag();
            if(target==null){Feedback((id==EvolutionDragId?"진화":"장착")+" 취소: 내 유닛 위에 놓으세요.");Render();return;}
            selectedUnitId=target;
            if(id==EvolutionDragId){
                if(!CanEvolveTarget(target)){Feedback("진화 취소: 진화 가능한 내 유닛 위에 놓으세요.");Render();return;}
                if(IsOnlineMatch){SendOnlineCommand(MatchCommandKind.Evolve,c=>{c.unitId=target;c.evolutionRevision=draggedEvolutionRevision;c.placementRevision=draggedEvolutionPlacementRevision;});return;}
                Perform(()=>commands.Evolve(target,draggedEvolutionRevision,draggedEvolutionPlacementRevision));return;
            }
            if(IsOnlineMatch){SendOnlineCommand(MatchCommandKind.EquipItem,c=>{c.unitId=target;c.itemId=id;c.itemRevision=revision;});return;}
            Feedback(commands.EquipItem(target,id,revision).Message);Render();
        }
        private void CancelItemDrag()
        {
            draggedItem=null;if(itemDragGhost!=null)itemDragGhost.gameObject.SetActive(false);
            ClearEvolutionHighlights();
            if(Match!=null)foreach(var slot in slots)slot.Graphic.color=BaseSlotColor(slot);
        }
        private void RenderItemWildUI()
        {
            if(itemRewardPanel==null)return;
            var owner=IsOnlineMatch?onlineService?.LocalMatchState:null;
            var pub=IsOnlineMatch?onlineService?.PublicState:null;
            if(IsOnlineMatch&&(owner==null||pub==null)){
                RenderWildRewardUI(false, false, null);wildPreviewLabel.gameObject.SetActive(false);itemInventoryPanel.gameObject.SetActive(false);CancelItemDrag();return;
            }
            var phase=IsOnlineMatch?pub.phase:Match.Phase;
            bool wild=IsOnlineMatch?pub.wildRound:WildRoundSystem.IsWild(Match);
            bool alive=IsOnlineMatch?!owner.eliminated&&owner.hp>0:!Player.IsEliminated&&Player.HP>0;
            bool ready=IsOnlineMatch?OnlineInputReady:Simulation?.Status!=SimulationStatus.Running&&!busy;
            var candidates=IsOnlineMatch?owner.itemCandidates??Array.Empty<string>():Player.PendingItemReward?.Candidates.ToArray()??Array.Empty<string>();
            displayedItemRevision=IsOnlineMatch?owner.itemRevision:Player.ItemRevision;
            visibleInventory=IsOnlineMatch?owner.inventory??Array.Empty<string>():Player.ItemInventory.ToArray();
            object slotOwner = IsOnlineMatch ? (object)onlineMatchId : Match;
            if (!Equals(itemSlotOwner, slotOwner))
            {
                inventoryPage = 0;
                itemSlotOwner = slotOwner;
            }
            int pageCount = Mathf.Max(1, (visibleInventory.Length + ToolsPerPage - 1) / ToolsPerPage);
            inventoryPage = Mathf.Clamp(inventoryPage, 0, pageCount - 1);
            bool selecting=alive&&candidates.Length==3&&phase==MatchPhase.Preparation;
            RenderWildRewardUI(selecting, ready, candidates);
            wildPreviewLabel.gameObject.SetActive(wild&&phase==MatchPhase.Preparation);
            hudNotice.gameObject.SetActive(!(wild&&phase==MatchPhase.Preparation));
            wildPreviewLabel.text="야생 라운드 · "+(IsOnlineMatch?pub.wildPreview:WildRoundSystem.Preview(Match.RoundNumber));
            itemInventoryPanel.gameObject.SetActive(phase!=MatchPhase.Finished);
            inventoryLabel.text="ITEMS ("+visibleInventory.Length+")";
            if(IsItemDragging&&(!CanDragItems()||(draggedItem==EvolutionDragId
                ? CurrentEvolutionPoints<=0||(IsOnlineMatch?owner.evolutionRevision:Player.EvolutionRevision)!=draggedEvolutionRevision||(IsOnlineMatch?owner.placementRevision:Player.PlacementRevision)!=draggedEvolutionPlacementRevision
                : displayedItemRevision!=draggedItemRevision||!visibleInventory.Contains(draggedItem))))CancelItemDrag();
            itemInventoryTitle.text = $"보유 {CurrentEvolutionPoints} EP";
            inventoryPageText.text = $"{inventoryPage + 1} / {pageCount}";
            inventoryPrev.interactable = !IsItemDragging && inventoryPage > 0;
            inventoryNext.interactable = !IsItemDragging && inventoryPage + 1 < pageCount;
            bool canUseTools = CanDragItems() && !selecting;
            for (int i = 0; i < inventoryChoices.Length; i++)
            {
                var button = inventoryChoices[i];
                var icon = button.transform.Find("Icon").GetComponent<UnityEngine.UI.Image>();
                if (i == 0)
                {
                    icon.sprite = inventoryMoonStone;
                    icon.enabled = true;
                    button.transform.Find("Count").GetComponent<UnityEngine.UI.Text>().text = $"{CurrentEvolutionPoints} EP";
                    button.interactable = canUseTools && CurrentEvolutionPoints > 0;
                    continue;
                }
                int index = InventoryIndex(i);
                string key = index < visibleInventory.Length ? ItemCatalog.DefinitionId(visibleInventory[index]) : null;
                icon.sprite = key != null ? itemIcons?.Get(key) : null;
                icon.enabled = icon.sprite != null;
                button.GetComponent<UnityEngine.UI.Image>().sprite = key == null ? inventoryEmptySlot : inventoryToolSlot;
                button.interactable = canUseTools && key != null;
            }
            if(IsItemDragging)itemDragGhost.SetAsLastSibling();
        }
        private bool CanEvolveTarget(string id)
        {
            if(!CanDragItems()||CurrentEvolutionPoints<=0)return false;
            if(!IsOnlineMatch)return commands.PreviewEvolution(id,draggedEvolutionRevision,draggedEvolutionPlacementRevision).Accepted;
            var unit=onlineService.LocalMatchState.units.FirstOrDefault(u=>u.id==id);
            var owner = onlineService.LocalMatchState;
            if (unit == null || owner.evolutionRevision != draggedEvolutionRevision ||
                owner.placementRevision != draggedEvolutionPlacementRevision ||
                unit.placement != PlacementKind.Board && unit.placement != PlacementKind.Bench) return false;
            var options = catalog.Get(unit.definitionId).Evolutions;
            return options.Count == 1 && owner.evolutionPoints >= options[0].EvolutionPointCost;
        }
    }
}






