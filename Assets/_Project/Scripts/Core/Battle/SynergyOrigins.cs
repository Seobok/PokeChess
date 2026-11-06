using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Board;
using PokeChess.Core.Match;
namespace PokeChess.Core.Battle
{
    public sealed partial class SynergySystem
    {
        private sealed class Fire { public BoardPosition Tile; public UnitCombatState Source; public long End,Next; public bool Spread; }
        private sealed class PoisonZone { public UnitCombatState Source; public BoardPosition Tile; public long End,Next; public float Damage; }
        private readonly List<PoisonZone> poisonZones=new List<PoisonZone>();
        private readonly HashSet<string> poisonTransferred=new HashSet<string>();
        private readonly List<Fire> fires=new List<Fire>();
        private readonly HashSet<string> ignited=new HashSet<string>();
        private readonly Dictionary<string,string> circuits=new Dictionary<string,string>();
        private readonly Dictionary<string,int> charges=new Dictionary<string,int>();
        private readonly Dictionary<string,long> stormReady=new Dictionary<string,long>();
        private readonly Dictionary<int,List<BoardPosition>> rocks=new Dictionary<int,List<BoardPosition>>();
        private readonly HashSet<string> rockShielded=new HashSet<string>();
        private readonly Dictionary<string,string> passengers=new Dictionary<string,string>();
        private readonly Dictionary<string,bool> protectionForcedOnly=new Dictionary<string,bool>();
        private readonly HashSet<string> protectedOnce=new HashSet<string>();
        private readonly HashSet<string> uniqueFirst=new HashSet<string>();
        private readonly Dictionary<string,long> royalReady=new Dictionary<string,long>();
        private readonly HashSet<string> starterMarks=new HashSet<string>();
        private readonly HashSet<int> starterDone=new HashSet<int>();
        private readonly Dictionary<int,int> natureCasts=new Dictionary<int,int>();
        public IReadOnlyList<BoardPosition> RockTiles(int team)=>rocks.TryGetValue(team,out var r)?r.AsReadOnly():Array.Empty<BoardPosition>();
        public IReadOnlyList<BoardPosition> FireTiles=>Array.AsReadOnly(fires.Where(f=>f.End>battle.CurrentTick).Select(f=>f.Tile).Distinct().ToArray());
        private UnitCombatState Representative(int team,string species)=>battle.Units.Where(u=>u.TeamId==team&&SynergyTraits.Family(u.DefinitionId)==SynergyTraits.Family(species)).OrderByDescending(u=>u.EvolutionStage).ThenByDescending(u=>u.Rank).ThenBy(u=>u.UnitInstanceId,StringComparer.Ordinal).FirstOrDefault();
        private void InitializeOrigins()
        {
            foreach(int team in new[]{1,2})
            {
                int earth=TeamTier(team,SynergyTraits.Earth);
                var members=battle.Units.Where(u=>u.TeamId==team&&u.TraitIds.Contains(SynergyTraits.Earth)).OrderBy(u=>u.UnitInstanceId,StringComparer.Ordinal).ToArray();
                rocks[team]=new List<BoardPosition>();
                if(earth>0&&battle.PreparedRocks.TryGetValue(team,out var prepared))rocks[team].AddRange(prepared.Where(p=>battle.Board.Contains(p)&&!battle.Board.IsOccupied(p)).Distinct().Take(earth>=4?2:1));
                if(earth>0&&rocks[team].Count==0)foreach(var u in members)
                {
                    var tile=HexCoordinates.Neighbors(HexCoordinates.ToAxial(u.Position)).Select(a=>HexCoordinates.TryToOffset(a,out var p)?(BoardPosition?)p:null).Where(p=>p.HasValue&&battle.Board.Contains(p.Value)&&!battle.Board.IsOccupied(p.Value)&&!rocks[team].Contains(p.Value)).OrderBy(p=>p.Value.Row).ThenBy(p=>p.Value.Column).FirstOrDefault();
                    if(tile.HasValue)rocks[team].Add(tile.Value);if(rocks[team].Count>=(earth>=4?2:1))break;
                }
                var storm=battle.Units.Where(u=>u.TeamId==team&&Tier(u,SynergyTraits.Storm)>0).OrderBy(u=>u.UnitInstanceId,StringComparer.Ordinal).ToArray();
                foreach(var u in storm)
                {
                    if(circuits.ContainsKey(u.UnitInstanceId))continue;
                    string key=u.UnitInstanceId;var queue=new Queue<UnitCombatState>();queue.Enqueue(u);circuits[key]=key;
                    while(queue.Count>0){var at=queue.Dequeue();foreach(var next in storm.Where(v=>!circuits.ContainsKey(v.UnitInstanceId)&&HexCoordinates.Distance(at.Position,v.Position)==1)){circuits[next.UnitInstanceId]=key;queue.Enqueue(next);}}
                    charges[key]=0;
                }
                var lapras=Representative(team,"lapras");if(lapras!=null)AssignPassenger(lapras,false);
                var snorlax=Representative(team,"snorlax");if(snorlax!=null)AssignPassenger(snorlax,true);
                if(TeamTier(team,SynergyTraits.Torrent)==10)
                {
                    var source=battle.Units.First(u=>u.TeamId==team&&Tier(u,SynergyTraits.Torrent)>0);
                    RecordTorrent(source,new BoardPosition(3,team==1?4:3),"surge");
                    foreach(var enemy in Enemies(source).ToArray())
                    {
                        RecordTorrent(source,enemy.Position,"impact");
                        Control(source,enemy,CrowdControlKind.Stun,1.5f);
                        Push(source,enemy,2);
                    }
                }
                if(TeamTier(team,SynergyTraits.Nature)==10){var source=battle.Units.First(u=>u.TeamId==team&&Tier(u,SynergyTraits.Nature)>0);foreach(var enemy in Enemies(source).ToArray())Control(source,enemy,CrowdControlKind.Root,rules.Origins.WorldTreeRootSeconds);}
            }
        }
        private void AssignPassenger(UnitCombatState source,bool mountain)
        {
            var target=(!mountain&&source.PersistentUnit.SynergyTargetId!=null?Allies(source).Where(u=>u.UnitInstanceId==source.PersistentUnit.SynergyTargetId):Allies(source).Where(u=>u!=source&&HexCoordinates.Distance(u.Position,source.Position)==1&&(!mountain||(source.TeamId==1?u.Position.Row<source.Position.Row:u.Position.Row>source.Position.Row)))).OrderByDescending(u=>u.UnitInstanceId==source.PersistentUnit.SynergyTargetId).ThenBy(u=>u.UnitInstanceId,StringComparer.Ordinal).FirstOrDefault();
            if(target!=null){passengers[source.UnitInstanceId]=target.UnitInstanceId;protectionForcedOnly[source.UnitInstanceId]=mountain;}
        }
        internal bool PreventControl(UnitCombatState unit,bool forced=false)
        {
            if(!rules.Enabled)return false;
            if(forced&&SynergyTraits.Family(unit.DefinitionId)==SynergyTraits.Family("snorlax"))return true;
            foreach(var source in passengers.Keys.OrderBy(id=>id,StringComparer.Ordinal))if(passengers[source]==unit.UnitInstanceId&&!protectedOnce.Contains(source)&&(forced||!protectionForcedOnly[source])){protectedOnce.Add(source);Record("passenger",unit,unit,"IMMUNE",1);return true;}return false;
        }
        private float OriginIncoming(UnitCombatState u,DamageRequest request)
        {
            if(TeamTier(u.TeamId,SynergyTraits.Earth)==0)return 1;
            bool near=rocks[u.TeamId].Any(p=>HexCoordinates.Distance(p,u.Position)<=1);
            var source=battle.Units.First(x=>x.UnitInstanceId==request.SourceId);
            return near&&source.Stats.AttackDelivery==AttackDeliveryType.Projectile?1-rules.Origins.EarthReduction:1;
        }
        private void AdvanceOrigins()
        {
            foreach(var fire in fires.ToArray()) {
                while(fire.Next<=battle.CurrentTick&&fire.Next<=fire.End){fire.Next+=T(1);foreach(var enemy in battle.Units.Where(e=>e.TeamId!=fire.Source.TeamId&&e.IsAreaTargetable&&e.Position.Equals(fire.Tile)).ToArray())Deal(fire.Source,enemy,rules.FlameDamage);}
                if(fire.End<=battle.CurrentTick)
                {
                    fires.Remove(fire);
                    RecordFlame(fire.Source,fire.Tile,"expire");
                }
            }
            foreach(var zone in poisonZones.ToArray()) {
                while(zone.Next<=battle.CurrentTick&&zone.Next<=zone.End){zone.Next+=T(1);foreach(var enemy in battle.Units.Where(e=>e.TeamId!=zone.Source.TeamId&&e.IsAreaTargetable&&HexCoordinates.Distance(e.Position,zone.Tile)<=1).ToArray())Deal(zone.Source,enemy,zone.Damage);}
                foreach(var enemy in battle.Units.Where(e=>e.TeamId!=zone.Source.TeamId&&e.IsTargetable&&HexCoordinates.Distance(e.Position,zone.Tile)<=1).ToArray())if(zone.Source.CurrentHP>0&&zone.Source.IsOnBoard)Control(zone.Source,enemy,CrowdControlKind.Slow,.1f,.20f);
                if(zone.End<=battle.CurrentTick)poisonZones.Remove(zone);
            }
            foreach(int team in new[]{1,2})
            {
                int torrent=TeamTier(team,SynergyTraits.Torrent);long interval=T(torrent==10?4:7);
                if(torrent>0&&battle.CurrentTick>=T(4)&&(battle.CurrentTick-T(4))%interval==0)
                {
                    var source=battle.Units.FirstOrDefault(u=>u.TeamId==team&&Tier(u,SynergyTraits.Torrent)>0&&u.CurrentHP>0&&u.IsOnBoard);if(source==null)continue;
                    bool flood=((battle.CurrentTick-T(4))/interval)%2==0;
                    if(torrent==10)RecordTorrent(source,new BoardPosition(3,team==1?4:3),flood?"field":"field-ebb");
                    if(flood)
                    {
                        var front=Enemies(source).OrderBy(e=>team==1?e.Position.Row:-e.Position.Row)
                            .ThenBy(e=>e.UnitInstanceId,StringComparer.Ordinal).ToArray();
                        if(torrent>=6&&torrent<10&&front.Length>0)
                        {
                            int row=front[0].Position.Row;
                            front=front.Where(e=>Math.Abs(e.Position.Row-row)<=1).ToArray();
                        }
                        if(torrent<10&&front.Length>0)RecordTorrent(source,new BoardPosition(3,front[0].Position.Row),"flood");
                        foreach(var enemy in front.Take(torrent==10?int.MaxValue:torrent>=6?5:torrent>=4?3:2))
                        {
                            RecordTorrent(source,enemy.Position,"impact");
                            if(torrent>=6)Deal(source,enemy,torrent==10?rules.Origins.UltimateWaveDamage:rules.Origins.TorrentDamage);
                            if(enemy.CurrentHP>0)Push(source,enemy,1);
                        }
                    }
                    else
                    {
                        RecordTorrent(source,new BoardPosition(3,team==1?2:5),"ebb");
                        foreach(var ally in Allies(source).Where(a=>Tier(a,SynergyTraits.Torrent)>0||
                            torrent>=4&&Allies(source).Any(w=>Tier(w,SynergyTraits.Torrent)>0&&HexCoordinates.Distance(a.Position,w.Position)<=1)).ToArray())
                        {
                            RecordTorrent(source,ally.Position,"guide",target:ally);
                            Shield(SynergyTraits.Torrent,source,ally,rules.TorrentShield,7);
                            if(torrent>=6)Mana(ally,8,SynergyTraits.Torrent);
                        }
                    }
                    Record(SynergyTraits.Torrent,source,source,flood?"FLOOD":"EBB",torrent);
                }
                if(TeamTier(team,SynergyTraits.Earth)>=6&&battle.CurrentTick>0&&battle.CurrentTick%T(rules.Origins.EarthPulseSeconds)==0)
                {var source=battle.Units.FirstOrDefault(u=>u.TeamId==team&&Tier(u,SynergyTraits.Earth)>0&&u.CurrentHP>0&&u.IsOnBoard);if(source!=null)foreach(var enemy in Enemies(source).Where(e=>rocks[team].Any(p=>HexCoordinates.Distance(p,e.Position)<=1)).ToArray())Control(source,enemy,CrowdControlKind.Slow,rules.Origins.EarthSlowSeconds,rules.Origins.EarthSlow);}
                var guardian=battle.Units.FirstOrDefault(u=>u.TeamId==team&&u.IsSynergySummon&&u.CurrentHP>0&&u.IsOnBoard);
                if(guardian!=null&&battle.CurrentTick>0&&battle.CurrentTick%T(rules.Origins.GuardianPeriod)==0)GuardianSkill(guardian);
            }
        }
        private void Control(UnitCombatState source,UnitCombatState target,CrowdControlKind kind,float duration,float slow=0)
        {if(source.CurrentHP>0&&source.IsOnBoard&&target.IsTargetable)battle.StatusEffects.Apply(new StatusEffectDefinition("synergy-control-"+kind,duration,crowdControl:kind,slowFraction:slow),source.UnitInstanceId,target.UnitInstanceId);}
        private void RecordTorrent(UnitCombatState source,BoardPosition cell,string clip,BoardPosition? destination=null,UnitCombatState target=null)
            =>events.Add(new SynergyEvent(SynergyTraits.Torrent,source.UnitInstanceId,(target??source).UnitInstanceId,clip,source.TeamId,battle.CurrentTick,cell,destination));

