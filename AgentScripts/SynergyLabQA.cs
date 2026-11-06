using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PokeChess.Client.UI;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
public static class SynergyLabQA
{
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    public static string VerifyPresets()
    {
        Check(Application.isPlaying,"Play Mode required.");var lab=UnityEngine.Object.FindFirstObjectByType<SynergyLabController>();Check(lab!=null,"Missing lab controller.");
        int verified=0;var report=new List<string>();
        foreach(var trait in SynergyTraits.All)foreach(var tier in SynergyTraits.Thresholds(trait))
        {
            lab.Select(trait,tier);var view=lab.View;
            var count=SynergyPreparation.Count(view.Player,PokeChess.Content.GameContent.CreateCatalog()).Single(c=>c.TraitId==trait);
            Check(count.Count==tier&&count.Tier==tier,trait+"/"+tier+" preset count.");Check(!view.Match.Rules.EnableWildRounds,"Lab must use PvP.");
            int expected=tier+(trait==SynergyTraits.Guardian?1:0);Check(view.Player.Units.Count==expected&&view.Player.DeployedUnitCount==expected,"Unexpected deployment.");
            Check(view.StartSynergyLabCombat(),"Cannot start "+trait+"/"+tier);var b=view.RoundLoop.GetBattleFor("p1").Battle;
            var own=b.Units.First(u=>u.OwnerPlayerId=="p1"&&!u.IsSynergySummon);
            Check(b.Synergies.TeamTier(own.TeamId,trait)==tier,"Battle snapshot does not match "+trait+"/"+tier);
            if(trait==SynergyTraits.Guardian)Check(b.Synergies.IsGuarded(b.Units.Single(u=>u.UnitInstanceId=="lab-beneficiary")),"Guardian preset needs a protected non-member.");
            Check(b.Synergies.TeamCounts(own.TeamId==1?2:1).Single(c=>c.TraitId==SynergyTraits.Guardian).Count==1,"Enemy duplicates counted more than once.");
            if(tier==10){Check(view.Player.Units.Count(u=>u.ItemInstanceIds.Any(i=>ItemCatalog.DefinitionId(i)=="emblem-"+trait))==2,"Ultimate needs two emblems.");}
            view.StepSynergyLab(2);view.Match.Pool.AssertConservation(view.Match);verified++;report.Add(trait+"/"+tier+": passed");
        }
        lab.Select(SynergyTraits.Guardian,4);
        Check(UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None).Length==1,"Exactly one EventSystem required.");
        var toolbar=lab.transform.Find("SynergyLabCanvas/Toolbar");Check(toolbar!=null,"Missing toolbar.");
        Check(toolbar.GetComponentsInChildren<UnityEngine.UI.Button>().Length==7,"Missing toolbar buttons.");
        toolbar.Find("Next").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();Check(lab.View.SynergyLabTrait!=SynergyTraits.Guardian,"Next button not wired.");
        toolbar.Find("Previous").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();Check(lab.View.SynergyLabTrait==SynergyTraits.Guardian,"Previous button not wired.");
        toolbar.Find("Tier").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();Check(lab.View.SynergyLabThreshold==4,"Tier button not wired.");
        toolbar.Find("Speed").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();Check(lab.Speed==4,"Speed button not wired.");
        toolbar.Find("Speed").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();toolbar.Find("Speed").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
        string directory=Path.GetFullPath("TestResults/SynergyV09");Directory.CreateDirectory(directory);File.WriteAllLines(Path.Combine(directory,"Lab-Presets.txt"),report);
        return "Passed "+verified+" presets; exact family counts, tiers, PvP snapshots, ultimate emblems, pool conservation, and one EventSystem.";
    }
    public static string VerifyBattleAndControls()
    {
        var lab=UnityEngine.Object.FindFirstObjectByType<SynergyLabController>();Check(lab!=null,"Missing lab.");
        lab.Select(SynergyTraits.Torrent,6);lab.Step();Check(!lab.Running,"Step must stay paused.");Check(lab.View.Match.Phase==MatchPhase.Combat,"Step must start a real battle.");
        var battle=lab.View.RoundLoop.GetBattleFor("p1").Battle;Check(battle.CurrentTick==1,"Single step must advance exactly one tick.");
        bool wave=false;for(int i=0;i<1351&&lab.View.Match.Phase==MatchPhase.Combat;i++){lab.View.StepSynergyLab();wave|=battle.Synergies.EventsThisTick.Any(e=>e.TraitId==SynergyTraits.Torrent);}
        Check(wave,"No wave observed.");Check(lab.View.Match.Phase==MatchPhase.Result,"Battle did not settle.");Check(lab.View.NextSynergyLabRound(),"Cannot enter next preparation.");
        Check(lab.View.Match.RoundNumber==2&&lab.View.Match.Phase==MatchPhase.Preparation,"Wrong next round.");lab.View.Match.Pool.AssertConservation(lab.View.Match);
        lab.Select(SynergyTraits.Guardian,4);return "Passed: single tick, pause, wave, complete combat, round settlement, next preparation, and pool conservation.";
    }
    public static string VerifyPlacement()
    {
        var lab=UnityEngine.Object.FindFirstObjectByType<SynergyLabController>();lab.Select(SynergyTraits.Guardian,4);var view=lab.View;
        string id="lab-ally-0";var destination=new BoardPosition(0,0);var data=new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current){button=UnityEngine.EventSystems.PointerEventData.InputButton.Left,position=view.ScreenPoint(view.Player.GetUnit(id).Placement)};
        UnityEngine.EventSystems.ExecuteEvents.Execute(view.Token(id).gameObject,data,UnityEngine.EventSystems.ExecuteEvents.beginDragHandler);
        data.position=view.ScreenPoint(UnitPlacement.OnBoard(destination));UnityEngine.EventSystems.ExecuteEvents.Execute(view.Token(id).gameObject,data,UnityEngine.EventSystems.ExecuteEvents.dragHandler);
        UnityEngine.EventSystems.ExecuteEvents.Execute(view.Token(id).gameObject,data,UnityEngine.EventSystems.ExecuteEvents.endDragHandler);
        Check(view.Player.GetUnit(id).Placement.Position.Value.Equals(destination),"Drag did not move the unit.");Check(view.TokenMatchesState(id),"Drag visual diverged from state.");view.Match.Pool.AssertConservation(view.Match);
        lab.Select(SynergyTraits.Guardian,4);return "Passed: real pointer-event drag, destination, token position, and pool conservation.";
    }
    public static string Capture(string filename)
    {
        var lab=UnityEngine.Object.FindFirstObjectByType<SynergyLabController>();Check(lab!=null,"Missing lab.");
        lab.RefreshToolbar();var camera=UnityEngine.Object.FindFirstObjectByType<Camera>();var rt=new RenderTexture(1280,720,24);var oldTarget=camera.targetTexture;
        var canvases=lab.GetComponentsInChildren<Canvas>();var modes=canvases.Select(c=>c.renderMode).ToArray();var cameras=canvases.Select(c=>c.worldCamera).ToArray();var planes=canvases.Select(c=>c.planeDistance).ToArray();var scalers=canvases.Select(c=>c.GetComponent<UnityEngine.UI.CanvasScaler>()).ToArray();var enabled=scalers.Select(s=>s!=null&&s.enabled).ToArray();var factors=canvases.Select(c=>c.scaleFactor).ToArray();
        try {
            camera.targetTexture=rt;for(int i=0;i<canvases.Length;i++){if(scalers[i]!=null)scalers[i].enabled=false;canvases[i].renderMode=RenderMode.ScreenSpaceCamera;canvases[i].worldCamera=camera;canvases[i].planeDistance=1;canvases[i].scaleFactor=1;}
            Canvas.ForceUpdateCanvases();camera.Render();var old=RenderTexture.active;RenderTexture.active=rt;var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();RenderTexture.active=old;
            var path=Path.GetFullPath("TestResults/SynergyV09/"+filename);File.WriteAllBytes(path,texture.EncodeToPNG());UnityEngine.Object.Destroy(texture);return path;
        }
        finally {camera.targetTexture=oldTarget;for(int i=0;i<canvases.Length;i++){canvases[i].renderMode=modes[i];canvases[i].worldCamera=cameras[i];canvases[i].scaleFactor=factors[i];canvases[i].planeDistance=planes[i];if(scalers[i]!=null)scalers[i].enabled=enabled[i];}UnityEngine.Object.Destroy(rt);}
    }
}
