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
        private readonly List<UnityEngine.UI.Image> synergyPortraits = new List<UnityEngine.UI.Image>();
        private readonly List<UnityEngine.UI.Image> synergyTargetMarks = new List<UnityEngine.UI.Image>();
        private IReadOnlyList<PokemonDefinition> synergyRoster;
        private (string id, string species, BoardPosition cell, string[] traits, int stage, int rank, string target)[] synergyBoard;
        private (string key, string name, int count, int next, int tier, int unique)[] synergyEntries;
        private UnityEngine.UI.Button synergyPrev, synergyNext;
        private RectTransform synergyPager, synergyTargetPanel;
        private UnityEngine.UI.Text synergyPageLabel, synergyTooltipTitle, synergyMemberLabel, synergyTargetTitle,
            synergyTargetSource, synergyTargetCurrent, synergyTargetEffect;
        private UnityEngine.UI.Image synergyLink, synergySourcePortrait, synergyCurrentPortrait;
        private readonly UnityEngine.UI.Image[] synergyThresholdRows = new UnityEngine.UI.Image[4];
        private Sprite synergyRowDefault, synergyRowInactive, synergyRowSelected, synergyPortraitSlot,
            synergyPortraitDeployed, synergyHex, synergyTexture;
        private string synergyTargetKey, synergyTargetSourceId, synergyTooltipSignature;
        private int synergyPage;
        public string SynergyDisplay => string.Join(" | ", synergyRows.Where(row => row != null && row.Image.gameObject.activeSelf)
            .Select(row => row.Name.text + " " + row.Count.text));

        private static Sprite SynergySprite(string name) => Resources.Load<Sprite>("SynergyHud/Synergy" + name);

        private UnityEngine.UI.Image SynergyImage(string name, Transform parent, Vector2 size, Vector2 position, Sprite sprite)
        {
            var image = HudImage(name, parent, size, position, Color.white);
            image.sprite = sprite;
            return image;
        }

        private UnityEngine.UI.Text SynergyText(string name, Transform parent, Vector2 size, Vector2 position, int fontSize)
        {
            var label = Label(name, parent, "", fontSize, size, position);
            label.font = itemFont ?? font;
            label.color = SynergyInk;
            label.fontStyle = FontStyle.Bold;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 8;
            label.resizeTextMaxSize = fontSize;
            return label;
        }

        private void BuildSynergyHud()
        {
            synergyRoster = PokeChess.Content.GameContent.CreateDefinitions().ToArray();
            synergyRowDefault = SynergySprite("Row");
            synergyRowInactive = SynergySprite("RowInactive");
            synergyRowSelected = SynergySprite("RowSelected");
            synergyPortraitSlot = SynergySprite("PortraitSlot");
            synergyPortraitDeployed = SynergySprite("PortraitDeployed");
            synergyHex = SynergySprite("TargetHex");
            synergyTexture = SynergySprite("TextureMark");
            var header = SynergySprite("Header");
            var gear = SynergySprite("SettingsGear");

            synergyRoot = SynergyImage("Synergies", canvasRect, new Vector2(150, 420), new Vector2(-553, 120),
                SynergySprite("ListFrame")).rectTransform;
            var banner = SynergyImage("Header", synergyRoot, new Vector2(139, 35), new Vector2(0, 187), header);
            var title = SynergyText("Title", banner.transform, new Vector2(134, 33), Vector2.zero, 17);
            title.text = "시너지";
            title.color = Color.white;

            for (int i = 0; i < synergyRows.Length; i++)
            {
                var image = SynergyImage("SynergyRow_" + i, synergyRoot, new Vector2(139, 35), new Vector2(0, 149 - i * 37), synergyRowDefault);
                image.raycastTarget = true;
                var row = new SynergyHudRow { Image = image };
                synergyRows[i] = row;
                row.Icon = SynergyImage("Mark", image.transform, new Vector2(21, 21), new Vector2(-53, 0), null);
                row.Glyph = SynergyText("Glyph", row.Icon.transform, new Vector2(21, 21), Vector2.zero, 14);
                row.Name = SynergyText("Name", image.transform, new Vector2(56, 30), new Vector2(-13, 0), 13);
                row.Name.alignment = TextAnchor.MiddleLeft;
                row.Count = SynergyText("Count", image.transform, new Vector2(49, 30), new Vector2(38, 0), 13);
                row.Count.alignment = TextAnchor.MiddleRight;
                row.Gear = SynergyImage("Gear", image.transform, new Vector2(12, 12), new Vector2(62, 0), gear);
                var events = image.gameObject.AddComponent<EventTrigger>();
                AddSynergyRowEvent(events, EventTriggerType.PointerEnter, _ =>
                {
                    synergyHovered = row.Key;
                    RefreshSynergyRows();
                });
                AddSynergyRowEvent(events, EventTriggerType.PointerExit, _ =>
                {
                    if (synergyHovered == row.Key) synergyHovered = null;
                    RefreshSynergyRows();
                });
                AddSynergyRowEvent(events, EventTriggerType.PointerClick, data =>
                {
                    if (data is PointerEventData pointer && pointer.button == PointerEventData.InputButton.Left)
                        OpenSynergyTarget(row.Key);
                });
            }
            synergyPager = Rect("SynergyPages", synergyRoot, new Vector2(136, 24), new Vector2(0, -145));
            var page = SynergyImage("PageBadge", synergyPager, new Vector2(61, 20), Vector2.zero, InventorySprite("InventoryPageBadge"));
            synergyPageLabel = SynergyText("Page", page.transform, new Vector2(55, 19), Vector2.zero, 12);
            synergyPrev = BuildSynergyPageButton("PreviousPage", -49, -1, "InventoryPagePrev");
            synergyNext = BuildSynergyPageButton("NextPage", 49, 1, "InventoryPageNext");

            synergyTooltip = SynergyImage("SynergyHelp", canvasRect, new Vector2(340, 390), new Vector2(-150, 105),
                SynergySprite("TooltipFrame")).rectTransform;
            SynergyImage("Pointer", synergyTooltip, new Vector2(19, 21), new Vector2(-176, 115), SynergySprite("TooltipPointer"));
            var tipHeader = SynergyImage("Header", synergyTooltip, new Vector2(320, 35), new Vector2(0, 169), header);
            synergyTooltipTitle = SynergyText("Title", tipHeader.transform, new Vector2(294, 32), Vector2.zero, 17);
            synergyTooltipTitle.color = Color.white;
            synergyHelp = SynergyText("Description", synergyTooltip, new Vector2(310, 49), new Vector2(0, 123), 12);
            synergyHelp.alignment = TextAnchor.MiddleLeft;
            for (int i = 0; i < synergyThresholdRows.Length; i++)
            {
                var tier = SynergyImage("Threshold_" + i, synergyTooltip, new Vector2(318, 49), new Vector2(0, 100 - i * 51),
                    SynergySprite("ThresholdActive"));
                synergyThresholdRows[i] = tier;
                var badge = SynergyImage("Badge", tier.transform, new Vector2(27, 23), new Vector2(-142, 0), SynergySprite("ThresholdBadge"));
                SynergyText("Count", badge.transform, new Vector2(25, 22), Vector2.zero, 12);
                var detail = SynergyText("Effect", tier.transform, new Vector2(274, 46), new Vector2(15, 0), 12);
                detail.alignment = TextAnchor.MiddleLeft;
            }
            synergyMemberLabel = SynergyText("Members", synergyTooltip, new Vector2(311, 21), new Vector2(0, -111), 13);
            synergyMemberLabel.alignment = TextAnchor.MiddleLeft;
            synergyMemberLabel.text = "해당 포켓몬";
            synergyTooltip.gameObject.SetActive(false);

            synergyTargetPanel = SynergyImage("SynergyTargetPanel", canvasRect, new Vector2(206, 141), new Vector2(288, 132),
                SynergySprite("TargetPanel")).rectTransform;
            var targetHeader = SynergyImage("Header", synergyTargetPanel, new Vector2(192, 29), new Vector2(0, 49), header);
            synergyTargetTitle = SynergyText("Title", targetHeader.transform, new Vector2(180, 27), Vector2.zero, 14);
            synergyTargetTitle.color = Color.white;
            synergySourcePortrait = SynergyImage("SourcePortrait", synergyTargetPanel, new Vector2(29, 27), new Vector2(-75, 15), null);
            synergySourcePortrait.preserveAspect = true;
            synergyTargetSource = SynergyText("Source", synergyTargetPanel, new Vector2(146, 24), new Vector2(11, 15), 13);
            synergyTargetSource.alignment = TextAnchor.MiddleLeft;
            synergyCurrentPortrait = SynergyImage("TargetPortrait", synergyTargetPanel, new Vector2(27, 24), new Vector2(75, -14), null);
            synergyCurrentPortrait.preserveAspect = true;
            synergyTargetCurrent = SynergyText("CurrentTarget", synergyTargetPanel, new Vector2(151, 24), new Vector2(-15, -14), 13);
            synergyTargetCurrent.alignment = TextAnchor.MiddleLeft;
            synergyTargetEffect = SynergyText("Effect", synergyTargetPanel, new Vector2(182, 32), new Vector2(0, -43), 12);
            synergyTargetPanel.gameObject.SetActive(false);
        }

        private static void AddSynergyRowEvent(EventTrigger events, EventTriggerType type, Action<BaseEventData> callback)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => callback(data));
            events.triggers.Add(entry);
        }

        private UnityEngine.UI.Button BuildSynergyPageButton(string name, float x, int step, string asset)
        {
            var image = SynergyImage(name, synergyPager, new Vector2(19, 20), new Vector2(x, 0), InventorySprite(asset));
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => { synergyPage += step; synergyHovered = null; RefreshSynergyRows(); });
            return button;
        }

        private void RenderSynergies(IReadOnlyList<SynergyCount> counts, bool visible)
        {
            synergyRoot.gameObject.SetActive(visible);
            if (!visible)
            {
                synergyHovered = null;
                synergyTooltip.gameObject.SetActive(false);
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
                .Where(entry => entry.data.species.All(id => families.Contains(SynergyTraits.Family(id))))
                .Select(entry => ("unique-" + entry.i, entry.data.name, 1, 1, 1, entry.i));
            var normal = counts.Where(c => c.Count > 0)
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
                row.Gear.gameObject.SetActive(entry.unique >= 0 && entry.unique <= 1 && CanChooseSynergyTarget());
                row.Count.rectTransform.anchoredPosition = new Vector2(row.Gear.gameObject.activeSelf ? 28 : 38, 0);
                row.Icon.sprite = entry.unique == 0 ? synergyTexture : null;
                row.Icon.enabled = row.Icon.sprite != null;
                row.Glyph.gameObject.SetActive(!row.Icon.enabled);
                row.Glyph.text = SynergyMark(entry.key, entry.name);
            }
            synergyPager.gameObject.SetActive(pages > 1);
            synergyPageLabel.text = $"{synergyPage + 1} / {pages}";
            synergyPrev.interactable = synergyPage > 0;
            synergyNext.interactable = synergyPage + 1 < pages;
            var hovered = synergyEntries.FirstOrDefault(entry => entry.key == synergyHovered);
            bool showTooltip = hovered.key != null && synergyTargetKey == null && synergyRows.Any(row => row.Key == hovered.key);
            synergyTooltip.gameObject.SetActive(showTooltip);
            if (showTooltip) RenderSynergyTooltip(hovered);
            else synergyTooltipSignature = null;
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
            if (signature == synergyTooltipSignature) return;
            synergyTooltipSignature = signature;
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
            RenderSynergyPortraits(membership, deployed, membersY - 26);
            synergyTooltip.SetAsLastSibling();
        }

        private void RenderSynergyPortraits(string[] members, string[] deployed, float firstY)
        {
            const int columns = 9;
            for (int i = 0; i < members.Length; i++)
            {
                if (i == synergyPortraits.Count)
                {
                    var slot = SynergyImage("Portrait_" + i, synergyTooltip, new Vector2(32, 32), Vector2.zero, synergyPortraitSlot);
                    SynergyImage("Pokemon", slot.transform, new Vector2(26, 26), Vector2.zero, null).preserveAspect = true;
                    synergyPortraits.Add(slot);
                }
                var portrait = synergyPortraits[i];
                portrait.gameObject.SetActive(true);
                portrait.sprite = deployed.Contains(members[i]) ? synergyPortraitDeployed : synergyPortraitSlot;
                portrait.rectTransform.anchoredPosition = new Vector2(-140 + i % columns * 35, firstY - i / columns * 34);
                SetEvolutionPortrait(portrait.transform.Find("Pokemon").GetComponent<UnityEngine.UI.Image>(), members[i]);
            }
            for (int i = members.Length; i < synergyPortraits.Count; i++) synergyPortraits[i].gameObject.SetActive(false);
        }

        private bool CanChooseSynergyTarget() => IsOnlineMatch
            ? OnlineInputReady && onlineService.LocalMatchState?.phase == MatchPhase.Preparation &&
                onlineService.WatchingPlayerId == onlineService.LocalMatchState.playerId
            : Match?.Phase == MatchPhase.Preparation && localViewedPlayerId == "p1" && !Player.IsEliminated &&
                !busy && !RoundLoop.NextPreparationPending && Simulation?.Status != SimulationStatus.Running;

        private void OpenSynergyTarget(string key)
        {
            if (!CanChooseSynergyTarget() || synergyEntries == null || key == null) return;
            var entry = synergyEntries.FirstOrDefault(item => item.key == key);
            if (entry.key == null || entry.unique < 0 || entry.unique > 1) return;
            if (synergyTargetKey == key)
            {
                CloseSynergyTarget();
                RefreshSynergyRows();
                return;
            }
            string family = SynergyTraits.Family(UniqueSynergies[entry.unique].species[0]);
            var source = synergyBoard.Where(unit => SynergyTraits.Family(unit.species) == family)
                .OrderByDescending(unit => unit.stage).ThenByDescending(unit => unit.rank).ThenBy(unit => unit.id, StringComparer.Ordinal).FirstOrDefault();
            if (source.id == null) return;
            DeselectUnit();
            synergyTargetKey = key;
            synergyTargetSourceId = source.id;
            synergyHovered = null;
            RefreshSynergyRows();
            RenderSynergyTarget();
        }

        private bool IsSynergyCandidate((string id, string species, BoardPosition cell, string[] traits, int stage, int rank, string target) source,
            (string id, string species, BoardPosition cell, string[] traits, int stage, int rank, string target) candidate)
            => candidate.id != source.id && (synergyTargetKey == "unique-0" || HexCoordinates.Distance(source.cell, candidate.cell) == 1);

        private bool TrySelectSynergyTarget(string id)
        {
            if (synergyTargetKey == null) return false;
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
            if (source.id == null || IsDragging || IsItemDragging || Keyboard.current?.escapeKey.wasPressedThisFrame == true ||
                (!CanChooseSynergyTarget() && !(IsOnlineMatch && onlineService.PendingCommand != null &&
                    onlineService.LocalMatchState?.phase == MatchPhase.Preparation && onlineService.WatchingPlayerId == onlineService.LocalMatchState.playerId)))
            {
                CloseSynergyTarget();
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
            synergyTargetPanel.gameObject.SetActive(true);
            synergyTargetPanel.SetAsLastSibling();
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