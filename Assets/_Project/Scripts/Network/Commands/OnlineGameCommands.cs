using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Unity.Services.Multiplayer;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Network.Session
{
    public sealed partial class OnlineConnection
    {
        private const string ChallengeMessage="PokeChess.Bind.Challenge.v1",BoundMessage="PokeChess.Bind.Accept.v1",CommandMessage="PokeChess.Command.v1",AckMessage="PokeChess.Ack.v1",OwnerStateMessage="PokeChess.StateSnapshot.v2";
        private readonly Dictionary<ulong,string> challenges=new Dictionary<ulong,string>();
        private readonly Dictionary<ulong,string> boundPlayers=new Dictionary<ulong,string>();
        private readonly Dictionary<ulong,double> bindingDeadlines=new Dictionary<ulong,double>();
        private readonly Dictionary<ulong,Queue<double>> incomingTimes=new Dictionary<ulong,Queue<double>>();
        private bool identityBound,bindingRefreshing;
        private double nextBindingRefresh,nextMatchSync;
        private HostCommandProcessor hostCommands;
        private TaskCompletionSource<CommandAck> pendingAck;
        private long nextCommandSequence=1;
        private long requiredStateVersion,requiredStateSequence=1;
        public bool IdentityBound=>IsHost&&session!=null||identityBound;
        private ulong hostMatchSeed;
        private readonly MatchSnapshotStore snapshots=new MatchSnapshotStore();
        private readonly Dictionary<ulong,string> lastSnapshots=new Dictionary<ulong,string>();
        public OwnerMatchState LocalMatchState=>snapshots.Owner;
        public PublicMatchState PublicState=>snapshots.Public;
        private bool RawStateSynchronized=>LocalMatchState!=null&&PublicState!=null&&LocalMatchState.revision==PublicState.revision&&LocalMatchState.revision>=requiredStateVersion&&LocalMatchState.nextSequence>=requiredStateSequence;
        public bool StateSynchronized=>!recoveryActive&&RawStateSynchronized;
        public string SyncFeedback {get;private set;}
        public CommandAck LastCommandAck { get; private set; }
        public MatchCommand PendingCommand { get; private set; }
        public string CommandFeedback { get; private set; }
        public event Action CommandStateChanged;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public bool QADropNextAck {get;set;}
        public bool QAHoldStatePublication {get;set;}
        public int QAExtraPlayers {get;set;}
        private readonly Dictionary<ulong,string> qaRememberedSnapshots=new Dictionary<ulong,string>();
        public void QARememberState(){qaRememberedSnapshots.Clear();foreach(var pair in lastSnapshots)qaRememberedSnapshots[pair.Key]=pair.Value;}
        public void QAReplayState(){foreach(var pair in qaRememberedSnapshots)if(manager.ConnectedClientsIds.Contains(pair.Key))SendText(OwnerStateMessage,pair.Key,pair.Value,65536);}
        public HostCommandProcessor QAHostCommands=>hostCommands;
        public string QACommandTransportError {get;private set;}
        public string QADisconnectReason=>manager?.DisconnectReason;
#endif
        private void RegisterGameMessages()
        {
            var messages=manager.CustomMessagingManager;
            messages.RegisterNamedMessageHandler(ChallengeMessage,OnBindingChallenge);messages.RegisterNamedMessageHandler(BoundMessage,OnBound);
            messages.RegisterNamedMessageHandler(CommandMessage,OnGameCommand);messages.RegisterNamedMessageHandler(AckMessage,OnCommandAck);messages.RegisterNamedMessageHandler(OwnerStateMessage,OnOwnerState);
            RegisterCombatMessages();
        }
        private void UnregisterGameMessages()
        {
            if(manager.CustomMessagingManager==null)return;
            foreach(var name in new[]{ChallengeMessage,BoundMessage,CommandMessage,AckMessage,OwnerStateMessage})manager.CustomMessagingManager.UnregisterNamedMessageHandler(name);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(CombatMessage);
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(WatchMessage);
        }
        private void ResetGameCommands()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            QAHoldStatePublication=false;QADropNextAck=false;QAExtraPlayers=0;QACommandTransportError=null;qaRememberedSnapshots.Clear();
#endif
            challenges.Clear();boundPlayers.Clear();bindingDeadlines.Clear();incomingTimes.Clear();lastSnapshots.Clear();snapshots.Clear();SyncFeedback=null;hostMatchSeed=0;identityBound=false;nextBindingRefresh=nextMatchSync=0;hostCommands=null;LastCommandAck=null;nextCommandSequence=1;
            requiredStateVersion=0;requiredStateSequence=1;pendingAck?.TrySetResult(new CommandAck{accepted=false,code="ConnectionLost",message="Connection ended."});pendingAck=null;PendingCommand=null;CommandFeedback=null;
            ResetCombat();
        }
        private static string BindingProof(string nonce,string roomId,string playerId)
        {using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes("PokeChess-bind-v1|"+nonce+"|"+roomId+"|"+playerId)));}
        private void IssueBindingChallenge(ulong sender)
        {
            if(boundPlayers.ContainsKey(sender)||challenges.ContainsKey(sender))return;
            var bytes=new byte[32];using(var random=RandomNumberGenerator.Create())random.GetBytes(bytes);
            var nonce=Convert.ToBase64String(bytes);challenges[sender]=nonce;bindingDeadlines[sender]=Time.realtimeSinceStartupAsDouble+30;
            SendText(ChallengeMessage,sender,nonce,512);
        }
        private async void OnBindingChallenge(ulong sender,FastBufferReader reader)
        {
            if(IsHost||sender!=NetworkManager.ServerClientId||session==null)return;
            string nonce;try{nonce=ReadText(reader,512);if(Convert.FromBase64String(nonce).Length!=32)return;}catch(Exception){return;}
            var current=session;lobbyPublishing=true;
            try {current.CurrentPlayer.SetProperty("bindingProof",new PlayerProperty(BindingProof(nonce,current.Id,current.CurrentPlayer.Id),VisibilityPropertyOptions.Member));await current.SaveCurrentPlayerDataAsync();}
            catch(Exception){if(session==current){Error="IdentityBindingFailed";SessionEnded();}}
            finally{lobbyPublishing=false;}
        }
        private void OnBound(ulong sender,FastBufferReader reader)
        {
            if(IsHost||sender!=NetworkManager.ServerClientId||session==null)return;
            try{if(ReadText(reader,512)==session.CurrentPlayer.Id){
                identityBound=true;
                if(recoveryActive){
                    if(PendingCommand!=null)DispatchPending();
                }
            }}catch(Exception){}
        }
        private async void RefreshBindings()
        {
            bindingRefreshing=true;var current=session;
            try {
                await current.RefreshAsync();if(session!=current)return;
                foreach(var pair in challenges.ToArray()){
                    if(!manager.ConnectedClientsIds.Contains(pair.Key)){challenges.Remove(pair.Key);bindingDeadlines.Remove(pair.Key);continue;}
                    var matches=current.Players.Where(p=>p.Id!=current.Host&&p.Properties.TryGetValue("bindingProof",out var proof)&&proof.Value==BindingProof(pair.Value,current.Id,p.Id)).ToArray();
                    if(matches.Length==1&&!boundPlayers.Values.Contains(matches[0].Id)){
                        var reason=ValidateRecoveredBinding(matches[0].Id);
                        if(reason!=null){manager.DisconnectClient(pair.Key,reason);challenges.Remove(pair.Key);bindingDeadlines.Remove(pair.Key);continue;}
                        boundPlayers[pair.Key]=matches[0].Id;challenges.Remove(pair.Key);bindingDeadlines.Remove(pair.Key);SendText(BoundMessage,pair.Key,matches[0].Id,512);
                        if(hostCommands!=null){SendText(OwnerStateMessage,pair.Key,JsonUtility.ToJson(hostCommands.FullSnapshot(matches[0].Id)),65536);SendCombatBaseline(pair.Key,matches[0].Id);}
                    }else if(Time.realtimeSinceStartupAsDouble>bindingDeadlines[pair.Key]){manager.DisconnectClient(pair.Key,"IdentityBindingTimeout");challenges.Remove(pair.Key);bindingDeadlines.Remove(pair.Key);}
                }
            }catch(Exception){if(session==current)Error="IdentityBindingRetry";}
            finally{bindingRefreshing=false;nextBindingRefresh=Time.realtimeSinceStartupAsDouble+2;}
        }
        private void UpdateGameCommands()
        {
            if(session==null||State!=ConnectionState.Connected)return;
            if(IsHost){
                foreach(var id in boundPlayers.Keys.Where(id=>!manager.ConnectedClientsIds.Contains(id)).ToArray())HostPlayerDisconnected(id);
                if(challenges.Count>0&&!bindingRefreshing&&!IsBusy&&!lobbyPublishing&&Time.realtimeSinceStartupAsDouble>=nextBindingRefresh)RefreshBindings();
                if(StartInfo!=null&&LobbyState==LobbyPhase.Started&&hostCommands==null){
                    var catalog=PrototypeRoster.CreateCatalog();var roster=StartInfo.players;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    if(QAExtraPlayers>0)roster=roster.Concat(Enumerable.Range(1,Math.Min(6,QAExtraPlayers)).Select(i=>"qa-bot-"+i)).ToArray();
#endif
                    var match=MatchStateFactory.CreateWithPool(StartInfo.matchId,roster,catalog,PrototypeRoster.CreateDefinitions().Select(d=>d.Id),matchSeed:hostMatchSeed);
                    match.TransitionTo(MatchPhase.Starting);match.TransitionTo(MatchPhase.Preparation);foreach(var id in roster)new ShopSystem().RefreshForRound(match,id);
                    var rounds=new LocalRoundCoordinator(match,catalog,skillCatalog:PrototypeRoster.CreateSkills(),statusCatalog:PrototypeRoster.CreateStatuses());
                    hostCommands=new HostCommandProcessor(match,catalog,rounds);CreateCombatRuntime(rounds);BroadcastOwnerStates(true);
                }
                // Deduplicates unchanged projections; also publishes non-command Host writes.
                AdvanceCombat();
                if(hostCommands!=null&&Time.realtimeSinceStartupAsDouble>=nextMatchSync){nextMatchSync=Time.realtimeSinceStartupAsDouble+.5;BroadcastOwnerStates();}
            }else if(IdentityBound&&StartInfo!=null&&!StateSynchronized&&PendingCommand==null&&Time.realtimeSinceStartupAsDouble>=nextMatchSync){nextMatchSync=Time.realtimeSinceStartupAsDouble+2;_ = RequestStateSyncAsync();}
        }
        public Task<CommandAck> RequestStateSyncAsync()
        {if(PendingCommand!=null)return RetryPendingCommandAsync();SyncFeedback="Synchronizing state…";return SubmitCommandAsync(CreateCommand(MatchCommandKind.Sync));}
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        public void QAClearSnapshots(){snapshots.Clear();SyncFeedback="State baseline cleared.";}
        public bool QAApplySnapshot(MatchStateSnapshot value)=>ApplySnapshot(value);
