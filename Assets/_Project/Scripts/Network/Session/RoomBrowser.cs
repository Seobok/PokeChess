using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;

namespace PokeChess.Network.Session
{
    public sealed class RoomCreation
    {
        public string Name { get; }
        public int Capacity { get; }
        public bool IsPrivate { get; }
        public RoomCreation(string name,int capacity,bool isPrivate) {Name=name?.Trim();Capacity=capacity;IsPrivate=isPrivate;}
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(Name)||Name.Length>32||Name.Any(char.IsControl))throw new ArgumentException("InvalidRoomName");
            if(Capacity<2||Capacity>8)throw new ArgumentException("InvalidRoomCapacity");
        }
    }
    public sealed class RoomMember
    {
        public string Id { get; }
        public bool IsHost { get; }
        public RoomMember(string id,bool host){Id=id;IsHost=host;}
        public string Label => "Player " + Id.Substring(Math.Max(0,Id.Length-6)) + (IsHost?" (Host)":"");
    }
    public sealed class RoomSnapshot
    {
        public string Name { get; }
        public int Capacity { get; }
        public bool IsPrivate { get; }
        public string Code { get; }
        public IReadOnlyList<RoomMember> Members { get; }
        public RoomSnapshot(string name,int capacity,bool isPrivate,string code,RoomMember[] members){Name=name;Capacity=capacity;IsPrivate=isPrivate;Code=code;Members=Array.AsReadOnly(members);}
    }
    public sealed class RoomListing
    {
        public string Id { get; }
        public string Name { get; }
        public int Players { get; }
        public int Capacity { get; }
        public RoomListing(string id,string name,int players,int capacity){Id=id;Name=name;Players=players;Capacity=capacity;}
    }
    public enum RoomListState { Idle, Loading, Ready, Failed }
    public sealed class RoomBrowser
    {
        public const string RoomKind="pokechess-room-v1";
        public RoomListState State { get; private set; }
        public string Error { get; private set; }
        public IReadOnlyList<RoomListing> Rooms { get; private set; }=Array.Empty<RoomListing>();
        public string NextPage { get; private set; }
        public event Action Changed;
        private Task pending;
        private double nextRequest;
        public Task RefreshAsync(bool nextPage=false)
        {
            if(pending!=null&&!pending.IsCompleted)return pending;
            if(Time.realtimeSinceStartupAsDouble<nextRequest)return Task.CompletedTask;
            pending=QueryAsync(nextPage?NextPage:null);return pending;
        }
        private async Task QueryAsync(string continuation)
        {
            State=RoomListState.Loading;Error=null;Changed?.Invoke();
            try
            {
                if(!AuthenticationService.Instance.IsAuthorized)throw new InvalidOperationException("NotAuthenticated");
                var result=await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions {
                    Count=25,ContinuationToken=continuation,
                    FilterOptions=new List<FilterOption> {
                        new FilterOption(FilterField.StringIndex1,Application.version,FilterOperation.Equal),
                        new FilterOption(FilterField.StringIndex2,RoomKind,FilterOperation.Equal),
                        new FilterOption(FilterField.AvailableSlots,"0",FilterOperation.Greater),
                        new FilterOption(FilterField.IsLocked,"false",FilterOperation.Equal)
                    }
                });
                Rooms=Array.AsReadOnly(result.Sessions.Select(s=>new RoomListing(s.Id,s.Name,s.MaxPlayers-s.AvailableSlots,s.MaxPlayers)).ToArray());
                NextPage=result.ContinuationToken;State=RoomListState.Ready;
            }
            catch(Exception e){Rooms=Array.Empty<RoomListing>();NextPage=null;Error=e is SessionException failure?failure.Error.ToString():e.GetType().Name;State=RoomListState.Failed;}
            finally {nextRequest=Time.realtimeSinceStartupAsDouble+1;Changed?.Invoke();}
        }
    }
}
