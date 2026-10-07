using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using PokeChess.Client.Services;
using PokeChess.Network.Session;
public static class VerifyNetworkConnection
{
    [Serializable] private sealed class HostInfo { public string code; }
    [Serializable] private sealed class Report { public string status,code,error; public int cycles,requests,replies,players; public bool hostLoss,invalidCodeRejected; public double rtt; }
    private static string Root => Path.Combine(Application.dataPath,"../TestResults/WBS5.2");
    public static string Main() { Host();return "Editor Host QA started"; }
    public static string JoinWindowsHost() { Client();return "Editor Client QA started"; }
    private static void Save(string name,Report report) { Directory.CreateDirectory(Root);File.WriteAllText(Path.Combine(Root,name),JsonUtility.ToJson(report,true)); }
    private static async Task Wait(Func<bool> condition)
    { double end=Time.realtimeSinceStartupAsDouble+120;while(!condition()){if(Time.realtimeSinceStartupAsDouble>end)throw new TimeoutException();await Task.Yield();} }
    private static async void Host()
    {
        try {
            await OnlineAuthenticationService.Instance.EnsureSignedInAsync();if(!OnlineAuthenticationService.Instance.IsReady)throw new Exception("Authentication failed");
            var s=OnlineConnection.Instance;
            await s.JoinAsync("INVALID-NETWORK-QA-CODE");
            if(s.State!=ConnectionState.Failed||s.SessionId!=null)throw new Exception("Invalid code not rejected/cleaned");
            await s.CreateAsync();if(s.State!=ConnectionState.Connected||!s.IsHost)throw new Exception(s.Error??"Host not started");
            Save("editor-host.json",new Report{status="Ready",code=s.Code,players=s.ConnectedPlayers,invalidCodeRejected=true});
            await Wait(()=>s.RequestsReceived>=2&&s.ConnectedPlayers>=2);
            Save("editor-host.json",new Report{status="ConnectedTwice",code=s.Code,requests=s.RequestsReceived,players=s.ConnectedPlayers,invalidCodeRejected=true});
        } catch(Exception e){Save("editor-host.json",new Report{status="Failed",error=e.ToString()});}
    }
    private static async void Client()
    {
        int cycles=0;
        try {
            await OnlineAuthenticationService.Instance.EnsureSignedInAsync();if(!OnlineAuthenticationService.Instance.IsReady)throw new Exception("Authentication failed");
            var code=JsonUtility.FromJson<HostInfo>(File.ReadAllText(Path.Combine(Root,"WindowsHost/status.json"))).code;
            var s=OnlineConnection.Instance;
            for(int i=0;i<2;i++) {
                await s.JoinAsync(code);await Wait(()=>s.State==ConnectionState.Connected&&s.RepliesReceived>0);cycles++;
                Save("editor-client.json",new Report{status="Connected",cycles=cycles,replies=s.RepliesReceived,rtt=s.LastRoundTripMilliseconds,players=s.ConnectedPlayers});
                if(i==0)await s.LeaveAsync();
            }
            await Wait(()=>s.State==ConnectionState.Idle);
            Save("editor-client.json",new Report{status="Completed",cycles=cycles,replies=s.RepliesReceived,rtt=s.LastRoundTripMilliseconds,hostLoss=true,error=s.Error});
        } catch(Exception e){Save("editor-client.json",new Report{status="Failed",cycles=cycles,error=e.ToString()});}
    }
}
