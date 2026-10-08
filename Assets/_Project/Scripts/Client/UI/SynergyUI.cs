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
        public string SynergyDisplay => string.Join(" | ", SynergyTraits.All.Select(t => synergyLabels[t].text));
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
            synergyRoot = Rect("Synergies", canvasRect, new Vector2(620, 24), new Vector2(0, 264));
            for (int i = 0; i < SynergyTraits.All.Count; i++)
            {
                string trait = SynergyTraits.All[i];
                var node = Rect(trait, synergyRoot, new Vector2(150, 24), new Vector2(-232.5f + i * 155, 0));
                var image = node.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = Navy;
                synergyColors[trait] = image;
                synergyLabels[trait] = Label("Count", node, "", 10, new Vector2(146, 22), Vector2.zero);
                var trigger = node.gameObject.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => { synergyHovered = trait; synergyTooltip.gameObject.SetActive(true); synergyTooltip.SetAsLastSibling(); });
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => { synergyHovered = null; synergyTooltip.gameObject.SetActive(false); });
                trigger.triggers.Add(enter); trigger.triggers.Add(exit);
            }
            synergyTooltip = Panel("SynergyHelp", new Vector2(600, 96), new Vector2(0, 200));
            synergyTooltip.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            synergyHelp = Label("Description", synergyTooltip, "", 12, new Vector2(584, 88), Vector2.zero);
            synergyTooltip.gameObject.SetActive(false);
        }
        private void RenderSynergies(IReadOnlyList<SynergyCount> counts, bool visible)
        {
            synergyRoot.gameObject.SetActive(visible);
            if (!visible) { synergyTooltip.gameObject.SetActive(false); synergyHovered = null; return; }
            foreach (var count in counts)
            {
                synergyLabels[count.TraitId].text = TraitName(count.TraitId) + " " + count.Count + "/" + (count.Tier == 3 ? 3 : count.Tier == 2 ? 3 : 2) +
                    " · " + (count.Tier == 3 ? "MAX" : count.Tier == 2 ? "ACTIVE +1" : "+" + count.Needed);
                synergyColors[count.TraitId].color = count.Tier == 3 ? new Color(.34f, .27f, .1f) : count.Tier == 2 ? new Color(.1f, .3f, .24f) : Navy;
            }
            if (synergyHovered != null)
            {
                var members = PrototypeRoster.CreateDefinitions().Where(d => d.TraitIds.Contains(synergyHovered)).Select(d => d.DisplayName);
                synergyHelp.text = TraitName(synergyHovered) + " · different species on board (2/3)\n" + SynergyDescription(synergyHovered) + "\n" + string.Join(" / ", members);
            }
        }
        private void RenderLocalSynergies()
        {
            synergyDemoButton.gameObject.SetActive(true);
            var battle = Match.Phase == MatchPhase.Combat ? RoundLoop.GetBattleFor("p1")?.Battle : null;
            if (synergyVisualBattle != battle) { synergyVisualBattle = battle; synergyVisualTick = -1; synergyFlashes.Clear(); }
            if (battle != null && synergyVisualTick != battle.CurrentTick)
            {
                synergyVisualTick = battle.CurrentTick;
                foreach (var e in battle.Synergies.EventsThisTick) synergyFlashes[e.TargetId] = (e.Detail, Time.unscaledTime + .65f);
            }
            var own = battle?.Units.FirstOrDefault(u => u.OwnerPlayerId == "p1");
            var counts = own == null ? SynergySystem.Count(Player.Units.Where(u => u.Placement.Kind == PlacementKind.Board).Select(u => catalog.Get(u.DefinitionId))) : battle.Synergies.TeamCounts(own.TeamId);
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