        private void Push(UnitCombatState source,UnitCombatState target,int distance)
        {
            var origin=target.Position;
            if(!battle.Movement.TryStart(source,target,new SkillMovementDefinition(SkillMovementKind.Knockback,distance:distance),generated:true))return;
            var movement=battle.Movement.Active.FirstOrDefault(m=>m.UnitId==target.UnitInstanceId&&m.Kind==SkillMovementKind.Knockback);
            if(movement!=null&&movement.Path.Count>1)
                RecordTorrent(source,origin,"arrow",movement.Path.Last());
        }
        private void OriginSkill(UnitCombatState u)
        {
            if(Tier(u,SynergyTraits.Storm)>0&&circuits.TryGetValue(u.UnitInstanceId,out var key)&&circuits.Count(c=>c.Value==key)>=2&&(!stormReady.TryGetValue(u.UnitInstanceId,out long ready)||battle.CurrentTick>=ready))
            {
                stormReady[u.UnitInstanceId]=battle.CurrentTick+T(2);int tier=Tier(u,SynergyTraits.Storm);var targets=Enemies(u).OrderBy(e=>HexCoordinates.Distance(u.Position,e.Position)).ThenBy(e=>e.UnitInstanceId,StringComparer.Ordinal).ToArray();
                var previous=u;
                foreach(var enemy in targets.Take(tier>=4&&circuits.Count(c=>c.Value==key)>=3?2:1))
                {
                    Record(SynergyTraits.Storm,previous,enemy,previous==u?"ARC":"CHAIN",0);
                    Deal(u,enemy,rules.StormDamage);
                    previous=enemy;
                }
                if(tier>=6)
                {
                    if(++charges[key]>=4)
                    {
                        charges[key]=0;
                        foreach(var enemy in targets.Where(e=>e.CurrentHP>0))
                        {
                            Record(SynergyTraits.Storm,u,enemy,"DISCHARGE",0);
                            Deal(u,enemy,rules.StormDamage);
                        }
                    }
                    var circuit=battle.Units.First(member=>member.UnitInstanceId==key);
                    Record(SynergyTraits.Storm,circuit,u,"CHARGE",charges[key]);
                }
                Record(SynergyTraits.Storm,u,u,"CURRENT",charges[key]);
            }
            if(Tier(u,SynergyTraits.Nature)>=6){natureCasts.TryGetValue(u.TeamId,out int count);natureCasts[u.TeamId]=++count;if(count%3==0){var guardian=Allies(u).FirstOrDefault(x=>x.IsSynergySummon);if(guardian!=null)GuardianSkill(guardian);}}
        }
        private void GuardianSkill(UnitCombatState guardian)
        {
            int level=battle.NatureExperience.TryGetValue(guardian.TeamId,out var xp)?xp:0;int tier=TeamTier(guardian.TeamId,SynergyTraits.Nature);
            if(level>=6||tier==10){var enemy=Enemies(guardian).OrderBy(e=>HexCoordinates.Distance(e.Position,guardian.Position)).ThenBy(e=>e.UnitInstanceId,StringComparer.Ordinal).FirstOrDefault();if(enemy!=null){if(tier==10){int row=Enemies(guardian).Select(e=>e.Position.Row).OrderBy(r=>guardian.TeamId==1?r:-r).First();foreach(var front in Enemies(guardian).Where(e=>e.Position.Row==row).ToArray())Deal(guardian,front,rules.Origins.WorldTreeDamage);}else {Deal(guardian,enemy,rules.Origins.GuardianDamage);if(enemy.IsTargetable)Control(guardian,enemy,CrowdControlKind.Root,1);}}}
            if(level>=16||tier==10)foreach(var ally in Allies(guardian).Where(a=>tier==10||HexCoordinates.Distance(a.Position,guardian.Position)<=1).ToArray())battle.Healing.Apply(ally,ally.Stats.MaxHP*rules.Origins.GuardianHeal);
        }
        private void OriginDamage(UnitCombatState source,UnitCombatState target,DamageResult result)
        {
            if(result.Request.Origin!=DamageOrigin.Synergy&&Tier(source,SynergyTraits.Flame)>0&&!result.Request.IsBasicAttack&&result.Request.Origin==DamageOrigin.Direct&&result.Request.SkillPolicy!=null&&ignited.Add(source.UnitInstanceId))Ignite(source,target.Position,3,false);
            if(TeamTier(target.TeamId,SynergyTraits.Earth)>=4&&rocks[target.TeamId].Any(p=>HexCoordinates.Distance(p,target.Position)<=1)&&target.CurrentHP>0&&rockShielded.Add(target.UnitInstanceId))Shield(SynergyTraits.Earth,target,target,rules.Origins.EarthShield,4);
            if(result.Killed)
            {
                foreach(var fire in fires.Where(f=>!f.Spread&&f.Tile.Equals(target.Position)&&Tier(f.Source,SynergyTraits.Flame)>=3).ToArray())
                if(battle.StatusEffects.HasBehavior(target,StatusBehavior.Burn)){
                    var tile=HexCoordinates.Neighbors(HexCoordinates.ToAxial(target.Position)).Select(a=>HexCoordinates.TryToOffset(a,out var p)?(BoardPosition?)p:null).Where(p=>p.HasValue&&battle.Board.Contains(p.Value)&&!fires.Any(f=>f.Tile.Equals(p.Value))).OrderBy(p=>p.Value.Row).ThenBy(p=>p.Value.Column).FirstOrDefault();if(tile.HasValue)Ignite(fire.Source,tile.Value,2,true);}
                int venom=TeamTier(source.TeamId,SynergyTraits.Venom);
                if(venom>0&&!target.IsSynergySummon&&battle.StatusEffects.Active.Any(s=>s.TargetId==target.UnitInstanceId&&s.Definition.Behavior==StatusBehavior.Poison&&battle.Units.Any(u=>u.UnitInstanceId==s.SourceId&&Tier(u,SynergyTraits.Venom)>0)))
                {battle.VenomEarned.TryGetValue(source.TeamId,out int earned);battle.VenomEarned[source.TeamId]=Math.Min(venom>=4?6:4,earned+1);int samples=battle.VenomSamples.TryGetValue(source.TeamId,out var n)?n:0;if(venom>=4&&samples>=10&&poisonTransferred.Add(target.UnitInstanceId)){var next=Enemies(source).OrderBy(e=>HexCoordinates.Distance(target.Position,e.Position)).ThenBy(e=>e.UnitInstanceId,StringComparer.Ordinal).FirstOrDefault();if(next!=null&&source.CurrentHP>0)battle.StatusEffects.Apply(new StatusEffectDefinition("synergy-venom-transfer",3,behavior:StatusBehavior.Poison),source.UnitInstanceId,next.UnitInstanceId);}}
            }
        }
        private void Ignite(UnitCombatState source,BoardPosition tile,float duration,bool spread)
        {
            var fire=fires.FirstOrDefault(f=>f.Tile.Equals(tile));if(fire!=null){fire.End=battle.CurrentTick+T(duration);return;}
            AddFire(source,tile,duration,spread);
            Record(SynergyTraits.Flame,source,source,"FIRE",duration);
            if(Tier(source,SynergyTraits.Flame)<5)return;
            RecordFlame(source,tile,"blast");
            foreach(var enemy in Enemies(source).Where(e=>HexCoordinates.Distance(tile,e.Position)<=1).ToArray())
                Deal(source,enemy,rules.Origins.FlameExplosion);
            if(!spread)
                foreach(var p in HexCoordinates.Neighbors(HexCoordinates.ToAxial(tile)))
                    if(HexCoordinates.TryToOffset(p,out var offset)&&battle.Board.Contains(offset)&&!fires.Any(f=>f.Tile.Equals(offset)))
                        AddFire(source,offset,duration,true);
        }

