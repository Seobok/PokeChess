using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private static readonly Color32 SpectatorInk = new Color32(13, 23, 55, 255);
        private const float SpectatorRowSpacing = 53;

        private RectTransform sidebar, spectatorBanner;
        private UnityEngine.UI.Text hudPlayers, spectatorCaption;
        private UnityEngine.UI.Button spectatorReturn;
        private Sprite spectatorRow, spectatorSelected, spectatorEliminated, spectatorArrow, spectatorTrack, spectatorFill;
        private readonly Sprite[] spectatorAvatars = new Sprite[8];

        private sealed class ProfileRow
        {
            public RectTransform Rect;
            public UnityEngine.UI.Image Background, Avatar, Arrow, Track, HP;
            public UnityEngine.UI.Text Name, State, Damage, Status;
            public UnityEngine.UI.Button Button;
            public int PreviousHP, DamageValue;
            public float DamageUntil;
        }

        private static Sprite LoadSpectatorSprite(string name) => Resources.Load<Sprite>("PlayerSpectator/Spectator" + name);

        private UnityEngine.UI.Image SpectatorImage(string name, Transform parent, Vector2 size, Vector2 position, Sprite sprite)
        {
            var image = HudImage(name, parent, size, position, Color.white);
            image.sprite = sprite;
            return image;
        }

        private UnityEngine.UI.Text SpectatorText(string name, Transform parent, string text, int size,
            Vector2 dimensions, Vector2 position, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var label = Label(name, parent, text, size, dimensions, position);
            label.font = itemFont ?? font;
            label.fontStyle = FontStyle.Bold;
            label.color = SpectatorInk;
            label.alignment = alignment;
            return label;
        }

        private void BuildSpectatorHud()
        {
            spectatorRow = LoadSpectatorSprite("Row");
            spectatorSelected = LoadSpectatorSprite("RowSelected");
            spectatorEliminated = LoadSpectatorSprite("RowEliminated");
            spectatorArrow = LoadSpectatorSprite("SelectionArrow");
            spectatorTrack = LoadSpectatorSprite("HpTrack");
            spectatorFill = LoadSpectatorSprite("HpFill");
            for (int i = 0; i < spectatorAvatars.Length; i++)
                spectatorAvatars[i] = LoadSpectatorSprite($"Avatar{i + 1:00}");

            sidebar = SpectatorImage("PlayerSidebar", canvasRect, new Vector2(228, 480), new Vector2(518, 112),
                LoadSpectatorSprite("ListFrame")).rectTransform;
            var heading = SpectatorImage("PlayerListHeader", sidebar, new Vector2(214, 41), new Vector2(0, 214),
                LoadSpectatorSprite("ListHeader"));
            hudPlayers = SpectatorText("PlayerCount", heading.transform, "", 18, heading.rectTransform.sizeDelta, Vector2.zero);
            hudPlayers.color = Color.white;

            spectatorBanner = Rect("SpectatorBanner", canvasRect, new Vector2(290, 34), new Vector2(-184, 245));
            var caption = SpectatorImage("WatchBanner", spectatorBanner, new Vector2(184, 32), new Vector2(-51, 0),
                LoadSpectatorSprite("WatchBanner"));
            spectatorCaption = SpectatorText("WatchCaption", caption.transform, "", 13, new Vector2(159, 28), Vector2.zero);
            spectatorCaption.resizeTextForBestFit = true;
            spectatorCaption.resizeTextMinSize = 9;
            spectatorCaption.resizeTextMaxSize = 13;
            var back = SpectatorImage("ReturnToOwnBoard", spectatorBanner, new Vector2(99, 31), new Vector2(93, 0),
                LoadSpectatorSprite("ReturnButton"));
            back.raycastTarget = true;
            spectatorReturn = back.gameObject.AddComponent<UnityEngine.UI.Button>();
            spectatorReturn.targetGraphic = back;
            spectatorReturn.onClick.AddListener(() => RequestPlayerView(IsOnlineMatch ? onlineService.LocalMatchState.playerId : "p1"));
            var backLabel = SpectatorText("Label", back.transform, "내 보드로", 12, back.rectTransform.sizeDelta, Vector2.zero);
            backLabel.color = Color.white;
            spectatorBanner.gameObject.SetActive(false);
        }

        private ProfileRow BuildProfileRow(string id, int hp, int avatarIndex)
        {
            var background = SpectatorImage("Profile_" + id, sidebar, new Vector2(214, 51), Vector2.zero, spectatorRow);
            background.raycastTarget = true;
            var rect = background.rectTransform;
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = background;
            button.transition = UnityEngine.UI.Selectable.Transition.None;
            button.onClick.AddListener(() => RequestPlayerView(id));

            var track = SpectatorImage("HPTrack", rect, new Vector2(97, 12), new Vector2(4, -10), spectatorTrack);
            var fill = SpectatorImage("HPFill", track.transform, new Vector2(91, 7), Vector2.zero, spectatorFill);
            fill.type = UnityEngine.UI.Image.Type.Filled;
            fill.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
            var row = new ProfileRow
            {
                Rect = rect, Background = background, Track = track, HP = fill, Button = button, PreviousHP = hp,
                Avatar = SpectatorImage("Avatar", rect, new Vector2(51, 46), new Vector2(-67, 0), spectatorAvatars[avatarIndex]),
                Arrow = SpectatorImage("WatchArrow", rect, new Vector2(11, 15), new Vector2(-97, 0), spectatorArrow),
                Name = SpectatorText("Name", rect, "", 15, new Vector2(96, 22), new Vector2(-37, 10), TextAnchor.MiddleLeft),
                State = SpectatorText("Status", rect, "", 14, new Vector2(67, 22), new Vector2(66, 10), TextAnchor.MiddleRight),
                Damage = SpectatorText("Damage", rect, "", 12, new Vector2(30, 18), new Vector2(84, -10), TextAnchor.MiddleRight),
                Status = SpectatorText("MatchStatus", rect, "", 10, new Vector2(49, 16), new Vector2(79, -10), TextAnchor.MiddleRight)
            };
            row.Name.resizeTextForBestFit = true;
            row.Name.resizeTextMinSize = 9;
            row.Name.resizeTextMaxSize = 15;
            row.Damage.color = new Color32(184, 28, 39, 255);
            hudProfiles.Add(id, row);
            return row;
        }

        private void RenderProfileRow(string id, string name, int index, int avatarIndex, int hp, int maxHP, int placement,
            bool eliminated, bool selected, bool selectable, string status)
        {
            if (!hudProfiles.TryGetValue(id, out var row))
                row = BuildProfileRow(id, hp, avatarIndex);
            row.Rect.anchoredPosition = new Vector2(0, 164 - index * SpectatorRowSpacing);
            row.Background.sprite = eliminated ? spectatorEliminated : selected ? spectatorSelected : spectatorRow;
            row.Avatar.color = eliminated ? new Color(.7f, .7f, .7f, .75f) : Color.white;
            row.Arrow.gameObject.SetActive(selected && !eliminated);
            row.Button.interactable = selectable && !eliminated && !selected;
            row.Name.text = name;
            row.State.text = eliminated ? (placement > 0 ? $"{placement}위 탈락" : "탈락") : $"{hp} HP";
            row.HP.fillAmount = Mathf.Clamp01(hp / (float)Mathf.Max(1, maxHP));
            row.Status.text = status;
            row.Track.gameObject.SetActive(!eliminated);
            if (hp < row.PreviousHP)
            {
                row.DamageValue = row.PreviousHP - hp;
                row.DamageUntil = Time.unscaledTime + 1.5f;
            }
            row.PreviousHP = hp;
            row.Damage.text = Time.unscaledTime < row.DamageUntil ? "−" + row.DamageValue : "";
            row.Status.gameObject.SetActive(!eliminated && status.Length > 0 && row.Damage.text.Length == 0);
        }

        private void RenderSpectatorBanner(string watchedName, bool canReturn, bool shadow)
        {
            spectatorBanner.gameObject.SetActive(watchedName != null || shadow);
            spectatorCaption.text = watchedName == null ? "그림자 상대"
                : shadow ? watchedName + " 관전 · 그림자 상대" : watchedName + "의 보드 관전 중";
            spectatorReturn.gameObject.SetActive(watchedName != null && canReturn);
        }

        private void ClearSpectatorRows()
        {
            foreach (var row in hudProfiles.Values)
            {
                row.Rect.gameObject.SetActive(false);
                Destroy(row.Rect.gameObject);
            }
            hudProfiles.Clear();
        }

        private void RenderLocalSpectators(LocalPairBattle battle)
        {
            if (!ReferenceEquals(hudMatch, Match))
            {
                ClearSpectatorRows();
                hudMatch = Match;
            }
            var roster = Match.Players.ToArray();
            var ordered = roster.OrderBy(p => p.PlayerId == "p1" ? 0 : p.IsEliminated ? 2 : 1).ToArray();
            hudPlayers.text = $"플레이어 {roster.Count(p => !p.IsEliminated)} / {roster.Length}";
            bool combat = Match.Phase == MatchPhase.Combat && battle != null;
            string opponent = combat && localViewedPlayerId == "p1" && !battle.IsWild
                ? battle.Pairing.OpponentOf("p1") : null;
            for (int i = 0; i < ordered.Length; i++)
            {
                var player = ordered[i];
                string status = player.PlayerId == opponent ? "VS" : "";
                RenderProfileRow(player.PlayerId, player.PlayerId == "p1" ? "나" : player.PlayerId.ToUpperInvariant(),
                    i, System.Array.IndexOf(roster, player), player.HP, player.Rules.StartingHP, player.FinalPlacement ?? 0,
                    player.IsEliminated, player.PlayerId == localViewedPlayerId, Match.Phase != MatchPhase.Finished, status);
            }
            RenderSpectatorBanner(localViewedPlayerId == "p1" ? null : localViewedPlayerId.ToUpperInvariant(),
                !Player.IsEliminated, combat && battle.IsShadow);
        }

        private void RenderOnlineSpectators(OwnerMatchState owner, PublicMatchState match, bool synchronized, CombatFrame frame)
        {
            var roster = match.players;
            string watched = onlineService.WatchingPlayerId;
            var ordered = roster.OrderBy(p => p.id == owner.playerId ? 0 : p.eliminated ? 2 : 1).ToArray();
            hudPlayers.text = $"플레이어 {roster.Count(p => !p.eliminated)} / {roster.Length}";
            bool combat = match.phase == MatchPhase.Combat && frame != null && frame.round == match.round;
            string opponent = combat && !frame.wild && watched == owner.playerId
                ? (frame.one == watched ? frame.two : frame.one) : null;
            for (int i = 0; i < ordered.Length; i++)
            {
                var player = ordered[i];
                string status = player.disconnected ? "연결 끊김" : player.id == opponent ? "VS" : "";
                RenderProfileRow(player.id, player.id == owner.playerId ? "나" : PlayerLabel(player.id, owner.playerId),
                    i, System.Array.IndexOf(roster, player), player.hp, player.maxHP, player.placement, player.eliminated,
                    player.id == watched, synchronized && match.phase != MatchPhase.Finished, status);
            }
            RenderSpectatorBanner(watched == owner.playerId ? null : PlayerLabel(watched, owner.playerId),
                synchronized && !owner.eliminated, combat && frame.shadow);
        }
    }
}