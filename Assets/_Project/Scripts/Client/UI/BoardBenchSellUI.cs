using System.Linq;
using UnityEngine;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private const int BoardColumns = 7, BoardRows = 8, DeployRows = 4;
        private static readonly Vector2 BoardCellSize = new Vector2(72, 58);
        private readonly UnityEngine.UI.Image[] boardMarks = new UnityEngine.UI.Image[BoardColumns * BoardRows];
        private readonly UnityEngine.UI.Image[] benchSlots = new UnityEngine.UI.Image[9];
        private readonly RectTransform[] boardTiles = new RectTransform[BoardColumns * BoardRows];
        private readonly Sprite[] deployOutlines = new Sprite[BoardColumns * BoardRows];
        private readonly Sprite[] benchIdleSprites = new Sprite[9];
        private Sprite validTarget, blockedTarget, benchSlotActive, sellIdle, sellActive;
        private UnityEngine.UI.Image sellDrop;
        private UnityEngine.UI.Text sellDropPrice;
        private PokemonSpriteView draggedSprite;
        private Slot dragTarget;
        private int blockedBoardIndex = -1, dragBenchOrigin = -1;
        private bool dragTargetValid, sellHovered;
        private int? dragSaleGold;

        private static Sprite LoadPlacementArt(string name) => Resources.Load<Sprite>("BoardBenchSell/" + name);

        private RectTransform BindPlacementArt()
        {
            validTarget = LoadPlacementArt("BoardValidTarget");
            blockedTarget = LoadPlacementArt("BoardBlockedTarget");
            benchSlotActive = LoadPlacementArt("BenchSlotActive");
            sellActive = LoadPlacementArt("SellDropActive");

            var root = UiPrefab.Instantiate("PlacementArt", canvasRect);
            root.SetSiblingIndex(1);
            for (int row = 0; row < BoardRows; row++)
            for (int column = 0; column < BoardColumns; column++)
            {
                int index = row * BoardColumns + column;
                var tile = boardTiles[index] = UiPrefab.Get<RectTransform>(root, $"BoardTile_{column}_{row}");
                var mark = boardMarks[index] = UiPrefab.Get<UnityEngine.UI.Image>(root, $"PlacementMark_{column}_{row}");
                var markRect = mark.rectTransform;
                markRect.anchorMin = tile.anchorMin;
                markRect.anchorMax = tile.anchorMax;
                markRect.pivot = tile.pivot;
                markRect.anchoredPosition = tile.anchoredPosition;
                markRect.sizeDelta = tile.sizeDelta;
                deployOutlines[index] = mark.sprite;
            }

            for (int i = 0; i < benchSlots.Length; i++)
            {
                benchSlots[i] = UiPrefab.Get<UnityEngine.UI.Image>(root, $"BenchSlotArt_{i}");
                benchIdleSprites[i] = benchSlots[i].sprite;
            }

            foreach (var slot in slots)
            {
                RectTransform art;
                if (slot.Placement.Position is BoardPosition position)
                    art = boardTiles[position.Row * BoardColumns + position.Column];
                else
                    art = benchSlots[slot.Placement.BenchSlot.Value].rectTransform;

                Place(slot.Rect, PlacementSize(art), PlacementCenter(art));
                slot.Graphic.enabled = false;
                foreach (Transform child in slot.Rect) child.gameObject.SetActive(false);
            }

            sellDrop = UiPrefab.Get<UnityEngine.UI.Image>(root, "SellDropZone");
            sellDropPrice = UiPrefab.Get<UnityEngine.UI.Text>(sellDrop.transform, "Price");
            sellIdle = sellDrop.sprite;
            return root;
        }

        private Vector2 PlacementCenter(RectTransform rect) =>
            canvasRect.InverseTransformPoint(rect.TransformPoint(rect.rect.center));

        private Vector2 PlacementSize(RectTransform rect) =>
            canvasRect.InverseTransformVector(rect.TransformVector(rect.rect.size));

        private Vector2 TablePoint(BoardPosition position)
        {
            if (position.Column >= 0 && position.Column < BoardColumns && position.Row >= 0 && position.Row < BoardRows)
                return PlacementCenter(boardTiles[position.Row * BoardColumns + position.Column]);
            return new Vector2(-252 + position.Column * 72 + (position.Row % 2) * 36, -70 + position.Row * 43);
        }

        private void ShowDraggedPokemon(string species, UnitPlacement origin)
        {
            dragBenchOrigin = origin.BenchSlot ?? -1;
            if (draggedSprite == null) draggedSprite = AddSprite(ghost, species);
            else if (draggedSprite.PokemonId != species) draggedSprite.Initialize(species);
            var background = ghost.GetComponent<UnityEngine.UI.Image>();
            background.enabled = !draggedSprite.ShowIdle(Time.unscaledTimeAsDouble);
            ghostLabel.text = background.enabled ? catalog.Get(species).DisplayName : "";
            ghost.gameObject.SetActive(true);
            ghost.SetAsLastSibling();
        }

        private bool HitSellDrop(Vector2 point) => sellDrop != null && sellDrop.gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(sellDrop.rectTransform, point, null);

        private bool CanSellDraggedUnit() => commands.CanSell(dragged) && Player.PlacementRevision == dragRevision &&
            !busy && !RoundLoop.NextPreparationPending && Simulation?.Status != SimulationStatus.Running;

        private bool CanDropOnlineUnit(OwnerUnit unit, UnitPlacement target)
        {
            if (!CanOnlinePlace(unit, target)) return false;
            if (unit.placement != PlacementKind.Bench || target.Kind != PlacementKind.Board) return true;
            var owner = onlineService.LocalMatchState;
            var point = target.Position.Value;
            if (owner.guardianTile != null && owner.guardianTile.column == point.Column && owner.guardianTile.row == point.Row ||
                owner.rockTiles?.Any(tile => tile.column == point.Column && tile.row == point.Row) == true)
                return false;
            return owner.units.Count(u => u.placement == PlacementKind.Board) < owner.level ||
                owner.units.Any(u => u.placement == PlacementKind.Board && u.column == point.Column && u.row == point.Row);
        }

        private void UpdatePlacementDragArt(Vector2 point, Slot destination, bool valid, int? price)
        {
            dragTarget = destination;
            dragTargetValid = valid;
            dragSaleGold = price;
            sellHovered = HitSellDrop(point);
            blockedBoardIndex = -1;
            if (destination == null && !sellHovered)
                for (int index = BoardColumns * DeployRows; index < boardMarks.Length; index++)
                    if (PlacementHexGraphic.ContainsPoint(boardMarks[index].rectTransform, point, null))
                    {
                        blockedBoardIndex = index;
                        break;
                    }
            RenderPlacementArt();
        }

        private void ClearPlacementDragArt()
        {
            dragTarget = null;
            blockedBoardIndex = dragBenchOrigin = -1;
            dragSaleGold = null;
            sellHovered = false;
        }

        private void RenderPlacementArt()
        {
            if (sellDrop == null) return;
            var owner = IsOnlineMatch ? onlineService.LocalMatchState : null;
            var phase = IsOnlineMatch ? onlineService.PublicState?.phase : Match?.Phase;
            bool ownBoard = IsOnlineMatch
                ? owner != null && onlineService.WatchingPlayerId == owner.playerId
                : localViewedPlayerId == "p1";
            bool canInteract = ownBoard && (IsOnlineMatch
                ? onlineService.StateSynchronized && !onlineService.IsRecovering && !owner.eliminated
                : Match != null && !Player.IsEliminated && Player.HP > 0);
            bool deploying = canInteract && phase == MatchPhase.Preparation;
            bool placementDragging = IsDragging || IsEarthRockDragging || draggingNatureGuardian;
            int targetIndex = -1;
            if (placementDragging && dragTarget?.Placement.Position is BoardPosition point)
                targetIndex = point.Row * BoardColumns + point.Column;
            for (int i = 0; i < boardMarks.Length; i++)
            {
                bool hovered = placementDragging && (i == targetIndex || i == blockedBoardIndex);
                var mark = boardMarks[i];
                mark.enabled = hovered || deploying && i < BoardColumns * DeployRows;
                if (mark.enabled) mark.sprite = hovered
                    ? i == targetIndex && dragTargetValid ? validTarget : blockedTarget
                    : deployOutlines[i];
            }
            int targetBench = placementDragging ? dragTarget?.Placement.BenchSlot ?? -1 : -1;
            for (int i = 0; i < benchSlots.Length; i++)
            {
                bool highlighted = placementDragging && (i == dragBenchOrigin || i == targetBench);
                benchSlots[i].sprite = highlighted ? benchSlotActive : benchIdleSprites[i];
                benchSlots[i].color = i == targetBench && !dragTargetValid ? new Color(1f, .4f, .4f) : Color.white;
            }
            foreach (var slot in slots) slot.Graphic.enabled = IsItemDragging && draggedItem != EvolutionDragId;
            sellDrop.gameObject.SetActive(canInteract && (phase == MatchPhase.Preparation || phase == MatchPhase.Combat));
            bool saleReady = IsDragging && dragSaleGold.HasValue;
            sellDrop.sprite = saleReady ? sellActive : sellIdle;
            sellDrop.color = saleReady && sellHovered ? new Color(1f, .97f, .73f) : Color.white;
            sellDropPrice.text = saleReady ? $"+{dragSaleGold.Value}G" : "";
            if (IsDragging && draggedSprite != null && draggedSprite.ShowIdle(Time.unscaledTimeAsDouble))
            {
                ghost.GetComponent<UnityEngine.UI.Image>().enabled = false;
                ghostLabel.text = "";
            }
        }
    }
}