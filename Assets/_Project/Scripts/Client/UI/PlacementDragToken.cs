using UnityEngine;
using UnityEngine.EventSystems;

namespace PokeChess.Client.UI
{
    public sealed class PlacementDragToken : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public PlacementSandboxView View { get; set; }
        public string UnitId { get; set; }
        public void OnBeginDrag(PointerEventData e) { if(e.button==PointerEventData.InputButton.Left) View.BeginDrag(UnitId,e.position); }
        public void OnDrag(PointerEventData e) { if(e.button==PointerEventData.InputButton.Left) View.Drag(e.position); }
        public void OnEndDrag(PointerEventData e) { if(e.button==PointerEventData.InputButton.Left) View.EndDrag(e.position); }
    }
}
