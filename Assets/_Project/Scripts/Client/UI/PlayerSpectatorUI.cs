using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private const float SpectatorRowGap = 2;

        private RectTransform sidebar, spectatorBanner;
        private UnityEngine.UI.Text hudPlayers, spectatorCaption;
        private UnityEngine.UI.Button spectatorReturn;
        private Sprite spectatorSelected, spectatorEliminated;
        private readonly Sprite[] spectatorAvatars = new Sprite[8];

        private sealed class ProfileRow
        {
            public RectTransform Rect;
            public Vector2 FirstPosition;
            public Sprite DefaultSprite;
            public UnityEngine.UI.Image Background, Avatar, Arrow, Track, HP;
            public UnityEngine.UI.Text Name, State, Damage, Status;
            public UnityEngine.UI.Button Button;
            public int PreviousHP, DamageValue;
            public float DamageUntil;
        }

        private static Sprite LoadSpectatorSprite(string name) => Resources.Load<Sprite>("PlayerSpectator/Spectator" + name);

        private void BuildSpectatorHud()
        {
            spectatorSelected = LoadSpectatorSprite("RowSelected");
            spectatorEliminated = LoadSpectatorSprite("RowEliminated");
            for (int i = 0; i < spectatorAvatars.Length; i++)
                spectatorAvatars[i] = LoadSpectatorSprite($"Avatar{i + 1:00}");

            sidebar = UiPrefab.Instantiate("PlayerSidebar", canvasRect, itemFont);
            hudPlayers = UiPrefab.Get<UnityEngine.UI.Text>(sidebar, "PlayerListHeader/PlayerCount");
            spectatorBanner = UiPrefab.Instantiate("SpectatorBanner", canvasRect, itemFont);
            spectatorCaption = UiPrefab.Get<UnityEngine.UI.Text>(spectatorBanner, "WatchBanner/WatchCaption");
            spectatorReturn = UiPrefab.Bind(spectatorBanner, "ReturnToOwnBoard",
                () => RequestPlayerView(IsOnlineMatch ? onlineService.LocalMatchState.playerId : "p1"));
        }

        private ProfileRow BuildProfileRow(string id, int hp, int avatarIndex)
        {
            var rect = UiPrefab.Instantiate("PlayerProfileRow", sidebar, itemFont);
            rect.name = "Profile_" + id;
            var button = rect.GetComponent<UnityEngine.UI.Button>();
            button.onClick.AddListener(() => RequestPlayerView(id));
            var background = rect.GetComponent<UnityEngine.UI.Image>();
            var avatar = UiPrefab.Get<UnityEngine.UI.Image>(rect, "Avatar");
            avatar.sprite = spectatorAvatars[avatarIndex];

            var row = new ProfileRow
            {
                Rect = rect,
                FirstPosition = rect.anchoredPosition,
                Background = background,
                DefaultSprite = background.sprite,
                Button = button,
                PreviousHP = hp,
                Track = UiPrefab.Get<UnityEngine.UI.Image>(rect, "HPTrack"),
                HP = UiPrefab.Get<UnityEngine.UI.Image>(rect, "HPTrack/HPFill"),
                Avatar = avatar,
                Arrow = UiPrefab.Get<UnityEngine.UI.Image>(rect, "WatchArrow"),
                Name = UiPrefab.Get<UnityEngine.UI.Text>(rect, "Name"),
                State = UiPrefab.Get<UnityEngine.UI.Text>(rect, "Status"),
                Damage = UiPrefab.Get<UnityEngine.UI.Text>(rect, "Damage"),
                Status = UiPrefab.Get<UnityEngine.UI.Text>(rect, "MatchStatus")
            };
            hudProfiles.Add(id, row);
            return row;
        }

        private void RenderProfileRow(string id, string name, int index, int avatarIndex, int hp, int maxHP, int placement,
            bool eliminated, bool selected, bool selectable, string status)
        {
            if (!hudProfiles.TryGetValue(id, out var row))
                row = BuildProfileRow(id, hp, avatarIndex);
            row.Rect.anchoredPosition = row.FirstPosition - Vector2.up * (index * (row.Rect.rect.height + SpectatorRowGap));
            row.Background.sprite = eliminated ? spectatorEliminated : selected ? spectatorSelected : row.DefaultSprite;
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