using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Multiplayer;

namespace PokeChess.Network.Session
{
    public enum LobbyPhase { Waiting, Starting, Started }

    [Serializable]
    public sealed class MatchStartInfo
    {
        public string matchId,gameVersion,ruleVersion;
        // Array order is the fixed seat order; Host occupies seat zero.
        public string[] players;
    }

    public sealed partial class OnlineConnection
    {
        private readonly HashSet<ulong> verifiedPeers=new HashSet<ulong>();
        private bool lobbyPublishing,connectedPublished;
        private double nextLobbyAttempt;
        private string acknowledgedMatch;
        public LobbyPhase LobbyState => Enum.TryParse(SessionValue("lobbyPhase"),out LobbyPhase phase)?phase:LobbyPhase.Waiting;
        public MatchStartInfo StartInfo { get; private set; }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public bool QAHoldStartAcknowledgement { get; set; }
#endif
        public bool LocalReady => session!=null&&PlayerFlag(session.CurrentPlayer,"ready");
        public bool LobbyConnectionConfirmed => connectedPublished;
        public bool LobbyBusy => lobbyPublishing||IsBusy;
        public string StartBlockedReason
        {
            get {
                if(!IsHost)return "Only the Host can start.";
                if(LobbyState!=LobbyPhase.Waiting)return "Match start is already in progress.";
                if(State!=ConnectionState.Connected||session==null)return "Waiting for connection.";
                if(session.Players.Count<2)return "At least 2 players are required.";
                if(session.Players.Any(p=>!PlayerFlag(p,"connected"))||!TransportMembersConfirmed())return "Waiting for player connections.";
                if(session.Players.Any(p=>p.Id!=session.Host&&!PlayerFlag(p,"ready")))return "Waiting for players to ready.";
                if(LobbyBusy)return "Saving lobby state…";
                return null;
            }
        }
        private static bool PlayerFlag(IReadOnlyPlayer p,string key) => p!=null&&p.Properties.TryGetValue(key,out var value)&&value.Value=="1";
        private string SessionValue(string key)=>session!=null&&session.Properties.TryGetValue(key,out var value)?value.Value:null;
        private bool TransportMembersConfirmed()=>manager!=null&&manager.IsHost&&manager.ConnectedClientsIds.Count==session.Players.Count&&
            manager.ConnectedClientsIds.Where(id=>id!=NetworkManagerServerId).All(id=>verifiedPeers.Contains(id)&&boundPlayers.ContainsKey(id))&&
            boundPlayers.Where(p=>manager.ConnectedClientsIds.Contains(p.Key)).Select(p=>p.Value).OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(session.Players.Where(p=>p.Id!=session.Host).Select(p=>p.Id).OrderBy(id=>id,StringComparer.Ordinal));
        private const ulong NetworkManagerServerId=0;
        private void ResetLobby(){verifiedPeers.Clear();acknowledgedMatch=null;StartInfo=null;nextLobbyAttempt=0;connectedPublished=false;ResetGameCommands();}