#endif
        public MatchCommand CreateCommand(MatchCommandKind kind)
        {
            var state=LocalMatchState;return new MatchCommand{protocol=1,matchId=StartInfo?.matchId,commandId=Guid.NewGuid().ToString("N"),playerId=session?.CurrentPlayer.Id,sequence=nextCommandSequence,kind=kind,
                round=state?.round??1,phase=state?.phase??MatchPhase.Preparation,playerRevision=state?.playerRevision??0,shopRevision=state?.shopRevision??0,placementRevision=state?.placementRevision??0};
        }
        public async Task<CommandAck> SubmitCommandAsync(MatchCommand command)
        {
            if(PendingCommand!=null){CommandFeedback="Waiting for the previous command. Retry to confirm it.";return null;}
            if(command==null||!IdentityBound||StartInfo==null||State!=ConnectionState.Connected||(command.kind!=MatchCommandKind.Sync&&!StateSynchronized)){CommandFeedback="Match connection is not ready.";return null;}
            PendingCommand=command.Copy();pendingAck=new TaskCompletionSource<CommandAck>();CommandFeedback="Confirming action…";SaveRecoveryTicket();var completion=pendingAck.Task;DispatchPending();
            if(await Task.WhenAny(completion,Task.Delay(5000))==completion)return await completion;
            if(PendingCommand!=null)CommandFeedback="Confirmation delayed. Retry the same action.";return null;
        }
        public async Task<CommandAck> RetryPendingCommandAsync()
        {
            if(PendingCommand==null)return LastCommandAck;if(!IdentityBound||(State!=ConnectionState.Connected&&State!=ConnectionState.Recovering))return null;var completion=pendingAck.Task;DispatchPending();
            return await Task.WhenAny(completion,Task.Delay(5000))==completion?await completion:null;
        }
        private void DispatchPending()
        {
            if(IsHost){ReceiveCommand(NetworkManager.ServerClientId,PendingCommand);return;}
            try{SendText(CommandMessage,NetworkManager.ServerClientId,JsonUtility.ToJson(PendingCommand),4096);}catch(Exception){CommandFeedback="Could not send. Retry to confirm the action.";}
        }
        private void OnGameCommand(ulong sender,FastBufferReader reader)
        {
            if(!IsHost||session==null)return;
            if(!incomingTimes.TryGetValue(sender,out var times))incomingTimes[sender]=times=new Queue<double>();
            double now=Time.realtimeSinceStartupAsDouble;while(times.Count>0&&now-times.Peek()>1)times.Dequeue();if(times.Count>=20){manager.DisconnectClient(sender,"CommandRateLimit");return;}times.Enqueue(now);
            try{ReceiveCommand(sender,JsonUtility.FromJson<MatchCommand>(ReadText(reader,4096)));}catch(Exception error){
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                QACommandTransportError=error.GetType().Name;
#endif
                manager.DisconnectClient(sender,"MalformedCommand");
            }
        }
        private void ReceiveCommand(ulong sender,MatchCommand command)
        {
            string player=sender==NetworkManager.ServerClientId?session?.CurrentPlayer.Id:boundPlayers.TryGetValue(sender,out var bound)?bound:null;
            if(player==null||hostCommands==null){var rejected=new CommandAck{matchId=command?.matchId,commandId=command?.commandId,sequence=command?.sequence??0,nextSequence=command?.sequence??1,accepted=false,code=player==null?"Unauthenticated":"MatchNotReady",message="Match connection is not ready."};DeliverAck(sender,rejected);return;}
            var ack=hostCommands.Process(player,command);DeliverAck(sender,ack);combatRuntime?.CaptureCommandEffects();BroadcastOwnerStates(command?.kind==MatchCommandKind.Sync);
            if(command?.kind==MatchCommandKind.Sync)SendCombatBaseline(sender,player);
        }
        private void DeliverAck(ulong sender,CommandAck ack)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(QADropNextAck){QADropNextAck=false;return;}
