using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Client.Data;
using PokeChess.Core.Battle;
namespace PokeChess.Client.UI
{
    // Visual state only. Never applies/removes gameplay statuses.
    public sealed class StatusSpriteView:MonoBehaviour
    {
        private sealed class Layer {public UnityEngine.UI.Image Image;public double Start;public bool Active;public StatusVisualPosition Position;}
        private readonly Dictionary<StatusVisual,Layer> layers=new Dictionary<StatusVisual,Layer>();
        private StatusVisualLibrary library;
        public float Top {get;private set;}
        public void Clear(){Top=0;foreach(var layer in layers.Values){layer.Active=false;if(layer.Image!=null)layer.Image.gameObject.SetActive(false);}}
        private void OnDisable()=>Clear();
        private void OnDestroy(){foreach(var layer in layers.Values)if(layer.Image!=null){if(Application.isPlaying)Destroy(layer.Image.gameObject);else DestroyImmediate(layer.Image.gameObject);}}
        public void Render(IEnumerable<StatusBehavior> behaviors,double time,UnityEngine.UI.Image character,bool alive,float scale,IEnumerable<CrowdControlKind> crowdControls=null)
        {
            if(library==null)library=Resources.Load<StatusVisualLibrary>("StatusVisuals");
            if(!alive||library==null||character.sprite==null){Clear();return;}
            var active=new HashSet<StatusBehavior>(behaviors??Array.Empty<StatusBehavior>());
            if(active.Contains(StatusBehavior.RestSleep))active.Add(StatusBehavior.Sleep);
            var cc=new HashSet<CrowdControlKind>(crowdControls??Array.Empty<CrowdControlKind>()); var visible=library.Effects.Where(e=>(e.CrowdControl!=CrowdControlKind.None?cc.Contains(e.CrowdControl):active.Contains(e.Behavior))&&e.Frames.Length>0).ToArray();
            if(visible.Length==0){Clear();return;}
            var head=visible.Where(e=>e.Position==StatusVisualPosition.Head).ToArray();
            // Tight sprite vertices locate the head even when frames contain transparent padding.
            var vertices=character.sprite.vertices;
            float pixelScale=character.rectTransform.sizeDelta.y/character.sprite.rect.height;
            float ppu=character.sprite.pixelsPerUnit;
            float top=vertices.Length==0?32:vertices.Max(v=>v.y)*ppu*pixelScale+character.rectTransform.anchoredPosition.y;
            float bottom=vertices.Length==0?0:vertices.Min(v=>v.y)*ppu*pixelScale+character.rectTransform.anchoredPosition.y;
            float width=vertices.Length==0?40:(vertices.Max(v=>v.x)-vertices.Min(v=>v.x))*ppu*pixelScale;
            Top=Mathf.Max(0,top);
            foreach(var entry in layers)if(!visible.Contains(entry.Key)){entry.Value.Active=false;entry.Value.Image.gameObject.SetActive(false);}
            foreach(var effect in visible) {
                if(!layers.TryGetValue(effect,out var layer)) {
                    var node=new GameObject("Status_"+(effect.CrowdControl!=CrowdControlKind.None?effect.CrowdControl.ToString():effect.Behavior.ToString()),typeof(RectTransform),typeof(UnityEngine.UI.Image));
                    node.transform.SetParent(transform.parent,false);
                    layer=new Layer{Image=node.GetComponent<UnityEngine.UI.Image>()};layer.Image.raycastTarget=false;
                    layers.Add(effect,layer);
                }
                if(!layer.Active||time<layer.Start)layer.Start=time;layer.Active=true;
                var image=layer.Image;image.gameObject.SetActive(true);
                layer.Position=effect.Position;
                int frame=(int)(Math.Max(0,time-layer.Start)*effect.FramesPerSecond)%effect.Frames.Length;
                image.sprite=effect.Frames[frame];image.color=new Color(1,1,1,effect.Opacity*character.color.a);
                var rect=image.rectTransform;rect.anchorMin=rect.anchorMax=new Vector2(.5f,.5f);
                rect.pivot=new Vector2(.5f,0);
                if(effect.Position==StatusVisualPosition.Feet) {
                    // Midpoint between the former bottom-pivot center (+27)
                    // and the lowered center-pivot position (-12).
                    rect.pivot=new Vector2(.5f,.5f);
                    rect.sizeDelta=Vector2.one*64*scale;
                    rect.anchoredPosition=new Vector2(0,7.5f*scale);
                } else if(effect.Position==StatusVisualPosition.Overlay) {
                    rect.sizeDelta=new Vector2(Mathf.Max(40*scale,width*1.2f),Mathf.Max(44*scale,top-bottom+8*scale));
                    rect.anchoredPosition=new Vector2(character.rectTransform.anchoredPosition.x,bottom-3*scale);
                } else {
                    rect.sizeDelta=Vector2.one*45*scale;
                    int index=Array.IndexOf(head,effect);
                    rect.anchoredPosition=new Vector2((index-(head.Length-1)*.5f)*33*scale,top+2*scale);
                    Top=Mathf.Max(Top,rect.anchoredPosition.y+rect.sizeDelta.y);
                }
            }
            SortLayers(character.transform);
        }
        private void SortLayers(Transform character)
        {
            // Moving a sibling to the character's current index toggles their order when
            // it already precedes the character. Assign absolute, deterministic slots instead.
            // Include hidden cached layers so removal/reapplication cannot reshuffle the stack.
            var ordered=layers.OrderBy(pair=>pair.Value.Position).ThenBy(pair=>pair.Key.Behavior).ThenBy(pair=>pair.Key.CrowdControl).ToArray();
            int index=0;
            SetLayerIndex(character,index++);
            // Feet, body overlays and head effects all render in front of the character.
            foreach(var entry in ordered)SetLayerIndex(entry.Value.Image.transform,index++);
            // Other siblings (health, energy, rank, equipment and labels) remain above this block.
        }
        private static void SetLayerIndex(Transform layer,int index)
        {
            if(layer.GetSiblingIndex()!=index)layer.SetSiblingIndex(index);
        }
    }
}
