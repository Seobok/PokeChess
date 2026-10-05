using System;

namespace PokeChess.Core.Match
{
    public sealed class EconomySystem
    {
        public EconomyRules Rules { get; }
        public EconomySystem(EconomyRules rules = null) => Rules = rules ?? new EconomyRules();

        // Recomputes from current state; a preview cannot later be submitted as a stale payout.
        public RoundIncome Preview(PlayerState player, int roundNumber, RoundOutcome outcome)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (roundNumber <= 0) throw new ArgumentOutOfRangeException(nameof(roundNumber));
            if (!Enum.IsDefined(typeof(RoundOutcome), outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
            if ((long)roundNumber != (long)player.LastEconomyRound + 1)
                throw new InvalidOperationException("Economy rounds must be settled once, in order.");
            int wins = outcome == RoundOutcome.Win ? checked(player.WinStreak + 1) : 0;
            int losses = outcome == RoundOutcome.Loss ? checked(player.LoseStreak + 1) : 0;
            int interest = Math.Min(player.Gold / Rules.GoldPerInterest, Rules.MaxInterest);
            int bonus = Rules.StreakBonus(Math.Max(wins, losses));
            return new RoundIncome(player.PlayerId, roundNumber, outcome, player.Gold,
                Rules.BaseIncome, interest, bonus, wins, losses);
        }

        // Host round coordinator calls during Result after determining eligible recipients.
        // Phase, elimination and pairing policy are outside this calculation service.
        public RoundIncome Settle(PlayerState player, int roundNumber, RoundOutcome outcome)
        {
            var income = Preview(player, roundNumber, outcome);
            player.ApplyRoundIncome(income);
            return income;
        }
    }
}
