using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PokeChess.Client.Data;
using PokeChess.Core.Battle;
using PokeChess.Core.Board;
using PokeChess.Core.Match;
namespace PokeChess.Client.UI
{
    // One player for all skill visuals. Catalog entries contain every skill-specific choice.
    public sealed class SkillVisualBattleView:MonoBehaviour
    {
        private sealed class Visual
        {
            public SkillVisualEntry Entry;
            public UnityEngine.UI.Image Image;
            public double Start,End=double.PositiveInfinity;
            public string Follow;
            public string Unit;
            public Vector2 From,To;
            public bool MoveAlongArea;
            public bool Continuous,Used;
            public bool Snapshot;
            public Color Tint=Color.white;
            public float Strength=1;
        }
        private sealed class AfterimageRequest
        {public SkillVisualEntry Entry;public string Unit;public long Tick,Movement;public Vector2[] Path;public double TravelTicks;public int Captured;public float Height;}
        private readonly List<AfterimageRequest> afterimages=new List<AfterimageRequest>();
        private sealed class Lunge {public string Unit;public Vector2 Contact,Landing;public long Start;public float Seconds;}
        private readonly List<Lunge> lunges=new List<Lunge>();
        private sealed class BoltVisual
        {public SkillVisualEntry Entry;public LightningLineGraphic Graphic;public string Source,Target;public double Start;public int Seed;}
        private readonly List<BoltVisual> bolts=new List<BoltVisual>();
        private SkillVisualCatalog catalog;
        private object context;
        private LocalPairBattle localPair;
        private Func<string,UnitActionRuntime> localRuntime;
        private Func<int,int,Vector2> point;
        private SkillVisualUnit[] units=Array.Empty<SkillVisualUnit>();
        private CombatProjectileView[] projectiles=Array.Empty<CombatProjectileView>();
        private CombatPathProjectileView[] paths=Array.Empty<CombatPathProjectileView>();
        private CombatGroundView[] grounds=Array.Empty<CombatGroundView>();
        public static bool UsesProjectileVisual(SkillVisualCatalog catalog,string skill,int step,int iteration=0)
            =>catalog!=null&&catalog.Entries.Any(e=>e.Matches(SkillVisualTrigger.ProjectileFlight,skill,null,step,iteration));
        private readonly Dictionary<long,string> casts=new Dictionary<long,string>();
        private readonly Dictionary<long,string> castTargets=new Dictionary<long,string>();
        private readonly HashSet<string> seen=new HashSet<string>();
        private readonly Dictionary<string,Visual> continuous=new Dictionary<string,Visual>();
        private readonly List<Visual> visuals=new List<Visual>();
        private int rate=30;
        private float pixelScale=1.5f;
        private double lastTick=-1;
        private readonly StormBattleVisuals storm=new StormBattleVisuals();
        private readonly TorrentBattleVisuals torrent = new TorrentBattleVisuals();
        private readonly FlameBattleVisuals flame = new FlameBattleVisuals();
        private IEnumerable<(int column,int row)> flameTiles=Array.Empty<(int,int)>();
        private readonly GuardianBattleVisuals guardian = new GuardianBattleVisuals();
        private void GuardianSignal(string target, string clip, long tick)
        {
            if(target!=null && clip!=null)guardian.Trigger(transform,target,clip,tick);
        }
        public static SkillVisualBattleView Get(Transform board,SkillVisualCatalog catalog)
        {
            var view=board.GetComponent<SkillVisualBattleView>()??board.gameObject.AddComponent<SkillVisualBattleView>();
            view.Configure(catalog);return view;
        }
        public void Configure(SkillVisualCatalog value)
        {if(catalog==value)return;Reset();catalog=value;}
        private void Context(object value)
        {if(Equals(context,value))return;Reset();context=value;}
        public void RenderLocal(LocalPairBattle pair,double tick,Func<int,int,Vector2> project,float scale=1.5f)
        {
            Context(pair.Battle);point=project;pixelScale=scale;rate=pair.Battle.TickRate;
            if(localPair!=pair){Unsubscribe();localPair=pair;localPair.Stepped+=CaptureLocal;}
            localRuntime=pair.GetActionRuntime;
            units=SkillVisualSnapshot.Local(pair,tick,point);
            CaptureLocal(pair.Battle);Render(tick);
        }
        private void CaptureLocal(BattleState battle)
        {
            if(localRuntime==null||point==null)return;
            flameTiles=battle.Synergies.FireTiles.Select(p=>(p.Column,p.Row));
            foreach(var e in battle.Synergies.EventsThisTick)
                if(e.TraitId==SynergyTraits.Flame && e.Cell.HasValue)
                    flame.Trigger(transform,(e.Cell.Value.Column,e.Cell.Value.Row),e.Detail,e.Tick);
            foreach(var e in battle.Synergies.EventsThisTick)
                if(e.TraitId==SynergyTraits.Guardian)
                    GuardianSignal(e.TargetId,e.Detail=="FIRST_HIT"?"first-hit":e.Detail=="GUARD_START"?"battle-guard":null,battle.CurrentTick);
            foreach(var e in battle.Shields.EventsThisTick)
                if(e.Shield.Group==SynergyTraits.Guardian)
                    GuardianSignal(e.Shield.TargetId,GuardianBattleVisuals.ShieldClip(e.Kind.ToString(),e.Shield.Remaining),e.Tick);
            foreach(var e in battle.Synergies.EventsThisTick)
                if(e.TraitId==SynergyTraits.Storm)storm.Signal(transform,e.Detail,e.SourceId,e.TargetId,(int)e.Value,e.Tick);
            foreach(var e in battle.Synergies.EventsThisTick)
                if(e.TraitId==SynergyTraits.Torrent)
                {
                    if(e.Cell.HasValue)torrent.Tile(transform,e.Detail,e.Cell.Value.Column,e.Cell.Value.Row,(int)e.Value,e.Tick,e.TargetId,e.Destination.HasValue?(e.Destination.Value.Column,e.Destination.Value.Row):((int,int)?)null);
                    else if(e.Detail=="MANA"&&e.Value>0)torrent.Mana(transform,e.TargetId,e.Tick);
                }
            if(catalog==null)return;
            units=SkillVisualSnapshot.Local(battle,localRuntime,battle.CurrentTick,point);
            projectiles=battle.Projectiles.Active.Select(OnlineCombatRuntime.Projectile).ToArray();
            paths=battle.PathProjectiles.Active.Select(OnlineCombatRuntime.PathProjectile).ToArray();
            grounds=battle.WorldEffects.Grounds.Select(g=>new CombatGroundView{id=g.Id,source=g.SourceId,status=g.StatusId,startTick=g.StartTick,endTick=g.EndTick,
                column=g.Center.Column,row=g.Center.Row,radius=g.Radius}).ToArray();
            foreach(var e in battle.Projectiles.EventsThisTick)if(e.Kind==ProjectileEventKind.Hit&&e.Projectile.SkillId!=null) {
                var p=e.Projectile;
                Signal(SkillVisualTrigger.ProjectileHit,p.SkillId,null,p.SourceId,p.TargetId,p.CastId,p.StepIndex,p.Iteration,e.Tick);
            }
            foreach(var e in battle.SkillEventsThisTick)
            {
                casts[e.CastId]=e.SkillId;
                if(e.TargetId!=null)castTargets[e.CastId]=e.TargetId;
                if(e.Kind==SkillEventKind.CastStarted)Signal(SkillVisualTrigger.CastStarted,e.SkillId,null,e.UnitId,e.TargetId,e.CastId,e.StepIndex,e.Iteration,e.Tick);
                if(e.Kind==SkillEventKind.EffectApplied)Signal(SkillVisualTrigger.CastEffect,e.SkillId,null,e.UnitId,e.TargetId,e.CastId,e.StepIndex,e.Iteration,e.Tick);
            }
            foreach(var e in battle.SkillEffectEventsThisTick)if(e.Outcome==SkillEffectOutcome.Applied)
            {
                if(e.Type==SkillEffectType.Damage)Signal(SkillVisualTrigger.Damage,e.SkillId,null,e.CasterId,e.TargetId,e.CastId,e.StepIndex,e.Iteration,e.Tick,conditionMatched:e.ConditionMatched,effectIndex:e.EffectIndex);
                if(e.Type==SkillEffectType.Heal)Signal(SkillVisualTrigger.Heal,e.SkillId,null,e.CasterId,e.TargetId,e.CastId,e.StepIndex,e.Iteration,e.Tick);
                if(e.Type==SkillEffectType.Shield)Signal(SkillVisualTrigger.Shield,e.SkillId,null,e.CasterId,e.TargetId,e.CastId,e.StepIndex,e.Iteration,e.Tick);
                if(e.Type==SkillEffectType.HealthCost&&e.ActualValue>0)Signal(SkillVisualTrigger.HealthCost,e.SkillId,null,e.CasterId,e.TargetId,e.CastId,e.StepIndex,e.Iteration,e.Tick);
            }
            foreach(var d in battle.DamageResultsThisTick)if(d.Request.IsBasicAttack&&d.PostMitigationDamage>0)SplashHit(d.Request.SourceId,d.Request.TargetId,battle.CurrentTick);
            foreach(var award in battle.SkillProcs.GoldAwardsThisTick)
                Signal(SkillVisualTrigger.GoldAwarded,"bounty",null,award.Key,award.Key,battle.CurrentTick,-1,0,battle.CurrentTick);
            foreach(var e in battle.SkillAreasThisTick)
                if(casts.TryGetValue(e.CastId,out var skill))Signal(SkillVisualTrigger.Area,skill,null,e.CasterId,null,e.CastId,e.StepIndex,e.Iteration,battle.CurrentTick,
                    e.Area.Cells.Select(c=>point(c.Column,c.Row)),point(e.Area.Aim.Column,e.Area.Aim.Row)-point(e.Area.Origin.Column,e.Area.Origin.Row),
                    areaCenter:point(e.Area.Center.Column,e.Area.Center.Row));
            foreach(var e in battle.StatusEffects.EventsThisTick)
            {
                var s=e.Effect;
                if(e.Kind==StatusEventKind.Proc)
                    Signal(SkillVisualTrigger.StatusProc,null,s.Definition.Id,s.SourceId,s.TargetId,s.InstanceId,-1,0,e.Tick);
                if(e.Kind==StatusEventKind.Pulse)
                    Signal(SkillVisualTrigger.StatusPulse,null,s.Definition.Id,s.SourceId,s.TargetId,s.InstanceId,-1,0,e.Tick);
                if(e.Kind==StatusEventKind.Applied||e.Kind==StatusEventKind.Refreshed)
                    Signal(SkillVisualTrigger.StatusApplied,null,s.Definition.Id,s.SourceId,s.TargetId,s.InstanceId,-1,0,e.Tick);
                if(e.Kind==StatusEventKind.Expired||e.Kind==StatusEventKind.Removed)
                    Signal(SkillVisualTrigger.StatusEnded,null,s.Definition.Id,s.SourceId,s.TargetId,s.InstanceId,-1,0,e.Tick);
            }
            foreach(var e in battle.Movement.EventsThisTick.Where(e=>e.Kind==CombatMovementEventKind.Cancelled||e.Kind==CombatMovementEventKind.Blocked))
                afterimages.RemoveAll(r=>r.Movement==e.Movement.Id);
            foreach(var e in battle.Movement.EventsThisTick)if((e.Kind==CombatMovementEventKind.Arrived||e.Kind==CombatMovementEventKind.Started)&&(casts.TryGetValue(e.Movement.CastId,out var movementSkill)||e.Movement.Kind==SkillMovementKind.TeleportSafe))
            {
                var trigger=e.Kind==CombatMovementEventKind.Started?SkillVisualTrigger.MovementStarted:SkillVisualTrigger.MovementArrived;
                Signal(trigger,movementSkill??"skill-abra",null,e.Movement.UnitId,e.Movement.UnitId,
                    e.Movement.Id,e.Movement.StepIndex,e.Movement.Iteration,e.Tick,movementKind:e.Movement.Kind,movementPosition:e.Movement.Path.Count>0?point((trigger==SkillVisualTrigger.MovementStarted?e.Movement.Path.First():e.Movement.Path.Last()).Column,(trigger==SkillVisualTrigger.MovementStarted?e.Movement.Path.First():e.Movement.Path.Last()).Row):(Vector2?)null);
                QueueAfterimages(movementSkill,e.Movement.Id,e.Movement.CastId,e.Movement.StepIndex,e.Movement.Iteration,
                    e.Movement.UnitId,e.Movement.Kind,e.Tick,e.Movement.Path.Select(p=>point(p.Column,p.Row)).ToArray(),trigger,
                    e.Movement.Kind==SkillMovementKind.JumpToTarget?e.Movement.EndTick:e.Movement.DepartureTick);
            }
        }
        public void CaptureSimulation(BattleState battle,BattleSimulation simulation,Func<int,int,Vector2> project,float scale=1.5f)
        {
            Context(battle);point=project;pixelScale=scale;rate=battle.TickRate;localRuntime=simulation.GetRuntime;
            CaptureLocal(battle);
        }
        public void RenderSimulation(BattleState battle,BattleSimulation simulation,double tick,Func<int,int,Vector2> project,float scale=1.5f)
        {
            CaptureSimulation(battle,simulation,project,scale);
            units=SkillVisualSnapshot.Local(battle,simulation.GetRuntime,tick,point);Render(tick);
        }
        public void PlaybackEvent(CombatEventView e,CombatFrame frame,Func<int,int,Vector2> project,float scale=1.5f)
        {
            Context(frame.battleId);point=project;pixelScale=scale;rate=frame.tickRate;
            if(e.kind=="Synergy"&&e.detail?.StartsWith("storm:",StringComparison.Ordinal)==true)
                storm.Signal(transform,e.detail.Substring(6),e.source,e.target,(int)e.value,e.tick);
            if(e.kind=="SynergyTile"&&e.synergyCell!=null&&e.detail?.StartsWith("torrent:",StringComparison.Ordinal)==true)
                torrent.Tile(transform,e.detail.Substring(8),e.synergyCell.column,e.synergyCell.row,(int)e.value,e.tick,e.target,e.synergyDestination==null?((int,int)?)null:(e.synergyDestination.column,e.synergyDestination.row));
            if(e.kind=="Synergy"&&e.detail=="torrent:MANA"&&e.value>0&&e.target!=null)
                torrent.Mana(transform,e.target,e.tick);
            if(e.kind=="SynergyTile" && e.synergyCell!=null && e.detail?.StartsWith("flame:",StringComparison.Ordinal)==true)
                flame.Trigger(transform,(e.synergyCell.column,e.synergyCell.row),e.detail.Substring(6),e.tick);
            if(e.kind=="Synergy")
                GuardianSignal(e.target,e.detail=="guardian:FIRST_HIT"?"first-hit":e.detail=="guardian:GUARD_START"?"battle-guard":null,e.tick);
            if(e.detail==SynergyTraits.Guardian && e.kind?.StartsWith("Shield",StringComparison.Ordinal)==true)
                GuardianSignal(e.target,GuardianBattleVisuals.ShieldClip(e.kind.Substring(6),e.value),e.tick);
            if(catalog==null)return;
            units=SkillVisualSnapshot.Playback(frame,e.tick,point);
            if(e.kind=="ProjectileHit"&&e.projectile!=null) {
                var p=e.projectile;
                Signal(SkillVisualTrigger.ProjectileHit,p.skillId,null,p.source,p.target,p.castId,p.stepIndex,p.iteration,e.tick);
            }
            if(e.kind=="SkillGoldAwarded") {
                Signal(SkillVisualTrigger.GoldAwarded,"bounty",null,e.source,e.target,e.tick,-1,0,e.tick);return;
            }
            foreach(var unit in units)if(unit.Cast!=0&&unit.Skill!=null&&!casts.ContainsKey(unit.Cast))casts[unit.Cast]=unit.Skill;
            if(e.kind!=null&&e.kind.StartsWith("Skill",StringComparison.Ordinal)&&e.detail!=null)casts[e.castId]=e.detail;
            if((e.kind=="SkillCastStarted"||e.kind=="SkillEffectApplied")&&e.target!=null)castTargets[e.castId]=e.target;
            casts.TryGetValue(e.castId,out var skill);
            if(e.kind=="Damage"&&e.isBasicAttack&&e.detail!="Miss")SplashHit(e.source,e.target,e.tick);
            if(e.kind=="SkillCastStarted")Signal(SkillVisualTrigger.CastStarted,skill,null,e.source,e.target,e.castId,e.stepIndex,e.iteration,e.tick);
            if(e.kind=="SkillEffectApplied")Signal(SkillVisualTrigger.CastEffect,skill,null,e.source,e.target,e.castId,e.stepIndex,e.iteration,e.tick);
            if(e.detail=="Applied")
            {
                if(e.kind=="EffectDamage")Signal(SkillVisualTrigger.Damage,skill,null,e.source,e.target,e.castId,e.stepIndex,e.iteration,e.tick,conditionMatched:e.conditionMatched,effectIndex:e.effectIndex);
                if(e.kind=="EffectHeal")Signal(SkillVisualTrigger.Heal,skill,null,e.source,e.target,e.castId,e.stepIndex,e.iteration,e.tick);
                if(e.kind=="EffectShield")Signal(SkillVisualTrigger.Shield,skill,null,e.source,e.target,e.castId,e.stepIndex,e.iteration,e.tick);
                if(e.kind=="EffectHealthCost"&&e.value>0)Signal(SkillVisualTrigger.HealthCost,skill,null,e.source,e.target,e.castId,e.stepIndex,e.iteration,e.tick);
            }
            if(e.kind=="SkillArea"&&e.area?.cells!=null)Signal(SkillVisualTrigger.Area,skill,null,e.source,e.target,e.castId,e.stepIndex,e.iteration,e.tick,
                e.area.cells.Select(c=>point(c.column,c.row)),e.area.aim!=null&&e.area.origin!=null?
                    point(e.area.aim.column,e.area.aim.row)-point(e.area.origin.column,e.area.origin.row):(Vector2?)null,
                areaCenter:e.area.center!=null?point(e.area.center.column,e.area.center.row):(Vector2?)null);
            if(e.kind=="StatusApplied"||e.kind=="StatusRefreshed")Signal(SkillVisualTrigger.StatusApplied,null,e.detail,e.source,e.target,e.sequence,-1,0,e.tick);
            if(e.kind=="StatusPulse")Signal(SkillVisualTrigger.StatusPulse,null,e.detail,e.source,e.target,e.effectId,-1,0,e.tick);
            if(e.kind=="StatusProc")Signal(SkillVisualTrigger.StatusProc,null,e.detail,e.source,e.target,e.effectId,-1,0,e.tick);
            if(e.kind=="StatusExpired"||e.kind=="StatusRemoved")Signal(SkillVisualTrigger.StatusEnded,null,e.detail,e.source,e.target,e.sequence,-1,0,e.tick);
            if((e.kind=="MovementCancelled"||e.kind=="MovementBlocked")&&e.movement!=null)afterimages.RemoveAll(r=>r.Movement==e.movement.id);
            if((e.kind=="MovementArrived"||e.kind=="MovementStarted")&&e.movement?.path!=null)
            {
                var trigger=e.kind=="MovementStarted"?SkillVisualTrigger.MovementStarted:SkillVisualTrigger.MovementArrived;
                Signal(trigger,skill??(e.movement.kind==(int)SkillMovementKind.TeleportSafe?"skill-abra":null),null,e.movement.unit,e.movement.unit,e.movement.id,e.stepIndex,e.iteration,e.tick,
                    movementKind:(SkillMovementKind)e.movement.kind,movementPosition:e.movement.path.Length>0?point((trigger==SkillVisualTrigger.MovementStarted?e.movement.path.First():e.movement.path.Last()).column,(trigger==SkillVisualTrigger.MovementStarted?e.movement.path.First():e.movement.path.Last()).row):(Vector2?)null);
                QueueAfterimages(skill,e.movement.id,e.castId,e.stepIndex,e.iteration,e.movement.unit,(SkillMovementKind)e.movement.kind,e.tick,
                    e.movement.path.Select(p=>point(p.column,p.row)).ToArray(),trigger,
                    e.movement.kind==(int)SkillMovementKind.JumpToTarget?e.movement.endTick:e.movement.departureTick);
            }
        }
        public void RenderPlayback(CombatFrame frame,double tick,Func<int,int,Vector2> project,float scale=1.5f)
        {
            Context(frame.battleId);point=project;pixelScale=scale;rate=frame.tickRate;
            if(lastTick>tick)ResetVisuals();
            foreach(var e in frame.events??Array.Empty<CombatEventView>())PlaybackEvent(e,frame,project,scale);
            units=SkillVisualSnapshot.Playback(frame,tick,point);projectiles=frame.projectiles??Array.Empty<CombatProjectileView>();
            paths=frame.pathProjectiles??Array.Empty<CombatPathProjectileView>();grounds=frame.grounds??Array.Empty<CombatGroundView>();
            flameTiles=(frame.synergyFire??Array.Empty<CombatCellView>()).Select(p=>(p.column,p.row));
            Render(tick);
        }
        private void SplashHit(string caster,string target,long tick)
        {
            var source=Find(caster);var buff=source?.Statuses.FirstOrDefault(s=>s.behavior==(int)StatusBehavior.AttackSplash&&s.startTick<=tick&&s.endTick>tick);if(buff==null)return;
            Signal(SkillVisualTrigger.AttackSplash,"skill-nidoking",null,caster,target,buff.id,-1,unchecked((int)tick),tick);
        }
        private void Signal(SkillVisualTrigger trigger,string skill,string status,string caster,string target,long cast,int step,int iteration,long tick,IEnumerable<Vector2> cells=null,Vector2? direction=null,SkillMovementKind? movementKind=null,Vector2? areaCenter=null,bool conditionMatched=false,Vector2? movementPosition=null,int effectIndex=-1)
        {
            if(catalog==null)return;
            for(int i=0;i<catalog.Entries.Length;i++)
            {
                var entry=catalog.Entries[i];
                if(entry.EffectIndex>=0&&entry.EffectIndex!=effectIndex)continue;
                if(entry.NumericCondition==1&&!conditionMatched||entry.NumericCondition==2&&conditionMatched)continue;
                if(!entry.Matches(trigger,skill,status,step,iteration)||(!entry.Lightning&&entry.Frames.Length==0))continue;
                if((trigger==SkillVisualTrigger.MovementArrived||trigger==SkillVisualTrigger.MovementStarted)&&entry.MovementKind!=movementKind)continue;
                var source=Find(caster);var destination=Find(target);
                if(entry.Lightning) {
                    string origin=caster;
                    if(entry.LightningFromPrimary&&castTargets.TryGetValue(cast,out var primary)&&target!=primary) {
                        origin=primary;source=Find(primary);
                    }
                    if(source==null||destination==null||!seen.Add("bolt:"+i+":"+cast+":"+step+":"+iteration+":"+target))continue;
                    var node=new GameObject("SkillVisual_"+entry.Name,typeof(RectTransform),typeof(LightningLineGraphic));node.transform.SetParent(transform,false);
                    bolts.Add(new BoltVisual{Entry=entry,Graphic=node.GetComponent<LightningLineGraphic>(),Source=origin,Target=target,
                        Start=tick+entry.DelaySeconds*rate,Seed=unchecked((int)cast)});
                    continue;
                }
                IEnumerable<Vector2> positions;
                string follow=null;
                var pathCells=entry.Placement==SkillVisualPlacement.AreaPath?cells?.ToArray():null;
                if(entry.Placement==SkillVisualPlacement.AreaPath) {
                    if(source==null||pathCells==null||pathCells.Length==0)continue;
                    positions=new[]{source.Position};
                }
                else if(entry.Placement==SkillVisualPlacement.TargetNeighbor) {
                    if(destination==null)continue;
                    var origin=new PokeChess.Core.Pokemon.BoardPosition(destination.Column,destination.Row);
                    if(!HexCoordinates.TryToOffset(HexCoordinates.Neighbor(HexCoordinates.ToAxial(origin),entry.RingIndex),out var neighbor)||!HexBoardBounds.Combat.Contains(neighbor))continue;
                    positions=new[]{point(neighbor.Column,neighbor.Row)};
                }
                else if(entry.Placement==SkillVisualPlacement.BoardCells)
                    positions=Enumerable.Range(0,8).SelectMany(r=>Enumerable.Range(0,7).Select(c=>point(c,r)));
                else if(entry.Placement==SkillVisualPlacement.AreaCells)positions=cells??Array.Empty<Vector2>();
                else
                {
                    var anchor=entry.Placement==SkillVisualPlacement.Caster||entry.Placement==SkillVisualPlacement.CasterRing?source:destination;
                    if(anchor==null)continue;
                    positions=new[]{movementPosition??anchor.Position};if(entry.FollowUnit)follow=anchor.Id;
                }
                int cell=0;
                foreach(var position in positions.Distinct())
                {
                    int cellIndex=cell++;
                    if(entry.Placement==SkillVisualPlacement.AreaCells&&entry.ExcludeAreaCenter&&areaCenter.HasValue&&position==areaCenter.Value)continue;
                    if(entry.Placement==SkillVisualPlacement.AreaCells&&entry.AreaCellIndex>=0&&entry.AreaCellIndex!=cellIndex)continue;
                    string key=i+":"+cast+":"+step+":"+iteration+":"+target+":"+cellIndex;
                    if(trigger==SkillVisualTrigger.StatusPulse||trigger==SkillVisualTrigger.StatusProc)key+=":"+tick;
                    if(!seen.Add(key))continue;
                    float distanceDelay=0;
                    if(entry.AreaDistanceDelaySeconds>0&&entry.Placement==SkillVisualPlacement.AreaCells&&source!=null) {
                        for(int r=0;r<8;r++)for(int c=0;c<7;c++)if(point(c,r)==position)
                            distanceDelay=Math.Max(0,HexCoordinates.Distance(new PokeChess.Core.Pokemon.BoardPosition(source.Column,source.Row),new PokeChess.Core.Pokemon.BoardPosition(c,r))-1)*entry.AreaDistanceDelaySeconds;
                    }
                    var visual=Create(entry,tick+Math.Max(0,entry.DelaySeconds+distanceDelay)*rate);visual.Follow=follow;
                    if(entry.DropFromAboveCells>0){visual.MoveAlongArea=true;visual.From=position+Vector2.up*entry.DropFromAboveCells*Vector2.Distance(point(0,0),point(1,0));visual.To=position;}
                    if((entry.Placement==SkillVisualPlacement.TargetToCaster||entry.Placement==SkillVisualPlacement.CasterToTarget)&&source!=null&&destination!=null){visual.MoveAlongArea=true;visual.From=entry.Placement==SkillVisualPlacement.TargetToCaster?destination.Position:source.Position;visual.To=entry.Placement==SkillVisualPlacement.TargetToCaster?source.Position:destination.Position;}
                    if(entry.Placement==SkillVisualPlacement.AreaPath){visual.MoveAlongArea=true;visual.From=source.Position;visual.To=pathCells.Last();}
                    visual.Unit=entry.Placement==SkillVisualPlacement.Caster?caster:target;
                    if(entry.Placement==SkillVisualPlacement.AreaCells&&areaCenter.HasValue&&position==areaCenter.Value)
                        visual.Image.rectTransform.sizeDelta*=entry.AreaCenterScale;
                    visual.Image.rectTransform.anchoredPosition=position+entry.Offset*OffsetScale(entry);
                    if(entry.Placement==SkillVisualPlacement.Target&&source!=null&&entry.TowardCasterCells>0) {
                        var toward=source.Position-position;
                        float shift=Math.Min(toward.magnitude*.5f,entry.TowardCasterCells*Vector2.Distance(point(0,0),point(1,0)));
                        visual.Image.rectTransform.anchoredPosition+=toward.normalized*shift;
                    }
                    if(entry.Placement==SkillVisualPlacement.CasterRing) {
                        float angle=(90-entry.RingIndex*60)*Mathf.Deg2Rad;
                        visual.Image.rectTransform.anchoredPosition+=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*
                            entry.RingRadius*Vector2.Distance(point(0,0),point(1,0));
                    }
                    var facing=direction??(destination!=null&&source!=null?destination.Position-source.Position:Vector2.zero);
                    if(entry.Placement==SkillVisualPlacement.TargetToCaster)facing=-facing;
                    if(entry.AlignToDirection&&facing.sqrMagnitude>.001f)
                        visual.Image.rectTransform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(facing.y,facing.x)*Mathf.Rad2Deg);
                }
            }
        }
        internal static Vector2 GroundOffset(Func<int,int,Vector2> project)
            =>Vector2.down*(Vector2.Distance(project(0,0),project(1,0))*.22f);

