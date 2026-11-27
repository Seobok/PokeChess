using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Pokemon;
using PokeChess.Core.Board;

namespace PokeChess.Core.Battle
{
    [Serializable]
    public struct BattleDamageBreakdown
    {
        public float physical, magic, trueDamage;
        public float Total => physical + magic + trueDamage;
        public void Add(DamageType type, float value)
        {
            switch (type)
            {
                case DamageType.Physical: physical += value; break;
                case DamageType.Magic: magic += value; break;
                case DamageType.True: trueDamage += value; break;
            }
        }
    }

    public enum CombatActionState { Idle, Moving, Attacking, Casting, Dead }
    public enum BattleResult { InProgress, TeamOneWin, TeamTwoWin, Draw }
    public enum BattleEndReason { None, Elimination, TimeLimit, Surrender }

    public sealed class UnitCombatState
    {
        internal UnitInstance PersistentUnit { get; }
        public int BountyStacks=>PersistentUnit.BountyStacks;
        public int SwordDanceHits { get; internal set; }
        public string UnitInstanceId { get; }
        public string DefinitionId { get; private set; }
        public string SkillId { get; private set; }
        public IReadOnlyList<string> TraitIds { get; internal set; }
        public string OwnerPlayerId { get; }
        public int TeamId { get; }
        public UnitRank Rank { get; }
        public int EvolutionStage { get; private set; }
        public PokemonStats Stats { get; private set; }
        private PokemonStats baseStats;
        internal float BaseAttack => baseStats.Attack;
        private bool canEvolve;
        private float maxHpMultiplier = 1;
        private float roundBonusHP, roundBonusAttack;
        public bool IsEmbarked { get; internal set; }
        internal void AddRoundStats(float hp, float attack)
        {
            roundBonusHP += hp;
            roundBonusAttack += attack;
            RefreshEquipment();
        }
        internal long ActiveSynergyCastId { get; set; }
        internal void ScaleMaxHP(float bonus)
        {
            maxHpMultiplier *= 1 + bonus;
            RefreshEquipment();
        }

        internal void RefreshEquipment()
        {
            var stats = ItemCombatSystem.ApplyStats(PersistentUnit, baseStats, canEvolve);
            float maxHp = stats.MaxHP * maxHpMultiplier + roundBonusHP;
            if (maxHp != Stats.MaxHP)
                CurrentHP = Math.Min(maxHp, CurrentHP / Stats.MaxHP * maxHp);
            Stats = maxHpMultiplier == 1 && roundBonusHP == 0 && roundBonusAttack == 0 ? stats : new PokemonStats(maxHp, stats.Attack + roundBonusAttack, stats.SpellPower,
                stats.AttackSpeed, stats.Armor, stats.MagicResistance, stats.AttackRange, stats.MoveSpeed,
                stats.MaxEnergy, stats.StartingEnergy, stats.AttackHitTime, stats.AttackDelivery,
                stats.ProjectileSpeed, stats.CritChance, stats.CritMultiplier, stats.EnergyLockSeconds);
            ItemInstanceIds = Array.AsReadOnly(PersistentUnit.ItemInstanceIds.ToArray());
        }

        internal void ApplyEvolution(PokemonDefinition definition)
        {
            DefinitionId = definition.Id;
            SkillId = definition.SkillId;
            EvolutionStage = PersistentUnit.EvolutionStage;
            IsBoss = definition.IsBoss;
            canEvolve = definition.Evolutions.Count > 0;
            baseStats = RankRules.Default.Apply(definition, Rank);
            RefreshEquipment();
        }
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
        public bool IsOriginalCombatUnit { get; internal set; }
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
        public bool IsSynergyStealthed => (StatusSystem?.Active.Any(e=>e.TargetId==UnitInstanceId&&e.Definition.Id=="synergy-stealth")??false)
            || (StatusSystem?.IsItemStealthed(this) ?? false);
        public bool IsAreaTargetable => CurrentHP>0&&IsOnBoard&&(!IsUntargetable||IsSynergyStealthed);
        public bool IsUntargetable => untargetable || (StatusSystem?.IsItemStealthed(this) ?? false) || (StatusSystem?.HasBehavior(this,StatusBehavior.Untargetable)??false);
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
        public IReadOnlyList<string> ItemInstanceIds { get; private set; }

