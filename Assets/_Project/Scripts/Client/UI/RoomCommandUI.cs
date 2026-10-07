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
        private void BuildOnlineCommandUI(RectTransform body)
        {
            var group=Rect("OnlineMatch",body,body.sizeDelta,Vector2.zero);onlineMatchPanel=group.gameObject;
            onlineEconomy=Label("Economy",group,"Connecting to match…",new Vector2(670,38),new Vector2(-110,195),16);
            Button("Leave match",group,new Vector2(350,195),new Vector2(150,32),Leave);
            onlineShop=new UnityEngine.UI.Button[5];for(int i=0;i<5;i++){int slot=i;onlineShop[i]=Button("Shop "+i,group,new Vector2(-340+i*170,135),new Vector2(155,54),()=>SendOnline(MatchCommandKind.Buy,c=>c.slot=slot));}
            onlineReroll=Button("Reroll",group,new Vector2(-300,75),new Vector2(160,30),()=>SendOnline(MatchCommandKind.Reroll));
            onlineLock=Button("Lock",group,new Vector2(-100,75),new Vector2(160,30),()=>SendOnline(MatchCommandKind.Lock,c=>c.locked=!OnlineConnection.Instance.LocalMatchState.locked));
            onlineXP=Button("Buy XP",group,new Vector2(100,75),new Vector2(160,30),()=>SendOnline(MatchCommandKind.BuyXP));
            onlineSurrender=Button("Surrender",group,new Vector2(300,75),new Vector2(160,30),()=>SendOnline(MatchCommandKind.Surrender));
            onlineBoard=new UnityEngine.UI.Button[28];for(int r=0;r<4;r++)for(int col=0;col<7;col++){int x=col,y=r;onlineBoard[r*7+col]=Button(col+","+r,group,new Vector2(-240+col*80,20-r*36),new Vector2(74,32),()=>OnlineCell(PlacementKind.Board,x,y,-1));onlineBoard[r*7+col].GetComponentInChildren<UnityEngine.UI.Text>().fontSize=11;}
            onlineBench=new UnityEngine.UI.Button[9];for(int i=0;i<9;i++){int slot=i;onlineBench[i]=Button("B"+i,group,new Vector2(-320+i*80,-130),new Vector2(74,42),()=>OnlineCell(PlacementKind.Bench,-1,-1,slot));onlineBench[i].GetComponentInChildren<UnityEngine.UI.Text>().fontSize=11;}
            onlineSelection=Label("Selection",group,"Select a unit, then a destination.",new Vector2(640,36),new Vector2(-110,-185),13);
            onlineSell=Button("Sell selected",group,new Vector2(350,-185),new Vector2(150,30),()=>SendOnline(MatchCommandKind.Sell,c=>c.unitId=selectedOnlineUnit));
            onlineActionStatus=Label("ActionStatus",group,"",new Vector2(650,38),new Vector2(-110,-240),13);
            onlineRetry=Button("Retry confirmation",group,new Vector2(355,-240),new Vector2(165,30),async()=>{await OnlineConnection.Instance.RetryPendingCommandAsync();});
            onlinePlayers=Label("Players",group,"",new Vector2(165,175),new Vector2(-390,-25),11);
            onlineObserved=Label("Observed",group,"My board",new Vector2(165,35),new Vector2(355,20),12);
            Button("Observe next",group,new Vector2(355,-20),new Vector2(150,30),()=>{
                var s=OnlineConnection.Instance;var players=s.PublicState?.players;if(players==null)return;
                int index=Array.FindIndex(players,p=>p.id==(observedOnlinePlayer??s.LocalMatchState.playerId));observedOnlinePlayer=players[(index+1)%players.Length].id;selectedOnlineUnit=null;
            });
            Button("My board",group,new Vector2(355,-65),new Vector2(150,30),()=>{observedOnlinePlayer=null;selectedOnlineUnit=null;});
            Button("Sync state",group,new Vector2(430,-110),new Vector2(90,30),async()=>{await OnlineConnection.Instance.RequestStateSyncAsync();});
            onlineMatchPanel.SetActive(false);
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
            for(int i=0;i<5;i++){var slot=state?.shop[i];onlineShop[i].interactable=active&&(state.phase==MatchPhase.Preparation||state.phase==MatchPhase.Combat)&&slot?.definitionId!=null;onlineShop[i].GetComponentInChildren<UnityEngine.UI.Text>().text=slot?.definitionId==null?"Empty":slot.definitionId+"\n"+slot.cost+"G";}
            bool preparation=active&&state.phase==MatchPhase.Preparation;onlineReroll.interactable=onlineLock.interactable=onlineXP.interactable=preparation;onlineSurrender.interactable=active;
            onlineLock.GetComponentInChildren<UnityEngine.UI.Text>().text=state?.locked==true?"Unlock":"Lock";
            if(selectedOnlineUnit!=null&&state!=null&&!state.units.Any(u=>u.id==selectedOnlineUnit))selectedOnlineUnit=null;
            for(int i=0;i<28;i++){int col=i%7,row=i/7;var unit=observed?.board.FirstOrDefault(u=>u.column==col&&u.row==row);onlineBoard[i].interactable=preparation&&myBoard;onlineBoard[i].GetComponentInChildren<UnityEngine.UI.Text>().text=unit==null?col+","+row:unit.definitionId.Substring(0,Math.Min(9,unit.definitionId.Length))+" R"+unit.rank+(unit.items.Length>0?" +"+unit.items.Length:"");}
            for(int i=0;i<9;i++){int slot=i;var unit=state?.units.FirstOrDefault(u=>u.placement==PlacementKind.Bench&&u.bench==slot);onlineBench[i].interactable=active&&myBoard;onlineBench[i].GetComponentInChildren<UnityEngine.UI.Text>().text=unit==null?"B"+i:UnitCaption(unit);}
            onlineSelection.text=selectedOnlineUnit==null?"Select a unit, then a destination to move or swap.":"Selected: "+selectedOnlineUnit+" / Select destination or sell.";
            onlineSell.interactable=preparation&&selectedOnlineUnit!=null;
            onlineActionStatus.text=service.StateSynchronized?(service.CommandFeedback??service.SyncFeedback):service.SyncFeedback??"Synchronizing state…";onlineRetry.gameObject.SetActive(pending);
        }
    }
}
