using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using PokeChess.Network.Session;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private OnlineConnection onlineService;
        private PokemonCatalog savedLocalCatalog;
        private OwnerMatchState previousOwner;
        private GameObject onlineControls;
        private UnityEngine.UI.Button onlineRetry,onlineSync;
        private string onlineMatchId;
        public bool IsOnlineMatch {get;private set;}
        public string OnlineObservedPlayerId=>onlineService?.WatchingPlayerId;
        private bool OnlineInputReady=>IsOnlineMatch&&onlineService.State==ConnectionState.Connected&&onlineService.StateSynchronized&&onlineService.PendingCommand==null&&onlineService.LocalMatchState?.eliminated==false&&onlineService.PublicState?.phase!=MatchPhase.Finished;
        private bool OnlineShopExpanded=>onlineService?.LocalMatchState!=null&&!onlineService.LocalMatchState.eliminated&&(onlineService.PublicState.phase==MatchPhase.Preparation||onlineService.PublicState.phase==MatchPhase.Combat)&&!shopCollapsed;
        private static string PlayerLabel(string id,string own)=>id==own?"YOU":"Player "+(id??"").Substring(Math.Max(0,(id?.Length??0)-6));

        public void EnterOnlineMatch(OnlineConnection connection)
        {
            if(IsOnlineMatch&&onlineMatchId==connection.StartInfo?.matchId)return;
            CancelDrag("Online match starting.");Simulation?.Cancel();Simulation=null;
            savedLocalCatalog=catalog;catalog=PrototypeRoster.CreateCatalog();onlineService=connection;onlineMatchId=connection.StartInfo.matchId;
            IsOnlineMatch=true;enabled=true;canvasRect.gameObject.SetActive(true);previousOwner=null;selectedUnitId=null;shopPhase=null;shopCollapsed=false;presentationRound=-1;eliminationDismissed=false;resultExpanded=false;
            ClearOnlineFighters();foreach(var row in hudProfiles.Values)Destroy(row.Rect.gameObject);hudProfiles.Clear();hudMatch=null;
            if(onlineControls==null)BuildOnlineControls();onlineControls.SetActive(true);
            SetLocalControls(false);ButtonText(newMatchButton,"Return to rooms");newMatchButton.onClick.RemoveAllListeners();newMatchButton.onClick.AddListener(LeaveOnlineMatch);
            onlineService.Combat.EventPlayed+=ShowOnlineEffect;RenderOnlineMatch();
        }
        public void ExitOnlineMatch()
        {
            if(!IsOnlineMatch)return;onlineService.Combat.EventPlayed-=ShowOnlineEffect;IsOnlineMatch=false;
            dragged=null;ghost.gameObject.SetActive(false);ClearOnlineFighters();catalog=savedLocalCatalog;previousOwner=null;onlineMatchId=null;selectedUnitId=null;
            onlineControls.SetActive(false);SetLocalControls(true);hudMatch=null;shopPhase=null;
            newMatchButton.onClick.RemoveAllListeners();newMatchButton.onClick.AddListener(ResetMatchUI);ButtonText(newMatchButton,"New match");Render();
            canvasRect.gameObject.SetActive(false);enabled=false;
        }
        public void ShowLocalMatch(){if(IsOnlineMatch)return;enabled=true;canvasRect.gameObject.SetActive(true);Render();}
        private void SetLocalControls(bool active)
        {
            string[] names={"DebugTitle","DebugState","P01-P04 demo","P05-P08 demo","P09-P12 demo","Reset match","Rank demo","Full bench","Fixed foe","4 players","6 players","8 players","3 players","5 players","7 players","DEV: Start battle","Auto play 20","Fast simulation","Save report"};
            foreach(var name in names){var node=canvasRect.Find(name);if(node!=null)node.gameObject.SetActive(active);}
            roundButton.gameObject.SetActive(active);
            SimulationStartButton.gameObject.SetActive(active);SimulationSpeedButton.gameObject.SetActive(active);simulationPanel.gameObject.SetActive(false);simulationInfo.gameObject.SetActive(false);
            foreach(var button in canvasRect.GetComponentsInChildren<UnityEngine.UI.Button>(true))if(button.name.Contains("simulat")||button.name.StartsWith("Run ")||button.name=="Copy report")button.gameObject.SetActive(active);
        }
        private void BuildOnlineControls()
        {
            onlineControls=Rect("OnlineMatchControls",canvasRect,Vector2.zero,Vector2.zero).gameObject;
            AddOnlineControl("My board",13,()=>WatchOnlinePlayer(onlineService.LocalMatchState.playerId));
            AddOnlineControl("Watch next",-23,()=>{
                var players=onlineService.PublicState.players.Where(p=>!p.eliminated).ToArray();if(players.Length==0)return;
                int index=Array.FindIndex(players,p=>p.id==onlineService.WatchingPlayerId);WatchOnlinePlayer(players[(index+1)%players.Length].id);
            });
            onlineSync=AddOnlineControl("Sync state",-59,async()=>{await onlineService.RequestStateSyncAsync();});
            onlineRetry=AddOnlineControl("Retry command",-95,async()=>{await onlineService.RetryPendingCommandAsync();});
            AddOnlineControl("Leave match",-123,LeaveOnlineMatch);
        }
        private UnityEngine.UI.Button AddOnlineControl(string name,float y,Action action)
        {var button=ActionButton(name,new Vector2(225,28),new Vector2(490,y),action);button.transform.SetParent(onlineControls.transform,false);return button;}
        private async void LeaveOnlineMatch(){await onlineService.LeaveAsync();}
        public void WatchOnlinePlayer(string id)
        {
            if(onlineService?.IsRecovering==true)return;
            if(!IsOnlineMatch)return;dragged=null;ghost.gameObject.SetActive(false);selectedUnitId=null;
            if(onlineService.WatchPlayer(id)){ClearOnlineFighters();Feedback(id==onlineService.LocalMatchState.playerId?"Your board":"Observing public board / combat");}
        }
        private void UpdateOnlineMatch()
        {
            if(Keyboard.current?.escapeKey.wasPressedThisFrame==true)CancelDrag("Drag cancelled.");
            var owner=onlineService.LocalMatchState;
            if(IsDragging&&(!OnlineInputReady||owner==null||owner.placementRevision!=dragRevision||!owner.units.Any(u=>u.id==dragged)||owner.phase!=MatchPhase.Preparation&&owner.units.First(u=>u.id==dragged).placement!=PlacementKind.Bench))CancelDrag("State changed; drag cancelled.");
            RenderOnlineMatch();AnimateShop();
        }
        private async void SendOnlineCommand(MatchCommandKind kind,Action<MatchCommand> configure=null)
        {
            if(!OnlineInputReady){Feedback("Waiting for confirmed state or command response.");return;}
            dragged=null;ghost.gameObject.SetActive(false);
            var command=onlineService.CreateCommand(kind);configure?.Invoke(command);
            try{await onlineService.SubmitCommandAsync(command);}catch(Exception e){Feedback(e.Message);}
        }
        private void SelectOnlineUnit(string id)
        {if(IsDragging)return;selectedUnitId=id;RenderOnlineMatch();}
        private bool CanOnlinePlace(OwnerUnit unit,UnitPlacement destination)
        {
            if(!OnlineInputReady||unit==null||onlineService.WatchingPlayerId!=onlineService.LocalMatchState.playerId)return false;
            return onlineService.PublicState.phase==MatchPhase.Preparation||onlineService.PublicState.phase==MatchPhase.Combat&&unit.placement==PlacementKind.Bench&&destination.Kind==PlacementKind.Bench;
        }
        private UnitPlacement OnlinePlacement(OwnerUnit unit)=>unit.placement==PlacementKind.Board?UnitPlacement.OnBoard(new BoardPosition(unit.column,unit.row)):UnitPlacement.OnBench(unit.bench);
        private void BeginOnlineDrag(string id,Vector2 point)
        {
            var owner=onlineService.LocalMatchState;var unit=owner?.units.FirstOrDefault(u=>u.id==id);
            if(unit==null||!CanOnlinePlace(unit,OnlinePlacement(unit))){Feedback("Placement unavailable for this view or phase.");return;}
            dragged=id;dragRevision=owner.placementRevision;ghostLabel.text=unit.definitionId;ghost.gameObject.SetActive(true);ghost.SetAsLastSibling();DragOnline(point);
        }
        private void DragOnline(Vector2 point)
        {
            if(!IsDragging)return;RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,point,null,out var local);ghost.anchoredPosition=local;
            var unit=onlineService.LocalMatchState.units.FirstOrDefault(u=>u.id==dragged);var destination=Hit(point);
            foreach(var slot in slots)slot.Graphic.color=slot.Color;
            if(HitShop(point)){Feedback(onlineService.PublicState.phase==MatchPhase.Preparation?"Release to request sale":"Sale unavailable during combat");return;}
            if(destination!=null)destination.Graphic.color=CanOnlinePlace(unit,destination.Placement)?new Color(.08f,.36f,.29f):new Color(.48f,.12f,.17f);
        }
        private void EndOnlineDrag(Vector2 point)
        {
            if(IsDragging&&onlineService.LocalMatchState?.placementRevision!=dragRevision){CancelDrag("State changed; drag cancelled.");return;}
            if(!IsDragging)return;string id=dragged;var unit=onlineService.LocalMatchState.units.FirstOrDefault(u=>u.id==id);var destination=Hit(point);
            dragged=null;ghost.gameObject.SetActive(false);
            if(HitShop(point)){if(onlineService.PublicState.phase==MatchPhase.Preparation)SendOnlineCommand(MatchCommandKind.Sell,c=>c.unitId=id);return;}
            if(destination!=null&&CanOnlinePlace(unit,destination.Placement))SendOnlineCommand(MatchCommandKind.Move,c=>{c.unitId=id;c.destination=destination.Placement.Kind;c.column=destination.Placement.Position?.Column??0;c.row=destination.Placement.Position?.Row??0;c.bench=destination.Placement.BenchSlot??0;});
            else Feedback("Move cancelled or unavailable.");
        }
        private void RenderOnlineMatch()
        {
            if(!IsOnlineMatch)return;var owner=onlineService.LocalMatchState;var pub=onlineService.PublicState;
            if(owner==null||pub==null){header.text="Synchronizing online match…";foreach(var b in shopCards)b.interactable=false;return;}
            if(previousOwner!=null){
                if(owner.gold!=previousOwner.gold)ShowEconomyChange((owner.gold>previousOwner.gold?"+":"")+(owner.gold-previousOwner.gold)+"G");
                var old=previousOwner.units.FirstOrDefault(u=>u.id==selectedUnitId);
                if(old!=null&&!owner.units.Any(u=>u.id==old.id))selectedUnitId=owner.units.FirstOrDefault(u=>u.definitionId==old.definitionId&&u.rank>old.rank)?.id;
                foreach(var unit in owner.units){var before=previousOwner.units.FirstOrDefault(u=>u.id==unit.id);if(before!=null&&before.rank<unit.rank){rankNotice="RANK UP R"+unit.rank;rankNoticeUntil=Time.unscaledTime+2;highlights[unit.id]=Time.unscaledTime+1.5f;}}
                if(owner.inventory.Length>previousOwner.inventory.Length){itemNotice="+"+(owner.inventory.Length-previousOwner.inventory.Length)+" item(s) returned";itemNoticeUntil=Time.unscaledTime+2;}
            }
            previousOwner=owner;
            if(shopPhase!=pub.phase){shopCollapsed=pub.phase!=MatchPhase.Preparation;shopPhase=pub.phase;}
            bool finished=pub.phase==MatchPhase.Finished;
            shopFrame.gameObject.SetActive(!finished);header.gameObject.SetActive(!finished&&pub.phase!=MatchPhase.Combat);resources.gameObject.SetActive(false);
            header.text="ONLINE / "+PlayerLabel(onlineService.WatchingPlayerId,owner.playerId)+" / BOARD "+owner.units.Count(u=>u.placement==PlacementKind.Board)+" / "+owner.level;
            status.text=onlineService.IsRecovering?onlineService.RecoveryFeedback:onlineService.PendingCommand!=null?"Command pending — retry checks the same command":!onlineService.StateSynchronized?"Synchronizing state…":onlineService.CommandFeedback??LastFeedback??"State synchronized.";
            onlineRetry.gameObject.SetActive(onlineService.PendingCommand!=null);onlineRetry.interactable=!onlineService.IsRecovering;onlineSync.interactable=!onlineService.IsRecovering&&onlineService.PendingCommand==null;
            bool trade=OnlineInputReady&&pub.phase==MatchPhase.Preparation;
            rerollButton.interactable=trade&&owner.gold>=2;xpButton.interactable=trade&&owner.gold>=4&&owner.level<onlineLevelRules.MaxLevel;lockButton.interactable=trade;ButtonText(rerollButton,"Reroll / 2G");ButtonText(xpButton,"Buy XP / 4G");ButtonText(lockButton,owner.locked?"Unlock shop":"Lock shop");
            surrenderButton.interactable=OnlineInputReady;
            shopToggle.interactable=!owner.eliminated&&(pub.phase==MatchPhase.Preparation||pub.phase==MatchPhase.Combat);ButtonText(shopToggle,ShopExpanded?"Collapse shop":"Open shop"+(owner.locked?" / LOCKED":""));
            RenderOnlineShop(owner);RenderOnlineUnits(owner,pub);RenderOnlineHud(owner,pub);RenderOnlineCombat(owner,pub);RenderOnlineResults(owner,pub);RenderOnlineContext(owner,pub);
            UpdateTokenFeedback();
        }
        private void RenderOnlineShop(OwnerMatchState owner)
        {
            economyGold.text="GOLD "+owner.gold;var rules=onlineLevelRules;bool max=owner.level>=rules.MaxLevel;
            economyLevel.text="Lv "+owner.level+" · "+(max?"XP MAX":"XP "+owner.xp+" / "+rules.XPToNextLevel(owner.level));
            ShopXPProgress=max?1:Mathf.Clamp01(owner.xp/(float)rules.XPToNextLevel(owner.level));economyXP.rectTransform.sizeDelta=new Vector2(200*ShopXPProgress,4);
            economyLock.text=owner.locked?"[LOCKED]\nKeep next refresh":"[UNLOCKED]\nAuto refresh next round";
            economyStreak.text=owner.winStreak>0?"WIN STREAK "+owner.winStreak:owner.loseStreak>0?"LOSS STREAK "+owner.loseStreak:"STREAK —";economyDelta.text=Time.unscaledTime<economyMessageUntil?economyMessage:"";
            for(int i=0;i<shopCards.Length;i++){
                var offer=owner.shop[i];var card=shopViews[i];bool available=!string.IsNullOrEmpty(offer.definitionId);shopCards[i].interactable=OnlineInputReady&&available&&owner.gold>=offer.cost;
                if(!available){card.Name.text="NO OFFER";card.Price.text=card.Types.text=card.Owned.text="";card.Action.text="Wait for refresh";}
                else {var d=catalog.Get(offer.definitionId);card.Name.text=d.DisplayName;card.Price.text=offer.cost+"G · C"+offer.cost;card.Types.text=string.Join(" / ",d.TypeIds);
                    card.Owned.text=string.Join(" · ",Enumerable.Range(1,3).Select(r=>"R"+r+" "+owner.units.Count(u=>u.definitionId==d.Id&&u.rank==r)));
                    card.Action.text=owner.eliminated?"ELIMINATED":!onlineService.StateSynchronized||onlineService.PendingCommand!=null?"WAITING":owner.gold<offer.cost?"NOT ENOUGH GOLD":"BUY";
                    if(!shopSprites.TryGetValue(i,out var sprite)||sprite==null)shopSprites[i]=sprite=AddSprite(shopCards[i].transform,d.Id);if(sprite.PokemonId!=d.Id)sprite.Initialize(d.Id);sprite.ShowIdle(Time.unscaledTimeAsDouble);sprite.transform.localPosition+=new Vector3(-50,16,0);
                }
                if(shopSprites.TryGetValue(i,out var image))image.gameObject.SetActive(available);
                card.Action.color=shopCards[i].interactable?new Color(.5f,1,.7f):new Color(1,.65f,.4f);card.Stripe.color=available?Color.Lerp(new Color(.3f,.7f,1),new Color(1,.7f,.3f),offer.cost/5f):Color.gray;
            }
        }
        private void RenderOnlineUnits(OwnerMatchState owner,PublicMatchState pub)
        {
            foreach(var token in tokens.Values)token.gameObject.SetActive(false);
            bool combat=pub.phase==MatchPhase.Combat||onlineService.Combat.IsPlaying&&onlineService.Combat.Current?.round==pub.round;
            var observed=pub.players.FirstOrDefault(p=>p.id==onlineService.WatchingPlayerId);
            foreach(var slot in slots){slot.Graphic.color=slot.Color;if(slot.Placement.Kind==PlacementKind.Board)slot.Rect.gameObject.SetActive(!combat&&pub.phase==MatchPhase.Preparation);}
            if(!combat&&pub.phase==MatchPhase.Preparation&&observed!=null)foreach(var unit in observed.board)ShowOnlineToken(unit.id,unit.definitionId,unit.rank,UnitPlacement.OnBoard(new BoardPosition(unit.column,unit.row)));
            foreach(var unit in owner.units.Where(u=>u.placement==PlacementKind.Bench))ShowOnlineToken(unit.id,unit.definitionId,unit.rank,UnitPlacement.OnBench(unit.bench));
        }
        private void ShowOnlineToken(string id,string definition,int rank,UnitPlacement placement)
        {
            if(!tokens.ContainsKey(id))CreateToken(id);var token=tokens[id];token.gameObject.SetActive(true);token.anchoredPosition=FindSlot(placement).Rect.anchoredPosition;groups[id].alpha=id==dragged?.3f:1;
            if(!placementSprites.TryGetValue(id,out var sprite)||sprite==null)placementSprites[id]=sprite=AddSprite(token,definition);if(sprite.PokemonId!=definition)sprite.Initialize(definition);sprite.ShowIdle(Time.unscaledTimeAsDouble);
            tokenLabels[id].text=new string('★',rank);tokenLabels[id].color=Color.white;tokenLabels[id].fontSize=11;tokenLabels[id].rectTransform.anchoredPosition=new Vector2(0,25);tokenLabels[id].transform.SetAsLastSibling();
        }
        private void RenderOnlineContext(OwnerMatchState owner,PublicMatchState pub)
        {
            inventoryLabel.text="ITEMS ("+owner.inventory.Length+")\n"+(owner.inventory.Length==0?"No items":string.Join("\n",owner.inventory.Take(3)))+"\n"+ItemFeedbackText;
            var unit=owner.units.FirstOrDefault(u=>u.id==selectedUnitId);var board=pub.players.SelectMany(p=>p.board).FirstOrDefault(u=>u.id==selectedUnitId);var fighter=onlineService.Combat.Current?.units.FirstOrDefault(u=>u.id==selectedUnitId);
            string definition=unit?.definitionId??board?.definitionId??fighter?.definitionId;int rank=unit?.rank??board?.rank??fighter?.rank??1;
            if(definition==null){detailLabel.text="SELECT A UNIT\nInspect board, bench or combat.\n"+RankFeedbackText;sellButton.interactable=false;ButtonText(sellButton,"Sell");return;}
            var d=catalog.Get(definition);var stats=RankRules.Default.Apply(d.BaseStats,(UnitRank)rank);var items=unit?.items??board?.items??Array.Empty<string>();
            detailLabel.text=d.DisplayName+" "+RankBadge((UnitRank)rank)+"\n"+Readable(d.RoleId)+" · "+string.Join(" / ",d.TypeIds)+"\n"+(d.TraitIds.Count>0?"Traits: "+string.Join(", ",d.TraitIds)+"\n":"")+
                (fighter==null?"Base HP "+Number(stats.MaxHP):"HP "+Number(fighter.hp)+" / "+Number(fighter.maxHP)+" · S "+Number(fighter.shield))+"\nBase AT "+Number(stats.Attack)+" · Range "+stats.AttackRange+"\n"+ContextSkill(d,(UnitRank)rank,stats)+"\nItems ("+items.Length+"): "+(items.Length==0?"None":string.Join(", ",items))+"\n"+RankFeedbackText;
            sellButton.interactable=unit!=null&&OnlineInputReady&&pub.phase==MatchPhase.Preparation;ButtonText(sellButton,sellButton.interactable?"Sell / +"+unit.saleGold+"G":"Sell unavailable");
        }
    }
}
