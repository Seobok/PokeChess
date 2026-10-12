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
        private int synergyPage;
        private IReadOnlyList<SynergyCount> lastSynergyCounts=Array.Empty<SynergyCount>();
        public string SynergyDisplay => string.Join(" | ",synergyLabels.Where(p=>synergyColors[p.Key].gameObject.activeSelf).Select(p=>p.Value.text));
        private static string TraitName(string trait) => Readable(trait);
        private static string TraitList(PokemonDefinition definition) => string.Join(" / ", definition.TraitIds.Select(TraitName));
        private static string SynergyDescription(string trait)
        {
            switch (trait)
            {
                case SynergyTraits.Guardian: return "(2) Guardians start with 80 shield.\n(3) 140 shield; when consumed, +25% Armor/MR for 4s.";
                case SynergyTraits.Assault: return "(2) After a skill effect, next attack gains 25% AT damage.\n(3) Next 2 attacks. Charges refresh and expire after 6s.";
                case SynergyTraits.Focus: return "(2) Focus units start with +10 Energy.\n(3) +20 Energy; first skill effect grants +25% AS for 4s.";
                default: return "(2) First skill effect grants nearest other ally 60 shield.\n(3) 100 shield and +15% AT for 4s. Once per caster.";
            }
        }
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
            synergyTooltip = Panel("SynergyHelp", new Vector2(320, 180), new Vector2(-290, 130));
            synergyTooltip.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            synergyHelp = Label("Description", synergyTooltip, "", 12, new Vector2(300, 165), Vector2.zero);
            synergyTooltip.gameObject.SetActive(false);
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
                synergyLabels[count.TraitId].text=TraitName(count.TraitId)+"\n"+count.Count+"/"+(count.Tier>=2?3:2);
                image.color=count.Tier==3?new Color(.34f,.27f,.1f):count.Tier==2?new Color(.1f,.3f,.24f):Navy;
            }
            synergyNext.gameObject.SetActive(lastSynergyCounts.Count>8);
            ((RectTransform)synergyNext.transform).anchoredPosition=new Vector2(0,165-page.Length*44);
            ButtonText(synergyNext,(synergyPage+1)+"/"+Math.Max(1,(lastSynergyCounts.Count+7)/8)+"  >");
            if (synergyHovered != null)
            {
                var members = PrototypeRoster.CreateDefinitions().Where(d => d.TraitIds.Contains(synergyHovered)).Select(d => d.DisplayName);
                synergyHelp.text = TraitName(synergyHovered) + " · different species on board (2/3)\n" + SynergyDescription(synergyHovered) + "\n" + string.Join(" / ", members);
            }
        }
        private void RenderLocalSynergies()
        {
            synergyDemoButton.gameObject.SetActive(true);
            var battle = Match.Phase == MatchPhase.Combat ? RoundLoop.GetBattleFor(localViewedPlayerId)?.Battle : null;
            if (synergyVisualBattle != battle) { synergyVisualBattle = battle; synergyVisualTick = -1; synergyFlashes.Clear(); }
            if (battle != null && synergyVisualTick != battle.CurrentTick)
            {
                synergyVisualTick = battle.CurrentTick;
                foreach (var e in battle.Synergies.EventsThisTick) synergyFlashes[e.TargetId] = (e.Detail, Time.unscaledTime + .65f);
                foreach (var e in battle.Items.EventsThisTick) synergyFlashes[e.TargetId] = (e.ItemId==ItemCatalog.RockyHelmet?"REFLECT":"ITEM",Time.unscaledTime+.65f);
            }
            var own = battle?.Units.FirstOrDefault(u => u.OwnerPlayerId == localViewedPlayerId);
            var counts = own == null ? SynergySystem.Count(ViewedPlayer.Units.Where(u => u.Placement.Kind == PlacementKind.Board).Select(u => catalog.Get(u.DefinitionId))) : battle.Synergies.TeamCounts(own.TeamId);
            RenderSynergies(counts, Match.Phase != MatchPhase.Finished);
        }
        private string LocalSynergyFlash(UnitCombatState unit) => synergyFlashes.TryGetValue(unit.UnitInstanceId, out var flash) && flash.until > Time.unscaledTime ? flash.text : "";
        private void RenderOnlineSynergies(PublicMatchState pub)
        {
            synergyDemoButton.gameObject.SetActive(false);
            var frame = onlineService.Combat.Current;
            IReadOnlyList<SynergyCount> counts;
            if (pub.phase == MatchPhase.Combat && frame != null && frame.round == pub.round)
                counts = SynergySystem.Count(frame.units.Where(u => u.owner == onlineService.WatchingPlayerId).Select(u => catalog.Get(u.definitionId)));
            else
            {
                var player = pub.players.FirstOrDefault(p => p.id == onlineService.WatchingPlayerId);
                counts = SynergySystem.Count((player?.board ?? Array.Empty<PublicBoardUnit>()).Select(u => catalog.Get(u.definitionId)));
            }
            RenderSynergies(counts, pub.phase != MatchPhase.Finished);
        }
        public void ResetSynergyDemo()
        {
            if (IsOnlineMatch) return;
            CancelDrag("Synergy demo."); catalog = PrototypeRoster.CreateCatalog();
            Match = MatchStateFactory.CreateWithPool("synergy-demo", new[] { "p1", "p2" }, catalog, PrototypeRoster.CreateDefinitions().Select(d => d.Id), matchSeed: 123);
            string[] species = { "slowpoke", "geodude", "chansey", "squirtle", "mankey", "bulbasaur" };
            var positions = new[] { new BoardPosition(1,3), new BoardPosition(3,3), new BoardPosition(1,0), new BoardPosition(3,1), new BoardPosition(4,3), new BoardPosition(5,3) };
            foreach (var player in Match.Players)
            {
                player.SetProgress(20,0,6);
                for (int i=0;i<species.Length;i++) SharedPoolSystem.RegisterUnit(Match,catalog.CreateUnit(player.PlayerId+"-synergy-"+i,species[i],player.PlayerId,UnitRank.One,0,UnitPlacement.OnBoard(positions[i])),species[i]);
            }
            Match.TransitionTo(MatchPhase.Starting); Match.TransitionTo(MatchPhase.Preparation);
            AttachMinimumMatch(); LastResult=null; Feedback("Synergy demo: Guardian 3 / Assault 2 / Link 3. Hover traits for effects."); Render();
        }
    }
}


