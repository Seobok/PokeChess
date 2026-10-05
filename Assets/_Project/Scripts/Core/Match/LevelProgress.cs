namespace PokeChess.Core.Match
{
    public sealed class LevelProgress
    {
        public string PlayerId { get; }
        public int GoldBefore { get; }
        public int GoldAfter { get; }
        public int GoldSpent => GoldBefore - GoldAfter;
        public int LevelBefore { get; }
        public int LevelAfter { get; }
        public int XPBefore { get; }
        public int XPAfter { get; }
        // Zero when already at max level. Excess XP is discarded upon reaching max.
        public int XPGranted { get; }
        internal LevelProgress(PlayerState player, int goldAfter, int levelAfter, int xpAfter, int xpGranted)
        {
            PlayerId = player.PlayerId;
            GoldBefore = player.Gold;
            GoldAfter = goldAfter;
            LevelBefore = player.Level;
            LevelAfter = levelAfter;
            XPBefore = player.XP;
            XPAfter = xpAfter;
            XPGranted = xpGranted;
        }
    }
}
