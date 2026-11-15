using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private readonly Dictionary<string,ProfileRow> hudProfiles=new Dictionary<string,ProfileRow>();
        private MatchState hudMatch;
        private UnityEngine.UI.Text hudRound,hudNotice,hudTime,hudCapacityText;
        private RectTransform hudCapacity;
        private readonly UnityEngine.UI.Image[] hudRoundIcons = new UnityEngine.UI.Image[7];
        private UnityEngine.UI.Image hudRoundPointer;
        private Sprite[] hudBattleIcons, hudWildIcons;
        private UnityEngine.UI.Image hudTimer,hudTimerBorder;
        public float HudTimerProgress {get;private set;}
        public string HudTimerKind {get;private set;}
        public string HudNoticeText=>hudNotice?.text;
        public int HudProfileCount=>hudProfiles.Count;
        public string HudProfileText(string id)=>hudProfiles[id].Name.text+" / "+hudProfiles[id].State.text;
        private string localViewedPlayerId = "p1";
        public string ViewedPlayerId => IsOnlineMatch ? OnlineObservedPlayerId : localViewedPlayerId;
        private PlayerState ViewedPlayer => Match.GetPlayer(localViewedPlayerId);
        public event Action<string> PlayerViewRequested;
        public void RequestPlayerView(string id) {
            if(IsOnlineMatch){WatchOnlinePlayer(id);return;}
            if(Match==null||!Match.Players.Any(p=>p.PlayerId==id)||localViewedPlayerId==id)return;
            SetLocalPlayerView(id);
            Render();
        }

        private void SetLocalPlayerView(string id)
        {
            CloseSynergyTarget();
            ClearDragState();
            selectedUnitId = hoveredUnitId = null;
            hoveredShopSlot = -1;
            unitInfoPinned = false;
            HideUnitInfoHint();
            localViewedPlayerId = id;
            Feedback("View changed.");
            foreach(var token in combatTokens.Values)Destroy(token.transform.parent.gameObject);combatTokens.Clear();ClearFighterSprites();lastVisualBattle=null;
            PlayerViewRequested?.Invoke(id);
        }
        private UnityEngine.UI.Image HudImage(string name,Transform parent,Vector2 size,Vector2 pos,Color color) {
            var rect=Rect(name,parent,size,pos);var image=rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color=color;image.raycastTarget=false;return image;
        }
        private void BuildMatchHud()
        {
            var root = UiPrefab.Instantiate("MatchHud", canvasRect);
            var badge = root.Find("RoundBadge");
            hudRound = UiPrefab.Get<UnityEngine.UI.Text>(badge, "RoundBadgeText");
            hudTime = UiPrefab.Get<UnityEngine.UI.Text>(badge, "RoundTime");
            for (int i = 0; i < hudRoundIcons.Length; i++)
                hudRoundIcons[i] = UiPrefab.Get<UnityEngine.UI.Image>(badge, $"RoundIcon_{i}");
            hudRoundPointer = UiPrefab.Get<UnityEngine.UI.Image>(badge, "CurrentRoundPointer");
            hudBattleIcons = LoadRoundIcons("HudPhaseBattle");
            hudWildIcons = LoadRoundIcons("HudPhaseWild");
            hudTimerBorder = UiPrefab.Get<UnityEngine.UI.Image>(root, "TimerTrack");
            hudTimer = UiPrefab.Get<UnityEngine.UI.Image>(hudTimerBorder.transform, "TimerFill");
            hudNotice = UiPrefab.Get<UnityEngine.UI.Text>(root, "HudNotice");
            hudCapacity = UiPrefab.Get<RectTransform>(root, "BoardCapacity");
            hudCapacityText = UiPrefab.Get<UnityEngine.UI.Text>(hudCapacity, "CapacityCount");
        }

        private static Sprite[] LoadRoundIcons(string asset) => new[]
        {
            Resources.Load<Sprite>("MainGameHud/" + asset),
            Resources.Load<Sprite>("MainGameHud/" + asset + "Active"),
            Resources.Load<Sprite>("MainGameHud/" + asset + "Upcoming")
        };

        private void RenderRoundStrip(int round, bool wildRoundsEnabled, double? remaining)
        {
            hudRound.text = round.ToString("00");
            if (remaining.HasValue)
            {
                int seconds = (int)Math.Ceiling(Math.Max(0, remaining.Value));
                hudTime.text = $"{seconds / 60:00}:{seconds % 60:00}";
            }
            else hudTime.text = "–";

            int first = Math.Max(1, round - 3);
            for (int i = 0; i < hudRoundIcons.Length; i++)
            {
                int iconRound = first + i;
                var icons = wildRoundsEnabled && WildRoundSystem.IsWildRound(iconRound) ? hudWildIcons : hudBattleIcons;
                hudRoundIcons[i].sprite = icons[iconRound < round ? 0 : iconRound == round ? 1 : 2];
            }
            hudRoundPointer.gameObject.SetActive(round >= 1);
            var pointer = hudRoundPointer.rectTransform;
            pointer.anchoredPosition = new Vector2(hudRoundIcons[Mathf.Clamp(round - first, 0, hudRoundIcons.Length - 1)].rectTransform.anchoredPosition.x, pointer.anchoredPosition.y);
        }

        private void RenderHudTimer(bool visible, bool warning)
        {
            hudTimerBorder.gameObject.SetActive(visible);
            hudTimer.fillAmount = HudTimerProgress;
            hudTime.color = warning ? new Color32(160, 54, 19, 255) : new Color32(13, 23, 55, 255);
            hudTimer.color = warning
                ? new Color(1, 1, 1, .75f + .25f * Mathf.Sin(Time.unscaledTime * 5))
                : Color.white;
        }

        private void RenderBoardCapacity(bool visible, int deployed, int capacity)
        {
            hudCapacity.gameObject.SetActive(visible);
            if (!visible) return;
            hudCapacityText.text = $"{deployed} / {capacity}";
            hudCapacityText.color = deployed < capacity
                ? new Color(.58f, 1f, .82f, .78f)
                : new Color(.76f, .9f, .96f, .66f);
        }

        private void RenderMatchHud() {
            if(hudRound==null||RoundFlow==null)return;
            var own=RoundLoop.GetBattleFor(localViewedPlayerId);
            string notice="";string kind="Idle";double progress=0; double? remaining=null;
            if(Match.Phase==MatchPhase.Finished)notice="MATCH COMPLETE";
            else if(!automaticRoundFlow)notice="Paused for input test";
            else if(RoundFlow.Fault!=null)notice="Round paused — retry required";
            else if(RoundFlow.AwaitingMatchEnd)notice="Waiting for match end";
            else if(RoundLoop.NextPreparationPending)notice="Updating shops…";
            else if(Match.Phase==MatchPhase.Preparation&&!RoundLoop.PreparationReady)notice="Preparing round…";
            else if(Match.Phase==MatchPhase.Result&&!RoundLoop.IsSettled)notice="Settling results…";
            else if(Match.Phase==MatchPhase.Preparation) {
                kind="Preparation";remaining=RoundFlow.RemainingSeconds;progress=remaining.Value/RoundFlow.Rules.PreparationSeconds;
            } else if(Match.Phase==MatchPhase.Result) {
                kind="Result";remaining=RoundFlow.RemainingSeconds;progress=RoundFlow.Rules.ResultSeconds>0?remaining.Value/RoundFlow.Rules.ResultSeconds:0;
            } else if(Match.Phase==MatchPhase.Combat) {
                if(own==null||own.IsComplete)notice="Waiting for other battles";
                else if(own.Battle!=null) {
                    double elapsed=own.Battle.ElapsedSeconds;
                    kind=own.Battle.IsOvertime?"Overtime":"Combat";
                    double overtime=own.Battle.OvertimeStartTick/(double)own.Battle.TickRate;
                    double limit=own.Battle.TimeLimitTick/(double)own.Battle.TickRate;
                    remaining=(kind=="Overtime"?limit:overtime)-elapsed;
                    double duration=kind=="Overtime"?limit-overtime:overtime;
                    progress=duration>0?remaining.Value/duration:0;
                }
            }
            if(kind=="Idle"&&Match.Phase!=MatchPhase.Finished&&ReferenceEquals(hudMatch,Match))progress=HudTimerProgress;
            HudTimerKind=kind;HudTimerProgress=Mathf.Clamp01((float)progress);
            RenderRoundStrip(Match.RoundNumber, Match.Rules.EnableWildRounds, remaining);
            RenderHudTimer(Match.Phase!=MatchPhase.Finished&&!(Match.Phase==MatchPhase.Combat&&(own==null||own.IsComplete)),
                kind=="Overtime"||kind=="Preparation"&&remaining<=5);
            hudNotice.text=notice;
            RenderBoardCapacity(Match.Phase==MatchPhase.Preparation&&localViewedPlayerId=="p1"&&!Player.IsEliminated,
                Player.DeployedUnitCount, Player.BoardCapacity);
            RenderLocalSpectators(own);
        }
    }
}


