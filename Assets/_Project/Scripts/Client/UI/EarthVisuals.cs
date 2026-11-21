using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Match;
namespace PokeChess.Client.UI
{
    public sealed class EarthVisuals : ScriptableObject
    {
        public Sprite[] preview,idle,spawn,end,range,guard,shieldEnter,shieldHold,shieldBreak,shieldExpire,pulse;
        // Keep the tile center as the depth and input reference while lifting the artwork.
        internal static readonly Vector2 RockPivot = new Vector2(.5f, .36f);

        internal static UnityEngine.UI.Image Create(Transform root,string name) {
            var image=new GameObject("Earth_"+name,typeof(RectTransform),typeof(UnityEngine.UI.Image)).GetComponent<UnityEngine.UI.Image>();
            image.transform.SetParent(root,false);image.raycastTarget=false;image.enabled=false;return image;
        }
        internal static bool Draw(UnityEngine.UI.Image image,Sprite[] frames,double age,bool loop=false) {
            int index=(int)Math.Floor(age*12);
            image.enabled=frames!=null&&frames.Length>0&&index>=0&&(loop||index<frames.Length);
            if(image.enabled)image.sprite=frames[index%frames.Length];
            return image.enabled;
        }
        internal static void Release(UnityEngine.Object item){if(Application.isPlaying)UnityEngine.Object.Destroy(item);else UnityEngine.Object.DestroyImmediate(item);}
        internal static IEnumerable<BoardPosition> Cells(BoardPosition tile,bool preparation=false) {
            return HexCoordinates.Neighbors(HexCoordinates.ToAxial(tile))
                .Select(a=>HexCoordinates.TryToOffset(a,out var p)?(BoardPosition?)p:null)
                .Where(p=>p.HasValue&&(preparation?HexBoardBounds.Preparation:HexBoardBounds.Combat).Contains(p.Value)).Select(p=>p.Value);
        }
    }
    internal sealed class EarthBattleVisuals
    {
        private sealed class Tile {
            public UnityEngine.UI.Image Rock,Pulse;
            public long PulseTick=long.MinValue,Ended=long.MinValue;
        }
        private sealed class ShieldFx
        {
            public UnityEngine.UI.Image Image;
            public string Clip;
            public long Tick, End;
            public bool WasActive;
        }
        private sealed class UnitFx
        {
            public ShieldFx FirstShield, PulseShield;
            public UnityEngine.UI.Image Guard;
            public long GuardTick = long.MinValue;
            public string GuardSource;
            public bool FortressGuard;
        }
        private static readonly Color PulseTint = new Color(.77f, 1f, .73f, .9f);
        private readonly Dictionary<(int team,int column,int row),Tile> tiles=new Dictionary<(int,int,int),Tile>();
        private readonly Dictionary<(int team,int column,int row),UnityEngine.UI.Image> ranges=new Dictionary<(int,int,int),UnityEngine.UI.Image>();
        private readonly Dictionary<string,UnitFx> effects=new Dictionary<string,UnitFx>(StringComparer.Ordinal);
        private EarthVisuals art;

        public IEnumerable<RectTransform> VisibleRocks => tiles.Values
            .Where(tile => tile.Rock.enabled).Select(tile => tile.Rock.rectTransform);
        private UnitFx Unit(string id) {
            if(!effects.TryGetValue(id,out var fx))effects[id]=fx=new UnitFx();
            return fx;
        }
        private Tile Rock(Transform root,(int,int,int) key) {
            if (!tiles.TryGetValue(key, out var tile))
            {
                var rock = EarthVisuals.Create(root, "Rock");
                rock.rectTransform.pivot = EarthVisuals.RockPivot;
                rock.transform.SetAsFirstSibling();
                tile = new Tile { Rock = rock, Pulse = EarthVisuals.Create(root, "Pulse") };
                tiles.Add(key, tile);
            }
            return tile;
        }
        public void TileSignal(Transform root,string clip,int team,int column,int row,long tick) {
            if (clip != "protective-pulse") return;
            var tile=Rock(root,(team,column,row));
            if(clip=="protective-pulse")tile.PulseTick=Math.Max(tile.PulseTick,tick);
        }
        public void GuardSignal(Transform root, string source, string target, long tick, bool fortress)
        {
            if (target == null) return;
            var fx = Unit(target);
            if (tick < fx.GuardTick) return;
            fx.Guard ??= EarthVisuals.Create(root, "RangedGuard");
            fx.GuardTick = tick;
            fx.GuardSource = source;
            fx.FortressGuard = fortress;
        }

