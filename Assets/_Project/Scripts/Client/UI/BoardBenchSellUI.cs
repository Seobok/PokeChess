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
        private Sprite deployOutline, validTarget, blockedTarget, benchSlot, benchSlotActive, sellIdle, sellActive;
        private UnityEngine.UI.Image sellDrop;
        private UnityEngine.UI.Text sellDropPrice;
        private PokemonSpriteView draggedSprite;
        private Slot dragTarget;
        private int blockedBoardIndex = -1, dragBenchOrigin = -1;
        private bool dragTargetValid, sellHovered;
        private int? dragSaleGold;

        private static Sprite LoadPlacementArt(string name) => Resources.Load<Sprite>("BoardBenchSell/" + name);

        private UnityEngine.UI.Image PlacementArt(string name, Transform parent, Vector2 size, Vector2 position, Sprite sprite)
        {
            var image = HudImage(name, parent, size, position, Color.white);
            image.sprite = sprite;
            return image;
        }

        private void BuildPlacementArt()
        {
            deployOutline = LoadPlacementArt("BoardDeployableOutline");
            validTarget = LoadPlacementArt("BoardValidTarget");
            blockedTarget = LoadPlacementArt("BoardBlockedTarget");
            benchSlot = LoadPlacementArt("BenchSlot");
            benchSlotActive = LoadPlacementArt("BenchSlotActive");
            sellIdle = LoadPlacementArt("SellDropIdle");
            sellActive = LoadPlacementArt("SellDropActive");

            var root = Rect("PlacementArt", canvasRect, Vector2.zero, Vector2.zero);
            root.SetSiblingIndex(2);
            var tile = LoadPlacementArt("BoardTile");
            for (int row = 0; row < BoardRows; row++)
            for (int col = 0; col < BoardColumns; col++)
            {
                Vector2 center = TablePoint(new BoardPosition(col, row));
                PlacementArt($"BoardTile_{col}_{row}", root, BoardCellSize, center, tile);
            }
            for (int row = 0; row < BoardRows; row++)
            for (int col = 0; col < BoardColumns; col++)
            {
                int index = row * BoardColumns + col;
                boardMarks[index] = PlacementArt($"PlacementMark_{col}_{row}", root, BoardCellSize,
                    TablePoint(new BoardPosition(col, row)), deployOutline);
                boardMarks[index].enabled = false;
            }

            PlacementArt("BenchFrame", root, new Vector2(553, 60), new Vector2(-12, -142), LoadPlacementArt("BenchFrame"));
            var plate = PlacementArt("BenchLabelPlate", root, new Vector2(83, 42), new Vector2(-326, -142),
                LoadPlacementArt("BenchLabelPlate"));
            var benchTitle = ItemLabel("BenchLabel", plate.transform, "벤치", 15, new Vector2(65, 34), Vector2.zero);
            benchTitle.color = new Color32(13, 23, 55, 255);
            benchTitle.fontStyle = FontStyle.Bold;
            foreach (var slot in slots)
            {
                if (slot.Placement.Kind == PlacementKind.Board)
                {
                    Place(slot.Rect, BoardCellSize, TablePoint(slot.Placement.Position.Value));
                }
                else
                {
                    int index = slot.Placement.BenchSlot.Value;
                    var center = new Vector2(-252 + index * 60, -142);
                    Place(slot.Rect, new Vector2(57, 47), center);
                    benchSlots[index] = PlacementArt("BenchSlotArt_" + index, root, slot.Rect.sizeDelta, center, benchSlot);
                }
                slot.Graphic.enabled = false;
                foreach (Transform child in slot.Rect) child.gameObject.SetActive(false);
            }

            sellDrop = PlacementArt("SellDropZone", root, new Vector2(126, 63), new Vector2(333, -142), sellIdle);
            PlacementArt("Coin", sellDrop.transform, new Vector2(27, 27), new Vector2(-35, 0), LoadPlacementArt("SellCoin"));
            var title = ItemLabel("Label", sellDrop.transform, "판매", 15, new Vector2(71, 21), new Vector2(13, 9));
            title.fontStyle = FontStyle.Bold;
            sellDropPrice = ItemLabel("Price", sellDrop.transform, "", 15, new Vector2(70, 20), new Vector2(13, -10));
            sellDropPrice.color = new Color32(255, 226, 120, 255);
            sellDropPrice.fontStyle = FontStyle.Bold;
            sellDrop.gameObject.SetActive(false);
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
                    : deployOutline;
            }
            int targetBench = placementDragging ? dragTarget?.Placement.BenchSlot ?? -1 : -1;
            for (int i = 0; i < benchSlots.Length; i++)
            {
                bool highlighted = placementDragging && (i == dragBenchOrigin || i == targetBench);
                benchSlots[i].sprite = highlighted ? benchSlotActive : benchSlot;
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