using System;

namespace PokeChess.Core.Match
{
    public sealed class LevelSystem
    {
        public LevelRules Rules { get; }
        public LevelSystem(LevelRules rules = null) => Rules = rules ?? new LevelRules();
        private void Validate(PlayerState player)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (player.Rules.MaxLevel != Rules.MaxLevel)
                throw new ArgumentException("Match maximum level must match the XP table.", nameof(player));
        }
        private LevelProgress Calculate(PlayerState player, int amount, int goldAfter)
        {
            Validate(player);
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            int level = player.Level;
            int granted = level == Rules.MaxLevel ? 0 : amount;
            // Wider intermediate supports large grants without overflowing stored int XP.
            long xp = (long)player.XP + granted;
            while (level < Rules.MaxLevel && xp >= Rules.XPToNextLevel(level))
            {
                xp -= Rules.XPToNextLevel(level);
                level++;
            }
            if (level == Rules.MaxLevel) xp = 0;
            return new LevelProgress(player, goldAfter, level, checked((int)xp), granted);
        }
        public LevelProgress PreviewXP(PlayerState player, int amount)
        {
            Validate(player);
            return Calculate(player, amount, player.Gold);
        }
        // Trusted Core entry point for rewards; command validation belongs at the caller.
        public LevelProgress GrantXP(PlayerState player, int amount)
        {
            var change = PreviewXP(player, amount);
            player.ApplyLevelProgress(change);
            return change;
        }
        public LevelProgress BuyXP(MatchState match, string playerId)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            var player = match.GetPlayer(playerId);
            Validate(player);
            if (match.Phase != MatchPhase.Preparation)
                throw new InvalidOperationException("XP can only be purchased during Preparation.");
            if (player.Level == Rules.MaxLevel) throw new InvalidOperationException("Already at maximum level.");
            if (player.Gold < Rules.PurchaseGoldCost) throw new InvalidOperationException("Insufficient gold.");
            var change = Calculate(player, Rules.PurchaseXP, player.Gold - Rules.PurchaseGoldCost);
            player.ApplyLevelProgress(change);
            return change;
        }
        // Result coordinator supplies eligible recipients, independently of economic settlement.
        public LevelProgress AwardAutomaticXP(PlayerState player, int roundNumber)
        {
            Validate(player);
            if (roundNumber <= 0) throw new ArgumentOutOfRangeException(nameof(roundNumber));
            if ((long)roundNumber != (long)player.LastAutomaticXPRound + 1)
                throw new InvalidOperationException("Automatic XP rounds must be awarded once, in order.");
            var change = Calculate(player, Rules.AutomaticXP, player.Gold);
            player.ApplyLevelProgress(change, roundNumber);
            return change;
        }
    }
}
