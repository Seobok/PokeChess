using UnityEngine;

namespace PokeChess.Client.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PlacementHexGraphic : UnityEngine.UI.MaskableGraphic, ICanvasRaycastFilter
    {
        public bool OutlineOnly;
        public float LineWidth = 1.5f;
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vh)
        {
            vh.Clear(); var rect=rectTransform.rect;
            var points=new[]{new Vector2(0,.5f),new Vector2(.5f,.25f),new Vector2(.5f,-.25f),
                new Vector2(0,-.5f),new Vector2(-.5f,-.25f),new Vector2(-.5f,.25f)};
            if(OutlineOnly){
                var inset=new Vector2(Mathf.Max(0,rect.width-LineWidth*2),Mathf.Max(0,rect.height-LineWidth*2));
                for(int i=0;i<6;i++){
                    vh.AddVert(rect.center+Vector2.Scale(points[i],rect.size),color,Vector2.zero);
                    vh.AddVert(rect.center+Vector2.Scale(points[i],inset),color,Vector2.zero);
                }
                for(int i=0;i<6;i++){int a=i*2,b=((i+1)%6)*2;vh.AddTriangle(a,b,a+1);vh.AddTriangle(a+1,b,b+1);}
                return;
            }
            vh.AddVert(rect.center,color,Vector2.zero);
            foreach(var point in points) vh.AddVert(rect.center+Vector2.Scale(point,rect.size),color,Vector2.zero);
            for(int i=0;i<6;i++) vh.AddTriangle(0,i+1,(i+1)%6+1);
        }
        public bool IsRaycastLocationValid(Vector2 screenPoint,Camera eventCamera)
        {
            if(!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,screenPoint,eventCamera,out var p)) return false;
            var rect=rectTransform.rect; p-=rect.center;
            return Mathf.Abs(p.x)<=rect.width/2 && Mathf.Abs(p.y)<=rect.height/2-Mathf.Abs(p.x)*rect.height/(2*rect.width);
        }
    }
}
