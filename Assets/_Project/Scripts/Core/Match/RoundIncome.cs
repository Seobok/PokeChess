namespace PokeChess.Core.Match
{
    // Perspective of the recipient; shadow opponents must not be settled as real players.
    public enum RoundOutcome { Win, Loss, Draw }

    public sealed class RoundIncome
    {
        public string PlayerId { get; }
        public int RoundNumber { get; }
        public RoundOutcome Outcome { get; }
        public int GoldBefore { get; }
        public int BaseIncome { get; }
        public int Interest { get; }
        public int StreakBonus { get; }
        public int TotalIncome { get; }
        public int GoldAfter { get; }
        public int WinStreak { get; }
        public int LoseStreak { get; }

        internal RoundIncome(string playerId, int roundNumber, RoundOutcome outcome, int goldBefore,
            int baseIncome, int interest, int streakBonus, int winStreak, int loseStreak)
        {
            PlayerId = playerId;
            RoundNumber = roundNumber;
            Outcome = outcome;
            GoldBefore = goldBefore;
            BaseIncome = baseIncome;
            Interest = interest;
            StreakBonus = streakBonus;
            TotalIncome = checked(baseIncome + interest + streakBonus);
            GoldAfter = checked(goldBefore + TotalIncome);
            WinStreak = winStreak;
            LoseStreak = loseStreak;
        }
    }
}
