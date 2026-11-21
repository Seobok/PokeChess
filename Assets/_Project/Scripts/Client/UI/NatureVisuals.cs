using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using UnityEngine;

namespace PokeChess.Client.UI
{
    public sealed class NatureVisuals : ScriptableObject
    {
        public Sprite[] summon, worldtreeSummon, tauntWave, tauntMarker;
        public Sprite[] rootEnter, rootLoop, rootExit;
        public Sprite[] shieldEnter, shieldLoop, shieldBreak, shieldExpire;
        public Sprite[] pulse, pulseHit, judgment, judgmentHit, heal;
    }

    internal sealed class NatureBattleVisuals
    {
        private const double Fps = 12;
        private const string RootStatus = "synergy-control-Root";
        private const string TauntStatus = "synergy-control-Taunt";

        private enum ShieldPhase { None, Enter, Hold, Break, Expire }

        private sealed class Burst
        {
            public UnityEngine.UI.Image Image;
            public BoardPosition Cell;
            public long Tick;
            public int Tier;
        }

        private sealed class UnitLayers
        {
            public UnityEngine.UI.Image Root, Taunt, Shield;
            public long RootEnd = -1, ShieldEnd;
            public long ShieldTick = -1;
            public ShieldPhase ShieldPhase;
            public bool HadShield;
        }

        private readonly Dictionary<(string Clip, string Unit), Burst> bursts = new();
        private readonly Dictionary<string, UnitLayers> layers = new(StringComparer.Ordinal);
        private NatureVisuals art;

        public void Signal(Transform root, string detail, string unit, BoardPosition cell, long tick, int tier)
        {
            switch (detail)
            {
                case "SUMMON":
                    // Summon effects belong to preparation roster changes.
                    break;
                case "GUARDIAN_CAST":
                    BurstAt(root, "TauntWave", unit, cell, tick, tier);
                    if (tier >= 10) BurstAt(root, "Judgment", unit, cell, tick, tier);
                    break;
                case "PULSE": BurstAt(root, "Pulse", unit, cell, tick, tier); break;
                case "PULSE_HIT": BurstAt(root, "PulseHit", unit, cell, tick, tier); break;
                case "JUDGMENT_HIT": BurstAt(root, "JudgmentHit", unit, cell, tick, tier); break;
                case "HEAL": BurstAt(root, "Heal", unit, cell, tick, tier); break;
            }
        }

        private void BurstAt(Transform root, string clip, string unit, BoardPosition cell, long tick, int tier)
        {
            if (string.IsNullOrEmpty(unit)) return;
            var key = (clip, unit);
            if (!bursts.TryGetValue(key, out var burst))
            {
                burst = new Burst { Image = Image(root, clip), Tick = -1 };
                bursts.Add(key, burst);
            }
            if (tick <= burst.Tick) return;
            burst.Tick = tick;
            burst.Cell = cell;
            burst.Tier = tier;
        }

        public void ShieldSignal(Transform root, string unit, string kind, float remaining, long tick)
        {
            if (string.IsNullOrEmpty(unit)) return;
            var phase = kind switch
            {
                "Applied" or "Replaced" => ShieldPhase.Enter,
                "Absorbed" when remaining <= 0 => ShieldPhase.Break,
                "Expired" or "Removed" => ShieldPhase.Expire,
                _ => ShieldPhase.None
            };
            if (phase == ShieldPhase.None) return;
            var state = Layers(unit);
            if (tick < state.ShieldTick) return;
            state.Shield ??= Image(root, "Shield");
            state.ShieldTick = tick;
            state.ShieldPhase = phase;
        }

