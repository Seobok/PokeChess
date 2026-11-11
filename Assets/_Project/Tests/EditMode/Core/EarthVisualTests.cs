#if UNITY_EDITOR
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
namespace PokeChess.Core.Tests
{
    public sealed class EarthVisualTests
    {
        private GameObject root;private SkillVisualBattleView view;private EarthVisuals art;private CombatFrame frame;
        private static Vector2 Point(int c,int r)=>new Vector2(c*60,-r*45);
        [SetUp] public void Setup() {
            root=new GameObject("EarthVisualTest",typeof(RectTransform));view=SkillVisualBattleView.Get(root.transform,null);
            art=Resources.Load<EarthVisuals>("EarthVisuals");
            frame=new CombatFrame{battleId="earth",tickRate=30,events=Array.Empty<CombatEventView>(),units=new[]{
                new CombatUnitView{id="a",team=1,hp=100,onBoard=true,column=2,row=3,statusDetails=Array.Empty<CombatStatusView>(),shields=Array.Empty<CombatShieldView>()},
                new CombatUnitView{id="b",team=2,hp=100,onBoard=true,column=4,row=3,statusDetails=Array.Empty<CombatStatusView>(),shields=Array.Empty<CombatShieldView>()}}};
        }
        [TearDown] public void Cleanup()=>UnityEngine.Object.DestroyImmediate(root);
        private UnityEngine.UI.Image[] Images(string name)=>root.GetComponentsInChildren<UnityEngine.UI.Image>(true).Where(i=>i.name=="Earth_"+name).ToArray();
        private void Render(double tick)=>view.RenderPlayback(frame,tick,Point);
        private void Rock()=>frame.synergyRocksOne=new[]{new CombatCellView{column=3,row=3}};
        private void Shield()=>frame.units[0].shields=new[]{new CombatShieldView{id=1,group="earth",remaining=40,endTick=120}};
        [Test] public void CurrentSheetsAreRegisteredWithAuthoredSizes() {
            var clips=new[]{art.preview,art.idle,art.spawn,art.end,art.range,art.guard,art.shieldEnter,art.shieldHold,art.shieldBreak,art.shieldExpire,art.pulse};
            Assert.That(clips.Select(c=>c.Length),Is.EqualTo(new[]{1,1,8,5,8,6,8,12,5,5,9}));
            Assert.That(clips.SelectMany(c=>c).All(s=>s!=null),Is.True);Assert.That(art.pulse[0].rect.size,Is.EqualTo(new Vector2(192,192)));
        }
        [Test] public void RecoveryBaselineRestoresOneRockAndDeduplicatedSixCellRange() {
            Rock();frame.synergyRocksOne=frame.synergyRocksOne.Concat(frame.synergyRocksOne).ToArray();Render(60);
            Assert.That(Images("Rock").Length,Is.EqualTo(1));Assert.That(Images("Rock").Single().sprite,Is.SameAs(art.idle[0]));
            Assert.That(Images("Range").Count(i=>i.enabled),Is.EqualTo(6));Assert.That(Images("Rock").Single().raycastTarget,Is.False);
        }
        [Test] public void RockSpawnsThenIdlesAndEndsWhenBattleCompletes() {
            Rock();Render(0);Assert.That(Images("Rock").Single().sprite,Is.SameAs(art.spawn[0]));
            Render(30);Assert.That(Images("Rock").Single().sprite,Is.SameAs(art.idle[0]));
            frame.complete=true;Render(31);Assert.That(Images("Rock").Single().sprite,Is.SameAs(art.end[0]));
            Assert.That(Images("Range").All(i=>!i.enabled),Is.True);Render(50);Assert.That(Images("Rock").Single().enabled,Is.False);
        }
        [Test] public void BothTeamsCanHaveIndependentRocksOnTheSameCell() {
            Rock();frame.synergyRocksTwo=frame.synergyRocksOne;Render(60);Assert.That(Images("Rock").Length,Is.EqualTo(2));
            Assert.That(Images("Range").Length,Is.EqualTo(12));
        }
        [Test] public void PulseUsesGroundPositionAndDoesNotDuplicateOnEventReplay() {
            Rock();frame.events=new[]{new CombatEventView{kind="SynergyTile",detail="earth:protective-pulse",source="a",target="a",value=1,tick=120,synergyCell=new CombatCellView{column=3,row=3}}};
            Render(120);Render(120);Assert.That(Images("Pulse").Length,Is.EqualTo(1));
            var pulse=Images("Pulse").Single();Assert.That(pulse.sprite,Is.SameAs(art.pulse[0]));Assert.That(pulse.rectTransform.anchoredPosition,Is.EqualTo(Point(3,3)));
            Assert.That(pulse.rectTransform.sizeDelta,Is.EqualTo(Vector2.one*180));Render(150);Assert.That(pulse.enabled,Is.False);
        }
        [Test] public void ShieldEnterHoldAndBreakUseActualShieldState() {
            Shield();frame.events=new[]{new CombatEventView{kind="ShieldApplied",detail="earth",target="a",tick=0,value=40}};
            Render(0);var image=Images("Shield").Single();Assert.That(image.sprite,Is.SameAs(art.shieldEnter[0]));
            frame.events=Array.Empty<CombatEventView>();Render(30);Assert.That(art.shieldHold,Does.Contain(image.sprite));
            frame.units[0].shields=Array.Empty<CombatShieldView>();frame.events=new[]{new CombatEventView{kind="ShieldAbsorbed",detail="earth",target="a",tick=31,value=0}};
            Render(31);Assert.That(image.sprite,Is.SameAs(art.shieldBreak[0]));Render(50);Assert.That(image.enabled,Is.False);
        }
        [Test] public void ShieldRestoresFromSnapshotAndExpiresWithoutReplayEvents() {
            Shield();Render(60);var image=Images("Shield").Single();Assert.That(art.shieldHold,Does.Contain(image.sprite));
            frame.units[0].shields=Array.Empty<CombatShieldView>();Render(120);Assert.That(image.sprite,Is.SameAs(art.shieldExpire[0]));
            Render(136);Assert.That(image.enabled,Is.False);
        }
        [Test] public void PulseAndFirstShieldRestoreAndExpireIndependently()
        {
            Shield();
            frame.units[0].shields = frame.units[0].shields.Concat(new[]{
                new CombatShieldView{id=2,group="earth-pulse",remaining=240,endTick=180}
            }).ToArray();
            Render(60);
            var first = Images("Shield").Single();
            var pulse = Images("PulseShield").Single();
            Assert.That(art.shieldHold, Does.Contain(first.sprite));
            Assert.That(art.shieldHold, Does.Contain(pulse.sprite));
            Assert.That(pulse.color, Is.Not.EqualTo(first.color));
            frame.units[0].shields = frame.units[0].shields.Skip(1).ToArray();
            Render(120);
            Assert.That(first.sprite, Is.SameAs(art.shieldExpire[0]));
            Assert.That(art.shieldHold, Does.Contain(pulse.sprite));
        }
        [Test] public void OtherSlowsAndOtherShieldsDoNotCreateEarthEffects() {
            frame.units[0].statusDetails=new[]{new CombatStatusView{definition="synergy-control-Slow",startTick=0,endTick=60}};
            frame.units[0].shields=new[]{new CombatShieldView{group="guardian",remaining=40,endTick=120}};
            Render(10);Assert.That(Images("Slow"),Is.Empty);Assert.That(Images("Shield"),Is.Empty);
        }
        [Test] public void RangedGuardHitFollowsTargetAndDoesNotBlockInput() {
            frame.events=new[]{new CombatEventView{kind="Synergy",detail="earth:RANGED_GUARD",source="b",target="a",tick=10}};
            Render(10);var image=Images("RangedGuard").Single();Assert.That(image.sprite,Is.SameAs(art.guard[0]));
            Assert.That(image.rectTransform.anchoredPosition.x,Is.EqualTo(Point(2,3).x));Assert.That(image.raycastTarget,Is.False);
            Render(30);Assert.That(image.enabled,Is.False);
        }
        [Test] public void LegacySlowAndShockwaveDoNotPlayAndDeathClearsShield()
        {
            Rock(); Shield();
            frame.units[0].statusDetails = new[]{new CombatStatusView{definition="synergy-earth-slow",startTick=0,endTick=60}};
            frame.events = new[]{new CombatEventView{kind="SynergyTile",detail="earth:pulse",source="a",target="a",value=1,tick=0,synergyCell=new CombatCellView{column=3,row=3}}};
            Render(0);
            Assert.That(Images("Slow"), Is.Empty);
            Assert.That(Images("Pulse").Single().enabled, Is.False);
            frame.units[0].hp = 0;
            Render(1);
            Assert.That(Images("Shield").Single().enabled, Is.False);
        }
    }
}
#endif
