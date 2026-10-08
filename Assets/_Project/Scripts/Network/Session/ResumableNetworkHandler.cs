using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport.Relay;
using UnityEngine;

namespace PokeChess.Network.Session
{
    // Sessions supplies network configuration. Only the client's transport is restarted;
    // membership is retained, so a locked match does not become an ordinary new join.
    internal sealed class ResumableNetworkHandler : INetworkHandler
    {
        private readonly NetworkManager manager;
        private readonly bool allowHost;
        private NetworkConfiguration configuration;
        private bool cancelled;
        private int generation;
        public ResumableNetworkHandler(NetworkManager manager,bool allowHost){this.manager=manager;this.allowHost=allowHost;}
        public async Task StartAsync(NetworkConfiguration value)
        {
            if(cancelled)throw new OperationCanceledException();
            if(value.Role!=NetworkRole.Client&&!allowHost)throw new InvalidOperationException("HostLost");
            if(value.Type!=NetworkType.Relay)throw new InvalidOperationException("UnsupportedNetworkType");
            configuration=value;
            await StartTransportAsync(value.Role,value.RelayServerData);
        }
        private async Task StartTransportAsync(NetworkRole role,RelayServerData data)
        {
            if(cancelled)throw new OperationCanceledException();
            if(manager.IsListening)return;
            while(manager.ShutdownInProgress)await Task.Yield();
            var transport=(UnityTransport)manager.NetworkConfig.NetworkTransport;
            transport.SetRelayServerData(data);
            if(!(role==NetworkRole.Client?manager.StartClient():manager.StartHost()))throw new InvalidOperationException("NetworkStartFailed");
            double deadline=Time.realtimeSinceStartupAsDouble+12;
            while(!manager.IsConnectedClient){
                if(!manager.IsListening||Time.realtimeSinceStartupAsDouble>=deadline){await StopAsync();throw new TimeoutException("NetworkConnectTimeout");}
                await Task.Yield();
            }
        }
        public async Task StopAsync()
        {generation++;if(manager.IsListening)manager.Shutdown();while(manager.ShutdownInProgress)await Task.Yield();}
        public void Cancel(){cancelled=true;generation++;}
        public async Task<string> RestartAsync(string relayCode)
        {
            if(configuration==null||configuration.Role!=NetworkRole.Client||string.IsNullOrEmpty(relayCode))throw new InvalidOperationException("NetworkConfigurationMissing");
            await StopAsync();
            int epoch=generation;
            // SDK 2.3.3 exposes no public client network restart. Retain Sessions
            // membership, but get fresh Relay credentials; old allocations expire.
            var allocation=await RelayService.Instance.JoinAllocationAsync(relayCode);
            if(cancelled||epoch!=generation)throw new OperationCanceledException();
            await StartTransportAsync(NetworkRole.Client,allocation.ToRelayServerData(configuration.RelayServerData.IsSecure!=0?"dtls":"udp"));
            return allocation.AllocationId.ToString();
        }
    }
}