        public Task SetReadyAsync(bool ready)=>Run(()=>SaveReadyAsync(ready));
        private async Task SaveReadyAsync(bool ready)
        {
            Error=null;
            if(session==null||State!=ConnectionState.Connected||!connectedPublished||IsHost||LobbyState!=LobbyPhase.Waiting||lobbyPublishing){Error="ReadyUnavailable";return;}
            var current=session;var old=LocalReady;
            try {current.CurrentPlayer.SetProperty("ready",new PlayerProperty(ready?"1":"0",VisibilityPropertyOptions.Member));await current.SaveCurrentPlayerDataAsync();}
            catch(Exception e){current.CurrentPlayer.SetProperty("ready",new PlayerProperty(old?"1":"0",VisibilityPropertyOptions.Member));Error="ReadySaveFailed: "+e.GetType().Name;try{await current.RefreshAsync();}catch(Exception){}}
        }
        public Task StartMatchAsync()
        {
            if(IsHost&&LobbyState==LobbyPhase.Started)return Task.CompletedTask;
            if(IsHost&&LobbyState==LobbyPhase.Starting&&IsBusy)return operation;
            var reason=StartBlockedReason;
            if(reason!=null){Error=reason;return Task.CompletedTask;}
            return Run(StartMatchCoreAsync);
        }
        private async Task StartMatchCoreAsync()
        {
            Error=null;var current=session;var host=current.AsHost();
            try {
                host.IsLocked=true;host.SetProperty("lobbyPhase",new SessionProperty("Starting",VisibilityPropertyOptions.Member));
                await host.SavePropertiesAsync();await current.RefreshAsync();
                var roster=current.Players.OrderBy(p=>p.Id==current.Host?0:1).ThenBy(p=>p.Id,StringComparer.Ordinal).Select(p=>p.Id).ToArray();
                ValidateStartRoster(current,roster);
                hostMatchSeed=(ulong)(Guid.NewGuid().GetHashCode()&int.MaxValue);
                var networkInfo=JsonUtility.FromJson<RecoveryNetworkMetadata>(SessionValue("_session_network")??"");
                if(string.IsNullOrEmpty(networkInfo?.RelayJoinCode))throw new InvalidOperationException("ReconnectNetworkMetadataMissing");
                host.SetProperty("reconnectRelay",new SessionProperty(networkInfo.RelayJoinCode,VisibilityPropertyOptions.Member));
                var info=new MatchStartInfo{matchId=Guid.NewGuid().ToString("N"),gameVersion=Application.version,ruleVersion="pokechess-alpha-v5",players=roster};
                host.SetProperty("matchStart",new SessionProperty(JsonUtility.ToJson(info),VisibilityPropertyOptions.Member));await host.SavePropertiesAsync();
                var deadline=Time.realtimeSinceStartupAsDouble+20;
                while(true){
                    ValidateStartRoster(current,roster);
                    if(current.Players.Where(p=>p.Id!=current.Host).All(p=>p.Properties.TryGetValue("startAck",out var ack)&&ack.Value==info.matchId))break;
                    if(Time.realtimeSinceStartupAsDouble>deadline)throw new TimeoutException("StartAcknowledgementTimeout");
                    await Task.Delay(1000);await current.RefreshAsync();
                }
                host.SetProperty("lobbyPhase",new SessionProperty("Started",VisibilityPropertyOptions.Member));await host.SavePropertiesAsync();StartInfo=info;Error=null;
            }
            catch(Exception e){
                Error=e is SessionException serviceError?serviceError.Error.ToString():e is InvalidOperationException||e is TimeoutException?e.Message:e.GetType().Name;
                if(session==current&&manager!=null&&manager.IsListening){
                    try{host.IsLocked=false;host.SetProperty("lobbyPhase",new SessionProperty("Waiting",VisibilityPropertyOptions.Member));host.SetProperty("matchStart",new SessionProperty("",VisibilityPropertyOptions.Member));await host.SavePropertiesAsync();StartInfo=null;}
                    catch(Exception){Error="StartRecoveryFailed";await CleanupAsync();State=ConnectionState.Failed;}
                }
            }
        }
        private void ValidateStartRoster(ISession current,string[] roster)
        {
            if(session!=current||!manager.IsListening||roster.Length<2||!TransportMembersConfirmed()||
                !current.Players.Select(p=>p.Id).OrderBy(id=>id,StringComparer.Ordinal).SequenceEqual(roster.OrderBy(id=>id,StringComparer.Ordinal))||
                current.Players.Any(p=>!PlayerFlag(p,"connected")||(p.Id!=current.Host&&!PlayerFlag(p,"ready"))))throw new InvalidOperationException("PlayersChangedDuringStart");
        }
        private void UpdateLobby()
        {
            if(session==null||State!=ConnectionState.Connected||lobbyPublishing||Time.realtimeSinceStartupAsDouble<nextLobbyAttempt)return;
            if(!IdentityBound)return;
            if(!connectedPublished&&!IsBusy){PublishLobbyState(false);return;}
            if(!IsHost&&LobbyState!=LobbyPhase.Waiting){
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                if(QAHoldStartAcknowledgement&&LobbyState==LobbyPhase.Starting)return;
#endif
                try {
                    var info=JsonUtility.FromJson<MatchStartInfo>(SessionValue("matchStart")??"");
                    if(info==null)return;
                    if(string.IsNullOrEmpty(info.matchId)||info.gameVersion!=Application.version||info.ruleVersion!="pokechess-alpha-v5"||info.players==null||info.players.Length<2||info.players.Length>8||info.players.Distinct().Count()!=info.players.Length||!info.players.Contains(session.CurrentPlayer.Id))throw new InvalidOperationException("InvalidMatchStart");
                    if(LobbyState==LobbyPhase.Starting&&acknowledgedMatch!=info.matchId&&!IsBusy){PublishLobbyState(true,info.matchId);return;}
                    if(LobbyState==LobbyPhase.Started&&acknowledgedMatch==info.matchId)StartInfo=info;
                }catch(Exception e){Error=e.Message;nextLobbyAttempt=Time.realtimeSinceStartupAsDouble+2;}
            }
        }
        private async void PublishLobbyState(bool acknowledge,string matchId=null)
        {
            lobbyPublishing=true;var current=session;
            try {
                if(acknowledge)current.CurrentPlayer.SetProperty("startAck",new PlayerProperty(matchId,VisibilityPropertyOptions.Member));
                else {current.CurrentPlayer.SetProperty("connected",new PlayerProperty("1",VisibilityPropertyOptions.Member));current.CurrentPlayer.SetProperty("ready",new PlayerProperty("0",VisibilityPropertyOptions.Member));current.CurrentPlayer.SetProperty("startAck",new PlayerProperty("",VisibilityPropertyOptions.Member));}
                await current.SaveCurrentPlayerDataAsync();if(session==current){if(acknowledge)acknowledgedMatch=matchId;else connectedPublished=true;}
            }catch(Exception e){if(session==current){Error="LobbySaveFailed: "+e.GetType().Name;nextLobbyAttempt=Time.realtimeSinceStartupAsDouble+2;}}
            finally {lobbyPublishing=false;}
        }
    }
}
