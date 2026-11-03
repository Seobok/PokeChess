using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Content;
using PokeChess.Core.Battle;
using PokeChess.Core.Pokemon;
namespace PokeChess.Client.UI
{
    // The lab owns an isolated real simulation. No edits are written back to content assets.
    public sealed class EffectLabSession
    {
        public sealed class Placement
        {
            public string Id,Pokemon;public int Team;public UnitRank Rank;public BoardPosition Position;
        }
        private sealed class LabPolicy:SkillCombatBehaviorPolicy
        {
            private readonly EffectLabSession lab;
            public LabPolicy(EffectLabSession lab,SkillCatalog skills):base(skills){this.lab=lab;}
            public override bool CanMove(BattleState b,UnitCombatState u)=>lab.Movement;
            public override bool CanAttack(BattleState b,UnitCombatState u)=>lab.BasicAttacks;
            public override bool CanUseSkill(BattleState b,UnitCombatState u)=>u.UnitInstanceId==lab.CasterId&&(lab.Requested||lab.AutoCast);
            public override bool TryGetSkillDefinition(BattleState b,UnitCombatState u,out SkillDefinition skill)
                =>TryGetSkillById(lab.SkillId,out skill);
            public override void OnSkillStarted(BattleState b,UnitCombatState u,UnitCombatState target){lab.Requested=false;}
        }
        public RosterData Roster {get;}
        public BattleState Battle {get;private set;}
        public BattleSimulation Simulation {get;private set;}
        public List<Placement> Layout {get;}=new List<Placement>();
        public Queue<string> Log {get;}=new Queue<string>();
        public HashSet<BoardPosition> LastArea {get;}=new HashSet<BoardPosition>();
        public long LastAreaTick {get;private set;}=-100;
        public string CasterId {get;private set;}="caster";
        public string SkillId {get;set;}
        public bool BasicAttacks,Movement,AutoCast;
        public bool PreserveHealth {get;private set;}
        public void SetPreserveHealth(bool enabled){PreserveHealth=enabled;Battle.PreserveHealthForPreview=enabled;Changed?.Invoke();}
        public bool Requested {get;private set;}
        public event Action Changed;
        private readonly HashSet<string> logged=new HashSet<string>();
        private int nextId;
        public EffectLabSession(RosterData roster)
        {
            Roster=roster??throw new ArgumentNullException(nameof(roster));
            var caster=roster.Pokemon.FirstOrDefault(p=>p.Id=="caterpie")??roster.Pokemon.First();
            var target=roster.Pokemon.FirstOrDefault(p=>p.Id=="snorlax")??roster.Pokemon.Last();
            SkillId=caster.Skill.Id;
            Layout.Add(new Placement{Id=CasterId,Pokemon=caster.Id,Team=1,Rank=UnitRank.One,Position=new BoardPosition(2,3)});
            Layout.Add(new Placement{Id="target",Pokemon=target.Id,Team=2,Rank=UnitRank.One,Position=new BoardPosition(3,3)});
            Layout.Add(new Placement{Id="enemy2",Pokemon=target.Id,Team=2,Rank=UnitRank.One,Position=new BoardPosition(4,3)});
            Layout.Add(new Placement{Id="enemy3",Pokemon=target.Id,Team=2,Rank=UnitRank.One,Position=new BoardPosition(5,3)});
            Layout.Add(new Placement{Id="ally",Pokemon=caster.Id,Team=1,Rank=UnitRank.One,Position=new BoardPosition(1,3)});
            Reset();
        }
        public UnitCombatState Unit(string id)=>Battle.Units.FirstOrDefault(u=>u.UnitInstanceId==id);
        public void Reset()
        {
            var catalog=Roster.CreateCatalog();
            Battle=BattleStateFactory.Create("effect-lab",1,123,30,catalog,Layout.Select(p=>new BattleUnitSetup(
                catalog.CreateUnit(p.Id,p.Pokemon,p.Team==1?"lab-one":"lab-two",p.Rank,0,UnitPlacement.Unplaced),p.Team,p.Position)));
            Battle.PreserveHealthForPreview=PreserveHealth;
            Simulation=new BattleSimulation(Battle,new LabPolicy(this,Roster.CreateSkills()),statusCatalog:Roster.CreateStatuses());
            Requested=false;LastArea.Clear();LastAreaTick=-100;Log.Clear();logged.Clear();Note("배치를 초기화했습니다. 실제 전투 규칙으로 실행합니다.");Changed?.Invoke();
        }
        public void Step()
        {
            if(Battle.Result!=BattleResult.InProgress)return;
            if(AutoCast){var caster=Unit(CasterId);if(caster.IsAlive)caster.SetVitals(caster.CurrentHP,caster.Stats.MaxEnergy);}
            Simulation.Step();Capture();Changed?.Invoke();
        }
        public void Cast()
        {
            var caster=Unit(CasterId);
            if(!caster.IsAlive)throw new InvalidOperationException("시전자가 쓰러졌습니다. 다시 시작하세요.");
            if(Battle.Result!=BattleResult.InProgress)throw new InvalidOperationException("전투가 끝났습니다. 다시 시작하세요.");
            if(caster.Stats.MaxEnergy<=0)throw new InvalidOperationException("이 포켓몬은 시전에 필요한 최대 에너지가 없습니다.");
            caster.SetVitals(caster.CurrentHP,caster.Stats.MaxEnergy);Requested=true;Step();
            if(Requested)Note("시전 대기: 사거리·유효 대상·상태·현재 행동 조건을 확인하세요.");
        }
        public void ReplaceCaster(string pokemon)
        {
            Layout.Single(p=>p.Id==CasterId).Pokemon=pokemon;
            SkillId=Roster.Pokemon.Single(p=>p.Id==pokemon).Skill.Id;Reset();
        }
        public void SetCaster(string id)
        {if(!Layout.Any(p=>p.Id==id))throw new ArgumentException("Unknown unit");CasterId=id;SkillId=Roster.Pokemon.Single(p=>p.Id==Layout.Single(p=>p.Id==id).Pokemon).Skill.Id;Reset();}
        public string Add(string pokemon,int team,BoardPosition position)
        {
            if(Layout.Any(p=>p.Position.Equals(position)))throw new InvalidOperationException("빈 칸을 선택하세요.");
            string id="lab-"+(++nextId);Layout.Add(new Placement{Id=id,Pokemon=pokemon,Team=team,Rank=UnitRank.One,Position=position});Reset();return id;
        }
        public void Move(string id,BoardPosition position)
        {
            if(Layout.Any(p=>p.Id!=id&&p.Position.Equals(position)))throw new InvalidOperationException("빈 칸을 선택하세요.");
            Layout.Single(p=>p.Id==id).Position=position;Reset();
        }
        public void Remove(string id)
        {
            if(id==CasterId)throw new InvalidOperationException("시전자는 제거할 수 없습니다.");
            var placement=Layout.Single(p=>p.Id==id);
            if(!Layout.Any(p=>p.Team==placement.Team&&p.Id!=id))throw new InvalidOperationException("팀마다 최소 한 유닛을 유지하세요.");
            Layout.Remove(placement);Reset();
        }
        public void SetRank(string id,UnitRank rank){Layout.Single(p=>p.Id==id).Rank=rank;Reset();}
        public void ApplyStatus(string status,string targetId)
        {
            var definition=Roster.Statuses.Single(s=>s.Id==status).ToCore();var target=Unit(targetId);
            var source=definition.TargetTeam==StatusTargetTeam.Ally?target:Battle.Units.FirstOrDefault(u=>u.TeamId!=target.TeamId&&u.IsAlive&&u.IsOnBoard);
            if(source==null)throw new InvalidOperationException("상태를 부여할 반대 팀 유닛이 없습니다.");
            Battle.StatusEffects.Apply(definition,source.UnitInstanceId,targetId);Capture();Changed?.Invoke();
        }
        public void RemoveStatus(long instance){Battle.StatusEffects.Remove(instance);Capture();Changed?.Invoke();}
        public void ClearStatuses(string id)
        {foreach(var status in Battle.StatusEffects.Active.Where(s=>s.TargetId==id).ToArray())Battle.StatusEffects.Remove(status.InstanceId);Capture();Changed?.Invoke();}
        public void Hit(string targetId,float amount)
        {
            var target=Unit(targetId);var source=Battle.Units.FirstOrDefault(u=>u.TeamId!=target.TeamId&&u.IsAlive&&u.IsOnBoard);
            if(source==null)throw new InvalidOperationException("공격할 반대 팀 유닛이 없습니다.");
            new DamageProcessor().Apply(Battle,new DamageRequest(source.UnitInstanceId,targetId,amount,DamageType.Physical,isBasicAttack:true));Capture();Changed?.Invoke();
        }
        public void Heal(string id){var unit=Unit(id);if(!unit.IsAlive)throw new InvalidOperationException("쓰러진 유닛은 다시 시작으로 복원하세요.");Battle.Healing.Apply(unit,unit.Stats.MaxHP);Changed?.Invoke();}
        public void Note(string text)
        {Log.Enqueue((Battle.CurrentTick/(double)Battle.TickRate).ToString("0.00")+"s  "+text);while(Log.Count>120)Log.Dequeue();}
        private void Once(string key,string text){if(logged.Add(key))Note(text);}
        private void Capture()
        {
            foreach(var e in Battle.SkillEventsThisTick)Once("s"+e.CastId+e.Kind+e.StepIndex+e.Iteration,e.Kind+"  "+e.SkillId+" → "+e.TargetId);
            foreach(var e in Battle.SkillEffectEventsThisTick)Once("e"+e.CastId+":"+e.StepIndex+":"+e.Iteration+":"+e.EffectIndex+":"+e.TargetId,
                e.Type+"  "+e.TargetId+"  "+e.ActualValue.ToString("0.#")+" ("+e.Outcome+")");
            foreach(var e in Battle.DamageResultsThisTick)Once("d"+Battle.CurrentTick+":"+e.Request.SourceId+":"+e.Request.TargetId+":"+e.AppliedDamage+":"+e.RemainingHP,
                "피해 "+e.Request.TargetId+" -"+e.AppliedDamage.ToString("0.#")+" / HP "+e.RemainingHP.ToString("0.#"));
            foreach(var e in Battle.StatusEffects.EventsThisTick)Once("t"+e.Effect.InstanceId+":"+e.Kind+":"+e.Tick+":"+e.Effect.StackCount,
                e.Kind+"  "+e.Effect.TargetId+"  "+e.Effect.Definition.Id+" ×"+e.Effect.StackCount);
            if(Battle.SkillAreasThisTick.Count>0)
            {LastArea.Clear();foreach(var e in Battle.SkillAreasThisTick)LastArea.UnionWith(e.Area.Cells);LastAreaTick=Battle.CurrentTick;}
        }
    }
}
