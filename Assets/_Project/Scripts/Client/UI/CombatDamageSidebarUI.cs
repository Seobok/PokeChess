using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Battle;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private sealed class DamageSidebarRow
        {
            public UnityEngine.UI.Image Background, Portrait, Fill, MagicFill, TrueFill;
            public UnityEngine.UI.Text Name, Value;
        }
        private sealed class DamageSidebarStat
        {
            public string Id, Species;
            public int Rank;
            public float Dealt, Taken;
            public BattleDamageBreakdown DealtTypes, TakenTypes;
        }
        private readonly DamageSidebarRow[] damageSidebarRows = new DamageSidebarRow[10];
        private DamageSidebarStat[] damageSidebarStats = Array.Empty<DamageSidebarStat>();
        private RectTransform damageSidebarRoot, damageSidebarBody, damageSidebarModes;
        private UnityEngine.UI.Button playerSidebarTab, damageSidebarTab, damageDealtTab, damageTakenTab;
        private bool damageSidebarSelected, damageTakenSelected;
        private object damageSidebarScope;
        private int damageSidebarRound;
        private static readonly Color DamageTabSelected = new Color(.55f, .94f, 1f);

        private void BuildCombatDamageSidebar()
        {
            damageSidebarRoot = UiPrefab.Instantiate("CombatDamagePanel", sidebar, itemFont);
            damageSidebarBody = UiPrefab.Get<RectTransform>(damageSidebarRoot, "Rows");
            damageSidebarModes = UiPrefab.Get<RectTransform>(damageSidebarRoot, "Modes");
            playerSidebarTab = UiPrefab.Bind(damageSidebarRoot, "Tabs/Players", () =>
                { damageSidebarSelected = false; RefreshSidebarPresentation(); });
            damageSidebarTab = UiPrefab.Bind(damageSidebarRoot, "Tabs/Damage", () =>
                { damageSidebarSelected = true; RefreshSidebarPresentation(); });
            damageDealtTab = UiPrefab.Bind(damageSidebarRoot, "Modes/Dealt", () =>
                { damageTakenSelected = false; RefreshSidebarPresentation(); });
            damageTakenTab = UiPrefab.Bind(damageSidebarRoot, "Modes/Taken", () =>
                { damageTakenSelected = true; RefreshSidebarPresentation(); });
            for (int i = 0; i < damageSidebarRows.Length; i++)
            {
                var root = damageSidebarBody.Find("Row_" + i);
                damageSidebarRows[i] = new DamageSidebarRow
                {
                    Background = root.GetComponent<UnityEngine.UI.Image>(),
                    Portrait = UiPrefab.Get<UnityEngine.UI.Image>(root, "Portrait"),
                    Fill = UiPrefab.Get<UnityEngine.UI.Image>(root, "Track/Fill"),
                    MagicFill = UiPrefab.Get<UnityEngine.UI.Image>(root, "Track/MagicFill"),
                    TrueFill = UiPrefab.Get<UnityEngine.UI.Image>(root, "Track/TrueFill"),
                    Name = UiPrefab.Get<UnityEngine.UI.Text>(root, "Name"),
                    Value = UiPrefab.Get<UnityEngine.UI.Text>(root, "Value")
                };
            }
        }
        private void RefreshSidebarPresentation()
        {
            if (IsOnlineMatch) RenderOnlineMatch();
            else RenderMatchHud();
        }
        private void RefreshCombatDamageSidebar()
        {
            if (damageSidebarRoot == null) return;
            object scope = IsOnlineMatch ? (object)onlineService.PublicState?.matchId : Match;
            if (!Equals(scope, damageSidebarScope))
            {
                damageSidebarScope = scope;
                damageSidebarRound = 0;
                damageSidebarStats = Array.Empty<DamageSidebarStat>();
            }
            int round = IsOnlineMatch ? onlineService.PublicState.round : Match.RoundNumber;
            bool inBattle = IsOnlineMatch ? onlineService.PublicState.phase.IsBattlePhase() : Match.Phase.IsBattlePhase();
            if (inBattle && damageSidebarRound != round)
            {
                damageSidebarStats = Array.Empty<DamageSidebarStat>();
                damageSidebarRound = round;
            }
            if (IsOnlineMatch)
            {
                var owner = onlineService.LocalMatchState;
                var frame = onlineService.Combat.Current;
                // Spectating an opponent must never substitute enemy statistics for our own.
                if (owner != null && frame != null && frame.round == onlineService.PublicState.round &&
                    (frame.one == owner.playerId || !frame.shadow && frame.two == owner.playerId))
                {
                    int team = frame.one == owner.playerId ? 1 : 2;
                    damageSidebarStats = (frame.units ?? Array.Empty<CombatUnitView>())
                        .Where(unit => unit.team == team && unit.owner == owner.playerId)
                        .Select(unit => new DamageSidebarStat { Id = unit.id, Species = unit.definitionId,
                            Rank = unit.rank, Dealt = unit.damageDealt, Taken = unit.damageTaken, DealtTypes = unit.damageDealtTypes, TakenTypes = unit.damageTakenTypes }).ToArray();
                }
            }
            else
            {
                var battle = RoundLoop?.GetBattleFor("p1")?.Battle;
                if (battle != null)
                    damageSidebarStats = battle.Units.Where(unit => unit.OwnerPlayerId == "p1")
                        .Select(unit => new DamageSidebarStat { Id = unit.UnitInstanceId, Species = unit.DefinitionId,
                            Rank = (int)unit.Rank, Dealt = battle.DamageDealtBy(unit.UnitInstanceId),
                            Taken = battle.DamageTakenBy(unit.UnitInstanceId), DealtTypes = battle.DamageDealtTypes(unit.UnitInstanceId), TakenTypes = battle.DamageTakenTypes(unit.UnitInstanceId) }).ToArray();
            }
            foreach (var row in hudProfiles.Values) row.Rect.gameObject.SetActive(!damageSidebarSelected);
            damageSidebarBody.gameObject.SetActive(damageSidebarSelected);
            damageSidebarModes.gameObject.SetActive(damageSidebarSelected);
            playerSidebarTab.image.color = damageSidebarSelected ? Color.white : DamageTabSelected;
            damageSidebarTab.image.color = damageSidebarSelected ? DamageTabSelected : Color.white;
            damageDealtTab.image.color = damageTakenSelected ? Color.white : DamageTabSelected;
            damageTakenTab.image.color = damageTakenSelected ? DamageTabSelected : Color.white;
            if (!damageSidebarSelected) return;
            sidebar.sizeDelta = new Vector2(sidebar.sizeDelta.x, 450);
            var stats = damageSidebarStats.OrderByDescending(unit => damageTakenSelected ? unit.Taken : unit.Dealt)
                .ThenBy(unit => unit.Id, StringComparer.Ordinal).Take(10).ToArray();
            float maximum = stats.Length == 0 ? 0 : stats.Max(unit => damageTakenSelected ? unit.Taken : unit.Dealt);
            for (int i = 0; i < damageSidebarRows.Length; i++)
            {
                var row = damageSidebarRows[i];
                bool present = i < stats.Length;
                row.Background.color = present ? Color.white : new Color(1, 1, 1, .35f);
                row.Portrait.enabled = present;
                row.Name.text = row.Value.text = "";
                row.Fill.fillAmount = 0;
                row.Fill.rectTransform.sizeDelta = row.MagicFill.rectTransform.sizeDelta = row.TrueFill.rectTransform.sizeDelta = new Vector2(0, 7);
                if (!present) continue;
                var unit = stats[i];
                float amount = damageTakenSelected ? unit.Taken : unit.Dealt;
                row.Name.text = PokeChess.Content.GameContent.DisplayName(unit.Species) ?? unit.Species;
                row.Value.text = Mathf.RoundToInt(amount).ToString("N0");
                row.Fill.fillAmount = maximum > 0 ? amount / maximum : 0;
                var types = damageTakenSelected ? unit.TakenTypes : unit.DealtTypes;
                float pixels = maximum > 0 ? 166 / maximum : 0;
                float physicalWidth = types.physical * pixels, magicWidth = types.magic * pixels;
                SetDamageSegment(row.Fill, 0, physicalWidth, new Color32(255, 153, 51, 255));
                SetDamageSegment(row.MagicFill, physicalWidth, magicWidth, new Color32(51, 153, 255, 255));
                SetDamageSegment(row.TrueFill, physicalWidth + magicWidth, types.trueDamage * pixels, Color.white);
                FitDamagePortrait(row.Portrait, unit.Species);
            }
        }
        private static void SetDamageSegment(UnityEngine.UI.Image image, float offset, float width, Color color)
        {
            image.rectTransform.anchoredPosition = new Vector2(-83 + offset, 0);
            image.rectTransform.sizeDelta = new Vector2(width, 7);
            image.color = color;
        }
        private void FitDamagePortrait(UnityEngine.UI.Image image, string species)
        {
            var sprite = unitPortraitLibrary?.Find(species, "Idle")?.Frames?.FirstOrDefault();
            image.sprite = sprite;
            image.enabled = sprite != null;
            if (sprite == null) return;
            if (!unitPortraitVisibleBounds.TryGetValue(sprite, out var bounds))
            {
                var vertices = sprite.vertices;
                bounds = vertices.Length > 0 ? new Bounds(vertices[0], Vector3.zero) : sprite.bounds;
                foreach (var vertex in vertices) bounds.Encapsulate(vertex);
                unitPortraitVisibleBounds.Add(sprite, bounds);
            }
            var size = (Vector2)bounds.size * sprite.pixelsPerUnit;
            float scale = Mathf.Min(28 / Mathf.Max(1, size.x), 28 / Mathf.Max(1, size.y));
            image.useSpriteMesh = true;
            image.preserveAspect = false;
            image.rectTransform.sizeDelta = sprite.rect.size * scale;
            image.rectTransform.anchoredPosition = new Vector2(-88, 0) -
                (Vector2)(bounds.center - sprite.bounds.center) * sprite.pixelsPerUnit * scale;
        }
    }
}