        internal void SortCombatDepth(IEnumerable<RectTransform> fighters)
        {
            var ground=visuals.Where(v=>v.Image.enabled&&v.Entry.Trigger==SkillVisualTrigger.GroundActive)
                .Select(v=>v.Image.rectTransform);
            SortBoardDepth(transform,fighters,ground.Concat(flame.VisibleLayers).Concat(torrent.VisibleLayers));
        }

        internal static void SortBoardDepth(Transform parent,IEnumerable<RectTransform> fighters,IEnumerable<RectTransform> foreground=null)
        {
            var desired=fighters.Where(r=>r!=null&&r.parent==parent&&r.gameObject.activeSelf)
                .OrderByDescending(r=>r.anchoredPosition.y).ThenBy(r=>r.GetSiblingIndex())
                .Concat((foreground??Array.Empty<RectTransform>()).OrderBy(r=>r.GetSiblingIndex()))
                .ToArray();
            if(desired.Length<2)return;
            var slots=desired.Select(r=>r.GetSiblingIndex()).OrderBy(i=>i).ToArray();
            if(desired.Select(r=>r.GetSiblingIndex()).SequenceEqual(slots))return;
            var siblings=Enumerable.Range(0,parent.childCount).Select(parent.GetChild).ToArray();
            for(int i=0;i<slots.Length;i++)siblings[slots[i]]=desired[i];
            for(int i=0;i<siblings.Length;i++)
                if(siblings[i].GetSiblingIndex()!=i)siblings[i].SetSiblingIndex(i);
        }

