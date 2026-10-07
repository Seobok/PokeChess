using System;
using System.Linq;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    [Serializable] public sealed class PublicBoardUnit
    {
        public string id,definitionId;public int rank,column,row;public string[] items;
    }
    [Serializable] public sealed class PublicPlayer
    {
        public string id;public int hp,level,placement;public bool eliminated;public PublicBoardUnit[] board;
    }
    [Serializable] public sealed class PublicRoundResult
    {
        public string playerId,opponentId,outcome;public bool shadow;public int opponentSurvivors;
    }
    [Serializable] public sealed class PublicMatchState
    {
        public string matchId,winnerPlayerId;public int round;public MatchPhase phase;public long revision;
        // No deadline is invented while the automatic online round driver is not connected.
        public bool hasPhaseDeadline;public double phaseEndsAt;public PublicPlayer[] players;public PublicRoundResult[] results;
    }
    [Serializable] public sealed class OwnerRoundReward
    {
        public int round,baseIncome,interest,streakBonus,totalIncome,goldBefore,goldAfter;
    }
    [Serializable] public sealed class MatchStateSnapshot
    {
        public int protocol=2;public PublicMatchState publicState;public OwnerMatchState ownerState;
    }
    public static class MatchStateProjection
    {
        public static OwnerRoundReward Reward(LocalRoundCoordinator rounds,string playerId)
        {
            if(rounds.LastResult==null||!rounds.LastResult.Income.TryGetValue(playerId,out var value))return null;
            return new OwnerRoundReward{round=value.RoundNumber,baseIncome=value.BaseIncome,interest=value.Interest,streakBonus=value.StreakBonus,totalIncome=value.TotalIncome,goldBefore=value.GoldBefore,goldAfter=value.GoldAfter};
        }
        public static PublicMatchState Public(MatchState match,LocalRoundCoordinator rounds,long version)
        {
            return new PublicMatchState{matchId=match.MatchId,round=match.RoundNumber,phase=match.Phase,revision=version,
                winnerPlayerId=match.FinalResult?.WinnerPlayerId,
                players=match.Players.Select(p=>new PublicPlayer{id=p.PlayerId,hp=p.HP,level=p.Level,eliminated=p.IsEliminated,
                    placement=match.FinalResult?.Standings.FirstOrDefault(r=>r.PlayerId==p.PlayerId)?.Placement??p.FinalPlacement??0,
                    board=p.Units.Where(u=>u.Placement.Kind==PlacementKind.Board).Select(u=>new PublicBoardUnit{id=u.InstanceId,definitionId=u.DefinitionId,
                        rank=(int)u.Rank,column=u.Placement.Position.Value.Column,row=u.Placement.Position.Value.Row,items=u.ItemInstanceIds.ToArray()}).ToArray()}).ToArray(),
                results=rounds.LastResult?.PlayerResults.Values.Select(r=>new PublicRoundResult{playerId=r.PlayerId,opponentId=r.OpponentId,outcome=r.Outcome.ToString(),shadow=r.IsShadow,opponentSurvivors=r.OpponentSurvivors}).ToArray()??Array.Empty<PublicRoundResult>()};
        }
    }
    // Applies a whole version atomically; never accepts partial state as an initial baseline.
    public sealed class MatchSnapshotStore
    {
        public PublicMatchState Public {get;private set;}
        public OwnerMatchState Owner {get;private set;}
        public bool Apply(MatchStateSnapshot snapshot,string matchId,string playerId)
        {
            var p=snapshot?.publicState;var o=snapshot?.ownerState;
            if(snapshot?.protocol!=2||p==null||o==null||p.matchId!=matchId||o.matchId!=matchId||o.playerId!=playerId||p.revision!=o.revision||p.round!=o.round||p.phase!=o.phase||
                p.players==null||p.players.Length<2||p.players.Length>8||p.players.Any(v=>v==null||string.IsNullOrEmpty(v.id)||v.board==null||v.board.Length>28||v.board.Any(u=>u==null||string.IsNullOrEmpty(u.id)||string.IsNullOrEmpty(u.definitionId)||u.items==null||u.column<0||u.column>=7||u.row<0||u.row>=4))||
                p.players.Select(v=>v.id).Distinct().Count()!=p.players.Length||!p.players.Any(v=>v.id==playerId)||p.results==null||o.shop==null||o.shop.Length!=5||o.shop.Any(s=>s==null)||o.units==null||o.inventory==null||o.nextSequence<1||p.revision<0)return false;
            if(Public!=null&&(p.revision<Public.revision||o.nextSequence<Owner.nextSequence))return false;
            Public=p;Owner=o;return true;
        }
        public void Clear(){Public=null;Owner=null;}
    }
}