        public void Render(Transform root, SkillVisualUnit[] units, Func<int, int, Vector2> point, double tick, int rate, bool complete)
        {
            if (complete) { Clear(); return; }
            if (point == null || bursts.Count == 0 && layers.Count == 0 && !units.Any(unit => unit.NatureTier > 0)) return;
            art ??= Resources.Load<NatureVisuals>("NatureVisuals");
            if (art == null) return;

            float width = Vector2.Distance(point(0, 0), point(1, 0));
            rate = Math.Max(1, rate);
            var roster = units.ToDictionary(unit => unit.Id, StringComparer.Ordinal);
            foreach (var entry in bursts)
                RenderBurst(entry.Key.Clip, entry.Key.Unit, entry.Value, roster, point, tick, rate, width);

            foreach (var entry in layers)
            {
                Hide(entry.Value.Root);
                Hide(entry.Value.Taunt);
                Hide(entry.Value.Shield);
            }
            foreach (var unit in units)
            {
                if (!unit.Alive) continue;
                var statuses = unit.Statuses ?? Array.Empty<CombatStatusView>();
                var rootStatus = statuses.Where(s => s.definition == RootStatus && s.startTick <= tick && s.endTick > tick)
                    .Where(s => s.source != null && roster.TryGetValue(s.source, out var source) && source.NatureTier > 0)
                    .OrderByDescending(s => s.endTick).FirstOrDefault();
                var taunt = statuses.Where(s => s.crowdControl == (int)CrowdControlKind.Taunt && s.startTick <= tick && s.endTick > tick)
                    .Where(s => s.source != null && roster.TryGetValue(s.source, out var source) && source.Targetable && source.Team != unit.Team)
                    .OrderByDescending(s => s.endTick).ThenBy(s => s.source, StringComparer.Ordinal).FirstOrDefault();
                var taunter = taunt != null && roster[taunt.source].NatureTier > 0 ? roster[taunt.source] : null;
                var shield = (unit.Shields ?? Array.Empty<CombatShieldView>())
                    .FirstOrDefault(s => s.group == SynergyTraits.Nature && s.remaining > 0 && s.endTick > tick);

                if (!layers.TryGetValue(unit.Id, out var state))
                {
                    if (rootStatus == null && taunter == null && shield == null) continue;
                    state = Layers(unit.Id);
                }
                RenderRoot(root, state, unit.Position, rootStatus, tick, rate, width);
                if (taunter != null)
                {
                    state.Taunt ??= Image(root, "TauntMarker");
                    Draw(state.Taunt, art.tauntMarker, 0);
                    Place(state.Taunt, unit.Position + Vector2.up * width * .72f, width * .42f);
                    var delta = taunter.Position - unit.Position;
                    state.Taunt.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90);
                    state.Taunt.transform.SetAsLastSibling();
                }
                RenderShield(root, state, unit, shield, tick, rate, width);
            }
        }

        private void RenderBurst(string clip, string unitId, Burst burst, Dictionary<string, SkillVisualUnit> roster,
            Func<int, int, Vector2> point, double tick, int rate, float width)
        {
            var frames = clip switch
            {
                "Summon" => art.summon,
                "WorldtreeSummon" => art.worldtreeSummon,
                "TauntWave" => art.tauntWave,
                "Pulse" => art.pulse,
                "PulseHit" => art.pulseHit,
                "Judgment" => art.judgment,
                "JudgmentHit" => art.judgmentHit,
                "Heal" => art.heal,
                _ => null
            };
            var image = burst.Image;
            if (!Draw(image, frames, (tick - burst.Tick) / rate)) return;
            var position = point(burst.Cell.Column, burst.Cell.Row);
            float scale = clip switch
            {
                "Summon" => 1.85f,
                "WorldtreeSummon" => 3f,
                "TauntWave" => burst.Tier >= 10 ? 5f : 3f,
                "Pulse" => 3.4f,
                "Judgment" => 2.8f,
                "JudgmentHit" => 1.45f,
                _ => 1f
            };
            if (clip == "Heal")
            {
                if (!roster.TryGetValue(unitId, out var unit) || !unit.Alive) { Hide(image); return; }
                position = unit.Position + Vector2.up * width * .24f;
            }
            else if (clip == "Judgment") position += Vector2.up * width * .8f;
            else if (clip == "JudgmentHit") position += Vector2.up * width * .28f;

            Place(image, position, width * scale);
            if (clip is "Summon" or "WorldtreeSummon" or "TauntWave" or "Pulse")
                image.transform.SetAsFirstSibling();
            else image.transform.SetAsLastSibling();
        }

