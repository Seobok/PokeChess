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
        private sealed class Arc
        {
            public Image Image;
            public long Tick;
        }

        private readonly Dictionary<(string clip,string from,string to),Arc> arcs =
            new Dictionary<(string,string,string),Arc>();
        private readonly Dictionary<string,(Image image,string unit,int count,long tick)> charges =
            new Dictionary<string,(Image,string,int,long)>();
        private StormVisuals art;

        public void Signal(Transform root,string clip,string from,string to,int count,long tick)
        {
            if(from==null||to==null)return;
            if(clip=="CHARGE")
            {
                if(!charges.TryGetValue(from,out var charge))
                    charge=(StormVisuals.Create(root,"charge"),to,count,long.MinValue);
                if(tick>charge.tick)charges[from]=(charge.image,to,count,tick);
                return;
            }
            if(clip!="ARC"&&clip!="CHAIN"&&clip!="DISCHARGE")return;
            var key=(clip,from,to);
            if(!arcs.TryGetValue(key,out var arc))
                arcs.Add(key,new Arc{Image=StormVisuals.Create(root,clip),Tick=tick});
            else if(tick>arc.Tick)arc.Tick=tick;
        }

        public void Render(SkillVisualUnit[] units,double tick,int rate,Func<int,int,Vector2> point)
        {
            if(point==null||(arcs.Count==0&&charges.Count==0))return;
            if(art==null)art=Resources.Load<StormVisuals>("StormVisuals");
            if(art==null)return;
            float width=Vector2.Distance(point(0,0),point(1,0));
            foreach(var entry in arcs)
            {
                var key=entry.Key;
                var arc=entry.Value;
                var from=Array.Find(units,u=>u.Id==key.from);
                var to=Array.Find(units,u=>u.Id==key.to);
                bool discharge=key.clip=="DISCHARGE";
                var frames=discharge?art.discharge:art.current;
                int frame=(int)Math.Floor(((tick-arc.Tick)/Math.Max(1,rate)-(key.clip=="CHAIN"?1d/12:0))*12);
                arc.Image.enabled=from!=null&&to!=null&&frame>=0&&frame<frames.Length;
                if(!arc.Image.enabled)continue;
                arc.Image.sprite=frames[frame];
                var offset=Vector2.up*width*.25f;
                StormVisuals.Line(arc.Image,from.Position+offset,to.Position+offset,width*(discharge?1.35f:.85f),48);
                arc.Image.transform.SetAsLastSibling();
            }
            foreach(var charge in charges.Values)
            {
                var unit=Array.Find(units,u=>u.Id==charge.unit&&u.Alive);
                charge.image.enabled=unit!=null&&tick>=charge.tick;
                if(!charge.image.enabled)continue;
                charge.image.sprite=art.charge[Mathf.Clamp(charge.count,0,art.charge.Length-1)];
                charge.image.rectTransform.sizeDelta=Vector2.one*width;
                charge.image.rectTransform.anchoredPosition=unit.Position+Vector2.up*width*.9f;
                charge.image.transform.SetAsLastSibling();
            }
        }

        public void Clear()
        {
            foreach(var arc in arcs.Values){arc.Image.enabled=false;UnityEngine.Object.Destroy(arc.Image.gameObject);}
            foreach(var charge in charges.Values){charge.image.enabled=false;UnityEngine.Object.Destroy(charge.image.gameObject);}
            arcs.Clear();
            charges.Clear();
        }
    }

    public sealed partial class PlacementSandboxView
    {
        private readonly Dictionary<(string from,string to),Image> stormLinks=new Dictionary<(string,string),Image>();
        private StormVisuals stormArt;

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
            if(stormLinks.Count==0)return;
            if(stormArt==null)stormArt=Resources.Load<StormVisuals>("StormVisuals");
            if(stormArt==null)return;
            int frame=(int)(Time.unscaledTimeAsDouble*12)%stormArt.link.Length;
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
