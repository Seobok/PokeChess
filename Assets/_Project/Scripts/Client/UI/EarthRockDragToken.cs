using UnityEngine;
using UnityEngine.EventSystems;
using PokeChess.Core.Match;
using PokeChess.Core.Pokemon;
namespace PokeChess.Client.UI
{
    public sealed class EarthRockDragToken : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public PlacementSandboxView View;public int Slot;
        public void OnBeginDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)View.BeginEarthRockDrag(Slot,e.position);}
        public void OnDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)View.DragEarthRock(e.position);}
        public void OnEndDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)View.EndEarthRockDrag(e.position);}
    }
    public sealed partial class PlacementSandboxView
    {
        private int draggedEarthRock=-1;
        private UnityEngine.UI.Image earthRockGhost;
        public bool IsEarthRockDragging=>draggedEarthRock>=0;
        private bool CanMoveSynergyPlacement=>IsOnlineMatch
            ?onlineService.LocalMatchState!=null&&onlineService.LocalMatchState.phase==MatchPhase.Preparation&&!onlineService.LocalMatchState.eliminated&&onlineService.WatchingPlayerId==onlineService.LocalMatchState.playerId
            :Match!=null&&Match.Phase==MatchPhase.Preparation&&localViewedPlayerId=="p1"&&!Player.IsEliminated&&!RoundLoop.NextPreparationPending&&Simulation?.Status!=SimulationStatus.Running;
        public void BeginEarthRockDrag(int slot,Vector2 screen)
        {
            if(!CanMoveSynergyPlacement||IsDragging||IsItemDragging||draggingNatureGuardian||slot<0)return;
            var art=Resources.Load<EarthVisuals>("EarthVisuals");if(art==null||art.idle==null||art.idle.Length==0)return;
            CancelEarthRockDrag();draggedEarthRock=slot;
            if (earthRockGhost == null)
            {
                earthRockGhost = EarthVisuals.Create(canvasRect, "DragGhost");
                earthRockGhost.rectTransform.pivot = EarthVisuals.RockPivot;
            }
            earthRockGhost.sprite=art.idle[0];earthRockGhost.enabled=true;
            earthRockGhost.rectTransform.sizeDelta=Vector2.one*Vector2.Distance(FindSlot(UnitPlacement.OnBoard(new BoardPosition(0,0))).Rect.anchoredPosition,FindSlot(UnitPlacement.OnBoard(new BoardPosition(1,0))).Rect.anchoredPosition);
            DragEarthRock(screen);
        }
        public void DragEarthRock(Vector2 screen)
        {
            if(!IsEarthRockDragging)return;
            if(!CanMoveSynergyPlacement){CancelEarthRockDrag();return;}
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,screen,null,out var local);
            earthRockGhost.rectTransform.anchoredPosition=local;earthRockGhost.transform.SetAsLastSibling();
            var target=Hit(screen);earthRockGhost.color=target!=null&&target.Placement.Kind==PlacementKind.Board?new Color(1,1,1,.8f):new Color(1,.4f,.4f,.7f);
            Feedback("바위: 빈 아군 칸에 놓으세요. 보드 밖에 놓으면 취소됩니다.");
        }
        public void EndEarthRockDrag(Vector2 screen)
        {
            if(!IsEarthRockDragging)return;
            int slot=draggedEarthRock;bool allowed=CanMoveSynergyPlacement;var target=Hit(screen);CancelEarthRockDrag();
            if(!allowed||target==null||target.Placement.Kind!=PlacementKind.Board){Feedback("바위 이동 취소");Render();return;}
            var tile=target.Placement.Position.Value;
            if(IsOnlineMatch)SendOnlineCommand(MatchCommandKind.ConfigureSynergy,c=>{c.bench=1;c.slot=slot;c.column=tile.Column;c.row=tile.Row;});
            else {var result=commands.ConfigureSynergy(1,null,null,slot,tile.Column,tile.Row);Feedback(result.Accepted?"바위 이동 완료":result.Message);if(result.Accepted)PlayActionSound(ActionSoundKind.Placement);Render();}
        }
        private void CancelEarthRockDrag(){draggedEarthRock=-1;if(earthRockGhost!=null)earthRockGhost.enabled=false;}
    }
}
