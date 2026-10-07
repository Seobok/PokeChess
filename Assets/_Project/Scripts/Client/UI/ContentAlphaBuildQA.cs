#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;

namespace PokeChess.Client.UI
{
    // Explicit command-line QA mode; regular launches do not create this component.
    public sealed class ContentAlphaBuildQA : MonoBehaviour
    {
        [Serializable] private sealed class Report {
            public string status,error,winner;public int endRound,consoleErrors;public bool finalUI;
        }
        private PlacementSandboxView view;
        private string output;
        private ulong seed=1;
        private int consoleErrors,finishedFrames;
        private bool started,done;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if(!Environment.GetCommandLineArgs().Contains("-pokechess-content-qa"))return;
            var obj=new GameObject("ContentAlphaBuildQA");DontDestroyOnLoad(obj);obj.AddComponent<ContentAlphaBuildQA>();
        }
        private void Awake()
        {
            var args=Environment.GetCommandLineArgs();
            output=Path.Combine(Application.persistentDataPath,"ContentAlphaQA");
            for(int i=0;i<args.Length-1;i++) {
                if(args[i]=="-pokechess-qa-output")output=Path.GetFullPath(args[i+1]);
                if(args[i]=="-pokechess-qa-seed")seed=ulong.Parse(args[i+1]);
            }
            Directory.CreateDirectory(output);Application.runInBackground=true;Application.logMessageReceived+=Log;
        }
        private void Log(string message,string stack,LogType type)
        { if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)consoleErrors++; }
        private void Update()
        {
            if(done)return;
            try {
                if(Time.realtimeSinceStartup>300)throw new TimeoutException("Build QA exceeded 300 seconds.");
                if(!started) {
                    view=FindFirstObjectByType<PlacementSandboxView>();
                    if(view==null)view=new GameObject("ContentAlphaSandbox").AddComponent<PlacementSandboxView>();
                    if(view.RoundLoop==null)return;
                    view.StartLocalSimulation(seed,true);started=true;return;
                }
                if(view.Simulation.Status==SimulationStatus.Running)return;
                if(++finishedFrames<2)return;
                view.RefreshFromState();var run=view.Simulation;
                File.WriteAllText(Path.Combine(output,"match.json"),SimulationReport.ToJson(run));
                File.WriteAllText(Path.Combine(output,"summary.csv"),SimulationReport.ToCsv(new[]{run}));
                if(run.Status!=SimulationStatus.Completed)throw new Exception(run.Status+": "+run.Error);
                run.Match.Pool.AssertConservation(run.Match);
                if(!view.FinalResultVisible||!run.Match.FinalResult.Standings.Select(r=>r.Placement).SequenceEqual(Enumerable.Range(1,8)))
                    throw new Exception("Missing final UI / standings.");
                Capture();
                if(consoleErrors>0)throw new Exception("Runtime console errors: "+consoleErrors);
                Finish(new Report{status="Completed",winner=run.Match.FinalResult.WinnerPlayerId,endRound=run.Match.RoundNumber,finalUI=true,consoleErrors=consoleErrors},0);
            }
            catch(Exception error){Finish(new Report{status="Failed",error=error.ToString(),consoleErrors=consoleErrors},1);}
        }
        private void Finish(Report report,int exitCode)
        {
            done=true;Application.logMessageReceived-=Log;
            File.WriteAllText(Path.Combine(output,"player-qa-status.json"),JsonUtility.ToJson(report,true));Application.Quit(exitCode);
        }
        private void Capture()
        {
            var canvas=view.GetComponentInChildren<Canvas>();var scaler=canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
            var oldMode=canvas.renderMode;var oldCamera=canvas.worldCamera;var oldScale=canvas.scaleFactor;var oldPlane=canvas.planeDistance;bool oldEnabled=scaler.enabled;
            var obj=new GameObject("BuildQACamera");var camera=obj.AddComponent<Camera>();
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.06f,.1f);
            var target=new RenderTexture(1280,720,24);target.Create();var oldTarget=RenderTexture.active;Texture2D image=null;
            try {
                camera.targetTexture=target;scaler.enabled=false;canvas.renderMode=RenderMode.ScreenSpaceCamera;
                canvas.worldCamera=camera;canvas.planeDistance=1;canvas.scaleFactor=1;Canvas.ForceUpdateCanvases();camera.Render();
                RenderTexture.active=target;image=new Texture2D(1280,720,TextureFormat.RGBA32,false);
                image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,"final-result.png"),image.EncodeToPNG());
            }
            finally {
                canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;canvas.scaleFactor=oldScale;canvas.planeDistance=oldPlane;scaler.enabled=oldEnabled;
                RenderTexture.active=oldTarget;camera.targetTexture=null;Destroy(obj);if(image!=null)Destroy(image);target.Release();Destroy(target);
            }
        }
    }
}
#endif
