using System;
namespace PokeChess.Core.Match
{
    public sealed class PlayerDamageRules
    {
        public int SurvivorDamage { get; }
        public PlayerDamageRules(int survivorDamage=1)
        { if(survivorDamage<0)throw new ArgumentOutOfRangeException(nameof(survivorDamage));SurvivorDamage=survivorDamage; }
        public int BaseDamage(int round)
        {
            if(round<1)throw new ArgumentOutOfRangeException(nameof(round));
            return round<=4 ? 2 : round<=8 ? 4 : round<=12 ? 6 : round<=16 ? 8 : 10;
        }
        public int Calculate(int round,RoundOutcome outcome,int opponentSurvivors)
        {
            BaseDamage(round);
            if(!Enum.IsDefined(typeof(RoundOutcome),outcome))throw new ArgumentOutOfRangeException(nameof(outcome));
            if(opponentSurvivors<0)throw new ArgumentOutOfRangeException(nameof(opponentSurvivors));
            return outcome==RoundOutcome.Win ? 0 : checked((outcome==RoundOutcome.Loss ? BaseDamage(round) : 0)+opponentSurvivors*SurvivorDamage);
        }
    }

    public sealed class PlayerDamageResult
    {
        public string PlayerId { get; }
        public int Round { get; }
        public RoundOutcome Outcome { get; }
        public int OpponentSurvivors { get; }
        public int BaseDamage { get; }
        public int SurvivorDamage { get; }
        public int TotalDamage { get; }
        public int HPBefore { get; }
        public int RawHPAfter { get; }
        public int HPAfter { get; }
        public int AppliedDamage => HPBefore-HPAfter;
        internal PlayerDamageResult(PlayerState player,int round,RoundOutcome outcome,int survivors,PlayerDamageRules rules)
        {
            PlayerId=player.PlayerId;Round=round;Outcome=outcome;OpponentSurvivors=survivors;
            TotalDamage=rules.Calculate(round,outcome,survivors);
            BaseDamage=outcome==RoundOutcome.Loss ? rules.BaseDamage(round) : 0;
            SurvivorDamage=TotalDamage-BaseDamage;HPBefore=player.HP;RawHPAfter=HPBefore-TotalDamage;HPAfter=Math.Max(0,RawHPAfter);
        }
    }

    // Host result application only; never uses preparation-board ownership as a survivor count.
    public sealed class PlayerDamageSystem
    {
        public PlayerDamageRules Rules { get; }
        public PlayerDamageSystem(PlayerDamageRules rules=null) { Rules=rules??new PlayerDamageRules(); }
        public PlayerDamageResult Preview(PlayerState player,int round,RoundOutcome outcome,int opponentSurvivors)
        {
            if(player==null)throw new ArgumentNullException(nameof(player));
            int total=Rules.Calculate(round,outcome,opponentSurvivors);
            if(player.LastDamageRound==round)
            {
                var prior=player.LastDamage;
                if(prior==null || prior.Outcome!=outcome || prior.OpponentSurvivors!=opponentSurvivors || prior.TotalDamage!=total)
                    throw new InvalidOperationException("Conflicting damage replay.");
                return prior;
            }
            if((long)player.LastDamageRound+1!=round)throw new InvalidOperationException("Player damage round gap or stale request.");
            return new PlayerDamageResult(player,round,outcome,opponentSurvivors,Rules);
        }
        public PlayerDamageResult Apply(PlayerState player,int round,RoundOutcome outcome,int opponentSurvivors)
        {
            var plan=Preview(player,round,outcome,opponentSurvivors);Apply(player,plan);return plan;
        }
        internal void Apply(PlayerState player,PlayerDamageResult plan) => player.ApplyDamage(plan);
    }
}