        private static ShieldFx ShieldLayer(Transform root, UnitFx unit, bool pulse)
        {
            if (pulse)
                return unit.PulseShield ??= new ShieldFx { Image = EarthVisuals.Create(root, "PulseShield") };
            return unit.FirstShield ??= new ShieldFx { Image = EarthVisuals.Create(root, "Shield") };
        }

        public void ShieldSignal(Transform root, string target, string group, string kind, float remaining, long tick)
        {
            if (target == null) return;
            string clip = kind == "Applied" || kind == "Replaced" ? "enter"
                : kind == "Absorbed" && remaining <= 0 ? "break"
                : kind == "Expired" || kind == "Removed" ? "expire" : null;
            if (clip == null) return;
            var layer = ShieldLayer(root, Unit(target), group == SynergySystem.EarthPulseShieldGroup);
            if (tick < layer.Tick) return;
            layer.Tick = tick;
            layer.Clip = clip;
        }

        private void RenderShield(Transform root, UnitFx unitFx, SkillVisualUnit unit, bool pulse, double tick, int rate, float width)
        {
            string group = pulse ? SynergySystem.EarthPulseShieldGroup : SynergyTraits.Earth;
            var shield = Array.Find(unit.Shields ?? Array.Empty<CombatShieldView>(),
                value => value.group == group && value.remaining > 0 && value.endTick > tick);
            var layer = pulse ? unitFx.PulseShield : unitFx.FirstShield;
            if (layer == null && shield == null) return;
            layer ??= ShieldLayer(root, unitFx, pulse);

            if (shield != null)
            {
                layer.End = shield.endTick;
                if (!layer.WasActive && layer.Clip != "enter")
                {
                    layer.Clip = "hold";
                    layer.Tick = (long)tick;
                }
                layer.WasActive = true;
            }
            else if (layer.WasActive)
            {
                if (layer.Clip == "hold" || layer.Clip == "enter")
                {
                    layer.Clip = tick >= layer.End ? "expire" : "break";
                    layer.Tick = (long)tick;
                }
                layer.WasActive = false;
            }

            double age = (tick - layer.Tick) / rate;
            double enterDuration = (art.shieldEnter?.Length ?? 0) / 12d;
            bool hold = shield != null && (layer.Clip == "hold" || layer.Clip == "enter" && age >= enterDuration);
            var frames = hold ? art.shieldHold : layer.Clip switch
            {
                "enter" => art.shieldEnter,
                "break" => art.shieldBreak,
                "expire" => art.shieldExpire,
                _ => null
            };
            if (hold && layer.Clip == "enter") age -= enterDuration;
            EarthVisuals.Draw(layer.Image, frames, age, hold);
            layer.Image.color = pulse ? PulseTint : Color.white;
            layer.Image.rectTransform.sizeDelta = Vector2.one * width * (pulse ? 1.45f : 1.2f);
            layer.Image.rectTransform.anchoredPosition = unit.Position + Vector2.up * width * .25f;
            layer.Image.transform.SetAsLastSibling();
        }

        private static void Hide(UnitFx fx)
        {
            if (fx.FirstShield != null) fx.FirstShield.Image.enabled = false;
            if (fx.PulseShield != null) fx.PulseShield.Image.enabled = false;
            if (fx.Guard != null) fx.Guard.enabled = false;
        }

