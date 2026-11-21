using PokeChess.Core.Battle;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
using UnityEngine;

namespace PokeChess.Client.UI
{
    public sealed partial class PlacementSandboxView
    {
        // Presence survives combat; phase changes only hide the preparation previews.
        private string summonEffectsPlayer;
        private BoardPosition[] previousPreparationRocks = System.Array.Empty<BoardPosition>();
        private BoardPosition? previousPreparationGuardian;
        private int previousPreparationGuardianTier;

        private void RenderPreparationSummonEffects(bool preparation, string playerId,
            System.Collections.Generic.IEnumerable<BoardPosition> rocks, BoardPosition? guardian, int tier)
        {
            if (!preparation) return;
            var current = System.Linq.Enumerable.ToArray(rocks);
            if (summonEffectsPlayer != playerId)
            {
                summonEffectsPlayer = playerId;
                previousPreparationRocks = current;
                previousPreparationGuardian = guardian;
                previousPreparationGuardianTier = tier;
                return; // Switching boards does not replay existing summons.
            }
            var earthArt = Resources.Load<EarthVisuals>("EarthVisuals");
            if (earthArt != null)
            {
                var added = System.Linq.Enumerable.Take(System.Linq.Enumerable.Except(current, previousPreparationRocks),
                    System.Math.Max(0, current.Length - previousPreparationRocks.Length));
                var removed = System.Linq.Enumerable.Take(System.Linq.Enumerable.Except(previousPreparationRocks, current),
                    System.Math.Max(0, previousPreparationRocks.Length - current.Length));
                foreach (var tile in added) PlayPreparationSummonEffect(tile, earthArt.spawn, 1, true);
                foreach (var tile in removed) PlayPreparationSummonEffect(tile, earthArt.end, 1, true);
            }
            if (guardian.HasValue != previousPreparationGuardian.HasValue)
            {
                bool spawning = guardian.HasValue;
                int effectTier = spawning ? tier : previousPreparationGuardianTier;
                var art = Resources.Load<NatureVisuals>("NatureVisuals");
                if (art != null)
                {
                    var frames = effectTier >= 10 ? art.worldtreeSummon : art.summon;
                    // No dedicated dismissal asset exists: reverse the summon clip.
                    if (!spawning && frames != null) frames = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Reverse(frames));
                    PlayPreparationSummonEffect(spawning ? guardian.Value : previousPreparationGuardian.Value,
                        frames, effectTier >= 10 ? 3 : 1.85f, false);
                }
            }
            previousPreparationRocks = current;
            previousPreparationGuardian = guardian;
            previousPreparationGuardianTier = tier;
        }

        private void PlayPreparationSummonEffect(BoardPosition tile, Sprite[] frames, float scale, bool rock)
        {
            if (!Application.isPlaying || frames == null || frames.Length == 0) return;
            var a = FindSlot(UnitPlacement.OnBoard(new BoardPosition(0, 0))).Rect.anchoredPosition;
            var b = FindSlot(UnitPlacement.OnBoard(new BoardPosition(1, 0))).Rect.anchoredPosition;
            var image = EarthVisuals.Create(canvasRect, rock ? "PreparationRockEffect" : "PreparationGuardianEffect");
            if (rock) image.rectTransform.pivot = EarthVisuals.RockPivot;
            image.rectTransform.anchoredPosition = FindSlot(UnitPlacement.OnBoard(tile)).Rect.anchoredPosition;
            image.rectTransform.sizeDelta = Vector2.one * Vector2.Distance(a, b) * scale;
            image.gameObject.AddComponent<EarthRockEndView>().Initialize(image, frames, 0);
        }

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
            if (natureGuardianTile.HasValue) yield return natureGuardianPreview;
        }

        private void RenderNatureGuardian(BoardPosition? tile, int tier)
        {
            if (tier == 0) tile = null;
            if (draggingNatureGuardian && (!CanContinueSpecialDrag || !System.Nullable.Equals(tile, natureGuardianTile))) CancelNatureGuardianDrag();
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
            natureGuardianGroup.alpha = .35f;
            specialDragRevision = SpecialPlacementRevision;
            ShowDraggedPokemon(natureGuardianSprite.PokemonId, UnitPlacement.OnBoard(natureGuardianTile.Value));
            draggedSprite.SetGuardianForm(natureGuardianTier);
            DeselectUnit();
            DragNatureGuardian(screen);
        }

        private void DragNatureGuardian(Vector2 screen)
        {
            if (!draggingNatureGuardian) return;
            if (!CanContinueSpecialDrag) { CancelNatureGuardianDrag(); return; }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var position);
            ghost.anchoredPosition = position;
            ghost.SetAsLastSibling();
            UpdateSpecialDragArt(screen);
        }

        private void EndNatureGuardianDrag(Vector2 screen)
        {
            if (!draggingNatureGuardian) return;
            var target = Hit(screen);
            bool allowed = CanContinueSpecialDrag;
            CancelNatureGuardianDrag();
            if (!allowed || target == null || target.Placement.Kind != PlacementKind.Board)
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
            draggedSprite.SetGuardianForm(0);
            ghost.gameObject.SetActive(false);
            ClearPlacementDragArt();
            RenderPlacementArt();
            natureGuardianGroup.blocksRaycasts = true;
            if (natureGuardianTile.HasValue)
                natureGuardianPreview.anchoredPosition = FindSlot(UnitPlacement.OnBoard(natureGuardianTile.Value)).Rect.anchoredPosition;
        }
    }
}
