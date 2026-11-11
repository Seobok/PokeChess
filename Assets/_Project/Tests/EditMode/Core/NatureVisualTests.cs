#if UNITY_EDITOR
using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Client.UI;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using UnityEngine;

namespace PokeChess.Core.Tests
{
    public sealed class NatureVisualTests
    {
        private GameObject root;
        private SkillVisualBattleView view;
        private NatureVisuals art;
        private CombatFrame frame;
        private CombatUnitView guardian, enemy;
        private static Vector2 Point(int column, int row) => new Vector2(column * 60, -row * 45);

        [SetUp] public void Setup()
        {
            root = new GameObject("NatureVisualTest", typeof(RectTransform));
            view = SkillVisualBattleView.Get(root.transform, null);
            art = Resources.Load<NatureVisuals>("NatureVisuals");
            guardian = new CombatUnitView { id = "guardian", team = 1, isSynergySummon = true, natureTier = 10, hp = 7500, onBoard = true, column = 2, row = 3 };
            enemy = new CombatUnitView { id = "enemy", team = 2, hp = 100, onBoard = true, column = 3, row = 3 };
            frame = new CombatFrame { battleId = "nature", tickRate = 30, units = new[] { guardian, enemy } };
        }

        [TearDown] public void Cleanup() => UnityEngine.Object.DestroyImmediate(root);

        private UnityEngine.UI.Image Find(string clip)
            => root.GetComponentsInChildren<UnityEngine.UI.Image>(true).SingleOrDefault(image => image.name == "Nature_" + clip);
        private void Render(double tick) => view.RenderPlayback(frame, tick, Point);
        private static CombatEventView Signal(string name, CombatUnitView target, long tick, int value = 10)
            => new CombatEventView { kind = "SynergyTile", detail = "nature:" + name, source = "guardian",
                target = target.id, tick = tick, value = value, synergyCell = new CombatCellView { column = target.column, row = target.row } };

        [Test] public void WorldtreeSummonPulseAndManaSkillAreSeparate()
        {
            frame.events = new[] { Signal("SUMMON", guardian, 0) };
            Render(0);
            Assert.That(Find("WorldtreeSummon").sprite, Is.SameAs(art.worldtreeSummon[0]));

            frame.events = new[] { Signal("PULSE", guardian, 240), Signal("PULSE_HIT", enemy, 240, 150) };
            enemy.hp = 0;
            Render(240);
            Assert.That(Find("WorldtreeSummon").enabled, Is.False);
            Assert.That(Find("Pulse").sprite, Is.SameAs(art.pulse[0]));
            Assert.That(Find("PulseHit").rectTransform.anchoredPosition, Is.EqualTo(Point(3, 3)));
            Assert.That(Find("Judgment"), Is.Null);

            frame.events = new[] { Signal("GUARDIAN_CAST", guardian, 300), Signal("JUDGMENT_HIT", enemy, 300, 250), Signal("HEAL", guardian, 300, 10) };
            Render(300);
            Render(300);
            Assert.That(Find("Pulse").enabled, Is.False);
            Assert.That(Find("Judgment").sprite, Is.SameAs(art.judgment[0]));
            Assert.That(Find("JudgmentHit").sprite, Is.SameAs(art.judgmentHit[0]));
            Assert.That(Find("Heal").sprite, Is.SameAs(art.heal[0]));
            Assert.That(Find("TauntWave").rectTransform.sizeDelta.x, Is.EqualTo(300));
        }

        [Test] public void RecoveryRestoresTargetStatesAndPlaysTheirOwnExitClips()
        {
            guardian.shields = new[] { new CombatShieldView { id = 3, group = "nature", remaining = 20, endTick = 120 } };
            enemy.statusDetails = new[]
            {
                new CombatStatusView { id = 1, definition = "synergy-control-Root", source = guardian.id, startTick = 0, endTick = 120 },
                new CombatStatusView { id = 2, definition = "synergy-control-Taunt", crowdControl = (int)CrowdControlKind.Taunt, source = guardian.id, startTick = 30, endTick = 110 }
            };
            Render(90);
            Assert.That(Find("WorldtreeSummon"), Is.Null);
            Assert.That(art.rootLoop, Does.Contain(Find("Root").sprite));
            Assert.That(art.shieldLoop, Does.Contain(Find("Shield").sprite));
            Assert.That(Find("TauntMarker").enabled, Is.True);

            guardian.shields = Array.Empty<CombatShieldView>();
            frame.events = new[] { new CombatEventView { kind = "ShieldAbsorbed", detail = "nature", target = guardian.id, tick = 110, value = 0 } };
            Render(110);
            Assert.That(Find("Shield").sprite, Is.SameAs(art.shieldBreak[0]));
            Assert.That(Find("TauntMarker").enabled, Is.False);
            frame.events = Array.Empty<CombatEventView>();
            Render(120);
            Assert.That(Find("Root").sprite, Is.SameAs(art.rootExit[0]));
            frame.complete = true;
            Render(121);
            Assert.That(Find("Root"), Is.Null);
        }

        [Test] public void NormalGuardianUsesSmallWaveAndNatureShieldTransitions()
        {
            guardian.natureTier = 4;
            guardian.shields = new[] { new CombatShieldView { id = 1, group = "nature", remaining = 200, endTick = 120 } };
            frame.events = new[]
            {
                Signal("SUMMON", guardian, 0, 4), Signal("GUARDIAN_CAST", guardian, 30, 4),
                new CombatEventView { kind = "ShieldApplied", detail = "nature", target = guardian.id, tick = 30, value = 200 }
            };
            Render(0);
            Assert.That(Find("Summon").sprite, Is.SameAs(art.summon[0]));
            Assert.That(Find("WorldtreeSummon"), Is.Null);
            Render(30);
            Assert.That(Find("TauntWave").rectTransform.sizeDelta.x, Is.EqualTo(180));
            Assert.That(Find("Shield").sprite, Is.SameAs(art.shieldEnter[0]));
            frame.events = Array.Empty<CombatEventView>();
            guardian.shields = Array.Empty<CombatShieldView>();
            Render(120);
            Assert.That(Find("Shield").sprite, Is.SameAs(art.shieldExpire[0]));
        }
    }
}
#endif
