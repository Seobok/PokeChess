using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView : MonoBehaviour
    {
        private sealed class Slot
        {
            public UnitPlacement Placement;
            public RectTransform Rect;
            public UnityEngine.UI.Graphic Graphic;
            public Color Color;
        }
        private readonly List<Slot> slots=new List<Slot>();
        private readonly Dictionary<string,RectTransform> tokens=new Dictionary<string,RectTransform>();
        private readonly Dictionary<string,CanvasGroup> groups=new Dictionary<string,CanvasGroup>();
        private readonly Dictionary<string,UnityEngine.UI.Text> tokenLabels=new Dictionary<string,UnityEngine.UI.Text>();
        private PokemonCatalog catalog;
        public PurchaseResult LastPurchase { get; private set; }
        private readonly PlayerPlacementSystem placements=new PlayerPlacementSystem();
        private RectTransform canvasRect,ghost;
        private UnityEngine.UI.Text header,status,ghostLabel;
        private Font font;
        private string dragged;
        private long dragRevision;
        public MatchState Match { get; private set; }
        public PlayerState Player => Match.GetPlayer("p1");
        public bool IsDragging => dragged!=null;
        public string LastFeedback { get; private set; }
        public PlacementCommandResult LastResult { get; private set; }
        private static Color Navy => new Color(.09f,.14f,.21f);
        private void Awake() { BuildUI();ResetMatchUI(); }
        private void Update()
        {
            var keyboard=Keyboard.current;
            if(keyboard!=null && keyboard.escapeKey.wasPressedThisFrame) CancelDrag("Drag cancelled.");
            if(keyboard!=null && keyboard.spaceKey.wasPressedThisFrame) AdvanceRound();
            if(IsDragging)
            {
                var source=Player.Units.FirstOrDefault(u=>u.InstanceId==dragged);
                if(source==null || Player.PlacementRevision!=dragRevision || (Match.Phase!=MatchPhase.Preparation && (Match.Phase!=MatchPhase.Combat || source.Placement.Kind!=PlacementKind.Bench)) || Player.HP==0 || Player.IsEliminated) CancelDrag("Placement changed or locked.");
            }
            UpdateTokenFeedback();AnimateShop();UpdateRoundLoop();
        }
        private void OnApplicationFocus(bool focus) { if(!focus && canvasRect!=null && IsDragging) CancelDrag("Drag cancelled."); }
        private void OnDisable() { if(canvasRect!=null && IsDragging) CancelDrag("Drag cancelled."); }
        private RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position)
        {
            var obj=new GameObject(name,typeof(RectTransform));var rect=obj.GetComponent<RectTransform>();
            rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);
            rect.sizeDelta=size;rect.anchoredPosition=position;return rect;
        }
        private UnityEngine.UI.Text Label(string name,Transform parent,string text,int size,Vector2 dimensions,Vector2 position)
        {
            var rect=Rect(name,parent,dimensions,position);var label=rect.gameObject.AddComponent<UnityEngine.UI.Text>();
            label.font=font;label.fontSize=size;label.color=Color.white;label.alignment=TextAnchor.MiddleCenter;
            label.raycastTarget=false;label.text=text;return label;
        }
        private void BuildUI()
        {
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            canvasRect=Rect("PlacementCanvas",transform,new Vector2(1280,720),Vector2.zero);
            var canvas=canvasRect.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;
            var scaler=canvasRect.gameObject.AddComponent<UnityEngine.UI.CanvasScaler>();scaler.uiScaleMode=UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution=new Vector2(1280,720);scaler.screenMatchMode=UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand;
            canvasRect.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var background=Rect("Background",canvasRect,Vector2.zero,Vector2.zero);background.anchorMin=Vector2.zero;background.anchorMax=Vector2.one;
            background.offsetMin=background.offsetMax=Vector2.zero;background.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(.035f,.06f,.10f);
            Label("Title",canvasRect,"POKECHESS",16,new Vector2(250,30),new Vector2(-510,305));
            header=Label("State",canvasRect,"",18,new Vector2(730,35),new Vector2(0,305));
            for(int row=0;row<4;row++) for(int column=0;column<7;column++)
            {
                var rect=Rect("Board_"+column+"_"+row,canvasRect,new Vector2(74,86),new Vector2(-250+column*78+(row%2)*39,15+row*64));
                var graphic=rect.gameObject.AddComponent<PlacementHexGraphic>();graphic.color=Navy;
                slots.Add(new Slot{Placement=UnitPlacement.OnBoard(new BoardPosition(column,row)),Rect=rect,Graphic=graphic,Color=Navy});
                Label("Coordinate",rect,column+","+row,11,new Vector2(60,16),new Vector2(0,-27)).color=new Color(.55f,.65f,.74f);
            }
            Label("BenchTitle",canvasRect,"BENCH  /  9 SLOTS",16,new Vector2(600,30),new Vector2(0,-54));
            for(int i=0;i<9;i++)
            {
                var rect=Rect("Bench_"+i,canvasRect,new Vector2(64,60),new Vector2(-280+i*70,-107));
                var graphic=rect.gameObject.AddComponent<UnityEngine.UI.Image>();graphic.color=Navy;
                slots.Add(new Slot{Placement=UnitPlacement.OnBench(i),Rect=rect,Graphic=graphic,Color=Navy});
                Label("Index",rect,"B"+i,11,new Vector2(70,16),new Vector2(0,-24)).color=new Color(.55f,.65f,.74f);
            }
            foreach(var id in new[]{"A","B","C","D"}) CreateToken(id);
            BuildMinimumUI();
            ghost=Rect("DragPreview",canvasRect,new Vector2(66,48),Vector2.zero);
            ghost.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(1,.78f,.36f,.85f);
            var group=ghost.gameObject.AddComponent<CanvasGroup>();group.blocksRaycasts=false;group.interactable=false;
            ghostLabel=Label("Name",ghost,"",20,ghost.sizeDelta,Vector2.zero);ghostLabel.color=Color.black;ghost.gameObject.SetActive(false);
            if(EventSystem.current==null)
            {
                var system=new GameObject("PlacementEventSystem",typeof(EventSystem));system.transform.SetParent(transform,false);
                system.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
        }
        public void ResetSandbox()
        {
            CancelDrag("Reset.");
            var definitions=CreateSandboxDefinitions();
            catalog=new PokemonCatalog(definitions);LastPurchase=null;Match=MatchStateFactory.CreateWithPool("placement",new[]{"p1","p2"},catalog,definitions.Select(d=>d.Id),matchSeed:123);
            Player.SetProgress(5,0,2); // Fixture level: demonstrate board/bench swaps at full capacity.
            var initial=new[]{UnitPlacement.OnBoard(new BoardPosition(2,0)),UnitPlacement.OnBoard(new BoardPosition(4,1)),UnitPlacement.OnBench(0),UnitPlacement.OnBench(8)};
            for(int i=0;i<4;i++) SharedPoolSystem.RegisterUnit(Match,catalog.CreateUnit(((char)('A'+i)).ToString(),definitions[i].Id,"p1",UnitRank.One,0,initial[i]),definitions[i].Id);
            Match.TransitionTo(MatchPhase.Starting);Match.TransitionTo(MatchPhase.Preparation);AttachMinimumMatch();LastResult=null;Feedback("Ready. Board is full: swap a bench token with a board token, or move a board token to the bench.");Render();
        }
        private void CreateToken(string id)
        {
            var rect=Rect("Unit_"+id,canvasRect,new Vector2(60,44),Vector2.zero);
            rect.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color(.88f,.63f,.22f);
            groups[id]=rect.gameObject.AddComponent<CanvasGroup>();tokens[id]=rect;
            var drag=rect.gameObject.AddComponent<PlacementDragToken>();drag.View=this;drag.UnitId=id;
            tokenLabels[id]=Label("Name",rect,id,15,rect.sizeDelta,Vector2.zero);tokenLabels[id].color=new Color(.08f,.11f,.16f);
        }
        public void ResetRankDemo()
        {
            CancelDrag("Reset rank demo.");LastPurchase=null;
            catalog=new PokemonCatalog(CreateSandboxDefinitions());
            Match=MatchStateFactory.CreateWithPool("rank-demo",new[]{"p1","p2"},catalog,new[]{"bulbasaur"},matchSeed:123);
            Player.SetProgress(20,0,1);
            var initial=new[]{UnitPlacement.OnBoard(new BoardPosition(2,0)),UnitPlacement.OnBench(0),UnitPlacement.OnBench(1),UnitPlacement.OnBench(2)};
            for(int i=0;i<4;i++) SharedPoolSystem.RegisterUnit(Match,catalog.CreateUnit(((char)('A'+i)).ToString(),"bulbasaur","p1",i<2 ? UnitRank.Two : UnitRank.One,0,initial[i],i==1 ? new[]{"demo-item"} : null),"bulbasaur");
            Match.TransitionTo(MatchPhase.Starting);Match.TransitionTo(MatchPhase.Preparation);new ShopSystem().RefreshForRound(Match,"p1");AttachMinimumMatch();
            Feedback("Rank demo ready. Buy a shop card: R1 -> R2 -> R3; board A survives and B's item returns.");Render();
        }
        public void BuyCopy()
        {
            int slot=Enumerable.Range(0,ShopRules.SlotCount).FirstOrDefault(i=>!Player.Shop.Slots[i].IsEmpty);
            if(Player.Shop.Slots.All(s=>s.IsEmpty)) { LastPurchase=null;Feedback("No offers left. Reset Rank demo.");return; }
            BuySlot(slot);
        }
        public void ChangeLevel(int delta)
        {
            CancelDrag("Level changed.");int level=Mathf.Clamp(Player.Level+delta,1,Player.Rules.MaxLevel);
            try { Player.SetProgress(Player.Gold,0,level);Feedback("Level "+level+"."); }
            catch(InvalidOperationException) { Feedback("Rejected: deployed units exceed the requested level."); }
            Render();
        }
        public void TogglePhase()
        {
            automaticRoundFlow=false; // Placement-only fixtures deliberately have no battle simulation.
            CancelDrag("Phase changed.");
            if(Match.Phase==MatchPhase.Preparation) Match.TransitionTo(MatchPhase.Combat);
            else { Match.TransitionTo(MatchPhase.Result);Match.TransitionTo(MatchPhase.Preparation);new ShopSystem().RefreshForRound(Match,"p1"); }
            Feedback(Match.Phase==MatchPhase.Preparation ? "Preparation: placement enabled." : "Combat: bench move / swap enabled; board locked. This scene tests input only.");Render();
        }
        private Slot FindSlot(UnitPlacement placement) => slots.First(s=>s.Placement.Kind==placement.Kind &&
            Nullable.Equals(s.Placement.Position,placement.Position) && s.Placement.BenchSlot==placement.BenchSlot);
        private Slot Hit(Vector2 screenPoint)
        {
            foreach(var slot in slots)
            {
                if(!RectTransformUtility.RectangleContainsScreenPoint(slot.Rect,screenPoint,null)) continue;
                var hex=slot.Graphic as PlacementHexGraphic;
                if(hex==null || hex.IsRaycastLocationValid(screenPoint,null)) return slot;
            }
            return null;
        }
        public Vector2 ScreenPoint(UnitPlacement placement) => RectTransformUtility.WorldToScreenPoint(null,FindSlot(placement).Rect.position);
        public PlacementDragToken Token(string id) => tokens[id].GetComponent<PlacementDragToken>();
        public bool TokenMatchesState(string id) => Vector2.Distance(tokens[id].anchoredPosition,FindSlot(Player.GetUnit(id).Placement).Rect.anchoredPosition)<.1f;
        private void Feedback(string message) { LastFeedback=message;if(status!=null) status.text=message; }
        private void Render()
        {
            foreach(var slot in slots) slot.Graphic.color=BaseSlotColor(slot);
            foreach(var unit in Player.Units) if(!tokens.ContainsKey(unit.InstanceId)) CreateToken(unit.InstanceId);
            foreach(var pair in tokens)
            {
                var unit=Player.Units.FirstOrDefault(u=>u.InstanceId==pair.Key);pair.Value.gameObject.SetActive(unit!=null && unit.Placement.Kind!=PlacementKind.Unplaced);
                if(unit!=null && unit.Placement.Kind!=PlacementKind.Unplaced) { pair.Value.anchoredPosition=FindSlot(unit.Placement).Rect.anchoredPosition;tokenLabels[pair.Key].text=(unit.DefinitionId.StartsWith("sample-c",StringComparison.Ordinal) ? "C"+catalog.Get(unit.DefinitionId).Cost : unit.DefinitionId.Substring(0,Math.Min(3,unit.DefinitionId.Length)).ToUpperInvariant())+" R"+(int)unit.Rank; }
                groups[pair.Key].alpha=pair.Key==dragged ? .3f : 1;
            }
            RenderMinimumUI();
        }
        public void BeginDrag(string id,Vector2 point)
        {
            if(Simulation?.Status==SimulationStatus.Running)return;
            if(RoundLoop.NextPreparationPending) { Feedback("Finish shop refresh before placement.");return; }
            if(IsDragging) CancelDrag("New drag.");
            var unit=Player.GetUnit(id);var preview=placements.Preview(Match,"p1",id,unit.Placement,Player.PlacementRevision);
            if(!preview.Accepted) { LastResult=preview;Feedback("Rejected: "+preview.Reason);Render();return; }
            dragged=id;dragRevision=Player.PlacementRevision;ghostLabel.text=id;ghost.gameObject.SetActive(true);ghost.SetAsLastSibling();Render();Drag(point);
        }
        public void Drag(Vector2 point)
        {
            if(!IsDragging) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,point,null,out var local);ghost.anchoredPosition=local;
            foreach(var slot in slots) slot.Graphic.color=BaseSlotColor(slot);var destination=Hit(point);
            shopFrame.GetComponent<UnityEngine.UI.Image>().color=new Color(.065f,.105f,.16f);
            if(HitShop(point)) { PreviewShopSale();return; }
            if(destination==null) { Feedback("Outside board / bench / shop: release to cancel.");return; }
            var preview=placements.Preview(Match,"p1",dragged,destination.Placement,dragRevision);
            destination.Graphic.color=preview.Accepted ? (preview.Outcome==PlacementOutcome.Swapped ? new Color(.45f,.31f,.10f) : new Color(.08f,.36f,.29f)) : new Color(.48f,.12f,.17f);
            Feedback(preview.Accepted ? preview.Outcome+ (preview.TargetUnitId==null ? "" : " with "+preview.TargetUnitId) : "Rejected: "+preview.Reason);
        }
        public void EndDrag(Vector2 point)
        {
            if(!IsDragging) return;
            if(HitShop(point))
            {
                string id=dragged;long revision=dragRevision;LastResult=null;Perform(()=>commands.Sell(id,revision));return;
            }
            var destination=Hit(point);
            if(destination!=null) { LastResult=placements.Move(Match,"p1",dragged,destination.Placement,dragRevision);Feedback(LastResult.Accepted ? LastResult.Outcome.ToString() : "Rejected: "+LastResult.Reason); }
            else { LastResult=null;Feedback("Cancelled: dropped outside board / bench."); }
            dragged=null;ghost.gameObject.SetActive(false);Render();Match.Pool.AssertConservation(Match);
        }
        public void CancelDrag(string message)
        {
            dragged=null;if(ghost!=null) ghost.gameObject.SetActive(false);
            if(Match!=null) Render();Feedback(message);
        }
    }
}

