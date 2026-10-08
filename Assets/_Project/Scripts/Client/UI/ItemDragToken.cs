using UnityEngine;
using UnityEngine.EventSystems;

namespace PokeChess.Client.UI
{
    public sealed class ItemDragToken : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public PlacementSandboxView View;
        public int Slot;
        public void OnBeginDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)View.BeginItemDrag(Slot,e.position);}
        public void OnDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)View.DragItem(e.position);}
        public void OnEndDrag(PointerEventData e){if(e.button==PointerEventData.InputButton.Left)View.EndItemDrag(e.position);}
        public void OnPointerEnter(PointerEventData e){View.ShowItemHint(Slot);}
        public void OnPointerExit(PointerEventData e){View.HideItemHint();}
    }
}
