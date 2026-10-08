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
        private UnityEngine.UI.Button itemReturnButton,itemPrevious,itemNext;
        private readonly UnityEngine.UI.Button[] itemChoices=new UnityEngine.UI.Button[3];
        private readonly UnityEngine.UI.Button[] inventoryChoices=new UnityEngine.UI.Button[6];
        private string[] visibleInventory=Array.Empty<string>();
        private int itemPage;
        private ItemIconLibrary itemIcons;
        private string draggedItem;
        private long draggedItemRevision;
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
            itemInventoryPanel=Rect("ItemIcons",canvasRect,new Vector2(195,64),new Vector2(-510,-96));
            for(int i=0;i<6;i++){
                int slot=i;inventoryChoices[i]=ItemButton("InventoryItem_"+i,itemInventoryPanel,new Vector2(30,34),new Vector2(-78+i*31,9),()=>{});
                inventoryChoices[i].GetComponentInChildren<UnityEngine.UI.Text>().gameObject.SetActive(false);
                var icon=Rect("Icon",inventoryChoices[i].transform,new Vector2(28,28),Vector2.zero).gameObject.AddComponent<UnityEngine.UI.Image>();
                icon.preserveAspect=true;icon.raycastTarget=false;
                var drag=inventoryChoices[i].gameObject.AddComponent<ItemDragToken>();drag.View=this;drag.Slot=slot;
            }
            itemPrevious=ItemButton("<",itemInventoryPanel,new Vector2(20,18),new Vector2(-86,-22),()=>{CancelItemDrag();HideItemHint();itemPage=Math.Max(0,itemPage-1);RenderItemWildUI();});
            itemNext=ItemButton(">",itemInventoryPanel,new Vector2(20,18),new Vector2(86,-22),()=>{CancelItemDrag();HideItemHint();itemPage++;RenderItemWildUI();});
            itemInventoryTitle=ItemLabel("ItemHint",itemInventoryPanel,"유닛에 드래그하여 장착",10,new Vector2(148,20),new Vector2(0,-22));
            itemReturnButton=ItemButton("장착 도구 회수",canvasRect,new Vector2(205,26),new Vector2(-510,-25),UnequipSelectedItem);
            itemDragGhost=Rect("ItemDragPreview",canvasRect,new Vector2(40,40),Vector2.zero);
            var ghostIcon=itemDragGhost.gameObject.AddComponent<UnityEngine.UI.Image>();ghostIcon.preserveAspect=true;ghostIcon.raycastTarget=false;
            itemDragGhost.gameObject.SetActive(false);
            itemTooltip=Panel("ItemTooltip",new Vector2(275,80),new Vector2(-260,-45));
            itemTooltip.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
            itemTooltipLabel=ItemLabel("Description",itemTooltip,"",12,new Vector2(255,70),Vector2.zero);
            itemTooltip.gameObject.SetActive(false);
            itemRewardPanel=Panel("ItemRewardPanel",new Vector2(690,245),new Vector2(0,100));
            ItemLabel("Title",itemRewardPanel,"야생 라운드 보상 · 도구 1개 선택",18,new Vector2(640,28),new Vector2(0,93));
            ItemLabel("Hint",itemRewardPanel,"시간이 끝나면 첫 번째 도구를 자동 획득합니다. 다음 준비 단계에서 장착하세요.",12,new Vector2(650,25),new Vector2(0,-97));
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
            int index=itemPage*6+slot;if(index>=visibleInventory.Length||IsItemDragging)return;
            string key=ItemCatalog.DefinitionId(visibleInventory[index]);if(key==null)return;
            var definition=ItemCatalog.Get(key);itemTooltipLabel.text=definition.Name+"\n"+definition.Description;
            itemTooltip.gameObject.SetActive(true);itemTooltip.SetAsLastSibling();
        }
        public void HideItemHint(){if(itemTooltip!=null)itemTooltip.gameObject.SetActive(false);}
        public void BeginItemDrag(int slot,Vector2 point)
        {
            if(IsDragging||!CanDragItems()||slot<0||slot>=6||!inventoryChoices[slot].interactable)return;
            int index=itemPage*6+slot;if(index>=visibleInventory.Length)return;
            if(ItemCatalog.DefinitionId(visibleInventory[index])==null)return;
            draggedItem=visibleInventory[index];draggedItemRevision=displayedItemRevision;
            HideItemHint();
            itemDragGhost.GetComponent<UnityEngine.UI.Image>().sprite=itemIcons?.Get(ItemCatalog.DefinitionId(draggedItem));
            itemDragGhost.gameObject.SetActive(true);itemDragGhost.SetAsLastSibling();DragItem(point);
        }
        private bool CanDragItems()=>IsOnlineMatch
            ? OnlineInputReady&&onlineService.LocalMatchState?.phase==MatchPhase.Preparation&&!onlineService.LocalMatchState.eliminated&&onlineService.LocalMatchState.hp>0
            : Match!=null&&Match.Phase==MatchPhase.Preparation&&!busy&&!RoundLoop.NextPreparationPending&&Simulation?.Status!=SimulationStatus.Running&&!Player.IsEliminated&&Player.HP>0;
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
            var destination=Hit(point);if(destination!=null)destination.Graphic.color=ItemTarget(point)!=null?new Color(.08f,.36f,.29f):new Color(.48f,.12f,.17f);
        }
        public void EndItemDrag(Vector2 point)
        {
            if(!IsItemDragging)return;
            string id=draggedItem,target=CanDragItems()?ItemTarget(point):null;long revision=draggedItemRevision;CancelItemDrag();
            if(target==null){Feedback("장착 취소: 준비 단계에서 내 유닛 위에 놓으세요.");Render();return;}
            selectedUnitId=target;
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
            visibleInventory=IsOnlineMatch?owner.inventory:Player.ItemInventory.ToArray();
            bool selecting=alive&&candidates.Length==3&&(phase==MatchPhase.Result||phase==MatchPhase.Preparation);
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
            if(IsItemDragging&&(!CanDragItems()||displayedItemRevision!=draggedItemRevision||!visibleInventory.Contains(draggedItem)))CancelItemDrag();
            itemPage=Math.Min(itemPage,Math.Max(0,(visibleInventory.Length-1)/6));
            bool own=IsOnlineMatch?owner.units.Any(u=>u.id==selectedUnitId):Player.Units.Any(u=>u.InstanceId==selectedUnitId);
            bool canEquip=alive&&ready&&own&&phase==MatchPhase.Preparation;
            var equipped=IsOnlineMatch?owner.units.FirstOrDefault(u=>u.id==selectedUnitId)?.items:Player.Units.FirstOrDefault(u=>u.InstanceId==selectedUnitId)?.ItemInstanceIds.ToArray();
            itemInventoryTitle.text=visibleInventory.Length==0?"도구 없음":phase==MatchPhase.Preparation?"유닛에 드래그하여 장착":"준비 단계에서 장착 가능";
            for(int i=0;i<6;i++){
                int index=itemPage*6+i;var button=inventoryChoices[i];button.gameObject.SetActive(index<visibleInventory.Length);
                if(index>=visibleInventory.Length)continue;
                string key=ItemCatalog.DefinitionId(visibleInventory[index]);var d=key==null?null:ItemCatalog.Get(key);
                button.transform.Find("Icon").GetComponent<UnityEngine.UI.Image>().sprite=d==null?null:itemIcons?.Get(key);button.interactable=alive&&ready&&!selecting&&phase==MatchPhase.Preparation&&d!=null;
            }
            itemPrevious.interactable=itemPage>0;itemNext.interactable=(itemPage+1)*6<visibleInventory.Length;
            itemPrevious.gameObject.SetActive(visibleInventory.Length>6);itemNext.gameObject.SetActive(visibleInventory.Length>6);
            itemReturnButton.interactable=canEquip&&equipped?.Length>0;
            if(IsItemDragging)itemDragGhost.SetAsLastSibling();
        }
    }
}

