using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using UnityEngine;
using UnityEngine.UI;

namespace PokeChess.Client.UI
{
    public sealed class StormVisuals : ScriptableObject
    {
        public Sprite[] link, current, discharge, charge;
        public Sprite[] badgeTwo, badgeThree, ready, spent, slowEnter, slowHold, slowExit;

        internal static Image Create(Transform root,string name)
        {
            var image=new GameObject("Storm_"+name,typeof(RectTransform),typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(root,false);
            image.raycastTarget=false;
            image.enabled=false;
            return image;
        }

        internal static void Line(Image image,Vector2 from,Vector2 to,float height,float span)
        {
            var delta=to-from;
            var rect=image.rectTransform;
            rect.anchoredPosition=(from+to)*.5f;
            rect.sizeDelta=new Vector2(delta.magnitude*64/span,height);
            rect.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg);
        }
    }

    internal sealed class StormBattleVisuals
    {
        private sealed class Arc { public UnityEngine.UI.Image Image; public long Tick; public float Delay,Strength=1; }
        private sealed class Mark { public UnityEngine.UI.Image Image; public long Start,End; }
        private sealed class TeamHud {
            public RectTransform Root; public UnityEngine.UI.Image Gauge,Flash;
            public UnityEngine.UI.Text Label; public long Burst=long.MinValue;
        }
        private readonly Dictionary<(string clip,string from,string to),Arc> arcs=new Dictionary<(string,string,string),Arc>();
        private readonly Dictionary<string,Mark> badges=new Dictionary<string,Mark>();
        private readonly Dictionary<string,Mark> slows=new Dictionary<string,Mark>();
        private readonly Dictionary<int,TeamHud> teams=new Dictionary<int,TeamHud>();
        private readonly Dictionary<string,long> bursts=new Dictionary<string,long>();
        private StormVisuals art;
        public void Signal(Transform root,string clip,string from,string to,int count,long tick)
        {
            if(from==null||to==null)return;
            if(clip!="ARC"&&clip!="CHAIN"&&clip!="DISCHARGE")return;
            var key=(clip,from,to);
            if(!arcs.TryGetValue(key,out var arc))arcs.Add(key,arc=new Arc{Image=StormVisuals.Create(root,clip),Tick=long.MinValue});
            if(tick<=arc.Tick)return;
            arc.Tick=tick;arc.Delay=0;arc.Strength=1;
            if(clip=="CHAIN") {
                var parent=arcs.Where(p=>p.Key.to==from&&p.Key.clip!="DISCHARGE"&&p.Value.Tick==tick)
                    .OrderByDescending(p=>p.Value.Delay).Select(p=>p.Value).FirstOrDefault();
                arc.Delay=(parent?.Delay??0)+1f/12;arc.Strength=(parent?.Strength??1)*.7f;
            }
            if(clip=="DISCHARGE")bursts[from]=tick;
        }
        private static bool Frame(UnityEngine.UI.Image image,Sprite[] frames,int index)
        {
            image.enabled=frames!=null&&index>=0&&index<frames.Length&&frames[index]!=null;
            if(image.enabled)image.sprite=frames[index];
            return image.enabled;
        }
        private static Mark Get(Dictionary<string,Mark> list,string id,Transform root,string name)
        {
            if(!list.TryGetValue(id,out var mark))list.Add(id,mark=new Mark{Image=StormVisuals.Create(root,name),End=long.MinValue});
            return mark;
        }
        private TeamHud Hud(Transform root,int team)
        {
            if(teams.TryGetValue(team,out var hud))return hud;
            var background=StormVisuals.Create(root,"Team"+team+"Hud");background.enabled=true;background.color=new Color(.035f,.08f,.13f,.9f);
            var rect=background.rectTransform;rect.anchorMin=rect.anchorMax=rect.pivot=Vector2.one;
            rect.sizeDelta=new Vector2(145,58);rect.anchoredPosition=new Vector2(-8-(team-1)*153,-4);
            var label=new GameObject("Label",typeof(RectTransform),typeof(UnityEngine.UI.Text)).GetComponent<UnityEngine.UI.Text>();
            label.transform.SetParent(rect,false);label.raycastTarget=false;
            label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.fontSize=12;label.alignment=TextAnchor.MiddleCenter;label.color=new Color(.65f,.9f,1);
            label.rectTransform.anchorMin=label.rectTransform.anchorMax=Vector2.one;
            label.rectTransform.sizeDelta=new Vector2(143,20);label.rectTransform.anchoredPosition=new Vector2(-72.5f,-44);
            hud=new TeamHud{Root=rect,Label=label,Gauge=StormVisuals.Create(rect,"Gauge"),Flash=StormVisuals.Create(rect,"Burst")};
            hud.Gauge.rectTransform.anchorMin=hud.Gauge.rectTransform.anchorMax=Vector2.one;
            hud.Gauge.rectTransform.anchoredPosition=new Vector2(-72.5f,-16);hud.Gauge.rectTransform.sizeDelta=new Vector2(110,64);
            hud.Flash.rectTransform.anchorMin=hud.Flash.rectTransform.anchorMax=Vector2.one;
            hud.Flash.rectTransform.anchoredPosition=new Vector2(-72.5f,-16);hud.Flash.rectTransform.sizeDelta=new Vector2(145,96);
            teams.Add(team,hud);return hud;
        }
        public void Render(Transform root,SkillVisualUnit[] units,double tick,int rate,Func<int,int,Vector2> point)
        {
            if(point==null)return;
            if(art==null)art=Resources.Load<StormVisuals>("StormVisuals");
            if(art==null)return;
            rate=Math.Max(1,rate);float width=Vector2.Distance(point(0,0),point(1,0));
            float readySeconds=(art.ready?.Length??0)/12f;
            foreach(var entry in arcs) {
                var key=entry.Key;var arc=entry.Value;
                var from=Array.Find(units,u=>u.Id==key.from);var to=Array.Find(units,u=>u.Id==key.to);
                bool discharge=key.clip=="DISCHARGE";
                int frame=(int)Math.Floor(((tick-arc.Tick)/rate-(discharge?readySeconds:arc.Delay))*12);
                if(!Frame(arc.Image,discharge?art.discharge:art.current,frame)||from==null||to==null){arc.Image.enabled=false;continue;}
                var offset=Vector2.up*width*.25f;
                StormVisuals.Line(arc.Image,from.Position+offset,to.Position+offset,width*(discharge?1.35f:.85f)*Mathf.Sqrt(arc.Strength),48);
                arc.Image.color=new Color(1,1,1,Mathf.Lerp(.45f,1,arc.Strength));arc.Image.transform.SetAsLastSibling();
            }
            foreach(var mark in badges.Values)mark.Image.enabled=false;
            foreach(var unit in units) {
                if(!unit.Alive)continue;
                if(unit.StormBonus>0) {
                    var mark=Get(badges,unit.Id,root,"CircuitBadge");
                    var frames=unit.StormBonus>=.25f?art.badgeThree:art.badgeTwo;
                    if(frames!=null&&frames.Length>0)Frame(mark.Image,frames,(int)Math.Floor(tick/rate*12)%frames.Length);
                    mark.Image.rectTransform.sizeDelta=Vector2.one*28;
                    mark.Image.rectTransform.anchoredPosition=unit.Position+new Vector2(width*.42f,width*.75f);
                    mark.Image.transform.SetAsLastSibling();
                }
                var status=unit.Statuses?.Where(s=>s.definition=="synergy-storm-overcharge"&&s.startTick<=tick&&s.endTick>tick)
                    .OrderByDescending(s=>s.endTick).FirstOrDefault();
                if(status!=null) {
                    var mark=Get(slows,unit.Id,root,"Slow");
                    mark.Start=status.startTick;mark.End=status.endTick;
                }
            }
            foreach(var pair in slows) {
                var mark=pair.Value;var unit=Array.Find(units,u=>u.Id==pair.Key&&u.Alive);
                if(unit==null){mark.Image.enabled=false;continue;}
                if(tick<mark.End&&!(unit.Statuses??Array.Empty<CombatStatusView>()).Any(s=>s.definition=="synergy-storm-overcharge"&&s.startTick<=tick&&s.endTick>tick))mark.End=(long)tick;
                double age=(tick-mark.Start)/rate;bool ended=tick>=mark.End;
                var frames=ended?art.slowExit:age<(art.slowEnter?.Length??0)/12d?art.slowEnter:art.slowHold;
                int index=ended?(int)Math.Floor((tick-mark.End)/rate*12):(int)Math.Floor(age*12);
                if(!ended&&frames==art.slowHold&&frames!=null&&frames.Length>0)index=(index-(art.slowEnter?.Length??0))%frames.Length;
                Frame(mark.Image,frames,index);
                mark.Image.rectTransform.sizeDelta=Vector2.one*48;
                mark.Image.rectTransform.anchoredPosition=unit.Position+new Vector2(-width*.35f,width*.75f);
                mark.Image.transform.SetAsLastSibling();
            }
            foreach(var hud in teams.Values)hud.Root.gameObject.SetActive(false);
            foreach(var group in units.Where(u=>u.StormTier>=4).GroupBy(u=>u.Team)) {
                var member=group.First();var hud=Hud(root,group.Key);hud.Root.gameObject.SetActive(true);
                foreach(var burst in bursts)if(units.Any(u=>u.Id==burst.Key&&u.Team==group.Key)&&burst.Value>hud.Burst)hud.Burst=burst.Value;
                double elapsed=hud.Burst==long.MinValue?double.PositiveInfinity:(tick-hud.Burst)/rate;
                bool ready=member.StormTier>=6&&elapsed>=0&&elapsed<readySeconds;
                int count=ready?4:member.StormEnergy;
                hud.Gauge.enabled=member.StormTier>=6&&art.charge!=null&&art.charge.Length>0;
                if(hud.Gauge.enabled)hud.Gauge.sprite=art.charge[Mathf.Clamp(count,0,Math.Min(4,art.charge.Length-1))];
                hud.Label.text="T"+group.Key+(member.StormTier>=6?" CHARGE "+count+"/4":" ENERGY "+count);
                var frames=ready?art.ready:art.spent;
                int frame=(int)Math.Floor((elapsed-(ready?0:readySeconds))*12);
                Frame(hud.Flash,member.StormTier>=6?frames:null,frame);
                hud.Root.SetAsLastSibling();
            }
        }
        private static void Release(UnityEngine.Object value){if(Application.isPlaying)UnityEngine.Object.Destroy(value);else UnityEngine.Object.DestroyImmediate(value);}
        public void Clear()
        {
            foreach(var arc in arcs.Values)Release(arc.Image.gameObject);
            foreach(var mark in badges.Values)Release(mark.Image.gameObject);
            foreach(var mark in slows.Values)Release(mark.Image.gameObject);
            foreach(var hud in teams.Values)Release(hud.Root.gameObject);
            arcs.Clear();badges.Clear();slows.Clear();teams.Clear();bursts.Clear();
        }
    }

