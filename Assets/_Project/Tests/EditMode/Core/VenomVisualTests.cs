#if UNITY_EDITOR
using System;
using System.Linq;
using NUnit.Framework;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
using UnityEngine;

namespace PokeChess.Core.Tests
{
    public sealed class VenomVisualTests
    {
        private GameObject root;
        private SkillVisualBattleView view;
        private VenomVisuals art;
        private CombatUnitView victim;
        private CombatFrame frame;
        private static Vector2 Point(int column, int row) => new Vector2(column * 60, -row * 45);

        [SetUp] public void Setup()
        {
            root = new GameObject("VenomVisualTest", typeof(RectTransform));
            view = SkillVisualBattleView.Get(root.transform, null);
            art = Resources.Load<VenomVisuals>("VenomVisuals");
            victim = new CombatUnitView { id = "victim", team = 2, hp = 1000, onBoard = true, column = 3, row = 3 };
            frame = new CombatFrame { battleId = "venom", tickRate = 30, units = new[] { victim } };
        }

        [TearDown] public void Cleanup() => UnityEngine.Object.DestroyImmediate(root);
        private UnityEngine.UI.Image Find(string name)
            => root.GetComponentsInChildren<UnityEngine.UI.Image>(true).SingleOrDefault(image => image.name == "Venom_" + name);
        private void Render(double tick) => view.RenderPlayback(frame, tick, Point);
        private CombatEventView Signal(string detail, long tick)
            => new CombatEventView { kind = "SynergyTile", detail = "venom:" + detail, target = victim.id, tick = tick,
                synergyCell = new CombatCellView { column = victim.column, row = victim.row } };

        [Test] public void RecoveryUsesActiveLoopThenExpiresWithoutFirstHit()
        {
            victim.contaminationEndTick = 150;
            victim.venomErosion = true;
            Render(120);
            Assert.That(art.markLoop, Does.Contain(Find("Mark").sprite));
            Assert.That(Find("ErosionIcon").enabled, Is.True);
            Assert.That(Find("FIRST_HIT"), Is.Null);
            victim.contaminationEndTick = 0;
            victim.venomErosion = false;
            Render(150);
            Assert.That(Find("Mark").sprite, Is.SameAs(art.markExit[0]));
            Assert.That(Find("ErosionIcon").enabled, Is.False);
        }

        [Test] public void TransferArrivesWithoutPlayingTheDamageClip()
        {
            var transfer = Signal("TRANSFER", 10);
            transfer.synergyCell = new CombatCellView { column = 1, row = 3 };
            transfer.synergyDestination = new CombatCellView { column = 3, row = 3 };
            frame.events = new[] { transfer };
            victim.contaminationEndTick = 160;
            Render(10);
            Assert.That(Find("TRANSFER").enabled, Is.True);
            Assert.That(Find("Mark").enabled, Is.False);
            Render(30);
            Assert.That(Find("TRANSFER").enabled, Is.False);
            Assert.That(Find("Mark").sprite, Is.SameAs(art.arrive[0]));
            Assert.That(Find("FIRST_HIT"), Is.Null);
        }

        [Test] public void MutationUsesIndependentCenterAndImpactClips()
        {
            frame.events = new[] { Signal("MUTATION", 50), Signal("MUTATION_HIT", 50) };
            Render(50);
            Render(50);
            Assert.That(Find("MUTATION").sprite, Is.SameAs(art.mutation[0]));
            Assert.That(Find("MUTATION_HIT").sprite, Is.SameAs(art.mutationHit[0]));
            Assert.That(Find("REACTION"), Is.Null);
        }
    }
}
#endif
