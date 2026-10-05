namespace PokeChess.Core.Match
{
    // A later pairing runtime can step all active battles and enter Result when all finish.
    // This clock only needs phase/readiness, never individual fighters or pairing details.
    public interface IRoundFlowRuntime
    {
        MatchState Match { get; }
        bool PreparationReady { get; }
        bool NextPreparationPending { get; }
        bool IsSettled { get; }
        int TickRate { get; }
        bool StartCombat(int expectedRound);
        void Step();
        void SettleResult();
        bool NextRound(int completedRound);
    }
}
