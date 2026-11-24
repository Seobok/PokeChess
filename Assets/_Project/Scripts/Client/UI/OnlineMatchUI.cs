using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using PokeChess.Core.Match;
using PokeChess.Core.Battle;
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
        private readonly ShopRules onlineShopRules = new ShopRules();
        public bool IsOnlineMatch {get;private set;}
        public string OnlineObservedPlayerId=>onlineService?.WatchingPlayerId;
        private bool OnlineInputReady=>IsOnlineMatch&&onlineService.State==ConnectionState.Connected&&onlineService.StateSynchronized&&onlineService.PendingCommand==null&&onlineService.LocalMatchState?.eliminated==false&&onlineService.PublicState?.phase!=MatchPhase.Finished;
        private bool OnlineShopExpanded=>onlineService?.LocalMatchState!=null&&!onlineService.LocalMatchState.eliminated&&(onlineService.PublicState.phase==MatchPhase.Preparation||onlineService.PublicState.phase.IsBattlePhase())&&!shopCollapsed;
        private static string PlayerLabel(string id,string own)=>id==own?"YOU":id?.StartsWith("wild-")==true?"WILD / "+id.Substring(5):"Player "+(id??"").Substring(Math.Max(0,(id?.Length??0)-6));

        public void EnterOnlineMatch(OnlineConnection connection)
        {
            if(IsOnlineMatch&&onlineMatchId==connection.StartInfo?.matchId)return;
            CancelDrag("Online match starting.");Simulation?.Cancel();Simulation=null;
            savedLocalCatalog=catalog;catalog=PokeChess.Content.GameContent.CreateCatalog();onlineService=connection;onlineMatchId=connection.StartInfo.matchId;
            IsOnlineMatch=true;enabled=true;canvasRect.gameObject.SetActive(true);previousOwner=null;selectedUnitId=null;shopPhase=null;shopCollapsed=false;presentationRound=-1;eliminationDismissed=false;resultExpanded=false;
            ClearOnlineFighters();ClearSpectatorRows();hudMatch=null;
            if(onlineControls==null)BuildOnlineControls();onlineControls.SetActive(true);
            SetLocalControls(false);ButtonText(newMatchButton,"Return to rooms");newMatchButton.onClick.RemoveAllListeners();newMatchButton.onClick.AddListener(LeaveOnlineMatch);
            onlineService.Combat.EventPlayed+=ShowOnlineEffect;RenderOnlineMatch();
        }
        public void ExitOnlineMatch()
        {
            if(!IsOnlineMatch)return;onlineService.Combat.EventPlayed-=ShowOnlineEffect;IsOnlineMatch=false;
            CloseSynergyTarget();
            ClearDragState();ClearOnlineFighters();catalog=savedLocalCatalog;previousOwner=null;onlineMatchId=null;selectedUnitId=null;
            onlineControls.SetActive(false);SetLocalControls(true);hudMatch=null;shopPhase=null;
            newMatchButton.onClick.RemoveAllListeners();newMatchButton.onClick.AddListener(ResetMatchUI);ButtonText(newMatchButton,"New match");Render();
            canvasRect.gameObject.SetActive(false);enabled=false;
        }
        public void ShowLocalMatch(){if(IsOnlineMatch)return;enabled=true;canvasRect.gameObject.SetActive(true);Render();}
        private void SetLocalControls(bool active)
        {
            string[] names={"DebugTitle","DebugState","P01-P04 demo","P05-P08 demo","P09-P12 demo","Reset match","Rank demo","Full bench","Fixed foe","4 players","6 players","8 players","3 players","5 players","7 players","DEV: Start battle","Auto play 20","Fast simulation","Save report"};
            foreach(var name in names){var node=canvasRect.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t=>t.name==name);if(node!=null)node.gameObject.SetActive(active);}
            roundButton.gameObject.SetActive(active);
            SimulationStartButton.gameObject.SetActive(active);SimulationSpeedButton.gameObject.SetActive(active);simulationPanel.gameObject.SetActive(false);simulationInfo.gameObject.SetActive(false);
            foreach(var button in canvasRect.GetComponentsInChildren<UnityEngine.UI.Button>(true))if(button.name.Contains("simulat")||button.name.StartsWith("Run ")||button.name=="Copy report")button.gameObject.SetActive(active);
        }
        private void BuildOnlineControls()
        {
            onlineControls=Rect("OnlineMatchControls",developerPanel,new Vector2(225,170),new Vector2(0,-110)).gameObject;
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
        {var button=ActionButton(name,new Vector2(225,28),new Vector2(0,y),action);button.transform.SetParent(onlineControls.transform,false);return button;}
        private async void LeaveOnlineMatch(){await onlineService.LeaveAsync();}
        public void WatchOnlinePlayer(string id)
        {
            if (!IsOnlineMatch || onlineService.IsRecovering || !onlineService.WatchPlayer(id)) return;
            CloseSynergyTarget();
            ClearDragState();
            selectedUnitId = hoveredUnitId = null;
            hoveredShopSlot = -1;
            unitInfoPinned = false;
            HideUnitInfoHint();
            ClearOnlineFighters();
            Feedback(id == onlineService.LocalMatchState.playerId ? "Your board" : "Observing public board / combat");
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
            var beforeMove=onlineService.LocalMatchState.units.FirstOrDefault(u=>u.id==command.unitId);
            bool moved=kind==MatchCommandKind.Move&&beforeMove!=null&&(beforeMove.placement!=command.destination||(command.destination==PlacementKind.Board?(beforeMove.column!=command.column||beforeMove.row!=command.row):beforeMove.bench!=command.bench));
            try{
                var ack=await onlineService.SubmitCommandAsync(command);
                if(IsOnlineMatch&&onlineMatchId==command.matchId&&ack?.accepted==true){
                    if(kind==MatchCommandKind.Buy || kind==MatchCommandKind.Sell)PlayActionSound(ActionSoundKind.Purchase);
                    else if(moved || kind==MatchCommandKind.ConfigureSynergy && (command.bench==1 || command.bench==SynergyPreparation.GuardianPlacementMode))PlayActionSound(ActionSoundKind.Placement);
                }
            }catch(Exception e){Feedback(e.Message);}
        }
        private void SelectOnlineUnit(string id)
        {if(IsDragging)return;if(!selectingHover){unitInfoPinned=true;hoveredUnitId=id;hoveredShopSlot=-1;}selectedUnitId=id;RenderOnlineMatch();}
        private bool CanOnlinePlace(OwnerUnit unit,UnitPlacement destination)
        {
            if(!OnlineInputReady||unit==null||onlineService.WatchingPlayerId!=onlineService.LocalMatchState.playerId)return false;
            return onlineService.PublicState.phase==MatchPhase.Preparation||onlineService.PublicState.phase.IsBattlePhase()&&unit.placement==PlacementKind.Bench&&destination.Kind==PlacementKind.Bench;
        }
        private bool CanOnlineSell(OwnerUnit unit)=>unit!=null&&OnlineInputReady&&onlineService.WatchingPlayerId==onlineService.LocalMatchState.playerId&&(onlineService.PublicState.phase==MatchPhase.Preparation||onlineService.PublicState.phase.IsBattlePhase()&&unit.placement==PlacementKind.Bench);
        private UnitPlacement OnlinePlacement(OwnerUnit unit)=>unit.placement==PlacementKind.Board?UnitPlacement.OnBoard(new BoardPosition(unit.column,unit.row)):UnitPlacement.OnBench(unit.bench);
        private void BeginOnlineDrag(string id,Vector2 point)
        {
            var owner=onlineService.LocalMatchState;var unit=owner?.units.FirstOrDefault(u=>u.id==id);
            if(unit==null||!CanOnlinePlace(unit,OnlinePlacement(unit))){Feedback("Placement unavailable for this view or phase.");return;}
            dragged=id;dragRevision=owner.placementRevision;ShowDraggedPokemon(unit.definitionId,OnlinePlacement(unit));DragOnline(point);
        }
        private void DragOnline(Vector2 point)
        {
            if(!IsDragging)return;RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,point,null,out var local);ghost.anchoredPosition=local;
            var unit=onlineService.LocalMatchState.units.FirstOrDefault(u=>u.id==dragged);var destination=Hit(point);
            int? price = CanOnlineSell(unit) ? unit.saleGold : (int?)null;
            UpdatePlacementDragArt(point, destination, destination != null && CanDropOnlineUnit(unit, destination.Placement), price);
            if (sellHovered) Feedback(price.HasValue ? $"Release to sell / +{price.Value}G" : "Sale unavailable for this unit");
        }
        private void EndOnlineDrag(Vector2 point)
        {
            if(IsDragging&&onlineService.LocalMatchState?.placementRevision!=dragRevision){CancelDrag("State changed; drag cancelled.");return;}
            if(!IsDragging)return;string id=dragged;var unit=onlineService.LocalMatchState.units.FirstOrDefault(u=>u.id==id);var destination=Hit(point);
            dragged=null;ClearPlacementDragArt();ghost.gameObject.SetActive(false);RenderPlacementArt();
            if(HitSellDrop(point)){if(CanOnlineSell(unit))SendOnlineCommand(MatchCommandKind.Sell,c=>c.unitId=id);return;}
            if(destination!=null&&CanDropOnlineUnit(unit,destination.Placement))SendOnlineCommand(MatchCommandKind.Move,c=>{c.unitId=id;c.destination=destination.Placement.Kind;c.column=destination.Placement.Position?.Column??0;c.row=destination.Placement.Position?.Row??0;c.bench=destination.Placement.BenchSlot??0;});
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
                if(owner.inventory.Length>previousOwner.inventory.Length){itemNotice="+"+(owner.inventory.Length-previousOwner.inventory.Length)+" 도구 획득 / 회수";itemNoticeUntil=Time.unscaledTime+2;}
            }
            previousOwner=owner;
            string watched = onlineService.WatchingPlayerId;
            if (pub.phase != MatchPhase.Finished && onlineService.StateSynchronized &&
                (watched != owner.playerId || eliminationDismissed && !onlineService.Combat.IsPlaying) &&
                pub.players.Any(player => player.id == watched && player.eliminated))
            {
                var next = pub.players.FirstOrDefault(player => !player.eliminated);
                if (next != null) WatchOnlinePlayer(next.id);
            }
            if(shopPhase!=pub.phase){if(pub.phase==MatchPhase.Preparation)shopCollapsed=false;shopPhase=pub.phase;}
            bool finished=pub.phase==MatchPhase.Finished;
            shopFrame.gameObject.SetActive(!finished);header.gameObject.SetActive(!finished&&!pub.phase.IsBattlePhase());resources.gameObject.SetActive(false);
            header.text="ONLINE / "+PlayerLabel(onlineService.WatchingPlayerId,owner.playerId)+" / BOARD "+owner.units.Count(u=>u.placement==PlacementKind.Board)+" / "+owner.BoardCapacity;
            status.text=onlineService.IsRecovering?onlineService.RecoveryFeedback:onlineService.PendingCommand!=null?"Command pending — retry checks the same command":!onlineService.StateSynchronized?"Synchronizing state…":onlineService.CommandFeedback??LastFeedback??"State synchronized.";
            onlineRetry.gameObject.SetActive(onlineService.PendingCommand!=null);onlineRetry.interactable=!onlineService.IsRecovering;onlineSync.interactable=!onlineService.IsRecovering&&onlineService.PendingCommand==null;
            bool economy=OnlineInputReady&&(pub.phase==MatchPhase.Preparation||pub.phase.IsBattlePhase());
            rerollButton.interactable = economy && (owner.gold >= onlineShopRules.RerollGoldCost || SynergyProgress.IsFreeRerollAvailable(owner.freeRerollRound, owner.round));
            xpButton.interactable = economy && owner.gold >= onlineLevelRules.PurchaseGoldCost && owner.level < onlineLevelRules.MaxLevel;
            lockButton.interactable = economy;
            surrenderButton.interactable=OnlineInputReady;
            shopToggle.interactable=!owner.eliminated&&(pub.phase==MatchPhase.Preparation||pub.phase.IsBattlePhase());ButtonText(shopToggle,ShopExpanded?"Collapse shop":"Open shop"+(owner.locked?" / LOCKED":""));
            RenderOnlineShop(owner);RenderOnlineUnits(owner,pub);RenderOnlineHud(owner,pub);RenderOnlineCombat(owner,pub);RenderOnlineResults(owner,pub);RenderOnlineContext(owner,pub);
            RenderOnlineSynergies(pub);
            if (guardianPreviewBoard != null)
                SkillVisualBattleView.SortBoardDepth(canvasRect,
                    guardianPreviewBoard.Select(unit => tokens[unit.id]).Concat(earthPreparation.VisibleRocks).Concat(VisibleNatureGuardian()));
            RenderItemWildUI();
            RenderEvolutionUI();
            RenderUnitCryAudio();
            UpdateTokenFeedback();
        }
        private void RenderOnlineShop(OwnerMatchState owner)
        {
            RenderShopToolbar(owner.gold, owner.level, owner.xp, owner.winStreak, owner.loseStreak,
                owner.locked, SynergyProgress.IsFreeRerollAvailable(owner.freeRerollRound, owner.round), onlineLevelRules, onlineShopRules);
            for(int i=0;i<shopCards.Length;i++){
                var offer=owner.shop[i];var card=shopViews[i];bool available=!string.IsNullOrEmpty(offer.definitionId);shopCards[i].gameObject.SetActive(ShopExpanded);shopCards[i].interactable=OnlineInputReady&&available&&offer.canPurchase;
                if(!available){card.Name.text="";card.Price.text=card.Types.text="";}
                else {var d=catalog.Get(offer.definitionId);card.Name.text=PokeChess.Content.GameContent.DisplayName(d.Id) ?? d.DisplayName;card.Price.text=ShopPurchasePrice(offer.purchasePrice, offer.purchaseQuantity);RenderShopCardTraits(card, d);}
                bool owned = available && owner.units.Any(unit => unit.definitionId == offer.definitionId);
                SetShopPurchaseEffect(card, owned, available ? (UnitRank)offer.projectedRank : UnitRank.One);
                RenderShopPortrait(i, offer.definitionId);
                ApplyShopCardArt(i, available ? offer.cost : 0);
            }
        }
        private PublicBoardUnit[] guardianPreviewBoard;
        private void RenderOnlineUnits(OwnerMatchState owner,PublicMatchState pub)
        {
            foreach(var token in tokens.Values)token.gameObject.SetActive(false);
            bool combat=pub.phase.IsBattlePhase()||onlineService.Combat.IsPlaying&&onlineService.Combat.Current?.round==pub.round;
            var observed=pub.players.FirstOrDefault(p=>p.id==onlineService.WatchingPlayerId);
            foreach(var slot in slots){slot.Graphic.color=slot.Color;if(slot.Placement.Kind==PlacementKind.Board)slot.Rect.gameObject.SetActive(!combat&&pub.phase==MatchPhase.Preparation);}
            if(!combat&&pub.phase==MatchPhase.Preparation&&observed!=null)foreach(var unit in observed.board){ShowOnlineToken(unit.id,unit.definitionId,unit.rank,UnitPlacement.OnBoard(new BoardPosition(unit.column,unit.row)));RenderEquippedItemIcon(tokens[unit.id],unit.items);RenderPreparationVitals(tokens[unit.id],unit.definitionId,unit.rank,observed.id==owner.playerId);}
            var previewBoard=!combat&&pub.phase==MatchPhase.Preparation?observed?.board:null;
            if(guardianPreviewBoard!=previewBoard || (previewBoard==null && (guardianLinks.Count>0||stormLinks.Count>0)))
            {
                guardianPreviewBoard=previewBoard;
                var roster=previewBoard==null?Array.Empty<UnitInstance>():previewBoard.Select(u=>
                    catalog.CreateUnit(u.id,u.definitionId,observed.id,(UnitRank)u.rank,u.evolutionStage,
                        UnitPlacement.OnBoard(new BoardPosition(u.column,u.row)),u.items)).ToArray();
                RefreshGuardianLinks(PokeChess.Core.Battle.SynergySystem.PreviewGuardianLinks(roster,catalog));
                RefreshStormLinks(roster);
            }
            AnimateGuardianLinks();
            AnimateStormLinks();
            foreach(var unit in owner.units.Where(u=>u.placement==PlacementKind.Bench)){ShowOnlineToken(unit.id,unit.definitionId,unit.rank,UnitPlacement.OnBench(unit.bench));RenderEquippedItemIcon(tokens[unit.id],unit.items);RenderPreparationVitals(tokens[unit.id],unit.definitionId,unit.rank,true);}
        }
        private void ShowOnlineToken(string id,string definition,int rank,UnitPlacement placement)
        {
            if(!tokens.ContainsKey(id))CreateToken(id);var token=tokens[id];token.gameObject.SetActive(true);token.anchoredPosition=FindSlot(placement).Rect.anchoredPosition;groups[id].alpha=id==dragged?.3f:1;
            if(!placementSprites.TryGetValue(id,out var sprite)||sprite==null)placementSprites[id]=sprite=AddSprite(token,definition);if(sprite.PokemonId!=definition)sprite.Initialize(definition);sprite.ShowIdle(Time.unscaledTimeAsDouble);
            tokenLabels[id].text="";
        }
        private void RenderOnlineContext(OwnerMatchState owner,PublicMatchState pub)
        {
            inventoryLabel.font=itemFont;
            inventoryLabel.text="ITEMS ("+owner.inventory.Length+")\n"+(owner.inventory.Length==0?"No items":string.Join("\n",owner.inventory.Take(3).Select(ItemCatalog.DisplayName)))+"\n"+ItemFeedbackText;
            if (RenderNatureGuardianInfo()) return;
            var unit=owner.units.FirstOrDefault(u=>u.id==selectedUnitId);var board=pub.players.SelectMany(p=>p.board).FirstOrDefault(u=>u.id==selectedUnitId);
            var activeFrame=onlineService.Combat.Current;
            var fighter=activeFrame?.round==pub.round&&(pub.phase.IsBattlePhase()||onlineService.Combat.IsPlaying)?activeFrame.units.FirstOrDefault(u=>u.id==selectedUnitId):null;
            string definition=unit?.definitionId??board?.definitionId??fighter?.definitionId;int rank=unit?.rank??board?.rank??fighter?.rank??1;
            if(definition==null){RenderUnitInfoIcons(null,UnitRank.One,null,0,0,0,0,0,Array.Empty<string>());sellButton.interactable=false;ButtonText(sellButton,"판매");return;}
            var d = catalog.Get(definition);
            int natureTier = fighter?.isSynergySummon == true ? fighter.natureTier : 0;
            var stats = natureTier > 0 ? SynergySystem.NatureGuardianDefinition(natureTier).BaseStats : RankRules.Default.Apply(d, (UnitRank)rank);
            if(fighter!=null&&definition==WildRoundSystem.BossId&&fighter.id.StartsWith("wild-",StringComparison.Ordinal))
                stats=WildRoundSystem.BossStats(activeFrame.round);
            var items = unit?.items ?? board?.items ?? fighter?.items ?? Array.Empty<string>();
            detailLabel.font=itemFont;
            detailLabel.text=d.DisplayName+" "+RankBadge((UnitRank)rank)+"\n"+Readable(d.RoleId)+" · "+string.Join(" / ",d.TypeIds)+"\n"+(d.TraitIds.Count>0?"Traits: "+string.Join(", ",d.TraitIds)+"\n":"")+
                (fighter==null?"Base HP "+Number(stats.MaxHP):"HP "+Number(fighter.hp)+" / "+Number(fighter.maxHP)+" · S "+Number(fighter.shield))+"\nBase AT "+Number(stats.Attack)+" · Range "+stats.AttackRange+"\n"+ContextSkill(d,(UnitRank)rank,stats)+"\nItems ("+items.Length+"): "+(items.Length==0?"None":string.Join(", ",items.Select(ItemCatalog.DisplayName)))+"\n"+RankFeedbackText;
            RenderUnitInfoIcons(d,(UnitRank)rank,stats,fighter?.hp??stats.MaxHP,fighter?.maxHP??stats.MaxHP,fighter?.energy??stats.StartingEnergy,fighter?.maxEnergy??stats.MaxEnergy,fighter?.shield??0,items,true,natureTier,saleGold:unit?.saleGold);
            sellButton.interactable=CanOnlineSell(unit);ButtonText(sellButton,sellButton.interactable?"판매 / +"+unit.saleGold+"G":"판매 불가");
        }
    }
}
