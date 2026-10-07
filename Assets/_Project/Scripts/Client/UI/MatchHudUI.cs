using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PokeChess.Core.Match;
namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private sealed class ProfileRow {
            public RectTransform Rect; public UnityEngine.UI.Image Background,HP;
            public UnityEngine.UI.Text Name,State,Damage;
            public int PreviousHP;public float DamageUntil;public int DamageValue;
        }
        private readonly Dictionary<string,ProfileRow> hudProfiles=new Dictionary<string,ProfileRow>();
        private MatchState hudMatch;
        private RectTransform sidebar;
        private UnityEngine.UI.Text hudRound,hudNotice,hudPlayers;
        private UnityEngine.UI.Image hudTimer,hudTimerBorder;
        public float HudTimerProgress {get;private set;}
        public string HudTimerKind {get;private set;}
        public string HudNoticeText=>hudNotice?.text;
        public int HudProfileCount=>hudProfiles.Count;
        public string HudProfileText(string id)=>hudProfiles[id].Name.text+" / "+hudProfiles[id].State.text;
        // Reserved for the later scouting controller; information-only profiles have no click target yet.
        public event Action<string> PlayerViewRequested;
        public void RequestPlayerView(string id) {
            if(Match.Players.Any(p=>p.PlayerId==id))PlayerViewRequested?.Invoke(id);
        }
        private UnityEngine.UI.Image HudImage(string name,Transform parent,Vector2 size,Vector2 pos,Color color) {
            var rect=Rect(name,parent,size,pos);var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color=color;image.raycastTarget=false;return image;
        }
        private void BuildMatchHud() {
            var badge=HudImage("RoundBadge",canvasRect,new Vector2(180,30),new Vector2(0,337),new Color(.12f,.2f,.3f));
            hudRound=Label("RoundBadgeText",badge.transform,"",19,new Vector2(175,28),Vector2.zero);
            hudTimerBorder=HudImage("TimerBorder",canvasRect,new Vector2(286,14),new Vector2(0,310),new Color(.3f,.4f,.5f));
            var track=HudImage("TimerTrack",hudTimerBorder.transform,new Vector2(280,8),Vector2.zero,new Color(.04f,.07f,.11f));
            hudTimer=HudImage("TimerFill",track.transform,new Vector2(280,8),Vector2.zero,Color.white);
            hudTimer.rectTransform.anchorMin=hudTimer.rectTransform.anchorMax=new Vector2(0,.5f);
            hudTimer.rectTransform.pivot=new Vector2(0,.5f);hudTimer.rectTransform.anchoredPosition=Vector2.zero;
            hudNotice=Label("HudNotice",canvasRect,"",12,new Vector2(600,20),new Vector2(0,288));
            sidebar=Panel("PlayerSidebar",new Vector2(205,334),new Vector2(-510,130));
            sidebar.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
            hudPlayers=Label("PlayerCount",sidebar,"",13,new Vector2(195,24),new Vector2(0,149));
        }
        private void RenderMatchHud() {
            if(hudRound==null||RoundFlow==null)return;
            hudRound.text="ROUND "+Match.RoundNumber;
            var own=RoundLoop.GetBattleFor("p1");
            string notice="";string kind="Idle";double progress=0;
            if(Match.Phase==MatchPhase.Finished)notice="MATCH COMPLETE";
            else if(!automaticRoundFlow)notice="Paused for input test";
            else if(RoundFlow.Fault!=null)notice="Round paused — retry required";
            else if(RoundFlow.AwaitingMatchEnd)notice="Waiting for match end";
            else if(RoundLoop.NextPreparationPending)notice="Updating shops…";
            else if(Match.Phase==MatchPhase.Preparation&&!RoundLoop.PreparationReady)notice="Preparing round…";
            else if(Match.Phase==MatchPhase.Result&&!RoundLoop.IsSettled)notice="Settling results…";
            else if(Match.Phase==MatchPhase.Preparation) {
                kind="Preparation";progress=RoundFlow.RemainingSeconds/RoundFlow.Rules.PreparationSeconds;
            } else if(Match.Phase==MatchPhase.Result) {
                kind="Result";progress=RoundFlow.RemainingSeconds/RoundFlow.Rules.ResultSeconds;
            } else if(Match.Phase==MatchPhase.Combat) {
                if(own==null||own.IsComplete)notice="Waiting for other battles";
                else if(own.Battle!=null) {
                    double elapsed=own.Battle.ElapsedSeconds;
                    kind=own.Battle.IsOvertime?"Overtime":"Combat";
                    double overtime=own.Battle.OvertimeStartTick/(double)own.Battle.TickRate;
                    double limit=own.Battle.TimeLimitTick/(double)own.Battle.TickRate;
                    progress=kind=="Overtime"?(limit-elapsed)/(limit-overtime):(overtime-elapsed)/overtime;
                }
            }
            if(kind=="Idle"&&Match.Phase!=MatchPhase.Finished&&ReferenceEquals(hudMatch,Match))progress=HudTimerProgress;
            HudTimerKind=kind;HudTimerProgress=Mathf.Clamp01((float)progress);
            var color=kind=="Preparation"?new Color(.2f,.6f,1):kind=="Combat"?new Color(1,.58f,.18f):
                kind=="Overtime"?new Color(1,.22f,.25f):kind=="Result"?new Color(.7f,.4f,.95f):new Color(.45f,.5f,.6f);
            bool warning=kind=="Overtime"||kind=="Preparation"&&RoundFlow.RemainingSeconds<=5;
            float pulse=warning?.7f+.3f*Mathf.Abs(Mathf.Sin(Time.unscaledTime*5)):1;
            hudTimer.color=new Color(color.r,color.g,color.b,kind=="Result"?.55f+.45f*HudTimerProgress:pulse);
            hudTimerBorder.color=warning?Color.Lerp(color,Color.white,1-pulse):color*.65f;
            hudTimer.rectTransform.sizeDelta=new Vector2(280*HudTimerProgress,8);
            hudNotice.text=notice;
            hudTimerBorder.gameObject.SetActive(Match.Phase!=MatchPhase.Finished&&!(Match.Phase==MatchPhase.Combat&&(own==null||own.IsComplete)));
            if(!ReferenceEquals(hudMatch,Match)) {
                foreach(var row in hudProfiles.Values){row.Rect.gameObject.SetActive(false);Destroy(row.Rect.gameObject);}
                hudProfiles.Clear();hudMatch=Match;
            }
            var ordered=Match.Players.OrderBy(p=>p.PlayerId=="p1"?0:p.IsEliminated?2:1).ToArray();
            hudPlayers.text="ALIVE "+ordered.Count(p=>p.HP>0&&!p.IsEliminated)+" / "+ordered.Length;
            float top=135;
            foreach(var p in ordered) {
                bool self=p.PlayerId=="p1";float height=self?48:32;
                if(!hudProfiles.TryGetValue(p.PlayerId,out var row)) {
                    var image=HudImage("Profile_"+p.PlayerId,sidebar,new Vector2(195,height),Vector2.zero,Color.white);
                    row=new ProfileRow{Rect=image.rectTransform,Background=image,PreviousHP=p.HP};
                    var avatar=HudImage("Avatar",row.Rect,new Vector2(self?32:22,self?32:22),new Vector2(-79,0),new Color(.22f,.35f,.46f));
                    Label("AvatarText",avatar.transform,p.PlayerId.ToUpperInvariant(),11,new Vector2(32,24),Vector2.zero);
                    row.Name=Label("Name",row.Rect,"",self?14:12,new Vector2(108,18),new Vector2(-5,self?12:8));
                    row.Name.alignment=TextAnchor.MiddleLeft;
                    row.State=Label("Status",row.Rect,"",self?10:9,new Vector2(130,16),new Vector2(2,-4));
                    row.State.alignment=TextAnchor.MiddleLeft;
                    var track=HudImage("HPTrack",row.Rect,new Vector2(125,3),new Vector2(8,-height/2+4),new Color(.05f,.08f,.11f));
                    row.HP=HudImage("HPFill",track.transform,new Vector2(125,3),Vector2.zero,new Color(.25f,.85f,.55f));
                    row.HP.rectTransform.anchorMin=row.HP.rectTransform.anchorMax=new Vector2(0,.5f);row.HP.rectTransform.pivot=new Vector2(0,.5f);
                    row.Damage=Label("Damage",row.Rect,"",12,new Vector2(35,18),new Vector2(77,8));
                    row.Damage.color=new Color(1,.45f,.4f);hudProfiles.Add(p.PlayerId,row);
                }
                row.Rect.anchoredPosition=new Vector2(0,top-height/2);top-=height+4;
                if(p.HP<row.PreviousHP){row.DamageValue=row.PreviousHP-p.HP;row.DamageUntil=Time.unscaledTime+1.5f;}
                row.PreviousHP=p.HP;row.Damage.text=Time.unscaledTime<row.DamageUntil?"-"+row.DamageValue:"";
                row.Name.text=(self?"YOU · ":"")+p.PlayerId+"  "+p.HP+" HP";
                string state=p.FinalPlacement==1?"#1 WINNER":p.IsEliminated?"#"+p.FinalPlacement+" "+
                    (p.Elimination?.Reason==EliminationReason.Surrender?"SURRENDER":"OUT"):"IN MATCH";
                bool opponent=Match.Phase==MatchPhase.Combat&&own!=null&&own.Pairing.OpponentOf("p1")==p.PlayerId;
                if(!p.IsEliminated&&p.FinalPlacement!=1) {
                    var pair=RoundLoop.GetBattleFor(p.PlayerId);
                    if(Match.Phase==MatchPhase.Combat)state=pair==null?"WAITING":pair.IsComplete?"BATTLE DONE":"IN BATTLE";
                    else if(Match.Phase==MatchPhase.Preparation)state="READY TO DEPLOY";
                    else if(Match.Phase==MatchPhase.Result)state="ROUND COMPLETE";
                }
                row.State.text=(opponent?"VS · ":"")+state;
                row.Background.color=p.IsEliminated?new Color(.09f,.12f,.16f):opponent?new Color(.32f,.23f,.13f):self?new Color(.1f,.27f,.36f):new Color(.12f,.18f,.25f);
                row.Name.color=row.State.color=p.IsEliminated?new Color(.55f,.6f,.65f):Color.white;
                row.HP.rectTransform.sizeDelta=new Vector2(125*Mathf.Clamp01(p.HP/(float)p.Rules.StartingHP),3);
            }
        }
    }
}
