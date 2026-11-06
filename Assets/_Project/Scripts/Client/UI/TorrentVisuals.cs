using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace PokeChess.Client.UI
{
    public sealed class TorrentVisuals : ScriptableObject
    {
        [SerializeField] private Sprite[] flood, ebb, impact, mana, shield, surge, field, fieldEbb, marker, arrow, guide;

        internal Sprite[] Frames(string clip)
        {
            switch(clip)
            {
                case "flood": return flood;
                case "ebb": return ebb;
                case "impact": return impact;
                case "mana": return mana;
                case "shield": return shield;
                case "marker": return marker;
                case "arrow": return arrow;
                case "guide": return guide;
                case "surge": return surge;
                case "field": return field;
                case "field-ebb": return fieldEbb;
                default: return null;
            }
        }
    }

    internal sealed class TorrentBattleVisuals
    {
        private readonly Dictionary<(string clip,string unit,int column,int row,int team),(Image image,long start)> effects =
            new Dictionary<(string,string,int,int,int),(Image,long)>();
        private const double GuideArrivalSeconds=.5;
        private const double FieldRowSeconds=.055;
        private const int FieldIntroFrame=6;
        private const int FieldFoamFrame=8;
        private const int SurgeFoamFrame=7;
        private const int FieldGuideFrame=9;
        private static int RowOrder(int row,int team,bool returning) => (team==2)^returning?7-row:row;
        private readonly Dictionary<string,long> guideStarts=new Dictionary<string,long>();
        private readonly Dictionary<(int column,int row,int team),(int column,int row)> arrowDestinations =
            new Dictionary<(int,int,int),(int,int)>();
        private readonly Dictionary<string,double> guideDelays=new Dictionary<string,double>();
        private TorrentVisuals catalog;

        public void Tile(Transform root,string clip,int column,int row,int team,long tick,string target,(int column,int row)? destination)
        {
            if(clip=="field"||clip=="field-ebb"||clip=="surge")
            {
                for(int y=0;y<8;y++)
                    for(int x=0;x<7;x++)Trigger(root,(clip,null,x,y,team),tick);
                return;
            }
            if(clip=="impact"||clip=="guide")Trigger(root,("marker",null,column,row,team),tick);
            if(clip=="arrow")
            {
                if(!destination.HasValue)return;
                arrowDestinations[(column,row,team)]=destination.Value;
            }
            if(clip=="guide"&&target!=null)
            {
                if(!guideStarts.TryGetValue(target,out long previous)||tick>previous)
                {
                    guideStarts[target]=tick;
                    bool fieldEbb=effects.TryGetValue(("field-ebb",null,column,row,team),out var wave)&&wave.start==tick;
                    guideDelays[target]=fieldEbb?RowOrder(row,team,true)*FieldRowSeconds+(FieldGuideFrame-FieldIntroFrame)/12d:0;
                }
            }
            if(clip=="flood"||clip=="ebb")
            {
                if(clip=="ebb"&&effects.TryGetValue(("field-ebb",null,0,0,team),out var fieldWave)&&fieldWave.start==tick)return;
                for(int lane=0;lane<7;lane++)Trigger(root,(clip,null,lane,row,team),tick);
                return;
            }
            Trigger(root,(clip,clip=="guide"?target:null,column,row,team),tick);
        }

        public void Mana(Transform root,string unit,long tick)
            =>Trigger(root,("mana",unit,0,0,0),tick);

        private void Trigger(Transform root,(string clip,string unit,int column,int row,int team) key,long tick)
        {
            if(effects.TryGetValue(key,out var previous))
            {
                if(tick>previous.start)effects[key]=(previous.image,tick);
                return;
            }
            var image=new GameObject("Torrent_"+key.clip,typeof(RectTransform),typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(root,false);
            image.raycastTarget=false;
            image.enabled=false;
            effects.Add(key,(image,tick));
        }

        public void Render(Transform root,SkillVisualUnit[] units,Func<int,int,Vector2> point,double tick,int rate)
        {
            if(point==null)return;
            foreach(var unit in units)
            {
                var shield=unit.Alive?unit.Shields.FirstOrDefault(s=>s.group=="torrent"&&s.remaining>0&&s.endTick>tick):null;
                if(shield!=null)Trigger(root,("shield",unit.Id,0,0,0),0);
            }
            if(effects.Count==0)return;
            if(catalog==null)catalog=Resources.Load<TorrentVisuals>("TorrentVisuals");
            if(catalog==null)return;
            var forward=point(3,7)-point(3,0);
            float width=Vector2.Distance(point(0,0),point(1,0));
            var groundOffset=SkillVisualBattleView.GroundOffset(point);
            foreach(var entry in effects)
            {
                var (clip,id,column,row,team)=entry.Key;
                var (image,start)=entry.Value;
                var frames=catalog.Frames(clip);
                var unit=id==null?null:Array.Find(units,u=>u.Id==id&&u.Alive);
                bool shield=clip=="shield", marker=clip=="marker", arrow=clip=="arrow";
                double elapsed=(tick-start)/Math.Max(1,rate);
                bool field=clip=="field"||clip=="field-ebb"||clip=="surge";
                if(field)elapsed-=RowOrder(row,team,clip=="field-ebb")*FieldRowSeconds;
                double guideDelay=0;
                if(clip=="guide" && effects.TryGetValue(("field-ebb",null,column,row,team),out var ebbWave)&&ebbWave.start==start)
                    guideDelay=RowOrder(row,team,true)*FieldRowSeconds+(FieldGuideFrame-FieldIntroFrame)/12d;
                if(clip=="guide")elapsed-=guideDelay;
                if(clip=="mana")elapsed-=GuideArrivalSeconds+(guideDelays.TryGetValue(id,out double manaDelay)?manaDelay:0);
                if(clip=="impact")
                {
                    bool surgeHit=effects.TryGetValue(("surge",null,column,row,team),out var surgeWave)&&surgeWave.start==start;
                    bool fieldHit=effects.TryGetValue(("field",null,column,row,team),out var crest)&&crest.start==start;
                    if(surgeHit||fieldHit)
                        elapsed-=RowOrder(row,team,false)*FieldRowSeconds+((surgeHit?SurgeFoamFrame:FieldFoamFrame)-FieldIntroFrame)/12d;
                }
                int frame=(int)Math.Floor(elapsed*12)+(field?FieldIntroFrame:0);
                bool pending=shield&&guideStarts.TryGetValue(id,out long guideTick)&&
                    tick>=guideTick&&(tick-guideTick)/Math.Max(1,rate)<GuideArrivalSeconds+guideDelays[id];
                image.enabled=frames!=null&&frames.Length>0&&frame>=0&&elapsed>=0&&!pending&&(id==null||unit!=null)&&
                    (shield?unit.Shields.Any(s=>s.group=="torrent"&&s.remaining>0&&s.endTick>tick):
                    marker?elapsed<.7:arrow?elapsed<.45:frame<frames.Length);
                if(!image.enabled)continue;
                image.sprite=frames[frame%frames.Length];
                var rect=image.rectTransform;
                bool laneWave=clip=="flood"||clip=="ebb";
                bool directed=laneWave||field;
                var direction=team==2?-forward:forward;
                if(clip=="field-ebb")direction=-direction;
                var cell=point(column,row);
                if(arrow)
                {
                    var destination=arrowDestinations[(column,row,team)];
                    direction=point(destination.column,destination.row)-cell;
                }
                bool guide=clip=="guide";
                if(guide)direction=(unit?.Position??cell)-point(3,team==1?2:5);
                if(direction.sqrMagnitude<.01f)direction=-forward;
                float angle=Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg;
                rect.localRotation=Quaternion.Euler(0,0,directed||arrow||guide?angle+(clip=="field-ebb"?-90:field?90:0):0);
                rect.anchoredPosition=guide?(unit?.Position??cell)+groundOffset:
                    unit!=null?unit.Position+Vector2.up*width*.3f:
                    cell+groundOffset+
                    (arrow?direction.normalized*width*.32f:Vector2.zero);
                rect.sizeDelta=Vector2.one*width*(directed?2.5f:1.25f);
                if(clip=="impact")rect.sizeDelta*=Mathf.Lerp(1.45f,1f,Mathf.Clamp01((float)elapsed/.25f));
                if(field)
                {
                    rect.anchoredPosition=cell+groundOffset;
                    rect.sizeDelta=new Vector2(width*1.2f,width*1.25f);
                }
                if(laneWave)
                {
                    bool returning=clip=="ebb";
                    int from=team==2?7:0;
                    if(returning)from=7-from;
                    int lane=column;
                    float progress=Mathf.Clamp01((float)(elapsed*12/(frames.Length-1)));
                    float rowProgress=progress*7;
                    int segment=Mathf.Min(6,Mathf.FloorToInt(rowProgress));
                    int step=from==0?1:-1;
                    var startCell=point(lane,from+step*segment);
                    var endCell=point(lane,from+step*(segment+1));
                    rect.sizeDelta=new Vector2(width*1.8f,width*2.8f);

                    // The art already moves inside its 64px frame. Keep its crest on the board path.
                    float artProgress=(frame%frames.Length)/(float)(frames.Length-1);
                    Vector2 artOffset=Vector2.right*(returning?Mathf.Lerp(17,-17,artProgress):Mathf.Lerp(-16,16,artProgress))/64f;
                    var projectedOffset=(Vector2)(rect.localRotation*Vector2.Scale(artOffset,rect.sizeDelta));
                    rect.anchoredPosition=Vector2.Lerp(startCell,endCell,rowProgress-segment)+groundOffset-projectedOffset;
                }
            }
        }

        public IEnumerable<RectTransform> VisibleLayers => effects.Values
            .Where(effect=>effect.image.enabled).Select(effect=>effect.image.rectTransform);

        public void Clear()
        {
            foreach(var entry in effects.Values)
            {
                entry.image.enabled=false;
                UnityEngine.Object.Destroy(entry.image.gameObject);
            }
            effects.Clear();
            guideStarts.Clear();
            guideDelays.Clear();
            arrowDestinations.Clear();
        }
    }
}