#endif
            if(sender==NetworkManager.ServerClientId)ReceiveAck(ack);else SendText(AckMessage,sender,JsonUtility.ToJson(ack),32768);
        }
        private void BroadcastOwnerStates(bool force=false)
        {
            if(hostCommands==null||session==null)return;ApplySnapshot(hostCommands.FullSnapshot(session.CurrentPlayer.Id));
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            if(QAHoldStatePublication)return;
#endif
            foreach(var pair in boundPlayers)if(manager.ConnectedClientsIds.Contains(pair.Key)&&hostCommands.Match.Players.Any(p=>p.PlayerId==pair.Value)){
                var json=JsonUtility.ToJson(hostCommands.FullSnapshot(pair.Value));if(!force&&lastSnapshots.TryGetValue(pair.Key,out var previous)&&previous==json)continue;
                SendText(OwnerStateMessage,pair.Key,json,65536);lastSnapshots[pair.Key]=json;
            }
        }
        private void OnCommandAck(ulong sender,FastBufferReader reader){if(IsHost||sender!=NetworkManager.ServerClientId)return;try{ReceiveAck(JsonUtility.FromJson<CommandAck>(ReadText(reader,32768)));}catch(Exception){CommandFeedback="Invalid command response.";}}
        private void ReceiveAck(CommandAck ack)
        {
            if(ack==null||StartInfo==null||ack.matchId!=StartInfo.matchId)return;
            // Ack settles the command. Only an atomic snapshot changes the displayed state.
            if(PendingCommand==null||ack.commandId!=PendingCommand.commandId||ack.sequence!=PendingCommand.sequence)return;
            if(ack.state!=null){requiredStateVersion=Math.Max(requiredStateVersion,ack.state.revision);requiredStateSequence=Math.Max(requiredStateSequence,ack.state.nextSequence);}
            nextCommandSequence=Math.Max(nextCommandSequence,ack.nextSequence);LastCommandAck=ack;CommandFeedback=ack.accepted?ack.message:"Rejected: "+ack.code;PendingCommand=null;var completion=pendingAck;pendingAck=null;completion?.TrySetResult(ack);SaveRecoveryTicket();CommandStateChanged?.Invoke();
        }
        private void OnOwnerState(ulong sender,FastBufferReader reader){if(IsHost||sender!=NetworkManager.ServerClientId)return;try{if(!ApplySnapshot(JsonUtility.FromJson<MatchStateSnapshot>(ReadText(reader,65536))))SyncFeedback="Outdated or invalid snapshot ignored.";}catch(Exception){SyncFeedback="Invalid snapshot. Request state sync.";}}
        private bool ApplySnapshot(MatchStateSnapshot state)
        {
            if(StartInfo==null||session==null||!snapshots.Apply(state,StartInfo.matchId,session.CurrentPlayer.Id))return false;
            nextCommandSequence=Math.Max(nextCommandSequence,state.ownerState.nextSequence);
            if(recoveryActive&&!recoverySnapshotReceived){
                recoverySnapshotReceived=true;
                // Validate the saved spectator target against the fresh public state,
                // then restore the subscription on this new NGO connection.
                watchingPlayer=PublicState.players.Any(p=>p.id==WatchingPlayerId)?WatchingPlayerId:session.CurrentPlayer.Id;
                SendText(WatchMessage,NetworkManager.ServerClientId,JsonUtility.ToJson(new WatchRequest{matchId=StartInfo.matchId,playerId=watchingPlayer}),512);
            }
            SyncFeedback="State synchronized.";CommandStateChanged?.Invoke();return true;
        }
        private void SendText(string name,ulong recipient,string text,int limit)
        {
            var bytes=Encoding.UTF8.GetBytes(text??"");if(bytes.Length>limit)throw new ArgumentException("MessageTooLarge");
            // Full snapshots exceed a single packet as boards grow; NGO must fragment them.
            using(var writer=new FastBufferWriter(bytes.Length+4,Allocator.Temp)){writer.WriteValueSafe(bytes.Length);writer.WriteBytesSafe(bytes);manager.CustomMessagingManager.SendNamedMessage(name,recipient,writer,NetworkDelivery.ReliableFragmentedSequenced);}
        }
        private static string ReadText(FastBufferReader reader,int limit)
        {
            reader.ReadValueSafe(out int size);if(size<0||size>limit||size>reader.Length-reader.Position)throw new ArgumentException("InvalidMessageSize");
            var bytes=new byte[size];reader.ReadBytesSafe(ref bytes,size);return new UTF8Encoding(false,true).GetString(bytes);
        }
    }
}
