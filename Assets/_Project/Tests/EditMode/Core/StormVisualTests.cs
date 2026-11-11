#if UNITY_EDITOR
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PokeChess.Client.UI;
using PokeChess.Core.Match;
namespace PokeChess.Core.Tests
{
    public sealed class StormVisualTests
    {
        private GameObject root;
        private SkillVisualBattleView view;
        private StormVisuals art;
        private CombatFrame frame;
        private static Vector2 Point(int c,int r)=>new Vector2(c*60,-r*40);
        private CombatUnitView Unit(string id,int col,int team=1)=>new CombatUnitView {
            id=id,definitionId="pikachu",column=col,row=2,team=team,hp=100,onBoard=true,statusDetails=Array.Empty<CombatStatusView>()
        };
        [SetUp] public void Setup() {
            root=new GameObject("StormVisualTest",typeof(RectTransform));
            root.GetComponent<RectTransform>().sizeDelta=new Vector2(600,350);
            view=SkillVisualBattleView.Get(root.transform,null);art=Resources.Load<StormVisuals>("StormVisuals");
            frame=new CombatFrame{battleId="storm",tickRate=30,events=Array.Empty<CombatEventView>(),units=new[]{Unit("a",0),Unit("b",1,2),Unit("c",2,2),Unit("d",3,2)}};
        }
        [TearDown] public void Cleanup()=>UnityEngine.Object.DestroyImmediate(root);
        private UnityEngine.UI.Image[] Images(string name)=>root.GetComponentsInChildren<UnityEngine.UI.Image>(true).Where(i=>i.name==name).ToArray();
        private UnityEngine.UI.Text Label()=>root.GetComponentsInChildren<UnityEngine.UI.Text>(true).Single();
        private void Render(double tick)=>view.RenderPlayback(frame,tick,Point);
        private CombatEventView Event(string clip,string from,string to,long tick=20)=>new CombatEventView{kind="Synergy",detail="storm:"+clip,source=from,target=to,tick=tick};
        [Test] public void AdditionalSheetsAreRegisteredWithAuthoredFrameCounts() {
            Assert.That(art.badgeTwo.Length,Is.EqualTo(8));Assert.That(art.badgeThree.Length,Is.EqualTo(8));
            Assert.That(art.ready.Length,Is.EqualTo(5));Assert.That(art.spent.Length,Is.EqualTo(7));
            Assert.That(art.slowEnter.Length,Is.EqualTo(3));Assert.That(art.slowHold.Length,Is.EqualTo(6));Assert.That(art.slowExit.Length,Is.EqualTo(2));
            Assert.That(new[]{art.badgeTwo,art.badgeThree,art.ready,art.spent,art.slowEnter,art.slowHold,art.slowExit}.SelectMany(s=>s).All(s=>s!=null),Is.True);
        }
        [Test] public void CircuitBadgeRestoresFromSnapshotAndFollowsUnitWithoutEvents() {
            frame.units[0].stormCircuitBonus=.25f;Render(0);
            var image=Images("Storm_CircuitBadge").Single();Assert.That(image.sprite,Is.SameAs(art.badgeThree[0]));
            var position=image.rectTransform.anchoredPosition;
            frame.units[0].column=2;Render(1);Assert.That(image.rectTransform.anchoredPosition.x,Is.EqualTo(position.x+120));
            frame.units[0].hp=0;Render(2);Assert.That(image.enabled,Is.False);
        }
        [Test] public void SlowRestoresEnterHoldAndExitFromStatusLifetime() {
            frame.units[1].statusDetails=new[]{new CombatStatusView{id=1,definition="synergy-storm-overcharge",startTick=0,endTick=30}};
            Render(0);var image=Images("Storm_Slow").Single();Assert.That(image.sprite,Is.SameAs(art.slowEnter[0]));
            Render(10);Assert.That(art.slowHold,Does.Contain(image.sprite));
            frame.units[1].statusDetails=Array.Empty<CombatStatusView>();Render(30);Assert.That(image.sprite,Is.SameAs(art.slowExit[0]));
            Render(36);Assert.That(image.enabled,Is.False);
        }
        [Test] public void SlowDispelStartsExitImmediately() {
            frame.units[1].statusDetails=new[]{new CombatStatusView{id=1,definition="synergy-storm-overcharge",startTick=0,endTick=30}};
            Render(0);frame.units[1].statusDetails=Array.Empty<CombatStatusView>();Render(10);
            Assert.That(Images("Storm_Slow").Single().sprite,Is.SameAs(art.slowExit[0]));
        }
        [Test] public void TeamChargeRestoresWithoutEventsAndSurvivesCasterDeath() {
            frame.units[0].stormTier=6;frame.units[0].stormEnergy=3;Render(10);
            var gauge=Images("Storm_Gauge").Single();Assert.That(gauge.sprite,Is.SameAs(art.charge[3]));
            Assert.That(gauge.rectTransform.anchorMin,Is.EqualTo(Vector2.one));
            var position=gauge.rectTransform.anchoredPosition;frame.units[0].hp=0;Render(11);
            Assert.That(gauge.enabled,Is.True);Assert.That(gauge.rectTransform.anchoredPosition,Is.EqualTo(position));Assert.That(Label().text,Does.Contain("3/4"));
        }
        [Test] public void TierFourUsesUnboundedNumberWithoutFourPipGauge() {
            frame.units[0].stormTier=4;frame.units[0].stormEnergy=20;Render(0);
            Assert.That(Images("Storm_Gauge").Single().enabled,Is.False);Assert.That(Label().text,Does.Contain("ENERGY 20"));Assert.That(Label().text,Does.Not.Contain("/4"));
        }
        [Test] public void OverchargeShowsReadyThenDischargeSpentAndEmptyGauge() {
            frame.units[0].stormTier=6;frame.events=new[]{Event("DISCHARGE","a","b")};
            Render(20);Assert.That(Images("Storm_Burst").Single().sprite,Is.SameAs(art.ready[0]));
            Assert.That(Images("Storm_Gauge").Single().sprite,Is.SameAs(art.charge[4]));
            Assert.That(Images("Storm_DISCHARGE").Single().enabled,Is.False);
            Render(33);Assert.That(Images("Storm_DISCHARGE").Single().enabled,Is.True);
            Assert.That(Images("Storm_Burst").Single().sprite,Is.SameAs(art.spent[0]));
            Assert.That(Images("Storm_Gauge").Single().sprite,Is.SameAs(art.charge[0]));
            Render(70);Assert.That(Images("Storm_Burst").Single().enabled,Is.False);
        }
        [Test] public void ChainsAreStaggeredAttenuatedAndDuplicateEventsAreIdempotent() {
            frame.events=new[]{Event("ARC","a","b"),Event("CHAIN","b","c"),Event("CHAIN","c","d")};
            Render(20);Assert.That(Images("Storm_CHAIN").Count(i=>i.enabled),Is.Zero);
            Render(23);Assert.That(Images("Storm_CHAIN").Count(i=>i.enabled),Is.EqualTo(1));
            Render(26);var chains=Images("Storm_CHAIN");Assert.That(chains.Count(i=>i.enabled),Is.EqualTo(2));
            Assert.That(chains[1].color.a,Is.LessThan(chains[0].color.a));
            Render(26);Assert.That(Images("Storm_CHAIN").Length,Is.EqualTo(2));
        }
    }
}
#endif
