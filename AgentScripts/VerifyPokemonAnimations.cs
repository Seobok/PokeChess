using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using PokeChess.Client.Data;
using PokeChess.Client.UI;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Match;
using PokeChess.Core.Battle;

public static class VerifyPokemonAnimations
{
    public static string Main()
    {
        var library=PokemonAnimationLibrary.Load();
        if(library==null)throw new Exception("Animation library missing.");
        int frames=0;
        foreach(var a in library.Animations)
        {
            if(a.Frames.Length!=a.FrameCount*a.Directions||a.GroundOffsets.Length!=a.Frames.Length||a.Frames.Any(s=>s==null))
                throw new Exception("Invalid frame references: "+a.PokemonId+"/"+a.Name);
            double start=0;
            for(int f=0;f<a.FrameCount;f++)
            {
                if(a.FrameAt(start+.00001,false)!=f)throw new Exception("Incorrect frame duration.");
                start+=a.Durations[f]/60d;
            }
            if(a.FrameAt(a.DurationSeconds+.00001,true)!=0)throw new Exception("Loop boundary failed.");
            if(a.HitFrame>=0&&a.FrameAt(a.ActionTime(.2,.2,.8)+.00001,false)!=a.HitFrame)
                throw new Exception("Hit synchronization failed: "+a.Name);
            foreach(var s in a.Frames)
                if(s.texture.filterMode!=FilterMode.Point||s.texture.mipmapCount!=1)throw new Exception("Pixel art import settings failed.");
            frames+=a.Frames.Length;
        }
        if(!Application.isPlaying)throw new Exception("Enter Play mode for client verification.");
        foreach(string id in new[]{"slowpoke","mankey","weedle","chansey"})
        {
            var catalog=PrototypeRoster.CreateCatalog();
            var battle=BattleStateFactory.Create("sprite-check",1,123,30,catalog,new[]{
                new BattleUnitSetup(catalog.CreateUnit("test",id,"p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(0,0)),
                new BattleUnitSetup(catalog.CreateUnit("ally","slowpoke","p1",UnitRank.One,0,UnitPlacement.Unplaced),1,new BoardPosition(1,0)),
                new BattleUnitSetup(catalog.CreateUnit("foe","slowpoke","p2",UnitRank.One,0,UnitPlacement.Unplaced),2,new BoardPosition(6,7))});
            var sim=new BattleSimulation(battle);var unit=battle.Units.Single(u=>u.UnitInstanceId=="test");
            var runtime=sim.GetRuntime("test");var obj=new GameObject("SpriteVerification",typeof(RectTransform));
            try
            {
                var sprite=obj.AddComponent<PokemonSpriteView>();sprite.Initialize(id);
                sprite.ShowIdle(0);var first=obj.GetComponent<UnityEngine.UI.Image>().sprite;
                sprite.ShowIdle(library.Find(id,"Idle").Durations[0]/60d+.001);
                if(obj.GetComponent<UnityEngine.UI.Image>().sprite==first)throw new Exception("Idle did not advance: "+id);
                for(int d=0;d<8;d++)
                {
                    sprite.ShowCombat(battle,unit,runtime,new Vector2((float)Math.Sin(d*Math.PI/4),-(float)Math.Cos(d*Math.PI/4)));
                    if(!obj.GetComponent<UnityEngine.UI.Image>().sprite.name.Contains("_d"+d+"_"))throw new Exception("Direction mismatch: "+id);
                }
                unit.SetVitals(0,0);sprite.ShowCombat(battle,unit,runtime,Vector2.zero);
                if(!obj.GetComponent<UnityEngine.UI.Image>().sprite.name.Contains("_Hurt_"))throw new Exception("Death pose missing: "+id);
                for(int i=0;i<15;i++)sim.Step();sprite.ShowCombat(battle,unit,runtime,Vector2.zero);
                if(sprite.Visible)throw new Exception("Death fade failed: "+id);
            }
            finally {UnityEngine.Object.DestroyImmediate(obj);}
        }
        var view=UnityEngine.Object.FindFirstObjectByType<PlacementSandboxView>();
        if(view==null)throw new Exception("Placement sandbox missing.");
        var seen=new HashSet<string>();int rounds=0;
        foreach(var rank in new[]{UnitRank.One,UnitRank.Two,UnitRank.Three})
        {
            view.ResetRosterDemo(rank);
            // Start the teams apart so even the melee attacker must demonstrate walking.
            foreach(var player in view.Match.Players)
                for(int i=0;i<player.Units.Count;i++)player.SetPlacement(player.Units[i].InstanceId,UnitPlacement.OnBoard(new BoardPosition(i,0)));
            view.RoundLoop.StartCombat(view.Match.RoundNumber);view.RoundFlow.RefreshState();
            foreach(var unit in view.RoundLoop.Battle.Units)unit.SetVitals(unit.CurrentHP,unit.Stats.MaxEnergy);
            int ticks=0;
            while(view.Match.Phase==MatchPhase.Combat&&ticks++<1400)
            {
                view.AdvanceRoundTime(1d/30);
                var sprites=view.GetComponentsInChildren<PokemonSpriteView>(true);
                foreach(var unit in view.RoundLoop.Battle.Units.Where(u=>u.IsAlive))
                {
                    var sprite=sprites.FirstOrDefault(s=>s.transform.parent.name=="Fighter_"+unit.UnitInstanceId&&s.gameObject.activeInHierarchy);
                    if(view.Match.Phase==MatchPhase.Combat)
                    {
                        if(sprite==null||!sprite.Visible)throw new Exception("Invisible unit: "+unit.UnitInstanceId+" at tick "+view.RoundLoop.Battle.CurrentTick+" / "+unit.ActionState+" / onboard="+unit.IsOnBoard+" / candidates="+string.Join(";",sprites.Where(s=>s.transform.parent.name=="Fighter_"+unit.UnitInstanceId).Select(s=>s.Visible+"/"+s.gameObject.activeInHierarchy+"/"+s.GetComponent<UnityEngine.UI.Image>().sprite?.name)));
                        var image=sprite.GetComponent<UnityEngine.UI.Image>();
                        if(!image.sprite.name.StartsWith(unit.DefinitionId+"_",StringComparison.Ordinal))throw new Exception("Wrong species sprite.");
                        seen.Add(unit.DefinitionId+":"+unit.ActionState);
                    }
                }
            }
            if(view.Match.Phase!=MatchPhase.Result||view.RoundFlow.Fault!=null)throw new Exception("Client round failed.");
            view.Match.Pool.AssertConservation(view.Match);rounds++;
        }
        foreach(string id in new[]{"slowpoke","mankey","weedle","chansey"})
            foreach(string state in new[]{"Moving","Attacking","Casting"})
                if(!seen.Contains(id+":"+state))throw new Exception("Unverified animation: "+id+":"+state);
        view.ResetRosterDemo();view.RoundLoop.StartCombat(view.Match.RoundNumber);view.RoundFlow.RefreshState();
        foreach(var unit in view.RoundLoop.Battle.Units)unit.SetVitals(unit.CurrentHP,unit.Stats.MaxEnergy);
        for(int i=0;i<17;i++)view.AdvanceRoundTime(1d/30);
        EditorApplication.isPaused=true;Canvas.ForceUpdateCanvases();
        return library.Animations.Length+" animations / "+frames+" frame references verified; "+rounds+
            " client rounds (ranks 1/2/3) passed; all species idle advancement / 8 directions / death fade passed; observed "+string.Join(", ",seen.OrderBy(s=>s));
    }
}
