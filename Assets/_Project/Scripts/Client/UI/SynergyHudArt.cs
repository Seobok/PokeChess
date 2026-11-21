using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private static readonly Color32 SynergyInk = new Color32(13, 22, 45, 255);
        private const int SynergiesPerPage = 8;
        private static readonly (string name, string[] species, string description)[] UniqueSynergies =
        {
            ("텍스처", new[] { "porygon" }, "지정한 아군의 기본 계열 하나를 복사합니다."),
            ("승선", new[] { "lapras" }, "인접한 아군을 승객으로 지정해 보호합니다."),
            ("움직이지 않는 산", new[] { "snorlax" }, "뒤쪽 인접 아군을 강제 이동으로부터 보호합니다."),
            ("왕가의 결속", new[] { "nidoran-f", "nidoran-m" }, "두 니도란 계열의 대표가 서로의 기술에 반응합니다."),
            ("스타팅 삼원합동", new[] { "bulbasaur", "charmander", "squirtle" }, "세 계열의 대표가 모두 기술을 쓰면 합동 효과를 발동합니다.")
        };

        private sealed class SynergyHudRow
        {
            public UnityEngine.UI.Image Image, Icon, Gear;
            public UnityEngine.UI.Text Name, Count, Glyph;
            public string Key;
        }

        private readonly SynergyHudRow[] synergyRows = new SynergyHudRow[SynergiesPerPage];
        private readonly List<(UnityEngine.UI.Image Slot, UnityEngine.UI.Image OwnedBackground, UnityEngine.UI.Image Frame)> synergyPortraits =
            new List<(UnityEngine.UI.Image, UnityEngine.UI.Image, UnityEngine.UI.Image)>();
        private readonly List<UnityEngine.UI.Image> synergyTargetMarks = new List<UnityEngine.UI.Image>();
        private IReadOnlyList<PokemonDefinition> synergyRoster;
        private SynergyPanelIcons synergyPanelIcons;
        private (string id, string species, BoardPosition cell, string[] traits, int stage, int rank, string target)[] synergyBoard;
        private (string key, string name, int count, int next, int tier, int unique)[] synergyEntries;
        private UnityEngine.UI.Button synergyPrev, synergyNext;
        private UnityEngine.UI.Button[] mysticOriginChoices;
        private bool mysticChoiceAvailable;
        private RectTransform synergyPager, synergyTargetPanel, mysticOriginVisuals;
        private UnityEngine.UI.Text synergyPageLabel, synergyTooltipTitle, synergyMemberLabel, synergyTargetTitle,
            synergyTargetSource, synergyTargetCurrent, synergyTargetEffect;
        private UnityEngine.UI.Image synergyLink, synergySourcePortrait, synergyCurrentPortrait;
        private readonly UnityEngine.UI.Image[] synergyThresholdRows = new UnityEngine.UI.Image[4];
        private Sprite synergyRowDefault, synergyRowInactive, synergyRowSelected, synergyPortraitSlot,
            synergyPortraitDeployed, synergyHex;
        private string synergyTargetKey, synergyTargetSourceId, synergyTooltipSignature;
        private int synergyPage;
        private PokemonDefinition hoveredSynergyPokemon;
        private string[] synergyMembers = Array.Empty<string>();
        public string SynergyDisplay => string.Join(" | ", synergyRows.Where(row => row != null && row.Image.gameObject.activeSelf)
            .Select(row => row.Name.text + " " + row.Count.text));

        private static Sprite SynergySprite(string name) => Resources.Load<Sprite>("SynergyHud/Synergy" + name);

        private UnityEngine.UI.Image SynergyImage(string name, Transform parent, Vector2 size, Vector2 position, Sprite sprite)
        {
            var image = HudImage(name, parent, size, position, Color.white);
            image.sprite = sprite;
            return image;
        }

        private void BindSynergyHud()
        {
            synergyRoster = PokeChess.Content.GameContent.CreateDefinitions().ToArray();
            synergyRowDefault = SynergySprite("Row");
            synergyRowInactive = SynergySprite("RowInactive");
            synergyRowSelected = SynergySprite("RowSelected");
            synergyPortraitSlot = SynergySprite("PortraitSlot");
            synergyPortraitDeployed = SynergySprite("PortraitDeployed");
            synergyHex = SynergySprite("TargetHex");

            synergyRoot = UiPrefab.Instantiate("Synergies", canvasRect, itemFont);
            synergyPanelIcons = synergyRoot.GetComponent<SynergyPanelIcons>();
            for (int i = 0; i < synergyRows.Length; i++)
            {
                var image = UiPrefab.Get<UnityEngine.UI.Image>(synergyRoot, $"SynergyRow_{i}");
                var root = image.transform;
                var row = synergyRows[i] = new SynergyHudRow
                {
                    Image = image,
                    Icon = UiPrefab.Get<UnityEngine.UI.Image>(root, "Mark"),
                    Glyph = UiPrefab.Get<UnityEngine.UI.Text>(root, "Mark/Glyph"),
                    Name = UiPrefab.Get<UnityEngine.UI.Text>(root, "Name"),
                    Count = UiPrefab.Get<UnityEngine.UI.Text>(root, "Count"),
                    Gear = UiPrefab.Get<UnityEngine.UI.Image>(root, "Gear")
                };
                row.Icon.preserveAspect = true;
                var events = image.gameObject.AddComponent<EventTrigger>();
                AddSynergyRowEvent(events, EventTriggerType.PointerEnter, _ =>
                {
                    synergyHovered = row.Key;
                    RefreshSynergyRows();
                });
                AddSynergyRowEvent(events, EventTriggerType.PointerClick, data =>
                {
                    if (data is PointerEventData pointer && pointer.button == PointerEventData.InputButton.Left)
                        OpenSynergyTarget(row.Key);
                });
            }
            synergyPager = UiPrefab.Get<RectTransform>(synergyRoot, "SynergyPages");
            synergyPageLabel = UiPrefab.Get<UnityEngine.UI.Text>(synergyPager, "PageBadge/Page");
            synergyPrev = BindSynergyPageButton("PreviousPage", -1);
            synergyNext = BindSynergyPageButton("NextPage", 1);
            UiPrefab.Get<UnityEngine.UI.Text>(synergyRoot, "SynergyProgress").gameObject.SetActive(false);

            synergyTooltip = UiPrefab.Instantiate("SynergyHelp", canvasRect, itemFont);
            synergyRoot.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            synergyTooltip.GetComponent<UnityEngine.UI.Image>().raycastTarget = true;
            AddSynergyRowEvent(synergyRoot.gameObject.AddComponent<EventTrigger>(), EventTriggerType.PointerExit, EndSynergyHover);
            AddSynergyRowEvent(synergyTooltip.gameObject.AddComponent<EventTrigger>(), EventTriggerType.PointerExit, EndSynergyHover);
            synergyTooltipTitle = UiPrefab.Get<UnityEngine.UI.Text>(synergyTooltip, "Header/Title");
            synergyHelp = UiPrefab.Get<UnityEngine.UI.Text>(synergyTooltip, "Description");
            for (int i = 0; i < synergyThresholdRows.Length; i++)
                synergyThresholdRows[i] = UiPrefab.Get<UnityEngine.UI.Image>(synergyTooltip, $"Threshold_{i}");
            synergyMemberLabel = UiPrefab.Get<UnityEngine.UI.Text>(synergyTooltip, "Members");

            synergyTargetPanel = UiPrefab.Instantiate("SynergyTargetPanel", canvasRect, itemFont);
            synergyTargetTitle = UiPrefab.Get<UnityEngine.UI.Text>(synergyTargetPanel, "Header/Title");
            synergySourcePortrait = UiPrefab.Get<UnityEngine.UI.Image>(synergyTargetPanel, "SourcePortrait");
            synergyTargetSource = UiPrefab.Get<UnityEngine.UI.Text>(synergyTargetPanel, "Source");
            synergyCurrentPortrait = UiPrefab.Get<UnityEngine.UI.Image>(synergyTargetPanel, "TargetPortrait");
            synergyTargetCurrent = UiPrefab.Get<UnityEngine.UI.Text>(synergyTargetPanel, "CurrentTarget");
            synergyTargetEffect = UiPrefab.Get<UnityEngine.UI.Text>(synergyTargetPanel, "Effect");

            mysticOriginVisuals = Rect("MysticOriginVisuals", synergyTargetPanel, new Vector2(30, 30), new Vector2(-76, -43));
            mysticOriginChoices = SynergyTraits.Origins.Select((origin, index) =>
            {
                var button = ActionButton(TraitName(origin), new Vector2(44, 23),
                    new Vector2((index % 4 - 1.5f) * 47, 16 - index / 4 * 26), () => ChooseMysticOrigin(origin));
                button.transform.SetParent(synergyTargetPanel, false);
                button.GetComponentInChildren<UnityEngine.UI.Text>().font = synergyTargetEffect.font;
                button.gameObject.SetActive(false);
                return button;
            }).ToArray();
        }

        private static void AddSynergyRowEvent(EventTrigger events, EventTriggerType type, Action<BaseEventData> callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => callback(data));
            events.triggers.Add(entry);
        }

        private void EndSynergyHover(BaseEventData data)
        {
            var hit = (data as PointerEventData)?.pointerCurrentRaycast.gameObject;
            if (hit != null && (hit.transform.IsChildOf(synergyRoot) || hit.transform.IsChildOf(synergyTooltip)))
                return;

            synergyHovered = null;
            RefreshSynergyRows();
        }

        private void AlignSynergyTooltip()
        {
            if (hoveredUnitTrait != null && detailPopup != null)
            {
                var edge = canvasRect.InverseTransformPoint(detailPopup.TransformPoint(
                    new Vector2(detailPopup.rect.xMin, detailPopup.rect.yMax)));
                synergyTooltip.localPosition = new Vector3(edge.x - synergyTooltip.rect.xMax,
                    edge.y - synergyTooltip.rect.yMax, synergyTooltip.localPosition.z);
                return;
            }
            var corner = canvasRect.InverseTransformPoint(synergyRoot.TransformPoint(
                new Vector2(synergyRoot.rect.xMax, synergyRoot.rect.yMax)));
            synergyTooltip.localPosition = new Vector3(corner.x - synergyTooltip.rect.xMin,
                corner.y - synergyTooltip.rect.yMax, synergyTooltip.localPosition.z);
        }

        private PokemonDefinition FindSynergyPokemon(string id)
        {
            if (catalog != null && catalog.TryGet(id, out var definition)) return definition;
            return PokeChess.Content.GameContent.CreateCatalog().TryGet(id, out definition) ? definition : null;
        }

        private void ShowSynergyPokemonPreview(int index)
        {
            if (!synergyTooltip.gameObject.activeInHierarchy || index >= synergyMembers.Length) return;
            hoveredSynergyPokemon = FindSynergyPokemon(synergyMembers[index]);
            HideUnitInfoHint();
            UpdateTablePresentation();
        }

        private bool RenderSynergyPokemonInfo()
        {
            if (hoveredSynergyPokemon == null || !synergyTooltip.gameObject.activeInHierarchy) return false;
            var stats = RankRules.Default.Apply(hoveredSynergyPokemon, UnitRank.One);
            RenderUnitInfoIcons(hoveredSynergyPokemon, UnitRank.One, stats, stats.MaxHP, stats.MaxHP,
                stats.StartingEnergy, stats.MaxEnergy, 0, Array.Empty<string>(), baseStats: true);
            unitDetailHeader.text = "시너지 포켓몬";
            return true;
        }

        private void HideSynergyPokemonPreview()
        {
            if (hoveredSynergyPokemon == null) return;
            hoveredSynergyPokemon = null;
            HideUnitInfoHint();
            if (selectedUnitId != null)
            {
                if (!IsOnlineMatch) RenderUnitContext();
                else if (onlineService.LocalMatchState != null && onlineService.PublicState != null)
                    RenderOnlineContext(onlineService.LocalMatchState, onlineService.PublicState);
            }
            UpdateTablePresentation();
        }

        private UnityEngine.UI.Button BindSynergyPageButton(string name, int step)
        {
            return UiPrefab.Bind(synergyPager, name, () =>
            {
                synergyPage += step;
                synergyHovered = null;
                RefreshSynergyRows();
            });
        }

        private void RenderSynergies(IReadOnlyList<SynergyCount> counts, bool visible)
        {
            synergyRoot.gameObject.SetActive(visible);
            if (!visible)
            {
                synergyHovered = null;
                synergyTooltip.gameObject.SetActive(false);
                synergyTooltipSignature = null;
                HideSynergyPokemonPreview();
                CloseSynergyTarget();
                return;
            }

            if (IsOnlineMatch)
            {
                var owner = onlineService.LocalMatchState;
                var observed = onlineService.PublicState.players.FirstOrDefault(p => p.id == onlineService.WatchingPlayerId);
                synergyBoard = (observed?.board ?? Array.Empty<PublicBoardUnit>()).Select(unit =>
                    (unit.id, unit.definitionId, new BoardPosition(unit.column, unit.row), unit.synergyIds ?? SynergyTraits.ForPokemon(unit.definitionId).ToArray(),
                        unit.evolutionStage, unit.rank, owner?.playerId == observed?.id ? owner?.units.FirstOrDefault(u => u.id == unit.id)?.synergyTargetId : null)).ToArray();
            }
            else
            {
                var player = ViewedPlayer;
                synergyBoard = player.Units.Where(unit => unit.Placement.Kind == PlacementKind.Board).Select(unit =>
                    (unit.InstanceId, unit.DefinitionId, unit.Placement.Position.Value, SynergyPreparation.Traits(unit, catalog, player),
                        unit.EvolutionStage, (int)unit.Rank, unit.SynergyTargetId)).ToArray();
            }

            var families = new HashSet<string>(synergyBoard.Select(unit => SynergyTraits.Family(unit.species)));
            var unique = UniqueSynergies.Select((data, i) => (data, i))
                .Select(entry => (entry.data, entry.i, count: entry.data.species.Count(id => families.Contains(SynergyTraits.Family(id)))))
                .Where(entry => entry.count > 0)
                .Select(entry => ("unique-" + entry.i, entry.data.name, entry.count, entry.data.species.Length,
                    entry.count == entry.data.species.Length ? 1 : 0, entry.i));
            var ownerState = IsOnlineMatch ? onlineService.LocalMatchState : null;
            bool viewingOwnBoard = IsOnlineMatch ? ownerState != null && onlineService.WatchingPlayerId == ownerState.playerId
                : localViewedPlayerId == "p1";
            bool earnedMysticChoice = viewingOwnBoard && (IsOnlineMatch
                ? ownerState.phase == MatchPhase.Preparation && ownerState.mysticTier >= 6 &&
                    SynergyProgress.IsFreeRerollAvailable(ownerState.freeRerollRound, ownerState.round)
                : Match.Phase == MatchPhase.Preparation && Player.Synergy.MysticTier >= 6 &&
                    SynergyProgress.IsFreeRerollAvailable(Player.Synergy.FreeRerollRound, Match.RoundNumber));
            mysticChoiceAvailable = viewingOwnBoard &&
                (earnedMysticChoice || counts.Any(c => c.TraitId == SynergyTraits.Mystic && c.Tier >= 6));
            var normal = counts.Where(c => c.Count > 0 || earnedMysticChoice && c.TraitId == SynergyTraits.Mystic)
                .OrderByDescending(c => Array.IndexOf(SynergyTraits.Thresholds(c.TraitId), c.Tier))
                .ThenByDescending(c => c.Count)
                .Select(c => (c.TraitId, TraitName(c.TraitId), c.Count, c.NextThreshold == 0 ? c.Tier : c.NextThreshold, c.Tier, -1));
            synergyEntries = unique.Concat(normal).ToArray();
            RefreshSynergyRows();
            RenderSynergyTarget();
        }

        private void RefreshSynergyRows()
        {
            if (synergyEntries == null) return;
            int pages = Math.Max(1, (synergyEntries.Length + SynergiesPerPage - 1) / SynergiesPerPage);
            synergyPage = Mathf.Clamp(synergyPage, 0, pages - 1);
            int offset = synergyPage * SynergiesPerPage;
            synergyRoot.sizeDelta = new Vector2(synergyRoot.sizeDelta.x, 14 + SynergiesPerPage * 37 + 28);
            synergyPager.anchoredPosition = new Vector2(0, -14 - SynergiesPerPage * 37 - 12);
            for (int i = 0; i < synergyRows.Length; i++)
            {
                var row = synergyRows[i];
                bool present = offset + i < synergyEntries.Length;
                if (row.Image.gameObject.activeSelf != present) row.Image.gameObject.SetActive(present);
                if (!present) { row.Key = null; continue; }
                var entry = synergyEntries[offset + i];
                row.Key = entry.key;
                bool selected = entry.key == synergyTargetKey || entry.key == synergyHovered;
                row.Image.sprite = selected ? synergyRowSelected : entry.tier > 0 ? synergyRowDefault : synergyRowInactive;
                row.Name.text = entry.name;
                row.Count.text = $"{entry.count} / {entry.next}";
                row.Gear.gameObject.SetActive(CanConfigureSynergyRow(entry.key, entry.unique) && CanChooseSynergyTarget());
                row.Count.rectTransform.anchoredPosition = new Vector2(row.Gear.gameObject.activeSelf ? 28 : 38, 0);
                row.Icon.sprite = synergyPanelIcons?.Find(entry.key);
                row.Icon.enabled = row.Icon.sprite != null;
                row.Glyph.gameObject.SetActive(!row.Icon.enabled);
                row.Glyph.text = SynergyMark(entry.key, entry.name);
            }
            synergyPager.gameObject.SetActive(pages > 1);
            synergyPageLabel.text = $"{synergyPage + 1} / {pages}";
            synergyPrev.interactable = synergyPage > 0;
            synergyNext.interactable = synergyPage + 1 < pages;
            string hoveredKey = hoveredUnitTrait ?? synergyHovered;
            var hovered = synergyEntries.FirstOrDefault(entry => entry.key == hoveredKey);
            if (hovered.key == null && hoveredUnitTrait != null)
            {
                if (hoveredUnitTrait.StartsWith("unique-", StringComparison.Ordinal) &&
                    int.TryParse(hoveredUnitTrait.Substring(7), out int unique) && unique >= 0 && unique < UniqueSynergies.Length)
                    hovered = (hoveredUnitTrait, UniqueSynergies[unique].name, 0, UniqueSynergies[unique].species.Length, 0, unique);
                else
                {
                    var thresholds = SynergyTraits.Thresholds(hoveredUnitTrait);
                    hovered = (hoveredUnitTrait, TraitName(hoveredUnitTrait), 0, thresholds.FirstOrDefault(), 0, -1);
                }
            }
            bool showTooltip = hovered.key != null && synergyTargetKey == null && (hoveredUnitTrait != null || synergyRows.Any(row => row.Key == hovered.key));
            synergyTooltip.gameObject.SetActive(showTooltip);
            if (showTooltip)
            {
                AlignSynergyTooltip();
                RenderSynergyTooltip(hovered);
            }
            else
            {
                synergyTooltipSignature = null;
                HideSynergyPokemonPreview();
            }
        }

        private static string SynergyMark(string key, string name) => key switch
        {
            SynergyTraits.Storm => "⚡", SynergyTraits.Nature => "◆", SynergyTraits.Earth => "▲",
            SynergyTraits.Guardian => "⬟", SynergyTraits.Duelist => "⚔",
            SynergyTraits.Torrent => "●", SynergyTraits.Mystic => "✦", _ => name.Substring(0, 1)
        };

        private void RenderSynergyTooltip((string key, string name, int count, int next, int tier, int unique) entry)
        {
            string signature = entry.key + ":" + entry.count + ":" + string.Join(",", synergyBoard.Select(unit => unit.species + "/" + string.Join("/", unit.traits)));
            var ownedSpecies = IsOnlineMatch
                ? (onlineService.LocalMatchState?.units ?? Array.Empty<OwnerUnit>())
                    .Where(unit => unit.placement == PlacementKind.Board || unit.placement == PlacementKind.Bench)
                    .Select(unit => unit.definitionId)
                : Player.Units.Where(unit => unit.Placement.Kind == PlacementKind.Board || unit.Placement.Kind == PlacementKind.Bench)
                    .Select(unit => unit.DefinitionId);
            var ownedFamilies = new HashSet<string>(ownedSpecies.Select(SynergyTraits.Family));
            signature += ":" + string.Join(",", ownedFamilies.OrderBy(family => family, StringComparer.Ordinal));
            if (signature == synergyTooltipSignature) return;
            synergyTooltipSignature = signature;
            HideSynergyPokemonPreview();
            synergyTooltipTitle.text = entry.name;
            string[] lines = entry.unique >= 0 ? Array.Empty<string>() : SynergyDescription(entry.key).Split('\n');
            int[] thresholds = entry.unique >= 0 ? Array.Empty<int>() : SynergyTraits.Thresholds(entry.key);
            synergyHelp.text = entry.unique >= 0 ? UniqueSynergies[entry.unique].description :
                string.Join("\n", lines.Skip(thresholds.Length));
            synergyHelp.gameObject.SetActive(synergyHelp.text.Length > 0);
            for (int i = 0; i < synergyThresholdRows.Length; i++)
            {
                var row = synergyThresholdRows[i];
                row.gameObject.SetActive(i < thresholds.Length);
                if (i >= thresholds.Length) continue;
                int threshold = thresholds[i];
                row.color = threshold == entry.tier ? Color.white : new Color(1, 1, 1, 0);
                row.transform.Find("Badge/Count").GetComponent<UnityEngine.UI.Text>().text = threshold.ToString();
                var effect = row.transform.Find("Effect").GetComponent<UnityEngine.UI.Text>();
                effect.text = i < lines.Length ? lines[i].Substring(lines[i].IndexOf(')') + 1).Trim() : "";
                effect.color = threshold <= entry.count ? SynergyInk : new Color32(99, 104, 117, 255);
            }
            var deployed = synergyBoard.Where(unit => entry.unique >= 0
                ? UniqueSynergies[entry.unique].species.Any(species => SynergyTraits.Family(species) == SynergyTraits.Family(unit.species))
                : unit.traits.Contains(entry.key)).Select(unit => unit.species).ToArray();
            var membership = synergyRoster.Where(definition => entry.unique >= 0
                    ? UniqueSynergies[entry.unique].species.Any(species => SynergyTraits.Family(species) == SynergyTraits.Family(definition.Id))
                    : definition.TraitIds.Contains(entry.key)).Select(definition => definition.Id)
                .Concat(deployed).GroupBy(SynergyTraits.Family)
                .Select(group => deployed.FirstOrDefault(id => SynergyTraits.Family(id) == group.Key) ??
                    group.OrderBy(PokeChess.Content.GameContent.Stage).First()).ToArray();
            float membersY = thresholds.Length == 0 ? 87 : 55 - (thresholds.Length - 1) * 51;
            if (thresholds.Length > 0 && synergyHelp.gameObject.activeSelf)
            {
                synergyHelp.rectTransform.anchoredPosition = new Vector2(0, membersY - 10);
                membersY -= 44;
            }
            else synergyHelp.rectTransform.anchoredPosition = new Vector2(0, 123);
            synergyMemberLabel.rectTransform.anchoredPosition = new Vector2(0, membersY);
            RenderSynergyPortraits(membership, deployed, ownedFamilies, membersY - 26);
            synergyTooltip.SetAsLastSibling();
        }

        private void RenderSynergyPortraits(string[] members, string[] deployed, HashSet<string> ownedFamilies, float firstY)
        {
            synergyMembers = members;
            const int columns = 9;
            for (int i = 0; i < members.Length; i++)
            {
                if (i == synergyPortraits.Count)
                {
                    var slot = SynergyImage("Portrait_" + i, synergyTooltip, new Vector2(32, 32), Vector2.zero, synergyPortraitSlot);
                    var ownedBackground = HudImage("OwnedBackground", slot.transform, new Vector2(26, 26),
                        Vector2.zero, new Color32(255, 235, 108, 255));
                    SynergyImage("Pokemon", slot.transform, new Vector2(26, 26), Vector2.zero, null).preserveAspect = true;
                    var costFrame = SynergyImage("CostFrame", slot.transform, slot.rectTransform.sizeDelta, Vector2.zero, null);
                    costFrame.preserveAspect = true;
                    synergyPortraits.Add((slot, ownedBackground, costFrame));
                    slot.raycastTarget = true;
                    int index = i;
                    var events = slot.gameObject.AddComponent<EventTrigger>();
                    AddSynergyRowEvent(events, EventTriggerType.PointerEnter, _ => ShowSynergyPokemonPreview(index));
                    AddSynergyRowEvent(events, EventTriggerType.PointerExit, _ => HideSynergyPokemonPreview());
                }
                var (portrait, background, frame) = synergyPortraits[i];
                portrait.gameObject.SetActive(true);
                background.gameObject.SetActive(ownedFamilies.Contains(SynergyTraits.Family(members[i])));
                portrait.sprite = deployed.Contains(members[i]) ? synergyPortraitDeployed : synergyPortraitSlot;
                var definition = FindSynergyPokemon(members[i]);
                frame.sprite = synergyPanelIcons?.FindPortraitCostFrame(definition?.Cost ?? 0);
                frame.enabled = frame.sprite != null;
                portrait.rectTransform.anchoredPosition = new Vector2(-140 + i % columns * 35, firstY - i / columns * 34);
                SetEvolutionPortrait(portrait.transform.Find("Pokemon").GetComponent<UnityEngine.UI.Image>(), members[i]);
            }
            for (int i = members.Length; i < synergyPortraits.Count; i++) synergyPortraits[i].Slot.gameObject.SetActive(false);
        }

        private bool CanChooseSynergyTarget() => IsOnlineMatch
            ? OnlineInputReady && onlineService.LocalMatchState?.phase == MatchPhase.Preparation &&
                onlineService.WatchingPlayerId == onlineService.LocalMatchState.playerId
            : Match?.Phase == MatchPhase.Preparation && localViewedPlayerId == "p1" && !Player.IsEliminated &&
                !busy && !RoundLoop.NextPreparationPending && Simulation?.Status != SimulationStatus.Running;

        private bool CanConfigureSynergyRow(string key, int unique) =>
            unique >= 0 && unique <= 1 || key == SynergyTraits.Mystic && mysticChoiceAvailable;

        private void ChooseMysticOrigin(string origin)
        {
            if (synergyTargetKey != SynergyTraits.Mystic || !mysticChoiceAvailable || !CanChooseSynergyTarget()) return;
            if (IsOnlineMatch)
            {
                SendOnlineCommand(MatchCommandKind.ConfigureSynergy, command => { command.bench = 2; command.itemId = origin; });
                return;
            }
            var result = commands.ConfigureSynergy(2, null, origin);
            Feedback(result.Accepted ? "예언: " + TraitName(origin) : result.Message);
            Render();
        }

        private void OpenSynergyTarget(string key)
        {
            if (!CanChooseSynergyTarget() || synergyEntries == null || key == null) return;
            var entry = synergyEntries.FirstOrDefault(item => item.key == key);
            if (entry.key == null || !CanConfigureSynergyRow(entry.key, entry.unique)) return;
            if (synergyTargetKey == key)
            {
                CloseSynergyTarget();
                RefreshSynergyRows();
                return;
            }
            string sourceId = null;
            if (entry.unique >= 0)
            {
                string family = SynergyTraits.Family(UniqueSynergies[entry.unique].species[0]);
                var source = synergyBoard.Where(unit => SynergyTraits.Family(unit.species) == family)
                    .OrderByDescending(unit => unit.stage).ThenByDescending(unit => unit.rank).ThenBy(unit => unit.id, StringComparer.Ordinal).FirstOrDefault();
                if (source.id == null) return;
                sourceId = source.id;
            }
            CloseSynergyTarget();
            if (key == SynergyTraits.Mystic) synergySettings.gameObject.SetActive(false);
            DeselectUnit();
            synergyTargetKey = key;
            synergyTargetSourceId = sourceId;
            synergyHovered = null;
            RefreshSynergyRows();
            RenderSynergyTarget();
        }

        private bool IsSynergyCandidate((string id, string species, BoardPosition cell, string[] traits, int stage, int rank, string target) source,
            (string id, string species, BoardPosition cell, string[] traits, int stage, int rank, string target) candidate)
            => candidate.id != source.id && (synergyTargetKey == "unique-0" || HexCoordinates.Distance(source.cell, candidate.cell) == 1);

        private bool TrySelectSynergyTarget(string id)
        {
            if (synergyTargetSourceId == null) return false;
            var source = synergyBoard.FirstOrDefault(unit => unit.id == synergyTargetSourceId);
            var candidate = synergyBoard.FirstOrDefault(unit => unit.id == id);
            if (!CanChooseSynergyTarget() || source.id == null || candidate.id == null || !IsSynergyCandidate(source, candidate)) return true;
            if (IsOnlineMatch)
                SendOnlineCommand(MatchCommandKind.ConfigureSynergy, command => { command.bench = 0; command.unitId = source.id; command.itemId = candidate.id; });
            else
            {
                Feedback(commands.ConfigureSynergy(0, source.id, candidate.id).Message);
                Render();
            }
            return true;
        }

        private void RenderSynergyTarget()
        {
            if (synergyTargetKey == null) return;
            var source = synergyBoard.FirstOrDefault(unit => unit.id == synergyTargetSourceId);
            bool mystic = synergyTargetKey == SynergyTraits.Mystic;
            if ((mystic ? !mysticChoiceAvailable : source.id == null) ||
                IsDragging || IsItemDragging || Keyboard.current?.escapeKey.wasPressedThisFrame == true ||
                (!CanChooseSynergyTarget() && !(IsOnlineMatch && onlineService.PendingCommand != null &&
                    onlineService.LocalMatchState?.phase == MatchPhase.Preparation && onlineService.WatchingPlayerId == onlineService.LocalMatchState.playerId)))
            {
                CloseSynergyTarget();
                return;
            }
            synergyTargetPanel.gameObject.SetActive(true);
            synergyTargetPanel.SetAsLastSibling();
            synergyTargetSource.gameObject.SetActive(!mystic);
            synergyTargetCurrent.gameObject.SetActive(!mystic);
            synergySourcePortrait.gameObject.SetActive(!mystic);
            synergyCurrentPortrait.gameObject.SetActive(!mystic);
            mysticOriginVisuals.gameObject.SetActive(mystic);
            foreach (var button in mysticOriginChoices) button.gameObject.SetActive(mystic);
            if (mystic)
            {
                string prophecy = IsOnlineMatch ? onlineService.LocalMatchState.prophecyOrigin : Player.Synergy.ProphecyOrigin;
                synergyTargetTitle.text = "신비 예언 계열 설정";
                synergyTargetEffect.text = "현재 예언: " + (prophecy == null ? "없음" : TraitName(prophecy));
                for (int i = 0; i < mysticOriginChoices.Length; i++)
                    mysticOriginChoices[i].interactable = CanChooseSynergyTarget() && SynergyTraits.Origins[i] != prophecy;
                return;
            }
            var candidates = synergyBoard.Where(unit => IsSynergyCandidate(source, unit)).ToArray();
            var selected = candidates.FirstOrDefault(unit => unit.id == source.target);
            if (selected.id == null && source.target == null && synergyTargetKey == "unique-1")
                selected = candidates.OrderBy(unit => unit.id, StringComparer.Ordinal).FirstOrDefault();
            var layer = canvasRect.Find("PlacementArt");
            if (synergyLink == null)
                synergyLink = SynergyImage("SynergyLink", layer, Vector2.zero, Vector2.zero, SynergySprite("TargetLink"));
            synergyLink.gameObject.SetActive(selected.id != null);
            Vector2 origin = TablePoint(source.cell);
            if (selected.id != null)
            {
                Vector2 target = TablePoint(selected.cell);
                Vector2 delta = target - origin;
                synergyLink.rectTransform.anchoredPosition = (origin + target) * .5f;
                synergyLink.rectTransform.sizeDelta = new Vector2(delta.magnitude, 4);
                synergyLink.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            }
            SetSynergyTargetMark(0, origin, true, layer);
            for (int i = 0; i < candidates.Length; i++)
                SetSynergyTargetMark(i + 1, TablePoint(candidates[i].cell), candidates[i].id == selected.id, layer);
            for (int i = candidates.Length + 1; i < synergyTargetMarks.Count; i++) synergyTargetMarks[i].gameObject.SetActive(false);
            synergyTargetTitle.text = (synergyTargetKey == "unique-0" ? "텍스처" : "승선") + " 대상 설정";
            synergyTargetSource.text = KoreanInfo(source.species);
            SetEvolutionPortrait(synergySourcePortrait, source.species);
            if (selected.id != null) SetEvolutionPortrait(synergyCurrentPortrait, selected.species);
            else synergyCurrentPortrait.enabled = false;
            synergyTargetCurrent.text = "현재 대상: " + (selected.id == null ? "없음" : KoreanInfo(selected.species));
            string copied = selected.id == null ? null : catalog.Get(selected.species).TraitIds.FirstOrDefault(SynergyTraits.Origins.Contains);
            synergyTargetEffect.text = synergyTargetKey == "unique-0" && copied != null
                ? "복사 계열: " + TraitName(copied)
                : candidates.Length == 0 ? "선택 가능한 아군 없음" : "보드에서 아군을 선택하세요";
        }

        private void SetSynergyTargetMark(int index, Vector2 position, bool strong, Transform layer)
        {
            if (index == synergyTargetMarks.Count)
                synergyTargetMarks.Add(SynergyImage("SynergyTargetHex", layer, BoardCellSize, Vector2.zero, synergyHex));
            var mark = synergyTargetMarks[index];
            mark.gameObject.SetActive(true);
            mark.rectTransform.anchoredPosition = position;
            mark.color = new Color(1, 1, 1, strong ? 1 : .42f);
        }

        private void CloseSynergyTarget()
        {
            synergyTargetKey = synergyTargetSourceId = null;
            if (synergyTargetPanel != null) synergyTargetPanel.gameObject.SetActive(false);
            if (synergyLink != null) synergyLink.gameObject.SetActive(false);
            foreach (var mark in synergyTargetMarks) mark.gameObject.SetActive(false);
        }
    }
}