        private void Render(double tick)
        {
            lastTick=tick;
            flame.Render(transform,flameTiles,point,tick,rate);
            torrent.Render(transform,units,point,tick,rate);
            storm.Render(units,tick,rate,point);
            guardian.Render(transform,units,tick,rate,pixelScale);if(catalog==null)return;
            foreach(var bolt in bolts.ToArray()) {
                double elapsed=(tick-bolt.Start)/Math.Max(1,rate);var source=Find(bolt.Source);var target=Find(bolt.Target);
                if(elapsed>=bolt.Entry.LightningSeconds||source==null||target==null){RemoveBolt(bolt);continue;}
                bolt.Graphic.enabled=elapsed>=0;if(elapsed<0)continue;
                float fade=Mathf.Clamp01((float)((bolt.Entry.LightningSeconds-elapsed)/.1));
                float cellWidth=Vector2.Distance(point(0,0),point(1,0));
                var end=target.Position+bolt.Entry.Offset;
                var start=bolt.Entry.OverheadLightning?end+Vector2.up*cellWidth*3:source.Position+bolt.Entry.Offset;
                if(bolt.Entry.OverheadLightning)fade*=elapsed<.07||elapsed>=.12&&elapsed<.19?1:.2f;
                bolt.Graphic.Sample(start,end,elapsed,
                    Vector2.Distance(point(0,0),point(1,0)),bolt.Entry.LightningWidth,bolt.Entry.Opacity*fade,bolt.Seed,
                    bolt.Entry.LightningGlow,bolt.Entry.LightningCore,bolt.Entry.StraightBeam,bolt.Entry.OverheadLightning);
            }
            foreach(var visual in continuous.Values)visual.Used=false;
            for(int index=0;index<catalog.Entries.Length;index++)
            {
                var entry=catalog.Entries[index];if(entry.Frames.Length==0)continue;
                if(entry.Trigger==SkillVisualTrigger.ShieldActive)
                foreach(var unit in units.Where(u=>u.Alive))
                foreach(var shield in unit.Shields.Where(s=>s.remaining>0&&s.endTick>tick&&s.group!=null&&entry.Matches(entry.Trigger,s.group.Split(':')[0],null,-1,0))) {
                    var visual=Continuous("shield:"+index+":"+unit.Id+":"+shield.id,entry,shield.endTick-entry.DurationSeconds*rate);
                    visual.End=shield.endTick;visual.Image.rectTransform.anchoredPosition=unit.Position+entry.Offset*OffsetScale(entry);
                }
                if(entry.Trigger==SkillVisualTrigger.PathProjectileFlight)
                foreach(var p in paths.Where(p=>p.path!=null&&p.nextIndex<p.path.Length&&entry.Matches(entry.Trigger,p.skill,null,p.stepIndex,p.iteration))) {
                    var visual=Continuous("path:"+index+":"+p.id,entry,p.spawnTick);
                    var from=p.nextIndex==0?point(p.column,p.row):point(p.path[p.nextIndex-1].column,p.path[p.nextIndex-1].row);
                    var to=point(p.path[p.nextIndex].column,p.path[p.nextIndex].row);
                    float t=Mathf.Clamp01((float)((tick-p.previousTick)/Math.Max(1,p.nextTick-p.previousTick)));
                    visual.Image.rectTransform.anchoredPosition=Vector2.Lerp(from,to,t)+entry.Offset*OffsetScale(entry);
                    if(entry.AlignToDirection)visual.Image.rectTransform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(to.y-from.y,to.x-from.x)*Mathf.Rad2Deg);
                }
                if(entry.Trigger==SkillVisualTrigger.GroundActive)
                foreach(var ground in grounds.Where(g=>g.startTick<=tick&&g.endTick>tick&&entry.Matches(entry.Trigger,null,g.status,-1,0))) {
                    var center=new PokeChess.Core.Pokemon.BoardPosition(ground.column,ground.row);
                    foreach(var cell in SkillGeometry.Cells(SkillAreaShape.Radius,center,center,ground.radius)) {
                        var visual=Continuous("ground:"+index+":"+ground.id+":"+cell.Column+":"+cell.Row,entry,ground.startTick);visual.End=ground.endTick;
                        visual.Image.rectTransform.anchoredPosition=point(cell.Column,cell.Row)+GroundOffset(point)+entry.Offset*OffsetScale(entry);
                    }
                }
                if(entry.Trigger==SkillVisualTrigger.ProjectileFlight)
                foreach(var p in projectiles.Where(p=>p.spawnTick<=tick&&p.arrivalTick>tick&&entry.Matches(entry.Trigger,p.skillId,null,p.stepIndex,p.iteration))) {
                    var visual=Continuous("projectile:"+index+":"+p.id,entry,p.spawnTick);
                    var from=point(p.column,p.row);var to=point(p.targetColumn,p.targetRow);
                    float t=Mathf.Clamp01((float)((tick-p.spawnTick)/Math.Max(1,p.arrivalTick-p.spawnTick)));
                    visual.Image.rectTransform.anchoredPosition=Vector2.Lerp(from,to,t)+entry.Offset*OffsetScale(entry);
                    if(entry.AlignToDirection)visual.Image.rectTransform.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(to.y-from.y,to.x-from.x)*Mathf.Rad2Deg);
                }
                if(entry.Trigger==SkillVisualTrigger.BeforeEffect)
                foreach(var unit in units)
                {
                    if(!unit.Alive||unit.Action!=CombatActionState.Casting||!entry.Matches(entry.Trigger,unit.Skill,null,-1,0))continue;
                    var target=Find(unit.Target);if(target==null||!target.Targetable)continue;
                    double length=Math.Min(entry.TravelSeconds,Math.Max(0,(unit.Effect-unit.Start)/(double)rate));
                    double remaining=(unit.Effect-tick)/rate;
                    if(length<=0||remaining<=0||remaining>length)continue;
                    var visual=Continuous("flight:"+index+":"+unit.Id,entry,unit.Effect-length*rate);
                    float progress=(float)(1-remaining/length);
                    var rect=visual.Image.rectTransform;
                    rect.anchoredPosition=Vector2.Lerp(unit.Position,target.Position,progress)+entry.Offset*OffsetScale(entry);
                    Vector2 direction=target.Position-unit.Position;
                    rect.localRotation=Quaternion.Euler(0,0,Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg);
                    visual.Image.sprite=entry.Frames[Math.Min(entry.Frames.Length-1,(int)(progress*entry.Frames.Length))];
                    visual.Image.enabled=true;
                }
                if(entry.Trigger==SkillVisualTrigger.StatusActive)
                foreach(var unit in units.Where(u=>u.Alive))
                {
                    var status=unit.Statuses.Where(s=>s.startTick<=tick&&s.endTick>tick&&entry.Matches(entry.Trigger,null,s.definition,-1,0))
                        .OrderByDescending(s=>s.endTick).FirstOrDefault();
                    if(status==null)continue;
                    var anchor=entry.Placement==SkillVisualPlacement.Caster?Find(status.source):unit;if(anchor==null)continue;
                    var visual=Continuous("status:"+index+":"+unit.Id,entry,status.startTick+Math.Max(0,entry.DelaySeconds)*rate);
                    visual.Unit=anchor.Id;
                    visual.End=status.endTick;
                    if(entry.FollowUnit||visual.Image.sprite==null)visual.Image.rectTransform.anchoredPosition=anchor.Position+entry.Offset*OffsetScale(entry);
                }
            }
            foreach(var key in continuous.Where(p=>!p.Value.Used).Select(p=>p.Key).ToArray())
            {Remove(continuous[key]);continuous.Remove(key);}
            foreach(var visual in visuals.ToArray())
            {
                var entry=visual.Entry;
                if(visual.Snapshot)
                {
                    float elapsed=(float)((tick-visual.Start)/Math.Max(1,rate));
                    if(elapsed>=entry.FadeSeconds){Remove(visual);continue;}
                    visual.Image.enabled=elapsed>=0;
                    visual.Image.color=new Color(visual.Tint.r,visual.Tint.g,visual.Tint.b,
                        visual.Tint.a*entry.Opacity*visual.Strength*Mathf.Clamp01(1-elapsed/Math.Max(.001f,entry.FadeSeconds)));
                    continue;
                }
                if(visual.Continuous&&entry.Trigger==SkillVisualTrigger.BeforeEffect)continue;
                double seconds=(tick-visual.Start)/Math.Max(1,rate);
                if(!visual.Continuous&&entry.DurationSeconds>0&&seconds>=entry.DurationSeconds){Remove(visual);continue;}
                if(visual.MoveAlongArea)visual.Image.rectTransform.anchoredPosition=Vector2.Lerp(visual.From,visual.To,
                    Mathf.Clamp01((float)(seconds/Math.Max(.001f,entry.DurationSeconds))))+entry.Offset*OffsetScale(entry);
                int frame=(int)(Math.Max(0,seconds)*entry.FramesPerSecond);
                if(!visual.Continuous&&entry.Playback==SkillVisualPlayback.Once&&frame>=entry.Frames.Length){Remove(visual);continue;}
                if(entry.Playback==SkillVisualPlayback.Loop)frame=(int)(Math.Max(0,seconds-entry.IntroFrames.Length/entry.FramesPerSecond)*entry.FramesPerSecond)%entry.Frames.Length;
                else if(entry.Playback==SkillVisualPlayback.HoldUntilEnd)
                {
                    int hold=Mathf.Clamp(entry.HoldFrame,0,entry.Frames.Length-1);frame=Math.Min(frame,hold);
                    double remaining=(visual.End-tick)/rate;
                    int tail=entry.Frames.Length-hold-1;
                    if(remaining<tail/entry.FramesPerSecond)frame=entry.Frames.Length-(int)Math.Ceiling(Math.Max(0,remaining)*entry.FramesPerSecond);
                }
                visual.Image.enabled=seconds>=0;
                visual.Image.sprite=entry.Frames[Mathf.Clamp(frame,0,entry.Frames.Length-1)];
                if(entry.Trigger==SkillVisualTrigger.GroundActive||entry.Trigger==SkillVisualTrigger.StatusActive||entry.IntroFrames.Length>0||entry.OutroFrames.Length>0) {
                    double remaining=(visual.End-tick)/rate;
                    if(entry.IntroFrames.Length>0&&seconds<entry.IntroFrames.Length/entry.FramesPerSecond)
                        visual.Image.sprite=entry.IntroFrames[Mathf.Clamp((int)(seconds*entry.FramesPerSecond),0,entry.IntroFrames.Length-1)];
                    else if(entry.OutroFrames.Length>0&&remaining<=entry.OutroFrames.Length/entry.FramesPerSecond)
                        visual.Image.sprite=entry.OutroFrames[Mathf.Clamp(entry.OutroFrames.Length-(int)Math.Ceiling(remaining*entry.FramesPerSecond),0,entry.OutroFrames.Length-1)];
                }
                if(visual.Follow!=null)
                {
                    var unit=Find(visual.Follow);
                    if(unit!=null)visual.Image.rectTransform.anchoredPosition=unit.Position+entry.Offset*OffsetScale(entry);
                }
            }
            var enabledVisuals=visuals.Where(v=>v.Image.enabled&&!string.IsNullOrEmpty(v.Entry.ExclusiveGroup)).ToArray();
            foreach(var visual in enabledVisuals)
                if(enabledVisuals.Any(other=>other!=visual&&other.Unit==visual.Unit&&
                    other.Entry.ExclusiveGroup==visual.Entry.ExclusiveGroup&&other.Entry.VisualPriority>visual.Entry.VisualPriority))
                    visual.Image.enabled=false;
            foreach(var visual in visuals)
            {
                if(visual.Snapshot)continue;
                var entry=visual.Entry;int overlaps=1;
                if(entry.CapOverlappingOpacity)overlaps=visuals.Count(v=>v.Entry==entry&&v.Image.enabled&&v.Image.rectTransform.anchoredPosition==visual.Image.rectTransform.anchoredPosition);
                float alpha=1-Mathf.Pow(1-Mathf.Clamp01(entry.Opacity),1f/Math.Max(1,overlaps));
                visual.Image.color=new Color(1,1,1,alpha);
            }
        }
        private void QueueAfterimages(string skill,long movement,long cast,int step,int iteration,string unit,SkillMovementKind kind,long tick,Vector2[] path,SkillVisualTrigger trigger=SkillVisualTrigger.MovementArrived,long departureTick=0)
        {
            if(catalog==null||path.Length==0)return;
            for(int i=0;i<catalog.Entries.Length;i++)
            {
                var entry=catalog.Entries[i];
                if(!entry.SpriteAfterimage||entry.MovementKind!=kind||!entry.Matches(trigger,skill,null,step,iteration))continue;
                var trail=kind==SkillMovementKind.CarryTarget&&path.Length>=3?path.Take(2).ToArray():path;
                SkillVisualUnit contact=null;
                if(entry.DashThroughTarget&&castTargets.TryGetValue(cast,out var targetId))contact=Find(targetId);
                if(contact!=null)trail=path.Concat(new[]{contact.Position,path.Last()}).ToArray();
                if(trail.Length<2||trail.All(p=>p==trail[0]))continue;
                if(!seen.Add("afterimage:"+i+":"+movement+":"+cast))continue;
                if(contact!=null){lunges.RemoveAll(l=>l.Unit==unit);lunges.Add(new Lunge{Unit=unit,Contact=contact.Position,Landing=path.Last(),Start=tick,Seconds=entry.LungeSeconds});}
                afterimages.Add(new AfterimageRequest{Entry=entry,Unit=unit,Tick=tick,Movement=movement,Path=trail,
                    TravelTicks=trigger==SkillVisualTrigger.MovementStarted?Math.Max(0,departureTick-tick):0,
                    Height=kind==SkillMovementKind.JumpToTarget?Vector2.Distance(point(0,0),point(1,0))*1.5f:0});
            }
        }
        private void LateUpdate(){UpdateSpriteVisibility();CaptureAfterimages();UpdateLunges();}
        public void UpdateLunges()
        {
            lunges.RemoveAll(l=>(lastTick-l.Start)/Math.Max(1,rate)>=l.Seconds||Find(l.Unit)?.Alive!=true);
            foreach(var actor in GetComponentsInChildren<PokemonSpriteView>()) {
                var lunge=lunges.FirstOrDefault(l=>l.Unit==actor.UnitInstanceId);
                float t=lunge==null?0:Mathf.Clamp01((float)((lastTick-lunge.Start)/Math.Max(1,rate)/Math.Max(.001f,lunge.Seconds)));
                float amount=t<.35f?t/.35f:t<=.65f?1:(1-t)/.35f;
                actor.SetSkillLungeOffset(lunge==null?Vector2.zero:(lunge.Contact-lunge.Landing)*amount);
            }
        }
        public void UpdateSpriteVisibility()
        {
            foreach(var actor in GetComponentsInChildren<PokemonSpriteView>())
            {
                var unit=Find(actor.UnitInstanceId);
                bool hidden=catalog!=null&&unit!=null&&unit.Alive&&catalog.Entries.Any(entry=>entry.HideUnitSprite&&
                    entry.Trigger==SkillVisualTrigger.StatusActive&&unit.Statuses.Any(status=>
                        status.startTick+Math.Max(0,entry.DelaySeconds)*rate<=lastTick&&status.endTick>lastTick&&
                        entry.Matches(entry.Trigger,null,status.definition,-1,0)));
                actor.SetSkillHidden(hidden);
                float scale=1;
                if(catalog!=null&&unit!=null&&unit.Alive)
                    foreach(var entry in catalog.Entries.Where(e=>e.ScaleUnitSprite&&e.Trigger==SkillVisualTrigger.StatusActive&&e.SpriteScaleSteps.Length>0))
                        foreach(var status in unit.Statuses.Where(s=>s.endTick>lastTick&&s.startTick+entry.DelaySeconds*rate<=lastTick&&entry.Matches(entry.Trigger,null,s.definition,-1,0))) {
                            double seconds=(lastTick-status.startTick)/rate-entry.DelaySeconds;
                            int step=Mathf.Clamp((int)Math.Floor((seconds+.000001)/Math.Max(.001,entry.SpriteScaleStepSeconds)),0,entry.SpriteScaleSteps.Length-1);
                            scale=Math.Min(scale,entry.SpriteScaleSteps[step]);
                        }
                actor.SetSkillScale(scale);
            }
        }
        public void CaptureAfterimages()
        {
            // Capture after the unit views have selected this frame's sprite, tint and ground offset.
            foreach(var request in afterimages.ToArray())
            {
                if(lastTick-request.Tick>=request.Entry.FadeSeconds*rate+request.TravelTicks){afterimages.Remove(request);continue;}
                var actor=GetComponentsInChildren<PokemonSpriteView>().FirstOrDefault(v=>v.UnitInstanceId==request.Unit&&
                    v.CharacterImage!=null&&v.CharacterImage.enabled&&v.CharacterImage.sprite!=null);
                var source=actor?.CharacterImage;
                if(source==null||!source.enabled||source.sprite==null)continue;
                int count=Mathf.Clamp(request.Entry.TrailCopies,1,16);
                for(int i=request.Captured;i<count;i++)
                {
                    float progress=i/(float)count;float along=progress*(request.Path.Length-1);int segment=Math.Min(request.Path.Length-2,(int)along);
                    double start=request.Tick+progress*request.TravelTicks;if(lastTick<start)break;
                    var visual=Create(request.Entry,start);visual.Snapshot=true;visual.Tint=source.color;visual.Strength=.35f+.65f*progress;
                    visual.Image.sprite=source.sprite;visual.Image.rectTransform.sizeDelta=source.rectTransform.sizeDelta;
                    visual.Image.rectTransform.pivot=source.rectTransform.pivot;
                    visual.Image.rectTransform.anchoredPosition=Vector2.Lerp(request.Path[segment],request.Path[segment+1],along-segment)+
                        Vector2.up*(4*progress*(1-progress)*request.Height)+source.rectTransform.anchoredPosition-actor.SkillLungeOffset;
                    visual.Image.color=new Color(source.color.r,source.color.g,source.color.b,source.color.a*request.Entry.Opacity*visual.Strength);
                    visual.Image.enabled=true;
                    // Keep the living sprite and its status/HUD children above the ghost.
                    visual.Image.transform.SetSiblingIndex(actor.transform.parent.GetSiblingIndex());
                    request.Captured++;
                }
                if(request.Captured>=count)afterimages.Remove(request);
            }
        }
        private float OffsetScale(SkillVisualEntry entry)=>entry.SizeMode==SkillVisualSize.Pixels?pixelScale:1;
        private SkillVisualUnit Find(string id)=>id==null?null:units.FirstOrDefault(u=>u.Id==id);
        private Visual Continuous(string key,SkillVisualEntry entry,double start)
        {
            if(!continuous.TryGetValue(key,out var visual)){visual=Create(entry,start);visual.Continuous=true;continuous.Add(key,visual);}
            visual.Start=start;visual.Used=true;return visual;
        }
        private Visual Create(SkillVisualEntry entry,double start)
        {
            var node=new GameObject("SkillVisual_"+entry.Name,typeof(RectTransform),typeof(UnityEngine.UI.Image));node.transform.SetParent(transform,false);
            var image=node.GetComponent<UnityEngine.UI.Image>();image.raycastTarget=false;image.enabled=false;
            float size=entry.SizeMode==SkillVisualSize.Pixels?entry.Size*pixelScale:entry.Size*Vector2.Distance(point(0,0),point(1,0));
            image.rectTransform.sizeDelta=Vector2.one*size;
            var visual=new Visual{Entry=entry,Image=image,Start=start,End=entry.DurationSeconds>0?start+entry.DurationSeconds*rate:double.PositiveInfinity};visuals.Add(visual);return visual;
        }
        private void Remove(Visual visual)
        {
            visuals.Remove(visual);
            if(visual.Image==null)return;
            visual.Image.enabled=false;
            if(Application.isPlaying)Destroy(visual.Image.gameObject);else DestroyImmediate(visual.Image.gameObject);
        }
        private void ResetVisuals()
        {
            foreach(var bolt in bolts.ToArray())RemoveBolt(bolt);
            foreach(var actor in GetComponentsInChildren<PokemonSpriteView>()){actor.SetSkillLungeOffset(Vector2.zero);actor.SetSkillHidden(false);actor.SetSkillScale(1);}
            lunges.Clear();
            foreach(var visual in visuals.ToArray())Remove(visual);continuous.Clear();guardian.Clear();flame.Clear();torrent.Clear();storm.Clear();flameTiles=Array.Empty<(int,int)>();casts.Clear();castTargets.Clear();seen.Clear();afterimages.Clear();lastTick=-1;
        }
        private void RemoveBolt(BoltVisual bolt)
        {
            bolts.Remove(bolt);if(bolt.Graphic==null)return;
            if(Application.isPlaying)Destroy(bolt.Graphic.gameObject);else DestroyImmediate(bolt.Graphic.gameObject);
        }
        private void Unsubscribe(){if(localPair!=null)localPair.Stepped-=CaptureLocal;localPair=null;localRuntime=null;}
        private void Reset(){Unsubscribe();ResetVisuals();context=null;units=Array.Empty<SkillVisualUnit>();}
        private void OnDisable()=>Reset();
    }
}