    public sealed partial class PlacementSandboxView
    {
        private readonly Dictionary<(string from,string to),Image> stormLinks=new Dictionary<(string,string),Image>();
        private StormVisuals stormArt;
        private readonly Dictionary<string,(Image image,int families)> stormBadges=new Dictionary<string,(Image,int)>();

        private void RefreshStormLinks(IEnumerable<UnitInstance> roster)
        {
            var members=roster.Where(u=>u.Placement.Kind==PlacementKind.Board&&
                SynergyPreparation.Traits(u,catalog).Contains(SynergyTraits.Storm)).ToArray();
            var desired=new HashSet<(string from,string to)>();
            if(members.Select(u=>SynergyTraits.Family(u.DefinitionId)).Distinct().Count()>=2)
                for(int i=0;i<members.Length;i++)
                    for(int j=i+1;j<members.Length;j++)
                        if(HexCoordinates.Distance(members[i].Placement.Position.Value,members[j].Placement.Position.Value)==1)
                            desired.Add((members[i].InstanceId,members[j].InstanceId));
            var grades=new Dictionary<string,int>();
            var pending=new HashSet<string>(members.Select(u=>u.InstanceId));
            foreach(var first in members) {
                if(!pending.Remove(first.InstanceId))continue;
                var group=new List<UnitInstance>{first};var queue=new Queue<UnitInstance>();queue.Enqueue(first);
                while(queue.Count>0) {
                    var at=queue.Dequeue();
                    foreach(var next in members.Where(u=>pending.Contains(u.InstanceId)&&HexCoordinates.Distance(at.Placement.Position.Value,u.Placement.Position.Value)==1)) {
                        pending.Remove(next.InstanceId);group.Add(next);queue.Enqueue(next);
                    }
                }
                int families=group.Select(u=>SynergyTraits.Family(u.DefinitionId)).Distinct().Count();
                if(families>=2)foreach(var member in group)grades[member.InstanceId]=families;
            }
            foreach(var stale in stormBadges.Keys.Where(id=>!grades.ContainsKey(id)).ToArray()){Destroy(stormBadges[stale].image.gameObject);stormBadges.Remove(stale);}
            foreach(var grade in grades) {
                if(!stormBadges.TryGetValue(grade.Key,out var old))old=(StormVisuals.Create(canvasRect,"PrepBadge"),grade.Value);
                stormBadges[grade.Key]=(old.image,grade.Value);
            }
            foreach(var stale in stormLinks.Keys.Where(k=>!desired.Contains(k)).ToArray())
            {
                stormLinks[stale].enabled=false;
                Destroy(stormLinks[stale].gameObject);
                stormLinks.Remove(stale);
            }
            foreach(var link in desired)
                if(!stormLinks.ContainsKey(link))stormLinks.Add(link,StormVisuals.Create(canvasRect,"link"));
        }

