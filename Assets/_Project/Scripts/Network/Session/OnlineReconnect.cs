using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using PokeChess.Core.Match;

namespace PokeChess.Network.Session
{
    public sealed partial class OnlineConnection
    {
        [Serializable] private sealed class RecoveryTicket
        {public string sessionId,playerId,sessionType,watch;public long lastSeenUtc;public MatchStartInfo start;public MatchCommand pending;}
        // Pinned SDK 2.3.3 metadata adapter, read once by the Host. Clients use
        // our member-only reconnectRelay property, never the SDK's internal JSON.
        [Serializable] private sealed class RecoveryNetworkMetadata {public string RelayJoinCode;}
        private RecoveryTicket recoveryTicket;
        private ResumableNetworkHandler networkHandler;
        private bool wasHosting,recoveryActive,recoverySnapshotReceived,ticketChecked;
        private double recoveryDeadline,nextRecoveryAttempt,nextTicketSave,recoveryAttemptStarted;
        private int recoveryAttempts,recoveryGeneration;
        private double lastKnownServerTime;
        public double NetworkServerTime=>manager!=null&&manager.IsListening?manager.ServerTime.Time:lastKnownServerTime;
        public bool IsRecovering=>recoveryActive;
        public double ReconnectRemainingSeconds=>recoveryActive?Math.Max(0,recoveryDeadline-Time.realtimeSinceStartupAsDouble):0;
        public string RecoveryFeedback {get;private set;}
        private string TicketKey=>"PokeChess.Recovery.v1."+AuthenticationService.Instance.PlayerId;
        private void SaveRecoveryTicket()
        {
            if(wasHosting||session==null||StartInfo==null||!AuthenticationService.Instance.IsAuthorized)return;
            if(LocalMatchState?.eliminated==true||PublicState?.phase==MatchPhase.Finished){ClearRecoveryTicket();return;}
            recoveryTicket=new RecoveryTicket{sessionId=session.Id,sessionType=session.Type,playerId=session.CurrentPlayer.Id,start=StartInfo,watch=WatchingPlayerId,
                lastSeenUtc=recoveryActive?(recoveryTicket?.lastSeenUtc??DateTime.UtcNow.Ticks):DateTime.UtcNow.Ticks,pending=PendingCommand?.Copy()};
            PlayerPrefs.SetString(TicketKey,JsonUtility.ToJson(recoveryTicket));PlayerPrefs.Save();
        }
        private void ClearRecoveryTicket()
        {if(AuthenticationService.Instance.IsAuthorized){PlayerPrefs.DeleteKey(TicketKey);PlayerPrefs.Save();}recoveryTicket=null;}
        private void ClientDisconnected(ulong clientId)
        {
            if(State==ConnectionState.Leaving)return;
            if(wasHosting){if(clientId!=Unity.Netcode.NetworkManager.ServerClientId)HostPlayerDisconnected(clientId);return;}
            if(clientId==manager.LocalClientId||!manager.IsConnectedClient)HandleConnectionLoss(manager.DisconnectReason??"ConnectionLost");
        }
        private void HostPlayerDisconnected(ulong clientId)
        {
            if(boundPlayers.TryGetValue(clientId,out var player)&&hostCommands?.Reconnect.Disconnect(player,manager.ServerTime.Time)==true){
                hostCommands.NotifyHostStateChanged();Debug.Log("Reconnect grace started / "+player+" / 120s");
            }
            boundPlayers.Remove(clientId);challenges.Remove(clientId);bindingDeadlines.Remove(clientId);verifiedPeers.Remove(clientId);lastSnapshots.Remove(clientId);incomingTimes.Remove(clientId);combatWatchers.Remove(clientId);
        }
        private string ValidateRecoveredBinding(string player)
        {
            if(StartInfo==null)return null;
            if(hostCommands==null)return "MatchNotReady";
            var reason=hostCommands.Reconnect.Reconnect(player,manager.ServerTime.Time);
            if(reason==null)hostCommands.NotifyHostStateChanged();return reason;
        }
        private void HandleConnectionLoss(string reason)
        {
            if(State==ConnectionState.Leaving||State==ConnectionState.Idle||State==ConnectionState.Failed)return;
            foreach(var terminal in new[]{"HostLost","ReconnectExpired","PlayerAlreadyEliminated","MatchFinished","VersionMismatch","NotMatchParticipant"})if(reason?.Contains(terminal)==true){reason=terminal;break;}
            if(StartInfo==null){Error=reason;SessionEnded();return;}
            if(LocalMatchState?.eliminated==true||PublicState?.phase==MatchPhase.Finished){_=EndRecoveryAsync(LocalMatchState?.eliminated==true?"PlayerAlreadyEliminated":"MatchFinished");return;}
            if(wasHosting||reason=="HostLost"||reason=="ReconnectExpired"||reason=="PlayerAlreadyEliminated"||reason=="MatchFinished"||reason=="VersionMismatch"||reason=="NotMatchParticipant"){
                _=EndRecoveryAsync(wasHosting?"HostLost":reason);return;
            }
            if(recoveryActive)return;
            SaveRecoveryTicket();recoveryGeneration++;recoveryActive=true;recoveryDeadline=Time.realtimeSinceStartupAsDouble+MatchReconnectSystem.GraceSeconds;
            nextRecoveryAttempt=Time.realtimeSinceStartupAsDouble+1;recoveryAttempts=0;recoverySnapshotReceived=false;identityBound=false;
            State=ConnectionState.DisconnectedGrace;Error=null;RecoveryFeedback="Connection lost. Reconnecting…";Combat.Clear();
        }
        private void UpdateReconnect()
        {
            if(manager.IsListening)lastKnownServerTime=manager.ServerTime.Time;
            if(!AuthenticationService.Instance.IsAuthorized)return;
            if(!ticketChecked){ticketChecked=true;
                try{var saved=JsonUtility.FromJson<RecoveryTicket>(PlayerPrefs.GetString(TicketKey,""));
                    if(State==ConnectionState.Idle&&saved?.playerId==AuthenticationService.Instance.PlayerId&&saved.start?.players?.Contains(saved.playerId)==true){
                        recoveryTicket=saved;StartInfo=saved.start;PendingCommand=saved.pending?.Copy();if(PendingCommand!=null)pendingAck=new TaskCompletionSource<CommandAck>();watchingPlayer=saved.watch;
                        recoveryGeneration++;recoveryActive=true;recoveryDeadline=Time.realtimeSinceStartupAsDouble+Math.Max(15,120-(DateTime.UtcNow.Ticks-saved.lastSeenUtc)/(double)TimeSpan.TicksPerSecond);
                        nextRecoveryAttempt=0;State=ConnectionState.DisconnectedGrace;RecoveryFeedback="Restoring previous match…";
                    }
                }catch(Exception){ClearRecoveryTicket();}
            }
            if(wasHosting&&hostCommands!=null&&manager.IsListening){var expired=hostCommands.Reconnect.Expire(manager.ServerTime.Time);if(expired.Length>0){hostCommands.NotifyHostStateChanged();combatRuntime?.CaptureCommandEffects();BroadcastOwnerStates(true);foreach(var id in expired)Debug.Log("Reconnect expired / "+id);}}
            if(!recoveryActive){if(State==ConnectionState.Connected&&Time.realtimeSinceStartupAsDouble>=nextTicketSave){nextTicketSave=Time.realtimeSinceStartupAsDouble+2;SaveRecoveryTicket();}return;}
            if(Time.realtimeSinceStartupAsDouble>=recoveryDeadline){_=EndRecoveryAsync("ReconnectExpired");return;}
            if(State==ConnectionState.Recovering){
                if(identityBound&&recoverySnapshotReceived&&PendingCommand==null&&RawStateSynchronized&&(PublicState.phase!=MatchPhase.Combat||Combat.Latest?.round==PublicState.round)){
                    recoveryActive=false;State=ConnectionState.Connected;Error=null;RecoveryFeedback="Match restored.";SaveRecoveryTicket();Debug.Log("Reconnect snapshot restored / "+StartInfo.matchId);return;
                }
                if(!IsBusy&&Time.realtimeSinceStartupAsDouble-recoveryAttemptStarted>15){State=ConnectionState.DisconnectedGrace;nextRecoveryAttempt=Time.realtimeSinceStartupAsDouble+2;}
            }
            if(State==ConnectionState.DisconnectedGrace&&!IsBusy&&Time.realtimeSinceStartupAsDouble>=nextRecoveryAttempt){
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if(QAHoldReconnect)return;
#endif
                _=ReconnectAsync();
            }
        }
        public Task ReconnectAsync()=>Run(ReconnectCoreAsync);
        private async Task ReconnectCoreAsync()
        {
            if(!recoveryActive||recoveryTicket==null)return;
            int generation=recoveryGeneration;State=ConnectionState.Reconnecting;recoveryAttempts++;recoveryAttemptStarted=Time.realtimeSinceStartupAsDouble;
            RecoveryFeedback="Reconnecting… attempt "+recoveryAttempts;identityBound=false;recoverySnapshotReceived=false;Combat.Clear();
            try{
                UnregisterGameMessages();messagesRegistered=false;
                if(session==null){networkHandler=new ResumableNetworkHandler(manager,false);
                    session=await MultiplayerService.Instance.ReconnectToSessionAsync(recoveryTicket.sessionId,new ReconnectSessionOptions{Type=recoveryTicket.sessionType}.WithNetworkHandler(networkHandler));
                    session.Deleted+=SessionEnded;session.RemovedFromSession+=SessionEnded;
                }else{
                    await session.ReconnectAsync();await session.RefreshAsync();
                    if(generation!=recoveryGeneration)return;
                    if(session.Host!=StartInfo.players[0])throw new InvalidOperationException("HostLost");
                    var allocationId=await networkHandler.RestartAsync(SessionValue("reconnectRelay"));
                    if(generation!=recoveryGeneration)return;
                    session.CurrentPlayer.SetAllocationId(allocationId);await session.SaveCurrentPlayerDataAsync();
                }
                if(generation!=recoveryGeneration){await CleanupAsync();return;}
                var info=JsonUtility.FromJson<MatchStartInfo>(SessionValue("matchStart")??"");
                if(info?.matchId!=StartInfo.matchId||info.ruleVersion!="pokechess-alpha-v5"||info.gameVersion!=Application.version||session.Host!=StartInfo.players[0])throw new InvalidOperationException("HostLost");
                StartInfo=info;acknowledgedMatch=info.matchId;connectedPublished=true;
                manager.CustomMessagingManager.RegisterNamedMessageHandler(RequestMessage,OnRequest);manager.CustomMessagingManager.RegisterNamedMessageHandler(ReplyMessage,OnReply);RegisterGameMessages();messagesRegistered=true;
                State=ConnectionState.Recovering;SendProbe();
            }catch(Exception e){
                if(generation!=recoveryGeneration)return;
                string reason=e is SessionException se?se.Error.ToString():e.Message;
                if(reason=="HostLost"||reason=="SessionDeleted"||reason=="PlayerNotFound"||reason=="SessionNotFound"||reason=="NotFound"){await EndRecoveryAsync("HostLost");return;}
                State=ConnectionState.DisconnectedGrace;RecoveryFeedback="Reconnect delayed. Retrying…";nextRecoveryAttempt=Time.realtimeSinceStartupAsDouble+Math.Min(10,2*recoveryAttempts);
            }
        }
        private async Task EndRecoveryAsync(string reason)
        {
            if(State==ConnectionState.Leaving||State==ConnectionState.Failed)return;
            recoveryGeneration++;recoveryActive=false;ClearRecoveryTicket();State=ConnectionState.Leaving;await CleanupAsync();
            Error=reason;RecoveryFeedback=reason=="HostLost"?"Host connection ended. Match closed.":reason=="ReconnectExpired"?"Reconnect time expired.":reason;
            State=ConnectionState.Failed;
        }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public bool QAHoldReconnect {get;set;}
        public ulong QALocalClientId=>manager.LocalClientId;
        public void QADisconnectTransport(){manager.Shutdown();}
        public void QAExpireDisconnected(){if(hostCommands!=null){var expired=hostCommands.Reconnect.Expire(manager.ServerTime.Time+121);if(expired.Length>0){hostCommands.NotifyHostStateChanged();combatRuntime?.CaptureCommandEffects();BroadcastOwnerStates(true);}}}
#endif
    }
}
