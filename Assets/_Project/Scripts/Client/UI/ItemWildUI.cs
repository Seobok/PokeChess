using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private RectTransform itemInventoryPanel,itemDragGhost,itemTooltip;
        private RectTransform itemRecipePreview;
        private UnityEngine.UI.Image itemRecipeIcon;
        private UnityEngine.UI.Text itemRecipeName, itemRecipeDescription;
        private UnityEngine.UI.Text itemTooltipLabel, itemDragName;
        private UnityEngine.UI.Text wildPreviewLabel;
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
        public UnityEngine.UI.Button InventoryItemButton(int slot)=>inventoryChoices[slot];
        private UnityEngine.UI.Text ItemLabel(string name,Transform parent,string value,int size,Vector2 dimensions,Vector2 position)
        {var label=Label(name,parent,value,size,dimensions,position);label.font=itemFont;return label;}
        private void BuildItemWildUI()
        {
            itemFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial Unicode MS", "Arial" }, 14);
            wildPreviewLabel = ItemLabel("WildPreview", canvasRect, "", 12, new Vector2(720, 22), new Vector2(0, 285));
            itemIcons = Resources.Load<ItemIconLibrary>("ItemIcons");

            BindInventoryArt();
            itemDragGhost = UiPrefab.Instantiate("ItemDragPreview", canvasRect, itemFont);
            itemDragName = UiPrefab.Get<UnityEngine.UI.Text>(itemDragGhost, "ItemName");
            BindEvolutionDragPreview();
            itemTooltip = UiPrefab.Instantiate("ItemTooltip", canvasRect, itemFont);
            itemTooltipLabel = UiPrefab.Get<UnityEngine.UI.Text>(itemTooltip, "Description");
            itemRecipePreview = UiPrefab.Instantiate("ItemRecipePreview", canvasRect, itemFont);
            itemRecipeIcon = UiPrefab.Get<UnityEngine.UI.Image>(itemRecipePreview, "ResultIcon");
            itemRecipeName = UiPrefab.Get<UnityEngine.UI.Text>(itemRecipePreview, "ResultName");
            itemRecipeDescription = UiPrefab.Get<UnityEngine.UI.Text>(itemRecipePreview, "Description");
            BuildItemDetailSections();
            BuildComponentRecipeHint();
        }

        public bool IsItemDragging=>draggedItem!=null;
        public void ShowItemHint(int slot)
        {
            if(IsItemDragging||slot<0||slot>=inventoryChoices.Length)return;
            HideItemHint();
            if(slot==0){itemTooltipLabel.text="진화 포인트 · "+CurrentEvolutionPoints+" EP\n내 유닛에 드래그 · 필드·대기열의 같은 종류도 진화\n한 단계당 1 EP · 상점의 같은 계열도 즉시 진화";itemTooltip.gameObject.SetActive(true);itemTooltip.SetAsLastSibling();return;}
            int index=InventoryIndex(slot);if(index<0||index>=visibleInventory.Length)return;
            string key=ItemCatalog.DefinitionId(visibleInventory[index]);if(key==null)return;
            var definition = ItemCatalog.Get(key);
            if (definition.IsComponent)
            {
                ShowComponentRecipeHint(definition, slot, visibleInventory[index]);
                return;
            }
            var point = (Vector2)canvasRect.InverseTransformPoint(inventoryChoices[slot].transform.position);
            UpdateItemRecipePreview(definition, point);
            hoveredComponentItem = visibleInventory[index];
            hoveredComponentSlot = slot;
            itemRecipePreview.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
        }
        public void HideItemHint()
        {
            if (itemTooltip != null) itemTooltip.gameObject.SetActive(false);
            if (itemRecipePreview != null) itemRecipePreview.gameObject.SetActive(false);
            if (hoveredComponentItem == null) return;
            if (hoveredUnitItem == hoveredComponentItem) hoveredUnitItem = null;
            hoveredComponentItem = null;
            equippedRecipeSource = null;
            componentRecipes.gameObject.SetActive(false);
            itemRecipePreview.gameObject.SetActive(false);
        }
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
            var ghostIcon = itemDragGhost.GetComponent<UnityEngine.UI.Image>();
            ghostIcon.sprite = draggedItem == EvolutionDragId ? inventoryMoonStone : itemIcons?.Get(ItemCatalog.DefinitionId(draggedItem));
            ghostIcon.color = ghostIcon.sprite != null ? Color.white : new Color(.92f, .94f, .98f);
            itemDragName.text = ghostIcon.sprite == null ? ItemCatalog.DisplayName(draggedItem) : "";
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
        private int HoveredInventorySlot(Vector2 point)
        {
            if (!itemInventoryPanel.gameObject.activeInHierarchy) return -1;
            for (int slot = 0; slot < inventoryChoices.Length; slot++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)inventoryChoices[slot].transform, point, null)) return slot;
            }
            return -1;
        }

        private string InventoryItemAt(int slot)
        {
            int index = InventoryIndex(slot);
            return index >= 0 && index < visibleInventory.Length ? visibleInventory[index] : null;
        }

        private ItemDefinition PreviewRecipe(int hoveredSlot, string unitId)
        {
            if (!ItemCatalog.IsComponent(draggedItem)) return null;

            string other = null;
            if (hoveredSlot >= 0)
            {
                other = InventoryItemAt(hoveredSlot);
            }
            else if (unitId != null)
            {
                var equipped = IsOnlineMatch
                    ? onlineService.LocalMatchState.units.FirstOrDefault(unit => unit.id == unitId)?.items
                    : Player.Units.FirstOrDefault(unit => unit.InstanceId == unitId)?.ItemInstanceIds;
                other = equipped?.FirstOrDefault(ItemCatalog.IsComponent);
            }

            return other != draggedItem && ItemCatalog.IsComponent(other)
                ? ItemCatalog.Recipe(ItemCatalog.DefinitionId(draggedItem), ItemCatalog.DefinitionId(other))
                : null;
        }

        private void UpdateItemRecipePreview(ItemDefinition result, Vector2 pointer, bool besideRecipeList = false)
        {
            itemRecipePreview.GetComponent<UnityEngine.UI.Image>().raycastTarget = result != null && besideRecipeList;
            itemRecipePreview.gameObject.SetActive(result != null);
            if (result == null) return;

            itemRecipeName.text = result.Name;
            LayoutItemDetailSections(result);
            itemRecipeIcon.sprite = itemIcons?.Get(result.Id);
            itemRecipeIcon.enabled = itemRecipeIcon.sprite != null;
            var bounds = canvasRect.rect;
            var half = itemRecipePreview.rect.size * .5f;
            var position = besideRecipeList
                ? pointer + new Vector2(half.x, -half.y)
                : pointer + new Vector2(half.x + 30, half.y + 15);
            if (position.x + half.x > bounds.xMax)
                position.x = besideRecipeList ? pointer.x - componentRecipes.rect.width * componentRecipes.localScale.x - half.x : pointer.x - half.x - 30;
            itemRecipePreview.anchoredPosition = new Vector2(
                Mathf.Clamp(position.x, bounds.xMin + half.x, bounds.xMax - half.x),
                Mathf.Clamp(position.y, bounds.yMin + half.y, bounds.yMax - half.y));
            itemRecipePreview.SetAsLastSibling();
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
            int hoveredSlot = HoveredInventorySlot(point);
            string target = hoveredSlot < 0 ? ItemTarget(point) : null;
            UpdateItemRecipePreview(PreviewRecipe(hoveredSlot, target), local);
            foreach (var slot in slots) slot.Graphic.color = BaseSlotColor(slot);
            if (destination != null && hoveredSlot < 0)
                destination.Graphic.color = target != null ? new Color(.08f,.36f,.29f) : new Color(.48f,.12f,.17f);
        }
        public void EndItemDrag(Vector2 point)
        {
            if(!IsItemDragging)return;
            string id = draggedItem;
            long revision = draggedItemRevision;
            int hoveredSlot = HoveredInventorySlot(point);
            if (hoveredSlot >= 0 && CanDragItems() && PreviewRecipe(hoveredSlot, null) != null)
            {
                string targetItemId = InventoryItemAt(hoveredSlot);
                CancelItemDrag();
                if (IsOnlineMatch)
                {
                    SendOnlineCommand(MatchCommandKind.CombineItems, command =>
                    {
                        command.itemId = id;
                        command.targetItemId = targetItemId;
                        command.itemRevision = revision;
                    });
                }
                else
                {
                    Feedback(commands.CombineItems(id, targetItemId, revision).Message);
                    Render();
                }
                return;
            }
            string target = null;
            if (hoveredSlot < 0 && CanDragItems())
                target = id == EvolutionDragId && ItemPhase == MatchPhase.Preparation
                    ? ItemTarget(EvolutionSlotAt(point, Hit(point))) : ItemTarget(point);
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
            if (itemRecipePreview != null) itemRecipePreview.gameObject.SetActive(false);
            ClearEvolutionHighlights();
            if(Match!=null)foreach(var slot in slots)slot.Graphic.color=BaseSlotColor(slot);
        }
        private void PlayItemReward(BoardPosition monsterCell, string rewardKey, int index)
        {
            if (!isActiveAndEnabled || combatRoot == null || itemIcons?.rewardFlight is not { Length: > 0 } ||
                itemIcons.rewardArrival is not { Length: > 0 })
                return;
            Vector2 start = canvasRect.InverseTransformPoint(combatRoot.TransformPoint(CombatPoint(monsterCell)));
            StartCoroutine(AnimateItemReward(start, rewardKey, index, IsOnlineMatch ? (object)onlineMatchId : Match));
        }

        private IEnumerator AnimateItemReward(Vector2 start, string rewardKey, int index, object matchKey)
        {
            const float flightSeconds = .5f, arrivalSeconds = .22f;
            float elapsed = -.07f * index;
            UnityEngine.UI.Image image = null;
            try
            {
                while (elapsed < flightSeconds + arrivalSeconds && isActiveAndEnabled &&
                    Equals(matchKey, IsOnlineMatch ? (object)onlineMatchId : Match) &&
                    (IsOnlineMatch ? onlineService.WatchingPlayerId == onlineService.LocalMatchState?.playerId : localViewedPlayerId == Player.PlayerId))
                {
                    if (elapsed >= 0 && itemInventoryPanel.gameObject.activeInHierarchy)
                    {
                        if (image == null)
                        {
                            image = InventoryImage("ItemRewardEffect", canvasRect, new Vector2(64, 64), start, null);
                            image.raycastTarget = false;
                            image.preserveAspect = true;
                        }

                        image.enabled = true;
                        int inventoryIndex = Array.FindIndex(visibleInventory, item =>
                            ItemCatalog.IsComponent(item) && item.Split(':')[2] == rewardKey);
                        int slot = 1 + inventoryIndex - inventoryPage * ToolsPerPage;
                        RectTransform destination = inventoryIndex >= 0 && slot > 0 && slot < inventoryChoices.Length
                            ? (RectTransform)inventoryChoices[slot].transform : inventoryPageText.rectTransform;
                        Vector2 end = canvasRect.InverseTransformPoint(destination.position);
                        bool flying = elapsed < flightSeconds;
                        float progress = flying ? elapsed / flightSeconds : (elapsed - flightSeconds) / arrivalSeconds;
                        var frames = flying ? itemIcons.rewardFlight : itemIcons.rewardArrival;
                        image.sprite = frames[Mathf.Min(frames.Length - 1, (int)(progress * frames.Length))];

                        float arc = Mathf.Min(90, Vector2.Distance(start, end) * .22f);
                        Vector2 heading = end - start + Vector2.up * (Mathf.PI * arc * Mathf.Cos(Mathf.PI * progress));
                        image.rectTransform.anchoredPosition = flying
                            ? Vector2.Lerp(start, end, progress) + Vector2.up * (arc * Mathf.Sin(Mathf.PI * progress)) : end;
                        image.rectTransform.localRotation = flying
                            ? Quaternion.Euler(0, 0, Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg) : Quaternion.identity;
                        image.transform.SetAsLastSibling();
                    }
                    else if (image != null) image.enabled = false;

                    yield return null;
                    elapsed += Time.unscaledDeltaTime;
                }
            }
            finally
            {
                if (image != null) Destroy(image.gameObject);
            }
        }

        private void RenderItemWildUI()
        {
            if(itemInventoryPanel==null)return;
            var owner=IsOnlineMatch?onlineService?.LocalMatchState:null;
            var pub=IsOnlineMatch?onlineService?.PublicState:null;
            if(IsOnlineMatch&&(owner==null||pub==null)){
                wildPreviewLabel.gameObject.SetActive(false);itemInventoryPanel.gameObject.SetActive(false);CancelItemDrag();HideItemHint();return;
            }
            var phase=IsOnlineMatch?pub.phase:Match.Phase;
            bool wild=IsOnlineMatch?pub.wildRound:WildRoundSystem.IsWild(Match);
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
            wildPreviewLabel.gameObject.SetActive(wild&&phase==MatchPhase.Preparation);
            hudNotice.gameObject.SetActive(false);
            wildPreviewLabel.text="야생 라운드 · "+(IsOnlineMatch?pub.wildPreview:WildRoundSystem.Preview(Match.RoundNumber));
            itemInventoryPanel.gameObject.SetActive(phase!=MatchPhase.Finished);
            ValidateComponentRecipeHint();
            inventoryLabel.text="ITEMS ("+visibleInventory.Length+")";
            if(IsItemDragging&&(!CanDragItems()||(draggedItem==EvolutionDragId
                ? CurrentEvolutionPoints<=0||(IsOnlineMatch?owner.evolutionRevision:Player.EvolutionRevision)!=draggedEvolutionRevision||(IsOnlineMatch?owner.placementRevision:Player.PlacementRevision)!=draggedEvolutionPlacementRevision
                : displayedItemRevision!=draggedItemRevision||!visibleInventory.Contains(draggedItem))))CancelItemDrag();
            inventoryPageText.text = $"{inventoryPage + 1} / {pageCount}";
            inventoryPrev.interactable = !IsItemDragging && inventoryPage > 0;
            inventoryNext.interactable = !IsItemDragging && inventoryPage + 1 < pageCount;
            bool canUseTools = CanDragItems();
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
                button.transform.Find("ItemName").GetComponent<UnityEngine.UI.Text>().text =
                    key != null && icon.sprite == null ? ItemCatalog.Get(key).Name : "";
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






