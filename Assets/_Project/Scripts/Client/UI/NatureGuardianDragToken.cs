using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using UnityEngine;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        private const string NatureGuardianPreviewId = "nature-guardian-preview";
        private RectTransform natureGuardianPreview;
        private CanvasGroup natureGuardianGroup;
        private PokemonSpriteView natureGuardianSprite;
        private int natureGuardianTier;
        private PokemonDefinition natureGuardianDefinition;
        private BoardPosition? natureGuardianTile;
        private bool draggingNatureGuardian;

        private System.Collections.Generic.IEnumerable<RectTransform> VisibleNatureGuardian()
        {
            if (natureGuardianTile.HasValue && !draggingNatureGuardian) yield return natureGuardianPreview;
        }

        private void RenderNatureGuardian(BoardPosition? tile, int tier)
        {
            if (tier == 0) tile = null;
            if (!CanMoveSynergyPlacement || !tile.HasValue) CancelNatureGuardianDrag();
            natureGuardianTile = tile;
            natureGuardianTier = tile.HasValue ? tier : 0;
            if (!tile.HasValue)
            {
                if (natureGuardianPreview != null) natureGuardianPreview.gameObject.SetActive(false);
                if (selectedUnitId == NatureGuardianPreviewId) DeselectUnit();
                return;
            }

            if (natureGuardianPreview == null)
            {
                natureGuardianPreview = CreatePlacementToken(NatureGuardianPreviewId, out natureGuardianGroup);
                natureGuardianSprite = AddSprite(natureGuardianPreview, tier == 2 ? "exeggcute" : "exeggutor");
            }
            natureGuardianPreview.gameObject.SetActive(true);
            natureGuardianGroup.blocksRaycasts = !draggingNatureGuardian;
            string species = tier == 2 ? "exeggcute" : "exeggutor";
            if (natureGuardianSprite.PokemonId != species) natureGuardianSprite.Initialize(species);
            natureGuardianSprite.SetGuardianForm(tier);
            if (natureGuardianDefinition?.SkillId != "synergy-nature-" + tier)
                natureGuardianDefinition = SynergySystem.NatureGuardianDefinition(tier);
            var stats = natureGuardianDefinition.BaseStats;
            bool friendly = IsOnlineMatch ? CanMoveSynergyPlacement : localViewedPlayerId == "p1";
            RenderPreparationVitals(natureGuardianPreview, stats, 1, 0, friendly);
            RenderNatureGuardianSelection();
            if (!draggingNatureGuardian)
                natureGuardianPreview.anchoredPosition = FindSlot(UnitPlacement.OnBoard(tile.Value)).Rect.anchoredPosition;
            AnimateNatureGuardian();
        }

        private void AnimateNatureGuardian()
        {
            if (natureGuardianTile.HasValue) natureGuardianSprite.ShowIdle(Time.unscaledTimeAsDouble);
        }

        private bool HasSelectedNatureGuardian => selectedUnitId == NatureGuardianPreviewId && natureGuardianTile.HasValue;

        private bool RenderNatureGuardianInfo()
        {
            if (!HasSelectedNatureGuardian || (IsOnlineMatch
                ? onlineService.PublicState?.phase != MatchPhase.Preparation : Match.Phase != MatchPhase.Preparation))
                return false;

            var stats = natureGuardianDefinition.BaseStats;
            RenderUnitInfoIcons(catalog.Get(natureGuardianDefinition.Id), UnitRank.One, stats, stats.MaxHP, stats.MaxHP,
                stats.StartingEnergy, stats.MaxEnergy, 0, System.Array.Empty<string>(), natureTier: natureGuardianTier);
            sellButton.interactable = false;
            ButtonText(sellButton, "판매 불가");
            return true;
        }

        private void RenderNatureGuardianSelection()
        {
            if (natureGuardianTile.HasValue)
                natureGuardianPreview.GetComponent<UnityEngine.UI.Image>().color =
                    PlacementTokenColor(NatureGuardianPreviewId, false);
        }

        private void BeginNatureGuardianDrag(Vector2 screen)
        {
            if (!CanMoveSynergyPlacement || !natureGuardianTile.HasValue || IsDragging || IsItemDragging || IsEarthRockDragging) return;
            draggingNatureGuardian = true;
            natureGuardianGroup.blocksRaycasts = false;
            natureGuardianGroup.alpha = .8f;
            DeselectUnit();
            DragNatureGuardian(screen);
        }

        private void DragNatureGuardian(Vector2 screen)
        {
            if (!draggingNatureGuardian) return;
            if (!CanMoveSynergyPlacement) { CancelNatureGuardianDrag(); return; }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var position);
            natureGuardianPreview.anchoredPosition = position;
            natureGuardianPreview.SetAsLastSibling();
        }

        private void EndNatureGuardianDrag(Vector2 screen)
        {
            if (!draggingNatureGuardian) return;
            var target = Hit(screen);
            CancelNatureGuardianDrag();
            if (!CanMoveSynergyPlacement || target == null || target.Placement.Kind != PlacementKind.Board)
            {
                Feedback("수호자 이동 취소");
                Render();
                return;
            }
            var tile = target.Placement.Position.Value;
            if (IsOnlineMatch)
                SendOnlineCommand(MatchCommandKind.ConfigureSynergy, command =>
                {
                    command.bench = SynergyPreparation.GuardianPlacementMode;
                    command.column = tile.Column;
                    command.row = tile.Row;
                });
            else
            {
                var result = commands.ConfigureSynergy(SynergyPreparation.GuardianPlacementMode, null, null, 0, tile.Column, tile.Row);
                Feedback(result.Accepted ? "수호자 이동 완료" : result.Message);
                if (result.Accepted) PlayActionSound(ActionSoundKind.Placement);
                Render();
            }
        }

        private void CancelNatureGuardianDrag()
        {
            if (!draggingNatureGuardian) return;
            draggingNatureGuardian = false;
            natureGuardianGroup.alpha = 1;
            natureGuardianGroup.blocksRaycasts = true;
            if (natureGuardianTile.HasValue)
                natureGuardianPreview.anchoredPosition = FindSlot(UnitPlacement.OnBoard(natureGuardianTile.Value)).Rect.anchoredPosition;
        }
    }
}
