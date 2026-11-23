using System.Linq;
using PokeChess.Network.Session;
using UnityEngine;

namespace PokeChess.Client.UI
{
    public sealed partial class RoomFlowView
    {
        private sealed class LobbySlot
        {
            public RectTransform Root;
            public UnityEngine.UI.Image Background, Portrait, EmptyPortrait, Waiting;
            public GameObject Selected, Host, Ready;
            public UnityEngine.UI.Text Name, Status;
        }

        private readonly LobbySlot[] lobbySlots = new LobbySlot[8];
        private UnityEngine.UI.Text roomCodeLabel, roomVisibilityLabel, roomPopulation, roomReadyCount;
        private UnityEngine.UI.Image roomVisibilityIcon;
        private Sprite publicRoomIcon;
        private GameObject[] browserChrome;

        private void BindRoomLobbyArt(Transform waiting)
        {
            roomCodeLabel = UiPrefab.Get<UnityEngine.UI.Text>(waiting, "RoomCode");
            roomVisibilityLabel = UiPrefab.Get<UnityEngine.UI.Text>(waiting, "VisibilityLabel");
            roomVisibilityIcon = UiPrefab.Get<UnityEngine.UI.Image>(waiting, "VisibilityIcon");
            publicRoomIcon = roomVisibilityIcon.sprite;
            roomPopulation = UiPrefab.Get<UnityEngine.UI.Text>(waiting, "Population");
            roomReadyCount = UiPrefab.Get<UnityEngine.UI.Text>(waiting, "ReadyCount");
            browserChrome = new[] { "BrowserHeader", "Title", "WindowFrame", "Local play", "ConnectionStatus", "ConnectionDot" }
                .Select(name => panel.transform.Find(name).gameObject).ToArray();
            for (int i = 0; i < lobbySlots.Length; i++)
            {
                var root = UiPrefab.Instantiate("LobbyPlayerSlot", waiting.Find("PlayerSlots"), font);
                foreach (var text in root.GetComponentsInChildren<UnityEngine.UI.Text>(true)) text.font = font;
                root.anchoredPosition = new Vector2(i % 2 == 0 ? -350 : 40, 133 - i / 2 * 88);
                lobbySlots[i] = new LobbySlot
                {
                    Root = root, Background = root.GetComponent<UnityEngine.UI.Image>(),
                    Selected = root.Find("Selected").gameObject,
                    Portrait = UiPrefab.Get<UnityEngine.UI.Image>(root, "Portrait"),
                    EmptyPortrait = UiPrefab.Get<UnityEngine.UI.Image>(root, "EmptyPortrait"),
                    Host = root.Find("Host").gameObject, Ready = root.Find("Ready").gameObject,
                    Waiting = UiPrefab.Get<UnityEngine.UI.Image>(root, "Waiting"),
                    Name = UiPrefab.Get<UnityEngine.UI.Text>(root, "Name"),
                    Status = UiPrefab.Get<UnityEngine.UI.Text>(root, "Status")
                };
            }
        }

        private void SetRoomLobbyChrome(bool inRoom)
        {
            foreach (var element in browserChrome) element.SetActive(!inRoom);
            feedback.rectTransform.anchoredPosition = new Vector2(0, inRoom ? -337 : -287);
        }

        private void RenderRoomLobby(RoomSnapshot room, string localId, bool isHost, bool localReady,
            bool connected, bool confirmed, bool busy, LobbyPhase phase, string blockedReason)
        {
            roomTitle.text = room.Name;
            roomCodeLabel.text = room.Code;
            roomVisibilityLabel.text = room.IsPrivate ? "비공개" : "공개";
            roomVisibilityIcon.sprite = room.IsPrivate
                ? privateVisibility.transform.Find("Icon").GetComponent<UnityEngine.UI.Image>().sprite : publicRoomIcon;
            roomPopulation.text = room.Members.Count + " / " + room.Capacity;
            var members = room.Members.OrderByDescending(member => member.IsHost)
                .ThenBy(member => member.Id, System.StringComparer.Ordinal).ToArray();
            for (int i = 0; i < lobbySlots.Length; i++)
            {
                var slot = lobbySlots[i];
                var member = i < members.Length ? members[i] : null;
                bool occupied = member != null;
                bool available = i < room.Capacity;
                slot.Background.color = available ? Color.white : new Color(1, 1, 1, .45f);
                slot.Selected.SetActive(occupied && member.Id == localId);
                slot.Portrait.gameObject.SetActive(occupied);
                slot.EmptyPortrait.gameObject.SetActive(!occupied);
                slot.Name.text = !occupied ? available ? "참가 대기" : "사용 안 함"
                    : member.Id == localId ? "나" : "트레이너 " + member.Id.Substring(System.Math.Max(0, member.Id.Length - 6));
                slot.Host.SetActive(occupied && member.IsConnected && member.IsHost);
                slot.Ready.SetActive(occupied && member.IsConnected && !member.IsHost && member.IsReady);
                slot.Waiting.gameObject.SetActive(occupied && (!member.IsConnected || !member.IsHost && !member.IsReady));
                slot.Waiting.color = occupied && !member.IsConnected ? new Color(.6f, .6f, .6f) : Color.white;
                slot.Portrait.color = occupied && !member.IsConnected ? new Color(.6f, .6f, .6f) : Color.white;
                slot.Status.text = !occupied ? "" : !member.IsConnected ? "접속 중" : member.IsHost ? "방장"
                    : member.IsReady ? "준비 완료" : "준비 중";
                slot.Status.color = occupied && member.IsConnected && member.IsReady && !member.IsHost
                    ? new Color(.05f, .45f, .15f) : new Color32(14, 24, 55, 255);
            }
            int participants = members.Count(member => !member.IsHost);
            int prepared = members.Count(member => !member.IsHost && member.IsReady && member.IsConnected);
            roomReadyCount.text = "준비 완료 " + prepared + " / " + participants;
            readyButton.gameObject.SetActive(!isHost && phase == LobbyPhase.Waiting);
            startButton.gameObject.SetActive(isHost && phase != LobbyPhase.Started);
            readyButton.interactable = connected && confirmed && !busy && phase == LobbyPhase.Waiting;
            startButton.interactable = isHost && connected && confirmed && !busy && phase == LobbyPhase.Waiting && blockedReason == null;
            readyButton.GetComponentInChildren<UnityEngine.UI.Text>().text = busy ? "처리 중…" : localReady ? "준비 취소" : "준비";
            startButton.GetComponentInChildren<UnityEngine.UI.Text>().text = phase == LobbyPhase.Starting ? "시작 중…" : "게임 시작";
            leave.interactable = copy.interactable = !busy;
            lobbyStatus.text = phase == LobbyPhase.Starting ? "모든 참가자의 시작 확인을 기다리는 중입니다."
                : phase == LobbyPhase.Started ? "게임을 시작합니다."
                : !connected || !confirmed ? "방 연결을 확인하는 중입니다."
                : busy ? "대기실 정보를 저장하는 중입니다."
                : members.Length < 2 ? "참가자를 기다리는 중입니다.\n2명 이상 모이면 시작할 수 있습니다."
                : members.Any(member => !member.IsConnected) ? "참가자의 접속을 기다리는 중입니다."
                : isHost && blockedReason == null ? "모든 참가자가 준비됐습니다.\n게임을 시작할 수 있습니다."
                : isHost ? "모든 참가자가 준비하면\n게임을 시작할 수 있습니다."
                : localReady ? "방장이 게임을 시작하기를 기다리는 중입니다." : "준비가 되면 아래 준비 버튼을 눌러 주세요.";
        }
    }
}
