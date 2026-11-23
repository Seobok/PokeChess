using PokeChess.Client.Services;
using PokeChess.Network.Session;
using UnityEngine;

namespace PokeChess.Client.UI
{
    public sealed partial class RoomFlowView
    {
        private UnityEngine.UI.Button capacityMinus, capacityPlus, privateVisibility;
        private UnityEngine.UI.Text browserEmptyLabel, browserConnectionLabel;
        private UnityEngine.UI.Image browserConnectionDot;
        private Sprite visibilityNormal, visibilitySelected;
        private UnityEngine.UI.ScrollRect browserScroll;
        private System.Collections.Generic.IReadOnlyList<RoomListing> renderedRooms;
        private bool resetRoomScroll = true;
        private bool roomListScrollRequested;

        private void BindBrowserArt(Transform body)
        {
            capacityMinus = UiPrefab.Bind(body, "CapacityMinus", () => ChangeCapacity(-1));
            capacityPlus = UiPrefab.Bind(body, "CapacityPlus", () => ChangeCapacity(1));
            privateVisibility = UiPrefab.Bind(body, "PrivateVisibility", () => isPrivate = true);
            browserScroll = UiPrefab.Get<UnityEngine.UI.ScrollRect>(body, "RoomList");
            BindRoomScrollInput(browserScroll.transform, UnityEngine.EventSystems.EventTriggerType.Scroll);
            BindRoomScrollInput(browserScroll.transform, UnityEngine.EventSystems.EventTriggerType.EndDrag);
            if (browserScroll.verticalScrollbar != null)
            {
                BindRoomScrollInput(browserScroll.verticalScrollbar.transform, UnityEngine.EventSystems.EventTriggerType.PointerUp);
                BindRoomScrollInput(browserScroll.verticalScrollbar.transform, UnityEngine.EventSystems.EventTriggerType.EndDrag);
            }
            browserEmptyLabel = UiPrefab.Get<UnityEngine.UI.Text>(body, "EmptyState");
            browserConnectionLabel = UiPrefab.Get<UnityEngine.UI.Text>(body.parent, "ConnectionStatus");
            browserConnectionDot = UiPrefab.Get<UnityEngine.UI.Image>(body.parent, "ConnectionDot");
            visibilityNormal = visibility.image.sprite;
            visibilitySelected = visibility.spriteState.selectedSprite;
            RenderList();
        }

        private void ChangeCapacity(int delta) => maxPlayers = Mathf.Clamp(maxPlayers + delta, 2, 8);

        private void UpdateBrowserArt(bool idle, bool authenticated)
        {
            capacity.interactable = false;
            capacityLabel.text = maxPlayers.ToString();
            capacityMinus.interactable = idle && maxPlayers > 2;
            capacityPlus.interactable = idle && maxPlayers < 8;
            privateVisibility.interactable = idle;
            visibilityLabel.text = "공개";
            visibility.image.sprite = isPrivate ? visibilityNormal : visibilitySelected;
            privateVisibility.image.sprite = isPrivate ? visibilitySelected : visibilityNormal;
            UpdateRoomListScroll(idle);
            browserConnectionLabel.text = authenticated
                ? Browser.State == RoomListState.Ready ? "연결됨" : "준비됨" : "로그인 필요";
            browserConnectionDot.color = authenticated ? Color.white : new Color(.45f, .45f, .45f);
        }

        private void BindRoomScrollInput(Transform target, UnityEngine.EventSystems.EventTriggerType kind)
        {
            var trigger = target.GetComponent<UnityEngine.EventSystems.EventTrigger>()
                ?? target.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
            var entry = new UnityEngine.EventSystems.EventTrigger.Entry { eventID = kind };
            entry.callback.AddListener(_ => roomListScrollRequested = true);
            trigger.triggers.Add(entry);
        }

        private async void UpdateRoomListScroll(bool idle)
        {
            if (!idle || !browse.activeInHierarchy)
            {
                roomListScrollRequested = false;
                return;
            }
            if (!roomListScrollRequested || Browser.State == RoomListState.Loading) return;
            bool atBottom = browserScroll.content.rect.height > browserScroll.viewport.rect.height + 1 &&
                browserScroll.verticalNormalizedPosition <= .08f;
            if (!atBottom || Browser.State != RoomListState.Ready || string.IsNullOrEmpty(Browser.NextPage))
            {
                roomListScrollRequested = false;
                return;
            }
            if (!Browser.CanLoadMore) return;
            roomListScrollRequested = false;
            await Browser.RefreshAsync(true);
        }

        private UnityEngine.UI.Button CreateBrowserRow(RoomListing room)
        {
            var row = UiPrefab.Instantiate("RoomBrowserRow", content, font);
            foreach (var label in row.GetComponentsInChildren<UnityEngine.UI.Text>(true)) label.font = font;
            UiPrefab.Get<UnityEngine.UI.Text>(row, "RoomName").text = room.Name;
            UiPrefab.Get<UnityEngine.UI.Text>(row, "PlayerCount").text = room.Players + " / " + room.Capacity;
            var button = row.GetComponent<UnityEngine.UI.Button>();
            var joinButton = UiPrefab.Get<UnityEngine.UI.Button>(row, "Join");
            UnityEngine.Events.UnityAction enter = async () => await OnlineConnection.Instance.JoinByIdAsync(room.Id);
            button.onClick.AddListener(enter);
            joinButton.onClick.AddListener(enter);
            rowButtons.Add(joinButton);
            return button;
        }

        private void RenderBrowserEmptyState()
        {
            browserEmptyLabel.gameObject.SetActive(Browser.Rooms.Count == 0);
            browserEmptyLabel.text = Browser.State == RoomListState.Loading ? "방 목록을 불러오는 중…"
                : Browser.State == RoomListState.Failed ? "방 목록을 불러오지 못했습니다.\n로그인 상태를 확인하고 새로고침해 주세요."
                : "공개된 방이 없습니다.\n새 방을 만들어 보세요.";
        }
    }
}
