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
        private readonly List<Fire> fires=new List<Fire>();
        private readonly HashSet<string> ignited=new HashSet<string>();
        private readonly Dictionary<string,string> circuits=new Dictionary<string,string>();
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
                if (earth > 0)
                {
                    var tiles = rocks[team];
                    int limit = earth >= 4 ? 2 : 1;
                    if (battle.PreparedRocks.TryGetValue(team, out var prepared))
                    {
                        foreach (var tile in prepared)
                        {
                            if (tiles.Count == limit) break;
                            if (battle.TryAddObstacle(tile)) tiles.Add(tile);
                        }
                    }
                    if (tiles.Count == 0)
                    {
                        foreach (var member in members)
                        {
                            var tile = battle.Board.EmptyNeighbors(member.Position)
                                .OrderBy(position => position.Row).ThenBy(position => position.Column)
                                .Select(position => (BoardPosition?)position).FirstOrDefault();
                            if (tile.HasValue && battle.TryAddObstacle(tile.Value)) tiles.Add(tile.Value);
                            if (tiles.Count == limit) break;
                        }
                    }
                    foreach (var tile in tiles)
                        events.Add(new SynergyEvent(SynergyTraits.Earth, members[0].UnitInstanceId,
                            members[0].UnitInstanceId, "rock-spawn", team, battle.CurrentTick, tile));
                }
                var storm=battle.Units.Where(u=>u.TeamId==team&&Tier(u,SynergyTraits.Storm)>0).OrderBy(u=>u.UnitInstanceId,StringComparer.Ordinal).ToArray();
                foreach(var u in storm)
                {
                    if(circuits.ContainsKey(u.UnitInstanceId))continue;
                    string key=u.UnitInstanceId;var queue=new Queue<UnitCombatState>();queue.Enqueue(u);circuits[key]=key;
                    while(queue.Count>0){var at=queue.Dequeue();foreach(var next in storm.Where(v=>!circuits.ContainsKey(v.UnitInstanceId)&&HexCoordinates.Distance(at.Position,v.Position)==1)){circuits[next.UnitInstanceId]=key;queue.Enqueue(next);}}
                    var group=storm.Where(v=>circuits.TryGetValue(v.UnitInstanceId,out var k)&&k==key).ToArray();
                    int families=group.Select(v=>SynergyTraits.Family(v.DefinitionId)).Distinct().Count();
                    foreach(var v in group)circuitBonus[v.UnitInstanceId]=families>=3?.25f:families==2?.15f:0;
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
                int nature = TeamTier(team, SynergyTraits.Nature);
                var guardian = battle.Units.FirstOrDefault(unit => unit.TeamId == team && unit.IsSynergySummon);
                if (guardian != null)
                {
                    RecordNature(guardian, guardian, "SUMMON", nature);
                    if (nature == 10)
                    {
                        foreach (var enemy in Enemies(guardian).ToArray())
                            Control(guardian, enemy, CrowdControlKind.Root, rules.Origins.WorldTreeRootSeconds);
                        Shield(SynergyTraits.Nature, guardian, guardian, 1000, 5);
                    }
                }
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
        public const string EarthPulseShieldGroup = "earth-pulse";

        private bool InEarthRange(UnitCombatState unit)
            => rocks.TryGetValue(unit.TeamId, out var tiles) &&
               tiles.Any(tile => HexCoordinates.Distance(tile, unit.Position) == 1);

        private float EarthReduction(UnitCombatState target, DamageRequest request, int tier)
        {
            if (tier == 0 || request.Type == DamageType.True || !InEarthRange(target))
                return 0;
            if (tier >= 6)
                return rules.Origins.EarthFortressReduction;

            if (request.Origin != DamageOrigin.Direct || (!request.IsBasicAttack && request.SkillPolicy == null))
                return 0;
            var source = battle.Units.FirstOrDefault(unit => unit.UnitInstanceId == request.SourceId);
            return source != null && source.IsOnBoard && HexCoordinates.Distance(source.Position, target.Position) >= 2
                ? rules.Origins.EarthReduction : 0;
        }

        private float OriginIncoming(UnitCombatState target, DamageRequest request)
            => 1 - EarthReduction(target, request, TeamTier(target.TeamId, SynergyTraits.Earth));

        internal void BeforeEarthDamage(UnitCombatState source, UnitCombatState target, DamageRequest request, float damage)
        {
            if (damage <= 0)
                return;

            int tier = TeamTier(target.TeamId, SynergyTraits.Earth);
            float reduction = EarthReduction(target, request, tier);
            if (reduction > 0)
                Record(SynergyTraits.Earth, source, target, tier >= 6 ? "FORTRESS_GUARD" : "RANGED_GUARD", reduction);

            if (tier >= 4 && InEarthRange(target) && rockShielded.Add(target.UnitInstanceId))
                Shield(SynergyTraits.Earth, target, target, rules.Origins.EarthShield, 4);
        }

        private void AdvanceEarthPulse(int team)
        {
            if (TeamTier(team, SynergyTraits.Earth) < 6 || rocks[team].Count == 0)
                return;
            long elapsed = battle.CurrentTick - T(rules.Origins.EarthFirstPulseSeconds);
            if (elapsed < 0 || elapsed % T(rules.Origins.EarthPulseSeconds) != 0)
                return;

            // The fixed structures keep working after their Earth members fall.
            var source = battle.Units.First(unit => unit.TeamId == team && unit.TraitIds.Contains(SynergyTraits.Earth));
            foreach (var tile in rocks[team])
                events.Add(new SynergyEvent(SynergyTraits.Earth, source.UnitInstanceId, source.UnitInstanceId,
                    "protective-pulse", team, battle.CurrentTick, tile));

            foreach (var ally in Allies(source).Where(InEarthRange))
            {
                float amount = Math.Min(ally.Stats.MaxHP * rules.Origins.EarthPulseHealthFraction,
                    rules.Origins.EarthPulseShieldCap);
                Shield(EarthPulseShieldGroup, source, ally, amount, 4);
            }
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
                AdvanceEarthPulse(team);
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
                var guardian=battle.Units.FirstOrDefault(u=>u.TeamId==team&&u.IsSynergySummon&&u.CurrentHP>0&&u.IsOnBoard);
                if(guardian!=null&&TeamTier(team,SynergyTraits.Nature)==10&&battle.CurrentTick>0&&battle.CurrentTick%T(rules.Origins.GuardianPeriod)==0)WorldTreePulse(guardian);
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
        private void OriginDamage(UnitCombatState source,UnitCombatState target,DamageResult result)
        {
            if(result.Request.Origin!=DamageOrigin.Synergy&&Tier(source,SynergyTraits.Flame)>0&&!result.Request.IsBasicAttack&&result.Request.Origin==DamageOrigin.Direct&&result.Request.SkillPolicy!=null&&ignited.Add(source.UnitInstanceId))Ignite(source,target.Position,3,false);
            if(result.Killed)
            {
                foreach(var fire in fires.Where(f=>!f.Spread&&f.Tile.Equals(target.Position)&&Tier(f.Source,SynergyTraits.Flame)>=3).ToArray())
                if(battle.StatusEffects.HasBehavior(target,StatusBehavior.Burn)){
                    var tile=HexCoordinates.Neighbors(HexCoordinates.ToAxial(target.Position)).Select(a=>HexCoordinates.TryToOffset(a,out var p)?(BoardPosition?)p:null).Where(p=>p.HasValue&&battle.Board.Contains(p.Value)&&!fires.Any(f=>f.Tile.Equals(p.Value))).OrderBy(p=>p.Value.Row).ThenBy(p=>p.Value.Column).FirstOrDefault();if(tile.HasValue)Ignite(fire.Source,tile.Value,2,true);}
                ContaminatedDeath(target);
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
