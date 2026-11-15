using System;
using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using PokeChess.Network.Session;

namespace PokeChess.Client.UI
{
    public sealed partial class RoomFlowView
    {
        private GameObject onlineMatchPanel;
        private UnityEngine.UI.Text onlineEconomy,onlineActionStatus,onlineSelection;
        private UnityEngine.UI.Button[] onlineShop,onlineBoard,onlineBench;
        private UnityEngine.UI.Button onlineReroll,onlineLock,onlineXP,onlineSell,onlineSurrender,onlineRetry;
        private string selectedOnlineUnit;
        private string observedOnlinePlayer;
        private UnityEngine.UI.Text onlinePlayers,onlineObserved;
        private void BindOnlineCommandUI(Transform group)
        {
            onlineMatchPanel = group.gameObject;
            onlineEconomy = UiPrefab.Get<UnityEngine.UI.Text>(group, "Economy");
            onlineSelection = UiPrefab.Get<UnityEngine.UI.Text>(group, "Selection");
            onlineActionStatus = UiPrefab.Get<UnityEngine.UI.Text>(group, "ActionStatus");
            onlinePlayers = UiPrefab.Get<UnityEngine.UI.Text>(group, "Players");
            onlineObserved = UiPrefab.Get<UnityEngine.UI.Text>(group, "Observed");
            UiPrefab.Bind(group, "Leave match", Leave);
            onlineShop = new UnityEngine.UI.Button[5];
            for (int i = 0; i < onlineShop.Length; i++)
            {
                int slot = i;
                onlineShop[i] = UiPrefab.Bind(group, "Shop " + i, () => SendOnline(MatchCommandKind.Buy, c => c.slot = slot));
            }
            onlineReroll = UiPrefab.Bind(group, "Reroll", () => SendOnline(MatchCommandKind.Reroll));
            onlineLock = UiPrefab.Bind(group, "Lock", () => SendOnline(MatchCommandKind.Lock, c => c.locked = !OnlineConnection.Instance.LocalMatchState.locked));
            onlineXP = UiPrefab.Bind(group, "Buy XP", () => SendOnline(MatchCommandKind.BuyXP));
            onlineSurrender = UiPrefab.Bind(group, "Surrender", () => SendOnline(MatchCommandKind.Surrender));
            onlineBoard = new UnityEngine.UI.Button[28];
            for (int i = 0; i < onlineBoard.Length; i++)
            {
                int column = i % 7, row = i / 7;
                onlineBoard[i] = UiPrefab.Bind(group, column + "," + row, () => OnlineCell(PlacementKind.Board, column, row, -1));
            }
            onlineBench = new UnityEngine.UI.Button[9];
            for (int i = 0; i < onlineBench.Length; i++)
            {
                int slot = i;
                onlineBench[i] = UiPrefab.Bind(group, "B" + i, () => OnlineCell(PlacementKind.Bench, -1, -1, slot));
            }
            onlineSell = UiPrefab.Bind(group, "Sell selected", () => SendOnline(MatchCommandKind.Sell, c => c.unitId = selectedOnlineUnit));
            onlineRetry = UiPrefab.Bind(group, "Retry confirmation", async () => await OnlineConnection.Instance.RetryPendingCommandAsync());
            UiPrefab.Bind(group, "Observe next", () =>
            {
                var service = OnlineConnection.Instance;
                var players = service.PublicState?.players;
                if (players == null) return;
                int index = Array.FindIndex(players, p => p.id == (observedOnlinePlayer ?? service.LocalMatchState.playerId));
                observedOnlinePlayer = players[(index + 1) % players.Length].id;
                selectedOnlineUnit = null;
            });
            UiPrefab.Bind(group, "My board", () => { observedOnlinePlayer = null; selectedOnlineUnit = null; });
            UiPrefab.Bind(group, "Sync state", async () => await OnlineConnection.Instance.RequestStateSyncAsync());
            BindOnlineCombatUI(group);
        }
        private async void SendOnline(MatchCommandKind kind,Action<MatchCommand> configure=null)
        {
            var service=OnlineConnection.Instance;if(!service.StateSynchronized||service.PendingCommand!=null)return;
            var command=service.CreateCommand(kind);configure?.Invoke(command);await service.SubmitCommandAsync(command);
        }
        private void OnlineCell(PlacementKind kind,int column,int row,int bench)
        {
            var service=OnlineConnection.Instance;var state=service.LocalMatchState;if(!service.StateSynchronized||state==null||observedOnlinePlayer!=null&&observedOnlinePlayer!=state.playerId)return;
            var unit=state.units.FirstOrDefault(u=>u.placement==kind&&(kind==PlacementKind.Board?u.column==column&&u.row==row:u.bench==bench));
            if(selectedOnlineUnit==null){selectedOnlineUnit=unit?.id;return;}
            if(unit?.id==selectedOnlineUnit){selectedOnlineUnit=null;return;}
            SendOnline(MatchCommandKind.Move,c=>{c.unitId=selectedOnlineUnit;c.destination=kind;c.column=column;c.row=row;c.bench=bench;});
        }
        private static string UnitCaption(OwnerUnit unit)=>unit==null?"":unit.definitionId.Substring(0,Math.Min(9,unit.definitionId.Length))+" R"+unit.rank;
        private void UpdateOnlineCommandUI(OnlineConnection service)
        {
            if(!onlineMatchPanel.activeSelf)return;var state=service.LocalMatchState;
            var pub=service.PublicState;
            bool pending=service.PendingCommand!=null,active=service.StateSynchronized&&!pending&&!state.eliminated&&pub.phase!=MatchPhase.Finished;
            var observed=pub?.players.FirstOrDefault(p=>p.id==(observedOnlinePlayer??state?.playerId));
            if(observed==null){observedOnlinePlayer=null;observed=pub?.players.FirstOrDefault(p=>p.id==state?.playerId);}
            bool myBoard=observed?.id==state?.playerId;
            onlineObserved.text=myBoard?"My board":"Board: "+(observed?.id.Substring(Math.Max(0,observed.id.Length-6))??"Waiting");
            onlinePlayers.text=pub==null?"Synchronizing…":string.Join("\n",pub.players.Select(p=>(p.id==state.playerId?"You":p.id.Substring(Math.Max(0,p.id.Length-6)))+" / HP "+p.hp+" / Lv "+p.level+(p.eliminated?" / Out":"")+(p.placement>0?" / #"+p.placement:"")));
            onlineEconomy.text=state==null?"Connecting to match…":"Round "+state.round+" / "+state.phase+" / "+state.gold+"G / Lv "+state.level+" / XP "+state.xp+" / HP "+state.hp;
            for(int i=0;i<5;i++){var slot=state?.shop[i];onlineShop[i].interactable=active&&slot?.canPurchase==true;onlineShop[i].GetComponentInChildren<UnityEngine.UI.Text>().text=slot?.definitionId==null?"Empty":slot.definitionId+"\n"+PlacementSandboxView.ShopPurchasePrice(slot.purchasePrice,slot.purchaseQuantity);}
            bool preparation=active&&state.phase==MatchPhase.Preparation;
            bool economy=preparation||(active&&state.phase==MatchPhase.Combat);onlineReroll.interactable=onlineXP.interactable=onlineLock.interactable=economy;onlineSurrender.interactable=active;
            onlineLock.GetComponentInChildren<UnityEngine.UI.Text>().text=state?.locked==true?"Unlock":"Lock";
            if(selectedOnlineUnit!=null&&state!=null&&!state.units.Any(u=>u.id==selectedOnlineUnit))selectedOnlineUnit=null;
            for(int i=0;i<28;i++){int col=i%7,row=i/7;var unit=observed?.board.FirstOrDefault(u=>u.column==col&&u.row==row);onlineBoard[i].interactable=preparation&&myBoard;onlineBoard[i].GetComponentInChildren<UnityEngine.UI.Text>().text=unit==null?col+","+row:unit.definitionId.Substring(0,Math.Min(9,unit.definitionId.Length))+" R"+unit.rank+(unit.items.Length>0?" +"+unit.items.Length:"");}
            for(int i=0;i<9;i++){int slot=i;var unit=state?.units.FirstOrDefault(u=>u.placement==PlacementKind.Bench&&u.bench==slot);onlineBench[i].interactable=active&&myBoard;onlineBench[i].GetComponentInChildren<UnityEngine.UI.Text>().text=unit==null?"B"+i:UnitCaption(unit);}
            onlineSelection.text=selectedOnlineUnit==null?"Select a unit, then a destination to move or swap.":"Selected: "+selectedOnlineUnit+" / Select destination or sell.";
            onlineSell.interactable=preparation&&selectedOnlineUnit!=null;
            onlineActionStatus.text=service.StateSynchronized?(service.CommandFeedback??service.SyncFeedback):service.SyncFeedback??"Synchronizing state…";onlineRetry.gameObject.SetActive(pending);
            UpdateOnlineCombatUI(service);
        }
    }
}
