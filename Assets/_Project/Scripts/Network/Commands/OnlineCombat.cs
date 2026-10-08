using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using PokeChess.Core.Match;

namespace PokeChess.Network.Session
{
    public sealed partial class OnlineConnection
    {
        private const string CombatMessage="PokeChess.Combat.v1";
        private const string WatchMessage="PokeChess.Watch.v1";
        [Serializable] private sealed class WatchRequest {public string matchId,playerId;}
        private readonly Dictionary<ulong,string> combatWatchers=new Dictionary<ulong,string>();
        private string watchingPlayer;
        public string WatchingPlayerId=>watchingPlayer??session?.CurrentPlayer.Id;
        public bool WatchPlayer(string playerId)
        {
            if(StartInfo==null||session==null||PublicState==null||!PublicState.players.Any(p=>p.id==playerId))return false;
            if(WatchingPlayerId==playerId&&Combat.Latest!=null)return true;
            watchingPlayer=playerId;Combat.Clear();
            if(IsHost){combatWatchers[NetworkManager.ServerClientId]=playerId;SendCombatBaseline(NetworkManager.ServerClientId,session.CurrentPlayer.Id);}
            else SendText(WatchMessage,NetworkManager.ServerClientId,JsonUtility.ToJson(new WatchRequest{matchId=StartInfo.matchId,playerId=playerId}),512);
            return true;
        }
        private string WatchedPlayer(ulong recipient,string owner)=>combatWatchers.TryGetValue(recipient,out var selected)?selected:owner;
        private void OnWatch(ulong sender,FastBufferReader reader)
        {
            if(!IsHost||hostCommands==null||!boundPlayers.TryGetValue(sender,out var owner))return;
            try{var request=JsonUtility.FromJson<WatchRequest>(ReadText(reader,512));if(request?.matchId!=StartInfo?.matchId||!hostCommands.Match.Players.Any(p=>p.PlayerId==request.playerId))return;
                if(!incomingTimes.TryGetValue(sender,out var times))incomingTimes[sender]=times=new Queue<double>();double now=Time.realtimeSinceStartupAsDouble;
                while(times.Count>0&&now-times.Peek()>1)times.Dequeue();if(times.Count>=12)return;times.Enqueue(now);
                combatWatchers[sender]=request.playerId;SendCombatBaseline(sender,owner);
            }catch(Exception){/* Malformed spectator request never mutates match state. */}
        }
        private OnlineCombatRuntime combatRuntime;
        private readonly Dictionary<string,List<CombatFrame>> combatFrames=new Dictionary<string,List<CombatFrame>>();
        private readonly Dictionary<string,CombatFrame> latestCombat=new Dictionary<string,CombatFrame>();
        private double lastCombatTime,nextCombatSend;
        private MatchPhase deadlinePhase;private int deadlineRound,combatFrameRound;
        public CombatPlayback Combat {get;}=new CombatPlayback();
        public double PhaseRemainingSeconds=>PublicState?.hasPhaseDeadline==true?Math.Max(0,PublicState.phaseEndsAt-NetworkServerTime):0;
        public string CombatError {get;private set;}
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public double QAOnlineSpeed {get;set;}=1;
        public bool QAHoldCombat {get;set;}
        public OnlineCombatRuntime QACombatRuntime=>combatRuntime;
#endif
        private void RegisterCombatMessages(){manager.CustomMessagingManager.RegisterNamedMessageHandler(CombatMessage,OnCombat);manager.CustomMessagingManager.RegisterNamedMessageHandler(WatchMessage,OnWatch);}
        private void ResetCombat(){combatRuntime=null;combatFrames.Clear();latestCombat.Clear();combatWatchers.Clear();watchingPlayer=null;Combat.Clear();lastCombatTime=nextCombatSend=0;deadlineRound=combatFrameRound=0;CombatError=null;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            QAOnlineSpeed=1;QAHoldCombat=false;
#endif
        }
        private void CreateCombatRuntime(LocalRoundCoordinator rounds)
        {
            combatRuntime=new OnlineCombatRuntime(rounds,hostCommands);lastCombatTime=Time.realtimeSinceStartupAsDouble;
            combatRuntime.FrameProduced+=frame=>{
                if(combatFrameRound!=frame.round){combatFrames.Clear();latestCombat.Clear();combatFrameRound=frame.round;}
                // A player has exactly one real pairing per round. Shadow source receives its real battle only.
                foreach(var id in frame.shadow?new[]{frame.one}:new[]{frame.one,frame.two}){
                    if(!combatFrames.TryGetValue(id,out var frames))combatFrames[id]=frames=new List<CombatFrame>();frames.Add(frame);latestCombat[id]=frame;
                }
            };
        }
        private void AdvanceCombat()
        {
            if(combatRuntime==null)return;
            double now=Time.realtimeSinceStartupAsDouble,elapsed=Math.Max(0,now-lastCombatTime);lastCombatTime=now;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            elapsed*=QAOnlineSpeed;
#endif
            var oldRound=hostCommands.Match.RoundNumber;var oldPhase=hostCommands.Match.Phase;
            combatRuntime.Advance(elapsed);
            if(combatRuntime.Clock.Fault!=null){CombatError=combatRuntime.Clock.Fault.GetType().Name+": "+combatRuntime.Clock.Fault.Message;return;}
            var phase=hostCommands.Match.Phase;int round=hostCommands.Match.RoundNumber;
            if(deadlinePhase!=phase||deadlineRound!=round){deadlinePhase=phase;deadlineRound=round;
                hostCommands.SetPhaseDeadline(phase==MatchPhase.Preparation||phase==MatchPhase.Result?manager.ServerTime.Time+combatRuntime.Clock.RemainingSeconds:(double?)null);
            }
            if(oldRound!=round||oldPhase!=phase)BroadcastOwnerStates();
            if(now<nextCombatSend)return;nextCombatSend=now+.1;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(QAHoldCombat){foreach(var frames in combatFrames.Values)if(frames.Count>120)frames.RemoveRange(0,frames.Count-120);return;}
#endif
            foreach(var pair in combatFrames){
                var recipients=boundPlayers.Where(p=>manager.ConnectedClientsIds.Contains(p.Key)&&WatchedPlayer(p.Key,p.Value)==pair.Key).Select(p=>p.Key).ToList();
                if(WatchedPlayer(NetworkManager.ServerClientId,session.CurrentPlayer.Id)==pair.Key)recipients.Add(NetworkManager.ServerClientId);
                while(pair.Value.Count>0){int count=Math.Min(3,pair.Value.Count);var frames=pair.Value.Take(count).ToArray();
                    var json=JsonUtility.ToJson(new CombatBatch{frames=frames});while(System.Text.Encoding.UTF8.GetByteCount(json)>65536&&count>1){count--;frames=pair.Value.Take(count).ToArray();json=JsonUtility.ToJson(new CombatBatch{frames=frames});}
                    foreach(var recipient in recipients)if(recipient==NetworkManager.ServerClientId){foreach(var frame in frames)ApplyCombat(frame);}else SendText(CombatMessage,recipient,json,65536);
                    pair.Value.RemoveRange(0,count);
                }
            }
        }
        private void SendCombatBaseline(ulong recipient,string player)
        {
            if(!latestCombat.TryGetValue(WatchedPlayer(recipient,player),out var frame))return;
            if(recipient==NetworkManager.ServerClientId)ApplyCombat(frame);else SendText(CombatMessage,recipient,JsonUtility.ToJson(new CombatBatch{frames=new[]{frame}}),65536);
        }
        private void OnCombat(ulong sender,FastBufferReader reader)
        {
            if(IsHost||sender!=NetworkManager.ServerClientId)return;
            try{var batch=JsonUtility.FromJson<CombatBatch>(ReadText(reader,65536));if(batch?.protocol!=1||batch.frames==null||batch.frames.Length>3)return;foreach(var frame in batch.frames)ApplyCombat(frame);}
            catch(Exception){CombatError="Invalid combat payload. Sync state to recover.";}
        }
        private void ApplyCombat(CombatFrame frame){if(StartInfo==null||session==null||PublicState==null)return;if(Combat.Accept(frame,StartInfo.matchId,WatchingPlayerId,PublicState.round))CombatError=null;}
    }
}

