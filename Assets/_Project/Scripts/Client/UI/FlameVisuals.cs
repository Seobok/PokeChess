using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace PokeChess.Client.UI
{
    public sealed class FlameVisuals : ScriptableObject
    {
        [SerializeField] private Sprite[] tile, flash, spread, blast, expire;

        internal Sprite[] Frames(string clip)
        {
            switch (clip)
            {
                case "tile": return tile;
                case "flash": return flash;
                case "spread": return spread;
                case "blast": return blast;
                case "expire": return expire;
                default: return null;
            }
        }
    }

    internal sealed class FlameBattleVisuals
    {
        private readonly Dictionary<((int column,int row) cell,string clip),(Image image,long tick)> effects =
            new Dictionary<((int,int),string),(Image,long)>();
        private FlameVisuals catalog;

        public void Trigger(Transform root,(int column,int row) cell,string clip,long tick)
        {
            var key=(cell,clip);
            if(effects.TryGetValue(key,out var current))
            {
                if(tick>current.tick)effects[key]=(current.image,tick);
                return;
            }
            var image=new GameObject("Flame_"+clip,typeof(RectTransform),typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(root,false);
            image.raycastTarget=false;
            image.enabled=false;
            effects.Add(key,(image,tick));
        }

        public void Render(Transform root,IEnumerable<(int column,int row)> tiles,Func<int,int,Vector2> point,double tick,int rate)
        {
            if(point==null || (!tiles.Any() && effects.Count==0))return;
            if(catalog==null)catalog=Resources.Load<FlameVisuals>("FlameVisuals");
            if(catalog==null)return;
            var active=new HashSet<(int,int)>(tiles);
            foreach(var cell in active)Trigger(root,cell,"tile",0);
            float cellWidth=Vector2.Distance(point(0,0),point(1,0));
            foreach(var entry in effects)
            {
                var (cell,clip)=entry.Key;
                var (image,start)=entry.Value;
                bool loop=clip=="tile";
                var frames=catalog.Frames(clip);
                int frame=(int)Math.Floor((tick-start)*12/Math.Max(1,rate));
                image.enabled=frames!=null && frames.Length>0 && frame>=0 &&
                    (loop?active.Contains(cell):frame<frames.Length);
                if(!image.enabled)continue;
                image.sprite=frames[frame%frames.Length];
                image.rectTransform.anchoredPosition=point(cell.column,cell.row)+SkillVisualBattleView.GroundOffset(point);
                image.rectTransform.sizeDelta=Vector2.one*cellWidth*(clip=="blast"?2.5f:1.2f);
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
        }
    }
}