        private void RecordFlame(UnitCombatState source,BoardPosition tile,string clip)
            =>events.Add(new SynergyEvent(SynergyTraits.Flame,source.UnitInstanceId,source.UnitInstanceId,clip,0,battle.CurrentTick,tile));

        private void AddFire(UnitCombatState source,BoardPosition tile,float duration,bool spread)
        {
            fires.Add(new Fire{Source=source,Tile=tile,End=battle.CurrentTick+T(duration),Next=battle.CurrentTick+T(1),Spread=spread});
            RecordFlame(source,tile,spread?"spread":"flash");
        }
        private void UniqueSkillCompleted(UnitCombatState u)
        {
            if(!rules.Enabled)return;
            int team=u.TeamId;var family=SynergyTraits.Family(u.DefinitionId);
            var starters=new[]{"bulbasaur","charmander","squirtle"}.Select(s=>Representative(team,s)).ToArray();
            if(starters.All(s=>s!=null)&&starters.Contains(u)){starterMarks.Add(u.UnitInstanceId);if(!starterDone.Contains(team)&&starters.All(s=>starterMarks.Contains(s.UnitInstanceId))){starterDone.Add(team);var enemy=Enemies(u).OrderByDescending(e=>e.CurrentHP/e.Stats.MaxHP).ThenBy(e=>e.UnitInstanceId,StringComparer.Ordinal).FirstOrDefault();if(enemy!=null)foreach(var e in Enemies(u).Where(e=>HexCoordinates.Distance(e.Position,enemy.Position)<=1).ToArray()){Deal(u,e,120);if(e.IsTargetable)Control(u,e,CrowdControlKind.Root,1);}foreach(var s in starters.Where(s=>s.CurrentHP>0&&s.IsOnBoard))Shield("starter",u,s,s.Stats.MaxHP*.10f,4);}}
            if(passengers.TryGetValue(u.UnitInstanceId,out string passenger)&&(family==SynergyTraits.Family("snorlax")||uniqueFirst.Add(u.UnitInstanceId)))
            {var ally=battle.Units.First(a=>a.UnitInstanceId==passenger);if(ally.CurrentHP>0&&ally.IsOnBoard)Shield("passenger",u,ally,ally.Stats.MaxHP*(family==SynergyTraits.Family("lapras")?.16f:.10f),4);}
            var queen=Representative(team,"nidoran-f");var king=Representative(team,"nidoran-m");
            if(queen!=null&&king!=null&&queen.CurrentHP>0&&king.CurrentHP>0&&queen.IsOnBoard&&king.IsOnBoard&&(u==queen||u==king)&&(!royalReady.TryGetValue(u.UnitInstanceId,out long ready)||battle.CurrentTick>=ready))
            {royalReady[u.UnitInstanceId]=battle.CurrentTick+T(6);var enemy=Enemies(u).FirstOrDefault(e=>e.UnitInstanceId==u.CurrentTargetId)??Enemies(u).OrderBy(e=>HexCoordinates.Distance(u.Position,e.Position)).ThenBy(e=>e.UnitInstanceId,StringComparer.Ordinal).FirstOrDefault();if(enemy!=null){if(u==queen){float value=king.Rank==UnitRank.Three?175:king.Rank==UnitRank.Two?130:90;foreach(var e in Enemies(king).Where(e=>HexCoordinates.Distance(e.Position,enemy.Position)<=1).ToArray())new DamageProcessor().Apply(battle,new DamageRequest(king.UnitInstanceId,e.UnitInstanceId,value*(e==enemy?1:.5f),DamageType.Physical,origin:DamageOrigin.Synergy));}else{poisonZones.Add(new PoisonZone{Source=queen,Tile=enemy.Position,End=battle.CurrentTick+T(3),Next=battle.CurrentTick+T(1),Damage=queen.Rank==UnitRank.Three?60:queen.Rank==UnitRank.Two?45:30});foreach(var e in Enemies(queen).Where(e=>HexCoordinates.Distance(e.Position,enemy.Position)<=1).ToArray())Control(queen,e,CrowdControlKind.Slow,3,.20f);}}}
        }
    }
}
