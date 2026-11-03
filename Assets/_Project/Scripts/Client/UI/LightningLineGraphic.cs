using UnityEngine;
using UnityEngine.UI;

namespace PokeChess.Client.UI
{
    // LineRenderer owns the geometry. Bake it into a UI Graphic so overlay canvases,
    // CanvasScaler and UI masks render the same bolt without a separate render camera.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LightningLineGraphic : MaskableGraphic
    {
        private LineRenderer glow,core;
        private Camera bakeCamera;
        private Mesh baked;
        private readonly Vector3[] points=new Vector3[13];
        private Color glowTint,coreTint;
        private LineRenderer[] branchGlows,branchCores;
        private bool branchesActive;
        public LineRenderer Bolt=>core;
        private void InitializeLines()
        {
            if(core!=null)return;
            raycastTarget=false;
            baked=new Mesh{name="Lightning UI mesh"};
            var cameraNode=new GameObject("GeometryCamera",typeof(Camera));cameraNode.transform.SetParent(transform,false);
            bakeCamera=cameraNode.GetComponent<Camera>();bakeCamera.enabled=false;bakeCamera.orthographic=true;
            bakeCamera.cullingMask=0;cameraNode.transform.localPosition=new Vector3(0,0,-100);
            glow=MakeLine("LightningGlow");core=MakeLine("LightningCore");
        }
        private LineRenderer MakeLine(string name)
        {
            var node=new GameObject(name,typeof(LineRenderer));node.transform.SetParent(transform,false);
            var line=node.GetComponent<LineRenderer>();line.enabled=false;line.useWorldSpace=false;
            line.alignment=LineAlignment.TransformZ;line.textureMode=LineTextureMode.Stretch;
            line.numCapVertices=3;line.numCornerVertices=2;line.positionCount=points.Length;
            return line;
        }
        public void Sample(Vector2 from,Vector2 to,double seconds,float cellWidth,float width,float opacity,int seed,Color glowColor,Color coreColor,bool straightBeam=false,bool overheadLightning=false)
        {
            InitializeLines();
            branchesActive=overheadLightning;
            glowTint=glowColor;coreTint=coreColor;
            if(straightBeam)to=Vector2.Lerp(from,to,Mathf.Clamp01((float)seconds/.08f));
            var delta=to-from;var normal=delta.sqrMagnitude>.001f?new Vector2(-delta.y,delta.x).normalized:Vector2.up;
            int phase=Mathf.FloorToInt((float)seconds*30);
            for(int i=0;i<points.Length;i++) {
                float t=i/(float)(points.Length-1);
                float noise=Mathf.Sin((i*127.1f+phase*311.7f+seed*17.3f))*43758.5453f;
                noise=(noise-Mathf.Floor(noise))*2-1;
                float envelope=i==0||i==points.Length-1?0:Mathf.Sin(t*Mathf.PI);
                points[i]=Vector2.Lerp(from,to,t)+normal*(straightBeam?0:noise*envelope*cellWidth*(overheadLightning?.25f:.14f));
            }
            glow.SetPositions(points);core.SetPositions(points);
            glow.startWidth=glow.endWidth=cellWidth*width*2.6f;
            core.startWidth=core.endWidth=cellWidth*width;
            if(overheadLightning) {
                if(branchCores==null) {
                    branchGlows=new[]{MakeLine("BranchGlow0"),MakeLine("BranchGlow1")};
                    branchCores=new[]{MakeLine("BranchCore0"),MakeLine("BranchCore1")};
                }
                for(int b=0;b<2;b++) {
                    var root=points[3+b*3];float side=b==0?-1:1;
                    var fork=new[]{root,root+new Vector3(side*cellWidth*.35f,-cellWidth*.25f,0),
                        root+new Vector3(side*cellWidth*.25f,-cellWidth*.5f,0),root+new Vector3(side*cellWidth*.65f,-cellWidth*.75f,0)};
                    branchGlows[b].positionCount=branchCores[b].positionCount=fork.Length;
                    branchGlows[b].SetPositions(fork);branchCores[b].SetPositions(fork);
                    branchGlows[b].startWidth=cellWidth*width*1.4f;branchGlows[b].endWidth=0;
                    branchCores[b].startWidth=cellWidth*width*.5f;branchCores[b].endWidth=0;
                }
            }
            if(straightBeam) {
                float pulse=1+.08f*Mathf.Sin((float)seconds*40);
                glow.startWidth=glow.endWidth=cellWidth*width*2.6f*pulse;
                core.startWidth=core.endWidth=cellWidth*width*pulse;
            }
            color=new Color(1,1,1,opacity);SetVerticesDirty();
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(core==null)return;
            Append(vh,glow,glowTint);
            Append(vh,core,coreTint);
            if(branchesActive&&branchCores!=null)for(int b=0;b<2;b++){Append(vh,branchGlows[b],glowTint);Append(vh,branchCores[b],coreTint);}
        }
        private void Append(VertexHelper vh,LineRenderer line,Color tint)
        {
            baked.Clear();line.BakeMesh(baked,bakeCamera,false);
            var vertices=baked.vertices;var uv=baked.uv;var triangles=baked.triangles;int offset=vh.currentVertCount;
            tint.a*=color.a;
            for(int i=0;i<vertices.Length;i++)vh.AddVert(vertices[i],tint,uv.Length>i?uv[i]:Vector2.zero);
            for(int i=0;i<triangles.Length;i+=3)vh.AddTriangle(offset+triangles[i],offset+triangles[i+1],offset+triangles[i+2]);
        }
        protected override void OnDestroy()
        {
            if(baked!=null){if(Application.isPlaying)Destroy(baked);else DestroyImmediate(baked);}
            base.OnDestroy();
        }
    }
}
