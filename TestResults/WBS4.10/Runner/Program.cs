using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Battle;
using PokeChess.Core.Match;

class Program {
    static string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../"));
    static JsonSerializerOptions json=new JsonSerializerOptions{WriteIndented=true};
    static void Check(bool valid,string message){if(!valid)throw new Exception(message);}
    static void Save(string name,object value)=>File.WriteAllText(Path.Combine(root,name),JsonSerializer.Serialize(value,json));
    sealed class SkillOnly:SkillCombatBehaviorPolicy {
        public SkillOnly():base(PrototypeRoster.CreateSkills()){}
        public override bool CanMove(BattleState b,UnitCombatState u)=>false;
        public override bool CanAttack(BattleState b,UnitCombatState u)=>false;
        public override bool CanUseSkill(BattleState b,UnitCombatState u)=>u.UnitInstanceId=="caster";
    }
    static void Smoke() {
        var rows=new List<object>();var catalog=PrototypeRoster.CreateCatalog();var skills=PrototypeRoster.CreateSkills();
        foreach(var def in PrototypeRoster.CreateDefinitions())foreach(UnitRank rank in Enum.GetValues(typeof(UnitRank))) {
            BattleUnitSetup Setup(string id,string species,int team,int x,int y,UnitRank r)=>new BattleUnitSetup(
                catalog.CreateUnit(id,species,"p"+team,r,0,UnitPlacement.Unplaced),team,new BoardPosition(x,y));
            var b=BattleStateFactory.Create("content-smoke",1,123,30,catalog,new[]{Setup("caster",def.Id,1,0,0,rank),
                Setup("ally","slowpoke",1,0,1,UnitRank.One),Setup("enemy","slowpoke",2,1,0,UnitRank.Three)});
            var caster=b.Units.Single(u=>u.UnitInstanceId=="caster");var ally=b.Units.Single(u=>u.UnitInstanceId=="ally");
            ally.SetVitals(100,0);caster.SetVitals(caster.CurrentHP,caster.Stats.MaxEnergy);
            var sim=new BattleSimulation(b,new SkillOnly(),statusCatalog:PrototypeRoster.CreateStatuses());
            int starts=0,completed=0;var effects=new HashSet<int>();var values=new List<object>();
            for(int tick=0;tick<240;tick++) {
                sim.Step();starts+=b.SkillEventsThisTick.Count(e=>e.UnitId=="caster"&&e.Kind==SkillEventKind.CastStarted);
                completed+=b.SkillEventsThisTick.Count(e=>e.UnitId=="caster"&&e.Kind==SkillEventKind.CastCompleted);
                foreach(var e in b.SkillEffectEventsThisTick.Where(e=>e.CasterId=="caster"&&e.Outcome!=SkillEffectOutcome.Skipped)) {
                    effects.Add(e.EffectIndex);values.Add(new{e.EffectIndex,Type=e.Type.ToString(),e.CalculatedValue,e.ActualValue,Outcome=e.Outcome.ToString()});
                }
                Check(b.Units.All(u=>!float.IsNaN(u.CurrentHP)&&!float.IsInfinity(u.CurrentHP)&&u.CurrentHP>=0),"Invalid HP "+def.Id);
            }
            skills.TryGet(def.SkillId,out var skill);
            Check(starts==1&&completed==1&&effects.Count==skill.Effects.Count,"Skill lifecycle/effect failed "+def.Id+" R"+(int)rank);
            Check(b.StatusEffects.Active.Count==0,"Status expiration "+def.Id);
            rows.Add(new{Species=def.Id,Rank=(int)rank,HP=caster.Stats.MaxHP,AT=caster.Stats.Attack,Skill=def.SkillId,Starts=starts,Completed=completed,Effects=values,Passed=true});
        }
        Save("smoke-36.json",rows);Console.WriteLine("36 skill lifecycle / effect / expiration smoke cases passed.");
    }
    sealed class Coverage { public int Purchases{get;set;} public int Deployed{get;set;} public int ShadowDeployed{get;set;} public int Casts{get;set;} public int Effects{get;set;} }
    static Dictionary<string,Coverage> RunObserved(ulong seed,out LocalMatchSimulation run,bool level7=false) {
        var defs=PrototypeRoster.CreateDefinitions();run=new LocalMatchSimulation(new LocalSimulationSettings(seed),PrototypeRoster.CreateCatalog(),defs.Select(d=>d.Id),
            matchRules:level7?new MatchRules(startingLevel:7):null,skillCatalog:PrototypeRoster.CreateSkills(),statusCatalog:PrototypeRoster.CreateStatuses());
        var coverage=defs.ToDictionary(d=>d.Id,d=>new Coverage());var seen=new Dictionary<BattleState,long>();int lastRound=0,maxLevel=run.Match.Rules.StartingLevel;
        while(run.Status==SimulationStatus.Running) {
            run.Advance(1d/30,1);maxLevel=Math.Max(maxLevel,run.Match.Players.Max(p=>p.Level));
            foreach(var pair in run.Coordinator.Battles) {
                var b=pair.Battle;if(b==null)continue;
                if(!seen.TryGetValue(b,out long previous)) {
                    previous=-1;foreach(var unit in b.Units){if(pair.IsShadow)coverage[unit.DefinitionId].ShadowDeployed++;else coverage[unit.DefinitionId].Deployed++;}
                }
                if(b.CurrentTick==previous)continue;seen[b]=b.CurrentTick;
                foreach(var e in b.SkillEventsThisTick.Where(e=>e.Kind==SkillEventKind.CastStarted))coverage[b.Units.Single(u=>u.UnitInstanceId==e.UnitId).DefinitionId].Casts++;
                foreach(var e in b.SkillEffectEventsThisTick.Where(e=>e.Outcome!=SkillEffectOutcome.Skipped))coverage[b.Units.Single(u=>u.UnitInstanceId==e.CasterId).DefinitionId].Effects++;
            }
            if(lastRound!=run.Match.RoundNumber){lastRound=run.Match.RoundNumber;run.Match.Pool.AssertConservation(run.Match);}
        }
        Check(run.Status==SimulationStatus.Completed,"Incomplete seed "+seed+": "+run.Status+" "+run.Error);
        run.Match.Pool.AssertConservation(run.Match);
        Check(run.Match.FinalResult.Standings.Select(s=>s.Placement).SequenceEqual(Enumerable.Range(1,8)),"Standings "+seed);
        Check(run.Rounds.All(r=>r.Settled)&&run.Rounds.Count==run.Match.RoundNumber,"Unsettled rounds "+seed);
        Check(Math.Abs(run.Rounds.Sum(r=>r.PreparationSeconds+r.CombatSeconds+r.ResultSeconds)-run.Flow.MatchElapsedSeconds)<.00001,"Duration accounting "+seed);
        foreach(var entry in run.PurchaseCounts)coverage[entry.Key].Purchases=entry.Value;
        File.WriteAllText(Path.Combine(root,"seed-"+seed+".json"),SimulationReport.ToJson(run));Save("seed-"+seed+"-coverage.json",coverage);
        Save("seed-"+seed+"-settings.json",new{Scenario=level7?"Level7ContentCoverage":"DefaultRules",StartingLevel=run.Match.Rules.StartingLevel,MaxObservedLevel=maxLevel});
        Console.WriteLine("Seed "+seed+": "+run.Status+", R"+run.Match.RoundNumber+", winner "+run.Match.FinalResult.WinnerPlayerId+", "+run.Flow.MatchElapsedSeconds.ToString("0.0")+" game seconds");
        return coverage;
    }
    static string Signature(LocalMatchSimulation r)=>JsonSerializer.Serialize(new {r.Match.FinalResult,r.Rounds,r.Flow.MatchElapsedSeconds,r.PurchaseCounts});
    static void Main() {
        Directory.CreateDirectory(root);Smoke();var runs=new List<LocalMatchSimulation>();var aggregate=PrototypeRoster.CreateDefinitions().ToDictionary(d=>d.Id,d=>new Coverage());
        for(ulong seed=1;seed<=20;seed++) {
            var coverage=RunObserved(seed,out var run);runs.Add(run);
            foreach(var e in coverage){var c=aggregate[e.Key];c.Purchases+=e.Value.Purchases;c.Deployed+=e.Value.Deployed;c.ShadowDeployed+=e.Value.ShadowDeployed;c.Casts+=e.Value.Casts;c.Effects+=e.Value.Effects;}
        }
        Save("coverage-all.json",aggregate);File.WriteAllText(Path.Combine(root,"summary.csv"),SimulationReport.ToCsv(runs));
        Save("coverage-standard-20.json",aggregate);
        var contentRuns=new List<LocalMatchSimulation>();
        foreach(ulong seed in new ulong[]{101,102,103}) {
            var coverage=RunObserved(seed,out var run,true);contentRuns.Add(run);
            foreach(var e in coverage){var c=aggregate[e.Key];c.Purchases+=e.Value.Purchases;c.Deployed+=e.Value.Deployed;c.ShadowDeployed+=e.Value.ShadowDeployed;c.Casts+=e.Value.Casts;c.Effects+=e.Value.Effects;}
        }
        File.WriteAllText(Path.Combine(root,"content-coverage-summary.csv"),SimulationReport.ToCsv(contentRuns));Save("coverage-all.json",aggregate);
        Check(aggregate.All(e=>e.Value.Purchases>0&&e.Value.Deployed>0&&e.Value.Casts>0&&e.Value.Effects>0),"Missing full-match species coverage");
        var fast=new LocalMatchSimulation(new LocalSimulationSettings(1),PrototypeRoster.CreateCatalog(),PrototypeRoster.CreateDefinitions().Select(d=>d.Id),
            skillCatalog:PrototypeRoster.CreateSkills(),statusCatalog:PrototypeRoster.CreateStatuses());fast.RunToEnd();
        Check(fast.Status==SimulationStatus.Completed&&Signature(fast)==Signature(runs[0]),"Normal / fast determinism");
        Save("determinism.json",new{Seed=1,Passed=true,NormalFrameSeconds=1d/30,FastFrameSeconds=10,Winner=fast.Match.FinalResult.WinnerPlayerId,EndRound=fast.Match.RoundNumber});
        Console.WriteLine("All 12 species purchased / deployed / cast / effects observed; seed 1 normal vs fast identical.");
    }
}
