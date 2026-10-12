using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private UnityEngine.UI.Text evolutionPointsLabel, evolutionHint;
        private UnityEngine.UI.Button evolveButton;
        private RectTransform evolutionHintPanel;
        private long displayedEvolutionRevision;
        public UnityEngine.UI.Button EvolveButton => evolveButton;
        public string EvolutionPointText => evolutionPointsLabel?.text;
        public string EvolutionHintText => evolutionHint?.text;

        private void BuildEvolutionUI()
        {
            evolutionPointsLabel = ItemLabel("EvolutionPoints", shopFrame, "", 12,
                new Vector2(140,38), new Vector2(330,65));
            // Share the existing action row with selling, keeping round/debug controls separate.
            var saleRect = sellButton.GetComponent<RectTransform>();
            saleRect.sizeDelta = new Vector2(98,32); saleRect.anchoredPosition = new Vector2(436,72);
            sellButton.GetComponentInChildren<UnityEngine.UI.Text>().rectTransform.sizeDelta = new Vector2(94,28);
            evolveButton = ActionButton("Evolve", new Vector2(106,32), new Vector2(544,72), EvolveSelected);
            evolveButton.GetComponentInChildren<UnityEngine.UI.Text>().font = itemFont;
            evolutionHintPanel = Panel("EvolutionPreview", new Vector2(320,115), new Vector2(205,207));
            evolutionHintPanel.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            evolutionHint = ItemLabel("EvolutionHint", evolutionHintPanel, "", 12,
                new Vector2(302,100), Vector2.zero);
            evolutionHintPanel.gameObject.SetActive(false);
            var trigger = evolveButton.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var enter = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => { evolutionHintPanel.gameObject.SetActive(true); evolutionHintPanel.SetAsLastSibling(); });
            var exit = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = UnityEngine.EventSystems.EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => evolutionHintPanel.gameObject.SetActive(false));
            trigger.triggers.Add(enter); trigger.triggers.Add(exit);
        }

        public void EvolveSelected()
        {
            if (selectedUnitId == null) { Feedback("진화할 포켓몬을 선택하세요."); return; }
            string id = selectedUnitId; long revision = displayedEvolutionRevision;
            if (IsOnlineMatch)
            {
                SendOnlineCommand(MatchCommandKind.Evolve, c => { c.unitId = id; c.evolutionRevision = revision; });
                return;
            }
            long placement = displayedPlacementRevision;
            Perform(() => commands.Evolve(id, revision, placement));
        }

        private void RenderEvolutionUI()
        {
            if (evolveButton == null) return;
            string id; UnitRank rank; int points, nextRound; bool allowed; string unavailable = null;
            if (IsOnlineMatch)
            {
                var owner = onlineService.LocalMatchState;
                if (owner == null) { evolveButton.interactable = false; return; }
                var selected = owner.units.FirstOrDefault(u => u.id == selectedUnitId);
                id = selected?.definitionId; rank = (UnitRank)(selected?.rank ?? 1);
                points = owner.evolutionPoints; nextRound = owner.nextEvolutionPointRound;
                displayedEvolutionRevision = owner.evolutionRevision;
                allowed = selected != null && OnlineInputReady && owner.phase == MatchPhase.Preparation &&
                    (selected.placement == PlacementKind.Board || selected.placement == PlacementKind.Bench);
            }
            else
            {
                var selected = Player.Units.FirstOrDefault(u => u.InstanceId == selectedUnitId);
                id = selected?.DefinitionId; rank = selected?.Rank ?? UnitRank.One;
                points = Player.EvolutionPoints; nextRound = EvolutionPointRules.NextGrantRound(Match.RoundNumber);
                displayedEvolutionRevision = Player.EvolutionRevision;
                allowed = selected != null && commands.CanTrade && !busy && !RoundLoop.NextPreparationPending &&
                    Simulation?.Status != SimulationStatus.Running;
                if (allowed)
                {
                    var preview = commands.PreviewEvolution(selectedUnitId, displayedEvolutionRevision, Player.PlacementRevision);
                    allowed = preview.Accepted; unavailable = preview.Accepted ? null : preview.Message;
                }
            }
            bool finished = IsOnlineMatch ? onlineService.PublicState?.phase == MatchPhase.Finished : Match.Phase == MatchPhase.Finished;
            evolveButton.gameObject.SetActive(false);
            if (finished) evolutionHintPanel.gameObject.SetActive(false);
            evolutionPointsLabel.text = "진화포인트 " + points + " EP\n다음 지급: " + nextRound + "라운드";
            evolveButton.interactable = false;
            if (id == null) { ButtonText(evolveButton,"진화 / 포켓몬 선택"); evolutionHint.text = "준비 단계에서 보유 포켓몬을\n선택해 진화할 수 있습니다."; return; }
            var d = catalog.Get(id);
            if (d.Evolutions.Count != 1)
            {
                ButtonText(evolveButton,"진화 불가");
                evolutionHint.text = PrototypeEvolutionRoster.Stage(id) == 2 ? "최종 진화체입니다." : "이 포켓몬의 진화는\n아직 지원하지 않습니다.";
                return;
            }
            var option = d.Evolutions[0]; var target = catalog.Get(option.TargetDefinitionId);
            var before = RankRules.Default.Apply(d.BaseStats,rank); var after = RankRules.Default.Apply(target.BaseStats,rank);
            evolveButton.interactable = allowed && points >= option.EvolutionPointCost;
            ButtonText(evolveButton,"진화 / " + option.EvolutionPointCost + " EP");
            evolutionHint.text = KoreanInfo(id)+" → "+KoreanInfo(target.Id)+"\n체력 "+Number(before.MaxHP)+" → "+Number(after.MaxHP)+
                " · 공격 "+Number(before.Attack)+" → "+Number(after.Attack)+"\n현재 상점의 같은 계열도 즉시 진화\n"+
                (points < option.EvolutionPointCost ? "진화포인트가 부족합니다." : !allowed ? unavailable ?? "준비 단계에서 진화할 수 있습니다." : "성급·장착 도구·위치 유지");
        }
    }
}
