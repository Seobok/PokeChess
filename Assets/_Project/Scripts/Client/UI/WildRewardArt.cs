using System;
using UnityEngine;
using PokeChess.Core.Match;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private sealed class WildRewardCard
        {
            public UnityEngine.UI.Image Frame, Icon;
            public UnityEngine.UI.Text Name, Description;
        }

        private static readonly Color32 WildRewardInk = new Color32(13, 20, 43, 255);
        private static readonly RoundFlowRules WildRewardOnlineClock = new RoundFlowRules();
        private readonly WildRewardCard[] wildRewardCards = new WildRewardCard[3];
        private Sprite wildRewardCardIdle, wildRewardCardHover;
        private RectTransform wildRewardBackdrop;
        private UnityEngine.UI.Text wildRewardClock;
        private UnityEngine.UI.Image wildRewardTimerFill;

        private static Sprite WildRewardSprite(string name) => Resources.Load<Sprite>("WildReward/WildReward" + name);

        private UnityEngine.UI.Image WildRewardImage(string name, Transform parent, Vector2 size, Vector2 position, Sprite sprite)
        {
            var image = HudImage(name, parent, size, position, Color.white);
            image.sprite = sprite;
            return image;
        }

        private UnityEngine.UI.Text WildRewardText(string name, Transform parent, string content, int fontSize, Vector2 size, Vector2 position)
        {
            var label = ItemLabel(name, parent, content, fontSize, size, position);
            label.fontStyle = FontStyle.Bold;
            label.color = WildRewardInk;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = fontSize;
            return label;
        }

        private void BuildWildRewardUI()
        {
            wildRewardCardIdle = WildRewardSprite("Card");
            wildRewardCardHover = WildRewardSprite("CardHover");
            var dim = HudImage("WildRewardBackdrop", canvasRect, Vector2.zero, Vector2.zero, new Color(0, 0, 0, .46f));
            wildRewardBackdrop = dim.rectTransform;
            wildRewardBackdrop.anchorMin = Vector2.zero;
            wildRewardBackdrop.anchorMax = Vector2.one;
            wildRewardBackdrop.offsetMin = wildRewardBackdrop.offsetMax = Vector2.zero;
            wildRewardBackdrop.gameObject.SetActive(false);

            var frame = WildRewardImage("ItemRewardPanel", canvasRect, new Vector2(626, 355), new Vector2(0, 39), WildRewardSprite("Frame"));
            frame.raycastTarget = true;
            itemRewardPanel = frame.rectTransform;
            var header = WildRewardImage("Header", itemRewardPanel, new Vector2(410, 62), new Vector2(0, 168), WildRewardSprite("Header"));
            var title = WildRewardText("Title", header.transform, "야생전 도구 보상", 26, new Vector2(370, 48), Vector2.zero);
            title.color = Color.white;

            var prompt = WildRewardText("Prompt", itemRewardPanel, "도구 1개를 선택하세요", 17, new Vector2(300, 27), new Vector2(-116, 116));
            prompt.alignment = TextAnchor.MiddleLeft;
            WildRewardImage("ClockIcon", itemRewardPanel, new Vector2(22, 22), new Vector2(172, 116), WildRewardSprite("Clock"));
            wildRewardClock = WildRewardText("Countdown", itemRewardPanel, "", 19, new Vector2(83, 26), new Vector2(221, 116));
            var track = WildRewardImage("TimerTrack", itemRewardPanel, new Vector2(529, 17), new Vector2(0, 92), WildRewardSprite("TimerTrack"));
            wildRewardTimerFill = WildRewardImage("Fill", track.transform, new Vector2(521, 11), Vector2.zero, WildRewardSprite("TimerFill"));
            ConfigureUnitMeter(wildRewardTimerFill);

            var namePlate = WildRewardSprite("NamePlate");
            for (int i = 0; i < itemChoices.Length; i++)
            {
                int slot = i;
                var button = ItemButton("ChooseItem_" + i, itemRewardPanel, new Vector2(183, 188), new Vector2((i - 1) * 194, -17),
                    () => ChooseItemReward(slot));
                itemChoices[i] = button;
                button.GetComponentInChildren<UnityEngine.UI.Text>().gameObject.SetActive(false);
                button.transition = UnityEngine.UI.Selectable.Transition.SpriteSwap;
                button.spriteState = new UnityEngine.UI.SpriteState
                {
                    highlightedSprite = wildRewardCardHover,
                    selectedSprite = wildRewardCardHover,
                    pressedSprite = wildRewardCardHover
                };
                var card = new WildRewardCard { Frame = button.GetComponent<UnityEngine.UI.Image>() };
                card.Frame.sprite = wildRewardCardIdle;
                card.Frame.color = Color.white;
                wildRewardCards[i] = card;
                var plate = WildRewardImage("NamePlate", button.transform, new Vector2(163, 29), new Vector2(0, 69), namePlate);
                card.Name = WildRewardText("ItemName", plate.transform, "", 16, new Vector2(153, 27), Vector2.zero);
                card.Icon = WildRewardImage("ItemIcon", button.transform, new Vector2(65, 65), new Vector2(0, 18), null);
                card.Icon.preserveAspect = true;
                HudImage("Divider", button.transform, new Vector2(155, 1), new Vector2(0, -23), WildRewardInk);
                card.Description = WildRewardText("Effect", button.transform, "", 13, new Vector2(151, 57), new Vector2(0, -55));
            }
            WildRewardText("Hint", itemRewardPanel, "시간 초과 시 첫 번째 도구를 획득합니다", 13,
                new Vector2(548, 24), new Vector2(0, -139));
            itemRewardPanel.gameObject.SetActive(false);
        }

        private void RenderWildRewardUI(bool selecting, bool ready, string[] candidates)
        {
            itemRewardPanel.gameObject.SetActive(selecting);
            wildRewardBackdrop.gameObject.SetActive(selecting);
            if (!selecting) return;
            wildRewardBackdrop.SetAsLastSibling();
            itemRewardPanel.SetAsLastSibling();
            for (int i = 0; i < itemChoices.Length; i++)
            {
                var definition = ItemCatalog.Get(candidates[i]);
                var card = wildRewardCards[i];
                card.Name.text = definition.Name;
                card.Description.text = definition.Description;
                card.Icon.sprite = itemIcons?.Get(definition.Id);
                card.Icon.enabled = card.Icon.sprite != null;
                card.Frame.color = ready ? Color.white : new Color(.78f, .81f, .85f);
                itemChoices[i].interactable = ready;
            }

            // The reward is claimed automatically when preparation ends.
            bool hasClock = IsOnlineMatch
                ? onlineService.PublicState?.hasPhaseDeadline == true
                : automaticRoundFlow && RoundFlow?.IsTimerRunning == true;
            double remaining = hasClock ? (IsOnlineMatch ? onlineService.PhaseRemainingSeconds : RoundFlow.RemainingSeconds) : 0;
            double duration = IsOnlineMatch ? WildRewardOnlineClock.PreparationSeconds : RoundFlow?.Rules.PreparationSeconds ?? 0;
            int seconds = (int)Math.Ceiling(remaining);
            wildRewardClock.text = hasClock ? $"{seconds / 60:00}:{seconds % 60:00}" : "–";
            wildRewardClock.color = hasClock && remaining <= 5 ? new Color32(165, 48, 31, 255) : WildRewardInk;
            wildRewardTimerFill.fillAmount = duration > 0 && hasClock ? Mathf.Clamp01((float)(remaining / duration)) : 0;
        }
    }
}