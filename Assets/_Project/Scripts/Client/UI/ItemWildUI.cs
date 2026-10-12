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
        private UnityEngine.UI.Button itemReturnButton;
        private readonly UnityEngine.UI.Button[] itemChoices=new UnityEngine.UI.Button[3];
        private readonly UnityEngine.UI.Button[] inventoryChoices=new UnityEngine.UI.Button[10];
        private string[] visibleInventory=Array.Empty<string>();

        private readonly string[] visibleItemSlots=new string[8];
        private object itemSlotOwner;
        private const string ReturnDragId="return-tools";
        private ItemIconLibrary itemIcons;
        private string draggedItem;
        private long draggedItemRevision;
        private const string EvolutionDragId="evolution-point";
        private long draggedEvolutionRevision,draggedEvolutionPlacementRevision;
        private int InventoryIndex(int slot)=>slot<2?-1:Array.IndexOf(visibleInventory,visibleItemSlots[slot-2]);
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
            itemInventoryPanel=Panel("ItemIcons",new Vector2(102,272),new Vector2(-390,70));
            for(int i=0;i<10;i++){
                int slot=i;inventoryChoices[i]=ItemButton("InventoryItem_"+i,itemInventoryPanel,new Vector2(42,44),new Vector2(i%2==0?-24:24,102-i/2*49),()=>{});
                inventoryChoices[i].GetComponentInChildren<UnityEngine.UI.Text>().gameObject.SetActive(false);
                var icon=Rect("Icon",inventoryChoices[i].transform,new Vector2(32,32),Vector2.zero).gameObject.AddComponent<UnityEngine.UI.Image>();
                icon.preserveAspect=true;icon.raycastTarget=false;
                if(i==0)ItemLabel("Count",inventoryChoices[i].transform,"0",11,new Vector2(30,16),new Vector2(0,-12));
                var drag=inventoryChoices[i].gameObject.AddComponent<ItemDragToken>();drag.View=this;drag.Slot=slot;
            }
            itemInventoryTitle=ItemLabel("ItemHint",itemInventoryPanel,"",9,new Vector2(100,24),new Vector2(0,-123));
            itemReturnButton=inventoryChoices[1];ItemLabel("ReturnGlyph",itemReturnButton.transform,"↶",28,new Vector2(40,40),Vector2.zero);
            itemDragGhost=Rect("ItemDragPreview",canvasRect,new Vector2(40,40),Vector2.zero);
            var ghostIcon=itemDragGhost.gameObject.AddComponent<UnityEngine.UI.Image>();ghostIcon.preserveAspect=true;ghostIcon.raycastTarget=false;
            ItemLabel("ReturnGlyph",itemDragGhost,"↶",30,new Vector2(40,40),Vector2.zero).gameObject.SetActive(false);
            itemDragGhost.gameObject.SetActive(false);
            itemTooltip=Panel("ItemTooltip",new Vector2(275,80),new Vector2(-260,-45));
            itemTooltip.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
            itemTooltipLabel=ItemLabel("Description",itemTooltip,"",12,new Vector2(255,70),Vector2.zero);
            itemTooltip.gameObject.SetActive(false);
            itemRewardPanel=Panel("ItemRewardPanel",new Vector2(690,245),new Vector2(0,100));
            ItemLabel("Title",itemRewardPanel,"야생 라운드 보상 · 도구 1개 선택",18,new Vector2(640,28),new Vector2(0,93));
            ItemLabel("Hint",itemRewardPanel,"준비 시간이 끝나면 첫 번째 도구를 자동 획득합니다. 선택 후 바로 장착할 수 있습니다.",12,new Vector2(650,25),new Vector2(0,-97));
            for(int i=0;i<3;i++){
                int slot=i;itemChoices[i]=ItemButton("ChooseItem_"+i,itemRewardPanel,new Vector2(207,142),new Vector2((i-1)*220,0),()=>ChooseItemReward(slot));
            }
            itemRewardPanel.gameObject.SetActive(false);
        }
        private void ChooseItemReward(int slot)
        {
            if(IsOnlineMatch){SendOnlineCommand(MatchCommandKind.ChooseItem,c=>{c.slot=slot;c.itemRevision=displayedItemRevision;});return;}
            Feedback(commands.ChooseItem(slot,displayedItemRevision).Message);Render();
        }
        public bool IsItemDragging=>draggedItem!=null;
        public void ShowItemHint(int slot)
        {
            if(IsItemDragging||slot<0||slot>=10)return;
            if(slot==0){itemTooltipLabel.text="진화 포인트 · "+CurrentEvolutionPoints+" EP\n내 유닛에 드래그 · 필드·대기열의 같은 종류도 진화\n한 단계당 1 EP · 상점의 같은 계열도 즉시 진화";itemTooltip.gameObject.SetActive(true);itemTooltip.SetAsLastSibling();return;}
            if(slot==1){itemTooltipLabel.text="도구 회수\n내 유닛에 드래그하면 장착 도구를 모두 회수합니다.";itemTooltip.gameObject.SetActive(true);itemTooltip.SetAsLastSibling();return;}
            int index=InventoryIndex(slot);if(index<0||index>=visibleInventory.Length)return;
            string key=ItemCatalog.DefinitionId(visibleInventory[index]);if(key==null)return;
            var definition=ItemCatalog.Get(key);itemTooltipLabel.text=definition.Name+"\n"+definition.Description;
            itemTooltip.gameObject.SetActive(true);itemTooltip.SetAsLastSibling();
        }
        public void HideItemHint(){if(itemTooltip!=null)itemTooltip.gameObject.SetActive(false);}
        public void BeginItemDrag(int slot,Vector2 point)
        {
            if(IsDragging||!CanDragItems()||slot<0||slot>=10||!inventoryChoices[slot].interactable)return;
            if(slot==0){
                if(CurrentEvolutionPoints<=0)return;
                draggedItem=EvolutionDragId;
                draggedEvolutionRevision=IsOnlineMatch?onlineService.LocalMatchState.evolutionRevision:Player.EvolutionRevision;
                draggedEvolutionPlacementRevision=IsOnlineMatch?onlineService.LocalMatchState.placementRevision:Player.PlacementRevision;
            }else if(slot==1){draggedItem=ReturnDragId;draggedItemRevision=displayedItemRevision;}
            else{
                int index=InventoryIndex(slot);if(index<0||index>=visibleInventory.Length)return;
                if(ItemCatalog.DefinitionId(visibleInventory[index])==null)return;
                draggedItem=visibleInventory[index];draggedItemRevision=displayedItemRevision;
            }
            HideItemHint();
            itemDragGhost.GetComponent<UnityEngine.UI.Image>().sprite=draggedItem==EvolutionDragId?itemIcons?.evolutionPoint:itemIcons?.Get(ItemCatalog.DefinitionId(draggedItem));
            itemDragGhost.transform.Find("ReturnGlyph").gameObject.SetActive(draggedItem==ReturnDragId);
            itemDragGhost.gameObject.SetActive(true);itemDragGhost.SetAsLastSibling();DragItem(point);
        }
        private bool CanDragItems()=>IsOnlineMatch
            ? onlineService.WatchingPlayerId==onlineService.LocalMatchState?.playerId&&OnlineInputReady&&onlineService.LocalMatchState?.phase==MatchPhase.Preparation&&!onlineService.LocalMatchState.eliminated&&onlineService.LocalMatchState.hp>0
            : localViewedPlayerId=="p1"&&Match!=null&&Match.Phase==MatchPhase.Preparation&&!busy&&!RoundLoop.NextPreparationPending&&Simulation?.Status!=SimulationStatus.Running&&!Player.IsEliminated&&Player.HP>0;
        private string ItemTarget(Vector2 point)
        {
            var slot=Hit(point);if(slot==null)return null;
            if(IsOnlineMatch)return onlineService.LocalMatchState?.units.FirstOrDefault(u=>u.placement==slot.Placement.Kind&&(u.placement==PlacementKind.Board?u.column==slot.Placement.Position.Value.Column&&u.row==slot.Placement.Position.Value.Row:u.bench==slot.Placement.BenchSlot))?.id;
            return Player.Units.FirstOrDefault(u=>u.Placement.Kind==slot.Placement.Kind&&Nullable.Equals(u.Placement.Position,slot.Placement.Position)&&u.Placement.BenchSlot==slot.Placement.BenchSlot)?.InstanceId;
        }
        public void DragItem(Vector2 point)
        {
            if(!IsItemDragging)return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,point,null,out var local);itemDragGhost.anchoredPosition=local;
            foreach(var slot in slots)slot.Graphic.color=BaseSlotColor(slot);
            var destination=Hit(point);if(destination!=null){var target=ItemTarget(point);destination.Graphic.color=target!=null&&(draggedItem!=EvolutionDragId||CanEvolveTarget(target))?new Color(.08f,.36f,.29f):new Color(.48f,.12f,.17f);}
        }
        public void EndItemDrag(Vector2 point)
        {
            if(!IsItemDragging)return;
            string id=draggedItem,target=CanDragItems()?ItemTarget(point):null;long revision=draggedItemRevision;CancelItemDrag();
            if(target==null){Feedback((id==EvolutionDragId?"진화":"장착")+" 취소: 준비 단계에서 내 유닛 위에 놓으세요.");Render();return;}
            selectedUnitId=target;
            if(id==ReturnDragId){if(IsOnlineMatch){SendOnlineCommand(MatchCommandKind.UnequipItem,c=>{c.unitId=target;c.itemRevision=revision;});return;}Feedback(commands.UnequipItem(target,revision).Message);Render();return;}
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
            if(Match!=null)foreach(var slot in slots)slot.Graphic.color=BaseSlotColor(slot);
        }
        private void UnequipSelectedItem()
        {
            if(selectedUnitId==null)return;
            if(IsOnlineMatch){SendOnlineCommand(MatchCommandKind.UnequipItem,c=>{c.unitId=selectedUnitId;c.itemRevision=displayedItemRevision;});return;}
            Feedback(commands.UnequipItem(selectedUnitId,displayedItemRevision).Message);Render();
        }
        private void RenderItemWildUI()
        {
            if(itemRewardPanel==null)return;
            var owner=IsOnlineMatch?onlineService?.LocalMatchState:null;
            var pub=IsOnlineMatch?onlineService?.PublicState:null;
            if(IsOnlineMatch&&(owner==null||pub==null)){
                itemRewardPanel.gameObject.SetActive(false);wildPreviewLabel.gameObject.SetActive(false);itemInventoryPanel.gameObject.SetActive(false);itemReturnButton.interactable=false;CancelItemDrag();return;
            }
            var phase=IsOnlineMatch?pub.phase:Match.Phase;
            bool wild=IsOnlineMatch?pub.wildRound:WildRoundSystem.IsWild(Match);
            bool alive=IsOnlineMatch?!owner.eliminated&&owner.hp>0:!Player.IsEliminated&&Player.HP>0;
            bool ready=IsOnlineMatch?OnlineInputReady:Simulation?.Status!=SimulationStatus.Running&&!busy;
            var candidates=IsOnlineMatch?owner.itemCandidates??Array.Empty<string>():Player.PendingItemReward?.Candidates.ToArray()??Array.Empty<string>();
            displayedItemRevision=IsOnlineMatch?owner.itemRevision:Player.ItemRevision;
            visibleInventory=IsOnlineMatch?owner.inventory??Array.Empty<string>():Player.ItemInventory.ToArray();
            object slotOwner=IsOnlineMatch?(object)owner.playerId:Match;
            if(!Equals(itemSlotOwner,slotOwner)){Array.Clear(visibleItemSlots,0,8);itemSlotOwner=slotOwner;}
            for(int i=0;i<8;i++)if(visibleItemSlots[i]!=null&&!visibleInventory.Contains(visibleItemSlots[i]))visibleItemSlots[i]=null;
            foreach(var id in visibleInventory){if(visibleItemSlots.Contains(id))continue;int free=Array.IndexOf(visibleItemSlots,null);if(free<0)break;visibleItemSlots[free]=id;}
            bool selecting=alive&&candidates.Length==3&&phase==MatchPhase.Preparation;
            itemRewardPanel.gameObject.SetActive(selecting);
            if(selecting){
                itemRewardPanel.SetAsLastSibling();
                for(int i=0;i<3;i++){var d=ItemCatalog.Get(candidates[i]);ButtonText(itemChoices[i],d.Name+"\n\n"+d.Description+"\n\n추천: "+d.Role);itemChoices[i].interactable=ready;}
            }
            wildPreviewLabel.gameObject.SetActive(wild&&phase==MatchPhase.Preparation);
            hudNotice.gameObject.SetActive(!(wild&&phase==MatchPhase.Preparation));
            wildPreviewLabel.text="야생 라운드 · "+(IsOnlineMatch?pub.wildPreview:WildRoundSystem.Preview(Match.RoundNumber));
            itemInventoryPanel.gameObject.SetActive(phase!=MatchPhase.Finished);
            itemReturnButton.gameObject.SetActive(phase!=MatchPhase.Finished);
            inventoryLabel.text="ITEMS ("+visibleInventory.Length+")";
            if(IsItemDragging&&(!CanDragItems()||(draggedItem==EvolutionDragId
                ? CurrentEvolutionPoints<=0||(IsOnlineMatch?owner.evolutionRevision:Player.EvolutionRevision)!=draggedEvolutionRevision||(IsOnlineMatch?owner.placementRevision:Player.PlacementRevision)!=draggedEvolutionPlacementRevision
                : displayedItemRevision!=draggedItemRevision||draggedItem!=ReturnDragId&&!visibleInventory.Contains(draggedItem))))CancelItemDrag();
            itemInventoryTitle.text=visibleInventory.Length>8?"+"+(visibleInventory.Length-8)+" 보관":"도구";
            for(int i=0;i<10;i++){
                var button=inventoryChoices[i];
                if(i==0){button.gameObject.SetActive(true);button.transform.Find("Icon").GetComponent<UnityEngine.UI.Image>().sprite=itemIcons?.evolutionPoint;button.transform.Find("Count").GetComponent<UnityEngine.UI.Text>().text=CurrentEvolutionPoints.ToString();button.interactable=alive&&ready&&!selecting&&phase==MatchPhase.Preparation&&CurrentEvolutionPoints>0;continue;}
                if(i==1){button.gameObject.SetActive(true);button.transform.Find("Icon").GetComponent<UnityEngine.UI.Image>().enabled=false;button.interactable=CanDragItems()&&!selecting;continue;}
                int index=InventoryIndex(i);button.gameObject.SetActive(true);
                if(index<0||index>=visibleInventory.Length){button.transform.Find("Icon").GetComponent<UnityEngine.UI.Image>().enabled=false;button.interactable=false;continue;}
                button.transform.Find("Icon").GetComponent<UnityEngine.UI.Image>().enabled=true;
                string key=ItemCatalog.DefinitionId(visibleInventory[index]);var d=key==null?null:ItemCatalog.Get(key);
                button.transform.Find("Icon").GetComponent<UnityEngine.UI.Image>().sprite=d==null?null:itemIcons?.Get(key);button.interactable=alive&&ready&&!selecting&&phase==MatchPhase.Preparation&&d!=null;
            }
            itemReturnButton.interactable=CanDragItems()&&!selecting;
            if(IsItemDragging)itemDragGhost.SetAsLastSibling();
        }
        private bool CanEvolveTarget(string id)
        {
            if(!CanDragItems()||CurrentEvolutionPoints<=0)return false;
            if(!IsOnlineMatch)return commands.PreviewEvolution(id,draggedEvolutionRevision,draggedEvolutionPlacementRevision).Accepted;
            var unit=onlineService.LocalMatchState.units.FirstOrDefault(u=>u.id==id);
            return unit!=null&&catalog.Get(unit.definitionId).Evolutions.Count==1;
        }
    }
}






