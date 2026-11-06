using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using PokeChess.Client.Data;
using PokeChess.Client.UI;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
namespace PokeChess.Core.Tests
{
    public sealed class SkillVisualTests
    {
        private GameObject root;
        private SkillVisualBattleView view;
        private SkillVisualCatalog catalog,custom;
        private CombatFrame frame;
        private static Vector2 Point(int c,int r)=>new Vector2(c*60+(r%2)*30,-r*35);
        [SetUp] public void Setup()
        {
            catalog=AssetDatabase.LoadAssetAtPath<SkillVisualCatalog>("Assets/_Project/Data/SkillVisualCatalog.asset");
            root=new GameObject("SkillVisualTest",typeof(RectTransform));view=SkillVisualBattleView.Get(root.transform,catalog);
            frame=new CombatFrame{battleId="test",tickRate=30,events=Array.Empty<CombatEventView>(),units=new[]{
                new CombatUnitView{id="caster",definitionId="clefairy",hp=100,onBoard=true,column=2,row=2,
                    action=CombatActionState.Casting,skillId="skill-weedle-r1",castId=1,target="target",startTick=0,effectTick=12,endTick=24},
                new CombatUnitView{id="target",definitionId="metapod",hp=100,onBoard=true,column=4,row=2}
            }};
        }
        [TearDown] public void Cleanup()
        {UnityEngine.Object.DestroyImmediate(root);if(custom!=null)UnityEngine.Object.DestroyImmediate(custom);}
        private UnityEngine.UI.Image[] Images()=>root.GetComponentsInChildren<UnityEngine.UI.Image>(true);
        private void Render(double tick)=>view.RenderPlayback(frame,tick,Point);
        private CombatEventView Skill(string id,long cast=1)=>new CombatEventView{kind="SkillEffectApplied",detail=id,source="caster",target="target",castId=cast,tick=12};
        private CombatEventView Damage(long tick,int step=0)=>new CombatEventView{kind="EffectDamage",detail="Applied",source="caster",target="target",castId=1,stepIndex=step,tick=tick};
        [TestCase("mankey")] [TestCase("machop")]
        public void KarateChopHitsTargetAndKeepsBuffOnCasterUntilConsumed(string pokemon)
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.units[0].statusDetails=new[]{new CombatStatusView{id=99,definition="status-"+pokemon+"-r1-s0",source="caster",startTick=12,endTick=1000}};
            frame.events=new[]{Skill("skill-"+pokemon+"-r1"),Damage(12)};Render(12);Render(12);
            var hit=Images().Single(i=>i.name=="SkillVisual_KarateChopHit");
            Assert.That(hit.rectTransform.anchoredPosition,Is.EqualTo(Point(frame.units[1].column,frame.units[1].row)+Vector2.up*24));
            var buff=Images().Single(i=>i.name=="SkillVisual_KarateChopCriticalReady");
            Assert.That(buff.rectTransform.anchoredPosition,Is.EqualTo(Point(frame.units[0].column,frame.units[0].row)+Vector2.up*42));
            frame.events=Array.Empty<CombatEventView>();frame.units[0].column=4;Render(40);
            Assert.That(Images().Single(),Is.SameAs(buff));
            Assert.That(buff.rectTransform.anchoredPosition,Is.EqualTo(Point(4,frame.units[0].row)+Vector2.up*42));
            frame.units[0].statusDetails=Array.Empty<CombatStatusView>();Render(41);Assert.That(Images(),Is.Empty);
        }
        [Test] public void KnockbackPresentationHoldsThenMovesQuicklyAndSettlesAtDestination()
        {
            var start=Point(2,2);var end=Point(3,2);
            frame.movements=new[]{new CombatMovementView{unit="target",kind=(int)SkillMovementKind.Knockback,startTick=10,departureTick=14,endTick=20,
                path=new[]{new CombatCellView{column=2,row=2},new CombatCellView{column=3,row=2}}}};
            Assert.That(ThrowPresentation.Playback(frame,"target",12,start,Point),Is.EqualTo(start));
            Assert.That(ThrowPresentation.Playback(frame,"target",14,start,Point),Is.EqualTo(start));
            var middle=ThrowPresentation.Playback(frame,"target",17,start,Point);
            Assert.That(middle.x,Is.GreaterThan((start.x+end.x)/2));
            Assert.That(ThrowPresentation.Playback(frame,"target",20,start,Point),Is.EqualTo(end));
        }
        [Test] public void ThrowArcRisesAndReturnsToGroundAndPlaybackUsesSamePath()
        {
            var start=Point(2,2);var end=Point(4,2);
            Assert.That(ThrowPresentation.Position(start,end,10,10,20,90),Is.EqualTo(start));
            Assert.That(ThrowPresentation.Position(start,end,15,10,20,90),Is.EqualTo((start+end)/2+Vector2.up*90));
            Assert.That(ThrowPresentation.Position(start,end,20,10,20,90),Is.EqualTo(end));
            frame.movements=new[]{new CombatMovementView{unit="target",kind=(int)SkillMovementKind.Throw,startTick=10,endTick=20,
                path=new[]{new CombatCellView{column=2,row=2},new CombatCellView{column=4,row=2}}}};
            Assert.That(ThrowPresentation.Playback(frame,"target",15,start,Point),Is.EqualTo((start+end)/2+Vector2.up*90));
            frame.movements=Array.Empty<CombatMovementView>();
            Assert.That(ThrowPresentation.Playback(frame,"target",15,end,Point),Is.EqualTo(end));
        }
        [TestCase(3,3,7)] [TestCase(0,0,3)]
        public void SeismicTossCoversEmptyAreaCellsAndEnlargesOnlyCenter(int column,int row,int count)
        {
            frame.units[0].action=CombatActionState.Idle;
            var center=new BoardPosition(column,row);
            var cells=SkillGeometry.Cells(SkillAreaShape.Radius,center,center,1)
                .Select(p=>new CombatCellView{column=p.Column,row=p.Row}).ToArray();
            var area=new CombatEventView{kind="SkillArea",castId=1,tick=12,area=new CombatAreaView {
                center=new CombatCellView{column=column,row=row},cells=cells}};
            frame.events=new[]{Skill("skill-primeape-r1"),area,area,Damage(12)};
            Render(12);Render(12);
            Assert.That(Images().Length,Is.EqualTo(count));
            Assert.That(Images().All(i=>i.name=="SkillVisual_SeismicTossLanding"),Is.True);
            CollectionAssert.AreEquivalent(cells.Select(c=>Point(c.column,c.row)+Vector2.up*5),
                Images().Select(i=>i.rectTransform.anchoredPosition));
            foreach(var image in Images())Assert.That(image.rectTransform.sizeDelta.x,Is.EqualTo(
                image.rectTransform.anchoredPosition==Point(column,row)+Vector2.up*5?90:60));
            Render(40);Assert.That(Images(),Is.Empty);
        }
        [TestCase("tentacruel")] [TestCase("tangela")]
        public void ConstrictBindingFollowsTargetAndStopsWithRoot(string species)
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.units[1].statusDetails=new[]{new CombatStatusView{id=70,definition="status-"+species+"-r1-s0",source="caster",startTick=12,endTick=72}};
            Render(12);Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_ConstrictBinding"));
            frame.units[1].column=5;Render(40);
            Assert.That(Images().Single().rectTransform.anchoredPosition,Is.EqualTo(Point(5,2)+Vector2.up*24));
            frame.units[1].statusDetails=Array.Empty<CombatStatusView>();Render(41);Assert.That(Images(),Is.Empty);
        }
        [Test] public void ConstrictPulseReplacesBindingThenReturnsToItsLastFrame()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.units[1].statusDetails=new[]{new CombatStatusView{id=70,definition="status-tangela-r1-s0",source="caster",startTick=12,endTick=102}};
            frame.units=frame.units.Concat(new[]{new CombatUnitView{id="other",hp=100,onBoard=true,column=5,row=2,
                statusDetails=new[]{new CombatStatusView{id=71,definition="status-tangela-r1-s0",source="caster",startTick=12,endTick=102}}}}).ToArray();
            Render(40);
            var binding=Images().Single(i=>i.rectTransform.anchoredPosition==Point(4,2)+Vector2.up*24);
            var last=catalog.Entries.Single(e=>e.Name=="ConstrictBinding").Frames.Last();
            Assert.That(binding.sprite,Is.SameAs(last));Assert.That(binding.enabled,Is.True);
            frame.events=new[]{new CombatEventView{kind="StatusPulse",source="caster",target="target",detail="status-tangela-r1-s0",effectId=70,tick=42}};
            Render(42);Assert.That(binding.enabled,Is.False);
            Assert.That(Images().Count(i=>i.enabled),Is.EqualTo(2));
            Assert.That(Images().Single(i=>i.name=="SkillVisual_ConstrictPeriodicDamage").enabled,Is.True);
            frame.events=Array.Empty<CombatEventView>();Render(56);Assert.That(binding.enabled,Is.False);
            Render(57);Assert.That(binding.enabled,Is.True);Assert.That(binding.sprite,Is.SameAs(last));
            Assert.That(Images().All(i=>i.name=="SkillVisual_ConstrictBinding"),Is.True);
            Render(102);Assert.That(Images(),Is.Empty);
        }
        [Test] public void ConstrictDamageOnlyPlaysAtEachTangelaPulseAndDeduplicatesReplayedEvents()
        {
            frame.units[0].action=CombatActionState.Idle;
            var pulse=new CombatEventView{kind="StatusPulse",source="caster",target="target",detail="status-tangela-r1-s0",effectId=70,tick=42};
            frame.events=new[]{pulse,pulse};Render(42);Render(42);
            Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_ConstrictPeriodicDamage"));
            frame.events=Array.Empty<CombatEventView>();Render(58);Assert.That(Images(),Is.Empty);
            pulse.tick=72;frame.events=new[]{pulse};Render(72);Assert.That(Images().Length,Is.EqualTo(1));
            frame.events=Array.Empty<CombatEventView>();Render(88);Assert.That(Images(),Is.Empty);
            pulse.tick=90;pulse.detail="status-tentacruel-r1-s0";frame.events=new[]{pulse};Render(90);Assert.That(Images(),Is.Empty);
        }
        [TestCase("farfetchd")] [TestCase("scyther")]
        public void SwordsDanceBuffAndSixRingSheetsUseProcRatherThanEachHit(string species)
        {
            frame.units[0].action=CombatActionState.Idle;
            string status="status-"+species+"-r1-s0";
            frame.units[0].statusDetails=new[]{new CombatStatusView{id=90,definition=status,source="caster",startTick=12,endTick=192}};
            frame.events=new[]{new CombatEventView{kind="StatusPulse",source="caster",target="caster",detail=status,effectId=90,tick=42}};
            Render(42);Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_SwordsDanceBuff"));
            frame.events=new[]{new CombatEventView{kind="StatusProc",source="caster",target="caster",detail=status,effectId=90,tick=72}};
            Render(72);Render(72);Assert.That(Images().Length,Is.EqualTo(7));
            for(int n=0;n<6;n++) {
                var image=Images().Single(i=>i.name=="SkillVisual_SwordsDanceRing"+(n+1));
                float angle=(90-n*60)*Mathf.Deg2Rad;
                var expected=Point(2,2)+Vector2.up*14+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*51;
                Assert.That(Vector2.Distance(image.rectTransform.anchoredPosition,expected),Is.LessThan(.001));
            }
            frame.events=Array.Empty<CombatEventView>();Render(100);
            Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_SwordsDanceBuff"));
            frame.units[0].statusDetails=Array.Empty<CombatStatusView>();Render(101);Assert.That(Images(),Is.Empty);
        }
        [Test] public void BindHoldsLastFrameUntilActualRootEnds()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.units[1].statusDetails=new[]{new CombatStatusView{id=80,definition="status-onix-r1-s0",source="caster",startTick=12,endTick=72}};
            Render(40);var image=Images().Single();Assert.That(image.name,Is.EqualTo("SkillVisual_Bind"));
            Assert.That(image.sprite,Is.SameAs(catalog.Entries.Single(e=>e.Name=="Bind").Frames.Last()));
            frame.units[1].statusDetails=Array.Empty<CombatStatusView>();Render(41);Assert.That(Images(),Is.Empty);
        }
        [TestCase("voltorb",1,"SelfDestruct",7,75)] [TestCase("graveler",1,"SelfDestruct",7,75)]
        [TestCase("electrode",2,"Explosion",19,90)] [TestCase("golem",2,"Explosion",19,90)]
        public void ExplosionsCoverEntireRadiusAndUseOnlyCoreOnCenter(string species,int radius,string prefix,int count,int size)
        {
            frame.units[0].action=CombatActionState.Idle;frame.units[0].column=3;frame.units[0].row=3;
            var center=new BoardPosition(3,3);
            var cells=SkillGeometry.Cells(SkillAreaShape.Radius,center,center,radius).Select(p=>new CombatCellView{column=p.Column,row=p.Row}).ToArray();
            var area=new CombatEventView{kind="SkillArea",castId=1,tick=12,area=new CombatAreaView{center=new CombatCellView{column=3,row=3},cells=cells}};
            frame.events=new[]{Skill("skill-"+species+"-r1"),area,area};Render(12);Render(12);
            Assert.That(Images().Length,Is.EqualTo(count));
            var core=Images().Single(i=>i.name=="SkillVisual_"+prefix+"Core");
            Assert.That(core.rectTransform.anchoredPosition,Is.EqualTo(Point(3,3)+Vector2.up*5));
            Assert.That(core.rectTransform.sizeDelta.x,Is.EqualTo(size));
            Assert.That(Images().Count(i=>i.rectTransform.anchoredPosition==core.rectTransform.anchoredPosition),Is.EqualTo(1));
            CollectionAssert.AreEquivalent(cells.Select(c=>Point(c.column,c.row)+Vector2.up*5),Images().Select(i=>i.rectTransform.anchoredPosition));
            Render(50);Assert.That(Images(),Is.Empty);
        }
        [Test] public void CatalogReferencesAllFramesOutsideResources()
        {
            Assert.That(catalog,Is.Not.Null);Assert.That(catalog.Entries.Length,Is.GreaterThanOrEqualTo(7));
            Assert.That(catalog.Entries.All(e=>e.ScaleUnitSprite||e.Lightning||e.SpriteAfterimage||e.HideUnitSprite||(e.Frames.Length>0&&e.Frames.All(s=>s!=null))),Is.True);
            Assert.That(AssetDatabase.GetAssetPath(catalog),Does.Not.Contain("Resources"));
        }
        [TestCase(false)] [TestCase(true)]
        public void ThunderShockUsesRealLineRenderersTracksBothEndsAndExpires(bool left)
        {
            root.AddComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            frame.units[0].action=CombatActionState.Idle;if(left)frame.units[1].column=0;
            frame.events=new[]{Skill("skill-pikachu-r1")};Render(12);Render(12);
            var graphic=root.GetComponentsInChildren<LightningLineGraphic>().Single();
            Assert.That(graphic.GetComponent<CanvasRenderer>(),Is.Not.Null);
            Canvas.ForceUpdateCanvases();graphic.Rebuild(UnityEngine.UI.CanvasUpdate.PreRender);
            Assert.That(graphic.canvasRenderer.GetMesh().vertexCount,Is.GreaterThan(24));
            Assert.That(graphic.GetComponentsInChildren<LineRenderer>().Length,Is.EqualTo(2));
            var line=graphic.Bolt;Assert.That(line.positionCount,Is.EqualTo(13));
            Assert.That((Vector2)line.GetPosition(0),Is.EqualTo(Point(2,2)+Vector2.up*24));
            Assert.That((Vector2)line.GetPosition(12),Is.EqualTo(Point(frame.units[1].column,2)+Vector2.up*24));
            using(var vh=new UnityEngine.UI.VertexHelper()) {
                typeof(LightningLineGraphic).GetMethod("OnPopulateMesh",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.DeclaredOnly)
                    .Invoke(graphic,new object[]{vh});
                Assert.That(vh.currentVertCount,Is.GreaterThan(24));
            }
            frame.events=Array.Empty<CombatEventView>();frame.units[1].column=3;Render(15);
            Assert.That((Vector2)line.GetPosition(12),Is.EqualTo(Point(3,2)+Vector2.up*24));
            Render(23);Assert.That(root.GetComponentsInChildren<LightningLineGraphic>(),Is.Empty);
        }
        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void ThunderboltBranchesFromPrimaryAndOnlyUsesActualVictims(int targets)
        {
            root.AddComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
            frame.units[0].action=CombatActionState.Idle;
            frame.units=frame.units.Concat(new[]{
                new CombatUnitView{id="chain1",hp=100,onBoard=true,column=5,row=2},
                new CombatUnitView{id="chain2",hp=100,onBoard=true,column=4,row=3}}).ToArray();
            var victims=new[]{"target","chain1","chain2"}.Take(targets).ToArray();
            frame.events=new[]{Skill("skill-raichu-r1")}.Concat(victims.Select(id=>new CombatEventView {
                kind="EffectDamage",detail="Applied",source="caster",target=id,castId=1,tick=12})).ToArray();
            Render(12);Render(12);var graphics=root.GetComponentsInChildren<LightningLineGraphic>();
            Assert.That(graphics.Length,Is.EqualTo(targets));
            foreach(var graphic in graphics) {
                var end=(Vector2)graphic.Bolt.GetPosition(12);
                var primary=Point(4,2)+Vector2.up*24;
                Assert.That((Vector2)graphic.Bolt.GetPosition(0),Is.EqualTo(end==primary?Point(2,2)+Vector2.up*24:primary));
                graphic.Rebuild(UnityEngine.UI.CanvasUpdate.PreRender);
                var colors=graphic.canvasRenderer.GetMesh().colors32;
                Assert.That(colors.Length,Is.GreaterThan(0));
                Assert.That(colors[0].r,Is.EqualTo(255));Assert.That(colors[0].g,Is.InRange(110,120));
            }
            frame.events=Array.Empty<CombatEventView>();Render(25);
            Assert.That(root.GetComponentsInChildren<LightningLineGraphic>(),Is.Empty);
        }
        [Test] public void MinimizeShrinksInFourStepsAndRestoresAtExpiryOrReset()
        {
            var node=new GameObject("Actor",typeof(RectTransform));node.transform.SetParent(root.transform,false);
            var actor=node.AddComponent<PokemonSpriteView>();actor.Initialize("clefable","caster");actor.ShowIdle(0);
            var size=actor.CharacterImage.rectTransform.sizeDelta;var offset=actor.CharacterImage.rectTransform.anchoredPosition;
            frame.units[0].action=CombatActionState.Idle;
            frame.units[0].statusDetails=new[]{new CombatStatusView{id=100,definition="status-clefable-r1-s0",source="caster",startTick=12,endTick=132}};
            var scales=new[]{1f,.8f,.6f,.4f};
            for(int i=0;i<4;i++) {
                Render(12+i*3);view.UpdateSpriteVisibility();view.UpdateSpriteVisibility();
                Assert.That(Vector2.Distance(actor.CharacterImage.rectTransform.sizeDelta,size*scales[i]),Is.LessThan(.001));
                Assert.That(Vector2.Distance(actor.CharacterImage.rectTransform.anchoredPosition,offset*scales[i]),Is.LessThan(.001));
            }
            Render(100);view.UpdateSpriteVisibility();Assert.That(Vector2.Distance(actor.CharacterImage.rectTransform.sizeDelta,size*.4f),Is.LessThan(.001));
            Render(132);view.UpdateSpriteVisibility();Assert.That(Vector2.Distance(actor.CharacterImage.rectTransform.sizeDelta,size),Is.LessThan(.001));
            frame.units[0].statusDetails[0].startTick=132;frame.units[0].statusDetails[0].endTick=252;
            Render(150);view.UpdateSpriteVisibility();view.Configure(null);
            Assert.That(Vector2.Distance(actor.CharacterImage.rectTransform.sizeDelta,size),Is.LessThan(.001));
        }
        [TestCase("jigglypuff","Sing",1)] [TestCase("vileplume","PetalDance",3)]
        public void PreparedAreaSkillsCoverEmptyCellsAtEachActualStep(string species,string name,int steps)
        {
            frame.units[0].action=CombatActionState.Idle;var center=new BoardPosition(3,3);
            var cells=SkillGeometry.Cells(SkillAreaShape.Radius,center,center,2).Select(p=>new CombatCellView{column=p.Column,row=p.Row}).ToArray();
            for(int step=0;step<steps;step++) {
                long tick=12+step*30;
                frame.events=new[]{Skill("skill-"+species+"-r1"),new CombatEventView{kind="SkillArea",castId=1,tick=tick,stepIndex=step,
                    area=new CombatAreaView{cells=cells}}};
                Render(tick);Render(tick);Assert.That(Images().Length,Is.EqualTo(19));
                Assert.That(Images().All(i=>i.name=="SkillVisual_"+name),Is.True);
                frame.events=Array.Empty<CombatEventView>();
                if(name=="PetalDance") {
                    Render(tick+29);Assert.That(Images().Length,Is.EqualTo(19));Assert.That(Images().All(i=>i.enabled),Is.True);
                    Render(tick+30);
                } else Render(tick+25);
                Assert.That(Images(),Is.Empty);
            }
        }
        [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void DoubleSlapUsesOneSheetRowPerActualHit(int hits)
        {
            frame.units[0].action=CombatActionState.Idle;
            for(int step=0;step<hits;step++) {
                long tick=12+step*6;var damage=Damage(tick,step);
                frame.events=step==0?new[]{Skill("skill-wigglytuff-r1-hits"+hits),damage}:new[]{damage};
                Render(tick);Render(tick);
                var image=Images().Single();Assert.That(image.name,Is.EqualTo("SkillVisual_DoubleSlap"+(step+1)));
                Assert.That(image.sprite,Is.SameAs(catalog.Entries.Single(e=>e.Name=="DoubleSlap"+(step+1)).Frames[0]));
            }
            frame.events=Array.Empty<CombatEventView>();Render(12+hits*6);Assert.That(Images(),Is.Empty);
        }
        [TestCase("oddish")] [TestCase("kabuto")]
        public void AbsorbPlaysOnVictimAndHealingCaster(string species)
        {
            frame.units[0].action=CombatActionState.Idle;frame.events=new[]{Skill("skill-"+species+"-r1"),Damage(12)};Render(12);
            Assert.That(Images().Length,Is.EqualTo(2));
            Assert.That(Images().Single(i=>i.name=="SkillVisual_Absorb").rectTransform.anchoredPosition,Is.EqualTo(Point(4,2)+Vector2.up*24));
            Assert.That(Images().Single(i=>i.name=="SkillVisual_AbsorbHeal").rectTransform.anchoredPosition,Is.EqualTo(Point(2,2)+Vector2.up*24));
        }
        [Test] public void PayDayGoldOnlyAppearsWhenActualGoldAwardArrives()
        {
            frame.units[0].action=CombatActionState.Idle;frame.events=new[]{Skill("skill-meowth-r1"),Damage(12)};Render(12);Assert.That(Images(),Is.Empty);
            frame.events=new[]{new CombatEventView{kind="SkillGoldAwarded",source="caster",target="caster",tick=20,value=1}};
            Render(20);Render(20);Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_PayDayGold"));
            frame.events=Array.Empty<CombatEventView>();Render(40);Assert.That(Images(),Is.Empty);
        }
        [Test] public void StompAndThunderWaveUseActualVictims()
        {
            frame.units[0].action=CombatActionState.Idle;frame.events=new[]{Skill("skill-ponyta-r1"),Damage(12)};Render(12);
            Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_Stomp"));
            frame.events=Array.Empty<CombatEventView>();Render(40);Assert.That(Images(),Is.Empty);
            frame.events=new[]{new CombatEventView{kind="StatusApplied",detail="status-magnemite-r1-s0",source="caster",target="target",sequence=101,tick=42},
                new CombatEventView{kind="StatusApplied",detail="status-magnemite-r1-s0",source="caster",target="caster",sequence=102,tick=42}};
            Render(42);Render(42);Assert.That(Images().Length,Is.EqualTo(2));
            Assert.That(Images().All(i=>i.name=="SkillVisual_ThunderWave"),Is.True);
        }
        [TestCase(1,7)] [TestCase(2,19)]
        public void FireSpinCoversGroundForThreeSecondsWithoutRestartingOnDamage(int radius,int count)
        {
            frame.units[0].action=CombatActionState.Idle;var center=new BoardPosition(3,3);
            var cells=SkillGeometry.Cells(SkillAreaShape.Radius,center,center,radius).Select(p=>new CombatCellView{column=p.Column,row=p.Row}).ToArray();
            var area=new CombatEventView{kind="SkillArea",castId=1,stepIndex=1,tick=12,area=new CombatAreaView{cells=cells}};
            frame.events=new[]{Skill("skill-rapidash-r"+(radius==2?3:1)),area};Render(12);Render(12);
            Assert.That(Images().Length,Is.EqualTo(count));
            area.stepIndex=0;area.tick=42;frame.events=new[]{area};Render(42);Assert.That(Images().Length,Is.EqualTo(count));
            frame.events=Array.Empty<CombatEventView>();Render(101);Assert.That(Images().All(i=>i.enabled),Is.True);
            Render(102);Assert.That(Images(),Is.Empty);
        }
        [TestCase(false)] [TestCase(true)]
        public void SonicBoomUsesThreeOrderedSheetsWithAuthoredDelayAndDirection(bool left)
        {
            frame.units[0].action=CombatActionState.Idle;int origin=left?5:1;
            var cells=Enumerable.Range(1,3).Select(n=>new CombatCellView{column=origin+(left?-n:n),row=3}).ToArray();
            frame.events=new[]{Skill("skill-magneton-r1"),new CombatEventView{kind="SkillArea",castId=1,tick=12,
                area=new CombatAreaView{cells=cells,origin=new CombatCellView{column=origin,row=3},aim=cells[0]}}};
            Render(12);Render(12);Assert.That(Images().Length,Is.EqualTo(3));
            for(int n=1;n<=3;n++) {
                var entry=catalog.Entries.Single(e=>e.Name=="SonicBoom"+n);var image=Images().Single(i=>i.name=="SkillVisual_SonicBoom"+n);
                Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(cells[n-1].column,3)+Vector2.up*14));
                Assert.That(image.rectTransform.localEulerAngles.z,Is.EqualTo(left?180:0).Within(.01));
                Assert.That(image.enabled,Is.EqualTo(entry.DelaySeconds==0));
            }
            frame.events=Array.Empty<CombatEventView>();Render(28);Assert.That(Images().All(i=>i.enabled),Is.True);
            Render(60);Assert.That(Images(),Is.Empty);
        }
        [TestCase(0,false)] [TestCase(1,false)] [TestCase(2,false)]
        [TestCase(0,true)] [TestCase(1,true)] [TestCase(2,true)]
        public void TriAttackUsesElementRowForActualFlightAndImpact(int step,bool left)
        {
            frame.units[0].action=CombatActionState.Idle;frame.units[1].column=left?0:4;
            var p=new CombatProjectileView{id=201,skillId="skill-dodrio-r1",castId=1,stepIndex=step,source="caster",target="target",
                spawnTick=12,arrivalTick=24,column=2,row=2,targetColumn=frame.units[1].column,targetRow=2};
            frame.projectiles=new[]{p};Render(18);Render(18);
            var image=Images().Single();Assert.That(image.name,Is.EqualTo("SkillVisual_TriAttackFlight"+(step+1)));
            Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo((Point(2,2)+Point(p.targetColumn,2))/2+Vector2.up*24));
            Assert.That(image.rectTransform.localEulerAngles.z,Is.EqualTo(left?180:0).Within(.01));
            Assert.That(SkillVisualBattleView.UsesProjectileVisual(catalog,p.skillId,step),Is.True);
            frame.projectiles=Array.Empty<CombatProjectileView>();frame.events=new[]{new CombatEventView{kind="ProjectileHit",projectile=p,tick=24}};
            Render(24);Render(24);image=Images().Single();Assert.That(image.name,Is.EqualTo("SkillVisual_TriAttackImpact"+(step+1)));
            Assert.That(image.sprite,Is.SameAs(catalog.Entries.Single(e=>e.Name=="TriAttackImpact"+(step+1)).Frames[0]));
            frame.events=Array.Empty<CombatEventView>();Render(30);Assert.That(Images(),Is.Empty);
        }
        [TestCase("dodrio")] [TestCase("porygon")]
        public void ActualTriAttackProjectilesCarryAllThreeStageTags(string species)
        {
            var session=new EffectLabSession(PokeChess.Content.GameContent.Roster);session.ReplaceCaster(species);session.Cast();
            var stages=new System.Collections.Generic.HashSet<int>();
            for(int i=0;i<80;i++) {
                session.Step();
                foreach(var e in session.Battle.Projectiles.EventsThisTick.Where(e=>e.Kind==ProjectileEventKind.Spawned)) {
                    var p=e.Projectile;Assert.That(p.SkillId,Does.StartWith("skill-"+species));
                    Assert.That(p.CastId,Is.GreaterThan(0));stages.Add(p.StepIndex);
                }
            }
            CollectionAssert.AreEquivalent(new[]{0,1,2},stages);
        }
        [Test] public void PorygonThreeStarTagsThreeFlightsForEveryEnemy()
        {
            var session=new EffectLabSession(PokeChess.Content.GameContent.Roster);session.ReplaceCaster("porygon");session.SetRank(session.CasterId,UnitRank.Three);session.Cast();
            var flights=new System.Collections.Generic.List<ProjectileState>();
            for(int i=0;i<80;i++) {
                session.Step();flights.AddRange(session.Battle.Projectiles.EventsThisTick.Where(e=>e.Kind==ProjectileEventKind.Spawned).Select(e=>e.Projectile));
            }
            Assert.That(flights.Count,Is.EqualTo(9));Assert.That(flights.Select(p=>p.TargetId).Distinct().Count(),Is.EqualTo(3));
            foreach(var group in flights.GroupBy(p=>p.TargetId))CollectionAssert.AreEquivalent(new[]{0,1,2},group.Select(p=>p.StepIndex));
        }
        [Test] public void ClampDamageReplacesBindingAndReturnsToHeldShell()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.units[1].statusDetails=new[]{new CombatStatusView{id=301,definition="status-shellder-r1-s0",source="caster",startTick=12,endTick=72}};
            Render(40);var bind=Images().Single();Assert.That(bind.name,Is.EqualTo("SkillVisual_ClampBinding"));
            frame.events=new[]{new CombatEventView{kind="StatusPulse",effectId=301,source="caster",target="target",detail="status-shellder-r1-s0",tick=42}};
            Render(42);Assert.That(bind.enabled,Is.False);Assert.That(Images().Single(i=>i.enabled).name,Is.EqualTo("SkillVisual_ClampDamage"));
            frame.events=Array.Empty<CombatEventView>();Render(58);Assert.That(bind.enabled,Is.True);
            frame.units[1].statusDetails=Array.Empty<CombatStatusView>();Render(59);Assert.That(Images(),Is.Empty);
        }
        [Test] public void SpikeCannonTagsAllFourActualShots()
        {
            var session=new EffectLabSession(PokeChess.Content.GameContent.Roster);session.ReplaceCaster("cloyster");session.Cast();
            var steps=new System.Collections.Generic.HashSet<int>();
            for(int i=0;i<80;i++){session.Step();foreach(var e in session.Battle.Projectiles.EventsThisTick.Where(e=>e.Kind==ProjectileEventKind.Spawned)){
                Assert.That(e.Projectile.SkillId,Does.StartWith("skill-cloyster"));steps.Add(e.Projectile.StepIndex);}}
            CollectionAssert.AreEquivalent(new[]{0,1,2,3},steps);
        }
        [Test] public void BonemerangUsesActualOutgoingAndReturningPath()
        {
            frame.units[0].action=CombatActionState.Idle;
            var p=new CombatPathProjectileView{id=302,skill="skill-marowak-r1",column=2,row=2,spawnTick=12,previousTick=12,nextTick=16,nextIndex=0,
                path=new[]{new CombatCellView{column=3,row=2},new CombatCellView{column=4,row=2},new CombatCellView{column=3,row=2},new CombatCellView{column=2,row=2}}};
            frame.pathProjectiles=new[]{p};Render(14);var image=Images().Single();Assert.That(image.name,Is.EqualTo("SkillVisual_Bonemerang"));
            Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo((Point(2,2)+Point(3,2))/2+Vector2.up*24));
            p.nextIndex=2;p.previousTick=20;p.nextTick=24;Render(22);
            Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo((Point(4,2)+Point(3,2))/2+Vector2.up*24));
            Assert.That(image.rectTransform.localEulerAngles.z,Is.EqualTo(180).Within(.01));
            frame.pathProjectiles=Array.Empty<CombatPathProjectileView>();Render(30);Assert.That(Images(),Is.Empty);
        }
        [Test] public void SmokeScreenUsesGroundLifetimeWithIntroLoopAndFadeOnEmptyCells()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.grounds=new[]{new CombatGroundView{id=303,status="status-horsea-r3-s0",startTick=12,endTick=87,column=3,row=3,radius=2}};
            var entry=catalog.Entries.Single(e=>e.Name=="SmokeScreen");Render(12);Assert.That(Images().Length,Is.EqualTo(19));
            Assert.That(Images().All(i=>i.sprite==entry.IntroFrames[0]),Is.True);
            Render(42);Assert.That(Images().All(i=>entry.Frames.Contains(i.sprite)),Is.True);
            Render(80);Assert.That(Images().All(i=>entry.OutroFrames.Contains(i.sprite)),Is.True);
            Render(87);Assert.That(Images(),Is.Empty);
        }
        [TestCase("koffing","SmogSustain")] [TestCase("weezing","SludgePoison")]
        public void PoisonVisualsFollowTheirActualStatusAndDisappearWhenRemoved(string species,string visual)
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.units[1].statusDetails=new[]{new CombatStatusView{id=304,definition="status-"+species+"-r1-s0",source="caster",startTick=12,endTick=132}};
            Render(12);Assert.That(Images().Single().enabled,Is.False);
            Render(30);Assert.That(Images().Single().enabled,Is.True);Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_"+visual));
            frame.units[1].column=5;Render(40);Assert.That(Images().Single().rectTransform.anchoredPosition,Is.EqualTo(Point(5,2)+Vector2.up*24));
            frame.units[1].statusDetails=Array.Empty<CombatStatusView>();Render(41);Assert.That(Images(),Is.Empty);
        }
        [Test] public void BoneClubUsesDamageTarget()
        {
            frame.units[0].action=CombatActionState.Idle;frame.events=new[]{Skill("skill-cubone-r1"),Damage(12)};Render(12);
            Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_BoneClub"));
        }
        [Test] public void VineWhipUsesImpactThenBindingIntroLoopAndExpiry()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.units[1].statusDetails=new[]{new CombatStatusView{id=401,definition="status-bulbasaur-r1-s0",source="caster",startTick=12,endTick=42}};
            frame.events=new[]{Skill("skill-bulbasaur-r1"),Damage(12)};Render(12);
            Assert.That(Images().Length,Is.EqualTo(2));
            var entry=catalog.Entries.Single(e=>e.Name=="VineWhipBinding");var bind=Images().Single(i=>i.name=="SkillVisual_VineWhipBinding");
            Assert.That(bind.sprite,Is.SameAs(entry.IntroFrames[0]));
            Assert.That(bind.rectTransform.anchoredPosition,Is.EqualTo(Point(4,2)+Vector2.up*12));
            frame.events=Array.Empty<CombatEventView>();Render(30);Assert.That(Images().Length,Is.EqualTo(1));
            Assert.That(entry.Frames.Contains(bind.sprite),Is.True);Render(42);Assert.That(Images(),Is.Empty);
        }
        [TestCase(false)] [TestCase(true)]
        public void RazorLeafFormsThenFliesAlongEntireAuthoredLine(bool left)
        {
            int origin=left?5:1;frame.units[0].column=origin;frame.units[0].row=3;frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{new CombatEventView{kind="SkillCastStarted",detail="skill-ivysaur-r1",source="caster",target="target",castId=1,tick=0}};
            Render(0);Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_RazorLeafForm"));
            var cells=Enumerable.Range(1,3).Select(n=>new CombatCellView{column=origin+(left?-n:n),row=3}).ToArray();
            frame.events=new[]{new CombatEventView{kind="SkillArea",source="caster",castId=1,tick=12,
                area=new CombatAreaView{cells=cells,origin=new CombatCellView{column=origin,row=3},aim=cells[0]}}};
            Render(12);Render(12);var leaf=Images().Single();Assert.That(leaf.name,Is.EqualTo("SkillVisual_RazorLeafFlight"));
            Assert.That(leaf.rectTransform.localEulerAngles.z,Is.EqualTo(left?180:0).Within(.01));
            Assert.That(leaf.rectTransform.anchoredPosition,Is.EqualTo(Point(origin,3)+Vector2.up*24));
            frame.events=Array.Empty<CombatEventView>();Render(17);
            var expected=Vector2.Lerp(Point(origin,3),Point(cells.Last().column,3),(5/30f)/.35f)+Vector2.up*24;
            Assert.That(Vector2.Distance(leaf.rectTransform.anchoredPosition,expected),Is.LessThan(.001));
            Render(23);Assert.That(Images(),Is.Empty);
        }
        [TestCase(2,2)] [TestCase(6,2)] [TestCase(4,4)]
        public void VineWhipImpactMovesTowardCasterButBindingStaysOnTarget(int casterColumn,int casterRow)
        {
            frame.units[0].action=CombatActionState.Idle;frame.units[0].column=casterColumn;frame.units[0].row=casterRow;
            frame.units[1].statusDetails=new[]{new CombatStatusView{id=401,definition="status-bulbasaur-r1-s0",source="caster",startTick=12,endTick=42}};
            frame.events=new[]{Skill("skill-bulbasaur-r1"),Damage(12)};Render(12);
            var impact=Images().Single(i=>i.name=="SkillVisual_VineWhipImpact");
            var expected=Point(4,2)+Vector2.up*24+(Point(casterColumn,casterRow)-Point(4,2)).normalized*18;
            Assert.That(Vector2.Distance(impact.rectTransform.anchoredPosition,expected),Is.LessThan(.001));
            Assert.That(Images().Single(i=>i.name=="SkillVisual_VineWhipBinding").rectTransform.anchoredPosition,Is.EqualTo(Point(4,2)+Vector2.up*12));
        }
        [TestCase(false)] [TestCase(true)]
        public void SolarBeamChargesThenUsesFourOrderedDirectionalSheets(bool left)
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{new CombatEventView{kind="SkillCastStarted",detail="skill-venusaur-r1",source="caster",target="target",castId=1,tick=0}};
            Render(0);Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_SolarBeamCharge"));
            Render(15);Assert.That(Images().Single().enabled,Is.True);
            int origin=left?5:1;var cells=Enumerable.Range(1,4).Select(n=>new CombatCellView{column=origin+(left?-n:n),row=3}).ToArray();
            frame.events=new[]{new CombatEventView{kind="SkillArea",castId=1,tick=30,area=new CombatAreaView{cells=cells,
                origin=new CombatCellView{column=origin,row=3},aim=cells[0]}}};
            Render(30);Render(30);Assert.That(Images().Length,Is.EqualTo(4));Render(43);
            for(int n=1;n<=4;n++) {
                var image=Images().Single(i=>i.name=="SkillVisual_SolarBeamTile"+n);
                Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(cells[n-1].column,3)+Vector2.up*24));
                Assert.That(image.rectTransform.localEulerAngles.z,Is.EqualTo(left?180:0).Within(.01));
                Assert.That(image.enabled,Is.True);
            }
            frame.events=Array.Empty<CombatEventView>();Render(60);Assert.That(Images(),Is.Empty);
        }
        [TestCase("charmander","Ember")] [TestCase("growlithe","Ember")] [TestCase("squirtle","WaterGun")]
        public void FireAndWaterProjectilesTurnIntoImpactsOnDamage(string species,string name)
        {
            frame.units[0].skillId="skill-"+species+"-r1";Render(8);
            Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_"+name+"Projectile"));
            frame.units[0].action=CombatActionState.Idle;frame.events=new[]{Skill("skill-"+species+"-r1"),Damage(12)};Render(12);Render(12);
            Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_"+name+"Impact"));
            frame.events=Array.Empty<CombatEventView>();Render(35);Assert.That(Images(),Is.Empty);
        }
        [Test] public void FlamethrowerUsesWholeConeWithGrowingLoopAndFadingFrames()
        {
            frame.units[0].action=CombatActionState.Idle;var origin=new BoardPosition(1,3);var aim=new BoardPosition(2,3);
            var cells=SkillGeometry.Cells(SkillAreaShape.Cone,origin,aim,3).Select(p=>new CombatCellView{column=p.Column,row=p.Row}).ToArray();
            frame.events=new[]{Skill("skill-charizard-r1"),new CombatEventView{kind="SkillArea",castId=1,tick=12,area=new CombatAreaView {
                cells=cells,origin=new CombatCellView{column=1,row=3},aim=new CombatCellView{column=2,row=3}}}};
            var entry=catalog.Entries.Single(e=>e.Name=="Flamethrower");Render(12);Render(12);Assert.That(Images().Length,Is.EqualTo(cells.Length));
            Assert.That(Images().All(i=>i.sprite==entry.IntroFrames[0]),Is.True);
            frame.events=Array.Empty<CombatEventView>();Render(27);Assert.That(Images().All(i=>entry.Frames.Contains(i.sprite)),Is.True);
            Render(37);Assert.That(Images().All(i=>entry.OutroFrames.Contains(i.sprite)),Is.True);
            Render(42);Assert.That(Images(),Is.Empty);
        }
        [Test] public void SkullBashShowsDefenseThenAdjacentContactAndAfterimages()
        {
            var node=new GameObject("Actor",typeof(RectTransform));node.transform.SetParent(root.transform,false);
            var actor=node.AddComponent<PokemonSpriteView>();actor.Initialize("wartortle","caster");actor.ShowIdle(0);
            frame.units[0].column=3;frame.units[0].action=CombatActionState.Idle;
            frame.units[0].statusDetails=new[]{new CombatStatusView{id=501,definition="status-wartortle-r1-s0",source="caster",startTick=12,endTick=42}};
            frame.events=new[]{Skill("skill-wartortle-r1")};Render(12);
            Assert.That(Images().Single(i=>i.name.StartsWith("SkillVisual_")).name,Is.EqualTo("SkillVisual_SkullBashDefense"));
            frame.events=Array.Empty<CombatEventView>();Render(41);
            Assert.That(Images().Any(i=>i.name=="SkillVisual_SkullBashDefense"),Is.True);
            frame.events=new[]{new CombatEventView{kind="MovementArrived",source="caster",target="caster",castId=1,stepIndex=1,tick=42,
                movement=new CombatMovementView{id=502,source="caster",unit="caster",kind=(int)SkillMovementKind.DashToTarget,
                    path=new[]{new CombatCellView{column=3,row=2}}}}};
            Render(42);view.CaptureAfterimages();view.UpdateLunges();
            Assert.That(Images().Any(i=>i.name=="SkillVisual_SkullBashDefense"),Is.False);
            Assert.That(Images().Count(i=>i.name=="SkillVisual_SkullBashAfterimage"),Is.EqualTo(4));
            frame.events=Array.Empty<CombatEventView>();Render(44);view.UpdateLunges();
            Assert.That(actor.SkillLungeOffset,Is.EqualTo(Point(4,2)-Point(3,2)));
            Render(48);view.UpdateLunges();Assert.That(actor.SkillLungeOffset,Is.EqualTo(Vector2.zero));
            Render(52);Assert.That(Images().Any(i=>i.name=="SkillVisual_SkullBashAfterimage"),Is.False);
        }
        [Test] public void ActualSkullBashKeepsOneSecondDefenseThenUsesFastDash()
        {
            var session=new EffectLabSession(PokeChess.Content.GameContent.Roster);session.ReplaceCaster("wartortle");session.Cast();
            long buffTick=session.Battle.StatusEffects.Active.Where(e=>e.TargetId==session.CasterId&&e.Definition.Id.StartsWith("status-wartortle"))
                .Select(e=>e.StartTick).DefaultIfEmpty(-1).First();bool dashed=false;
            for(int i=0;i<80;i++) {
                session.Step();
                foreach(var e in session.Battle.StatusEffects.EventsThisTick.Where(e=>e.Kind==StatusEventKind.Applied&&e.Effect.TargetId==session.CasterId))
                    if(e.Effect.Definition.Id.StartsWith("status-wartortle"))buffTick=e.Tick;
                foreach(var e in session.Battle.Movement.EventsThisTick.Where(e=>e.Kind==CombatMovementEventKind.Started&&e.Movement.Kind==SkillMovementKind.DashToTarget)) {
                    Assert.That(buffTick,Is.GreaterThanOrEqualTo(0));
                    Assert.That(e.Tick-buffTick,Is.EqualTo(session.Battle.TickRate));
                    Assert.That(e.Movement.EndTick-e.Movement.StartTick,Is.EqualTo(BattleSimulation.ToTicks(.05,session.Battle.TickRate,false)));dashed=true;
                }
            }
            Assert.That(dashed,Is.True);
        }
        [TestCase("poliwag")] [TestCase("haunter")]
        public void HypnosisPlaysOnActualSleepingTargetAndFollowsIt(string species)
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{new CombatEventView{kind="StatusApplied",detail="status-"+species+"-r1-s0",source="caster",target="target",sequence=601,tick=12}};
            Render(12);Render(12);var image=Images().Single();Assert.That(image.name,Is.EqualTo("SkillVisual_Hypnosis"));
            Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(4,2)+Vector2.up*24));
            frame.events=Array.Empty<CombatEventView>();frame.units[1].column=5;Render(15);
            Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(5,2)+Vector2.up*24));
            Render(40);Assert.That(Images(),Is.Empty);
        }
        [TestCase("poliwhirl")] [TestCase("slowpoke")]
        public void AmnesiaUsesBuffLifetimeForIntroLoopFadeAndEarlyRemoval(string species)
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.units[0].statusDetails=new[]{new CombatStatusView{id=602,definition="status-"+species+"-r1-s0",source="caster",startTick=12,endTick=162}};
            var entry=catalog.Entries.Single(e=>e.Name=="Amnesia");Render(12);var image=Images().Single();
            Assert.That(image.sprite,Is.SameAs(entry.IntroFrames[0]));
            Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(2,2)+Vector2.up*42));
            frame.units[0].column=3;Render(60);Assert.That(entry.Frames.Contains(image.sprite),Is.True);
            Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(3,2)+Vector2.up*42));
            Render(157);Assert.That(entry.OutroFrames.Contains(image.sprite),Is.True);
            Render(162);Assert.That(Images(),Is.Empty);
            frame.units[0].statusDetails[0].startTick=162;frame.units[0].statusDetails[0].endTick=312;Render(163);
            frame.units[0].statusDetails=Array.Empty<CombatStatusView>();Render(164);Assert.That(Images(),Is.Empty);
        }
        [TestCase("poliwrath")] [TestCase("machamp")]
        public void SubmissionPlaysImpactHealthCostAndSharedStunForActualLifetime(string species)
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.units[1].statusDetails=new[]{new CombatStatusView{id=701,definition="status-"+species+"-r1-s0",source="caster",startTick=12,endTick=42}};
            frame.events=new[]{Skill("skill-"+species+"-r1"),Damage(12),new CombatEventView{kind="EffectHealthCost",detail="Applied",source="caster",target="caster",castId=1,tick=12,value=8}};
            Render(12);Render(12);Assert.That(Images().Length,Is.EqualTo(2));
            Assert.That(Images().Single(i=>i.name=="SkillVisual_HealthCost").rectTransform.anchoredPosition,Is.EqualTo(Point(2,2)+Vector2.up*24));
            Assert.That(catalog.Entries.Any(e=>e.Name=="SubmissionStun"),Is.False);
            frame.events=Array.Empty<CombatEventView>();Render(35);Assert.That(Images().Any(i=>i.enabled),Is.False);
            Render(42);Assert.That(Images(),Is.Empty);
        }
        [Test] public void SubmissionCarryUsesExistingSpriteAfterimagesOnlyForCaster()
        {
            var node=new GameObject("Actor",typeof(RectTransform));node.transform.SetParent(root.transform,false);
            var actor=node.AddComponent<PokemonSpriteView>();actor.Initialize("poliwrath","caster");actor.ShowIdle(0);
            frame.units[0].action=CombatActionState.Idle;frame.units[0].column=2;
            var casterMove=new CombatMovementView{id=702,source="caster",unit="caster",kind=(int)SkillMovementKind.CarryTarget,
                startTick=12,departureTick=16,endTick=28,path=new[]{new CombatCellView{column=2,row=2},new CombatCellView{column=4,row=2},new CombatCellView{column=3,row=2}}};
            frame.movements=new[]{casterMove};
            Assert.That(ThrowPresentation.Playback(frame,"caster",14,Vector2.zero,Point),Is.EqualTo(Point(3,2)));
            frame.movements=Array.Empty<CombatMovementView>();
            frame.events=new[]{Skill("skill-poliwrath-r1"),new CombatEventView{kind="MovementStarted",castId=1,source="caster",target="caster",tick=12,movement=casterMove},
                new CombatEventView{kind="MovementStarted",castId=1,source="caster",target="target",tick=12,movement=new CombatMovementView{id=703,unit="target",kind=(int)SkillMovementKind.Throw,path=casterMove.path}}};
            Render(12);view.CaptureAfterimages();Render(12);view.CaptureAfterimages();
            Assert.That(Images().Count(i=>i.name=="SkillVisual_SubmissionAfterimage"),Is.EqualTo(1));
            frame.events=Array.Empty<CombatEventView>();Render(14);view.CaptureAfterimages();
            Assert.That(Images().Count(i=>i.name=="SkillVisual_SubmissionAfterimage"),Is.EqualTo(3));
            Render(16);view.CaptureAfterimages();
            var ghosts=Images().Where(i=>i.name=="SkillVisual_SubmissionAfterimage").ToArray();Assert.That(ghosts.Length,Is.EqualTo(4));
            Assert.That(ghosts.All(i=>i.sprite==actor.CharacterImage.sprite),Is.True);
            Assert.That(ghosts.All(i=>i.rectTransform.anchoredPosition.x<Point(4,2).x+actor.CharacterImage.rectTransform.anchoredPosition.x),Is.True);
            frame.events=new[]{new CombatEventView{kind="MovementArrived",castId=1,source="caster",target="caster",tick=28,movement=casterMove}};
            Render(28);view.CaptureAfterimages();Assert.That(Images().Any(i=>i.name=="SkillVisual_SubmissionAfterimage"&&i.enabled),Is.False);
        }
        [TestCase("poliwrath",true)] [TestCase("machamp",true)]
        [TestCase("poliwrath",false)] [TestCase("machamp",false)]
        public void ActualSubmissionKeepsItsMovementDamageStunAndHealthCostRules(string species,bool shouldCarry)
        {
            var session=new EffectLabSession(PokeChess.Content.GameContent.Roster);session.ReplaceCaster(species);
            session.Layout.Single(p=>p.Id=="target").Pokemon=shouldCarry?"slowpoke":"snorlax";session.Reset();
            session.Remove("enemy2");session.Remove("enemy3");session.Cast();
            bool carry=false,damage=false,cost=false,stun=false;
            for(int i=0;i<80;i++) {
                foreach(var e in session.Battle.Movement.EventsThisTick)carry|=e.Movement.Kind==SkillMovementKind.CarryTarget;
                foreach(var e in session.Battle.SkillEffectEventsThisTick){damage|=e.Type==SkillEffectType.Damage&&e.ActualValue>0;cost|=e.Type==SkillEffectType.HealthCost&&e.ActualValue>0;}
                stun|=session.Battle.StatusEffects.Active.Any(s=>s.Definition.CrowdControl==CrowdControlKind.Stun);
                session.Step();
            }
            Assert.That(carry,Is.EqualTo(shouldCarry));Assert.That(damage&&cost&&stun,Is.True);
        }
        [Test] public void SubmissionApproachesStationaryTargetThenBothJumpFromSamePosition()
        {
            var caster=new CombatMovementView{unit="caster",kind=(int)SkillMovementKind.CarryTarget,startTick=10,departureTick=14,endTick=26,
                path=new[]{new CombatCellView{column=1,row=3},new CombatCellView{column=3,row=3},new CombatCellView{column=4,row=3}}};
            var target=new CombatMovementView{unit="target",kind=(int)SkillMovementKind.Throw,startTick=10,departureTick=14,endTick=26,
                path=new[]{new CombatCellView{column=3,row=3},new CombatCellView{column=5,row=3}}};
            frame.movements=new[]{caster,target};
            Assert.That(ThrowPresentation.Playback(frame,"target",12,Vector2.zero,Point),Is.EqualTo(Point(3,3)));
            Assert.That(ThrowPresentation.Playback(frame,"caster",12,Vector2.zero,Point),Is.EqualTo(Point(2,3)));
            Assert.That(ThrowPresentation.Playback(frame,"caster",14,Vector2.zero,Point),Is.EqualTo(Point(3,3)));
            Assert.That(ThrowPresentation.Playback(frame,"target",14,Vector2.zero,Point),Is.EqualTo(Point(3,3)));
            var a=ThrowPresentation.Playback(frame,"caster",20,Vector2.zero,Point);var b=ThrowPresentation.Playback(frame,"target",20,Vector2.zero,Point);
            Assert.That(a.y,Is.EqualTo(Point(3,3).y+90));Assert.That(b.y,Is.EqualTo(a.y));
            Assert.That(ThrowPresentation.Playback(frame,"caster",26,Vector2.zero,Point),Is.EqualTo(Point(4,3)));
            Assert.That(ThrowPresentation.Playback(frame,"target",26,Vector2.zero,Point),Is.EqualTo(Point(5,3)));
        }
        [TestCase("poliwrath")] [TestCase("machamp")]
        public void ActualSubmissionAtWallStillDealsDamageStunAndOneHealthCost(string species)
        {
            var session=new EffectLabSession(PokeChess.Content.GameContent.Roster);session.ReplaceCaster(species);
            session.Layout.Single(p=>p.Id=="target").Pokemon="slowpoke";session.Reset();
            session.Move("enemy3",new BoardPosition(0,1));session.Move("enemy2",new BoardPosition(0,0));
            session.Move(session.CasterId,new BoardPosition(5,3));session.Move("target",new BoardPosition(6,3));
            float beforeCaster=session.Unit(session.CasterId).CurrentHP;float beforeTarget=session.Unit("target").CurrentHP;
            int costs=0,hits=0;bool stunned=false;session.Cast();
            for(int i=0;i<80;i++) {
                foreach(var e in session.Battle.SkillEffectEventsThisTick) {
                    if(e.Type==SkillEffectType.HealthCost&&e.ActualValue>0)costs++;
                    if(e.Type==SkillEffectType.Damage&&e.ActualValue>0)hits++;
                }
                stunned|=session.Battle.StatusEffects.Active.Any(s=>s.TargetId=="target"&&s.Definition.CrowdControl==CrowdControlKind.Stun);session.Step();
            }
            Assert.That(costs,Is.EqualTo(1));Assert.That(hits,Is.EqualTo(1));Assert.That(stunned,Is.True);
            Assert.That(session.Unit(session.CasterId).CurrentHP,Is.LessThan(beforeCaster));Assert.That(session.Unit("target").CurrentHP,Is.LessThan(beforeTarget));
            Assert.That(session.Unit(session.CasterId).Position,Is.EqualTo(new BoardPosition(5,3)));
            Assert.That(session.Unit("target").Position,Is.EqualTo(new BoardPosition(6,3)));
        }
        [TestCase("slowbro",1)] [TestCase("alakazam",1)] [TestCase("mewtwo",3)]
        public void PsychicEnergyAndDefenseBreakUseEveryActualVictim(string species,int victims)
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.units=frame.units.Concat(Enumerable.Range(1,victims-1).Select(n=>new CombatUnitView{id="victim"+n,hp=100,onBoard=true,column=4,row=2+n})).ToArray();
            var events=new System.Collections.Generic.List<CombatEventView>{Skill("skill-"+species+"-r1")};
            foreach(var victim in frame.units.Skip(1)) {
                events.Add(new CombatEventView{kind="EffectDamage",detail="Applied",source="caster",target=victim.id,castId=1,tick=12});
                events.Add(new CombatEventView{kind="StatusApplied",detail="status-"+species+"-r1-s0",source="caster",target=victim.id,sequence=800+victim.row,tick=12});
            }
            frame.events=events.ToArray();Render(12);Render(12);
            Assert.That(Images().Count(i=>i.name=="SkillVisual_PsychicEnergy"&&i.enabled),Is.EqualTo(victims));
            Assert.That(Images().Count(i=>i.name=="SkillVisual_PsychicDefenseBreak"&&i.enabled),Is.EqualTo(victims));
            foreach(var victim in frame.units.Skip(1)) {
                Assert.That(Images().Any(i=>i.name=="SkillVisual_PsychicEnergy"&&i.rectTransform.anchoredPosition==Point(victim.column,victim.row)+Vector2.up*24),Is.True);
                Assert.That(Images().Any(i=>i.name=="SkillVisual_PsychicDefenseBreak"&&i.rectTransform.anchoredPosition==Point(victim.column,victim.row)+Vector2.up*42),Is.True);
            }
            frame.events=Array.Empty<CombatEventView>();Render(40);Assert.That(Images().Any(i=>i.enabled),Is.False);
        }
        [Test] public void HighJumpKickLeavesArcAfterimagesDuringTravelThenPlaysImpactAndSharedCost()
        {
            var node=new GameObject("Actor",typeof(RectTransform));node.transform.SetParent(root.transform,false);
            var actor=node.AddComponent<PokemonSpriteView>();actor.Initialize("hitmonlee","caster");actor.ShowIdle(0);
            frame.units[0].action=CombatActionState.Idle;
            var move=new CombatMovementView{id=901,unit="caster",source="caster",kind=(int)SkillMovementKind.JumpToTarget,startTick=12,departureTick=12,endTick=24,
                path=new[]{new CombatCellView{column=2,row=2},new CombatCellView{column=5,row=2}}};
            frame.movements=new[]{move};Assert.That(ThrowPresentation.Playback(frame,"caster",18,Vector2.zero,Point),Is.EqualTo((Point(2,2)+Point(5,2))/2+Vector2.up*90));
            frame.events=new[]{Skill("skill-hitmonlee-r1"),new CombatEventView{kind="MovementStarted",source="caster",target="caster",castId=1,tick=12,movement=move}};
            Render(12);view.CaptureAfterimages();Assert.That(Images().Count(i=>i.name=="SkillVisual_HighJumpKickAfterimage"),Is.EqualTo(1));
            frame.events=Array.Empty<CombatEventView>();Render(18);view.CaptureAfterimages();
            var ghosts=Images().Where(i=>i.name=="SkillVisual_HighJumpKickAfterimage").ToArray();Assert.That(ghosts.Length,Is.EqualTo(3));
            var peak=(Point(2,2)+Point(5,2))/2+Vector2.up*90+actor.CharacterImage.rectTransform.anchoredPosition;
            Assert.That(ghosts.Any(g=>Vector2.Distance(g.rectTransform.anchoredPosition,peak)<.001f),Is.True);
            Render(21);view.CaptureAfterimages();Assert.That(Images().Count(i=>i.name=="SkillVisual_HighJumpKickAfterimage"&&i.enabled),Is.EqualTo(3));
            frame.movements=Array.Empty<CombatMovementView>();frame.events=new[]{Damage(24),new CombatEventView{kind="EffectHealthCost",detail="Applied",source="caster",target="caster",castId=1,tick=24,value=8}};
            Render(24);Assert.That(Images().Any(i=>i.name=="SkillVisual_HighJumpKickImpact"&&i.enabled),Is.True);
            Assert.That(Images().Any(i=>i.name=="SkillVisual_HealthCost"&&i.enabled),Is.True);
            frame.events=Array.Empty<CombatEventView>();Render(48);Assert.That(Images().Any(i=>i.name.StartsWith("SkillVisual_")&&i.enabled),Is.False);
        }
        [Test] public void HealthCostAssetIsSharedFromCommonFolder()
        {
            var entry=catalog.Entries.Single(e=>e.Name=="HealthCost");
            Assert.That(catalog.Entries.Any(e=>e.Name=="SubmissionHealthCost"),Is.False);
            foreach(var species in new[]{"poliwrath","machamp","hitmonlee"})Assert.That(entry.Matches(SkillVisualTrigger.HealthCost,"skill-"+species+"-r1",null,0,0),Is.True);
            Assert.That(entry.Frames.All(s=>AssetDatabase.GetAssetPath(s)=="Assets/_Project/Sprites/Effects/Common/health-cost/health_cost_8f_4x2_64.png"),Is.True);
        }
        [Test] public void ActualHighJumpKickHitsFarthestEnemyAndPaysHealthOnLanding()
        {
            var session=new EffectLabSession(PokeChess.Content.GameContent.Roster);session.ReplaceCaster("hitmonlee");session.Cast();
            string victim=null;long end=-1,hit=-1,cost=-1;
            for(int i=0;i<100;i++) {
                foreach(var e in session.Battle.Movement.EventsThisTick)if(e.Kind==CombatMovementEventKind.Started&&e.Movement.Kind==SkillMovementKind.JumpToTarget)end=e.Movement.EndTick;
                foreach(var e in session.Battle.SkillEffectEventsThisTick) {
                    if(e.Type==SkillEffectType.Damage&&e.ActualValue>0){victim=e.TargetId;hit=e.Tick;}
                    if(e.Type==SkillEffectType.HealthCost&&e.ActualValue>0)cost=e.Tick;
                }
                session.Step();
            }
            Assert.That(victim,Is.EqualTo("enemy3"));Assert.That(end,Is.GreaterThan(0));Assert.That(hit,Is.EqualTo(end));Assert.That(cost,Is.EqualTo(end));
        }
        [Test] public void LightScreenTracksEachBuffedUnitThroughIntroLoopAndFade()
        {
            frame.units[0].action=CombatActionState.Idle;
            foreach(var unit in frame.units)unit.statusDetails=new[]{new CombatStatusView{id=1001,definition="status-chansey-r1-s0",source="caster",startTick=12,endTick=132}};
            var entry=catalog.Entries.Single(e=>e.Name=="LightScreen");Render(12);Assert.That(Images().Count(i=>i.enabled&&i.sprite==entry.IntroFrames[0]),Is.EqualTo(2));
            Render(42);Assert.That(Images().Count(i=>i.enabled&&entry.Frames.Contains(i.sprite)),Is.EqualTo(2));
            Render(127);Assert.That(Images().Count(i=>i.enabled&&entry.OutroFrames.Contains(i.sprite)),Is.EqualTo(2));
            Render(132);Assert.That(Images().Any(i=>i.enabled),Is.False);
        }
        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void SkyAttackUsesTakeoffDiveRankImpactAndActualBurnLifetime(int rank)
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{new CombatEventView{kind="StatusApplied",detail="status-moltres-r"+rank+"-s1",source="caster",target="caster",sequence=1900,tick=0}};
            Render(0);Assert.That(Images().Single(i=>i.enabled).name,Is.EqualTo("SkillVisual_SkyAttackTakeoff"));
            frame.units[0].column=3;var cell=new CombatCellView{column=3,row=2};
            frame.events=new[]{Skill("skill-moltres-r"+rank),new CombatEventView{kind="MovementArrived",castId=1,tick=45,movement=new CombatMovementView{id=1901,unit="caster",kind=(int)SkillMovementKind.JumpToCluster,path=new[]{cell}}},
                new CombatEventView{kind="SkillArea",castId=1,tick=45,area=new CombatAreaView{cells=new[]{cell,new CombatCellView{column=4,row=2}}}}};
            Render(45);var dive=Images().Single(i=>i.name=="SkillVisual_SkyAttackDive");Assert.That(dive.rectTransform.anchoredPosition.y,Is.GreaterThan(Point(3,2).y+100));
            frame.events=Array.Empty<CombatEventView>();Render(51);
            Assert.That(Images().Any(i=>i.enabled&&i.name=="SkillVisual_"+(rank==3?"SkyAttackImpact3":"SkyAttackImpact")),Is.True);
            Assert.That(Images().Count(i=>i.enabled&&i.name=="SkillVisual_SkyAttackArea3"),Is.EqualTo(rank==3?2:0));
            frame.units[1].statusDetails=new[]{new CombatStatusView{id=1902,definition="status-moltres-r"+rank+"-s0",source="caster",startTick=45,endTick=135}};
            Render(75);Assert.That(Images().Single(i=>i.enabled).name,Is.EqualTo("SkillVisual_SkyAttackResidual"));
            frame.units[1].statusDetails=Array.Empty<CombatStatusView>();Render(76);Assert.That(Images().Any(i=>i.enabled),Is.False);
        }
        [Test] public void ThunderFallsOntoVictimWithBranchesAndFootImpact()
        {
            frame.units[0].action=CombatActionState.Idle;frame.events=new[]{Skill("skill-zapdos-r1"),Damage(12)};Render(12);
            var graphic=root.GetComponentsInChildren<LightningLineGraphic>().Single();var bolt=graphic.Bolt;
            var top=bolt.GetPosition(0);var bottom=bolt.GetPosition(bolt.positionCount-1);
            Assert.That(top.x,Is.EqualTo(bottom.x).Within(.001));Assert.That(top.y-bottom.y,Is.EqualTo(180).Within(.001));
            Assert.That(bottom.x,Is.EqualTo(Point(4,2).x));Assert.That(bottom.y,Is.EqualTo(Point(4,2).y+5));
            Assert.That(graphic.GetComponentsInChildren<LineRenderer>(true).Length,Is.EqualTo(6));
            Assert.That(Images().Single(i=>i.enabled).name,Is.EqualTo("SkillVisual_ThunderImpact"));
            frame.events=Array.Empty<CombatEventView>();Render(15);Assert.That(graphic.color.a,Is.LessThan(.3f));
            Render(17);Assert.That(graphic.color.a,Is.GreaterThan(.9f));
            Render(25);Assert.That(root.GetComponentsInChildren<LightningLineGraphic>(),Is.Empty);
        }
        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void ThunderEmitsRealDamageEventsForDelayedChainVictims(int rank)
        {
            var session=new EffectLabSession(PokeChess.Content.GameContent.Roster);session.ReplaceCaster("zapdos");session.SetRank(session.CasterId,(UnitRank)rank);session.SetPreserveHealth(true);
            session.Cast();var hits=new System.Collections.Generic.List<SkillEffectEvent>();
            for(int n=0;n<60;n++) {
                hits.AddRange(session.Battle.SkillEffectEventsThisTick.Where(e=>e.SkillId=="skill-zapdos-r"+rank&&e.Type==SkillEffectType.Damage));session.Step();
            }
            Assert.That(hits.Select(e=>e.TargetId).Distinct().Count(),Is.EqualTo(3));
            Assert.That(hits.All(e=>e.CastId>0),Is.True);Assert.That(hits.Select(e=>e.Tick).Distinct().Count(),Is.GreaterThan(1));
            if(rank==3)Assert.That(hits.Select(e=>e.Tick).Distinct().Count(),Is.EqualTo(3));
        }
        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void BlizzardKeepsBaseImpactAndAddsWholeBoardSnowOnlyAtThreeStars(int rank)
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{Skill("skill-articuno-r"+rank),Damage(12)};Render(12);Render(12);
            Assert.That(Images().Count(i=>i.enabled&&i.name=="SkillVisual_BlizzardImpact"),Is.EqualTo(1));
            var snow=Images().Where(i=>i.name=="SkillVisual_BlizzardMapSnow").ToArray();
            Assert.That(snow.Length,Is.EqualTo(rank==3?56:0));
            if(rank==3) {
                Assert.That(snow.All(i=>i.enabled&&Mathf.Abs(i.color.a-.35f)<.001f),Is.True);
                Assert.That(snow.Select(i=>i.rectTransform.anchoredPosition).Distinct().Count(),Is.EqualTo(56));
            }
            frame.events=Array.Empty<CombatEventView>();Render(60);Assert.That(Images().Any(i=>i.enabled),Is.False);
        }
        [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void RestHealingLoopsOnlyWhileActualRestStatusExists(int rank)
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.units[0].statusDetails=new[]{new CombatStatusView{id=1800,definition="status-snorlax-r"+rank+"-s0",startTick=0,endTick=60}};
            var entry=catalog.Entries.Single(e=>e.Name=="RestHealing");Assert.That(entry.Frames.Length,Is.EqualTo(8));
            Render(0);var image=Images().Single(i=>i.enabled);Assert.That(image.sprite,Is.SameAs(entry.Frames[0]));
            frame.units[0].column=3;Render(30);Assert.That(image.sprite,Is.SameAs(entry.Frames[0]));
            Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(3,2)+Vector2.up*14));
            frame.units[0].statusDetails=Array.Empty<CombatStatusView>();Render(31);Assert.That(image==null||!image.enabled,Is.True);
        }
        [TestCase(false)] [TestCase(true)]
        public void IceBeamChargesDrawsStraightLineAndImpactsActualTarget(bool left)
        {
            frame.units[0].action=CombatActionState.Idle;if(left)frame.units[1].column=0;
            frame.events=new[]{new CombatEventView{kind="SkillCastStarted",detail="skill-lapras-r1",source="caster",target="target",castId=1,tick=0}};
            Render(0);Assert.That(Images().Single(i=>i.enabled).name,Is.EqualTo("SkillVisual_IceBeamCharge"));
            frame.events=new[]{Skill("skill-lapras-r1"),Damage(12)};Render(12);
            Assert.That(Images().Any(i=>i.name=="SkillVisual_IceBeamImpact"&&i.enabled),Is.False);
            frame.events=Array.Empty<CombatEventView>();Render(15);
            var graphic=root.GetComponentsInChildren<LightningLineGraphic>().Single();
            var from=graphic.Bolt.GetPosition(0);var to=graphic.Bolt.GetPosition(graphic.Bolt.positionCount-1);
            Assert.That((to.x-from.x)*(left?-1:1),Is.GreaterThan(0));
            for(int n=1;n<graphic.Bolt.positionCount;n++)Assert.That(graphic.Bolt.GetPosition(n).y,Is.EqualTo(from.y).Within(.001));
            Assert.That(Images().Single(i=>i.enabled).name,Is.EqualTo("SkillVisual_IceBeamImpact"));
            Render(30);Assert.That(Images().Any(i=>i.enabled),Is.False);Assert.That(root.GetComponentsInChildren<LightningLineGraphic>(),Is.Empty);
        }
        [TestCase("kadabra","PsybeamImpact",false,0)]
        [TestCase("machoke","LowKick",false,0)]
        [TestCase("geodude","RockThrowImpact",false,0)]
        [TestCase("gengar","DreamEaterHit",false,0)]
        [TestCase("gengar","DreamEaterAsleepHit",true,0)]
        [TestCase("pinsir","GuillotineNormal",false,0)]
        [TestCase("pinsir","GuillotineExecute",true,0)]
        [TestCase("dratini","SlamMain",false,0)]
        [TestCase("dratini","SlamRear",false,2)]
        public void PreparedTenDamageUsesActualConditionAndEffect(string species,string expected,bool condition,int effect)
        {
            frame.units[0].action=CombatActionState.Idle;var hit=Damage(12);hit.conditionMatched=condition;hit.effectIndex=effect;
            frame.events=new[]{Skill("skill-"+species+"-r1"),hit};Render(12);
            Assert.That(Images().Single(i=>i.enabled).name,Is.EqualTo("SkillVisual_"+expected));
        }
        [Test] public void FireBlastCoversEmptyCellsAndTeleportFocusExpires()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{Skill("skill-arcanine-r1"),new CombatEventView{kind="SkillArea",castId=1,tick=12,area=new CombatAreaView{cells=new[]{new CombatCellView{column=3,row=2},new CombatCellView{column=4,row=2}}}}};
            Render(12);Assert.That(Images().Count(i=>i.enabled&&i.name=="SkillVisual_FireBlast"),Is.EqualTo(2));
            frame.events=Array.Empty<CombatEventView>();frame.units[0].statusDetails=new[]{new CombatStatusView{id=1700,definition="status-abra-r1-s0",startTick=12,endTick=72}};
            Render(12);var focus=Images().Single(i=>i.name=="SkillVisual_TeleportFocus");Assert.That(focus.enabled,Is.True);
            Render(40);Assert.That(catalog.Entries.Single(e=>e.Name=="TeleportFocus").Frames.Contains(focus.sprite),Is.True);
            Render(72);Assert.That(focus==null||!focus.enabled,Is.True);
        }
        [Test] public void SurfWaveStartsNearCellsBeforeFarCells()
        {
            frame.units[0].action=CombatActionState.Idle;
            var cells=new[]{new CombatCellView{column=3,row=2},new CombatCellView{column=4,row=2},new CombatCellView{column=5,row=2}};
            frame.events=new[]{Skill("skill-dragonair-r1"),new CombatEventView{kind="SkillArea",source="caster",castId=1,tick=12,area=new CombatAreaView{cells=cells,origin=new CombatCellView{column=2,row=2},aim=cells[0]}}};
            Render(12);var waves=Images().Where(i=>i.name=="SkillVisual_SurfWave").OrderBy(i=>i.rectTransform.anchoredPosition.x).ToArray();
            Assert.That(waves.Length,Is.EqualTo(3));Assert.That(waves.Select(i=>i.enabled),Is.EqualTo(new[]{true,false,false}));
            frame.events=Array.Empty<CombatEventView>();Render(15);Assert.That(waves.Select(i=>i.enabled),Is.EqualTo(new[]{true,true,false}));
            Render(18);Assert.That(waves.All(i=>i.enabled),Is.True);Assert.That(waves[0].sprite,Is.Not.SameAs(waves[2].sprite));
            Render(40);Assert.That(Images().Any(i=>i.enabled),Is.False);
        }
        [Test] public void TeleportWithoutCastIdUsesActualDepartureAndArrivalPositions()
        {
            frame.units[0].action=CombatActionState.Idle;frame.units[0].column=5;
            var movement=new CombatMovementView{id=1500,unit="caster",kind=(int)SkillMovementKind.TeleportSafe,path=new[]{new CombatCellView{column=2,row=2},new CombatCellView{column=5,row=2}}};
            frame.events=new[]{new CombatEventView{kind="MovementStarted",movement=movement,tick=12},new CombatEventView{kind="MovementArrived",movement=movement,tick=12}};
            Render(12);
            Assert.That(Images().Single(i=>i.name=="SkillVisual_TeleportDepart").rectTransform.anchoredPosition,Is.EqualTo(Point(2,2)+Vector2.up*24));
            Assert.That(Images().Single(i=>i.name=="SkillVisual_TeleportArrive").rectTransform.anchoredPosition,Is.EqualTo(Point(5,2)+Vector2.up*24));
            frame.events=Array.Empty<CombatEventView>();Render(20);Assert.That(Images().Any(i=>i.name=="SkillVisual_TeleportMana"&&i.enabled),Is.True);
        }
        [Test] public void ConfuseRayVisitsEachActuallyConfusedTarget()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{new CombatEventView{kind="StatusApplied",detail="status-gastly-r1-s0",source="caster",target="target",sequence=1600,tick=12}};
            Render(12);Assert.That(Images().Single(i=>i.enabled).name,Is.EqualTo("SkillVisual_ConfuseRayFlight"));
            frame.events=Array.Empty<CombatEventView>();Render(19);Assert.That(Images().Single(i=>i.enabled).name,Is.EqualTo("SkillVisual_ConfuseRayImpact"));
        }
        [TestCase("pinsir",.25f,false,true)] [TestCase("pinsir",.26f,false,false)]
        [TestCase("gengar",1f,true,true)] [TestCase("gengar",1f,false,false)]
        public void PreparedTenRecordsActualExecutionAndSleepConditions(string species,float ratio,bool sleeping,bool expected)
        {
            var session=new EffectLabSession(PokeChess.Content.GameContent.Roster);session.ReplaceCaster(species);
            var target=session.Unit("target");target.SetVitals(target.Stats.MaxHP*ratio,0);
            if(sleeping)session.Battle.StatusEffects.Apply(new StatusEffectDefinition("visual-sleep",5,behavior:StatusBehavior.Sleep),session.CasterId,"target");
            session.Cast();SkillEffectEvent? hit=null;
            for(int n=0;n<100&&!hit.HasValue;n++) {
                foreach(var e in session.Battle.SkillEffectEventsThisTick)if(e.CasterId==session.CasterId&&e.Type==SkillEffectType.Damage&&e.Outcome==SkillEffectOutcome.Applied)hit=e;
                if(!hit.HasValue)session.Step();
            }
            Assert.That(hit.HasValue,Is.True);Assert.That(hit.Value.ConditionMatched,Is.EqualTo(expected));
        }
        [TestCase(.25f,true)] [TestCase(.26f,false)]
        public void HornDrillRecordsConditionBeforeDamage(float hpRatio,bool expected)
        {
            var session=new EffectLabSession(PokeChess.Content.GameContent.Roster);session.ReplaceCaster("nidorino");
            var target=session.Unit("target");target.SetVitals(target.Stats.MaxHP*hpRatio,0);session.Cast();
            SkillEffectEvent? hit=null;
            for(int n=0;n<100&&!hit.HasValue;n++) {
                foreach(var e in session.Battle.SkillEffectEventsThisTick)if(e.CasterId==session.CasterId&&e.Type==SkillEffectType.Damage&&e.Outcome==SkillEffectOutcome.Applied)hit=e;
                if(!hit.HasValue)session.Step();
            }
            Assert.That(hit.HasValue,Is.True);Assert.That(hit.Value.ConditionMatched,Is.EqualTo(expected));
        }
        [Test] public void ThrashBuffAndSixNeighborImpactsRequireRealBasicHit()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.units[0].statusDetails=new[]{new CombatStatusView{id=1200,definition="status-nidoking-r1-s0",behavior=(int)StatusBehavior.AttackSplash,startTick=0,endTick=90}};
            Render(0);Assert.That(Images().Count(i=>i.enabled),Is.EqualTo(1));
            frame.events=new[]{new CombatEventView{kind="Damage",detail="Physical",source="caster",target="target",tick=12,isBasicAttack=true}};
            Render(12);Assert.That(Images().Count(i=>i.enabled),Is.EqualTo(8));
            var target=new BoardPosition(4,2);
            for(int n=0;n<6;n++) {
                var cell=HexCoordinates.ToOffset(HexCoordinates.Neighbor(HexCoordinates.ToAxial(target),n));
                var image=Images().Single(i=>i.name=="SkillVisual_ThrashSplash"+n);
                Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(cell.Column,cell.Row)+Vector2.up*14));
            }
            frame.events=Array.Empty<CombatEventView>();Render(30);Assert.That(Images().Count(i=>i.enabled),Is.EqualTo(1));
            Render(90);Assert.That(Images().Any(i=>i.enabled),Is.False);
            frame.events=new[]{new CombatEventView{kind="Damage",detail="Physical",source="caster",target="target",tick=91,isBasicAttack=true}};
            Render(91);Assert.That(Images().Any(i=>i.enabled),Is.False);
        }
        [TestCase("nidoran-m",false,"HornAttack")]
        [TestCase("nidorino",false,"HornDrillNormal")]
        [TestCase("nidorino",true,"HornDrillFinisher")]
        public void HornsChooseExactlyOneVisualFromActualCondition(string species,bool matched,string visual)
        {
            frame.units[0].action=CombatActionState.Idle;var hit=Damage(12);hit.conditionMatched=matched;
            frame.events=new[]{Skill("skill-"+species+"-r1"),hit};Render(12);
            var image=Images().Single(i=>i.enabled);Assert.That(image.name,Is.EqualTo("SkillVisual_"+visual));
            Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(4,2)+Vector2.up*24));
            Assert.That(image.rectTransform.sizeDelta.x,Is.EqualTo(60));
            frame.events=Array.Empty<CombatEventView>();Render(40);Assert.That(Images().Any(i=>i.enabled),Is.False);
        }
        [Test] public void DoubleKickUsesSeparateFramesForEachActualHit()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{Skill("skill-nidorina-r1"),Damage(12)};Render(12);
            var first=catalog.Entries.Single(e=>e.Name=="DoubleKickFirst");
            var second=catalog.Entries.Single(e=>e.Name=="DoubleKickSecond");
            Assert.That(first.Frames.Length,Is.EqualTo(5));Assert.That(second.Frames.Length,Is.EqualTo(6));
            Assert.That(first.Frames.All(s=>s.rect.y>=64),Is.True);Assert.That(second.Frames.All(s=>s.rect.y<64),Is.True);
            Assert.That(first.Frames.Intersect(second.Frames),Is.Empty);
            Assert.That(Images().Single(i=>i.enabled).sprite,Is.SameAs(first.Frames[0]));
            frame.events=new[]{Damage(18,1)};Render(18);
            var hit=Images().Single(i=>i.enabled);Assert.That(hit.sprite,Is.SameAs(second.Frames[0]));
            Assert.That(hit.rectTransform.anchoredPosition,Is.EqualTo(Point(4,2)+Vector2.up*24));
        }
        [Test] public void BodySlamJumpRisesAboveBoardAndReturnsAtLanding()
        {
            frame.movements=new[]{new CombatMovementView{unit="caster",kind=(int)SkillMovementKind.JumpToCluster,startTick=10,departureTick=10,endTick=25,
                path=new[]{new CombatCellView{column=2,row=2},new CombatCellView{column=4,row=2}}}};
            Assert.That(ThrowPresentation.Playback(frame,"caster",10,Point(2,2),Point),Is.EqualTo(Point(2,2)));
            Assert.That(ThrowPresentation.Playback(frame,"caster",17.5,Point(2,2),Point),Is.EqualTo((Point(2,2)+Point(4,2))/2+Vector2.up*90));
            Assert.That(ThrowPresentation.Playback(frame,"caster",25,Point(4,2),Point),Is.EqualTo(Point(4,2)));
        }
        [Test] public void BodySlamShowsLandingEmptyNeighborAndActualShield()
        {
            frame.units[0].action=CombatActionState.Idle;
            var center=new CombatCellView{column=2,row=2};var neighbor=new CombatCellView{column=3,row=2};
            frame.units[0].shields=new[]{new CombatShieldView{id=1100,source="caster",group="skill-nidoqueen-r1:0:0",remaining=100,endTick=132}};
            frame.events=new[]{Skill("skill-nidoqueen-r1"),
                new CombatEventView{kind="MovementArrived",castId=1,tick=12,movement=new CombatMovementView{unit="caster",kind=(int)SkillMovementKind.JumpToCluster,path=new[]{center}}},
                new CombatEventView{kind="SkillArea",castId=1,tick=12,area=new CombatAreaView{center=center,cells=new[]{center,neighbor}}}};
            Render(12);
            var landing=Images().Single(i=>i.name=="SkillVisual_BodySlamLanding");
            Assert.That(landing.rectTransform.anchoredPosition,Is.EqualTo(Point(2,2)+Vector2.up*5));
            var outer=Images().Single(i=>i.name=="SkillVisual_BodySlamNeighbors");
            Assert.That(outer.rectTransform.anchoredPosition,Is.EqualTo(Point(3,2)+Vector2.up*5));
            var shield=Images().Single(i=>i.name=="SkillVisual_BodySlamShield");
            var entry=catalog.Entries.Single(e=>e.Name=="BodySlamShield");Assert.That(shield.sprite,Is.SameAs(entry.IntroFrames[0]));
            frame.events=Array.Empty<CombatEventView>();Render(42);Assert.That(entry.Frames.Contains(shield.sprite),Is.True);
            frame.units[0].shields=Array.Empty<CombatShieldView>();Render(43);Assert.That(shield==null||!shield.enabled,Is.True);
        }
        [Test] public void BarrierFollowsOnlyItsRealShieldsAndStopsWhenBroken()
        {
            frame.units[0].action=CombatActionState.Idle;
            for(int i=0;i<2;i++)frame.units[i].shields=new[]{new CombatShieldView{id=1010+i,source="caster",group="skill-mr-mime-r1:0:0",remaining=100,endTick=132}};
            var entry=catalog.Entries.Single(e=>e.Name=="Barrier");Render(12);Assert.That(Images().Count(i=>i.enabled&&i.sprite==entry.IntroFrames[0]),Is.EqualTo(2));
            Render(42);Assert.That(Images().Count(i=>i.enabled&&entry.Frames.Contains(i.sprite)),Is.EqualTo(2));
            frame.units[1].shields=Array.Empty<CombatShieldView>();Render(43);Assert.That(Images().Count(i=>i.enabled),Is.EqualTo(1));
            Render(132);Assert.That(Images().Any(i=>i.enabled),Is.False);
        }
        [TestCase("kangaskhan","DizzyPunch")] [TestCase("electabuzz","ThunderPunch")]
        public void PreparedPunchesPlayOnDamageTarget(string species,string visual)
        {
            frame.units[0].action=CombatActionState.Idle;frame.events=new[]{Skill("skill-"+species+"-r1"),Damage(12)};Render(12);Render(12);
            var image=Images().Single(i=>i.enabled);Assert.That(image.name,Is.EqualTo("SkillVisual_"+visual));
            Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(4,2)+Vector2.up*24));
            frame.events=Array.Empty<CombatEventView>();Render(40);Assert.That(Images().Any(i=>i.enabled),Is.False);
        }
        [Test] public void LovelyKissSequencesSleepReturnAndCasterHealing()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{Skill("skill-jynx-r1"),new CombatEventView{kind="StatusApplied",detail="status-jynx-r1-s0",source="caster",target="target",sequence=1020,tick=12},
                new CombatEventView{kind="EffectHeal",detail="Applied",source="caster",target="caster",castId=1,tick=12}};
            Render(12);Render(12);Assert.That(Images().Single(i=>i.enabled).name,Is.EqualTo("SkillVisual_LovelyKissSleep"));
            frame.events=Array.Empty<CombatEventView>();Render(24);var returning=Images().Single(i=>i.name=="SkillVisual_LovelyKissReturn");
            Assert.That(returning.enabled,Is.True);Assert.That(returning.rectTransform.anchoredPosition.x,Is.InRange(Point(2,2).x,Point(4,2).x));
            Render(32);Assert.That(returning==null||!returning.enabled,Is.True);Assert.That(Images().Any(i=>i.name=="SkillVisual_LovelyKissHeal"&&i.enabled),Is.True);
            Render(50);Assert.That(Images().Any(i=>i.enabled),Is.False);
        }
        [TestCase("aerodactyl",3,false)] [TestCase("aerodactyl",3,true)]
        [TestCase("dragonite",4,false)] [TestCase("dragonite",4,true)]
        public void HyperBeamChargesCoversFullLineAndShowsActualRecoil(string species,int count,bool left)
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{new CombatEventView{kind="SkillCastStarted",detail="skill-"+species+"-r1",source="caster",target="target",castId=1,tick=0}};
            Render(0);Assert.That(Images().Single(i=>i.enabled).name,Is.EqualTo("SkillVisual_HyperBeamCharge"));
            int origin=left?5:1;var cells=Enumerable.Range(1,count).Select(n=>new CombatCellView{column=origin+(left?-n:n),row=3}).ToArray();
            frame.units[0].statusDetails=new[]{new CombatStatusView{id=1030,definition="status-"+species+"-r1-s0",source="caster",startTick=24,endTick=54}};
            frame.events=new[]{new CombatEventView{kind="SkillArea",castId=1,tick=24,area=new CombatAreaView{cells=cells,origin=new CombatCellView{column=origin,row=3},aim=cells[0]}}};
            Render(24);Render(37);
            for(int n=1;n<=count;n++) {
                var image=Images().Single(i=>i.name=="SkillVisual_HyperBeam_"+species+"_"+n);
                Assert.That(image.enabled,Is.True);Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(cells[n-1].column,3)+Vector2.up*24));
                Assert.That(image.rectTransform.localEulerAngles.z,Is.EqualTo(left?180:0).Within(.01));
            }
            Assert.That(Images().Any(i=>i.name=="SkillVisual_HyperBeamRecoil"&&i.enabled),Is.True);
            frame.events=Array.Empty<CombatEventView>();Render(60);Assert.That(Images().Any(i=>i.enabled),Is.False);
        }
        [Test] public void ProjectileUsesResolvedCopiedSkillAndFacesBothDirections()
        {
            Render(8);var shot=Images().Single();Assert.That(shot.enabled,Is.True);
            Assert.That(shot.rectTransform.localEulerAngles.z,Is.EqualTo(0).Within(.01));
            Assert.That(shot.rectTransform.sizeDelta.x,Is.EqualTo(30));
            frame.units[1].column=0;Render(9);
            Assert.That(shot.rectTransform.localEulerAngles.z,Is.EqualTo(180).Within(.01));
            frame.units[0].action=CombatActionState.Idle;Render(10);Assert.That(Images(),Is.Empty);
        }
        [Test] public void TwoHitsUseSeparateStagesAndLatestTargetPositionWithoutDuplicates()
        {
            frame.units[0].skillId="skill-beedrill-r1";
            frame.events=new[]{Skill("skill-beedrill-r1"),Damage(12)};Render(12);
            Assert.That(Images().Single().sprite,Is.SameAs(catalog.Entries.Single(e=>e.Name=="TwineedleLeft").Frames[0]));
            frame.units[1].column=3;frame.events=new[]{Damage(18,1)};Render(18);Render(18);
            var right=Images().Single(i=>i.name=="SkillVisual_TwineedleRight");
            Assert.That(right.sprite,Is.SameAs(catalog.Entries.Single(e=>e.Name=="TwineedleRight").Frames[0]));
            Assert.That(right.rectTransform.anchoredPosition,Is.EqualTo(Point(3,2)+Vector2.up*24));
            Assert.That(Images().Length,Is.EqualTo(2));Render(27);Assert.That(Images(),Is.Empty);
        }
        [Test] public void PowderUsesExactCellsCapsOpacityAndExpires()
        {
            frame.units[0].action=CombatActionState.Idle;
            var center=new BoardPosition(3,3);
            var cells=SkillGeometry.Cells(SkillAreaShape.Radius,center,center,2).Select(p=>new CombatCellView{column=p.Column,row=p.Row}).ToArray();
            var skill=Skill("skill-butterfree-r1");
            var area=new CombatEventView{kind="SkillArea",castId=1,source="caster",tick=12,area=new CombatAreaView{cells=cells}};
            frame.events=new[]{skill,area};Render(12);Render(12);
            Assert.That(Images().Length,Is.EqualTo(cells.Length));Assert.That(cells.Length,Is.EqualTo(19));
            Assert.That(Images().All(i=>Math.Abs(i.color.a-.4f)<.001f&&!i.raycastTarget&&i.rectTransform.sizeDelta.x==30),Is.True);
            skill.castId=area.castId=2;Render(13);
            Assert.That(Images().Length,Is.EqualTo(38));
            float alpha=Images()[0].color.a;Assert.That(1-(1-alpha)*(1-alpha),Is.EqualTo(.4f).Within(.001));
            Render(43);Assert.That(Images(),Is.Empty);
        }
        [Test] public void StatusBindingFollowsUnitHoldsAndDisappearsOnRemoval()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.units[1].statusDetails=new[]{new CombatStatusView{id=7,definition="status-caterpie-r1-s0",source="caster",startTick=12,endTick=102}};
            Render(20);var web=Images().Single();Assert.That(web.rectTransform.sizeDelta.x,Is.EqualTo(48));
            frame.units[1].column=5;Render(40);
            Assert.That(web.rectTransform.anchoredPosition,Is.EqualTo(Point(5,2)+Vector2.up*30));
            Assert.That(web.sprite,Is.SameAs(catalog.Entries.Single(e=>e.Name=="StringShotBinding").Frames[3]));
            Render(101);Assert.That(web.sprite,Is.SameAs(catalog.Entries.Single(e=>e.Name=="StringShotBinding").Frames[5]));
            frame.units[1].statusDetails=Array.Empty<CombatStatusView>();Render(101.5);Assert.That(Images(),Is.Empty);
        }
        [TestCase(false)] [TestCase(true)]
        public void WingTilesFollowLineOrderDirectionAndAuthoredDelays(bool left)
        {
            var origin=new BoardPosition(3,3);var aim=new BoardPosition(left?2:4,3);
            var cells=SkillGeometry.Cells(SkillAreaShape.Line,origin,aim,3).Select(p=>new CombatCellView{column=p.Column,row=p.Row}).ToArray();
            frame.events=new[]{Skill("skill-pidgeotto-r1"),new CombatEventView {kind="SkillArea",source="caster",castId=1,tick=12,
                area=new CombatAreaView {cells=cells,origin=new CombatCellView{column=3,row=3},aim=new CombatCellView{column=aim.Column,row=3}}}};
            frame.units[0].action=CombatActionState.Idle;
            Render(12);Assert.That(Images().Length,Is.EqualTo(3));
            for(int i=0;i<3;i++)
            {
                var image=Images().Single(v=>v.name=="SkillVisual_WingAttack"+(i+1));
                Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(cells[i].column,cells[i].row)+Vector2.up*14));
                Assert.That(image.rectTransform.localEulerAngles.z,Is.EqualTo(left?180:0).Within(.01));
                Assert.That(image.enabled,Is.EqualTo(i==0));
            }
            Render(18);Assert.That(Images().Single(i=>i.name=="SkillVisual_WingAttack2").enabled,Is.True);
            Assert.That(Images().Single(i=>i.name=="SkillVisual_WingAttack3").enabled,Is.False);
            Render(24);Assert.That(Images().All(i=>i.enabled),Is.True);
            Render(54);Assert.That(Images(),Is.Empty);
        }
        [TestCase("golduck",false)] [TestCase("golduck",true)]
        [TestCase("seadra",false)] [TestCase("seadra",true)]
        [TestCase("blastoise",false)] [TestCase("blastoise",true)]
        public void HydroPumpUsesFourOrderedTilesAndMirrorsDirection(string pokemon,bool left)
        {
            frame.units[0].action=CombatActionState.Idle;
            int origin=left?5:1;
            var cells=Enumerable.Range(1,4).Select(n=>new CombatCellView{column=origin+(left?-n:n),row=3}).ToArray();
            frame.events=new[]{Skill("skill-"+pokemon+"-r1"),new CombatEventView{kind="SkillArea",castId=1,tick=12,
                area=new CombatAreaView{cells=cells,origin=new CombatCellView{column=origin,row=3},aim=cells[0]}}};
            Render(12);Render(12);Assert.That(Images().Length,Is.EqualTo(4));
            for(int n=1;n<=4;n++) {
                var image=Images().Single(i=>i.name=="SkillVisual_HydroPump"+n);
                Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(cells[n-1].column,3)+Vector2.up*5));
                Assert.That(image.rectTransform.localEulerAngles.z,Is.EqualTo(left?180:0).Within(.01));
                Assert.That(image.rectTransform.sizeDelta.x,Is.EqualTo(60));
                Assert.That(image.enabled,Is.EqualTo(n!=4));
            }
            Render(15);Assert.That(Images().All(i=>i.enabled),Is.True);
            Render(40);Assert.That(Images(),Is.Empty);
        }
        [Test] public void WingTilesStopAtBoardEdge()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{Skill("skill-pidgeotto-r1"),new CombatEventView {kind="SkillArea",castId=1,tick=12,
                area=new CombatAreaView {cells=new[]{new CombatCellView{column=6,row=3}},origin=new CombatCellView{column=5,row=3},aim=new CombatCellView{column=6,row=3}}}};
            Render(12);Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_WingAttack1"));
        }
        [TestCase(false)] [TestCase(true)]
        public void SuccessfulRetreatCopiesCurrentSpriteAndFades(bool left)
        {
            var staleNode=new GameObject("OldActor",typeof(RectTransform));staleNode.transform.SetParent(root.transform,false);
            var stale=staleNode.AddComponent<PokemonSpriteView>();stale.Initialize("caterpie","caster");stale.CharacterImage.enabled=false;
            var actorNode=new GameObject("Actor",typeof(RectTransform));actorNode.transform.SetParent(root.transform,false);
            var actor=actorNode.AddComponent<PokemonSpriteView>();actor.Initialize("pidgeot","caster");actor.ShowIdle(0);
            var source=actor.CharacterImage;int end=left?1:3;
            frame.units[0].column=end;frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{Skill("skill-pidgeot-r1"),new CombatEventView{kind="MovementArrived",source="caster",target="caster",castId=1,tick=12,
                movement=new CombatMovementView{id=7,castId=1,unit="caster",source="caster",kind=(int)SkillMovementKind.Retreat,
                    path=new[]{new CombatCellView{column=2,row=2},new CombatCellView{column=end,row=2}}}}};
            Render(12);view.CaptureAfterimages();
            var ghosts=Images().Where(i=>i.name=="SkillVisual_AgilityAfterimage").ToArray();
            Assert.That(ghosts.Length,Is.EqualTo(4));
            Assert.That(ghosts.All(i=>i.sprite==source.sprite&&i.rectTransform.sizeDelta==source.rectTransform.sizeDelta&&!i.raycastTarget),Is.True);
            Assert.That(ghosts.Select(i=>i.rectTransform.anchoredPosition).Distinct().Count(),Is.EqualTo(4));
            float alpha=ghosts[2].color.a;Render(15);view.CaptureAfterimages();
            Assert.That(ghosts[2].color.a,Is.LessThan(alpha));
            Assert.That(Images().Count(i=>i.name=="SkillVisual_AgilityAfterimage"),Is.EqualTo(4));
            Render(22);Assert.That(Images().Any(i=>i.name=="SkillVisual_AgilityAfterimage"),Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void DoduoDashFollowsPathAndLeavesCurrentSpriteAfterimages(bool left)
        {
            var node=new GameObject("Actor",typeof(RectTransform));node.transform.SetParent(root.transform,false);
            var actor=node.AddComponent<PokemonSpriteView>();actor.Initialize("doduo","caster");actor.ShowIdle(0);
            var source=actor.CharacterImage;
            int end=left?0:4;int middle=left?1:3;
            var path=new[]{new CombatCellView{column=2,row=2},new CombatCellView{column=middle,row=2},new CombatCellView{column=end,row=3}};
            frame.movements=new[]{new CombatMovementView{unit="caster",kind=(int)SkillMovementKind.DashToTarget,startTick=0,endTick=12,path=path}};
            Assert.That(ThrowPresentation.Playback(frame,"caster",6,Vector2.zero,Point),Is.EqualTo(Point(middle,2)));
            frame.movements=Array.Empty<CombatMovementView>();frame.units[0].column=end;frame.units[0].row=3;frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{Skill("skill-doduo-r1"),new CombatEventView{kind="MovementArrived",castId=1,source="caster",target="caster",tick=12,
                movement=new CombatMovementView{id=91,castId=1,source="caster",unit="caster",kind=(int)SkillMovementKind.DashToTarget,path=path}}};
            Render(12);view.CaptureAfterimages();Render(12);view.CaptureAfterimages();
            var ghosts=Images().Where(i=>i.name=="SkillVisual_DrillPeckAfterimage").ToArray();Assert.That(ghosts.Length,Is.EqualTo(4));
            Assert.That(ghosts.All(i=>i.sprite==source.sprite&&i.rectTransform.sizeDelta==source.rectTransform.sizeDelta),Is.True);
            Assert.That(ghosts.Select(i=>i.rectTransform.anchoredPosition).Distinct().Count(),Is.EqualTo(4));
            float alpha=ghosts[2].color.a;frame.events=Array.Empty<CombatEventView>();Render(15);Assert.That(ghosts[2].color.a,Is.LessThan(alpha));
            Render(22);Assert.That(Images().Any(i=>i.name=="SkillVisual_DrillPeckAfterimage"),Is.False);
        }
        [Test] public void AdjacentDoduoLungesIntoTargetAndReturnsWithoutChasingMovingVictim()
        {
            var node=new GameObject("Actor",typeof(RectTransform));node.transform.SetParent(root.transform,false);
            var actor=node.AddComponent<PokemonSpriteView>();actor.Initialize("doduo","caster");actor.ShowIdle(0);
            frame.units[0].column=3;frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{Skill("skill-doduo-r1"),new CombatEventView{kind="MovementArrived",castId=1,source="caster",target="caster",tick=12,
                movement=new CombatMovementView{id=93,castId=1,source="caster",unit="caster",kind=(int)SkillMovementKind.DashToTarget,
                    path=new[]{new CombatCellView{column=3,row=2}}}}};
            Render(12);view.CaptureAfterimages();view.UpdateLunges();
            Assert.That(Images().Count(i=>i.name=="SkillVisual_DrillPeckAfterimage"),Is.EqualTo(4));
            frame.events=Array.Empty<CombatEventView>();Render(14);view.UpdateLunges();
            Assert.That(actor.SkillLungeOffset,Is.EqualTo(Point(4,2)-Point(3,2)));
            frame.units[1].column=5;Render(15);view.UpdateLunges();
            Assert.That(actor.SkillLungeOffset,Is.EqualTo(Point(4,2)-Point(3,2)));
            Render(18);view.UpdateLunges();Assert.That(actor.SkillLungeOffset,Is.EqualTo(Vector2.zero));
        }
        [Test] public void BlockedDoduoDashDoesNotCreateAfterimages()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{Skill("skill-doduo-r1"),new CombatEventView{kind="MovementBlocked",castId=1,source="caster",target="caster",tick=12,
                movement=new CombatMovementView{id=91,unit="caster",kind=(int)SkillMovementKind.DashToTarget,path=new[]{new CombatCellView{column=2,row=2}}}}};
            Render(12);view.CaptureAfterimages();Assert.That(Images(),Is.Empty);
        }
        [Test] public void BlockedRetreatDoesNotCreateAfterimages()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{Skill("skill-pidgeot-r1"),new CombatEventView{kind="MovementBlocked",source="caster",castId=1,tick=12,
                movement=new CombatMovementView{id=7,unit="caster",kind=(int)SkillMovementKind.Retreat,path=new[]{new CombatCellView{column=2,row=2}}}}};
            Render(12);view.CaptureAfterimages();Assert.That(Images(),Is.Empty);
        }
        [TestCase("ekans","Glare")] [TestCase("sandshrew","SandAttack")] [TestCase("paras","Spore")]
        public void ControlSkillsPlayOnTheCastTarget(string pokemon,string visual)
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{Skill("skill-"+pokemon+"-r1")};Render(12);Render(12);
            var image=Images().Single(i=>i.name=="SkillVisual_"+visual);
            Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(4,2)+Vector2.up*24));
            Assert.That(image.enabled,Is.True);Render(60);Assert.That(Images(),Is.Empty);
        }
        [TestCase("sandslash")] [TestCase("parasect")] [TestCase("persian")] [TestCase("charmeleon")] [TestCase("kabutops")]
        public void SlashSharesTheImpactAcrossAllUsers(string pokemon)
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{Skill("skill-"+pokemon+"-r1"),Damage(12)};Render(12);
            Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_Slash"));
            Assert.That(Images().Single().sprite,Is.SameAs(catalog.Entries.Single(e=>e.Name=="Slash").Frames[0]));
            Render(30);Assert.That(Images(),Is.Empty);
        }
        [TestCase("arbok")] [TestCase("gloom")]
        public void AcidImpactAndDefenseLoopFollowActualHitAndStatusLifetime(string pokemon)
        {
            frame.units[0].skillId="skill-"+pokemon+"-r1";Render(8);
            Assert.That(Images().Single().name,Is.EqualTo("SkillVisual_AcidProjectile"));
            string status="status-"+pokemon+"-r1-s0";
            frame.units[1].statusDetails=new[]{new CombatStatusView{id=9,definition=status,source="caster",startTick=12,endTick=102}};
            frame.events=new[]{Skill(frame.units[0].skillId),Damage(12),new CombatEventView{kind="StatusApplied",detail=status,source="caster",target="target",sequence=9,tick=12}};
            Render(12);
            Assert.That(Images().Any(i=>i.name=="SkillVisual_AcidImpact"&&i.enabled),Is.True);
            Assert.That(Images().Any(i=>i.name=="SkillVisual_AcidDefenseBreak"&&i.enabled),Is.True);
            Assert.That(Images().Single(i=>i.name=="SkillVisual_AcidDefenseDown").enabled,Is.False);
            frame.units[1].column=5;Render(28);
            var loop=Images().Single(i=>i.name=="SkillVisual_AcidDefenseDown");
            Assert.That(loop.enabled,Is.True);Assert.That(loop.rectTransform.anchoredPosition,Is.EqualTo(Point(5,2)+Vector2.up*42));
            Render(102);Assert.That(Images(),Is.Empty);
        }
        [Test] public void ConfusionUsesTheActualDamageTarget()
        {
            frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{Skill("skill-psyduck-r1"),Damage(12)};Render(12);
            var image=Images().Single();Assert.That(image.name,Is.EqualTo("SkillVisual_ConfusionHit"));
            Assert.That(image.rectTransform.anchoredPosition,Is.EqualTo(Point(4,2)+Vector2.up*24));
            Render(40);Assert.That(Images(),Is.Empty);
        }
        [Test] public void EarthquakeCoversEachRealRadiusCellOnce()
        {
            frame.units[0].action=CombatActionState.Idle;var center=new BoardPosition(3,3);
            var cells=SkillGeometry.Cells(SkillAreaShape.Radius,center,center,2).Select(p=>new CombatCellView{column=p.Column,row=p.Row}).ToArray();
            var area=new CombatEventView{kind="SkillArea",source="caster",castId=1,tick=12,area=new CombatAreaView{cells=cells}};
            frame.events=new[]{Skill("skill-dugtrio-r1"),area,area};Render(12);
            Assert.That(Images().Length,Is.EqualTo(19));
            Assert.That(Images().All(i=>i.name=="SkillVisual_Earthquake"&&i.rectTransform.sizeDelta.x==60),Is.True);
            Render(40);Assert.That(Images(),Is.Empty);
        }
        [Test] public void DigHidesDuringBurrowAndReappearsAtSuccessfulArrival()
        {
            var actorNode=new GameObject("Actor",typeof(RectTransform));actorNode.transform.SetParent(root.transform,false);
            var actor=actorNode.AddComponent<PokemonSpriteView>();actor.Initialize("diglett","caster");actor.ShowIdle(0);
            frame.units[0].action=CombatActionState.Idle;
            frame.units[0].statusDetails=new[]{new CombatStatusView{id=11,definition="status-diglett-r1-s0",source="caster",startTick=12,endTick=42}};
            frame.events=new[]{Skill("skill-diglett-r1")};Render(12);view.UpdateSpriteVisibility();
            Assert.That(actor.CharacterImage.color.a,Is.EqualTo(1));
            Assert.That(Images().Any(i=>i.name=="SkillVisual_DigIn"&&i.enabled),Is.True);
            Render(20);view.UpdateSpriteVisibility();Assert.That(actor.CharacterImage.color.a,Is.Zero);
            frame.units[0].column=1;frame.units[0].row=3;
            frame.events=new[]{new CombatEventView{kind="MovementArrived",castId=1,stepIndex=1,tick=42,
                movement=new CombatMovementView{id=12,unit="caster",kind=(int)SkillMovementKind.BurrowToTarget,
                    path=new[]{new CombatCellView{column=2,row=2},new CombatCellView{column=1,row=3}}}}};
            Render(42);view.UpdateSpriteVisibility();Assert.That(actor.CharacterImage.color.a,Is.EqualTo(1));
            Assert.That(Images().Single(i=>i.name=="SkillVisual_DigOut").rectTransform.anchoredPosition,Is.EqualTo(Point(1,3)+Vector2.up*6));
            Assert.That(Images().Any(i=>i.name=="SkillVisual_AgilityAfterimage"),Is.False);
        }
        [Test] public void ClearingTheBattleRestoresHiddenSprites()
        {
            var actorNode=new GameObject("Actor",typeof(RectTransform));actorNode.transform.SetParent(root.transform,false);
            var actor=actorNode.AddComponent<PokemonSpriteView>();actor.Initialize("diglett","caster");actor.ShowIdle(0);
            frame.units[0].action=CombatActionState.Idle;
            frame.units[0].statusDetails=new[]{new CombatStatusView{id=11,definition="status-diglett-r1-s0",startTick=0,endTick=42}};
            Render(20);view.UpdateSpriteVisibility();Assert.That(actor.CharacterImage.color.a,Is.Zero);
            view.Configure(null);Assert.That(actor.CharacterImage.color.a,Is.EqualTo(1));
        }
        [Test] public void OneEventCanPlayDifferentEffectsOnCasterAndTarget()
        {
            custom=ScriptableObject.CreateInstance<SkillVisualCatalog>();var frames=catalog.Entries[0].Frames;
            custom.Entries=new[]{
                new SkillVisualEntry{Name="Self",SkillIds=new[]{"heal"},Trigger=SkillVisualTrigger.Heal,Placement=SkillVisualPlacement.Caster,Frames=frames},
                new SkillVisualEntry{Name="Other",SkillIds=new[]{"heal"},Trigger=SkillVisualTrigger.Heal,Placement=SkillVisualPlacement.Target,Frames=frames}
            };view.Configure(custom);frame.units[0].action=CombatActionState.Idle;
            frame.events=new[]{Skill("heal"),new CombatEventView{kind="EffectHeal",detail="Applied",source="caster",target="target",castId=1,tick=12}};Render(12);
            Assert.That(Images().Single(i=>i.name=="SkillVisual_Self").rectTransform.anchoredPosition,Is.EqualTo(Point(2,2)+Vector2.up*24));
            Assert.That(Images().Single(i=>i.name=="SkillVisual_Other").rectTransform.anchoredPosition,Is.EqualTo(Point(4,2)+Vector2.up*24));
            frame.battleId="next";frame.events=Array.Empty<CombatEventView>();Render(13);Assert.That(Images(),Is.Empty);
        }
        [Test] public void LocalTickSubscriptionCapturesBothHitsBetweenRenderedFrames()
        {
            var pokemons=new PokemonCatalog(new[]{
                new PokemonDefinition("caster","Caster",1,new PokemonStats(1000,10,0,1,0,0,3,2,100,100,.2f),"role","skill-beedrill-r1"),
                new PokemonDefinition("dummy","Dummy",1,new PokemonStats(10000,1,0,1,0,0,1,2,0,0,.2f),"role","none")
            });
            var match=MatchStateFactory.CreateWithPool("visual-local",new[]{"p1","p2"},pokemons,new[]{"caster","dummy"},matchSeed:123);
            match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);
            foreach(var player in match.Players)new ShopSystem().RefreshForRound(match,player.PlayerId);
            SharedPoolSystem.RegisterUnit(match,pokemons.CreateUnit("a","caster","p1",UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(2,0))),"caster");
            SharedPoolSystem.RegisterUnit(match,pokemons.CreateUnit("z","dummy","p2",UnitRank.One,0,UnitPlacement.OnBoard(new BoardPosition(2,0))),"dummy");
            var loop=new LocalRoundCoordinator(match,pokemons,skillCatalog:PokeChess.Content.GameContent.CreateSkills(),statusCatalog:PokeChess.Content.GameContent.CreateStatuses());
            Assert.That(loop.StartCombat(match.RoundNumber),Is.True);
            var pair=loop.GetBattleFor("p1");view.RenderLocal(pair,0,Point);
            int hits=0;
            for(int i=0;i<180&&hits<2;i++)
            {
                loop.Step();
                hits+=pair.Battle.SkillEffectEventsThisTick.Count(e=>e.CasterId=="a"&&e.SkillId=="skill-beedrill-r1"&&e.Type==SkillEffectType.Damage&&e.Outcome==SkillEffectOutcome.Applied);
            }
            Assert.That(hits,Is.EqualTo(2));
            view.RenderLocal(pair,pair.Battle.CurrentTick,Point);
            Assert.That(Images().Count(i=>i.name.StartsWith("SkillVisual_Twineedle")),Is.EqualTo(2));
        }
    }
}