        public void Render(Transform root,SkillVisualUnit[] units,IEnumerable<(int team,int column,int row)> rocks,bool complete,double tick,int rate,Func<int,int,Vector2> point)
        {
            if(point==null)return;if(art==null)art=Resources.Load<EarthVisuals>("EarthVisuals");if(art==null)return;
            rate=Math.Max(1,rate);float width=Vector2.Distance(point(0,0),point(1,0));
            var active=new HashSet<(int team,int column,int row)>(complete?Array.Empty<(int,int,int)>():rocks);
            foreach(var key in active) Rock(root,key);
            foreach(var entry in tiles) {
                var key=entry.Key;var tile=entry.Value;
                if(!active.Contains(key)&&tile.Ended==long.MinValue){tile.Ended=(long)tick;}
                Vector2 position=point(key.column,key.row);
                if(tile.Ended!=long.MinValue)tile.Rock.enabled=false;
                else EarthVisuals.Draw(tile.Rock,art.idle,0,true);
                tile.Rock.rectTransform.anchoredPosition=position;tile.Rock.rectTransform.sizeDelta=Vector2.one*width;
                EarthVisuals.Draw(tile.Pulse,art.pulse,tile.PulseTick==long.MinValue?-1:(tick-tile.PulseTick)/rate);
                tile.Pulse.color = PulseTint;
                if(tile.Ended!=long.MinValue)tile.Pulse.enabled=false;
                tile.Pulse.rectTransform.anchoredPosition=position;tile.Pulse.rectTransform.sizeDelta=Vector2.one*width*3;
                tile.Pulse.transform.SetAsFirstSibling();
            }
            var covered=new HashSet<(int team,int column,int row)>(active.SelectMany(r=>EarthVisuals.Cells(new BoardPosition(r.column,r.row)).Select(p=>(r.team,p.Column,p.Row))));
            foreach(var stale in ranges.Where(p=>!covered.Contains(p.Key)))stale.Value.enabled=false;
            foreach(var cell in covered) {
                if(!ranges.TryGetValue(cell,out var image))ranges[cell]=image=EarthVisuals.Create(root,"Range");
                EarthVisuals.Draw(image,art.range,tick/rate,true);
                image.rectTransform.anchoredPosition=point(cell.column,cell.row);image.rectTransform.sizeDelta=Vector2.one*width*64/50;
                image.color=cell.team==1?new Color(1,.9f,.65f,.3f):new Color(1,.65f,.35f,.3f);image.transform.SetAsFirstSibling();
            }
            foreach (var missing in effects.Where(entry => !units.Any(unit => unit.Id == entry.Key)))
                Hide(missing.Value);

            foreach (var unit in units)
            {
                if (!unit.Alive || complete)
                {
                    if (effects.TryGetValue(unit.Id, out var hidden)) Hide(hidden);
                    continue;
                }
                if (!effects.TryGetValue(unit.Id, out var fx))
                {
                    if (unit.Shields == null || !unit.Shields.Any(shield =>
                        shield.group == SynergyTraits.Earth || shield.group == SynergySystem.EarthPulseShieldGroup)) continue;
                    fx = Unit(unit.Id);
                }
                RenderShield(root, fx, unit, true, tick, rate, width);
                RenderShield(root, fx, unit, false, tick, rate, width);
                if (fx.Guard == null) continue;

                EarthVisuals.Draw(fx.Guard, art.guard, (tick - fx.GuardTick) / rate);
                fx.Guard.color = fx.FortressGuard ? PulseTint : Color.white;
                fx.Guard.rectTransform.sizeDelta = Vector2.one * width;
                fx.Guard.rectTransform.anchoredPosition = unit.Position + Vector2.up * width * .25f;
                var source = Array.Find(units, value => value.Id == fx.GuardSource);
                if (source != null)
                {
                    var delta = source.Position - unit.Position;
                    fx.Guard.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                }
                fx.Guard.transform.SetAsLastSibling();
            }
        }
        public void Clear() {
            foreach(var tile in tiles.Values){EarthVisuals.Release(tile.Rock.gameObject);EarthVisuals.Release(tile.Pulse.gameObject);}
            foreach(var image in ranges.Values)EarthVisuals.Release(image.gameObject);
            foreach (var fx in effects.Values)
            {
                if (fx.FirstShield != null) EarthVisuals.Release(fx.FirstShield.Image.gameObject);
                if (fx.PulseShield != null) EarthVisuals.Release(fx.PulseShield.Image.gameObject);
                if (fx.Guard != null) EarthVisuals.Release(fx.Guard.gameObject);
            }
            tiles.Clear();ranges.Clear();effects.Clear();
        }
    }
    public sealed class EarthRockEndView : MonoBehaviour
    {
        private UnityEngine.UI.Image image;private Sprite[] frames;private double started;
        public void Initialize(UnityEngine.UI.Image target,Sprite[] sprites,double age) {
            image=target;frames=sprites;started=Time.unscaledTimeAsDouble-age;Update();
        }
        private void Update() {
            if(image!=null&&!EarthVisuals.Draw(image,frames,Time.unscaledTimeAsDouble-started))Destroy(gameObject);
        }
    }
    internal sealed class EarthPreparationVisuals
    {
        private readonly Dictionary<BoardPosition,UnityEngine.UI.Image> rocks=new Dictionary<BoardPosition,UnityEngine.UI.Image>();
        private readonly Dictionary<BoardPosition,UnityEngine.UI.Image> ranges=new Dictionary<BoardPosition,UnityEngine.UI.Image>();
        private EarthVisuals art;

