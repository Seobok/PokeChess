using System;
using System.Collections.Generic;
using System.Linq;

namespace PokeChess.Core.Match
{
    // Host monotonic time, independent of simulation speed and the client's clock.
    public sealed class MatchReconnectSystem
    {
        public const double GraceSeconds=120;
        private sealed class Entry {public bool disconnected,expired;public double deadline;}
        private readonly Dictionary<string,Entry> entries=new Dictionary<string,Entry>(StringComparer.Ordinal);
        private readonly MatchState match;
        private readonly LocalRoundCoordinator rounds;
        public MatchReconnectSystem(MatchState match,LocalRoundCoordinator rounds)
        {this.match=match;this.rounds=rounds;foreach(var p in match.Players)entries.Add(p.PlayerId,new Entry());}
        public bool Disconnect(string id,double now)
        {
            if(!entries.TryGetValue(id,out var e)||e.disconnected||e.expired||match.GetPlayer(id).IsEliminated||match.Phase==MatchPhase.Finished)return false;
            e.disconnected=true;e.deadline=now+GraceSeconds;return true;
        }
        public string Reconnect(string id,double now)
        {
            if(!entries.TryGetValue(id,out var e))return "NotMatchParticipant";
            if(e.expired||e.disconnected&&now>=e.deadline)return "ReconnectExpired";
            if(match.GetPlayer(id).IsEliminated)return "PlayerAlreadyEliminated";
            if(match.Phase==MatchPhase.Finished)return "MatchFinished";
            e.disconnected=false;e.deadline=0;return null;
        }
        public bool IsDisconnected(string id)=>entries.TryGetValue(id,out var e)&&e.disconnected;
        public double Deadline(string id)=>entries.TryGetValue(id,out var e)&&e.disconnected?e.deadline:0;
        public string[] Expire(double now)
        {
            var changed=new List<string>();
            foreach(var pair in entries.Where(p=>p.Value.disconnected&&!p.Value.expired&&now>=p.Value.deadline).ToArray()){
                pair.Value.expired=true;
                if(rounds.RemoveDisconnectedPlayer(pair.Key).Accepted)changed.Add(pair.Key);
            }
            return changed.ToArray();
        }
    }
}
