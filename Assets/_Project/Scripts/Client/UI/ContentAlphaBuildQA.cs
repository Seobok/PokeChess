#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    // Explicit command-line QA mode; regular launches do not create this component.
    public sealed class ContentAlphaBuildQA : MonoBehaviour
    {
        [Serializable] private sealed class Report {
            public string status,error,winner;public int endRound,consoleErrors;public bool finalUI;
            public bool itemRewardUI,itemEquip,itemReturn;
        }
        private PlacementSandboxView view;
        private string output;
        private ulong seed=1;
        private int consoleErrors,finishedFrames;
        private bool started,done;
        private bool itemsMode,itemsChecked;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if(!Environment.GetCommandLineArgs().Contains("-pokechess-content-qa")&&!Environment.GetCommandLineArgs().Contains("-pokechess-items-qa"))return;
            var obj=new GameObject("ContentAlphaBuildQA");DontDestroyOnLoad(obj);obj.AddComponent<ContentAlphaBuildQA>();
        }
        private void Awake()
        {
            var args=Environment.GetCommandLineArgs();
            itemsMode=args.Contains("-pokechess-items-qa");
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
                    if(itemsMode){CheckItems();itemsChecked=true;}
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
                Finish(new Report{status="Completed",winner=run.Match.FinalResult.WinnerPlayerId,endRound=run.Match.RoundNumber,finalUI=true,consoleErrors=consoleErrors,itemRewardUI=itemsChecked,itemEquip=itemsChecked,itemReturn=itemsChecked},0);
            }
            catch(Exception error){Finish(new Report{status="Failed",error=error.ToString(),consoleErrors=consoleErrors},1);}
        }
        private void Finish(Report report,int exitCode)
        {
            done=true;Application.logMessageReceived-=Log;
            File.WriteAllText(Path.Combine(output,"player-qa-status.json"),JsonUtility.ToJson(report,true));Application.Quit(exitCode);
        }
        private void CheckItems()
        {
            view.ShowLocalMatch();view.ResetMatchUI();view.enabled=false;
            var p=view.Player;var catalog=PokeChess.Content.GameContent.CreateCatalog();p.SetProgress(100,0,3);
            for(int i=0;i<3;i++)SharedPoolSystem.RegisterUnit(view.Match,catalog.CreateUnit("items-qa-"+i,"geodude","p1",UnitRank.Two,0,UnitPlacement.OnBoard(new BoardPosition(1+i*2,3))),"geodude");
            if(!view.RoundLoop.StartCombat(1))throw new Exception("Wild battle rejected.");
            for(int i=0;i<1400&&view.Match.Phase==MatchPhase.Combat;i++)view.RoundLoop.Step();
            view.RefreshFromState();
            if(!view.ItemRewardVisible||p.PendingItemReward==null||p.HP!=60)throw new Exception("Missing wild reward UI / changed HP.");
            Capture("item-reward.png");view.ItemChoiceButton(0).onClick.Invoke();
            if(p.ItemInventory.Count!=1||p.PendingItemReward!=null)throw new Exception("Reward button did not claim exactly one tool.");
            if(!view.RoundLoop.NextRound(1))throw new Exception("Next preparation rejected.");view.RefreshFromState();
            string helmet="item:"+ItemCatalog.RockyHelmet+":99:p1";p.AddInventoryItem(helmet);
            view.SelectUnit("items-qa-0");view.RefreshFromState();
            var source=view.InventoryItemButton(1);var icon=source.transform.Find("Icon").GetComponent<UnityEngine.UI.Image>();
            if(icon.sprite==null)throw new Exception("Missing item icon asset.");
            var pointer=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){button=UnityEngine.EventSystems.PointerEventData.InputButton.Left,position=RectTransformUtility.WorldToScreenPoint(null,source.transform.position)};
            var drag=source.GetComponent<ItemDragToken>();drag.OnBeginDrag(pointer);pointer.position=new Vector2(10,10);drag.OnEndDrag(pointer);
            if(!p.ItemInventory.Contains(helmet))throw new Exception("Invalid drop consumed tool.");
            drag.OnBeginDrag(pointer);pointer.position=view.ScreenPoint(p.GetUnit("items-qa-0").Placement);drag.OnDrag(pointer);drag.OnEndDrag(pointer);
            if(!p.GetUnit("items-qa-0").ItemInstanceIds.Contains(helmet)||p.ItemInventory.Contains(helmet))throw new Exception("Drag equip failed.");
            Capture("item-inventory.png");
            view.GetComponentsInChildren<UnityEngine.UI.Button>(true).Single(b=>b.name=="장착 도구 회수").onClick.Invoke();
            if(!p.ItemInventory.Contains(helmet)||p.GetUnit("items-qa-0").ItemInstanceIds.Count!=0)throw new Exception("Unequip button failed.");
            view.enabled=true;
        }
        private void Capture(string filename="final-result.png")
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
                image.ReadPixels(new Rect(0,0,1280,720),0,0);image.Apply();File.WriteAllBytes(Path.Combine(output,filename),image.EncodeToPNG());
            }
            finally {
                canvas.renderMode=oldMode;canvas.worldCamera=oldCamera;canvas.scaleFactor=oldScale;canvas.planeDistance=oldPlane;scaler.enabled=oldEnabled;
                RenderTexture.active=oldTarget;camera.targetTexture=null;Destroy(obj);if(image!=null)Destroy(image);target.Release();Destroy(target);
            }
        }
    }
}
#endif
