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
        private readonly Dictionary<string, UnityEngine.UI.Text> synergyLabels = new Dictionary<string, UnityEngine.UI.Text>();
        private readonly Dictionary<string, UnityEngine.UI.Image> synergyColors = new Dictionary<string, UnityEngine.UI.Image>();
        private RectTransform synergyRoot, synergyTooltip;
        private UnityEngine.UI.Button synergyDemoButton;
        private UnityEngine.UI.Text synergyHelp;
        private string synergyHovered;
        private BattleState synergyVisualBattle;
        private long synergyVisualTick = -1;
        private readonly Dictionary<string, (string text, float until)> synergyFlashes = new Dictionary<string, (string, float)>();
        private UnityEngine.UI.Button synergyNext;
        private int synergyPage, synergyChoice, synergyRockChoice, synergyProphecy;
        private UnityEngine.UI.Text synergyProgress;
        private RectTransform synergySettings;
        private IReadOnlyList<SynergyCount> lastSynergyCounts=Array.Empty<SynergyCount>();
        public string SynergyDisplay => string.Join(" | ",synergyLabels.Where(p=>synergyColors[p.Key].gameObject.activeSelf).Select(p=>p.Value.text));
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
            synergyRoot = Rect("Synergies", canvasRect, new Vector2(155,400), new Vector2(-540,80));
            synergyNext=ActionButton("Next traits",new Vector2(145,25),Vector2.zero,()=>{
                synergyPage=(synergyPage+1)%Math.Max(1,(lastSynergyCounts.Count+7)/8);
                synergyHovered=null;synergyTooltip.gameObject.SetActive(false);RenderSynergies(lastSynergyCounts,true);
            });
            synergyNext.transform.SetParent(synergyRoot,false);synergyNext.gameObject.SetActive(false);
            synergyTooltip = Panel("SynergyHelp", new Vector2(510, 370), new Vector2(-290, 130));
            synergyTooltip.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            synergyHelp = Label("Description", synergyTooltip, "", 12, new Vector2(490, 350), Vector2.zero);
            synergyTooltip.gameObject.SetActive(false);
            synergyProgress=Label("SynergyProgress",synergyRoot,"",10,new Vector2(155,62),new Vector2(0,-258));
            var settings=ActionButton("시너지 설정",new Vector2(145,25),new Vector2(-540,-142),()=>synergySettings.gameObject.SetActive(!synergySettings.gameObject.activeSelf));
            synergySettings=Panel("SynergySettings",new Vector2(440,310),new Vector2(-210,-30));
            Label("Help",synergySettings,"준비 단계 설정 · 선택한 폴리곤/라프라스의 대상 지정\n버튼을 다시 누르면 다음 후보로 변경됩니다.",12,new Vector2(420,45),new Vector2(0,125));
            AddSynergySetting("선택 유닛의 대상 변경",new Vector2(0,78),()=>ConfigureSynergyChoice(0));
            AddSynergySetting("바위 1 위치 변경",new Vector2(-105,37),()=>ConfigureSynergyChoice(1,0));
            AddSynergySetting("바위 2 위치 변경",new Vector2(105,37),()=>ConfigureSynergyChoice(1,1));
            AddSynergySetting("신비 예언 계열 변경",new Vector2(0,-4),()=>ConfigureSynergyChoice(2));
            AddSynergySetting("역경: 골드",new Vector2(-130,-48),()=>ConfigureSynergyChoice(3,0),125);
            AddSynergySetting("역경: 장비 조각",new Vector2(0,-48),()=>ConfigureSynergyChoice(3,1),125);
            AddSynergySetting("역경: 상징 조각",new Vector2(130,-48),()=>ConfigureSynergyChoice(3,2),125);
            AddSynergySetting("닫기",new Vector2(0,-108),()=>synergySettings.gameObject.SetActive(false));
            synergySettings.gameObject.SetActive(false);
        }
        private void AddSynergySetting(string label,Vector2 position,Action action,float width=200)
        {
            var button=ActionButton(label,new Vector2(width,30),Vector2.zero,action);button.transform.SetParent(synergySettings,false);((RectTransform)button.transform).anchoredPosition=position;
        }
        private void ConfigureSynergyChoice(int mode,int slot=0)
        {
            if((IsOnlineMatch?onlineService.LocalMatchState?.phase:Match.Phase)!=MatchPhase.Preparation){Feedback("준비 단계에서 설정하세요.");return;}
            string value=null;int col=0,row=0;
            if(mode==0){var ids=IsOnlineMatch?onlineService.LocalMatchState.units.Where(u=>u.placement==PlacementKind.Board&&u.id!=selectedUnitId).Select(u=>u.id).ToArray():Player.Units.Where(u=>u.Placement.Kind==PlacementKind.Board&&u.InstanceId!=selectedUnitId).Select(u=>u.InstanceId).ToArray();if(ids.Length==0)return;value=ids[synergyChoice++%ids.Length];}
            if(mode==1){var cells=PokeChess.Core.Board.HexBoardBounds.Preparation.Cells().Where(p=>IsOnlineMatch?!onlineService.LocalMatchState.units.Any(u=>u.placement==PlacementKind.Board&&u.column==p.Column&&u.row==p.Row):!Player.Units.Any(u=>u.Placement.Kind==PlacementKind.Board&&u.Placement.Position.Value.Equals(p))).ToArray();if(cells.Length==0)return;var tile=cells[synergyRockChoice++%cells.Length];col=tile.Column;row=tile.Row;}
            if(mode==2)value=SynergyTraits.Origins[synergyProphecy++%SynergyTraits.Origins.Count];
            if(IsOnlineMatch)SendOnlineCommand(MatchCommandKind.ConfigureSynergy,c=>{c.bench=mode;c.unitId=selectedUnitId;c.itemId=value;c.slot=slot;c.column=col;c.row=row;});
            else {var result=commands.ConfigureSynergy(mode,selectedUnitId,value,slot,col,row);Feedback(result.Accepted?(mode==0?"대상: "+Player.GetUnit(value).DefinitionId:mode==1?"바위 "+(slot+1)+": "+col+", "+row:mode==2?"예언: "+SynergyTraits.Name(value):result.Message):result.Message);Render();}
        }
        private void RenderSynergies(IReadOnlyList<SynergyCount> counts, bool visible)
        {
            synergyRoot.gameObject.SetActive(visible);
            if (!visible) { synergyTooltip.gameObject.SetActive(false); synergyHovered = null; return; }
            lastSynergyCounts=counts.Where(c=>c.Count>0).ToArray();
            synergyPage=Math.Min(synergyPage,Math.Max(0,(lastSynergyCounts.Count-1)/8));
            foreach(var image in synergyColors.Values)image.gameObject.SetActive(false);
            var page=lastSynergyCounts.Skip(synergyPage*8).Take(8).ToArray();
            for(int i=0;i<page.Length;i++){
                var count=page[i];
                if(!synergyColors.TryGetValue(count.TraitId,out var image)){
                    var node=Rect(count.TraitId,synergyRoot,new Vector2(150,40),Vector2.zero);
                    image=node.gameObject.AddComponent<UnityEngine.UI.Image>();synergyColors[count.TraitId]=image;
                    var mark=HudImage("Mark",node,new Vector2(29,29),new Vector2(-56,0),new Color(.22f,.38f,.4f));
                    Label("Glyph",mark.transform,count.TraitId.Substring(0,1).ToUpperInvariant(),17,new Vector2(28,28),Vector2.zero);
                    synergyLabels[count.TraitId]=Label("Count",node,"",11,new Vector2(112,36),new Vector2(19,0));
                    var trigger=node.gameObject.AddComponent<EventTrigger>();string id=count.TraitId;
                    var enter=new EventTrigger.Entry{eventID=EventTriggerType.PointerEnter};
                    enter.callback.AddListener(_=>{synergyHovered=id;synergyTooltip.gameObject.SetActive(true);synergyTooltip.SetAsLastSibling();RenderSynergies(lastSynergyCounts,true);});
                    var exit=new EventTrigger.Entry{eventID=EventTriggerType.PointerExit};
                    exit.callback.AddListener(_=>{synergyHovered=null;synergyTooltip.gameObject.SetActive(false);});
                    trigger.triggers.Add(enter);trigger.triggers.Add(exit);
                }
                image.gameObject.SetActive(true);image.rectTransform.anchoredPosition=new Vector2(0,165-i*44);
                synergyLabels[count.TraitId].text=TraitName(count.TraitId)+"\n"+count.Count+"/"+(count.NextThreshold==0?count.Tier:count.NextThreshold);
                image.color=count.Needed==0?new Color(.34f,.27f,.1f):count.Tier>0?new Color(.1f,.3f,.24f):Navy;
            }
            synergyNext.gameObject.SetActive(lastSynergyCounts.Count>8);
            ((RectTransform)synergyNext.transform).anchoredPosition=new Vector2(0,165-page.Length*44);
            ButtonText(synergyNext,(synergyPage+1)+"/"+Math.Max(1,(lastSynergyCounts.Count+7)/8)+"  >");
            if (synergyHovered != null)
            {
                var members = PokeChess.Content.GameContent.CreateDefinitions().Where(d => d.TraitIds.Contains(synergyHovered)).Select(d => d.DisplayName);
                synergyHelp.text = TraitName(synergyHovered) + " · 서로 다른 진화 계열\n" + SynergyDescription(synergyHovered) + "\n" + string.Join(" / ", members);
            }
        }
        private void RenderSynergyTiles(IEnumerable<BoardPosition> fire,IEnumerable<BoardPosition> rocks)
        {
            foreach(var tile in synergyTiles.Values)tile.gameObject.SetActive(false);
            foreach(var marker in rocks.Select(p=>(p,fire:false)).Concat(fire.Select(p=>(p,fire:true)))) {
                string key=(marker.fire?"fire":"rock")+marker.p.Column+":"+marker.p.Row;
                if(!synergyTiles.TryGetValue(key,out var image)){var node=Rect("SynergyTile_"+key,combatRoot,new Vector2(44,50),Vector2.zero);image=node.gameObject.AddComponent<PlacementHexGraphic>();image.raycastTarget=false;synergyTiles[key]=image;node.SetSiblingIndex(56);Label("Kind",node,marker.fire?"불길":"바위",9,new Vector2(40,15),new Vector2(0,-16));}
                image.gameObject.SetActive(true);image.rectTransform.anchoredPosition=CombatPoint(marker.p);image.color=marker.fire?new Color(1,.24f,.04f,.5f):new Color(.65f,.65f,.7f,.45f);
            }
        }
        private void RenderLocalSynergies()
        {
            synergyDemoButton.gameObject.SetActive(true);
            synergyProgress.text="숲 XP "+ViewedPlayer.Synergy.NatureXP+" · 표본 "+ViewedPlayer.Synergy.VenomSamples+"\n투지 "+ViewedPlayer.Synergy.Resolve+" · 조각 "+ViewedPlayer.Synergy.EquipmentFragments+"/"+ViewedPlayer.Synergy.EmblemFragments+(ViewedPlayer.Synergy.FreeRerollRound==Match.RoundNumber?" · 무료 리롤":"");
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
            RenderSynergies(counts, Match.Phase != MatchPhase.Finished);
        }
        private string LocalSynergyFlash(UnitCombatState unit) => synergyFlashes.TryGetValue(unit.UnitInstanceId, out var flash) && flash.until > Time.unscaledTime ? flash.text : "";
        private void RenderOnlineSynergies(PublicMatchState pub)
        {
            synergyDemoButton.gameObject.SetActive(false);
            var progress=onlineService.LocalMatchState;synergyProgress.text=progress==null?"":"숲 XP "+progress.natureXP+" · 표본 "+progress.venomSamples+"\n투지 "+progress.resolve+" · 조각 "+progress.equipmentFragments+"/"+progress.emblemFragments+(progress.freeRerollRound==progress.round?" · 무료 리롤":"");
            var frame = onlineService.Combat.Current;
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
            RenderSynergies(counts, pub.phase != MatchPhase.Finished);
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