        internal UnitCombatState(UnitInstance source, PokemonStats stats, int teamId, BoardPosition position, string skillId, IReadOnlyList<string> traits, bool canEvolve = false)
        {
            PersistentUnit=source;UnitInstanceId = source.InstanceId;
            DefinitionId = source.DefinitionId;
            SkillId = skillId;
            TraitIds = Array.AsReadOnly(traits.ToArray());
            OwnerPlayerId = source.OwnerPlayerId;
            Rank = source.Rank;
            EvolutionStage = source.EvolutionStage;
            TeamId = teamId;
            this.canEvolve = canEvolve;
            baseStats = stats;
            Stats = ItemCombatSystem.ApplyStats(source, stats, canEvolve);
            Position = position;
            CurrentHP = Stats.MaxHP;
            CurrentEnergy = Stats.StartingEnergy;
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
        internal void RestoreToBoard(BoardPosition position)
        {
            Position = position;
            IsOnBoard = true;
            IsEmbarked = false;
            ActionState = CombatActionState.Idle;
            CurrentTargetId = null;
        }
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
        public Dictionary<int,BoardPosition[]> PreparedRocks { get; } = new Dictionary<int,BoardPosition[]>();
        public bool IsPvP { get; internal set; } = true;
        internal PokemonCatalog Catalog { get; set; }

        internal void RefreshLoadout(UnitCombatState fighter)
        {
            bool evolved = fighter.DefinitionId != fighter.PersistentUnit.DefinitionId;
            if (evolved) fighter.ApplyEvolution(Catalog.Get(fighter.PersistentUnit.DefinitionId));
            else fighter.RefreshEquipment();

            var team = Units.Where(unit => unit.TeamId == fighter.TeamId && unit.IsOriginalCombatUnit).ToArray();
            foreach (var unit in team)
                unit.TraitIds = Array.AsReadOnly(BattleStateFactory.ResolveTraits(unit.PersistentUnit,
                    team.Select(member => member.PersistentUnit), Catalog));
            Synergies.RefreshTeamCounts(fighter.TeamId);
            Items.TryTriggerSitrusBerry(fighter);
        }
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
        private readonly Dictionary<string, BattleDamageBreakdown> damageDealt = new Dictionary<string, BattleDamageBreakdown>(StringComparer.Ordinal);
        private readonly Dictionary<string, BattleDamageBreakdown> damageTaken = new Dictionary<string, BattleDamageBreakdown>(StringComparer.Ordinal);
        public BattleDamageBreakdown DamageDealtTypes(string unitId) => unitId != null && damageDealt.TryGetValue(unitId, out var value) ? value : default;
        public BattleDamageBreakdown DamageTakenTypes(string unitId) => unitId != null && damageTaken.TryGetValue(unitId, out var value) ? value : default;
        public float DamageDealtBy(string unitId) => DamageDealtTypes(unitId).Total;
        public float DamageTakenBy(string unitId) => DamageTakenTypes(unitId).Total;
        internal void RecordDamage(DamageResult result)
        {
            damageResults.Add(result);
            float value = result.AppliedDamage + result.ShieldAbsorbed;
            if (value <= 0 || result.Missed || result.Request.SourceId == null || result.Request.TargetId == null) return;
            if (!unitsById.TryGetValue(result.Request.SourceId, out var source) ||
                !unitsById.TryGetValue(result.Request.TargetId, out var target) || source.TeamId == target.TeamId) return;
            var dealt = DamageDealtTypes(source.UnitInstanceId);
            var taken = DamageTakenTypes(target.UnitInstanceId);
            dealt.Add(result.Request.Type, value);
            taken.Add(result.Request.Type, value);
            damageDealt[source.UnitInstanceId] = dealt;
            damageTaken[target.UnitInstanceId] = taken;
        }
        private readonly CombatBoard board;
        private readonly Dictionary<string, UnitCombatState> unitsById;
        public IReadOnlyCombatBoard Board => board.ReadOnly;

        internal bool TryAddObstacle(BoardPosition position) => board.TryAddObstacle(position);

        // Occupancy operations only: AI movement timing and death rules are implemented later.
        public BoardOperationResult TryMoveUnit(string unitId, BoardPosition destination)
        {
            var result = board.TryMove(unitId, destination);
            if (result == BoardOperationResult.Success) unitsById[unitId].SetPosition(destination);
            return result;
        }
        internal bool TryRestoreUnit(UnitCombatState unit, BoardPosition destination)
        {
            if (!unit.IsAlive || unit.IsOnBoard || board.TryPlace(unit.UnitInstanceId, destination) != BoardOperationResult.Success)
                return false;
            unit.RestoreToBoard(destination);
            return true;
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
        internal static string[] ResolveTraits(UnitInstance unit,IEnumerable<UnitInstance> entries,PokemonCatalog catalog,
            Func<UnitInstance, BoardPosition?> position = null)
            => PokeChess.Core.Match.SynergyPreparation.RosterTraits(unit, entries, catalog, position);
        public static BattleState Create(string battleId, int roundNumber, ulong seed, int tickRate,
            PokemonCatalog catalog, IEnumerable<BattleUnitSetup> setup,
            Func<UnitInstance, PokemonDefinition, PokemonStats> resolveStats = null, SynergyRules synergyRules = null,
            BoardPosition? guardianOne = null, BoardPosition? guardianTwo = null)
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
            var combatPositions = entries.ToDictionary(e => e.Unit.InstanceId, e => e.Position);
            var states = entries.Select(e =>
            {
                var definition = catalog.Get(e.Unit.DefinitionId);
                var stats = resolveStats == null ? RankRules.Default.Apply(definition,e.Unit.Rank) : resolveStats(e.Unit, definition);
                if (stats == null) throw new ArgumentException("Stat resolver returned null.");
                return new UnitCombatState(e.Unit, stats, e.TeamId, e.Position, definition.SkillId, ResolveTraits(e.Unit,entries.Where(entry => entry.TeamId == e.TeamId).Select(entry => entry.Unit),catalog, u => combatPositions[u.InstanceId]), definition.Evolutions.Count > 0){IsBoss=definition.IsBoss,IsOriginalCombatUnit=true};
            }).ToArray();
            var expanded=states.ToList();
            if(synergyRules==null||synergyRules.Enabled)foreach(int team in new[]{1,2}) {
                var own=states.Where(u=>u.TeamId==team).ToArray();
                int nature=own.Where(u=>u.TraitIds.Contains(SynergyTraits.Nature)).Select(u=>SynergyTraits.Family(u.DefinitionId)).Distinct().Count();
                if(nature<2)continue;
                BoardPosition? tile = team == 1 ? guardianOne : guardianTwo;
                if (tile.HasValue && (!board.Contains(tile.Value) || board.IsOccupied(tile.Value))) tile = null;
                for(int row=0;row<8&&!tile.HasValue;row++)for(int col=0;col<7;col++){var p=new BoardPosition(col,team==1?row:7-row);if(!board.IsOccupied(p)){tile=p;break;}}
                if(!tile.HasValue)continue;
                int tier=nature>=10?10:nature>=6?6:nature>=4?4:2;
                var definition = SynergySystem.NatureGuardianDefinition(tier, synergyRules?.Origins);
                var unit = new UnitInstance("nature-guardian-"+team, definition.Id, own[0].OwnerPlayerId,
                    UnitRank.One, 0, UnitPlacement.Unplaced, Array.Empty<string>());
                var guardian = new UnitCombatState(unit, definition.BaseStats, team, tile.Value,
                    definition.SkillId, Array.Empty<string>()) { IsSynergySummon = true };
                board.TryPlace(unit.InstanceId,tile.Value);expanded.Add(guardian);
            }
            var battle = new BattleState(battleId, roundNumber, seed, tickRate, expanded.ToArray(), board) { Catalog = catalog };
            battle.Synergies = new SynergySystem(battle, synergyRules);
            return battle;
        }
    }
}
