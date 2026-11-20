using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Board;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Battle
{
    public readonly struct ItemCombatEvent
    {
        public long Tick { get; }
        public string ItemId { get; }
        public string SourceId { get; }
        public string TargetId { get; }
        public float Value { get; }
        internal ItemCombatEvent(long tick,string item,string source,string target,float value)
        { Tick=tick;ItemId=item;SourceId=source;TargetId=target;Value=value; }
    }
    public sealed class ItemCombatSystem
    {
        private sealed class TriggerState
        {
            internal int Hits;
            internal int Charges;
            internal long Ready;
            internal long Until;
        }

        private readonly Dictionary<string, TriggerState> triggers = new Dictionary<string, TriggerState>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> sashRecovery = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> choiceBandUntil = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<(string item, string target), long> exposure = new Dictionary<(string, string), long>();
        private readonly Dictionary<string, long> firstCast = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> stealthUntil = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> nextQuickClaw = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly BattleState battle;
        private readonly HashSet<string> consumed=new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string,long> protection=new Dictionary<string,long>(StringComparer.Ordinal);
        private readonly Dictionary<string,long> shareReady=new Dictionary<string,long>(StringComparer.Ordinal);
        private readonly List<ItemCombatEvent> events=new List<ItemCombatEvent>();
        public IReadOnlyList<ItemCombatEvent> EventsThisTick=>events.AsReadOnly();
        internal ItemCombatSystem(BattleState battle){this.battle=battle;}
        public static bool Has(UnitCombatState u,string id)=>ItemCatalog.Has(u.ItemInstanceIds,id);
        internal static PokemonStats ApplyStats(UnitInstance unit, PokemonStats s, bool canEvolve)
        {
            var definitions = unit.ItemInstanceIds.Select(ItemCatalog.DefinitionId)
                .Where(id => id != null).Select(ItemCatalog.Get).ToArray();
            if (definitions.Length == 0) return s;

            float attack = s.Attack * (1 + definitions.Sum(item => item.Stats.attack) / 100f);
            float speedBonus = definitions.Sum(item => item.Stats.speed) / 100f;
            if (definitions.Any(item => item.Id == ItemCatalog.ChoiceScarf)) speedBonus += .25f;
            float defenseMultiplier = 1 + .15f * definitions.Count(item => item.Id == "crafted-eviolite")
                * (canEvolve ? 2 : 1);
            float armor = (s.Armor + definitions.Sum(item => item.Stats.armor)) * defenseMultiplier;
            float resistance = (s.MagicResistance + definitions.Sum(item => item.Stats.resistance)) * defenseMultiplier;
            float power = s.SpellPower + definitions.Sum(item => item.Stats.power)
                + Math.Min(30, armor * .3f) * definitions.Count(item => item.Id == "crafted-wise-glasses");
            float crit = s.CritChance + definitions.Sum(item => item.Stats.crit) / 100f
                + .15f * definitions.Count(item => item.Id == "crafted-wide-lens");
            float critDamage = s.CritMultiplier + Math.Max(0, crit - 1) * .5f
                + .15f * definitions.Count(item => item.Id == "crafted-razor-claw");

            return new PokemonStats(s.MaxHP + definitions.Sum(item => item.Stats.hp), attack, power,
                speedBonus == 0 ? s.AttackSpeed : Math.Min(5, s.AttackSpeed * (1 + speedBonus)), armor,
                resistance, s.AttackRange, s.MoveSpeed, s.MaxEnergy,
                Math.Min(s.MaxEnergy, s.StartingEnergy + definitions.Sum(item => item.Stats.energy)),
                s.AttackHitTime, s.AttackDelivery, s.ProjectileSpeed, Math.Min(1, crit), critDamage, s.EnergyLockSeconds);
        }

        internal static int Count(UnitCombatState unit, string definition)
            => unit.ItemInstanceIds.Count(id => ItemCatalog.DefinitionId(id) == definition);

        internal void OnCastStarted(UnitCombatState unit, long castId)
        {
            if (!firstCast.ContainsKey(unit.UnitInstanceId)) firstCast.Add(unit.UnitInstanceId, castId);
        }

        internal float FirstCastBonus(UnitCombatState unit, long castId)
            => castId > 0 && firstCast.TryGetValue(unit.UnitInstanceId, out long first) && first == castId
                ? .45f * Count(unit, "crafted-choice-specs") : 0;

        internal bool IsStealthed(UnitCombatState unit)
            => stealthUntil.TryGetValue(unit.UnitInstanceId, out long end) && end > battle.CurrentTick;

        internal float WeaknessPolicyBonus(UnitCombatState unit)
            => stealthUntil.ContainsKey(unit.UnitInstanceId) && !IsStealthed(unit) ? .2f * Count(unit, "crafted-weakness-policy") : 0;

        internal bool TrySpendQuickClaw(UnitCombatState unit)
        {
            if (nextQuickClaw.TryGetValue(unit.UnitInstanceId, out long next) && next > battle.CurrentTick) return false;
            if (!TryConsume(unit, "crafted-quick-claw")) return false;
            nextQuickClaw[unit.UnitInstanceId] = battle.CurrentTick + BattleSimulation.ToTicks(.35, battle.TickRate);
            return true;
        }

        internal bool PreventEnemyStatus(UnitCombatState source, UnitCombatState target, StatusEffectDefinition definition)
        {
            if (Has(target, "crafted-safety-goggles"))
            {
                var powder = new[] { "poison-powder", "poisonpowder", "sleep-powder", "stun-spore", "spore", "powder" };
                if (powder.Any(key => definition.Id.Contains(key) || (source.SkillId?.Contains(key) ?? false))) return true;
            }
            if (Has(target, "crafted-clear-amulet") && definition.Modifiers.Any(modifier =>
                modifier.Type == StatModifierType.Multiplicative ? modifier.Value < 1 : modifier.Value < 0))
                return true;

            foreach (var id in Equipment(target, "crafted-covert-cloak"))
            {
                var state = Trigger(id);
                if (state.Ready > battle.CurrentTick) continue;
                state.Ready = battle.CurrentTick + 6L * battle.TickRate;
                return true;
            }

            bool control = definition.CrowdControl == CrowdControlKind.Stun || definition.CrowdControl == CrowdControlKind.Root ||
                definition.Behavior == StatusBehavior.Sleep || definition.Behavior == StatusBehavior.Confusion;
            if (!control || !TryConsume(target, "crafted-mental-herb")) return false;
            battle.Energy.Gain(target, 20, EnergyChangeReason.Item, capAtMax: true);
            return true;
        }

        internal void BeginTick()
        {
            events.Clear();
            foreach (var unit in battle.Units)
            {
                if (sashRecovery.TryGetValue(unit.UnitInstanceId, out long until) && until <= battle.CurrentTick)
                {
                    sashRecovery.Remove(unit.UnitInstanceId);
                    Heal(unit, unit.Stats.MaxHP * .1f, "crafted-focus-sash");
                }
            }
            if (battle.CurrentTick == 0)
            {
                foreach (var unit in battle.Units.Where(unit => unit.IsAlive && unit.IsOnBoard))
                {
                    var clay = Equipment(unit, "crafted-light-clay").ToArray();
                    if (clay.Length == 0) continue;
                    var ally = battle.Units.Where(other => other != unit && other.TeamId == unit.TeamId && other.IsAlive && other.IsOnBoard)
                        .OrderBy(other => HexCoordinates.Distance(unit.Position, other.Position))
                        .ThenBy(other => other.UnitInstanceId, StringComparer.Ordinal).FirstOrDefault();
                    float shield = unit.Stats.MaxHP * .08f;
                    foreach (var id in clay)
                    {
                        battle.Shields.Add(unit, unit.UnitInstanceId, id, shield, 5);
                        if (ally != null) battle.Shields.Add(ally, unit.UnitInstanceId, id, shield, 5);
                    }
                }
                return;
            }
            if (battle.CurrentTick % battle.TickRate != 0) return;
            foreach (var unit in battle.Units.Where(u => u.CurrentHP > 0 && u.IsOnBoard))
            {
                int leftovers = Count(unit, "crafted-leftovers");
                if (leftovers > 0) Heal(unit, unit.Stats.MaxHP * .012f * leftovers, "crafted-leftovers");
                int ppMax = Count(unit, "crafted-pp-max");
                if (ppMax > 0) battle.Energy.Gain(unit, 2 * ppMax, EnergyChangeReason.Item, capAtMax: true);
            }
            if (battle.CurrentTick % (3L * battle.TickRate) != 0) return;
            foreach(var u in battle.Units.Where(u=>u.CurrentHP>0&&u.IsOnBoard&&Has(u,ItemCatalog.Leftovers)))
                Heal(u,u.Stats.MaxHP*.04f,ItemCatalog.Leftovers);
        }
        internal bool HasChoiceBandBuff(UnitCombatState unit)
            => choiceBandUntil.TryGetValue(unit.UnitInstanceId, out var until) && until > battle.CurrentTick;

        internal bool HasStatusDamageReduction(UnitCombatState unit)
            => Equipment(unit, "crafted-protective-pads").Any(id => Trigger(id).Until > battle.CurrentTick);

        internal bool HasStatusBlockCharge(UnitCombatState unit)
            => Equipment(unit, "crafted-covert-cloak").Any(id => Trigger(id).Ready <= battle.CurrentTick);

        internal float Outgoing(UnitCombatState u,DamageRequest request)
        {
            if (request.IsBasicAttack && HasChoiceBandBuff(u))
                return 1 + .25f * Count(u, "crafted-choice-band");
            if (!IsSkillDamage(request)) return 1;
            float legacy = request.Origin != DamageOrigin.Direct ? 1
                : Has(u, ItemCatalog.WiseGlasses) ? 1.2f : Has(u, ItemCatalog.ChoiceScarf) ? .85f : 1;
            long castId = request.SkillPolicy?.CastId ?? u.ActiveSynergyCastId;
            return legacy * SkillDamageMultiplier(u, castId);
        }

        internal float SkillDamageMultiplier(UnitCombatState unit, long castId)
            => 1 + .35f * Count(unit, "crafted-life-orb") + FirstCastBonus(unit, castId);
        internal float ExposureMultiplier(UnitCombatState target)
            => 1 + .08f * exposure.Count(entry => entry.Key.target == target.UnitInstanceId && entry.Value > battle.CurrentTick);

        internal float IncomingReduction(UnitCombatState u, DamageRequest request, float reduction)
        {
            float remaining = 1 - reduction;
            foreach (var id in Equipment(u, "crafted-protective-pads"))
            {
                if (Trigger(id).Until <= battle.CurrentTick) continue;
                remaining *= .88f;
                if (request.Origin == DamageOrigin.Reflection) remaining *= .5f;
            }
            if (request.Type == DamageType.Magic && Has(u, ItemCatalog.AssaultVest)) remaining *= .75f;
            if (IsSkillDamage(request))
            {
                remaining *= (float)Math.Pow(.8, Count(u, "crafted-assault-vest"));
                if (request.IsAreaDamage) remaining *= (float)Math.Pow(.7, Count(u, "crafted-safety-goggles"));
            }
            return 1 - remaining;
        }

        internal static bool IsSkillDamage(DamageRequest request)
            => !request.IsBasicAttack && (request.Origin == DamageOrigin.Direct || request.Origin == DamageOrigin.Periodic);
        internal bool IsRecoveringFromSash(UnitCombatState unit)
            => sashRecovery.TryGetValue(unit.UnitInstanceId, out var until) && until > battle.CurrentTick;

        internal float AdjustHPDamage(UnitCombatState target,float hpDamage)
        {
            if(protection.TryGetValue(target.UnitInstanceId,out var until)&&battle.CurrentTick<until)return 0;
            if (!battle.PreserveHealthForPreview && hpDamage >= target.CurrentHP && target.CurrentHP > 0 && TryConsume(target, "crafted-focus-sash"))
            {
                sashRecovery[target.UnitInstanceId] = battle.CurrentTick + battle.TickRate;
                Emit("crafted-focus-sash", target, target, hpDamage);
                return 0;
            }
            if(hpDamage>=target.CurrentHP&&target.CurrentHP>0&&TryConsume(target,ItemCatalog.FocusSash)){
                protection[target.UnitInstanceId]=battle.CurrentTick+battle.TickRate;
                Emit(ItemCatalog.FocusSash,target,target,Math.Max(0,hpDamage-target.CurrentHP+1));
                return Math.Max(0,target.CurrentHP-1);
            }
            return hpDamage;
        }
        internal bool ConvertBasicMiss(UnitCombatState source)
            => SpendActiveCharge(source, "crafted-blunder-policy");

        internal bool HasGuaranteedBasicCritical(UnitCombatState source)
            => Equipment(source, "crafted-focus-band").Concat(Equipment(source, "crafted-clear-amulet"))
                .Any(id => Trigger(id).Charges > 0 && Trigger(id).Until > battle.CurrentTick);

        internal bool GuaranteeBasicCritical(UnitCombatState source)
            => SpendActiveCharge(source, "crafted-focus-band") || SpendActiveCharge(source, "crafted-clear-amulet");

        private bool SpendActiveCharge(UnitCombatState unit, string definition)
        {
            foreach (var id in Equipment(unit, definition))
            {
                var trigger = Trigger(id);
                if (trigger.Charges <= 0 || trigger.Until <= battle.CurrentTick) continue;
                trigger.Charges--;
                return true;
            }
            return false;
        }

        internal float AttackSpeedMultiplier(UnitCombatState unit)
        {
            float bonus = Equipment(unit, "crafted-metronome").Sum(id => Math.Min(8, Trigger(id).Hits)) * .04f;
            bonus += .25f * Equipment(unit, "crafted-blunder-policy").Count(id => Trigger(id).Until > battle.CurrentTick);
            bonus += .25f * Count(unit, "crafted-quick-claw") * (battle.CurrentTick < 5L * battle.TickRate ? 1 : 0);
            bonus += .05f * Equipment(unit, "crafted-adrenaline-orb")
                .Where(id => Trigger(id).Until > battle.CurrentTick).Sum(id => Trigger(id).Hits);
            return 1 + bonus;
        }

        internal float DynamicCritChance(UnitCombatState unit)
            => .05f * Equipment(unit, "crafted-scope-lens").Sum(id => Math.Min(5, Trigger(id).Hits))
                + .3f * Equipment(unit, "crafted-salac-berry").Count(consumed.Contains);

        internal void OnBasicAttack(UnitCombatState unit)
        {
            foreach (var id in Equipment(unit, "crafted-metronome").Concat(Equipment(unit, "crafted-scope-lens")))
                Trigger(id).Hits = Math.Min(8, Trigger(id).Hits + 1);
        }

        internal void AfterDamage(UnitCombatState source,UnitCombatState target,DamageResult result,DamageProcessor processor)
        {
            TryTriggerSitrusBerry(target);
            var request = result.Request;
            if (result.AppliedDamage > 0 && (request.Origin == DamageOrigin.Direct || request.Origin == DamageOrigin.Periodic))
            {
                int shellBell = Count(source, "crafted-shell-bell");
                if (shellBell > 0 && source.IsAlive && source.IsOnBoard)
                    Heal(source, result.AppliedDamage * shellBell * (request.IsAreaDamage || request.Origin == DamageOrigin.Periodic ? .04f : .12f)
                        * (1 + .25f * Count(source, "crafted-big-root")), "crafted-shell-bell");
            }
            if (result.AppliedDamage > 0 && target.CurrentHP > 0 && !battle.PreserveHealthForPreview)
                OnHealthThreshold(target);

            if (result.AppliedDamage > 0 && target.CurrentHP > 0 && !battle.PreserveHealthForPreview &&
                target.CurrentHP <= target.Stats.MaxHP * .5f)
                foreach (var id in Equipment(target, "crafted-salac-berry")) consumed.Add(id);

            if (result.PostMitigationDamage > 0 && request.Origin == DamageOrigin.Direct)
            {
                if (target.CurrentHP > 0 && target.IsOnBoard)
                {
                    foreach (var id in Equipment(target, "crafted-focus-band"))
                    {
                        var state = Trigger(id);
                        if (state.Ready > battle.CurrentTick || ++state.Hits < 4) continue;
                        state.Hits = 0;
                        state.Charges++;
                        state.Until = long.MaxValue;
                        state.Ready = battle.CurrentTick + 8L * battle.TickRate;
                        battle.Shields.Add(target, target.UnitInstanceId, id, target.Stats.MaxHP * .12f, 4);
                    }
                    if (request.Type == DamageType.Magic)
                    {
                        foreach (var id in Equipment(target, "crafted-muscle-band"))
                        {
                            var state = Trigger(id);
                            if (state.Ready > battle.CurrentTick) continue;
                            state.Ready = battle.CurrentTick + 8L * battle.TickRate;
                            state.Until = battle.CurrentTick + 4L * battle.TickRate;
                        }
                        foreach (var id in Equipment(target, "crafted-adrenaline-orb"))
                        {
                            var state = Trigger(id);
                            if (state.Ready > battle.CurrentTick) continue;
                            state.Hits = state.Until > battle.CurrentTick ? Math.Min(5, state.Hits + 1) : 1;
                            state.Ready = battle.CurrentTick + BattleSimulation.ToTicks(.5, battle.TickRate);
                            state.Until = battle.CurrentTick + 5L * battle.TickRate;
                        }
                    }
                }
                if (result.IsCritical)
                    foreach (var id in Equipment(source, "crafted-expert-belt"))
                    {
                        var key = (id, target.UnitInstanceId);
                        if (!exposure.TryGetValue(key, out long end) || end <= battle.CurrentTick)
                            exposure[key] = battle.CurrentTick + 3L * battle.TickRate;
                    }

                if (request.IsBasicAttack && source.CurrentHP > 0 && source.IsOnBoard)
                {
                    if (target.IsAlive && target.IsOnBoard)
                    {
                        foreach (var id in Equipment(source, "crafted-muscle-band"))
                        {
                            if (!target.IsAlive || !target.IsOnBoard) break;
                            float extra = source.EffectiveStats.Attack * (Trigger(id).Until > battle.CurrentTick ? .24f : .12f);
                            var hit = processor.Apply(battle, new DamageRequest(source.UnitInstanceId, target.UnitInstanceId,
                                extra, DamageType.Physical, origin: DamageOrigin.Item));
                            Emit("crafted-muscle-band", source, target, hit.AppliedDamage);
                        }
                    }
                    foreach (var id in Equipment(source, "crafted-protective-pads"))
                    {
                        var state = Trigger(id);
                        if (++state.Hits < 3) continue;
                        state.Hits = 0;
                        state.Until = battle.CurrentTick + 3L * battle.TickRate;
                    }
                    foreach (var id in Equipment(source, "crafted-punching-glove"))
                    {
                        var state = Trigger(id);
                        if (state.Ready > battle.CurrentTick || ++state.Hits < 3) continue;
                        state.Hits = 0;
                        state.Ready = battle.CurrentTick + 6L * battle.TickRate;
                        battle.Shields.Add(source, source.UnitInstanceId, id, source.Stats.MaxHP * .12f, 4);
                    }
                    foreach (var id in Equipment(source, "crafted-choice-scarf"))
                    {
                        var state = Trigger(id);
                        if (state.Ready <= battle.CurrentTick)
                        {
                            state.Hits = 0;
                            state.Ready = battle.CurrentTick + battle.TickRate;
                        }
                        if (state.Hits < 2 && battle.Energy.Gain(source, 2, EnergyChangeReason.Item, capAtMax: true)) state.Hits++;
                    }
                }
            }

            int craftedHelmet = Count(target, "crafted-rocky-helmet");
            if (craftedHelmet > 0 && request.IsBasicAttack && request.Origin == DamageOrigin.Direct &&
                result.PostMitigationDamage > 0 && source.Stats.AttackDelivery == AttackDeliveryType.Melee &&
                source.CurrentHP > 0 && source.IsOnBoard)
            {
                var hit = processor.Apply(battle, new DamageRequest(target.UnitInstanceId, source.UnitInstanceId,
                    target.Stats.MaxHP * .025f * craftedHelmet, DamageType.Physical, origin: DamageOrigin.Reflection));
                Emit("crafted-rocky-helmet", target, source, hit.AppliedDamage);
            }
            if(result.Request.Origin!=DamageOrigin.Direct||result.AppliedDamage<=0||!Has(target,ItemCatalog.RockyHelmet)||
                source.CurrentHP<=0||!source.IsOnBoard)return;
            var reflected=processor.Apply(battle,new DamageRequest(target.UnitInstanceId,source.UnitInstanceId,
                result.AppliedDamage*.2f,DamageType.True,origin:DamageOrigin.Reflection));
            Emit(ItemCatalog.RockyHelmet,target,source,reflected.AppliedDamage);
        }
        private void OnHealthThreshold(UnitCombatState unit)
        {
            float ratio = unit.CurrentHP / unit.Stats.MaxHP;
            if (ratio <= .6f && !stealthUntil.ContainsKey(unit.UnitInstanceId))
            {
                int count = Count(unit, "crafted-weakness-policy");
                if (count > 0)
                {
                    stealthUntil[unit.UnitInstanceId] = battle.CurrentTick + BattleSimulation.ToTicks(1.25 + .25 * (count - 1), battle.TickRate);
                    battle.StatusEffects.NotifyTargetability(unit);
                }
            }

            if (ratio > .5f) return;
            var cards = Equipment(unit, "crafted-red-card").Where(id => !consumed.Contains(id)).ToArray();
            if (cards.Length == 0) return;
            var targets = battle.Units.Where(enemy => enemy.TeamId != unit.TeamId && enemy.IsTargetable)
                .Select(enemy => (enemy, distance: HexCoordinates.Distance(unit.Position, enemy.Position)))
                .Where(entry => entry.distance <= 2).OrderBy(entry => entry.distance)
                .ThenBy(entry => entry.enemy.UnitInstanceId, StringComparer.Ordinal).Take(cards.Length).ToArray();
            for (int i = 0; i < cards.Length; i++)
            {
                string id = cards[i];
                consumed.Add(id);
                if (i >= targets.Length)
                {
                    battle.Shields.Add(unit, unit.UnitInstanceId, id, .05f * unit.Stats.MaxHP, 3);
                    continue;
                }
                var target = targets[i].enemy;
                battle.Movement.TryStart(unit, target, new SkillMovementDefinition(SkillMovementKind.Knockback, distance: 1), generated: true);
                battle.StatusEffects.Apply(new StatusEffectDefinition("item-red-card-stun", 1, crowdControl: CrowdControlKind.Stun),
                    unit.UnitInstanceId, target.UnitInstanceId);
            }
        }

        private static IEnumerable<string> Equipment(UnitCombatState unit, string definition)
            => unit.ItemInstanceIds.Where(id => ItemCatalog.DefinitionId(id) == definition);

        private TriggerState Trigger(string itemId)
        {
            if (!triggers.TryGetValue(itemId, out var state)) triggers.Add(itemId, state = new TriggerState());
            return state;
        }

        private bool TryConsume(UnitCombatState unit, string definition)
        {
            var instance = unit.ItemInstanceIds.FirstOrDefault(id => !consumed.Contains(id) && ItemCatalog.DefinitionId(id) == definition);
            return instance != null && consumed.Add(instance);
        }

        internal void TryTriggerSitrusBerry(UnitCombatState unit)
        {
            if (unit.CurrentHP > 0 && unit.IsOnBoard && unit.CurrentHP <= unit.Stats.MaxHP * .4f &&
                TryConsume(unit, ItemCatalog.SitrusBerry))
                Heal(unit, unit.Stats.MaxHP * .25f, ItemCatalog.SitrusBerry);
        }

        internal void OnSkillCompleted(UnitCombatState caster)
        {
            int lifeOrb = Count(caster, "crafted-life-orb");
            if (lifeOrb > 0 && caster.CurrentHP > 1 && !battle.PreserveHealthForPreview)
                caster.SetVitals(Math.Max(1, caster.CurrentHP - caster.Stats.MaxHP * .03f * lifeOrb), caster.CurrentEnergy);
        }

        internal void OnSkillEffect(UnitCombatState caster)
        {
            foreach (var id in Equipment(caster, "crafted-big-root"))
            {
                var state = Trigger(id);
                if (state.Ready > battle.CurrentTick) continue;
                state.Ready = battle.CurrentTick + 5L * battle.TickRate;
                Heal(caster, caster.Stats.MaxHP * .04f, "crafted-big-root");
            }
            foreach (var id in Equipment(caster, "crafted-blunder-policy"))
            {
                Trigger(id).Until = battle.CurrentTick + 4L * battle.TickRate;
                Trigger(id).Charges = 1;
            }
            foreach (var id in Equipment(caster, "crafted-clear-amulet"))
            {
                var state = Trigger(id);
                if (state.Ready > battle.CurrentTick) continue;
                state.Ready = state.Until = battle.CurrentTick + 6L * battle.TickRate;
                state.Charges = 1;
            }
            if (Has(caster, "crafted-choice-band"))
                choiceBandUntil[caster.UnitInstanceId] = battle.CurrentTick + 5L * battle.TickRate;
            foreach(var u in battle.Units.Where(u=>u.TeamId==caster.TeamId&&u!=caster&&u.CurrentHP>0&&u.IsOnBoard&&
                Has(u,ItemCatalog.ExpShare)&&HexCoordinates.IsInRange(u.Position,caster.Position,1))){
                if(shareReady.TryGetValue(u.UnitInstanceId,out var until)&&battle.CurrentTick<until)continue;
                if(battle.Energy.Gain(u,10,EnergyChangeReason.Item)){
                    shareReady[u.UnitInstanceId]=battle.CurrentTick+2L*battle.TickRate;
                    Emit(ItemCatalog.ExpShare,caster,u,10);
                }
            }
        }
        private void Heal(UnitCombatState u,float amount,string item)
        {
            float before=u.CurrentHP;battle.Healing.Apply(u,amount,false);
            if(u.CurrentHP>before)Emit(item,u,u,u.CurrentHP-before);
        }
        private void Emit(string item,UnitCombatState source,UnitCombatState target,float value)
            =>events.Add(new ItemCombatEvent(battle.CurrentTick,item,source.UnitInstanceId,target.UnitInstanceId,value));
    }
}