        public void SetDraggedRock(int slot, bool dragging, bool editable)
        {
            foreach (var rock in rocks.Values.Where(rock => rock.enabled))
            {
                if (rock.GetComponent<EarthRockDragToken>().Slot != slot) continue;
                rock.color = dragging ? new Color(1,1,1,.35f) : Color.white;
                rock.raycastTarget = editable && !dragging;
            }
        }

        public IEnumerable<RectTransform> VisibleRocks => rocks.Values
            .Where(rock => rock.enabled).Select(rock => rock.rectTransform);

        public void Render(Transform root,IEnumerable<BoardPosition> selected,Func<BoardPosition,Vector2> point,float width,PlacementSandboxView view,bool editable)
        {
            if(art==null)art=Resources.Load<EarthVisuals>("EarthVisuals");if(art==null)return;
            var ordered=selected.Where(HexBoardBounds.Preparation.Contains).Distinct().ToArray();
            var active=new HashSet<BoardPosition>(ordered);
            var covered=new HashSet<BoardPosition>(active.SelectMany(p=>EarthVisuals.Cells(p,true)));
            foreach(var image in rocks.Values)image.enabled=false;foreach(var image in ranges.Values)image.enabled=false;
            foreach(var tile in active) {
                if (!rocks.TryGetValue(tile, out var image))
                {
                    image = EarthVisuals.Create(root, "Preview");
                    image.rectTransform.pivot = EarthVisuals.RockPivot;
                    rocks.Add(tile, image);
                }
                EarthVisuals.Draw(image,art.idle,0,true);image.color=Color.white;
                var token=image.GetComponent<EarthRockDragToken>()??image.gameObject.AddComponent<EarthRockDragToken>();
                token.View=view;token.Slot=Array.IndexOf(ordered,tile);image.raycastTarget=editable&&!view.IsDraggingRock(token.Slot);image.color=view.IsDraggingRock(token.Slot)?new Color(1,1,1,.35f):Color.white;
                image.rectTransform.sizeDelta=Vector2.one*width;image.rectTransform.anchoredPosition=point(tile);
            }
            foreach(var tile in covered) {
                if(!ranges.TryGetValue(tile,out var image))ranges[tile]=image=EarthVisuals.Create(root,"PreviewRange");
                EarthVisuals.Draw(image,art.range,Time.unscaledTimeAsDouble,true);image.color=new Color(1,.9f,.65f,.3f);
                image.rectTransform.sizeDelta=Vector2.one*width*64/50;image.rectTransform.anchoredPosition=point(tile);
                image.transform.SetAsLastSibling();
            }
        }
    }
    public sealed partial class PlacementSandboxView
    {
        private readonly EarthPreparationVisuals earthPreparation=new EarthPreparationVisuals();
        private void RenderEarthPreparation(IEnumerable<BoardPosition> tiles) {
            if(IsEarthRockDragging && !CanContinueSpecialDrag)CancelEarthRockDrag();
            Vector2 a=FindSlot(PokeChess.Core.Pokemon.UnitPlacement.OnBoard(new BoardPosition(0,0))).Rect.anchoredPosition;
            Vector2 b=FindSlot(PokeChess.Core.Pokemon.UnitPlacement.OnBoard(new BoardPosition(1,0))).Rect.anchoredPosition;
            earthPreparation.Render(canvasRect,tiles,p=>FindSlot(PokeChess.Core.Pokemon.UnitPlacement.OnBoard(p)).Rect.anchoredPosition,Vector2.Distance(a,b),this,CanMoveSynergyPlacement);
        }
    }

}