        private void RenderRoot(Transform root, UnitLayers state, Vector2 position, CombatStatusView status,
            double tick, int rate, float width)
        {
            if (status != null)
            {
                state.Root ??= Image(root, "Root");
                double age = (tick - status.startTick) / rate;
                double enter = (art.rootEnter?.Length ?? 0) / Fps;
                if (age < enter) Draw(state.Root, art.rootEnter, age);
                else Draw(state.Root, art.rootLoop, age - enter, true);
                state.RootEnd = status.endTick;
            }
            else
            {
                if (state.Root == null || state.RootEnd < 0) return;
                if (tick < state.RootEnd) state.RootEnd = (long)tick;
                Draw(state.Root, art.rootExit, (tick - state.RootEnd) / rate);
            }
            Place(state.Root, position + Vector2.down * width * .16f, width);
            state.Root.transform.SetAsLastSibling();
        }

        private void RenderShield(Transform root, UnitLayers state, SkillVisualUnit unit, CombatShieldView shield,
            double tick, int rate, float width)
        {
            if (shield != null)
            {
                state.Shield ??= Image(root, "Shield");
                if (state.ShieldPhase is ShieldPhase.None or ShieldPhase.Break or ShieldPhase.Expire)
                {
                    state.ShieldPhase = tick <= 1 ? ShieldPhase.Enter : ShieldPhase.Hold;
                    state.ShieldTick = (long)tick;
                }
                state.ShieldEnd = shield.endTick;
            }
            else if (state.HadShield && state.ShieldPhase is ShieldPhase.Enter or ShieldPhase.Hold)
            {
                state.ShieldPhase = tick >= state.ShieldEnd ? ShieldPhase.Expire : ShieldPhase.Break;
                state.ShieldTick = (long)tick;
            }
            state.HadShield = shield != null;
            if (state.Shield == null) return;

            double age = (tick - state.ShieldTick) / rate;
            double enter = (art.shieldEnter?.Length ?? 0) / Fps;
            bool hold = shield != null && (state.ShieldPhase == ShieldPhase.Hold ||
                state.ShieldPhase == ShieldPhase.Enter && age >= enter);
            var frames = hold ? art.shieldLoop : state.ShieldPhase switch
            {
                ShieldPhase.Enter => art.shieldEnter,
                ShieldPhase.Break => art.shieldBreak,
                ShieldPhase.Expire => art.shieldExpire,
                _ => null
            };
            if (hold && state.ShieldPhase == ShieldPhase.Enter) age -= enter;
            Draw(state.Shield, frames, age, hold);
            float scale = unit.NatureTier >= 10 ? 1.9f : unit.NatureTier >= 6 ? 1.55f : 1.4f;
            Place(state.Shield, unit.Position + Vector2.up * width * .33f, width * scale);
            state.Shield.transform.SetAsLastSibling();
        }

        private UnitLayers Layers(string id)
        {
            if (!layers.TryGetValue(id, out var state)) layers.Add(id, state = new UnitLayers());
            return state;
        }

        private static UnityEngine.UI.Image Image(Transform root, string name)
        {
            var image = new GameObject("Nature_" + name, typeof(RectTransform), typeof(UnityEngine.UI.Image))
                .GetComponent<UnityEngine.UI.Image>();
            image.transform.SetParent(root, false);
            image.raycastTarget = false;
            image.enabled = false;
            return image;
        }

        private static void Place(UnityEngine.UI.Image image, Vector2 position, float width)
        {
            image.rectTransform.anchoredPosition = position;
            image.rectTransform.sizeDelta = Vector2.one * width;
        }

        private static bool Draw(UnityEngine.UI.Image image, Sprite[] frames, double age, bool loop = false)
        {
            int frame = (int)Math.Floor(age * Fps);
            image.enabled = frames != null && frames.Length > 0 && frame >= 0 && (loop || frame < frames.Length);
            if (image.enabled) image.sprite = frames[frame % frames.Length];
            return image.enabled;
        }

        private static void Hide(UnityEngine.UI.Image image)
        {
            if (image != null) image.enabled = false;
        }

        private static void Release(UnityEngine.UI.Image image)
        {
            if (image == null) return;
            Hide(image);
            if (Application.isPlaying) UnityEngine.Object.Destroy(image.gameObject);
            else UnityEngine.Object.DestroyImmediate(image.gameObject);
        }

        public void Clear()
        {
            foreach (var burst in bursts.Values) Release(burst.Image);
            foreach (var state in layers.Values)
            {
                Release(state.Root);
                Release(state.Taunt);
                Release(state.Shield);
            }
            bursts.Clear();
            layers.Clear();
        }
    }
}
