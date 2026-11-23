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
        public bool IsReady { get; }
        public bool IsConnected { get; }
        public RoomMember(string id,bool host,bool ready=false,bool connected=false){Id=id;IsHost=host;IsReady=ready;IsConnected=connected;}
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
        public static string RoomKind=>OnlineConnection.RequireAccountAdmission?"pokechess-room-account-v2":"pokechess-room-anonymous-test-v1";
        // Client flow gate only; server-verified account/UGS binding is a separate integration.
        public static Func<bool> AccountAccessAllowed { get; set; } = () => false;
        public RoomListState State { get; private set; }
        public string Error { get; private set; }
        public IReadOnlyList<RoomListing> Rooms { get; private set; }=Array.Empty<RoomListing>();
        public string NextPage { get; private set; }
        public bool IsLoadingMore { get; private set; }
        public bool CanLoadMore => State == RoomListState.Ready && !string.IsNullOrEmpty(NextPage) && Time.realtimeSinceStartupAsDouble >= nextRequest;
        public event Action Changed;
        private Task pending;
        private double nextRequest;
        public Task RefreshAsync(bool nextPage=false)
        {
            if (nextPage && string.IsNullOrEmpty(NextPage)) return Task.CompletedTask;
            if(pending!=null&&!pending.IsCompleted)return pending;
            if(Time.realtimeSinceStartupAsDouble<nextRequest)return Task.CompletedTask;
            pending=QueryAsync(nextPage?NextPage:null);return pending;
        }
        private static IReadOnlyList<RoomListing> MergeRooms(IEnumerable<RoomListing> existing, IEnumerable<RoomListing> page)
            => Array.AsReadOnly(existing.Concat(page).GroupBy(room => room.Id, StringComparer.Ordinal)
                .Select(group => group.Last()).ToArray());

        private async Task QueryAsync(string continuation)
        {
            bool append = !string.IsNullOrEmpty(continuation);
            IsLoadingMore = append;
            State=RoomListState.Loading;Error=null;Changed?.Invoke();
            try
            {
                if(!AccountAccessAllowed()) throw new InvalidOperationException("AccountLoginRequired");
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
                if (!AccountAccessAllowed()) throw new InvalidOperationException("AccountLoginRequired");
                var page = result.Sessions.Select(s=>new RoomListing(s.Id,s.Name,s.MaxPlayers-s.AvailableSlots,s.MaxPlayers));
                Rooms = MergeRooms(append ? Rooms : Array.Empty<RoomListing>(), page);
                NextPage=result.ContinuationToken;State=RoomListState.Ready;
            }
            catch(Exception e){if(!append){Rooms=Array.Empty<RoomListing>();NextPage=null;}Error=e is SessionException failure?failure.Error.ToString():e.GetType().Name;State=RoomListState.Failed;}
            finally {IsLoadingMore=false;nextRequest=Time.realtimeSinceStartupAsDouble+1;Changed?.Invoke();}
        }
    }
}
