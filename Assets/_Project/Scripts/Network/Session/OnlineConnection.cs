using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;

namespace PokeChess.Network.Session
{
    public enum ConnectionState { Idle, Creating, Joining, Connecting, Connected, Leaving, Failed }

    public sealed partial class OnlineConnection : MonoBehaviour
    {
        private const ushort ProtocolVersion = 4;
        private const string RequestMessage = "PokeChess.Connection.Request.v1", ReplyMessage = "PokeChess.Connection.Reply.v1";
        private static OnlineConnection instance;
        public static OnlineConnection Instance
        {
            get { if(instance == null) instance = new GameObject("OnlineConnection").AddComponent<OnlineConnection>(); return instance; }
        }
        private NetworkManager manager;
        private ISession session;
        private Task operation;
        private double sentAt;
        private int sequence;
        private bool messagesRegistered;
        public ConnectionState State { get; private set; }
        public string Error { get; private set; }
        public string Code => session?.Code;
        public string SessionId => session?.Id;
        public RoomSnapshot Room => session == null ? null : new RoomSnapshot(session.Name,session.MaxPlayers,session.IsPrivate,session.Code,
            session.Players.Select(p=>new RoomMember(p.Id,p.Id==session.Host,PlayerFlag(p,"ready"),PlayerFlag(p,"connected"))).ToArray());
        public bool IsHost => manager != null && manager.IsHost;
        public int ConnectedPlayers => manager != null && manager.IsServer ? manager.ConnectedClientsIds.Count : (State == ConnectionState.Connected ? session?.Players.Count ?? 0 : 0);
        public int RequestsReceived { get; private set; }
        public int RepliesReceived { get; private set; }
        public double LastRoundTripMilliseconds { get; private set; }
        public bool IsBusy => operation != null && !operation.IsCompleted;
        private void Awake()
        {
            instance = this; DontDestroyOnLoad(gameObject);
            var obj = new GameObject("NetworkManager"); DontDestroyOnLoad(obj);
            var transport = obj.AddComponent<UnityTransport>(); manager = obj.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig();
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.EnableSceneManagement = false;
            manager.NetworkConfig.ForceSamePrefabs = false;
            manager.NetworkConfig.ProtocolVersion = ProtocolVersion;
        }
        private void OnDestroy()
        {
            if(instance == this) instance=null;
            if(manager != null) { if(manager.IsListening)manager.Shutdown(); if(Application.isPlaying)Destroy(manager.gameObject);else DestroyImmediate(manager.gameObject); }
        }
        public Task CreateAsync() => Run(() => ConnectAsync(null));
        public Task CreateRoomAsync(string name,int capacity,bool isPrivate) => Run(()=>ConnectAsync(null,new RoomCreation(name,capacity,isPrivate)));
        public Task JoinAsync(string code) => Run(() => ConnectAsync((code ?? "").Trim().ToUpperInvariant()));
        public Task JoinByIdAsync(string id) => Run(()=>ConnectAsync(id ?? "",byId:true));
        public Task LeaveAsync() => Run(() => LeaveCoreAsync());
        private Task Run(Func<Task> action)
        {
            if(IsBusy) return operation;
            operation = action(); return operation;
        }
        private async Task ConnectAsync(string code,RoomCreation room=null,bool byId=false)
        {
            if(session != null || manager.IsListening) { Error = "AlreadyConnected"; return; }
            Error = null; RequestsReceived = RepliesReceived = sequence = 0;
            try
            {
                if(!AuthenticationService.Instance.IsAuthorized) throw new InvalidOperationException("NotAuthenticated");
                if(code != null && (code.Length == 0 || code.Length > (byId?128:32))) throw new ArgumentException(byId?"InvalidRoomId":"InvalidJoinCode");
                if(room != null)room.Validate();
                State = code == null ? ConnectionState.Creating : ConnectionState.Joining;
                if(code == null)
                    session = await MultiplayerService.Instance.CreateSessionAsync(new SessionOptions {
                        Name=room?.Name??"PokeChess connection test", MaxPlayers=room?.Capacity??8, IsPrivate=room?.IsPrivate??true,
                        SessionProperties=new Dictionary<string,SessionProperty> {
                            {"gameVersion",new SessionProperty(Application.version,VisibilityPropertyOptions.Public,PropertyIndex.String1)},
                            {"roomKind",new SessionProperty(RoomBrowser.RoomKind,VisibilityPropertyOptions.Public,PropertyIndex.String2)}
                        }
                    }.WithRelayNetwork());
                else session = byId ? await MultiplayerService.Instance.JoinSessionByIdAsync(code) : await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                session.Deleted += SessionEnded; session.RemovedFromSession += SessionEnded;
                ResetLobby();
                State = ConnectionState.Connecting;
                manager.CustomMessagingManager.RegisterNamedMessageHandler(RequestMessage, OnRequest);
                manager.CustomMessagingManager.RegisterNamedMessageHandler(ReplyMessage, OnReply);
                RegisterGameMessages();
                messagesRegistered = true;
                if(manager.IsHost) State = ConnectionState.Connected;
                else SendProbe();
            }
            catch(Exception error)
            {
                Error = error is SessionException failure ? failure.Error.ToString() : error is InvalidOperationException || error is ArgumentException ? error.Message : error.GetType().Name;
                await CleanupAsync(); State = ConnectionState.Failed;
            }
        }
        public void SendProbe()
        {
            if(manager == null || !manager.IsConnectedClient || manager.IsHost) return;
            using(var writer = new FastBufferWriter(1024, Allocator.Temp))
            {
                writer.WriteValueSafe(ProtocolVersion); writer.WriteValueSafe(Application.version); writer.WriteValueSafe(++sequence);
                sentAt = Time.realtimeSinceStartupAsDouble;
                manager.CustomMessagingManager.SendNamedMessage(RequestMessage,NetworkManager.ServerClientId,writer,NetworkDelivery.ReliableSequenced);
            }
        }
        private void OnRequest(ulong sender, FastBufferReader reader)
        {
            if(State == ConnectionState.Leaving || !manager.IsServer || sender == NetworkManager.ServerClientId) return;
            try
            {
                reader.ReadValueSafe(out ushort version); reader.ReadValueSafe(out string gameVersion); reader.ReadValueSafe(out int id);
                if(version != ProtocolVersion || gameVersion != Application.version) { manager.DisconnectClient(sender,"VersionMismatch"); return; }
                RequestsReceived++; verifiedPeers.Add(sender);
                IssueBindingChallenge(sender);
                using(var writer = new FastBufferWriter(1024,Allocator.Temp))
                {
                    writer.WriteValueSafe(version); writer.WriteValueSafe(gameVersion); writer.WriteValueSafe(id);
                    manager.CustomMessagingManager.SendNamedMessage(ReplyMessage,sender,writer,NetworkDelivery.ReliableSequenced);
                }
            }
            catch(Exception) { manager.DisconnectClient(sender,"InvalidHandshake"); }
        }
        private void OnReply(ulong sender, FastBufferReader reader)
        {
            if(State == ConnectionState.Leaving || manager.IsServer || sender != NetworkManager.ServerClientId) return;
            try
            {
                reader.ReadValueSafe(out ushort version); reader.ReadValueSafe(out string gameVersion); reader.ReadValueSafe(out int id);
                if(version != ProtocolVersion || gameVersion != Application.version) { Error="VersionMismatch"; SessionEnded(); return; }
                if(id != sequence) return;
                RepliesReceived++; LastRoundTripMilliseconds=(Time.realtimeSinceStartupAsDouble-sentAt)*1000;
                State=ConnectionState.Connected;
            }
            catch(Exception) { Error="InvalidHandshake"; SessionEnded(); }
        }
        private void Update()
        {
            UpdateLobby();
            UpdateGameCommands();
            Combat.Advance(Time.unscaledDeltaTime);
            if(session == null || IsBusy || State == ConnectionState.Leaving) return;
            if(!manager.IsListening) { Error="ConnectionLost"; SessionEnded(); }
            else if(State == ConnectionState.Connecting && Time.realtimeSinceStartupAsDouble-sentAt > 15) { Error="HandshakeTimeout"; SessionEnded(); }
        }
        private async void SessionEnded()
        {
            if(IsBusy || State == ConnectionState.Leaving) return;
            if(Error == null) Error="SessionEnded";
            await LeaveAsync();
        }
        private async Task LeaveCoreAsync() { State=ConnectionState.Leaving; await CleanupAsync(); State=ConnectionState.Idle; }
        private async Task CleanupAsync()
        {
            var old=session; session=null; ResetLobby();
            if(old != null)
            {
                old.Deleted-=SessionEnded; old.RemovedFromSession-=SessionEnded;
                try { if(old.IsHost) await ((IHostSession)old).DeleteAsync(); else await old.LeaveAsync(); }
                catch(Exception error) { Error=Error ?? "Cleanup: " + error.GetType().Name; }
            }
            if(messagesRegistered && manager.CustomMessagingManager != null)
            {
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(RequestMessage);
                manager.CustomMessagingManager.UnregisterNamedMessageHandler(ReplyMessage);
            }
            messagesRegistered=false;
            UnregisterGameMessages();
            if(manager.IsListening) manager.Shutdown();
            // NGO shutdown finishes on a subsequent frame before another connection can start.
            while(manager.ShutdownInProgress) await Task.Yield();
        }
    }
}
