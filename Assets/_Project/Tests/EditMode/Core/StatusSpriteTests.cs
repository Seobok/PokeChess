#if UNITY_EDITOR
using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PokeChess.Client.Data;
using PokeChess.Client.UI;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using PokeChess.Content;
namespace PokeChess.Core.Tests
{
    public sealed class StatusSpriteTests
    {
        private GameObject root;private PokemonSpriteView view;
        [SetUp] public void Setup()
        {
            root=new GameObject("StatusSpriteTest",typeof(RectTransform));
            var node=new GameObject("Pokemon",typeof(RectTransform));node.transform.SetParent(root.transform,false);
            view=node.AddComponent<PokemonSpriteView>();view.Initialize("caterpie");
        }
        [TearDown] public void Cleanup(){UnityEngine.Object.DestroyImmediate(root);}
        private CombatUnitView Unit(params StatusBehavior[] behaviors)=>new CombatUnitView {
            id="test",definitionId="caterpie",hp=530,maxHP=530,onBoard=true,
            statusDetails=behaviors.Select((b,i)=>new CombatStatusView{id=i+1,behavior=(int)b,endTick=300}).ToArray()
        };
        private UnityEngine.UI.Image Effect(StatusBehavior behavior)=>root.transform.Find("Status_"+behavior)?.GetComponent<UnityEngine.UI.Image>();
        [Test] public void AllAuthoredEffectsHavePersistentFramesAndCorrectPositions()
        {
            var library=Resources.Load<StatusVisualLibrary>("StatusVisuals");Assert.That(library,Is.Not.Null);
            Assert.That(library.Effects.Length,Is.EqualTo(9));
            foreach(var effect in library.Effects){Assert.That(effect.Frames.Length,Is.GreaterThan(0));Assert.That(effect.Frames.All(s=>s!=null),Is.True,effect.Behavior.ToString());}
            foreach(var b in new[]{StatusBehavior.Burn,StatusBehavior.Poison,StatusBehavior.Paralysis})Assert.That(library.Effects.Single(e=>e.Behavior==b).Position,Is.EqualTo(StatusVisualPosition.Feet));
            Assert.That(library.Effects.Single(e=>e.Behavior==StatusBehavior.Freeze).Frames.Single().rect.size,Is.EqualTo(new Vector2(64,64)));
        }
        [Test] public void AnyStunUsesCommonHeadEffectUntilLastStunExpires()
        {
            var unit=Unit();unit.statusDetails=new[]{
                new CombatStatusView{id=1,definition="arbitrary-stun",crowdControl=(int)CrowdControlKind.Stun,startTick=10,endTick=20},
                new CombatStatusView{id=2,definition="another-stun",crowdControl=(int)CrowdControlKind.Stun,startTick=15,endTick=30}};
            view.ShowPlayback(unit,9,30,Vector2.zero);Assert.That(root.transform.Find("Status_Stun"),Is.Null);
            view.ShowPlayback(unit,10,30,Vector2.zero);var image=root.transform.Find("Status_Stun").GetComponent<UnityEngine.UI.Image>();
            Assert.That(image.gameObject.activeSelf,Is.True);Assert.That(image.sprite,Is.Not.Null);
            Assert.That(image.transform.GetSiblingIndex(),Is.GreaterThan(view.transform.GetSiblingIndex()));
            view.ShowPlayback(unit,20,30,Vector2.zero);Assert.That(image.gameObject.activeSelf,Is.True);
            Assert.That(root.GetComponentsInChildren<UnityEngine.UI.Image>().Count(i=>i.name=="Status_Stun"),Is.EqualTo(1));
            view.ShowPlayback(unit,30,30,Vector2.zero);Assert.That(image.gameObject.activeSelf,Is.False);
        }
        [Test] public void SimultaneousEffectsUseFeetOverlayAndSeparateHeadSlots()
        {
            var unit=Unit(StatusBehavior.Burn,StatusBehavior.Poison,StatusBehavior.Paralysis,StatusBehavior.Freeze,StatusBehavior.Blind,StatusBehavior.Bleed,StatusBehavior.Confusion);
            view.ShowPlayback(unit,0,30,Vector2.zero,1.5f);
            foreach(var b in new[]{StatusBehavior.Burn,StatusBehavior.Poison,StatusBehavior.Paralysis}) {
                Assert.That(Effect(b).rectTransform.anchoredPosition.y,Is.LessThanOrEqualTo(0));
                Assert.That(Effect(b).transform.GetSiblingIndex(),Is.GreaterThan(view.transform.GetSiblingIndex()));
            }
            Assert.That(Effect(StatusBehavior.Freeze).color.a,Is.EqualTo(.55f).Within(.001));
            Assert.That(Effect(StatusBehavior.Freeze).transform.GetSiblingIndex(),Is.GreaterThan(view.transform.GetSiblingIndex()));
            var heads=new[]{StatusBehavior.Blind,StatusBehavior.Bleed,StatusBehavior.Confusion}.Select(Effect).ToArray();
            Assert.That(heads.Select(h=>h.rectTransform.anchoredPosition.x).Distinct().Count(),Is.EqualTo(3));
            Assert.That(heads.All(h=>h.rectTransform.anchoredPosition.y>0&&!h.raycastTarget),Is.True);
        }
        [Test] public void LayerOrderRemainsStableAcrossFramesAndHudUpdates()
        {
            var hud=new GameObject("HealthBar",typeof(RectTransform));hud.transform.SetParent(root.transform,false);hud.transform.SetAsFirstSibling();
            var unit=Unit(StatusBehavior.Burn,StatusBehavior.Poison,StatusBehavior.Paralysis,StatusBehavior.Freeze,StatusBehavior.Blind,StatusBehavior.Bleed,StatusBehavior.Confusion);
            string[] initial=null;
            for(int tick=0;tick<90;tick++) {
                view.ShowPlayback(unit,tick,30,Vector2.zero,1.5f);
                var order=Enumerable.Range(0,root.transform.childCount).Select(i=>root.transform.GetChild(i).name).ToArray();
                if(initial==null)initial=order;else Assert.That(order,Is.EqualTo(initial),"frame "+tick);
                foreach(var b in new[]{StatusBehavior.Burn,StatusBehavior.Poison,StatusBehavior.Paralysis})
                    Assert.That(Effect(b).transform.GetSiblingIndex(),Is.GreaterThan(view.transform.GetSiblingIndex()),"frame "+tick);
                var overlay=Effect(StatusBehavior.Freeze).transform.GetSiblingIndex();
                Assert.That(overlay,Is.GreaterThan(view.transform.GetSiblingIndex()));
                foreach(var b in new[]{StatusBehavior.Blind,StatusBehavior.Bleed,StatusBehavior.Confusion}) {
                    Assert.That(Effect(b).transform.GetSiblingIndex(),Is.GreaterThan(overlay));
                    Assert.That(Effect(b).transform.GetSiblingIndex(),Is.LessThan(hud.transform.GetSiblingIndex()));
                }
                hud.transform.SetAsLastSibling();
            }
        }
        [Test] public void RemovingAndReapplyingStatusesRestoresTheSameLayerOrder()
        {
            var all=Unit(StatusBehavior.Burn,StatusBehavior.Poison,StatusBehavior.Paralysis,StatusBehavior.Freeze,StatusBehavior.Blind);
            view.ShowPlayback(all,0,30,Vector2.zero,1.5f);
            var initial=Enumerable.Range(0,root.transform.childCount).Select(i=>root.transform.GetChild(i).name).ToArray();
            view.ShowPlayback(Unit(StatusBehavior.Freeze),1,30,Vector2.zero,1.5f);
            view.ShowIdle(0);
            view.ShowPlayback(all,2,30,Vector2.zero,1.5f);
            Assert.That(Enumerable.Range(0,root.transform.childCount).Select(i=>root.transform.GetChild(i).name).ToArray(),Is.EqualTo(initial));
            foreach(var b in new[]{StatusBehavior.Burn,StatusBehavior.Poison,StatusBehavior.Paralysis})
                Assert.That(Effect(b).transform.GetSiblingIndex(),Is.GreaterThan(view.transform.GetSiblingIndex()));
        }
        [Test] public void FramesAdvanceAndExpirationDeathAndIdleClearEffects()
        {
            var unit=Unit(StatusBehavior.Poison);view.ShowPlayback(unit,0,30,Vector2.zero,1.5f);
            var first=Effect(StatusBehavior.Poison).sprite;view.ShowPlayback(unit,5,30,Vector2.zero,1.5f);
            Assert.That(Effect(StatusBehavior.Poison).sprite,Is.Not.SameAs(first));
            view.ShowPlayback(unit,300,30,Vector2.zero,1.5f);Assert.That(Effect(StatusBehavior.Poison).gameObject.activeSelf,Is.False);
            view.ShowPlayback(unit,0,30,Vector2.zero,1.5f);unit.hp=0;view.ShowPlayback(unit,1,30,Vector2.zero,1.5f);
            Assert.That(Effect(StatusBehavior.Poison).gameObject.activeSelf,Is.False);
            unit.hp=530;view.ShowPlayback(unit,0,30,Vector2.zero,1.5f);view.ShowIdle(0);
            Assert.That(Effect(StatusBehavior.Poison).gameObject.activeSelf,Is.False);
        }
        [Test] public void RestSleepUsesSleepVisualAndReinitializingClearsOldStatus()
        {
            view.ShowPlayback(Unit(StatusBehavior.RestSleep),0,30,Vector2.zero,1.5f);
            Assert.That(Effect(StatusBehavior.Sleep).gameObject.activeSelf,Is.True);
            view.Initialize("weedle");Assert.That(Effect(StatusBehavior.Sleep).gameObject.activeSelf,Is.False);
        }
        [Test] public void LocalStatusApplicationAndRemovalUpdateSprites()
        {
            var catalog=GameContent.CreateCatalog();
            var battle=BattleStateFactory.Create("status-visual",1,1,30,catalog,new[]{
                new BattleUnitSetup(catalog.CreateUnit("a","caterpie","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(1,3)),
                new BattleUnitSetup(catalog.CreateUnit("b","weedle","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(2,3))});
            var sim=new BattleSimulation(battle);var unit=battle.Units.Single(u=>u.UnitInstanceId=="a");
            var burn=battle.StatusEffects.Apply(new StatusEffectDefinition("visual-burn",4,behavior:StatusBehavior.Burn),"b","a");
            view.ShowCombat(battle,unit,sim.GetRuntime("a"),Vector2.zero);
            Assert.That(Effect(StatusBehavior.Burn).gameObject.activeSelf,Is.True);
            Assert.That(Effect(StatusBehavior.Burn).transform.GetSiblingIndex(),Is.GreaterThan(view.transform.GetSiblingIndex()));
            battle.StatusEffects.Remove(burn.InstanceId);view.ShowCombat(battle,unit,sim.GetRuntime("a"),Vector2.zero);
            Assert.That(Effect(StatusBehavior.Burn).gameObject.activeSelf,Is.False);
        }
        [Test] public void HealthBarMovesAboveHeadEffects()
        {
            view.ShowPlayback(Unit(StatusBehavior.Confusion),0,30,Vector2.zero,1.5f);
            var hud=root.AddComponent<PlacementSandboxView>();
            typeof(PlacementSandboxView).GetMethod("RenderUnitHealthBar",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                .Invoke(hud,new object[]{root.transform,530d,530d,true,0d});
            Assert.That(((RectTransform)root.transform.Find("HealthBar")).anchoredPosition.y,
                Is.GreaterThan(view.GetComponent<StatusSpriteView>().Top));
        }
    }
}
#endif
