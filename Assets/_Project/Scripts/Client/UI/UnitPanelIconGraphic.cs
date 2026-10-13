using UnityEngine;
namespace PokeChess.Client.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UnitPanelIconGraphic : UnityEngine.UI.MaskableGraphic
    {
        public int Kind; // 0: skill burst, 1: front shield, 2: rear bow
        private void Polygon(UnityEngine.UI.VertexHelper mesh,Vector2[] points)
        {
            int start=mesh.currentVertCount;var r=rectTransform.rect;
            mesh.AddVert(r.center,color,Vector2.zero);
            foreach(var p in points)mesh.AddVert(r.center+Vector2.Scale(p,r.size),color,Vector2.zero);
            for(int i=0;i<points.Length;i++)mesh.AddTriangle(start,start+i+1,start+(i+1)%points.Length+1);
        }
        private void Line(UnityEngine.UI.VertexHelper mesh,Vector2 a,Vector2 b,float width)
        {
            var r=rectTransform.rect;a=r.center+Vector2.Scale(a,r.size);b=r.center+Vector2.Scale(b,r.size);
            var n=new Vector2(-(b-a).y,(b-a).x).normalized*width/2;int i=mesh.currentVertCount;
            foreach(var p in new[]{a+n,b+n,b-n,a-n})mesh.AddVert(p,color,Vector2.zero);
            mesh.AddTriangle(i,i+1,i+2);mesh.AddTriangle(i,i+2,i+3);
        }
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            if(Kind==0){
                var points=new Vector2[16];
                for(int i=0;i<16;i++){float angle=i*Mathf.PI/8;float radius=i%2==0?.43f:.2f;points[i]=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;}
                Polygon(mesh,points);
            }else if(Kind==1){
                Polygon(mesh,new[]{new Vector2(-.36f,.35f),new Vector2(.36f,.35f),new Vector2(.3f,-.12f),new Vector2(0,-.43f),new Vector2(-.3f,-.12f)});
            }else{
                var arc=new[]{new Vector2(-.27f,.4f),new Vector2(.02f,.26f),new Vector2(.16f,0),new Vector2(.02f,-.26f),new Vector2(-.27f,-.4f)};
                for(int i=0;i<arc.Length-1;i++)Line(mesh,arc[i],arc[i+1],3);
                Line(mesh,arc[0],arc[4],2);Line(mesh,new Vector2(-.4f,0),new Vector2(.43f,0),3);
                Line(mesh,new Vector2(.43f,0),new Vector2(.27f,.14f),3);Line(mesh,new Vector2(.43f,0),new Vector2(.27f,-.14f),3);
            }
        }
    }
}
