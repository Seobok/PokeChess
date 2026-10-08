using System;
using System.Collections.Generic;
using System.Linq;
using PokeChess.Core.Pokemon;

namespace PokeChess.Core.Match
{
    public enum MatchCommandKind { Sync, Buy, Move, Sell, Reroll, Lock, BuyXP, Surrender }
    [Serializable] public sealed class MatchCommand
    {
        public int protocol=1,round,slot,column,row,bench;
        public long sequence,playerRevision,shopRevision,placementRevision;
        public string matchId,commandId,playerId,unitId;
        public MatchPhase phase;
        public MatchCommandKind kind;
        public PlacementKind destination;
        public bool locked;
        public MatchCommand Copy()=>(MatchCommand)MemberwiseClone();
        public bool SamePayload(MatchCommand b)=>b!=null&&protocol==b.protocol&&round==b.round&&slot==b.slot&&column==b.column&&row==b.row&&bench==b.bench&&sequence==b.sequence&&playerRevision==b.playerRevision&&shopRevision==b.shopRevision&&placementRevision==b.placementRevision&&matchId==b.matchId&&commandId==b.commandId&&playerId==b.playerId&&unitId==b.unitId&&phase==b.phase&&kind==b.kind&&destination==b.destination&&locked==b.locked;
    }
    [Serializable] public sealed class OwnerShopSlot { public int index,cost;public string definitionId; }
    [Serializable] public sealed class OwnerUnit { public string id,definitionId;public int rank,column,row,bench,saleGold;public PlacementKind placement;public string[] items; }
    [Serializable] public sealed class OwnerMatchState
    {
        public string matchId,playerId;public int round,gold,xp,level,hp,winStreak,loseStreak;public MatchPhase phase;
        public long revision,playerRevision,shopRevision,placementRevision,nextSequence;
        public bool locked,eliminated;public OwnerShopSlot[] shop;public OwnerUnit[] units;public string[] inventory;public OwnerRoundReward reward;
    }
    [Serializable] public sealed class CommandAck
    {
        public string matchId,commandId,code,message;public long sequence,nextSequence;public bool accepted;
        public OwnerMatchState state;
    }
    // Single Host thread owns this processor. Never accepts a client-supplied identity as authority.
    public sealed class HostCommandProcessor
    {
        private sealed class Cached { public MatchCommand command;public CommandAck ack; }
        private sealed class PlayerLedger
        {
            public long sequence,revision;public readonly Dictionary<string,Cached> cache=new Dictionary<string,Cached>(StringComparer.Ordinal);
            public readonly Queue<string> order=new Queue<string>();
        }
        private readonly MatchState match;
        private readonly PokemonCatalog catalog;
        private readonly LocalRoundCoordinator rounds;
        private readonly Dictionary<string,PlayerLedger> ledgers=new Dictionary<string,PlayerLedger>(StringComparer.Ordinal);
        private long revision;
        private double? phaseEndsAt;
        public MatchState Match=>match;
        public long StateVersion=>revision;
        // Future timer/settlement writers must publish through this hook after changing Core state.
        public void NotifyHostStateChanged()
        {
            if(revision==long.MaxValue||ledgers.Values.Any(l=>l.revision==long.MaxValue))throw new OverflowException("State version exhausted.");
            revision++;foreach(var ledger in ledgers.Values)ledger.revision++;
        }
        public void SetPhaseDeadline(double? value){phaseEndsAt=value;}
        public MatchReconnectSystem Reconnect {get;private set;}
        public PublicMatchState PublicSnapshot(){var value=MatchStateProjection.Public(match,rounds,revision);value.hasPhaseDeadline=phaseEndsAt.HasValue;value.phaseEndsAt=phaseEndsAt??0;foreach(var p in value.players){p.disconnected=Reconnect.IsDisconnected(p.id);p.reconnectDeadline=Reconnect.Deadline(p.id);}return value;}
        public MatchStateSnapshot FullSnapshot(string playerId)=>new MatchStateSnapshot{protocol=2,publicState=PublicSnapshot(),ownerState=Snapshot(playerId)};
        public HostCommandProcessor(MatchState match,PokemonCatalog catalog,LocalRoundCoordinator rounds)
        {this.match=match;this.catalog=catalog;this.rounds=rounds;Reconnect=new MatchReconnectSystem(match,rounds);foreach(var p in match.Players)ledgers.Add(p.PlayerId,new PlayerLedger());}
        public OwnerMatchState Snapshot(string playerId)
        {
            var p=match.GetPlayer(playerId);var ledger=ledgers[playerId];
            return new OwnerMatchState{matchId=match.MatchId,playerId=playerId,round=match.RoundNumber,phase=match.Phase,revision=revision,playerRevision=ledger.revision,nextSequence=ledger.sequence+1,
                gold=p.Gold,xp=p.XP,level=p.Level,hp=p.HP,winStreak=p.WinStreak,loseStreak=p.LoseStreak,eliminated=p.IsEliminated,locked=p.Shop.IsLocked,shopRevision=p.Shop.Revision,placementRevision=p.PlacementRevision,
                shop=p.Shop.Slots.Select(s=>new OwnerShopSlot{index=s.Index,cost=s.Cost,definitionId=s.DefinitionId}).ToArray(),
                units=p.Units.Select(u=>new OwnerUnit{id=u.InstanceId,definitionId=u.DefinitionId,rank=(int)u.Rank,placement=u.Placement.Kind,column=u.Placement.Position?.Column??-1,row=u.Placement.Position?.Row??-1,bench=u.Placement.BenchSlot??-1,items=u.ItemInstanceIds.ToArray(),saleGold=new ShopTransactionSystem(catalog).SalePrice(u)}).ToArray(),
                inventory=p.ItemInventory.ToArray(),reward=MatchStateProjection.Reward(rounds,playerId)};
        }
        public CommandAck Process(string authenticatedPlayer,MatchCommand c)
        {
            if(authenticatedPlayer==null||!ledgers.TryGetValue(authenticatedPlayer,out var ledger))return Reject(c,"Unauthenticated",null);
            if(c==null||c.protocol!=1||string.IsNullOrEmpty(c.commandId)||c.commandId.Length>64||c.sequence<1||!Enum.IsDefined(typeof(MatchCommandKind),c.kind)||!Enum.IsDefined(typeof(MatchPhase),c.phase)||
                (c.unitId?.Length??0)>64||(c.playerId?.Length??0)>64||(c.matchId?.Length??0)>64)return Reject(c,"InvalidRequest",authenticatedPlayer);
            if(c.matchId!=match.MatchId)return Reject(c,"WrongMatch",authenticatedPlayer);
            if(ledger.cache.TryGetValue(c.commandId,out var cached))return cached.command.SamePayload(c)?cached.ack:Reject(c,"DuplicateConflict",authenticatedPlayer);
            if(c.sequence!=ledger.sequence+1)return Reject(c,c.sequence<=ledger.sequence?"ExpiredCommand":"SequenceGap",authenticatedPlayer);
            ledger.sequence=c.sequence;
            string code="None",message="Accepted.";bool changed=false;
            if(c.playerId!=authenticatedPlayer)code="WrongPlayer";
            else if(c.kind!=MatchCommandKind.Sync)
            {
                var p=match.GetPlayer(authenticatedPlayer);
                if(match.Phase==MatchPhase.Finished)code="MatchFinished";
                else if(c.round!=match.RoundNumber)code="WrongRound";
                else if(c.phase!=match.Phase)code="WrongPhase";
                else if(c.playerRevision!=ledger.revision)code="StaleRevision";
                else if(p.IsEliminated||p.HP==0)code="Eliminated";
                else if(revision==long.MaxValue||ledger.revision==long.MaxValue)code="RevisionOverflow";
                else
                {
                    var executor=new MatchCommandExecutor(match,catalog,authenticatedPlayer);
                    try {
                        MatchCommandResult result=null;
                        switch(c.kind){
                            case MatchCommandKind.Buy:result=executor.Buy(c.slot,c.shopRevision);break;
                            case MatchCommandKind.Sell:result=executor.Sell(c.unitId,c.placementRevision);break;
                            case MatchCommandKind.Reroll:result=executor.Reroll(c.shopRevision);break;
                            case MatchCommandKind.Lock:changed=p.Shop.IsLocked!=c.locked;result=executor.Lock(c.locked,c.shopRevision);break;
                            case MatchCommandKind.BuyXP:result=executor.BuyXP();break;
                            case MatchCommandKind.Move:
                                if(c.destination!=PlacementKind.Board&&c.destination!=PlacementKind.Bench){code="InvalidDestination";break;}
                                var destination=c.destination==PlacementKind.Board?UnitPlacement.OnBoard(new BoardPosition(c.column,c.row)):UnitPlacement.OnBench(c.bench);
                                var move=new PlayerPlacementSystem().Move(match,authenticatedPlayer,c.unitId,destination,c.placementRevision);code=move.Accepted?"None":move.Reason.ToString();changed=move.Accepted&&move.Outcome!=PlacementOutcome.Unchanged;break;
                            case MatchCommandKind.Surrender:
                                var surrender=rounds.Surrender(authenticatedPlayer,c.round,c.phase);code=surrender.Accepted?"None":"SurrenderUnavailable";changed=surrender.Accepted&&!surrender.AlreadySurrendered;message=surrender.Message;break;
                        }
                        if(result!=null){code=result.Failure.ToString();message=result.Message;if(result.Accepted&&c.kind!=MatchCommandKind.Lock)changed=true;}
                    }catch(ArgumentException){code="InvalidRequest";changed=false;}
                    catch(KeyNotFoundException){code="UnitNotOwned";changed=false;}
                    catch(InvalidOperationException){code="Unavailable";changed=false;}
                    catch(OverflowException){code="InvalidRequest";changed=false;}
                }
            }
            if(code=="None"&&changed){revision++;ledger.revision++;}
            var ack=new CommandAck{matchId=match.MatchId,commandId=c.commandId,sequence=c.sequence,nextSequence=ledger.sequence+1,accepted=code=="None",code=code,message=code=="None"?message:code,state=Snapshot(authenticatedPlayer)};
            ledger.cache.Add(c.commandId,new Cached{command=c.Copy(),ack=ack});ledger.order.Enqueue(c.commandId);if(ledger.order.Count>128)ledger.cache.Remove(ledger.order.Dequeue());return ack;
        }
        private CommandAck Reject(MatchCommand c,string code,string player)=>new CommandAck{matchId=match.MatchId,commandId=c?.commandId,sequence=c?.sequence??0,nextSequence=player==null?1:ledgers[player].sequence+1,accepted=false,code=code,message=code,state=player==null?null:Snapshot(player)};
    }
}