        private void AnimateStormLinks()
        {
            if(stormLinks.Count==0&&stormBadges.Count==0)return;
            if(stormArt==null)stormArt=Resources.Load<StormVisuals>("StormVisuals");
            if(stormArt==null)return;
            int frame=(int)(Time.unscaledTimeAsDouble*12)%stormArt.link.Length;
            foreach(var entry in stormBadges) {
                var image=entry.Value.image;
                image.enabled=tokens.TryGetValue(entry.Key,out var token)&&token.gameObject.activeInHierarchy&&dragged!=entry.Key;
                var frames=entry.Value.families>=3?stormArt.badgeThree:stormArt.badgeTwo;
                if(!image.enabled||frames==null||frames.Length==0){image.enabled=false;continue;}
                image.sprite=frames[(int)(Time.unscaledTimeAsDouble*12)%frames.Length];
                image.rectTransform.sizeDelta=Vector2.one*28;
                image.rectTransform.anchoredPosition=token.anchoredPosition+new Vector2(23,43);
                image.transform.SetAsLastSibling();
            }
            foreach(var pair in stormLinks)
            {
                var from=tokens[pair.Key.from];
                var to=tokens[pair.Key.to];
                var image=pair.Value;
                image.enabled=from.gameObject.activeInHierarchy&&to.gameObject.activeInHierarchy&&
                    dragged!=pair.Key.from&&dragged!=pair.Key.to;
                if(!image.enabled)continue;
                image.sprite=stormArt.link[frame];
                StormVisuals.Line(image,from.anchoredPosition,to.anchoredPosition,46,50);
            }
        }
    }
}
