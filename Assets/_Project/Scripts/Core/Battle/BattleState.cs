using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Board;

namespace PokeChess.Core.Battle
{
    public enum CombatActionState { Idle, Moving, Attacking, Casting, Dead }
    public enum BattleResult { InProgress, TeamOneWin, TeamTwoWin, Draw }
    public enum BattleEndReason { None, Elimination, TimeLimit, Surrender }

    public sealed class UnitCombatState
    {
        internal UnitInstance PersistentUnit { get; }
        public int BountyStacks=>PersistentUnit.BountyStacks;
        public int SwordDanceHits { get; internal set; }
        public string UnitInstanceId { get; }
        public string DefinitionId { get; }
        public string SkillId { get; }
        public IReadOnlyList<string> TraitIds { get; }
        public string OwnerPlayerId { get; }
        public int TeamId { get; }
        public UnitRank Rank { get; }
        public int EvolutionStage { get; }
        public PokemonStats Stats { get; private set; }
        internal long ActiveSynergyCastId { get; set; }
        internal void ScaleMaxHP(float bonus) { var s=Stats; float hp=s.MaxHP*(1+bonus); CurrentHP*=hp/s.MaxHP; Stats=new PokemonStats(hp,s.Attack,s.SpellPower,s.AttackSpeed,s.Armor,s.MagicResistance,s.AttackRange,s.MoveSpeed,s.MaxEnergy,s.StartingEnergy,s.AttackHitTime,s.AttackDelivery,s.ProjectileSpeed,s.CritChance,s.CritMultiplier,s.EnergyLockSeconds); }
        internal StatusEffectSystem StatusSystem { get; set; }
        public EffectiveCombatStats EffectiveStats => StatusSystem.Resolve(this);
        public DamageModifiers EffectiveDamageModifiers => StatusSystem.ResolveDamageModifiers(this);
        public bool HasCrowdControl(CrowdControlKind kind) => StatusSystem.Has(this,kind);
        public string EffectiveTauntSource => StatusSystem.TauntSource(this) ?? TauntSourceId;
        public BoardPosition Position { get; private set; }
        public float CurrentHP { get; private set; }
        public float CurrentEnergy { get; private set; }
        public long EnergyLockUntilTick { get; internal set; }
        internal void SetEnergy(float value) { CurrentEnergy=ModelGuard.Number(value,nameof(value)); }
        internal ShieldSystem ShieldSystem { get; set; }
        internal float LegacyShield { get; set; }
        public float CurrentShield => LegacyShield+(ShieldSystem?.Total(this)??0);
        public bool IsBoss { get; internal set; }
        public bool IsSynergySummon { get; internal set; }
        public DamageModifiers DamageModifiers { get; private set; } = new DamageModifiers();
        public void SetDamageModifiers(DamageModifiers modifiers)
        { DamageModifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers)); }
        public void SetShield(float shield) {
            ModelGuard.Number(shield,nameof(shield));
            if(ShieldSystem==null)LegacyShield=shield;else ShieldSystem.SetTotal(this,shield);
        }
        internal DeathReason DeathCause { get; set; } = DeathReason.StateChange;
        private bool resolvingSimultaneousAttacks;
        public bool IsAlive => CurrentHP > 0 || resolvingSimultaneousAttacks;
        internal void BeginSimultaneousAttacks() => resolvingSimultaneousAttacks=CurrentHP>0 && IsOnBoard;
        internal void EndSimultaneousAttacks()
        {
            resolvingSimultaneousAttacks=false;
            if(!IsAlive)ActionState=CombatActionState.Dead;
        }
        public bool IsOnBoard { get; private set; } = true;
        private bool untargetable;
        public bool IsSynergyStealthed => StatusSystem?.Active.Any(e=>e.TargetId==UnitInstanceId&&e.Definition.Id=="synergy-stealth")??false;
        public bool IsAreaTargetable => CurrentHP>0&&IsOnBoard&&(!IsUntargetable||IsSynergyStealthed);
        public bool IsUntargetable => untargetable||(StatusSystem?.HasBehavior(this,StatusBehavior.Untargetable)??false);
        public string TauntSourceId { get; private set; }
        public bool IsTargetable => IsAlive && IsOnBoard && !IsUntargetable;
        // Status application/duration/stack rules are supplied by WBS 1.15.
        public void SetUntargetable(bool value) {untargetable=value;StatusSystem?.NotifyTargetability(this);}
        public void SetTauntSource(string sourceId)
        {
            if(sourceId != null && string.IsNullOrWhiteSpace(sourceId))throw new ArgumentException("Invalid taunt source.");
            TauntSourceId = sourceId;
        }
        public string CurrentTargetId { get; internal set; }
        public CombatActionState ActionState { get; internal set; }
        public IReadOnlyList<string> ItemInstanceIds { get; }

        internal UnitCombatState(UnitInstance source, PokemonStats stats, int teamId, BoardPosition position, string skillId, IReadOnlyList<string> traits)
        {
            PersistentUnit=source;UnitInstanceId = source.InstanceId;
            DefinitionId = source.DefinitionId;
            SkillId = skillId;
            TraitIds = Array.AsReadOnly(traits.ToArray());
            OwnerPlayerId = source.OwnerPlayerId;
            Rank = source.Rank;
            EvolutionStage = source.EvolutionStage;
            TeamId = teamId;
            Stats = stats;
            Position = position;
            CurrentHP = stats.MaxHP;
            CurrentEnergy = stats.StartingEnergy;
            ItemInstanceIds = Array.AsReadOnly(source.ItemInstanceIds.ToArray());
        }
        // No damage formula or command validation here; this only stores validated state.
        public void SetVitals(float hp, float energy)
        {
            ModelGuard.Number(hp, nameof(hp));
            ModelGuard.Number(energy, nameof(energy));
            if (hp > Stats.MaxHP) throw new ArgumentOutOfRangeException(nameof(hp));
            if(CurrentHP>0&&hp==0)DeathCause=DeathReason.StateChange;
            CurrentHP = hp;
            CurrentEnergy = energy; // Energy overflow is supported by the combat specification.
            if (!IsAlive) ActionState = CombatActionState.Dead;
        }
        internal void SetPosition(BoardPosition position) => Position = position;
        internal void MarkRemovedFromBoard() => IsOnBoard = false;
    }

    public sealed class BattleUnitSetup
    {
        public UnitInstance Unit { get; }
        public int TeamId { get; }
        public BoardPosition Position { get; }
        public BattleUnitSetup(UnitInstance unit, int teamId, BoardPosition position)
        {
            Unit = unit ?? throw new ArgumentNullException(nameof(unit));
            if (teamId != 1 && teamId != 2) throw new ArgumentOutOfRangeException(nameof(teamId));
            TeamId = teamId;
            Position = position;
        }
    }

    // Host-internal state; never serialize this directly as a client snapshot (Seed is private information).
    public sealed class BattleState
    {
        public string BattleId { get; }
        public int RoundNumber { get; }
        public ulong BattleSeed { get; }
        public PokeChess.Core.Random.BattleRandomStreams RngStreams { get; }
        public int TickRate { get; }
        public ProjectileSystem Projectiles { get; }
        public SkillTimelineSystem SkillTimeline { get; }
        public CombatMovementSystem Movement { get; }
        public PathProjectileSystem PathProjectiles { get; }
        public EnergySystem Energy { get; }
        public ShieldSystem Shields { get; }
        public HealingSystem Healing { get; }
        public StatusEffectSystem StatusEffects { get; }
        public SynergySystem Synergies { get; internal set; }
        public Dictionary<int,int> NatureEarned { get; } = new Dictionary<int,int>();
        public HashSet<int> NatureHealBonus { get; } = new HashSet<int>();
        public Dictionary<int,BoardPosition[]> PreparedRocks { get; } = new Dictionary<int,BoardPosition[]>();
        public Dictionary<int,int> NatureExperience { get; } = new Dictionary<int,int>();
        public Dictionary<int,int> VenomSamples { get; } = new Dictionary<int,int>();
        public Dictionary<int,int> VenomEarned { get; } = new Dictionary<int,int>();
        public ItemCombatSystem Items { get; }
        private readonly List<SkillEvent> skillEvents = new List<SkillEvent>();
        public IReadOnlyList<SkillEvent> SkillEventsThisTick => Array.AsReadOnly(skillEvents.ToArray());
        private readonly List<SkillEffectEvent> skillEffectEvents = new List<SkillEffectEvent>();
        public IReadOnlyList<SkillEffectEvent> SkillEffectEventsThisTick => Array.AsReadOnly(skillEffectEvents.ToArray());
        internal void RecordSkillEffect(SkillEffectEvent signal) => skillEffectEvents.Add(signal);
        private readonly List<SkillAreaEvent> skillAreaEvents=new List<SkillAreaEvent>();
        public IReadOnlyList<SkillAreaEvent> SkillAreasThisTick=>Array.AsReadOnly(skillAreaEvents.ToArray());
        internal void RecordSkillArea(SkillAreaEvent area)=>skillAreaEvents.Add(area);
        internal void BeginSkillTick() { skillEvents.Clear(); skillEffectEvents.Clear();skillAreaEvents.Clear(); }
        internal void RecordSkill(SkillEvent signal) => skillEvents.Add(signal);
        internal bool HasSimulation { get; set; }
        public long CurrentTick { get; internal set; }
        public double ElapsedSeconds => (double)CurrentTick / TickRate;
        public bool IsOvertime { get; internal set; }
        // Only enabled by the isolated EffectLab; preserves hit events for visual inspection.
        public bool PreserveHealthForPreview { get; set; }
        public long OvertimeStartTick => PreserveHealthForPreview?long.MaxValue:checked(30L * TickRate);
        public long TimeLimitTick => PreserveHealthForPreview?long.MaxValue:checked(45L * TickRate);
        public long? EndTick { get; internal set; }
        private readonly List<BattleLifecycleEvent> lifecycleEvents = new List<BattleLifecycleEvent>();
        public IReadOnlyList<BattleLifecycleEvent> LifecycleEventsThisTick => Array.AsReadOnly(lifecycleEvents.ToArray());
        internal void BeginLifecycleTick() => lifecycleEvents.Clear();
        internal void RecordLifecycle(BattleLifecycleEvent value) => lifecycleEvents.Add(value);
        public BattleResult Result { get; internal set; } = BattleResult.InProgress;
        public BattleEndReason EndReason { get; internal set; } = BattleEndReason.None;
        public SkillProcSystem SkillProcs { get; }
        public SkillWorldEffects WorldEffects { get; }
        public IReadOnlyList<UnitCombatState> Units { get; }
        private readonly List<DamageResult> damageResults = new List<DamageResult>();
        public IReadOnlyList<DamageResult> DamageResultsThisTick => Array.AsReadOnly(damageResults.ToArray());
        internal void BeginDamageTick() {damageResults.Clear();SkillProcs.BeginTick();}
        internal void RecordDamage(DamageResult result) => damageResults.Add(result);
        private readonly CombatBoard board;
        private readonly Dictionary<string, UnitCombatState> unitsById;
        public IReadOnlyCombatBoard Board => board.ReadOnly;

        // Occupancy operations only: AI movement timing and death rules are implemented later.
        public BoardOperationResult TryMoveUnit(string unitId, BoardPosition destination)
        {
            var result = board.TryMove(unitId, destination);
            if (result == BoardOperationResult.Success) unitsById[unitId].SetPosition(destination);
            return result;
        }
        public BoardOperationResult TryRemoveUnit(string unitId)
        {
            var result = board.TryRemove(unitId);
            if (result == BoardOperationResult.Success) unitsById[unitId].MarkRemovedFromBoard();
            return result;
        }

        internal BattleState(string battleId, int roundNumber, ulong seed, int tickRate, UnitCombatState[] units, CombatBoard board)
        {
            BattleId = ModelGuard.Id(battleId, nameof(battleId));
            if (roundNumber < 1) throw new ArgumentOutOfRangeException(nameof(roundNumber));
            if (tickRate < 1) throw new ArgumentOutOfRangeException(nameof(tickRate));
            RoundNumber = roundNumber;
            BattleSeed = seed;
            RngStreams = new PokeChess.Core.Random.BattleRandomStreams(seed);
            TickRate = tickRate;
            Projectiles = new ProjectileSystem(this);
            SkillTimeline = new SkillTimelineSystem(this);
            Energy = new EnergySystem(this);
            StatusEffects = new StatusEffectSystem(this);SkillProcs=new SkillProcSystem(this);WorldEffects=new SkillWorldEffects(this);
            Shields=new ShieldSystem(this);Healing=new HealingSystem(this);
            foreach(var unit in units){unit.StatusSystem=StatusEffects;unit.ShieldSystem=Shields;}
            this.board = board ?? throw new ArgumentNullException(nameof(board));
            unitsById = units.ToDictionary(u => u.UnitInstanceId, StringComparer.Ordinal);
            Units = Array.AsReadOnly(units);
            Items = new ItemCombatSystem(this);
            Movement = new CombatMovementSystem(this);
            PathProjectiles = new PathProjectileSystem(this);
        }
    }

    public static class BattleStateFactory
    {
        private static string[] ResolveTraits(UnitInstance unit,BattleUnitSetup[] entries,PokemonCatalog catalog) {
            var traits=PokeChess.Core.Match.SynergyPreparation.Traits(unit,catalog).ToList();
            if(SynergyTraits.Family(unit.DefinitionId)==SynergyTraits.Family("porygon")&&unit.SynergyTargetId!=null){var target=entries.FirstOrDefault(e=>e.Unit.InstanceId==unit.SynergyTargetId&&e.Unit.OwnerPlayerId==unit.OwnerPlayerId);if(target!=null)traits.AddRange(catalog.Get(target.Unit.DefinitionId).TraitIds.Where(SynergyTraits.Origins.Contains).Take(1));}
            return traits.Distinct(StringComparer.Ordinal).ToArray();
        }
        public static BattleState Create(string battleId, int roundNumber, ulong seed, int tickRate,
            PokemonCatalog catalog, IEnumerable<BattleUnitSetup> setup,
            Func<UnitInstance, PokemonDefinition, PokemonStats> resolveStats = null, SynergyRules synergyRules = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            var entries = setup.ToArray();
            if (entries.Length == 0 || entries.Any(e => e == null)) throw new ArgumentException("Empty or invalid setup.");
            if (entries.Select(e => e.Unit.InstanceId).Distinct().Count() != entries.Length)
                throw new ArgumentException("Duplicate unit instance IDs.");
            if (!entries.Any(e => e.TeamId == 1) || !entries.Any(e => e.TeamId == 2))
                throw new ArgumentException("Both combat teams are required.");
            // Validate the entire setup before invoking stat policies or exposing a battle.
            var board = new CombatBoard();
            foreach (var entry in entries)
            {
                var placement = board.TryPlace(entry.Unit.InstanceId, entry.Position);
                if (placement != BoardOperationResult.Success)
                    throw new ArgumentException("Invalid initial placement: " + placement, nameof(setup));
            }
            var states = entries.Select(e =>
            {
                var definition = catalog.Get(e.Unit.DefinitionId);
                var stats = resolveStats == null ? RankRules.Default.Apply(definition,e.Unit.Rank) : resolveStats(e.Unit, definition);
                if (stats == null) throw new ArgumentException("Stat resolver returned null.");
                stats = ItemCombatSystem.ApplyStats(e.Unit,stats);
                return new UnitCombatState(e.Unit, stats, e.TeamId, e.Position, definition.SkillId, ResolveTraits(e.Unit,entries,catalog)){IsBoss=definition.IsBoss};
            }).ToArray();
            var expanded=states.ToList();
            if(synergyRules==null||synergyRules.Enabled)foreach(int team in new[]{1,2}) {
                var own=states.Where(u=>u.TeamId==team).ToArray();
                int nature=own.Where(u=>u.TraitIds.Contains(SynergyTraits.Nature)).Select(u=>SynergyTraits.Family(u.DefinitionId)).Distinct().Count();
                if(nature<2)continue;
                BoardPosition? tile=null;
                for(int row=0;row<8&&!tile.HasValue;row++)for(int col=0;col<7;col++){var p=new BoardPosition(col,team==1?row:7-row);if(!board.IsOccupied(p)){tile=p;break;}}
                if(!tile.HasValue)continue;
                var unit=new UnitInstance("nature-guardian-"+team,catalog.TryGet("tangela",out _)?"tangela":own[0].DefinitionId,own[0].OwnerPlayerId,UnitRank.One,0,UnitPlacement.Unplaced,Array.Empty<string>());
                var tuning=(synergyRules??new SynergyRules()).Origins;
                var stats=new PokemonStats(nature>=10?tuning.WorldTreeHP:tuning.GuardianHP,nature>=10?tuning.WorldTreeAttack:tuning.GuardianAttack,100,.6f,30,30,1,2,0,0,.2f);
                var guardian=new UnitCombatState(unit,stats,team,tile.Value,own[0].SkillId,Array.Empty<string>()){IsSynergySummon=true};
                board.TryPlace(unit.InstanceId,tile.Value);expanded.Add(guardian);
            }
            var battle = new BattleState(battleId, roundNumber, seed, tickRate, expanded.ToArray(), board);
            battle.Synergies = new SynergySystem(battle, synergyRules);
            return battle;
        }
    }
}
