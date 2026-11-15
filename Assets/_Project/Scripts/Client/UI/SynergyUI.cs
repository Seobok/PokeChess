using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private readonly Dictionary<string,PlacementHexGraphic> synergyTiles=new Dictionary<string,PlacementHexGraphic>();
        private RectTransform synergyRoot, synergyTooltip;
        private UnityEngine.UI.Button synergyDemoButton;
        private UnityEngine.UI.Text synergyHelp;
        private string synergyHovered;
        private BattleState synergyVisualBattle;
        private long synergyVisualTick = -1;
        private readonly Dictionary<string, (string text, float until)> synergyFlashes = new Dictionary<string, (string, float)>();
        private int synergyRockChoice, synergyProphecy;
        private UnityEngine.UI.Text synergyProgress;
        private VenomHud venomHud;
        private MysticShopVisuals mysticVisuals;
        private AdversityHud adversityHud;
        private UnityEngine.UI.Button synergySettingsButton;
        private UnityEngine.UI.Button[] adversityChoices;
        private int localMysticTier;
        private RectTransform synergySettings;
        private static string TraitName(string trait) => SynergyTraits.Name(trait);
        private static string TraitList(PokemonDefinition definition) => string.Join(" / ", definition.TraitIds.Select(TraitName));
        private static string SynergyDescription(string trait) => SynergyTraits.Description(trait);
        private void BuildSynergyUI()
        {
            synergyDemoButton = ActionButton("Synergy demo", new Vector2(180,25), new Vector2(-510,337), ResetSynergyDemo);
            header.rectTransform.anchoredPosition = new Vector2(235, 337);
            header.rectTransform.sizeDelta = new Vector2(280, 30); header.fontSize = 10;
            combatRoot.anchoredPosition = new Vector2(0,-28);
            combatInfo.rectTransform.anchoredPosition = new Vector2(235,365);
            combatInfo.rectTransform.sizeDelta = new Vector2(280,24); combatInfo.fontSize = 12;
            BindSynergyHud();
            venomHud = new VenomHud(canvasRect, synergyRoot, synergyProgress.font);
            var toggle = UiPrefab.Instantiate("SynergySettingsButton", canvasRect);
            synergySettingsButton = toggle.GetComponent<UnityEngine.UI.Button>();
            synergySettings = UiPrefab.Instantiate("SynergySettings", canvasRect);
            synergySettingsButton.onClick.AddListener(() => synergySettings.gameObject.SetActive(!synergySettings.gameObject.activeSelf));
            UiPrefab.Bind(synergySettings, "바위 1 위치 변경", () => ConfigureSynergyChoice(1, 0));
            UiPrefab.Bind(synergySettings, "바위 2 위치 변경", () => ConfigureSynergyChoice(1, 1));
            UiPrefab.Bind(synergySettings, "신비 예언 계열 변경", () => ConfigureSynergyChoice(2));
            adversityChoices = new[]
            {
                UiPrefab.Bind(synergySettings, "골드", () => ConfigureSynergyChoice(3, 0)),
                UiPrefab.Bind(synergySettings, "장비 조각", () => ConfigureSynergyChoice(3, 1)),
                UiPrefab.Bind(synergySettings, "상징 조각", () => ConfigureSynergyChoice(3, 2))
            };
            UiPrefab.Bind(synergySettings, "닫기", () => synergySettings.gameObject.SetActive(false));
            mysticVisuals = new MysticShopVisuals(synergyRoot, synergySettings,
                (RectTransform)rerollButton.transform, (RectTransform)shopCards[0].transform);
        }
        private void BuildAdversityUI()
        {
            adversityHud = new AdversityHud(canvasRect, synergyRoot, synergyProgress.font, resultTitle.rectTransform,
                economyGold.rectTransform, itemInventoryPanel, synergySettingsButton, adversityChoices,
                itemIcons?.Get(ItemCatalog.SitrusBerry));
        }

        private void ConfigureSynergyChoice(int mode,int slot=0)
        {
            if((IsOnlineMatch?onlineService.LocalMatchState?.phase:Match.Phase)!=MatchPhase.Preparation){Feedback("준비 단계에서 설정하세요.");return;}
            string value=null;int col=0,row=0;
            if(mode==1){var cells=PokeChess.Core.Board.HexBoardBounds.Preparation.Cells().Where(p=>IsOnlineMatch?!onlineService.LocalMatchState.units.Any(u=>u.placement==PlacementKind.Board&&u.column==p.Column&&u.row==p.Row):!Player.Units.Any(u=>u.Placement.Kind==PlacementKind.Board&&u.Placement.Position.Value.Equals(p))).ToArray();if(cells.Length==0)return;var tile=cells[synergyRockChoice++%cells.Length];col=tile.Column;row=tile.Row;}
            if(mode==2)value=SynergyTraits.Origins[synergyProphecy++%SynergyTraits.Origins.Count];
            if(IsOnlineMatch)SendOnlineCommand(MatchCommandKind.ConfigureSynergy,c=>{c.bench=mode;c.unitId=selectedUnitId;c.itemId=value;c.slot=slot;c.column=col;c.row=row;});
            else {var result=commands.ConfigureSynergy(mode,selectedUnitId,value,slot,col,row);Feedback(result.Accepted?(mode==1?"바위 "+(slot+1)+": "+col+", "+row:mode==2?"예언: "+SynergyTraits.Name(value):result.Message):result.Message);Render();}
        }
        private void RenderSynergyTiles(IEnumerable<BoardPosition> fire,IEnumerable<BoardPosition> rocks)
        {
            foreach(var tile in synergyTiles.Values)tile.gameObject.SetActive(false);
            foreach(var marker in fire.Select(p=>(p,fire:true))) {
                string key=(marker.fire?"fire":"rock")+marker.p.Column+":"+marker.p.Row;
                if(!synergyTiles.TryGetValue(key,out var image)){var node=Rect("SynergyTile_"+key,combatRoot,new Vector2(44,50),Vector2.zero);image=node.gameObject.AddComponent<PlacementHexGraphic>();image.raycastTarget=false;synergyTiles[key]=image;node.SetSiblingIndex(56);Label("Kind",node,marker.fire?"불길":"바위",9,new Vector2(40,15),new Vector2(0,-16));}
                image.gameObject.SetActive(true);image.rectTransform.anchoredPosition=CombatPoint(marker.p);image.color=marker.fire?new Color(1,.24f,.04f,.5f):new Color(.65f,.65f,.7f,.45f);
            }
        }
        private void RenderLocalSynergies()
        {
            synergyDemoButton.gameObject.SetActive(true);
            var progress = ViewedPlayer.Synergy;
            synergyProgress.text = "표본 " + progress.VenomSamples;
            if (!adversityHud.HasArt)
                synergyProgress.text += $"\n투지 {progress.Resolve} · 조각 {progress.EquipmentFragments}/{progress.EmblemFragments}";
            if (progress.FreeRerollRound == Match.RoundNumber) synergyProgress.text += " · 무료 리롤";
            var battle = Match.Phase == MatchPhase.Combat ? RoundLoop.GetBattleFor(localViewedPlayerId)?.Battle : null;
            if (synergyVisualBattle != battle) { synergyVisualBattle = battle; synergyVisualTick = -1; synergyFlashes.Clear(); }
            if (battle != null && synergyVisualTick != battle.CurrentTick)
            {
                synergyVisualTick = battle.CurrentTick;
                foreach (var e in battle.Synergies.EventsThisTick.Where(e=>!e.Cell.HasValue&&e.Detail!="FIRST_HIT"&&e.Detail!="GUARD_START")) synergyFlashes[e.TargetId] = (e.Detail, Time.unscaledTime + .65f);
                foreach (var e in battle.Items.EventsThisTick) synergyFlashes[e.TargetId] = (e.ItemId==ItemCatalog.RockyHelmet?"REFLECT":"ITEM",Time.unscaledTime+.65f);
            }
            RenderSynergyTiles(battle?.Synergies.FireTiles??Array.Empty<BoardPosition>(),battle==null?Array.Empty<BoardPosition>():battle.Synergies.RockTiles(1).Concat(battle.Synergies.RockTiles(2)));
            var own = battle?.Units.FirstOrDefault(u => u.OwnerPlayerId == localViewedPlayerId);
            var counts = own == null ? SynergyPreparation.Count(ViewedPlayer,catalog) : battle.Synergies.TeamCounts(own.TeamId);
            int earthTier=counts.First(c=>c.TraitId==SynergyTraits.Earth).Tier;
            var preview=Match.Phase==MatchPhase.Preparation&&earthTier>0?SynergyPreparation.PreparationRocks(ViewedPlayer,catalog):Enumerable.Empty<BoardPosition>();
            RenderEarthPreparation(preview);
            RenderNatureGuardian(Match.Phase == MatchPhase.Preparation ? SynergyPreparation.PreparationGuardian(ViewedPlayer, catalog) : null,
                counts.First(count => count.TraitId == SynergyTraits.Nature).Tier);
            int venomTier=counts.First(c=>c.TraitId==SynergyTraits.Venom).Tier;
            int samples=ViewedPlayer.Synergy.VenomSamples;
            int research=own!=null?battle.Synergies.VenomResearch(own.TeamId):venomTier>=6&&samples>=120?120:venomTier>=4&&samples>=40?40:venomTier>=2&&samples>=10?10:0;
            synergyProgress.text+=" · 연구 "+(research==0?"없음":research.ToString());
            venomHud.Show(Match, localViewedPlayerId, Match.RoundNumber, battle != null ? research : 0,
                Match.Phase == MatchPhase.Result && ViewedPlayer.Synergy.VenomMinimumRewardRound == Match.RoundNumber);
            bool ownView = localViewedPlayerId == Player.PlayerId;
            adversityHud.Show(Match, localViewedPlayerId, Match.RoundNumber, Match.Phase,
                ownView && Match.Phase != MatchPhase.Finished, ownView && !Player.IsEliminated,
                counts.First(count => count.TraitId == SynergyTraits.Adversity).Tier, progress.Resolve,
                progress.EquipmentFragments, progress.EmblemFragments, ownView ? progress.RewardChoices : 0,
                progress.LastAdversitySettlement, progress.LastAdversityChoice);
            localMysticTier = localViewedPlayerId == "p1" ? counts.First(c => c.TraitId == SynergyTraits.Mystic).Tier : 0;
            RenderLocalMystic();
            RenderSynergies(counts, Match.Phase != MatchPhase.Finished);
        }
        private string LocalSynergyFlash(UnitCombatState unit) => synergyFlashes.TryGetValue(unit.UnitInstanceId, out var flash) && flash.until > Time.unscaledTime ? flash.text : "";
        private void RenderOnlineSynergies(PublicMatchState pub)
        {
            synergyDemoButton.gameObject.SetActive(false);
            var progress = onlineService.LocalMatchState;
            synergyProgress.text = progress == null ? "" : "표본 " + progress.venomSamples;
            if (progress != null && !adversityHud.HasArt)
                synergyProgress.text += $"\n투지 {progress.resolve} · 조각 {progress.equipmentFragments}/{progress.emblemFragments}";
            if (progress != null && progress.freeRerollRound == progress.round) synergyProgress.text += " · 무료 리롤";
            var frame = onlineService.Combat.Current;
            var member=frame?.units.FirstOrDefault(u=>u.owner==onlineService.WatchingPlayerId);
            if(pub.phase==MatchPhase.Combat&&member!=null)synergyProgress.text+=" · 연구 "+(member.venomResearch==0?"없음":member.venomResearch.ToString());
            string watching = onlineService.WatchingPlayerId;
            venomHud.Show(pub.matchId, watching, pub.round,
                pub.phase == MatchPhase.Combat && frame?.round == pub.round ? member?.venomResearch ?? 0 : 0,
                pub.phase == MatchPhase.Result && progress?.playerId == watching && progress.venomMinimumRewardRound == pub.round);
            bool combat=pub.phase==MatchPhase.Combat&&frame!=null&&frame.round==pub.round;
            RenderSynergyTiles(combat?(frame.synergyFire??Array.Empty<CombatCellView>()).Select(p=>new BoardPosition(p.column,p.row)):Array.Empty<BoardPosition>(),combat?(frame.synergyRocksOne??Array.Empty<CombatCellView>()).Concat(frame.synergyRocksTwo??Array.Empty<CombatCellView>()).Select(p=>new BoardPosition(p.column,p.row)):Array.Empty<BoardPosition>());
            IReadOnlyList<SynergyCount> counts;
            if (pub.phase == MatchPhase.Combat && frame != null && frame.round == pub.round)
                counts = SynergySystem.CountAssignments(frame.units.Where(u => u.owner == onlineService.WatchingPlayerId && !u.isSynergySummon).Select(u => new KeyValuePair<string,IReadOnlyList<string>>(u.definitionId,u.synergyIds??SynergyTraits.ForPokemon(u.definitionId))));
            else
            {
                var player = pub.players.FirstOrDefault(p => p.id == onlineService.WatchingPlayerId);
                counts = SynergySystem.CountAssignments((player?.board ?? Array.Empty<PublicBoardUnit>()).Select(u => new KeyValuePair<string,IReadOnlyList<string>>(u.definitionId,u.synergyIds??SynergyTraits.ForPokemon(u.definitionId))));
            }
            int earthTier=counts.First(c=>c.TraitId==SynergyTraits.Earth).Tier;
            var preview=pub.phase==MatchPhase.Preparation&&earthTier>0&&progress!=null&&onlineService.WatchingPlayerId==progress.playerId
                ?(progress.rockTiles??Array.Empty<CombatCellView>()).Where(p=>!progress.units.Any(u=>u.placement==PlacementKind.Board&&u.column==p.column&&u.row==p.row)).Take(earthTier>=4?2:1).Select(p=>new BoardPosition(p.column,p.row))
                :Enumerable.Empty<BoardPosition>();
            RenderEarthPreparation(preview);
            var guardian = pub.phase == MatchPhase.Preparation && progress != null &&
                onlineService.WatchingPlayerId == progress.playerId ? progress.guardianTile : null;
            RenderNatureGuardian(guardian != null ? new BoardPosition(guardian.column, guardian.row) : (BoardPosition?)null,
                counts.First(count => count.TraitId == SynergyTraits.Nature).Tier);
            if (progress != null)
            {
                int tier = watching == progress.playerId ? counts.First(c => c.TraitId == SynergyTraits.Mystic).Tier : 0;
                mysticVisuals.Show(pub.matchId, progress.playerId, pub.round, pub.phase, tier, progress.mysticTier,
                    progress.mysticBattles, progress.freeRerollRound, progress.freeRerollConsumedRound,
                    progress.prophecyOrigin, progress.shop.FirstOrDefault(slot => slot.index == 0)?.prophecyOrigin);
            }
            bool ownView = progress != null && watching == progress.playerId;
            adversityHud.Show(pub.matchId, progress?.playerId, pub.round, pub.phase,
                ownView && pub.phase != MatchPhase.Finished, ownView && OnlineInputReady && !progress.eliminated,
                counts.First(count => count.TraitId == SynergyTraits.Adversity).Tier, progress?.resolve ?? 0,
                progress?.equipmentFragments ?? 0, progress?.emblemFragments ?? 0, progress?.synergyRewardChoices ?? 0,
                progress?.adversitySettlement ?? default, progress?.adversityChoice ?? default);
            RenderSynergies(counts, pub.phase != MatchPhase.Finished);
        }
        private void RenderLocalMystic()
        {
            if (mysticVisuals == null || Match == null) return;
            var progress = Player.Synergy;
            mysticVisuals.Show(Match, Player.PlayerId, Match.RoundNumber, Match.Phase, localMysticTier, progress.MysticTier,
                progress.MysticBattles, progress.FreeRerollRound, progress.FreeRerollConsumedRound,
                progress.ProphecyOrigin, Player.Shop.Slots[0].ProphecyOrigin);
        }

        public void ResetSynergyDemo()
        {
            if (IsOnlineMatch) return;
            CancelDrag("Synergy demo."); catalog = PokeChess.Content.GameContent.CreateCatalog();
            Match = MatchStateFactory.CreateWithPool("synergy-demo", new[] { "p1", "p2" }, catalog, PokeChess.Content.GameContent.CreateDefinitions().Select(d => d.Id), matchSeed: 123);
            string[] species = { "slowpoke", "geodude", "squirtle", "shellder", "chansey", "bulbasaur" };
            var positions = new[] { new BoardPosition(1,3), new BoardPosition(3,3), new BoardPosition(1,0), new BoardPosition(3,1), new BoardPosition(4,3), new BoardPosition(5,3) };
            foreach (var player in Match.Players)
            {
                player.SetProgress(20,0,6);
                for (int i=0;i<species.Length;i++) SharedPoolSystem.RegisterUnit(Match,catalog.CreateUnit(player.PlayerId+"-synergy-"+i,species[i],player.PlayerId,UnitRank.One,0,UnitPlacement.OnBoard(positions[i])),species[i]);
            }
            Match.TransitionTo(MatchPhase.Starting); Match.TransitionTo(MatchPhase.Preparation);
            AttachMinimumMatch(); LastResult=null; Feedback("시너지 데모: 격류 / 수호자. 시너지에 마우스를 올려 효과를 확인하세요."); Render();
        }
    }
}


